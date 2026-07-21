using System.Globalization;
using System.Text.RegularExpressions;

namespace Impetus.Physics;

public record IterationStep(int Iteration, string Field, string From, string To, string Reason);

public record PropellantOption(string Key, string Name, double CoolantRiseK, bool PassesGate);

public record IterationMetrics(
    int Iteration,
    EngineSpec Spec,
    EngineDesign Design,
    ThermalResult Thermal,
    ValidationResult Validation,
    double TotalLengthMM,
    double IspSeaLevelS,
    double CoolantTempRiseK,
    double ThroatHeatFluxMW,
    int FailCount,
    int WarnCount
);

public record IterateOutcome
{
    public required EngineSpec OriginalSpec { get; init; }
    public required EngineDesign OriginalDesign { get; init; }
    public required ThermalResult OriginalThermal { get; init; }
    public required EngineSpec FinalSpec { get; init; }
    public required EngineDesign FinalDesign { get; init; }
    public required ThermalResult FinalThermal { get; init; }
    public required ValidationResult FinalValidation { get; init; }
    public required IReadOnlyList<IterationStep> Steps { get; init; }
    public required IReadOnlyList<string> Unfixable { get; init; }
    public required IReadOnlyList<PropellantOption> PropellantWhatIf { get; init; }
    public required bool Converged { get; init; }
    public required int Iterations { get; init; }
    public required IReadOnlyList<IterationMetrics> MetricsHistory { get; init; }
    public required bool OptimizationMode { get; init; }
}

/// <summary>
/// Rule-based auto-correction: turns the validation gate's recommended actions
/// into actual spec mutations and loops (size → validate → mutate) until the
/// analytic checks pass, every knob hits its physical bound, or the iteration
/// cap is reached. CFD is intentionally not run inside the loop (minutes per
/// pass) — run `test` on the result.
///
/// The loop only fixes what the model can see. Checks whose drivers are
/// not spec fields (gas-table L*, c*) are reported as unfixable with the
/// upgrade path, instead of being silently iterated forever.
/// </summary>
public static class AutoIterate
{
    const int nMaxIterations = 20;
    const double fPcStepBar = 2.0;
    const double fPcFloorBar = 12.0;
    const double fOfStep = 0.1;
    const double fOfFloorFraction = 0.85; // gas tables valid to ~±15% of nominal O/F

    public static IterateOutcome Run(EngineSpec oSpec, bool bOptimizeLength = false)
    {
        EngineSpec oCur = oSpec with { Name = BumpRevision(oSpec.Name) };
        List<IterationStep> aoSteps = [];
        List<string> astrUnfixable = [];
        List<IterationMetrics> aoMetrics = [];

        (EngineDesign oDesign, ThermalResult oTherm, ValidationResult oVal) = Evaluate(oCur);
        EngineDesign oOrigDesign = oDesign;
        ThermalResult oOrigTherm = oTherm;

        // Record initial metrics
        aoMetrics.Add(BuildMetrics(0, oCur, oDesign, oTherm, oVal));

        int nIter = 0;
        while (nIter < nMaxIterations && HasFail(oVal))
        {
            nIter++;
            astrUnfixable = [];
            List<IterationStep> aoMut = Mutations(oCur, oDesign, oTherm, nIter, astrUnfixable, out EngineSpec oNext);
            if (aoMut.Count == 0)
                break; // every remaining failure is outside the spec's reach

            aoSteps.AddRange(aoMut);
            oCur = oNext;
            (oDesign, oTherm, oVal) = Evaluate(oCur);

            // Record metrics after each iteration
            aoMetrics.Add(BuildMetrics(nIter, oCur, oDesign, oTherm, oVal));
        }

        bool bConverged = !HasFail(oVal);

        // Optimization mode: if converged, try to minimize length
        if (bConverged && bOptimizeLength)
        {
            (oCur, oDesign, oTherm, oVal, aoSteps, aoMetrics) = OptimizeLength(
                oCur, oDesign, oTherm, oVal, aoSteps, aoMetrics, nIter);
            bConverged = !HasFail(oVal);
        }

        List<PropellantOption> aoWhatIf = [];
        if (!bConverged && oTherm.CoolantTempRise > oDesign.Gas.MaxCoolantRiseK)
            aoWhatIf = PropellantWhatIf(oCur);

        return new IterateOutcome
        {
            OriginalSpec = oSpec,
            OriginalDesign = oOrigDesign,
            OriginalThermal = oOrigTherm,
            FinalSpec = oCur,
            FinalDesign = oDesign,
            FinalThermal = oTherm,
            FinalValidation = oVal,
            Steps = aoSteps,
            Unfixable = astrUnfixable,
            PropellantWhatIf = aoWhatIf,
            Converged = bConverged,
            Iterations = nIter,
            MetricsHistory = aoMetrics,
            OptimizationMode = bOptimizeLength
        };
    }

    static (EngineDesign, ThermalResult, ValidationResult) Evaluate(EngineSpec oSpec)
    {
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        NozzleContour oContour = new(oDesign);
        ThermalResult oTherm = ThermalModel.Evaluate(oDesign, oContour);
        ValidationResult oVal = VirtualValidation.Evaluate(oDesign, oTherm, null, null, null);
        return (oDesign, oTherm, oVal);
    }

    static (EngineDesign, ThermalResult, ValidationResult) EvaluateWithManufacturability(EngineSpec oSpec)
    {
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        NozzleContour oContour = new(oDesign);
        ThermalResult oTherm = ThermalModel.Evaluate(oDesign, oContour);

        // Run manufacturability checks (requires PicoGK library context)
        ManufacturabilityResult? oMfg = null;
        try
        {
            // This would need to be called within a Library.Go() context
            // For now, we'll skip it in AutoIterate to avoid library context issues
            oMfg = null;
        }
        catch
        {
            // Manufacturability checks require PicoGK library context
            // Skip in AutoIterate to avoid complexity
        }

        ValidationResult oVal = VirtualValidation.Evaluate(oDesign, oTherm, null, null, oMfg);
        return (oDesign, oTherm, oVal);
    }

    static IterationMetrics BuildMetrics(
        int nIter,
        EngineSpec oSpec,
        EngineDesign oDesign,
        ThermalResult oTherm,
        ValidationResult oVal)
    {
        return new IterationMetrics(
            nIter,
            oSpec,
            oDesign,
            oTherm,
            oVal,
            (oDesign.ChamberCylinderLength + oDesign.BellLength) * 1000.0, // mm
            oDesign.IspSeaLevelS,
            oTherm.CoolantTempRise,
            oTherm.ThroatHeatFlux / 1e6,
            oVal.Checks.Count(c => c.Status == CheckStatus.Fail),
            oVal.Checks.Count(c => c.Status == CheckStatus.Warn)
        );
    }

    static (EngineSpec, EngineDesign, ThermalResult, ValidationResult,
        List<IterationStep>, List<IterationMetrics>) OptimizeLength(
        EngineSpec oSpec,
        EngineDesign oDesign,
        ThermalResult oTherm,
        ValidationResult oVal,
        List<IterationStep> aoSteps,
        List<IterationMetrics> aoMetrics,
        int nStartIter)
    {
        Console.WriteLine("  Optimization mode: minimizing engine length while passing gates...");

        // Try reducing bell fraction (shortens nozzle)
        double fOriginalBell = oSpec.BellFraction;
        double fMinBell = 0.3; // Minimum reasonable bell fraction
        double fBellStep = 0.05;

        EngineSpec oBest = oSpec;
        EngineDesign oBestDesign = oDesign;
        ThermalResult oBestTherm = oTherm;
        ValidationResult oBestVal = oVal;
        double fBestLength = (oDesign.ChamberCylinderLength + oDesign.BellLength) * 1000.0;

        for (double fBell = fOriginalBell - fBellStep; fBell >= fMinBell; fBell -= fBellStep)
        {
            EngineSpec oTry = oSpec with { BellFraction = fBell };
            (EngineDesign oTryDesign, ThermalResult oTryTherm, ValidationResult oTryVal) = Evaluate(oTry);

            if (!HasFail(oTryVal))
            {
                double fTryLength = (oTryDesign.ChamberCylinderLength + oTryDesign.BellLength) * 1000.0;
                if (fTryLength < fBestLength)
                {
                    oBest = oTry;
                    oBestDesign = oTryDesign;
                    oBestTherm = oTryTherm;
                    oBestVal = oTryVal;
                    fBestLength = fTryLength;

                    aoSteps.Add(new IterationStep(
                        nStartIter + 1,
                        "bellFraction",
                        F(fOriginalBell),
                        F(fBell),
                        "optimization: reduce length while passing gates"
                    ));

                    aoMetrics.Add(BuildMetrics(nStartIter + 1, oTry, oTryDesign, oTryTherm, oTryVal));
                    Console.WriteLine($"    Bell {F(fBell)} → Length {fBestLength:F0} mm (Isp {oTryDesign.IspSeaLevelS:F1} s)");
                }
            }
            else
            {
                // Gates failed, stop reducing bell
                break;
            }
        }

        return (oBest, oBestDesign, oBestTherm, oBestVal, aoSteps, aoMetrics);
    }

    static bool HasFail(ValidationResult oVal)
        => oVal.Checks.Any(c => c.Status == CheckStatus.Fail);

    /// <summary>One corrective pass: at most one mutation per failing area.</summary>
    static List<IterationStep> Mutations(
        EngineSpec o, EngineDesign oDesign, ThermalResult oTherm,
        int nIter, List<string> astrUnfixable, out EngineSpec oNext)
    {
        List<IterationStep> ao = [];
        EngineSpec n = o;
        bool bPcTouched = false;

        // --- Channel manufacturability (geometry floor, fix first) ---------
        if (n.Cooling.DiameterMM < VirtualValidation.MinChannelDiameterMM)
        {
            ao.Add(new(nIter, "cooling.diameterMM", F(n.Cooling.DiameterMM), "1.0",
                "channel bore below metal-AM minimum"));
            n = n with { Cooling = n.Cooling with { DiameterMM = 1.0 } };
        }
        if (n.Cooling.Count < VirtualValidation.MinChannelCount)
        {
            ao.Add(new(nIter, "cooling.count", n.Cooling.Count.ToString(), "16",
                "too few channels for circumferential coverage"));
            n = n with { Cooling = n.Cooling with { Count = 16 } };
        }

        // --- Chamber pressure beyond test-stand envelope --------------------
        if (n.ChamberPressureBar > VirtualValidation.PcFailBar)
        {
            ao.Add(new(nIter, "chamberPressureBar", F(n.ChamberPressureBar), F(VirtualValidation.PcWarnBar),
                "outside small-engine test-stand envelope"));
            n = n with { ChamberPressureBar = VirtualValidation.PcWarnBar };
            bPcTouched = true;
        }

        // --- Bulk coolant temperature rise ----------------------------------
        double fDtLimit = oDesign.Gas.MaxCoolantRiseK;
        if (oTherm.CoolantTempRise > fDtLimit)
        {
            double fFloor = Math.Round(oDesign.Gas.NominalOf * fOfFloorFraction, 2);
            if (n.OfRatio > fFloor + 1e-9)
            {
                double fProjectedAtFloor = oTherm.CoolantTempRise * (1.0 + fFloor) / (1.0 + n.OfRatio);
                double fNew = fProjectedAtFloor > fDtLimit
                    ? fFloor
                    : Math.Max(fFloor, Math.Round(n.OfRatio - fOfStep, 2));

                ao.Add(new(nIter, "ofRatio", F(n.OfRatio), F(fNew),
                    $"coolant dT {oTherm.CoolantTempRise:F0} K > fuel limit {fDtLimit:F0} K — more fuel coolant"));
                n = n with { OfRatio = fNew };
            }
            else
            {
                astrUnfixable.Add(
                    $"Coolant temperature rise ({oTherm.CoolantTempRise:F0} K, fuel limit {fDtLimit:F0} K): " +
                    "ofRatio is at the table floor. Options: film cooling (roadmap §8.4), a higher-cp fuel, or lower Pc/thrust.");
            }
        }

        // --- Peak wall temperature (1D regen) --------------------------------
        if (oTherm.PeakWallTempK > RegenSolver.MaxWallTempK)
        {
            // Try film cooling first if not enabled
            if (n.Injector.FilmCoolingFraction == 0.0)
            {
                ao.Add(new(nIter, "injector.filmCoolingFraction", "0.0", "0.05",
                    $"peak wall {oTherm.PeakWallTempK:F0} K > {RegenSolver.MaxWallTempK:F0} K — enable film cooling"));
                n = n with { Injector = n.Injector with { FilmCoolingFraction = 0.05, FilmCooling = true } };
            }
            // Increase film cooling fraction if already enabled but insufficient
            else if (n.Injector.FilmCoolingFraction < 0.15)
            {
                double fNew = Math.Min(n.Injector.FilmCoolingFraction + 0.05, 0.15);
                ao.Add(new(nIter, "injector.filmCoolingFraction", F(n.Injector.FilmCoolingFraction), F(fNew),
                    $"peak wall {oTherm.PeakWallTempK:F0} K > {RegenSolver.MaxWallTempK:F0} K — increase film cooling"));
                n = n with { Injector = n.Injector with { FilmCoolingFraction = fNew } };
            }
            // Fall back to channel sizing if film cooling maxed out
            else
            {
                CoolingSpec oSized = RegenSolver.SizeChannelsForWallTemp(
                    oDesign, new NozzleContour(oDesign), n.Cooling);
                if (oSized.Count != n.Cooling.Count || Math.Abs(oSized.DiameterMM - n.Cooling.DiameterMM) > 1e-6)
                {
                    ao.Add(new(nIter, "cooling",
                        $"{n.Cooling.Count}x{n.Cooling.DiameterMM:F1}mm",
                        $"{oSized.Count}x{oSized.DiameterMM:F1}mm",
                        $"peak wall {oTherm.PeakWallTempK:F0} K > {RegenSolver.MaxWallTempK:F0} K (film cooling at max)"));
                    n = n with { Cooling = oSized };
                }
            }
        }

        // --- Throat heat flux ------------------------------------------------
        if (oTherm.ThroatHeatFlux / 1e6 > VirtualValidation.ThroatFluxFailMW && !bPcTouched)
        {
            if (n.ChamberPressureBar - fPcStepBar >= fPcFloorBar)
            {
                double fNew = n.ChamberPressureBar - fPcStepBar;
                ao.Add(new(nIter, "chamberPressureBar", F(n.ChamberPressureBar), F(fNew),
                    $"throat flux {oTherm.ThroatHeatFlux / 1e6:F1} MW/m2 beyond regen-cooled copper"));
                n = n with { ChamberPressureBar = fNew };
            }
            else
            {
                astrUnfixable.Add(
                    $"Throat heat flux ({oTherm.ThroatHeatFlux / 1e6:F1} MW/m2): chamber pressure already at " +
                    $"the {fPcFloorBar:F0} bar floor. Lower thrust per engine or a larger throat (different " +
                    "thrust class) is required.");
            }
        }

        // --- Injector stability: drivers are not spec fields ----------------
        double fTauMs = oDesign.Gas.LStar / oDesign.CStar * 1000.0;
        double fDpRatio = oDesign.InjectorDeltaP / oDesign.Spec.Pc;
        if (fTauMs < 0.4 || fDpRatio < 0.15)
        {
            astrUnfixable.Add(
                $"Injector stability (τ_c {fTauMs:F2} ms, Δp/Pc {fDpRatio * 100:F0}%): set by gas-table " +
                "L*/c* and the fixed 20% Δp sizing rule — not spec-tunable in v1. Needs a different " +
                "propellant pair or an impinging/swirl injector (roadmap §8.4).");
        }

        oNext = n;
        return ao;
    }

    /// <summary>
    /// Same thrust/Pc re-sized with each propellant pair at its nominal O/F —
    /// shows whether the coolant gate is passable at all in this thrust class.
    /// </summary>
    static List<PropellantOption> PropellantWhatIf(EngineSpec oSpec)
    {
        List<PropellantOption> ao = [];
        foreach (string strKey in CombustionGas.PropellantKeys)
        {
            try
            {
                CombustionGas oNom = CombustionGas.ForPair(strKey);
                EngineSpec oTry = oSpec with { Propellants = strKey, OfRatio = oNom.NominalOf };
                EngineDesign oDesign = EngineSizing.Size(oTry);
                ThermalResult oTherm = ThermalModel.Evaluate(oDesign, new NozzleContour(oDesign));
                ao.Add(new(strKey, oNom.Name, oTherm.CoolantTempRise,
                    oTherm.CoolantTempRise <= oNom.MaxCoolantRiseK));
            }
            catch
            {
                // sizing/contour can't close for this pair at these settings — skip
            }
        }
        return [.. ao.OrderBy(o => o.CoolantRiseK)];
    }

    /// <summary>IMP-1K-A → IMP-1K-B; names without a revision letter get "-B".</summary>
    static string BumpRevision(string strName)
    {
        Match m = Regex.Match(strName, @"^(.*)-([A-Y])$");
        if (m.Success)
            return $"{m.Groups[1].Value}-{(char)(m.Groups[2].Value[0] + 1)}";
        return strName + "-B";
    }

    static string F(double f) => f.ToString("0.##", CultureInfo.InvariantCulture);
}

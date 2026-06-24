using System.Globalization;
using System.Text.RegularExpressions;

namespace Impetus.Physics;

public record IterationStep(int Iteration, string Field, string From, string To, string Reason);

public record PropellantOption(string Key, string Name, double CoolantRiseK, bool PassesGate);

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
}

/// <summary>
/// Rule-based auto-correction: turns the validation gate's recommended actions
/// into actual spec mutations and loops (size → validate → mutate) until the
/// analytic checks pass, every knob hits its physical bound, or the iteration
/// cap is reached. CFD is intentionally not run inside the loop (minutes per
/// pass) — run `test` on the result.
///
/// The loop only fixes what the v1 model can see. Checks whose drivers are
/// not spec fields (gas-table L*, c*, the lumped coolant balance at small
/// scale) are reported as "not fixable from the spec" with the upgrade path,
/// instead of being silently iterated forever.
/// </summary>
public static class AutoIterate
{
    const int nMaxIterations = 20;
    const double fPcStepBar = 2.0;
    const double fPcFloorBar = 12.0;
    const double fOfStep = 0.1;
    const double fOfFloorFraction = 0.85; // gas tables valid to ~±15% of nominal O/F

    public static IterateOutcome Run(EngineSpec oSpec)
    {
        EngineSpec oCur = oSpec with { Name = BumpRevision(oSpec.Name) };
        List<IterationStep> aoSteps = [];
        List<string> astrUnfixable = [];

        (EngineDesign oDesign, ThermalResult oTherm, ValidationResult oVal) = Evaluate(oCur);
        EngineDesign oOrigDesign = oDesign;
        ThermalResult oOrigTherm = oTherm;

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
        }

        bool bConverged = !HasFail(oVal);

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
            Iterations = nIter
        };
    }

    static (EngineDesign, ThermalResult, ValidationResult) Evaluate(EngineSpec oSpec)
    {
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        NozzleContour oContour = new(oDesign);
        ThermalResult oTherm = ThermalModel.Evaluate(oDesign, oContour);
        ValidationResult oVal = VirtualValidation.Evaluate(oDesign, oTherm);
        return (oDesign, oTherm, oVal);
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
        // v1 lumped balance: dT = Q / (mdot_fuel · cp). Channel geometry does
        // not appear in that equation — the only spec lever is more fuel flow,
        // i.e. a lower O/F (bounded by the gas table's ±15% validity band).
        double fDtLimit = oDesign.Gas.MaxCoolantRiseK;
        if (oTherm.CoolantTempRise > fDtLimit)
        {
            double fFloor = Math.Round(oDesign.Gas.NominalOF * fOfFloorFraction, 2);
            if (n.OfRatio > fFloor + 1e-9)
            {
                // dT scales ~ (1+OF); if even the floor can't pass, jump there in one step
                double fProjectedAtFloor = oTherm.CoolantTempRise * (1.0 + fFloor) / (1.0 + n.OfRatio);
                double fNew = fProjectedAtFloor > fDtLimit
                    ? fFloor
                    : Math.Max(fFloor, Math.Round(n.OfRatio - fOfStep, 2));

                ao.Add(new(nIter, "ofRatio", F(n.OfRatio), F(fNew),
                    $"coolant dT {oTherm.CoolantTempRise:F0} K > fuel limit {fDtLimit:F0} K — more fuel = more coolant"));
                n = n with { OfRatio = fNew };
            }
            else
            {
                astrUnfixable.Add(
                    $"Coolant temperature rise ({oTherm.CoolantTempRise:F0} K, fuel limit {fDtLimit:F0} K): " +
                    "ofRatio is at its -15% floor and, in the v1 lumped balance, bulk dT depends only on " +
                    "fuel flow × heat capacity — no spec field can close the gap. Real options: film cooling " +
                    "(roadmap §8.3), a higher-cp fuel (see propellant what-if table), or a larger engine " +
                    "(better volume-to-surface ratio).");
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
        foreach ((string strKey, CombustionGas oGas) in CombustionGas.Table)
        {
            try
            {
                EngineSpec oTry = oSpec with { Propellants = strKey, OfRatio = oGas.NominalOF };
                EngineDesign oDesign = EngineSizing.Size(oTry);
                ThermalResult oTherm = ThermalModel.Evaluate(oDesign, new NozzleContour(oDesign));
                ao.Add(new(strKey, oGas.Name, oTherm.CoolantTempRise,
                    oTherm.CoolantTempRise <= oGas.MaxCoolantRiseK));
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

using Impetus;

namespace Impetus.Physics;

public enum CheckStatus { Pass, Warn, Fail }

public record ValidationCheck(
    string Name,
    CheckStatus Status,
    string Detail,
    string Action,
    string? SpecField = null);

public record FeedSystemSizing
{
    public required double FuelTankPressureBar { get; init; }
    public required double OxTankPressureBar { get; init; }
    public required double FeedLineDiameterMM { get; init; }
    public required double FuelMassKg { get; init; }
    public required double OxMassKg { get; init; }
    public required double BurnDurationS { get; init; }
}

public record ValidationResult
{
    public required IReadOnlyList<ValidationCheck> Checks { get; init; }
    public required FeedSystemSizing Feed { get; init; }
    public required string Verdict { get; init; }
    public required string VerdictSummary { get; init; }
    public required bool CfdVerified { get; init; }
    public required ManufacturabilityResult? Manufacturability { get; init; }
}

/// <summary>
/// Virtual test-stand checks: screening a design before any physical hot fire.
/// Impetus cannot replace a test stand, but it can flag bad designs early and
/// size the feed system / define what the physical test must prove.
/// </summary>
public static class VirtualValidation
{
    // Shared gate thresholds (also consumed by AutoIterate's corrective rules).
    // The coolant-rise limit is per-fuel: CombustionGas.MaxCoolantRiseK.
    public const double MinCoolantVelocityMS = 2.0;
    public const double MaxCoolantVelocityMS = 12.0;
    public const double ThroatFluxWarnMW = 25.0;
    public const double ThroatFluxFailMW = 40.0;
    public const double PcWarnBar = 30.0;
    public const double PcFailBar = 50.0;
    public const double MinChannelDiameterMM = 0.8;
    public const int MinChannelCount = 12;

    const double fDefaultBurnS = 5.0;
    const double fLineLengthM = 2.0;
    const double fLineLossBar = 0.3;
    const double fValveLossBar = 0.2;
    const double fTankMarginBar = 1.0;

    public static ValidationResult Evaluate(
        EngineDesign oDesign,
        ThermalResult oTherm,
        Cfd.CfdResult? oCfd = null,
        double? fBurnDurationS = null,
        ManufacturabilityResult? oManufacturability = null)
    {
        double fBurn = fBurnDurationS ?? fDefaultBurnS;
        List<ValidationCheck> aoChecks = [];

        aoChecks.Add(CheckCoolantRise(oDesign, oTherm));
        aoChecks.Add(CheckCoolantVelocity(oTherm));
        aoChecks.Add(CheckWallTemperature(oTherm));
        aoChecks.Add(CheckInjectorStiffness(oDesign));
        aoChecks.Add(CheckInjectorStability(oDesign));
        aoChecks.Add(CheckExpansionMatch(oDesign));
        aoChecks.Add(CheckThroatHeatFlux(oTherm));
        aoChecks.Add(CheckCoolingChannels(oDesign.Spec.Cooling, oDesign.Spec.TargetProcess));
        aoChecks.Add(CheckChamberPressure(oDesign));
        aoChecks.Add(CheckProcessRequirements(oDesign.Spec));
        if (oCfd is not null)
            aoChecks.Add(CheckCfdAgreement(oDesign, oCfd));
        else
            aoChecks.Add(new(
                "CFD virtual test",
                CheckStatus.Warn,
                "No SU2 result on disk — nozzle aerodynamics not yet verified.",
                "Run: dotnet run --project src/Impetus -- test <spec.json>",
                null));

        FeedSystemSizing oFeed = SizeFeedSystem(oDesign, fBurn);
        (string strVerdict, string strSummary) = Summarize(aoChecks, oCfd is not null, oManufacturability);

        return new ValidationResult
        {
            Checks = aoChecks,
            Feed = oFeed,
            Verdict = strVerdict,
            VerdictSummary = strSummary,
            CfdVerified = oCfd is not null && oCfd.Converged,
            Manufacturability = oManufacturability
        };
    }

    static ValidationCheck CheckCoolantRise(EngineDesign o, ThermalResult oTherm)
    {
        // Per-fuel coking / boiling / decomposition margin on the bulk rise;
        // two thirds of the limit is the comfortable band.
        double fDT = oTherm.CoolantTempRise;
        double fLimit = o.Gas.MaxCoolantRiseK;
        if (fDT <= fLimit * 2.0 / 3.0)
            return new("Coolant temperature rise", CheckStatus.Pass,
                $"{fDT:F0} K — within comfortable regen margin (fuel limit {fLimit:F0} K).",
                "None.",
                "ofRatio");
        if (fDT <= fLimit)
            return new("Coolant temperature rise", CheckStatus.Warn,
                $"{fDT:F0} K — under the {fLimit:F0} K fuel limit but tight; watch outlet temperature on hot fire.",
                "Margin is thin — a slightly lower ofRatio adds fuel coolant; verify with instrumented short burns.",
                "ofRatio");
        return new("Coolant temperature rise", CheckStatus.Fail,
            $"{fDT:F0} K — exceeds this fuel's {fLimit:F0} K coolant limit; boiling / coking likely.",
            "Lower ofRatio (more fuel coolant), increase regen-cooled length, or run iterate to auto-tune channels and O/F.",
            "ofRatio");
    }

    static ValidationCheck CheckWallTemperature(ThermalResult oTherm)
    {
        double fTw = oTherm.PeakWallTempK;
        if (double.IsNaN(fTw))
            fTw = oTherm.AssumedWallTemp;

        if (fTw <= RegenSolver.MaxWallTempK * 0.9)
            return new("Peak wall temperature (1D regen)", CheckStatus.Pass,
                $"{fTw:F0} K — within CuCrZr regen liner limit ({RegenSolver.MaxWallTempK:F0} K).",
                "None.",
                "cooling");
        if (fTw <= RegenSolver.MaxWallTempK)
            return new("Peak wall temperature (1D regen)", CheckStatus.Warn,
                $"{fTw:F0} K — near the {RegenSolver.MaxWallTempK:F0} K design ceiling.",
                "Increase cooling.count or reduce channel diameter (higher h_c); run iterate.",
                "cooling");
        return new("Peak wall temperature (1D regen)", CheckStatus.Fail,
            $"{fTw:F0} K — exceeds {RegenSolver.MaxWallTempK:F0} K CuCrZr regen limit.",
            "Increase cooling.count, reduce channel diameter for higher velocity, or lower Pc / O/F.",
            "cooling");
    }

    static ValidationCheck CheckCoolantVelocity(ThermalResult oTherm)
    {
        double fV = oTherm.CoolantVelocity;
        if (fV is >= MinCoolantVelocityMS and <= MaxCoolantVelocityMS)
            return new("Coolant channel velocity", CheckStatus.Pass,
                $"{fV:F1} m/s — in the usual 2–12 m/s regen window.",
                "None.",
                "cooling");
        if (fV < MinCoolantVelocityMS)
            return new("Coolant channel velocity", CheckStatus.Warn,
                $"{fV:F1} m/s — low; soot deposition risk in channels.",
                "Reduce cooling.count or cooling.diameterMM (less flow area = faster coolant).",
                "cooling");
        return new("Coolant channel velocity", CheckStatus.Warn,
            $"{fV:F1} m/s — high; erosion risk in soft copper alloys.",
            "Increase channel count or diameter, or add parallel paths to lower velocity.",
                "cooling");
    }

    static ValidationCheck CheckInjectorStiffness(EngineDesign o)
    {
        double fRatio = o.InjectorDeltaP / o.Spec.Pc;
        if (fRatio is >= 0.15 and <= 0.30)
            return new("Injector pressure drop", CheckStatus.Pass,
                $"{fRatio * 100:F0}% of Pc ({o.InjectorDeltaP / 1e5:F1} bar) — adequate stiffness.",
                "None.",
                null);
        if (fRatio < 0.15)
            return new("Injector pressure drop", CheckStatus.Warn,
                $"{fRatio * 100:F0}% of Pc — may be too soft for stable combustion.",
                "Target 15–25% of Pc; Impetus uses 20% by default in sizing.",
                null);
        return new("Injector pressure drop", CheckStatus.Warn,
            $"{fRatio * 100:F0}% of Pc — high feed pressure requirement.",
            "Acceptable if tank/feed can supply it; otherwise reduce stiffness target.",
                null);
    }

    static ValidationCheck CheckInjectorStability(EngineDesign o)
    {
        // First-order chugging screen: combustion time scale vs injector response.
        // τ_c ≈ L*/c* ; stable showers want L* > ~0.8 m and stiff injection.
        double fTauC = o.Gas.LStar / o.CStar * 1000.0; // ms
        double fDpRatio = o.InjectorDeltaP / o.Spec.Pc;

        if (fTauC >= 0.5 && fDpRatio >= 0.18)
            return new("Injector stability (screening)", CheckStatus.Pass,
                $"τ_c ≈ {fTauC:F1} ms, Δp/Pc = {fDpRatio * 100:F0}% — passes simplified chugging screen.",
                "Showerhead pattern is still the least stable option; impinging elements are better for hot fire.",
                "injector");

        if (fTauC < 0.4 || fDpRatio < 0.15)
            return new("Injector stability (screening)", CheckStatus.Fail,
                $"τ_c ≈ {fTauC:F1} ms, Δp/Pc = {fDpRatio * 100:F0}% — elevated chugging risk.",
                "Increase L* (chamber length), raise injector Δp, or move to impinging injector (future Impetus module).",
                "injector");

        return new("Injector stability (screening)", CheckStatus.Warn,
            $"τ_c ≈ {fTauC:F1} ms, Δp/Pc = {fDpRatio * 100:F0}% — marginal; showerhead + marginal L* is risky.",
            "Run cold-flow injector tests before hot fire; consider impinging elements.",
            "injector");
    }

    static ValidationCheck CheckExpansionMatch(EngineDesign o)
    {
        double fPeBar = o.ExitPressure / 1e5;
        double fPaBar = o.Spec.AmbientPressureBar;
        double fRatio = fPeBar / fPaBar;

        if (fRatio is >= 0.95 and <= 1.05)
            return new("Nozzle expansion vs ambient", CheckStatus.Pass,
                $"Pe = {fPeBar:F3} bar vs Pa = {fPaBar:F3} bar — near optimal expansion.",
                "None.",
                "expansionRatio");
        if (fRatio < 0.85)
            return new("Nozzle expansion vs ambient", CheckStatus.Warn,
                $"Pe = {fPeBar:F3} bar < Pa — over-expanded; separation possible at sea level.",
                "Reduce expansion ratio or test at lower ambient (altitude simulation).",
                "expansionRatio");
        return new("Nozzle expansion vs ambient", CheckStatus.Warn,
            $"Pe = {fPeBar:F3} bar vs Pa = {fPaBar:F3} bar — under-expanded; thrust loss at nozzle.",
            "Increase expansion ratio or accept loss at this ambient.",
                "expansionRatio");
    }

    static ValidationCheck CheckThroatHeatFlux(ThermalResult oTherm)
    {
        double fQ = oTherm.ThroatHeatFlux / 1e6;
        if (fQ <= ThroatFluxWarnMW)
            return new("Throat heat flux (Bartz)", CheckStatus.Pass,
                $"{fQ:F1} MW/m² — within CuCrZr regen-cooled range for short burns.",
                "None.",
                "chamberPressureBar");
        if (fQ <= ThroatFluxFailMW)
            return new("Throat heat flux (Bartz)", CheckStatus.Warn,
                $"{fQ:F1} MW/m² — high; 1D regen march reports peak wall {oTherm.PeakWallTempK:F0} K.",
                "Consider Inconel 718 for uncooled regions or more aggressive channel sizing.",
                "chamberPressureBar");
        return new("Throat heat flux (Bartz)", CheckStatus.Fail,
            $"{fQ:F1} MW/m² — beyond typical regen-cooled copper capability.",
            "Reduce Pc, enlarge throat, or upgrade cooling geometry.",
                "chamberPressureBar");
    }

    static ValidationCheck CheckCoolingChannels(CoolingSpec oCool, ManufacturingProcess eProcess)
    {
        double fMinChannel = ProcessGeometry.GetMinChannelDiameter(eProcess);
        string strProcessName = eProcess.ToString();

        if (oCool.DiameterMM >= fMinChannel && oCool.Count >= MinChannelCount)
            return new("Cooling channel manufacturability", CheckStatus.Pass,
                $"{oCool.Count} × Ø{oCool.DiameterMM:F1} mm — suitable for {strProcessName}.",
                "None.",
                "cooling");
        if (oCool.DiameterMM >= fMinChannel * 0.8)
            return new("Cooling channel manufacturability", CheckStatus.Warn,
                $"{oCool.Count} × Ø{oCool.DiameterMM:F1} mm — tight for {strProcessName}; verify manufacturability.",
                $"Increase cooling.diameterMM to at least {fMinChannel} mm.",
                "cooling");
        return new("Cooling channel manufacturability", CheckStatus.Fail,
            $"{oCool.Count} × Ø{oCool.DiameterMM:F1} mm — below minimum for {strProcessName}.",
            $"Increase cooling.diameterMM to at least {fMinChannel} mm.",
                "cooling");
    }

    static ValidationCheck CheckProcessRequirements(EngineSpec oSpec)
    {
        (bool bMeets, List<string> aoFailures) = ProcessGeometry.ValidateProcessRequirements(oSpec);

        if (bMeets)
        {
            // Check for warnings
            List<string> aoWarnings = ProcessGeometry.GetProcessWarnings(oSpec);
            if (aoWarnings.Count > 0)
            {
                return new("Process requirements", CheckStatus.Warn,
                    $"Spec meets {oSpec.TargetProcess} requirements with warnings: {string.Join("; ", aoWarnings)}",
                    "Review warnings above; consider adjusting spec if needed.",
                    "targetProcess");
            }

            return new("Process requirements", CheckStatus.Pass,
                $"Spec meets all {oSpec.TargetProcess} requirements.",
                "None.",
                "targetProcess");
        }

        return new("Process requirements", CheckStatus.Fail,
            $"Spec fails {oSpec.TargetProcess} requirements: {string.Join("; ", aoFailures)}",
            "Adjust cooling channels, wall thickness, or voxel size to meet process requirements.",
                "targetProcess");
    }

    static ValidationCheck CheckChamberPressure(EngineDesign o)
    {
        double fPc = o.Spec.ChamberPressureBar;
        if (fPc <= PcWarnBar)
            return new("Chamber pressure", CheckStatus.Pass,
                $"{fPc:F1} bar — within small-engine test-stand range.",
                "None.",
                "chamberPressureBar");
        if (fPc <= PcFailBar)
            return new("Chamber pressure", CheckStatus.Warn,
                $"{fPc:F1} bar — needs qualified pressure vessels and remote operation.",
                "Ensure feed hardware and test cell are rated; add burst disks.",
                "chamberPressureBar");
        return new("Chamber pressure", CheckStatus.Fail,
            $"{fPc:F1} bar — outside typical amateur / university test-stand envelope.",
            "Reduce chamberPressureBar unless you have certified high-pressure infrastructure.",
                "chamberPressureBar");
    }

    static ValidationCheck CheckCfdAgreement(EngineDesign o, Cfd.CfdResult oCfd)
    {
        if (!oCfd.Converged)
            return new("CFD vs analytic", CheckStatus.Fail,
                "SU2 did not converge — virtual nozzle test invalid.",
                "Check cfd/su2.log; reduce expansion ratio or edit Su2Case.cs CFL limits.",
                "expansionRatio");

        double fDevThrust = 100.0 * Math.Abs(oCfd.ThrustN - o.Spec.ThrustN) / o.Spec.ThrustN;
        double fDevMdot = 100.0 * Math.Abs(oCfd.MassFlow - o.MassFlow) / o.MassFlow;

        if (fDevThrust <= 5.0 && fDevMdot <= 5.0)
            return new("CFD vs analytic", CheckStatus.Pass,
                $"Thrust {fDevThrust:F1}% / mass flow {fDevMdot:F1}% deviation — nozzle aerodynamics verified.",
                "None.",
                null);
        if (fDevThrust <= 10.0 && fDevMdot <= 10.0)
            return new("CFD vs analytic", CheckStatus.Warn,
                $"Thrust {fDevThrust:F1}% / mass flow {fDevMdot:F1}% deviation — acceptable for RANS (viscous losses).",
                "Investigate mesh or spec if deviation grows on iteration.",
                null);
        return new("CFD vs analytic", CheckStatus.Fail,
            $"Thrust {fDevThrust:F1}% / mass flow {fDevMdot:F1}% deviation — analytic and CFD disagree.",
            "Do not hot-fire until nozzle sizing is reconciled.",
                null);
    }

    static FeedSystemSizing SizeFeedSystem(EngineDesign o, double fBurnS)
    {
        double fPcBar = o.Spec.ChamberPressureBar;
        double fInjBar = o.InjectorDeltaP / 1e5;
        double fReqBar = fPcBar + fInjBar + fLineLossBar + fValveLossBar + fTankMarginBar;

        // Darcy-Weisbach rough sizing: target v ≈ 5 m/s in feed lines
        const double fTargetVelocity = 5.0;
        double fMdotMax = Math.Max(o.MassFlowFuel, o.MassFlowOx);
        double fRhoMax = Math.Max(o.Gas.FuelDensity, o.Gas.OxDensity);
        double fArea = fMdotMax / (fRhoMax * fTargetVelocity);
        double fDiamMM = 2.0 * Math.Sqrt(fArea / Math.PI) * 1000.0;
        fDiamMM = Math.Max(fDiamMM, 6.0); // practical minimum

        return new FeedSystemSizing
        {
            FuelTankPressureBar = fReqBar,
            OxTankPressureBar = fReqBar,
            FeedLineDiameterMM = fDiamMM,
            FuelMassKg = o.MassFlowFuel * fBurnS,
            OxMassKg = o.MassFlowOx * fBurnS,
            BurnDurationS = fBurnS
        };
    }

    static (string Verdict, string Summary) Summarize(
        IReadOnlyList<ValidationCheck> aoChecks, bool bHasCfd, ManufacturabilityResult? oManufacturability)
    {
        int nFail = aoChecks.Count(c => c.Status == CheckStatus.Fail);
        int nWarn = aoChecks.Count(c => c.Status == CheckStatus.Warn);

        // Include manufacturability in verdict
        if (oManufacturability is not null)
        {
            int nMfgFail = oManufacturability.Checks.Count(c => c.Status == CheckStatus.Fail);
            int nMfgWarn = oManufacturability.Checks.Count(c => c.Status == CheckStatus.Warn);

            if (nMfgFail > 0)
                nFail += nMfgFail;
            if (nMfgWarn > 0)
                nWarn += nMfgWarn;
        }

        if (nFail > 0)
            return ("ITERATE_DESIGN",
                $"{nFail} virtual check(s) failed — fix the spec and re-run before building hardware.");

        if (!bHasCfd)
            return ("VIRTUAL_PARTIAL",
                "Analytic checks passed with warnings, but CFD not run — nozzle not yet verified.");

        if (nWarn > 0)
            return ("VIRTUAL_OK_WITH_WARNINGS",
                "Virtual checks passed with warnings — proceed to hardware with caution; follow the hot-fire ladder.");

        return ("VIRTUAL_OK",
            "All virtual checks passed — proceed to metal AM, feed hardware, and the physical test ladder.");
    }
}

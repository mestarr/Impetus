namespace Impetus.Physics;

/// <summary>
/// Multi-engine and stage sizing: thrust class sweeps and stage Δv budgets.
/// </summary>
public static class StageSizing
{
    /// <summary>
    /// Stage definition for multi-stage vehicle analysis.
    /// </summary>
    public record StageDefinition
    {
        public required string Name { get; init; }
        public required double ThrustN { get; init; }           // Total stage thrust [N]
        public required double BurnTimeS { get; init; }         // Stage burn time [s]
        public required double DryMassKg { get; init; }         // Stage dry mass (structure + engines) [kg]
        public required double PropellantMassKg { get; init; } // Stage propellant mass [kg]
        public required int EngineCount { get; init; }         // Number of engines
    }

    /// <summary>
    /// Result of stage analysis including delta-v and mass ratios.
    /// </summary>
    public record StageResult
    {
        public required StageDefinition Stage { get; init; }
        public required double DeltaV { get; init; }            // Stage delta-v [m/s]
        public required double MassRatio { get; init; }         // Mass ratio (m0/mf)
        public required double Isp { get; init; }               // Effective Isp [s]
        public required double TotalMassKg { get; init; }       // Total stage mass at ignition [kg]
    }

    /// <summary>
    /// Thrust class sweep result for a single engine design.
    /// </summary>
    public record ThrustClassResult
    {
        public required double ThrustN { get; init; }
        public required double IspVac { get; init; }
        public required double MassFlow { get; init; }
        public required double ThroatArea { get; init; }
        public required double ExitArea { get; init; }
    }

    /// <summary>
    /// Perform a thrust class sweep: analyze engine performance across a range of thrust levels.
    /// Returns performance metrics for each thrust level.
    /// </summary>
    public static List<ThrustClassResult> ThrustClassSweep(
        EngineSpec oBaseSpec,
        double[] afThrustN) // Thrust levels to analyze [N]
    {
        List<ThrustClassResult> aoResults = [];

        foreach (double fThrust in afThrustN)
        {
            EngineSpec oSpec = oBaseSpec with { ThrustN = fThrust };
            EngineDesign oDesign = EngineSizing.Size(oSpec);
            NozzleContour oContour = new(oDesign);

            // Calculate vacuum Isp
            double fCfVac = IsentropicFlow.ThrustCoefficient(
                oDesign.Gas.Gamma, oDesign.ExpansionRatio, oDesign.ExitPressure, oDesign.ChamberPressurePa, 0.0);
            double fIspVac = fCfVac * oDesign.CStar / CombustionGas.G0;

            aoResults.Add(new ThrustClassResult
            {
                ThrustN = fThrust,
                IspVac = fIspVac,
                MassFlow = oDesign.MassFlow,
                ThroatArea = oDesign.ThroatArea,
                ExitArea = oDesign.ExitArea
            });
        }

        return aoResults;
    }

    /// <summary>
    /// Calculate stage delta-v using the rocket equation.
    /// Δv = Isp * g0 * ln(m0 / mf)
    /// </summary>
    public static double CalculateDeltaV(double fIsp, double fMassInitial, double fMassFinal)
        => fIsp * CombustionGas.G0 * Math.Log(fMassInitial / fMassFinal);

    /// <summary>
    /// Analyze a single stage performance.
    /// </summary>
    public static StageResult AnalyzeStage(StageDefinition oStage, double fIsp)
    {
        double fTotalMass = oStage.DryMassKg + oStage.PropellantMassKg;
        double fMassFinal = oStage.DryMassKg;
        double fDeltaV = CalculateDeltaV(fIsp, fTotalMass, fMassFinal);
        double fMassRatio = fTotalMass / fMassFinal;

        return new StageResult
        {
            Stage = oStage,
            DeltaV = fDeltaV,
            MassRatio = fMassRatio,
            Isp = fIsp,
            TotalMassKg = fTotalMass
        };
    }

    /// <summary>
    /// Analyze multi-stage vehicle performance.
    /// Returns cumulative delta-v and individual stage results.
    /// </summary>
    public static (double TotalDeltaV, List<StageResult> StageResults) AnalyzeMultiStage(
        List<StageDefinition> aoStages,
        double[] afStageIsp) // Isp for each stage [s]
    {
        if (aoStages.Count != afStageIsp.Length)
            throw new ArgumentException("Stage count must match Isp array length.");

        List<StageResult> aoResults = [];
        double fTotalDeltaV = 0;

        for (int i = 0; i < aoStages.Count; i++)
        {
            StageResult oResult = AnalyzeStage(aoStages[i], afStageIsp[i]);
            aoResults.Add(oResult);
            fTotalDeltaV += oResult.DeltaV;
        }

        return (fTotalDeltaV, aoResults);
    }

    /// <summary>
    /// Estimate required propellant mass for target delta-v.
    /// Inverse rocket equation: mf = m0 / exp(Δv / (Isp * g0))
    /// </summary>
    public static double PropellantMassForDeltaV(
        double fTargetDeltaV,
        double fIsp,
        double fDryMass)
    {
        double fMassRatio = Math.Exp(fTargetDeltaV / (fIsp * CombustionGas.G0));
        return fDryMass * (fMassRatio - 1.0);
    }

    /// <summary>
    /// Generate a standard thrust class sweep for common rocket engine categories.
    /// Returns thrust levels from 100 N to 1 MN in logarithmic steps.
    /// </summary>
    public static double[] StandardThrustClasses()
    {
        return
        [
            100,      // Small thruster
            500,      // Small engine
            1000,     // 1 kN class
            5000,     // 5 kN class
            10000,    // 10 kN class
            50000,    // 50 kN class
            100000,   // 100 kN class
            250000,   // 250 kN class
            500000,   // 500 kN class
            1000000   // 1 MN class
        ];
    }
}

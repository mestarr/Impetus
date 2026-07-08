using System.Globalization;
using System.Linq;
using System.Text;

namespace Impetus.Physics;

/// <summary>
/// Sweep configuration: which parameters to vary and over what ranges.
/// </summary>
public record SweepConfig
{
    /// <summary>Chamber pressure values to sweep [bar].</summary>
    public double[]? PcBar { get; init; }

    /// <summary>O/F ratio values to sweep.</summary>
    public double[]? OfRatio { get; init; }

    /// <summary>Expansion ratio values to sweep (0 = optimal).</summary>
    public double[]? ExpansionRatio { get; init; }

    /// <summary>Bell fraction values to sweep (0.6-1.0 typical).</summary>
    public double[]? BellFraction { get; init; }

    /// <summary>Cooling channel count values to sweep.</summary>
    public int[]? CoolingCount { get; init; }

    /// <summary>Cooling channel diameter values to sweep [mm].</summary>
    public double[]? CoolingDiameterMM { get; init; }

    /// <summary>Number of random samples (if > 0, overrides grid).</summary>
    public int RandomSamples { get; init; } = 0;

    /// <summary>Random seed for reproducibility.</summary>
    public int? RandomSeed { get; init; }
}

/// <summary>
/// Single point in the design space with its evaluated metrics.
/// </summary>
public record SweepPoint
{
    public required EngineSpec Spec { get; init; }
    public required EngineDesign Design { get; init; }
    public required ThermalResult Thermal { get; init; }

    // Key metrics for Pareto filtering
    public double IspSeaLevelS => Design.IspSeaLevelS;
    public double CoolantTempRise => Thermal.CoolantTempRise;
    public double EngineLengthM => Design.ChamberCylinderLength + Design.BellLength;
    public double ThroatHeatFluxMW => Thermal.ThroatHeatFlux / 1e6;
    public double PeakWallTempK => Thermal.PeakWallTempK;
}

/// <summary>
/// Sweep results with Pareto front analysis.
/// </summary>
public record SweepResult
{
    public required EngineSpec BaseSpec { get; init; }
    public required IReadOnlyList<SweepPoint> Points { get; init; }
    public required IReadOnlyList<SweepPoint> ParetoFront { get; init; }
    public required TimeSpan EvaluationTime;
    public required int TotalCombinations;
}

/// <summary>
/// Parameter sweep: grid or random sampling over design space with Pareto analysis.
/// </summary>
public static class ParameterSweep
{
    /// <summary>
    /// Run a full parameter sweep over the specified ranges.
    /// </summary>
    public static SweepResult Run(EngineSpec oBaseSpec, SweepConfig oConfig)
    {
        DateTime tStart = DateTime.Now;
        List<SweepPoint> aoPoints = [];

        // Generate parameter combinations
        List<EngineSpec> aoSpecs = GenerateSpecs(oBaseSpec, oConfig);

        Console.WriteLine($"Sweep: evaluating {aoSpecs.Count} design points...");

        int n = 0;
        foreach (EngineSpec oSpec in aoSpecs)
        {
            n++;
            try
            {
                EngineDesign oDesign = EngineSizing.Size(oSpec);
                NozzleContour oContour = new(oDesign);
                ThermalResult oTherm = ThermalModel.Evaluate(oDesign, oContour);

                aoPoints.Add(new SweepPoint
                {
                    Spec = oSpec,
                    Design = oDesign,
                    Thermal = oTherm
                });

                if (n % 10 == 0 || n == aoSpecs.Count)
                    Console.WriteLine($"  [{n}/{aoSpecs.Count}] Isp={oDesign.IspSeaLevelS:F1}s dT={oTherm.CoolantTempRise:F0}K");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{n}/{aoSpecs.Count}] FAILED: {ex.Message}");
            }
        }

        // Compute Pareto front (maximize Isp, minimize coolant dT, minimize length)
        IReadOnlyList<SweepPoint> aoPareto = ComputeParetoFront(aoPoints);

        return new SweepResult
        {
            BaseSpec = oBaseSpec,
            Points = aoPoints,
            ParetoFront = aoPareto,
            EvaluationTime = DateTime.Now - tStart,
            TotalCombinations = aoSpecs.Count
        };
    }

    static List<EngineSpec> GenerateSpecs(EngineSpec oBase, SweepConfig oConfig)
    {
        List<EngineSpec> aoSpecs = [];

        if (oConfig.RandomSamples > 0)
        {
            // Random sampling
            Random oRng = oConfig.RandomSeed.HasValue ? new Random(oConfig.RandomSeed.Value) : new Random();
            for (int i = 0; i < oConfig.RandomSamples; i++)
            {
                aoSpecs.Add(RandomSpec(oBase, oConfig, oRng));
            }
        }
        else
        {
            // Grid sampling (Cartesian product)
            var aoPc = oConfig.PcBar ?? [oBase.ChamberPressureBar];
            var aoOf = oConfig.OfRatio ?? [oBase.OfRatio];
            var aoEps = oConfig.ExpansionRatio ?? [oBase.ExpansionRatio];
            var aoBell = oConfig.BellFraction ?? [oBase.BellFraction];
            var aoCoolCount = oConfig.CoolingCount ?? [oBase.Cooling.Count];
            var aoCoolDiam = oConfig.CoolingDiameterMM ?? [oBase.Cooling.DiameterMM];

            foreach (double fPc in aoPc)
            foreach (double fOf in aoOf)
            foreach (double fEps in aoEps)
            foreach (double fBell in aoBell)
            foreach (int nCoolCount in aoCoolCount)
            foreach (double fCoolDiam in aoCoolDiam)
            {
                aoSpecs.Add(oBase with
                {
                    ChamberPressureBar = fPc,
                    OfRatio = fOf,
                    ExpansionRatio = fEps,
                    BellFraction = fBell,
                    Cooling = oBase.Cooling with
                    {
                        Count = nCoolCount,
                        DiameterMM = fCoolDiam
                    }
                });
            }
        }

        return aoSpecs;
    }

    static EngineSpec RandomSpec(EngineSpec oBase, SweepConfig oConfig, Random oRng)
    {
        double fPc = PickRandom(oConfig.PcBar, oBase.ChamberPressureBar, 10.0, 40.0, oRng);
        double fOf = PickRandom(oConfig.OfRatio, oBase.OfRatio, 1.5, 3.5, oRng);
        double fEps = PickRandom(oConfig.ExpansionRatio, oBase.ExpansionRatio, 0.0, 50.0, oRng);
        double fBell = PickRandom(oConfig.BellFraction, oBase.BellFraction, 0.6, 1.0, oRng);
        int nCoolCount = (int)PickRandom(
            oConfig.CoolingCount?.Select(x => (double)x).ToArray(),
            oBase.Cooling.Count, 8.0, 32.0, oRng);
        double fCoolDiam = PickRandom(oConfig.CoolingDiameterMM, oBase.Cooling.DiameterMM, 0.8, 2.5, oRng);

        return oBase with
        {
            ChamberPressureBar = fPc,
            OfRatio = fOf,
            ExpansionRatio = fEps,
            BellFraction = fBell,
            Cooling = oBase.Cooling with
            {
                Count = nCoolCount,
                DiameterMM = fCoolDiam
            }
        };
    }

    static double PickRandom(double[]? aoValues, double fDefault, double fMin, double fMax, Random oRng)
    {
        if (aoValues == null || aoValues.Length == 0)
            return fMin + oRng.NextDouble() * (fMax - fMin);
        return aoValues[oRng.Next(aoValues.Length)];
    }

    /// <summary>
    /// Compute Pareto front: points that are not dominated in any objective.
    /// Objectives: maximize Isp, minimize coolant dT, minimize length.
    /// </summary>
    static List<SweepPoint> ComputeParetoFront(List<SweepPoint> aoPoints)
    {
        List<SweepPoint> aoPareto = [];

        foreach (SweepPoint oCandidate in aoPoints)
        {
            bool bDominated = false;
            foreach (SweepPoint oOther in aoPoints)
            {
                if (oOther == oCandidate) continue;

                // oOther dominates oCandidate if it's better or equal in all objectives
                // and strictly better in at least one
                bool bBetterIsp = oOther.IspSeaLevelS >= oCandidate.IspSeaLevelS;
                bool bBetterDt = oOther.CoolantTempRise <= oCandidate.CoolantTempRise;
                bool bBetterLen = oOther.EngineLengthM <= oCandidate.EngineLengthM;
                bool bStrictlyBetter =
                    oOther.IspSeaLevelS > oCandidate.IspSeaLevelS ||
                    oOther.CoolantTempRise < oCandidate.CoolantTempRise ||
                    oOther.EngineLengthM < oCandidate.EngineLengthM;

                if (bBetterIsp && bBetterDt && bBetterLen && bStrictlyBetter)
                {
                    bDominated = true;
                    break;
                }
            }

            if (!bDominated)
                aoPareto.Add(oCandidate);
        }

        // Sort by Isp descending for presentation
        return [.. aoPareto.OrderByDescending(p => p.IspSeaLevelS)];
    }

    /// <summary>
    /// Parse command-line arguments into a SweepConfig.
    /// Format: --pc 15,20,25 --of 2.0,2.3,2.6 --eps 0,10,20 --bell 0.7,0.8,0.9
    /// </summary>
    public static SweepConfig ParseArgs(string[] args)
    {
        SweepConfig oConfig = new();

        for (int i = 0; i < args.Length; i++)
        {
            string strArg = args[i].ToLowerInvariant();
            switch (strArg)
            {
                case "--pc":
                    oConfig = oConfig with { PcBar = ParseDoubles(args[++i]) };
                    break;
                case "--of":
                    oConfig = oConfig with { OfRatio = ParseDoubles(args[++i]) };
                    break;
                case "--eps":
                case "--expansion":
                    oConfig = oConfig with { ExpansionRatio = ParseDoubles(args[++i]) };
                    break;
                case "--bell":
                    oConfig = oConfig with { BellFraction = ParseDoubles(args[++i]) };
                    break;
                case "--cool-count":
                    oConfig = oConfig with { CoolingCount = ParseInts(args[++i]) };
                    break;
                case "--cool-diam":
                    oConfig = oConfig with { CoolingDiameterMM = ParseDoubles(args[++i]) };
                    break;
                case "--random":
                    oConfig = oConfig with { RandomSamples = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
                case "--seed":
                    oConfig = oConfig with { RandomSeed = int.Parse(args[++i], CultureInfo.InvariantCulture) };
                    break;
            }
        }

        return oConfig;
    }

    static double[] ParseDoubles(string str)
        => str.Split(',').Select(s => double.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();

    static int[] ParseInts(string str)
        => str.Split(',').Select(s => int.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
}

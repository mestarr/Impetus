using System.Globalization;
using System.Linq;

namespace Impetus.Physics;

/// <summary>
/// Optimization objective: minimize a penalty function combining multiple metrics.
/// </summary>
public record OptimizationObjective
{
    /// <summary>Weight for Isp (positive = maximize, negative = minimize).</summary>
    public double IspWeight { get; init; } = 1.0;

    /// <summary>Weight for coolant temperature rise (positive = minimize).</summary>
    public double CoolantDtWeight { get; init; } = 1.0;

    /// <summary>Weight for engine length (positive = minimize).</summary>
    public double LengthWeight { get; init; } = 0.5;

    /// <summary>Weight for throat heat flux (positive = minimize).</summary>
    public double HeatFluxWeight { get; init; } = 0.1;

    /// <summary>Target coolant dT (penalty if exceeded).</summary>
    public double TargetCoolantDtK { get; init; } = 120.0;

    /// <summary>Target throat flux MW/m2 (penalty if exceeded).</summary>
    public double TargetHeatFluxMW { get; init; } = 15.0;
}

/// <summary>
/// Optimization result with final design and convergence info.
/// </summary>
public record OptimizationResult
{
    public required EngineSpec BestSpec { get; init; }
    public required EngineDesign BestDesign { get; init; }
    public required ThermalResult BestThermal { get; init; }
    public required double BestObjectiveValue;
    public required int Iterations;
    public required bool Converged;
    public required IReadOnlyList<EngineSpec> History;
}

/// <summary>
/// Nelder-Mead simplex optimizer for multi-objective engine design.
/// </summary>
public static class Optimizer
{
    const double fReflection = 1.0;
    const double fExpansion = 2.0;
    const double fContraction = 0.5;
    const double fShrink = 0.5;
    const double fTolerance = 1e-6;
    const int nMaxIterations = 100;

    /// <summary>
    /// Run Nelder-Mead optimization on specified spec parameters.
    /// </summary>
    /// <param name="oBaseSpec">Base specification to optimize from.</param>
    /// <param name="aoParams">Parameters to optimize: "pc", "of", "eps", "bell", "coolCount", "coolDiam".</param>
    /// <param name="aoBounds">Bounds for each parameter as (min, max) tuples.</param>
    /// <param name="oObjective">Objective function weights.</param>
    public static OptimizationResult Optimize(
        EngineSpec oBaseSpec,
        string[] aoParams,
        (double fMin, double fMax)[] aoBounds,
        OptimizationObjective oObjective)
    {
        if (aoParams.Length != aoBounds.Length)
            throw new ArgumentException("Parameters and bounds must have same length.");

        int nDim = aoParams.Length;
        if (nDim < 1 || nDim > 6)
            throw new ArgumentException("Optimization requires 1-6 parameters.");

        Console.WriteLine($"Nelder-Mead: optimizing {nDim} parameters over {nMaxIterations} iterations...");

        // Initialize simplex (n+1 vertices)
        List<double[]> aoSimplex = InitializeSimplex(aoBounds, nDim);
        List<double> afValues = new();
        List<EngineSpec> aoSpecs = new();

        // Evaluate initial simplex
        for (int i = 0; i <= nDim; i++)
        {
            EngineSpec oSpec = ApplyParams(oBaseSpec, aoParams, aoSimplex[i], aoBounds);
            double fValue = Evaluate(oSpec, oObjective);
            afValues.Add(fValue);
            aoSpecs.Add(oSpec);
        }

        List<EngineSpec> aoHistory = [.. aoSpecs];
        int nIter = 0;

        while (nIter < nMaxIterations)
        {
            nIter++;

            // Sort by objective value (ascending = better)
            var aoSorted = afValues
                .Select((f, i) => (Value: f, Index: i))
                .OrderBy(x => x.Value)
                .ToList();

            int iBest = aoSorted[0].Index;
            int iWorst = aoSorted[^1].Index;
            int iSecondWorst = aoSorted[^2].Index;

            // Check convergence
            double fRange = afValues[iWorst] - afValues[iBest];
            if (fRange < fTolerance && nIter > 10)
            {
                Console.WriteLine($"  Converged at iteration {nIter} (range = {fRange:E2})");
                break;
            }

            // Compute centroid of all points except worst
            double[] afCentroid = new double[nDim];
            for (int i = 0; i <= nDim; i++)
            {
                if (i == iWorst) continue;
                for (int j = 0; j < nDim; j++)
                    afCentroid[j] += aoSimplex[i][j];
            }
            for (int j = 0; j < nDim; j++)
                afCentroid[j] /= nDim;

            // Reflection
            double[] afReflected = Reflect(aoSimplex[iWorst], afCentroid, fReflection);
            EngineSpec oSpecReflected = ApplyParams(oBaseSpec, aoParams, afReflected, aoBounds);
            double fReflected = Evaluate(oSpecReflected, oObjective);

            if (fReflected >= afValues[iBest] && fReflected < afValues[iSecondWorst])
            {
                // Accept reflection
                aoSimplex[iWorst] = afReflected;
                afValues[iWorst] = fReflected;
                aoSpecs[iWorst] = oSpecReflected;
            }
            else if (fReflected < afValues[iBest])
            {
                // Try expansion
                double[] afExpanded = Reflect(aoSimplex[iWorst], afCentroid, fExpansion);
                EngineSpec oSpecExpanded = ApplyParams(oBaseSpec, aoParams, afExpanded, aoBounds);
                double fExpanded = Evaluate(oSpecExpanded, oObjective);

                if (fExpanded < fReflected)
                {
                    aoSimplex[iWorst] = afExpanded;
                    afValues[iWorst] = fExpanded;
                    aoSpecs[iWorst] = oSpecExpanded;
                }
                else
                {
                    aoSimplex[iWorst] = afReflected;
                    afValues[iWorst] = fReflected;
                    aoSpecs[iWorst] = oSpecReflected;
                }
            }
            else
            {
                // Try contraction
                double[] afContracted = Reflect(aoSimplex[iWorst], afCentroid, fContraction);
                EngineSpec oSpecContracted = ApplyParams(oBaseSpec, aoParams, afContracted, aoBounds);
                double fContracted = Evaluate(oSpecContracted, oObjective);

                if (fContracted < afValues[iWorst])
                {
                    aoSimplex[iWorst] = afContracted;
                    afValues[iWorst] = fContracted;
                    aoSpecs[iWorst] = oSpecContracted;
                }
                else
                {
                    // Shrink
                    for (int i = 0; i <= nDim; i++)
                    {
                        if (i == iBest) continue;
                        for (int j = 0; j < nDim; j++)
                            aoSimplex[i][j] = aoSimplex[iBest][j] + fShrink * (aoSimplex[i][j] - aoSimplex[iBest][j]);
                        aoSpecs[i] = ApplyParams(oBaseSpec, aoParams, aoSimplex[i], aoBounds);
                        afValues[i] = Evaluate(aoSpecs[i], oObjective);
                    }
                }
            }

            aoHistory.AddRange(aoSpecs);

            // Recalculate indices after modifications
            aoSorted = afValues
                .Select((f, i) => (Value: f, Index: i))
                .OrderBy(x => x.Value)
                .ToList();
            iBest = aoSorted[0].Index;
            iWorst = aoSorted[^1].Index;

            if (nIter % 10 == 0)
                Console.WriteLine($"  [{nIter}] Best: {afValues[iBest]:F4}  Worst: {afValues[iWorst]:F4}");
        }

        // Return best point
        int iBestFinal = afValues.IndexOf(afValues.Min());
        return new OptimizationResult
        {
            BestSpec = aoSpecs[iBestFinal],
            BestDesign = EngineSizing.Size(aoSpecs[iBestFinal]),
            BestThermal = ThermalModel.Evaluate(
                EngineSizing.Size(aoSpecs[iBestFinal]),
                new NozzleContour(EngineSizing.Size(aoSpecs[iBestFinal]))),
            BestObjectiveValue = afValues[iBestFinal],
            Iterations = nIter,
            Converged = nIter < nMaxIterations,
            History = aoHistory
        };
    }

    static List<double[]> InitializeSimplex((double fMin, double fMax)[] aoBounds, int nDim)
    {
        List<double[]> aoSimplex = new(nDim + 1);

        // Initial point at center of bounds
        double[] afCenter = new double[nDim];
        for (int i = 0; i < nDim; i++)
            afCenter[i] = (aoBounds[i].fMin + aoBounds[i].fMax) / 2.0;
        aoSimplex.Add([.. afCenter]);

        // Create n additional points by perturbing each dimension
        for (int i = 0; i < nDim; i++)
        {
            double[] afPoint = [.. afCenter];
            double fStep = (aoBounds[i].fMax - aoBounds[i].fMin) * 0.1;
            afPoint[i] += fStep;
            aoSimplex.Add(afPoint);
        }

        return aoSimplex;
    }

    static double[] Reflect(double[] afPoint, double[] afCentroid, double fAlpha)
    {
        double[] afReflected = new double[afPoint.Length];
        for (int i = 0; i < afPoint.Length; i++)
            afReflected[i] = afCentroid[i] + fAlpha * (afCentroid[i] - afPoint[i]);
        return afReflected;
    }

    static EngineSpec ApplyParams(EngineSpec oBase, string[] aoParams, double[] afValues, (double fMin, double fMax)[] aoBounds)
    {
        EngineSpec oSpec = oBase;
        for (int i = 0; i < aoParams.Length; i++)
        {
            string strParam = aoParams[i].ToLowerInvariant();
            double fValue = Math.Max(aoBounds[i].fMin, Math.Min(aoBounds[i].fMax, afValues[i]));

            oSpec = strParam switch
            {
                "pc" => oSpec with { ChamberPressureBar = fValue },
                "of" => oSpec with { OfRatio = fValue },
                "eps" or "expansion" => oSpec with { ExpansionRatio = fValue },
                "bell" => oSpec with { BellFraction = fValue },
                "coolcount" => oSpec with { Cooling = oSpec.Cooling with { Count = (int)Math.Round(fValue) } },
                "cooldiam" => oSpec with { Cooling = oSpec.Cooling with { DiameterMM = fValue } },
                _ => oSpec
            };
        }
        return oSpec;
    }

    static double Evaluate(EngineSpec oSpec, OptimizationObjective oObjective)
    {
        try
        {
            EngineDesign oDesign = EngineSizing.Size(oSpec);
            NozzleContour oContour = new(oDesign);
            ThermalResult oTherm = ThermalModel.Evaluate(oDesign, oContour);

            // Sanity checks for physically impossible results
            if (oTherm.CoolantTempRise < 0 || double.IsNaN(oTherm.CoolantTempRise))
                return double.MaxValue;
            if (oDesign.IspSeaLevelS < 0 || double.IsNaN(oDesign.IspSeaLevelS))
                return double.MaxValue;
            if (oTherm.ThroatHeatFlux < 0 || double.IsNaN(oTherm.ThroatHeatFlux))
                return double.MaxValue;

            // Penalty function: lower is better
            double fPenalty = 0.0;

            // Isp: maximize (negative weight in penalty)
            fPenalty -= oObjective.IspWeight * oDesign.IspSeaLevelS;

            // Coolant dT: minimize
            fPenalty += oObjective.CoolantDtWeight * oTherm.CoolantTempRise;

            // Engine length: minimize
            fPenalty += oObjective.LengthWeight * (oDesign.ChamberCylinderLength + oDesign.BellLength);

            // Throat heat flux: minimize
            fPenalty += oObjective.HeatFluxWeight * (oTherm.ThroatHeatFlux / 1e6);

            // Soft constraints (penalties if exceeded)
            if (oTherm.CoolantTempRise > oObjective.TargetCoolantDtK)
                fPenalty += 1000.0 * (oTherm.CoolantTempRise - oObjective.TargetCoolantDtK);

            if (oTherm.ThroatHeatFlux / 1e6 > oObjective.TargetHeatFluxMW)
                fPenalty += 1000.0 * (oTherm.ThroatHeatFlux / 1e6 - oObjective.TargetHeatFluxMW);

            return fPenalty;
        }
        catch
        {
            // Invalid design: return large penalty
            return double.MaxValue;
        }
    }

    /// <summary>
    /// Parse optimization parameters from command line.
    /// Format: --params pc,of,eps --bounds "10:40,1.5:3.5,0:50"
    /// </summary>
    public static (string[] aoParams, (double fMin, double fMax)[] aoBounds) ParseOptimizationArgs(string[] args)
    {
        string[]? aoParams = null;
        (double fMin, double fMax)[]? aoBounds = null;

        for (int i = 0; i < args.Length; i++)
        {
            string strArg = args[i].ToLowerInvariant();
            switch (strArg)
            {
                case "--params":
                    aoParams = args[++i].Split(',');
                    break;
                case "--bounds":
                    aoBounds = args[++i].Split(',').Select(ParseBound).ToArray();
                    break;
            }
        }

        if (aoParams == null || aoBounds == null)
            throw new ArgumentException("Optimization requires --params and --bounds arguments.");

        return (aoParams, aoBounds);
    }

    static (double fMin, double fMax) ParseBound(string str)
    {
        string[] aoParts = str.Split(':');
        return (double.Parse(aoParts[0], CultureInfo.InvariantCulture),
                double.Parse(aoParts[1], CultureInfo.InvariantCulture));
    }
}

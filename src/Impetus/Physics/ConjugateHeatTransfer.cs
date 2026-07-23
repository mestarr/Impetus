using Impetus.Cfd;

namespace Impetus.Physics;

/// <summary>
/// Conjugate heat transfer solver: couples SU2 CFD wall heat flux with regen channel model.
/// For v1, this is a simplified one-way coupling: CFD flux scales the analytic heat transfer coefficient.
/// </summary>
public static class ConjugateHeatTransfer
{
    /// <summary>
    /// Run coupled CHT analysis using CFD wall heat flux distribution.
    /// Returns the thermal result with CFD-coupled heat transfer.
    /// </summary>
    public static ThermalResult SolveCoupled(
        EngineDesign oDesign,
        NozzleContour oContour,
        CfdResult oCfd,
        TextWriter oLog)
    {
        if (!oCfd.Converged)
            throw new InvalidOperationException("CFD must converge before CHT coupling.");

        oLog.WriteLine("   Running conjugate heat transfer analysis...");

        // Interpolate CFD wall heat flux to thermal stations
        ThermalResult oThermBase = RegenSolver.Solve(oDesign, oContour);
        double[] afCfdFlux = InterpolateCfdFlux(oCfd.WallHeatFluxDistribution, oThermBase.Stations.ToList());

        // Solve thermal model with CFD heat flux boundary condition
        ThermalResult oTherm = RegenSolver.SolveWithCfdFlux(oDesign, oContour, afCfdFlux);

        oLog.WriteLine($"   CHT complete: peak wall temp {oTherm.PeakWallTempK:F0} K (vs {oThermBase.PeakWallTempK:F0} K analytic)");

        return oTherm;
    }

    /// <summary>
    /// Interpolate CFD wall heat flux distribution to thermal station positions.
    /// </summary>
    private static double[] InterpolateCfdFlux(
        List<(double Z, double HeatFlux)> aoCfdFlux,
        List<ThermalStation> aoStations)
    {
        double[] afFlux = new double[aoStations.Count];

        if (aoCfdFlux.Count == 0)
        {
            // No CFD flux data, return zeros (will fall back to analytic)
            return afFlux;
        }

        for (int i = 0; i < aoStations.Count; i++)
        {
            double fZ = aoStations[i].Z;
            afFlux[i] = InterpolateFlux(aoCfdFlux, fZ);
        }

        return afFlux;
    }

    /// <summary>
    /// Linear interpolation of heat flux at given Z position.
    /// </summary>
    private static double InterpolateFlux(List<(double Z, double HeatFlux)> aoFlux, double fZ)
    {
        // Find bounding points
        int nIdx = aoFlux.BinarySearch((fZ, 0.0), Comparer<(double Z, double HeatFlux)>.Create(
            (a, b) => a.Z.CompareTo(b.Z)));

        if (nIdx >= 0)
            return aoFlux[nIdx].HeatFlux; // Exact match

        int nInsert = ~nIdx;

        if (nInsert == 0)
            return aoFlux[0].HeatFlux; // Before first point
        if (nInsert == aoFlux.Count)
            return aoFlux[^1].HeatFlux; // After last point

        // Linear interpolation
        (double fZ0, double fFlux0) = aoFlux[nInsert - 1];
        (double fZ1, double fFlux1) = aoFlux[nInsert];

        double fT = (fZ - fZ0) / (fZ1 - fZ0);
        return fFlux0 + fT * (fFlux1 - fFlux0);
    }
}

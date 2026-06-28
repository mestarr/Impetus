namespace Impetus.Physics;

public record ThermalStation(
    double Z,
    double R,
    double Mach,
    double HeatFlux,
    double WallTempGasSide = double.NaN,
    double CoolantTemp = double.NaN,
    double CoolantPressure = double.NaN);

public record ThermalResult
{
    public required IReadOnlyList<ThermalStation> Stations { get; init; }
    public required double ThroatHeatFlux { get; init; }     // [W/m2]
    public required double TotalHeatLoad { get; init; }      // [W]
    public required double CoolantTempRise { get; init; }    // [K]
    public required double CoolantVelocity { get; init; }    // [m/s] in channels
    public required double AssumedWallTemp { get; init; }    // [K] peak gas-side wall (legacy label)

    public double CoolantInletTempK { get; init; } = RegenSolver.CoolantInletTempK;
    public double CoolantOutletTempK { get; init; } = double.NaN;
    public double PeakWallTempK { get; init; } = double.NaN;
    public double ThroatWallTempK { get; init; } = double.NaN;
    public double ChannelPressureDropPa { get; init; }
}

/// <summary>
/// Hot-gas-side Bartz correlation plus a 1D regenerative-cooling march along
/// the channel path (see <see cref="RegenSolver"/>).
/// </summary>
public static class ThermalModel
{
    /// <summary>Fallback isothermal wall for CFD when regen has not been run.</summary>
    public const double AssumedRegenWallTempK = 800.0;

    public static ThermalResult Evaluate(EngineDesign oDesign, NozzleContour oContour)
        => RegenSolver.Solve(oDesign, oContour);
}

namespace Impetus.Physics;

/// <summary>
/// Combustion gas properties: equilibrium Tc, molar mass, and gamma from
/// CEA-derived tables in <c>data/gas/</c> (bilinear in O/F and Pc), plus
/// liquid-side fuel/oxidizer data for injector and regen estimates.
/// </summary>
public record CombustionGas
{
    public required string Name { get; init; }

    /// <summary>Adiabatic flame (chamber stagnation) temperature [K].</summary>
    public required double Tc { get; init; }

    /// <summary>Mean molar mass of combustion products [kg/mol].</summary>
    public required double MolarMass { get; init; }

    /// <summary>Ratio of specific heats of products (frozen through expansion for v1).</summary>
    public required double Gamma { get; init; }

    /// <summary>Whether to use equilibrium expansion (varying gamma) instead of frozen flow.</summary>
    public bool EquilibriumExpansion { get; init; } = false;

    /// <summary>Characteristic chamber length L* [m] (combustion residence sizing).</summary>
    public required double LStar { get; init; }

    /// <summary>Nominal O/F mass ratio for this propellant pair.</summary>
    public required double NominalOf { get; init; }

    /// <summary>Fuel density (liquid, storage conditions) [kg/m3].</summary>
    public required double FuelDensity { get; init; }

    /// <summary>Oxidizer density (liquid) [kg/m3].</summary>
    public required double OxDensity { get; init; }

    /// <summary>Liquid fuel specific heat for coolant temperature-rise estimate [J/(kg K)].</summary>
    public required double FuelCp { get; init; }

    /// <summary>Max allowable bulk coolant temperature rise [K].</summary>
    public required double MaxCoolantRiseK { get; init; }

    /// <summary>O/F used for this evaluation (from spec).</summary>
    public double OfRatioUsed { get; init; }

    /// <summary>Chamber pressure [bar] used for table lookup.</summary>
    public double PcBarUsed { get; init; }

    /// <summary>Provenance string from the gas table file.</summary>
    public string TableSource { get; init; } = "";

    public const double R0 = 8.314462618;
    public const double G0 = 9.80665;

    public double Rs => R0 / MolarMass;
    public double Cp => Gamma * Rs / (Gamma - 1.0);
    public double Pr => 4.0 * Gamma / (9.0 * Gamma - 5.0);

    public double Viscosity(double fT)
        => 1.184e-7 * Math.Sqrt(MolarMass * 1000.0) * Math.Pow(fT, 0.6);

    /// <summary>
    /// Compute local gamma for equilibrium expansion at given Mach number.
    /// Simplified correlation: gamma decreases as expansion proceeds (recombination).
    /// For frozen flow, returns constant Gamma.
    /// </summary>
    public double LocalGamma(double fMach)
    {
        if (!EquilibriumExpansion)
            return Gamma;

        // Simplified equilibrium gamma correlation
        // Gamma decreases from chamber value (~1.2-1.3) toward ~1.1 at high Mach
        // This is a rough approximation - full CEA integration would be more accurate
        double fGammaChamber = Gamma;
        double fGammaExit = Gamma - 0.15; // Approximate recombination effect

        // Transition based on Mach number (typical nozzle exit Mach ~3-5)
        double fMachTransition = 4.0;
        double fT = Math.Min(fMach / fMachTransition, 1.0);

        return fGammaChamber - fT * (fGammaChamber - fGammaExit);
    }

    /// <summary>Interpolate equilibrium properties at the spec O/F and Pc.</summary>
    public static CombustionGas Resolve(string strKey, double fOfRatio, double fPcBar, bool bEquilibriumExpansion = false)
        => GasTableStore.Resolve(strKey, fOfRatio, fPcBar, bEquilibriumExpansion);

    /// <summary>Nominal O/F at 20 bar — tests and legacy callers.</summary>
    public static CombustionGas ForPair(string strKey)
        => GasTableStore.ForPair(strKey);

    /// <summary>Available propellant pair keys (from data/gas/*.json).</summary>
    public static IReadOnlyCollection<string> PropellantKeys => GasTableStore.Keys;
}

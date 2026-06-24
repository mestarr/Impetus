namespace Impetus.Physics;

/// <summary>
/// Equilibrium combustion gas properties for common propellant pairs near their
/// typical mixture ratio, plus liquid-side properties needed for injector and
/// cooling estimates.
///
/// Values are representative textbook numbers (Sutton, "Rocket Propulsion
/// Elements"; Huzel &amp; Huang) at Pc ~ 2 MPa. They are good to a few percent
/// for sizing. A future milestone is replacing this table with a real
/// NASA-CEA-derived dataset that varies with O/F and Pc.
/// </summary>
public record CombustionGas
{
    public required string Name { get; init; }

    /// <summary>Adiabatic flame (chamber stagnation) temperature [K].</summary>
    public required double Tc { get; init; }

    /// <summary>Mean molar mass of combustion products [kg/mol].</summary>
    public required double MolarMass { get; init; }

    /// <summary>Ratio of specific heats of products (frozen, chamber conditions).</summary>
    public required double Gamma { get; init; }

    /// <summary>Characteristic chamber length L* [m] (combustion residence sizing).</summary>
    public required double LStar { get; init; }

    /// <summary>Nominal O/F mass ratio the table values are calibrated at.</summary>
    public required double NominalOF { get; init; }

    /// <summary>Fuel density (liquid, storage conditions) [kg/m3].</summary>
    public required double FuelDensity { get; init; }

    /// <summary>Oxidizer density (liquid) [kg/m3].</summary>
    public required double OxDensity { get; init; }

    /// <summary>Liquid fuel specific heat for coolant temperature-rise estimate [J/(kg K)].</summary>
    public required double FuelCp { get; init; }

    /// <summary>
    /// Max allowable bulk coolant temperature rise [K] when the fuel is the
    /// regen coolant. First-order engineering limits: coking onset for RP-1,
    /// boiling margin for ethanol, decomposition margin for methane; hydrogen
    /// tolerates large rises by design (cryogenic inlet).
    /// </summary>
    public required double MaxCoolantRiseK { get; init; }

    public const double R0 = 8.314462618; // universal gas constant [J/(mol K)]
    public const double G0 = 9.80665;     // standard gravity [m/s2]

    /// <summary>Specific gas constant of the combustion products [J/(kg K)].</summary>
    public double Rs => R0 / MolarMass;

    /// <summary>Specific heat at constant pressure [J/(kg K)].</summary>
    public double Cp => Gamma * Rs / (Gamma - 1.0);

    /// <summary>Prandtl number estimate for combustion gas (Bartz assumption).</summary>
    public double Pr => 4.0 * Gamma / (9.0 * Gamma - 5.0);

    /// <summary>
    /// Dynamic viscosity estimate [Pa s] at temperature T.
    /// SI form of Sutton's approximation: mu ~ 1.184e-7 * M[g/mol]^0.5 * T^0.6.
    /// </summary>
    public double Viscosity(double fT)
        => 1.184e-7 * Math.Sqrt(MolarMass * 1000.0) * Math.Pow(fT, 0.6);

    public static CombustionGas ForPair(string strKey)
    {
        if (!Table.TryGetValue(strKey, out CombustionGas? oGas))
        {
            throw new ArgumentException(
                $"Unknown propellant pair '{strKey}'. Available: {string.Join(", ", Table.Keys)}");
        }
        return oGas;
    }

    public static readonly IReadOnlyDictionary<string, CombustionGas> Table =
        new Dictionary<string, CombustionGas>(StringComparer.OrdinalIgnoreCase)
    {
        ["LOX_Kerosene"] = new()
        {
            Name = "LOX / Kerosene (RP-1), O/F ~ 2.3",
            Tc = 3570, MolarMass = 0.0219, Gamma = 1.22, LStar = 1.0, NominalOF = 2.3,
            FuelDensity = 810, OxDensity = 1141, FuelCp = 2000, MaxCoolantRiseK = 120
        },
        ["LOX_Methane"] = new()
        {
            Name = "LOX / Liquid Methane, O/F ~ 3.4",
            Tc = 3530, MolarMass = 0.0213, Gamma = 1.23, LStar = 0.9, NominalOF = 3.4,
            FuelDensity = 423, OxDensity = 1141, FuelCp = 3500, MaxCoolantRiseK = 200
        },
        ["LOX_Hydrogen"] = new()
        {
            Name = "LOX / Liquid Hydrogen, O/F ~ 5.5",
            Tc = 3300, MolarMass = 0.0120, Gamma = 1.26, LStar = 0.75, NominalOF = 5.5,
            FuelDensity = 71, OxDensity = 1141, FuelCp = 14300, MaxCoolantRiseK = 300
        },
        ["N2O_Ethanol"] = new()
        {
            Name = "Nitrous Oxide / Ethanol, O/F ~ 4.5",
            Tc = 2980, MolarMass = 0.0245, Gamma = 1.22, LStar = 1.2, NominalOF = 4.5,
            FuelDensity = 789, OxDensity = 745, FuelCp = 2400, MaxCoolantRiseK = 120
        },
    };
}

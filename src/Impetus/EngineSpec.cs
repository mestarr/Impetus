using System.Text.Json;
using System.Text.Json.Serialization;

namespace Impetus;

/// <summary>
/// Target manufacturing process for the engine geometry.
/// Different processes have different constraints on resolution, feature size, and tolerances.
/// </summary>
public enum ManufacturingProcess
{
    /// <summary>Fused Deposition Modeling (plastic) - for display models only, not hot fire.</summary>
    FDM,

    /// <summary>Laser Powder Bed Fusion (metal) - for functional regen-cooled engines.</summary>
    LPBF
}

/// <summary>
/// Injector type for the engine.
/// Different injector patterns have different stability, mixing, and manufacturability characteristics.
/// </summary>
public enum InjectorType
{
    /// <summary>Simple showerhead with orifices - simplest but least stable. Good for display models.</summary>
    Showerhead,

    /// <summary>Coaxial swirl elements - good mixing and stability, common in LOX/kerosene engines.</summary>
    CoaxialSwirl,

    /// <summary>Impinging doublet elements - excellent atomization, good for hypergolic propellants.</summary>
    ImpingingDoublet
}

public record CoolingSpec
{
    /// <summary>Number of regenerative cooling channels around the circumference.</summary>
    public int Count { get; init; } = 24;

    /// <summary>Channel bore diameter in mm.</summary>
    public double DiameterMM { get; init; } = 1.4;

    /// <summary>Total helical turns each channel makes from manifold to manifold. 0 = straight axial channels.</summary>
    public double HelixTurns { get; init; } = 1.0;
}

public record InjectorSpec
{
    /// <summary>Injector type pattern.</summary>
    public InjectorType Type { get; init; } = InjectorType.Showerhead;

    /// <summary>Number of injector elements (orifices or element groups).</summary>
    public int ElementCount { get; init; } = 12;

    /// <summary>Central orifice diameter in mm (for showerhead).</summary>
    public double OrificeDiameterMM { get; init; } = 1.0;

    /// <summary>Outer injector diameter in mm (for coaxial swirl).</summary>
    public double OuterDiameterMM { get; init; } = 2.0;

    /// <summary>Inner injector diameter in mm (for coaxial swirl).</summary>
    public double InnerDiameterMM { get; init; } = 1.2;

    /// <summary>Whether to include film cooling orifice ring.</summary>
    public bool FilmCooling { get; init; } = false;

    /// <summary>Film cooling orifice diameter in mm.</summary>
    public double FilmOrificeDiameterMM { get; init; } = 0.8;

    /// <summary>Number of film cooling orifices.</summary>
    public int FilmOrificeCount { get; init; } = 24;

    /// <summary>Fraction of total fuel flow diverted to film cooling (0.0 = none, 0.1 = 10%).</summary>
    public double FilmCoolingFraction { get; init; } = 0.0;
}

/// <summary>
/// The input specification: everything the engineer decides up front.
/// All downstream numbers (dimensions, performance, geometry) are derived from this.
/// </summary>
public record EngineSpec
{
    public string Name { get; init; } = "unnamed";

    /// <summary>Target thrust at the specified ambient pressure [N].</summary>
    public double ThrustN { get; init; } = 1000;

    public double ChamberPressureBar { get; init; } = 20;

    /// <summary>Ambient pressure the engine is optimized for [bar]. 1.01325 = sea level.</summary>
    public double AmbientPressureBar { get; init; } = 1.01325;

    /// <summary>Propellant pair key, see GasModel.Table for options.</summary>
    public string Propellants { get; init; } = "LOX_Kerosene";

    /// <summary>Oxidizer-to-fuel mass ratio.</summary>
    public double OfRatio { get; init; } = 2.3;

    /// <summary>Nozzle area expansion ratio Ae/At. 0 = compute optimal (exit pressure = ambient).</summary>
    public double ExpansionRatio { get; init; } = 0;

    /// <summary>Bell length as fraction of an equivalent 15° conical nozzle. 0.8 is the classic Rao bell.</summary>
    public double BellFraction { get; init; } = 0.8;

    /// <summary>Chamber-to-throat area contraction ratio Ac/At.</summary>
    public double ContractionRatio { get; init; } = 4.0;

    public double WallThicknessMM { get; init; } = 3.0;

    public CoolingSpec Cooling { get; init; } = new();

    public InjectorSpec Injector { get; init; } = new();

    /// <summary>Target manufacturing process (FDM for display, LPBF for metal hot-fire).</summary>
    public ManufacturingProcess TargetProcess { get; init; } = ManufacturingProcess.LPBF;

    /// <summary>Voxel resolution for geometry generation [mm]. Smaller = finer + slower.</summary>
    /// Auto-set based on TargetProcess if not explicitly specified.</summary>
    public double VoxelSizeMM { get; init; } = 0.4;

    /// <summary>Printer bed size [mm] for bed-fit checks in the print report. 0 = 250 mm default.</summary>
    public double PrinterBedMM { get; init; } = 250;

    private static readonly JsonSerializerOptions s_oJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions s_oJsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static EngineSpec Load(string strPath)
    {
        string strJson = File.ReadAllText(strPath);
        EngineSpec? oSpec = JsonSerializer.Deserialize<EngineSpec>(strJson, s_oJsonOptions);
        return oSpec ?? throw new InvalidDataException($"Could not parse engine spec '{strPath}'");
    }

    public void Save(string strPath)
        => File.WriteAllText(strPath, JsonSerializer.Serialize(this, s_oJsonWriteOptions));

    // Derived SI conveniences
    [JsonIgnore] public double Pc => ChamberPressureBar * 1e5;
    [JsonIgnore] public double Pa => AmbientPressureBar * 1e5;
}

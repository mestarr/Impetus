using System.Text.Json;
using System.Text.Json.Serialization;

namespace Impetus;

public record CoolingSpec
{
    /// <summary>Number of regenerative cooling channels around the circumference.</summary>
    public int Count { get; init; } = 24;

    /// <summary>Channel bore diameter in mm.</summary>
    public double DiameterMM { get; init; } = 1.4;

    /// <summary>Total helical turns each channel makes from manifold to manifold. 0 = straight axial channels.</summary>
    public double HelixTurns { get; init; } = 1.0;
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

    /// <summary>Voxel resolution for geometry generation [mm]. Smaller = finer + slower.</summary>
    public double VoxelSizeMM { get; init; } = 0.4;

    /// <summary>Printer bed size [mm] for bed-fit checks in the print report. 0 = 250 mm default.</summary>
    public double PrinterBedMM { get; init; } = 250;

    private static readonly JsonSerializerOptions s_oJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions s_oJsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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

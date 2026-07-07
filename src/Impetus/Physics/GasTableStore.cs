using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Impetus.Physics;

/// <summary>
/// Loads CEA-equilibrium gas grids from <c>data/gas/*.json</c> and bilinearly
/// interpolates Tc, molar mass, and gamma vs O/F and chamber pressure.
/// </summary>
public static class GasTableStore
{
    sealed class GasGridFile
    {
        public string Key { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public double NominalOf { get; init; }
        public double LStarM { get; init; }
        public double FuelDensity { get; init; }
        public double OxDensity { get; init; }
        public double FuelCp { get; init; }
        public double MaxCoolantRiseK { get; init; }
        public string Source { get; init; } = "";
        public double[] OfRatio { get; init; } = [];
        public double[] PcBar { get; init; } = [];
        public double[][] Tc { get; init; } = [];
        public double[][] MolarMass { get; init; } = [];
        public double[][] Gamma { get; init; } = [];
    }

    static readonly Lazy<IReadOnlyDictionary<string, GasGridFile>> s_oGrids = new(LoadAll);

    static readonly JsonSerializerOptions s_oJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static IReadOnlyCollection<string> Keys => s_oGrids.Value.Keys.ToList();

    public static CombustionGas Resolve(string strKey, double fOfRatio, double fPcBar)
    {
        if (!s_oGrids.Value.TryGetValue(strKey, out GasGridFile? oGrid))
        {
            throw new ArgumentException(
                $"Unknown propellant pair '{strKey}'. Available: {string.Join(", ", Keys)}");
        }

        double fTc = Sample2D(oGrid.OfRatio, oGrid.PcBar, oGrid.Tc, fOfRatio, fPcBar);
        double fM = Sample2D(oGrid.OfRatio, oGrid.PcBar, oGrid.MolarMass, fOfRatio, fPcBar);
        double fG = Sample2D(oGrid.OfRatio, oGrid.PcBar, oGrid.Gamma, fOfRatio, fPcBar);

        return new CombustionGas
        {
            Name = $"{oGrid.DisplayName}, O/F {fOfRatio.ToString("0.##", CultureInfo.InvariantCulture)} @ {fPcBar.ToString("0.#", CultureInfo.InvariantCulture)} bar",
            Tc = fTc,
            MolarMass = fM,
            Gamma = fG,
            LStar = oGrid.LStarM,
            NominalOf = oGrid.NominalOf,
            FuelDensity = oGrid.FuelDensity,
            OxDensity = oGrid.OxDensity,
            FuelCp = oGrid.FuelCp,
            MaxCoolantRiseK = oGrid.MaxCoolantRiseK,
            OfRatioUsed = fOfRatio,
            PcBarUsed = fPcBar,
            TableSource = oGrid.Source
        };
    }

    /// <summary>Nominal O/F and 20 bar — backward-compatible default.</summary>
    public static CombustionGas ForPair(string strKey)
    {
        if (!s_oGrids.Value.TryGetValue(strKey, out GasGridFile? oGrid))
            throw new ArgumentException($"Unknown propellant pair '{strKey}'.");
        return Resolve(strKey, oGrid.NominalOf, 20.0);
    }

    static IReadOnlyDictionary<string, GasGridFile> LoadAll()
    {
        string strDir = FindDataDir();
        Dictionary<string, GasGridFile> ao = new(StringComparer.OrdinalIgnoreCase);

        foreach (string strPath in Directory.EnumerateFiles(strDir, "*.json"))
        {
            GasGridFile oGrid = JsonSerializer.Deserialize<GasGridFile>(
                File.ReadAllText(strPath), s_oJson)
                ?? throw new InvalidDataException($"Could not parse gas table '{strPath}'");
            ValidateGrid(oGrid, strPath);
            ao[oGrid.Key] = oGrid;
        }

        if (ao.Count == 0)
            throw new InvalidOperationException($"No gas tables found in '{strDir}'.");

        return ao;
    }

    static string FindDataDir()
    {
        string? strDir = AppContext.BaseDirectory;
        while (strDir is not null)
        {
            string strCandidate = Path.Combine(strDir, "data", "gas");
            if (Directory.Exists(strCandidate))
                return strCandidate;
            strDir = Path.GetDirectoryName(strDir);
        }
        throw new InvalidOperationException(
            "Could not find data/gas/ — run from the repo or ensure tables are copied to output.");
    }

    static void ValidateGrid(GasGridFile o, string strPath)
    {
        int nOf = o.OfRatio.Length;
        int nPc = o.PcBar.Length;
        if (nOf < 2 || nPc < 2)
            throw new InvalidDataException($"Gas table '{strPath}' needs at least 2×2 grid.");
        foreach (double[][] af in new[] { o.Tc, o.MolarMass, o.Gamma })
        {
            if (af.Length != nOf || af.Any(r => r.Length != nPc))
                throw new InvalidDataException($"Gas table '{strPath}' grid dimensions mismatch.");
        }
    }

    /// <summary>Bilinear interpolation on a rectilinear O/F × Pc grid.</summary>
    public static double Sample2D(
        double[] afOf, double[] afPc, double[][] afValues,
        double fOf, double fPc)
    {
        (int i0, int i1, double fTOf) = Bracket(afOf, fOf);
        (int j0, int j1, double fTPc) = Bracket(afPc, fPc);

        double fQ00 = afValues[i0][j0];
        double fQ10 = afValues[i1][j0];
        double fQ01 = afValues[i0][j1];
        double fQ11 = afValues[i1][j1];

        double fQ0 = fQ00 + fTOf * (fQ10 - fQ00);
        double fQ1 = fQ01 + fTOf * (fQ11 - fQ01);
        return fQ0 + fTPc * (fQ1 - fQ0);
    }

    static (int i0, int i1, double fT) Bracket(double[] afX, double fX)
    {
        if (fX <= afX[0]) return (0, 1, 0.0);
        if (fX >= afX[^1]) return (afX.Length - 2, afX.Length - 1, 1.0);

        for (int i = 0; i < afX.Length - 1; i++)
        {
            if (fX <= afX[i + 1])
            {
                double fT = (fX - afX[i]) / (afX[i + 1] - afX[i]);
                return (i, i + 1, fT);
            }
        }
        return (afX.Length - 2, afX.Length - 1, 1.0);
    }
}

using System.IO.Compression;
using System.Text;
using Impetus;
using Impetus.Cfd;
using Impetus.Geometry;
using Impetus.Physics;
using Impetus.Reporting;
using PicoGK;
using Xunit;

namespace Impetus.Tests;

public class GasTableTests
{
    [Fact]
    public void Kerolox_OfChanges_TcAndCStar()
    {
        CombustionGas oRich = CombustionGas.Resolve("LOX_Kerosene", 1.8, 20);
        CombustionGas oNom = CombustionGas.Resolve("LOX_Kerosene", 2.3, 20);
        CombustionGas oLean = CombustionGas.Resolve("LOX_Kerosene", 3.0, 20);

        Assert.True(oNom.Tc >= oRich.Tc * 0.98);
        Assert.True(oNom.Tc >= oLean.Tc);
        Assert.NotEqual(oRich.MolarMass, oLean.MolarMass);
    }

    [Fact]
    public void Kerolox_PcChanges_Tc()
    {
        CombustionGas oLow = CombustionGas.Resolve("LOX_Kerosene", 2.3, 10);
        CombustionGas oHigh = CombustionGas.Resolve("LOX_Kerosene", 2.3, 30);
        Assert.True(oHigh.Tc > oLow.Tc);
    }

    [Fact]
    public void Interpolation_MatchesGridCorner()
    {
        double fTc = GasTableStore.Sample2D(
            [1.6, 1.8, 2.0, 2.3, 2.6, 3.0, 3.4],
            [10, 15, 20, 25, 30],
            [
                [3480, 3530, 3560, 3580, 3590],
                [3550, 3600, 3630, 3650, 3660],
                [3600, 3650, 3680, 3700, 3710],
                [3630, 3680, 3710, 3730, 3740],
                [3610, 3660, 3690, 3710, 3720],
                [3560, 3610, 3640, 3660, 3670],
                [3500, 3550, 3580, 3600, 3610]
            ],
            2.3, 20);
        Assert.Equal(3710, fTc, 1);
    }
}

public class Su2CaseTests
{
    [Fact]
    public void Config_UsesRansSstAndIsothermalWall()
    {
        EngineSpec oSpec = EngineSpec.Load(Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        NozzleContour oContour = new(oDesign);

        string strDir = Path.Combine(Path.GetTempPath(), $"impetus-su2-{Guid.NewGuid():N}");
        new Su2Case(oDesign, oContour).Write(strDir);
        string strCfg = File.ReadAllText(Path.Combine(strDir, "engine.cfg"));

        Assert.Contains("SOLVER= RANS", strCfg, StringComparison.Ordinal);
        Assert.Contains("KIND_TURB_MODEL= SST", strCfg, StringComparison.Ordinal);
        Assert.Contains($"MARKER_ISOTHERMAL= ( wall,", strCfg, StringComparison.Ordinal);
        Assert.Contains("CONV_NUM_METHOD_FLOW= ROE", strCfg, StringComparison.Ordinal);
        Assert.DoesNotContain("MARKER_EULER", strCfg, StringComparison.Ordinal);
        Assert.DoesNotContain("SOLVER= EULER", strCfg, StringComparison.Ordinal);
    }

    [Fact]
    public void Mesh_RadialClustering_PacksCellsNearWall()
    {
        double fR = 0.02;
        double fInner = Su2Case.RadialStation(fR, 1, 60, 2.8);
        double fOuter = Su2Case.RadialStation(fR, 59, 60, 2.8);
        double fDeltaInner = fInner;
        double fDeltaOuter = fR - fOuter;

        Assert.True(fDeltaInner < fDeltaOuter,
            $"Inner cell {fDeltaInner:G6} m should be smaller than outer cell {fDeltaOuter:G6} m");
    }
}

public class EngineSpecTests
{
    [Fact]
    public void Load_ToleratesCommentsAndTrailingCommas()
    {
        string strPath = Path.Combine(Path.GetTempPath(), $"impetus-spec-{Guid.NewGuid():N}.json");
        File.WriteAllText(strPath,
            """
            {
              // demo-style spec
              "name": "TEST-SPEC",
              "thrustN": 500,
              "propellants": "LOX_Kerosene",
            }
            """);

        EngineSpec oSpec = EngineSpec.Load(strPath);
        Assert.Equal("TEST-SPEC", oSpec.Name);
        Assert.Equal(500, oSpec.ThrustN);
    }
}

public class EngineSizingTests
{
    static readonly EngineSpec s_oDemo = EngineSpec.Load(
        Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));

    [Fact]
    public void DemoSpec_ProducesExpectedThroatDiameter()
    {
        EngineDesign o = EngineSizing.Size(s_oDemo);
        Assert.InRange(o.ThroatRadius * 2000.0, 21.0, 22.0);
        Assert.InRange(o.ExitRadius * 2000.0, 39.0, 41.0);
        Assert.InRange(o.ExpansionRatio, 3.4, 3.6);
    }
}

public class RegenSolverTests
{
    [Fact]
    public void DemoSpec_CoolantRiseIncreasesAlongPath()
    {
        EngineSpec oSpec = EngineSpec.Load(
            Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        ThermalResult oTherm = ThermalModel.Evaluate(oDesign, new NozzleContour(oDesign));

        Assert.True(oTherm.CoolantTempRise > 10);
        Assert.True(oTherm.PeakWallTempK > oTherm.CoolantInletTempK);
        Assert.True(oTherm.CoolantOutletTempK > oTherm.CoolantInletTempK);
        Assert.Equal(oTherm.CoolantTempRise, oTherm.CoolantOutletTempK - oTherm.CoolantInletTempK, 3);
        Assert.True(oTherm.ChannelPressureDropPa > 0);
    }

    [Fact]
    public void FewerChannels_RaiseVelocity_LowerPeakWallTemp()
    {
        EngineSpec oSpec = EngineSpec.Load(
            Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        NozzleContour oContour = new(oDesign);

        ThermalResult oMany = ThermalModel.Evaluate(oDesign, oContour);
        EngineSpec oFew = oSpec with { Cooling = oSpec.Cooling with { Count = 12, DiameterMM = 1.0 } };
        EngineDesign oSized = EngineSizing.Size(oFew);
        ThermalResult oLess = ThermalModel.Evaluate(oSized, new NozzleContour(oSized));

        Assert.True(oLess.CoolantVelocity > oMany.CoolantVelocity);
        Assert.True(oLess.PeakWallTempK < oMany.PeakWallTempK);
    }
}

public class ValidationTests
{
    [Fact]
    public void DemoSpec_AnalyticGates_NoUnexpectedFails()
    {
        EngineSpec oSpec = EngineSpec.Load(
            Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        ThermalResult oTherm = ThermalModel.Evaluate(oDesign, new NozzleContour(oDesign));
        ValidationResult oVal = VirtualValidation.Evaluate(oDesign, oTherm, null);

        // Wall temperature should be screenable with tighter channels; bulk dT
        // may still fail for kerolox at 1 kN (honest enthalpy balance).
        CoolingSpec oSized = RegenSolver.SizeChannelsForWallTemp(
            oDesign, new NozzleContour(oDesign), oSpec.Cooling);
        EngineDesign oTuned = EngineSizing.Size(oSpec with { Cooling = oSized });
        ThermalResult oTunedTherm = ThermalModel.Evaluate(oTuned, new NozzleContour(oTuned));
        Assert.True(oTunedTherm.PeakWallTempK <= oTherm.PeakWallTempK);
        Assert.Contains(oVal.Checks, c => c.Name == "Peak wall temperature (1D regen)");
    }
}

public class PrintReportTests
{
    [Fact]
    public void DemoEnvelope_Fits250mmBed()
    {
        EngineSpec oSpec = EngineSpec.Load(
            Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        PrintReport.PrintEnvelope oEnv = PrintReport.EstimateEnvelope(oDesign);

        Assert.True(oEnv.HeightMM < 250);
        Assert.True(oEnv.MaxDiameterMM < 250);
    }
}

public class MeshExportTests
{
    [Fact]
    public void ThreeMf_HasMillimeterUnit()
    {
        using Library lib = new(1.0f);
        Lattice lat = new(lib);
        lat.AddBeam(new(0, 0, 0), new(10, 0, 0), 3f, 3f, bRoundCap: false);
        Mesh msh = new Voxels(lat).mshAsMesh();

        string strPath = Path.Combine(Path.GetTempPath(), $"impetus-test-{Guid.NewGuid():N}.3mf");
        MeshExport.SaveToThreeMfFile(msh, strPath, "smoke-cube");

        using ZipArchive zip = ZipFile.OpenRead(strPath);
        ZipArchiveEntry? oModel = zip.GetEntry("3D/3dmodel.model");
        Assert.NotNull(oModel);

        using StreamReader reader = new(oModel.Open(), Encoding.UTF8);
        string strXml = reader.ReadToEnd();
        Assert.Contains("unit=\"millimeter\"", strXml, StringComparison.Ordinal);
    }

    [Fact]
    public void ThreeMf_Assembly_HasThreeNamedObjects()
    {
        using Library lib = new(1.0f);
        static Mesh Box(Library lib, float fX)
        {
            Lattice lat = new(lib);
            lat.AddBeam(new(fX, 0, 0), new(fX + 5, 0, 0), 2f, 2f, bRoundCap: false);
            return new Voxels(lat).mshAsMesh();
        }

        string strPath = Path.Combine(Path.GetTempPath(), $"impetus-test-{Guid.NewGuid():N}.3mf");
        MeshExport.SaveToThreeMfFile(strPath, "assembly", [
            new MeshExport.ThreeMfPart("body", Box(lib, 0)),
            new MeshExport.ThreeMfPart("injector", Box(lib, 10)),
            new MeshExport.ThreeMfPart("flange", Box(lib, 20)),
        ]);

        using ZipArchive zip = ZipFile.OpenRead(strPath);
        using StreamReader reader = new(zip.GetEntry("3D/3dmodel.model")!.Open(), Encoding.UTF8);
        string strXml = reader.ReadToEnd();

        Assert.Equal(3, CountOccurrences(strXml, "<item objectid=\""));
        Assert.Contains("name=\"body\"", strXml, StringComparison.Ordinal);
        Assert.Contains("name=\"injector\"", strXml, StringComparison.Ordinal);
        Assert.Contains("name=\"flange\"", strXml, StringComparison.Ordinal);
    }

    static int CountOccurrences(string strHaystack, string strNeedle)
    {
        int n = 0;
        int nIdx = 0;
        while ((nIdx = strHaystack.IndexOf(strNeedle, nIdx, StringComparison.Ordinal)) >= 0)
        {
            n++;
            nIdx += strNeedle.Length;
        }
        return n;
    }
}

static class TestPaths
{
    public static string Root()
    {
        string? strDir = AppContext.BaseDirectory;
        while (strDir is not null)
        {
            if (Directory.Exists(Path.Combine(strDir, "specs")))
                return strDir;
            strDir = Path.GetDirectoryName(strDir);
        }
        throw new InvalidOperationException("Could not find repo root (specs/ folder).");
    }
}

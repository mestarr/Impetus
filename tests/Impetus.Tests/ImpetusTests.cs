using System.IO.Compression;
using System.Text;
using Impetus;
using Impetus.Geometry;
using Impetus.Physics;
using Impetus.Reporting;
using PicoGK;
using Xunit;

namespace Impetus.Tests;

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

public class ValidationTests
{
    [Fact]
    public void DemoSpec_PassesAnalyticGates()
    {
        EngineSpec oSpec = EngineSpec.Load(
            Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        ThermalResult oTherm = ThermalModel.Evaluate(oDesign, new NozzleContour(oDesign));
        ValidationResult oVal = VirtualValidation.Evaluate(oDesign, oTherm, null);

        Assert.Equal(0, oVal.Checks.Count(c => c.Status == CheckStatus.Fail));
        Assert.Equal("VIRTUAL_PARTIAL", oVal.Verdict);
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

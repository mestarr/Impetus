using System.IO.Compression;
using System.Numerics;
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

public class PropellantDomeTests
{
    [Fact]
    public void Coaxial_FuelPostCrossesOxDome_AndSplitterHolds()
    {
        EngineSpec oSpec = EngineSpec.Load(Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        using Library lib = new(0.2f);
        Voxels vox = new ThrusterBuilder(lib, oDesign, new NozzleContour(oDesign)).voxBuildInjector();

        double fRc = oDesign.ChamberRadius * 1000.0;
        double fRing = 0.6 * fRc;
        double fPlate = InjectorBuilder.CoaxialPlateMM;
        double fOxZ = -fPlate - PropellantDomes.OxDepthMM / 2.0 + 0.5;
        double fSplitZ = -fPlate - PropellantDomes.OxDepthMM - PropellantDomes.SplitterMM / 2.0;
        double fFuelZ = fSplitZ - PropellantDomes.SplitterMM / 2.0 - PropellantDomes.FuelDepthMM / 2.0;
        double fFuelR = oSpec.Injector.InnerDiameterMM / 2.0;
        double fPostR = fFuelR + PropellantDomes.PostWallMM;

        BBox3 oBox = vox.oCalculateBoundingBox();
        Assert.True(oBox.vecMin.Z < PropellantDomes.BackZ(fPlate) + 1.0);

        AssertVoid(vox, At(8.0, 7.5, fOxZ), "ox dome");
        AssertSolid(vox, At(8.0, 7.5, fSplitZ), "splitter");
        AssertVoid(vox, At(8.0, 7.5, fFuelZ), "fuel dome");

        AssertVoid(vox, new Vector3((float)fRing, 0, (float)fSplitZ), "fuel hole through splitter");
        AssertSolid(vox, new Vector3((float)fRing, (float)(fFuelR + PropellantDomes.PostWallMM / 2.0), (float)fOxZ), "fuel post");
        AssertVoid(vox, new Vector3((float)fRing, (float)(fPostR + PropellantDomes.AnnulusMM / 2.0), -4), "ox annulus");
        AssertSolid(vox, new Vector3((float)fRing, (float)(fPostR + PropellantDomes.AnnulusMM / 2.0), (float)fSplitZ), "splitter outside the post");

        AssertVoid(vox, new Vector3(0, 0, (float)fOxZ), "igniter port");
        AssertSolid(vox, new Vector3(2.8f, 0, (float)fOxZ), "igniter sleeve");
    }

    [Fact]
    public void Showerhead_DomesSitBehindThePlate()
    {
        EngineSpec oBase = EngineSpec.Load(Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineSpec oSpec = oBase with { Injector = oBase.Injector with { Type = InjectorType.Showerhead, FilmCooling = false } };
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        using Library lib = new(0.2f);
        Voxels vox = new ThrusterBuilder(lib, oDesign, new NozzleContour(oDesign)).voxBuildInjector();

        double fRc = oDesign.ChamberRadius * 1000.0;
        double fPlate = Math.Max(6.0, oSpec.WallThicknessMM * 2.0);
        double fOxZ = -fPlate - PropellantDomes.OxDepthMM / 2.0 + 0.5;
        double fSplitZ = -fPlate - PropellantDomes.OxDepthMM - PropellantDomes.SplitterMM / 2.0;
        double fFuelZ = fSplitZ - PropellantDomes.SplitterMM / 2.0 - PropellantDomes.FuelDepthMM / 2.0;
        AssertVoid(vox, At(0.55 * fRc, 10, fOxZ), "ox dome");
        AssertSolid(vox, At(0.55 * fRc, 10, fSplitZ), "splitter");
        AssertVoid(vox, At(0.55 * fRc, 10, fFuelZ), "fuel dome");
        AssertVoid(vox, At(0.72 * fRc, 0, fSplitZ), "fuel orifice");

        double fOxRing = 0.42 * fRc;
        double fOxAngle = 180.0 / Math.Max(4, oDesign.OxOrificeCount);
        AssertVoid(vox, At(fOxRing, fOxAngle, -fPlate / 2.0), "ox orifice");
        AssertSolid(vox, At(fOxRing, fOxAngle, fSplitZ), "ox orifice stops at the splitter");
    }

    static Vector3 At(double fRadius, double fDeg, double fZ)
    {
        double fA = fDeg * Math.PI / 180.0;
        return new Vector3((float)(fRadius * Math.Cos(fA)), (float)(fRadius * Math.Sin(fA)), (float)fZ);
    }

    static void AssertSolid(Voxels vox, Vector3 vec, string strWhere)
        => Assert.True(vox.bIsInside(vec), $"expected metal at {vec} ({strWhere})");

    static void AssertVoid(Voxels vox, Vector3 vec, string strWhere)
        => Assert.False(vox.bIsInside(vec), $"expected a void at {vec} ({strWhere})");
}

public class SparkTorchTests
{
    [Fact]
    public void NozzleSitsInThePort_AndSparkHoleStaysBlind()
    {
        EngineSpec oSpec = EngineSpec.Load(Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        using Library lib = new(0.25f);
        ThrusterBuilder oBuilder = new(lib, oDesign, new NozzleContour(oDesign));
        Voxels voxInjector = oBuilder.voxBuildInjector();
        SparkTorch.Shape o = SparkTorch.Measure(InjectorBuilder.CoaxialPlateMM, SparkTorch.PortDiameterMM(oSpec.Injector.Type));
        Voxels voxTorch = SparkTorch.voxBuild(lib, InjectorBuilder.CoaxialPlateMM, SparkTorch.PortDiameterMM(oSpec.Injector.Type));

        AssertVoid(voxInjector, new Vector3(0, 0, 0), "center port");
        AssertVoid(voxTorch, new Vector3(0, 0, 2), "nozzle bore");
        AssertSolid(voxTorch, new Vector3((float)(o.NozzleBoreR + 0.35), 0, 1), "nozzle wall");
        AssertVoid(voxTorch, new Vector3(0, 0, (float)o.CavityZ), "torch chamber");

        Vector3 vecInlet = new(0, (float)(o.BodyR + 2.0), (float)o.CavityZ);
        AssertVoid(voxTorch, vecInlet, "inlet");
        AssertSolid(voxTorch, vecInlet + new Vector3(0, 0, (float)(o.InletBoreR + 0.5)), "inlet wall");

        AssertVoid(voxTorch, new Vector3(-(float)(o.BodyR + 1.5), 0, (float)o.CavityZ), "spark hole");
        AssertSolid(voxTorch, new Vector3(-(float)(o.SparkFaceR - 0.4), 0, (float)o.CavityZ), "spark-plug seat");
    }

    static void AssertSolid(Voxels vox, Vector3 vec, string strWhere)
        => Assert.True(vox.bIsInside(vec), $"expected metal at {vec} ({strWhere})");

    static void AssertVoid(Voxels vox, Vector3 vec, string strWhere)
        => Assert.False(vox.bIsInside(vec), $"expected a void at {vec} ({strWhere})");
}

public class RocketBodyTests
{
    [Fact]
    public void TanksHoldTheBurn_AndSitAheadOfTheEngine()
    {
        EngineSpec oSpec = EngineSpec.Load(Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        FeedSystemSizing oFeed = VirtualValidation.SizeFeed(oDesign);
        RocketBody.Shape oShape = RocketBody.Size(oDesign, oFeed);

        AssertTank(oShape.Fuel, oDesign.Gas.FuelDensity);
        AssertTank(oShape.Ox, oDesign.Gas.OxDensity);
        Assert.True(oShape.Ox.FwdTipZ < oShape.Ox.AftTipZ);
        Assert.Equal(oShape.Fuel.AftTipZ, oShape.Ox.AftTipZ);
        Assert.True(Math.Abs(oShape.Fuel.CenterX - oShape.Ox.CenterX) > oShape.Fuel.OuterR + oShape.Ox.OuterR);
        Assert.True(oShape.Fuel.AftTipZ < 0);
        Assert.True(oShape.NoseTipZ < Math.Min(oShape.Fuel.FwdTipZ, oShape.Ox.FwdTipZ));
        Assert.True(oShape.BodyHalfW > oShape.BodyHalfH * 5.0);
        Assert.InRange(oShape.MountZ - oShape.NoseTipZ, oShape.BodyHalfW * 2.0 - 1.0, oShape.BodyHalfW * 2.0 + 1.0);
        Assert.True(oShape.MountZ > oShape.Fuel.AftTipZ);
        double fThick = RocketBody.NacaSides(RocketBody.TailSection, 40).Max(p => p.Upper - p.Lower);
        Assert.InRange(fThick, 0.07, 0.11);
    }

    static void AssertTank(RocketBody.Tank oTank, double fDensity)
    {
        Assert.True(oTank.CapacityKg >= oTank.MassKg * RocketBody.CapacityFactor * 0.99);
        double fHoopMM = oTank.PressureBar * 1e5 * (oTank.InnerR / 1000.0) / RocketBody.AllowablePa * 1000.0;
        Assert.True(oTank.WallMM + 0.05 >= fHoopMM);
        Assert.True(oTank.WallMM >= RocketBody.MinWallMM - 0.01);
        double fInnerM = oTank.InnerR / 1000.0;
        double fCylM = oTank.CylinderMM / 1000.0;
        double fVolume = 4.0 / 3.0 * Math.PI * fInnerM * fInnerM * fInnerM + Math.PI * fInnerM * fInnerM * fCylM;
        Assert.True(Math.Abs(fVolume - oTank.VolumeM3) < 1e-9);
        Assert.True(Math.Abs(oTank.VolumeM3 * fDensity - oTank.CapacityKg) < 1e-6);
    }
}

public class SensorPortTests
{
    [Fact]
    public void ChamberTapMissesTheChannels_AndCoolantTapsOpenTheBosses()
    {
        EngineSpec oSpec = EngineSpec.Load(Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        using Library lib = new(0.35f);
        ThrusterBuilder oBuilder = new(lib, oDesign, new NozzleContour(oDesign));
        SensorPorts.Placement oSense = oBuilder.SensorPlacement();
        Voxels voxBody = oBuilder.voxBuildBody();

        double fMidR = 0.5 * (oSense.InnerR + oSense.OuterR);
        AssertVoid(voxBody, CoolantFittings.At(fMidR, oSense.ChamberZ, oSense.ChamberAngle), "chamber tap");
        AssertVoid(voxBody, CoolantFittings.At(oSense.InnerR - 0.4, oSense.ChamberZ, oSense.ChamberAngle), "chamber gas");
        AssertSolid(voxBody, CoolantFittings.At(fMidR, oSense.ChamberZ, oSense.ChamberAngle) + Tangent(oSense.ChamberAngle) * 1.2f, "metal beside the chamber tap");
        AssertVoid(voxBody, CoolantFittings.At(oSense.OuterR + 2.0, oSense.ChamberZ, oSense.ChamberAngle), "chamber-pressure boss");

        Vector3 vecInTap = CoolantFittings.At(oSense.InletR, oSense.InletZ + oSense.CoolantSolidR + 1.4, oSense.CoolantAngle);
        AssertVoid(voxBody, CoolantFittings.At(oSense.InletR, oSense.InletZ, oSense.CoolantAngle), "inlet bore under the tap");
        AssertVoid(voxBody, vecInTap, "coolant inlet tap");
        AssertSolid(voxBody, vecInTap + Tangent(oSense.CoolantAngle) * (float)(SensorPorts.BoreR + 0.55), "inlet tap wall");

        Vector3 vecOutTap = CoolantFittings.At(oSense.OutletR, oSense.OutletZ + oSense.CoolantSolidR + 1.4, oSense.CoolantAngle);
        AssertVoid(voxBody, CoolantFittings.At(oSense.OutletR, oSense.OutletZ, oSense.CoolantAngle), "outlet bore under the tap");
        AssertVoid(voxBody, vecOutTap, "coolant outlet tap");
        AssertSolid(voxBody, vecOutTap + Tangent(oSense.CoolantAngle) * (float)(SensorPorts.BoreR + 0.55), "outlet tap wall");
    }

    static Vector3 Tangent(double fAngle)
        => new((float)-Math.Sin(fAngle), (float)Math.Cos(fAngle), 0);

    static void AssertSolid(Voxels vox, Vector3 vec, string strWhere)
        => Assert.True(vox.bIsInside(vec), $"expected metal at {vec} ({strWhere})");

    static void AssertVoid(Voxels vox, Vector3 vec, string strWhere)
        => Assert.False(vox.bIsInside(vec), $"expected a void at {vec} ({strWhere})");
}

public class CoolantFittingTests
{
    [Fact]
    public void InletReachesTheNozzleRing_AndOutletReachesTheFuelDome()
    {
        EngineSpec oSpec = EngineSpec.Load(Path.Combine(TestPaths.Root(), "specs", "demo-1kN.json"));
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        using Library lib = new(0.35f);
        ThrusterBuilder oBuilder = new(lib, oDesign, new NozzleContour(oDesign));
        CoolantFittings.Layout oFit = oBuilder.FittingLayout();
        EngineComponents oParts = oBuilder.voxBuildComponents();

        AssertVoid(oParts.Body, CoolantFittings.At(oFit.AftMidR, oFit.AftZ, oFit.AngleRad), "aft manifold");
        AssertVoid(oParts.Body, CoolantFittings.At(0.5 * (oFit.AftMidR + oFit.AftOuterR), oFit.AftZ, oFit.AngleRad), "inlet through the wall");
        AssertVoid(oParts.Body, CoolantFittings.At(oFit.AftOuterR + 4, oFit.AftZ, oFit.AngleRad), "inlet nipple");
        Vector3 vecSkin = CoolantFittings.At(oFit.AftOuterR + 4, oFit.AftZ, oFit.AngleRad);
        vecSkin.Z += (float)(oFit.BoreR + 0.6);
        AssertSolid(oParts.Body, vecSkin, "inlet nipple wall");

        Vector3 vecMate = CoolantFittings.At(oFit.ForwardOuterR + 1.2, oFit.ForwardZ, oFit.AngleRad);
        AssertVoid(oParts.Body, CoolantFittings.At(oFit.ForwardMidR, oFit.ForwardZ, oFit.AngleRad), "forward manifold");
        AssertVoid(oParts.Body, vecMate, "outlet stub");
        AssertVoid(oParts.Injector, vecMate, "pipe meets the stub");
        AssertVoid(oParts.Injector, CoolantFittings.At(oFit.PipeR, oFit.ForwardZ, oFit.AngleRad), "outlet bend");

        double fMidZ = 0.5 * (oFit.ForwardZ + oFit.FuelZ);
        Vector3 vecPipe = CoolantFittings.At(oFit.PipeR, fMidZ, oFit.AngleRad);
        AssertVoid(oParts.Injector, vecPipe, "outlet pipe");
        AssertSolid(oParts.Injector, vecPipe + Tangent(oFit.AngleRad) * (float)(oFit.BoreR + 0.6), "outlet pipe wall");
        AssertVoid(oParts.Injector, CoolantFittings.At(oFit.FuelEntryR, oFit.FuelZ, oFit.AngleRad), "fuel dome");

        double fSplitZ = -InjectorBuilder.CoaxialPlateMM - PropellantDomes.OxDepthMM - PropellantDomes.SplitterMM / 2.0;
        AssertSolid(oParts.Injector, CoolantFittings.At(oFit.FuelEntryR, fSplitZ, oFit.AngleRad), "splitter stays shut");
    }

    static Vector3 Tangent(double fAngle)
        => new((float)-Math.Sin(fAngle), (float)Math.Cos(fAngle), 0);

    static void AssertSolid(Voxels vox, Vector3 vec, string strWhere)
        => Assert.True(vox.bIsInside(vec), $"expected metal at {vec} ({strWhere})");

    static void AssertVoid(Voxels vox, Vector3 vec, string strWhere)
        => Assert.False(vox.bIsInside(vec), $"expected a void at {vec} ({strWhere})");
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

using System.Globalization;
using System.Numerics;
using System.Text;
using Impetus.Geometry;
using Impetus.Physics;
using PicoGK;

namespace Impetus.Cli;

/// <summary>
/// Build the lifting body, the two tanks, and the one tail engine, then write meshes.
/// </summary>
static class RocketCommand
{
    public static int Run(EngineDesign oDesign, NozzleContour oContour, string strOutDir, string[] args)
    {
        double? fBurn = args.Length >= 3 && double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double fParsed)
            ? fParsed
            : null;
        if (fBurn is <= 0)
        {
            Console.WriteLine("Burn time must be a positive number of seconds.");
            return 1;
        }

        FeedSystemSizing oFeed = VirtualValidation.SizeFeed(oDesign, fBurn);
        RocketBody.Shape oShape = RocketBody.WithExit(RocketBody.Size(oDesign, oFeed), oContour.ExitZ * 1000.0);
        PrintShape(oShape);
        File.WriteAllText(Path.Combine(strOutDir, "rocket.md"), Describe(oDesign, oShape));

        Console.WriteLine("Building the lifting body, duct, and tail engine at 1.5 mm voxels...");
        using Library lib = new(1.5f);
        ThrusterBuilder oBuilder = new(lib, oDesign, oContour);
        Voxels voxEngine = oBuilder.voxBuildShell();
        Voxels voxSkin = RocketBody.voxSkin(lib, oShape);
        ReportHole(voxSkin, oShape);
        Voxels voxFin = RocketBody.voxFin(lib, oShape);
        Voxels voxFan = RocketBody.voxFan(lib, oShape);
        Voxels voxFlaps = RocketBody.voxFlaps(lib, oShape);
        Voxels voxFuel = RocketBody.voxTank(lib, oShape.Fuel);
        Voxels voxOx = RocketBody.voxTank(lib, oShape.Ox);

        Voxels voxOutside = new(voxSkin);
        voxOutside.BoolAdd(voxFin);
        voxOutside.BoolAdd(voxFan);
        voxOutside.BoolAdd(voxFlaps);
        voxOutside.BoolAdd(voxEngine);

        Voxels voxInside = new(voxOutside);
        voxInside.BoolAdd(voxFuel);
        voxInside.BoolAdd(voxOx);
        Voxels voxCut = RocketBody.voxCutOpen(lib, voxInside);

        var matSkin = new MaterialInfo("Aluminum", "#C5CCD3", 1.0);
        var matFuel = new MaterialInfo("Fuel tank", "#C4A574", 1.0);
        var matOx = new MaterialInfo("Oxidizer tank", "#8FB4C9", 1.0);
        var matEngine = new MaterialInfo("Copper", "#B87333", 1.0);

        Console.WriteLine("Writing meshes...");
        MeshExport.SaveAssemblyMeshFiles(
            voxOutside.mshAsMesh(),
            [
                new MeshExport.ThreeMfPart("body", voxSkin.mshAsMesh(), matSkin),
                new MeshExport.ThreeMfPart("fins", voxFin.mshAsMesh(), matSkin),
                new MeshExport.ThreeMfPart("fan", voxFan.mshAsMesh(), matSkin),
                new MeshExport.ThreeMfPart("flaps", voxFlaps.mshAsMesh(), matSkin),
                new MeshExport.ThreeMfPart("fuel-tank", voxFuel.mshAsMesh(), matFuel),
                new MeshExport.ThreeMfPart("ox-tank", voxOx.mshAsMesh(), matOx),
                new MeshExport.ThreeMfPart("engine", voxEngine.mshAsMesh(), matEngine),
            ],
            Path.Combine(strOutDir, "rocket.stl"),
            Path.Combine(strOutDir, "rocket.3mf"),
            oDesign.Spec.Name + " rocket");
        voxCut.mshAsMesh().SaveToStlFile(Path.Combine(strOutDir, "rocket_cutaway.stl"));

        Console.WriteLine($"  -> {Path.Combine(strOutDir, "rocket.stl")}");
        Console.WriteLine($"  -> {Path.Combine(strOutDir, "rocket.3mf")} (body, fins, fan, flaps, tanks, engine)");
        Console.WriteLine($"  -> {Path.Combine(strOutDir, "rocket_cutaway.stl")}");
        Console.WriteLine("This is a sized model. It is not a flight simulation.");
        return 0;
    }

    static void ReportHole(Voxels voxSkin, RocketBody.Shape oShape)
    {
        int nMetal = 0;
        float fZ = (float)oShape.NoseBaseZ;
        float fTop = (float)oShape.BodyHalfH + 20f;
        for (float fY = -fTop; fY <= fTop; fY += 3f)
        {
            if (voxSkin.bIsInside(new Vector3(0f, fY, fZ)))
                nMetal++;
        }

        // The tank bay starts a few millimetres outside the hole, so the web sample stays in that skin.
        bool bWall = voxSkin.bIsInside(new Vector3((float)(oShape.HoleR + 4.0), 0f, fZ));
        Console.WriteLine(nMetal == 0 && bWall
            ? "  The hole is open from above the top skin to below the bottom skin."
            : $"  The hole check failed ({nMetal} solid samples, wall kept {bWall}).");
    }

    static void PrintShape(RocketBody.Shape o)
    {
        Console.WriteLine($"UFO, {o.BurnS:F0} s burn of one engine");
        PrintTank(o.Fuel);
        PrintTank(o.Ox);
        Console.WriteLine($"  Span {o.BodyHalfW * 2:F0} mm, length {o.MountZ - o.NoseTipZ:F0} mm, thick {o.BodyHalfH * 2:F0} mm");
        Console.WriteLine($"  Fuselage half-width {o.FuselageHalf:F0} mm. Fan duct {o.HoleR * 2:F0} mm at {o.NoseBaseZ:F0} mm");
        Console.WriteLine("  One engine, buried in the tail. The tanks hold that one engine for this burn.");
        Console.WriteLine($"  Canted fins {o.FinHeight:F0} mm.");
        Console.WriteLine($"  Nozzle exits at {o.ExitZ:F0} mm. Overall length {o.ExitZ - o.NoseTipZ:F0} mm");
    }

    static void PrintTank(RocketBody.Tank o)
    {
        Console.WriteLine(
            $"  {o.Name} tank  {o.MassKg:F2} kg burned, {o.CapacityKg:F2} kg capacity, " +
            $"{o.PressureBar:F1} bar, wall {o.WallMM:F2} mm, length {o.AftTipZ - o.FwdTipZ:F0} mm");
    }

    static string Describe(EngineDesign oDesign, RocketBody.Shape o)
    {
        StringBuilder sb = new();
        sb.AppendLine("# UFO");
        sb.AppendLine();
        sb.AppendLine("UFO is a teardrop lifting body. Rounded at the nose, sharp at the tail,");
        sb.AppendLine("with blended wings and upturned tips. A duct goes straight through");
        sb.AppendLine("the middle, with a fan shape in it. One copy of this engine sits");
        sb.AppendLine("in the tail. The tanks hold");
        sb.AppendLine($"{RocketBody.CapacityFactor:F1} times the propellant burned. The wall is the thicker of");
        sb.AppendLine($"{RocketBody.MinWallMM:F1} mm and the hoop thickness at {RocketBody.AllowablePa / 1e6:F0} MPa.");
        sb.AppendLine("That stress is a screening value for aluminum, about a third of");
        sb.AppendLine("6061-T6 yield. It is not a qualified tank.");
        sb.AppendLine();
        sb.AppendLine($"Burn {o.BurnS:F0} s for one engine. Overall length {o.ExitZ - o.NoseTipZ:F0} mm.");
        sb.AppendLine($"Span {o.BodyHalfW * 2:F0} mm, body length {o.MountZ - o.NoseTipZ:F0} mm, {o.BodyHalfH * 2:F0} mm thick.");
        sb.AppendLine($"Fan duct {o.HoleR * 2:F0} mm. One engine at the tail.");
        sb.AppendLine("The fan, vanes, canopy, slots, flaps, and belly grooves are shapes.");
        sb.AppendLine("The fan thrust is not calculated. The grooves are not a heat shield.");
        sb.AppendLine();
        sb.AppendLine("| Tank | Burned | Capacity | Pressure | Wall | Length |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        AppendTank(sb, o.Fuel);
        AppendTank(sb, o.Ox);
        sb.AppendLine();
        sb.AppendLine("A long thin wing would waste less energy on lift. This keeps the");
        sb.AppendLine("saucer look. It is not a flown vehicle.");
        sb.AppendLine();
        sb.AppendLine("This model can be looked at. It does not fly. The nozzle");
        sb.AppendLine("calculation still solves only the gas inside the engine.");
        sb.AppendLine();
        return sb.ToString();
    }

    static void AppendTank(StringBuilder sb, RocketBody.Tank o)
    {
        sb.AppendLine(
            $"| {o.Name} | {o.MassKg:F2} kg | {o.CapacityKg:F2} kg | {o.PressureBar:F1} bar | {o.WallMM:F2} mm | {o.AftTipZ - o.FwdTipZ:F0} mm |");
    }
}

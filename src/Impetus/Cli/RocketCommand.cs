using System.Globalization;
using System.Text;
using Impetus.Geometry;
using Impetus.Physics;
using PicoGK;

namespace Impetus.Cli;

/// <summary>
/// Build the tube and the two tanks in front of the engine and write meshes.
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

        Console.WriteLine("Building the round plate and engine shell at 1.5 mm voxels...");
        using Library lib = new(1.5f);
        ThrusterBuilder oBuilder = new(lib, oDesign, oContour);
        Voxels voxShell = oBuilder.voxBuildShell();
        Voxels voxSkin = RocketBody.voxSkin(lib, oShape);
        Voxels voxFin = RocketBody.voxFin(lib, oShape);
        Voxels voxFuel = RocketBody.voxTank(lib, oShape.Fuel);
        Voxels voxOx = RocketBody.voxTank(lib, oShape.Ox);

        Voxels voxOutside = new(voxSkin);
        voxOutside.BoolAdd(voxFin);
        voxOutside.BoolAdd(voxShell);

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
                new MeshExport.ThreeMfPart("fin", voxFin.mshAsMesh(), matSkin),
                new MeshExport.ThreeMfPart("fuel-tank", voxFuel.mshAsMesh(), matFuel),
                new MeshExport.ThreeMfPart("ox-tank", voxOx.mshAsMesh(), matOx),
                new MeshExport.ThreeMfPart("engine", voxShell.mshAsMesh(), matEngine),
            ],
            Path.Combine(strOutDir, "rocket.stl"),
            Path.Combine(strOutDir, "rocket.3mf"),
            oDesign.Spec.Name + " rocket");
        voxCut.mshAsMesh().SaveToStlFile(Path.Combine(strOutDir, "rocket_cutaway.stl"));

        Console.WriteLine($"  -> {Path.Combine(strOutDir, "rocket.stl")}");
        Console.WriteLine($"  -> {Path.Combine(strOutDir, "rocket.3mf")} (body, fin, fuel-tank, ox-tank, engine)");
        Console.WriteLine($"  -> {Path.Combine(strOutDir, "rocket_cutaway.stl")}");
        Console.WriteLine("This is a sized model. It is not a flight simulation.");
        return 0;
    }

    static void PrintShape(RocketBody.Shape o)
    {
        Console.WriteLine($"Round plate, {o.BurnS:F0} s burn");
        PrintTank(o.Fuel);
        PrintTank(o.Ox);
        Console.WriteLine($"  Plate {o.BodyHalfW * 2:F0} mm across and {o.BodyHalfH * 2:F0} mm thick in the middle");
        Console.WriteLine($"  Fin {o.FinHeight:F0} mm");
        Console.WriteLine($"  Engine exit at {o.ExitZ:F0} mm. Overall length {o.ExitZ - o.NoseTipZ:F0} mm");
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
        sb.AppendLine($"# {oDesign.Spec.Name} rocket stack");
        sb.AppendLine();
        sb.AppendLine("A round plate. It is thick in the middle, where the two tanks sit");
        sb.AppendLine("side by side, and thin at the rim. A short fin is on top, and the");
        sb.AppendLine("engine is at the back. The tanks hold");
        sb.AppendLine($"{RocketBody.CapacityFactor:F1} times the propellant burned. The wall is the thicker of");
        sb.AppendLine($"{RocketBody.MinWallMM:F1} mm and the hoop thickness at {RocketBody.AllowablePa / 1e6:F0} MPa.");
        sb.AppendLine("That stress is a screening value for aluminum, about a third of");
        sb.AppendLine("6061-T6 yield. It is not a qualified tank.");
        sb.AppendLine();
        sb.AppendLine($"Burn {o.BurnS:F0} s. Overall length {o.ExitZ - o.NoseTipZ:F0} mm.");
        sb.AppendLine($"Plate {o.BodyHalfW * 2:F0} mm across, {o.BodyHalfH * 2:F0} mm thick in the middle.");
        sb.AppendLine();
        sb.AppendLine("| Tank | Burned | Capacity | Pressure | Wall | Length |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|");
        AppendTank(sb, o.Fuel);
        AppendTank(sb, o.Ox);
        sb.AppendLine();
        sb.AppendLine("The rim is the thin edge of the plate. This is not a flown vehicle.");
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

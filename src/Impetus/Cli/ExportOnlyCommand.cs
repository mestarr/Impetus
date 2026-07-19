using Impetus.Geometry;
using Impetus.Physics;
using PicoGK;

namespace Impetus.Cli;

/// <summary>
/// Export-only command - regenerate 3MF/STL from cached voxels.
/// </summary>
static class ExportOnlyCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: impetus export-only <spec.json>");
            return 1;
        }

        string strSpecPath = args[1];
        EngineSpec oSpec = EngineSpec.Load(strSpecPath);

        Console.WriteLine($"Impetus - loaded spec '{oSpec.Name}' from {strSpecPath}");

        // Apply process-aware defaults
        oSpec = Physics.ProcessGeometry.ApplyProcessDefaults(oSpec);

        Console.WriteLine($"  Target process: {oSpec.TargetProcess}");
        Console.WriteLine($"  Voxel size: {oSpec.VoxelSizeMM:F2} mm");

        string strOutDir = RepoPaths.DesignDir(oSpec.Name);
        Directory.CreateDirectory(strOutDir);

        // Regenerate geometry from spec (voxel caching not yet implemented)
        Console.WriteLine("Regenerating geometry from spec...");
        EngineDesign oDesign = EngineSizing.Size(oSpec);
        NozzleContour oContour = new(oDesign);

        using Library lib = new((float)oSpec.VoxelSizeMM);
        ThrusterBuilder oBuilder = new(lib, oDesign, oContour);

        EngineComponents oParts = oBuilder.voxBuildComponents();

        Console.WriteLine("Regenerating mesh exports...");
        RegenerateExports(oParts, strOutDir, oSpec);

        Console.WriteLine("Export-only complete.");
        return 0;
    }

    static void RegenerateExports(EngineComponents oParts, string strOutDir, EngineSpec oSpec)
    {
        Voxels voxEngine = new(oParts.Body);
        voxEngine.BoolAdd(oParts.Injector);
        voxEngine.BoolAdd(oParts.Flange);

        Mesh mshEngine = voxEngine.mshAsMesh();

        // Define materials for colored 3MF export
        var matBody = new MaterialInfo("Copper", "#B87333", 1.0);
        var matInjector = new MaterialInfo("Inconel", "#A0A0A0", 1.0);
        var matFlange = new MaterialInfo("Aluminum", "#D4D4D4", 1.0);

        MeshExport.SaveAssemblyMeshFiles(
            mshEngine,
            [
                new MeshExport.ThreeMfPart("body", oParts.Body.mshAsMesh(), matBody),
                new MeshExport.ThreeMfPart("injector", oParts.Injector.mshAsMesh(), matInjector),
                new MeshExport.ThreeMfPart("flange", oParts.Flange.mshAsMesh(), matFlange),
            ],
            Path.Combine(strOutDir, "engine.stl"),
            Path.Combine(strOutDir, "engine.3mf"),
            oSpec.Name);

        // Also export OBJ for viewers that don't support 3MF
        MeshExport.SaveToObjFile(mshEngine, Path.Combine(strOutDir, "engine.obj"), oSpec.Name);

        Console.WriteLine($"  -> {Path.Combine(strOutDir, "engine.stl")}");
        Console.WriteLine($"  -> {Path.Combine(strOutDir, "engine.3mf")} (colored body + injector + flange)");
        Console.WriteLine($"  -> {Path.Combine(strOutDir, "engine.obj")} (for OBJ viewers)");
    }
}

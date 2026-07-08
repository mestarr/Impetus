using Impetus.Cfd;
using Impetus.Geometry;
using Impetus.Physics;
using Impetus.Reporting;
using PicoGK;

namespace Impetus.Cli;

static class ImpetusApp
{
    public static int Run(string[] args)
    {
        string strCmd = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
        string strSpecPath = args.Length > 1 ? args[1] : RepoPaths.DefaultSpec();

        if (strCmd == "smoke")
            return RunSmoke();

        if (strCmd is not ("design" or "test" or "all" or "view" or "post" or "validate" or "iterate" or "print" or "sweep" or "optimize"))
        {
            Console.WriteLine("usage: impetus design|test|all|view|post|validate|iterate|print|sweep|optimize [spec.json] [args]");
            return 1;
        }

        EngineSpec oSpec = EngineSpec.Load(strSpecPath);
        Console.WriteLine($"Impetus - loaded spec '{oSpec.Name}' from {strSpecPath}");

        EngineDesign oDesign = EngineSizing.Size(oSpec);
        NozzleContour oContour = new(oDesign);
        ThermalResult oTherm = ThermalModel.Evaluate(oDesign, oContour);
        DesignReport.PrintSummary(oDesign, oTherm);

        string strOutDir = RepoPaths.DesignDir(oSpec.Name);
        Directory.CreateDirectory(strOutDir);

        string strReport = DesignReport.Build(oDesign, oTherm);
        CfdResult? oCfdForValidation = TryLoadCfd(strOutDir, oDesign);
        ValidationResult oValidation = VirtualValidation.Evaluate(oDesign, oTherm, oCfdForValidation);
        ValidationReport.PrintSummary(oValidation);

        if (strCmd == "iterate")
            return RunIterate(oSpec);

        if (strCmd == "sweep")
            return RunSweep(oSpec, args);

        if (strCmd == "optimize")
            return RunOptimize(oSpec, args);

        if (strCmd == "print")
        {
            PrintReport.PrintConsoleSummary(oDesign, oSpec);
            WriteReports(strOutDir, strReport, oDesign, oTherm, oValidation, oSpec);
            return 0;
        }

        if (strCmd is "design" or "all")
            ExportGeometry(oSpec, oDesign, oContour, strOutDir);

        if (strCmd is "test" or "all")
            (strReport, oValidation) = RunCfd(oSpec, oDesign, oContour, oTherm, strOutDir, strReport, ref oCfdForValidation);

        if (strCmd == "post")
            (strReport, oValidation) = PostProcessCfd(oDesign, oTherm, strOutDir, strReport);

        WriteReports(strOutDir, strReport, oDesign, oTherm, oValidation, oSpec);

        if (strCmd is "view")
            RunViewer(oSpec, oDesign, oContour);

        return 0;
    }

    static int RunSmoke()
    {
        Console.WriteLine("smoke: creating library...");
        using (Library libSmoke = new(0.5f))
        {
            Console.WriteLine("smoke: one beam...");
            Lattice latSmoke = new(libSmoke);
            latSmoke.AddBeam(new(0, 0, 0), new(0, 0, 20), 5f, 8f, bRoundCap: false);
            Voxels voxSmoke = new(latSmoke);
            Console.WriteLine("smoke: bool ops...");
            Lattice latHole = new(libSmoke);
            latHole.AddBeam(new(0, 0, -1), new(0, 0, 21), 2f, 2f, bRoundCap: false);
            voxSmoke.BoolSubtract(new Voxels(latHole));
            Console.WriteLine("smoke: meshing...");
            string strSmokePath = Path.Combine(Path.GetTempPath(), "impetus_smoke.stl");
            voxSmoke.mshAsMesh().SaveToStlFile(strSmokePath);
            Console.WriteLine($"smoke: OK -> {strSmokePath}");
        }
        Console.WriteLine("smoke: library disposed cleanly");
        return 0;
    }

    static int RunIterate(EngineSpec oSpec)
    {
        Console.WriteLine("Auto-iterate: turning validation failures into spec corrections");
        Console.WriteLine("(CFD is not run inside the loop - run 'test' on the result).");
        IterateOutcome oOut = AutoIterate.Run(oSpec);

        if (oOut.Steps.Count == 0 && oOut.Converged)
        {
            Console.WriteLine("  Spec already passes all analytic gates - nothing to change.");
            Console.WriteLine($"  Next: dotnet run --project src/Impetus -- test specs/{oSpec.Name}.json");
            return 0;
        }

        foreach (IterationStep oStep in oOut.Steps)
            Console.WriteLine($"  [pass {oStep.Iteration}] {oStep.Field}: {oStep.From} -> {oStep.To}  ({oStep.Reason})");

        Console.WriteLine();
        Console.WriteLine($"  Coolant dT   {oOut.OriginalThermal.CoolantTempRise,7:F0} K    -> {oOut.FinalThermal.CoolantTempRise,7:F0} K");
        Console.WriteLine($"  Throat flux  {oOut.OriginalThermal.ThroatHeatFlux / 1e6,7:F1} MW/m2 -> {oOut.FinalThermal.ThroatHeatFlux / 1e6,7:F1} MW/m2");
        Console.WriteLine($"  Isp (SL)     {oOut.OriginalDesign.IspSeaLevelS,7:F1} s    -> {oOut.FinalDesign.IspSeaLevelS,7:F1} s");
        Console.WriteLine();
        ValidationReport.PrintSummary(oOut.FinalValidation);

        if (!oOut.Converged)
        {
            foreach (string strNote in oOut.Unfixable)
                Console.WriteLine($"  NOT FIXABLE FROM SPEC: {strNote}");

            if (oOut.PropellantWhatIf.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  Propellant what-if (same thrust/Pc, nominal O/F):");
                foreach (PropellantOption oOpt in oOut.PropellantWhatIf)
                    Console.WriteLine($"    {(oOpt.PassesGate ? "PASS" : "fail"),-4}  {oOpt.Key,-14} dT {oOpt.CoolantRiseK,5:F0} K");
            }
        }

        if (oOut.Steps.Count > 0)
        {
            string strNewSpecPath = Path.Combine(RepoPaths.Root(), "specs", oOut.FinalSpec.Name + ".json");
            oOut.FinalSpec.Save(strNewSpecPath);

            string strIterDir = RepoPaths.DesignDir(oOut.FinalSpec.Name);
            Directory.CreateDirectory(strIterDir);
            string strLogPath = Path.Combine(strIterDir, "iteration-log.md");
            File.WriteAllText(strLogPath, ValidationReport.BuildIterationLog(oOut));

            Console.WriteLine();
            Console.WriteLine($"  New spec:      {strNewSpecPath}");
            Console.WriteLine($"  Iteration log: {strLogPath}");
            Console.WriteLine(oOut.Converged
                ? $"  Next: dotnet run --project src/Impetus -- design specs/{oOut.FinalSpec.Name}.json"
                : "  Analytic gates not fully clean - remaining items need model upgrades, not spec edits (see log).");
        }
        else if (!oOut.Converged)
        {
            Console.WriteLine("  No spec-level fix available - the failures need model/scale changes (see notes above).");
        }
        return 0;
    }

    static void ExportGeometry(EngineSpec oSpec, EngineDesign oDesign, NozzleContour oContour, string strOutDir)
    {
        Console.WriteLine("Generating geometry (PicoGK, headless)...");
        using Library lib = new((float)oSpec.VoxelSizeMM);
        ThrusterBuilder oBuilder = new(lib, oDesign, oContour);

        EngineComponents oParts = oBuilder.voxBuildComponents();
        Voxels voxEngine = new(oParts.Body);
        voxEngine.BoolAdd(oParts.Injector);
        voxEngine.BoolAdd(oParts.Flange);

        Mesh mshEngine = voxEngine.mshAsMesh();
        MeshExport.SaveAssemblyMeshFiles(
            mshEngine,
            [
                new MeshExport.ThreeMfPart("body", oParts.Body.mshAsMesh()),
                new MeshExport.ThreeMfPart("injector", oParts.Injector.mshAsMesh()),
                new MeshExport.ThreeMfPart("flange", oParts.Flange.mshAsMesh()),
            ],
            Path.Combine(strOutDir, "engine.stl"),
            Path.Combine(strOutDir, "engine.3mf"),
            oSpec.Name);

        Voxels voxCutBody = oBuilder.voxCutaway(oParts.Body);
        Voxels voxCutInjector = oBuilder.voxCutaway(oParts.Injector);
        Voxels voxCutFlange = oBuilder.voxCutaway(oParts.Flange);

        Voxels voxCut = new(voxCutBody);
        voxCut.BoolAdd(voxCutInjector);
        voxCut.BoolAdd(voxCutFlange);

        MeshExport.SaveAssemblyMeshFiles(
            voxCut.mshAsMesh(),
            [
                new MeshExport.ThreeMfPart("body", voxCutBody.mshAsMesh()),
                new MeshExport.ThreeMfPart("injector", voxCutInjector.mshAsMesh()),
                new MeshExport.ThreeMfPart("flange", voxCutFlange.mshAsMesh()),
            ],
            Path.Combine(strOutDir, "engine_cutaway.stl"),
            Path.Combine(strOutDir, "engine_cutaway.3mf"),
            oSpec.Name + " (cutaway)");

        Console.WriteLine($"  -> {Path.Combine(strOutDir, "engine.stl")}");
        Console.WriteLine($"  -> {Path.Combine(strOutDir, "engine.3mf")} (body + injector + flange)");
        Console.WriteLine($"  -> {Path.Combine(strOutDir, "engine_cutaway.stl")}");
        Console.WriteLine($"  -> {Path.Combine(strOutDir, "engine_cutaway.3mf")} (body + injector + flange)");
    }

    static (string Report, ValidationResult Validation) RunCfd(
        EngineSpec oSpec,
        EngineDesign oDesign,
        NozzleContour oContour,
        ThermalResult oTherm,
        string strOutDir,
        string strReport,
        ref CfdResult? oCfdForValidation)
    {
        string strCaseDir = Path.Combine(strOutDir, "cfd");
        Console.WriteLine("Writing SU2 case (axisymmetric RANS-SST, hot gas path)...");
        double fWallBc = double.IsNaN(oTherm.ThroatWallTempK)
            ? oTherm.PeakWallTempK
            : oTherm.ThroatWallTempK;
        if (double.IsNaN(fWallBc) || fWallBc <= 0)
            fWallBc = ThermalModel.AssumedRegenWallTempK;
        new Su2Case(oDesign, oContour, fWallBc).Write(strCaseDir);

        string? strSu2 = Su2Runner.FindSu2();
        if (strSu2 is null)
        {
            Console.WriteLine("  SU2_CFD.exe not found under tools/ - case written, not run.");
            Console.WriteLine($"  Run manually: cd \"{strCaseDir}\" && SU2_CFD {Su2Case.ConfigFile}");
            return (strReport, VirtualValidation.Evaluate(oDesign, oTherm, oCfdForValidation));
        }

        Console.WriteLine($"  Solver: {strSu2}");
        Console.WriteLine("  Running virtual test (this takes a few minutes)...");
        int nExit = Su2Runner.Run(strSu2, strCaseDir, Console.Out);

        if (nExit == 0)
        {
            try
            {
                CfdResult oCfd = Su2Runner.PostProcess(strCaseDir, oDesign);
                oCfdForValidation = oCfd;
                ValidationResult oValidation = VirtualValidation.Evaluate(oDesign, oTherm, oCfdForValidation);
                strReport += DesignReport.CfdSection(oDesign, oCfd, oTherm);

                Console.WriteLine();
                Console.WriteLine($"  CFD thrust     {oCfd.ThrustN,10:F1} N   (analytic {oSpec.ThrustN:F1} N)");
                Console.WriteLine($"  CFD mass flow  {oCfd.MassFlow,10:F3} kg/s (analytic {oDesign.MassFlow:F3} kg/s)");
                Console.WriteLine($"  CFD exit Mach  {oCfd.ExitMachAvg,10:F2}     (analytic {oDesign.ExitMach:F2})");
                return (strReport, oValidation);
            }
            catch (Exception e)
            {
                Console.WriteLine($"  CFD post-processing failed: {e.Message}");
                Console.WriteLine($"  Solver output preserved in {strCaseDir}");
            }
        }
        else
        {
            Console.WriteLine($"  SU2 exited with code {nExit} - see {Path.Combine(strCaseDir, "su2.log")}");
        }

        return (strReport, VirtualValidation.Evaluate(oDesign, oTherm, oCfdForValidation));
    }

    static (string Report, ValidationResult Validation) PostProcessCfd(
        EngineDesign oDesign, ThermalResult oTherm, string strOutDir, string strReport)
    {
        string strCaseDir = Path.Combine(strOutDir, "cfd");
        CfdResult oCfd = Su2Runner.PostProcess(strCaseDir, oDesign);
        ValidationResult oValidation = VirtualValidation.Evaluate(oDesign, oTherm, oCfd);
        strReport += DesignReport.CfdSection(oDesign, oCfd, oTherm);
        Console.WriteLine($"  CFD thrust     {oCfd.ThrustN,10:F1} N   (analytic {oDesign.Spec.ThrustN:F1} N)");
        Console.WriteLine($"  CFD mass flow  {oCfd.MassFlow,10:F3} kg/s (analytic {oDesign.MassFlow:F3} kg/s)");
        Console.WriteLine($"  CFD exit Mach  {oCfd.ExitMachAvg,10:F2}     (analytic {oDesign.ExitMach:F2})");
        return (strReport, oValidation);
    }

    static void WriteReports(
        string strOutDir,
        string strReport,
        EngineDesign oDesign,
        ThermalResult oTherm,
        ValidationResult oValidation,
        EngineSpec oSpec)
    {
        strReport += PrintReport.BuildSection(oDesign, oSpec);
        strReport += ValidationReport.BuildSection(oDesign, oValidation);
        File.WriteAllText(Path.Combine(strOutDir, "report.md"), strReport);
        File.WriteAllText(
            Path.Combine(strOutDir, "hotfire-plan.md"),
            ValidationReport.BuildHotFirePlan(oDesign, oTherm, oValidation));
        Console.WriteLine($"Report:       {Path.Combine(strOutDir, "report.md")}");
        Console.WriteLine($"Hot-fire plan: {Path.Combine(strOutDir, "hotfire-plan.md")}");
    }

    static void RunViewer(EngineSpec oSpec, EngineDesign oDesign, NozzleContour oContour)
    {
        Console.WriteLine("Opening PicoGK viewer (close window to exit)...");
        Library.Go((float)oSpec.VoxelSizeMM, () =>
        {
            ThrusterBuilder oBuilder = new(Library.oLibrary(), oDesign, oContour);
            Voxels voxEngine = oBuilder.voxBuild();
            Voxels voxCut = oBuilder.voxCutaway(voxEngine);

            Library.oViewer().SetGroupMaterial(0, new ColorFloat("B87333"), 0.9f, 0.3f);
            Library.oViewer().Add(voxCut);
            Library.Log("Engine cutaway shown. Full STL/3MF exported to designs folder.");
        });
    }

    static CfdResult? TryLoadCfd(string strOutDir, EngineDesign oDesign)
    {
        string strCaseDir = Path.Combine(strOutDir, "cfd");
        string strCsv = Path.Combine(strCaseDir, "surface_flow.csv");
        if (!File.Exists(strCsv)) return null;
        try
        {
            return Su2Runner.PostProcess(strCaseDir, oDesign);
        }
        catch
        {
            return null;
        }
    }

    static int RunSweep(EngineSpec oSpec, string[] args)
    {
        Console.WriteLine("Parameter sweep: exploring design space...");

        // Parse sweep arguments (skip command and spec path)
        string[] aoSweepArgs = args.Length > 2 ? args[2..] : [];
        SweepConfig oConfig = ParameterSweep.ParseArgs(aoSweepArgs);

        // If no parameters specified, use a reasonable default sweep
        if (oConfig.PcBar == null && oConfig.OfRatio == null && oConfig.ExpansionRatio == null)
        {
            Console.WriteLine("No sweep parameters specified, using default sweep over Pc and O/F...");
            oConfig = oConfig with
            {
                PcBar = [15, 20, 25, 30],
                OfRatio = [2.0, 2.3, 2.6]
            };
        }

        SweepResult oResult = ParameterSweep.Run(oSpec, oConfig);
        SweepReport.PrintConsoleSummary(oResult);

        // Write report
        string strOutDir = RepoPaths.DesignDir(oSpec.Name + "-sweep");
        Directory.CreateDirectory(strOutDir);
        string strReport = SweepReport.Build(oResult);
        File.WriteAllText(Path.Combine(strOutDir, "sweep-report.md"), strReport);

        Console.WriteLine();
        Console.WriteLine($"Sweep report: {Path.Combine(strOutDir, "sweep-report.md")}");

        // Export best Pareto designs as specs
        for (int i = 0; i < Math.Min(5, oResult.ParetoFront.Count); i++)
        {
            SweepPoint oPt = oResult.ParetoFront[i];
            string strSpecName = $"{oSpec.Name}-pareto-{i + 1}";
            EngineSpec oNewSpec = oPt.Spec with { Name = strSpecName };
            string strSpecPath = Path.Combine(RepoPaths.Root(), "specs", strSpecName + ".json");
            oNewSpec.Save(strSpecPath);
            Console.WriteLine($"  Pareto spec {i + 1}: {strSpecPath}");
        }

        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  - Review sweep-report.md for full analysis");
        Console.WriteLine("  - Run 'design' on a Pareto spec to generate geometry");
        Console.WriteLine("  - Run 'test' on a shortlisted design for CFD verification");

        return 0;
    }

    static int RunOptimize(EngineSpec oSpec, string[] args)
    {
        Console.WriteLine("Nelder-Mead optimization: finding optimal design...");

        // Parse optimization arguments (skip command and spec path)
        string[] aoOptArgs = args.Length > 2 ? args[2..] : [];

        // Default optimization: Pc and O/F
        string[] aoParams = ["pc", "of"];
        (double fMin, double fMax)[] aoBounds =
        [
            (10.0, 40.0),  // Pc: 10-40 bar
            (1.5, 3.5)     // O/F: 1.5-3.5
        ];

        if (aoOptArgs.Length > 0)
        {
            try
            {
                (aoParams, aoBounds) = Optimizer.ParseOptimizationArgs(aoOptArgs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing optimization args: {ex.Message}");
                Console.WriteLine("Usage: --params pc,of,eps --bounds \"10:40,1.5:3.5,0:50\"");
                return 1;
            }
        }
        else
        {
            Console.WriteLine("Using default optimization: Pc (10-40 bar) and O/F (1.5-3.5)");
        }

        OptimizationObjective oObjective = new();
        OptimizationResult oResult = Optimizer.Optimize(oSpec, aoParams, aoBounds, oObjective);
        SweepReport.PrintOptimizationSummary(oResult);

        // Write report
        string strOutDir = RepoPaths.DesignDir(oSpec.Name + "-optimize");
        Directory.CreateDirectory(strOutDir);
        string strReport = SweepReport.BuildOptimization(oResult);
        File.WriteAllText(Path.Combine(strOutDir, "optimization-report.md"), strReport);

        // Save best spec
        string strBestSpecName = $"{oSpec.Name}-opt";
        EngineSpec oBestSpec = oResult.BestSpec with { Name = strBestSpecName };
        string strBestSpecPath = Path.Combine(RepoPaths.Root(), "specs", strBestSpecName + ".json");
        oBestSpec.Save(strBestSpecPath);

        Console.WriteLine();
        Console.WriteLine($"Optimization report: {Path.Combine(strOutDir, "optimization-report.md")}");
        Console.WriteLine($"Best spec: {strBestSpecPath}");
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine($"  - Run 'design specs/{strBestSpecName}.json' to generate geometry");
        Console.WriteLine($"  - Run 'test specs/{strBestSpecName}.json' for CFD verification");

        return 0;
    }
}

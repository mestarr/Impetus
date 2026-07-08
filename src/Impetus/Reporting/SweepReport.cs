using System.Globalization;
using System.Linq;
using System.Text;
using Impetus.Physics;

namespace Impetus.Reporting;

/// <summary>
/// Report generation for parameter sweep results.
/// </summary>
public static class SweepReport
{
    /// <summary>
    /// Build a markdown report for sweep results.
    /// </summary>
    public static string Build(SweepResult oResult)
    {
        StringBuilder sb = new();

        sb.AppendLine($"# {oResult.BaseSpec.Name} — Parameter Sweep Report");
        sb.AppendLine();
        sb.AppendLine($"**Base spec:** {oResult.BaseSpec.Name}");
        sb.AppendLine($"**Total points evaluated:** {oResult.Points.Count}");
        sb.AppendLine($"**Pareto front size:** {oResult.ParetoFront.Count}");
        sb.AppendLine($"**Evaluation time:** {oResult.EvaluationTime.TotalSeconds:F1} s");
        sb.AppendLine();

        sb.AppendLine("## Base Specification");
        sb.AppendLine();
        sb.AppendLine($"| Parameter | Value |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| Thrust | {F(oResult.BaseSpec.ThrustN, "F0")} N |");
        sb.AppendLine($"| Chamber pressure | {F(oResult.BaseSpec.ChamberPressureBar, "F1")} bar |");
        sb.AppendLine($"| O/F ratio | {F(oResult.BaseSpec.OfRatio, "F2")} |");
        sb.AppendLine($"| Expansion ratio | {F(oResult.BaseSpec.ExpansionRatio, "F2")} |");
        sb.AppendLine($"| Bell fraction | {F(oResult.BaseSpec.BellFraction, "F2")} |");
        sb.AppendLine($"| Cooling channels | {oResult.BaseSpec.Cooling.Count} × Ø{F(oResult.BaseSpec.Cooling.DiameterMM, "F1")} mm |");
        sb.AppendLine();

        sb.AppendLine("## Pareto Front");
        sb.AppendLine();
        sb.AppendLine("Non-dominated designs (maximize Isp, minimize coolant dT, minimize length):");
        sb.AppendLine();
        sb.AppendLine($"| Rank | Isp (s) | Coolant dT (K) | Length (mm) | Throat Flux (MW/m²) | Pc (bar) | O/F | ε | Bell | Cooling |");
        sb.AppendLine($"|---|---|---|---|---|---|---|---|---|---|");

        for (int i = 0; i < oResult.ParetoFront.Count; i++)
        {
            SweepPoint oPt = oResult.ParetoFront[i];
            EngineSpec s = oPt.Spec;
            sb.AppendLine($"| {i + 1} | {F(oPt.IspSeaLevelS, "F1")} | {F(oPt.CoolantTempRise, "F0")} | {MM(oPt.EngineLengthM)} | {F(oPt.ThroatHeatFluxMW, "F1")} | {F(s.ChamberPressureBar, "F1")} | {F(s.OfRatio, "F2")} | {F(s.ExpansionRatio, "F1")} | {F(s.BellFraction, "F2")} | {s.Cooling.Count}×{F(s.Cooling.DiameterMM, "F1")} |");
        }
        sb.AppendLine();

        if (oResult.Points.Count > oResult.ParetoFront.Count)
        {
            sb.AppendLine("## All Evaluated Points");
            sb.AppendLine();
            sb.AppendLine($"| Isp (s) | Coolant dT (K) | Length (mm) | Throat Flux (MW/m²) | Pc (bar) | O/F | ε | Bell | Cooling |");
            sb.AppendLine($"|---|---|---|---|---|---|---|---|---|");

            foreach (SweepPoint oPt in oResult.Points.OrderByDescending(p => p.IspSeaLevelS))
            {
                EngineSpec s = oPt.Spec;
                sb.AppendLine($"| {F(oPt.IspSeaLevelS, "F1")} | {F(oPt.CoolantTempRise, "F0")} | {MM(oPt.EngineLengthM)} | {F(oPt.ThroatHeatFluxMW, "F1")} | {F(s.ChamberPressureBar, "F1")} | {F(s.OfRatio, "F2")} | {F(s.ExpansionRatio, "F1")} | {F(s.BellFraction, "F2")} | {s.Cooling.Count}×{F(s.Cooling.DiameterMM, "F1")} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Design Space Summary");
        sb.AppendLine();
        var aoIsp = oResult.Points.Select(p => p.IspSeaLevelS).ToList();
        var aoDt = oResult.Points.Select(p => p.CoolantTempRise).ToList();
        var aoLen = oResult.Points.Select(p => p.EngineLengthM).ToList();

        sb.AppendLine($"| Metric | Min | Max | Mean |");
        sb.AppendLine($"|---|---|---|---|");
        sb.AppendLine($"| Isp (s) | {F(aoIsp.Min(), "F1")} | {F(aoIsp.Max(), "F1")} | {F(aoIsp.Average(), "F1")} |");
        sb.AppendLine($"| Coolant dT (K) | {F(aoDt.Min(), "F0")} | {F(aoDt.Max(), "F0")} | {F(aoDt.Average(), "F0")} |");
        sb.AppendLine($"| Length (mm) | {MM(aoLen.Min())} | {MM(aoLen.Max())} | {MM(aoLen.Average())} |");
        sb.AppendLine();

        sb.AppendLine("## Recommendations");
        sb.AppendLine();
        if (oResult.ParetoFront.Count > 0)
        {
            SweepPoint oBestIsp = oResult.ParetoFront.OrderByDescending(p => p.IspSeaLevelS).First();
            SweepPoint oBestDt = oResult.ParetoFront.OrderBy(p => p.CoolantTempRise).First();
            SweepPoint oBestLen = oResult.ParetoFront.OrderBy(p => p.EngineLengthM).First();

            sb.AppendLine($"- **Best Isp:** {oBestIsp.IspSeaLevelS:F1} s at Pc={oBestIsp.Spec.ChamberPressureBar:F1} bar, O/F={oBestIsp.Spec.OfRatio:F2}");
            sb.AppendLine($"- **Best cooling:** dT={oBestDt.CoolantTempRise:F0} K at Pc={oBestDt.Spec.ChamberPressureBar:F1} bar, O/F={oBestDt.Spec.OfRatio:F2}");
            sb.AppendLine($"- **Most compact:** {MM(oBestLen.EngineLengthM)} mm at Pc={oBestLen.Spec.ChamberPressureBar:F1} bar, O/F={oBestLen.Spec.OfRatio:F2}");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Build a markdown report for optimization results.
    /// </summary>
    public static string BuildOptimization(OptimizationResult oResult)
    {
        StringBuilder sb = new();

        sb.AppendLine($"# {oResult.BestSpec.Name} — Optimization Report");
        sb.AppendLine();
        sb.AppendLine($"**Algorithm:** Nelder-Mead simplex");
        sb.AppendLine($"**Iterations:** {oResult.Iterations}");
        sb.AppendLine($"**Converged:** {(oResult.Converged ? "Yes" : "No (max iterations reached)")}");
        sb.AppendLine($"**Final objective value:** {oResult.BestObjectiveValue:F4}");
        sb.AppendLine();

        sb.AppendLine("## Best Design");
        sb.AppendLine();
        sb.AppendLine($"| Parameter | Value |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| Thrust | {F(oResult.BestSpec.ThrustN, "F0")} N |");
        sb.AppendLine($"| Chamber pressure | {F(oResult.BestSpec.ChamberPressureBar, "F1")} bar |");
        sb.AppendLine($"| O/F ratio | {F(oResult.BestSpec.OfRatio, "F2")} |");
        sb.AppendLine($"| Expansion ratio | {F(oResult.BestSpec.ExpansionRatio, "F2")} |");
        sb.AppendLine($"| Bell fraction | {F(oResult.BestSpec.BellFraction, "F2")} |");
        sb.AppendLine($"| Cooling channels | {oResult.BestSpec.Cooling.Count} × Ø{F(oResult.BestSpec.Cooling.DiameterMM, "F1")} mm |");
        sb.AppendLine();

        sb.AppendLine("## Performance");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|---|---|");
        sb.AppendLine($"| Isp (sea level) | {F(oResult.BestDesign.IspSeaLevelS, "F1")} s |");
        sb.AppendLine($"| Isp (vacuum) | {F(oResult.BestDesign.IspVacuumS, "F1")} s |");
        sb.AppendLine($"| Coolant temperature rise | {F(oResult.BestThermal.CoolantTempRise, "F0")} K |");
        sb.AppendLine($"| Engine length | {MM(oResult.BestDesign.ChamberCylinderLength + oResult.BestDesign.BellLength)} mm |");
        sb.AppendLine($"| Throat heat flux | {F(oResult.BestThermal.ThroatHeatFlux / 1e6, "F1")} MW/m² |");
        sb.AppendLine($"| Peak wall temperature | {F(oResult.BestThermal.PeakWallTempK, "F0")} K |");
        sb.AppendLine();

        sb.AppendLine("## Convergence History");
        sb.AppendLine();
        sb.AppendLine("The optimizer evaluated " + oResult.History.Count + " design points.");
        sb.AppendLine("Run with `--save-history` to export full iteration trace.");
        sb.AppendLine();

        return sb.ToString();
    }

    public static void PrintConsoleSummary(SweepResult oResult)
    {
        Console.WriteLine();
        Console.WriteLine("  == Parameter Sweep Results ==");
        Console.WriteLine($"  Total points: {oResult.Points.Count}");
        Console.WriteLine($"  Pareto front: {oResult.ParetoFront.Count} designs");
        Console.WriteLine($"  Time: {oResult.EvaluationTime.TotalSeconds:F1} s");
        Console.WriteLine();

        if (oResult.ParetoFront.Count > 0)
        {
            Console.WriteLine("  Pareto front (Isp vs dT vs length):");
            Console.WriteLine("  Rank  Isp (s)    dT (K)     Len (mm)   Pc     O/F  ");
            for (int i = 0; i < Math.Min(10, oResult.ParetoFront.Count); i++)
            {
                SweepPoint oPt = oResult.ParetoFront[i];
                Console.WriteLine($"  {i + 1,-4} {oPt.IspSeaLevelS,-10:F1} {oPt.CoolantTempRise,-10:F0} {MM(oPt.EngineLengthM),-10} {oPt.Spec.ChamberPressureBar,-6:F1} {oPt.Spec.OfRatio,-6:F2}");
            }
            if (oResult.ParetoFront.Count > 10)
                Console.WriteLine($"  ... and {oResult.ParetoFront.Count - 10} more");
        }

        // Show best in each category
        if (oResult.Points.Count > 0)
        {
            var aoByIsp = oResult.Points.OrderByDescending(p => p.IspSeaLevelS).First();
            var aoByDt = oResult.Points.OrderBy(p => p.CoolantTempRise).First();
            var aoByLen = oResult.Points.OrderBy(p => p.EngineLengthM).First();

            Console.WriteLine();
            Console.WriteLine("  Best by category:");
            Console.WriteLine($"  Isp:    {aoByIsp.IspSeaLevelS:F1} s @ Pc={aoByIsp.Spec.ChamberPressureBar:F1} bar, O/F={aoByIsp.Spec.OfRatio:F2}");
            Console.WriteLine($"  Cooling: {aoByDt.CoolantTempRise:F0} K @ Pc={aoByDt.Spec.ChamberPressureBar:F1} bar, O/F={aoByDt.Spec.OfRatio:F2}");
            Console.WriteLine($"  Length:  {MM(aoByLen.EngineLengthM)} mm @ Pc={aoByLen.Spec.ChamberPressureBar:F1} bar, O/F={aoByLen.Spec.OfRatio:F2}");
        }
    }

    public static void PrintOptimizationSummary(OptimizationResult oResult)
    {
        Console.WriteLine();
        Console.WriteLine("  == Nelder-Mead Optimization Results ==");
        Console.WriteLine($"  Iterations: {oResult.Iterations}");
        Console.WriteLine($"  Converged: {(oResult.Converged ? "Yes" : "No")}");
        Console.WriteLine($"  Objective value: {oResult.BestObjectiveValue:F4}");
        Console.WriteLine();
        Console.WriteLine("  Best design:");
        Console.WriteLine($"    Isp (SL):     {oResult.BestDesign.IspSeaLevelS:F1} s");
        Console.WriteLine($"    Coolant dT:   {oResult.BestThermal.CoolantTempRise:F0} K");
        Console.WriteLine($"    Engine length: {MM(oResult.BestDesign.ChamberCylinderLength + oResult.BestDesign.BellLength)} mm");
        Console.WriteLine($"    Throat flux:  {oResult.BestThermal.ThroatHeatFlux / 1e6:F1} MW/m²");
        Console.WriteLine($"    Pc:           {oResult.BestSpec.ChamberPressureBar:F1} bar");
        Console.WriteLine($"    O/F:          {oResult.BestSpec.OfRatio:F2}");
    }

    static string F(double f, string strFmt) => f.ToString(strFmt, CultureInfo.InvariantCulture);
    static string MM(double fMeters) => F(fMeters * 1000.0, "F0");
}

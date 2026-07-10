using System.Globalization;
using System.Text;
using Impetus.Physics;

namespace Impetus.Reporting;

/// <summary>
/// Report generation for iteration log with comparison table.
/// </summary>
public static class IterationLog
{
    /// <summary>
    /// Build a markdown report for iteration results.
    /// </summary>
    public static string Build(IterateOutcome oOutcome)
    {
        StringBuilder sb = new();

        sb.AppendLine("# Iteration Log");
        sb.AppendLine();
        sb.AppendLine($"**Spec:** {oOutcome.FinalSpec.Name}");
        sb.AppendLine($"**Original:** {oOutcome.OriginalSpec.Name}");
        sb.AppendLine($"**Converged:** {(oOutcome.Converged ? "Yes" : "No")}");
        sb.AppendLine($"**Iterations:** {oOutcome.Iterations}");
        sb.AppendLine($"**Optimization Mode:** {(oOutcome.OptimizationMode ? "Yes" : "No")}");
        sb.AppendLine();

        // Summary of changes
        if (oOutcome.Steps.Count > 0)
        {
            sb.AppendLine("## Mutations Applied");
            sb.AppendLine();
            sb.AppendLine("| Iter | Field | From | To | Reason |");
            sb.AppendLine("|------|-------|------|-----|--------|");
            foreach (IterationStep oStep in oOutcome.Steps)
            {
                sb.AppendLine($"| {oStep.Iteration} | `{oStep.Field}` | {oStep.From} | {oStep.To} | {oStep.Reason} |");
            }
            sb.AppendLine();
        }

        // Comparison table
        sb.AppendLine("## Iteration Comparison");
        sb.AppendLine();
        sb.AppendLine("| Iter | Pc (bar) | O/F | Bell | Len (mm) | Isp (s) | dT (K) | Flux (MW/m²) | Fails | Warns |");
        sb.AppendLine("|------|----------|-----|------|----------|---------|--------|--------------|-------|-------|");

        foreach (IterationMetrics oMetrics in oOutcome.MetricsHistory)
        {
            string strStatus = "";
            if (oMetrics.FailCount > 0)
                strStatus = " ❌";
            else if (oMetrics.WarnCount > 0)
                strStatus = " ⚠️";

            sb.AppendLine($"| {oMetrics.Iteration} | " +
                $"{oMetrics.Spec.ChamberPressureBar:F1} | " +
                $"{oMetrics.Spec.OfRatio:F2} | " +
                $"{oMetrics.Spec.BellFraction:F2} | " +
                $"{oMetrics.TotalLengthMM:F0} | " +
                $"{oMetrics.IspSeaLevelS:F1} | " +
                $"{oMetrics.CoolantTempRiseK:F0} | " +
                $"{oMetrics.ThroatHeatFluxMW:F1} | " +
                $"{oMetrics.FailCount}{strStatus} | " +
                $"{oMetrics.WarnCount} |");
        }
        sb.AppendLine();

        // Final validation summary
        sb.AppendLine("## Final Validation");
        sb.AppendLine();
        foreach (var oCheck in oOutcome.FinalValidation.Checks)
        {
            string strStatus = oCheck.Status switch
            {
                CheckStatus.Pass => "✅",
                CheckStatus.Warn => "⚠️",
                CheckStatus.Fail => "❌",
                _ => "?"
            };
            sb.AppendLine($"- {strStatus} **{oCheck.Name}**: {oCheck.Detail}");
        }
        sb.AppendLine();

        // Unfixable issues
        if (oOutcome.Unfixable.Count > 0)
        {
            sb.AppendLine("## Unfixable Issues");
            sb.AppendLine();
            foreach (string strIssue in oOutcome.Unfixable)
            {
                sb.AppendLine($"- {strIssue}");
            }
            sb.AppendLine();
        }

        // Propellant what-if
        if (oOutcome.PropellantWhatIf.Count > 0)
        {
            sb.AppendLine("## Propellant Alternatives");
            sb.AppendLine();
            sb.AppendLine("| Propellant | Coolant dT (K) | Passes Gate |");
            sb.AppendLine("|-------------|---------------|-------------|");
            foreach (PropellantOption oOption in oOutcome.PropellantWhatIf)
            {
                string strPass = oOption.PassesGate ? "✅" : "❌";
                sb.AppendLine($"| {oOption.Name} | {oOption.CoolantRiseK:F0} | {strPass} |");
            }
            sb.AppendLine();
        }

        // Performance comparison
        sb.AppendLine("## Performance Comparison");
        sb.AppendLine();
        sb.AppendLine("| Metric | Original | Final | Change |");
        sb.AppendLine("|--------|----------|-------|--------|");
        sb.AppendLine($"| Total Length (mm) | {(oOutcome.OriginalDesign.ChamberCylinderLength + oOutcome.OriginalDesign.BellLength) * 1000:F0} | {(oOutcome.FinalDesign.ChamberCylinderLength + oOutcome.FinalDesign.BellLength) * 1000:F0} | {((oOutcome.FinalDesign.ChamberCylinderLength + oOutcome.FinalDesign.BellLength) * 1000 - (oOutcome.OriginalDesign.ChamberCylinderLength + oOutcome.OriginalDesign.BellLength) * 1000):+F0} |");
        sb.AppendLine($"| Isp (SL, s) | {oOutcome.OriginalDesign.IspSeaLevelS:F1} | {oOutcome.FinalDesign.IspSeaLevelS:F1} | {(oOutcome.FinalDesign.IspSeaLevelS - oOutcome.OriginalDesign.IspSeaLevelS):+F1} |");
        sb.AppendLine($"| Coolant dT (K) | {oOutcome.OriginalThermal.CoolantTempRise:F0} | {oOutcome.FinalThermal.CoolantTempRise:F0} | {(oOutcome.FinalThermal.CoolantTempRise - oOutcome.OriginalThermal.CoolantTempRise):+F0} |");
        sb.AppendLine($"| Throat Flux (MW/m²) | {oOutcome.OriginalThermal.ThroatHeatFlux / 1e6:F1} | {oOutcome.FinalThermal.ThroatHeatFlux / 1e6:F1} | {((oOutcome.FinalThermal.ThroatHeatFlux - oOutcome.OriginalThermal.ThroatHeatFlux) / 1e6):+F1} |");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Print a console summary of iteration results.
    /// </summary>
    public static void PrintConsoleSummary(IterateOutcome oOutcome)
    {
        Console.WriteLine();
        Console.WriteLine("  == Iteration Results ==");
        Console.WriteLine($"  Converged: {(oOutcome.Converged ? "Yes" : "No")}");
        Console.WriteLine($"  Iterations: {oOutcome.Iterations}");
        Console.WriteLine($"  Mutations: {oOutcome.Steps.Count}");

        if (oOutcome.Steps.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Mutations:");
            foreach (IterationStep oStep in oOutcome.Steps)
            {
                Console.WriteLine($"    [{oStep.Iteration}] {oStep.Field}: {oStep.From} → {oStep.To} ({oStep.Reason})");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  Comparison:");
        Console.WriteLine("  Iter  Pc    O/F   Bell  Len   Isp   dT    Flux  Fails Warns");
        foreach (IterationMetrics oMetrics in oOutcome.MetricsHistory)
        {
            string strStatus = oMetrics.FailCount > 0 ? " ❌" : (oMetrics.WarnCount > 0 ? " ⚠️" : "");
            Console.WriteLine($"  {oMetrics.Iteration,-4} " +
                $"{oMetrics.Spec.ChamberPressureBar,-5:F1} " +
                $"{oMetrics.Spec.OfRatio,-5:F2} " +
                $"{oMetrics.Spec.BellFraction,-5:F2} " +
                $"{oMetrics.TotalLengthMM,-5:F0} " +
                $"{oMetrics.IspSeaLevelS,-5:F1} " +
                $"{oMetrics.CoolantTempRiseK,-5:F0} " +
                $"{oMetrics.ThroatHeatFluxMW,-5:F1} " +
                $"{oMetrics.FailCount}{strStatus,-5} " +
                $"{oMetrics.WarnCount}");
        }

        Console.WriteLine();
        Console.WriteLine("  Performance change:");
        double fLenChange = (oOutcome.FinalDesign.ChamberCylinderLength + oOutcome.FinalDesign.BellLength) * 1000 -
                           (oOutcome.OriginalDesign.ChamberCylinderLength + oOutcome.OriginalDesign.BellLength) * 1000;
        double fIspChange = oOutcome.FinalDesign.IspSeaLevelS - oOutcome.OriginalDesign.IspSeaLevelS;
        double fDtChange = oOutcome.FinalThermal.CoolantTempRise - oOutcome.OriginalThermal.CoolantTempRise;
        double fFluxChange = (oOutcome.FinalThermal.ThroatHeatFlux - oOutcome.OriginalThermal.ThroatHeatFlux) / 1e6;

        string strLen = fLenChange >= 0 ? $"+{fLenChange:F1}" : $"{fLenChange:F1}";
        string strIsp = fIspChange >= 0 ? $"+{fIspChange:F1}" : $"{fIspChange:F1}";
        string strDt = fDtChange >= 0 ? $"+{fDtChange:F0}" : $"{fDtChange:F0}";
        string strFlux = fFluxChange >= 0 ? $"+{fFluxChange:F1}" : $"{fFluxChange:F1}";

        Console.WriteLine($"    Length: {strLen,7} mm");
        Console.WriteLine($"    Isp:    {strIsp,7} s");
        Console.WriteLine($"    dT:     {strDt,7} K");
        Console.WriteLine($"    Flux:   {strFlux,7} MW/m²");
        Console.WriteLine();
    }
}

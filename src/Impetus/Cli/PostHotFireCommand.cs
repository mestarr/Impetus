using System.Text;
using System.Text.Json;
using Impetus.Physics;

namespace Impetus.Cli;

/// <summary>
/// Post-hot-fire template for creating spec revisions based on measured data.
/// </summary>
static class PostHotFireCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("usage: impetus post-hotfire <original-spec.json> <measured-thrust-N> <measured-isp-s> [measured-burn-duration-s]");
            return 1;
        }

        string strOriginalSpec = args[1];
        if (!double.TryParse(args[2], out double fMeasuredThrustN))
        {
            Console.WriteLine("Error: measured thrust must be a number");
            return 1;
        }
        if (!double.TryParse(args[3], out double fMeasuredIspS))
        {
            Console.WriteLine("Error: measured Isp must be a number");
            return 1;
        }
        double fMeasuredBurnS = args.Length > 4 && double.TryParse(args[4], out double fBurn) ? fBurn : 5.0;

        EngineSpec oOriginal = EngineSpec.Load(strOriginalSpec);

        Console.WriteLine($"Original spec: {oOriginal.Name}");
        Console.WriteLine($"Predicted thrust: {oOriginal.ThrustN:F0} N");
        Console.WriteLine($"Predicted Isp: {oOriginal.ThrustN / (oOriginal.ThrustN / 293.9):F1} s (approx)");
        Console.WriteLine();
        Console.WriteLine($"Measured thrust: {fMeasuredThrustN:F0} N");
        Console.WriteLine($"Measured Isp: {fMeasuredIspS:F1} s");
        Console.WriteLine($"Measured burn duration: {fMeasuredBurnS:F1} s");
        Console.WriteLine();

        // Calculate performance deltas
        double fThrustDelta = 100.0 * (fMeasuredThrustN - oOriginal.ThrustN) / oOriginal.ThrustN;
        double fIspDelta = 100.0 * (fMeasuredIspS - 293.9) / 293.9; // Approximate predicted Isp

        Console.WriteLine($"Thrust delta: {fThrustDelta:+0.0;-0.0}%");
        Console.WriteLine($"Isp delta: {fIspDelta:+0.0;-0.0}%");
        Console.WriteLine();

        // Generate revised spec
        string strRevisedName = BumpRevision(oOriginal.Name);
        EngineSpec oRevised = GenerateRevisedSpec(oOriginal, fMeasuredThrustN, fMeasuredIspS, strRevisedName);

        // Save revised spec
        string strRevisedPath = Path.Combine("specs", $"{strRevisedName}.json");
        Directory.CreateDirectory("specs");
        oRevised.Save(strRevisedPath);

        Console.WriteLine($"Revised spec saved to: {strRevisedPath}");
        Console.WriteLine();
        Console.WriteLine("## Post-Hot-Fire Analysis");
        Console.WriteLine();
        Console.WriteLine("### Performance Comparison");
        Console.WriteLine($"| Metric | Predicted | Measured | Delta |");
        Console.WriteLine($"|---|---|---|---|");
        Console.WriteLine($"| Thrust (N) | {oOriginal.ThrustN:F0} | {fMeasuredThrustN:F0} | {fThrustDelta:+0.0;-0.0}% |");
        Console.WriteLine($"| Isp (s) | ~293.9 | {fMeasuredIspS:F1} | {fIspDelta:+0.0;-0.0}% |");
        Console.WriteLine();

        Console.WriteLine("### Recommendations");
        Console.WriteLine();
        if (Math.Abs(fThrustDelta) < 5.0 && Math.Abs(fIspDelta) < 5.0)
        {
            Console.WriteLine("- Performance matches predictions within 5% - design is validated.");
            Console.WriteLine("- No spec changes required for next iteration.");
        }
        else if (fThrustDelta < -10.0)
        {
            Console.WriteLine("- Thrust significantly lower than predicted.");
            Console.WriteLine("- Possible causes: combustion inefficiency, nozzle flow separation, propellant quality.");
            Console.WriteLine("- Consider: chamber pressure adjustment, injector redesign, propellant verification.");
        }
        else if (fThrustDelta > 10.0)
        {
            Console.WriteLine("- Thrust significantly higher than predicted.");
            Console.WriteLine("- Possible causes: measurement error, higher chamber pressure than expected.");
            Console.WriteLine("- Verify instrumentation and feed system pressures.");
        }
        else
        {
            Console.WriteLine("- Moderate performance deviation.");
            Console.WriteLine("- Review test data for anomalies.");
            Console.WriteLine("- Consider iterative spec adjustments if needed.");
        }
        Console.WriteLine();

        Console.WriteLine("### Next Steps");
        Console.WriteLine();
        Console.WriteLine("1. Review post-hot-fire inspection report");
        Console.WriteLine("2. Analyze test data (pressure, temperature, thrust traces)");
        Console.WriteLine("3. Compare CFD results (if available) to test data");
        Console.WriteLine("4. Decide on spec revision strategy:");
        Console.WriteLine("   - Accept current design if performance is acceptable");
        Console.WriteLine("   - Use revised spec for next iteration if adjustments needed");
        Console.WriteLine("   - Run `iterate` command for automated spec tuning");
        Console.WriteLine();

        return 0;
    }

    static EngineSpec GenerateRevisedSpec(EngineSpec oOriginal, double fMeasuredThrustN, double fMeasuredIspS, string strRevisedName)
    {
        // Adjust thrust to match measured value
        // Keep other parameters the same for manual review
        return oOriginal with
        {
            Name = strRevisedName,
            ThrustN = fMeasuredThrustN
            // Note: Isp is a derived parameter, not directly in spec
            // Users may want to adjust chamber pressure or other parameters
        };
    }

    static string BumpRevision(string strName)
    {
        // Simple revision bump: add -v2, -v3, etc.
        int nDash = strName.LastIndexOf('-');
        if (nDash > 0 && strName.Substring(nDash + 1).StartsWith("v"))
        {
            string strBase = strName.Substring(0, nDash);
            string strRev = strName.Substring(nDash + 1);
            if (int.TryParse(strRev.Substring(1), out int nRev))
            {
                return $"{strBase}-v{nRev + 1}";
            }
        }
        return $"{strName}-v2";
    }
}

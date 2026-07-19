using System.Text;
using Impetus.Reporting;

namespace Impetus.Cli;

/// <summary>
/// Diff command - compare two report.md files or two specs side by side.
/// </summary>
static class DiffCommand
{
    public static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("usage: impetus diff report <path1> <path2>");
            Console.WriteLine("       impetus diff spec <path1> <path2>");
            return 1;
        }

        string strType = args[1].ToLowerInvariant();
        string strPath1 = args[2];
        string strPath2 = args.Length > 3 ? args[3] : "";

        if (strType == "report")
            return DiffReports(strPath1, strPath2);
        else if (strType == "spec")
            return DiffSpecs(strPath1, strPath2);
        else
        {
            Console.WriteLine("usage: impetus diff report <path1> <path2>");
            Console.WriteLine("       impetus diff spec <path1> <path2>");
            return 1;
        }
    }

    static int DiffReports(string strPath1, string strPath2)
    {
        if (!File.Exists(strPath1))
        {
            Console.WriteLine($"Error: report not found: {strPath1}");
            return 1;
        }
        if (!File.Exists(strPath2))
        {
            Console.WriteLine($"Error: report not found: {strPath2}");
            return 1;
        }

        string strContent1 = File.ReadAllText(strPath1);
        string strContent2 = File.ReadAllText(strPath2);

        Console.WriteLine();
        Console.WriteLine($"Comparing reports:");
        Console.WriteLine($"  A: {strPath1}");
        Console.WriteLine($"  B: {strPath2}");
        Console.WriteLine();

        // Extract key metrics from reports
        var metrics1 = ExtractReportMetrics(strContent1);
        var metrics2 = ExtractReportMetrics(strContent2);

        PrintMetricComparison(metrics1, metrics2);
        return 0;
    }

    static int DiffSpecs(string strPath1, string strPath2)
    {
        if (!File.Exists(strPath1))
        {
            Console.WriteLine($"Error: spec not found: {strPath1}");
            return 1;
        }
        if (!File.Exists(strPath2))
        {
            Console.WriteLine($"Error: spec not found: {strPath2}");
            return 1;
        }

        EngineSpec spec1 = EngineSpec.Load(strPath1);
        EngineSpec spec2 = EngineSpec.Load(strPath2);

        Console.WriteLine();
        Console.WriteLine($"Comparing specs:");
        Console.WriteLine($"  A: {spec1.Name} ({strPath1})");
        Console.WriteLine($"  B: {spec2.Name} ({strPath2})");
        Console.WriteLine();

        PrintSpecComparison(spec1, spec2);
        return 0;
    }

    static Dictionary<string, string> ExtractReportMetrics(string strReport)
    {
        var metrics = new Dictionary<string, string>();

        // Extract key metrics from markdown report
        var lines = strReport.Split('\n');
        foreach (var line in lines)
        {
            // Look for table rows with metric values
            if (line.Contains("|") && !line.TrimStart().StartsWith("|"))
            {
                var parts = line.Split('|');
                if (parts.Length >= 3)
                {
                    string key = parts[1].Trim();
                    string value = parts[2].Trim();
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        // Clean up the key
                        key = key.Replace("**", "").Trim();
                        if (!metrics.ContainsKey(key))
                            metrics[key] = value;
                    }
                }
            }
        }

        return metrics;
    }

    static void PrintMetricComparison(Dictionary<string, string> metrics1, Dictionary<string, string> metrics2)
    {
        var allKeys = metrics1.Keys.Union(metrics2.Keys).OrderBy(k => k).ToList();

        Console.WriteLine("Metric Comparison:");
        Console.WriteLine();
        Console.WriteLine($"{"Metric",-40} {"A",-20} {"B",-20} {"Diff",-15}");
        Console.WriteLine(new string('-', 95));

        foreach (var key in allKeys)
        {
            string val1 = metrics1.GetValueOrDefault(key, "N/A");
            string val2 = metrics2.GetValueOrDefault(key, "N/A");

            string diff = val1 == val2 ? "=" : "≠";
            if (val1 != val2)
                diff = $"{val1} → {val2}";

            Console.WriteLine($"{key,-40} {val1,-20} {val2,-20} {diff,-15}");
        }
        Console.WriteLine();
    }

    static void PrintSpecComparison(EngineSpec spec1, EngineSpec spec2)
    {
        Console.WriteLine("Spec Comparison:");
        Console.WriteLine();
        Console.WriteLine($"{"Parameter",-30} {"A",-20} {"B",-20} {"Diff",-15}");
        Console.WriteLine(new string('-', 85));

        PrintSpecField("Name", spec1.Name, spec2.Name);
        PrintSpecField("Thrust (N)", spec1.ThrustN.ToString(), spec2.ThrustN.ToString());
        PrintSpecField("Chamber Pressure (bar)", spec1.ChamberPressureBar.ToString(), spec2.ChamberPressureBar.ToString());
        PrintSpecField("Propellants", spec1.Propellants, spec2.Propellants);
        PrintSpecField("O/F Ratio", spec1.OfRatio.ToString(), spec2.OfRatio.ToString());
        PrintSpecField("Expansion Ratio", spec1.ExpansionRatio.ToString(), spec2.ExpansionRatio.ToString());
        PrintSpecField("Bell Fraction", spec1.BellFraction.ToString(), spec2.BellFraction.ToString());
        PrintSpecField("Contraction Ratio", spec1.ContractionRatio.ToString(), spec2.ContractionRatio.ToString());
        PrintSpecField("Wall Thickness (mm)", spec1.WallThicknessMM.ToString(), spec2.WallThicknessMM.ToString());
        PrintSpecField("Target Process", spec1.TargetProcess.ToString(), spec2.TargetProcess.ToString());
        PrintSpecField("Voxel Size (mm)", spec1.VoxelSizeMM.ToString(), spec2.VoxelSizeMM.ToString());
        PrintSpecField("Cooling Count", spec1.Cooling.Count.ToString(), spec2.Cooling.Count.ToString());
        PrintSpecField("Cooling Diameter (mm)", spec1.Cooling.DiameterMM.ToString(), spec2.Cooling.DiameterMM.ToString());
        PrintSpecField("Injector Type", spec1.Injector.Type.ToString(), spec2.Injector.Type.ToString());
        PrintSpecField("Injector Element Count", spec1.Injector.ElementCount.ToString(), spec2.Injector.ElementCount.ToString());

        Console.WriteLine();
    }

    static void PrintSpecField(string name, string val1, string val2)
    {
        string diff = val1 == val2 ? "=" : "≠";
        if (val1 != val2)
            diff = $"{val1} → {val2}";
        Console.WriteLine($"{name,-30} {val1,-20} {val2,-20} {diff,-15}");
    }
}

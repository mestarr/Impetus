namespace Impetus.Physics;

/// <summary>
/// Hardware closure loop: import test stand data and update gas tables or discharge coefficients.
/// </summary>
public static class TestStandData
{
    /// <summary>
    /// Test stand measurement data from hot-fire testing.
    /// </summary>
    public record TestMeasurement
    {
        public required DateTime Timestamp { get; init; }
        public required double ThrustN { get; init; }           // Measured thrust [N]
        public required double ChamberPressureBar { get; init; } // Chamber pressure [bar]
        public required double MassFlowFuel { get; init; }     // Fuel mass flow [kg/s]
        public required double MassFlowOx { get; init; }        // Oxidizer mass flow [kg/s]
        public required double AmbientPressureBar { get; init; } // Ambient pressure [bar]
        public double? IspMeasuredS { get; init; }              // Calculated Isp [s] (optional)
        public double? CStarMeasured { get; init; }            // Calculated C* [m/s] (optional)
        public double? DischargeCoefficient { get; init; }     // Measured discharge coefficient (optional)
    }

    /// <summary>
    /// Comparison between predicted and measured performance.
    /// </summary>
    public record PerformanceComparison
    {
        public required EngineDesign Predicted { get; init; }
        public required TestMeasurement Measured { get; init; }

        public double ThrustErrorPercent { get; init; }
        public double IspErrorPercent { get; init; }
        public double CStarErrorPercent { get; init; }
        public double MassFlowErrorPercent { get; init; }

        public double MeasuredIspS { get; init; }
        public double MeasuredCStar { get; init; }
        public double MeasuredOfRatio { get; init; }
    }

    /// <summary>
    /// Recommended correction factors based on test data.
    /// </summary>
    public record CorrectionFactors
    {
        public double DischargeCoefficientCorrection { get; init; } = 1.0;
        public double CStarCorrection { get; init; } = 1.0;
        public double ThrustCoefficientCorrection { get; init; } = 1.0;

        public string Summary { get; init; } = "";
    }

    /// <summary>
    /// Parse test stand data from CSV file.
    /// Expected columns: timestamp, thrustN, chamberPressureBar, massFlowFuel, massFlowOx, ambientPressureBar
    /// </summary>
    public static List<TestMeasurement> ParseCsv(string strPath)
    {
        if (!File.Exists(strPath))
            throw new FileNotFoundException($"Test stand CSV not found: {strPath}");

        List<TestMeasurement> aoMeasurements = [];
        string[] astrLines = File.ReadAllLines(strPath);

        // Skip header row
        for (int i = 1; i < astrLines.Length; i++)
        {
            string strLine = astrLines[i].Trim();
            if (string.IsNullOrWhiteSpace(strLine) || strLine.StartsWith('#'))
                continue;

            string[] astrFields = strLine.Split(',');
            if (astrFields.Length < 6)
                continue;

            try
            {
                var oMeasurement = new TestMeasurement
                {
                    Timestamp = DateTime.Parse(astrFields[0].Trim()),
                    ThrustN = double.Parse(astrFields[1].Trim()),
                    ChamberPressureBar = double.Parse(astrFields[2].Trim()),
                    MassFlowFuel = double.Parse(astrFields[3].Trim()),
                    MassFlowOx = double.Parse(astrFields[4].Trim()),
                    AmbientPressureBar = double.Parse(astrFields[5].Trim())
                };

                // Calculate derived values
                double fTotalMassFlow = oMeasurement.MassFlowFuel + oMeasurement.MassFlowOx;
                oMeasurement = oMeasurement with
                {
                    IspMeasuredS = oMeasurement.ThrustN / (fTotalMassFlow * CombustionGas.G0),
                    CStarMeasured = oMeasurement.ChamberPressureBar * 100000 * Math.PI * Math.Pow(0.01, 2) / fTotalMassFlow // Rough estimate
                };

                aoMeasurements.Add(oMeasurement);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Warning: Failed to parse line {i + 1}: {e.Message}");
            }
        }

        return aoMeasurements;
    }

    /// <summary>
    /// Compare predicted performance to measured test data.
    /// </summary>
    public static PerformanceComparison Compare(EngineDesign oPredicted, TestMeasurement oMeasured)
    {
        double fMeasuredIsp = oMeasured.IspMeasuredS ?? (oMeasured.ThrustN / ((oMeasured.MassFlowFuel + oMeasured.MassFlowOx) * CombustionGas.G0));
        double fMeasuredCStar = oMeasured.CStarMeasured ?? (oMeasured.ChamberPressureBar * 100000 * oPredicted.ThroatArea / (oMeasured.MassFlowFuel + oMeasured.MassFlowOx));
        double fMeasuredOfRatio = oMeasured.MassFlowOx / oMeasured.MassFlowFuel;

        return new PerformanceComparison
        {
            Predicted = oPredicted,
            Measured = oMeasured,
            ThrustErrorPercent = (oMeasured.ThrustN - oPredicted.Spec.ThrustN) / oPredicted.Spec.ThrustN * 100,
            IspErrorPercent = (fMeasuredIsp - oPredicted.IspSeaLevelS) / oPredicted.IspSeaLevelS * 100,
            CStarErrorPercent = (fMeasuredCStar - oPredicted.CStar) / oPredicted.CStar * 100,
            MassFlowErrorPercent = ((oMeasured.MassFlowFuel + oMeasured.MassFlowOx) - oPredicted.MassFlow) / oPredicted.MassFlow * 100,
            MeasuredIspS = fMeasuredIsp,
            MeasuredCStar = fMeasuredCStar,
            MeasuredOfRatio = fMeasuredOfRatio
        };
    }

    /// <summary>
    /// Calculate correction factors to align predictions with test data.
    /// </summary>
    public static CorrectionFactors CalculateCorrections(PerformanceComparison oComparison)
    {
        double fCdCorrection = 1.0;
        double fCStarCorrection = 1.0;
        double fCfCorrection = 1.0;

        List<string> astrCorrections = [];

        // C* correction based on combustion efficiency
        if (Math.Abs(oComparison.CStarErrorPercent) > 2)
        {
            fCStarCorrection = oComparison.MeasuredCStar / oComparison.Predicted.CStar;
            astrCorrections.Add($"C* correction: {fCStarCorrection:F3} ({oComparison.CStarErrorPercent:+F1}%)");
        }

        // Discharge coefficient correction based on mass flow error
        if (Math.Abs(oComparison.MassFlowErrorPercent) > 2)
        {
            fCdCorrection = 1.0 - oComparison.MassFlowErrorPercent / 100;
            astrCorrections.Add($"Cd correction: {fCdCorrection:F3} ({oComparison.MassFlowErrorPercent:+F1}%)");
        }

        // Thrust coefficient correction based on thrust error
        if (Math.Abs(oComparison.ThrustErrorPercent) > 2)
        {
            fCfCorrection = oComparison.Measured.ThrustN / oComparison.Predicted.Spec.ThrustN;
            astrCorrections.Add($"Cf correction: {fCfCorrection:F3} ({oComparison.ThrustErrorPercent:+F1}%)");
        }

        string strSummary = astrCorrections.Count > 0
            ? string.Join(", ", astrCorrections)
            : "No significant corrections needed (errors < 2%)";

        return new CorrectionFactors
        {
            DischargeCoefficientCorrection = fCdCorrection,
            CStarCorrection = fCStarCorrection,
            ThrustCoefficientCorrection = fCfCorrection,
            Summary = strSummary
        };
    }

    /// <summary>
    /// Apply correction factors to an engine spec for the next design iteration.
    /// </summary>
    public static EngineSpec ApplyCorrections(EngineSpec oSpec, CorrectionFactors oCorrections)
    {
        // For v1, we can't directly modify gas tables, but we can suggest spec adjustments
        // In a full implementation, this would update the gas table JSON files

        Console.WriteLine("Hardware closure corrections:");
        Console.WriteLine($"  {oCorrections.Summary}");
        Console.WriteLine();
        Console.WriteLine("To apply these corrections:");
        Console.WriteLine("  1. For C* correction: Update gas table Tc values or adjust chamber pressure");
        Console.WriteLine("  2. For Cd correction: Adjust injector orifice diameters");
        Console.WriteLine("  3. For Cf correction: Adjust expansion ratio or nozzle contour");

        return oSpec; // Return unchanged for v1 - corrections are informational only
    }

    /// <summary>
    /// Generate a sample test stand CSV template.
    /// </summary>
    public static void GenerateTemplate(string strPath)
    {
        string strCsv = @"# Test Stand Data Template
# Columns: timestamp, thrustN, chamberPressureBar, massFlowFuel, massFlowOx, ambientPressureBar
# timestamp: ISO 8601 format (e.g., 2024-01-15T10:30:00)
# thrustN: Measured thrust in Newtons
# chamberPressureBar: Chamber pressure in bar
# massFlowFuel: Fuel mass flow in kg/s
# massFlowOx: Oxidizer mass flow in kg/s
# ambientPressureBar: Ambient pressure in bar
timestamp,thrustN,chamberPressureBar,massFlowFuel,massFlowOx,ambientPressureBar
2024-01-15T10:30:00,1050,20.5,0.15,0.35,1.013
2024-01-15T10:31:00,1048,20.4,0.149,0.351,1.013
2024-01-15T10:32:00,1052,20.6,0.151,0.349,1.013
";

        File.WriteAllText(strPath, strCsv);
        Console.WriteLine($"Test stand template written to: {strPath}");
    }
}

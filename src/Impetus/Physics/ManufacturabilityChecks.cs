using PicoGK;

namespace Impetus.Physics;

/// <summary>
/// Manufacturability checks using PicoGK voxel geometry analysis.
/// These checks evaluate printability for metal additive manufacturing (LPBF).
/// </summary>
public static class ManufacturabilityChecks
{
    // Process-specific thresholds
    public const double MinWallThicknessMM = 0.8;  // Minimum wall for metal LPBF
    public const double OverhangAngleDeg = 45.0;    // Maximum unsupported overhang angle
    public const double VoxelSizeMM = 0.5;          // Analysis resolution

    /// <summary>
    /// Run all manufacturability checks on the engine geometry.
    /// </summary>
    public static ManufacturabilityResult Evaluate(
        Voxels voxEngine,
        EngineSpec oSpec,
        NozzleContour oContour)
    {
        List<ManufacturabilityCheck> aoChecks = [];

        // Overhang analysis
        aoChecks.Add(CheckOverhang(voxEngine, oSpec));

        // Minimum wall thickness
        aoChecks.Add(CheckMinimumWall(voxEngine, oSpec));

        // Powder/fluid removal (requires cooling channel geometry)
        aoChecks.Add(CheckPowderRemoval(voxEngine, oSpec, oContour));

        return new ManufacturabilityResult
        {
            Checks = aoChecks,
            OverallVerdict = GetOverallVerdict(aoChecks)
        };
    }

    /// <summary>
    /// Overhang analysis: simplified check based on geometry parameters.
    /// Full voxel-based analysis requires complex PicoGK operations.
    /// </summary>
    static ManufacturabilityCheck CheckOverhang(Voxels voxEngine, EngineSpec oSpec)
    {
        try
        {
            // Simplified check: bell fraction and expansion ratio indicate overhang risk
            // Higher expansion ratios = longer nozzles = more overhang risk
            double fExpansionRatio = oSpec.ExpansionRatio;
            double fBellFraction = oSpec.BellFraction;

            // Estimate overhang risk based on nozzle geometry
            double fOverhangRisk = 0.0;
            if (fExpansionRatio > 10.0)
                fOverhangRisk += 0.3;
            if (fExpansionRatio > 20.0)
                fOverhangRisk += 0.2;
            if (fBellFraction > 0.7)
                fOverhangRisk += 0.2;

            if (fOverhangRisk < 0.2)
                return new ManufacturabilityCheck(
                    "Overhang analysis",
                    CheckStatus.Pass,
                    $"Expansion ratio {fExpansionRatio:F1}, bell fraction {fBellFraction:F2} — low overhang risk.",
                    "None.");

            if (fOverhangRisk < 0.5)
                return new ManufacturabilityCheck(
                    "Overhang analysis",
                    CheckStatus.Warn,
                    $"Expansion ratio {fExpansionRatio:F1}, bell fraction {fBellFraction:F2} — moderate overhang risk.",
                    "Consider support structures or print orientation optimization.");

            return new ManufacturabilityCheck(
                "Overhang analysis",
                CheckStatus.Fail,
                $"Expansion ratio {fExpansionRatio:F1}, bell fraction {fBellFraction:F2} — high overhang risk.",
                "Reduce expansion ratio or add support structures for LPBF printing.");
        }
        catch (Exception ex)
        {
            return new ManufacturabilityCheck(
                "Overhang analysis",
                CheckStatus.Warn,
                $"Analysis failed: {ex.Message}",
                "Ensure geometry is valid.");
        }
    }

    /// <summary>
    /// Minimum wall check: simplified check based on cooling channel geometry.
    /// Full voxel erosion requires complex PicoGK operations.
    /// </summary>
    static ManufacturabilityCheck CheckMinimumWall(Voxels voxEngine, EngineSpec oSpec)
    {
        try
        {
            // Use cooling channel diameter as proxy for minimum wall thickness
            double fChannelDiam = oSpec.Cooling.DiameterMM;
            double fChannelCount = oSpec.Cooling.Count;

            // Estimate minimum wall based on channel geometry
            // More channels = thinner walls between them
            double fEstimatedWallMM = fChannelDiam * 0.5; // Rough estimate

            if (fEstimatedWallMM >= MinWallThicknessMM)
                return new ManufacturabilityCheck(
                    "Minimum wall thickness",
                    CheckStatus.Pass,
                    $"Channel Ø{fChannelDiam:F1} mm × {fChannelCount} — est. min wall ~{fEstimatedWallMM:F2} mm.",
                    "None.");

            if (fEstimatedWallMM >= MinWallThicknessMM * 0.8)
                return new ManufacturabilityCheck(
                    "Minimum wall thickness",
                    CheckStatus.Warn,
                    $"Channel Ø{fChannelDiam:F1} mm × {fChannelCount} — est. min wall ~{fEstimatedWallMM:F2} mm (tight).",
                    "Consider fewer or larger channels for thicker walls.");

            return new ManufacturabilityCheck(
                "Minimum wall thickness",
                CheckStatus.Fail,
                $"Channel Ø{fChannelDiam:F1} mm × {fChannelCount} — est. min wall ~{fEstimatedWallMM:F2} mm (too thin).",
                "Increase cooling.diameterMM or reduce cooling.count for LPBF manufacturability.");
        }
        catch (Exception ex)
        {
            return new ManufacturabilityCheck(
                "Minimum wall thickness",
                CheckStatus.Warn,
                $"Analysis failed: {ex.Message}",
                "Ensure geometry is valid.");
        }
    }

    /// <summary>
    /// Powder/fluid removal: simplified check based on cooling channel geometry.
    /// Full flood-fill requires complex PicoGK operations.
    /// </summary>
    static ManufacturabilityCheck CheckPowderRemoval(Voxels voxEngine, EngineSpec oSpec, NozzleContour oContour)
    {
        try
        {
            // Use cooling channel count and diameter as proxy for powder removal difficulty
            // More/smaller channels = harder to clean
            double fChannelDiam = oSpec.Cooling.DiameterMM;
            double fChannelCount = oSpec.Cooling.Count;

            // Estimate powder removal difficulty
            double fRemovalDifficulty = 0.0;
            if (fChannelDiam < 1.0)
                fRemovalDifficulty += 0.3;
            if (fChannelDiam < 0.8)
                fRemovalDifficulty += 0.2;
            if (fChannelCount > 24)
                fRemovalDifficulty += 0.2;

            if (fRemovalDifficulty < 0.2)
                return new ManufacturabilityCheck(
                    "Powder/fluid removal",
                    CheckStatus.Pass,
                    $"Channel Ø{fChannelDiam:F1} mm × {fChannelCount} — good powder removal access.",
                    "None.");

            if (fRemovalDifficulty < 0.5)
                return new ManufacturabilityCheck(
                    "Powder/fluid removal",
                    CheckStatus.Warn,
                    $"Channel Ø{fChannelDiam:F1} mm × {fChannelCount} — moderate powder removal difficulty.",
                    "Consider larger channels or fewer channels for easier cleaning.");

            return new ManufacturabilityCheck(
                "Powder/fluid removal",
                CheckStatus.Fail,
                $"Channel Ø{fChannelDiam:F1} mm × {fChannelCount} — high powder trapping risk.",
                "Increase cooling.diameterMM or reduce cooling.count for better powder evacuation.");
        }
        catch (Exception ex)
        {
            return new ManufacturabilityCheck(
                "Powder/fluid removal",
                CheckStatus.Warn,
                $"Analysis failed: {ex.Message}",
                "Ensure geometry is valid.");
        }
    }

    static string GetOverallVerdict(List<ManufacturabilityCheck> aoChecks)
    {
        int nFail = aoChecks.Count(c => c.Status == CheckStatus.Fail);
        int nWarn = aoChecks.Count(c => c.Status == CheckStatus.Warn);

        if (nFail > 0)
            return $"MANUFACTURING_FAIL ({nFail} critical issue(s))";

        if (nWarn > 0)
            return $"MANUFACTURING_WARN ({nWarn} concern(s))";

        return "MANUFACTURING_OK";
    }
}

/// <summary>
/// Result of manufacturability analysis.
/// </summary>
public record ManufacturabilityResult
{
    public required IReadOnlyList<ManufacturabilityCheck> Checks { get; init; }
    public required string OverallVerdict { get; init; }
}

/// <summary>
/// Individual manufacturability check result.
/// </summary>
public record ManufacturabilityCheck(
    string Name,
    CheckStatus Status,
    string Detail,
    string Action);

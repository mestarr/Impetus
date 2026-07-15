namespace Impetus.Physics;

/// <summary>
/// Process-aware geometry configuration and validation.
/// Auto-sets voxel size and provides warnings based on manufacturing process.
/// </summary>
public static class ProcessGeometry
{
    // Process-specific voxel size recommendations
    private const double FDM_VoxelSizeMM = 1.0;      // Coarser voxels OK for FDM
    private const double LPBF_VoxelSizeMM = 0.4;     // Finer voxels needed for metal LPBF

    // Process-specific minimum feature sizes
    private const double FDM_MinChannelMM = 2.0;     // FDM can handle larger channels
    private const double LPBF_MinChannelMM = 0.8;    // LPBF minimum for metal

    private const double FDM_MinWallMM = 1.5;         // FDM minimum wall thickness
    private const double LPBF_MinWallMM = 0.8;        // LPBF minimum wall thickness

    /// <summary>
    /// Auto-set voxel size based on target process if not explicitly specified.
    /// </summary>
    public static EngineSpec ApplyProcessDefaults(EngineSpec oSpec)
    {
        // If voxel size is at default (0.4), auto-set based on process
        if (oSpec.VoxelSizeMM == 0.4)
        {
            double fRecommendedVoxelSize = oSpec.TargetProcess == ManufacturingProcess.FDM
                ? FDM_VoxelSizeMM
                : LPBF_VoxelSizeMM;

            return oSpec with { VoxelSizeMM = fRecommendedVoxelSize };
        }

        return oSpec;
    }

    /// <summary>
    /// Get process-specific warnings for the current spec.
    /// </summary>
    public static List<string> GetProcessWarnings(EngineSpec oSpec)
    {
        List<string> aoWarnings = [];

        if (oSpec.TargetProcess == ManufacturingProcess.FDM)
        {
            // FDM-specific warnings
            if (oSpec.Cooling.DiameterMM < FDM_MinChannelMM)
                aoWarnings.Add($"FDM: Channel Ø{oSpec.Cooling.DiameterMM:F1} mm is below recommended {FDM_MinChannelMM} mm for FDM.");

            if (oSpec.WallThicknessMM < FDM_MinWallMM)
                aoWarnings.Add($"FDM: Wall thickness {oSpec.WallThicknessMM:F1} mm is below recommended {FDM_MinWallMM} mm for FDM.");

            aoWarnings.Add("FDM: Geometry is for display only - not suitable for hot-fire testing.");
        }
        else if (oSpec.TargetProcess == ManufacturingProcess.LPBF)
        {
            // LPBF-specific warnings
            if (oSpec.Cooling.DiameterMM < LPBF_MinChannelMM)
                aoWarnings.Add($"LPBF: Channel Ø{oSpec.Cooling.DiameterMM:F1} mm is below minimum {LPBF_MinChannelMM} mm for metal LPBF.");

            if (oSpec.WallThicknessMM < LPBF_MinWallMM)
                aoWarnings.Add($"LPBF: Wall thickness {oSpec.WallThicknessMM:F1} mm is below minimum {LPBF_MinWallMM} mm for metal LPBF.");

            if (oSpec.Cooling.Count < 12)
                aoWarnings.Add($"LPBF: Cooling channel count {oSpec.Cooling.Count} is low - consider more channels for better regen cooling.");
        }

        // Voxel size warnings
        double fRecommendedVoxelSize = oSpec.TargetProcess == ManufacturingProcess.FDM
            ? FDM_VoxelSizeMM
            : LPBF_VoxelSizeMM;

        if (Math.Abs(oSpec.VoxelSizeMM - fRecommendedVoxelSize) > 0.2)
        {
            aoWarnings.Add($"Voxel size {oSpec.VoxelSizeMM:F2} mm differs from recommended {fRecommendedVoxelSize:F2} mm for {oSpec.TargetProcess}.");
        }

        return aoWarnings;
    }

    /// <summary>
    /// Check if spec meets process requirements.
    /// </summary>
    public static (bool MeetsRequirements, List<string> Failures) ValidateProcessRequirements(EngineSpec oSpec)
    {
        List<string> aoFailures = [];

        if (oSpec.TargetProcess == ManufacturingProcess.LPBF)
        {
            if (oSpec.Cooling.DiameterMM < LPBF_MinChannelMM)
                aoFailures.Add($"Channel Ø{oSpec.Cooling.DiameterMM:F1} mm below minimum {LPBF_MinChannelMM} mm for LPBF.");

            if (oSpec.WallThicknessMM < LPBF_MinWallMM)
                aoFailures.Add($"Wall thickness {oSpec.WallThicknessMM:F1} mm below minimum {LPBF_MinWallMM} mm for LPBF.");
        }

        return (aoFailures.Count == 0, aoFailures);
    }

    /// <summary>
    /// Get process-specific minimum channel diameter.
    /// </summary>
    public static double GetMinChannelDiameter(ManufacturingProcess eProcess)
    {
        return eProcess == ManufacturingProcess.FDM ? FDM_MinChannelMM : LPBF_MinChannelMM;
    }

    /// <summary>
    /// Get process-specific minimum wall thickness.
    /// </summary>
    public static double GetMinWallThickness(ManufacturingProcess eProcess)
    {
        return eProcess == ManufacturingProcess.FDM ? FDM_MinWallMM : LPBF_MinWallMM;
    }

    /// <summary>
    /// Get recommended voxel size for a process.
    /// </summary>
    public static double GetRecommendedVoxelSize(ManufacturingProcess eProcess)
    {
        return eProcess == ManufacturingProcess.FDM ? FDM_VoxelSizeMM : LPBF_VoxelSizeMM;
    }
}

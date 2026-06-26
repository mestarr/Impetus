using System.Globalization;
using System.Text;
using Impetus.Physics;

namespace Impetus.Reporting;

/// <summary>FDM / slicer guidance appended to design reports.</summary>
public static class PrintReport
{
    public const double DefaultBedMM = 250.0;
    public const double DefaultFdmNozzleMM = 0.4;

    public static string BuildSection(EngineDesign o, EngineSpec s)
    {
        PrintEnvelope oEnv = EstimateEnvelope(o);
        double fBed = s.PrinterBedMM > 0 ? s.PrinterBedMM : DefaultBedMM;
        bool bFits = oEnv.HeightMM <= fBed && oEnv.MaxDiameterMM <= fBed;
        double fMarginZ = fBed - oEnv.HeightMM;
        double fMarginXY = fBed - oEnv.MaxDiameterMM;

        var sb = new StringBuilder();
        sb.AppendLine("## FDM display print (reference)");
        sb.AppendLine();
        sb.AppendLine(
            "**Display model only** — FDM plastic is not for hot fire. Flight-intent " +
            "geometry is metal powder-bed fusion (CuCrZr / Inconel).");
        sb.AppendLine();
        sb.AppendLine("### Envelope");
        sb.AppendLine();
        sb.AppendLine("| Quantity | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Overall height (approx.) | {F(oEnv.HeightMM, "F0")} mm |");
        sb.AppendLine($"| Max diameter (approx.) | {F(oEnv.MaxDiameterMM, "F1")} mm |");
        sb.AppendLine($"| Flange diameter (approx.) | {F(oEnv.FlangeDiameterMM, "F1")} mm |");
        sb.AppendLine($"| Bed fit ({F(fBed, "F0")} mm square) | {(bFits ? "yes" : "**no**")} " +
                      $"(margin Z {F(fMarginZ, "F0")} mm, XY {F(fMarginXY, "F0")} mm) |");
        sb.AppendLine();
        sb.AppendLine("### Files");
        sb.AppendLine();
        sb.AppendLine("- **Anycubic Kobra S1 / Slicer Next:** prefer `engine.3mf` (explicit mm units).");
        sb.AppendLine("- `engine.3mf` has **three objects** — `body`, `injector`, `flange` — for per-part slicer control.");
        sb.AppendLine("- Universal fallback: `engine.stl` (same geometry, millimeters).");
        sb.AppendLine("- Inspection: `engine_cutaway.3mf` or `engine_cutaway.stl`.");
        sb.AppendLine();
        sb.AppendLine("### Slicer recipe (starting point)");
        sb.AppendLine();
        sb.AppendLine("1. Import `engine.3mf` — **do not scale**.");
        sb.AppendLine("2. Orientation: stand on the **flat injector flange**, nozzle up.");
        sb.AppendLine("3. Add a **brim** (tall, slender part).");
        sb.AppendLine("4. Supports: usually **none** for the full engine; cutaway may need tree supports under the bell.");
        sb.AppendLine("5. Settings: **0.2 mm** layers, **2–3** perimeters, **10–15%** infill, PLA or PETG.");
        sb.AppendLine();
        sb.AppendLine("### Resolution notes");
        sb.AppendLine();

        double fChan = s.Cooling.DiameterMM;
        double fMinOrifice = Math.Min(o.FuelOrificeDiameter, o.OxOrificeDiameter) * 1000.0;
        if (fChan < DefaultFdmNozzleMM || fMinOrifice < DefaultFdmNozzleMM)
        {
            sb.AppendLine(
                $"- Cooling channels (Ø{F(fChan, "F1")} mm) and injector orifices " +
                $"(down to Ø{F(fMinOrifice, "F2")} mm) are sized for **metal AM** — FDM will only partially resolve them.");
            if (s.VoxelSizeMM > 0.25)
                sb.AppendLine("- For a crisper desk model, set `voxelSizeMM` to **0.2** and re-run `design` (slower).");
        }
        else
        {
            sb.AppendLine("- Feature sizes are coarse enough for typical FDM nozzles at this scale.");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    public static void PrintConsoleSummary(EngineDesign o, EngineSpec s)
    {
        PrintEnvelope oEnv = EstimateEnvelope(o);
        double fBed = s.PrinterBedMM > 0 ? s.PrinterBedMM : DefaultBedMM;
        bool bFits = oEnv.HeightMM <= fBed && oEnv.MaxDiameterMM <= fBed;
        Console.WriteLine();
        Console.WriteLine("  == FDM print ==");
        Console.WriteLine($"  Envelope ~{oEnv.HeightMM:F0} mm tall x Ø{oEnv.MaxDiameterMM:F0} mm");
        Console.WriteLine($"  Bed {fBed:F0} mm: {(bFits ? "fits" : "TOO LARGE")} — use engine.3mf, flange down, add brim");
        Console.WriteLine("  Display model only — not for hot fire.");
        Console.WriteLine();
    }

    public static PrintEnvelope EstimateEnvelope(EngineDesign o)
    {
        EngineSpec s = o.Spec;
        double fWall = Math.Max(s.WallThicknessMM, s.Cooling.DiameterMM + 1.6);
        double fRcMM = o.ChamberRadius * 1000.0;
        double fPlateT = Math.Max(6.0, fWall * 2.0);
        double fFlangeD = 2.0 * (fRcMM + fWall + 8.0);
        double fExitD = o.ExitRadius * 2000.0 + 2.0 * fWall;
        double fHeight = (o.ChamberCylinderLength + o.BellLength) * 1000.0 + fPlateT + 6.0;

        return new PrintEnvelope(
            fHeight,
            Math.Max(fExitD, fFlangeD),
            fFlangeD);
    }

    public readonly record struct PrintEnvelope(
        double HeightMM,
        double MaxDiameterMM,
        double FlangeDiameterMM);

    static string F(double f, string strFmt) => f.ToString(strFmt, CultureInfo.InvariantCulture);
}

using System.Diagnostics;
using System.Globalization;
using Impetus.Physics;

namespace Impetus.Cfd;

public record CfdResult
{
    public required bool Converged { get; init; }
    public required double ThrustN { get; init; }
    public required double MassFlow { get; init; }
    public required double ExitMachAvg { get; init; }
    public required double ExitPressureAvg { get; init; }
    public required int Iterations { get; init; }

    /// <summary>Area-averaged wall heat flux near the throat from RANS [MW/m²], if available.</summary>
    public double? ThroatWallHeatFluxMWm2 { get; init; }
}

/// <summary>
/// Runs SU2_CFD on a generated case and post-processes the exit-plane surface
/// data into integral quantities (thrust, mass flow) that can be compared
/// directly against the analytic design prediction.
/// </summary>
public static class Su2Runner
{
    /// <summary>Locate SU2_CFD.exe under &lt;repo&gt;/tools, or via IMPETUS_SU2 env var.</summary>
    public static string? FindSu2()
    {
        string? strEnv = Environment.GetEnvironmentVariable("IMPETUS_SU2");
        if (strEnv is not null && File.Exists(strEnv))
            return strEnv;

        string? strDir = AppContext.BaseDirectory;
        while (strDir is not null)
        {
            string strTools = Path.Combine(strDir, "tools");
            if (Directory.Exists(strTools))
            {
                string? strExe = Directory
                    .EnumerateFiles(strTools, "SU2_CFD.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (strExe is not null)
                    return strExe;
            }
            strDir = Path.GetDirectoryName(strDir);
        }
        return null;
    }

    public static int Run(string strSu2Exe, string strCaseDir, TextWriter oLog)
    {
        ProcessStartInfo oInfo = new()
        {
            FileName = strSu2Exe,
            Arguments = Su2Case.ConfigFile,
            WorkingDirectory = strCaseDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        int nThreads = Math.Max(1, Environment.ProcessorCount - 1);
        oInfo.EnvironmentVariables["OMP_NUM_THREADS"] = nThreads.ToString();

        using StreamWriter oFileLog = new(Path.Combine(strCaseDir, "su2.log")) { AutoFlush = true };
        using Process oProc = new() { StartInfo = oInfo };

        int nIterSeen = 0;
        oProc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            oFileLog.WriteLine(e.Data);

            // Echo a heartbeat: SU2 iteration rows look like "|  1200| -4.21|..."
            string strTrim = e.Data.TrimStart();
            if (strTrim.StartsWith('|'))
            {
                string strBody = strTrim.TrimStart('|', ' ');
                if (strBody.Length > 0 && char.IsDigit(strBody[0]))
                {
                    nIterSeen++;
                    if (nIterSeen % 5 == 1)
                        oLog.WriteLine($"   SU2 {e.Data.Trim()}");
                }
            }
        };
        oProc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) oFileLog.WriteLine("[err] " + e.Data);
        };

        oProc.Start();
        oProc.BeginOutputReadLine();
        oProc.BeginErrorReadLine();

        if (!oProc.WaitForExit(TimeSpan.FromMinutes(45)))
        {
            oProc.Kill(entireProcessTree: true);
            oLog.WriteLine("   SU2 timed out after 45 min - case kept for inspection.");
            return -1;
        }
        return oProc.ExitCode;
    }

    /// <summary>
    /// Integrate thrust and mass flow over the exit plane from SU2's surface CSV.
    /// F = Integral over exit area of [rho u^2 + (p - pa)] dA,  dA = 2 pi r dr.
    /// </summary>
    public static CfdResult PostProcess(string strCaseDir, EngineDesign oDesign)
    {
        string strCsv = Path.Combine(strCaseDir, "surface_flow.csv");
        if (!File.Exists(strCsv))
            throw new FileNotFoundException(
                "SU2 produced no surface CSV - check su2.log", strCsv);

        string[] astrLines = File.ReadAllLines(strCsv);
        string[] astrHeader = SplitCsv(astrLines[0]);

        // SU2's surface CSV carries the conservative variables; primitives
        // (p, M) are derived below via the ideal-gas relations.
        int iX = Col(astrHeader, "x");
        int iY = Col(astrHeader, "y");
        int iRho = Col(astrHeader, "Density");
        int iMomX = Col(astrHeader, "Momentum_x");
        int iMomY = Col(astrHeader, "Momentum_y");
        int iE = Col(astrHeader, "Energy");
        int iHeatFlux = TryCol(astrHeader, "Heat_Flux", "HEAT_FLUX", "HeatFlux");

        double fGamma = oDesign.Gas.Gamma;
        NozzleContour oContour = new(oDesign);
        double fExitZ = oContour.ExitZ;
        double fExitR = oContour.RadiusAt(fExitZ);
        double fThroatZ = oContour.ThroatZ;
        double fThroatBand = 0.25 * oDesign.ThroatRadius;

        // Rows: outlet [r, rho, rho*u, p, M] or wall [x, y, heat flux, ...]
        List<double[]> aoOutlet = [];
        List<double> afThroatFlux = [];
        for (int i = 1; i < astrLines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(astrLines[i])) continue;
            string[] astr = SplitCsv(astrLines[i]);

            double fX = D(astr[iX]);
            double fY = D(astr[iY]);
            double fRWall = oContour.RadiusAt(fX);
            bool bOnWall = Math.Abs(fY - fRWall) <= 0.03 * Math.Max(fRWall, 1e-6);
            bool bOnOutlet = fX >= fExitZ * 0.995 && fY <= fExitR * 1.02;

            if (bOnWall && iHeatFlux >= 0 && Math.Abs(fX - fThroatZ) <= fThroatBand)
                afThroatFlux.Add(Math.Abs(D(astr[iHeatFlux])));

            if (!bOnOutlet || bOnWall)
                continue;

            double fRho = D(astr[iRho]);
            double fMomX = D(astr[iMomX]);
            double fMomY = D(astr[iMomY]);
            double fRhoE = D(astr[iE]);

            double fKin = 0.5 * (fMomX * fMomX + fMomY * fMomY) / fRho;
            double fP = (fGamma - 1.0) * (fRhoE - fKin);
            double fA = Math.Sqrt(fGamma * fP / fRho);
            double fM = Math.Sqrt(fMomX * fMomX + fMomY * fMomY) / fRho / fA;

            aoOutlet.Add([fY, fRho, fMomX, fP, fM]);
        }
        aoOutlet.Sort((a, b) => a[0].CompareTo(b[0]));

        double fPa = oDesign.Spec.Pa;
        double fThrust = 0, fMdot = 0, fMachSum = 0, fPSum = 0, fArea = 0;

        for (int i = 1; i < aoOutlet.Count; i++)
        {
            double[] a = aoOutlet[i - 1];
            double[] b = aoOutlet[i];
            double fDr = b[0] - a[0];
            if (fDr <= 0) continue;
            double fRMid = 0.5 * (a[0] + b[0]);
            double fDA = 2.0 * Math.PI * fRMid * fDr;

            fThrust += 0.5 * (MomentumFlux(a, fPa) + MomentumFlux(b, fPa)) * fDA;
            fMdot += 0.5 * (a[2] + b[2]) * fDA;
            fMachSum += 0.5 * (a[4] + b[4]) * fDA;
            fPSum += 0.5 * (a[3] + b[3]) * fDA;
            fArea += fDA;
        }

        bool bConverged = ReadConvergence(strCaseDir, out int nIter);
        double? fThroatFlux = afThroatFlux.Count > 0
            ? afThroatFlux.Average() / 1e6
            : null;

        return new CfdResult
        {
            Converged = bConverged,
            ThrustN = fThrust,
            MassFlow = fMdot,
            ExitMachAvg = fMachSum / Math.Max(fArea, 1e-12),
            ExitPressureAvg = fPSum / Math.Max(fArea, 1e-12),
            Iterations = nIter,
            ThroatWallHeatFluxMWm2 = fThroatFlux
        };

        static double MomentumFlux(double[] afRow, double fPa)
        {
            double fRho = afRow[1];
            double fU = afRow[2] / fRho;       // Momentum_x / Density
            double fP = afRow[3];
            return fRho * fU * fU + (fP - fPa);
        }
    }

    static bool ReadConvergence(string strCaseDir, out int nIter)
    {
        nIter = 0;
        string strLog = Path.Combine(strCaseDir, "su2.log");
        if (!File.Exists(strLog)) return false;

        bool bConv = false;
        foreach (string strLine in File.ReadLines(strLog))
        {
            // Iteration rows: "|  1200|  -4.21| ..." - first cell is the iteration
            string strTrim = strLine.TrimStart();
            if (strTrim.StartsWith('|'))
            {
                string strFirst = strTrim.Split('|', StringSplitOptions.RemoveEmptyEntries
                                                   | StringSplitOptions.TrimEntries)
                                         .FirstOrDefault() ?? "";
                if (int.TryParse(strFirst, out int n)) nIter = n;
            }
            if (strLine.Contains("All convergence criteria satisfied")
                || strLine.Contains("Convergence criteria satisfied"))
                bConv = true;
        }
        return bConv;
    }

    static string[] SplitCsv(string strLine)
        => strLine.Split(',', StringSplitOptions.TrimEntries);

    static int Col(string[] astrHeader, string strName)
    {
        int i = TryCol(astrHeader, strName);
        if (i < 0)
            throw new InvalidDataException($"Column '{strName}' not in SU2 surface CSV");
        return i;
    }

    static int TryCol(string[] astrHeader, params string[] astrNames)
    {
        for (int i = 0; i < astrHeader.Length; i++)
        {
            string h = astrHeader[i].Trim('"', ' ');
            foreach (string strName in astrNames)
            {
                if (h.Equals(strName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    static int TryCol(string[] astrHeader, string strName)
        => TryCol(astrHeader, [strName]);

    static double D(string str) => double.Parse(str.Trim('"'), CultureInfo.InvariantCulture);
}

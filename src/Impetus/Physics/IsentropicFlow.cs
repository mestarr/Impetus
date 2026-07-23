namespace Impetus.Physics;

/// <summary>
/// Classic quasi-1D isentropic nozzle flow relations (Sutton ch. 3).
/// All pressures/temperatures are stagnation-referenced unless noted.
/// </summary>
public static class IsentropicFlow
{
    /// <summary>A/A* as a function of Mach number.</summary>
    public static double AreaRatio(double fM, double fGamma)
    {
        double g = fGamma;
        double f = (2.0 / (g + 1.0)) * (1.0 + 0.5 * (g - 1.0) * fM * fM);
        return Math.Pow(f, (g + 1.0) / (2.0 * (g - 1.0))) / fM;
    }

    /// <summary>Static-to-stagnation pressure ratio p/p0 at Mach M.</summary>
    public static double PressureRatio(double fM, double fGamma)
        => Math.Pow(1.0 + 0.5 * (fGamma - 1.0) * fM * fM, -fGamma / (fGamma - 1.0));

    /// <summary>Static-to-stagnation temperature ratio T/T0 at Mach M.</summary>
    public static double TemperatureRatio(double fM, double fGamma)
        => 1.0 / (1.0 + 0.5 * (fGamma - 1.0) * fM * fM);

    /// <summary>Mach number from pressure ratio p0/p (&gt;= 1).</summary>
    public static double MachFromPressureRatio(double fP0OverP, double fGamma)
    {
        double g = fGamma;
        return Math.Sqrt((Math.Pow(fP0OverP, (g - 1.0) / g) - 1.0) * 2.0 / (g - 1.0));
    }

    /// <summary>
    /// Invert the area-Mach relation by bisection on the requested branch.
    /// </summary>
    public static double MachFromAreaRatio(double fAreaRatio, double fGamma, bool bSupersonic)
    {
        if (fAreaRatio < 1.0)
            throw new ArgumentOutOfRangeException(nameof(fAreaRatio), "A/A* must be >= 1");

        double fLo = bSupersonic ? 1.0 + 1e-9 : 1e-6;
        double fHi = bSupersonic ? 100.0 : 1.0 - 1e-9;

        for (int i = 0; i < 200; i++)
        {
            double fMid = 0.5 * (fLo + fHi);
            bool bTooBig = AreaRatio(fMid, fGamma) > fAreaRatio;

            // Area ratio decreases with M on the subsonic branch, increases on the supersonic branch
            if (bTooBig == bSupersonic) fHi = fMid;
            else fLo = fMid;
        }
        return 0.5 * (fLo + fHi);
    }

    /// <summary>Characteristic velocity c* [m/s].</summary>
    public static double CStar(double fGamma, double fRs, double fTc)
    {
        double g = fGamma;
        double fGammaFunc = g * Math.Pow(2.0 / (g + 1.0), (g + 1.0) / (2.0 * (g - 1.0)));
        return Math.Sqrt(g * fRs * fTc) / fGammaFunc;
    }

    /// <summary>
    /// Thrust coefficient CF for given expansion ratio, exit and ambient pressure.
    /// F = CF * Pc * At.
    /// </summary>
    public static double ThrustCoefficient(double fGamma, double fEps, double fPe, double fPc, double fPa)
    {
        double g = fGamma;
        double fMomTerm = Math.Sqrt(
            (2.0 * g * g / (g - 1.0))
            * Math.Pow(2.0 / (g + 1.0), (g + 1.0) / (g - 1.0))
            * (1.0 - Math.Pow(fPe / fPc, (g - 1.0) / g)));
        return fMomTerm + fEps * (fPe - fPa) / fPc;
    }

    /// <summary>Ideal exhaust velocity [m/s] for expansion from Pc to Pe.</summary>
    public static double ExhaustVelocity(double fGamma, double fRs, double fTc, double fPe, double fPc)
    {
        double g = fGamma;
        return Math.Sqrt(2.0 * g / (g - 1.0) * fRs * fTc
                         * (1.0 - Math.Pow(fPe / fPc, (g - 1.0) / g)));
    }

    /// <summary>
    /// Equilibrium expansion: integrate varying gamma along the nozzle.
    /// Simplified approach: use average gamma between chamber and exit conditions.
    /// For frozen flow, this reduces to the standard constant-gamma formula.
    /// </summary>
    public static double ExhaustVelocityEquilibrium(CombustionGas oGas, double fPe, double fPc)
    {
        if (!oGas.EquilibriumExpansion)
            return ExhaustVelocity(oGas.Gamma, oGas.Rs, oGas.Tc, fPe, fPc);

        // Estimate exit Mach number using chamber gamma
        double fMe = MachFromPressureRatio(fPc / fPe, oGas.Gamma);

        // Use local gamma at exit for more accurate expansion
        double fGammaExit = oGas.LocalGamma(fMe);

        // Average gamma for expansion (simplified)
        double fGammaAvg = 0.5 * (oGas.Gamma + fGammaExit);

        return ExhaustVelocity(fGammaAvg, oGas.Rs, oGas.Tc, fPe, fPc);
    }

    /// <summary>
    /// Thrust coefficient with equilibrium expansion support.
    /// </summary>
    public static double ThrustCoefficientEquilibrium(CombustionGas oGas, double fEps, double fPe, double fPc, double fPa)
    {
        if (!oGas.EquilibriumExpansion)
            return ThrustCoefficient(oGas.Gamma, fEps, fPe, fPc, fPa);

        // Estimate exit Mach
        double fMe = MachFromPressureRatio(fPc / fPe, oGas.Gamma);
        double fGammaExit = oGas.LocalGamma(fMe);
        double fGammaAvg = 0.5 * (oGas.Gamma + fGammaExit);

        return ThrustCoefficient(fGammaAvg, fEps, fPe, fPc, fPa);
    }
}

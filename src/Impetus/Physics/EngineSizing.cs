namespace Impetus.Physics;

/// <summary>
/// Everything derived from the spec: dimensions [m], mass flows [kg/s], performance.
/// This is the "design state" that geometry generation and CFD consume.
/// </summary>
public record EngineDesign
{
    public required EngineSpec Spec { get; init; }
    public required CombustionGas Gas { get; init; }

    // Performance
    public required double ExpansionRatio { get; init; }
    public required double ExitMach { get; init; }
    public required double ExitPressure { get; init; }      // [Pa]
    public required double CStar { get; init; }             // [m/s]
    public required double ThrustCoeff { get; init; }       // at spec ambient
    public required double ThrustCoeffVac { get; init; }
    public required double MassFlow { get; init; }          // [kg/s] total
    public required double MassFlowFuel { get; init; }
    public required double MassFlowOx { get; init; }
    public required double IspSeaLevelS { get; init; }      // at spec ambient, [s]
    public required double IspVacuumS { get; init; }
    public required double ExhaustVelocity { get; init; }   // [m/s]

    // Geometry [m]
    public required double ThroatRadius { get; init; }
    public required double ThroatArea { get; init; }
    public required double ExitRadius { get; init; }
    public required double ContractionRatioUsed { get; init; }
    public required double ChamberRadius { get; init; }
    public required double ChamberCylinderLength { get; init; }
    public required double ChamberVolume { get; init; }     // injector face to throat
    public required double BellLength { get; init; }        // throat to exit plane
    public required double BellThetaN { get; init; }        // initial parabola angle [rad]
    public required double BellThetaE { get; init; }        // exit angle [rad]

    // Injector (flat-face showerhead, v1)
    public required double InjectorDeltaP { get; init; }    // [Pa]
    public required int FuelOrificeCount { get; init; }
    public required double FuelOrificeDiameter { get; init; } // [m]
    public required int OxOrificeCount { get; init; }
    public required double OxOrificeDiameter { get; init; }   // [m]

    // Film cooling (if enabled)
    public required double FilmCoolingMassFlow { get; init; } // [kg/s] fuel diverted to film cooling
    public required double FilmCoolingFraction { get; init; }  // fraction of total fuel used for film cooling
}

public static class EngineSizing
{
    /// <summary>
    /// Size the complete engine from the spec using ideal-rocket-theory
    /// (Sutton ch. 3, Huzel &amp; Huang ch. 4).
    /// </summary>
    public static EngineDesign Size(EngineSpec oSpec)
    {
        CombustionGas oGas = CombustionGas.Resolve(
            oSpec.Propellants, oSpec.OfRatio, oSpec.ChamberPressureBar, bEquilibriumExpansion: oSpec.EquilibriumExpansion);
        double g = oGas.Gamma;
        double fPc = oSpec.Pc;
        double fPa = oSpec.Pa;

        // --- Nozzle expansion -------------------------------------------------
        double fEps, fMe, fPe;
        if (oSpec.ExpansionRatio > 1.0)
        {
            fEps = oSpec.ExpansionRatio;
            fMe = IsentropicFlow.MachFromAreaRatio(fEps, g, bSupersonic: true);
            fPe = fPc * IsentropicFlow.PressureRatio(fMe, g);
        }
        else
        {
            // Optimal expansion: exit pressure matches ambient
            fPe = fPa;
            fMe = IsentropicFlow.MachFromPressureRatio(fPc / fPe, g);
            fEps = IsentropicFlow.AreaRatio(fMe, g);
        }

        // --- Throat sizing from required thrust -------------------------------
        double fCf = oGas.EquilibriumExpansion
            ? IsentropicFlow.ThrustCoefficientEquilibrium(oGas, fEps, fPe, fPc, fPa)
            : IsentropicFlow.ThrustCoefficient(g, fEps, fPe, fPc, fPa);
        double fCfVac = oGas.EquilibriumExpansion
            ? IsentropicFlow.ThrustCoefficientEquilibrium(oGas, fEps, fPe, fPc, 0.0)
            : IsentropicFlow.ThrustCoefficient(g, fEps, fPe, fPc, 0.0);
        double fAt = oSpec.ThrustN / (fCf * fPc);
        double fRt = Math.Sqrt(fAt / Math.PI);
        double fRe = fRt * Math.Sqrt(fEps);

        double fCStar = IsentropicFlow.CStar(g, oGas.Rs, oGas.Tc);
        double fMdot = fPc * fAt / fCStar;
        double fVe = oGas.EquilibriumExpansion
            ? IsentropicFlow.ExhaustVelocityEquilibrium(oGas, fPe, fPc)
            : IsentropicFlow.ExhaustVelocity(g, oGas.Rs, oGas.Tc, fPe, fPc);

        double fIsp = oSpec.ThrustN / (fMdot * CombustionGas.G0);
        double fIspVac = fCfVac * fPc * fAt / (fMdot * CombustionGas.G0);

        double fMdotFuel = fMdot / (1.0 + oSpec.OfRatio);
        double fMdotOx = fMdot - fMdotFuel;

        // Film cooling: divert fraction of fuel to film cooling orifices
        double fFilmFraction = oSpec.Injector.FilmCoolingFraction;
        double fMdotFilm = fMdotFuel * fFilmFraction;
        double fMdotFuelMain = fMdotFuel - fMdotFilm;

        // --- Combustion chamber ----------------------------------------------
        // Small engines need larger contraction ratios or the L* volume rule
        // produces absurdly long chambers. Empirical floor from Huzel & Huang
        // (fig. 4-9 correlation): CR = 8.0 * Dt[cm]^-0.6 + 1.25.
        double fDtCm = 2.0 * fRt * 100.0;
        double fCrFloor = 8.0 * Math.Pow(fDtCm, -0.6) + 1.25;
        double fCr = Math.Max(oSpec.ContractionRatio, fCrFloor);

        double fRc = fRt * Math.Sqrt(fCr);
        double fVc = oGas.LStar * fAt;

        // Convergent section approximated as a 30° cone frustum for the volume budget
        double fHConv = (fRc - fRt) / Math.Tan(30.0 * Math.PI / 180.0);
        double fVConv = Math.PI * fHConv / 3.0 * (fRc * fRc + fRc * fRt + fRt * fRt);
        double fAc = Math.PI * fRc * fRc;
        double fLcyl = Math.Max((fVc - fVConv) / fAc, 0.8 * fRc);

        // Slenderness cap: beyond ~2.2 chamber diameters the extra residence
        // time is wasted; accept a slight L* shortfall instead (conservative
        // L* values have margin built in).
        fLcyl = Math.Min(fLcyl, 2.2 * 2.0 * fRc);

        // --- Bell nozzle (Rao parabolic approximation) ------------------------
        double fLBell = oSpec.BellFraction * (fRe - fRt) / Math.Tan(15.0 * Math.PI / 180.0);
        (double fThetaN, double fThetaE) = RaoAngles(fEps);

        // --- Injector orifices (showerhead, 20% of Pc stiffness, Cd = 0.7) ----
        double fDp = 0.20 * fPc;
        const double fCd = 0.7;
        const double fDFuel = 0.6e-3, fDOx = 0.8e-3;

        // Size main fuel orifices for remaining fuel flow (after film cooling diversion)
        double fAFuel = fMdotFuelMain / (fCd * Math.Sqrt(2.0 * oGas.FuelDensity * fDp));
        double fAOx = fMdotOx / (fCd * Math.Sqrt(2.0 * oGas.OxDensity * fDp));
        int nFuel = Math.Max(4, (int)Math.Ceiling(fAFuel / (Math.PI * fDFuel * fDFuel / 4.0)));
        int nOx = Math.Max(4, (int)Math.Ceiling(fAOx / (Math.PI * fDOx * fDOx / 4.0)));

        return new EngineDesign
        {
            Spec = oSpec,
            Gas = oGas,
            ExpansionRatio = fEps,
            ExitMach = fMe,
            ExitPressure = fPe,
            CStar = fCStar,
            ThrustCoeff = fCf,
            ThrustCoeffVac = fCfVac,
            MassFlow = fMdot,
            MassFlowFuel = fMdotFuel,
            MassFlowOx = fMdotOx,
            IspSeaLevelS = fIsp,
            IspVacuumS = fIspVac,
            ExhaustVelocity = fVe,
            ThroatRadius = fRt,
            ThroatArea = fAt,
            ExitRadius = fRe,
            ContractionRatioUsed = fCr,
            ChamberRadius = fRc,
            ChamberCylinderLength = fLcyl,
            ChamberVolume = fVc,
            BellLength = fLBell,
            BellThetaN = fThetaN,
            BellThetaE = fThetaE,
            InjectorDeltaP = fDp,
            FuelOrificeCount = nFuel,
            FuelOrificeDiameter = fDFuel,
            OxOrificeCount = nOx,
            OxOrificeDiameter = fDOx,
            FilmCoolingMassFlow = fMdotFilm,
            FilmCoolingFraction = fFilmFraction
        };
    }

    /// <summary>
    /// Initial/exit wall angles for an 80%-length Rao bell, linearly interpolated
    /// from the classic Rao design charts (Huzel &amp; Huang fig. 4-16, approximate).
    /// </summary>
    private static (double fThetaN, double fThetaE) RaoAngles(double fEps)
    {
        double[] afEps = [3.5, 4, 5, 10, 20, 30, 40, 50];
        double[] afThetaN = [20.5, 21.0, 22.0, 24.5, 27.0, 28.2, 29.0, 29.5];
        double[] afThetaE = [14.5, 14.0, 13.0, 11.0, 9.0, 8.3, 8.0, 7.5];

        double fN = Interp(afEps, afThetaN, fEps);
        double fE = Interp(afEps, afThetaE, fEps);
        return (fN * Math.PI / 180.0, fE * Math.PI / 180.0);
    }

    private static double Interp(double[] afX, double[] afY, double fX)
    {
        if (fX <= afX[0]) return afY[0];
        if (fX >= afX[^1]) return afY[^1];
        for (int i = 1; i < afX.Length; i++)
        {
            if (fX <= afX[i])
            {
                double fT = (fX - afX[i - 1]) / (afX[i] - afX[i - 1]);
                return afY[i - 1] + fT * (afY[i] - afY[i - 1]);
            }
        }
        return afY[^1];
    }
}

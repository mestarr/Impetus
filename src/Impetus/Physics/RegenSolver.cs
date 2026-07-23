namespace Impetus.Physics;

/// <summary>
/// 1D marching regenerative cooling model along the channel path (injector
/// manifold → nozzle collector). At each station: Bartz hot-gas convection,
/// conduction through the CuCrZr liner, Gnielinski tube-side convection;
/// coolant enthalpy and pressure are updated per segment.
/// </summary>
public static class RegenSolver
{
    /// <summary>Fuel inlet temperature at the regen manifold [K].</summary>
    public const double CoolantInletTempK = 300.0;

    /// <summary>Absolute feed pressure at the regen inlet [Pa] (before channel losses).</summary>
    public const double CoolantInletPressurePa = 25e5;

    /// <summary>CuCrZr liner thermal conductivity [W/(m·K)].</summary>
    public const double LinerConductivityWmK = 320.0;

    /// <summary>Design-limit hot-gas-side wall temperature for screening [K].</summary>
    public const double MaxWallTempK = 950.0;

    const double fZStart = 0.002;
    const double fZEndFraction = 0.94;

    public static ThermalResult Solve(EngineDesign oDesign, NozzleContour oContour)
    {
        CombustionGas oGas = oDesign.Gas;
        CoolingSpec oCool = oDesign.Spec.Cooling;
        double g = oGas.Gamma;
        double fDt = 2.0 * oDesign.ThroatRadius;
        double fPr = oGas.Pr;
        double fMu = oGas.Viscosity(oGas.Tc);
        double fRecovery = Math.Pow(fPr, 1.0 / 3.0);
        double fRCurv = 0.5 * (1.5 + 0.382) * oDesign.ThroatRadius;

        // Film cooling effectiveness (if enabled)
        double fFilmEffectiveness = ComputeFilmCoolingEffectiveness(oDesign);

        double fC = 0.026 / Math.Pow(fDt, 0.2)
                  * (Math.Pow(fMu, 0.2) * oGas.Cp / Math.Pow(fPr, 0.6))
                  * Math.Pow(oDesign.Spec.Pc / oDesign.CStar, 0.8)
                  * Math.Pow(fDt / fRCurv, 0.1);

        double fWallMM = Math.Max(oDesign.Spec.WallThicknessMM, oCool.DiameterMM + 1.6);
        double fLinerM = (fWallMM - oCool.DiameterMM) * 0.5e-3;
        double fChanD = oCool.DiameterMM * 1e-3;
        double fAChan = Math.PI * fChanD * fChanD / 4.0;
        int nChan = oCool.Count;

        // Adjust coolant flow for film cooling (film fuel doesn't go through regen)
        double fMdotCoolant = oDesign.MassFlowFuel * (1.0 - oDesign.FilmCoolingFraction);

        double fZEnd = oContour.ExitZ * fZEndFraction;
        List<ContourPoint> aoPath = oContour.Resampled(120, fZStart, fZEnd);
        double fAxialSpan = Math.Max(fZEnd - fZStart, 1e-6);
        double fPitch = fAxialSpan / Math.Max(oCool.HelixTurns, 1e-6);

        double fTc = CoolantInletTempK;
        double fPc = CoolantInletPressurePa;
        double fQTotal = 0;
        double fQThroat = 0;
        double fPeakWall = 0;
        double fThroatWall = CoolantInletTempK;
        double fDpTotal = 0;

        List<ThermalStation> aoStations = [];
        ContourPoint oPrev = aoPath[0];

        for (int i = 1; i < aoPath.Count; i++)
        {
            ContourPoint oPt = aoPath[i];
            double dz = oPt.Z - oPrev.Z;
            double dr = oPt.R - oPrev.R;
            double fDsContour = Math.Sqrt(dz * dz + dr * dr);
            double fDTheta = 2.0 * Math.PI * fDsContour / fPitch;
            double fDs = Math.Sqrt(fDsContour * fDsContour + (oPt.R * fDTheta) * (oPt.R * fDTheta));
            double fR = 0.5 * (oPt.R + oPrev.R);

            double fAreaRatio = (fR * fR) / (oDesign.ThroatRadius * oDesign.ThroatRadius);
            bool bSupersonic = oPt.Z > oContour.ThroatZ;
            double fM = fAreaRatio <= 1.0 + 1e-9
                ? 1.0
                : IsentropicFlow.MachFromAreaRatio(fAreaRatio, g, bSupersonic);
            double fStag = 1.0 + 0.5 * (g - 1.0) * fM * fM;
            double fTaw = oGas.Tc * (1.0 + fRecovery * 0.5 * (g - 1.0) * fM * fM) / fStag;

            // Apply film cooling effectiveness to reduce adiabatic wall temperature
            if (fFilmEffectiveness > 0)
            {
                double fTfilm = CoolantInletTempK; // Film coolant temperature (fuel inlet)
                fTaw = fTfilm + fFilmEffectiveness * (fTaw - fTfilm);
            }

            double fTw = fTc + 200.0;
            double fQ = 0;
            for (int nSub = 0; nSub < 6; nSub++)
            {
                double fSigma = 1.0 / (
                    Math.Pow(0.5 * fTw / oGas.Tc * fStag + 0.5, 0.68)
                    * Math.Pow(fStag, 0.12));
                double fHg = fC * Math.Pow(1.0 / fAreaRatio, 0.9) * fSigma;
                double fHc = CoolantSideCoeff(oGas, fTc, fMdotCoolant, nChan, fAChan, fChanD);

                fQ = (fTaw - fTc) / (1.0 / fHg + fLinerM / LinerConductivityWmK + 1.0 / fHc);
                fTw = fTc + fQ * (1.0 / fHc + 0.5 * fLinerM / LinerConductivityWmK);
            }

            double fStrip = 2.0 * Math.PI * fR * fDsContour;
            double fDQ = fQ * fStrip;
            fQTotal += fDQ;
            fPeakWall = Math.Max(fPeakWall, fTw);

            if (Math.Abs(oPt.Z - oContour.ThroatZ) < 0.5 * fDsContour || fQ > fQThroat)
            {
                fQThroat = Math.Max(fQThroat, fQ);
                fThroatWall = fTw;
            }

            fTc += fDQ / (fMdotCoolant * oGas.FuelCp);
            fDpTotal += ChannelDpSegment(oGas, fMdotCoolant, nChan, fAChan, fChanD, fDs);

            aoStations.Add(new(oPt.Z, oPt.R, fM, fQ, fTw, fTc, fPc - fDpTotal));
            oPrev = oPt;
        }

        double fMdotChan = fMdotCoolant / nChan;
        double fV = fMdotChan / (oGas.FuelDensity * fAChan);

        return new ThermalResult
        {
            Stations = aoStations,
            ThroatHeatFlux = fQThroat,
            TotalHeatLoad = fQTotal,
            CoolantTempRise = fTc - CoolantInletTempK,
            CoolantVelocity = fV,
            AssumedWallTemp = fPeakWall,
            CoolantInletTempK = CoolantInletTempK,
            CoolantOutletTempK = fTc,
            PeakWallTempK = fPeakWall,
            ThroatWallTempK = fThroatWall,
            ChannelPressureDropPa = fDpTotal,
        };
    }

    /// <summary>
    /// Search channel count and bore for peak wall temperature and velocity
    /// inside the regen window (higher coolant velocity → higher h_c).
    /// </summary>
    public static CoolingSpec SizeChannelsForWallTemp(
        EngineDesign oDesign, NozzleContour oContour, CoolingSpec oCool)
    {
        CoolingSpec oBest = oCool;
        double fBestScore = double.MaxValue;

        foreach (int n in Enumerable.Range(VirtualValidation.MinChannelCount, 49).Where(n => n % 2 == 0))
        {
            foreach (double fD in new[] { 0.8, 1.0, 1.2, 1.4, 1.6 })
            {
                if (fD < VirtualValidation.MinChannelDiameterMM) continue;
                CoolingSpec oTry = oCool with { Count = n, DiameterMM = fD };
                EngineDesign oSized = oDesign with { Spec = oDesign.Spec with { Cooling = oTry } };
                ThermalResult oTherm = Solve(oSized, oContour);

                double fV = oTherm.CoolantVelocity;
                bool bVelOk = fV >= VirtualValidation.MinCoolantVelocityMS
                           && fV <= VirtualValidation.MaxCoolantVelocityMS;
                bool bWallOk = oTherm.PeakWallTempK <= MaxWallTempK;
                double fScore = oTherm.PeakWallTempK
                              + Math.Max(0, fV - VirtualValidation.MaxCoolantVelocityMS) * 80
                              + Math.Max(0, VirtualValidation.MinCoolantVelocityMS - fV) * 80;

                if (bVelOk && bWallOk)
                    return oTry;
                if (fScore < fBestScore)
                {
                    fBestScore = fScore;
                    oBest = oTry;
                }
            }
        }
        return oBest;
    }

    static double CoolantSideCoeff(
        CombustionGas oGas, double fTc, double fMdotFuel, int nChan, double fAChan, double fChanD)
    {
        double fMdotChan = fMdotFuel / nChan;
        double fV = fMdotChan / (oGas.FuelDensity * fAChan);
        double fMu = FuelViscosity(oGas, fTc);
        double fRe = oGas.FuelDensity * fV * fChanD / Math.Max(fMu, 1e-9);
        double fPr = FuelPrandtl(oGas);
        double fF = FrictionFactor(fRe);
        double fNu = GnielinskiNu(fRe, fPr, fF);
        double fK = FuelConductivity(oGas, fTc);
        return fNu * fK / fChanD;
    }

    static double ChannelDpSegment(
        CombustionGas oGas, double fMdotFuel, int nChan, double fAChan, double fChanD, double fDs)
    {
        double fMdotChan = fMdotFuel / nChan;
        double fV = fMdotChan / (oGas.FuelDensity * fAChan);
        double fMu = FuelViscosity(oGas, CoolantInletTempK);
        double fRe = oGas.FuelDensity * fV * fChanD / Math.Max(fMu, 1e-9);
        double fF = FrictionFactor(fRe);
        return fF * (fDs / fChanD) * 0.5 * oGas.FuelDensity * fV * fV;
    }

    static double GnielinskiNu(double fRe, double fPr, double fF)
    {
        if (fRe < 2300)
            return 3.66;
        return (fF / 8.0) * (fRe - 1000.0) * fPr
             / (1.0 + 12.7 * Math.Sqrt(fF / 8.0) * (Math.Pow(fPr, 2.0 / 3.0) - 1.0));
    }

    static double FrictionFactor(double fRe)
    {
        if (fRe < 2300) return 64.0 / Math.Max(fRe, 1.0);
        return Math.Pow(0.316 / Math.Pow(Math.Max(fRe, 2300), 0.25), 1.0);
    }

    static double FuelViscosity(CombustionGas oGas, double fT)
    {
        // Andrade-style scaling from a 300 K reference per propellant class.
        double fMuRef = oGas.FuelDensity switch
        {
            < 200 => 1.3e-5,
            < 600 => 2.0e-3,
            _ => 2.4e-3,
        };
        return fMuRef * Math.Exp(1200.0 * (1.0 / fT - 1.0 / 300.0));
    }

    static double FuelPrandtl(CombustionGas oGas)
        => oGas.FuelCp * FuelViscosity(oGas, CoolantInletTempK) / FuelConductivity(oGas, CoolantInletTempK);

    static double FuelConductivity(CombustionGas oGas, double fT)
    {
        double fKRef = oGas.FuelDensity switch
        {
            < 200 => 0.12,
            < 600 => 0.13,
            _ => 0.14,
        };
        return fKRef * Math.Pow(fT / 300.0, 0.7);
    }

    /// <summary>
    /// Compute film cooling effectiveness based on film cooling fraction and geometry.
    /// Uses a simplified correlation: η = 1 - exp(-C * (m_film/m_total))
    /// where C ≈ 2.0 for typical film cooling configurations.
    /// </summary>
    static double ComputeFilmCoolingEffectiveness(EngineDesign oDesign)
    {
        if (oDesign.FilmCoolingFraction <= 0.0)
            return 0.0;

        double fFilmFraction = oDesign.FilmCoolingFraction;

        // Simplified effectiveness correlation based on film flow fraction
        // Higher film fraction = better cooling, but diminishing returns
        double fEffectiveness = 1.0 - Math.Exp(-2.0 * fFilmFraction);

        // Cap effectiveness at reasonable maximum (film cooling can't eliminate all heat)
        return Math.Min(fEffectiveness, 0.7);
    }

    /// <summary>
    /// Solve thermal model with CFD-provided wall heat flux boundary condition.
    /// This is used for conjugate heat transfer coupling.
    /// </summary>
    public static ThermalResult SolveWithCfdFlux(
        EngineDesign oDesign,
        NozzleContour oContour,
        double[] afCfdHeatFlux) // [W/m²] at each station
    {
        CombustionGas oGas = oDesign.Gas;
        CoolingSpec oCool = oDesign.Spec.Cooling;
        double g = oGas.Gamma;

        double fWallMM = Math.Max(oDesign.Spec.WallThicknessMM, oCool.DiameterMM + 1.6);
        double fLinerM = (fWallMM - oCool.DiameterMM) * 0.5e-3;
        double fChanD = oCool.DiameterMM * 1e-3;
        double fAChan = Math.PI * fChanD * fChanD / 4.0;
        int nChan = oCool.Count;

        // Adjust coolant flow for film cooling (film fuel doesn't go through regen)
        double fMdotCoolant = oDesign.MassFlowFuel * (1.0 - oDesign.FilmCoolingFraction);

        double fZEnd = oContour.ExitZ * fZEndFraction;
        List<ContourPoint> aoPath = oContour.Resampled(120, fZStart, fZEnd);
        double fAxialSpan = Math.Max(fZEnd - fZStart, 1e-6);
        double fPitch = fAxialSpan / Math.Max(oCool.HelixTurns, 1e-6);

        double fTc = CoolantInletTempK;
        double fPc = CoolantInletPressurePa;
        double fQTotal = 0;
        double fQThroat = 0;
        double fPeakWall = 0;
        double fThroatWall = CoolantInletTempK;
        double fDpTotal = 0;

        List<ThermalStation> aoStations = [];
        ContourPoint oPrev = aoPath[0];

        for (int i = 1; i < aoPath.Count; i++)
        {
            ContourPoint oPt = aoPath[i];
            double dz = oPt.Z - oPrev.Z;
            double dr = oPt.R - oPrev.R;
            double fDsContour = Math.Sqrt(dz * dz + dr * dr);
            double fDTheta = 2.0 * Math.PI * fDsContour / fPitch;
            double fDs = Math.Sqrt(fDsContour * fDsContour + (oPt.R * fDTheta) * (oPt.R * fDTheta));
            double fR = 0.5 * (oPt.R + oPrev.R);

            double fAreaRatio = (fR * fR) / (oDesign.ThroatRadius * oDesign.ThroatRadius);
            bool bSupersonic = oPt.Z > oContour.ThroatZ;
            double fM = fAreaRatio <= 1.0 + 1e-9
                ? 1.0
                : IsentropicFlow.MachFromAreaRatio(fAreaRatio, g, bSupersonic);
            double fStag = 1.0 + 0.5 * (g - 1.0) * fM * fM;

            // Use CFD heat flux if available, otherwise fall back to analytic
            bool bUseCfdFlux = afCfdHeatFlux.Length > 0 && (i - 1) < afCfdHeatFlux.Length && afCfdHeatFlux[i - 1] > 0;

            double fHg;
            if (bUseCfdFlux)
            {
                // CFD provides heat flux directly: q = h_g * (T_aw - T_w)
                // For simplicity, we'll use a simplified approach where CFD flux scales the analytic coefficient
                double fPr = oGas.Pr;
                double fMu = oGas.Viscosity(oGas.Tc);
                double fRecovery = Math.Pow(fPr, 1.0 / 3.0);
                double fRCurv = 0.5 * (1.5 + 0.382) * oDesign.ThroatRadius;
                double fDt = 2.0 * oDesign.ThroatRadius;
                double fC = 0.026 / Math.Pow(fDt, 0.2)
                          * (Math.Pow(fMu, 0.2) * oGas.Cp / Math.Pow(fPr, 0.6))
                          * Math.Pow(oDesign.Spec.Pc / oDesign.CStar, 0.8)
                          * Math.Pow(fDt / fRCurv, 0.1);
                double fSigma = 1.0 / (
                    Math.Pow(0.5 * fTc / oGas.Tc * fStag + 0.5, 0.68)
                    * Math.Pow(fStag, 0.12));
                fHg = fC * Math.Pow(1.0 / fAreaRatio, 0.9) * fSigma;

                // Scale by CFD flux ratio (simplified coupling)
                double fAnalyticFlux = fHg * 100.0; // Approximate analytic flux
                double fCfdFlux = afCfdHeatFlux[i - 1];
                fHg *= Math.Max(0.5, Math.Min(2.0, fCfdFlux / fAnalyticFlux));
            }
            else
            {
                // Fall back to analytic Bartz
                double fPr = oGas.Pr;
                double fMu = oGas.Viscosity(oGas.Tc);
                double fRecovery = Math.Pow(fPr, 1.0 / 3.0);
                double fRCurv = 0.5 * (1.5 + 0.382) * oDesign.ThroatRadius;
                double fDt = 2.0 * oDesign.ThroatRadius;
                double fC = 0.026 / Math.Pow(fDt, 0.2)
                          * (Math.Pow(fMu, 0.2) * oGas.Cp / Math.Pow(fPr, 0.6))
                          * Math.Pow(oDesign.Spec.Pc / oDesign.CStar, 0.8)
                          * Math.Pow(fDt / fRCurv, 0.1);
                double fSigma = 1.0 / (
                    Math.Pow(0.5 * fTc / oGas.Tc * fStag + 0.5, 0.68)
                    * Math.Pow(fStag, 0.12));
                fHg = fC * Math.Pow(1.0 / fAreaRatio, 0.9) * fSigma;
            }

            double fTaw = oGas.Tc * (1.0 + Math.Pow(oGas.Pr, 1.0 / 3.0) * 0.5 * (g - 1.0) * fM * fM) / fStag;

            // Apply film cooling effectiveness
            double fFilmEffectiveness = ComputeFilmCoolingEffectiveness(oDesign);
            if (fFilmEffectiveness > 0)
            {
                double fTfilm = CoolantInletTempK;
                fTaw = fTfilm + fFilmEffectiveness * (fTaw - fTfilm);
            }

            double fTw = fTc + 200.0;
            double fQ = 0;
            for (int nSub = 0; nSub < 6; nSub++)
            {
                double fHc = CoolantSideCoeff(oGas, fTc, fMdotCoolant, nChan, fAChan, fChanD);
                fQ = (fTaw - fTc) / (1.0 / fHg + fLinerM / LinerConductivityWmK + 1.0 / fHc);
                fTw = fTc + fQ * (1.0 / fHc + 0.5 * fLinerM / LinerConductivityWmK);
            }

            double fStrip = 2.0 * Math.PI * fR * fDsContour;
            double fDQ = fQ * fStrip;
            fQTotal += fDQ;
            fPeakWall = Math.Max(fPeakWall, fTw);

            if (Math.Abs(oPt.Z - oContour.ThroatZ) < 0.5 * fDsContour || fQ > fQThroat)
            {
                fQThroat = Math.Max(fQThroat, fQ);
                fThroatWall = fTw;
            }

            fTc += fDQ / (fMdotCoolant * oGas.FuelCp);
            fDpTotal += ChannelDpSegment(oGas, fMdotCoolant, nChan, fAChan, fChanD, fDs);

            aoStations.Add(new(oPt.Z, oPt.R, fM, fQ, fTw, fTc, fPc - fDpTotal));
            oPrev = oPt;
        }

        double fMdotChan = fMdotCoolant / nChan;
        double fV = fMdotChan / (oGas.FuelDensity * fAChan);

        return new ThermalResult
        {
            Stations = aoStations,
            ThroatHeatFlux = fQThroat,
            TotalHeatLoad = fQTotal,
            CoolantTempRise = fTc - CoolantInletTempK,
            CoolantVelocity = fV,
            AssumedWallTemp = fPeakWall,
            CoolantInletTempK = CoolantInletTempK,
            CoolantOutletTempK = fTc,
            PeakWallTempK = fPeakWall,
            ThroatWallTempK = fThroatWall,
            ChannelPressureDropPa = fDpTotal
        };
    }
}

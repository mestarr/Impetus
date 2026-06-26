namespace Impetus.Physics;

public record ThermalStation(double Z, double R, double Mach, double HeatFlux);

public record ThermalResult
{
    public required IReadOnlyList<ThermalStation> Stations { get; init; }
    public required double ThroatHeatFlux { get; init; }     // [W/m2]
    public required double TotalHeatLoad { get; init; }      // [W]
    public required double CoolantTempRise { get; init; }    // [K]
    public required double CoolantVelocity { get; init; }    // [m/s] in channels
    public required double AssumedWallTemp { get; init; }    // [K]
}

/// <summary>
/// Hot-gas-side convective heat transfer along the contour using the Bartz
/// correlation (Bartz 1957; Sutton ch. 8), plus a first-order regenerative
/// cooling balance: total heat load dumped into the fuel flow.
/// </summary>
public static class ThermalModel
{
    const double fWallTempK = 800.0; // typical regen-cooled copper liner design point

    public static ThermalResult Evaluate(EngineDesign oDesign, NozzleContour oContour)
    {
        CombustionGas oGas = oDesign.Gas;
        double g = oGas.Gamma;
        double fDt = 2.0 * oDesign.ThroatRadius;
        double fPr = oGas.Pr;
        double fMu = oGas.Viscosity(oGas.Tc);
        double fRecovery = Math.Pow(fPr, 1.0 / 3.0);

        // Mean throat curvature radius used by Bartz (average of the two arcs)
        double fRCurv = 0.5 * (1.5 + 0.382) * oDesign.ThroatRadius;

        // Constant part of the Bartz correlation
        double fC = 0.026 / Math.Pow(fDt, 0.2)
                  * (Math.Pow(fMu, 0.2) * oGas.Cp / Math.Pow(fPr, 0.6))
                  * Math.Pow(oDesign.Spec.Pc / oDesign.CStar, 0.8)
                  * Math.Pow(fDt / fRCurv, 0.1);

        List<ThermalStation> aoStations = [];
        double fQTotal = 0;
        double fQThroat = 0;
        ContourPoint oPrev = oContour.Points[0];

        foreach (ContourPoint oPt in oContour.Points)
        {
            double fAreaRatio = (oPt.R * oPt.R) / (oDesign.ThroatRadius * oDesign.ThroatRadius);
            bool bSupersonic = oPt.Z > oContour.ThroatZ;
            double fM = fAreaRatio <= 1.0 + 1e-9
                ? 1.0
                : IsentropicFlow.MachFromAreaRatio(fAreaRatio, g, bSupersonic);

            double fStag = 1.0 + 0.5 * (g - 1.0) * fM * fM;

            // Bartz local-property correction factor
            double fSigma = 1.0 / (
                Math.Pow(0.5 * fWallTempK / oGas.Tc * fStag + 0.5, 0.68)
                * Math.Pow(fStag, 0.12));

            double fHg = fC * Math.Pow(1.0 / fAreaRatio, 0.9) * fSigma;

            // Adiabatic wall (recovery) temperature
            double fTaw = oGas.Tc * (1.0 + fRecovery * 0.5 * (g - 1.0) * fM * fM) / fStag;
            double fQ = fHg * (fTaw - fWallTempK);

            aoStations.Add(new(oPt.Z, oPt.R, fM, fQ));
            if (Math.Abs(oPt.Z - oContour.ThroatZ) < 1e-9 || fQ > fQThroat)
                fQThroat = Math.Max(fQThroat, fQ);

            // Integrate over the wall surface (cone frustum strip)
            double dz = oPt.Z - oPrev.Z;
            double dr = oPt.R - oPrev.R;
            double fStrip = Math.PI * (oPt.R + oPrev.R) * Math.Sqrt(dz * dz + dr * dr);
            fQTotal += fQ * fStrip;
            oPrev = oPt;
        }

        // v1 lumped regen balance: only a fraction of integrated Bartz heat is
        // picked up by the fuel coolant (uncooled injector face/flange, film
        // losses, etc.). Replaced by the 1D regen solver in roadmap §8.3.
        const double fRegenHeatPickupFraction = 0.15;
        double fDT = fRegenHeatPickupFraction * fQTotal / (oDesign.MassFlowFuel * oGas.FuelCp);

        CoolingSpec oCool = oDesign.Spec.Cooling;
        double fAChannel = Math.PI * Math.Pow(oCool.DiameterMM * 1e-3 / 2.0, 2);
        double fVCool = oDesign.MassFlowFuel / (oGas.FuelDensity * oCool.Count * fAChannel);

        return new ThermalResult
        {
            Stations = aoStations,
            ThroatHeatFlux = fQThroat,
            TotalHeatLoad = fQTotal,
            CoolantTempRise = fDT,
            CoolantVelocity = fVCool,
            AssumedWallTemp = fWallTempK
        };
    }
}

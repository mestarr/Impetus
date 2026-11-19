using System.Numerics;
using Impetus.Physics;
using PicoGK;

namespace Impetus.Geometry;

/// <summary>
/// Builds the injector plate and the fuel and oxidizer domes behind it.
/// Supports showerhead, coaxial swirl, and impinging doublet patterns.
/// </summary>
public class InjectorBuilder
{
    public const double CoaxialPlateMM = 8.0;
    public const double ShowerheadPlateMM = 5.0;
    public const double ImpingingPlateMM = 6.0;

    readonly Library m_lib;
    readonly EngineDesign m_oDesign;
    readonly InjectorSpec m_oSpec;

    public InjectorBuilder(Library lib, EngineDesign oDesign, InjectorSpec oSpec)
    {
        m_lib = lib;
        m_oDesign = oDesign;
        m_oSpec = oSpec;
    }

    static double MM(double fMeters) => fMeters * 1000.0;

    /// <summary>Build the injector plate with appropriate element pattern.</summary>
    public Voxels voxBuild()
    {
        return m_oSpec.Type switch
        {
            InjectorType.Showerhead => voxBuildShowerhead(),
            InjectorType.CoaxialSwirl => voxBuildCoaxialSwirl(),
            InjectorType.ImpingingDoublet => voxBuildImpingingDoublet(),
            _ => voxBuildShowerhead()
        };
    }

    Voxels voxBuildShowerhead()
    {
        double fChamberRadius = MM(m_oDesign.ChamberRadius);
        List<InjectorPort> aoPorts = [];
        AddRing(aoPorts, m_oDesign.FuelOrificeCount, m_oDesign.FuelOrificeDiameter * 1000.0,
            0.72 * fChamberRadius, bFuel: true, 0);
        AddRing(aoPorts, m_oDesign.OxOrificeCount, m_oDesign.OxOrificeDiameter * 1000.0,
            0.42 * fChamberRadius, bFuel: false, Math.PI / Math.Max(4, m_oDesign.OxOrificeCount));
        AddFilm(aoPorts, fChamberRadius);
        return voxFinish(ShowerheadPlateMM, fChamberRadius, 3.0, aoPorts);
    }

    Voxels voxBuildCoaxialSwirl()
    {
        double fChamberRadius = MM(m_oDesign.ChamberRadius);
        double fRingRadius = fChamberRadius * 0.6;
        int nElements = Math.Max(1, m_oSpec.ElementCount);
        List<InjectorPort> aoPorts = [];

        for (int i = 0; i < nElements; i++)
        {
            double fAngle = 2 * Math.PI * i / nElements;
            aoPorts.Add(new InjectorPort(
                fRingRadius * Math.Cos(fAngle),
                fRingRadius * Math.Sin(fAngle),
                m_oSpec.InnerDiameterMM,
                m_oSpec.OuterDiameterMM));
        }

        AddFilm(aoPorts, fChamberRadius);
        return voxFinish(CoaxialPlateMM, fChamberRadius, 4.0, aoPorts);
    }

    Voxels voxBuildImpingingDoublet()
    {
        double fChamberRadius = MM(m_oDesign.ChamberRadius);
        double fHoleR = m_oSpec.OrificeDiameterMM / 2.0;
        double fPostR = fHoleR + PropellantDomes.PostWallMM;
        double fSep = fPostR + fHoleR + 0.8;
        double fRingRadius = fChamberRadius * 0.6;
        double fMaxRing = fChamberRadius - PropellantDomes.WallMM - (fSep / 2.0 + fPostR + 0.4);
        if (fRingRadius > fMaxRing)
            fRingRadius = Math.Max(4.0, fMaxRing);

        int nElements = Math.Max(1, m_oSpec.ElementCount);
        List<InjectorPort> aoPorts = [];
        for (int i = 0; i < nElements; i++)
        {
            double fAngle = 2 * Math.PI * i / nElements;
            double fX = fRingRadius * Math.Cos(fAngle);
            double fY = fRingRadius * Math.Sin(fAngle);
            double fTx = -Math.Sin(fAngle);
            double fTy = Math.Cos(fAngle);
            double fOx = (fSep / 2.0) * fTx;
            double fOy = (fSep / 2.0) * fTy;
            aoPorts.Add(new InjectorPort(fX + fOx, fY + fOy, m_oSpec.OrificeDiameterMM, 0));
            aoPorts.Add(new InjectorPort(fX - fOx, fY - fOy, 0, m_oSpec.OrificeDiameterMM));
        }

        AddFilm(aoPorts, fChamberRadius);
        return voxFinish(ImpingingPlateMM, fChamberRadius, 3.0, aoPorts);
    }

    void AddFilm(List<InjectorPort> aoPorts, double fChamberRadius)
    {
        if (!m_oSpec.FilmCooling || m_oSpec.FilmOrificeCount < 1 || m_oSpec.FilmOrificeDiameterMM <= 0.2)
            return;

        double fPostR = m_oSpec.FilmOrificeDiameterMM / 2.0 + PropellantDomes.PostWallMM;
        double fLimit = fChamberRadius - PropellantDomes.WallMM - fPostR - 0.4;
        double fRing = Math.Min(fChamberRadius * 0.85, fLimit);
        if (fRing < fPostR + 2.0)
            return;

        AddRing(aoPorts, m_oSpec.FilmOrificeCount, m_oSpec.FilmOrificeDiameterMM, fRing, bFuel: true, 0);
    }

    static void AddRing(
        List<InjectorPort> aoPorts, int nCount, double fDiameterMM, double fRingRMM, bool bFuel, double fAngle0)
    {
        if (nCount < 1 || fDiameterMM <= 0.2 || fRingRMM <= fDiameterMM)
            return;

        int nMax = Math.Max(4, (int)(2.0 * Math.PI * fRingRMM / (1.6 * fDiameterMM)));
        int n = Math.Min(nCount, nMax);
        for (int i = 0; i < n; i++)
        {
            double fA = fAngle0 + 2.0 * Math.PI * i / n;
            double fX = fRingRMM * Math.Cos(fA);
            double fY = fRingRMM * Math.Sin(fA);
            aoPorts.Add(bFuel
                ? new InjectorPort(fX, fY, fDiameterMM, 0)
                : new InjectorPort(fX, fY, 0, fDiameterMM));
        }
    }

    Voxels voxFinish(double fPlateThickness, double fRadius, double fIgniterDiam, List<InjectorPort> aoPorts)
    {
        Lattice latPlate = new(m_lib);
        latPlate.AddBeam(
            new Vector3(0, 0, (float)-fPlateThickness),
            new Vector3(0, 0, 0.5f),
            (float)fRadius,
            (float)fRadius,
            bRoundCap: false);

        return PropellantDomes.voxAttach(
            m_lib, new Voxels(latPlate), fPlateThickness, fRadius, fIgniterDiam, aoPorts);
    }
}

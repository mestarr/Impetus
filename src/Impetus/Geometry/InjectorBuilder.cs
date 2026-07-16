using System.Numerics;
using Impetus;
using Impetus.Physics;
using PicoGK;

namespace Impetus.Geometry;

/// <summary>
/// Builds injector plate geometry based on injector type and specifications.
/// Supports showerhead, coaxial swirl, and impinging doublet patterns.
/// </summary>
public class InjectorBuilder
{
    readonly Library m_lib;
    readonly EngineDesign m_oDesign;
    readonly InjectorSpec m_oSpec;

    public InjectorBuilder(Library lib, EngineDesign oDesign, InjectorSpec oSpec)
    {
        m_lib = lib;
        m_oDesign = oDesign;
        m_oSpec = oSpec;
    }

    // Convert meters to millimeters (PicoGK convention)
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

    /// <summary>Build simple showerhead injector with ring pattern of orifices.</summary>
    Voxels voxBuildShowerhead()
    {
        double fChamberRadius = MM(m_oDesign.ChamberRadius);
        double fPlateThickness = 5.0;
        double fOrificeDiam = m_oSpec.OrificeDiameterMM;
        int nElements = m_oSpec.ElementCount;

        // Base plate using Lattice
        Lattice latPlate = new(m_lib);
        latPlate.AddBeam(
            new Vector3(0, 0, (float)-fPlateThickness),
            new Vector3(0, 0, 0.5f),
            (float)fChamberRadius,
            (float)fChamberRadius,
            bRoundCap: false);

        Voxels voxPlate = new(latPlate);

        // Add orifices in ring pattern
        Lattice latHoles = new(m_lib);
        double fRingRadius = fChamberRadius * 0.6;
        for (int i = 0; i < nElements; i++)
        {
            double fAngle = 2 * Math.PI * i / nElements;
            double fX = fRingRadius * Math.Cos(fAngle);
            double fY = fRingRadius * Math.Sin(fAngle);

            latHoles.AddBeam(
                new Vector3((float)fX, (float)fY, (float)(-fPlateThickness - 1)),
                new Vector3((float)fX, (float)fY, 1),
                (float)(fOrificeDiam / 2),
                (float)(fOrificeDiam / 2),
                bRoundCap: false);
        }

        // Add central igniter port
        double fIgniterDiam = 3.0;
        latHoles.AddBeam(
            new Vector3(0, 0, (float)(-fPlateThickness - 1)),
            new Vector3(0, 0, 1),
            (float)(fIgniterDiam / 2),
            (float)(fIgniterDiam / 2),
            bRoundCap: false);

        voxPlate.BoolSubtract(new Voxels(latHoles));

        // Add film cooling orifices if enabled
        if (m_oSpec.FilmCooling)
        {
            voxPlate = AddFilmCoolingOrifices(voxPlate, fChamberRadius, fPlateThickness);
        }

        return voxPlate;
    }

    /// <summary>Build coaxial swirl injector elements.</summary>
    Voxels voxBuildCoaxialSwirl()
    {
        double fChamberRadius = MM(m_oDesign.ChamberRadius);
        double fPlateThickness = 8.0;
        double fOuterDiam = m_oSpec.OuterDiameterMM;
        double fInnerDiam = m_oSpec.InnerDiameterMM;
        int nElements = m_oSpec.ElementCount;

        // Base plate using Lattice
        Lattice latPlate = new(m_lib);
        latPlate.AddBeam(
            new Vector3(0, 0, (float)-fPlateThickness),
            new Vector3(0, 0, 0.5f),
            (float)fChamberRadius,
            (float)fChamberRadius,
            bRoundCap: false);

        Voxels voxPlate = new(latPlate);

        // Add coaxial elements in ring pattern
        Lattice latHoles = new(m_lib);
        double fRingRadius = fChamberRadius * 0.6;
        for (int i = 0; i < nElements; i++)
        {
            double fAngle = 2 * Math.PI * i / nElements;
            double fX = fRingRadius * Math.Cos(fAngle);
            double fY = fRingRadius * Math.Sin(fAngle);

            // Outer injector (oxidizer)
            latHoles.AddBeam(
                new Vector3((float)fX, (float)fY, (float)(-fPlateThickness - 1)),
                new Vector3((float)fX, (float)fY, 1),
                (float)(fOuterDiam / 2),
                (float)(fOuterDiam / 2),
                bRoundCap: false);

            // Inner injector (fuel)
            latHoles.AddBeam(
                new Vector3((float)fX, (float)fY, (float)(-fPlateThickness - 2)),
                new Vector3((float)fX, (float)fY, 1),
                (float)(fInnerDiam / 2),
                (float)(fInnerDiam / 2),
                bRoundCap: false);
        }

        // Add central igniter port
        double fIgniterDiam = 4.0;
        latHoles.AddBeam(
            new Vector3(0, 0, (float)(-fPlateThickness - 1)),
            new Vector3(0, 0, 1),
            (float)(fIgniterDiam / 2),
            (float)(fIgniterDiam / 2),
            bRoundCap: false);

        voxPlate.BoolSubtract(new Voxels(latHoles));

        // Add film cooling orifices if enabled
        if (m_oSpec.FilmCooling)
        {
            voxPlate = AddFilmCoolingOrifices(voxPlate, fChamberRadius, fPlateThickness);
        }

        return voxPlate;
    }

    /// <summary>Build impinging doublet injector elements.</summary>
    Voxels voxBuildImpingingDoublet()
    {
        double fChamberRadius = MM(m_oDesign.ChamberRadius);
        double fPlateThickness = 6.0;
        double fOrificeDiam = m_oSpec.OrificeDiameterMM;
        int nElements = m_oSpec.ElementCount;

        // Base plate using Lattice
        Lattice latPlate = new(m_lib);
        latPlate.AddBeam(
            new Vector3(0, 0, (float)-fPlateThickness),
            new Vector3(0, 0, 0.5f),
            (float)fChamberRadius,
            (float)fChamberRadius,
            bRoundCap: false);

        Voxels voxPlate = new(latPlate);

        // Add impinging doublet pairs in ring pattern
        Lattice latHoles = new(m_lib);
        double fRingRadius = fChamberRadius * 0.6;
        for (int i = 0; i < nElements; i++)
        {
            double fAngle = 2 * Math.PI * i / nElements;
            double fX = fRingRadius * Math.Cos(fAngle);
            double fY = fRingRadius * Math.Sin(fAngle);

            // Pair of orifices at 60-degree impingement angle
            double fOffset = fOrificeDiam * 0.8;
            double fAngle1 = fAngle + Math.PI / 6;
            double fAngle2 = fAngle - Math.PI / 6;

            latHoles.AddBeam(
                new Vector3((float)(fX + fOffset * Math.Cos(fAngle1)), (float)(fY + fOffset * Math.Sin(fAngle1)), (float)(-fPlateThickness - 1)),
                new Vector3((float)(fX + fOffset * Math.Cos(fAngle1)), (float)(fY + fOffset * Math.Sin(fAngle1)), 1),
                (float)(fOrificeDiam / 2),
                (float)(fOrificeDiam / 2),
                bRoundCap: false);

            latHoles.AddBeam(
                new Vector3((float)(fX + fOffset * Math.Cos(fAngle2)), (float)(fY + fOffset * Math.Sin(fAngle2)), (float)(-fPlateThickness - 1)),
                new Vector3((float)(fX + fOffset * Math.Cos(fAngle2)), (float)(fY + fOffset * Math.Sin(fAngle2)), 1),
                (float)(fOrificeDiam / 2),
                (float)(fOrificeDiam / 2),
                bRoundCap: false);
        }

        // Add central igniter port
        double fIgniterDiam = 3.0;
        latHoles.AddBeam(
            new Vector3(0, 0, (float)(-fPlateThickness - 1)),
            new Vector3(0, 0, 1),
            (float)(fIgniterDiam / 2),
            (float)(fIgniterDiam / 2),
            bRoundCap: false);

        voxPlate.BoolSubtract(new Voxels(latHoles));

        // Add film cooling orifices if enabled
        if (m_oSpec.FilmCooling)
        {
            voxPlate = AddFilmCoolingOrifices(voxPlate, fChamberRadius, fPlateThickness);
        }

        return voxPlate;
    }

    /// <summary>Add film cooling orifice ring near chamber wall.</summary>
    Voxels AddFilmCoolingOrifices(Voxels voxPlate, double fChamberRadius, double fPlateThickness)
    {
        double fFilmDiam = m_oSpec.FilmOrificeDiameterMM;
        int nFilmOrifices = m_oSpec.FilmOrificeCount;
        double fFilmRingRadius = fChamberRadius * 0.85;

        Lattice latFilm = new(m_lib);
        for (int i = 0; i < nFilmOrifices; i++)
        {
            double fAngle = 2 * Math.PI * i / nFilmOrifices;
            double fX = fFilmRingRadius * Math.Cos(fAngle);
            double fY = fFilmRingRadius * Math.Sin(fAngle);

            latFilm.AddBeam(
                new Vector3((float)fX, (float)fY, (float)(-fPlateThickness - 1)),
                new Vector3((float)fX, (float)fY, 1),
                (float)(fFilmDiam / 2),
                (float)(fFilmDiam / 2),
                bRoundCap: false);
        }

        voxPlate.BoolSubtract(new Voxels(latFilm));
        return voxPlate;
    }
}

using System.Numerics;
using Impetus.Physics;
using PicoGK;

namespace Impetus.Geometry;

/// <summary>Regeneratively-cooled wall, injector plate, and mounting flange as separate solids.</summary>
public readonly record struct EngineComponents(Voxels Body, Voxels Injector, Voxels Flange);

/// <summary>
/// Turns an EngineDesign + NozzleContour into solid voxel geometry:
/// regeneratively-cooled chamber/nozzle wall, helical cooling channels with
/// inlet/outlet manifolds, showerhead injector plate and a bolted flange.
///
/// Geometry lives in mm (PicoGK convention), engine axis = +Z,
/// injector face at Z = 0, exhaust exits toward +Z.
/// </summary>
public class ThrusterBuilder
{
    readonly Library m_lib;
    readonly EngineDesign m_oDesign;
    readonly NozzleContour m_oContour;
    readonly double m_fWall;       // wall thickness [mm]
    readonly double m_fChanD;      // cooling channel diameter [mm]

    public ThrusterBuilder(Library lib, EngineDesign oDesign, NozzleContour oContour)
    {
        m_lib = lib;
        m_oDesign = oDesign;
        m_oContour = oContour;
        m_fChanD = oDesign.Spec.Cooling.DiameterMM;

        // The wall must swallow the channels and leave a sane liner + closeout
        m_fWall = Math.Max(oDesign.Spec.WallThicknessMM, m_fChanD + 1.6);
    }

    /// <summary>Build body, injector plate, and flange as separate voxel fields.</summary>
    public EngineComponents voxBuildComponents()
    {
        Console.WriteLine("  [geo] wall (revolve outer/inner)...");
        Voxels voxBody = voxWall();
        Console.WriteLine("  [geo] cooling channels...");
        Voxels voxChannels = voxCoolingChannels();
        Console.WriteLine("  [geo] subtract channels...");
        voxBody.BoolSubtract(voxChannels);

        InjectorDims oDims = GetInjectorDims();
        Console.WriteLine("  [geo] injector plate...");
        Voxels voxInjector = voxInjectorPlate(oDims);
        Console.WriteLine("  [geo] mounting flange...");
        Voxels voxMountFlange = voxMountingFlange(oDims);
        Console.WriteLine("  [geo] done");
        return new EngineComponents(voxBody, voxInjector, voxMountFlange);
    }

    /// <summary>Build the complete engine. Returns the solid voxel field.</summary>
    public Voxels voxBuild()
    {
        EngineComponents oParts = voxBuildComponents();
        Voxels voxEngine = new(oParts.Body);
        voxEngine.BoolAdd(oParts.Injector);
        voxEngine.BoolAdd(oParts.Flange);
        return voxEngine;
    }

    /// <summary>A cutaway (negative Y half removed) for inspecting internals.</summary>
    public Voxels voxCutaway(Voxels voxEngine)
    {
        BBox3 oBounds = voxEngine.oCalculateBoundingBox();
        Voxels voxCut = new(voxEngine);
        voxCut.BoolSubtract(new Voxels(m_lib, new HalfSpaceY(), oBounds));
        return voxCut;
    }

    // -------------------------------------------------------------------------

    /// <summary>Chamber + nozzle wall: revolved outer contour minus inner contour.</summary>
    Voxels voxWall()
    {
        List<ContourPoint> aoPts = m_oContour.Resampled(
            300, 0, m_oContour.ExitZ);

        // Outer surface: inner contour offset radially by the wall thickness.
        // (Radial offset, not normal offset - acceptable for shallow wall angles,
        // slightly thicker at the steep convergent which is conservative.)
        Voxels voxOuter = voxRevolve(aoPts, fROffsetMM: m_fWall, fZPadMM: 0);

        // Inner void: extended axially on both ends so the subtraction opens them
        Voxels voxInner = voxRevolve(aoPts, fROffsetMM: 0, fZPadMM: 2.0);

        voxOuter.BoolSubtract(voxInner);
        return voxOuter;
    }

    /// <summary>
    /// Revolve the contour around Z by stacking conical lattice beams between
    /// consecutive contour samples. Voxel union turns ~300 frusta into a smooth solid.
    /// </summary>
    Voxels voxRevolve(IReadOnlyList<ContourPoint> aoPts, double fROffsetMM, double fZPadMM)
    {
        Lattice lat = new(m_lib);

        for (int i = 1; i < aoPts.Count; i++)
        {
            Vector3 vecA = new(0, 0, MM(aoPts[i - 1].Z));
            Vector3 vecB = new(0, 0, MM(aoPts[i].Z));
            float fRA = (float)(MM(aoPts[i - 1].R) + fROffsetMM);
            float fRB = (float)(MM(aoPts[i].R) + fROffsetMM);
            lat.AddBeam(vecA, vecB, fRA, fRB, bRoundCap: false);
        }

        if (fZPadMM > 0)
        {
            // Extend straight stubs beyond both ends
            ContourPoint oFirst = aoPts[0];
            ContourPoint oLast = aoPts[^1];
            lat.AddBeam(
                new Vector3(0, 0, (float)(MM(oFirst.Z) - fZPadMM)),
                new Vector3(0, 0, MM(oFirst.Z)),
                (float)(MM(oFirst.R) + fROffsetMM),
                (float)(MM(oFirst.R) + fROffsetMM),
                bRoundCap: false);
            lat.AddBeam(
                new Vector3(0, 0, MM(oLast.Z)),
                new Vector3(0, 0, (float)(MM(oLast.Z) + fZPadMM)),
                (float)(MM(oLast.R) + fROffsetMM),
                (float)(MM(oLast.R) + fROffsetMM),
                bRoundCap: false);
        }

        return new Voxels(lat);
    }

    /// <summary>
    /// Helical cooling channels running inside the wall, mid-thickness,
    /// connected by toroidal supply/collector manifolds at both ends.
    /// </summary>
    Voxels voxCoolingChannels()
    {
        CoolingSpec oCool = m_oDesign.Spec.Cooling;
        double fZStart = 0.002;                       // just below injector face [m]
        double fZEnd = m_oContour.ExitZ * 0.94;       // stop before the exit lip
        double fRChan = m_fChanD / 2.0;               // [mm]
        double fMidOffset = m_fWall / 2.0;            // channel centerline depth [mm]

        List<ContourPoint> aoPath = m_oContour.Resampled(160, fZStart, fZEnd);
        Lattice lat = new(m_lib);

        for (int iChan = 0; iChan < oCool.Count; iChan++)
        {
            double fPhi0 = 2.0 * Math.PI * iChan / oCool.Count;
            Vector3? vecPrev = null;

            for (int i = 0; i < aoPath.Count; i++)
            {
                double fT = i / (aoPath.Count - 1.0);
                double fPhi = fPhi0 + 2.0 * Math.PI * oCool.HelixTurns * fT;
                double fRMid = MM(aoPath[i].R) + fMidOffset;

                Vector3 vec = new(
                    (float)(fRMid * Math.Cos(fPhi)),
                    (float)(fRMid * Math.Sin(fPhi)),
                    MM(aoPath[i].Z));

                if (vecPrev is Vector3 vecP)
                    lat.AddBeam(vecP, vec, (float)fRChan, (float)fRChan);
                vecPrev = vec;
            }
        }

        // Manifold rings at both ends (slightly fatter than the channels)
        AddManifoldRing(lat, aoPath[0], fMidOffset, fRChan * 1.3);
        AddManifoldRing(lat, aoPath[^1], fMidOffset, fRChan * 1.3);

        return new Voxels(lat);
    }

    void AddManifoldRing(Lattice lat, ContourPoint oAt, double fMidOffsetMM, double fRingRMM)
    {
        const int nSeg = 64;
        double fR = MM(oAt.R) + fMidOffsetMM;
        for (int i = 0; i < nSeg; i++)
        {
            double fA = 2.0 * Math.PI * i / nSeg;
            double fB = 2.0 * Math.PI * (i + 1) / nSeg;
            lat.AddBeam(
                new Vector3((float)(fR * Math.Cos(fA)), (float)(fR * Math.Sin(fA)), MM(oAt.Z)),
                new Vector3((float)(fR * Math.Cos(fB)), (float)(fR * Math.Sin(fB)), MM(oAt.Z)),
                (float)fRingRMM, (float)fRingRMM);
        }
    }

    readonly record struct InjectorDims(
        double PlateThicknessMM,
        double ChamberOuterRadiusMM,
        double FlangeOuterRadiusMM,
        double FlangeHeightMM,
        double ChamberRadiusMM);

    InjectorDims GetInjectorDims()
    {
        double fRcMM = MM(m_oDesign.ChamberRadius);
        double fROuter = fRcMM + m_fWall;
        return new InjectorDims(
            Math.Max(6.0, m_fWall * 2.0),
            fROuter,
            fROuter + 8.0,
            4.0,
            fRcMM);
    }

    /// <summary>Showerhead injector plate (orifices only — no flange ring or bolt holes).</summary>
    Voxels voxInjectorPlate(InjectorDims oDims)
    {
        Lattice latPlate = new(m_lib);
        latPlate.AddBeam(
            new Vector3(0, 0, (float)-oDims.PlateThicknessMM),
            new Vector3(0, 0, 0.5f),
            (float)oDims.ChamberOuterRadiusMM,
            (float)oDims.ChamberOuterRadiusMM,
            bRoundCap: false);

        Voxels voxPlate = new(latPlate);
        Lattice latHoles = new(m_lib);

        AddOrificeRing(latHoles, m_oDesign.FuelOrificeCount,
            m_oDesign.FuelOrificeDiameter, 0.72 * oDims.ChamberRadiusMM, oDims.PlateThicknessMM);
        AddOrificeRing(latHoles, m_oDesign.OxOrificeCount,
            m_oDesign.OxOrificeDiameter, 0.42 * oDims.ChamberRadiusMM, oDims.PlateThicknessMM);

        latHoles.AddBeam(
            new Vector3(0, 0, (float)(-oDims.PlateThicknessMM - 1)),
            new Vector3(0, 0, 2),
            1.5f, 1.5f, bRoundCap: false);

        voxPlate.BoolSubtract(new Voxels(latHoles));
        return voxPlate;
    }

    /// <summary>Bolted mounting flange (annular ring with clearance holes).</summary>
    Voxels voxMountingFlange(InjectorDims oDims)
    {
        Lattice latFlange = new(m_lib);
        latFlange.AddBeam(
            new Vector3(0, 0, (float)-oDims.PlateThicknessMM),
            new Vector3(0, 0, (float)(-oDims.PlateThicknessMM + oDims.FlangeHeightMM)),
            (float)oDims.FlangeOuterRadiusMM,
            (float)oDims.FlangeOuterRadiusMM,
            bRoundCap: false);

        Voxels voxFlange = new(latFlange);

        Lattice latBore = new(m_lib);
        latBore.AddBeam(
            new Vector3(0, 0, (float)(-oDims.PlateThicknessMM - 1)),
            new Vector3(0, 0, 1),
            (float)oDims.ChamberOuterRadiusMM,
            (float)oDims.ChamberOuterRadiusMM,
            bRoundCap: false);
        voxFlange.BoolSubtract(new Voxels(latBore));

        Lattice latBolts = new(m_lib);
        const int nBolts = 8;
        double fRBoltCircle = oDims.ChamberOuterRadiusMM + 4.0;
        for (int i = 0; i < nBolts; i++)
        {
            double fA = 2.0 * Math.PI * i / nBolts;
            float fX = (float)(fRBoltCircle * Math.Cos(fA));
            float fY = (float)(fRBoltCircle * Math.Sin(fA));
            latBolts.AddBeam(
                new Vector3(fX, fY, (float)(-oDims.PlateThicknessMM - 1)),
                new Vector3(fX, fY, 1),
                2.1f, 2.1f, bRoundCap: false);
        }

        voxFlange.BoolSubtract(new Voxels(latBolts));
        return voxFlange;
    }

    void AddOrificeRing(Lattice lat, int nCount, double fDiaM, double fRingRMM, double fPlateT)
    {
        // Cap how many orifices physically fit on the ring at 1.6x diameter spacing
        double fDiaMM = fDiaM * 1000.0;
        int nMax = (int)(2.0 * Math.PI * fRingRMM / (1.6 * fDiaMM));
        int n = Math.Min(nCount, Math.Max(4, nMax));

        for (int i = 0; i < n; i++)
        {
            double fA = 2.0 * Math.PI * i / n;
            float fX = (float)(fRingRMM * Math.Cos(fA));
            float fY = (float)(fRingRMM * Math.Sin(fA));
            lat.AddBeam(
                new Vector3(fX, fY, (float)(-fPlateT - 1)),
                new Vector3(fX, fY, 1),
                (float)(fDiaMM / 2.0), (float)(fDiaMM / 2.0),
                bRoundCap: false);
        }
    }

    static float MM(double fMeters) => (float)(fMeters * 1000.0);

    /// <summary>Implicit half-space y &lt;= 0 (used to cut the engine open).</summary>
    class HalfSpaceY : IImplicit
    {
        public float fSignedDistance(in Vector3 vec) => vec.Y;
    }
}

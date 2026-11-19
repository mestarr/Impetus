using Impetus.Physics;
using PicoGK;

namespace Impetus.Geometry;

/// <summary>
/// Chamber-pressure boss on the cylinder, and a tap on the coolant inlet and
/// the coolant outlet. Each hole reaches the fluid it measures and opens at
/// the boss face, where the sensor seals it.
/// </summary>
public static class SensorPorts
{
    public const double BoreR = 0.7;
    public const double BossR = 1.8;
    public const double BossLen = 4.0;

    public readonly record struct Placement(
        double ChamberZ,
        double ChamberAngle,
        double ChannelAngle,
        double InnerR,
        double OuterR,
        double ChamberTipR,
        double InletR,
        double InletZ,
        double OutletR,
        double OutletZ,
        double CoolantAngle,
        double CoolantSolidR);

    public static Placement Locate(
        IReadOnlyList<ContourPoint> aoPath,
        CoolantFittings.Layout oFit,
        EngineDesign oDesign,
        double fWallMM)
    {
        double fCylEnd = oDesign.ChamberCylinderLength;
        double fFwd = aoPath[0].Z;
        double fTarget = fFwd + 0.016;
        if (fTarget > fCylEnd - 0.003)
            fTarget = fFwd + 0.5 * Math.Max(0.004, fCylEnd - fFwd);

        int iBest = 0;
        double fBest = double.MaxValue;
        for (int i = 0; i < aoPath.Count; i++)
        {
            if (aoPath[i].Z > fCylEnd - 0.002)
                break;
            double fD = Math.Abs(aoPath[i].Z - fTarget);
            if (fD < fBest)
            {
                fBest = fD;
                iBest = i;
            }
        }

        double fT = iBest / (double)Math.Max(1, aoPath.Count - 1);
        int nChan = Math.Max(4, oDesign.Spec.Cooling.Count);
        double fChannel = 2.0 * Math.PI * oDesign.Spec.Cooling.HelixTurns * fT;
        double fTap = fChannel + Math.PI / nChan;
        double fInner = aoPath[iBest].R * 1000.0;
        double fOuter = fInner + fWallMM;

        return new Placement(
            aoPath[iBest].Z * 1000.0,
            fTap,
            fChannel,
            fInner,
            fOuter,
            fOuter + BossLen,
            oFit.AftOuterR + 5.5,
            oFit.AftZ,
            oFit.ForwardOuterR + 1.2,
            oFit.ForwardZ,
            oFit.AngleRad,
            oFit.SolidR);
    }

    public static void Add(Library lib, Voxels voxBody, Placement o)
    {
        Console.WriteLine("  [geo] chamber-pressure port...");
        AddRadial(
            lib, voxBody,
            o.InnerR - 0.8, o.OuterR - 0.5, o.ChamberTipR, o.ChamberTipR + 0.7,
            o.ChamberZ, o.ChamberAngle);

        Console.WriteLine("  [geo] coolant sensor taps...");
        AddAxial(lib, voxBody, o.InletR, o.InletZ, o.CoolantAngle, o.CoolantSolidR);
        AddAxial(lib, voxBody, o.OutletR, o.OutletZ, o.CoolantAngle, o.CoolantSolidR);
    }

    static void AddRadial(
        Library lib, Voxels vox,
        double fGasR, double fSolidInnerR, double fSolidOuterR, double fBoreOuterR,
        double fZ, double fAngle)
    {
        Lattice latSolid = new(lib);
        latSolid.AddBeam(
            CoolantFittings.At(fSolidInnerR, fZ, fAngle),
            CoolantFittings.At(fSolidOuterR, fZ, fAngle),
            (float)BossR, (float)BossR, bRoundCap: false);
        vox.BoolAdd(new Voxels(latSolid));

        Lattice latBore = new(lib);
        latBore.AddBeam(
            CoolantFittings.At(fGasR, fZ, fAngle),
            CoolantFittings.At(fBoreOuterR, fZ, fAngle),
            (float)BoreR, (float)BoreR, bRoundCap: false);
        vox.BoolSubtract(new Voxels(latBore));
    }

    static void AddAxial(Library lib, Voxels vox, double fR, double fZ, double fAngle, double fHostR)
    {
        double fZ0 = fZ + fHostR - 0.4;
        double fZ1 = fZ + fHostR + BossLen;
        Lattice latSolid = new(lib);
        latSolid.AddBeam(
            CoolantFittings.At(fR, fZ0, fAngle),
            CoolantFittings.At(fR, fZ1, fAngle),
            (float)BossR, (float)BossR, bRoundCap: false);
        vox.BoolAdd(new Voxels(latSolid));

        Lattice latBore = new(lib);
        latBore.AddBeam(
            CoolantFittings.At(fR, fZ, fAngle),
            CoolantFittings.At(fR, fZ1 + 0.7, fAngle),
            (float)BoreR, (float)BoreR, bRoundCap: false);
        vox.BoolSubtract(new Voxels(latBore));
    }
}

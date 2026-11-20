using System.Numerics;
using PicoGK;

namespace Impetus.Geometry;

/// <summary>
/// Spark torch that sits in the injector's center port. A short nozzle aims
/// into the chamber. Two radial inlets feed the torch. The spark-plug boss is
/// a blind hole, so the plug seals it. This is not a pyrotechnic charge.
/// </summary>
public static class SparkTorch
{
    public static double PortDiameterMM(InjectorType eType)
        => eType == InjectorType.CoaxialSwirl ? 4.0 : 3.0;

    public readonly record struct Shape(
        double NozzleOuterR,
        double NozzleBoreR,
        double TipZ,
        double CavityZ,
        double BodyR,
        double InletTipR,
        double SparkFaceR,
        double InletBoreR,
        double SparkBoreR);

    public static Shape Measure(double fPlateThicknessMM, double fPortDiameterMM)
    {
        double fPortR = fPortDiameterMM / 2.0;
        double fNozzleR = Math.Max(1.05, fPortR - 0.25);
        double fBoreR = Math.Min(0.85, fNozzleR - 0.55);
        return new Shape(
            fNozzleR,
            fBoreR,
            4.0,
            PropellantDomes.BackZ(fPlateThicknessMM) - 8.0,
            5.5,
            5.5 + 4.2,
            5.5 + 5.0,
            0.7,
            1.0);
    }

    public static Voxels voxBuild(Library lib, double fPlateThicknessMM, double fPortDiameterMM)
    {
        Console.WriteLine("  [geo] spark torch...");
        Shape o = Measure(fPlateThicknessMM, fPortDiameterMM);
        double fBack = PropellantDomes.BackZ(fPlateThicknessMM);
        double fSeat = fBack - 0.6;
        double fAft = fBack - 14.0;
        double fCavityFront = o.CavityZ + 3.6;
        double fCavityAft = o.CavityZ - 3.6;

        Voxels vox = voxCylinder(lib, 0, 0, o.TipZ - 0.8, fCavityFront - 0.4, o.NozzleOuterR);
        vox.BoolAdd(voxCylinder(lib, 0, 0, fSeat, fAft, o.BodyR));
        AddBoss(vox, lib, o.CavityZ, Math.PI / 2.0, o.BodyR - 0.4, o.InletTipR, o.InletBoreR + 1.1);
        AddBoss(vox, lib, o.CavityZ, -Math.PI / 2.0, o.BodyR - 0.4, o.InletTipR, o.InletBoreR + 1.1);
        AddBoss(vox, lib, o.CavityZ, Math.PI, o.BodyR - 0.4, o.SparkFaceR, o.SparkBoreR + 1.6);

        vox.BoolSubtract(voxCylinder(lib, 0, 0, fCavityFront, fCavityAft, 3.0));
        vox.BoolSubtract(voxCylinder(lib, 0, 0, o.TipZ + 0.6, o.CavityZ, o.NozzleBoreR));
        SubtractRadialBore(vox, lib, o.CavityZ, Math.PI / 2.0, 1.6, o.InletTipR + 0.8, o.InletBoreR, bOpenOuter: true);
        SubtractRadialBore(vox, lib, o.CavityZ, -Math.PI / 2.0, 1.6, o.InletTipR + 0.8, o.InletBoreR, bOpenOuter: true);
        SubtractRadialBore(vox, lib, o.CavityZ, Math.PI, 1.6, o.SparkFaceR - 1.6, o.SparkBoreR, bOpenOuter: false);
        return vox;
    }

    static void AddBoss(Voxels vox, Library lib, double fZ, double fAngle, double fR0, double fR1, double fBossR)
    {
        Lattice lat = new(lib);
        lat.AddBeam(At(fR0, fZ, fAngle), At(fR1, fZ, fAngle), (float)fBossR, (float)fBossR, bRoundCap: false);
        vox.BoolAdd(new Voxels(lat));
    }

    static void SubtractRadialBore(
        Voxels vox, Library lib, double fZ, double fAngle, double fR0, double fR1, double fBoreR, bool bOpenOuter)
    {
        Lattice lat = new(lib);
        lat.AddBeam(
            At(fR0, fZ, fAngle),
            At(fR1, fZ, fAngle),
            (float)fBoreR, (float)fBoreR,
            bRoundCap: bOpenOuter);
        vox.BoolSubtract(new Voxels(lat));
    }

    static Vector3 At(double fR, double fZ, double fAngle)
        => new((float)(fR * Math.Cos(fAngle)), (float)(fR * Math.Sin(fAngle)), (float)fZ);

    static Voxels voxCylinder(Library lib, double fX, double fY, double fZ0, double fZ1, double fR)
    {
        Lattice lat = new(lib);
        lat.AddBeam(
            new Vector3((float)fX, (float)fY, (float)fZ0),
            new Vector3((float)fX, (float)fY, (float)fZ1),
            (float)fR, (float)fR,
            bRoundCap: false);
        return new Voxels(lat);
    }
}

using System.Numerics;
using Impetus.Physics;
using PicoGK;

namespace Impetus.Geometry;

/// <summary>
/// Fuel inlet nipple on the nozzle-end cooling ring, and a fuel-out pipe from
/// the injector-end ring into the fuel dome. The nipple and the short outlet
/// stub are part of the body. The pipe that finishes the run into the dome is
/// part of the injector, and the two bores overlap when the engine is assembled.
/// </summary>
public static class CoolantFittings
{
    public const double AngleDeg = 22.5;
    public const double NippleMM = 8.0;
    public const double SkinMM = 1.6;

    public readonly record struct Layout(
        double AngleRad,
        double BoreR,
        double SolidR,
        double ForwardZ,
        double ForwardMidR,
        double ForwardOuterR,
        double AftZ,
        double AftMidR,
        double AftOuterR,
        double InletTipR,
        double StubOuterR,
        double PipeR,
        double FuelZ,
        double FuelEntryR);

    public static Layout Make(
        ContourPoint oForward,
        ContourPoint oAft,
        double fWallMM,
        double fChannelDiameterMM,
        double fPlateThicknessMM,
        double fPlateRadiusMM,
        double fFlangeOuterR)
    {
        double fBore = Math.Max(0.7, fChannelDiameterMM * 0.5 * 1.3);
        double fSolid = fBore + SkinMM;
        double fAngle = AngleDeg * Math.PI / 180.0;
        double fFwdInner = oForward.R * 1000.0;
        double fAftInner = oAft.R * 1000.0;
        double fEntry = Math.Clamp(fPlateRadiusMM * 0.45, 6.0, fPlateRadiusMM - PropellantDomes.WallMM - 1.2);

        return new Layout(
            fAngle,
            fBore,
            fSolid,
            oForward.Z * 1000.0,
            fFwdInner + fWallMM * 0.5,
            fFwdInner + fWallMM,
            oAft.Z * 1000.0,
            fAftInner + fWallMM * 0.5,
            fAftInner + fWallMM,
            fAftInner + fWallMM + NippleMM,
            fFwdInner + fWallMM + fSolid + 1.0,
            fFlangeOuterR + fSolid + 1.0,
            PropellantDomes.FuelMidZ(fPlateThicknessMM),
            fEntry);
    }

    public static void AddToBody(Library lib, Voxels voxBody, Layout o)
    {
        Console.WriteLine("  [geo] coolant inlet nipple...");
        AddJacket(
            lib, voxBody,
            o.AftMidR, o.AftOuterR - 0.8, o.InletTipR, o.InletTipR + 1.0,
            o.AftZ, o.AngleRad, o.BoreR, o.SolidR);

        Console.WriteLine("  [geo] coolant outlet stub...");
        AddJacket(
            lib, voxBody,
            o.ForwardMidR, o.ForwardOuterR - 0.8, o.StubOuterR, o.StubOuterR + 0.6,
            o.ForwardZ, o.AngleRad, o.BoreR, o.SolidR);
    }

    public static void AddToInjector(Library lib, Voxels voxInjector, Layout o, double fPlateRadiusMM)
    {
        Console.WriteLine("  [geo] coolant outlet into the fuel dome...");
        float fSolid = (float)o.SolidR;
        float fBore = (float)o.BoreR;
        Vector3 vecStub = At(o.ForwardOuterR + 0.5, o.ForwardZ, o.AngleRad);
        Vector3 vecBend = At(o.PipeR, o.ForwardZ, o.AngleRad);
        Vector3 vecDrop = At(o.PipeR, o.FuelZ, o.AngleRad);
        Vector3 vecDome = At(fPlateRadiusMM - 0.8, o.FuelZ, o.AngleRad);

        Lattice latSolid = new(lib);
        latSolid.AddBeam(vecStub, vecBend, fSolid, fSolid, bRoundCap: false);
        latSolid.AddBeam(vecBend, vecDrop, fSolid, fSolid, bRoundCap: true);
        float fNeck = fBore + 1.0f;
        latSolid.AddBeam(vecDrop, vecDome, fNeck, fNeck, bRoundCap: false);
        voxInjector.BoolAdd(new Voxels(latSolid));

        Lattice latBore = new(lib);
        latBore.AddBeam(At(o.ForwardOuterR + 0.2, o.ForwardZ, o.AngleRad), vecBend, fBore, fBore, bRoundCap: false);
        latBore.AddBeam(vecBend, vecDrop, fBore, fBore, bRoundCap: true);
        latBore.AddBeam(vecDrop, At(o.FuelEntryR, o.FuelZ, o.AngleRad), fBore, fBore, bRoundCap: false);
        voxInjector.BoolSubtract(new Voxels(latBore));
    }

    /// <summary>Radial boss. The bore runs from the manifold center out past the solid tip.</summary>
    static void AddJacket(
        Library lib, Voxels vox,
        double fManifoldR, double fSolidInnerR, double fSolidOuterR, double fBoreOuterR,
        double fZ, double fAngle, double fBoreR, double fSolidR)
    {
        Lattice latSolid = new(lib);
        latSolid.AddBeam(
            At(fSolidInnerR, fZ, fAngle),
            At(fSolidOuterR, fZ, fAngle),
            (float)fSolidR, (float)fSolidR, bRoundCap: false);
        vox.BoolAdd(new Voxels(latSolid));

        Lattice latBore = new(lib);
        latBore.AddBeam(
            At(fManifoldR, fZ, fAngle),
            At(fBoreOuterR, fZ, fAngle),
            (float)fBoreR, (float)fBoreR, bRoundCap: true);
        vox.BoolSubtract(new Voxels(latBore));
    }

    public static Vector3 At(double fR, double fZ, double fAngle)
        => new((float)(fR * Math.Cos(fAngle)), (float)(fR * Math.Sin(fAngle)), (float)fZ);
}

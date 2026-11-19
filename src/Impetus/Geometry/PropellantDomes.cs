using System.Numerics;
using PicoGK;

namespace Impetus.Geometry;

/// <summary>
/// One injector passage. A coaxial element sets both diameters at the same point:
/// the larger is the oxidizer annulus, the smaller is the fuel hole inside the post.
/// </summary>
public readonly record struct InjectorPort(double Xmm, double Ymm, double FuelDiameterMM, double OxDiameterMM);

/// <summary>
/// Two manifolds on the back of the injector plate. The oxidizer volume sits
/// against the plate. The fuel volume sits behind a splitter. Fuel holes cross
/// the oxidizer volume inside a post, so the two propellants meet only in the
/// chamber. The igniter port runs through the center and stays sealed from both.
/// </summary>
public static class PropellantDomes
{
    public const double OxDepthMM = 6.0;
    public const double FuelDepthMM = 6.0;
    public const double SplitterMM = 1.6;
    public const double WallMM = 1.6;
    public const double PostWallMM = 0.8;
    public const double AnnulusMM = 0.8;

    /// <summary>Outside face of the fuel dome, behind the plate. Plate back is -thickness.</summary>
    public static double BackZ(double fPlateThicknessMM)
        => -fPlateThicknessMM - OxDepthMM - SplitterMM - FuelDepthMM - WallMM;

    public static Voxels voxAttach(
        Library lib,
        Voxels voxPlate,
        double fPlateThicknessMM,
        double fPlateRadiusMM,
        double fIgniterDiameterMM,
        IReadOnlyList<InjectorPort> aoPorts)
    {
        Console.WriteLine("  [geo] propellant domes (oxidizer against the plate, fuel behind)...");

        double fBack = -fPlateThicknessMM;
        double fOxFront = fBack + 1.0;
        double fOxBack = fBack - OxDepthMM;
        double fFuelFront = fOxBack - SplitterMM;
        double fFuelBack = fFuelFront - FuelDepthMM;
        double fShellBack = fFuelBack - WallMM;
        double fSleeveR = fIgniterDiameterMM / 2.0 + WallMM;
        double fCavityR = Math.Max(fSleeveR + 1.0, fPlateRadiusMM - WallMM);

        Voxels vox = new(voxPlate);
        vox.BoolAdd(voxCylinder(lib, 0, 0, fBack + 0.8, fShellBack, fPlateRadiusMM));
        SubtractAnnulus(vox, lib, fOxFront, fOxBack, fSleeveR, fCavityR);
        SubtractAnnulus(vox, lib, fFuelFront, fFuelBack, fSleeveR, fCavityR);

        double fOxStop = (fOxFront + fOxBack) * 0.5;
        double fFuelStop = (fFuelFront + fFuelBack) * 0.5;
        bool bLoggedWiden = false;

        foreach (InjectorPort oPort in aoPorts)
        {
            bool bFuel = oPort.FuelDiameterMM > 0.2;
            bool bOx = oPort.OxDiameterMM > 0.2;
            if (!bFuel && !bOx)
                continue;

            double fFuelR = bFuel ? oPort.FuelDiameterMM / 2.0 : 0;
            double fOxR = bOx ? oPort.OxDiameterMM / 2.0 : 0;
            if (bFuel && bOx)
            {
                double fNeeded = fFuelR + PostWallMM + AnnulusMM;
                if (fNeeded > fOxR + 0.05 && !bLoggedWiden)
                {
                    Console.WriteLine(
                        $"  [geo] coaxial annulus widened to Ø{2 * fNeeded:F1} mm so the fuel post stays sealed");
                    bLoggedWiden = true;
                }
                fOxR = Math.Max(fOxR, fNeeded);
            }

            if (bOx)
                vox.BoolSubtract(voxCylinder(lib, oPort.Xmm, oPort.Ymm, 2.0, fOxStop, fOxR));

            if (bFuel)
            {
                double fPostR = fFuelR + PostWallMM;
                vox.BoolAdd(voxCylinder(lib, oPort.Xmm, oPort.Ymm, 0.5, fFuelStop, fPostR));
                vox.BoolSubtract(voxCylinder(lib, oPort.Xmm, oPort.Ymm, 2.0, fFuelStop, fFuelR));
            }
        }

        if (fIgniterDiameterMM > 0.2)
            vox.BoolSubtract(voxCylinder(lib, 0, 0, 2.0, fShellBack - 1.0, fIgniterDiameterMM / 2.0));

        return vox;
    }

    static void SubtractAnnulus(Voxels vox, Library lib, double fZ0, double fZ1, double fRInner, double fROuter)
    {
        if (fROuter <= fRInner + 0.2)
            return;

        Voxels voxTool = voxCylinder(lib, 0, 0, fZ0, fZ1, fROuter);
        voxTool.BoolSubtract(voxCylinder(lib, 0, 0, fZ0 - 0.6, fZ1 + 0.6, fRInner));
        vox.BoolSubtract(voxTool);
    }

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

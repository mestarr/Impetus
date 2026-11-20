using System.Numerics;
using Impetus.Physics;
using PicoGK;

namespace Impetus.Geometry;

/// <summary>
/// A round plate around the two tanks, thin at the rim and thick enough in
/// the middle to hold them. A short fin sits on top. The engine is at the
/// back. This is still a shape, not a flight simulation.
/// </summary>
public static class RocketBody
{
    /// <summary>Tank capacity divided by the propellant burned. Matches the feed checklist.</summary>
    public const double CapacityFactor = 1.5;

    /// <summary>
    /// Screening allowable for an aluminum tank wall, about a third of 6061-T6 yield.
    /// A real tank needs a qualified material, welds, and a proof test.
    /// </summary>
    public const double AllowablePa = 80e6;

    public const double MinWallMM = 2.2;
    public const double SkinMM = 2.2;
    public const double GapMM = 3.0;
    public const double BayMM = 12.0;
    public const double TankOuterMM = 40.0;
    public const string WingSection = "2410";
    public const string TailSection = "0009";

    public readonly record struct Tank(
        string Name,
        double MassKg,
        double CapacityKg,
        double DensityKgM3,
        double PressureBar,
        double WallMM,
        double InnerR,
        double OuterR,
        double CenterX,
        double CylinderMM,
        double AftTipZ,
        double FwdTipZ,
        double VolumeM3);

    public readonly record struct Shape(
        double BurnS,
        double BodyHalfW,
        double BodyHalfH,
        double WingSpan,
        double RootChord,
        double TipChord,
        double SweepMM,
        double WingZ0,
        double WingZ1,
        double TailSpan,
        double TailChord,
        double TailZ0,
        double TailZ1,
        double FinHeight,
        double NoseTipZ,
        double NoseBaseZ,
        double MountZ,
        double EngineFairR,
        double ExitZ,
        Tank Fuel,
        Tank Ox);

    public static Shape Size(EngineDesign oDesign, FeedSystemSizing oFeed)
    {
        double fEngineWall = Math.Max(oDesign.Spec.WallThicknessMM, oDesign.Spec.Cooling.DiameterMM + 1.6);
        double fPlate = oDesign.Spec.Injector.Type switch
        {
            InjectorType.CoaxialSwirl => InjectorBuilder.CoaxialPlateMM,
            InjectorType.ImpingingDoublet => InjectorBuilder.ImpingingPlateMM,
            _ => Math.Max(6.0, fEngineWall * 2.0)
        };

        double fCenter = TankOuterMM + 4.0;
        double fHalfH = TankOuterMM + GapMM + SkinMM + 6.0;

        Tank oFuel = Place(MakeTank(
            "Fuel", oFeed.FuelMassKg, oDesign.Gas.FuelDensity, oFeed.FuelTankPressureBar, TankOuterMM), fCenter);
        Tank oOx = Place(MakeTank(
            "Oxidizer", oFeed.OxMassKg, oDesign.Gas.OxDensity, oFeed.OxTankPressureBar, TankOuterMM), -fCenter);

        double fAft = PropellantDomes.BackZ(fPlate) - BayMM;
        oFuel = oFuel with { AftTipZ = fAft, FwdTipZ = fAft - Length(oFuel) };
        oOx = oOx with { AftTipZ = fAft, FwdTipZ = fAft - Length(oOx) };

        double fEngineR = oDesign.ChamberRadius * 1000.0 + fEngineWall;
        double fMidZ = 0.5 * (Math.Min(oFuel.FwdTipZ, oOx.FwdTipZ) + Math.Max(oFuel.AftTipZ, oOx.AftTipZ));
        double fHub = 0.0;
        foreach (Tank oTank in new[] { oFuel, oOx })
        {
            double fDx = Math.Abs(oTank.CenterX) + oTank.OuterR + 16.0;
            fHub = Math.Max(fHub, Math.Sqrt(fDx * fDx + Math.Pow(oTank.FwdTipZ - fMidZ, 2)));
            fHub = Math.Max(fHub, Math.Sqrt(fDx * fDx + Math.Pow(oTank.AftTipZ - fMidZ, 2)));
        }

        const double fThickFrac = 0.70;
        double fRadius = fHub / fThickFrac;
        double fTailChord = 80.0;
        double fTail0 = fMidZ + fHub * 0.35;
        return new Shape(
            oFeed.BurnDurationS,
            fRadius,
            fHalfH,
            fRadius * 2.0,
            fRadius * 2.0,
            fRadius * 0.2,
            0.0,
            fMidZ - fRadius,
            fMidZ + fRadius * 0.2,
            0.0,
            fTailChord,
            fTail0,
            fTail0 + fTailChord,
            70.0,
            fMidZ - fRadius,
            fMidZ,
            fMidZ + fRadius,
            fEngineR,
            0,
            oFuel,
            oOx);
    }

    public static Shape WithExit(Shape oShape, double fExitZ)
        => oShape with { ExitZ = fExitZ };

    public static Voxels voxTank(Library lib, Tank oTank)
    {
        double fAftCenter = oTank.AftTipZ - oTank.OuterR;
        double fFwdCenter = oTank.FwdTipZ + oTank.OuterR;
        Voxels voxOuter = voxLathe(lib, Profile(fAftCenter, fFwdCenter, oTank.OuterR), oTank.CenterX);
        Voxels voxInner = voxLathe(lib, Profile(fAftCenter, fFwdCenter, oTank.InnerR), oTank.CenterX);
        voxOuter.BoolSubtract(voxInner);
        return voxOuter;
    }

    public static Voxels voxSkin(Library lib, Shape oShape)
    {
        Lattice latOuter = new(lib);
        AddStreamBody(latOuter, oShape);
        Lattice latInner = new(lib);
        double fBayW = Math.Abs(oShape.Fuel.CenterX) + oShape.Fuel.OuterR + 6.0;
        double fBay0 = Math.Min(oShape.Fuel.FwdTipZ, oShape.Ox.FwdTipZ) + oShape.Fuel.OuterR;
        double fBay1 = Math.Max(oShape.Fuel.AftTipZ, oShape.Ox.AftTipZ) - oShape.Fuel.OuterR;
        AddFilled(latInner, fBay0, fBay1, fBayW, oShape.BodyHalfH - 6.0, fBayW, oShape.BodyHalfH - 6.0);
        Voxels vox = new(latOuter);
        vox.BoolSubtract(new Voxels(latInner));
        return vox;
    }

    public static Voxels voxWing(Library lib, Shape oShape)
    {
        List<(double X, double Y)> ao = NacaLoop(WingSection, 36);
        Lattice lat = new(lib);
        LoftSpan(lat, ao, -oShape.WingSpan / 2.0, oShape.WingSpan / 2.0, 16, 2.2f, (fX, fXn, fYn) =>
        {
            double fFrac = Math.Abs(fX) / (oShape.WingSpan / 2.0);
            double fChord = oShape.RootChord + (oShape.TipChord - oShape.RootChord) * fFrac;
            double fZ = oShape.WingZ0 + fFrac * oShape.SweepMM + fXn * fChord;
            return new Vector3((float)fX, (float)(fYn * fChord), (float)fZ);
        });
        return new Voxels(lat);
    }

    public static Voxels voxTail(Library lib, Shape oShape)
    {
        List<(double X, double Y)> ao = NacaLoop(TailSection, 18);
        Lattice lat = new(lib);
        LoftSpan(lat, ao, -oShape.TailSpan / 2.0, oShape.TailSpan / 2.0, 10, 2.0f, (fX, fXn, fYn) =>
        {
            double fFrac = Math.Abs(fX) / (oShape.TailSpan / 2.0);
            double fChord = oShape.TailChord * (1.0 - 0.35 * fFrac);
            double fZ = oShape.TailZ0 + fFrac * 40.0 + fXn * fChord;
            return new Vector3((float)fX, (float)(fYn * fChord), (float)fZ);
        });
        return new Voxels(lat);
    }

    public static Voxels voxFin(Library lib, Shape oShape)
    {
        List<(double X, double Y)> ao = NacaLoop(TailSection, 16);
        Lattice lat = new(lib);
        const int n = 8;
        List<Vector3>? aoPrev = null;
        for (int i = 0; i <= n; i++)
        {
            double fFrac = i / (double)n;
            double fY = oShape.BodyHalfH + fFrac * oShape.FinHeight;
            double fChord = oShape.TailChord * (1.0 - 0.45 * fFrac);
            List<Vector3> aoCur = [];
            foreach ((double fXn, double fYn) in ao)
            {
                double fZ = oShape.TailZ0 + fFrac * 36.0 + fXn * fChord;
                aoCur.Add(new Vector3((float)(fYn * fChord), (float)fY, (float)fZ));
            }

            Join(lat, aoPrev, aoCur, 2.0f);
            aoPrev = aoCur;
        }

        return new Voxels(lat);
    }

    public static Voxels voxCutOpen(Library lib, Voxels vox)
    {
        BBox3 oBounds = vox.oCalculateBoundingBox();
        Voxels voxCut = new(vox);
        voxCut.BoolSubtract(new Voxels(lib, new HalfSpaceY(), oBounds));
        return voxCut;
    }

    static Tank MakeTank(string strName, double fMassKg, double fDensity, double fPressureBar, double fOuterR)
    {
        double fPressure = fPressureBar * 1e5;
        double fOuterM = fOuterR / 1000.0;
        double fWallM = fPressure * fOuterM / (AllowablePa + fPressure);
        double fWall = Math.Max(MinWallMM, fWallM * 1000.0);
        double fInner = fOuterR - fWall;
        double fInnerM = fInner / 1000.0;
        double fNeed = CapacityFactor * fMassKg / fDensity;
        double fSphere = 4.0 / 3.0 * Math.PI * fInnerM * fInnerM * fInnerM;
        double fCylM = Math.Max(0.0, (fNeed - fSphere) / (Math.PI * fInnerM * fInnerM));
        double fVolume = fSphere + Math.PI * fInnerM * fInnerM * fCylM;
        return new Tank(
            strName, fMassKg, fVolume * fDensity, fDensity, fPressureBar, fWall, fInner, fOuterR,
            0, fCylM * 1000.0, 0, 0, fVolume);
    }

    static Tank Place(Tank oTank, double fCenterX)
        => oTank with { CenterX = fCenterX };

    static double Length(Tank oTank)
        => oTank.CylinderMM + 2.0 * oTank.OuterR;

    static void AddStadium(Lattice lat, double fZ0, double fZ1, double fHalfW, double fHalfH)
        => AddStadium(lat, fZ0, fZ1, fHalfW, fHalfH, fHalfW, fHalfH);

    static void AddStadium(
        Lattice lat, double fZ0, double fZ1,
        double fHalfW0, double fHalfH0, double fHalfW1, double fHalfH1)
    {
        double fLobe0 = Math.Min(fHalfH0, Math.Max(2.0, fHalfW0 - 1.0));
        double fLobe1 = Math.Min(fHalfH1, Math.Max(2.0, fHalfW1 - 1.0));
        double fX0 = Math.Max(0.0, fHalfW0 - fLobe0);
        double fX1 = Math.Max(0.0, fHalfW1 - fLobe1);
        lat.AddBeam(
            new Vector3((float)-fX0, 0, (float)fZ0),
            new Vector3((float)-fX1, 0, (float)fZ1),
            (float)fLobe0, (float)fLobe1, bRoundCap: false);
        if (fX0 > 0.4 || fX1 > 0.4)
        {
            lat.AddBeam(
                new Vector3((float)fX0, 0, (float)fZ0),
                new Vector3((float)fX1, 0, (float)fZ1),
                (float)fLobe0, (float)fLobe1, bRoundCap: false);
        }
    }

    /// <summary>
    /// NACA 4-digit section, closed from the trailing edge over the top and back
    /// underneath. Coefficients are from NACA Report 460.
    /// </summary>
    public static List<(double X, double Upper, double Lower)> NacaSides(string strCode, int n)
    {
        double fM = (strCode[0] - '0') / 100.0;
        double fP = (strCode[1] - '0') / 10.0;
        double fT = int.Parse(strCode.AsSpan(2)) / 100.0;
        List<(double X, double Upper, double Lower)> ao = [];
        for (int i = 0; i <= n; i++)
        {
            double fX = 0.5 * (1.0 - Math.Cos(Math.PI * i / n));
            double fYt = 5.0 * fT * (0.2969 * Math.Sqrt(fX) - 0.1260 * fX - 0.3516 * fX * fX
                + 0.2843 * fX * fX * fX - 0.1015 * fX * fX * fX * fX);
            double fYc = 0;
            double fDy = 0;
            if (fP > 0)
            {
                if (fX < fP)
                {
                    fYc = fM / (fP * fP) * (2.0 * fP * fX - fX * fX);
                    fDy = 2.0 * fM / (fP * fP) * (fP - fX);
                }
                else
                {
                    double fQ = (1.0 - fP) * (1.0 - fP);
                    fYc = fM / fQ * ((1.0 - 2.0 * fP) + 2.0 * fP * fX - fX * fX);
                    fDy = 2.0 * fM / fQ * (fP - fX);
                }
            }

            double fTh = Math.Atan(fDy);
            double fXu = fX - fYt * Math.Sin(fTh);
            double fYu = fYc + fYt * Math.Cos(fTh);
            double fXl = fX + fYt * Math.Sin(fTh);
            double fYl = fYc - fYt * Math.Cos(fTh);
            ao.Add((0.5 * (fXu + fXl), fYu, fYl));
        }

        return ao;
    }

    static List<(double X, double Y)> NacaLoop(string strCode, int n)
    {
        List<(double X, double Upper, double Lower)> aoSides = NacaSides(strCode, n);
        List<(double X, double Y)> ao = [];
        for (int i = aoSides.Count - 1; i >= 0; i--)
            ao.Add((aoSides[i].X, aoSides[i].Upper));
        for (int i = 1; i < aoSides.Count; i++)
            ao.Add((aoSides[i].X, aoSides[i].Lower));
        return ao;
    }

    static void AddStreamBody(Lattice lat, Shape oShape)
        => AddPlate(lat, oShape);

    /// <summary>
    /// Circular plate. Each slice is a smooth stadium, so the rim is not a
    /// set of spokes. The middle stays thick enough for the tanks.
    /// </summary>
    static void AddPlate(Lattice lat, Shape oShape)
    {
        double fR = oShape.BodyHalfW;
        double fCz = oShape.NoseBaseZ;
        const int n = 56;
        for (int i = 0; i < n; i++)
        {
            double fA = fCz - fR + 2.0 * fR * i / n;
            double fB = fCz - fR + 2.0 * fR * (i + 1) / n;
            double fWa = PlateHalfW(fA, fCz, fR);
            double fWb = PlateHalfW(fB, fCz, fR);
            if (fWa < 4.0 && fWb < 4.0)
                continue;
            float fHa = (float)PlateHalfH(Math.Abs(fA - fCz) / fR, oShape.BodyHalfH);
            float fHb = (float)PlateHalfH(Math.Abs(fB - fCz) / fR, oShape.BodyHalfH);
            AddFilled(lat, fA, fB, Math.Max(fWa, 4.0), fHa, Math.Max(fWb, 4.0), fHb);
        }
    }

    static double PlateHalfW(double fZ, double fCz, double fR)
    {
        double fDz = fZ - fCz;
        return Math.Sqrt(Math.Max(0.0, fR * fR - fDz * fDz));
    }

    static void AddFilled(
        Lattice lat, double fZ0, double fZ1,
        double fHalfW0, double fHalfH0, double fHalfW1, double fHalfH1)
    {
        double fW = Math.Min(fHalfW0, fHalfW1);
        double fH = Math.Min(fHalfH0, fHalfH1);
        if (fW < 4.0 || fH < 3.0)
            return;
        double fReach = Math.Max(0.0, fW - fH);
        double fStep = Math.Min(16.0, fH);
        for (double fX = -fReach; fX <= fReach + 0.1; fX += fStep)
        {
            lat.AddBeam(
                new Vector3((float)fX, 0, (float)fZ0),
                new Vector3((float)fX, 0, (float)fZ1),
                (float)fHalfH0, (float)fHalfH1, false);
        }
    }

    static double PlateHalfH(double fFrac, double fHubH)
    {
        const double fHubFrac = 0.70;
        const double fRim = 12.0;
        if (fFrac <= fHubFrac)
            return fHubH;
        double fU = (fFrac - fHubFrac) / (1.0 - fHubFrac);
        fU = fU * fU * (3.0 - 2.0 * fU);
        return fHubH + (fRim - fHubH) * fU;
    }

    static void LoftSpan(
        Lattice lat,
        List<(double X, double Y)> aoSection,
        double fX0,
        double fX1,
        int nStations,
        float fRadius,
        Func<double, double, double, Vector3> fnPoint)
    {
        List<Vector3>? aoPrev = null;
        for (int i = 0; i <= nStations; i++)
        {
            double fX = fX0 + (fX1 - fX0) * i / nStations;
            List<Vector3> aoCur = [];
            foreach ((double fXn, double fYn) in aoSection)
                aoCur.Add(fnPoint(fX, fXn, fYn));
            Join(lat, aoPrev, aoCur, fRadius);
            aoPrev = aoCur;
        }
    }

    static void Join(Lattice lat, List<Vector3>? aoPrev, List<Vector3> aoCur, float fR)
    {
        if (aoPrev is null)
            return;
        for (int k = 0; k < aoCur.Count; k++)
            lat.AddBeam(aoPrev[k], aoCur[k], fR, fR, bRoundCap: false);
    }

    static List<(double Z, double R)> Profile(double fAftCenter, double fFwdCenter, double fR)
    {
        List<(double Z, double R)> ao = [];
        const int n = 18;
        for (int i = n; i >= 1; i--)
        {
            double fAng = 0.5 * Math.PI * i / n;
            ao.Add((fAftCenter + fR * Math.Sin(fAng), Math.Max(0.2, fR * Math.Cos(fAng))));
        }

        ao.Add((fAftCenter, fR));
        if (fFwdCenter < fAftCenter - 0.5)
            ao.Add((fFwdCenter, fR));

        for (int i = 1; i <= n; i++)
        {
            double fAng = 0.5 * Math.PI * i / n;
            ao.Add((fFwdCenter - fR * Math.Sin(fAng), Math.Max(0.2, fR * Math.Cos(fAng))));
        }

        return ao;
    }

    static Voxels voxLathe(Library lib, List<(double Z, double R)> ao, double fX)
    {
        Lattice lat = new(lib);
        for (int i = 1; i < ao.Count; i++)
        {
            if (Math.Abs(ao[i].Z - ao[i - 1].Z) < 0.05 && Math.Abs(ao[i].R - ao[i - 1].R) < 0.05)
                continue;
            lat.AddBeam(
                new Vector3((float)fX, 0, (float)ao[i - 1].Z),
                new Vector3((float)fX, 0, (float)ao[i].Z),
                (float)ao[i - 1].R,
                (float)ao[i].R,
                bRoundCap: false);
        }

        return new Voxels(lat);
    }

    sealed class HalfSpaceY : IImplicit
    {
        public float fSignedDistance(in Vector3 vec) => vec.Y;
    }
}

using System.Numerics;
using Impetus.Physics;
using PicoGK;

namespace Impetus.Geometry;

/// <summary>
/// A teardrop lifting body with blended wings, a duct through the middle, and
/// one engine buried in the tail. The fan and tiles are shapes. This
/// is still not a flight simulation.
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
    public const int EngineCount = 1;

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
        Tank Ox,
        double HoleR,
        double EnginePitch,
        double FuselageHalf);

    public static Shape Size(EngineDesign oDesign, FeedSystemSizing oFeed)
    {
        double fEngineWall = Math.Max(oDesign.Spec.WallThicknessMM, oDesign.Spec.Cooling.DiameterMM + 1.6);
        double fPlate = oDesign.Spec.Injector.Type switch
        {
            InjectorType.CoaxialSwirl => InjectorBuilder.CoaxialPlateMM,
            InjectorType.ImpingingDoublet => InjectorBuilder.ImpingingPlateMM,
            _ => Math.Max(6.0, fEngineWall * 2.0)
        };

        const double fHoleR = 115.0;
        double fCenter = fHoleR + 18.0 + TankOuterMM;
        double fHalfH = Math.Max(78.0, TankOuterMM + GapMM + SkinMM + 12.0);

        Tank oFuel = Place(MakeTank(
            "Fuel", oFeed.FuelMassKg, oDesign.Gas.FuelDensity, oFeed.FuelTankPressureBar, TankOuterMM), fCenter);
        Tank oOx = Place(MakeTank(
            "Oxidizer", oFeed.OxMassKg, oDesign.Gas.OxDensity, oFeed.OxTankPressureBar, TankOuterMM), -fCenter);

        double fAft = PropellantDomes.BackZ(fPlate) - BayMM;
        oFuel = oFuel with { AftTipZ = fAft, FwdTipZ = fAft - Length(oFuel) };
        oOx = oOx with { AftTipZ = fAft, FwdTipZ = fAft - Length(oOx) };

        double fEngineR = oDesign.ChamberRadius * 1000.0 + fEngineWall;
        double fNose = Math.Min(oFuel.FwdTipZ, oOx.FwdTipZ) - 320.0;
        double fMount = 120.0;
        double fLen = Math.Max(1.0, fMount - fNose);
        double fFus = fEngineR + 36.0;
        foreach (Tank oTank in new[] { oFuel, oOx })
        {
            double fOut = Math.Abs(oTank.CenterX) + oTank.OuterR + 20.0;
            foreach (double fZ in new[] { oTank.FwdTipZ, oTank.AftTipZ })
            {
                double fFrac = Math.Max(0.2, BodyFrac(Station(fNose, fMount, fZ)));
                fFus = Math.Max(fFus, fOut / fFrac);
            }
        }

        double fHalfW = Math.Max(fFus + 480.0, fHalfH * 6.0);
        double fHoleZ = fNose + 0.46 * fLen;
        double fTail0 = fNose + 0.74 * fLen;
        double fTailChord = Math.Min(168.0, Math.Max(90.0, fMount - fTail0 - 8.0));
        return new Shape(
            oFeed.BurnDurationS,
            fHalfW,
            fHalfH,
            fHalfW * 2.0,
            fLen,
            fHalfW * 0.12,
            0.0,
            fNose,
            fMount,
            0.0,
            fTailChord,
            fTail0,
            fTail0 + fTailChord,
            150.0,
            fNose,
            fHoleZ,
            fMount,
            fEngineR,
            0,
            oFuel,
            oOx,
            fHoleR,
            0.0,
            fFus);
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
        Voxels vox = new(lib, new BodyField(oShape), BodyBounds(oShape));
        Lattice latCut = new(lib);
        foreach (Tank oTank in new[] { oShape.Fuel, oShape.Ox })
        {
            double fBay0 = oTank.FwdTipZ + oTank.OuterR;
            double fBay1 = oTank.AftTipZ - oTank.OuterR;
            AddFilled(
                latCut, fBay0, fBay1,
                oTank.OuterR + 6.0, oShape.BodyHalfH - 10.0,
                oTank.OuterR + 6.0, oShape.BodyHalfH - 10.0,
                oTank.CenterX);
        }

        float fOpen = (float)oShape.BodyHalfH + 90f;
        latCut.AddBeam(
            new Vector3(0f, -fOpen, (float)oShape.NoseBaseZ),
            new Vector3(0f, fOpen, (float)oShape.NoseBaseZ),
            (float)oShape.HoleR, (float)oShape.HoleR, bRoundCap: false);
        AddInlets(latCut, oShape);
        AddTileGrooves(latCut, oShape);
        AddPanelLines(latCut, oShape);
        AddFlapGaps(latCut, oShape);
        AddScoop(latCut, oShape);
        vox.BoolSubtract(new Voxels(latCut));
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
        List<(double X, double Upper, double Lower)> ao = NacaSides(TailSection, 22);
        Lattice lat = new(lib);
        const int nSpan = 18;
        const double fCant = 32.0 * Math.PI / 180.0;
        foreach (int nSide in new[] { -1, 1 })
        {
            List<Vector3>? aoPrevUp = null;
            List<Vector3>? aoPrevLo = null;
            for (int i = 0; i <= nSpan; i++)
            {
                double fFrac = i / (double)nSpan;
                double fChord = oShape.TailChord * (1.0 - 0.52 * fFrac);
                double fRise = fFrac * oShape.FinHeight;
                double fX = nSide * (58.0 + Math.Sin(fCant) * fRise);
                double fY = oShape.BodyHalfH * 0.50 + Math.Cos(fCant) * fRise;
                double fZRoot = oShape.TailZ0 + fFrac * 24.0;
                List<Vector3> aoUp = [];
                List<Vector3> aoLo = [];
                foreach ((double fXn, double fYu, double fYl) in ao)
                {
                    double fZ = fZRoot + fXn * fChord;
                    aoUp.Add(FinPoint(nSide, fX, fY, fZ, fYu * fChord, fCant));
                    aoLo.Add(FinPoint(nSide, fX, fY, fZ, fYl * fChord, fCant));
                }

                for (int k = 0; k < aoUp.Count; k++)
                {
                    lat.AddBeam(aoUp[k], aoLo[k], 4.0f, 4.0f, bRoundCap: false);
                    if (k > 0)
                    {
                        lat.AddBeam(aoUp[k - 1], aoUp[k], 4.0f, 4.0f, bRoundCap: false);
                        lat.AddBeam(aoLo[k - 1], aoLo[k], 4.0f, 4.0f, bRoundCap: false);
                    }

                    if (aoPrevUp is not null)
                    {
                        lat.AddBeam(aoPrevUp[k], aoUp[k], 4.0f, 4.0f, bRoundCap: false);
                        lat.AddBeam(aoPrevLo![k], aoLo[k], 4.0f, 4.0f, bRoundCap: false);
                    }
                }

                aoPrevUp = aoUp;
                aoPrevLo = aoLo;
            }
        }

        return new Voxels(lat);
    }

    static Vector3 FinPoint(int nSide, double fX, double fY, double fZ, double fThick, double fCant)
        => new(
            (float)(fX + fThick * Math.Cos(fCant) * nSide),
            (float)(fY - fThick * Math.Sin(fCant)),
            (float)fZ);

    public static Voxels voxFan(Library lib, Shape oShape)
    {
        Lattice lat = new(lib);
        float fZ = (float)oShape.NoseBaseZ;
        float fTip = (float)(oShape.HoleR - 14.0);
        lat.AddBeam(new Vector3(0, -28f, fZ), new Vector3(0, -6f, fZ), 14f, 18f, bRoundCap: false);
        lat.AddBeam(new Vector3(0, -6f, fZ), new Vector3(0, 22f, fZ), 18f, 5f, bRoundCap: true);
        const int nBlades = 7;
        for (int i = 0; i < nBlades; i++)
            AddBlade(lat, 2.0 * Math.PI * i / nBlades, fZ, 18f, fTip, 0f, 0.90, 0.28, 2.3f);
        for (int i = 0; i < 5; i++)
            AddBlade(lat, Math.PI * 2.0 * i / 5.0 + 0.3, fZ, 28f, fTip - 4f, (float)(-oShape.BodyHalfH * 0.42), 0.42, 0.42, 2.1f);
        AddLip(lat, oShape, (float)(oShape.BodyHalfH - 2.0));
        AddLip(lat, oShape, (float)(-oShape.BodyHalfH + 2.0));
        return new Voxels(lat);
    }

    static void AddBlade(
        Lattice lat, double fAngle, float fHubZ, float fRoot, float fTip,
        float fHeight, double fPitchRoot, double fPitchTip, float fRadius)
    {
        const int nSpan = 7;
        const int nChord = 5;
        Vector3[,] ao = new Vector3[nSpan, nChord];
        double fRx = Math.Cos(fAngle);
        double fRz = Math.Sin(fAngle);
        double fCx = -fRz;
        double fCz = fRx;
        for (int s = 0; s < nSpan; s++)
        {
            double fS = s / (double)(nSpan - 1);
            double fR = fRoot + (fTip - fRoot) * fS;
            double fPitch = fPitchRoot + (fPitchTip - fPitchRoot) * fS;
            double fChord = 16.0 + 22.0 * Math.Sin(Math.PI * fS);
            for (int c = 0; c < nChord; c++)
            {
                double fT = c / (double)(nChord - 1) - 0.5;
                double fAlong = fT * fChord;
                double fY = fHeight + (float)(fAlong * Math.Sin(fPitch) + 1.6 * (0.25 - fT * fT));
                double fSpanX = fR * fRx + fAlong * Math.Cos(fPitch) * fCx;
                double fSpanZ = fHubZ + fR * fRz + fAlong * Math.Cos(fPitch) * fCz;
                ao[s, c] = new Vector3((float)fSpanX, (float)fY, (float)fSpanZ);
            }
        }

        FillGrid(lat, ao, fRadius);
    }

    static void AddLip(Lattice lat, Shape oShape, float fY)
    {
        const int n = 28;
        float fZ = (float)oShape.NoseBaseZ;
        float fR = (float)oShape.HoleR;
        for (int i = 0; i < n; i++)
        {
            double fA = 2.0 * Math.PI * i / n;
            double fB = 2.0 * Math.PI * (i + 1) / n;
            lat.AddBeam(
                new Vector3(fR * (float)Math.Cos(fA), fY, fZ + fR * (float)Math.Sin(fA)),
                new Vector3(fR * (float)Math.Cos(fB), fY, fZ + fR * (float)Math.Sin(fB)),
                14f, 14f, bRoundCap: false);
        }
    }

    static void FillGrid(Lattice lat, Vector3[,] ao, float fR)
    {
        int nS = ao.GetLength(0);
        int nC = ao.GetLength(1);
        for (int s = 0; s < nS; s++)
        {
            for (int c = 0; c < nC; c++)
            {
                if (c + 1 < nC)
                    lat.AddBeam(ao[s, c], ao[s, c + 1], fR, fR, bRoundCap: false);
                if (s + 1 < nS)
                    lat.AddBeam(ao[s, c], ao[s + 1, c], fR, fR, bRoundCap: false);
            }
        }
    }

    public static Voxels voxFlaps(Library lib, Shape oShape)
    {
        Lattice lat = new(lib);
        double fLen = Math.Max(1.0, oShape.MountZ - oShape.NoseTipZ);
        float fHingeZ = (float)(oShape.NoseTipZ + 0.90 * fLen);
        float fEdgeZ = (float)(oShape.NoseTipZ + 0.96 * fLen);
        float fX0 = (float)(oShape.FuselageHalf * 0.35);
        float fX1 = (float)(oShape.FuselageHalf + 20.0);
        foreach (int nSide in new[] { -1, 1 })
        {
            Vector3 vecHinge0 = new(nSide * fX0, -36f, fHingeZ);
            Vector3 vecHinge1 = new(nSide * fX1, -32f, fHingeZ);
            Vector3 vecEdge0 = new(nSide * fX0, -42f, fEdgeZ);
            Vector3 vecEdge1 = new(nSide * fX1, -36f, fEdgeZ);
            const int nRibs = 36;
            for (int i = 0; i <= nRibs; i++)
            {
                float fT = i / (float)nRibs;
                lat.AddBeam(
                    vecHinge0 + (vecHinge1 - vecHinge0) * fT,
                    vecEdge0 + (vecEdge1 - vecEdge0) * fT,
                    4.2f, 4.2f, bRoundCap: false);
            }

            for (int k = 0; k <= 4; k++)
            {
                float fU = k / 4f;
                lat.AddBeam(
                    vecHinge0 + (vecEdge0 - vecHinge0) * fU,
                    vecHinge1 + (vecEdge1 - vecHinge1) * fU,
                    4.2f, 4.2f, bRoundCap: false);
            }
        }

        return new Voxels(lat);
    }

    public static Voxels voxLegs(Library lib, Shape oShape)
    {
        Lattice lat = new(lib);
        float fBelly = (float)(-oShape.BodyHalfH + 4.0);
        float fFoot = fBelly - 155f;
        float fFan = (float)oShape.NoseBaseZ;
        AddLeg(lat, new Vector3(0, fBelly, fFan - 190f), new Vector3(0, fFoot, fFan - 230f), new Vector3(0, fBelly, fFan - 110f));
        AddLeg(lat, new Vector3(-150f, fBelly, fFan + 70f), new Vector3(-230f, fFoot, fFan + 20f), new Vector3(-90f, fBelly, fFan + 130f));
        AddLeg(lat, new Vector3(150f, fBelly, fFan + 70f), new Vector3(230f, fFoot, fFan + 20f), new Vector3(90f, fBelly, fFan + 130f));
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

    static void AddFilled(
        Lattice lat, double fZ0, double fZ1,
        double fHalfW0, double fHalfH0, double fHalfW1, double fHalfH1,
        double fCenterX = 0.0)
    {
        double fW = Math.Min(fHalfW0, fHalfW1);
        double fH = Math.Min(fHalfH0, fHalfH1);
        if (fW < 4.0 || fH < 3.0)
            return;
        double fReach = Math.Max(0.0, fW - fH);
        double fStep = Math.Min(8.0, Math.Max(4.0, fH * 0.65));
        for (double fX = -fReach; fX <= fReach + 0.1; fX += fStep)
        {
            lat.AddBeam(
                new Vector3((float)(fCenterX + fX), 0, (float)fZ0),
                new Vector3((float)(fCenterX + fX), 0, (float)fZ1),
                (float)fHalfH0, (float)fHalfH1, false);
        }
    }

    public static double Station(Shape o, double fZ)
        => Station(o.NoseTipZ, o.MountZ, fZ);

    public static double PlanHalfWidth(Shape o, double fZ)
    {
        double fU = Station(o, fZ);
        return o.FuselageHalf * BodyFrac(fU) + (o.BodyHalfW - o.FuselageHalf) * WingFrac(fU);
    }

    static double Station(double fNose, double fTail, double fZ)
        => (fZ - fNose) / Math.Max(1.0, fTail - fNose);

    /// <summary>Fuselage planform. Round at the nose, full through the tanks, sharp at the tail.</summary>
    public static double BodyFrac(double fU)
    {
        if (fU <= 0.0 || fU >= 1.0)
            return 0.0;
        if (fU < 0.34)
            return Math.Sqrt(fU / 0.34);
        if (fU < 0.86)
            return 1.0;
        return Math.Pow(Math.Clamp((1.0 - fU) / 0.14, 0.0, 1.0), 0.8);
    }

    /// <summary>Wing added outside the fuselage. It fades in behind the nose and ends in the same sharp tail.</summary>
    public static double WingFrac(double fU)
    {
        if (fU <= 0.30 || fU >= 1.0)
            return 0.0;
        if (fU < 0.72)
        {
            double fS = (fU - 0.30) / 0.42;
            return fS * fS * (3.0 - 2.0 * fS);
        }

        if (fU < 0.90)
            return 1.0;
        return Math.Pow(Math.Clamp((1.0 - fU) / 0.10, 0.0, 1.0), 0.65);
    }

    /// <summary>Side profile. Square-root nose, thick middle, sharp trailing edge.</summary>
    public static double ThickFrac(double fU)
    {
        if (fU <= 0.0 || fU >= 1.0)
            return 0.0;
        if (fU < 0.24)
            return Math.Sqrt(fU / 0.24);
        if (fU < 0.78)
            return 1.0;
        return Math.Pow(Math.Clamp((1.0 - fU) / 0.22, 0.0, 1.0), 1.05);
    }

    static void AddInlets(Lattice lat, Shape o)
    {
        double fLen = Math.Max(1.0, o.MountZ - o.NoseTipZ);
        double fZ0 = o.NoseTipZ + 0.07 * fLen;
        double fZ1 = o.NoseTipZ + 0.16 * fLen;
        foreach (int nSide in new[] { -1, 1 })
        {
            foreach (double fX in new[] { 34.0, 78.0 })
            {
                float fSx = (float)(nSide * fX);
                lat.AddBeam(new Vector3(fSx, 62f, (float)fZ0), new Vector3(fSx, 44f, (float)fZ1), 4.2f, 3.2f, bRoundCap: false);
                lat.AddBeam(new Vector3(fSx, -62f, (float)fZ0), new Vector3(fSx, -44f, (float)fZ1), 4.2f, 3.2f, bRoundCap: false);
            }
        }

        float fExitZ = (float)(o.MountZ - 18.0);
        lat.AddBeam(new Vector3(28f, 6f, fExitZ - 50f), new Vector3(28f, 16f, fExitZ), 5f, 5f, bRoundCap: false);
        lat.AddBeam(new Vector3(-28f, 6f, fExitZ - 50f), new Vector3(-28f, 16f, fExitZ), 5f, 5f, bRoundCap: false);
    }

    static void AddTileGrooves(Lattice lat, Shape o)
    {
        double fZ0 = o.NoseTipZ + 0.28 * (o.MountZ - o.NoseTipZ);
        double fZ1 = o.NoseTipZ + 0.74 * (o.MountZ - o.NoseTipZ);
        float fY = (float)(-o.BodyHalfH + 1.2);
        for (double fX = -96; fX <= 96.1; fX += 32)
            lat.AddBeam(new Vector3((float)fX, fY, (float)fZ0), new Vector3((float)fX, fY, (float)fZ1), 2.1f, 2.1f, bRoundCap: false);
        for (double fZ = fZ0; fZ <= fZ1; fZ += 70)
            lat.AddBeam(new Vector3(-96f, fY, (float)fZ), new Vector3(96f, fY, (float)fZ), 2.1f, 2.1f, bRoundCap: false);
    }

    static void AddPanelLines(Lattice lat, Shape o)
    {
        double fLen = Math.Max(1.0, o.MountZ - o.NoseTipZ);
        float fY = (float)(o.BodyHalfH - 1.4);
        float fZ0 = (float)(o.NoseTipZ + 0.30 * fLen);
        float fZ1 = (float)(o.NoseBaseZ - o.HoleR - 28.0);
        float fZ2 = (float)(o.NoseBaseZ + o.HoleR + 28.0);
        float fZ3 = (float)(o.NoseTipZ + 0.70 * fLen);
        foreach (float fX in new[] { -150f, -80f, 80f, 150f })
        {
            lat.AddBeam(new Vector3(fX, fY, fZ0), new Vector3(fX, fY, fZ1), 1.5f, 1.5f, bRoundCap: false);
            lat.AddBeam(new Vector3(fX, fY, fZ2), new Vector3(fX, fY, fZ3), 1.5f, 1.5f, bRoundCap: false);
        }

        lat.AddBeam(new Vector3(-170f, fY, fZ1), new Vector3(170f, fY, fZ1), 1.5f, 1.5f, bRoundCap: false);
        lat.AddBeam(new Vector3(-170f, fY, fZ2), new Vector3(170f, fY, fZ2), 1.5f, 1.5f, bRoundCap: false);
    }

    static void AddFlapGaps(Lattice lat, Shape o)
    {
        double fLen = Math.Max(1.0, o.MountZ - o.NoseTipZ);
        float fZ = (float)(o.NoseTipZ + 0.895 * fLen);
        float fX0 = (float)(o.FuselageHalf * 0.32);
        float fX1 = (float)(o.FuselageHalf + 28.0);
        lat.AddBeam(new Vector3(fX0, -3f, fZ), new Vector3(fX1, -3f, fZ), 3.6f, 3.6f, bRoundCap: false);
        lat.AddBeam(new Vector3(-fX0, -3f, fZ), new Vector3(-fX1, -3f, fZ), 3.6f, 3.6f, bRoundCap: false);
    }

    static void AddScoop(Lattice lat, Shape o)
    {
        float fZ0 = (float)(o.MountZ - 190.0);
        float fZ1 = (float)(o.MountZ - 16.0);
        lat.AddBeam(new Vector3(0, 18f, fZ0), new Vector3(0, 8f, fZ1), 24f, 18f, bRoundCap: false);
    }

    static void AddLeg(Lattice lat, Vector3 vecTop, Vector3 vecFoot, Vector3 vecBrace)
    {
        Vector3 vecKnee = vecTop + (vecFoot - vecTop) * 0.42f;
        lat.AddBeam(vecTop, vecKnee, 10f, 7f, bRoundCap: false);
        lat.AddBeam(vecKnee, vecFoot, 5.2f, 4f, bRoundCap: false);
        lat.AddBeam(vecKnee, vecBrace, 3.4f, 3.4f, bRoundCap: false);
        lat.AddBeam(vecFoot + new Vector3(-32f, 0, -14f), vecFoot + new Vector3(32f, 0, 16f), 3.6f, 3.6f, bRoundCap: false);
        lat.AddBeam(vecFoot + new Vector3(-10f, 1.5f, -30f), vecFoot + new Vector3(12f, 1.5f, 24f), 3.2f, 3.2f, bRoundCap: false);
    }

    static BBox3 BodyBounds(Shape oShape)
    {
        const float fPad = 30f;
        float fW = (float)oShape.BodyHalfW + fPad;
        float fTop = (float)oShape.BodyHalfH + 150f;
        float fBot = (float)oShape.BodyHalfH + fPad;
        return new BBox3(
            new Vector3(-fW, -fBot, (float)oShape.NoseTipZ - fPad),
            new Vector3(fW, fTop, (float)oShape.MountZ + fPad));
    }

    /// <summary>
    /// Teardrop in side view, blended wings in plan, upturned tips, a low
    /// canopy, and a vertical duct. Negative distance is inside the material.
    /// </summary>
    sealed class BodyField : IImplicit
    {
        readonly Shape m_o;

        public BodyField(Shape oShape) => m_o = oShape;

        public float fSignedDistance(in Vector3 vec)
        {
            double fLen = Math.Max(1.0, m_o.MountZ - m_o.NoseTipZ);
            double fU = Station(m_o, vec.Z);
            double fW = PlanHalfWidth(m_o, vec.Z);
            double fSlope = fU > 0.0 && fU < 1.0
                ? (PlanHalfWidth(m_o, vec.Z + 0.008 * fLen) - fW) / (0.008 * fLen)
                : 0.0;
            double fSide = (Math.Abs(vec.X) - fW) / Math.Max(1.0, Math.Sqrt(1.0 + fSlope * fSlope));
            double fPlan = vec.Z < m_o.NoseTipZ
                ? Math.Sqrt(vec.X * vec.X + (vec.Z - m_o.NoseTipZ) * (vec.Z - m_o.NoseTipZ))
                : vec.Z > m_o.MountZ
                    ? Math.Sqrt(vec.X * vec.X + (vec.Z - m_o.MountZ) * (vec.Z - m_o.MountZ))
                    : fSide;

            double fAbs = Math.Abs(vec.X);
            double fInset = Math.Max(0.0, fW - fAbs);
            double fToWing = Smooth(Math.Clamp((fAbs - m_o.FuselageHalf - 8.0) / 320.0, 0.0, 1.0));
            double fSection = m_o.BodyHalfH + (17.0 - m_o.BodyHalfH) * fToWing;
            double fToTip = Smooth(Math.Clamp((fAbs - m_o.BodyHalfW * 0.72) / (m_o.BodyHalfW * 0.28), 0.0, 1.0));
            fSection += (8.0 - fSection) * fToTip;
            double fEdge = Smooth(Math.Clamp(fInset / 46.0, 0.0, 1.0));
            double fH = fInset > 0.0 ? fSection * ThickFrac(fU) * (0.12 + 0.88 * fEdge) : 0.0;
            double fBend = Smooth(Math.Clamp((fAbs - m_o.BodyHalfW * 0.78) / (m_o.BodyHalfW * 0.22), 0.0, 1.0));
            double fCamber = 105.0 * fBend;
            double fUp = Math.Abs(vec.Y - fCamber) - fH;
            double fBody = fPlan > 0.0 && fUp > 0.0
                ? Math.Sqrt(fPlan * fPlan + fUp * fUp)
                : Math.Max(fPlan, fUp);

            double fCz = m_o.NoseTipZ + 0.18 * fLen;
            double fCy = m_o.BodyHalfH * 0.92;
            fBody = SmoothMin(fBody, Ellipsoid(vec.X, vec.Y - fCy, vec.Z - fCz, 62, 18, 108), 18.0);
            double fWindow = Ellipsoid(vec.X, vec.Y - (fCy + 17.0), vec.Z - fCz, 36, 7, 64);
            fBody = Math.Max(fBody, -fWindow);

            double fHole = Math.Sqrt(vec.X * vec.X + (vec.Z - m_o.NoseBaseZ) * (vec.Z - m_o.NoseBaseZ)) - m_o.HoleR;
            return (float)Math.Max(fBody, -fHole);
        }

        static double Ellipsoid(double fX, double fY, double fZ, double fA, double fB, double fC)
        {
            double fQ = Math.Sqrt(fX * fX / (fA * fA) + fY * fY / (fB * fB) + fZ * fZ / (fC * fC)) - 1.0;
            return fQ * Math.Min(fA, Math.Min(fB, fC));
        }

        static double Smooth(double fT)
            => fT * fT * (3.0 - 2.0 * fT);

        static double SmoothMin(double fA, double fB, double fK)
        {
            double fH = Math.Clamp(0.5 + 0.5 * (fB - fA) / fK, 0.0, 1.0);
            return (1.0 - fH) * fB + fH * fA - fK * fH * (1.0 - fH);
        }
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

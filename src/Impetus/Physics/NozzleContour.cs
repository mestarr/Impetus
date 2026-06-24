namespace Impetus.Physics;

/// <summary>
/// A point on the engine's inner wall contour. Z is the axial coordinate
/// (0 = injector face, increasing toward the nozzle exit), R the wall radius. [m]
/// </summary>
public readonly record struct ContourPoint(double Z, double R);

/// <summary>
/// The full inner contour of chamber + convergent + throat + bell,
/// built from arcs, a cone and the Rao parabola (Huzel &amp; Huang ch. 4).
/// </summary>
public class NozzleContour
{
    public IReadOnlyList<ContourPoint> Points => m_aoPoints;
    public double ThroatZ { get; }
    public double ExitZ => m_aoPoints[^1].Z;

    readonly List<ContourPoint> m_aoPoints = [];

    const double fConvAngleDeg = 30.0;   // convergent cone half-angle
    const double fRUpstreamArc = 1.5;    // upstream throat arc radius / Rt
    const double fRDownstreamArc = 0.382; // downstream throat arc radius / Rt

    public NozzleContour(EngineDesign oDesign)
    {
        double fRt = oDesign.ThroatRadius;
        double fRc = oDesign.ChamberRadius;
        double fConvRad = fConvAngleDeg * Math.PI / 180.0;
        double fRFillet = 0.5 * fRc;            // chamber-to-convergent blend radius
        double fR1 = fRUpstreamArc * fRt;
        double fR2 = fRDownstreamArc * fRt;

        // Radial drop across each convergent element determines the cone length
        double fDropFillet = fRFillet * (1.0 - Math.Cos(fConvRad));
        double fDropArc1 = fR1 * (1.0 - Math.Cos(fConvRad));
        double fDropCone = (fRc - fRt) - fDropFillet - fDropArc1;
        if (fDropCone < 0)
            throw new InvalidOperationException(
                "Contraction ratio too small for blend radii - increase contractionRatio.");

        double fZCylEnd = oDesign.ChamberCylinderLength;
        double fLCone = fDropCone / Math.Tan(fConvRad);
        double fZThroat = fZCylEnd
                        + fRFillet * Math.Sin(fConvRad)
                        + fLCone
                        + fR1 * Math.Sin(fConvRad);
        ThroatZ = fZThroat;

        // --- 1. Chamber cylinder ---------------------------------------------
        AddLine(new(0, fRc), new(fZCylEnd, fRc), 24);

        // --- 2. Fillet from cylinder into the cone (center below the wall) ----
        // Arc center at (zCylEnd, Rc - Rf); phi from 0 (wall tangent horizontal)
        // to the cone angle.
        AddArc(fZCylEnd, fRc - fRFillet, fRFillet,
               fPhiStart: 0, fPhiEnd: fConvRad, nSteps: 16, bAbove: true);

        // --- 3. Convergent cone ------------------------------------------------
        ContourPoint oConeStart = m_aoPoints[^1];
        ContourPoint oConeEnd = new(
            oConeStart.Z + fLCone,
            oConeStart.R - fDropCone);
        AddLine(oConeStart, oConeEnd, 24);

        // --- 4. Upstream throat arc (radius 1.5 Rt, center above the throat) --
        // Points: (zThroat + R1 sin phi, (Rt + R1) - R1 cos phi), phi from -30° to 0
        AddArc(fZThroat, fRt + fR1, fR1,
               fPhiStart: -fConvRad, fPhiEnd: 0, nSteps: 20, bAbove: false);

        // --- 5. Downstream throat arc up to the parabola start angle ----------
        AddArc(fZThroat, fRt + fR2, fR2,
               fPhiStart: 0, fPhiEnd: oDesign.BellThetaN, nSteps: 16, bAbove: false);

        // --- 6. Rao bell as quadratic Bezier -----------------------------------
        ContourPoint oN = m_aoPoints[^1];                       // parabola start
        ContourPoint oE = new(fZThroat + oDesign.BellLength,    // exit point
                              oDesign.ExitRadius);

        // Control point = intersection of the entry/exit tangents
        double fTanN = Math.Tan(oDesign.BellThetaN);
        double fTanE = Math.Tan(oDesign.BellThetaE);
        double fZq = (oE.R - oN.R + fTanN * oN.Z - fTanE * oE.Z) / (fTanN - fTanE);
        double fRq = oN.R + fTanN * (fZq - oN.Z);

        for (int i = 1; i <= 80; i++)
        {
            double t = i / 80.0;
            double mt = 1.0 - t;
            double fZ = mt * mt * oN.Z + 2 * mt * t * fZq + t * t * oE.Z;
            double fR = mt * mt * oN.R + 2 * mt * t * fRq + t * t * oE.R;
            m_aoPoints.Add(new(fZ, fR));
        }
    }

    /// <summary>Wall radius at axial position z, linear interpolation.</summary>
    public double RadiusAt(double fZ)
    {
        if (fZ <= m_aoPoints[0].Z) return m_aoPoints[0].R;
        for (int i = 1; i < m_aoPoints.Count; i++)
        {
            if (fZ <= m_aoPoints[i].Z)
            {
                ContourPoint a = m_aoPoints[i - 1], b = m_aoPoints[i];
                double fT = (fZ - a.Z) / (b.Z - a.Z);
                return a.R + fT * (b.R - a.R);
            }
        }
        return m_aoPoints[^1].R;
    }

    /// <summary>Resample the contour at n points uniformly spaced in meridional arc length.</summary>
    public List<ContourPoint> Resampled(int nCount, double fZFrom, double fZTo)
    {
        List<ContourPoint> aoSrc = [];
        foreach (ContourPoint o in m_aoPoints)
            if (o.Z >= fZFrom - 1e-12 && o.Z <= fZTo + 1e-12)
                aoSrc.Add(o);

        if (aoSrc.Count < 2)
            throw new InvalidOperationException("Resample range contains no contour points.");

        double[] afArc = new double[aoSrc.Count];
        for (int i = 1; i < aoSrc.Count; i++)
        {
            double dz = aoSrc[i].Z - aoSrc[i - 1].Z;
            double dr = aoSrc[i].R - aoSrc[i - 1].R;
            afArc[i] = afArc[i - 1] + Math.Sqrt(dz * dz + dr * dr);
        }

        List<ContourPoint> aoOut = [];
        int iSeg = 1;
        for (int i = 0; i < nCount; i++)
        {
            double fS = afArc[^1] * i / (nCount - 1.0);
            while (iSeg < aoSrc.Count - 1 && afArc[iSeg] < fS) iSeg++;
            double fT = (fS - afArc[iSeg - 1]) / Math.Max(afArc[iSeg] - afArc[iSeg - 1], 1e-12);
            aoOut.Add(new(
                aoSrc[iSeg - 1].Z + fT * (aoSrc[iSeg].Z - aoSrc[iSeg - 1].Z),
                aoSrc[iSeg - 1].R + fT * (aoSrc[iSeg].R - aoSrc[iSeg - 1].R)));
        }
        return aoOut;
    }

    void AddLine(ContourPoint oA, ContourPoint oB, int nSteps)
    {
        int nStart = m_aoPoints.Count == 0 ? 0 : 1;
        for (int i = nStart; i <= nSteps; i++)
        {
            double t = (double)i / nSteps;
            m_aoPoints.Add(new(oA.Z + t * (oB.Z - oA.Z), oA.R + t * (oB.R - oA.R)));
        }
    }

    /// <summary>
    /// Add a circular arc. bAbove: arc center sits radially inside the wall
    /// (fillet at the chamber); otherwise the center sits above (throat arcs).
    /// </summary>
    void AddArc(double fZc, double fRc, double fRadius,
                double fPhiStart, double fPhiEnd, int nSteps, bool bAbove)
    {
        for (int i = 1; i <= nSteps; i++)
        {
            double fPhi = fPhiStart + (fPhiEnd - fPhiStart) * i / nSteps;
            double fZ = fZc + fRadius * Math.Sin(fPhi);
            double fR = bAbove
                ? fRc + fRadius * Math.Cos(fPhi)
                : fRc - fRadius * Math.Cos(fPhi);
            m_aoPoints.Add(new(fZ, fR));
        }
    }
}

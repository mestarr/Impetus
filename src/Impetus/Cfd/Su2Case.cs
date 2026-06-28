using System.Globalization;
using System.Text;
using Impetus.Physics;

namespace Impetus.Cfd;

/// <summary>
/// Generates a complete, self-contained SU2 case for the engine's hot-gas path:
/// a structured 2D axisymmetric mesh of the chamber/nozzle interior (written in
/// native .su2 format, no external mesher needed) plus the solver configuration.
///
/// SU2 axisymmetric convention: x = engine axis, y = radius (y >= 0).
/// Viscous (RANS-SST) flow of the equilibrium combustion gas with an isothermal
/// wall at the regen-cooled liner design temperature.
/// </summary>
public class Su2Case
{
    public const string MeshFile = "mesh.su2";
    public const string ConfigFile = "engine.cfg";

    readonly EngineDesign m_oDesign;
    readonly NozzleContour m_oContour;

    // Grid resolution: axial x radial cells
    const int NI = 240;
    const int NJ = 60;

    /// <summary>Radial clustering toward the wall (higher = finer near-wall cells).</summary>
    const double WallRadialStretch = 2.8;

    public Su2Case(EngineDesign oDesign, NozzleContour oContour)
    {
        m_oDesign = oDesign;
        m_oContour = oContour;
    }

    public void Write(string strCaseDir)
    {
        Directory.CreateDirectory(strCaseDir);
        WriteMesh(Path.Combine(strCaseDir, MeshFile));
        WriteConfig(Path.Combine(strCaseDir, ConfigFile));
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Axial stations clustered around the throat using a Gaussian weight on
    /// the integrated point density.
    /// </summary>
    double[] AxialStations()
    {
        double fZt = m_oContour.ThroatZ;
        double fZe = m_oContour.ExitZ;
        double fSigma = 1.5 * m_oDesign.ThroatRadius;

        const int nFine = 4000;
        double[] afZ = new double[nFine + 1];
        double[] afCum = new double[nFine + 1];
        for (int i = 0; i <= nFine; i++)
        {
            double fZ = fZe * i / nFine;
            afZ[i] = fZ;
            double fW = 1.0 + 2.5 * Math.Exp(-Math.Pow((fZ - fZt) / fSigma, 2));
            afCum[i] = i == 0 ? 0 : afCum[i - 1] + fW * (fZe / nFine);
        }

        double[] afOut = new double[NI + 1];
        int iSeg = 1;
        for (int i = 0; i <= NI; i++)
        {
            double fTarget = afCum[^1] * i / NI;
            while (iSeg < nFine && afCum[iSeg] < fTarget) iSeg++;
            double fT = (fTarget - afCum[iSeg - 1])
                      / Math.Max(afCum[iSeg] - afCum[iSeg - 1], 1e-30);
            afOut[i] = afZ[iSeg - 1] + fT * (afZ[iSeg] - afZ[iSeg - 1]);
        }
        afOut[0] = 0;
        afOut[NI] = fZe;
        return afOut;
    }

    /// <summary>Map radial index j in [0, NJ] to radius with wall clustering.</summary>
    public static double RadialStation(double fWallRadius, int j, int nJ, double fStretch)
    {
        if (j <= 0) return 0;
        if (j >= nJ) return fWallRadius;
        if (fStretch < 1e-6) return fWallRadius * j / nJ;

        double fEta = (double)j / nJ;
        double fDenom = Math.Exp(fStretch) - 1.0;
        return fWallRadius * (Math.Exp(fStretch * fEta) - 1.0) / fDenom;
    }

    void WriteMesh(string strPath)
    {
        double[] afZ = AxialStations();

        // Node id = i * (NJ+1) + j ; i axial, j radial (0 = axis, NJ = wall)
        StringBuilder sb = new();
        sb.AppendLine("NDIME= 2");

        int nPoints = (NI + 1) * (NJ + 1);
        sb.AppendLine($"NPOIN= {nPoints}");
        for (int i = 0; i <= NI; i++)
        {
            double fR = m_oContour.RadiusAt(afZ[i]);
            for (int j = 0; j <= NJ; j++)
            {
                double fY = RadialStation(fR, j, NJ, WallRadialStretch);
                sb.Append(S(afZ[i])).Append(' ').Append(S(fY)).Append('\n');
            }
        }

        sb.AppendLine($"NELEM= {NI * NJ}");
        for (int i = 0; i < NI; i++)
        {
            for (int j = 0; j < NJ; j++)
            {
                int n00 = i * (NJ + 1) + j;
                int n10 = (i + 1) * (NJ + 1) + j;
                int n11 = (i + 1) * (NJ + 1) + j + 1;
                int n01 = i * (NJ + 1) + j + 1;
                sb.Append("9 ").Append(n00).Append(' ').Append(n10).Append(' ')
                  .Append(n11).Append(' ').Append(n01).Append('\n');
            }
        }

        sb.AppendLine("NMARK= 4");

        AppendLineMarker(sb, "axis", Enumerable.Range(0, NI)
            .Select(i => (i * (NJ + 1), (i + 1) * (NJ + 1))));

        AppendLineMarker(sb, "wall", Enumerable.Range(0, NI)
            .Select(i => (i * (NJ + 1) + NJ, (i + 1) * (NJ + 1) + NJ)));

        AppendLineMarker(sb, "inlet", Enumerable.Range(0, NJ)
            .Select(j => (j, j + 1)));

        AppendLineMarker(sb, "outlet", Enumerable.Range(0, NJ)
            .Select(j => (NI * (NJ + 1) + j, NI * (NJ + 1) + j + 1)));

        File.WriteAllText(strPath, sb.ToString());
    }

    static void AppendLineMarker(StringBuilder sb, string strTag,
                                 IEnumerable<(int A, int B)> aoEdges)
    {
        var ao = aoEdges.ToList();
        sb.AppendLine($"MARKER_TAG= {strTag}");
        sb.AppendLine($"MARKER_ELEMS= {ao.Count}");
        foreach ((int a, int b) in ao)
            sb.Append("3 ").Append(a).Append(' ').Append(b).Append('\n');
    }

    // -------------------------------------------------------------------------

    void WriteConfig(string strPath)
    {
        CombustionGas oGas = m_oDesign.Gas;
        EngineSpec oSpec = m_oDesign.Spec;
        double fWallT = ThermalModel.AssumedRegenWallTempK;

        string strCfg = $"""
            % Impetus auto-generated SU2 case: {oSpec.Name}
            % Axisymmetric RANS-SST hot-gas flow, ideal-gas combustion products
            SOLVER= RANS
            MATH_PROBLEM= DIRECT
            AXISYMMETRIC= YES
            RESTART_SOL= NO

            FLUID_MODEL= IDEAL_GAS
            GAMMA_VALUE= {S(oGas.Gamma)}
            GAS_CONSTANT= {S(oGas.Rs)}
            VISCOSAL_MODEL= SUTHERLAND
            REF_DIMENSIONALIZATION= DIMENSIONAL

            KIND_TURB_MODEL= SST
            FREESTREAM_TURBULENCEINTENSITY= 0.05
            FREESTREAM_TURB2VISC_RATIO= 1.0

            % Dimensional setup; freestream values only initialize the field
            MACH_NUMBER= 0.05
            FREESTREAM_OPTION= TEMPERATURE_FS
            FREESTREAM_PRESSURE= {S(oSpec.Pc)}
            FREESTREAM_TEMPERATURE= {S(oGas.Tc)}
            INIT_OPTION= TD_CONDITIONS

            % Boundaries
            INLET_TYPE= TOTAL_CONDITIONS
            MARKER_INLET= ( inlet, {S(oGas.Tc)}, {S(oSpec.Pc)}, 1.0, 0.0, 0.0 )
            MARKER_OUTLET= ( outlet, {S(oSpec.Pa)} )
            MARKER_ISOTHERMAL= ( wall, {S(fWallT)} )
            MARKER_SYM= ( axis )
            MARKER_PLOTTING= ( outlet, wall )
            MARKER_MONITORING= ( wall )
            MARKER_ANALYZE= ( outlet )
            MARKER_ANALYZE_AVERAGE= AREA

            % Numerics (RANS: upwind + implicit, conservative CFL ramp)
            NUM_METHOD_GRAD= GREEN_GAUSS
            CONV_NUM_METHOD_FLOW= ROE
            MUSCL_FLOW= YES
            SLOPE_LIMITER_FLOW= VENKATAKRISHNAN
            SLOPE_LIMITER_COEFF= 0.5
            TIME_DISCRE_FLOW= EULER_IMPLICIT
            TIME_DISCRE_TURB= EULER_IMPLICIT
            CFL_NUMBER= 0.25
            CFL_ADAPT= YES
            CFL_ADAPT_PARAM= ( 0.25, 1.02, 0.1, 10.0 )
            LINEAR_SOLVER= FGMRES
            LINEAR_SOLVER_PREC= ILU
            LINEAR_SOLVER_ERROR= 1E-4
            LINEAR_SOLVER_ITER= 15

            % Convergence
            ITER= 12000
            CONV_FIELD= RMS_DENSITY
            CONV_RESIDUAL_MINVAL= -8
            CONV_STARTITER= 200

            % I/O
            MESH_FILENAME= {MeshFile}
            MESH_FORMAT= SU2
            SCREEN_OUTPUT= ( INNER_ITER, RMS_DENSITY, RMS_MOMENTUM-X, RMS_ENERGY, RMS_K, RMS_OMEGA )
            SCREEN_WRT_FREQ_INNER= 200
            HISTORY_OUTPUT= ( ITER, RMS_RES, FLOW_COEFF )
            OUTPUT_FILES= ( RESTART, PARAVIEW, SURFACE_CSV )
            OUTPUT_WRT_FREQ= 2000
            VOLUME_OUTPUT= ( COORDINATES, SOLUTION, PRIMITIVE )
            VOLUME_FILENAME= flow
            SURFACE_FILENAME= surface_flow
            RESTART_FILENAME= restart_flow.dat
            CONV_FILENAME= history
            """;

        File.WriteAllText(strPath, strCfg);
    }

    /// <summary>Invariant-culture number formatting (decimal *points*, always).</summary>
    static string S(double f) => f.ToString("G10", CultureInfo.InvariantCulture);
}

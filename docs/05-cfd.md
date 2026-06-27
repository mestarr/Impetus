# 5. The Virtual Test — CFD with SU2

Code: `src/Impetus/Cfd/Su2Case.cs` (mesh + config generation) and
`Su2Runner.cs` (execution + post-processing). Solver:
[SU2](https://su2code.github.io/) v8.x, the open-source compressible-flow
suite (Stanford-born, now SU2 Foundation), `win64-omp` build under
`tools/SU2/bin/SU2_CFD.exe`.

## 5.1 What exactly is simulated

**The hot-gas path**: combustion chamber interior → convergent → throat →
bell → exit plane, as a **2D axisymmetric, viscous (RANS-SST), ideal-gas**
compressible flow of the equilibrium combustion products. The nozzle wall is
**isothermal** at 800 K — the same regen-cooled liner design point used by the
Bartz correlation in the report.

What this verifies:

- the nozzle actually chokes and expands as designed (shock-free bell),
- delivered **thrust** (momentum + pressure integral at the exit),
- **mass flow** (i.e. the throat is the size the 1D theory thinks it is),
- exit Mach / pressure distribution (1D theory assumes uniform; CFD shows the
  real 2D profile that the Rao bell produces),
- **wall heat flux** near the throat (RANS vs Bartz, when SU2 writes it to
  `surface_flow.csv`).

What it deliberately does *not* model: combustion (gas enters already
burnt at \(T_c, p_c\)), conjugate heat transfer into the coolant (wall
temperature is fixed, not solved), and the external plume beyond the exit
plane. Expected agreement with 1D theory for a healthy bell:
**within a few percent** on thrust and mass flow — RANS may read slightly low
due to boundary-layer displacement.

## 5.2 The mesh — structured, generated natively, no external tools

Because the hot-gas path is a single smoothly-curved channel and the wall
contour is *already* an analytic function \(r(z)\), Impetus writes a
**structured quad mesh in SU2's native ASCII format** directly:

- **Topology:** (NI+1) × (NJ+1) nodes = 241 × 61 ≈ 14.7 k nodes,
  240 × 60 ≈ 14.4 k quad cells. Node (i, j) sits at
  \( (z_i,\; r(z_i)\cdot \phi(j/N_J)) \) where \(\phi\) is a geometric
  stretch mapping that clusters nodes toward the wall — radial lines from the
  axis to the wall at every axial station. SU2's axisymmetric solver interprets
  x = axis, y = radius.
- **Throat clustering:** axial stations are distributed by inverting the
  cumulative integral of a Gaussian point-density weight
  \( w(z) = 1 + 2.5\,e^{-((z-z_t)/1.5R_t)^2} \) — 3.5× denser cells where the
  sonic line and the strongest gradients live, smoothly blended (no abrupt
  size jumps).
- **Wall clustering:** radial stations use
  \( y = r(z)\,(e^{\beta j/N_J}-1)/(e^\beta-1) \) with \(\beta \approx 2.8\)
  — cells are finest next to the viscous wall for RANS-SST.
- **Markers:** `inlet` (i = 0, chamber face), `outlet` (i = NI, exit plane),
  `wall` (j = NJ, the contour), `axis` (j = 0, symmetry axis).

Mesh quality is deterministic: cells are axis-aligned quads with mild
stretching; the worst skew appears at the steep convergent wall and remains
benign for ROE/RANS at this resolution.

## 5.3 The solver configuration, explained line by line

The generated `engine.cfg` (values inserted from the design):

| Setting | Value | Why |
|---|---|---|
| `SOLVER= RANS` | viscous | boundary layers, wall heat flux, separation screening |
| `KIND_TURB_MODEL= SST` | | industry-standard two-equation model for internal nozzle flows |
| `AXISYMMETRIC= YES` | | solves the 2D meridian plane with the 1/r source terms — full 3D answer for a body of revolution at ~1/1000 the cost |
| `GAMMA_VALUE`, `GAS_CONSTANT` | from `CombustionGas` | the *same* γ and R_s the analytic model used — apples-to-apples comparison |
| `MARKER_INLET= (inlet, T_c, p_c, 1,0,0)` with `INLET_TYPE= TOTAL_CONDITIONS` | | classic subsonic reservoir inlet: stagnation temperature + pressure + axial flow direction. The chamber face Mach (~0.15 at CR = 4) develops on its own |
| `MARKER_OUTLET= (outlet, p_a)` | | back-pressure outlet; once the exit goes supersonic during convergence SU2 switches to pure extrapolation and the value becomes irrelevant — robust in both startup and converged phases |
| `MARKER_ISOTHERMAL= (wall, 800 K)` | | no-slip wall at the regen-cooled liner design temperature (matches Bartz) |
| `MARKER_SYM= (axis)` | | symmetry/axis condition |
| `CONV_NUM_METHOD_FLOW= ROE` + `MUSCL_FLOW= YES` | | upwind RANS scheme; more robust than JST for viscous walls |
| `TIME_DISCRE_FLOW/TURB= EULER_IMPLICIT` + `CFL_ADAPT 0.25 → 10` | | implicit pseudo-time marching; conservative CFL ramp for RANS |
| `FREESTREAM_* = (p_c, T_c)`, `INIT_OPTION= TD_CONDITIONS` | | the whole field initializes at chamber stagnation state; flow "starts" from rest and establishes through the nozzle — the reliable way to start internal nozzle cases |
| `ITER= 12000`, `CONV_FIELD= RMS_DENSITY`, `CONV_RESIDUAL_MINVAL= -8` | | RANS needs more iterations than Euler; stops early when density residual drops 8 orders |
| `OUTPUT_FILES= (RESTART, PARAVIEW, SURFACE_CSV)` | | restart for re-runs, `flow.vtu` for ParaView, surface CSV for thrust + wall flux |
| `MARKER_PLOTTING= (outlet, wall)` | | surface CSV contains exit-plane nodes for thrust integration and wall nodes for heat-flux comparison |

Everything is plain text in `designs/<name>/cfd/` — you can hand-edit the
config and re-run SU2 manually at any time:

```powershell
cd designs\IMP-1K-A\cfd
..\..\..\tools\SU2\bin\SU2_CFD.exe engine.cfg
```

## 5.4 Execution (`Su2Runner`)

- Finds `SU2_CFD.exe` by walking up from the executable to the repo root and
  searching `tools/**`; the `IMPETUS_SU2` environment variable overrides.
- Sets `OMP_NUM_THREADS = cores − 1` (the omp build parallelizes well; one
  core is left for you).
- Streams all solver output to `cfd/su2.log`, echoing every 5th residual line
  to the console as a heartbeat.
- Hard 45-minute timeout → process-tree kill, case preserved for inspection.

## 5.5 Post-processing — how CFD thrust is computed

SU2's `surface_flow.csv` contains, per exit-plane node: position, density,
momentum, pressure, Mach. Impetus sorts nodes by radius and evaluates the
axisymmetric momentum-flux integral with the trapezoid rule:

\[ F_{CFD} = \int_0^{R_e} \left[\rho u^2 + (p - p_a)\right]\,2\pi r\,dr \]

where \(u = (\rho u)/\rho\) is the axial velocity from the conserved momentum.
This is the *gross thrust* definition — directly comparable to the analytic
\(F = \dot m v_e + (p_e-p_a)A_e\). Similarly:

\[ \dot m_{CFD} = \int_0^{R_e} \rho u\, 2\pi r\, dr, \qquad
   \bar M = \frac{1}{A_e}\int M\, dA, \qquad
   \bar p_e = \frac{1}{A_e}\int p\, dA \]

The report's CFD section prints analytic vs CFD vs deviation for all of them,
plus the convergence status and iteration count parsed from the log.

## 5.6 Reading the results

- **Thrust within ±3 %** of target and **mass flow within ±2 %**: the design
  is aerodynamically sound; 1D theory and 2D reality agree.
- **CFD thrust noticeably low + exit pressure ≫ ambient:** under-expanded
  (ε too small) — raise `expansionRatio` or accept the loss.
- **Oscillating residuals / no convergence:** look at `flow.vtu`; a shock
  sitting in the bell (over-expanded at this \(p_a\)) is the usual suspect —
  reduce ε or reduce ambient pressure (vacuum-stage design).
- **Mass flow off by > 5 %:** the throat region mesh is mis-resolving the
  sonic line; lower `voxelSize`-independent — this is a *CFD* resolution
  issue: increase `NI`/`NJ` in `Su2Case.cs`.

**Visual inspection:** open `cfd/flow.vtu` in
[ParaView](https://www.paraview.org/) → color by `Mach` → you should see the
clean transonic "smile" at the throat, smooth acceleration to the design exit
Mach, no diamonds inside the bell.

## 5.7 Validation pedigree of the setup

Axisymmetric RANS-SST on a structured wall-clustered grid is the standard
upgrade path from inviscid nozzle screening. Impetus runs **RANS-SST** with an
isothermal wall BC. Known systematic deviations vs. reality (not vs. 1D theory)
include chemistry effects (γ varying through expansion) and conjugate coolant
coupling — both further roadmap items.

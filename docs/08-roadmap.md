# 8. Limitations in Detail & Roadmap

Each v1 simplification below is listed with its *consequence* (what to watch
for) and its *upgrade path* (how it slots into the existing architecture —
which is why the architecture looks the way it does).

## 8.1 Gas model: textbook tables instead of chemical equilibrium

- **Now:** fixed \(T_c, \mathcal{M}, \gamma\) per propellant pair at its
  typical O/F (`GasModel.cs`).
- **Consequence:** Isp-class results good to a few percent near the nominal
  O/F; off-nominal mixture studies are not meaningful yet; γ is frozen through
  the expansion (real products recombine → slightly more thrust).
- **Upgrade:** generate lookup tables with NASA CEA (or Cantera) offline —
  \(T_c, \gamma, \mathcal{M} = f(O/F, p_c)\) — and ship them as data files.
  `CombustionGas` is already the single seam: swap the static table for an
  interpolator, nothing else changes.

## 8.2 CFD: inviscid single-species

- **Now:** axisymmetric Euler with the chamber-condition ideal gas
  (`Su2Case.cs`).
- **Consequence:** no boundary-layer losses (<1 % thrust at this scale), no
  wall heat flux from CFD, no separation prediction in heavily over-expanded
  bells (the shock shows up, but the separation point needs viscosity).
- **Upgrade:** flip the generated config to `SOLVER= RANS`,
  `KIND_TURB_MODEL= SST`, mark the wall `MARKER_HEATFLUX` or isothermal, and
  add wall-normal mesh clustering (geometric stretching toward j = NJ in the
  mesh writer — one function). SU2 then reports wall heat flux directly,
  which can be compared against Bartz station-by-station.

## 8.3 Thermal: Bartz + single-node coolant balance

- **Now:** hot-side flux profile + total load + bulk coolant ΔT and velocity
  (`ThermalModel.cs`).
- **Consequence:** no wall temperature distribution, no channel pressure drop,
  no boiling/decomposition margins. The 800 K wall assumption is fixed.
- **Upgrade:** 1D marching regen solver — walk the contour from the coolant
  inlet manifold: per station, hot-side Bartz / wall conduction / coolant-side
  Dittus-Boelter (or Gnielinski), update coolant T and p. ~200 lines in a new
  `Physics/RegenSolver.cs`; the contour and channel geometry it needs already
  exist. This also unlocks *automatic* channel sizing (solve for count/Ø that
  keeps wall < limit).

## 8.4 Injector: hydraulically-sized showerhead

- **Now:** correct orifice areas/counts for the Δp rule, geometric rings,
  igniter port (`ThrusterBuilder.voxInjectorAssembly`).
- **Consequence:** real injectors dominate combustion efficiency and
  stability; a showerhead is the simplest (and least stable) pattern. Fine
  for geometry/printing studies, not a combustion design.
- **Upgrade:** coaxial swirl or impinging-doublet element generator —
  per-element mass flow from the existing sizing, element geometry as another
  lattice/implicit recipe, plus film-cooling orifice ring fed from the fuel
  manifold (the manifold torus is already there to tap into).

## 8.5 No optimization loop

- **Now:** human-in-the-loop iteration (edit spec → regenerate → read report).
- **Upgrade:** the pipeline is already a pure function
  `spec → (design, thermal, cfd)`. Wrap it: parameter sweeps (grid over Pc,
  ε, O/F), Pareto filter (Isp vs coolant ΔT vs length), or black-box
  optimization (e.g. Nelder-Mead over 3–4 spec fields with a penalty-weighted
  objective). The per-candidate cost is seconds without CFD, minutes with —
  run sweeps analytically, CFD-verify only the shortlist.

## 8.6 Geometry: no manufacturability checks

- **Now:** watertight printable STLs, sane minimum walls by construction.
- **Consequence:** no overhang/support analysis, no powder-removal check for
  the channels (they are open at both manifolds, which is the main thing),
  no distortion compensation.
- **Upgrade:** voxel-based checks are natural in PicoGK: overhang = compare
  solid against its morphological dilation along −Z; minimum wall = erosion
  test; trapped powder = flood-fill the void space from the exits. All are
  roadmap-friendly because the geometry is already voxels.

## 8.7 Aerospike & advanced cycles

The contour generator and CFD mesher are bell-specific. An aerospike module
needs: spike contour (Angelino's approximation is the classic), annular
chamber + throat gap sizing, annular CFD topology (two wall boundaries), and
center-body regen cooling. The physics layer (isentropic relations, Bartz,
gas tables) carries over unchanged. This is the most interesting future
milestone — and the one LEAP 71 famously demonstrated.

## 8.8 Suggested order of attack

1. RANS CFD switch + wall clustering (small effort, big credibility gain)
2. 1D regen solver (unlocks real cooling design)
3. CEA gas tables (removes the biggest accuracy asterisk)
4. Parameter sweep driver (turns the tool into a design-space explorer)
5. Film cooling + better injector elements
6. Manufacturability checks
7. Aerospike

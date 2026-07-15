# 9. Improvement Backlog

A prioritized list of ways to make Impetus better — quick wins, engineering
upgrades, manufacturing/print workflow, product UX, and long-term milestones.

For the technical rationale behind physics/CFD/thermal limitations, see
[08-roadmap.md](08-roadmap.md). This document is the **action backlog**: what to
build, why, and in what order.

---

## What the project already does well

- **Computational engineering loop** — `spec.json` → sizing physics → PicoGK
  geometry → STL/3MF → SU2 CFD → `report.md` → edit spec and repeat.
- **Deterministic and explainable** — every dimension traces to a formula
  (Sutton, Bartz, Rao, etc.).
- **Fast inner loop** — `design` runs in seconds to minutes; CFD is optional and
  slower.
- **Real geometry** — regenerative cooling channels, manifolds, showerhead
  injector, bolted flange.
- **Virtual validation** — pass/fail gates, hot-fire plan, `iterate` command
  for auto spec correction.
- **Dual export** — `engine.stl` + `engine.3mf` (and cutaway versions) with
  explicit millimeter units for slicers such as Anycubic Slicer Next / Kobra S1.
- **Good documentation** — `docs/` covers physics, architecture, workflow, spec
  reference, and roadmap.

---

## Tier 1 — Quick wins (days, high value)

*Status: largely shipped — see git history.*

### Demo and trust

- [x] **Demo spec analytic validation** — v1 used a 15% heat-pickup hack; replaced
  by the 1D regen solver (Tier 2). `demo-1kN.json` now reports honest coolant
  ΔT (iterate / O/F tune expected for kerolox).

### Testing

- [x] **`tests/Impetus.Tests/`** — xUnit: spec loader, sizing golden numbers,
  validation gates, print envelope, 3MF unit check.

### CI

- [x] **`.github/workflows/ci.yml`** — build, test, PicoGK smoke on Windows.

### 3MF / slicer polish

- [x] **Richer 3MF metadata** — title, creation date, application, description.
- [x] **Separate 3MF components** — `body`, `injector`, `flange` as named objects in
  `engine.3mf` and `engine_cutaway.3mf` (combined STL unchanged).
- [x] **`print` command** — fast slicer/bed-fit summary without geometry.

### FDM print helper in `report.md`

- [x] **`PrintReport.cs`** — envelope, bed fit, orientation, settings, FDM vs
  metal disclaimer (appended to every `report.md`).

### Documentation sync

- [x] **01-overview, 04-geometry, 07-workflow, 03-physics** updated for STL+3MF,
  MeshExport, print command, heat pickup.

### Code hygiene

- [x] **`Cli/ImpetusApp.cs`** — `Program.cs` is a thin entry; commands live in
  `Cli/`.

---

## Tier 2 — Engineering credibility (weeks, biggest technical gain)

*Aligned with [08-roadmap.md](08-roadmap.md).*

### 1. RANS CFD instead of Euler

- [x] **Shipped:** `SOLVER= RANS`, `KIND_TURB_MODEL= SST`, `MARKER_ISOTHERMAL`
  wall at 800 K, ROE + MUSCL, radial wall clustering (NJ=60, stretch β≈2.8).
  Post-processing compares throat wall heat flux vs Bartz when SU2 writes
  `Heat_Flux` to `surface_flow.csv`.

### 2. 1D regenerative cooling solver

- [x] **Shipped:** `Physics/RegenSolver.cs` — march along the channel path
  (injector → nozzle collector): Bartz → CuCrZr conduction → Gnielinski coolant
  side; updates coolant T and p per station. Reports peak/throat wall temperature,
  channel Δp, outlet temperature. `AutoIterate` can auto-size count/Ø for wall
  temp; O/F for bulk ΔT. SU2 wall BC uses 1D throat wall temperature.

### 3. CEA-derived gas property tables

- [x] **Shipped:** `data/gas/*.json` grids (Tc, M, γ vs O/F and Pc);
  `GasTableStore` bilinear interpolation; `CombustionGas.Resolve(key, of, pc)`.
  Reports show interpolated gas properties. Frozen γ through expansion remains.

### 4. Parameter sweep / optimization driver

- [x] **Now:** human edits spec, runs `design` / `test`, reads report.
- [x] **Upgrade:**
  - New command, e.g. `sweep specs/demo.json --pc 15,20,25 --of 2.0,2.3,2.6`.
  - Grid or random samples over Pc, ε, O/F, `bellFraction`, cooling knobs.
  - Pareto filter: Isp vs coolant ΔT vs engine length.
  - Optional black-box optimizer (Nelder-Mead on 3–4 spec fields with penalty
    objective).
- [x] **Workflow:** sweep analytically (seconds each); run CFD only on shortlist.
- [x] **Benefit:** Turns Impetus from “one engine” into a **design-space explorer**.

### 5. Stronger `iterate` loop

- [x] **Now:** rule-based spec mutations until analytic gates pass or hit bounds.
- [x] **Improvements:**
  - [x] Include regen solver feedback once it exists.
  - [x] Optional objective: pass all gates + minimize length.
  - [x] Richer comparison table across iterations in `iteration-log.md`.

---

## Tier 3 — Geometry and manufacturing

### Manufacturability checks (natural in PicoGK voxels)

- [x] **Overhang analysis** — compare solid against morphological dilation along −Z
  (build direction); score flange-down printability.
- [x] **Minimum wall check** — voxel erosion test; flag walls thinner than spec or
  process minimum (e.g. 0.8 mm metal).
- [x] **Powder / fluid removal** — flood-fill cooling void from manifold exits;
  detect trapped internal volumes.
- [x] **Output:** pass/warn/fail in validation report, same style as thermal gates.

### Process-aware geometry (`targetProcess` in spec)

- **`FDM`** — coarser voxels OK; channels can be representative, not functional.
- **`LPBF`** — enforce min channel Ø, wall thickness, manifold access.
- Auto-set `voxelSizeMM` and warnings per process.

### Better injector geometry

- **Now:** hydraulically-sized showerhead — orifice areas, ring pattern, igniter
  port (`ThrusterBuilder`).
- **Limitation:** showerhead is simplest and least stable; fine for display, not
  combustion design.
- **Upgrade:** coaxial swirl or impinging-doublet elements; per-element mass
  flow from existing sizing; film-cooling orifice ring from fuel manifold
  (manifold torus already exists).

### Geometry module extras

- **Aerospike** (large milestone) — spike contour, annular chamber, annular CFD,
  center-body regen.
- **Export formats** — OBJ for some viewers; 3MF with materials/colors for
  cutaway visualization.
- **Assembly exports** — separate STL/3MF for injector, nozzle, flange for
  multi-part prints.

---

## Tier 4 — Workflow and product UX

### CLI improvements

- **`sweep`** — parameter grids (see Tier 2).
- **`print`** — slicer-oriented summary + bed fit.
- **`diff`** — compare two `report.md` files or two specs side by side.
- **`export-only`** — regenerate 3MF/STL from cached voxels (optional voxel
  cache).

### Viewer and sharing

- **`view` needs GPU** — fails on remote desktop.
- **Alternatives:** headless cutaway screenshot in report; simple HTML/WebGL
  viewer for STL/3MF; ParaView for `flow.vtu` (already documented).

### Feed system and hot-fire workflow

- **Partially implemented:** `VirtualValidation` sizes tanks/lines/burn;
  `hotfire-plan.md`.
- **Improve:** link validation failures to concrete spec edits; checklist export;
  post-hot-fire template to paste measured thrust/Isp into a new spec revision.

### Spec schema extensions

- **`printerBedMM`** — auto bed-fit in report.
- **`targetProcess`** — `FDM` vs `LPBF`.
- **`maxCoolantRiseK`** — per-fuel override.
- **`burnDurationS`** — already used in feed sizing; expose in spec JSON docs.

---

## Tier 5 — Ambitious (months)

- **Conjugate heat transfer** — SU2 wall heat flux → regen channel model →
  coupled iteration.
- **Combustion modeling** — beyond frozen equilibrium gas (far beyond v1).
- **Film cooling** — fuel bleed through wall orifices; ties to injector + regen.
- **Multi-engine / stage sizing** — thrust class sweeps, stage Δv budgets.
- **Hardware closure loop** — test stand CSV → update gas tables or discharge
  coefficients.
- **Plugin propellants** — user-defined `GasModel` rows without recompiling.

---

## Known v1 limitations (baseline)

- Textbook gas tables, not full CEA equilibrium.
- RANS-SST CFD — nozzle aerodynamics + throat wall flux vs Bartz; not combustion or conjugate cooling in CFD.
- Bartz + lumped coolant balance — not conjugate heat transfer.
- Showerhead injector — geometry only; stability screening only.
- No automatic printability validation in geometry yet.
- No optimization loop beyond manual `iterate`.
- Bell nozzle only — no aerospike in v1.

---

## Suggested order of attack

1. RANS CFD + wall mesh clustering — small effort, big credibility.
2. 1D regen solver — unlocks real cooling design; fixes demo validation story.
3. CEA gas tables — removes largest accuracy caveat.
4. Parameter sweep driver — design-space explorer.
5. Film cooling + better injector elements.
6. Manufacturability checks (overhang, min wall, trapped voids).
7. Aerospike module.

---

## Kobra S1 / FDM print workflow (reference)

- Use **`engine.3mf`** first — explicit mm units.
- Orientation: **injector flange on bed, nozzle up**; add a **brim**.
- Starting settings: **0.2 mm layers, 2–3 perimeters, 10–15% infill, PLA or
  PETG**.
- **`engine_cutaway.3mf`** for display — lay on cut face; tree supports under
  bell if needed.
- Lower **`voxelSizeMM` to 0.2** before `design` for cleaner small features
  (slower generation).
- **Display model only** — do not hot-fire FDM plastic.
- Internal channels will **not print fully** at FDM resolution — expected for
  metal-AM-sized features.

---

## Recommended next three steps

1. **Fix demo spec** — `iterate` or tune cooling until validation passes.
2. **1D regen solver** — root fix for coolant ΔT and channel sizing.
3. **Parameter sweep** — explore Pc / O/F / ε without hand-editing JSON each
   time.

---

## Tracking

Use this file as the master backlog. When an item ships, move a short note to
[08-roadmap.md](08-roadmap.md) or strike it here with the commit/PR reference.
Tier 1 items are good first PRs; Tier 2 items are the core engineering roadmap.

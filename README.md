# Impetus

Computational rocket thruster design and virtual testing, in the spirit of
[LEAP 71's](https://leap71.com/) Computational Engineering approach: the engine
is not drawn, it is **computed** from an input specification, then verified in
a virtual test, then iterated.

Built on [PicoGK](https://github.com/leap71/PicoGK), LEAP 71's open-source
voxel geometry kernel, and [SU2](https://su2code.github.io/), the open-source
CFD solver.

## The loop

```
spec.json ──► sizing physics ──► geometry (PicoGK) ──► STL / 3D view
                  │                                        ▲
                  └─► SU2 case ──► virtual test (CFD) ──► report ──► edit spec, repeat
```

1. **Specification** - thrust, chamber pressure, propellants, O/F ratio,
   expansion ratio... a small JSON file (`specs/`).
2. **Sizing physics** - ideal rocket theory (Sutton): throat/exit/chamber
   dimensions, Rao bell contour, mass flows, Isp; Bartz correlation for wall
   heat flux and a first-order regenerative-cooling balance.
3. **Geometry** - PicoGK builds the regeneratively-cooled chamber and bell
   nozzle with helical cooling channels, manifolds, showerhead injector plate
   and bolted flange. Output: printable STL + cutaway STL.
4. **Virtual test** - a structured 2D axisymmetric mesh of the hot-gas path is
   written directly in SU2 format (no external mesher), SU2 solves **RANS-SST**
   with an isothermal wall and wall-clustered mesh, and the exit plane is
   integrated to get CFD thrust / mass flow / exit Mach — compared side-by-side
   against the analytic prediction (and throat wall heat flux vs Bartz) in the
   report.
5. **Iterate** - change the spec, run again. Generation takes seconds,
   the CFD a few minutes.

## Setup from zero (Windows)

**1. Install the .NET SDK** (one-time, ~2 min):

```powershell
winget install Microsoft.DotNet.SDK.10
```

Open a *new* terminal afterwards so `dotnet` is on the PATH.
Verify: `dotnet --version` → `10.x`.

**2. Install the SU2 CFD solver** (one-time, only needed for virtual tests):

1. Download `SU2-vX.Y.Z-win64-omp.zip` from the
   [SU2 releases page](https://github.com/su2code/SU2/releases).
2. The release zip contains a **nested** `win64-omp.zip` — extract twice.
3. Place the contents so this file exists:
   `tools/SU2/bin/SU2_CFD.exe`
   (anywhere under `tools/` works; alternatively set the `IMPETUS_SU2`
   environment variable to the full path of `SU2_CFD.exe`).

**3. Build** (first build restores PicoGK automatically from NuGet,
including its native voxel runtime — no separate install):

```powershell
cd C:\Users\<you>\Impetus
dotnet build src/Impetus
```

**4. Sanity check** (build, unit tests, PicoGK smoke):

```powershell
dotnet build Impetus.sln
dotnet test tests/Impetus.Tests
dotnet run --project src/Impetus -- smoke
```

`smoke: OK` means geometry generation is fully operational.

## Starting / using it

All commands run from the repository root:

```powershell
# FULL LOOP: sizing -> geometry/STL -> CFD virtual test -> report
dotnet run --project src/Impetus -- all specs/demo-1kN.json

# fast inner loop: sizing + geometry + STL/3MF only (seconds-to-minutes)
dotnet run --project src/Impetus -- design specs/demo-1kN.json

# slicer / bed-fit summary without regenerating geometry (fast)
dotnet run --project src/Impetus -- print specs/demo-1kN.json

# virtual test only (SU2 CFD, ~30 min on a typical desktop)
dotnet run --project src/Impetus -- test specs/demo-1kN.json

# re-analyze an existing CFD result without re-running the solver
dotnet run --project src/Impetus -- post specs/demo-1kN.json

# virtual pass/fail gate: validation checks + hot-fire test plan
dotnet run --project src/Impetus -- validate specs/demo-1kN.json

# auto-iterate: apply corrective rules to the spec until the virtual gates
# pass (writes a new revision spec + iteration log)
dotnet run --project src/Impetus -- iterate specs/demo-1kN.json

# interactive 3D view (PicoGK viewer window, shows the cutaway)
dotnet run --project src/Impetus -- view specs/demo-1kN.json
```

With no arguments, `all specs/demo-1kN.json` is assumed.

To design **your own engine**: copy `specs/demo-1kN.json`, change the values
(see [docs/06-spec-reference.md](docs/06-spec-reference.md)), run `design`,
read the report, iterate. Run `test`/`all` when the design is worth a CFD
verdict.

Results land in `designs/<name>/`:

| File | Content |
|---|---|
| `report.md` | full design dossier: performance, dimensions, thermal, CFD vs analytic |
| `engine.stl` | the engine, millimeter units, watertight — ready for slicing |
| `engine.3mf` | same assembly as STL, 3MF with mm units — **three objects:** `body`, `injector`, `flange` |
| `engine_cutaway.stl` | half-section showing cooling channels and injector |
| `engine_cutaway.3mf` | cutaway in 3MF format |
| `cfd/flow.vtu` | CFD flow field (open in ParaView) |
| `cfd/su2.log` | solver log |

## 3D-printing the demo engine (FDM)

### STL or 3MF?

Impetus exports **both** formats on every `design` / `all` run:

| Format | Notes |
|---|---|
| **`.stl`** | Universal — every slicer accepts it. |
| **`.3mf`** | ZIP + XML with explicit **millimeter** units. Preferred for Anycubic Slicer Next / Kobra S1. |

**For your Anycubic Kobra S1 Combo:** import **`engine.3mf`** (or `engine.stl` — both are true-scale mm geometry, no scaling needed).

The models are true-scale millimeter geometry. The demo engine
(~Ø74 mm flange × ~185 mm tall) fits a 250 mm-class printer such as the
Kobra S1 with room to spare. This is a **display model** — FDM
plastic obviously doesn't fire; the flight-intent process for this geometry
is metal powder-bed fusion (CuCrZr / Inconel).

Recipe (Anycubic Slicer Next / OrcaSlicer):

1. Import `designs/<name>/engine.3mf` or `engine.stl` (units are mm — no scaling needed).
2. Orientation:
   - **Full engine:** stand it on the flat injector flange, nozzle up.
     All walls stay within ~30° of vertical, so it prints essentially
     support-free. Add a brim (it is tall and slender).
   - **Cutaway:** lay `engine_cutaway.stl` on its flat cut face — the
     channels and manifolds become visible; add tree supports for the bell
     overhang only.
3. Settings: 0.2 mm layers, 2–3 perimeters, 10–15 % infill, PLA or PETG.
4. Expect the Ø1.4 mm internal cooling channels and Ø0.6–0.8 mm injector
   orifices to print only partially at FDM resolution — they are sized for
   metal AM. For a crisper display piece, drop `voxelSizeMM` to `0.2` in the
   spec and regenerate (`design` takes a few minutes longer).

## The spec file

```json
{
  "name": "IMP-1K-A",
  "thrustN": 1000,
  "chamberPressureBar": 20,
  "ambientPressureBar": 1.01325,
  "propellants": "LOX_Kerosene",
  "ofRatio": 2.3,
  "expansionRatio": 0,
  "bellFraction": 0.8,
  "contractionRatio": 4.0,
  "wallThicknessMM": 3.0,
  "cooling": { "count": 24, "diameterMM": 1.4, "helixTurns": 1.0 },
  "voxelSizeMM": 0.4
}
```

- `propellants`: `LOX_Kerosene`, `LOX_Methane`, `LOX_Hydrogen`, `N2O_Ethanol`
- `expansionRatio: 0` means "optimal for the given ambient pressure"
- `voxelSizeMM`: geometry resolution; 0.4 mm is a good default for desktop-size engines

## Documentation

Detailed engineering documentation lives in [`docs/`](docs/01-overview.md):

| | |
|---|---|
| [01 Overview](docs/01-overview.md) | concept, the design loop, repo layout |
| [02 Architecture](docs/02-architecture.md) | code structure, data flow, design decisions |
| [03 Physics](docs/03-physics.md) | every equation with references (Sutton, Huzel & Huang, Bartz, Rao) |
| [04 Geometry](docs/04-geometry.md) | voxel geometry generation with PicoGK |
| [05 CFD](docs/05-cfd.md) | mesh, SU2 setup line by line, post-processing math |
| [06 Spec reference](docs/06-spec-reference.md) | every input field, ranges, iteration recipes |
| [07 Workflow](docs/07-workflow.md) | install, run, iterate, troubleshoot |
| [08 Roadmap](docs/08-roadmap.md) | limitations in detail and upgrade paths |
| [09 Improvement backlog](docs/09-improvement-backlog.md) | prioritized backlog: quick wins, physics, print workflow, UX |

## Honest limitations (v1)

- Combustion gas properties come from **CEA-equilibrium tables** in `data/gas/`
  (Tc, γ, M vs O/F and Pc). γ is frozen through nozzle expansion.
- The CFD is **RANS-SST** axisymmetric with an isothermal wall: it verifies nozzle
  aerodynamics and reports throat wall heat flux vs Bartz, not conjugate cooling
  or combustion in the chamber.
- Bartz + 1D regen march (`RegenSolver.cs`) — outlet ΔT, peak wall temp, channel
  Δp; not conjugate CFD.
- Injector is a geometric showerhead pattern with orifice-area sizing only.

## Roadmap

- [x] CEA-derived gas property tables (O/F and Pc dependent)
- [x] RANS (viscous) nozzle CFD + wall heat flux extraction from SU2
- [x] 1D regen channel solver (pressure drop, wall temperature profile)
- [ ] Aerospike module
- [ ] Parameter sweeps / optimization driver ("generate 50 candidates, keep the best")

## Credits

- [PicoGK](https://github.com/leap71/PicoGK) (Apache-2.0) by LEAP 71
- [SU2](https://su2code.github.io/) (LGPL-2.1) by the SU2 Foundation
- Methods: Sutton & Biblarz *Rocket Propulsion Elements*; Huzel & Huang
  *Modern Engineering for Design of Liquid-Propellant Rocket Engines*;
  Bartz (1957); Rao (1958)

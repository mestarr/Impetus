# 1. Overview — What Impetus Is and How It Thinks

## 1.1 The idea

Impetus is a small **Computational Engineering Model (CEM)** for liquid rocket
thrusters. The term comes from [LEAP 71](https://leap71.com/), the company that
pioneered this way of working: instead of drawing an engine in CAD, you write
**code that encodes engineering knowledge**, feed it a specification, and the
code *computes* the complete design — dimensions, performance numbers and
manufacturable 3D geometry.

The key properties of this approach:

- **Deterministic** — the same spec always produces exactly the same engine.
  There is no neural network and no randomness. Every number can be traced to
  a formula, and every formula to a textbook reference.
- **Iterable** — because generation is fast (seconds for sizing + geometry,
  minutes for CFD), you can explore the design space rapidly: change one
  parameter, regenerate, compare reports.
- **Explainable** — if the throat diameter is 21.4 mm, you can point to the
  exact equation that made it so. Nothing is "drawn by feel".

LEAP 71's production system (Noyron RP) is proprietary, but they describe its
recipe publicly: *"analytical thermal and thrust models, heuristics, and
condensed aerospace engineering logic, all encoded in a coherent algorithmic
framework"*. Impetus is an honest, open, miniature implementation of that same
recipe — built on the same open-source geometry kernel LEAP 71 publishes
([PicoGK](https://github.com/leap71/PicoGK)), with the
[SU2](https://su2code.github.io/) CFD solver bolted on as a virtual test stand.

## 1.2 The design loop

```
        ┌──────────────────────────────────────────────────────────┐
        │                      spec.json                            │
        │   thrust, Pc, propellants, O/F, ε, cooling, wall, ...     │
        └──────────────┬───────────────────────────────────────────┘
                       ▼
        ┌──────────────────────────────┐
        │  SIZING PHYSICS  (instant)   │   ideal rocket theory,
        │  EngineSizing / NozzleContour│   Rao bell, Bartz thermal
        │  ThermalModel                │
        └──────┬───────────────┬───────┘
               ▼               ▼
   ┌───────────────────┐   ┌──────────────────────┐
   │ GEOMETRY (PicoGK) │   │ VIRTUAL TEST (SU2)   │
   │ voxel engine      │   │ axisym. RANS-SST CFD │
   │ → engine.stl      │   │ → thrust, Mach, p    │
   │ → cutaway.stl     │   │ → flow.vtu (ParaView)│
   └─────────┬─────────┘   └──────────┬───────────┘
             ▼                        ▼
        ┌──────────────────────────────────────────┐
        │            report.md                      │
        │  analytic vs CFD, side by side            │
        └──────────────┬───────────────────────────┘
                       ▼
              engineer edits spec.json  ──►  loop again
```

The human stays in the loop as the **decision maker** (per the project's own
philosophy: the tool assists, it does not replace judgment). Impetus computes;
you interpret the report, adjust the spec, and converge.

## 1.3 What v1 honestly does — and does not do

| Does | Does not (yet) |
|---|---|
| Size a complete bell-nozzle thruster from thrust/Pc/propellants | Aerospike or annular geometries |
| Generate manufacturable voxel geometry with regen cooling channels | Validate printability (overhangs, min wall checks) |
| Predict Isp, c*, CF, mass flows from ideal rocket theory | Full NASA-CEA chemical equilibrium (uses textbook gas tables) |
| Estimate wall heat flux (Bartz) and coolant temperature rise | Conjugate heat transfer / detailed channel hydraulics |
| Run a real compressible CFD of the hot-gas path and integrate thrust | Combustion simulation, conjugate cooling (RANS-SST + isothermal wall in v1) |
| Produce a traceable engineering report | Optimize automatically (no search loop yet) |

Every limitation above is a deliberate v1 scope cut, listed with the upgrade
path in [08-roadmap.md](08-roadmap.md).

## 1.4 Repository layout

```
Impetus/
├── specs/                  ← input specifications (JSON), the only thing you edit
│   └── demo-1kN.json
├── src/Impetus/            ← the computational model (C# / .NET 10)
│   ├── Program.cs          ← CLI entry (delegates to Cli/ImpetusApp.cs)
│   ├── Cli/                ← command orchestration
│   ├── EngineSpec.cs       ← spec schema + loader
│   ├── Physics/
│   ├── Geometry/
│   │   ├── ThrusterBuilder.cs
│   │   └── MeshExport.cs   ← STL + 3MF export
│   ├── Cfd/
│   └── Reporting/
│       ├── DesignReport.cs
│       └── PrintReport.cs  ← FDM / slicer section in report.md
├── tests/Impetus.Tests/    ← unit tests (xUnit)
├── designs/                ← generated output (gitignored)
│   └── <engine name>/
│       ├── report.md
│       ├── engine.stl / engine.3mf
│       ├── engine_cutaway.stl / engine_cutaway.3mf
│       └── cfd/ (mesh, config, log, flow field)
├── tools/SU2/              ← SU2 binaries (gitignored)
├── Impetus.sln
└── docs/
```

## 1.5 Documentation map

| Document | Contents |
|---|---|
| [02-architecture.md](02-architecture.md) | Code structure, data flow, design decisions |
| [03-physics.md](03-physics.md) | Every equation, with symbols, units and references |
| [04-geometry.md](04-geometry.md) | How voxel geometry is built with PicoGK |
| [05-cfd.md](05-cfd.md) | Mesh, solver setup, boundary conditions, post-processing |
| [06-spec-reference.md](06-spec-reference.md) | Every spec field: meaning, units, sane ranges |
| [07-workflow.md](07-workflow.md) | Install, run, iterate, troubleshoot |
| [08-roadmap.md](08-roadmap.md) | Limitations in detail + planned upgrades |
| [09-improvement-backlog.md](09-improvement-backlog.md) | Prioritized improvement backlog (all tiers) |

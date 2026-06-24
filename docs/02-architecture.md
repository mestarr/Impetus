# 2. Architecture

## 2.1 Design principles

1. **One direction of data flow.** `EngineSpec` (input) → `EngineDesign`
   (derived state) → consumers (geometry, CFD, report). Nothing writes back
   into the spec; nothing downstream mutates the design. This makes every run
   reproducible and every artifact traceable to its inputs.

2. **Physics is dependency-free.** Everything under `src/Impetus/Physics/` is
   pure C# math — no PicoGK, no file I/O. It can be unit-tested headlessly and
   reused (e.g. a future optimizer can call `EngineSizing.Size()` ten thousand
   times without touching geometry).

3. **PicoGK touchpoints are concentrated.** Only `Geometry/ThrusterBuilder.cs`
   and `Program.cs` reference PicoGK types. If PicoGK's API changes, the blast
   radius is two files.

4. **The CFD case is self-contained.** `designs/<name>/cfd/` holds everything
   SU2 needs (mesh + config). You can re-run, edit the config by hand, or
   archive the folder; nothing depends on global state. The mesh is written in
   SU2's native ASCII format directly from the contour — there is **no
   external mesher** in the loop.

5. **Culture-invariant numerics everywhere.** All file formats (`.su2`,
   `.cfg`, CSV parsing) use `CultureInfo.InvariantCulture`. This matters: on a
   Croatian-locale Windows machine, `1.5.ToString()` produces `"1,5"`, which
   would silently corrupt mesh files. All writers/parsers in Impetus are
   locale-proof.

## 2.2 Data flow, type by type

```
EngineSpec  (record, from JSON)
    │  EngineSizing.Size()
    ▼
EngineDesign (record: all scalars — radii, flows, Isp, angles, orifice counts)
    │            │
    │            │  new NozzleContour(design)
    │            ▼
    │       NozzleContour (list of ContourPoint(Z,R) along the inner wall,
    │            │         + ThroatZ / ExitZ landmarks, RadiusAt(), Resampled())
    │            │
    ├────────────┼──► ThermalModel.Evaluate(design, contour) ──► ThermalResult
    │            │
    ├────────────┼──► ThrusterBuilder(lib, design, contour)  ──► Voxels ──► STL
    │            │
    ├────────────┼──► Su2Case(design, contour).Write(dir)    ──► mesh.su2 + engine.cfg
    │            │                                                  │ Su2Runner.Run()
    │            │                                                  ▼
    │            │                                              su2.log, surface_flow.csv,
    │            │                                              flow.vtu, restart_flow.dat
    │            │                                                  │ Su2Runner.PostProcess()
    │            │                                                  ▼
    │            │                                              CfdResult
    │            ▼
    └──► DesignReport.Build(...) + CfdSection(...) ──► report.md
```

### `EngineSpec` (`EngineSpec.cs`)

Immutable C# record deserialized from JSON (case-insensitive, comments and
trailing commas tolerated). Carries only *decisions*, never derived values.
Two convenience accessors convert bar → Pa (`Pc`, `Pa`).

### `EngineDesign` (`Physics/EngineSizing.cs`)

The complete derived design state in SI units. Required-init record: it is
impossible to construct one with missing fields. Contains performance
(Isp, c*, CF, mass flows), geometry scalars (radii, lengths, bell angles) and
injector sizing. Holds references to its `EngineSpec` and `CombustionGas` so
consumers need only one object.

### `NozzleContour` (`Physics/NozzleContour.cs`)

The inner wall line of the whole engine as an ordered polyline in the (z, r)
half-plane. Built once from arcs/cone/Bézier (see [03-physics.md](03-physics.md)
§3.5), then queried by everyone else:

- `RadiusAt(z)` — linear interpolation, used by the CFD mesher for every axial station;
- `Resampled(n, zFrom, zTo)` — arc-length-uniform resampling, used by the
  geometry builder for revolve stacking and cooling channel paths;
- `ThroatZ` / `ExitZ` — axial landmarks.

### `ThrusterBuilder` (`Geometry/ThrusterBuilder.cs`)

Consumes design + contour, produces PicoGK `Voxels`. Stateless w.r.t. files —
the caller decides what to export. Detail in [04-geometry.md](04-geometry.md).

### `Su2Case` / `Su2Runner` (`Cfd/`)

`Su2Case` writes the structured axisymmetric mesh and a fully parameterized
solver config (gas constants, boundary pressures and temperatures all come
from the design). `Su2Runner` finds the solver binary, runs it with sensible
threading, captures the log, then integrates the exit plane from SU2's surface
CSV into `CfdResult`. Detail in [05-cfd.md](05-cfd.md).

### `DesignReport` (`Reporting/DesignReport.cs`)

Pure formatter: design + thermal (+ optional CFD) → markdown tables and a
short console summary. No logic, no state.

## 2.3 The CLI (`Program.cs`)

```
impetus design [spec.json]   sizing + geometry → STL + report
impetus test   [spec.json]   sizing + SU2 case → run CFD → report with CFD section
impetus all    [spec.json]   both (default command)
impetus view   [spec.json]   open PicoGK 3D viewer with the cutaway
```

Notable mechanics:

- **Headless vs viewer mode.** PicoGK 2.x allows a plain `new Library(voxelSize)`
  for headless work (used by `design`/`all` so the pipeline can run in CI or
  scripts). The interactive viewer needs the `Library.Go(...)` entry point,
  which spawns the render window and runs the build task on a worker thread —
  only `view` uses that.
- **Repo-root discovery.** Output goes to `<repo>/designs/<name>/` regardless
  of the working directory: the program walks up from the executable location
  until it finds the `specs/` folder or `.git`.
- **SU2 discovery.** `tools/**/SU2_CFD.exe` is searched the same way; the
  `IMPETUS_SU2` environment variable overrides it.

## 2.4 Why these technology choices

| Choice | Reason |
|---|---|
| **C# / .NET** | It is the language of the entire LEAP 71 stack; PicoGK is a C# library. One language end-to-end, no glue scripts. |
| **PicoGK (voxels)** | Voxel booleans are unconditionally robust — no failed fillets, no non-manifold edge cases as in B-rep CAD kernels. Complex internal passages (helical channels) are trivial: union of beams, subtract. It is also literally the kernel the LEAP 71 engines are built with. |
| **SU2** | Serious open-source compressible-flow solver, native Windows binaries, text-file in/out (easy to generate and parse), proven for nozzle flows, LGPL. |
| **Own structured mesher** | The hot-gas path of a bell nozzle is a single curved channel — a textbook case for a structured quad grid. Writing the 200 lines ourselves removes gmsh as a dependency and makes mesh quality deterministic. |
| **JSON specs** | Human-diffable. A future optimizer or UI can generate them trivially. |

## 2.5 Concurrency & performance notes

- Sizing physics: microseconds. Contour: sub-millisecond.
- Geometry: dominated by voxel ops; at 0.4 mm voxels a ~1 kN engine takes a
  few seconds to render and Boolean.
- CFD: SU2 runs multi-threaded (OpenMP build; `OMP_NUM_THREADS` is set to
  `cores − 1` by `Su2Runner`). The supplied 240×44 mesh converges in minutes
  on a typical desktop.
- The SU2 process is sandboxed by a 45-minute hard timeout and writes
  everything into the case folder, so a hung run cannot poison anything else.

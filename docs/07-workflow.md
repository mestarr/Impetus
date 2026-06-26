# 7. Workflow — Install, Run, Iterate, Troubleshoot

## 7.1 Prerequisites

| Tool | Version | Install |
|---|---|---|
| .NET SDK | 10.x | `winget install Microsoft.DotNet.SDK.10` |
| SU2 | 8.x win64-omp | download `SU2-vX.Y.Z-win64-omp.zip` from [SU2 releases](https://github.com/su2code/SU2/releases), extract so that `tools/SU2/bin/SU2_CFD.exe` exists (the release zip contains a nested `win64-omp.zip` — extract twice). Alternatively set `IMPETUS_SU2` to the full path of any `SU2_CFD.exe`. |
| ParaView (optional) | any recent | for viewing `flow.vtu` — [paraview.org](https://www.paraview.org/) |

PicoGK installs itself: it is a NuGet package (`dotnet build` restores it,
including the native voxel runtime).

## 7.2 Commands

Run everything from the repository root:

```powershell
# Full loop: sizing → geometry/STL → CFD virtual test → report
dotnet run --project src/Impetus -- all specs/demo-1kN.json

# Fast inner loop while shaping the design (no CFD, seconds):
dotnet run --project src/Impetus -- design specs/demo-1kN.json

# Virtual test only (writes + runs the SU2 case):
dotnet run --project src/Impetus -- test specs/demo-1kN.json

# FDM / slicer summary without geometry (fast):
dotnet run --project src/Impetus -- print specs/demo-1kN.json

# Interactive 3D inspection (PicoGK viewer, shows the cutaway):
dotnet run --project src/Impetus -- view specs/demo-1kN.json
```

With no arguments, `all` + `specs/demo-1kN.json` is assumed.

There is also a hidden diagnostic:

```powershell
# PicoGK sanity check: one beam -> voxels -> boolean -> STL, headless
dotnet run --project src/Impetus -- smoke
```

If `smoke` passes but engine generation fails, the problem is in the engine
geometry; if `smoke` itself fails, the PicoGK runtime/installation is the
suspect (see troubleshooting below).

## 7.3 The intended iteration rhythm

1. **Copy** a spec: `specs/demo-1kN.json` → `specs/my-engine-A.json`,
   change `name` accordingly.
2. `design` it (seconds). Read the console summary: Isp plausible? coolant ΔT
   sane? dimensions printable?
3. Look at the **cutaway STL** (drag `engine_cutaway.stl` into any STL viewer,
   or run `view`): channels intact? wall thickness look right? injector
   pattern reasonable?
4. `test` it (minutes). Read the CFD section of `designs/<name>/report.md`:
   thrust/mass-flow deviation small? exit profile healthy?
5. Edit the spec (see iteration recipes in
   [06-spec-reference.md](06-spec-reference.md)), bump the revision letter,
   repeat. Old revisions keep their folders — diff the reports.

## 7.4 Outputs explained

```
designs/IMP-1K-A/
├── report.md            ← the dossier: spec, performance, geometry,
│                          thermal, injector, CFD-vs-analytic table
├── engine.stl           ← full engine, mm units, watertight
├── engine.3mf           ← same geometry, 3MF with explicit mm units
├── engine_cutaway.stl   ← y<0 half removed: inspect channels & manifolds
├── engine_cutaway.3mf   ← cutaway in 3MF
└── cfd/
    ├── mesh.su2         ← structured axisymmetric mesh (text)
    ├── engine.cfg       ← complete SU2 configuration (text, hand-editable)
    ├── su2.log          ← full solver output
    ├── history.csv      ← residual/iteration history
    ├── surface_flow.csv ← exit-plane nodal data (thrust integration source)
    ├── flow.vtu         ← volume flow field → ParaView
    └── restart_flow.dat ← SU2 restart (re-run continues from here if enabled)
```

## 7.5 Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `dotnet` not found | new shell after SDK install, or PATH not refreshed — open a fresh terminal, or use the full path `C:\Program Files\dotnet\dotnet.exe` |
| Build error mentioning PicoGK runtime / DllNotFoundException | the native runtime ships in the NuGet package for win-x64; ensure you build/run on x64 and not through an x86 host process. `dotnet --info` should say `RID: win-x64` |
| `SU2_CFD.exe not found under tools/` | check `tools/SU2/bin/SU2_CFD.exe` exists (nested-zip trap, see 7.1), or set `IMPETUS_SU2` |
| SU2 crashes instantly | run it manually in `designs/<name>/cfd/` to see the message; usual causes are a hand-edited config typo or an antivirus quarantining the exe |
| CFD diverges (NaN residuals) | extremely off-design spec (e.g. huge ε at sea level). Reduce ε; if needed lower `CFL_ADAPT_PARAM` max in `Su2Case.cs` |
| Geometry has holes/membranes at the ends | should not happen (inner void is extended ±2 mm past both ends) — if you modify `ThrusterBuilder`, keep that padding |
| Viewer window doesn't open over remote desktop | PicoGK's viewer needs a real GPU surface; use `design` headless mode and inspect STLs instead |
| Everything is slow the first run | one-time costs: NuGet restore, native runtime JIT, SU2 first-touch under antivirus. Subsequent runs are fast |
| Numbers look absurd | check the spec units: thrust **N**, pressure **bar**, lengths **mm** — a 1000 *bar* chamber will happily size a monster |

## 7.6 Git etiquette

`designs/` and `tools/` are gitignored — generated artifacts and binaries stay
out of the repo. Version *specs* and *code*. (This repository is local-only;
nothing is committed or pushed automatically.)

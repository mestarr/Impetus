# 4. Geometry Generation with PicoGK

All geometry code lives in `src/Impetus/Geometry/ThrusterBuilder.cs`.
PicoGK conventions: **millimeters**, right-handed coordinates. Impetus puts
the engine axis on **+Z**, injector face at Z = 0, exhaust toward +Z. The
physics side works in meters; the single `MM()` helper converts at the
geometry boundary.

## 4.1 Why voxels (and not B-rep CAD)

PicoGK represents solids as **voxel fields** (narrow-band signed distance
fields on an OpenVDB grid). Three properties matter for engines:

1. **Booleans always succeed.** Union/subtract/intersect are trivial voxel
   operations. There are no "failed fillet", "non-manifold edge",
   "tolerance explosion" failure modes — the classic killers when generating
   complex CAD programmatically.
2. **Organic internal passages are natural.** A helical cooling channel is
   just a chain of beads (capsules) subtracted from the wall. In B-rep CAD
   that is a sweep along a 3D spline intersecting a revolved shell — fragile;
   here it is bulletproof.
3. **Watertight by construction.** The exported mesh is always manifold —
   exactly what 3D-printing toolchains want.

The cost: a resolution parameter (`voxelSizeMM`). At 0.4 mm a desktop-class
engine is crisp and still generates in seconds; halve the voxel size for
print-final output (8× memory/time, still fine).

## 4.2 The revolve technique

PicoGK has no built-in "revolve a profile" primitive — but it has conical
**lattice beams**. `AddBeam(A, B, rA, rB)` produces a capsule/cone between two
points with independent end radii. A body of revolution is therefore:

```
for each consecutive contour sample (zᵢ, rᵢ) → (zᵢ₊₁, rᵢ₊₁):
    AddBeam( (0,0,zᵢ), (0,0,zᵢ₊₁), rᵢ, rᵢ₊₁, flat caps )
```

i.e. a stack of ~300 conical frusta along the axis. The voxel union fuses the
stack into a single smooth revolved solid; at 0.4 mm voxels, 300 samples of
the C¹-continuous contour leave no visible facets.

The **wall** is then:

```
solid  = revolve(contour + wallThickness)   // outer surface
void   = revolve(contour, axially extended ±2 mm)
wall   = solid − void
```

The inner revolve is extended past both ends so the subtraction cleanly opens
the bore at the injector end and the nozzle exit (no membranes).

The radial (not surface-normal) offset for the outer wall slightly thickens
steep regions like the convergent cone — geometrically conservative, thinning
nowhere.

## 4.3 Cooling channels

Real regenerative engines route fuel through passages **inside** the nozzle
wall. Impetus implements them exactly that way:

- The wall thickness is auto-clamped to
  `max(spec.wallThicknessMM, channelD + 1.6)` so a channel always has ≥ 0.8 mm
  of liner (hot side) and closeout (cold side) around it.
- Each channel follows the contour at **mid-wall depth**:
  \( r_{path}(z) = r_{inner}(z) + t_{wall}/2 \).
- The path is sampled at 160 points uniform in meridional arc length
  (`NozzleContour.Resampled`), so bead spacing stays even through the throat
  where the wall turns sharply.
- The angular position advances with axial progress:
  \( \varphi(s) = \varphi_0 + 2\pi\,n_{turns}\,s \) — `helixTurns = 0` gives
  straight axial channels, `1.0` a gentle spiral (longer residence time,
  better coverage of the throat circumference).
- All `count` channels (default 24) are duplicated at equal angular offsets,
  built as one lattice, voxelized, and **subtracted** from the wall.

**Manifolds:** toroidal collector rings (64 beam segments each) at both ends
of the channel run, fattened 1.3× relative to the channels, also at mid-wall
depth — one near the injector face (fuel out → injector), one near the nozzle
exit (fuel in). They guarantee all channels are hydraulically connected in the
printed part.

Channel run extent: from 2 mm below the injector face to 94 % of the way to
the exit — the last few mm of the bell lip see little heat flux and are left
solid for stiffness.

## 4.4 Injector head

A geometric showerhead (v1):

- **Plate**: solid disc, thickness `max(6 mm, 2·wall)`, covering the chamber
  bore plus the wall, overlapping 0.5 mm into the chamber wall so the union is
  fused (no zero-thickness seam).
- **Flange**: a wider disc (+8 mm radius) at the cold side of the plate with
  an 8-bolt circle of Ø4.2 mm clearance holes (M4).
- **Orifices**, all drilled as thin beam-cylinders subtracted through the
  plate:
  - fuel ring at 0.72 R_c — count and Ø from the sizing model (§3.3.7);
  - oxidizer ring at 0.42 R_c — likewise;
  - central Ø3 mm igniter port.
- If the sized orifice count physically cannot fit on its ring at 1.6×Ø
  spacing, the builder caps the count to what fits (the report still shows the
  hydraulic requirement, so you can see the conflict and e.g. enlarge the
  chamber or orifice diameter in the spec).

This is a *geometric placeholder with correct hydraulic areas* — real injector
engineering (coax swirl elements, impinging doublets, film cooling rows) is a
roadmap item and slots into exactly this builder method.

## 4.5 Cutaway

For inspection, `voxCutaway()` intersects a copy of the engine with the
implicit half-space \(y \le 0\):

```csharp
class HalfSpaceY : IImplicit
{
    public float fSignedDistance(in Vector3 vec) => vec.Y;  // SDF of plane y=0
}
```

`Voxels(lib, implicit, bounds)` renders the half-space as voxels over the
engine's bounding box, and one Boolean subtract produces the section view —
this is `engine_cutaway.stl`, the file to look at to verify channels and
manifolds.

## 4.6 Outputs and modes

| Mode | PicoGK entry | What happens |
|---|---|---|
| `design` / `all` | `new Library(voxelSize)` (headless) | build voxels → `mshAsMesh().SaveToStlFile()` → `engine.stl`, `engine_cutaway.stl` |
| `view` | `Library.Go(voxelSize, task)` | opens PicoGK's interactive viewer, shows the cutaway with a copper-like material |

STLs are written in millimeters (PicoGK native), the unit every slicer
assumes by default.

## 4.7 Resolution guidance

| voxelSizeMM | Use |
|---|---|
| 0.6–0.8 | fast spec exploration, chunky but instant |
| **0.4** | **default: crisp channels, seconds-scale generation** |
| 0.2 | final geometry for printing/quoting |
| 0.1 | only if your machine has serious RAM, large engines become heavy |

Rule of thumb: voxel size ≤ ⅓ of the smallest feature (smallest feature in the
default spec = 0.8 mm liner → 0.27 mm ideal; 0.4 mm is acceptable for
iteration, drop to 0.2 mm before printing).

# 6. Specification Reference

The spec is the *only* input to the entire pipeline. One JSON file in
`specs/` per engine concept. All fields, their meaning, units, defaults and
sane ranges:

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

JSON parsing is case-insensitive and tolerates comments and trailing commas.

---

### `name` — string

Used for the output folder `designs/<name>/` and report headers. Use a
revision scheme (`IMP-1K-A`, `IMP-1K-B`, ...) — each revision keeps its own
folder, so you can diff reports across iterations.

### `thrustN` — number, newtons

Target thrust **at the design ambient pressure**. The throat is sized so the
engine delivers exactly this. Sane range for this model class: **50 – 50 000 N**
(see physics doc §3.6).

### `chamberPressureBar` — number, bar

Stagnation pressure in the combustion chamber. Drives:

- engine *size* for a given thrust (higher Pc → smaller throat),
- heat flux (Bartz scales with \(p_c^{0.8}\) — high Pc engines run hot),
- feed system difficulty in reality (pumps/pressurant).

Hobby/research class: 10–30 bar. Keep ≥ 7 bar for the choked-flow assumptions
to be comfortably valid.

### `ambientPressureBar` — number, bar

The backpressure the engine is optimized *for* (when `expansionRatio: 0`) and
tested *against* (the CFD outlet and the pressure-thrust term).

- `1.01325` — sea-level stand,
- `0.1`–`0.3` — high-altitude stage,
- small ≠ 0 value (e.g. `0.001`) — "vacuum" design (exactly 0 would make the
  optimal expansion ratio infinite).

### `propellants` — string enum

One of `LOX_Kerosene`, `LOX_Methane`, `LOX_Hydrogen`, `N2O_Ethanol`
(see `GasModel.cs` to add more — one table row each: Tc, molar mass, γ, L*,
liquid densities, fuel cp).

### `ofRatio` — number

Oxidizer-to-fuel **mass** ratio. Used for flow splits, injector sizing, and
**CEA gas-table lookup** (`data/gas/` — Tc, γ, M interpolated at your O/F and
Pc). Stay within each file's tabulated O/F range (see grid in the JSON).

Side effect: **lower O/F = more fuel = more coolant** (at a small Isp cost).

### `expansionRatio` — number

Nozzle exit-to-throat area ratio \(A_e/A_t\).

- **`0` (recommended start):** computed optimally so exit pressure = ambient.
- Explicit value: forces the geometry. Useful for studying off-design
  behavior (the CFD will happily show you the shock when you over-expand).

Keep within **3.5–50** — the Rao bell angle charts are tabulated there.

Rule of thumb if setting manually at sea level: avoid \(p_e < 0.4\,p_a\)
(Summerfield criterion) or the bell will flow-separate in reality.

### `bellFraction` — number

Bell length as a fraction of the equivalent 15° cone, default **0.8**
(the classic "80 % bell" — 99 %+ of the ideal thrust at 80 % of the length).
The θn/θe chart in v1 is calibrated for 0.8; values 0.6–1.0 scale the length
but reuse the same angle chart (acceptable approximation, noted in the code).

### `contractionRatio` — number

Chamber-to-throat area ratio \(A_c/A_t\), default **4**.

- Larger (5–8): slower chamber gas, gentler on the injector face, longer
  engine.
- Smaller (2.5–3.5): compact but the chamber Mach rises and the convergent
  blend radii start fighting for space — below ~2.5 the contour constructor
  will throw (the radial drop can't accommodate the blend arcs).
- **Auto-floor:** small engines silently get
  \(CR \ge 8\,D_{t,cm}^{-0.6} + 1.25\) (Huzel & Huang small-throat
  correlation) — the report's "Contraction ratio (applied)" row shows what was
  actually used. A 1 kN engine lands around 6–7 regardless of the spec value.

### `wallThicknessMM` — number, mm

Liner + channel + closeout total wall. **Auto-clamped** to
`cooling.diameterMM + 1.6` so a channel always retains ≥ 0.8 mm metal on each
side. For printability in CuCrZr/Inconel keep final walls ≥ 0.8 mm; the
defaults respect that.

### `cooling` — object

| Field | Default | Notes |
|---|---|---|
| `count` | 24 | channels around the circumference. More + smaller = better coverage, higher pressure drop. 16–60 typical. |
| `diameterMM` | 1.4 | bore of each channel. Sets coolant velocity (report shows it; 10–50 m/s healthy for kerosene). |
| `helixTurns` | 1.0 | total spiral turns over the engine length. `0` = straight axial. Helical channels lengthen the cooling path and look fantastic in the cutaway. |

### `voxelSizeMM` — number, mm

Geometry resolution. 0.4 default; 0.2 for print-final; see geometry doc §4.7.
Does **not** affect the CFD (the CFD meshes the analytic contour directly).

---

## Iteration recipes

| Symptom in report | Knob to turn |
|---|---|
| Coolant ΔT too high (> ~250 K for kerosene) | raise `cooling.count`/`diameterMM`, lower `chamberPressureBar`, lower `ofRatio` slightly, or accept film cooling (roadmap) — small engines are genuinely hard to regen-cool |
| Coolant velocity > ~50 m/s | larger `diameterMM` or more `count` |
| CFD shows shock in bell / no convergence | over-expanded: reduce `expansionRatio` or design for altitude (`ambientPressureBar` ↓) |
| CFD exit pressure ≫ ambient, thrust low | under-expanded: raise `expansionRatio` |
| Engine too long for the printer | `bellFraction` ↓ (e.g. 0.7), `contractionRatio` ↓ a notch |
| Injector orifice count capped (warning in geometry) | larger orifice Ø in `EngineSizing` constants, larger chamber (`contractionRatio` ↑) |

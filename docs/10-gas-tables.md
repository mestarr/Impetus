# Gas property tables (`data/gas/`)

Equilibrium combustion properties for sizing and CFD, bilinearly interpolated
in **O/F** and **chamber pressure [bar]**.

## Files

| File | Propellant | O/F grid | Pc grid [bar] |
|---|---|---|---|
| `LOX_Kerosene.json` | LOX / RP-1 | 1.6 – 3.4 | 10 – 30 |
| `LOX_Methane.json` | LOX / CH₄ | 2.5 – 5.0 | 10 – 30 |
| `LOX_Hydrogen.json` | LOX / LH₂ | 4.0 – 8.5 | 10 – 30 |
| `N2O_Ethanol.json` | N₂O / ethanol | 3.5 – 6.5 | 10 – 30 |

Each file also carries liquid-side constants (fuel/ox density, fuel cp, L*,
max coolant ΔT).

## JSON schema

```json
{
  "key": "LOX_Kerosene",
  "displayName": "LOX / Kerosene (RP-1)",
  "nominalOf": 2.3,
  "ofRatio": [1.6, 1.8, ...],
  "pcBar": [10, 15, 20, 25, 30],
  "tc": [[...], ...],
  "molarMass": [[...], ...],
  "gamma": [[...], ...]
}
```

Rows index **O/F**; columns index **Pc**. Values are NASA CEA equilibrium at
the stated conditions (condensed grids checked into the repo).

## Code seam

```csharp
CombustionGas gas = CombustionGas.Resolve(
    spec.Propellants, spec.OfRatio, spec.ChamberPressureBar);
```

Called from `EngineSizing.Size`. SU2 uses the same γ and R_s as the analytic model.

## Regenerating from NASA CEA

1. Run CEA for each (O/F, Pc) point in the grid (equilibrium, frozen products).
2. Extract chamber T, mean molar mass, and γ (or Cp/Cv).
3. Update the JSON arrays — dimensions must stay consistent.
4. No recompile; restart Impetus.

A scripted batch job under `tools/cea/` is a future convenience; the shipped
grids are sufficient for sizing within the tabulated envelope.

## Limitations

- **Frozen γ** through the nozzle — no shifting equilibrium / recombination in
  expansion (roadmap item).
- Grids **clamp** at edges — do not extrapolate far outside tabulated O/F or Pc.
- Liquid propellant properties are still fixed per pair (not P/T dependent).

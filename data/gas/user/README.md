# User-Defined Propellants

This directory allows you to define custom propellant combinations without recompiling Impetus.

## How to Add a Custom Propellant

1. Copy an existing gas table file from the parent directory (e.g., `../LOX_Methane.json`) as a template.
2. Rename it to your propellant pair name (e.g., `NTO_MMH.json`).
3. Edit the JSON file with your propellant data.
4. The propellant will be available in Impetus using the filename (without extension) as the key.

## Gas Table Format

The JSON file must contain the following fields:

```json
{
  "Key": "YourPropellantPair",
  "DisplayName": "Human-readable name",
  "NominalOf": 2.5,
  "LStarM": 1.0,
  "FuelDensity": 800.0,
  "OxDensity": 1140.0,
  "FuelCp": 3500.0,
  "MaxCoolantRiseK": 200.0,
  "Source": "Your data source",
  "OfRatio": [2.0, 2.5, 3.0],
  "PcBar": [10, 20, 30, 40],
  "Tc": [
    [3000, 3100, 3200, 3300],
    [3050, 3150, 3250, 3350],
    [3100, 3200, 3300, 3400]
  ],
  "MolarMass": [
    [0.020, 0.021, 0.022, 0.023],
    [0.019, 0.020, 0.021, 0.022],
    [0.018, 0.019, 0.020, 0.021]
  ],
  "Gamma": [
    [1.20, 1.19, 1.18, 1.17],
    [1.21, 1.20, 1.19, 1.18],
    [1.22, 1.21, 1.20, 1.19]
  ]
}
```

## Field Descriptions

- **Key**: Unique identifier used in spec files (e.g., `"propellants": "NTO_MMH"`)
- **DisplayName**: Human-readable name for reports
- **NominalOf**: Optimal O/F mass ratio
- **LStarM**: Characteristic chamber length [m] for combustion residence sizing
- **FuelDensity**: Liquid fuel density [kg/m³]
- **OxDensity**: Liquid oxidizer density [kg/m³]
- **FuelCp**: Liquid fuel specific heat [J/(kg·K)] for coolant temperature rise
- **MaxCoolantRiseK**: Maximum allowable coolant temperature rise [K]
- **Source**: Data source (e.g., CEA, literature, test data)
- **OfRatio**: Array of O/F ratios (must have ≥2 values)
- **PcBar**: Array of chamber pressures [bar] (must have ≥2 values)
- **Tc**: 2D array of adiabatic flame temperatures [K] (rows = O/F, cols = Pc)
- **MolarMass**: 2D array of product molar masses [kg/mol]
- **Gamma**: 2D array of specific heat ratios

## Data Sources

For accurate gas properties, use NASA CEA (Chemical Equilibrium with Applications) or similar tools. The tables should cover the expected operating range of your engine.

## Overriding Built-in Propellants

If you create a file with the same Key as a built-in propellant, your version will override the built-in one. This is useful for:
- Updating gas tables with new test data
- Adjusting properties for specific operating conditions
- Testing sensitivity to gas property variations

"""Vortex-lattice estimate for the Impetus vehicle.

Uses AeroSandbox (MIT), https://github.com/peterdsharpe/AeroSandbox
The wing and tail match RocketBody for the 5 second demo: NACA 2410,
span 1.5 m, and a NACA 0009 tail. Drag from this method is induced drag.
It is not SU2, and it is not a wind tunnel.

    py -3.13 aero.py
"""
import json
from pathlib import Path

import aerosandbox as asb
import numpy as np

OUT = Path(__file__).with_name("aero.json")
FLOW = Path(__file__).with_name("flow.json")
STL = Path(__file__).with_name("rocket-view.stl")

# Meters. AeroSandbox: +x aft, +y right, +z up.
# The plate is 676 mm across, centered at x = -0.249, 102 mm thick in the middle.
CENTER_X = -0.249
RADIUS = 0.338
HUB_H = 0.051
FIN_X = CENTER_X + 0.35 * (RADIUS * 0.70)
FIN_ROOT_Z = HUB_H
SPEED = 50.0


def naca_loop(code: str, n: int = 81):
    m = int(code[0]) / 100.0
    p = int(code[1]) / 10.0
    t = int(code[2:]) / 100.0
    x = 0.5 * (1.0 - np.cos(np.linspace(0.0, np.pi, n)))
    yt = 5.0 * t * (
        0.2969 * np.sqrt(np.clip(x, 0, None))
        - 0.1260 * x
        - 0.3516 * x**2
        + 0.2843 * x**3
        - 0.1015 * x**4
    )
    yc = np.zeros_like(x)
    slope = np.zeros_like(x)
    if m > 0 and 0 < p < 1:
        left = x < p
        yc[left] = m / p**2 * (2 * p * x[left] - x[left] ** 2)
        yc[~left] = m / (1 - p) ** 2 * ((1 - 2 * p) + 2 * p * x[~left] - x[~left] ** 2)
        slope[left] = 2 * m / p**2 * (p - x[left])
        slope[~left] = 2 * m / (1 - p) ** 2 * (p - x[~left])
    th = np.arctan(slope)
    xu, yu = x - yt * np.sin(th), yc + yt * np.cos(th)
    xl, yl = x + yt * np.sin(th), yc - yt * np.cos(th)
    upper = np.column_stack([xu[::-1], yu[::-1]])
    lower = np.column_stack([xl[1:], yl[1:]])
    return np.vstack([upper, lower])


def airplane():
    foil = asb.Airfoil(name="naca0015", coordinates=naca_loop("0015"))
    fin_foil = asb.Airfoil(name="naca0009", coordinates=naca_loop("0009"))
    diameter = RADIUS * 2.0
    stations = [0.0, 0.35, 0.65, 0.92]
    xsecs = []
    for frac in stations:
        chord = diameter * np.sqrt(max(0.0, 1.0 - frac * frac))
        chord = max(chord, 0.02)
        xsecs.append(asb.WingXSec(
            xyz_le=[(diameter - chord) * 0.5, frac * RADIUS, 0.0],
            chord=float(chord),
            airfoil=foil,
        ))
    wing = asb.Wing(name="Plate", symmetric=True, xsecs=xsecs).translate([CENTER_X - RADIUS, 0.0, 0.0])
    fin = asb.Wing(
        name="Fin",
        symmetric=False,
        xsecs=[
            asb.WingXSec(xyz_le=[0.0, 0.0, 0.0], chord=0.080, airfoil=fin_foil),
            asb.WingXSec(xyz_le=[0.020, 0.0, 0.070], chord=0.045, airfoil=fin_foil),
        ],
    ).translate([FIN_X, 0.0, FIN_ROOT_Z])
    return asb.Airplane(name="IMP-1K-A", xyz_ref=[CENTER_X, 0.0, 0.0], wings=[wing, fin])


def view_center_mm():
    """Same center three.js uses when it recenters rocket-view.stl."""
    with STL.open("rb") as f:
        f.seek(80)
        n = int(np.frombuffer(f.read(4), dtype="<u4")[0])
    dt = np.dtype([("n", "<f4", (3,)), ("v", "<f4", (3, 3)), ("a", "<u2")])
    rec = np.memmap(STL, dtype=dt, mode="r", offset=84, shape=(n,))
    verts = np.asarray(rec["v"]).reshape(-1, 3)
    return (verts.min(0) + verts.max(0)) * 0.5


def plate_half_h(frac):
    if frac <= 0.70:
        return HUB_H
    u = (frac - 0.70) / 0.30
    u = u * u * (3.0 - 2.0 * u)
    return HUB_H + (0.012 - HUB_H) * u


def inside_body(x, y, z):
    radius = float(np.hypot(x - CENTER_X, y))
    if radius > RADIUS + 0.004:
        return False
    return abs(float(z)) < plate_half_h(radius / RADIUS) + 0.004


def seeds():
    ys = np.linspace(-0.46, 0.46, 8)
    zs = np.array([-0.02, 0.03, 0.08, 0.14])
    return np.array([[CENTER_X - RADIUS - 0.12, y, z] for z in zs for y in ys], dtype=float)


def solve(plane, alpha: float):
    vlm = asb.VortexLatticeMethod(
        airplane=plane,
        op_point=asb.OperatingPoint(velocity=SPEED, alpha=alpha),
        spanwise_resolution=12,
        chordwise_resolution=6,
        align_trailing_vortices_with_wind=True,
    )
    got = vlm.run()
    cl, cd = float(got["CL"]), float(got["CD"])
    raw = vlm.calculate_streamlines(seed_points=seeds(), n_steps=56, length=2.05)
    wind = np.asarray(vlm.steady_freestream_velocity, dtype=float)
    row = {
        "alpha_deg": alpha,
        "CL": round(cl, 4),
        "CD": round(cd, 5),
        "L_over_D": None if cd <= 1e-6 else round(cl / cd, 2),
        "lift_N": round(float(got["L"]), 1),
        "drag_N": round(float(got["D"]), 2),
    }
    return row, raw, wind


def to_viewer_mm(raw, center):
    """AeroSandbox meters (+x aft, +y right, +z up) to the centered viewer frame."""
    lines = []
    for i in range(raw.shape[0]):
        pts = []
        for k in range(raw.shape[2]):
            x, y, z = (float(v) for v in raw[i, :, k])
            if not np.isfinite([x, y, z]).all() or inside_body(x, y, z):
                break
            local = np.array([y * 1000.0, z * 1000.0, x * 1000.0]) - center
            pts.append([round(float(v), 1) for v in local])
        if len(pts) >= 6:
            lines.append(pts)
    return lines


def main():
    plane = airplane()
    area = float(plane.s_ref)
    center = view_center_mm()
    rows = []
    cases = []
    for alpha in range(-4, 15, 2):
        row, raw, wind = solve(plane, float(alpha))
        rows.append(row)
        lines = to_viewer_mm(raw, center)
        cases.append({"alpha_deg": alpha, "lines": lines})
        print(f"  alpha {alpha:5.0f}  lines {len(lines):2d}  wind {np.round(wind, 2)}")
    payload = {
        "tool": "AeroSandbox VortexLatticeMethod",
        "repo": "https://github.com/peterdsharpe/AeroSandbox",
        "license": "MIT",
        "speed_m_s": SPEED,
        "wing_area_m2": round(area, 4),
        "note": (
            "The lines in the viewer are the airflow from this same vortex lattice, "
            "drawn on the round plate. Drag is induced drag only. "
            "Not a wind tunnel, and not the SU2 nozzle run. "
            "The vehicle is sized for a 5 second rocket burn. It is not a flown airplane."
        ),
        "points": rows,
    }
    OUT.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    FLOW.write_text(json.dumps({"cases": cases}, separators=(",", ":")), encoding="utf-8")
    print(f"wing area {area:.3f} m^2, speed {SPEED:.0f} m/s")
    print("alpha   CL     CDi    L/D    lift N")
    for row in rows:
        print(
            f"{row['alpha_deg']:5.0f}  {row['CL']:7.3f}  {row['CD']:7.4f}  "
            f"{row['L_over_D']!s:>6}  {row['lift_N']:7.1f}"
        )
    print("wrote", OUT)
    print("wrote", FLOW, "bytes", FLOW.stat().st_size)


if __name__ == "__main__":
    main()

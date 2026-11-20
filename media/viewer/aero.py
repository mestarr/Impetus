"""Aerodynamic estimate for the round plate.

Uses AeroSandbox (MIT), https://github.com/peterdsharpe/AeroSandbox
Forces include induced drag and airfoil skin friction (AeroBuildup).
Streamlines come from the vortex lattice and are kept outside the plate.
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


def scalar(value) -> float:
    return float(np.asarray(value).reshape(-1)[0])


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


CLEARANCE = 0.008


def seeds():
    """A curtain of lines that meet the plate, not a wide field that misses it."""
    ys = np.linspace(-0.22, 0.22, 5)
    zs = np.linspace(-0.16, 0.20, 8)
    return np.array([[CENTER_X - RADIUS - 0.22, y, z] for z in zs for y in ys], dtype=float)


def body_distance(x, y, z):
    dx = float(x) - CENTER_X
    radius = float(np.hypot(dx, y))
    half_h = plate_half_h(min(radius / RADIUS, 1.0))
    radial = radius - RADIUS
    vertical = abs(float(z)) - half_h
    if radial > 0.0 and vertical > 0.0:
        return float(np.hypot(radial, vertical))
    return float(max(radial, vertical))


def outward(x, y, z):
    dx = float(x) - CENTER_X
    radius = float(np.hypot(dx, y))
    half_h = plate_half_h(min(radius / RADIUS, 1.0))
    radial = radius - RADIUS
    vertical = abs(float(z)) - half_h
    if radial > 0.0 and vertical > 0.0:
        scale = 1.0 / max(float(np.hypot(radial, vertical)), 1.0e-8)
        horiz = radial * scale / max(radius, 1.0e-6)
        up = vertical * scale * (1.0 if z >= 0.0 else -1.0)
        grad = np.array([horiz * dx, horiz * float(y), up], dtype=float)
    elif radial > vertical:
        horiz = 1.0 / max(radius, 1.0e-6)
        grad = np.array([horiz * dx, horiz * float(y), 0.0], dtype=float)
    else:
        eps = 1.0e-3
        half_next = plate_half_h(min((radius + eps) / RADIUS, 1.0))
        slope = (half_next - half_h) / eps
        scale = -slope / max(radius, 1.0e-6)
        grad = np.array([
            scale * dx,
            scale * float(y),
            1.0 if z >= 0.0 else -1.0,
        ], dtype=float)
    return grad / max(float(np.linalg.norm(grad)), 1.0e-8)


def hold_off(point):
    """Keep the point a few millimetres off the skin so the line stays visible."""
    p = np.array(point, dtype=float)
    for _ in range(8):
        gap = body_distance(p[0], p[1], p[2])
        if gap >= CLEARANCE:
            return p
        p = p + (CLEARANCE - gap + 0.001) * outward(p[0], p[1], p[2])
    return p if body_distance(p[0], p[1], p[2]) >= 0.0 else None


def trace(vlm):
    """Follow the vortex-lattice velocity and ride over the plate instead of through it."""
    pts = seeds()
    n = len(pts)
    steps = 90
    alive = np.ones(n, dtype=bool)
    xyz = np.zeros((n, steps, 3))
    speed = np.zeros((n, steps))
    xyz[:, 0] = pts
    ds = 1.25 / (steps - 1)
    for k in range(1, steps):
        vel = vlm.get_velocity_at_points(pts)
        mag = np.linalg.norm(vel, axis=1)
        speed[:, k - 1] = mag
        nxt = pts + ds * vel / np.clip(mag[:, None], 1e-6, None)
        for i in range(n):
            if not alive[i]:
                nxt[i] = pts[i]
                continue
            direction = vel[i] / max(mag[i], 1e-6)
            if body_distance(pts[i, 0], pts[i, 1], pts[i, 2]) < 0.025:
                normal = outward(pts[i, 0], pts[i, 1], pts[i, 2])
                direction = direction - normal * float(np.dot(direction, normal))
                length = float(np.linalg.norm(direction))
                if length < 1e-4:
                    direction = np.array([1.0, 0.0, 0.0])
                else:
                    direction = direction / length
                nxt[i] = pts[i] + ds * direction
            held = hold_off(nxt[i])
            if held is None or not np.isfinite(held).all():
                alive[i] = False
                nxt[i] = pts[i]
            else:
                nxt[i] = held
        pts = nxt
        xyz[:, k] = pts
    speed[:, -1] = speed[:, -2]
    return xyz, speed


def solve(plane, alpha: float):
    op = asb.OperatingPoint(velocity=SPEED, alpha=alpha)
    forces = asb.AeroBuildup(
        airplane=plane, op_point=op, include_wave_drag=False, model_size="small"
    ).run()
    vlm = asb.VortexLatticeMethod(
        airplane=plane,
        op_point=op,
        spanwise_resolution=14,
        chordwise_resolution=8,
        align_trailing_vortices_with_wind=True,
    )
    vlm.run()
    xyz, speed = trace(vlm)
    cl, cd = scalar(forces["CL"]), max(0.0, scalar(forces["CD"]))
    row = {
        "alpha_deg": alpha,
        "CL": round(cl, 4),
        "CD": round(cd, 5),
        "L_over_D": None if cd <= 1e-6 else round(cl / cd, 2),
        "lift_N": round(scalar(forces["L"]), 1),
        "drag_N": round(scalar(forces["D"]), 2),
    }
    wind = np.asarray(vlm.steady_freestream_velocity, dtype=float)
    return row, xyz, speed, wind


def to_viewer_mm(xyz, speed, center):
    """AeroSandbox meters (+x aft, +y right, +z up) to the centered viewer frame.

    Each point is [x, y, z, speed / freestream].
    """
    lines = []
    for i in range(xyz.shape[0]):
        pts = []
        for k in range(xyz.shape[1]):
            x, y, z = (float(v) for v in xyz[i, k])
            if not np.isfinite([x, y, z]).all():
                break
            if k > 0 and np.allclose(xyz[i, k], xyz[i, k - 1]):
                break
            local = np.array([y * 1000.0, z * 1000.0, x * 1000.0]) - center
            ratio = float(speed[i, k]) / SPEED
            pts.append([round(float(v), 1) for v in local] + [round(ratio, 3)])
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
        row, xyz, speed, wind = solve(plane, float(alpha))
        rows.append(row)
        lines = to_viewer_mm(xyz, speed, center)
        cases.append({"alpha_deg": alpha, "lines": lines})
        print(f"  alpha {alpha:5.0f}  lines {len(lines):2d}  wind {np.round(wind, 2)}")
    payload = {
        "tool": "AeroSandbox AeroBuildup + vortex lattice",
        "repo": "https://github.com/peterdsharpe/AeroSandbox",
        "license": "MIT",
        "speed_m_s": SPEED,
        "wing_area_m2": round(area, 4),
        "note": (
            "The lines follow the vortex-lattice airflow and stay outside the plate. "
            "Color shows speed: pale is faster, blue is slower. "
            "Drag includes induced drag and skin friction. "
            "Not a wind tunnel, and not the SU2 nozzle run. "
            "The vehicle is sized for a 5 second rocket burn. It is not a flown airplane."
        ),
        "points": rows,
    }
    OUT.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    FLOW.write_text(json.dumps({"cases": cases}, separators=(",", ":")), encoding="utf-8")
    print(f"wing area {area:.3f} m^2, speed {SPEED:.0f} m/s")
    print("alpha   CL      CD    L/D    lift N   drag N")
    for row in rows:
        print(
            f"{row['alpha_deg']:5.0f}  {row['CL']:7.3f}  {row['CD']:7.4f}  "
            f"{row['L_over_D']!s:>6}  {row['lift_N']:7.1f}  {row['drag_N']:7.1f}"
        )
    print("wrote", OUT)
    print("wrote", FLOW, "bytes", FLOW.stat().st_size)


if __name__ == "__main__":
    main()

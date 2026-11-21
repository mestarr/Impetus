"""Aerodynamic estimate for the teardrop lifting body.

Uses AeroSandbox (MIT), https://github.com/peterdsharpe/AeroSandbox
Forces include induced drag and airfoil skin friction (AeroBuildup).
Streamlines come from the vortex lattice. They stay outside the body,
cross the duct only at the opening, and pass around the nozzle.
The fan and the engine are not in the force estimate. It is not SU2,
and it is not a wind tunnel.

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
# From the rocket command: nose -784 mm, tail 120 mm, span 1426 mm.
NOSE_X = -0.784
BASE_X = 0.120
HALF_W = 0.713
FUS = 0.233
HOLE_X = -0.368
HOLE_R = 0.115
HUB_H = 0.078
FIN_X = -0.115
FIN_CHORD = 0.168
FIN_ROOT_Z = 0.033
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
    root = asb.Airfoil(name="naca0018", coordinates=naca_loop("0018"))
    tip = asb.Airfoil(name="naca0012", coordinates=naca_loop("0012"))
    fin_foil = asb.Airfoil(name="naca0009", coordinates=naca_loop("0009"))
    xsecs = []
    for frac, foil in ((0.0, root), (0.22, root), (0.55, tip), (0.82, tip), (0.98, tip)):
        y = frac * HALF_W
        le, te = plan_edges(y)
        rise = 0.10 * smooth(max(0.0, (frac - 0.78) / 0.22))
        xsecs.append(asb.WingXSec(
            xyz_le=[le, y, rise],
            chord=max(te - le, 0.05),
            airfoil=foil,
        ))
    wing = asb.Wing(name="LiftingBody", symmetric=True, xsecs=xsecs)
    cant = np.radians(32.0)
    fins = []
    for sign in (-1.0, 1.0):
        fins.append(asb.Wing(
            name="Fin",
            symmetric=False,
            xsecs=[
                asb.WingXSec(xyz_le=[0.0, 0.0, 0.0], chord=FIN_CHORD, airfoil=fin_foil),
                asb.WingXSec(
                    xyz_le=[0.02, sign * np.sin(cant) * 0.15, np.cos(cant) * 0.15],
                    chord=0.08,
                    airfoil=fin_foil,
                ),
            ],
        ).translate([FIN_X, sign * 0.06, FIN_ROOT_Z]))
    return asb.Airplane(
        name="IMP-1K-A",
        xyz_ref=[0.5 * (NOSE_X + BASE_X), 0.0, 0.0],
        wings=[wing, *fins],
    )


def view_center_mm():
    """Same center three.js uses when it recenters rocket-view.stl."""
    with STL.open("rb") as f:
        f.seek(80)
        n = int(np.frombuffer(f.read(4), dtype="<u4")[0])
    dt = np.dtype([("n", "<f4", (3,)), ("v", "<f4", (3, 3)), ("a", "<u2")])
    rec = np.memmap(STL, dtype=dt, mode="r", offset=84, shape=(n,))
    verts = np.asarray(rec["v"]).reshape(-1, 3)
    return (verts.min(0) + verts.max(0)) * 0.5


CLEARANCE = 0.014


def seeds():
    """Lines over the body, plus lines that pass above and below the whole wing.

    Outboard lines at fuselage height are left out. From the side they stack
    on the body and look as if they pass through it.
    """
    center = [
        [NOSE_X - 0.28, y, z]
        for z in np.linspace(-0.12, 0.22, 9)
        for y in np.linspace(-0.24, 0.24, 7)
    ]
    wide = [
        [NOSE_X - 0.28, y, z]
        for z in (-0.12, 0.16, 0.22)
        for y in np.linspace(-0.55, 0.55, 11)
        if abs(y) > 0.24
    ]
    near = [
        [NOSE_X - 0.18, y, z]
        for z in (-0.03, 0.04)
        for y in np.linspace(-0.22, 0.22, 9)
    ]
    return np.array(center + wide + near, dtype=float)


def smooth(t):
    t = float(np.clip(t, 0.0, 1.0))
    return t * t * (3.0 - 2.0 * t)


def body_frac(u):
    if u <= 0.0 or u >= 1.0:
        return 0.0
    if u < 0.34:
        return float(np.sqrt(u / 0.34))
    if u < 0.86:
        return 1.0
    return float(((1.0 - u) / 0.14) ** 0.8)


def wing_frac(u):
    if u <= 0.30 or u >= 1.0:
        return 0.0
    if u < 0.72:
        return smooth((u - 0.30) / 0.42)
    if u < 0.90:
        return 1.0
    return float(((1.0 - u) / 0.10) ** 0.65)


def thick_frac(u):
    if u <= 0.0 or u >= 1.0:
        return 0.0
    if u < 0.24:
        return float(np.sqrt(u / 0.24))
    if u < 0.78:
        return 1.0
    return float(((1.0 - u) / 0.22) ** 1.05)


def fin_bump(x, y):
    """Extra height over the canted fins. The centerline is left alone."""
    ay = abs(float(y))
    if ay < 0.055 or ay > 0.165:
        return 0.0
    span = smooth((ay - 0.055) / 0.025) * smooth((0.165 - ay) / 0.025)
    x = float(x)
    x0 = FIN_X - 0.18
    x1 = FIN_X - 0.02
    x2 = FIN_X + 0.17
    x3 = x2 + 0.06
    if x <= x0 or x >= x3:
        return 0.0
    if x < x1:
        s = smooth((x - x0) / (x1 - x0))
    elif x > x2:
        s = smooth((x3 - x) / (x3 - x2))
    else:
        s = 1.0
    return 0.09 * s * span


def half_width(x):
    if x <= NOSE_X or x >= BASE_X:
        return 0.0
    u = (x - NOSE_X) / (BASE_X - NOSE_X)
    return FUS * body_frac(u) + (HALF_W - FUS) * wing_frac(u) + 0.012


def plan_edges(y):
    xs = np.linspace(NOSE_X + 0.002, BASE_X - 0.002, 90)
    inside = [float(x) for x in xs if half_width(x) >= abs(y)]
    if not inside:
        return BASE_X - 0.08, BASE_X - 0.02
    return inside[0], inside[-1]


def surface_z(x, y, side, fin=True):
    """Skin height. None means open air, including the duct."""
    if np.hypot(y, x - HOLE_X) < HOLE_R - 0.006:
        return None
    width = half_width(x)
    if width <= 0.0 or abs(y) >= width:
        return None
    u = (x - NOSE_X) / (BASE_X - NOSE_X)
    inset = width - abs(y)
    to_wing = smooth((abs(y) - FUS - 0.008) / 0.32)
    section = HUB_H + (0.017 - HUB_H) * to_wing
    to_tip = smooth((abs(y) - HALF_W * 0.72) / (HALF_W * 0.28))
    section = section + (0.008 - section) * to_tip
    edge = smooth(min(1.0, inset / 0.046))
    height = section * thick_frac(u) * (0.12 + 0.88 * edge)
    bend = smooth((abs(y) - HALF_W * 0.78) / (HALF_W * 0.22))
    camber = 0.105 * bend
    if side > 0.0:
        if fin:
            height += fin_bump(x, y)
        length = BASE_X - NOSE_X
        cz = NOSE_X + 0.18 * length
        q = float(np.hypot(y / 0.062, (x - cz) / 0.108))
        if q < 1.15:
            crown = HUB_H * 0.92 + 0.018 * np.sqrt(max(0.0, 1.0 - min(q, 0.999) ** 2))
            edge = smooth(float(np.clip((1.15 - q) / 0.40, 0.0, 1.0)))
            target = max(height, crown - camber)
            height = height + (target - height) * edge
    z = camber + side * (height + CLEARANCE)
    if side < 0.0:
        flap = flap_bottom(x, y)
        if flap is not None:
            z = min(z, flap)
    return z


def flap_bottom(x, y):
    """Lower edge of a flap, or None when this station has no flap."""
    if abs(y) < 0.06 or abs(y) > 0.28 or x < 0.02 or x > 0.10:
        return None
    t = float(np.clip((x - 0.030) / 0.055, 0.0, 1.0))
    return -0.036 - 0.008 * t


def in_opening(point):
    """True when the point is in the duct mouth, where there is no skin."""
    return np.hypot(point[1], point[0] - HOLE_X) < HOLE_R - 0.008


def clear_fan(point, side):
    """Ease a line off the fan disk. A hard jump reads as a kink."""
    if abs(point[2]) < 0.022:
        target = 0.026 if side > 0.0 else -0.026
        point[2] += float(np.clip(target - point[2], -0.008, 0.008))
    return point


def leg_segments():
    """Oleo, lower strut, and brace for the three gear legs. Meters, aero axes."""
    specs = (
        (np.array([-0.558, 0.0, -0.074]), np.array([-0.598, 0.0, -0.229]), np.array([-0.478, 0.0, -0.074])),
        (np.array([-0.298, -0.150, -0.074]), np.array([-0.348, -0.230, -0.229]), np.array([-0.238, -0.090, -0.074])),
        (np.array([-0.298, 0.150, -0.074]), np.array([-0.348, 0.230, -0.229]), np.array([-0.238, 0.090, -0.074])),
    )
    segs = []
    for top, foot, brace in specs:
        knee = top + 0.42 * (foot - top)
        segs.append((top, knee, 0.016))
        segs.append((knee, foot, 0.012))
        segs.append((knee, brace, 0.010))
    return segs


LEGS = leg_segments()
MAX_TURN = np.radians(9.0)


def clear_fins(point):
    """Lift a point that has entered a canted fin up over that fin."""
    p = np.array(point, dtype=float)
    if p[0] < -0.15 or p[0] > 0.0:
        return p
    ay = abs(float(p[1]))
    if ay < 0.045 or ay > 0.16:
        return p
    span = float(np.clip((ay - 0.050) / 0.090, 0.0, 1.0))
    top = 0.045 + 0.135 * span
    if top - 0.028 <= p[2] <= top + 0.006:
        p[2] = top + 0.012
    return p


def _avoid_legs(point):
    p = np.array(point, dtype=float)
    for a, b, radius in LEGS:
        ab = b - a
        t = float(np.clip(np.dot(p - a, ab) / np.dot(ab, ab), 0.0, 1.0))
        closest = a + t * ab
        radial = p - closest
        dist = float(np.linalg.norm(radial))
        if dist >= radius:
            continue
        # On the centerline the offset runs along the strut. Step out to the side.
        if abs(radial[1]) < 0.004:
            radial = np.array([0.0, 1.0 if p[1] >= 0.0 else -1.0, min(0.0, radial[2])])
        p = closest + radial / max(float(np.linalg.norm(radial)), 1e-6) * radius
    return p


def spread_legs(points):
    """Share a leg deflection with the neighboring samples so the line curves around."""
    pts = np.array(points, dtype=float)
    delta = np.zeros_like(pts)
    for k, p in enumerate(pts):
        if p[2] < -0.04 and -0.70 < p[0] < -0.10:
            delta[k] = _avoid_legs(p) - p
    spread = delta.copy()
    for k in range(len(pts)):
        if float(np.linalg.norm(delta[k])) < 1e-4:
            continue
        for j, w in ((-4, 0.15), (-3, 0.35), (-2, 0.55), (-1, 0.8), (1, 0.8), (2, 0.55), (3, 0.35), (4, 0.15)):
            i = k + j
            if 0 <= i < len(pts):
                spread[i] = spread[i] + delta[k] * w
    return pts + spread


def avoid_solid(point):
    """Push a sample out of the fins, the gear, the flaps, and the nozzle bell."""
    p = clear_fins(point)
    return _avoid_engine_and_flaps(p)


def _avoid_engine_and_flaps(p):
    if 0.125 < p[0] < 0.205:
        rad = float(np.hypot(p[1], p[2]))
        if rad < 0.030:
            if rad < 1e-5:
                p[2] += 0.006 if p[2] >= 0.0 else -0.006
            else:
                scale = min(0.030 / rad, 1.0 + 0.006 / rad)
                p[1] *= scale
                p[2] *= scale
    flap = flap_bottom(p[0], p[1])
    if flap is not None and flap < p[2] < -0.004:
        p[2] = flap
    return p


def sit(point, nxt, side):
    """Move toward the skin, but no steeper than the real surface."""
    z = surface_z(nxt[0], nxt[1], side)
    if z is None:
        return nxt
    inside = (side > 0.0 and point[2] < z - 0.002) or (side < 0.0 and point[2] > z + 0.002)
    if inside:
        nxt[2] = z
        return nxt
    dz = z - point[2]
    horiz = max(float(np.hypot(nxt[0] - point[0], nxt[1] - point[1])), 1e-4)
    limit = horiz * 0.7
    nxt[2] = point[2] + float(np.clip(dz, -limit, limit))
    return nxt


def steer(vel, prev):
    """Keep one step from turning more than MAX_TURN. Vortex lattice spikes kink otherwise."""
    v = np.asarray(vel, dtype=float)
    mag = float(np.linalg.norm(v))
    if mag < 1e-6 or not np.isfinite(v).all():
        v = np.array([SPEED, 0.0, 0.0])
        mag = SPEED
    direction = v / mag
    previous = prev / max(float(np.linalg.norm(prev)), 1e-6)
    ang = float(np.arccos(np.clip(np.dot(previous, direction), -1.0, 1.0)))
    if ang <= MAX_TURN or ang < 1e-4:
        return direction * mag
    axis = np.cross(previous, direction)
    length = float(np.linalg.norm(axis))
    if length < 1e-6:
        return previous * mag
    axis /= length
    turned = (
        previous * np.cos(MAX_TURN)
        + np.cross(axis, previous) * np.sin(MAX_TURN)
        + axis * np.dot(axis, previous) * (1.0 - np.cos(MAX_TURN))
    )
    turned /= max(float(np.linalg.norm(turned)), 1e-6)
    return turned * mag


def slide(point, vel, side, ds, limit):
    """Follow the airflow along the skin instead of stepping into it."""
    h = 0.012
    zx = surface_z(point[0] + h, point[1], side)
    zy = surface_z(point[0], point[1] + h, side)
    dzdx = 0.0 if zx is None else (zx - limit) / h
    dzdy = 0.0 if zy is None else (zy - limit) / h
    normal = np.array([-dzdx, -dzdy, 1.0])
    normal /= np.linalg.norm(normal)
    tang = vel - np.dot(vel, normal) * normal
    mag = float(np.linalg.norm(tang))
    if mag < 1e-3:
        tang = np.array([SPEED, 0.0, 0.0])
        mag = SPEED
    nxt = point + ds * tang / mag
    if in_opening(nxt):
        return clear_fan(nxt, side)
    return sit(point, nxt, side)


def advance(point, vel, side, ds):
    v = np.asarray(vel, dtype=float)
    mag = float(np.linalg.norm(v))
    if not np.isfinite(v).all() or mag < 1e-6:
        v = np.array([SPEED, 0.0, 0.0])
        mag = SPEED
    if in_opening(point):
        nxt = clear_fan(point + ds * v / mag, side)
        return avoid_solid(nxt)
    limit = surface_z(point[0], point[1], side)
    on_skin = limit is not None and (
        (side > 0.0 and point[2] <= limit + 0.006) or (side < 0.0 and point[2] >= limit - 0.006)
    )
    if on_skin:
        return avoid_solid(slide(point, v, side, ds, limit))
    nxt = point + ds * v / mag
    if in_opening(nxt):
        return avoid_solid(clear_fan(nxt, side))
    cap = surface_z(nxt[0], nxt[1], side)
    penetrated = cap is not None and (
        (side > 0.0 and nxt[2] < cap) or (side < 0.0 and nxt[2] > cap)
    )
    if penetrated:
        nxt = sit(point, nxt, side)
    return avoid_solid(nxt)


def park(point, side, fin=True):
    """Put a sample on the outside of the skin, then clear the parts that stick out."""
    p = np.array(point, dtype=float)
    if in_opening(p):
        return avoid_solid(clear_fan(p, side))
    z = surface_z(p[0], p[1], side, fin=fin)
    if z is not None:
        if side > 0.0 and p[2] < z:
            p[2] = z
        elif side < 0.0 and p[2] > z:
            p[2] = z
    return avoid_solid(p)


def _fillet(out):
    nxt = out.copy()
    n, steps, _ = out.shape
    for i in range(n):
        for k in range(1, steps - 1):
            a, b, c = out[i, k - 1], out[i, k], out[i, k + 1]
            u, v = b - a, c - b
            nu, nv = float(np.linalg.norm(u)), float(np.linalg.norm(v))
            if nu < 1e-4 or nv < 1e-4:
                continue
            ang = float(np.arccos(np.clip(np.dot(u, v) / (nu * nv), -1.0, 1.0)))
            if ang < np.radians(16.0):
                continue
            nxt[i, k] = 0.5 * b + 0.25 * a + 0.25 * c
    return nxt


def smooth_paths(xyz, sides):
    """Round a hard corner, then move the point back outside the metal."""
    out = np.array(xyz, dtype=float)
    for _ in range(5):
        out = _fillet(out)
    out = _place(out, sides, fin=False)
    for _ in range(4):
        out = _fillet(out)
    return _place(out, sides, fin=False)


def _place(xyz, sides, fin=True):
    out = np.array(xyz, dtype=float)
    for i in range(out.shape[0]):
        for k in range(out.shape[1]):
            out[i, k] = park(out[i, k], sides[i], fin=fin)
    return out


def trace(vlm):
    """Follow the vortex-lattice velocity. Lines slide on the skin, then leave into the wake."""
    pts = seeds()
    sides = np.where(pts[:, 2] >= 0.0, 1.0, -1.0)
    n = len(pts)
    steps = 150
    xyz = np.zeros((n, steps, 3))
    speed = np.zeros((n, steps))
    xyz[:, 0] = pts
    heading = np.tile(np.array([1.0, 0.0, 0.0], dtype=float), (n, 1))
    ds = 1.65 / (steps - 1)
    for k in range(1, steps):
        vel = vlm.get_velocity_at_points(pts)
        mag = np.linalg.norm(vel, axis=1)
        speed[:, k - 1] = mag
        nxt = np.empty_like(pts)
        for i in range(n):
            guided = steer(vel[i], heading[i])
            nxt[i] = advance(pts[i], guided, sides[i], ds)
            step = nxt[i] - pts[i]
            step_mag = float(np.linalg.norm(step))
            if step_mag > 1e-6:
                heading[i] = step / step_mag
        pts = nxt
        xyz[:, k] = pts
    speed[:, -1] = speed[:, -2]
    return smooth_paths(xyz, sides), speed


def solve(plane, alpha: float):
    op = asb.OperatingPoint(velocity=SPEED, alpha=alpha)
    forces = asb.AeroBuildup(
        airplane=plane, op_point=op, include_wave_drag=False, model_size="small"
    ).run()
    vlm = asb.VortexLatticeMethod(
        airplane=plane,
        op_point=op,
        spanwise_resolution=20,
        chordwise_resolution=12,
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
            "The lines follow the vortex-lattice airflow over the wing and along the skin, then leave into the wake. "
            "They cross the duct only when the path reaches the opening, stay off the fan, and pass around the nozzle. "
            "Color shows speed: pale is faster, blue is slower. "
            "Drag includes induced drag and skin friction of the body. The fan and the engine are not in this estimate. "
            "Not a wind tunnel, and not the SU2 nozzle run. "
            "The tanks hold one engine for 5 seconds. It is not a flown vehicle."
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

"""Render body, injector, and flange from the PERF-KERO-A 3MF."""
import sys
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

import render_stills as rs

THREE_MF = rs.ROOT / "designs" / "PERF-KERO-A" / "engine.3mf"
OUT = rs.OUT


def object_slices(blob: bytes):
    marks = []
    key = b'<object id="'
    i = 0
    while True:
        i = blob.find(key, i)
        if i < 0:
            break
        name_at = blob.find(b'name="', i)
        name_end = blob.find(b'"', name_at + 6)
        name = blob[name_at + 6 : name_end].decode()
        marks.append((name, i))
        i = name_end
    parts = {}
    for n, (name, start) in enumerate(marks):
        end = marks[n + 1][1] if n + 1 < len(marks) else len(blob)
        parts[name] = blob[start:end]
    return parts


def vertices(blob: bytes, stride: int) -> np.ndarray:
    xs, ys, zs = [], [], []
    key = b'<vertex x="'
    i = 0
    n = 0
    while True:
        i = blob.find(key, i)
        if i < 0:
            break
        if n % stride == 0:
            i += len(key)
            x_end = blob.find(b'"', i)
            y_at = blob.find(b'y="', x_end) + 3
            y_end = blob.find(b'"', y_at)
            z_at = blob.find(b'z="', y_end) + 3
            z_end = blob.find(b'"', z_at)
            xs.append(float(blob[i:x_end]))
            ys.append(float(blob[y_at:y_end]))
            zs.append(float(blob[z_at:z_end]))
            i = z_end
        else:
            i += len(key)
        n += 1
    print(f"  vertices kept {len(xs)} of {n}")
    return np.column_stack([xs, ys, zs]).astype(np.float32)


def radial_normals(pts: np.ndarray) -> np.ndarray:
    nrm = np.zeros_like(pts)
    radial = np.linalg.norm(pts[:, :2], axis=1)
    side = radial > np.percentile(radial, 12)
    nrm[side, 0] = pts[side, 0]
    nrm[side, 1] = pts[side, 1]
    nrm[side] /= np.linalg.norm(nrm[side], axis=1, keepdims=True)
    cap = ~side
    nrm[cap, 2] = np.where(pts[cap, 2] >= np.median(pts[:, 2]), 1.0, -1.0)
    return nrm


def render_part(pts, nrm, yaw, pitch, size, raw_path: Path):
    pts, nrm = rs.transform(pts, nrm, yaw, pitch)
    img = rs.raster(pts, nrm, size, size, margin=0.16)
    img.save(raw_path)
    return img


def exploded(parts: dict[str, np.ndarray], outfile: Path):
    order = ["flange", "injector", "body"]
    titles = {"flange": "Flange", "injector": "Injector", "body": "Body"}
    shifts = {"flange": -120.0, "injector": -28.0, "body": 55.0}
    width, height, margin = 1600, 900, 0.10
    clouds = []
    for name in order:
        pts = parts[name].copy()
        pts[:, 2] += shifts[name]
        clouds.append(pts)
    all_pts = np.vstack(clouds)
    center = (all_pts.min(0) + all_pts.max(0)) * 0.5
    r = rs.rot_x(-16) @ rs.rot_y(72)
    frames = []
    for name, pts in zip(order, clouds):
        nrm = radial_normals(parts[name])
        moved = (pts - center) @ r.T
        frames.append((name, moved, nrm @ r.T))
    merged_pts = np.vstack([p for _, p, _ in frames])
    merged_nrm = np.vstack([n for _, _, n in frames])
    img = rs.raster(merged_pts, merged_nrm, width, height, margin=margin)
    canvas = rs.backdrop(width, height)
    arr = np.array(img)
    mask = Image.fromarray((arr.max(axis=2) > 18).astype(np.uint8) * 255, mode="L")
    canvas.paste(img, (0, 0), mask)

    x = merged_pts[:, 0]
    y = merged_pts[:, 1]
    span = max(float(x.max() - x.min()), float(y.max() - y.min()))
    scale = (1 - 2 * margin) * min(width, height) / max(span, 1e-6)
    cx = 0.5 * (float(x.min()) + float(x.max()))
    cy = 0.5 * (float(y.min()) + float(y.max()))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.truetype(str(rs.FONT), 36)
    small = ImageFont.truetype(str(rs.FONT_REG), 22)
    placed = []
    for name, moved, _ in frames:
        sx = (float(moved[:, 0].mean()) - cx) * scale + width * 0.5
        py = height * 0.52 - (moved[:, 1] - cy) * scale
        label = titles[name]
        tw = draw.textlength(label, font=font)
        placed.append([sx, float(py.max()) + 16, tw, label])
    placed.sort(key=lambda item: item[0])
    for i in range(1, len(placed)):
        overlap = (placed[i - 1][0] + placed[i - 1][2] / 2 + 40) - (placed[i][0] - placed[i][2] / 2)
        if overlap > 0:
            placed[i][0] += overlap
    right = placed[-1][0] + placed[-1][2] / 2
    if right > width - 40:
        for item in placed:
            item[0] -= right - (width - 40)
    left = placed[0][0] - placed[0][2] / 2
    if left < 40:
        for item in placed:
            item[0] += 40 - left
    for sx, sy, tw, label in placed:
        draw.text((sx - tw / 2, min(sy, height - 52)), label, font=font, fill=(244, 246, 248))
    draw.text((72, 36), "Three solids", font=font, fill=(244, 246, 248))
    draw.text((72, 84), "The 3MF is body, injector, and flange. The STL is the same engine joined.", font=small, fill=(168, 176, 186))
    canvas.save(outfile, quality=95)
    print("wrote", outfile)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    cache = OUT / "parts-points.npz"
    stride = {"body": 3, "injector": 1, "flange": 1}
    yaw = {"body": 72, "injector": 58, "flange": 58}
    pitch = {"body": -16, "injector": -42, "flange": -38}
    titles = {
        "body": ("Body", "Cooled chamber and bell."),
        "injector": ("Injector", "Coaxial elements in the face."),
        "flange": ("Flange", "Bolt circle for the test stand."),
    }
    if cache.exists():
        loaded = np.load(cache)
        points = {name: loaded[name] for name in ("body", "injector", "flange")}
        print("loaded cached points")
    else:
        print("reading", THREE_MF)
        with zipfile.ZipFile(THREE_MF) as zf:
            blob = zf.read("3D/3dmodel.model")
        slices = object_slices(blob)
        points = {}
        for name, section in slices.items():
            print(name)
            points[name] = vertices(section, stride[name])
        np.savez(cache, **points)
    if "exploded" not in sys.argv:
        for name, pts in points.items():
            nrm = radial_normals(pts)
            raw = render_part(pts, nrm, yaw[name], pitch[name], 1200, OUT / f"{name}-raw.png")
            title, sub = titles[name]
            rs.composite(raw, title, sub, OUT / f"04-{name}.png")
    exploded(points, OUT / "05-parts.png")


if __name__ == "__main__":
    main()

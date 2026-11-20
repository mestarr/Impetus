"""Render the rocket stack STL. Not part of the app."""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

import render_stills as rs

FONT = Path(r"C:\Windows\Fonts\segoeuib.ttf")
FONT_REG = Path(r"C:\Windows\Fonts\segoeui.ttf")


def load_surface(path: Path, stride: int):
    with path.open("rb") as f:
        f.seek(80)
        n = int(np.frombuffer(f.read(4), dtype="<u4")[0])
    dt = np.dtype([("n", "<f4", (3,)), ("v", "<f4", (3, 3)), ("a", "<u2")])
    rec = np.memmap(path, dtype=dt, mode="r", offset=84, shape=(n,))
    rec = rec[::stride]
    verts = rec["v"].reshape(-1, 3).astype(np.float32)
    nrm = np.repeat(rec["n"], 3, axis=0).astype(np.float32)
    length = np.linalg.norm(nrm, axis=1, keepdims=True)
    nrm = nrm / np.clip(length, 1e-8, None)
    return verts, nrm


def raster_solid(pts, nrm, width, height, margin=0.08):
    light = np.array([0.25, 0.85, 0.45], dtype=np.float32)
    light /= np.linalg.norm(light)
    shade = np.nan_to_num(np.clip(nrm @ light, 0, 1), nan=0.35)
    shade = 0.28 + 0.72 * shade
    x, y, z = pts[:, 0], pts[:, 1], pts[:, 2]
    span = max(float(np.nanmax(x) - np.nanmin(x)), float(np.nanmax(y) - np.nanmin(y)), 1e-6)
    scale = (1 - 2 * margin) * min(width, height) / span
    cx = 0.5 * (float(np.nanmin(x)) + float(np.nanmax(x)))
    cy = 0.5 * (float(np.nanmin(y)) + float(np.nanmax(y)))
    px = np.rint((x - cx) * scale + width * 0.5).astype(np.int32)
    py = np.rint(height * 0.52 - (y - cy) * scale).astype(np.int32)
    ok = (px >= 1) & (px < width - 1) & (py >= 1) & (py < height - 1) & np.isfinite(z)
    px, py, z, shade = px[ok], py[ok], z[ok], shade[ok]
    key = py.astype(np.int64) * width + px
    order = np.lexsort((-z, key))
    key = key[order]
    first = np.empty(len(key), dtype=bool)
    first[0] = True
    first[1:] = key[1:] != key[:-1]
    win = order[first]
    lum = np.zeros((height, width), dtype=np.float32)
    lum[py[win], px[win]] = shade[win]
    rgb = np.clip(lum, 0, 1)[..., None] * rs.METAL
    img = Image.fromarray(rgb.astype(np.uint8), mode="RGB")
    return img.filter(ImageFilter.GaussianBlur(radius=0.4))


def render(pts, nrm, yaw: float, pitch: float, size, outfile: Path, title: str, subtitle: str):
    pts, nrm = rs.transform(pts, nrm, yaw, pitch)
    width, height = size
    img = raster_solid(pts, nrm, width, height)
    canvas = rs.backdrop(width, height)
    engine, alpha = rs.crop_content(img, pad=12)
    top, caption = 24, height - 108
    scale = min(0.94 * width / engine.width, 0.94 * (caption - top) / engine.height)
    sized = (max(1, int(engine.width * scale)), max(1, int(engine.height * scale)))
    engine = engine.resize(sized, Image.Resampling.LANCZOS)
    alpha = alpha.resize(sized, Image.Resampling.LANCZOS)
    x = (width - engine.width) // 2
    y = top + max(0, (caption - top - engine.height) // 2)
    canvas.paste(engine, (x, y), alpha)
    draw = ImageDraw.Draw(canvas)
    draw.text((48, height - 92), title, font=ImageFont.truetype(str(FONT), 36), fill=(244, 246, 248))
    draw.text((48, height - 46), subtitle, font=ImageFont.truetype(str(FONT_REG), 22), fill=(168, 176, 186))
    canvas.save(outfile, quality=95)
    print("wrote", outfile, "points", len(pts))


def main():
    root = Path(r"C:\Users\stjep\Desktop\Projekti\Impetus")
    out = root / "designs" / "IMP-1K-A"
    pictures = root / "media"
    pts, nrm = load_surface(out / "rocket.stl", stride=2)
    views = [
        (58, -32, (1500, 900), "rocket.png", "Round plate", "Thick in the middle, thin at the rim. Engine at the back."),
        (88, -12, (1500, 700), "rocket-aft.png", "From the side", "The plate is much wider than it is thick."),
        (0, 90, (1500, 900), "rocket-top.png", "From above", "A round plate. Nose at the top."),
        (180, 0, (1200, 800), "rocket-front.png", "From the front", "Wing and tail against the nose."),
        (0, 0, (1200, 800), "rocket-rear.png", "From the nozzle", "The rocket engine is the tail."),
        (128, -24, (1500, 900), "rocket-quarter.png", "Other side", "The same vehicle, from the other rear quarter."),
    ]
    for yaw, pitch, size, name, title, subtitle in views:
        render(pts, nrm, yaw, pitch, size, pictures / name, title, subtitle)


if __name__ == "__main__":
    sys.exit(main())

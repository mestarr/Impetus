"""Render Impetus STL geometry into LinkedIn stills. Not part of the app."""
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(r"C:\Users\stjep\Desktop\Projekti\Impetus")
OUT = ROOT / "media" / "linkedin"
ENGINE = ROOT / "designs" / "PERF-KERO-A" / "engine.stl"
CUTAWAY = ROOT / "designs" / "PERF-KERO-A" / "engine_cutaway.stl"

BG = (14, 16, 20)
METAL = np.array([226, 230, 236], dtype=np.float32)
FONT = Path(r"C:\Windows\Fonts\segoeuib.ttf")
FONT_REG = Path(r"C:\Windows\Fonts\segoeui.ttf")


def load_points(path: Path, stride: int):
    with path.open("rb") as f:
        f.seek(80)
        n = int(np.frombuffer(f.read(4), dtype="<u4")[0])
    dt = np.dtype([("n", "<f4", (3,)), ("v", "<f4", (3, 3)), ("a", "<u2")])
    rec = np.memmap(path, dtype=dt, mode="r", offset=84, shape=(n,))
    rec = rec[::stride]
    pts = rec["v"].mean(axis=1).astype(np.float32)
    nrm = rec["n"].astype(np.float32)
    length = np.linalg.norm(nrm, axis=1, keepdims=True)
    nrm = nrm / np.clip(length, 1e-8, None)
    return pts, nrm


def rot_y(deg):
    a = np.deg2rad(deg)
    c, s = np.cos(a), np.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]], dtype=np.float32)


def rot_x(deg):
    a = np.deg2rad(deg)
    c, s = np.cos(a), np.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]], dtype=np.float32)


def transform(pts, nrm, yaw, pitch):
    r = rot_x(pitch) @ rot_y(yaw)
    center = (pts.min(0) + pts.max(0)) * 0.5
    p = (pts - center) @ r.T
    n = nrm @ r.T
    return p, n


def raster(pts, nrm, width, height, margin=0.12):
    light = np.array([0.25, 0.85, 0.45], dtype=np.float32)
    light /= np.linalg.norm(light)
    fill = np.array([-0.4, 0.2, -0.3], dtype=np.float32)
    fill /= np.linalg.norm(fill)
    shade = np.clip(nrm @ light, 0, 1)
    shade = 0.22 + 0.78 * shade + 0.12 * np.clip(nrm @ fill, 0, 1)
    shade = np.clip(shade, 0, 1.15)

    x, y, z = pts[:, 0], pts[:, 1], pts[:, 2]
    span = max(x.max() - x.min(), y.max() - y.min())
    scale = (1 - 2 * margin) * min(width, height) / max(span, 1e-6)
    cx = 0.5 * (x.min() + x.max())
    cy = 0.5 * (y.min() + y.max())
    px = np.rint((x - cx) * scale + width * 0.5).astype(np.int32)
    py = np.rint(height * 0.52 - (y - cy) * scale).astype(np.int32)
    ok = (px >= 1) & (px < width - 1) & (py >= 1) & (py < height - 1)
    px, py, z, shade = px[ok], py[ok], z[ok], shade[ok]
    order = np.argsort(z)  # far first
    px, py, shade = px[order], py[order], shade[order]

    lum = np.zeros((height, width), dtype=np.float32)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            w = 1.0 if dx == 0 and dy == 0 else 0.55
            np.maximum.at(lum, (py + dy, px + dx), shade * w)

    lum = np.clip(lum, 0, 1)
    rgb = lum[..., None] * METAL
    img = Image.fromarray(rgb.astype(np.uint8), mode="RGB")
    return img.filter(ImageFilter.GaussianBlur(radius=0.6))


def backdrop(w, h):
    y = np.linspace(0, 1, h, dtype=np.float32)[:, None]
    x = np.linspace(0, 1, w, dtype=np.float32)[None, :]
    g = 18 + 10 * (1 - y) + 6 * x
    rgb = np.dstack([g * 0.9, g * 0.95, g])
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), mode="RGB")


def crop_content(engine: Image.Image, pad=24):
    arr = np.array(engine)
    mask = arr.max(axis=2) > 18
    ys, xs = np.where(mask)
    box = (
        max(int(xs.min()) - pad, 0),
        max(int(ys.min()) - pad, 0),
        min(int(xs.max()) + pad, engine.width),
        min(int(ys.max()) + pad, engine.height),
    )
    cropped = engine.crop(box)
    alpha = Image.fromarray((np.array(cropped).max(axis=2) > 18).astype(np.uint8) * 255, mode="L")
    return cropped, alpha


def composite(engine: Image.Image, title: str, subtitle: str, outfile: Path):
    canvas = backdrop(1200, 1200)
    engine, alpha = crop_content(engine)
    scale = 1000 / max(engine.width, engine.height)
    size = (int(engine.width * scale), int(engine.height * scale))
    engine = engine.resize(size, Image.Resampling.LANCZOS)
    alpha = alpha.resize(size, Image.Resampling.LANCZOS)
    canvas.paste(engine, ((1200 - engine.width) // 2, 80), alpha)
    draw = ImageDraw.Draw(canvas)
    title_font = ImageFont.truetype(str(FONT), 60)
    sub_font = ImageFont.truetype(str(FONT_REG), 30)
    draw.text((72, 1028), title, font=title_font, fill=(244, 246, 248))
    draw.text((72, 1104), subtitle, font=sub_font, fill=(168, 176, 186))
    canvas.save(outfile, quality=95)
    print("wrote", outfile)


def loop_card(engine: Image.Image, outfile: Path):
    canvas = backdrop(1200, 1200)
    engine, alpha = crop_content(engine)
    scale = 520 / engine.width
    size = (int(engine.width * scale), int(engine.height * scale))
    engine = engine.resize(size, Image.Resampling.LANCZOS)
    alpha = alpha.resize(size, Image.Resampling.LANCZOS)
    canvas.paste(engine, ((1200 - engine.width) // 2, 70), alpha)
    draw = ImageDraw.Draw(canvas)
    title = ImageFont.truetype(str(FONT), 54)
    body = ImageFont.truetype(str(FONT_REG), 22)
    label = ImageFont.truetype(str(FONT), 26)
    draw.text((72, 640), "Not drawn. Computed.", font=title, fill=(244, 246, 248))
    steps = [
        ("1", "Spec", "Thrust, pressure,\npropellants"),
        ("2", "Size", "Bell, mass flow,\nheat flux"),
        ("3", "Geometry", "Channels, injector,\nflange"),
        ("4", "Virtual test", "RANS check,\nthen iterate"),
    ]
    x0 = 72
    for i, (n, name, line) in enumerate(steps):
        x = x0 + i * 280
        draw.rounded_rectangle((x, 760, x + 250, 1040), radius=18, fill=(28, 32, 38))
        draw.text((x + 22, 788), n, font=title, fill=(120, 168, 210))
        draw.text((x + 22, 870), name, font=label, fill=(244, 246, 248))
        draw.multiline_text((x + 22, 920), line, font=body, fill=(168, 176, 186), spacing=6)
    canvas.save(outfile, quality=95)
    print("wrote", outfile)


def render_mesh(path, yaw, pitch, stride, size, outfile_raw: Path):
    pts, nrm = load_points(path, stride)
    pts, nrm = transform(pts, nrm, yaw, pitch)
    img = raster(pts, nrm, size, size)
    img.save(outfile_raw)
    print("wrote", outfile_raw, "points", len(pts))
    return img


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    raw_engine = OUT / "engine-raw.png"
    raw_cut = OUT / "cutaway-raw.png"
    if raw_engine.exists() and raw_cut.exists():
        engine = Image.open(raw_engine).convert("RGB")
        cut = Image.open(raw_cut).convert("RGB")
    else:
        engine = render_mesh(ENGINE, yaw=72, pitch=-16, stride=4, size=1400, outfile_raw=raw_engine)
        cut = render_mesh(CUTAWAY, yaw=108, pitch=-28, stride=3, size=1400, outfile_raw=raw_cut)
    composite(engine, "Impetus", "A thruster computed from a specification.", OUT / "01-engine.png")
    composite(cut, "Look inside", "Cooling channels in the wall. Not a sketch.", OUT / "02-cutaway.png")
    loop_card(engine, OUT / "03-loop.png")


if __name__ == "__main__":
    main()

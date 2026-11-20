"""Make a lighter copy of designs/IMP-1K-A/rocket.stl for the browser viewer.

The print mesh is about 80 MB. This keeps the same shape and drops it to a
size the browser can turn. Run from this folder:

    py -3 simplify_mesh.py
"""
from pathlib import Path

import fast_simplification
import numpy as np

ROOT = Path(r"C:\Users\stjep\Desktop\Projekti\Impetus")
SRC = ROOT / "designs" / "IMP-1K-A" / "rocket.stl"
OUT = Path(__file__).with_name("rocket-view.stl")
TARGET = 45000


def load_triangles(path: Path):
    with path.open("rb") as f:
        f.seek(80)
        n = int(np.frombuffer(f.read(4), dtype="<u4")[0])
    dt = np.dtype([("n", "<f4", (3,)), ("v", "<f4", (3, 3)), ("a", "<u2")])
    rec = np.memmap(path, dtype=dt, mode="r", offset=84, shape=(n,))
    return np.asarray(rec["v"]).reshape(-1, 3)


def weld(verts: np.ndarray, step_mm: float):
    key = np.round(verts / step_mm).astype(np.int32)
    _, index, inverse = np.unique(key, axis=0, return_index=True, return_inverse=True)
    points = verts[index]
    faces = inverse.reshape(-1, 3)
    keep = (faces[:, 0] != faces[:, 1]) & (faces[:, 1] != faces[:, 2]) & (faces[:, 0] != faces[:, 2])
    return points.astype(np.float32), np.ascontiguousarray(faces[keep], dtype=np.int32)


def write_stl(path: Path, points: np.ndarray, faces: np.ndarray):
    a = points[faces[:, 0]]
    b = points[faces[:, 1]]
    c = points[faces[:, 2]]
    normal = np.cross(b - a, c - a).astype(np.float32)
    length = np.linalg.norm(normal, axis=1, keepdims=True)
    normal /= np.clip(length, 1e-8, None)
    header = b"Impetus vehicle, decimated for the viewer".ljust(80, b"\0")
    with path.open("wb") as f:
        f.write(header[:80])
        f.write(np.uint32(len(faces)).tobytes())
        for nrm, tri in zip(normal, faces):
            f.write(nrm.tobytes())
            f.write(points[tri].astype("<f4").tobytes())
            f.write(np.uint16(0).tobytes())


def main():
    print("reading", SRC)
    verts = load_triangles(SRC)
    print(f"triangles {len(verts) // 3}")
    points, faces = weld(verts, 1.0)
    print(f"welded points {len(points)} faces {len(faces)}")
    points, faces = fast_simplification.simplify(points, faces, target_count=TARGET)
    print(f"simplified points {len(points)} faces {len(faces)}")
    write_stl(OUT, points, faces)
    print("wrote", OUT, "bytes", OUT.stat().st_size)


if __name__ == "__main__":
    main()

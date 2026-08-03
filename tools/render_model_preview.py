#!/usr/bin/env python3
"""True-3D previewer for .mer.json primitive models and shallow schematics.

Perspective camera + painter's algorithm + Lambert shading (PIL only).
Successor to render_mer_preview.py (similar CLI style). Also importable as an
engine by render_heli_jump_anim.py:
    load_model(), render(), rot_matrix(), block_faces(), parse_color()
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

SUPERSAMPLE = 2
NEAR = 0.06
KEY_LIGHT = (-0.45, 0.80, 0.55)   # from up-forward-left
FILL_LIGHT = (0.50, 0.60, -0.62)  # from up-back-right
AMBIENT = 0.37
BOUNCE = 0.16  # upward floor-bounce so interior ceilings are not pure black


def norm(v):
    l = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]) or 1.0
    return (v[0] / l, v[1] / l, v[2] / l)


KEY_LIGHT = norm(KEY_LIGHT)
FILL_LIGHT = norm(FILL_LIGHT)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def parse_color(s: str):
    s = s.lstrip("#")
    r = int(s[0:2], 16)
    g = int(s[2:4], 16)
    b = int(s[4:6], 16)
    a = int(s[6:8], 16) if len(s) >= 8 else 255
    return (r, g, b, a)


def rot_matrix(rx, ry, rz):
    """Unity euler order: R = Ry(y) @ Rx(x) @ Rz(z), right-handed matrices."""
    ax, ay, az = math.radians(rx), math.radians(ry), math.radians(rz)
    cx, sx = math.cos(ax), math.sin(ax)
    cy, sy = math.cos(ay), math.sin(ay)
    cz, sz = math.cos(az), math.sin(az)
    # Rz then Rx then Ry applied to column vectors
    m00 = cy * cz + sy * sx * sz
    m01 = -cy * sz + sy * sx * cz
    m02 = sy * cx
    m10 = cx * sz
    m11 = cx * cz
    m12 = -sx
    m20 = -sy * cz + cy * sx * sz
    m21 = sy * sz + cy * sx * cz
    m22 = cy * cx
    return ((m00, m01, m02), (m10, m11, m12), (m20, m21, m22))


def mat_vec(m, v):
    return (m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
            m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
            m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2])


def trs_matrix(block):
    """Return a column-vector affine matrix for one block's local T * R * S."""
    rotation = block.get("Rotation", {})
    r = rot_matrix(rotation.get("x", 0.0), rotation.get("y", 0.0), rotation.get("z", 0.0))
    scale = block.get("Scale", {})
    sx, sy, sz = scale.get("x", 1.0), scale.get("y", 1.0), scale.get("z", 1.0)
    position = block.get("Position", {})
    px, py, pz = position.get("x", 0.0), position.get("y", 0.0), position.get("z", 0.0)
    return (
        (r[0][0] * sx, r[0][1] * sy, r[0][2] * sz, px),
        (r[1][0] * sx, r[1][1] * sy, r[1][2] * sz, py),
        (r[2][0] * sx, r[2][1] * sy, r[2][2] * sz, pz),
        (0.0, 0.0, 0.0, 1.0),
    )


def mat4_mul(a, b):
    return tuple(tuple(sum(a[row][k] * b[k][column] for k in range(4))
                       for column in range(4)) for row in range(4))


def transform_point(matrix, point):
    x, y, z = point
    return (
        matrix[0][0] * x + matrix[0][1] * y + matrix[0][2] * z + matrix[0][3],
        matrix[1][0] * x + matrix[1][1] * y + matrix[1][2] * z + matrix[1][3],
        matrix[2][0] * x + matrix[2][1] * y + matrix[2][2] * z + matrix[2][3],
    )


# --- tessellation: faces as lists of 3D verts (scale already applied) -------

def mesh_cube(s):
    x, y, z = s[0] / 2, s[1] / 2, s[2] / 2
    c = [(-x, -y, -z), (x, -y, -z), (x, y, -z), (-x, y, -z),
         (-x, -y, z), (x, -y, z), (x, y, z), (-x, y, z)]
    idx = [(0, 1, 2, 3), (5, 4, 7, 6), (4, 0, 3, 7), (1, 5, 6, 2),
           (3, 2, 6, 7), (4, 5, 1, 0)]
    return [[c[i] for i in f] for f in idx]


def mesh_cylinder(s, segs=20):
    rx, ry, rz = s[0] / 2, s[1], s[2] / 2  # height = 2*sy
    ring_t, ring_b = [], []
    for i in range(segs):
        t = math.tau * i / segs
        ring_t.append((rx * math.cos(t), ry, rz * math.sin(t)))
        ring_b.append((rx * math.cos(t), -ry, rz * math.sin(t)))
    faces = []
    for i in range(segs):
        j = (i + 1) % segs
        faces.append([ring_b[i], ring_b[j], ring_t[j], ring_t[i]])
    faces.append(list(reversed(ring_t)))
    faces.append(ring_b)
    return faces


def mesh_sphere(s, slices=16, stacks=10):
    rx, ry, rz = s[0] / 2, s[1] / 2, s[2] / 2
    rings = []
    for st in range(stacks + 1):
        phi = math.pi * st / stacks
        ring = []
        for sl in range(slices):
            th = math.tau * sl / slices
            ring.append((rx * math.sin(phi) * math.cos(th),
                         ry * math.cos(phi),
                         rz * math.sin(phi) * math.sin(th)))
        rings.append(ring)
    faces = []
    for st in range(stacks):
        for sl in range(slices):
            j = (sl + 1) % slices
            quad = [rings[st][sl], rings[st][j], rings[st + 1][j], rings[st + 1][sl]]
            faces.append(quad)
    return faces


def mesh_capsule(s, slices=16, cap_stacks=4):
    # Unity capsule: radius sx/2 (x/z), total height 2*sy (cyl core 1*sy)
    rx, rz = s[0] / 2, s[2] / 2
    hy = s[1] / 2          # half of the cylinder core
    cy = s[1] / 2          # hemisphere y radius
    rings = []
    for st in range(cap_stacks + 1):  # top hemisphere: phi 0..pi/2
        phi = (math.pi / 2) * st / cap_stacks
        y = hy + cy * math.cos(phi)
        r = math.sin(phi)
        rings.append([(rx * r * math.cos(math.tau * i / slices), y,
                       rz * r * math.sin(math.tau * i / slices)) for i in range(slices)])
    for st in range(cap_stacks + 1):  # bottom hemisphere: phi pi/2..pi
        phi = (math.pi / 2) + (math.pi / 2) * st / cap_stacks
        y = -hy + cy * math.cos(phi)
        r = math.sin(phi)
        rings.append([(rx * r * math.cos(math.tau * i / slices), y,
                       rz * r * math.sin(math.tau * i / slices)) for i in range(slices)])
    faces = []
    for ri in range(len(rings) - 1):
        for i in range(slices):
            j = (i + 1) % slices
            faces.append([rings[ri][i], rings[ri][j], rings[ri + 1][j], rings[ri + 1][i]])
    return faces


def mesh_quad(s):
    """Unity Quad: one unit square in the local XY plane (the renderer is two-sided)."""
    x, y = s[0] / 2, s[1] / 2
    return [[(-x, -y, 0.0), (x, -y, 0.0), (x, y, 0.0), (-x, y, 0.0)]]


MESHERS = {0: mesh_sphere, 1: mesh_capsule, 2: mesh_cylinder, 3: mesh_cube,
           4: mesh_cube, 5: mesh_quad}  # Plane remains the legacy fallback; Quad is a true XY surface.

MAX_EDGE = 3.0  # mild subdivision (z-buffer handles depth; keeps tris tame)


def _bilerp(q, u, v):
    return tuple((1 - u) * (1 - v) * q[0][k] + u * (1 - v) * q[1][k]
                 + u * v * q[2][k] + (1 - u) * v * q[3][k] for k in range(3))


def subdivide(faces, max_edge=MAX_EDGE):
    out = []
    for f in faces:
        if len(f) == 4:
            e0 = math.dist(f[0], f[1])
            e1 = math.dist(f[1], f[2])
            m = max(1, math.ceil(e0 / max_edge))
            n = max(1, math.ceil(e1 / max_edge))
            if m * n == 1:
                out.append(f)
                continue
            for i in range(m):
                for j in range(n):
                    out.append([_bilerp(f, i / m, j / n), _bilerp(f, (i + 1) / m, j / n),
                                _bilerp(f, (i + 1) / m, (j + 1) / n), _bilerp(f, i / m, (j + 1) / n)])
        elif len(f) > 4:  # n-gon caps: fan triangulate for finer depth sorting
            c = tuple(sum(v[k] for v in f) / len(f) for k in range(3))
            for i in range(len(f)):
                out.append([f[i], f[(i + 1) % len(f)], c])
        else:
            out.append(f)
    return out


def block_faces(block, parent_matrix=None):
    """Tessellate one block -> list of (verts_model_space, rgba).

    ``parent_matrix`` is the resolved world matrix of the block's parent. Applying it after the
    primitive's local rotation/scale preserves the non-uniform-parent shear used by MER emblem quads.
    Omitting it retains the historical flat-block behavior for callers importing this helper directly.
    """
    props = block["Properties"]
    s = (block["Scale"]["x"], block["Scale"]["y"], block["Scale"]["z"])
    ptype = props.get("PrimitiveType", 3)
    local = subdivide(MESHERS.get(ptype, mesh_cube)(s))
    r = block["Rotation"]
    m = rot_matrix(r["x"], r["y"], r["z"])
    p = (block["Position"]["x"], block["Position"]["y"], block["Position"]["z"])
    rgba = parse_color(props.get("Color", "#FFFFFF"))
    out = []
    for f in local:
        transformed = []
        for vertex in f:
            rotated = mat_vec(m, vertex)
            point = (rotated[0] + p[0], rotated[1] + p[1], rotated[2] + p[2])
            transformed.append(transform_point(parent_matrix, point) if parent_matrix else point)
        out.append((transformed, rgba))
    return out


def load_model(path, hide_prefixes=(), only_prefixes=(), show_markers=False):
    """-> list of (name, verts, rgba) faces in model space."""
    payload = json.loads(Path(path).read_text(encoding="utf-8"))
    blocks = payload["Blocks"]
    by_id = {block["ObjectId"]: block for block in blocks if "ObjectId" in block}
    world_matrices = {}
    resolving = set()

    def world_matrix(object_id):
        if object_id in world_matrices:
            return world_matrices[object_id]
        if object_id in resolving:
            raise ValueError(f"MER transform hierarchy contains a cycle at ObjectId {object_id}")

        resolving.add(object_id)
        block = by_id[object_id]
        local = trs_matrix(block)
        parent_id = block.get("ParentId")
        result = mat4_mul(world_matrix(parent_id), local) if parent_id in by_id else local
        resolving.remove(object_id)
        world_matrices[object_id] = result
        return result

    faces = []
    for block in blocks:
        if block.get("BlockType") != 1:
            continue
        name = block.get("Name", "")
        flags = block["Properties"].get("PrimitiveFlags", 2)
        if flags == 0 and not show_markers:
            continue
        if only_prefixes and not any(name.startswith(p) for p in only_prefixes):
            continue
        if any(name.startswith(p) for p in hide_prefixes):
            continue
        parent_id = block.get("ParentId")
        parent_matrix = world_matrix(parent_id) if parent_id in by_id else None
        for verts, rgba in block_faces(block, parent_matrix):
            faces.append((name, verts, rgba))
    return faces


# --- camera + rasterizer -----------------------------------------------------

def camera_basis(pos, look, up=(0, 1, 0)):
    fwd = norm(sub(look, pos))
    if abs(dot(fwd, up)) > 0.985:
        up = (0, 0, -1) if fwd[1] > 0 else (0, 0, 1)
    right = norm(cross(fwd, up))
    upv = cross(right, fwd)
    return right, upv, fwd


def clip_near(poly):
    """Sutherland-Hodgman clip of view-space polygon against z >= NEAR."""
    out = []
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        ain, bin_ = a[2] >= NEAR, b[2] >= NEAR
        if ain:
            out.append(a)
        if ain != bin_:
            t = (NEAR - a[2]) / (b[2] - a[2])
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, NEAR))
    return out


def shade(rgba, normal, centroid, cam_pos, exposure=1.0):
    n = normal
    to_cam = sub(cam_pos, centroid)
    if dot(n, to_cam) < 0:
        n = (-n[0], -n[1], -n[2])
    lum = (AMBIENT + 0.80 * max(0.0, dot(n, KEY_LIGHT))
           + 0.42 * max(0.0, dot(n, FILL_LIGHT)) + BOUNCE * max(0.0, -n[1]))
    # exposure > 1 approximates the room's in-scene point lights (the raster itself has none), so dark
    # themed surfaces stay legible; clamp scales with exposure so bright accents don't blow out immediately.
    lum = min(1.25 * exposure, lum * exposure)
    return (min(255, int(rgba[0] * lum)), min(255, int(rgba[1] * lum)),
            min(255, int(rgba[2] * lum)), rgba[3])


def _raster_tri(p0, p1, p2, col, color, zbuf, write_z, alpha=None):
    """Edge-function triangle raster with 1/z depth test (numpy)."""
    H, W = zbuf.shape
    xs = (p0[0], p1[0], p2[0])
    ys = (p0[1], p1[1], p2[1])
    x0i = max(0, int(math.floor(min(xs))))
    x1i = min(W, int(math.ceil(max(xs))) + 1)
    y0i = max(0, int(math.floor(min(ys))))
    y1i = min(H, int(math.ceil(max(ys))) + 1)
    if x1i <= x0i or y1i <= y0i:
        return
    denom = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
    if abs(denom) < 1e-9:
        return
    px = np.arange(x0i, x1i, dtype=np.float64) + 0.5
    py = np.arange(y0i, y1i, dtype=np.float64) + 0.5
    gx, gy = np.meshgrid(px, py)
    l0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / denom
    l1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / denom
    l2 = 1.0 - l0 - l1
    eps = -1e-7
    mask = (l0 >= eps) & (l1 >= eps) & (l2 >= eps)
    if not mask.any():
        return
    invz = l0 / p0[2] + l1 / p1[2] + l2 / p2[2]
    zb = zbuf[y0i:y1i, x0i:x1i]
    mask &= invz > zb
    if not mask.any():
        return
    cb = color[y0i:y1i, x0i:x1i]
    if alpha is None:
        zb[mask] = invz[mask]
        cb[mask] = col
    else:
        a = alpha / 255.0
        cb[mask] = (np.array(col, dtype=np.float64) * a
                    + cb[mask] * (1.0 - a))
        if write_z:
            zb[mask] = invz[mask]


def render(faces_world, cam_pos, look, fov=60.0, width=1280, height=None,
           bg="#10141A", supersample=SUPERSAMPLE, up=(0, 1, 0), exposure=1.0):
    """faces_world: iterable of (name, verts, rgba). Z-buffer raster -> RGB image."""
    if height is None:
        height = int(width * 9 / 16)
    W, H = width * supersample, height * supersample
    right, upv, fwd = camera_basis(cam_pos, look, up)
    f = 1.0 / math.tan(math.radians(fov) / 2)
    aspect = W / H

    opaque, translucent = [], []
    for name, verts, rgba in faces_world:
        view = []
        behind = 0
        for v in verts:
            rel = sub(v, cam_pos)
            vv = (dot(rel, right), dot(rel, upv), dot(rel, fwd))
            if vv[2] < NEAR:
                behind += 1
            view.append(vv)
        if behind == len(view):
            continue
        if behind:
            view = clip_near(view)
            if len(view) < 3:
                continue
        pts = []
        for vx, vy, vz in view:
            px = (vx / vz * f * 0.5 + 0.5) * W
            py = (0.5 - vy / vz * f * aspect * 0.5) * H
            pts.append((px, py, vz))
        if max(p[0] for p in pts) < 0 or min(p[0] for p in pts) > W:
            continue
        if max(p[1] for p in pts) < 0 or min(p[1] for p in pts) > H:
            continue
        nrm = cross(sub(verts[1], verts[0]), sub(verts[2], verts[0]))
        l = math.sqrt(dot(nrm, nrm))
        if l < 1e-12:
            continue
        nrm = (nrm[0] / l, nrm[1] / l, nrm[2] / l)
        wc = (sum(v[0] for v in verts) / len(verts), sum(v[1] for v in verts) / len(verts),
              sum(v[2] for v in verts) / len(verts))
        col = shade(rgba, nrm, wc, cam_pos, exposure)
        if col[3] >= 255:
            opaque.append((pts, col[:3]))
        else:
            depth = sum(p[2] for p in pts) / len(pts)
            translucent.append((depth, pts, col))

    bgc = parse_color(bg)
    color = np.empty((H, W, 3), dtype=np.float64)
    color[:, :] = bgc[:3]
    zbuf = np.zeros((H, W), dtype=np.float64)  # stores 1/z; 0 = infinitely far
    for pts, col in opaque:
        for i in range(1, len(pts) - 1):
            _raster_tri(pts[0], pts[i], pts[i + 1], col, color, zbuf, True)
    translucent.sort(key=lambda t: -t[0])  # far first
    for _, pts, col in translucent:
        for i in range(1, len(pts) - 1):
            _raster_tri(pts[0], pts[i], pts[i + 1], col[:3], color, zbuf, False,
                        alpha=col[3])
    img = Image.fromarray(color.clip(0, 255).astype(np.uint8), "RGB")
    if supersample > 1:
        img = img.resize((width, height), Image.LANCZOS)
    return img


# --- presets / sheet / CLI ---------------------------------------------------

def model_bounds(faces):
    xs = [v[0] for _, verts, _ in faces for v in verts]
    ys = [v[1] for _, verts, _ in faces for v in verts]
    zs = [v[2] for _, verts, _ in faces for v in verts]
    lo = (min(xs), min(ys), min(zs))
    hi = (max(xs), max(ys), max(zs))
    center = tuple((a + b) / 2 for a, b in zip(lo, hi))
    size = max(hi[i] - lo[i] for i in range(3))
    return lo, hi, center, size


def preset_view(name, faces):
    _, _, c, s = model_bounds(faces)
    d = s * 1.05 + 2.0
    views = {
        "front": ((c[0], c[1] + 0.06 * s, c[2] + d), c, 60.0),
        "side": ((c[0] + d, c[1] + 0.06 * s, c[2]), c, 60.0),
        "top": ((c[0], c[1] + d * 1.1, c[2] + 0.001), c, 60.0),
        "threequarter": ((c[0] + 0.72 * d, c[1] + 0.5 * d, c[2] + 0.72 * d), c, 60.0),
        "interior": ((0.0, 1.64, -1.0), (0.0, 1.35, 6.0), 75.0),
    }
    if name not in views:
        raise SystemExit(f"Unknown preset '{name}'")
    return views[name]


def parse_vec(s):
    parts = [float(p) for p in s.split(",")]
    if len(parts) != 3:
        raise SystemExit(f"Expected x,y,z - got '{s}'")
    return tuple(parts)


def parse_view_spec(spec, faces):
    """'label:px,py,pz:lx,ly,lz[:fov]' or a preset name."""
    if ":" not in spec:
        pos, look, fov = preset_view(spec, faces)
        return spec, pos, look, fov
    parts = spec.split(":")
    label = parts[0]
    pos = parse_vec(parts[1])
    look = parse_vec(parts[2])
    fov = float(parts[3]) if len(parts) > 3 else 60.0
    return label, pos, look, fov


def label_image(img, text):
    d = ImageDraw.Draw(img)
    pad = 6
    tw = d.textlength(text)
    d.rectangle([4, 4, 4 + tw + 2 * pad, 26], fill=(8, 10, 14, 220))
    d.text((4 + pad, 9), text, fill=(225, 230, 238))
    return img


def make_sheet(cells, cols=None):
    """cells: list of (label, PIL image). All same size."""
    n = len(cells)
    if cols is None:
        cols = 2 if n <= 4 else 3
    rows = (n + cols - 1) // cols
    cw, ch = cells[0][1].size
    sheet = Image.new("RGB", (cw * cols + (cols + 1) * 4, ch * rows + (rows + 1) * 4), "#06080B")
    for i, (label, img) in enumerate(cells):
        label_image(img, label)
        r, c = divmod(i, cols)
        sheet.paste(img, (4 + c * (cw + 4), 4 + r * (ch + 4)))
    return sheet


def main() -> None:
    ap = argparse.ArgumentParser(description="True-3D .mer.json previewer")
    ap.add_argument("--input", type=Path, required=True)
    ap.add_argument("--output", type=Path, required=True)
    ap.add_argument("--width", type=int, default=1280)
    ap.add_argument("--height", type=int, default=None)
    ap.add_argument("--pos", type=str, default=None, help="camera x,y,z")
    ap.add_argument("--look", type=str, default=None, help="target x,y,z")
    ap.add_argument("--fov", type=float, default=60.0)
    ap.add_argument("--preset", choices=("front", "side", "top", "threequarter", "interior"))
    ap.add_argument("--sheet", action="store_true",
                    help="contact sheet of --view specs (or default presets)")
    ap.add_argument("--view", action="append", default=[],
                    help="'label:px,py,pz:lx,ly,lz[:fov]' or preset name (repeatable)")
    ap.add_argument("--hide-prefix", action="append", default=[],
                    help="hide blocks whose name starts with this (csv ok)")
    ap.add_argument("--only-prefix", action="append", default=[],
                    help="only show blocks whose name starts with this (csv ok)")
    ap.add_argument("--show-markers", action="store_true")
    ap.add_argument("--background", default="#10141A")
    ap.add_argument("--exposure", type=float, default=1.0,
                    help="brighten shading to approximate in-scene point lights (room previews)")
    args = ap.parse_args()

    hide = [p for spec in args.hide_prefix for p in spec.split(",") if p]
    only = [p for spec in args.only_prefix for p in spec.split(",") if p]
    faces = load_model(args.input, hide, only, args.show_markers)
    if not faces:
        raise SystemExit("No visible primitive blocks after filtering")

    args.output.parent.mkdir(parents=True, exist_ok=True)
    if args.sheet:
        specs = args.view or ["front", "side", "top", "threequarter"]
        cells = []
        for spec in specs:
            label, pos, look, fov = parse_view_spec(spec, faces)
            img = render(faces, pos, look, fov, args.width, args.height, args.background)
            cells.append((label, img))
        make_sheet(cells).save(args.output)
        print(f"Wrote sheet {args.output} ({len(cells)} views, {len(faces)} faces)")
        return

    if args.preset:
        pos, look, fov = preset_view(args.preset, faces)
    else:
        if not args.pos or not args.look:
            raise SystemExit("Provide --preset or both --pos and --look")
        pos, look, fov = parse_vec(args.pos), parse_vec(args.look), args.fov
    img = render(faces, pos, look, fov, args.width, args.height, args.background)
    img.save(args.output)
    print(f"Rendered {len(faces)} faces to {args.output}")


if __name__ == "__main__":
    main()

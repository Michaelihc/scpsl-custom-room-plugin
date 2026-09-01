#!/usr/bin/env python3
"""Find z-fighting in an exported station schematic.

Two surfaces flicker when they FACE THE SAME WAY at the same depth. An abutting pair (one face
pointing +y, the other -y) is fine; a shared plane is not. Colour is irrelevant - two identically
coloured slabs flicker just as visibly, which is how the deck seams went unnoticed for so long.

Run it against the plugin's own export of the LIVE world (Config.ExportSchematicName or the
`warmupexport` RA command), never against a hand-written description of the geometry: only the export
contains what the authored asset and the code-owned Aim Bay actually produced together.

    python tools/scan_coplanar.py "<...>/Schematics/verify/verify.json"

Two things this deliberately does NOT skip, both of which hid real defects:

  * boxes rotated by multiples of 90 degrees. They are still axis aligned - the extents just permute -
    and dropping them skips most of an authored file.
  * same-colour pairs.

It does skip faces sitting at deck level (y=0) that point downwards: those are buried inside the
0.4 m deck slab and no camera can reach them.
"""
from __future__ import annotations

import json
import math
import sys

MIN_AREA = 0.05          # m^2 of overlap worth reporting
PLANE_EPS = 0.0015       # metres; closer than this counts as "the same plane"
CUBE = 3                 # UnityEngine.PrimitiveType.Cube
VISIBLE = 2              # AdminToys.PrimitiveFlags.Visible


def vec(v):
    return (v["x"], v["y"], v["z"])


def rotation_matrix(euler):
    """Unity ZXY euler snapped to right angles, or None if this box is not axis aligned."""
    def snapped(angle):
        angle %= 360
        return min(abs(angle - k) for k in (0, 90, 180, 270, 360)) < 0.6

    if not all(snapped(a) for a in euler):
        return None

    def cs(angle):
        angle = round((angle % 360) / 90) * 90
        return ({0: 1, 90: 0, 180: -1, 270: 0}[angle], {0: 0, 90: 1, 180: 0, 270: -1}[angle])

    cx, sx = cs(euler[0])
    cy, sy = cs(euler[1])
    cz, sz = cs(euler[2])
    rx = [[1, 0, 0], [0, cx, -sx], [0, sx, cx]]
    ry = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]
    rz = [[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]]

    def mul(a, b):
        return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]

    return mul(mul(ry, rx), rz)


def load_boxes(path):
    schematic = json.load(open(path, encoding="utf-8"))
    blocks = schematic["Blocks"]
    root = schematic["RootObjectId"]
    by_id = {b["ObjectId"]: b for b in blocks if "ObjectId" in b}

    def world(block):
        # Positions are local to the parent; parent rotation is ignored, which is accurate for the
        # shell (every structural block is a root-level child) and close enough for decor.
        pos = list(vec(block["Position"]))
        parent = block.get("ParentId")
        for _ in range(8):
            if parent is None or parent == root or parent not in by_id:
                break
            owner = by_id[parent]
            pos = [pos[i] + vec(owner["Position"])[i] for i in range(3)]
            parent = owner.get("ParentId")
        return tuple(pos)

    boxes = []
    for block in blocks:
        if block.get("BlockType") != 1:
            continue
        props = block["Properties"]
        if props.get("PrimitiveType") != CUBE:
            continue
        if (props.get("PrimitiveFlags", VISIBLE) & VISIBLE) == 0:
            continue  # invisible: it cannot fight with anything
        matrix = rotation_matrix(vec(block["Rotation"]))
        if matrix is None:
            continue
        scale = [abs(x) for x in vec(block["Scale"])]
        extent = [sum(abs(matrix[i][k]) * scale[k] for k in range(3)) for i in range(3)]
        boxes.append((world(block), extent, props.get("Color"), block.get("Name", "")))
    return boxes


def find_pairs(boxes):
    hits = []
    for i, (pi, si, ci, ni) in enumerate(boxes):
        for pj, sj, cj, nj in boxes[i + 1:]:
            for axis in range(3):
                for sign in (1, -1):
                    face = pi[axis] + sign * si[axis] / 2
                    if abs(face - (pj[axis] + sign * sj[axis] / 2)) > PLANE_EPS:
                        continue
                    if axis == 1 and sign < 0 and abs(face) < 0.005:
                        continue  # buried in the deck slab
                    area = 1.0
                    for k in range(3):
                        if k == axis:
                            continue
                        low = max(pi[k] - si[k] / 2, pj[k] - sj[k] / 2)
                        high = min(pi[k] + si[k] / 2, pj[k] + sj[k] / 2)
                        if high - low <= 0.03:
                            area = 0.0
                            break
                        area *= high - low
                    if area >= MIN_AREA:
                        hits.append((area, "xyz"[axis], sign, face, ci, pi, cj, pj))
    return sorted(hits, key=lambda h: -h[0])


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2
    boxes = load_boxes(argv[1])
    hits = find_pairs(boxes)
    print(f"{len(boxes)} visible axis-aligned boxes")
    print(f"{len(hits)} coplanar overlapping face pairs >= {MIN_AREA} m^2")
    for area, axis, sign, plane, ci, pi, cj, pj in hits:
        print(f"  area={area:6.2f} {'+' if sign > 0 else '-'}{axis}={plane:.3f}  "
              f"{ci} @({pi[0]:.2f},{pi[1]:.2f},{pi[2]:.2f})  ||  {cj} @({pj[0]:.2f},{pj[1]:.2f},{pj[2]:.2f})")
    return 1 if hits else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))

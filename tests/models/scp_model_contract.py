#!/usr/bin/env python3
"""Reusable offline geometry contract checks for the WarmupScpSelector SCP models.

The local SCP:SL server cannot be booted from an automated shell, so each test validates the artifact
the C# MerModelLoader consumes: an empty root container, a sane visible-block count, required marker
names, an upright layout seated on the ground, a footprint that fits its pedestal, and a varied palette
with valid colors. Per-SCP tests call run(...) with their own expectations.
"""
from __future__ import annotations

import json
import math
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
MODELS = REPO / "generated" / "models"
HEXRE = re.compile(r"^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$")
KNOWN_TYPES = {0, 1, 2, 3, 4, 5}  # sphere, capsule, cylinder, cube, (quad 4/5) - the loader's range
FLAGS_VISIBLE = 2
FLAGS_NONE = 0


def _rotation_matrix(rx: float, ry: float, rz: float) -> tuple[tuple[float, float, float], ...]:
    ax, ay, az = math.radians(rx), math.radians(ry), math.radians(rz)
    cx, sx = math.cos(ax), math.sin(ax)
    cy, sy = math.cos(ay), math.sin(ay)
    cz, sz = math.cos(az), math.sin(az)
    return (
        (cy * cz + sy * sx * sz, -cy * sz + sy * sx * cz, sy * cx),
        (cx * sz, cx * cz, -sx),
        (-sy * cz + cy * sx * sz, sy * sz + cy * sx * cz, cy * cx),
    )


def _half_extents(block: dict) -> tuple[float, float, float]:
    ptype = block["Properties"].get("PrimitiveType")
    scale = block["Scale"]
    sx, sy, sz = abs(scale["x"]), abs(scale["y"]), abs(scale["z"])
    if ptype == 2:  # Unity cylinder scale.y is half-height.
        return sx / 2, sy, sz / 2
    if ptype == 1:  # Capsule body is cylinder-like enough for conservative bounds.
        return sx / 2, sy, sz / 2
    return sx / 2, sy / 2, sz / 2


def _world_aabb(block: dict) -> tuple[float, float, float, float, float, float]:
    pos = block["Position"]
    rot = block["Rotation"]
    hx, hy, hz = _half_extents(block)
    matrix = _rotation_matrix(rot["x"], rot["y"], rot["z"])
    wx = abs(matrix[0][0]) * hx + abs(matrix[0][1]) * hy + abs(matrix[0][2]) * hz
    wy = abs(matrix[1][0]) * hx + abs(matrix[1][1]) * hy + abs(matrix[1][2]) * hz
    wz = abs(matrix[2][0]) * hx + abs(matrix[2][1]) * hy + abs(matrix[2][2]) * hz
    return (
        pos["x"] - wx,
        pos["y"] - wy,
        pos["z"] - wz,
        pos["x"] + wx,
        pos["y"] + wy,
        pos["z"] + wz,
    )


def _is_axis_aligned(block: dict) -> bool:
    rot = block["Rotation"]
    return all(abs(rot[axis]) < 1e-5 for axis in ("x", "y", "z"))


def _overlap_1d(a0: float, a1: float, b0: float, b1: float, eps: float) -> bool:
    return min(a1, b1) - max(a0, b0) > eps


def load(name: str) -> dict:
    return json.loads((MODELS / f"{name}.mer.json").read_text(encoding="utf-8"))


def validate(
    name: str,
    *,
    min_visible: int,
    max_visible: int,
    required_markers: tuple[str, ...] = (),
    min_y: float = -0.05,
    max_y: float | None = None,
    max_radius: float | None = None,
    min_distinct_colors: int = 3,
) -> tuple[list[str], dict]:
    doc = load(name)
    blocks = doc["Blocks"]
    prims = [b for b in blocks if b.get("BlockType") == 1]
    visible = [b for b in prims if b["Properties"].get("PrimitiveFlags") == FLAGS_VISIBLE]
    markers = [b for b in prims if b["Properties"].get("PrimitiveFlags") == FLAGS_NONE]
    marker_names = {b["Name"] for b in markers}
    ids = [b.get("ObjectId") for b in blocks]
    id_set = set(ids)
    issues: list[str] = []

    def check(cond: bool, msg: str) -> None:
        if not cond:
            issues.append(msg)

    check(doc.get("RootObjectId") == 0, "RootObjectId must be 0")
    check(bool(blocks) and blocks[0]["BlockType"] == 0, "first block must be the empty root container")
    check(len(ids) == len(id_set), "ObjectId values must be unique")
    for b in blocks:
        parent = b.get("ParentId")
        check(parent == -1 or parent in id_set, f"{b.get('Name', '<unnamed>')} has missing ParentId {parent}")
    check(min_visible <= len(visible) <= max_visible,
          f"expected {min_visible}-{max_visible} visible primitives, got {len(visible)}")

    for marker in required_markers:
        check(marker in marker_names, f"missing marker {marker}")

    aabbs: dict[str, tuple[float, float, float, float, float, float]] = {}
    for b in visible:
        props = b["Properties"]
        check(props.get("PrimitiveType") in KNOWN_TYPES,
              f"{b['Name']} has unknown primitive type {props.get('PrimitiveType')}")
        check(bool(HEXRE.match(props.get("Color", ""))),
              f"{b['Name']} has bad color {props.get('Color')}")
        scale = b["Scale"]
        check(all(scale[axis] > 0 for axis in ("x", "y", "z")),
              f"{b['Name']} must have positive non-zero scale")
        aabbs[b["Name"]] = _world_aabb(b)

    if visible:
        min_bound_y = min(aabb[1] for aabb in aabbs.values())
        max_bound_y = max(aabb[4] for aabb in aabbs.values())
        check(min_bound_y >= min_y, f"model should sit on the floor, min y={min_bound_y:.2f} < {min_y}")
        if max_y is not None:
            check(max_bound_y <= max_y, f"model top should be <= {max_y}, got max y={max_bound_y:.2f}")

    if max_radius is not None:
        for b in visible:
            min_x, _, min_z, max_x, _, max_z = aabbs[b["Name"]]
            radius = max(
                math.hypot(x, z)
                for x in (min_x, max_x)
                for z in (min_z, max_z)
            )
            check(radius <= max_radius,
                  f"{b['Name']} extends {radius:.2f}m off-axis (> {max_radius}m footprint)")

    axis_aligned = [b for b in visible if _is_axis_aligned(b)]
    for i, a in enumerate(axis_aligned):
        a_box = aabbs[a["Name"]]
        for b in axis_aligned[i + 1:]:
            b_box = aabbs[b["Name"]]
            axes = (
                (0, 3, 1, 4, 2, 5, "x"),
                (1, 4, 0, 3, 2, 5, "y"),
                (2, 5, 0, 3, 1, 4, "z"),
            )
            for lo, hi, o0, o1, p0, p1, axis in axes:
                faces_touch = (
                    abs(a_box[lo] - b_box[lo]) < 1e-4
                    or abs(a_box[lo] - b_box[hi]) < 1e-4
                    or abs(a_box[hi] - b_box[lo]) < 1e-4
                    or abs(a_box[hi] - b_box[hi]) < 1e-4
                )
                if (
                    faces_touch
                    and _overlap_1d(a_box[o0], a_box[o1], b_box[o0], b_box[o1], 1e-4)
                    and _overlap_1d(a_box[p0], a_box[p1], b_box[p0], b_box[p1], 1e-4)
                ):
                    issues.append(f"{a['Name']} and {b['Name']} have coplanar overlapping {axis}-faces")

    palette = {b["Properties"].get("Color", "").upper() for b in visible}
    check(len(palette) >= min_distinct_colors,
          f"expected >= {min_distinct_colors} distinct colors, got {len(palette)}")

    return issues, {"visible": len(visible), "markers": len(markers), "colors": len(palette)}


def run(name: str, **kwargs) -> None:
    issues, stats = validate(name, **kwargs)
    if issues:
        print(f"FAIL {name} ({len(issues)} issue(s)):")
        for issue in issues:
            print(f"  - {issue}")
        sys.exit(1)
    print(f"PASS {name}  {stats['visible']} visible primitives, {stats['markers']} markers, {stats['colors']} colors.")
    sys.exit(0)

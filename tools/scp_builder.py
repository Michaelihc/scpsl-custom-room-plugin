#!/usr/bin/env python3
"""Shared ProjectMER (.mer.json) model builder for the WarmupScpSelector SCP models.

Every SCP model script (build_scp_*_asset.py) imports this module so all models share the exact
block schema the C# MerModelLoader consumes and the offline render_model_preview.py renders. Models
are authored y-up, base at y=0, FACING +Z (the runtime rotates each model to face the player). Use
cubes/spheres/cylinders only so the in-game MER, the previewer, and the contract test all agree.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

BLOCK_EMPTY = 0
BLOCK_PRIMITIVE = 1

PRIM_SPHERE = 0
PRIM_CAPSULE = 1
PRIM_CYLINDER = 2
PRIM_CUBE = 3
PRIM_QUAD = 5

FLAGS_NONE = 0      # invisible named marker (PrimitiveFlags.None)
FLAGS_VISIBLE = 2   # visible, non-collidable (PrimitiveFlags.Visible)


def clean(value: float) -> float:
    rounded = round(value, 5)
    return 0.0 if rounded == -0.0 else rounded


def v3(x: float, y: float, z: float) -> dict[str, float]:
    return {"x": clean(x), "y": clean(y), "z": clean(z)}


class Builder:
    """Flat MER builder: one empty root with every primitive parented to it."""

    def __init__(self, root_name: str) -> None:
        self.blocks: list[dict] = [
            {
                "Name": root_name,
                "ObjectId": 0,
                "ParentId": -1,
                "AnimatorName": "",
                "Position": v3(0, 0, 0),
                "Rotation": v3(0, 0, 0),
                "Scale": v3(1, 1, 1),
                "BlockType": BLOCK_EMPTY,
                "Properties": {"Static": False},
            }
        ]
        self.next_id = 2
        self.counter = 0

    def add(self, name, pos, rot=(0.0, 0.0, 0.0), scale=(1.0, 1.0, 1.0),
            color="#FFFFFF", prim=PRIM_CUBE, flags=FLAGS_VISIBLE, exact_name=False) -> None:
        self.counter += 1
        self.blocks.append({
            "Name": name if exact_name else f"{name}_{self.counter:04d}",
            "ObjectId": self.next_id,
            "ParentId": 0,
            "AnimatorName": "",
            "Position": v3(*pos),
            "Rotation": v3(*rot),
            "Scale": v3(*scale),
            "BlockType": BLOCK_PRIMITIVE,
            "Properties": {
                "PrimitiveType": prim,
                "Color": color,
                "PrimitiveFlags": flags,
                "Static": False,
            },
        })
        self.next_id += 1

    # Convenience shapes -----------------------------------------------------------------------

    def box(self, name, center, size, color, rot=(0.0, 0.0, 0.0)) -> None:
        self.add(name, center, rot, size, color, PRIM_CUBE)

    def sphere(self, name, center, diameter, color) -> None:
        self.add(name, center, (0, 0, 0), (diameter, diameter, diameter), color, PRIM_SPHERE)

    def cylinder(self, name, center, size, color, rot=(0.0, 0.0, 0.0)) -> None:
        self.add(name, center, rot, size, color, PRIM_CYLINDER)

    def marker(self, name, pos) -> None:
        """Tiny invisible cube the runtime resolves by exact name (anchors, light points)."""
        self.add(name, pos, (0, 0, 0), (0.01, 0.01, 0.01), "#FF00FF", PRIM_CUBE, FLAGS_NONE, exact_name=True)

    def seg(self, name, a, b, width, color, thick=None, overrun=0.0) -> None:
        """Thin cube spanning a 3D segment; local Z carries the length."""
        dx, dy, dz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
        length = math.sqrt(dx * dx + dy * dy + dz * dz)
        if length < 1e-4:
            return
        mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2)
        pitch = -math.degrees(math.asin(max(-1.0, min(1.0, dy / length))))
        yaw = math.degrees(math.atan2(dx, dz))
        self.add(name, mid, (pitch, yaw, 0.0),
                 (width, thick if thick is not None else width, length + overrun), color)

    def to_json(self) -> dict[str, object]:
        return {"RootObjectId": 0, "Blocks": self.blocks}

    # Counts -----------------------------------------------------------------------------------

    def visible_count(self) -> int:
        return sum(1 for b in self.blocks
                   if b.get("Properties", {}).get("PrimitiveFlags") == FLAGS_VISIBLE)

    def marker_count(self) -> int:
        return sum(1 for b in self.blocks
                   if b.get("Properties", {}).get("PrimitiveFlags") == FLAGS_NONE)


def default_out_dir() -> Path:
    return Path(__file__).resolve().parent.parent / "generated" / "models"


def save(builder: Builder, name: str, out_dir: Path | None = None) -> Path:
    out_dir = out_dir or default_out_dir()
    out_dir.mkdir(parents=True, exist_ok=True)
    path = out_dir / f"{name}.mer.json"
    path.write_text(json.dumps(builder.to_json(), indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {path} ({builder.visible_count()} visible primitives, "
          f"{builder.marker_count()} markers, {len(builder.blocks)} blocks)")
    return path


def standard_main(name: str, build_fn) -> int:
    """A build_scp_*_asset.py main(): parse --out-dir/--name, build, and save."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--out-dir", type=Path, default=default_out_dir())
    parser.add_argument("--name", default=name)
    args = parser.parse_args()
    save(build_fn(), args.name, args.out_dir)
    return 0

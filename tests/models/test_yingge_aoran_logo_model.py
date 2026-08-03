#!/usr/bin/env python3
"""Contract for the sheared-quad server logo supplied by the adjacent Embel converter repo.

Run directly with the production asset in ``generated/models``; the adjacent ``Embel-conversion-fr``
checkout remains a development fallback for validating a freshly converted copy.
"""
from __future__ import annotations

import json
import math
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
HEX_COLOR = re.compile(r"^#[0-9A-Fa-f]{8}$")
EXPECTED_BOUNDS = (-3.213721, -3.684212, 3.205746, 4.266352)


def find_asset() -> Path | None:
    candidates = (
        REPO / "generated" / "models" / "yingge-aoran-logo-opt.mer.json",
        REPO / "generated" / "models" / "yingge-aoran-logo.mer.json",
        REPO.parent
        / "Embel-conversion-fr"
        / "converted_mer"
        / "yingge-aoran-logo-opt"
        / "yingge-aoran-logo-opt.json",
    )
    return next((path for path in candidates if path.is_file()), None)


def apply_trs(block: dict, point: tuple[float, float, float]) -> tuple[float, float, float]:
    """Apply this asset's Z-rotation TRS to a local point."""
    scale = block["Scale"]
    x = point[0] * scale["x"]
    y = point[1] * scale["y"]
    z = point[2] * scale["z"]
    angle = math.radians(block["Rotation"]["z"])
    cosine, sine = math.cos(angle), math.sin(angle)
    position = block["Position"]
    return (
        cosine * x - sine * y + position["x"],
        sine * x + cosine * y + position["y"],
        z + position["z"],
    )


def world_quad_corners(block: dict, by_id: dict[int, dict]) -> list[tuple[float, float, float]]:
    corners = []
    parent = by_id.get(block["ParentId"])
    for x in (-0.5, 0.5):
        for y in (-0.5, 0.5):
            point = apply_trs(block, (x, y, 0.0))
            if parent is not None:
                point = apply_trs(parent, point)
            corners.append(point)
    return corners


def main() -> int:
    asset = find_asset()
    if asset is None:
        print("SKIP yingge-aoran-logo: production artwork is not present in this checkout.")
        return 0

    document = json.loads(asset.read_text(encoding="utf-8"))
    blocks = document.get("Blocks", [])
    empties = [block for block in blocks if block.get("BlockType") == 0]
    primitives = [block for block in blocks if block.get("BlockType") == 1]
    by_id = {block.get("ObjectId"): block for block in blocks}
    issues: list[str] = []

    def check(condition: bool, message: str) -> None:
        if not condition:
            issues.append(message)

    check(document.get("RootObjectId") == 0, "RootObjectId must be the virtual root 0")
    check(len(blocks) == 214, f"expected 214 MER blocks, got {len(blocks)}")
    check(len(by_id) == len(blocks), "ObjectId values must be unique")
    check(len(empties) == 106, f"expected 106 shear parents, got {len(empties)}")
    check(len(primitives) == 108, f"expected 108 visible logo quads, got {len(primitives)}")
    check(0 not in by_id, "the optimized asset should use a virtual root rather than a root block")

    child_counts: dict[int, int] = {}
    for block in blocks:
        child_counts[block["ParentId"]] = child_counts.get(block["ParentId"], 0) + 1

    for empty in empties:
        check(empty["ParentId"] == 0, f"{empty['Name']} must be directly under the virtual root")
        check(child_counts.get(empty["ObjectId"]) == 1, f"{empty['Name']} must own exactly one quad")

    sheared = [block for block in primitives if block["ParentId"] in by_id]
    direct = [block for block in primitives if block["ParentId"] == 0]
    check(len(sheared) == 106, f"expected 106 parented quads, got {len(sheared)}")
    check(len(direct) == 2, f"expected 2 direct root quads, got {len(direct)}")

    for block in primitives:
        properties = block.get("Properties", {})
        check(properties.get("PrimitiveType") == 5, f"{block['Name']} must be a Quad")
        check(properties.get("PrimitiveFlags") == 2, f"{block['Name']} must be visible/non-collidable")
        check(properties.get("Static") is True, f"{block['Name']} must be static")
        check(bool(HEX_COLOR.match(properties.get("Color", ""))), f"{block['Name']} has an invalid RGBA color")
        check(all(block["Scale"][axis] > 0 for axis in ("x", "y", "z")), f"{block['Name']} has non-positive scale")

    corners = [corner for block in primitives for corner in world_quad_corners(block, by_id)]
    min_x = min(point[0] for point in corners)
    min_y = min(point[1] for point in corners)
    max_x = max(point[0] for point in corners)
    max_y = max(point[1] for point in corners)
    actual_bounds = (min_x, min_y, max_x, max_y)
    for actual, expected, label in zip(actual_bounds, EXPECTED_BOUNDS, ("min x", "min y", "max x", "max y")):
        check(abs(actual - expected) < 1e-4, f"unexpected {label}: {actual:.6f}, expected {expected:.6f}")

    palette = {block["Properties"]["Color"].upper() for block in primitives}
    check(len(palette) == 33, f"expected the optimized 33-color palette, got {len(palette)}")

    if issues:
        print(f"FAIL yingge-aoran-logo ({len(issues)} issue(s)) from {asset}:")
        for issue in issues:
            print(f"  - {issue}")
        return 1

    print(
        "PASS yingge-aoran-logo  "
        f"{len(primitives)} visible quads, {len(empties)} shear parents, "
        f"{max_x - min_x:.3f} x {max_y - min_y:.3f} bounds, {len(palette)} colors."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())

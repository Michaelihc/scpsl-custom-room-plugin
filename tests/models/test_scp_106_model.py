#!/usr/bin/env python3
"""Geometry contract for the SCP-106 model. Run: python tests/models/test_scp_106_model.py"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_model_contract import FLAGS_VISIBLE, load, validate  # noqa: E402

CUBE = 3


def _visible_blocks(doc: dict) -> list[dict]:
    return [
        block for block in doc["Blocks"]
        if block.get("BlockType") == 1
        and block["Properties"].get("PrimitiveFlags") == FLAGS_VISIBLE
    ]


def _by_prefix(blocks: list[dict], prefix: str) -> list[dict]:
    return [block for block in blocks if block["Name"].startswith(prefix)]


def _box_aabb(block: dict) -> tuple[float, float, float, float, float, float]:
    pos = block["Position"]
    scale = block["Scale"]
    hx, hy, hz = scale["x"] / 2, scale["y"] / 2, scale["z"] / 2
    return (
        pos["x"] - hx,
        pos["y"] - hy,
        pos["z"] - hz,
        pos["x"] + hx,
        pos["y"] + hy,
        pos["z"] + hz,
    )


def _luma(color: str) -> float:
    color = color.lstrip("#")
    r = int(color[0:2], 16)
    g = int(color[2:4], 16)
    b = int(color[4:6], 16)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def _single(blocks: list[dict], prefix: str, issues: list[str]) -> dict | None:
    matches = _by_prefix(blocks, prefix)
    if len(matches) != 1:
        issues.append(f"expected exactly one {prefix} block, got {len(matches)}")
        return None
    return matches[0]


def main() -> int:
    issues, stats = validate(
        "scp-106",
        min_visible=11,
        max_visible=11,
        required_markers=("marker_pivot", "marker_head"),
        min_y=-0.05,
        max_y=1.85,
        max_radius=1.0,
        min_distinct_colors=4,
    )

    doc = load("scp-106")
    visible = _visible_blocks(doc)

    if any(block["Properties"].get("PrimitiveType") != CUBE for block in visible):
        issues.append("SCP-106 should stay box-only for predictable ProjectMER icon extents")

    aabbs = {block["Name"]: _box_aabb(block) for block in visible}
    min_y = min(aabb[1] for aabb in aabbs.values())
    max_y = max(aabb[4] for aabb in aabbs.values())
    if abs(min_y) > 1e-5:
        issues.append(f"visible base should be exactly y=0, got {min_y:.5f}")
    if max_y > 1.85:
        issues.append(f"hunched humanoid should stay compact, top y={max_y:.2f}")

    min_x = min(aabb[0] for aabb in aabbs.values())
    max_x = max(aabb[3] for aabb in aabbs.values())
    min_z = min(aabb[2] for aabb in aabbs.values())
    max_z = max(aabb[5] for aabb in aabbs.values())
    if max_y - min_y < 1.5:
        issues.append("silhouette is too short to read as a humanoid")
    if max_x - min_x < 0.9:
        issues.append("silhouette is too narrow to read from a few meters")
    if max_z - min_z < 0.85:
        issues.append("model needs visible front/back depth, not a flat slab")

    head = _single(visible, "head_", issues)
    hunch = _single(visible, "back_hunch_", issues)
    torso = _single(visible, "torso_", issues)
    eyes = _by_prefix(visible, "eye_")
    if len(eyes) != 2:
        issues.append(f"expected two forward eye sockets, got {len(eyes)}")

    if head and hunch:
        if hunch["Position"]["y"] <= head["Position"]["y"]:
            issues.append("back hunch should sit higher than the low forward head")
        if head["Position"]["z"] <= hunch["Position"]["z"] + 0.35:
            issues.append("head should be visibly forward of the hunch, facing +Z")
    if head and torso:
        if head["Position"]["z"] <= torso["Position"]["z"] + 0.25:
            issues.append("head should jut forward from the torso to sell the stoop")
    if head and eyes:
        head_front = _box_aabb(head)[5]
        for eye in eyes:
            eye_min_z = _box_aabb(eye)[2]
            eye_max_z = _box_aabb(eye)[5]
            if eye_min_z >= head_front or eye_max_z <= head_front:
                issues.append(f"{eye['Name']} should overlap the +Z face instead of floating or hiding")

    palette = {
        block["Properties"]["Color"].upper()
        for block in visible
    }
    if "#000000" in palette:
        issues.append("SCP-106 should not use pure black visible primitives")
    body_lumas = [
        _luma(block["Properties"]["Color"])
        for block in visible
        if block["Name"].startswith(("leg_", "hips_", "torso_", "arm_", "head_"))
    ]
    if not body_lumas or min(body_lumas) < 50:
        issues.append("body/head colors should be visible dark slate, not near-black")

    if issues:
        print(f"FAIL scp-106 ({len(issues)} issue(s)):")
        for issue in issues:
            print(f"  - {issue}")
        return 1

    print(
        "PASS scp-106  "
        f"{stats['visible']} visible primitives, {stats['markers']} markers, "
        f"{stats['colors']} colors. Bounds y={min_y:.2f}..{max_y:.2f}."
    )
    return 0

if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Geometry contract for the SCP-096 model. Run: python tests/models/test_scp_096_model.py"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_model_contract import FLAGS_VISIBLE, load, validate  # noqa: E402

PRIM_CUBE = 3
FACE = "#2A211C"


def visible_blocks(doc: dict) -> list[dict]:
    return [
        block for block in doc["Blocks"]
        if block.get("BlockType") == 1
        and block["Properties"].get("PrimitiveFlags") == FLAGS_VISIBLE
    ]


def named(blocks: list[dict], prefix: str) -> list[dict]:
    return [block for block in blocks if block["Name"].startswith(prefix)]


def aabb(block: dict) -> tuple[float, float, float, float, float, float]:
    pos = block["Position"]
    scale = block["Scale"]
    return (
        pos["x"] - scale["x"] / 2.0,
        pos["y"] - scale["y"] / 2.0,
        pos["z"] - scale["z"] / 2.0,
        pos["x"] + scale["x"] / 2.0,
        pos["y"] + scale["y"] / 2.0,
        pos["z"] + scale["z"] / 2.0,
    )


def validate_shape() -> list[str]:
    doc = load("scp-096")
    blocks = visible_blocks(doc)
    boxes = {block["Name"]: aabb(block) for block in blocks}
    issues: list[str] = []

    def check(condition: bool, message: str) -> None:
        if not condition:
            issues.append(message)

    for block in blocks:
        check(block["Properties"].get("PrimitiveType") == PRIM_CUBE,
              f"{block['Name']} should be a cube primitive for the simple PMER icon")

    min_x = min(bounds[0] for bounds in boxes.values())
    min_y = min(bounds[1] for bounds in boxes.values())
    min_z = min(bounds[2] for bounds in boxes.values())
    max_x = max(bounds[3] for bounds in boxes.values())
    max_y = max(bounds[4] for bounds in boxes.values())
    max_z = max(bounds[5] for bounds in boxes.values())

    check(abs(min_y) <= 0.01, f"visible base should sit at y=0, got {min_y:.3f}")
    check(2.85 <= max_y <= 3.0, f"096 should be tall and fit the 3m test cap, got {max_y:.3f}")
    check(max_x - min_x >= 0.95, f"long arms should create a readable width, got {max_x - min_x:.3f}")
    check(max_z - min_z >= 0.34, f"body should have real depth, got {max_z - min_z:.3f}")

    torso = named(blocks, "torso")
    check(len(torso) == 1, "expected one torso block")
    if torso:
        scale = torso[0]["Scale"]
        check(scale["x"] >= 0.44 and scale["z"] >= 0.32,
              f"torso should be gaunt but not a flat stick, got {scale}")

    arms = named(blocks, "arm")
    hands = named(blocks, "hand")
    check(len(arms) == 2, f"expected two long arms, got {len(arms)}")
    check(len(hands) == 2, f"expected two hands, got {len(hands)}")
    if arms:
        arm_boxes = [aabb(block) for block in arms]
        check(min(bounds[1] for bounds in arm_boxes) <= 0.55,
              "arms should hang below the knees")
        check(min(block["Scale"]["y"] for block in arms) >= 1.8,
              "arms should be visibly long")
        check(max(bounds[4] for bounds in arm_boxes) >= 2.45,
              "arms should reach into the shoulder area")
    if hands:
        hand_boxes = [aabb(block) for block in hands]
        check(min(bounds[1] for bounds in hand_boxes) <= 0.32,
              "hands should hang low near the shins")

    head = named(blocks, "head")
    mouth = named(blocks, "mouth")
    check(len(head) == 1, "expected one small head block")
    check(len(mouth) == 1, "expected one dark mouth/face accent")
    if head:
        hscale = head[0]["Scale"]
        check(max(hscale["x"], hscale["y"], hscale["z"]) <= 0.32,
              f"head should stay small, got {hscale}")
    if head and mouth:
        head_front = aabb(head[0])[5]
        mouth_back = aabb(mouth[0])[2]
        mouth_front = aabb(mouth[0])[5]
        check(mouth[0]["Properties"].get("Color", "").upper() == FACE,
              "mouth/face accent should be dark")
        check(mouth_back < head_front < mouth_front,
              "mouth should be embedded into and proud of the +Z-facing head front")

    return issues


def main() -> int:
    issues, stats = validate(
        "scp-096",
        min_visible=10,
        max_visible=14,
        required_markers=("marker_pivot", "marker_head"),
        min_y=-0.05,
        max_y=3.0,
        max_radius=0.6,
        min_distinct_colors=3,
    )
    issues.extend(validate_shape())

    if issues:
        print(f"FAIL scp-096 ({len(issues)} issue(s)):")
        for issue in issues:
            print(f"  - {issue}")
        return 1

    print(
        f"PASS scp-096  {stats['visible']} visible primitives, "
        f"{stats['markers']} markers, {stats['colors']} colors; silhouette checks passed."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

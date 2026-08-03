#!/usr/bin/env python3
"""Geometry contract for the SCP-173 model. Run: python tests/models/test_scp_173_model.py"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_model_contract import FLAGS_VISIBLE, _world_aabb, load, validate  # noqa: E402

PRIM_CUBE = 3
EPS = 1e-4
EYE_GREEN = "#32C766"


def aabb(block: dict) -> tuple[float, float, float, float, float, float]:
    return _world_aabb(block)


def require(issues: list[str], condition: bool, message: str) -> None:
    if not condition:
        issues.append(message)


def visible_blocks(doc: dict) -> list[dict]:
    return [
        b for b in doc["Blocks"]
        if b.get("BlockType") == 1 and b["Properties"].get("PrimitiveFlags") == FLAGS_VISIBLE
    ]


def with_prefix(blocks: list[dict], prefix: str) -> list[dict]:
    return [b for b in blocks if b["Name"].startswith(f"{prefix}_")]


def one(blocks: list[dict], prefix: str) -> dict:
    found = with_prefix(blocks, prefix)
    assert len(found) == 1
    return found[0]


if __name__ == "__main__":
    issues, stats = validate(
        "scp-173",
        min_visible=11,
        max_visible=11,
        required_markers=("marker_pivot", "marker_head"),
        min_y=-0.05,
        max_y=2.0,
        max_radius=0.95,
        min_distinct_colors=4,
    )
    doc = load("scp-173")
    visible = visible_blocks(doc)

    for block in visible:
        require(issues, block["Properties"].get("PrimitiveType") == PRIM_CUBE,
                f"{block['Name']} should be a cube for the blocky icon silhouette")
        rot = block["Rotation"]
        if block["Name"].startswith(("left_arm_", "right_arm_")):
            require(issues, abs(rot["x"] - 90.0) < EPS and abs(rot["y"]) < EPS and abs(rot["z"]) < EPS,
                    f"{block['Name']} should be rotated 90 degrees forward around X")
        else:
            require(issues, all(abs(rot[axis]) < EPS for axis in ("x", "y", "z")),
                    f"{block['Name']} should be axis-aligned")

    expected_counts = {
        "left_leg": 1,
        "right_leg": 1,
        "torso": 1,
        "shoulders": 1,
        "left_arm": 1,
        "right_arm": 1,
        "head": 1,
        "face": 1,
        "eye": 2,
        "mouth": 1,
    }
    for prefix, expected in expected_counts.items():
        found = with_prefix(visible, prefix)
        require(issues, len(found) == expected,
                f"expected {expected} visible {prefix} block(s), got {len(found)}")

    if not issues:
        left_leg = aabb(one(visible, "left_leg"))
        right_leg = aabb(one(visible, "right_leg"))
        torso = aabb(one(visible, "torso"))
        shoulders = aabb(one(visible, "shoulders"))
        left_arm = aabb(one(visible, "left_arm"))
        right_arm = aabb(one(visible, "right_arm"))
        head = aabb(one(visible, "head"))
        face = aabb(one(visible, "face"))
        mouth = aabb(one(visible, "mouth"))
        eye_blocks = with_prefix(visible, "eye")
        eyes = [aabb(b) for b in eye_blocks]
        min_y = min(aabb(b)[1] for b in visible)

        require(issues, abs(min_y) <= EPS, f"lowest visible extent should be y=0, got {min_y:.4f}")
        require(issues, left_leg[3] < -0.04 and right_leg[0] > 0.04,
                "legs should be split into left and right supports")
        require(issues, torso[1] < left_leg[4] and torso[1] < right_leg[4],
                "torso should overlap the legs instead of sitting as a separate box")
        require(issues, left_arm[3] < torso[0] and right_arm[0] > torso[3],
                "arms should sit outside the torso")
        require(issues, left_arm[2] < shoulders[5] and right_arm[2] < shoulders[5],
                "forward arms should start inside the shoulder mass")
        require(issues, left_arm[5] > face[5] + 0.25 and right_arm[5] > face[5] + 0.25,
                "forward arms should project well past the face toward +Z")
        require(issues, left_arm[4] <= shoulders[4] and right_arm[4] <= shoulders[4],
                "forward arms should stay below the shoulder top")
        require(issues, shoulders[1] < torso[4] and shoulders[4] > torso[4],
                "shoulders should bridge over the torso")
        require(issues, head[1] < shoulders[4] and head[4] > shoulders[4],
                "head should overlap and rise above the shoulders")
        require(issues, face[2] < head[5] and face[5] > head[5] + 0.05,
                "rust-red face should overlap the head and protrude toward +Z")
        require(issues, min(eye[2] for eye in eyes) < face[5] and max(eye[5] for eye in eyes) > face[5],
                "eye marks should overlap and protrude from the red face")
        require(issues, all(b["Properties"]["Color"].upper() == EYE_GREEN for b in eye_blocks),
                "eye marks should be green")
        require(issues, mouth[2] < face[5] and mouth[5] > face[5],
                "mouth mark should overlap and protrude from the red face")
        require(issues, mouth[4] < min(eye[1] for eye in eyes),
                "mouth should sit below both eye marks")

    if issues:
        print(f"FAIL scp-173 ({len(issues)} issue(s)):")
        for issue in issues:
            print(f"  - {issue}")
        sys.exit(1)

    print(f"PASS scp-173  {stats['visible']} visible primitives, "
          f"{stats['markers']} markers, {stats['colors']} colors.")

#!/usr/bin/env python3
"""Geometry contract for the SCP-3114 model. Run: python tests/models/test_scp_3114_model.py"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_model_contract import FLAGS_VISIBLE, load, validate  # noqa: E402

PRIM_SPHERE = 0
PRIM_CYLINDER = 2
PRIM_CUBE = 3


def _visible_blocks() -> list[dict]:
    doc = load("scp-3114")
    return [
        block
        for block in doc["Blocks"]
        if block.get("Properties", {}).get("PrimitiveFlags") == FLAGS_VISIBLE
    ]


def _prefixed(blocks: list[dict], prefix: str) -> list[dict]:
    return [block for block in blocks if block["Name"].startswith(prefix)]


def _ptype(block: dict) -> int:
    return block["Properties"]["PrimitiveType"]


def _color(block: dict) -> str:
    return block["Properties"]["Color"].upper()


if __name__ == "__main__":
    issues, stats = validate(
        "scp-3114",
        min_visible=18,
        max_visible=18,
        required_markers=("marker_pivot", "marker_head"),
        min_y=-0.05,
        max_y=2.5,
        max_radius=0.45,
        min_distinct_colors=3,
    )

    blocks = _visible_blocks()

    def check(cond: bool, msg: str) -> None:
        if not cond:
            issues.append(msg)

    skull = _prefixed(blocks, "skull")
    eyes = _prefixed(blocks, "eye_socket")
    jaw = _prefixed(blocks, "jaw")
    ribs = _prefixed(blocks, "rib_")
    spine = _prefixed(blocks, "spine")
    pelvis = _prefixed(blocks, "pelvis")
    legs = _prefixed(blocks, "leg")
    feet = _prefixed(blocks, "foot")
    arms = _prefixed(blocks, "arm")
    hands = _prefixed(blocks, "hand")
    neck = _prefixed(blocks, "neck")

    check(len(skull) == 1 and _ptype(skull[0]) == PRIM_SPHERE, "needs one sphere skull")
    check(len(eyes) == 2 and all(_ptype(eye) == PRIM_SPHERE for eye in eyes),
          "needs two sphere eye sockets")
    check(all(_color(eye) == "#1E1A16" for eye in eyes), "eye sockets must be dark")
    check(len(jaw) == 1 and _ptype(jaw[0]) == PRIM_CUBE, "needs one cube jaw")
    check(len(ribs) == 3 and all(_ptype(rib) == PRIM_CUBE for rib in ribs),
          "needs three separated cube rib bars")
    check(len(spine) == 1 and _ptype(spine[0]) == PRIM_CYLINDER, "needs one cylinder spine")
    check(len(pelvis) == 1 and _ptype(pelvis[0]) == PRIM_CUBE, "needs one cube pelvis")
    check(len(legs) == 2 and all(_ptype(leg) == PRIM_CYLINDER for leg in legs),
          "needs two cylinder leg bones")
    check(len(feet) == 2 and all(_ptype(foot) == PRIM_CUBE for foot in feet),
          "needs two cube feet")
    check(len(arms) == 2 and all(_ptype(arm) == PRIM_CYLINDER for arm in arms),
          "needs two cylinder arm bones")
    check(len(hands) == 2 and all(_ptype(hand) == PRIM_CUBE for hand in hands),
          "needs two cube hands")
    check(len(neck) == 1 and _ptype(neck[0]) == PRIM_CYLINDER, "needs one cylinder neck")

    if len(skull) == 1 and len(eyes) == 2:
        check(all(eye["Position"]["z"] > skull[0]["Position"]["z"] for eye in eyes),
              "eye sockets must sit on the +Z face")
        check(all(abs(eye["Position"]["x"]) > 0.04 for eye in eyes),
              "eye sockets must be visibly separated")
    if len(jaw) == 1 and len(skull) == 1:
        check(jaw[0]["Position"]["z"] > skull[0]["Position"]["z"], "jaw must face +Z")
        check(jaw[0]["Position"]["y"] < skull[0]["Position"]["y"], "jaw must sit below skull center")
    if len(ribs) == 3:
        rib_ys = sorted(rib["Position"]["y"] for rib in ribs)
        rib_widths = sorted(rib["Scale"]["x"] for rib in ribs)
        check(rib_ys[2] - rib_ys[0] >= 0.25, "rib bars need vertical separation")
        check(rib_widths[2] - rib_widths[0] >= 0.12, "rib bars need a broad chest silhouette")
    if len(feet) == 2 and len(legs) == 2:
        check(all(foot["Position"]["z"] > 0.08 for foot in feet), "feet must project forward")
        check(max(foot["Position"]["y"] for foot in feet) < min(leg["Position"]["y"] for leg in legs),
              "feet must sit below leg centers")
    if len(arms) == 2 and len(legs) == 2:
        check(
            min(abs(arm["Position"]["x"]) for arm in arms)
            > max(abs(leg["Position"]["x"]) for leg in legs) + 0.15,
            "arms must be outside the legs for a readable skeleton silhouette",
        )

    if issues:
        print(f"FAIL scp-3114 ({len(issues)} issue(s)):")
        for issue in issues:
            print(f"  - {issue}")
        sys.exit(1)
    print(
        f"PASS scp-3114  {stats['visible']} visible primitives, "
        f"{stats['markers']} markers, {stats['colors']} colors; skeleton silhouette checks passed."
    )

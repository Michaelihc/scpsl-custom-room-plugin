#!/usr/bin/env python3
"""Geometry contract for the SCP-049 model. Run: python tests/models/test_scp_049_model.py"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_model_contract import FLAGS_VISIBLE, load, validate  # noqa: E402

ROBE = "#2E2E37"
MASK = "#D9CDB0"
LENS = "#0C0C10"
HAT = "#17171C"


def visible_blocks() -> list[dict]:
    return [
        block for block in load("scp-049")["Blocks"]
        if block.get("BlockType") == 1 and block["Properties"].get("PrimitiveFlags") == FLAGS_VISIBLE
    ]


def by_prefix(blocks: list[dict], prefix: str) -> list[dict]:
    return [block for block in blocks if block["Name"].startswith(prefix)]


def only(blocks: list[dict], prefix: str, issues: list[str]) -> dict | None:
    matches = by_prefix(blocks, prefix)
    if len(matches) != 1:
        issues.append(f"expected exactly one {prefix}, got {len(matches)}")
        return None
    return matches[0]


def check_049_readability(blocks: list[dict], issues: list[str]) -> None:
    def check(condition: bool, message: str) -> None:
        if not condition:
            issues.append(message)

    for prefix in ("robe_skirt", "robe_body", "shoulders", "neck", "mask_head", "beak", "hat_brim", "hat_crown"):
        only(blocks, prefix, issues)
    check(len(by_prefix(blocks, "sleeve")) == 2, "expected two hanging sleeves")
    check(len(by_prefix(blocks, "lens")) == 2, "expected two dark eye lenses")

    skirt = only(blocks, "robe_skirt", issues)
    body = only(blocks, "robe_body", issues)
    shoulders = only(blocks, "shoulders", issues)
    mask = only(blocks, "mask_head", issues)
    beak = only(blocks, "beak", issues)
    brim = only(blocks, "hat_brim", issues)
    crown = only(blocks, "hat_crown", issues)
    if not all((skirt, body, shoulders, mask, beak, brim, crown)):
        return

    palette = {block["Properties"]["Color"].upper() for block in blocks}
    for color in (ROBE, MASK, LENS, HAT):
        check(color in palette, f"missing expected 049 color {color}")

    check(skirt["Scale"]["x"] > body["Scale"]["x"], "robe skirt should be wider than the body")
    check(shoulders["Scale"]["x"] > body["Scale"]["x"], "shoulders should widen the robe silhouette")
    check(brim["Scale"]["x"] > mask["Scale"]["x"] * 1.8, "hat brim should read wider than the mask")
    check(crown["Position"]["y"] > brim["Position"]["y"], "hat crown should sit above the brim")

    mask_front_z = mask["Position"]["z"] + mask["Scale"]["z"] / 2
    check(beak["Rotation"]["x"] < -45.0, "beak should angle forward and down toward +Z")
    check(beak["Position"]["z"] > mask_front_z + 0.15, "beak should project clearly forward from the mask")
    check(beak["Position"]["y"] < mask["Position"]["y"], "beak should sit below the eye line")

    lenses = by_prefix(blocks, "lens")
    check(all(lens["Properties"]["Color"].upper() == LENS for lens in lenses), "eye lenses should be dark")
    check(all(lens["Position"]["z"] > mask_front_z for lens in lenses), "eye lenses should sit proud of the +Z mask face")


if __name__ == "__main__":
    issues, stats = validate(
        "scp-049",
        min_visible=8,
        max_visible=14,
        required_markers=("marker_pivot", "marker_head"),
        min_y=-0.05,
        max_y=3.1,
        max_radius=0.95,
        min_distinct_colors=3,
    )
    check_049_readability(visible_blocks(), issues)
    if issues:
        print(f"FAIL scp-049 ({len(issues)} issue(s)):")
        for issue in issues:
            print(f"  - {issue}")
        sys.exit(1)
    print(f"PASS scp-049  {stats['visible']} visible primitives, {stats['markers']} markers, {stats['colors']} colors.")
    sys.exit(0)

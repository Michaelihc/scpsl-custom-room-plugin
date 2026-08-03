#!/usr/bin/env python3
"""Geometry contract for the Aim Range weapon rack. Run: python tests/models/test_aim_weapon_rack_model.py

Enforces the shared contract (unique IDs/hierarchy, positive scales, grounded bounds, palette,
coplanar overlap) plus the rack-specific rules: the exact unique marker set, every shelf-slot marker
inside the bounds markers with real gun clearance above each shelf board, and a restrained brand-compatible
navy/teal palette. The racks are mirrored into place at runtime from AimRangeLayout.ShelfAnchors:
local (x, y, z) here maps to world (door-relative x, floor + y, door-relative z) under +/-90 yaw.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_model_contract import FLAGS_NONE, FLAGS_VISIBLE, _world_aabb, load, validate  # noqa: E402

EPS = 1e-4
SLOT_MARKERS = tuple(f"marker_shelf_{i}" for i in range(6))
REQUIRED_MARKERS = ("marker_pivot", "marker_bounds_min", "marker_bounds_max") + SLOT_MARKERS
# Runtime anchor mapping (AimRangeLayout.ShelfAnchors, one rack instance per side): symmetric
# columns so the same asset serves both wall yaws. Per side, the runtime uses the three markers
# whose authored local column lands on that side's world anchors; the mirrored three are spare.
EXPECTED_SLOTS = {
    "marker_shelf_0": (-0.80, 1.38, 0.05),  # low, column A
    "marker_shelf_1": (-0.80, 2.10, 0.05),  # tall, column A (0.08 below the 2.15 grip line)
    "marker_shelf_2": (0.80, 1.38, 0.05),   # low, column B
    "marker_shelf_3": (0.80, 2.10, 0.05),   # tall, column B
    "marker_shelf_4": (0.80, 1.38, 0.05),   # low, column B (mirror of 2)
    "marker_shelf_5": (-0.80, 2.10, 0.05),  # tall, column A (mirror of 1)
}

# Brand palette (matches tools/build_aim_weapon_rack_asset.py)
NAVIES = {"#1A2130", "#242E42", "#1F2938", "#2E3A50", "#3A465C"}
TEALS = {"#33EEDA"}


def blocks_by_flag(doc: dict, flag: int) -> list[dict]:
    return [
        b for b in doc["Blocks"]
        if b.get("BlockType") == 1 and b["Properties"].get("PrimitiveFlags") == flag
    ]


def require(issues: list[str], condition: bool, message: str) -> None:
    if not condition:
        issues.append(message)


if __name__ == "__main__":
    issues, stats = validate(
        "aim-weapon-rack",
        min_visible=20,
        max_visible=36,
        required_markers=REQUIRED_MARKERS,
        min_y=-0.05,
        max_y=2.60,
        max_radius=1.20,
        min_distinct_colors=5,
    )
    doc = load("aim-weapon-rack")
    visible = blocks_by_flag(doc, FLAGS_VISIBLE)
    markers = blocks_by_flag(doc, FLAGS_NONE)

    # Exact unique marker set: no missing, no duplicates, no extras.
    marker_names = [b["Name"] for b in markers]
    require(issues, len(marker_names) == len(set(marker_names)),
            f"marker names must be unique, got {sorted(marker_names)}")
    require(issues, set(marker_names) == set(REQUIRED_MARKERS),
            f"marker set must be exactly {sorted(REQUIRED_MARKERS)}, got {sorted(set(marker_names))}")

    marker_pos = {b["Name"]: b["Position"] for b in markers}

    if not issues:
        pivot = marker_pos["marker_pivot"]
        require(issues, abs(pivot["x"]) < EPS and abs(pivot["y"]) < EPS and abs(pivot["z"]) < EPS,
                f"marker_pivot must sit at the local origin, got {pivot}")

        lo = marker_pos["marker_bounds_min"]
        hi = marker_pos["marker_bounds_max"]
        require(issues, all(lo[a] < hi[a] for a in ("x", "y", "z")),
                f"bounds min {lo} must be strictly below bounds max {hi}")
        require(issues, abs(lo["y"]) < 1e-3, f"bounds min should be grounded at y=0, got {lo['y']}")

        # Hard bounds: every visible primitive's AABB must fit inside the authored bounds markers.
        for b in visible:
            box = _world_aabb(b)
            for axis, (i_lo, i_hi) in zip(("x", "y", "z"), ((0, 3), (1, 4), (2, 5))):
                require(issues, box[i_lo] >= lo[axis] - 1e-3 and box[i_hi] <= hi[axis] + 1e-3,
                        f"{b['Name']} escapes bounds on {axis}: {box[i_lo]:.3f}..{box[i_hi]:.3f} "
                        f"vs {lo[axis]}..{hi[axis]}")

        # Slot markers inside bounds, above ground, and exactly on the runtime anchor mapping.
        for name in SLOT_MARKERS:
            p = marker_pos[name]
            for axis in ("x", "y", "z"):
                require(issues, lo[axis] - 1e-3 <= p[axis] <= hi[axis] + 1e-3,
                        f"{name} escapes bounds on {axis}: {p[axis]} not in {lo[axis]}..{hi[axis]}")
            require(issues, p["y"] > 0.5, f"{name} should be above the floor zone, got y={p['y']}")
            expect = EXPECTED_SLOTS[name]
            err = max(abs(p[a] - expect[i]) for i, a in enumerate(("x", "y", "z")))
            require(issues, err < 0.02,
                    f"{name} should sit at the runtime anchor {expect}, got "
                    f"({p['x']}, {p['y']}, {p['z']}) err={err:.3f}")

        # Gun clearance: every slot marker needs free vertical space before the next visible
        # surface above it — 0.30 m for low boards (next board above), 0.24 m for the tall board
        # (only the rack crown above; an E11-SR receiver is ~0.20 m tall).
        clearances = {name: (0.24 if EXPECTED_SLOTS[name][1] > 2.0 else 0.30) for name in SLOT_MARKERS}
        for name in SLOT_MARKERS:
            p = marker_pos[name]
            clearance = 10.0
            for b in visible:
                box = _world_aabb(b)
                if box[0] - 1e-3 <= p["x"] <= box[3] + 1e-3 and box[2] - 1e-3 <= p["z"] <= box[5] + 1e-3:
                    if box[1] > p["y"] + 1e-3:
                        clearance = min(clearance, box[1] - p["y"])
            require(issues, clearance >= clearances[name],
                    f"{name} has only {clearance:.2f} m vertical clearance (< {clearances[name]} m)")

        # Two cradle furniture groups per slot must exist near each marker.
        for name in SLOT_MARKERS:
            p = marker_pos[name]
            near = [b for b in visible
                    if abs(b["Position"]["x"] - p["x"]) < 0.3
                    and abs(b["Position"]["y"] - p["y"]) < 0.3
                    and abs(b["Position"]["z"] - p["z"]) < 0.3
                    and b["Name"].startswith(("cradle_pad_", "cradle_prong_"))]
            kinds = {b["Name"].split("_0")[0] for b in near}
            require(issues, "cradle_pad" in kinds and "cradle_prong" in kinds,
                    f"{name} should have a cradle pad and prong within reach, got {sorted(kinds)}")

        # Mirrored spare cradles: the furniture set must be symmetric across x=0 so the same asset
        # serves both wall yaws (markers stay one-sided and yaw-independent by construction).
        pads = sorted(round(b["Position"]["x"], 3) for b in visible if b["Name"].startswith("cradle_pad_"))
        require(issues, pads == sorted(-x for x in pads),
                f"cradle pads should mirror across x=0, got {pads}")

        # Brand palette: only approved navy/teal families. Gold was deliberately removed with the unexplained
        # floating header support so the guns remain the visual focus.
        palette = {b["Properties"].get("Color", "").upper() for b in visible}
        unknown = palette - NAVIES - TEALS
        require(issues, not unknown, f"off-brand colors: {sorted(unknown)}")
        require(issues, palette & NAVIES and palette & TEALS,
                f"palette should include navy and teal families, got {sorted(palette)}")

    if issues:
        print(f"FAIL aim-weapon-rack ({len(issues)} issue(s)):")
        for issue in issues:
            print(f"  - {issue}")
        sys.exit(1)

    print(f"PASS aim-weapon-rack  {stats['visible']} visible primitives, "
          f"{stats['markers']} markers, {stats['colors']} colors.")

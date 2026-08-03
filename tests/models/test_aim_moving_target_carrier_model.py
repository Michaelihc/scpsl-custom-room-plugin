#!/usr/bin/env python3
"""Geometry contract for the Aim Range moving-target carrier.
Run: python tests/models/test_aim_moving_target_carrier_model.py

Enforces the shared contract (unique IDs/hierarchy, positive scales, grounded bounds, palette,
coplanar overlap) plus carrier-specific rules: the exact unique marker set, the target mount inside
the bounds at the authored 1.5 m moving-path height, a compact center-bay footprint, and the
brand navy/teal/gold palette. The runtime slides this prop along X on the bounded moving path and
attaches the native ShootingTargetToy at marker_target_mount.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_model_contract import FLAGS_NONE, FLAGS_VISIBLE, _world_aabb, load, validate  # noqa: E402

EPS = 1e-4
REQUIRED_MARKERS = ("marker_pivot", "marker_bounds_min", "marker_bounds_max", "marker_target_mount")
MOUNT_Y = 1.50  # AimRangeLayout moving-path height

NAVIES = {"#1A2130", "#242E42", "#2E3A50"}
TEALS = {"#33EEDA", "#2BC7B8"}
GOLDS = {"#FFD24D"}


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
        "aim-moving-target-carrier",
        min_visible=12,
        max_visible=24,
        required_markers=REQUIRED_MARKERS,
        min_y=-0.05,
        max_y=2.00,
        max_radius=0.60,
        min_distinct_colors=5,
    )
    doc = load("aim-moving-target-carrier")
    visible = blocks_by_flag(doc, FLAGS_VISIBLE)
    markers = blocks_by_flag(doc, FLAGS_NONE)

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

        # Hard bounds: every visible primitive must fit inside the authored bounds markers.
        for b in visible:
            box = _world_aabb(b)
            for axis, (i_lo, i_hi) in zip(("x", "y", "z"), ((0, 3), (1, 4), (2, 5))):
                require(issues, box[i_lo] >= lo[axis] - 1e-3 and box[i_hi] <= hi[axis] + 1e-3,
                        f"{b['Name']} escapes bounds on {axis}: {box[i_lo]:.3f}..{box[i_hi]:.3f} "
                        f"vs {lo[axis]}..{hi[axis]}")

        # Target mount: inside bounds, at the authored moving-path height, on the carriage centerline.
        mount = marker_pos["marker_target_mount"]
        for axis in ("x", "y", "z"):
            require(issues, lo[axis] - 1e-3 <= mount[axis] <= hi[axis] + 1e-3,
                    f"marker_target_mount escapes bounds on {axis}")
        require(issues, abs(mount["y"] - MOUNT_Y) < 0.05,
                f"marker_target_mount should sit at the {MOUNT_Y} m path height, got {mount['y']}")
        require(issues, abs(mount["x"]) < 0.05,
                f"marker_target_mount should be on the carriage centerline, got x={mount['x']}")

        # The mount must have a visible backboard directly behind it (target visual support) and
        # clear space in front (+Z) for the native ShootingTargetToy face.
        behind = [b for b in visible
                  if b["Position"]["z"] < mount["z"] - 0.02
                  and abs(b["Position"]["y"] - mount["y"]) < 0.45
                  and abs(b["Position"]["x"] - mount["x"]) < 0.45]
        require(issues, any(b["Name"].startswith("head_board_") for b in behind),
                "marker_target_mount should have the head_board backboard behind it")
        for b in visible:
            box = _world_aabb(b)
            if box[1] < mount["y"] < box[4] and abs(b["Position"]["x"] - mount["x"]) < 0.20:
                require(issues, box[5] <= mount["z"] + 0.06,
                        f"{b['Name']} protrudes {box[5] - mount['z']:.2f} m in front of the target mount")

        # Compact bounded carriage: horizontal extents stay inside the 0.8 m center-bay lane budget.
        for b in visible:
            box = _world_aabb(b)
            require(issues, box[3] - box[0] <= 0.80 and box[5] - box[2] <= 0.80,
                    f"{b['Name']} is wider than the 0.8 m carriage budget")

        # Brand palette: only approved navy/teal/gold families, and all three families present.
        palette = {b["Properties"].get("Color", "").upper() for b in visible}
        unknown = palette - NAVIES - TEALS - GOLDS
        require(issues, not unknown, f"off-brand colors: {sorted(unknown)}")
        require(issues, palette & NAVIES and palette & TEALS and palette & GOLDS,
                f"palette should include navy, teal and gold families, got {sorted(palette)}")

    if issues:
        print(f"FAIL aim-moving-target-carrier ({len(issues)} issue(s)):")
        for issue in issues:
            print(f"  - {issue}")
        sys.exit(1)

    print(f"PASS aim-moving-target-carrier  {stats['visible']} visible primitives, "
          f"{stats['markers']} markers, {stats['colors']} colors.")

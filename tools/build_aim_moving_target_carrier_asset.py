#!/usr/bin/env python3
"""Aim Range moving-target carrier: the center-bay rail carriage that carries a native shooting target.

Authored y-up, base (rail contact) at y=0, facing +Z toward the firing line. The prop is a compact
gun-carriage: a low sled body that rides the floor rail, a navy mast, a gold mast collar, and a teal
chevron backboard behind the mount. The native ``ShootingTargetToy`` is attached by the runtime at
``marker_target_mount`` (front face of the mast head, 1.5 m above the rail — exactly the authored
moving-path height in AimRangeLayout), so the whole carrier slides along X on the bounded path.

Visual read: teal rail glow on the floor contact, navy carriage mass, gold accents, teal chevrons
framing the target. Structure is visual only — colliders stay in C# AddBox geometry.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_builder import Builder, standard_main  # noqa: E402

# Brand palette (source of truth: scpsl-plugins-metarepo/.server/server-identity-preview.html)
BODY = "#1A2130"        # fixture navy
BODY_L = "#242E42"      # lifted navy
TRIM = "#2E3A50"        # edge navy
TEAL = "#33EEDA"        # glowing teal seam
TEAL_SOFT = "#2BC7B8"   # secondary teal (chevrons)
GOLD = "#FFD24D"        # 莺歌傲然 cream/gold

MOUNT_Y = 1.50          # native target mount height (AimRangeLayout moving path y)


def build() -> Builder:
    b = Builder("aim_target_carrier_root")

    # Rail contact: a thin teal glow strip under the sled (sits on the floor, guides the eye).
    b.box("rail_glow", (0.0, 0.02, 0.0), (0.30, 0.04, 0.66), TEAL)

    # Sled: low wide carriage body with bevel steps and small wheel blocks at the corners.
    b.box("sled", (0.0, 0.105, 0.0), (0.62, 0.11, 0.56), BODY)
    b.box("sled_deck", (0.0, 0.19, 0.0), (0.50, 0.05, 0.46), BODY_L)
    for sx in (-1, 1):
        for sz in (-1, 1):
            b.cylinder("wheel", (0.26 * sx, 0.055, 0.22 * sz), (0.10, 0.045, 0.10), TRIM,
                       rot=(0, 0, 90))
    b.box("sled_glow_front", (0.0, 0.10, 0.30), (0.44, 0.05, 0.03), TEAL)

    # Mast: tapered navy column rising from the sled deck.
    b.box("mast_low", (0.0, 0.55, -0.04), (0.22, 0.75, 0.20), BODY)
    b.box("mast_collar", (0.0, 0.945, -0.04), (0.30, 0.06, 0.28), GOLD)
    b.box("mast_high", (0.0, 1.21, -0.04), (0.16, 0.50, 0.14), BODY)

    # Target head: backboard panel behind the mount point, teal chevrons framing it, gold crown dot.
    b.box("head_board", (0.0, MOUNT_Y, -0.10), (0.56, 0.50, 0.06), BODY_L)
    b.box("head_frame_top", (0.0, 1.81, -0.10), (0.66, 0.05, 0.10), TRIM)
    b.box("head_frame_bottom", (0.0, 1.19, -0.10), (0.66, 0.05, 0.10), TRIM)
    for s in (-1, 1):
        b.box("chevron", (0.335 * s, MOUNT_Y, -0.10), (0.05, 0.42, 0.08), TEAL_SOFT, rot=(0, 0, -12 * s))
    b.box("crown_dot", (0.0, 1.87, -0.10), (0.10, 0.05, 0.06), GOLD)
    # Mount plate: the native target visually bolts onto this gold pad on the board's front face.
    b.box("mount_plate", (0.0, MOUNT_Y, -0.05), (0.22, 0.22, 0.04), GOLD, rot=(0, 0, 45))

    b.marker("marker_pivot", (0, 0, 0))
    b.marker("marker_bounds_min", (-0.42, 0.0, -0.34))
    b.marker("marker_bounds_max", (0.42, 1.90, 0.34))
    b.marker("marker_target_mount", (0.0, MOUNT_Y, 0.0))
    return b


if __name__ == "__main__":
    raise SystemExit(standard_main("aim-moving-target-carrier", build))

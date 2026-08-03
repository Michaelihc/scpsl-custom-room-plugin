#!/usr/bin/env python3
"""Aim Range weapon rack: a 莺歌傲然 armory shelf unit in the navy/teal/gold gallery language.

Authored y-up, base at y=0, facing +Z. One rack instance serves one side wall and carries three
cradle slots matching AimRangeLayout.ShelfAnchors exactly: the rack pivot sits at the C# BuildShelf
strip center, so rack-local (x, y, z) maps to world (depth-from-door, height, along-wall) under
+/-90 yaw. The layout is deliberately asymmetric — a low+high pair at local x=+0.8 (runtime anchor
depth -2.0) and a single low slot at local x=-0.8 (depth -3.6) — which mirrors the staggered anchor
pattern and reads as a designed armory column instead of a uniform grid.

Each slot marker sits on a shelf board: ``marker_shelf_0`` low-right, ``marker_shelf_1`` high-right,
``marker_shelf_2`` low-left (matching runtime slot IDs 0-2 on the left rack, mirrored to 3-5 on the
right). Cradle furniture is a flat butt-rest pad + a short barrel cradle prong sized for pickupable
guns (FSP-9 to E11-SR lengths), floating clear of the board so kinematic pickups never clip.

The visual read: a restrained navy display monolith with teal edge and shelf lighting. The former
floating gold header rail and diamond badge were removed because they read as an unexplained gun support in-game.
Structure is visual only — colliders remain explicit C# AddBox geometry; the MER asset carries geometry + markers.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_builder import Builder, standard_main  # noqa: E402

# Brand palette (source of truth: scpsl-plugins-metarepo/.server/server-identity-preview.html)
BODY = "#1A2130"        # pedestal/fixture navy
BODY_L = "#242E42"      # lifted navy for back panel
SHELF = "#1F2938"       # shelf board navy
TRIM = "#2E3A50"        # cornice/edge navy
TEAL = "#33EEDA"        # glowing teal seam
CRADLE = "#3A465C"      # cradle pads/prongs (visible against boards)

# Rack envelope: 0.6 deep (into -Z), 2.2 wide, 2.5 tall. Face looks toward +Z.
# Pivot = C# BuildShelf strip center; anchors float 0.25 m in front of the strip face (+0.05 rack-local).
BACK_Z = -0.20           # back panel center (yaw-independent symmetric columns)
# Runtime ShelfAnchors per side: (slot, y, depthFromDoor) -> rack-local (slot, y, x):
#   0/3: (1.35, -2.0) low  |  1/4: (2.15, -2.0) high  |  2/5: (1.35, -3.6) low
# Both cradle columns are authored fully symmetric (low markers in both columns, tall markers in
# both columns) so the runtime can resolve markers by authored local position per side, independent
# of which way the rack instance is yawed: under left-wall yaw=-90 and right-wall yaw=+90 the two
# local columns land on opposite world depth columns, and the symmetric marker set covers both.
# Low markers sit at anchor + 0.03 pickup float. The tall anchor (y=2.15) is the long-gun grip
# line; its markers are authored 0.08 lower (board-supported receiver line) so an E11-SR clears
# the rack crown — the runtime keeps the anchor as the pickup pivot while the cradle carries it.
# Boards sit 0.155 below their markers.
SLOTS = ((0, -0.80, 1.38), (1, -0.80, 2.10), (2, 0.80, 1.38),
         (3, 0.80, 2.10), (4, 0.80, 1.38), (5, -0.80, 2.10))
BOARD_Y = {1.38: 1.225, 2.10: 1.945}


def build() -> Builder:
    b = Builder("aim_weapon_rack_root")

    # Monolith back panel + side stiles + cornice (all navy). Every touching pair is interleaved
    # (inset or offset by >= 5 mm) so no two axis-aligned blocks share an exact face plane.
    b.box("back_panel", (0.0, 1.25, BACK_Z), (2.10, 2.28, 0.10), BODY_L)
    for s in (-1, 1):
        b.box("side_stile", (1.02 * s, 1.28, 0.0), (0.14, 2.14, 0.60), BODY)
        # Teal edge strip on each stile's front face so the silhouette reads from the side too.
        b.box("stile_edge", (1.02 * s, 1.28, 0.32), (0.06, 2.12, 0.03), TEAL)
    b.box("cornice", (0.0, 2.43, 0.0), (2.20, 0.14, 0.62), TRIM)
    b.box("crown_glow", (0.0, 2.515, 0.0), (2.12, 0.04, 0.54), TEAL)
    b.box("toe_kick", (0.0, 0.10, 0.0), (2.14, 0.20, 0.58), BODY)
    b.box("toe_glow", (0.0, 0.235, 0.20), (2.00, 0.03, 0.16), TEAL)

    # Shelf boards at each used height with teal under-glow lips, then cradle furniture per slot.
    # Both boards span the full width (symmetric columns) so the rack mirrors cleanly under yaw.
    for y in sorted(BOARD_Y.values()):
        b.box("shelf_board", (0.0, y, 0.0), (1.96, 0.06, 0.56), SHELF)
        b.box("shelf_lip", (0.0, y - 0.065, 0.20), (1.88, 0.03, 0.14), TEAL)
    furniture = set()
    for slot, x, marker_y in SLOTS:
        pad_y = marker_y - 0.06        # gun receiver line: 0.125 above the board surface
        if (x, marker_y) not in furniture:  # mirrored markers share one cradle per column/height
            furniture.add((x, marker_y))
            # Butt-rest pad against the back panel (prong stands proud of it; no shared planes).
            b.box("cradle_pad", (x, pad_y, -0.11), (0.24, 0.05, 0.12), CRADLE)
            # Barrel cradle: a vertical prong fork post in front of the pad.
            b.box("cradle_prong", (x, pad_y + 0.055, 0.13), (0.05, 0.16, 0.06), CRADLE)
        # Slot marker: where the kinematic pickup is aligned by the runtime (exact anchor point).
        b.marker(f"marker_shelf_{slot}", (x, marker_y, 0.05))

    b.marker("marker_pivot", (0, 0, 0))
    b.marker("marker_bounds_min", (-1.10, 0.0, -0.32))
    b.marker("marker_bounds_max", (1.10, 2.54, 0.34))
    return b


if __name__ == "__main__":
    raise SystemExit(standard_main("aim-weapon-rack", build))

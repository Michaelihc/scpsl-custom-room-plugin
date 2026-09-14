#!/usr/bin/env python3
"""SCP-079 gallery asset: y-up, floor at zero, facing +Z.

Base component recipe below; star_gallery_models.remodel owns the final rounded
silhouette and pose revision without adding primitives. Both embedded models and
the authored star station consume this build() result.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_builder import Builder, standard_main  # noqa: E402

BEIGE = "#C7BD96"      # main beige plastic housing
BEIGE_D = "#A89C72"    # darker beige (base / shading)
BEZEL = "#20201E"      # dark screen bezel
GREEN = "#2BC24A"      # glowing green screen
RED = "#E0402E"        # red power LED


def build() -> Builder:
    b = Builder("scp079_root")

    # ----- Beige base: a chunky plinth that lifts the monitor to eye level ------------------
    # Sits on the floor (bottom at y=0). One big block + a slightly inset top cap for volume.
    b.box("base", (0, 0.30, 0.0), (1.10, 0.60, 0.80), BEIGE_D)
    # Top cap floats 0.04m proud of the base top (y=0.60) -> centered above it, no coplanar face.
    b.box("base_top", (0, 0.65, 0.02), (0.98, 0.08, 0.70), BEIGE)

    # ----- Monitor housing: one big beige box --------------------------------------------------
    # Bottom of the monitor (y=0.74) overlaps INTO the base cap (top y=0.69) by 0.05m: no shared plane.
    MON_Y = 1.18                          # monitor center; bottom at 0.74, top at 1.62
    b.box("monitor", (0, MON_Y, -0.04), (1.20, 0.88, 0.78), BEIGE)
    # A darker beige top cap, floated 0.03m above the monitor top for a clean shaded edge.
    b.box("monitor_top", (0, MON_Y + 0.47, -0.04), (1.06, 0.10, 0.72), BEIGE_D)

    # ----- Dark bezel: stands proud of the monitor front (front face at z=+0.39) ---------------
    # Monitor front face sits at z = -0.04 + 0.78/2 = 0.35. Bezel center at z=0.44 so its back
    # face (z=0.40) is in FRONT of the monitor front (0.35) -> overlap, never coplanar.
    BEZEL_Z = 0.44
    b.box("bezel", (0, MON_Y + 0.02, BEZEL_Z), (1.02, 0.74, 0.08), BEZEL)

    # ----- Glowing green screen: inset into the bezel, standing slightly proud ------------------
    # Bezel front face at z = 0.44 + 0.04 = 0.48. Screen front at 0.52 -> proud, no coplanar face.
    SCR_Y = MON_Y + 0.04
    b.box("screen", (0, SCR_Y, 0.50), (0.72, 0.52, 0.05), GREEN)

    # ----- Single small red power LED on the bezel, lower-right ---------------------------------
    # Bezel front face at z=0.48; LED front at 0.52 (proud of bezel, beside the screen).
    b.box("led", (0.40, MON_Y - 0.30, 0.50), (0.07, 0.05, 0.05), RED)

    b.marker("marker_pivot", (0, 0, 0))
    b.marker("marker_screen", (0, SCR_Y, 0.53))
    from star_gallery_models import remodel
    return remodel(b, "079")


if __name__ == "__main__":
    raise SystemExit(standard_main("scp-079", build))

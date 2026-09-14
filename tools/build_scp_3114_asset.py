#!/usr/bin/env python3
"""SCP-3114 gallery asset: y-up, floor at zero, facing +Z.

Base component recipe below; star_gallery_models.remodel owns the final rounded
silhouette and pose revision without adding primitives. Both embedded models and
the authored star station consume this build() result.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_builder import Builder, standard_main  # noqa: E402

BONE = "#ECE6D8"     # bone white
BONE_D = "#C3B79E"   # shaded bone
DARK = "#1E1A16"     # dark eye sockets


def build() -> Builder:
    b = Builder("scp3114_root")

    # Legs and feet: cylinder scale.y is half-height for ProjectMER/Unity cylinders.
    for s in (-1, 1):
        x = 0.14 * s
        b.box("foot", (x, 0.055, 0.13), (0.16, 0.11, 0.32), BONE_D)
        b.cylinder("leg", (x, 0.57, 0.0), (0.08, 0.48, 0.08), BONE)

    b.box("pelvis", (0, 1.16, 0.0), (0.42, 0.18, 0.20), BONE_D)
    b.cylinder("spine", (0, 1.53, -0.04), (0.06, 0.32, 0.06), BONE_D)

    # Three separated bars read as ribs without a high primitive count or hidden coplanar faces.
    b.box("rib_lower", (0, 1.43, 0.04), (0.42, 0.055, 0.11), BONE)
    b.box("rib_middle", (0, 1.58, 0.06), (0.50, 0.055, 0.12), BONE)
    b.box("rib_upper", (0, 1.73, 0.05), (0.58, 0.055, 0.13), BONE)

    for s in (-1, 1):
        ax = 0.35 * s
        b.cylinder("arm", (ax, 1.40, 0.04), (0.06, 0.36, 0.06), BONE)
        b.box("hand", (ax + 0.01 * s, 1.005, 0.07), (0.10, 0.11, 0.06), BONE_D)

    b.cylinder("neck", (0, 1.92, -0.02), (0.065, 0.12, 0.065), BONE_D)
    b.sphere("skull", (0, 2.18, 0.0), 0.36, BONE)
    b.box("jaw", (0, 2.03, 0.08), (0.22, 0.11, 0.17), BONE_D)
    for s in (-1, 1):
        b.sphere("eye_socket", (0.085 * s, 2.20, 0.155), 0.115, DARK)

    b.marker("marker_pivot", (0, 0, 0))
    b.marker("marker_head", (0, 2.18, 0))
    from star_gallery_models import remodel
    return remodel(b, "3114")


if __name__ == "__main__":
    raise SystemExit(standard_main("scp-3114", build))

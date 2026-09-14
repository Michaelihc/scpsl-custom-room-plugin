#!/usr/bin/env python3
"""SCP-173 gallery asset: y-up, floor at zero, facing +Z.

Base component recipe below; star_gallery_models.remodel owns the final rounded
silhouette and pose revision without adding primitives. Both embedded models and
the authored star station consume this build() result.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_builder import Builder, standard_main  # noqa: E402

CONCRETE = "#A89F90"      # main weathered concrete (tan-gray)
CONCRETE_L = "#B6AEA0"    # lighter worn concrete
CONCRETE_D = "#81796D"    # shaded concrete
RUST_RED = "#7A2D22"      # rust-red painted face
RUST_DARK = "#4B211D"     # darker paint marks
EYE_GREEN = "#32C766"     # bright green eye marks


def build() -> Builder:
    b = Builder("scp173_root")

    # Separate legs make the silhouette read as a humanoid while keeping the floor contact at y=0.
    b.box("left_leg", (-0.17, 0.38, 0.00), (0.22, 0.76, 0.28), CONCRETE_D)
    b.box("right_leg", (0.17, 0.38, 0.00), (0.22, 0.76, 0.28), CONCRETE_D)

    # Squat torso and shoulder block overlap the legs/body slightly without sharing exact face planes.
    b.box("torso", (0.00, 1.02, 0.00), (0.58, 0.72, 0.42), CONCRETE)
    b.box("shoulders", (0.00, 1.27, 0.04), (0.72, 0.28, 0.48), CONCRETE_L)

    # Forward block arms/hands, rotated up 90 degrees from the old hanging pose so they point +Z.
    b.box("left_arm", (-0.43, 1.02, 0.38), (0.22, 0.70, 0.24), CONCRETE_D, rot=(90, 0, 0))
    b.box("right_arm", (0.43, 1.02, 0.38), (0.22, 0.70, 0.24), CONCRETE_D, rot=(90, 0, 0))

    # Forward-leaning head and a proud rust-red face panel so the model clearly faces +Z.
    b.box("head", (0.00, 1.62, 0.08), (0.48, 0.52, 0.46), CONCRETE_L)
    b.box("face", (0.00, 1.65, 0.34), (0.36, 0.34, 0.07), RUST_RED)

    for s in (-1, 1):
        b.box("eye", (0.09 * s, 1.71, 0.392), (0.08, 0.08, 0.04), EYE_GREEN)
    b.box("mouth", (0.00, 1.56, 0.392), (0.24, 0.06, 0.04), RUST_DARK)

    b.marker("marker_pivot", (0, 0, 0))
    b.marker("marker_head", (0, 1.74, 0))
    from star_gallery_models import remodel
    return remodel(b, "173")


if __name__ == "__main__":
    raise SystemExit(standard_main("scp-173", build))

#!/usr/bin/env python3
"""SCP-096 gallery asset: y-up, floor at zero, facing +Z.

Base component recipe below; star_gallery_models.remodel owns the final rounded
silhouette and pose revision without adding primitives. Both embedded models and
the authored star station consume this build() result.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_builder import Builder, standard_main  # noqa: E402

SKIN = "#E6DDCE"      # pale ashen skin
SKIN_D = "#B8AE9C"    # shaded skin (joints, hands, feet)
FACE = "#2A211C"      # dark gaping mouth / face recess


def build() -> Builder:
    b = Builder("scp096_root")

    # Long thin legs + small forward feet. Feet sit on the floor; legs start slightly inside them
    # instead of sharing the same bottom plane.
    for s in (-1, 1):
        x = 0.18 * s
        b.box("foot", (x, 0.06, 0.12), (0.22, 0.12, 0.36), SKIN_D)
        b.box("leg", (x, 0.84, 0.0), (0.15, 1.52, 0.16), SKIN)

    # Pelvis + slim torso. The torso has real front-to-back depth so the model reads as a body,
    # not a flat pole, while staying visibly gaunt.
    b.box("hips", (0, 1.57, 0.02), (0.44, 0.20, 0.30), SKIN_D)
    b.box("torso", (0, 2.08, 0.02), (0.48, 0.90, 0.34), SKIN)
    b.box("shoulders", (0, 2.55, 0.03), (0.76, 0.20, 0.38), SKIN_D)

    # Signature arms: very long, thin, and hanging straight down past the knees.
    for s in (-1, 1):
        x = 0.43 * s
        b.box("arm", (x, 1.50, 0.11), (0.14, 1.94, 0.16), SKIN)
        b.box("hand", (x, 0.43, 0.16), (0.17, 0.25, 0.18), SKIN_D)

    # Small head with a single dark gaping mouth/face accent, proud of the +Z-facing front.
    b.box("neck", (0, 2.70, 0.01), (0.13, 0.18, 0.13), SKIN_D)
    b.box("head", (0, 2.84, 0.02), (0.30, 0.30, 0.30), SKIN)
    b.box("mouth", (0, 2.76, 0.20), (0.20, 0.17, 0.08), FACE)

    b.marker("marker_pivot", (0, 0, 0))
    b.marker("marker_head", (0, 2.84, 0.02))
    from star_gallery_models import remodel
    return remodel(b, "096")


if __name__ == "__main__":
    raise SystemExit(standard_main("scp-096", build))

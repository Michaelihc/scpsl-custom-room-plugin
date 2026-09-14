#!/usr/bin/env python3
"""SCP-049 gallery asset: y-up, floor at zero, facing +Z.

Base component recipe below; star_gallery_models.remodel owns the final rounded
silhouette and pose revision without adding primitives. Both embedded models and
the authored star station consume this build() result.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_builder import Builder, standard_main  # noqa: E402

ROBE = "#2E2E37"   # charcoal robe
MASK = "#D9CDB0"   # bone mask / beak
LENS = "#0C0C10"   # dark eye lenses
HAT = "#17171C"    # black hat


def build() -> Builder:
    b = Builder("scp049_root")

    # Robe: a wide oval skirt seated on the floor plus a narrower body block sunk into it.
    b.cylinder("robe_skirt", (0, 0.43, 0.0), (1.08, 0.43, 0.82), ROBE)
    b.box("robe_body", (0, 1.22, 0.0), (0.68, 1.02, 0.52), ROBE)

    # Strong shoulder line for the coat silhouette, overlapped into the torso.
    b.box("shoulders", (0, 1.75, 0.0), (0.9, 0.28, 0.54), ROBE)

    # Long hanging sleeves, pushed out past the shoulders so the outline reads from the front.
    for s in (-1, 1):
        b.box("sleeve", (0.51 * s, 1.22, 0.04), (0.23, 0.92, 0.36), ROBE)

    # Short dark neck joining shoulders to the pale mask.
    b.cylinder("neck", (0, 1.96, 0.02), (0.22, 0.10, 0.22), ROBE)

    # Pale bird mask, slightly forward of the body.
    head_y = 2.27
    b.box("mask_head", (0, head_y, 0.07), (0.44, 0.48, 0.42), MASK)

    # Dark round eye lenses, set proud of the +Z face.
    for s in (-1, 1):
        b.sphere("lens", (0.14 * s, head_y + 0.07, 0.32), 0.14, LENS)

    # Long forward-and-down beak. The back cap enters the mask; the low tip points along +Z.
    b.cylinder("beak", (0, head_y - 0.09, 0.52), (0.13, 0.30, 0.13), MASK, rot=(-65.0, 0.0, 0.0))

    # Wide-brim black hat: flat brim plus short crown, overlapped but not coplanar with the mask.
    b.cylinder("hat_brim", (0, head_y + 0.28, 0.0), (0.94, 0.07, 0.84), HAT)
    b.cylinder("hat_crown", (0, head_y + 0.49, 0.0), (0.42, 0.22, 0.42), HAT)

    b.marker("marker_pivot", (0, 0, 0))
    b.marker("marker_head", (0, head_y, 0))  # head_y kept in sync with mask_head above
    from star_gallery_models import remodel
    return remodel(b, "049")


if __name__ == "__main__":
    raise SystemExit(standard_main("scp-049", build))

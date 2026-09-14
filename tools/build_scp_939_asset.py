#!/usr/bin/env python3
"""SCP-939 gallery asset: y-up, floor at zero, facing +Z.

Base component recipe below; star_gallery_models.remodel owns the final rounded
silhouette and pose revision without adding primitives. Both embedded models and
the authored star station consume this build() result.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_builder import Builder, standard_main  # noqa: E402

SKIN = "#6E1F1F"     # blood-red skin (main mass)
SHADOW = "#3A1414"   # dark red shadow / underside / mouth recess
TEETH = "#EDE6D6"    # bone-white teeth

# Layout convention: head/mouth toward +Z (front), tail toward -Z (back).


def build() -> Builder:
    b = Builder("scp939_root")

    # ---- Body: one long, low barrel running front(+Z) to back(-Z). ----------------------------
    # Top of the back ~y=0.95; clearly horizontal so it never reads as a biped.
    b.box("body", (0, 0.62, -0.1), (0.66, 0.58, 1.7), SKIN)
    # Dark underside, narrower + tucked up inside the body so its side faces don't coplane.
    b.box("belly", (0, 0.34, -0.1), (0.5, 0.16, 1.5), SHADOW)

    # ---- Four short legs: one stubby box each, planted on the floor. ---------------------------
    # Front pair near z=+0.55, rear pair near z=-0.75. Tops poke up into the body (overlap).
    for s in (-1, 1):
        b.box("leg_front", (0.27 * s, 0.24, 0.55), (0.22, 0.5, 0.26), SHADOW)
        b.box("leg_rear", (0.27 * s, 0.24, -0.75), (0.22, 0.5, 0.26), SHADOW)

    # ---- Head: a thick blocky skull reaching forward, no eyes (smooth top). --------------------
    # Sits just above + ahead of the body; overlaps the body front by ~0.05m, no coplanar seam.
    b.box("head", (0, 0.78, 1.0), (0.62, 0.46, 0.62), SKIN)

    # ---- Maw: upper jaw lifted high, lower jaw dropped low, a wide dark gape between. -----------
    b.box("upper_jaw", (0, 1.0, 1.5), (0.56, 0.2, 0.46), SKIN)
    b.box("lower_jaw", (0, 0.42, 1.46), (0.54, 0.18, 0.42), SHADOW)
    # Dark gaping interior filling the gap (inset so it never coplanes with the jaws).
    b.box("maw", (0, 0.71, 1.32), (0.46, 0.5, 0.32), SHADOW)

    # ---- Teeth: two white rows ringing the open maw (top hanging down, bottom jutting up). ------
    b.box("teeth_top", (0, 0.83, 1.61), (0.52, 0.16, 0.1), TEETH)
    b.box("teeth_bot", (0, 0.56, 1.58), (0.5, 0.16, 0.1), TEETH)

    # ---- Tail: one tapering segment trailing back and down toward the floor. -------------------
    b.seg("tail", (0, 0.52, -1.0), (0, 0.18, -1.7), 0.26, SKIN, thick=0.26)

    b.marker("marker_pivot", (0, 0, 0))
    b.marker("marker_head", (0, 0.78, 1.2))
    from star_gallery_models import remodel
    return remodel(b, "939")


if __name__ == "__main__":
    raise SystemExit(standard_main("scp-939", build))

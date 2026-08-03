#!/usr/bin/env python3
"""SCP-106 "The Old Man": a LOW-POLY dark, hunched humanoid icon.

Authored y-up, base at y=0, facing +Z. The read is intentionally simple from a few meters away:
legs sinking into a tar/shadow base, a compact torso, a higher dark hunch, a low forward head with
sunken eye sockets, and two long hanging arms. The body is visible dark slate rather than pure black;
only the tar and eye recesses are near-black accents. All blocks overlap or have clear gaps, so no
visible faces are coplanar/z-fighting. 11 visible primitives.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_builder import Builder, standard_main  # noqa: E402

BODY = "#363B43"    # visible dark slate body, not pure black
HUNCH = "#242930"   # darker upper-back mass
HEAD = "#41464E"    # slightly lighter head so the silhouette reads
TAR = "#15181D"     # near-black base accent
EYE = "#0B0D10"     # sunken eye sockets


def build() -> Builder:
    b = Builder("scp106_root")

    # Thin tar/shadow slab: this owns the model's base at y=0; legs sink into it slightly.
    b.box("tar_shadow", (0, 0.03, 0.10), (1.08, 0.06, 0.92), TAR)

    # Two blocky legs, planted slightly apart and inset into the tar so their bottom faces do not
    # share the same plane as the base slab.
    for s in (-1, 1):
        b.box("leg", (0.15 * s, 0.45, 0.02), (0.18, 0.82, 0.22), BODY)

    # Hips and torso overlap vertically, avoiding touching planes while keeping a compact body.
    b.box("hips", (0, 0.88, 0.04), (0.54, 0.30, 0.40), BODY)
    b.box("torso", (0, 1.20, 0.16), (0.58, 0.70, 0.42), BODY)

    # The hunch is higher and farther back than the head. From the side, this reads as a stooped
    # upper back with the head slung forward.
    b.box("back_hunch", (0, 1.52, -0.04), (0.72, 0.46, 0.48), HUNCH)

    # Low, forward head under the hunch, with two proud dark eye sockets on the +Z face.
    b.box("head", (0, 1.39, 0.48), (0.42, 0.38, 0.38), HEAD)
    for s in (-1, 1):
        b.box("eye", (0.10 * s, 1.43, 0.69), (0.10, 0.11, 0.06), EYE)

    # Long hanging arms at the sides, forward of the hips so they do not read as extra legs.
    for s in (-1, 1):
        b.box("arm", (0.44 * s, 0.91, 0.18), (0.18, 0.88, 0.18), BODY)

    b.marker("marker_pivot", (0, 0, 0))
    b.marker("marker_head", (0, 1.39, 0.48))
    return b


if __name__ == "__main__":
    raise SystemExit(standard_main("scp-106", build))

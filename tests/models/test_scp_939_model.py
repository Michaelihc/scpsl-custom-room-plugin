#!/usr/bin/env python3
"""Geometry contract for the SCP-939 model. Run: python tests/models/test_scp_939_model.py"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_model_contract import run  # noqa: E402

if __name__ == "__main__":
    run(
        "scp-939",
        min_visible=10,
        max_visible=16,
        required_markers=("marker_pivot", "marker_head"),
        min_y=-0.05,
        max_y=1.45,
        max_radius=2.5,
        min_distinct_colors=3,
    )

#!/usr/bin/env python3
"""Geometry contract for the SCP-079 model. Run: python tests/models/test_scp_079_model.py"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from scp_model_contract import run  # noqa: E402

if __name__ == "__main__":
    run(
        "scp-079",
        min_visible=5,
        max_visible=14,
        required_markers=("marker_pivot", "marker_screen"),
        min_y=-0.05,
        max_y=2.0,
        max_radius=1.0,
        min_distinct_colors=4,
    )

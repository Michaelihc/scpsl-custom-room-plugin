#!/usr/bin/env python3
"""Build the angular collectible SCP-939 gallery figure."""
from scp_builder import standard_main
from star_gallery_models import build as build_figure


def build():
    return build_figure("939")


if __name__ == "__main__":
    raise SystemExit(standard_main("scp-939", build))

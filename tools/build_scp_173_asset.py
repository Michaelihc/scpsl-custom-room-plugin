#!/usr/bin/env python3
"""Build the wiki-referenced SCP:SL SCP-173 gallery figure."""
from scp_builder import standard_main
from star_gallery_models import build as build_figure


def build():
    return build_figure("173")


if __name__ == "__main__":
    raise SystemExit(standard_main("scp-173", build))

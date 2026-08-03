#!/usr/bin/env python3
"""Emit a selector-room .mer.json so the live layout can be inspected offline.

Mirrors SelectorRoom.cs: floor/walls/ceiling, a center-clear display layout, one pedestal + model + coin
per SCP, the real back-wall server logo, and a small proxy for the welcome-message TextToy. The logo's
Empty -> Quad hierarchy is preserved because its non-uniform parent transforms are what produce the
authored shears; flattening those child TRS values would collapse or distort the emblem.

The primitive renderer cannot draw TMP text, so ``message_proxy`` is only a placement guide for
``莺歌傲然 / 祝你玩得愉快　·　欢迎加入 QQ 群 860705092``. Render with render_model_preview.py.

Usage:
    python tools/build_room_preview.py            # writes generated/preview/room.mer.json
    python tools/render_model_preview.py --input generated/preview/room.mer.json \
        --output generated/preview/room_front.png --width 1600 --height 900 \
        --pos "0,3,-25" --look "0,2.2,3.5" --fov 72 --hide-prefix wall_front --exposure 2.2
"""
from __future__ import annotations

import json
import math
from pathlib import Path

# --------------------------------------------------------------------------------------------------
# THEME — edit these hex values to match the final retheme spec, then re-emit + re-render.
# --------------------------------------------------------------------------------------------------
THEME = {
    "floor":          "#293140",
    "floor_accent":   "#33EEDA",   # glowing teal seam for ground-plane legibility
    "wall":           "#242C3B",
    "ceiling":        "#1B2230",
    "pedestal":       "#1A2130",
    "placeholder":    "#5A6472",
    "coin":           "#FFD24D",
    "message":        "#FFE08A",
    # Aim Range shell (kept in lockstep with AimRangeWorld.cs)
    "fixture":        "#242C3B",
    "divider":        "#35445C",
    "backstop":       "#202735",
    "target":         "#D8DDE6",   # native ShootingTargetToy proxy
    "bot":            "#5A6472",   # RA-dummy silhouette proxy
}

# --- geometry (kept in lockstep with SelectorRoom.cs) ---------------------------------------------
FLOOR_TOP_Y = 0.0
SPAWN_Z = -3.0
ROW_Z = 2.5
FLOOR_CENTER_Z = -0.25
FLOOR_DEPTH = 9.0
FLOOR_THICKNESS = 0.4
WALL_HEIGHT = 5.0
WALL_THICKNESS = 0.3
PEDESTAL_HEIGHT = 1.0
PEDESTAL_DEPTH = 1.4
SPACING = 4.0
CENTER_CLEARANCE = 3.0
MODEL_SCALE = 1.0

LOGO_MODEL = "yingge-aoran-logo-opt"
LOGO_SCALE = 0.39
LOGO_SOURCE_CENTER = (-0.0039871843, 0.2910698635, -0.08)
LOGO_WORLD_CENTER_Y = 3.15
MESSAGE_PROXY_Y = 0.95

# SCPs offered left->right (matches Config.cs defaults). basename of the embedded model.
SCPS = ["scp-049", "scp-079", "scp-096", "scp-106", "scp-173", "scp-939", "scp-3114"]

# --- Aim Range extension (kept in lockstep with AimRangeWorld.cs + AimRangeLayout.cs) --------------
AIM_RANGE = True                    # build the widened 19.2 x 22 m three-lane range
RACK_MODEL = "aim-weapon-rack"
RANGE_WIDTH = 19.2
RANGE_DEPTH = 22.0
RANGE_HEIGHT = 5.0
DOOR_WIDTH = 2.8
DOOR_HEIGHT = 3.2
RANGE_WALL = 0.3
# AimRangeLayout.ShelfAnchors: (slot, x, y, depthFromDoor, yawDeg). The pickup anchors float 0.30 m
# ahead of the rack furniture; the rack instances sit against the widened side walls (runtime
# strip center at +/-9.15). Both racks yaw toward the room (left -90 faces +X, right +90 faces -X).
# The rack asset carries a SYMMETRIC six-marker set (low+tall in both local columns); each rack
# instance uses the three markers whose local column lands on its side's world anchor columns.
SHELF_ANCHORS = [
    (0, -8.85, 1.35, -2.0, 90.0),
    (1, -8.85, 2.15, -2.0, 90.0),
    (2, -8.85, 1.35, -3.6, 90.0),
    (3, 8.85, 1.35, -2.0, -90.0),
    (4, 8.85, 2.15, -2.0, -90.0),
    (5, 8.85, 1.35, -3.6, -90.0),
]
# Runtime slot -> authored rack-local (x, y) marker position per side. The left rack (yaw +90,
# face +X) puts local x=-0.8 on the door-near column (depth -2.0); the right rack (yaw -90,
# face -X) puts local x=+0.8 there. Symmetric markers cover both columns on each instance.
RACK_SLOT_LOCAL = {
    "left": {0: (-0.8, 1.38), 1: (-0.8, 2.10), 2: (0.8, 1.38)},
    "right": {0: (0.8, 1.38), 1: (0.8, 2.10), 2: (-0.8, 1.38)},
}
SLIDING_TRACKS = [
    (-2.35, 2.35, 1.30, -10.2),
    (-2.15, 2.15, 2.15, -14.7),
    (-2.40, 2.40, 1.55, -19.0),
]
BOT_PATHS = [
    [(-8.25, -10.0), (-6.55, -10.0), (-5.05, -11.5), (-7.95, -13.4)],
    [(-8.30, -16.1), (-6.35, -16.1), (-4.55, -18.2), (-7.75, -20.0)],
]
SPHERE_OFFSETS = [
    (-2.05, 1.05, -3.0), (-0.70, 2.15, -3.8), (1.45, 1.40, -4.7),
    (2.10, 3.25, -5.6), (-1.55, 3.55, -6.5), (0.20, 1.00, -7.2),
    (1.75, 2.25, -8.0), (-2.15, 2.55, -8.9), (-0.25, 3.75, -9.8),
    (2.00, 1.20, -10.7), (-1.20, 1.55, -11.5), (0.85, 2.95, -12.2),
]

REPO = Path(__file__).resolve().parents[1]
MODELS_DIR = REPO / "generated" / "models"


def rot_matrix(rx, ry, rz):
    """Unity euler order R = Ry(y) @ Rx(x) @ Rz(z) (matches render_model_preview.rot_matrix)."""
    ax, ay, az = math.radians(rx), math.radians(ry), math.radians(rz)
    cx, sx = math.cos(ax), math.sin(ax)
    cy, sy = math.cos(ay), math.sin(ay)
    cz, sz = math.cos(az), math.sin(az)
    return [
        [cy * cz + sy * sx * sz, -cy * sz + sy * sx * cz, sy * cx],
        [cx * sz, cx * cz, -sx],
        [-sy * cz + cy * sx * sz, sy * sz + cy * sx * cz, cy * cx],
    ]


def matmul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def euler_from_matrix(m):
    """Inverse of rot_matrix: extract (x,y,z) degrees in Unity YXZ order."""
    sx = -m[1][2]
    sx = max(-1.0, min(1.0, sx))
    x = math.asin(sx)
    if abs(sx) < 0.99999:
        z = math.atan2(m[1][0], m[1][1])
        y = math.atan2(m[0][2], m[2][2])
    else:  # gimbal lock
        z = 0.0
        y = math.atan2(-m[2][0], m[0][0])
    return math.degrees(x), math.degrees(y), math.degrees(z)


FACING = rot_matrix(0.0, 180.0, 0.0)  # models authored on +Z, players stand on -Z


def vec(x, y, z):
    return {"x": round(x, 4), "y": round(y, 4), "z": round(z, 4)}


_next_id = 1


def block(name, pos, scale, color, rot=(0.0, 0.0, 0.0), ptype=3, parent_id=0):
    global _next_id
    oid = _next_id
    _next_id += 1
    return {
        "Name": name, "ObjectId": oid, "ParentId": parent_id, "AnimatorName": "",
        "Position": vec(*pos), "Rotation": vec(*rot), "Scale": vec(*scale),
        "BlockType": 1,
        "Properties": {"PrimitiveType": ptype, "Color": color, "PrimitiveFlags": 2, "Static": True},
    }


def add_box(blocks, name, center, size, color):
    blocks.append(block(name, center, size, color))


def add_model(blocks, model_name, base, scale):
    payload = json.loads((MODELS_DIR / f"{model_name}.mer.json").read_text(encoding="utf-8"))
    for b in payload["Blocks"]:
        if b.get("BlockType") != 1:
            continue
        props = b["Properties"]
        if props.get("PrimitiveFlags", 2) == 0:  # marker, non-visible
            continue
        p = b["Position"]; s = b["Scale"]; r = b["Rotation"]
        lp = (p["x"] * scale, p["y"] * scale, p["z"] * scale)
        # world pos = base + FACING * localPos  (FACING is Y-180 => (-x, y, -z))
        wp = (base[0] - lp[0], base[1] + lp[1], base[2] - lp[2])
        m = matmul(FACING, rot_matrix(r["x"], r["y"], r["z"]))
        wr = euler_from_matrix(m)
        ws = (s["x"] * scale, s["y"] * scale, s["z"] * scale)
        blocks.append(block(f"{model_name}_{b.get('Name','p')}", wp, ws, props.get("Color", "#FFFFFF"),
                            rot=wr, ptype=props.get("PrimitiveType", 3)))


def add_logo_hierarchy(blocks, model_name, world_center, scale):
    """Place a MER hierarchy while retaining each Empty shear parent and Quad child.

    The source root is conceptual (ObjectId 0, with no corresponding block), so top-level source blocks
    become direct children of the room root. Uniform placement is baked into those top-level transforms;
    nested child transforms remain byte-for-byte equivalent apart from remapped IDs.
    """
    global _next_id

    payload = json.loads((MODELS_DIR / f"{model_name}.mer.json").read_text(encoding="utf-8"))
    source_blocks = payload["Blocks"]
    source_root = payload.get("RootObjectId", 0)

    id_map = {}
    for source in source_blocks:
        id_map[source["ObjectId"]] = _next_id
        _next_id += 1

    for source in source_blocks:
        copied = json.loads(json.dumps(source))
        copied["Name"] = f"logo_{source.get('Name', source['ObjectId'])}"
        copied["ObjectId"] = id_map[source["ObjectId"]]

        source_parent = source.get("ParentId", source_root)
        is_top_level = source_parent == source_root
        copied["ParentId"] = 0 if is_top_level else id_map[source_parent]

        if is_top_level:
            p = source["Position"]
            s = source["Scale"]
            copied["Position"] = vec(
                world_center[0] + (p["x"] - LOGO_SOURCE_CENTER[0]) * scale,
                world_center[1] + (p["y"] - LOGO_SOURCE_CENTER[1]) * scale,
                world_center[2] + (p["z"] - LOGO_SOURCE_CENTER[2]) * scale,
            )
            copied["Scale"] = vec(s["x"] * scale, s["y"] * scale, s["z"] * scale)

        blocks.append(copied)


def display_positions(count):
    """Return the runtime layout with a centered -clearance/+clearance logo opening."""
    left_count = count // 2
    return [
        -CENTER_CLEARANCE - (left_count - 1 - i) * SPACING
        if i < left_count
        else CENTER_CLEARANCE + (i - left_count) * SPACING
        for i in range(count)
    ]


def rot_y(deg):
    """Column-vector rotation matrix about +Y (Unity convention)."""
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return [[c, 0.0, s], [0.0, 1.0, 0.0], [-s, 0.0, c]]


def add_rack(blocks, base, yaw_deg):
    """Flatten the authored rack into world space: world = base + Ry(yaw) * local.

    Face +Z under yaw=+90 points at +X (right side wall placement), yaw=-90 points at -X (left).
    """
    yaw = rot_y(yaw_deg)
    payload = json.loads((MODELS_DIR / f"{RACK_MODEL}.mer.json").read_text(encoding="utf-8"))
    for src in payload["Blocks"]:
        if src.get("BlockType") != 1 or src["Properties"].get("PrimitiveFlags", 2) == 0:
            continue
        p, s, r = src["Position"], src["Scale"], src["Rotation"]
        wp = (
            base[0] + yaw[0][0] * p["x"] + yaw[0][2] * p["z"],
            base[1] + p["y"],
            base[2] + yaw[2][0] * p["x"] + yaw[2][2] * p["z"],
        )
        m = matmul(yaw, rot_matrix(r["x"], r["y"], r["z"]))
        wr = euler_from_matrix(m)
        blocks.append(block(f"rack_{src.get('Name', 'p')}", wp, (s["x"], s["y"], s["z"]),
                            src["Properties"].get("Color", "#FFFFFF"), rot=wr,
                            ptype=src["Properties"].get("PrimitiveType", 3)))


def rack_slot_world(base, yaw_deg, local_xy):
    """Resolve the rack marker authored at local (x, y) through the yaw transform.

    The rack's symmetric columns mean several markers share namespaced IDs; the runtime resolves
    the anchor by authored local position, exactly as this preview does.
    """
    payload = json.loads((MODELS_DIR / f"{RACK_MODEL}.mer.json").read_text(encoding="utf-8"))
    yaw = rot_y(yaw_deg)
    for src in payload["Blocks"]:
        name = src.get("Name", "")
        if not name.startswith("marker_shelf_"):
            continue
        p = src["Position"]
        if abs(p["x"] - local_xy[0]) < 0.05 and abs(p["y"] - local_xy[1]) < 0.05:
            return (
                base[0] + yaw[0][0] * p["x"] + yaw[0][2] * p["z"],
                base[1] + p["y"],
                base[2] + yaw[2][0] * p["x"] + yaw[2][2] * p["z"],
            )
    raise KeyError(f"no rack marker at local {local_xy} in {RACK_MODEL}")


def add_gun_proxy(blocks, name, center, yaw_deg, length=0.7):
    """Simple long-gun silhouette resting across a cradle: body + barrel + stock.

    Guns lie ALONG the rack face (perpendicular to the outward face normal), so ``yaw_deg`` is the
    facing of the cradle prongs and the body axis runs 90 degrees off it — the rifle reads as
    resting in the cradle from the room, not spearing through the back panel.
    """
    blocks.append(block(f"gun_{name}_body", center, (0.07, 0.11, length), "#4A5161",
                        rot=(0.0, yaw_deg, 0.0)))
    a = math.radians(yaw_deg)
    fx, fz = math.sin(a), math.cos(a)  # along the rack face
    blocks.append(block(f"gun_{name}_barrel",
                        (center[0] + fx * (length / 2 + 0.09), center[1] + 0.02, center[2] + fz * (length / 2 + 0.09)),
                        (0.035, 0.035, 0.18), "#6A7285", rot=(0.0, yaw_deg, 0.0)))
    blocks.append(block(f"gun_{name}_stock",
                        (center[0] - fx * (length / 2 - 0.05), center[1] - 0.045, center[2] - fz * (length / 2 - 0.05)),
                        (0.06, 0.11, 0.15), "#4A5161", rot=(0.0, yaw_deg, 0.0)))


def add_target_proxy(blocks, name, center, yaw_deg=0.0):
    """Native ShootingTargetToy stand-in: slim light plate on a small stem, facing the firing line."""
    blocks.append(block(f"target_{name}_plate", center, (0.5, 0.6, 0.04), THEME["target"],
                        rot=(0.0, yaw_deg, 0.0)))
    blocks.append(block(f"target_{name}_stem",
                        (center[0], center[1] - 0.45, center[2]), (0.08, 0.3, 0.08), THEME["fixture"],
                        rot=(0.0, yaw_deg, 0.0)))


def add_bot_proxy(blocks, name, x, z):
    """RA-dummy silhouette: capsule body + head so path corners can be checked for clearance."""
    blocks.append(block(f"bot_{name}_body", (x, 0.85, z), (0.5, 0.7, 0.5), THEME["bot"], ptype=1))
    blocks.append(block(f"bot_{name}_head", (x, 1.45, z), (0.3, 0.3, 0.3), THEME["bot"], ptype=0))


def build_aim_range(blocks, door_z):
    """Mirror AimRangeWorld.Build: shell, dividers, racks + gun cradles, target bays, bot corners."""
    half_w = RANGE_WIDTH / 2
    center_z = door_z - RANGE_DEPTH / 2

    # Widened shell continuing the gallery floor/seam language toward -Z.
    add_box(blocks, "range_floor", (0.0, FLOOR_TOP_Y - FLOOR_THICKNESS / 2, center_z),
            (RANGE_WIDTH, FLOOR_THICKNESS, RANGE_DEPTH), THEME["floor"])
    add_box(blocks, "range_floor_seam", (0.0, FLOOR_TOP_Y + 0.02, center_z),
            (RANGE_WIDTH - 0.5, 0.04, RANGE_DEPTH - 0.5), THEME["floor_accent"])
    add_box(blocks, "range_floor_inner", (0.0, FLOOR_TOP_Y + 0.03, center_z),
            (RANGE_WIDTH - 0.9, 0.04, RANGE_DEPTH - 0.9), THEME["floor"])
    add_box(blocks, "range_wall_left", (-half_w, wall_mid_y(), center_z),
            (RANGE_WALL, RANGE_HEIGHT, RANGE_DEPTH), THEME["wall"])
    add_box(blocks, "range_wall_right", (half_w, wall_mid_y(), center_z),
            (RANGE_WALL, RANGE_HEIGHT, RANGE_DEPTH), THEME["wall"])
    add_box(blocks, "range_backstop", (0.0, wall_mid_y(), door_z - RANGE_DEPTH),
            (RANGE_WIDTH, RANGE_HEIGHT, 0.6), THEME["backstop"])
    add_box(blocks, "range_ceiling", (0.0, FLOOR_TOP_Y + RANGE_HEIGHT, center_z),
            (RANGE_WIDTH, RANGE_WALL, RANGE_DEPTH), THEME["ceiling"])

    lane_width = RANGE_WIDTH / 3
    add_box(blocks, "range_cover_rail", (0.0, FLOOR_TOP_Y + 0.55, door_z - 6.8),
            (RANGE_WIDTH - 1.2, 1.1, 0.25), THEME["fixture"])
    add_box(blocks, "range_divider_l", (-lane_width / 2, FLOOR_TOP_Y + 1.4, door_z - 14.4),
            (0.25, 2.8, 15.2), THEME["divider"])
    add_box(blocks, "range_divider_r", (lane_width / 2, FLOOR_TOP_Y + 1.4, door_z - 14.4),
            (0.25, 2.8, 15.2), THEME["divider"])

    rack_bases = {"left": (-9.15, FLOOR_TOP_Y, door_z - 2.8), "right": (9.15, FLOOR_TOP_Y, door_z - 2.8)}
    rack_yaws = {"left": 90.0, "right": -90.0}
    add_rack(blocks, rack_bases["left"], rack_yaws["left"])
    add_rack(blocks, rack_bases["right"], rack_yaws["right"])
    for slot, x, y, depth, yaw in SHELF_ANCHORS:
        side = "left" if slot < 3 else "right"
        pos = rack_slot_world(rack_bases[side], rack_yaws[side], RACK_SLOT_LOCAL[side][slot % 3])
        add_gun_proxy(blocks, f"slot{slot}", (pos[0], pos[1] + 0.02, pos[2]), 0.0,
                      length=0.62 if slot % 2 else 0.78)

    # Lane 1: cover-backed native-walking bots. Every path includes a visible emerge/dwell/retreat beat.
    cover = [
        (-8.0, 0.85, -9.35, 1.9, 1.7), (-4.75, 0.75, -10.9, 1.55, 1.5),
        (-7.9, 0.9, -12.85, 1.8, 1.8), (-8.05, 0.9, -15.45, 1.9, 1.8),
        (-4.65, 0.75, -17.55, 1.55, 1.5), (-7.75, 0.95, -19.55, 1.9, 1.9),
    ]
    for i, (x, y, depth, width, height) in enumerate(cover):
        add_box(blocks, f"bot_cover_{i}", (x, FLOOR_TOP_Y + y, door_z + depth),
                (width, height, 0.45 if i not in (2, 3, 5) else 0.5), THEME["fixture"])
    for path_i, path in enumerate(BOT_PATHS):
        for corner_i, (x, depth) in enumerate(path):
            add_bot_proxy(blocks, f"p{path_i}c{corner_i}", x, door_z + depth)

    # Lane 2: three parallel persistent sliding targets at different distances and heights.
    for i, (x0, x1, height, depth) in enumerate(SLIDING_TRACKS):
        add_box(blocks, f"sliding_rail_{i}", ((x0 + x1) / 2, FLOOR_TOP_Y + 0.025, door_z + depth),
                (x1 - x0 + 0.6, 0.05, 0.18), THEME["divider"])
        pose = (x0, (x0 + x1) / 2, x1)[i]
        add_target_proxy(blocks, f"sliding_{i}", (pose, FLOOR_TOP_Y + height, door_z + depth))

    # Lane 3: representative active Aim-Lab spheres from the authored 12-point cloud.
    sphere_origin = (lane_width, FLOOR_TOP_Y, door_z - 7.0)
    for i in (0, 5, 9):
        x, y, depth = SPHERE_OFFSETS[i]
        blocks.append(block(f"sphere_{i}", (sphere_origin[0] + x, sphere_origin[1] + y, sphere_origin[2] + depth),
                            (0.72, 0.72, 0.72), THEME["floor_accent"], ptype=0))


def wall_mid_y():
    return FLOOR_TOP_Y + RANGE_HEIGHT / 2


def verify_aim_range(door_z):
    """Hard checks that the preview's rack slot markers land on the runtime's AimRangeLayout anchors.

    This catches any mirror mistake between the authored rack markers and the C# shelf anchors:
    the marker-derived world position (minus the authored floats) must equal the runtime local
    anchor (x, floor + y, door + depth) for all six slots within 0.05 m on every axis.
    """
    rack_bases = {"left": (-9.15, FLOOR_TOP_Y, door_z - 2.8), "right": (9.15, FLOOR_TOP_Y, door_z - 2.8)}
    rack_yaws = {"left": 90.0, "right": -90.0}
    # High-board markers sit 0.08 below the tall-anchor height so an E11-SR clears the rack crown;
    # the runtime anchor stays the pickup pivot (a long gun's grip line), which the cradle supports.
    marker_anchor_dy = {1: -0.08, 4: -0.08}
    failures = []
    for slot, x, y, depth, yaw in SHELF_ANCHORS:
        side = "left" if slot < 3 else "right"
        pos = rack_slot_world(rack_bases[side], rack_yaws[side], RACK_SLOT_LOCAL[side][slot % 3])
        # Markers float 0.03 above the anchor height (plus the tall-slot cradle offset) and sit
        # 0.25 m behind the anchor plane toward the wall (authored z=0.05 vs the 0.30 anchor
        # float); after removing those authored floats the marker must land exactly on the anchor.
        face = 1.0 if side == "left" else -1.0
        expect = (pos[0] + 0.25 * face, pos[1] - 0.03 - marker_anchor_dy.get(slot, 0.0), pos[2])
        got = (x, FLOOR_TOP_Y + y, door_z + depth)
        err = max(abs(a - b) for a, b in zip(expect, got))
        if err > 0.05:
            failures.append(
                f"slot {slot}: marker-derived {tuple(round(v, 3) for v in expect)} vs runtime anchor "
                f"{tuple(round(v, 3) for v in got)} (err={err:.3f})")
    if failures:
        raise SystemExit("Aim range anchor verification failed:\n  " + "\n  ".join(failures))
    print("Aim range anchor verification passed (6/6 rack slot markers match AimRangeLayout anchors)")


def build():
    global _next_id
    _next_id = 1

    blocks = []
    count = len(SCPS)
    positions = display_positions(count)
    max_abs_x = max((abs(x) for x in positions), default=0.0)
    floor_width = 2 * max_abs_x + 7.0
    pedestal_width = min(SPACING * 0.7, 1.6)

    # floor + a thin bright accent seam sitting just above it around the play area
    add_box(blocks, "floor", (0.0, FLOOR_TOP_Y - FLOOR_THICKNESS / 2, FLOOR_CENTER_Z),
            (floor_width, FLOOR_THICKNESS, FLOOR_DEPTH), THEME["floor"])
    add_box(blocks, "floor_seam", (0.0, FLOOR_TOP_Y + 0.02, FLOOR_CENTER_Z),
            (floor_width - 0.5, 0.04, FLOOR_DEPTH - 0.5), THEME["floor_accent"])
    add_box(blocks, "floor_inner", (0.0, FLOOR_TOP_Y + 0.03, FLOOR_CENTER_Z),
            (floor_width - 0.9, 0.03, FLOOR_DEPTH - 0.9), THEME["floor"])

    wall_mid = FLOOR_TOP_Y + WALL_HEIGHT / 2
    back_z = FLOOR_CENTER_Z + FLOOR_DEPTH / 2
    front_z = FLOOR_CENTER_Z - FLOOR_DEPTH / 2
    add_box(blocks, "wall_back", (0.0, wall_mid, back_z), (floor_width, WALL_HEIGHT, WALL_THICKNESS), THEME["wall"])
    if AIM_RANGE:
        # SelectorRoom.BuildAimRangeDoorway: two stubs + lintel, gate removed (range open).
        stub_width = (floor_width - DOOR_WIDTH) / 2
        offset = (DOOR_WIDTH + stub_width) / 2
        add_box(blocks, "wall_front_stub_l", (-offset, wall_mid, front_z), (stub_width, WALL_HEIGHT, WALL_THICKNESS), THEME["wall"])
        add_box(blocks, "wall_front_stub_r", (offset, wall_mid, front_z), (stub_width, WALL_HEIGHT, WALL_THICKNESS), THEME["wall"])
        add_box(blocks, "wall_front_lintel", (0.0, DOOR_HEIGHT + (WALL_HEIGHT - DOOR_HEIGHT) / 2, front_z),
                (DOOR_WIDTH, WALL_HEIGHT - DOOR_HEIGHT, WALL_THICKNESS), THEME["wall"])
        # Gold doorway trim so the opening reads as a designed portal from the gallery.
        add_box(blocks, "door_trim_top", (0.0, DOOR_HEIGHT - 0.03, front_z + WALL_THICKNESS / 2 + 0.02),
                (DOOR_WIDTH - 0.1, 0.06, 0.04), THEME["coin"])
    else:
        add_box(blocks, "wall_front", (0.0, wall_mid, front_z), (floor_width, WALL_HEIGHT, WALL_THICKNESS), THEME["wall"])
    add_box(blocks, "wall_left", (-floor_width / 2, wall_mid, FLOOR_CENTER_Z), (WALL_THICKNESS, WALL_HEIGHT, FLOOR_DEPTH), THEME["wall"])
    add_box(blocks, "wall_right", (floor_width / 2, wall_mid, FLOOR_CENTER_Z), (WALL_THICKNESS, WALL_HEIGHT, FLOOR_DEPTH), THEME["wall"])
    add_box(blocks, "ceiling", (0.0, FLOOR_TOP_Y + WALL_HEIGHT, FLOOR_CENTER_Z), (floor_width, WALL_THICKNESS, FLOOR_DEPTH), THEME["ceiling"])

    wall_art_z = back_z - WALL_THICKNESS / 2 - 0.06
    add_logo_hierarchy(
        blocks,
        LOGO_MODEL,
        (0.0, LOGO_WORLD_CENTER_Y, wall_art_z),
        LOGO_SCALE,
    )
    # TMP cannot be rasterized here; this slim gold guide marks the live welcome message's baseline.
    add_box(blocks, "message_proxy", (0.0, MESSAGE_PROXY_Y, wall_art_z - 0.01),
            (5.4, 0.08, 0.02), THEME["message"])

    for scp, x in zip(SCPS, positions):
        add_box(blocks, f"pedestal_{scp}", (x, FLOOR_TOP_Y + PEDESTAL_HEIGHT / 2, ROW_Z),
                (pedestal_width, PEDESTAL_HEIGHT, PEDESTAL_DEPTH), THEME["pedestal"])

        base = (x, FLOOR_TOP_Y + PEDESTAL_HEIGHT, ROW_Z + 0.05)
        add_model(blocks, scp, base, MODEL_SCALE)

        # coin proxy (flat gold disc) floating in front of the model
        coin_z = ROW_Z - PEDESTAL_DEPTH / 2 - 0.8
        blocks.append(block(f"coin_{scp}", (x, FLOOR_TOP_Y + 1.1, coin_z), (0.5, 0.03, 0.5),
                            THEME["coin"], rot=(90.0, 0.0, 0.0), ptype=2))

    if AIM_RANGE:
        build_aim_range(blocks, front_z)
        verify_aim_range(front_z)

    out = REPO / "generated" / "preview" / "room.mer.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({"RootObjectId": 0, "Blocks": blocks}, indent=2), encoding="utf-8")
    logo_visible = sum(
        1 for b in blocks
        if b.get("Name", "").startswith("logo_") and b.get("BlockType") == 1
        and b.get("Properties", {}).get("PrimitiveFlags", 2) != 0
    )
    logo_parents = sum(
        1 for b in blocks if b.get("Name", "").startswith("logo_") and b.get("BlockType") == 0
    )
    print(
        f"Wrote {out} ({len(blocks)} blocks, {count} SCPs at {positions}, "
        f"logo {logo_visible} visible + {logo_parents} shear parents, floor {floor_width:.1f}m)"
    )


if __name__ == "__main__":
    build()

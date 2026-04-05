#!/usr/bin/env python3
"""Analyze terrain at the 74% death zone with correct GID→collision mapping."""
import xml.etree.ElementTree as ET, csv

# Collision table (index 0-255, matching MetatileCollision.cs)
COL_NAMES = {
    0x00: "NONE", 0x01: "FLOOR_CEIL", 0x02: "FLOOR_CEIL", 0x03: "BOTTOM",
    0x04: "DEATH_TOP", 0x05: "FLOOR_CEIL", 0x06: "FLOOR_CEIL", 0x07: "NONE",
    0x08: "DEATH_BOT", 0x09: "DEATH_BOT", 0x0A: "DEATH_TOP", 0x0B: "DEATH_TOP",
    0x0C: "DEATH_BOT", 0x0D: "DEATH_TOP", 0x0E: "DEATH_LEFT", 0x0F: "DEATH_RIGHT",
    0x10: "DEATH", 0x11: "TOP", 0x12: "DEATH_BOT", 0x13: "TOP", 0x14: "FLOOR_CEIL",
    0x15: "FLOOR_CEIL", 0x16: "FLOOR_CEIL", 0x17: "FLOOR_CEIL", 0x18: "FLOOR_CEIL",
    0x19: "DEATH", 0x1A: "DEATH", 0x1B: "FLOOR_CEIL", 0x1C: "FLOOR_CEIL",
    0x1D: "TOP", 0x1E: "TOP", 0x1F: "DEATH", 0x20: "DEATH", 0x21: "DEATH",
    0x22: "DEATH", 0x23: "DEATH_BOT", 0x24: "DEATH_TOP", 0x25: "NONE",
    0x26: "TOP_LEFT_STAIRS", 0x27: "TOP_RIGHT_STAIRS", 0x28: "BOT_LEFT_STAIRS",
    0x29: "BOT_RIGHT_STAIRS", 0x2A: "STAIRS_FULL", 0x2B: "STAIRS_FULL",
    0x2C: "NONE", 0x2D: "NONE", 0x2E: "NONE", 0x2F: "NONE",
    0x30: "DEATH_BOT", 0x31: "DEATH_TOP", 0x32: "STAIRS_FULL", 0x33: "STAIRS_FULL",
    0x34: "NONE", 0x35: "NONE",
}

def get_col(idx):
    return COL_NAMES.get(idx, f"?{idx:02X}")

tree = ET.parse(r"c:\Editor Test\eon2.tmx")
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

# Find tile layer
for layer in root.findall('.//layer'):
    if layer.attrib.get('name', '') in ('Tiles', 'tiles', 'Tile Layer 1', 'tile_layer'):
        data = layer.find('data')
        break
else:
    # Try first layer
    layer = root.findall('.//layer')[0]
    data = layer.find('data')

text = data.text.strip()
gids = [int(x) for x in text.split(',')]
print(f"Map: {width}x{height}, total tiles={len(gids)}")

# Analyze cols 400-420, rows 14-26
print(f"\n{'':8s}", end='')
for c in range(400, 421):
    print(f"{'c'+str(c):>13s}", end='')
print()

for r in range(14, 27):
    Y = r * 16
    print(f"r{r:2d} Y={Y:3d}", end='')
    for c in range(400, 421):
        idx = r * width + c
        gid = gids[idx] & 0x3FFFFFFF  # strip flip flags
        if gid == 0:
            col_name = "EMPTY"
            tile_idx = -1
        else:
            tile_idx = gid - 1
            col_name = get_col(tile_idx)
        print(f"  {col_name[:11]:>11s}", end='')
    print()

# Also check sprite layer for H_BLOCKs near col 120-130
print("\n\nSprite layer around col 120-130, rows 18-24:")
for layer in root.findall('.//layer'):
    name = layer.attrib.get('name', '')
    if 'sprite' in name.lower() or 'object' in name.lower() or name == 'Sprite Layer 1':
        data2 = layer.find('data')
        sgids = [int(x) for x in data2.text.strip().split(',')]
        for r in range(18, 25):
            for c in range(120, 135):
                idx = r * width + c
                gid2 = sgids[idx] & 0x3FFFFFFF
                if gid2 > 256:  # sprite GIDs start at 257
                    sprite_id = gid2 - 257
                    if sprite_id == 0xF8 or sprite_id == 0xF9:  # H_BLOCK / F_BLOCK
                        print(f"  Sprite 0x{sprite_id:02X} at col={c} row={r} Y={r*16}")
        break

# Detailed analysis of the gap
print("\n\nDetailed gap analysis at col 414:")
for r in range(12, 27):
    Y = r * 16
    idx = r * width + 414
    gid = gids[idx] & 0x3FFFFFFF
    if gid == 0:
        col_name = "EMPTY"
        tile_idx = -1
    else:
        tile_idx = gid - 1
        col_name = get_col(tile_idx)
    passable = col_name in ("NONE", "EMPTY", "TOP", "BOTTOM")
    print(f"  r{r:2d} Y={Y:3d}-{Y+15}: GID={gid:3d} idx={tile_idx:3d} coll={col_name:16s} {'PASS' if passable else 'BLOCK'}")

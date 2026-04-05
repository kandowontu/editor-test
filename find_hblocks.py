#!/usr/bin/env python3
"""Find all H_BLOCK and F_BLOCK sprites in eon2.tmx."""
import xml.etree.ElementTree as ET

tree = ET.parse(r"c:\Editor Test\eon2.tmx")
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

# Find sprite layer
for layer in root.findall('.//layer'):
    name = layer.attrib.get('name', '')
    if 'sprite' in name.lower() or name == 'Sprite Layer 1':
        data = layer.find('data')
        sgids = [int(x) for x in data.text.strip().split(',')]
        break

# H_BLOCK = sprite 0xF8 = 248, GID = 257 + 248 = 505
# F_BLOCK = sprite 0xF9 = 249, GID = 257 + 249 = 506
SPRITE_FIRST_GID = 257
H_BLOCK_GID = SPRITE_FIRST_GID + 0xF8  # 505
F_BLOCK_GID = SPRITE_FIRST_GID + 0xF9  # 506

print("H_BLOCK and F_BLOCK sprites in eon2.tmx:")
print(f"{'Type':10s} {'Col':>5s} {'Row':>5s} {'X':>6s} {'Y':>6s}")
for i, gid in enumerate(sgids):
    raw_gid = gid & 0x3FFFFFFF
    if raw_gid in (H_BLOCK_GID, F_BLOCK_GID):
        r = i // width
        c = i % width
        name = "H_BLOCK" if raw_gid == H_BLOCK_GID else "F_BLOCK"
        print(f"{name:10s} {c:5d} {r:5d} {c*16:6d} {r*16:6d}")

# Also check terrain around each H_BLOCK (ceiling above?)
print("\nTerrain above/below each H_BLOCK:")
for layer2 in root.findall('.//layer'):
    name = layer2.attrib.get('name', '')
    if 'sprite' not in name.lower() and name != 'Sprite Layer 1':
        data2 = layer2.find('data')
        tgids = [int(x) for x in data2.text.strip().split(',')]
        break

COL_NAMES = {
    0: "NONE", 1: "FLOOR_CEIL", 2: "FLOOR_CEIL", 3: "BOTTOM",
    4: "DEATH_TOP", 5: "FLOOR_CEIL", 6: "FLOOR_CEIL", 7: "NONE",
    8: "DEATH_BOT", 9: "DEATH_BOT", 16: "DEATH", 17: "TOP",
    18: "DEATH_BOT", 19: "TOP", 20: "FLOOR_CEIL", 24: "FLOOR_CEIL",
    25: "DEATH", 26: "DEATH",
}

for i, gid in enumerate(sgids):
    raw_gid = gid & 0x3FFFFFFF
    if raw_gid in (H_BLOCK_GID, F_BLOCK_GID):
        r = i // width
        c = i % width
        name = "H_BLOCK" if raw_gid == H_BLOCK_GID else "F_BLOCK"
        print(f"\n{name} at col={c} row={r} (Y={r*16}):")
        for dr in range(-3, 4):
            rr = r + dr
            if 0 <= rr < height:
                tidx = rr * width + c
                tgid = tgids[tidx] & 0x3FFFFFFF
                if tgid == 0:
                    coll = "EMPTY"
                else:
                    tile_idx = tgid - 1
                    coll = COL_NAMES.get(tile_idx, f"?{tile_idx:02X}")
                marker = " <-- SPRITE" if dr == 0 else ""
                print(f"  r{rr:2d} Y={rr*16:3d}: {coll}{marker}")

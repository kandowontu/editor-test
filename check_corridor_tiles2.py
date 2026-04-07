#!/usr/bin/env python3
"""Check the actual metatile IDs and collision types at the corridor death positions in eighto.tmx"""
import xml.etree.ElementTree as ET
import csv, io

# Load TMX
tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\eighto.tmx")
root = tree.getroot()

# Find tiles layer
tiles_layer = None
for layer in root.findall('layer'):
    if layer.get('name') == 'Tiles':
        tiles_layer = layer
        break

if not tiles_layer:
    print("No Tiles layer found")
    exit(1)

width = int(tiles_layer.get('width'))
height = int(tiles_layer.get('height'))
data = tiles_layer.find('data')
reader = csv.reader(io.StringIO(data.text.strip()))
all_gids = []
for row in reader:
    for val in row:
        val = val.strip()
        if val:
            all_gids.append(int(val))

# Collision table from MetatileCollision.cs (need to read the full table)
# Let me just read the first 256 entries from the source file
col_table_text = """COL_NONE
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_NONE
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_LEFT
COL_DEATH_RIGHT
COL_ALL
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_TOP_CENTER_SPIKE
COL_ALL
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_TOP
COL_DEATH
COL_DEATH
COL_DEATH
COL_DEATH_LEFT
COL_DEATH_TOP
COL_DEATH_RIGHT"""

col_entries = [line.strip() for line in col_table_text.strip().split('\n') if line.strip()]

FIRST_GID = 1

# Check corridor area
print(f"Map size: {width}x{height}")
print(f"Collision table: {len(col_entries)} entries loaded")

print(f"\nAll tiles in corridor (rows 30-38, cols 376-390):")
for row in range(30, 39):
    print(f"\n  row={row} Y={row*16}px:")
    for col in range(376, 391):
        idx = row * width + col
        if idx >= len(all_gids):
            continue
        gid = all_gids[idx]
        if gid == 0:
            continue
        metatile = gid - FIRST_GID
        if metatile < len(col_entries):
            col_type = col_entries[metatile]
        else:
            col_type = f"mt{metatile}_UNKNOWN"
        x_px = col * 16
        print(f"    col={col} X={x_px}px GID={gid} metatile={metatile} collision={col_type}")

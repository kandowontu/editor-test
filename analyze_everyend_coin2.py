#!/usr/bin/env python3
"""Analyze everyend level coin - CORRECTED with proper collision table"""

import xml.etree.ElementTree as ET

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx"

# Build collision table from MetatileCollision.cs
COLLISION_TABLE_TEXT = """COL_NONE
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
COL_DEATH_RIGHT
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_TOP_CENTER_SPIKE
COL_ALL
COL_ALL
COL_ALL
COL_DEATH_BOTTOM
COL_ALL
COL_ALL
COL_ALL
COL_ALL"""

import re
collision_table = []
for line in COLLISION_TABLE_TEXT.strip().split('\n'):
    line = line.strip()
    m = re.match(r'COL_\w+', line)
    if m:
        collision_table.append(m.group())

def get_collision(gid):
    """Get collision type for a TMX GID (firstgid=1)"""
    if gid == 0:
        return 'EMPTY'
    local = gid - 1
    if 0 <= local < len(collision_table):
        return collision_table[local]
    return f'UNK_{local}'

# Parse TMX
tree = ET.parse(TMX)
root = tree.getroot()
mapW = int(root.get('width'))
mapH = int(root.get('height'))
groundRowsToReserve = 3
groundSurface_px = (mapH - groundRowsToReserve) * 16

# Parse layers
layers = root.findall('.//layer')
tile_layer = None
sp_layer = None
for layer in layers:
    name = layer.get('name') or ''
    data = layer.find('data')
    text = data.text.strip()
    vals = [int(x.strip()) for x in text.split(',') if x.strip()]
    grid = []
    for r in range(mapH):
        grid.append(vals[r*mapW:(r+1)*mapW])
    if name == 'SP':
        sp_layer = grid
    else:
        tile_layer = grid

coinCol = 2237
coinRow = 37  # from the Y=607: but that's adjusted. Storage row for the coin sprite is 41

# Corrected: the sprite is at storage row 41, col 2237
# adjustedY = (41 - 3) * 16 = 608, coin hitbox top = 607 (with -1 offset)
spriteRow = 41

print(f"Map: {mapW}x{mapH} tiles, groundRowsToReserve={groundRowsToReserve}")
print(f"groundSurface_px = {groundSurface_px}")
print(f"Coin sprite at storage row={spriteRow}, col={coinCol}")
print(f"Coin adjustedY = ({spriteRow}-{groundRowsToReserve})*16 = {(spriteRow-groundRowsToReserve)*16}")
print(f"Player at Y=673 => storage row = 673/16 + {groundRowsToReserve} = {673/16 + groundRowsToReserve:.2f}")
print()

# Show collision type for each GID in the area
col_start = max(0, coinCol - 15)
col_end = min(mapW, coinCol + 11)
row_start = max(0, spriteRow - 8)
row_end = min(mapH, spriteRow + 16)

# Short collision names
def short_col(col_type):
    mapping = {
        'EMPTY': '  .  ',
        'COL_NONE': '  .  ',
        'COL_ALL': ' ALL ',
        'COL_TOP': ' TOP ',
        'COL_BOTTOM': ' BOT ',
        'COL_LEFT': ' LFT ',
        'COL_RIGHT': ' RGT ',
        'COL_FLOOR_CEIL': ' F/C ',
        'COL_DEATH': ' DTH ',
        'COL_DEATH_TOP': ' D_T ',
        'COL_DEATH_BOTTOM': ' D_B ',
        'COL_DEATH_LEFT': ' D_L ',
        'COL_DEATH_RIGHT': ' D_R ',
        'COL_TOP_CENTER_SPIKE': ' TCS ',
    }
    return mapping.get(col_type, col_type[:5].rjust(5))

print(f"{'='*120}")
print(f"COLLISION MAP (cols {col_start}-{col_end-1}, rows {row_start}-{row_end-1})")
print(f"Legend: .=air ALL=solid DTH=death D_T/D_B/D_L/D_R=directional death F/C=floor+ceil")
print(f"{'='*120}")

# Header
hdr = "           "
for c in range(col_start, col_end):
    if c == coinCol:
        hdr += " COIN"
    else:
        hdr += f"{c%100:5d}"
print(hdr)

for r in range(row_start, row_end):
    adjY = (r - groundRowsToReserve) * 16
    marker = ""
    if r == spriteRow:
        marker = " <<COIN(adjY=%d)" % adjY
    elif abs(adjY - 673) < 16:
        marker = " <<PLAYER_TOP(adjY=%d)" % adjY
    elif r == mapH - groundRowsToReserve:
        marker = " <<GROUND_SURFACE"
    
    line = f"r{r:02d} y{adjY:4d} "
    for c in range(col_start, col_end):
        gid = tile_layer[r][c]
        col_type = get_collision(gid)
        line += short_col(col_type)
    print(f"{line}{marker}")

# Find SP sprites in approach zone
print(f"\n{'='*120}")
print(f"SPRITE LAYER (non-empty only)")
print(f"{'='*120}")

SPRITE_NAMES = {
    0x01: 'SHIP_PORT', 0x02: 'CUBE_PORT', 0x03: 'BALL_PORT',
    0x04: 'UFO_PORT', 0x05: 'WAVE_PORT', 0x06: 'SPIDER_PORT',
    0x07: 'SECRET_COIN', 0x08: 'MINI_PORT', 0x09: 'BIG_PORT',
    0x0A: 'PAD_YELLOW', 0x0B: 'ORB_YELLOW', 0x0C: 'PAD_PINK',
    0x0D: 'ORB_PINK', 0x0E: 'PAD_RED', 0x0F: 'ORB_RED',
    0x10: 'PAD_BLUE', 0x11: 'ORB_BLUE', 0x12: 'PAD_GREEN',
    0x13: 'ORB_GREEN', 0x14: 'MIRROR_ON', 0x15: 'MIRROR_OFF',
    0x16: 'DUAL_ON', 0x17: 'DUAL_OFF',
    0x18: 'SPEED2', 0x19: 'SPEED1', 0x1A: 'SPEED3', 0x1B: 'SPEED4',
    0x1C: 'SPEED0', 0x1D: 'GRAVITY_UP', 0x1E: 'GRAVITY_DN',
}

# Wide scan for sprites: cols coinCol-60 to coinCol+10, all rows
for r in range(mapH):
    for c in range(max(0, coinCol-60), min(mapW, coinCol+15)):
        gid = sp_layer[r][c]
        if gid > 1:
            local_id = gid - 257 if gid >= 257 else gid
            sname = SPRITE_NAMES.get(local_id, f's0x{local_id:02X}')
            adjY = (r - groundRowsToReserve) * 16
            adjX = c * 16
            dist = c - coinCol
            print(f"  row={r:2d} col={c:4d} (adjY={adjY:4d}) dist={dist:+4d}cols  sid=0x{local_id:02X} = {sname}")

# Floor/platform analysis near the coin
print(f"\n{'='*120}")
print(f"FLOOR/PLATFORM ANALYSIS at coin column {coinCol}")
print(f"{'='*120}")

# Find solid (COL_ALL) tiles in the column
for c in [coinCol-5, coinCol-3, coinCol-1, coinCol, coinCol+1, coinCol+3, coinCol+5]:
    if c < 0 or c >= mapW:
        continue
    solids = []
    for r in range(mapH):
        gid = tile_layer[r][c]
        col_type = get_collision(gid)
        if col_type == 'COL_ALL':
            solids.append(r)
    if solids:
        # Find gaps
        gaps = []
        if solids[0] > 0:
            gaps.append((0, solids[0]-1))
        for i in range(len(solids)-1):
            if solids[i+1] - solids[i] > 1:
                gaps.append((solids[i]+1, solids[i+1]-1))
        if solids[-1] < mapH-1:
            gaps.append((solids[-1]+1, mapH-1))
        
        print(f"  col={c}:")
        for g_top, g_bot in gaps:
            adj_top = (g_top - groundRowsToReserve) * 16
            adj_bot = (g_bot - groundRowsToReserve + 1) * 16
            height = adj_bot - adj_top
            in_range = abs(adj_top - 607) < 200 or abs(adj_bot - 607) < 200
            coin_in = g_top <= spriteRow <= g_bot
            label = ""
            if coin_in:
                label = " *** COIN IS HERE ***"
            elif in_range:
                label = " (near coin)"
            if in_range or coin_in:
                print(f"    gap rows {g_top:2d}-{g_bot:2d} => adjY {adj_top:4d}-{adj_bot:4d} ({height}px tall){label}")
    else:
        print(f"  col={c}: NO solid tiles at all")

# Check if ground tiles are present (bottom rows)
print(f"\n{'='*120}")
print(f"GROUND AREA (bottom rows)")
print(f"{'='*120}")
for r in range(mapH-5, mapH):
    gid = tile_layer[r][coinCol]
    col_type = get_collision(gid)
    adjY = (r - groundRowsToReserve) * 16
    print(f"  row={r} adjY={adjY} GID={gid} collision={col_type}")

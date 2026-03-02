#!/usr/bin/env python3
"""Wide approach zone analysis for everyend coin at col 2237"""

import xml.etree.ElementTree as ET
import re

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx"

# Build collision table
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

collision_table = []
for line in COLLISION_TABLE_TEXT.strip().split('\n'):
    m = re.match(r'COL_\w+', line.strip())
    if m:
        collision_table.append(m.group())

def get_col(gid):
    if gid == 0: return 'EMPTY'
    local = gid - 1
    if 0 <= local < len(collision_table):
        return collision_table[local]
    return f'UNK_{local}'

def is_solid(gid):
    ct = get_col(gid)
    return ct in ('COL_ALL', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM')

# Parse TMX
tree = ET.parse(TMX)
root = tree.getroot()
mapW = int(root.get('width'))
mapH = int(root.get('height'))

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

GR = 3  # groundRowsToReserve
coinCol = 2237
coinSpriteRow = 41

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
    0x1F: 'DECO_SPIKE', 0x20: 'BLACK_ORB',
    0x23: 'DASH_GREEN', 0x24: 'DASH_PINK', 0x25: 'SPIDER_ORB',
    0x26: 'TELEPORT', 0x27: 'TRIGGER_ORB', 0x28: 'SWING_PORT',
    0x29: 'ROBOT_PORT', 0x2A: 'TOGGLE_ORB', 0x2B: 'GRAV_SWITCH',
    0x2C: 'GRAV_RING', 0x2D: 'DROP_ORB',
}

# ======= 1. Wide sprite scan (100 cols before to 20 after) =======
print("=" * 100)
print("ALL SPRITES in wide approach zone (cols %d to %d)" % (coinCol-100, coinCol+20))
print("=" * 100)

for r in range(mapH):
    for c in range(max(0, coinCol-100), min(mapW, coinCol+20)):
        gid = sp_layer[r][c]
        if gid > 1:
            local_id = gid - 257 if gid >= 257 else gid
            sname = SPRITE_NAMES.get(local_id, f's0x{local_id:02X}')
            adjY = (r - GR) * 16
            dist = c - coinCol
            print(f"  r={r:2d} c={c:4d} adjY={adjY:4d} dist={dist:+4d}  0x{local_id:02X}={sname}")

# ======= 2. Floor profile: topmost solid tile for each column =======
print("\n" + "=" * 100)
print("FLOOR PROFILE (topmost solid tile per column, cols %d to %d)" % (coinCol-30, coinCol+10))
print("=" * 100)

for c in range(max(0, coinCol-30), min(mapW, coinCol+10)):
    # Find highest solid tile (floor the player walks on)
    floor_row = None
    for r in range(mapH):
        gid = tile_layer[r][c]
        if is_solid(gid):
            floor_row = r
            break
    
    if floor_row is not None:
        adjY = (floor_row - GR) * 16
        player_top_y = adjY - 15  # cube standing on this floor, hitbox = 15px
        bar_len = max(0, (adjY - 400) // 8)
        marker = ""
        if c == coinCol:
            marker = " <<< COIN COLUMN"
        print(f"  c={c:4d}: floor at r={floor_row:2d} adjY={adjY:4d} playerTop={player_top_y:4d} {'#'*bar_len}{marker}")
    else:
        marker = " <<< COIN COLUMN" if c == coinCol else ""
        print(f"  c={c:4d}: NO floor (open){marker}")

# ======= 3. Detailed floor analysis near coin =======
print("\n" + "=" * 100)
print("ALL SOLID TILES near coin (rows 38-52, cols %d to %d)" % (coinCol-15, coinCol+10))
print("Coin sprite at row 41, adjY=608. Player ground at adjY=688, playerTop=673")
print("=" * 100)

for r in range(38, min(53, mapH)):
    adjY = (r - GR) * 16
    line_items = []
    for c in range(max(0, coinCol-15), min(mapW, coinCol+10)):
        gid = tile_layer[r][c]
        ct = get_col(gid)
        if ct != 'COL_NONE' and ct != 'EMPTY':
            line_items.append(f"c{c}={ct}")
    if line_items:
        marker = " <<COIN_ROW" if r == coinSpriteRow else ""
        print(f"  r={r:2d} adjY={adjY:4d}: {', '.join(line_items)}{marker}")

# ======= 4. Check for elevated platforms above the floor =======
print("\n" + "=" * 100)
print("PLATFORMS/STEPS in approach (cols %d to %d that have solid tiles between rows 38-45)" % (coinCol-30, coinCol+10))
print("These would be elevated platforms the player could jump to")
print("=" * 100)

for c in range(max(0, coinCol-30), min(mapW, coinCol+10)):
    platforms = []
    for r in range(38, 46):
        gid = tile_layer[r][c]
        if is_solid(gid):
            adjY = (r - GR) * 16
            platforms.append((r, adjY))
    if platforms:
        marker = " <<< COIN COL" if c == coinCol else ""
        for r, adjY in platforms:
            print(f"  c={c:4d} r={r:2d} adjY={adjY:4d} (coin is at adjY=608, {adjY-608:+d}px){marker}")

# ======= 5. Cube jump height analysis =======
print("\n" + "=" * 100)
print("JUMP HEIGHT ANALYSIS")
print("=" * 100)
print(f"  Floor at coin column: row 46, adjY=688")
print(f"  Player top on floor: 688 - 15 = 673")
print(f"  Coin hitbox top: adjY=607 (approx)")
print(f"  Distance from player top to coin top: 673 - 607 = 66px")
print(f"  Standard cube jump height (NES GD): ~64px (4 tiles)")
print(f"  The coin is 66px above the player — JUST beyond normal cube jump height")
print(f"  Player would need: orb, pad, higher starting platform, or different game mode")

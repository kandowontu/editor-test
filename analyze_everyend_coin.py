#!/usr/bin/env python3
"""Analyze everyend level coin at position (35792,607)-(35808,623)"""

import xml.etree.ElementTree as ET

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx"

# Parse TMX
tree = ET.parse(TMX)
root = tree.getroot()
mapW = int(root.get('width'))
mapH = int(root.get('height'))
print(f"Map: {mapW}x{mapH} tiles ({mapW*16}x{mapH*16} px)")

# Ground calculation
groundTileRows = 3
groundRowsToReserve = 3
groundSurface_px = (mapH - groundRowsToReserve) * 16
print(f"groundRowsToReserve={groundRowsToReserve}, groundSurface_px={groundSurface_px}")

# Coin position
coinX, coinY = 35792, 607
coinCol = coinX // 16
coinRow = coinY // 16
print(f"\nCoin at pixel ({coinX},{coinY}), tile col={coinCol} row={coinRow}")
print(f"Player Y=673, tile row={673//16}")
print(f"Coin top Y={coinY}, player top Y=673, diff={673-coinY}px (player is BELOW coin)")

# Parse layers
layers = root.findall('.//layer')
tile_layer = None
sp_layer = None
for layer in layers:
    name = layer.get('name') or ''
    data = layer.find('data')
    if data is not None:
        text = data.text.strip()
        vals = [int(x.strip()) for x in text.split(',') if x.strip()]
        grid = []
        for r in range(mapH):
            grid.append(vals[r*mapW:(r+1)*mapW])
        if name == 'SP':
            sp_layer = grid
        else:
            tile_layer = grid
        print(f"Layer '{name}': parsed {len(grid)} rows x {len(grid[0]) if grid else 0} cols")

# Sprite ID reference
SPRITE_NAMES = {
    0x01: 'SHIP_PORT',
    0x02: 'CUBE_PORT',
    0x03: 'BALL_PORT',
    0x04: 'UFO_PORT',
    0x05: 'WAVE_PORT',
    0x06: 'SPIDER_PORT',
    0x07: 'SECRET_COIN',
    0x08: 'MINI_PORT',
    0x09: 'BIG_PORT',
    0x0A: 'PAD_YELLOW',
    0x0B: 'ORB_YELLOW',
    0x0C: 'PAD_PINK',
    0x0D: 'ORB_PINK',
    0x0E: 'PAD_RED',
    0x0F: 'ORB_RED',
    0x10: 'PAD_BLUE',
    0x11: 'ORB_BLUE',
    0x12: 'PAD_GREEN',
    0x13: 'ORB_GREEN',
    0x14: 'MIRROR_ON',
    0x15: 'MIRROR_OFF',
    0x16: 'DUAL_ON',
    0x17: 'DUAL_OFF',
    0x18: 'SPEED2',
    0x19: 'SPEED1',
    0x1A: 'SPEED3',
    0x1B: 'SPEED4',
    0x1C: 'SPEED0',
    0x1D: 'GRAVITY_UP',
    0x1E: 'GRAVITY_DN',
    0x1F: 'DECO_SPIKE',
    0x20: 'BLACK_ORB',
    0x23: 'DASH_ORB_GREEN',
    0x24: 'DASH_ORB_PINK',
    0x25: 'SPIDER_ORB',
    0x26: 'TELEPORT',
    0x27: 'TRIGGER_ORB',
    0x28: 'SWING_PORT',
    0x29: 'ROBOT_PORT',
    0x2A: 'TOGGLE_ORB',
    0x2B: 'GRAVITY_SWITCH',
    0x2C: 'GRAVITY_RING',
    0x2D: 'DROP_ORB',
}

# Tile type names
TILE_NAMES = {
    0: 'empty',
    1: 'air',
    17: 'solidTL',
    18: 'hazardR',
    27: 'slopeUR',
    28: 'hazardL',
    29: 'slopeDR',
    35: 'solidTR',
    37: 'solidBL',
    46: 'hazardU',
    48: 'solid',
}

# Show area around coin
# Range: 30 cols before to 10 cols after the coin
col_start = max(0, coinCol - 30)
col_end = min(mapW, coinCol + 11)
# Show all rows from 0 to mapH (focus on the relevant area around coin row)
row_start = max(0, coinRow - 10)
row_end = min(mapH, coinRow + 15)

print(f"\n{'='*80}")
print(f"TILE LAYER around coin (cols {col_start}-{col_end-1}, rows {row_start}-{row_end-1})")
print(f"Coin at col={coinCol}, row={coinRow}")
print(f"{'='*80}")

# Print header
hdr = "ROW\\COL"
for c in range(col_start, col_end):
    marker = " *" if c == coinCol else ""
    hdr += f" {c:>5}{marker}"
print(f"  {hdr}")

for r in range(row_start, row_end):
    ypos = r * 16
    marker = " <<COIN_ROW" if r == coinRow else ""
    marker2 = " <<PLAYER_Y" if r == 673//16 else ""
    marker3 = " <<GROUND" if r == mapH - groundRowsToReserve else ""
    line = f"r{r:02d}(y{ypos:>4})"
    for c in range(col_start, col_end):
        val = tile_layer[r][c]
        if val == 1:
            line += "   .  "
        elif val == 0:
            line += "   0  "
        else:
            tn = TILE_NAMES.get(val, f't{val}')
            line += f" {tn:>5}"
    print(f"  {line}{marker}{marker2}{marker3}")

print(f"\n{'='*80}")
print(f"SPRITE LAYER around coin (cols {col_start}-{col_end-1}, rows {row_start}-{row_end-1})")
print(f"{'='*80}")

found_sprites = []
for r in range(row_start, row_end):
    ypos = r * 16
    line = f"r{r:02d}(y{ypos:>4})"
    has_content = False
    for c in range(col_start, col_end):
        val = sp_layer[r][c]
        if val == 0 or val == 1:
            line += "   .  "
        else:
            sn = SPRITE_NAMES.get(val, f's{val:02X}')
            line += f" {sn:>5}"
            has_content = True
            found_sprites.append((r, c, val, sn))
    if has_content:
        marker = " <<COIN_ROW" if r == coinRow else ""
        print(f"  {line}{marker}")

print(f"\n{'='*80}")
print(f"All sprites found in approach zone:")
print(f"{'='*80}")
for r, c, val, sn in found_sprites:
    worldX = c * 16
    worldY = r * 16
    # With groundRowsToReserve, the hitbox Y adjustment:
    # hitTop = (storageTileY - groundRowsToReserve) * TILE + hyoff + pyOff - 1
    adjustedY = (r - groundRowsToReserve) * 16
    print(f"  row={r:2d} col={c:4d}  worldXY=({worldX},{worldY})  adjustedY={adjustedY}  sid=0x{val:02X} = {sn}")

# Now look at a wider range for portals
print(f"\n{'='*80}")
print(f"Portal/mode-change scan: cols {max(0,coinCol-60)} to {coinCol+5}")
print(f"{'='*80}")
portal_cols = range(max(0, coinCol - 60), min(mapW, coinCol + 5))
for r in range(mapH):
    for c in portal_cols:
        val = sp_layer[r][c]
        if val in (0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x08, 0x09, 0x28, 0x29):
            sn = SPRITE_NAMES.get(val, f's{val:02X}')
            print(f"  row={r:2d} col={c:4d} (x={c*16},{r*16}) sid=0x{val:02X} = {sn}")

# Now check floor/ceiling at coin column
print(f"\n{'='*80}")
print(f"Floor/ceiling scan at coin column {coinCol} (and nearby)")
print(f"{'='*80}")
for c in range(max(0, coinCol-5), min(mapW, coinCol+6)):
    floor_row = None
    ceil_row = None
    for r in range(mapH):
        val = tile_layer[r][c]
        if val not in (0, 1):  # solid-ish tile
            if val in (48, 17, 35, 37):  # solid tiles
                if floor_row is None:
                    # Find the first solid from top down for ceiling
                    pass
    # Better approach: scan from top for ceiling, from bottom for floor
    solids = []
    for r in range(mapH):
        val = tile_layer[r][c]
        if val in (48, 17, 35, 37):
            solids.append(r)
    if solids:
        # Ceiling = first solid from top
        # Floor = analyze gaps
        print(f"  col={c}: solid rows = {solids[:8]}{'...' if len(solids)>8 else ''}")
        # Find gap around coin row
        for i in range(len(solids)-1):
            gap_top = solids[i]
            gap_bot = solids[i+1]
            if gap_bot - gap_top > 1:
                gap_top_y = (gap_top - groundRowsToReserve + 1) * 16
                gap_bot_y = (gap_bot - groundRowsToReserve) * 16
                if abs(gap_top_y - coinY) < 200 or abs(gap_bot_y - coinY) < 200:
                    print(f"         gap: row {gap_top+1}-{gap_bot-1} => adjustedY {gap_top_y}-{gap_bot_y}")
    else:
        print(f"  col={c}: NO solid tiles")

# Full column analysis at the coin
print(f"\n{'='*80}")
print(f"Full column data at coin col={coinCol}")
print(f"{'='*80}")
for r in range(mapH):
    tv = tile_layer[r][coinCol]
    sv = sp_layer[r][coinCol]
    tname = TILE_NAMES.get(tv, f't{tv}') if tv not in (0, 1) else ('.' if tv == 1 else '0')
    sname = SPRITE_NAMES.get(sv, f's{sv:02X}') if sv not in (0, 1) else ''
    worldY_raw = r * 16
    adjustedY = (r - groundRowsToReserve) * 16
    if tv not in (0, 1) or sv not in (0, 1):
        print(f"  row={r:2d} rawY={worldY_raw:4d} adjY={adjustedY:4d}  tile={tname:>8}  sprite={sname}")

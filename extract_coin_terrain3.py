#!/usr/bin/env python3
"""Final terrain analysis with proper collision table for everyend.tmx coin route"""
import xml.etree.ElementTree as ET

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx"
tree = ET.parse(TMX)
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

# Parse tile layer
for l in root.findall('.//layer'):
    if l.attrib.get('name', '') != 'SP':
        layer = l
        break
data_elem = layer.find('data')
csv_text = data_elem.text.strip()
values = [int(v.strip()) for v in csv_text.split(',') if v.strip()]
all_tiles = []
for row in range(height):
    start = row * width
    end = start + width
    all_tiles.append(values[start:end])

# Parse SP layer
sp_layer = None
for l in root.findall('.//layer'):
    if l.attrib.get('name') == 'SP':
        sp_layer = l
        break
sp_tiles = []
if sp_layer:
    sp_data = sp_layer.find('data')
    sp_csv = sp_data.text.strip()
    sp_values = [int(v.strip()) for v in sp_csv.split(',') if v.strip()]
    for row in range(height):
        start = row * width
        end = start + width
        sp_tiles.append(sp_values[start:end])

# Full collision table (game_tile_id 0-55, from MetatileCollision.cs)
# TMX tile value -> game tile = TMX_value - 1
ct = ['COL_NONE','COL_FLOOR_CEIL','COL_FLOOR_CEIL','COL_BOTTOM','COL_DEATH_TOP','COL_FLOOR_CEIL','COL_FLOOR_CEIL','COL_NONE',
      'COL_DEATH_BOTTOM','COL_DEATH_BOTTOM','COL_DEATH_TOP','COL_DEATH_TOP','COL_DEATH_BOTTOM','COL_DEATH_TOP','COL_DEATH_LEFT','COL_DEATH_RIGHT',
      'COL_ALL','COL_DEATH','COL_DEATH_BOTTOM','COL_DEATH_BOTTOM','COL_DEATH_TOP','COL_TOP_CENTER_SPIKE','COL_ALL','COL_DEATH_TOP',
      'COL_DEATH_BOTTOM','COL_TOP','COL_DEATH','COL_DEATH','COL_DEATH','COL_DEATH_LEFT','COL_DEATH_TOP','COL_DEATH_RIGHT',
      'COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL',
      'COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_NONE',
      'COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_NONE','COL_NONE','COL_NONE','COL_TOP_CENTER_SPIKE']

def gc(tmx_val):
    """Get collision type for a TMX tile value"""
    if tmx_val <= 0:
        return 'COL_NONE'
    game_id = tmx_val - 1
    if game_id >= len(ct):
        return 'COL_NONE'
    return ct[game_id]

def classify(tmx_val):
    """Classify tile: . = air, # = solid, X = death, ~ = passable-nonzero"""
    c = gc(tmx_val)
    if tmx_val == 0:
        return '.'  # empty
    if c == 'COL_NONE':
        return '.'  # passable (air-equivalent)
    if 'DEATH' in c:
        return 'X'  # deadly
    if c in ('COL_ALL', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM', 'COL_TOP_CENTER_SPIKE'):
        return '#'  # solid
    return '?'

def classify_detail(tmx_val):
    """More detailed classification"""
    c = gc(tmx_val)
    if tmx_val == 0: return 'EMPTY'
    if c == 'COL_NONE': return 'AIR'
    if c == 'COL_ALL': return 'SOLID'
    if c == 'COL_FLOOR_CEIL': return 'FLOOR_CEIL'
    if c == 'COL_TOP': return 'TOP_ONLY'
    if c == 'COL_BOTTOM': return 'BOT_ONLY'
    if c == 'COL_TOP_CENTER_SPIKE': return 'SPIKE_TOP'
    if 'DEATH' in c: return f'DEATH({c.replace("COL_DEATH","").strip("_") or "ALL"})'
    return c

# Key tile reference table
print("=== TILE COLLISION REFERENCE (TMX IDs found in region) ===")
region_tiles = set()
for r in range(35, 56):
    for c in range(2180, 2270):
        region_tiles.add(all_tiles[r][c])

for tid in sorted(region_tiles):
    print(f"  TMX {tid:>3} -> game {tid-1:>3} -> {gc(tid):>25} -> {classify_detail(tid):>15} [{classify(tid)}]")

print()

# GROUND_ROWS offset (everyend.tmx uses 57 rows, need to figure out groundRowsToReserve)
# From previous analyses, groundRowsToReserve varies per level. For height=57, let's check
# by finding where the action is. The user says player is at Y=673 = row 42.
# In the game, gameY = (tmx_row - GROUND_ROWS) * 16
# If player floor is at Y=673 and that's around tmx_row 42:
#   673 = (42 - GR) * 16 -> GR = 42 - 673/16 = 42 - 42.0625 => GR ≈ 0
# But the game uses integer positions, so let's try GR=0:
#   gameY = 42*16 = 672. That's close to 673.
# Let's also try looking at this from the bottom. Height=57, so the last row is 56.
# The game likely allows some rows for ground below the visible area.

GR = 0  # We'll use TMX coordinates directly since GR seems to be ~0 for this level

print("=== VISUAL MAP: Cols 2190-2260, Rows 35-55 ===")
print("  . = air/passable (COL_NONE)    # = solid (COL_ALL etc)")
print("  X = death tile                 C = coin (SP layer)")
print("  D = coin-related (TMX 219)     S = other sprite")
print()

# Header with column numbers (every 5th)
header = "         "
for c in range(2190, 2261):
    if c % 10 == 0:
        header += str(c)[-2:][-1]
    elif c % 5 == 0:
        header += '|'
    else:
        header += ' '
print(header)

# Column number labels
colnum = "         "
for c in range(2190, 2261):
    if c % 10 == 0:
        colnum += '0'
    else:
        colnum += ' '

for tmx_r in range(35, 56):
    game_y = tmx_r * 16
    line = f'r{tmx_r:>2}({game_y:>3}): '
    for c in range(2190, 2261):
        tv = all_tiles[tmx_r][c]
        sv = sp_tiles[tmx_r][c] if sp_tiles else 0
        sid = sv - 257 if sv >= 257 else -1
        
        # Check sprite layer first
        if sid == 0x07 or sid == 0x1A or sid == 0x1B:
            line += 'C'  # coin
        elif sid >= 0:
            line += 'S'  # other sprite
        elif tv == 219:
            line += 'D'  # coin beam/decoration
        else:
            line += classify(tv)
    
    # Add annotations
    label = ""
    if tmx_r == 38:
        label = " <-- estimated coin row (but see actual coin below)"
    elif tmx_r == 41:
        label = " <-- COIN sprite at col 2237"
    elif tmx_r == 42:
        label = " <-- player floor level (Y=672)"
    elif tmx_r == 44:
        label = " <-- TMX 219 (coin beam?) at col 2235"
    elif tmx_r == 45:
        label = " <-- platform surface (row 45)"
    elif tmx_r == 46:
        label = " <-- platform surface (row 46)"
    
    print(line + label)

print()
print("         " + "".join(str(c % 10) for c in range(2190, 2261)))
print("         " + "".join(str((c//10)%10) for c in range(2190, 2261)))
print("    cols: 2190" + " "*(2260-2190-5) + "2260")

# Detailed analysis of the coin area
print("\n=== COIN LOCATION ANALYSIS ===")
print(f"SP layer coin: row 41, col 2237 (X={2237*16}, Y={41*16})")
print(f"TMX 219 tile: row 44, col 2235 (X={2235*16}, Y={44*16})")

# Check what's around the coin at r41, c2237
print(f"\nTiles around coin (r41, c2237):")
for dr in range(-3, 4):
    r = 41 + dr
    for dc in range(-3, 4):
        c = 2237 + dc
        tv = all_tiles[r][c]
        col = gc(tv)
        if col != 'COL_NONE':
            print(f"  r{r} c{c}: TMX {tv} = {classify_detail(tv)}")

# Route analysis
print("\n=== ROUTE ANALYSIS ===")
print("Looking for solid surfaces between rows 38-50 that the player could stand on:")
print("(Only showing non-air tiles)")
for r in range(38, 52):
    solids = []
    deaths = []
    for c in range(2190, 2261):
        tv = all_tiles[r][c]
        cl = classify(tv)
        if cl == '#':
            solids.append((c, tv, gc(tv)))
        elif cl == 'X':
            deaths.append((c, tv, gc(tv)))
    if solids or deaths:
        print(f"\n  Row {r} (Y={r*16}):")
        if solids:
            # Group consecutive columns
            groups = []
            cur = [solids[0]]
            for s in solids[1:]:
                if s[0] == cur[-1][0] + 1:
                    cur.append(s)
                else:
                    groups.append(cur)
                    cur = [s]
            groups.append(cur)
            for g in groups:
                if len(g) <= 3:
                    for c, tv, col in g:
                        print(f"    SOLID: col {c} (X={c*16}) TMX={tv} {col}")
                else:
                    print(f"    SOLID: cols {g[0][0]}-{g[-1][0]} (X={g[0][0]*16}-{g[-1][0]*16}) [{len(g)} tiles]")
        if deaths:
            for c, tv, col in deaths:
                print(f"    DEATH: col {c} (X={c*16}) TMX={tv} {col}")

print("\n=== SUMMARY: PLATFORM SURFACES (top of solid blocks) ===")
print("A platform surface exists where a solid tile has air above it")
for c in range(2190, 2261):
    for r in range(38, 52):
        tv = all_tiles[r][c]
        if classify(tv) == '#':
            # Check if air above
            if r > 0:
                tv_above = all_tiles[r-1][c]
                if classify(tv_above) == '.':
                    print(f"  Platform top: col {c} (X={c*16}), row {r} (Y={r*16}) - surface at Y={r*16}")
            break  # only care about topmost solid per column

print("\n=== 219 TILE CONTEXT ===")
# TMX 219 at r44, c2235
print(f"TMX 219 at row 44, col 2235: collision = {gc(219)} = {classify_detail(219)}")
print(f"This is PASSABLE (COL_NONE), likely a coin beam/light decoration")

# What about the SP coin at r41, c2237?  
print(f"\nSP sprite 264 (sid=7) at row 41, col 2237:")
print(f"  Position: X={2237*16}, Y={41*16}")
print(f"  Underlying tile: TMX {all_tiles[41][2237]} = {classify_detail(all_tiles[41][2237])}")
print(f"  This is the actual COIN collectible")

# Check for jump pads, orbs, portals in the region
print("\n=== SPRITES (PADS/ORBS/PORTALS) IN COLS 2190-2260 ===")
sprite_names = {
    0x00: 'CUBE_PORTAL', 0x01: 'SHIP_PORTAL', 0x02: 'BALL_PORTAL',
    0x03: 'UFO_PORTAL', 0x04: 'WAVE_PORTAL', 0x05: 'SPIDER_PORTAL',
    0x06: 'ROBOT_PORTAL', 0x07: 'COIN', 0x08: 'YELLOW_ORB',
    0x09: 'BLUE_ORB', 0x0A: 'YELLOW_PAD', 0x0B: 'BLUE_PAD',
    0x0C: 'PINK_PAD', 0x0D: 'PINK_ORB', 0x0E: 'GREEN_ORB',
    0x0F: 'GREEN_PAD', 0x10: 'RED_ORB', 0x11: 'RED_PAD',
    0x12: 'DASH_ORB', 0x13: 'REVERSE_GRAVITY',
    0x1A: 'COIN2', 0x1B: 'COIN3',
    0x2C: 'DECO', 0x2D: 'DECO', 0x2E: 'DECO', 0x2F: 'DECO', 0x3D: 'DECO',
}
for r in range(30, 56):
    for c in range(2190, 2261):
        sv = sp_tiles[r][c] if sp_tiles else 0
        sid = sv - 257 if sv >= 257 else -1
        if sid >= 0 and sid not in (0x2C, 0x2D, 0x2E, 0x2F, 0x3D):
            name = sprite_names.get(sid, f'UNKNOWN_{sid:#04x}')
            print(f"  Row {r} (Y={r*16}), Col {c} (X={c*16}): sprite {sid:#04x} = {name}")

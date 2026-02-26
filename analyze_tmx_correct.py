import xml.etree.ElementTree as ET

tree = ET.parse(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\baseafterbase.tmx')
root = tree.getroot()

layer = root.find('.//layer/data')
csv_text = layer.text.strip()
rows = csv_text.split('\n')

# Parse all rows
grid = []
for row_str in rows:
    vals = [int(v.strip()) for v in row_str.split(',') if v.strip()]
    grid.append(vals)

mapH = len(grid)
mapW = len(grid[0])
GROUND_ROWS_TO_RESERVE = 3  # Min(3, groundTileRows=8) for this level

# Collision mapping from MetatileCollision.cs
mapping = """COL_NONE
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
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_TOP
COL_TOP
COL_TOP
COL_TOP
COL_BOTTOM
COL_BOTTOM
COL_BOTTOM
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_RIGHT
COL_DEATH_LEFT
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
COL_DOWN_RIGHT_SPIKE
COL_DEATH_BOTTOM
COL_DOWN_LEFT_SPIKE
COL_DEATH_RIGHT
COL_NONE
COL_DEATH_LEFT
COL_UP_RIGHT_SPIKE
COL_DEATH_TOP
COL_UP_LEFT_SPIKE
COL_DEATH
COL_ALL
COL_DEATH_BOTTOM
COL_NONE
COL_NONE
COL_DEATH
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_RIGHT
COL_LEFT
COL_RIGHT
COL_LEFT
COL_NONE
COL_NO_SIDE
COL_SLOPE_RD45
COL_SLOPE_LD45
COL_SLOPE_RU45
COL_SLOPE_LU45
COL_SLOPE_RD22_RIGHT
COL_SLOPE_RD22_LEFT
COL_SLOPE_LD22_RIGHT
COL_SLOPE_LD22_LEFT
COL_SLOPE_RU22_RIGHT
COL_SLOPE_RU22_LEFT
COL_SLOPE_LU22_RIGHT
COL_SLOPE_LU22_LEFT
COL_SLOPE_RD66_TOP
COL_SLOPE_RD66_BOT
COL_SLOPE_LD66_BOT
COL_SLOPE_LD66_TOP
COL_SLOPE_RU66_TOP
COL_SLOPE_RU66_BOT
COL_SLOPE_LU66_BOT
COL_SLOPE_LU66_TOP
COL_NO_SIDE
COL_NO_SIDE
COL_NO_SIDE
COL_NO_SIDE
COL_LEFT_SPIKE_BLOCK
COL_RIGHT_SPIKE_BLOCK
COL_BOTTOM_LEFT_SPIKE
COL_BOTTOM_RIGHT_SPIKE
COL_BOTTOM_SPIKES
COL_DOWN_LEFT_SPIKE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_BOTH_SPIKES
COL_UP_LEFT
COL_UP_RIGHT
COL_DOWN_LEFT
COL_DOWN_RIGHT
COL_TOP
COL_BOTTOM
COL_LEFT
COL_RIGHT
COL_TOP_LEFT_STAIRS
COL_TOP_RIGHT_STAIRS
COL_BOTTOM_LEFT_STAIRS
COL_BOTTOM_RIGHT_STAIRS
COL_TOP_LEFT_BOTTOM_RIGHT
COL_TOP_RIGHT_BOTTOM_LEFT
COL_UP_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_UP_BOTH_SPIKES
COL_DEATH_TOP_RIGHT
COL_DEATH_TOP_LEFT
COL_DEATH_BOTTOM_RIGHT
COL_DEATH_BOTTOM_LEFT
COL_NONE
COL_NONE
COL_BOTTOM_CENTER_SPIKE
COL_BOTTOM_CENTER_SPIKE
COL_TOP
COL_BOTTOM
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_UP_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_LEFT
COL_RIGHT
COL_UP_RIGHT
COL_RIGHT
COL_TOP
COL_TOP
COL_UP_LEFT
COL_TOP
COL_RIGHT
COL_BOTTOM
COL_TOP
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_UP_LEFT_SPIKE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_DEATH
COL_RIGHT
COL_LEFT
COL_UP_RIGHT_SPIKE
COL_UP_LEFT_SPIKE
COL_DEATH
COL_ALL
COL_LEFT
COL_DOWN_RIGHT
COL_DOWN_LEFT"""

col_lines = [l.strip() for l in mapping.strip().split('\n') if l.strip()]
col_lookup = {}
for i, name in enumerate(col_lines):
    col_lookup[i] = name

def get_col(tmx_val):
    if tmx_val == 0:
        return 'EMPTY'
    return col_lookup.get(tmx_val - 1, '???')

def get_sym(tmx_val):
    if tmx_val == 0: return '.'
    col = col_lookup.get(tmx_val - 1, 'COL_NONE')
    if col == 'COL_NONE': return '.'       # visual only
    if col == 'COL_ALL': return '#'
    if col == 'COL_TOP': return 'T'
    if col == 'COL_BOTTOM': return 'B'
    if col == 'COL_FLOOR_CEIL': return 'F'
    if col == 'COL_DEATH': return 'X'
    if col == 'COL_DEATH_TOP': return '^'
    if col == 'COL_DEATH_BOTTOM': return 'v'
    if col == 'COL_DEATH_LEFT': return '<'
    if col == 'COL_DEATH_RIGHT': return '>'
    if 'SPIKE' in col: return 'S'
    if 'DEATH' in col: return 'D'
    if col == 'COL_NO_SIDE': return '-'
    if col == 'COL_LEFT': return '['
    if col == 'COL_RIGHT': return ']'
    return '~'

def get_tile(game_x, game_y):
    """Get TMX tile value at game coordinates, accounting for groundRowsToReserve offset."""
    tmx_row = game_y + GROUND_ROWS_TO_RESERVE
    if game_x < 0 or game_x >= mapW:
        return -1  # out of bounds
    if tmx_row < 0:
        return -2  # ceiling
    if tmx_row >= mapH:
        return -3  # ground
    return grid[tmx_row][game_x]

print("=" * 100)
print("CORRECTED TILE LAYOUT ANALYSIS: baseafterbase.tmx")
print("groundRowsToReserve = 3  =>  game tileY + 3 = TMX row")
print("Death at X=2931px Y=336px => game tile(183, 21)")
print("Forward probe at (2946, 343) => game tile(184, 21)")
print("TMX height=27, so game Y can be 0..23 (row 3..26 in TMX), Y>=24 = ground")
print("=" * 100)

# Verify the death tile
print("\n--- VERIFY: Death tile mapping ---")
for gx, gy, label in [(183, 21, "Death pixel"), (184, 21, "Forward probe")]:
    tmx_row = gy + 3
    v = grid[tmx_row][gx]
    mt = v - 1 if v > 0 else -1
    col = col_lookup.get(mt, 'EMPTY') if mt >= 0 else 'EMPTY'
    px_range_x = f"{gx*16}-{gx*16+15}"
    px_range_y = f"{gy*16}-{gy*16+15}"
    print(f"  Game({gx},{gy}) -> TMX row {tmx_row}: tmx={v} mt=0x{mt:02X} col={col}")
    print(f"    Pixel range: X=[{px_range_x}] Y=[{px_range_y}]")

# ============================================================
# GAME COORDINATES GRID: tileX=175-195, tileY=16-24
# ============================================================
x_min, x_max = 170, 200
y_min, y_max = 14, 23

print(f"\n{'='*100}")
print(f"TILE GRID IN GAME COORDINATES (x={x_min}-{x_max}, y={y_min}-{y_max})")
print(f"{'='*100}")

# Hex grid
print("\n--- METATILE IDs (hex) ---")
hdr = "       "
for x in range(x_min, x_max + 1):
    hdr += f"{x:4d}"
print(hdr)
for y in range(y_min, y_max + 1):
    line = f"y={y:2d}: "
    for x in range(x_min, x_max + 1):
        v = get_tile(x, y)
        if v == -1: line += " OOB"
        elif v == -2: line += " CEI"
        elif v == -3: line += " GND"
        elif v == 0: line += "   ."
        else: line += f"  {v-1:02X}"
    print(line)

# Collision grid
print("\n--- COLLISION TYPES ---")
hdr = "       "
for x in range(x_min, x_max + 1):
    hdr += f"{x:>6}"
print(hdr)
for y in range(y_min, y_max + 1):
    line = f"y={y:2d}: "
    for x in range(x_min, x_max + 1):
        v = get_tile(x, y)
        if v == -1: line += "   OOB"
        elif v == -2: line += "   CEI"
        elif v == -3: line += "   GND"
        elif v == 0: line += "     ."
        else:
            c = get_col(v)
            short = c.replace('COL_','')[:5]
            line += f"{short:>6}"
    print(line)

# Visual map  
print(f"\n--- VISUAL MAP (game coords) ---")
print("Legend: .=empty #=ALL(solid) T=TOP B=BOT X=DEATH ^=DTH_TOP v=DTH_BOT")
print("       <=DTH_LEFT >=DTH_RIGHT S=spike D=other_death G=ground")
hdr = "     "
for x in range(x_min, x_max + 1):
    hdr += str(x % 10)
print(hdr)
for y in range(y_min, y_max + 1):
    line = f"y{y:2d}: "
    for x in range(x_min, x_max + 1):
        v = get_tile(x, y)
        if v == -1: line += '?'
        elif v == -2: line += 'C'
        elif v == -3: line += 'G'
        else: line += get_sym(v)
    print(line)
    if y == 21:
        mark = "     "
        for x in range(x_min, x_max + 1):
            if x == 183:
                mark += 'D'
            elif x == 184:
                mark += 'P'
            else:
                mark += ' '
        print(mark + "  D=death(183,21) P=fwdProbe(184,21)")

# ============================================================
# KEY TILES DETAIL
# ============================================================
print(f"\n{'='*100}")
print("KEY TILE DETAILS (game coordinates)")
print(f"{'='*100}")

keys = [
    (183, 21, "Death pixel (2931,336)"),
    (184, 21, "Forward probe (2946,343) - the COL_TOP tile"),
    (185, 21, "Right of probe (185,21)"),
    (182, 21, "Left of death (182,21)"),
    (183, 20, "Above death (183,20)"),
    (184, 20, "Above probe (184,20)"),
    (183, 22, "Below death (183,22)"),
    (184, 22, "Below probe (184,22)"),
    (178, 19, "Upper platform left (178,19)"),
    (179, 19, "Upper platform mid (179,19)"),
    (180, 19, "Upper platform right (180,19)"),
    (181, 16, "Floating block left (181,16)"),
    (182, 16, "Floating block right (182,16)"),
    (181, 17, "Under float left (181,17)"),
    (182, 17, "Under float right (182,17)"),
    (170, 19, "Far left platform (170,19)"),
    (173, 19, "Spike area (173,19)"),
    (175, 19, "Spike area (175,19)"),
    (173, 20, "Under spike (173,20)"),
    (175, 20, "Under spike (175,20)"),
    (188, 20, "Right structure (188,20)"),
    (189, 20, "Right structure (189,20)"),
    (193, 20, "Right structure (193,20)"),
    (187, 19, "Right area (187,19)"),
    (190, 19, "Right spikes? (190,19)"),
]

for gx, gy, desc in keys:
    tmx_row = gy + 3
    if tmx_row >= mapH:
        print(f"  Game({gx:3d},{gy:2d}) -> TMX row {tmx_row}: GROUND (below map)  -- {desc}")
        continue
    v = grid[tmx_row][gx]
    if v == 0:
        print(f"  Game({gx:3d},{gy:2d}) -> TMX row {tmx_row}: EMPTY                   -- {desc}")
    else:
        mt = v - 1
        col = col_lookup.get(mt, '???')
        print(f"  Game({gx:3d},{gy:2d}) -> TMX row {tmx_row}: 0x{mt:02X} = {col:<25s} -- {desc}")

# ============================================================
# Floor scan: what's at floor level y=20 (game coords) wide area
# ============================================================
print(f"\n{'='*100}")
print("FLOOR SCAN: All non-empty tiles in game y=16-23, x=165-210")
print(f"{'='*100}")
for gy in range(16, 24):
    tmx_row = gy + 3
    if tmx_row >= mapH:
        print(f"  y={gy}: GROUND (TMX row {tmx_row} >= {mapH})")
        continue
    found = []
    for gx in range(165, 211):
        if gx < mapW:
            v = grid[tmx_row][gx]
            if v != 0:
                mt = v - 1
                col = col_lookup.get(mt, '???')
                found.append(f"x={gx}:0x{mt:02X}({col.replace('COL_','')})")
    if found:
        print(f"  y={gy}: {', '.join(found)}")
    else:
        print(f"  y={gy}: (all empty)")

# ============================================================
# Geometry explanation
# ============================================================
print(f"\n{'='*100}")
print("GEOMETRY ANALYSIS")
print(f"{'='*100}")
print("""
In game coordinates (with groundRowsToReserve=3 offset applied):

STRUCTURE MAP (annotated):

y=14-15: Empty sky
y=16:    Floating 2-tile solid platform at x=181-182 (COL_ALL)
         This is a small ceiling/overhang
y=17:    Death tiles at x=181-182 (COL_DEATH) - spikes hanging below the platform
         Also decorative (COL_NONE) at x=174
y=18:    Decorative (COL_NONE) at x=174, rest empty - open air corridor
y=19:    LEFT SECTION: COL_TOP platform at x=170, DEATH at x=173-175, COL_TOP at x=178-180
         This is a spike pit with platforms on either side
         RIGHT SECTION: DEATH at x=190-191, decorative at x=193
y=20:    LEFT: COL_TOP at x=173-175 (floor under spikes)
         RIGHT: Solid structure at x=188-193 (COL_ALL blocks, 6 tiles wide)
         This is the main floor on the right side
y=21:    LEFT: COL_TOP at x=183-185 *** THIS IS THE DEATH LOCATION ***
         RIGHT: solid (COL_ALL) at x=193 continues, more structure at x=188-192
         Also: COL_ALL at x=198+
y=22:    Mostly ground features. COL_ALL/NONE pattern at x=188+
y=23:    Ground level (TMX row 26) - continuous death/solid tiles

THE DEATH SCENARIO:
- The cube is at X=2931px (tileX=183), Y=336px (tileY=21)
- At game tile (183,21) -> TMX row 24: tile 0x19 = COL_TOP
- The forward probe at (2946,343) -> game tile (184,21) -> TMX row 24: also 0x19 = COL_TOP
- COL_TOP means solid when localY < 8 (top half of 16px tile)
- localY for the probe = 343 % 16 = 7, which is < 8, so it's SOLID
- The cube hits the TOP surface of the platform at game row 21
""")

# What's the walkSurv=1 situation?
print("WALKSURV=1 ANALYSIS:")
print("The walk path from the death area (x~183, y=21):")
for gx in range(181, 196):
    v = get_tile(gx, 21)
    col = get_col(v)
    v_above = get_tile(gx, 20)
    col_above = get_col(v_above)
    v_below = get_tile(gx, 22)
    col_below = get_col(v_below)
    print(f"  x={gx}: y=20={col_above:<20s} y=21={col:<20s} y=22={col_below}")

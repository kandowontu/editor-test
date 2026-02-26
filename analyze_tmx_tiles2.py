import xml.etree.ElementTree as ET

tree = ET.parse(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\baseafterbase.tmx')
root = tree.getroot()

layer = root.find('.//layer/data')
csv_text = layer.text.strip()
rows = csv_text.split('\n')

# Build collision lookup from the mapping in MetatileCollision.cs
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
    if col == 'COL_NONE': return '.'
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

# Parse all rows
grid = []
for row_str in rows:
    vals = [int(v.strip()) for v in row_str.split(',') if v.strip()]
    grid.append(vals)

x_min, x_max = 170, 200
y_min, y_max = 16, 26

print("=" * 80)
print("TILE LAYOUT ANALYSIS: baseafterbase.tmx")
print("Death at X=2931px Y=336px => tileX=183, tileY=21")
print("Debug says tile(184,21) tid=0x19 => possible +1 offset in game coords")
print("=" * 80)

# --- HEX TILE ID TABLE ---
print("\n--- METATILE IDs (hex) ---")
hdr = "     "
for x in range(x_min, x_max + 1):
    hdr += f"{x:4d}"
print(hdr)
for y in range(y_min, y_max + 1):
    line = f"y{y:2d}: "
    for x in range(x_min, x_max + 1):
        v = grid[y][x]
        if v == 0:
            line += "   ."
        else:
            line += f"  {v-1:02X}"
    print(line)

# --- COLLISION TABLE ---
print("\n--- COLLISION TYPES ---")
hdr = "     "
for x in range(x_min, x_max + 1):
    hdr += f"{x:>6}"
print(hdr)
for y in range(y_min, y_max + 1):
    line = f"y{y:2d}: "
    for x in range(x_min, x_max + 1):
        c = get_col(grid[y][x])
        if c == 'EMPTY':
            line += "     ."
        else:
            short = c.replace('COL_','')[:5]
            line += f"{short:>6}"
        
    print(line)

# --- SYMBOL MAP ---
print("\n--- VISUAL MAP ---")
print("Legend: .=empty #=ALL(solid) T=TOP B=BOT X=DEATH ^=DTH_TOP v=DTH_BOT")
print("       <=DTH_LEFT >=DTH_RIGHT S=spike D=other_death -=NO_SIDE")
hdr = "     "
for x in range(x_min, x_max + 1):
    hdr += str(x % 10)
print(hdr)
for y in range(y_min, y_max + 1):
    line = f"y{y:2d}: "
    for x in range(x_min, x_max + 1):
        line += get_sym(grid[y][x])
    print(line)

# --- KEY TILE INFO ---
print("\n--- KEY TILE DETAILS ---")
key_tiles = [
    (183, 21, "Death pixel X=2931 (floor(2931/16)=183)"),
    (184, 21, "Game-reported tile(184,21)"),
    (178, 22, "Platform tile (178,22)"),
    (179, 22, "Platform tile (179,22)"),
    (180, 22, "Platform tile (180,22)"),
    (181, 19, "Upper block (181,19)"),
    (182, 19, "Upper block (182,19)"),
    (181, 20, "Under upper block (181,20)"),
    (182, 20, "Under upper block (182,20)"),
    (175, 22, "Left edge (175,22)"),
    (175, 23, "Left edge (175,23)"),
    (189, 23, "Right structure (189,23)"),
    (190, 22, "Spike area? (190,22)"),
]

for tx, ty, desc in key_tiles:
    v = grid[ty][tx]
    if v == 0:
        print(f"  ({tx:3d},{ty:2d}): EMPTY               -- {desc}")
    else:
        mt = v - 1
        col = col_lookup.get(mt, '???')
        print(f"  ({tx:3d},{ty:2d}): 0x{mt:02X} = {col:<25s} -- {desc}")

# --- SCAN WIDER for 0x19 tiles near death Y ---
print("\n--- SCAN: All non-empty tiles in y=19-24, x=170-210 ---")
for y in range(19, 25):
    for x in range(170, 211):
        if x < len(grid[y]):
            v = grid[y][x]
            if v != 0:
                mt = v - 1
                col = col_lookup.get(mt, '???')
                print(f"  ({x:3d},{y:2d}): tmx={v:3d} mt=0x{mt:02X} col={col}")

# --- Explain pixel→tile mapping ---
print("\n--- PIXEL TO TILE ANALYSIS ---")
print("Death at X=2931, Y=336:")
print(f"  floor(2931/16) = {2931//16}, 2931%16 = {2931%16}")
print(f"  floor(336/16)  = {336//16},  336%16  = {336%16}")
print(f"  -> TMX tile ({2931//16}, {336//16})")
print()
print("Forward probe at X=2946, Y=343:")
print(f"  floor(2946/16) = {2946//16}, 2946%16 = {2946%16}")
print(f"  floor(343/16)  = {343//16},  343%16  = {343%16}")
print(f"  -> TMX tile ({2946//16}, {343//16})")
print()

# Check if there's a +1 offset (game tile = TMX tile + 1?)
print("If game uses tile = floor(px/16)+1:")
gx = 2931 // 16 + 1  # 184
gy = 336 // 16       # 21 (still 21, since 336%16=0)
# But that would mean game tile 184 = TMX col 183
# Let's check TMX col 183 at row 21
tmx_x = gx - 1  # 183
v = grid[21][tmx_x]
print(f"  Game tile (184,21) -> TMX col 183: tmx_val={v}, ", end="")
if v == 0:
    print("EMPTY")
else:
    print(f"mt=0x{v-1:02X} col={col_lookup.get(v-1,'???')}")

# Check +1 on both axes
# Maybe game y is also +1?
for off_label, ox, oy in [
    ("TMX(183,21)", 183, 21),
    ("TMX(184,21)", 184, 21),
    ("TMX(183,20)", 183, 20),
    ("TMX(184,20)", 184, 20),
    ("TMX(183,22)", 183, 22),
    ("TMX(184,22)", 184, 22),
]:
    v = grid[oy][ox]
    if v == 0:
        print(f"  {off_label}: EMPTY")
    else:
        print(f"  {off_label}: 0x{v-1:02X} = {col_lookup.get(v-1,'???')}")

# Look for 0x19 (COL_TOP) tiles anywhere near the death area
print("\n--- ALL 0x19 (COL_TOP) tiles near death, x=175-195 ---")
for y in range(16, 27):
    for x in range(175, 196):
        v = grid[y][x]
        if v - 1 == 0x19:  # metatile 0x19
            print(f"  TMX({x},{y}) = 0x19 (COL_TOP)  pixel: ({x*16},{y*16})-({x*16+15},{y*16+15})")

import xml.etree.ElementTree as ET
import sys

tree = ET.parse(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\baseafterbase.tmx')
root = tree.getroot()

layer = root.find('.//layer/data')
csv_text = layer.text.strip()
rows = csv_text.split('\n')

# Collision mapping from MetatileCollision.cs static constructor
# Index 0..255 in order
mapping_lines = """COL_NONE
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

col_lines = [l.strip() for l in mapping_lines.strip().split('\n') if l.strip()]
col_lookup = {}
for i, name in enumerate(col_lines):
    col_lookup[i] = name

# Short names for display
SHORT = {
    'COL_NONE': '  .  ',
    'COL_ALL': ' ALL ',
    'COL_TOP': ' TOP ',
    'COL_BOTTOM': ' BOT ',
    'COL_FLOOR_CEIL': 'FLCL ',
    'COL_DEATH': 'DEATH',
    'COL_DEATH_TOP': 'DTH_T',
    'COL_DEATH_BOTTOM': 'DTH_B',
    'COL_DEATH_LEFT': 'DTH_L',
    'COL_DEATH_RIGHT': 'DTH_R',
    'COL_TOP_CENTER_SPIKE': 'T_CSP',
    'COL_BOTTOM_CENTER_SPIKE': 'B_CSP',
    'COL_NO_SIDE': 'NOSID',
    'COL_LEFT': ' LFT ',
    'COL_RIGHT': ' RGT ',
    'COL_UP_LEFT_SPIKE': 'UL_SP',
    'COL_UP_RIGHT_SPIKE': 'UR_SP',
    'COL_DOWN_LEFT_SPIKE': 'DL_SP',
    'COL_DOWN_RIGHT_SPIKE': 'DR_SP',
    'COL_SLOPE_RD45': 'SRD45',
    'COL_SLOPE_LD45': 'SLD45',
}

def get_short(col_name):
    if col_name in SHORT:
        return SHORT[col_name]
    return col_name.replace('COL_','')[:5]

ncols = len(rows[0].split(','))
print(f'TMX: {len(rows)} rows x {ncols} cols')
print()

# ==========================================
# TILE ID GRID
# ==========================================
print('=== TILE ID GRID (hex metatile index, --- = empty) ===')
header = '       '
for x in range(175, 196):
    header += f' x{x:<4d}'
print(header)

for y in range(16, 25):
    cols = rows[y].split(',')
    line = f'y={y:2d} | '
    for x in range(175, 196):
        tmx_val = int(cols[x].strip())
        if tmx_val == 0:
            line += '  --- '
        else:
            metatile = tmx_val - 1
            line += f' 0x{metatile:02X} '
    print(line)

# ==========================================
# COLLISION TYPE GRID
# ==========================================
print()
print('=== COLLISION TYPE GRID ===')
header = '       '
for x in range(175, 196):
    header += f' x{x:<4d}'
print(header)

for y in range(16, 25):
    cols = rows[y].split(',')
    line = f'y={y:2d} | '
    for x in range(175, 196):
        tmx_val = int(cols[x].strip())
        if tmx_val == 0:
            line += '  .   '
        else:
            metatile = tmx_val - 1
            col_name = col_lookup.get(metatile, '???')
            s = get_short(col_name)
            line += f'{s:>6}'
    print(line)

# ==========================================
# KEY TILES DETAIL
# ==========================================
print()
print('=== KEY TILE DETAILS ===')
checks = [
    ('DEATH tile (184,21)', 184, 21),
    ('Forward probe (2946px/16=184.1 -> tile 184, 343px/16=21.4 -> tile 21)', 184, 21),
    ('Floor wall (181,23)', 181, 23),
    ('Floor wall (182,23)', 182, 23),
    ('Below death (184,22)', 184, 22),
    ('Above death (184,20)', 184, 20),
    ('Left of death (183,21)', 183, 21),
    ('Right of death (185,21)', 185, 21),
]

for label, tx, ty in checks:
    cols = rows[ty].split(',')
    tmx_val = int(cols[tx].strip())
    if tmx_val == 0:
        print(f'{label}: EMPTY (tmx=0)')
    else:
        metatile = tmx_val - 1
        col_name = col_lookup.get(metatile, '???')
        print(f'{label}: tmx_raw={tmx_val}, metatile=0x{metatile:02X}, collision={col_name}')

# ==========================================
# VISUAL MAP with symbols
# ==========================================
print()
print('=== VISUAL MAP (175-195, y16-24) ===')
print('Legend: . = empty, # = ALL(solid), T = TOP, B = BOT, X = DEATH, ^ = DEATH_TOP, v = DTH_BOT')
print('        < = DTH_LEFT, > = DTH_RIGHT, S = spike variant, F = FLOOR_CEIL, ~ = other')
print()

def get_symbol(metatile):
    if metatile < 0:
        return '.'
    col_name = col_lookup.get(metatile, 'COL_NONE')
    if col_name == 'COL_NONE': return '.'
    if col_name == 'COL_ALL': return '#'
    if col_name == 'COL_TOP': return 'T'
    if col_name == 'COL_BOTTOM': return 'B'
    if col_name == 'COL_FLOOR_CEIL': return 'F'
    if col_name == 'COL_DEATH': return 'X'
    if col_name == 'COL_DEATH_TOP': return '^'
    if col_name == 'COL_DEATH_BOTTOM': return 'v'
    if col_name == 'COL_DEATH_LEFT': return '<'
    if col_name == 'COL_DEATH_RIGHT': return '>'
    if 'SPIKE' in col_name: return 'S'
    if 'DEATH' in col_name: return 'D'
    if 'SLOPE' in col_name: return '/'
    if col_name == 'COL_LEFT': return '['
    if col_name == 'COL_RIGHT': return ']'
    if col_name == 'COL_NO_SIDE': return '-'
    return '~'

header = '       '
for x in range(175, 196):
    header += f'{x%10}'
print(header)

for y in range(16, 25):
    cols = rows[y].split(',')
    line = f'y={y:2d} | '
    for x in range(175, 196):
        tmx_val = int(cols[x].strip())
        metatile = (tmx_val - 1) if tmx_val > 0 else -1
        line += get_symbol(metatile)
    print(line)
    if y == 21:
        # Mark death position
        line2 = '  *    '
        for x in range(175, 196):
            if x == 184:
                line2 += '*'  # death at x=184, y=21
            else:
                line2 += ' '
        print(line2 + '  <-- death at (184,21)')

# Also check 0x19 specifically
print()
print(f'=== Tile 0x19 collision = {col_lookup.get(0x19, "???")} ===')
print(f'    COL_TOP means: solid only when localY < 8 (top half of the 16x16 tile)')
print(f'    At Y=336px: tileY=336/16=21, localY=336%16=0  -> localY=0 < 8 -> SOLID (blocks from above)')
print(f'    At Y=343px: tileY=343/16=21, localY=343%16=7  -> localY=7 < 8 -> SOLID')
print(f'    At Y=344px: tileY=344/16=21, localY=344%16=8  -> localY=8 >= 8 -> NOT solid (passable)')

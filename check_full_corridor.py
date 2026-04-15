"""Full corridor terrain analysis: rows 10-22, cols 450-570, using CORRECT C# collision table."""
import xml.etree.ElementTree as ET

# CORRECT collision table from MetatileCollision.cs static constructor
COLLISION_TABLE = [0]*256
def s(tid, col): COLLISION_TABLE[tid] = col

# From MetatileCollision.cs - exact mappings
COL_NONE=0; COL_FLOOR_CEIL=1; COL_ALL=2; COL_DEATH=3; COL_DEATH_TOP=4; COL_DEATH_BOTTOM=5
COL_DEATH_LEFT=6; COL_DEATH_RIGHT=7; COL_TOP=8; COL_NO_SIDE=9; COL_DEATH_BOTTOM_RIGHT=10
COL_DEATH_BOTTOM_LEFT=11; COL_DEATH_TOP_RIGHT=12; COL_DEATH_TOP_LEFT=13
COL_SLOPE_RD45=14; COL_SLOPE_RU45=15; COL_SLOPE_LU45=16; COL_SLOPE_LD45=17
COL_SLOPE_RD22_BOT=18; COL_SLOPE_RD22_TOP=19; COL_SLOPE_RU22_BOT=20; COL_SLOPE_RU22_TOP=21
COL_SLOPE_LU22_BOT=22; COL_SLOPE_LU22_TOP=23; COL_SLOPE_LD22_BOT=24; COL_SLOPE_LD22_TOP=25
COL_SLOPE_RD66_BOT=26; COL_SLOPE_RD66_TOP=27; COL_SLOPE_RU66_BOT=28; COL_SLOPE_RU66_TOP=29
COL_SLOPE_LU66_BOT=30; COL_SLOPE_LU66_TOP=31; COL_DOWN_LEFT_SPIKE=32; COL_TOP_RIGHT_STAIRS=33

COL_SLOPE_LD66_BOT=34; COL_SLOPE_LD66_TOP=35

COL_NAMES = {
    0:'NONE', 1:'FC', 2:'ALL', 3:'DEATH', 4:'D_TOP', 5:'D_BOT', 6:'D_LEFT', 7:'D_RIGHT',
    8:'TOP', 9:'NO_SIDE', 10:'D_BR', 11:'D_BL', 12:'D_TR', 13:'D_TL',
    14:'sRD45', 15:'sRU45', 16:'sLU45', 17:'sLD45',
    18:'sRD22b', 19:'sRD22t', 20:'sRU22b', 21:'sRU22t',
    22:'sLU22b', 23:'sLU22t', 24:'sLD22b', 25:'sLD22t',
    26:'sRD66b', 27:'sRD66t', 28:'sRU66b', 29:'sRU66t',
    30:'sLU66b', 31:'sLU66t', 32:'DL_SPIKE', 33:'TR_STRS',
    34:'sLD66b', 35:'sLD66t'
}

# From MetatileCollision.cs static constructor - all 256 entries
s(0x01, COL_FLOOR_CEIL)
s(0x02, COL_ALL)
s(0x03, COL_DEATH)
s(0x04, COL_DEATH_TOP)
s(0x05, COL_DEATH_BOTTOM)
s(0x06, COL_DEATH_LEFT)
s(0x07, COL_NONE)  # CORRECT: was wrongly DEATH in old table
s(0x08, COL_DEATH_RIGHT)
s(0x09, COL_FLOOR_CEIL)
s(0x0A, COL_TOP)
s(0x0B, COL_ALL)
s(0x0C, COL_ALL)
s(0x0D, COL_ALL)
s(0x0E, COL_ALL)
s(0x0F, COL_ALL)
s(0x10, COL_ALL)
s(0x11, COL_ALL)
s(0x12, COL_ALL)
s(0x13, COL_ALL)
s(0x14, COL_ALL)
s(0x15, COL_ALL)
s(0x16, COL_ALL)
s(0x17, COL_ALL)
s(0x18, COL_ALL)
s(0x19, COL_ALL)
s(0x1A, COL_ALL)
s(0x1B, COL_NONE)
s(0x1C, COL_DEATH)
s(0x1D, COL_TOP)
s(0x1E, COL_ALL)
s(0x1F, COL_ALL)
s(0x20, COL_DEATH_BOTTOM)

# Tiles 0x21-0x3F
s(0x21, COL_DEATH_BOTTOM)
s(0x22, COL_DEATH_BOTTOM)
s(0x23, COL_DEATH_BOTTOM)
s(0x24, COL_DEATH_BOTTOM)
s(0x25, COL_DEATH_BOTTOM)
s(0x26, COL_NONE)
s(0x27, COL_DEATH_BOTTOM)
s(0x28, COL_DEATH_BOTTOM)
s(0x29, COL_NONE)
s(0x2A, COL_NONE)
s(0x2B, COL_NONE)
s(0x2C, COL_NONE)
s(0x2D, COL_NONE)
s(0x2E, COL_DEATH_BOTTOM)
s(0x2F, COL_DEATH_BOTTOM)
s(0x30, COL_DEATH_BOTTOM)
s(0x31, COL_NONE)
s(0x32, COL_NONE)
s(0x33, COL_NONE)
s(0x34, COL_NONE)
s(0x35, COL_DEATH_BOTTOM)
s(0x36, COL_ALL)
s(0x37, COL_ALL)
s(0x38, COL_ALL)

# Slopes 0x39-0x50
s(0x39, COL_SLOPE_RD45)
s(0x3A, COL_SLOPE_RU45)
s(0x3B, COL_SLOPE_LU45)
s(0x3C, COL_SLOPE_LD45)
s(0x3D, COL_SLOPE_RD22_BOT)
s(0x3E, COL_SLOPE_RD22_TOP)
s(0x3F, COL_SLOPE_RU22_BOT)
s(0x40, COL_SLOPE_RU22_TOP)
s(0x41, COL_SLOPE_LU22_BOT)
s(0x42, COL_SLOPE_LU22_TOP)
s(0x43, COL_SLOPE_LD22_BOT)
s(0x44, COL_SLOPE_LD22_TOP)
s(0x45, COL_SLOPE_RD66_BOT)
s(0x46, COL_SLOPE_RD66_TOP)
s(0x47, COL_SLOPE_RU66_BOT)
s(0x48, COL_SLOPE_RU66_TOP)
s(0x49, COL_SLOPE_LU66_BOT)
s(0x4A, COL_SLOPE_LU66_TOP)
s(0x4B, COL_SLOPE_LD66_BOT)
s(0x4C, COL_SLOPE_LD66_TOP)

# More tiles
s(0x4D, COL_NONE)
s(0x4E, COL_NONE)
s(0x4F, COL_NO_SIDE)
s(0x50, COL_NO_SIDE)
s(0x51, COL_NONE)
s(0x52, COL_NONE)
s(0x53, COL_NONE)
s(0x54, COL_NONE)
s(0x55, COL_NONE)
s(0x56, COL_NONE)
s(0x57, COL_NONE)
s(0x58, COL_DEATH_BOTTOM_RIGHT)
s(0x59, COL_DEATH_BOTTOM_LEFT)
s(0x5A, COL_DEATH_TOP_RIGHT)
s(0x5B, COL_DEATH_TOP_LEFT)
s(0x5C, COL_DOWN_LEFT_SPIKE)
s(0x5D, COL_TOP_RIGHT_STAIRS)
s(0x5E, COL_ALL)
s(0x5F, COL_ALL)
# 0x60+ are mostly NONE
for i in range(0x60, 0x100):
    COLLISION_TABLE[i] = COL_NONE

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()
map_w = int(root.get('width'))
map_h = int(root.get('height'))

layers = {}
for layer in root.findall('layer'):
    name = layer.get('name')
    data = layer.find('data')
    tiles = list(map(int, data.text.strip().split(',')))
    layers[name] = tiles

bg = layers.get('BG', [])

# Also check SP layer for sprites/portals in the corridor area
sp = layers.get('SP', [])

print(f"Map: {map_w}x{map_h}")
print()

# Print terrain for rows 10-22, cols 490-572 (around the transition and coin areas)
print("=== TERRAIN MAP (rows 10-22, cols 490-572) ===")
print(f"{'Row':>4}", end='')
for c in range(490, 572, 2):
    print(f" {c:>7}", end='')
print()

for r in range(10, 23):
    print(f"r{r:>2}:", end='')
    for c in range(490, 572, 2):
        idx = r * map_w + c
        if idx < len(bg):
            tid = bg[idx]
            col = COLLISION_TABLE[tid & 0xFF]
            name = COL_NAMES.get(col, f'?{col}')
        else:
            name = 'OOB'
        print(f" {name:>7}", end='')
    print()

print()

# Detailed col 505-512 for the transition
print("=== DETAILED TRANSITION (cols 505-515, rows 12-20) ===")
for r in range(12, 21):
    print(f"r{r:>2}:", end='')
    for c in range(505, 516):
        idx = r * map_w + c
        if idx < len(bg):
            tid = bg[idx]
            col = COLLISION_TABLE[tid & 0xFF]
            name = COL_NAMES.get(col, f'?{col}')
        else:
            name = 'OOB'
        print(f" {name:>7}", end='')
    print()

print()

# Find ALL sprites in X range 7200-9200
print("=== SPRITES col 450-580 (X=7200-9280) ===")
FIRSTGID = 257
for r in range(0, map_h):
    for c in range(450, 580):
        idx = r * map_w + c
        if idx < len(sp):
            raw = sp[idx]
            if raw > 0:
                sid = (raw & 0x1FF) - FIRSTGID  # strip flip bits, subtract firstgid
                flip = (raw >> 28) & 0xF
                x = c * 16
                y = (r - 5) * 16  # approximate world Y (groundRowsToReserve ~5)
                print(f"  col={c} row={r} X={x} Y~{y} sid=0x{sid:02X}({sid}) raw=0x{raw:08X} flip={flip:X}")

print()

# Check what row the coin is at - coin at X=9064, Y=248
# X=9064 / 16 = 566.5 → col 566
# Y=248 / 16 = 15.5 → row 15 in world coords. With groundRows=5, TMX row = 15+5=20? or different?
# Let me check all possible groundRows interpretations
print("=== COIN POSITION ANALYSIS ===")
print(f"Coin X=9064, Y=248")
print(f"Tile col = {9064//16} (X={9064//16*16})")
print(f"World row (Y/16) = {248//16} (Y={248//16*16})")
for gr in range(0, 10):
    tmx_r = 248 // 16 + gr
    print(f"  groundRowsToReserve={gr} → TMX row={tmx_r}")
    if tmx_r < map_h:
        idx = tmx_r * map_w + 566
        if idx < len(bg):
            tid = bg[idx]
            col = COLLISION_TABLE[tid & 0xFF]
            name = COL_NAMES.get(col, f'?{col}')
            print(f"    bg tile=0x{tid:02X} col={name}")

import xml.etree.ElementTree as ET
import re

# Load collision table
with open(r'C:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()
start = content.find('mappingText = @"') + len('mappingText = @"')
end = content.find('";', start)
mapping_text = content[start:end]
lines = [l for l in mapping_text.split('\n') if l.strip()]
rx = re.compile(r'COL_[A-Z0-9_]+')
coll_table = ['COL_NONE'] * 256
for idx, raw in enumerate(lines):
    if idx >= 256: break
    m = rx.search(raw.strip())
    if m:
        coll_table[idx] = m.group()

# Sprite name mapping
SPRITE_NAMES = {
    0x00: 'CubePortal', 0x01: 'ShipPortal', 0x02: 'BallPortal', 0x03: 'UFOPortal',
    0x04: 'WavePortal', 0x05: 'BlueOrb', 0x06: 'PinkOrb', 0x07: 'Coin',
    0x08: 'NormGrav', 0x09: 'RevGrav', 0x0A: 'YellowPad', 0x0B: 'YellowOrb',
    0x0C: 'YellowPad2', 0x0D: 'BluePadBot', 0x0E: 'BluePadTop', 0x0F: 'EndLevel',
    0x10: 'NormGrav2', 0x11: 'NormGrav3', 0x12: 'RevGrav2', 0x13: 'RevGrav3',
    0x14: 'Speed0.5x', 0x15: 'Speed1x', 0x16: 'Speed2x', 0x17: 'RobotPortal',
    0x18: 'MiniPortal', 0x19: 'GrowthPortal', 0x1A: 'Coin2', 0x1B: 'Coin3',
    0x1F: 'YellowOrbBig', 0x20: 'Speed3x', 0x21: 'Speed4x',
    0x24: 'SpiderPortal', 0x25: 'PinkPad', 0x26: 'PinkPad2',
    0x27: 'GreenOrb', 0x28: 'RedOrb', 0x29: 'YellowOrbSmall',
    0x2D: 'Spr45', 0x3D: 'Spr61',
    0x44: 'BlackOrb', 0x4B: 'SwingPortal',
    0x52: 'RedPad', 0x53: 'RedPad2', 0x58: 'NinjaPortal',
    0x65: 'GreenPad', 0x6A: 'PogoPortal', 0x6B: 'SnakePortal', 0x6C: 'FootballPortal',
    0x6D: 'SpeedSlow',
    0x91: 'Spr145', 0xA1: 'Spr161', 0xA5: 'Spr165', 0x81: 'Spr129',
}

# Load TMX
tmxpath = r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep.tmx'
tree = ET.parse(tmxpath)
root = tree.getroot()
layers = root.findall('layer')

TILES_FIRST_GID = 1
SPRITE_FIRST_GID = 257

# Parse tiles layer (layer 0)
tile_data = layers[0].find('data').text.strip()
tile_rows = [r.strip() for r in tile_data.split('\n') if r.strip()]

# Parse SP layer (layer 1) 
sp_data = layers[1].find('data').text.strip()
sp_rows = [r.strip() for r in sp_data.split('\n') if r.strip()]

map_w = int(root.attrib['width'])
map_h = int(root.attrib['height'])

def get_coll_char(coll):
    if coll == 'COL_NONE': return '.'
    if coll == 'COL_ALL': return 'A'
    if coll == 'COL_TOP': return 'T'
    if coll == 'COL_BOTTOM': return 'B'
    if coll == 'COL_FLOOR_CEIL': return 'F'
    if 'DEATH' in coll: return 'D'
    if 'SPIKE' in coll: return 's'
    if 'SLOPE' in coll: return '/'
    if coll in ('COL_LEFT','COL_RIGHT'): return '|'
    return 'X'

# Print terrain map for ball section: cols 400-470, rows 25-38
print("=== TERRAIN MAP (cols 400-470, rows 25-38) ===")
col_start, col_end = 400, 471
row_start, row_end = 25, 39

# Column headers
h1 = "wtY tmxR | "
for c in range(col_start, col_end):
    h1 += str((c // 100) % 10) if c % 10 == 0 else " "
print(h1)
h2 = "         | "
for c in range(col_start, col_end):
    h2 += str((c // 10) % 10) if c % 10 == 0 else " "
print(h2)
h3 = "         | "
for c in range(col_start, col_end):
    h3 += str(c % 10)
print(h3)
print("---------+" + "-" * (col_end - col_start))

for r in range(row_start, row_end):
    wty = r - 3
    line = f" {wty:2d}  {r:2d}  | "
    cols = tile_rows[r].split(',')
    for c in range(col_start, col_end):
        gid = int(cols[c]) if c < len(cols) else 0
        if gid == 0:
            line += '.'
        else:
            editor = gid - TILES_FIRST_GID
            coll = coll_table[editor] if 0 <= editor < 256 else 'COL_NONE'
            line += get_coll_char(coll)
    print(line)

# Print sprite map for same range
print("\n=== SPRITE MAP (cols 400-470, rows 25-38) ===")
print("         | ", end="")
for c in range(col_start, col_end):
    print(str(c % 10), end="")
print()
print("---------+" + "-" * (col_end - col_start))

for r in range(row_start, row_end):
    wty = r - 3
    line = f" {wty:2d}  {r:2d}  | "
    cols = sp_rows[r].split(',')
    for c in range(col_start, col_end):
        gid = int(cols[c]) if c < len(cols) else 0
        if gid == 0:
            line += '.'
        else:
            sid = gid - SPRITE_FIRST_GID
            if sid == 0x05: line += 'B'  # Blue orb
            elif sid == 0x06: line += 'P'  # Pink orb
            elif sid == 0x0B: line += 'Y'  # Yellow orb
            elif sid == 0x25: line += 'p'  # Pink pad
            elif sid == 0x0A or sid == 0x0C: line += 'y'  # Yellow pad
            elif sid == 0x09 or sid == 0x12 or sid == 0x13: line += 'R'  # Rev grav
            elif sid == 0x08 or sid == 0x10 or sid == 0x11: line += 'N'  # Norm grav
            elif sid in (0x00,0x01,0x02,0x03,0x04,0x17,0x24,0x4B,0x58,0x6A,0x6B,0x6C): line += 'M'  # Mode portal
            elif sid == 0x18: line += 'm'  # Mini
            elif sid == 0x19: line += 'G'  # Growth
            elif sid == 0x16: line += '2'  # Speed 2x
            elif sid == 0x0F: line += 'E'  # End
            elif sid in (0x07, 0x1A, 0x1B): line += 'C'  # Coin
            else: line += f'{sid:1X}' if sid < 16 else '*'
        line_str = line
    print(line)

# Print sprite details
print("\n=== SPRITE DETAILS (cols 400-470, rows 25-38) ===")
for r in range(row_start, row_end):
    cols = sp_rows[r].split(',')
    for c in range(col_start, col_end):
        gid = int(cols[c]) if c < len(cols) else 0
        if gid > 0:
            sid = gid - SPRITE_FIRST_GID
            name = SPRITE_NAMES.get(sid, f'Unknown_{sid}')
            wty = r - 3
            print(f"  tmxRow={r} wTileY={wty} col={c} X={c*16}px sprite=0x{sid:02X}({sid}) {name}")

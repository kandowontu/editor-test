import xml.etree.ElementTree as ET
import csv, io

tree = ET.parse(r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep.tmx")
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

layers = root.findall('layer')
data = layers[0].find('data')
reader = csv.reader(io.StringIO(data.text.strip()))
tiles = []
for row in reader:
    for val in row:
        val = val.strip()
        if val:
            tiles.append(int(val))

# Parse collision table entries (sequential)
col_names_raw = open(r"C:\Editor Test\native-windows\MetatileCollision.cs").read()
import re
# Extract the mappingText string
m = re.search(r'mappingText\s*=\s*@"(.*?)"', col_names_raw, re.DOTALL)
mapping_text = m.group(1)
col_regex = re.compile(r'COL_[A-Z0-9_]+')
collision_table = {}
idx = 0
for line in mapping_text.split('\n'):
    line = line.strip()
    if not line:
        continue
    mm = col_regex.match(line)
    if mm:
        collision_table[idx] = mm.group()
        idx += 1

# Short names
short = {
    'COL_NONE': '____', 'COL_ALL': 'ALL_', 'COL_FLOOR_CEIL': 'FC__',
    'COL_BOTTOM': 'BOT_', 'COL_TOP': 'TOP_', 'COL_DEATH': 'DEAD',
    'COL_DEATH_TOP': 'DTOP', 'COL_DEATH_BOTTOM': 'DBOT',
    'COL_DEATH_LEFT': 'DLFT', 'COL_DEATH_RIGHT': 'DRGT',
    'COL_TOP_CENTER_SPIKE': 'TCSP', 'COL_NO_SIDE': 'NOSD',
    'COL_LEFT': 'LEFT', 'COL_RIGHT': 'RGHT',
}

# MapTileForCollision
def map_tile(tid):
    if tid in (0xFC, 0xDF, 0xE3, 0xFE, 0xFF): return 0
    if tid == 0xFD: return 0x26
    return tid

def get_col_name(gid):
    if gid == 0: return '____'
    tid = gid - 1
    mapped = map_tile(tid)
    name = collision_table.get(mapped, f'?x{mapped:02X}')
    return short.get(name, name[:4])

def get_col_full(gid):
    if gid == 0: return 'EMPTY'
    tid = gid - 1
    mapped = map_tile(tid)
    return collision_table.get(mapped, f'UNKNOWN_x{mapped:02X}')

GROUND_ROWS = 3

# Correct columns: TMX column = tile column, no offset
# wTY = tileArrayY - GROUND_ROWS
print("=== Level layout: cols 455-470, wTY 22-34 ===")
print(f"{'':10s}", end='')
for c in range(455, 471):
    px = c * 16
    print(f" c{c:3d}/{px:5d}", end='')
print()

for ar in range(25, 38):
    wty = ar - GROUND_ROWS
    py = wty * 16
    print(f"wTY{wty:2d}/{py:3d} ", end='')
    for c in range(455, 471):
        idx2 = ar * width + c
        gid = tiles[idx2]
        cn = get_col_name(gid)
        if gid == 0:
            print(f"  {'____':>9s}", end='')
        else:
            tid = gid - 1
            print(f" {cn:>4s}/x{tid:02X}", end='')
    print()

# Ball probe positions at various X and Y for BallEject
print("\n=== BallEject floor probe at various X positions ===")
print("Mini ball: hbW=8, hbH=7, miniOffset=4, ballYOffset=1")
print("collisionY = Y + miniOffset + ballYOffset = Y + 5")
print("checkFloor bottom = collisionY + hbH = Y + 12")
print("tileBelowY = (Y+12) / 16")
print()

for x_px in [7280, 7312, 7344, 7376, 7408, 7417, 7424]:
    col = x_px // 16
    left_col = x_px // 16
    right_col = (x_px + 8) // 16
    print(f"X={x_px} (col {col}, right_col {right_col})")

# For the death zone, check what path is available
print("\n=== Corridor analysis: empty tiles forming passage ===")
for ar in range(25, 38):
    wty = ar - GROUND_ROWS
    py = wty * 16
    empty_cols = []
    for c in range(455, 471):
        idx2 = ar * width + c
        gid = tiles[idx2]
        if gid == 0:
            empty_cols.append(c)
    if empty_cols:
        print(f"wTY{wty:2d} / {py:3d}px: empty at cols {empty_cols}")

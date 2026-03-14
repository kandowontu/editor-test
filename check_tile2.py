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

# Collision type mapping (from MetatileCollision.cs)
col_names_raw = """COL_NONE
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
COL_DEATH_RIGHT"""

col_map = {}
for i, name in enumerate(col_names_raw.strip().split('\n')):
    col_map[i] = name.strip()

# Short names for display
short = {
    'COL_NONE': '____',
    'COL_ALL': 'ALL_', 
    'COL_FLOOR_CEIL': 'FC__',
    'COL_BOTTOM': 'BOT_',
    'COL_TOP': 'TOP_',
    'COL_DEATH': 'DEAD',
    'COL_DEATH_TOP': 'DTOP',
    'COL_DEATH_BOTTOM': 'DBOT',
    'COL_DEATH_LEFT': 'DLFT',
    'COL_DEATH_RIGHT': 'DRGT',
    'COL_TOP_CENTER_SPIKE': 'TCSP',
}

def get_col(tile_id):
    if tile_id == 0:
        return '____'
    # Map special tiles
    if tile_id in (0xFC, 0xDF, 0xE3, 0xFE, 0xFF):
        return '____'  # COL_NONE
    if tile_id == 0xFD:
        return get_col(0x26)
    name = col_map.get(tile_id, f'?{tile_id:02X}')
    return short.get(name, name[:4])

GROUND_ROWS = 3

# Scan cols 455-470, rows 25-36 (array rows)
print(f"{'':8s}", end='')
for c in range(455, 471):
    print(f"  c{c:3d}  ", end='')
print()

for ar in range(25, 37):
    wty = ar - GROUND_ROWS
    print(f"wTY{wty:2d}  ", end='')
    for c in range(455, 471):
        idx = ar * width + c
        gid = tiles[idx]
        if gid == 0:
            print(f"  ____  ", end='')
        else:
            et = gid - 1
            col = get_col(et)
            print(f" {col:4s}/{et:02X}", end='')
    print()

# Now show exactly what happens at the ball's death positions
print("\n=== Ball death analysis ===")
print("Ball at X=7417, Y=468:")
print(f"  leftX = {7417+3}, rightX = {7417+5}, topRowY = {468+9}, botRowY = {468+4}")
for name, px, py in [("TL", 7420, 477), ("TR", 7422, 477), ("BL", 7420, 472), ("BR", 7422, 472)]:
    tx = px // 16
    ty = py // 16
    ary = ty + GROUND_ROWS
    lx = px % 16
    ly = py - ty * 16
    idx = ary * width + tx
    gid = tiles[idx]
    et = gid - 1 if gid > 0 else -1
    col = get_col(et) if et >= 0 else '____'
    col_full = col_map.get(et, 'NONE') if et >= 0 else 'NONE'
    
    # Check if kills
    kills = False
    if col_full == 'COL_DEATH_RIGHT':
        kills = (lx >= 10) and (6 <= ly <= 8)
    elif col_full == 'COL_DEATH_TOP':
        kills = (ly < 6) and (5 <= lx <= 7)
    elif col_full == 'COL_DEATH':
        kills = (4 <= ly <= 11) and (4 <= lx <= 8)
    
    print(f"  {name}: px=({px},{py}) tile=({tx},{ty}) ary={ary} local=({lx},{ly}) tile=0x{et:02X} col={col_full} KILLS={kills}")

# Check for different Y values that might survive
print("\n=== Y values that survive the 4-corner check at X=7417 ===")
for y in range(460, 485):
    leftX = 7417 + 3
    rightX = 7417 + 5
    topY = y + 9
    botY = y + 4
    
    kills = False
    for px, py in [(leftX, topY), (rightX, topY), (leftX, botY), (rightX, botY)]:
        tx = px // 16
        ty = py // 16
        ary = ty + GROUND_ROWS
        if ary < 0 or ary >= height or tx < 0 or tx >= width:
            continue
        idx = ary * width + tx
        gid = tiles[idx]
        if gid == 0:
            continue
        et = gid - 1
        col_full = col_map.get(et, 'NONE')
        lx = px % 16
        ly = py - ty * 16
        
        if col_full == 'COL_DEATH_RIGHT':
            if (lx >= 10) and (6 <= ly <= 8):
                kills = True
        elif col_full == 'COL_DEATH_TOP':
            if (ly < 6) and (5 <= lx <= 7):
                kills = True
        elif col_full == 'COL_DEATH_BOTTOM':
            if (ly > 10) and (5 <= lx <= 7):
                kills = True
        elif col_full == 'COL_DEATH_LEFT':
            if (lx < 6) and (6 <= ly <= 8):
                kills = True
        elif col_full == 'COL_DEATH':
            if (4 <= ly <= 11) and (4 <= lx <= 8):
                kills = True
    
    if not kills:
        print(f"  Y={y} SURVIVES (topY={topY} botY={botY})")

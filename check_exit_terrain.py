import xml.etree.ElementTree as ET
import csv, io

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()

# Collision table from MetatileCollision.cs
collision_map = {}
col_str = "01020506008889"  # FC tiles: 01,02,05,06,88,89
fc_set = {0x01, 0x02, 0x05, 0x06, 0x88, 0x89}
# Full collision table from MetatileCollisionTable.cs
# COL_NONE=0, COL_ALL=1, COL_TOPONLY=2, COL_RIGHTONLY=3, COL_FWD_ONLY=4, COL_SPIKE=5
# COL_RSLOPE_UP=6, COL_RSLOPE_DOWN=7, COL_LSLOPE_UP=8, COL_LSLOPE_DOWN=9, COL_FLOOR_CEIL=10

# Just print raw tile IDs and identify solid/FC/death tiles
for layer in root.findall('.//layer'):
    name = layer.get('name')
    if name != 'collision':
        continue
    width = int(layer.get('width'))
    height = int(layer.get('height'))
    data_el = layer.find('data')
    reader = csv.reader(io.StringIO(data_el.text.strip()))
    tiles = []
    for row in reader:
        tiles.append([int(x.strip()) for x in row if x.strip()])
    
    print(f"Map: {width}x{height}")
    
    # Print terrain at cols 590-650, rows 0-27
    # Focus on the exit area near the wave→ship portal
    print("\n=== Terrain cols 590-650, rows 0-27 ===")
    print("     ", end="")
    for c in range(590, 651, 5):
        print(f"c{c:3d}  ", end="")
    print()
    
    for r in range(height):
        y = r * 16
        print(f"r{r:2d} Y={y:3d}: ", end="")
        for c in range(590, 651):
            if c < width:
                tid = tiles[r][c]
                if tid == 0:
                    ch = '.'
                else:
                    metatile = tid - 1  # firstgid=1
                    if metatile in fc_set:
                        ch = 'F'
                    elif metatile in (0x03, 0x04, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F):
                        ch = 'S'  # solid-ish (ALL, slopes, etc)
                    elif metatile in (0x10, 0x11, 0x12, 0x13, 0x14):
                        ch = 'D'  # death/spike
                    else:
                        ch = f'{metatile:02X}'[-1]  # last hex digit
                print(ch, end="")
            else:
                print(' ', end="")
        print()

    # Also specifically check cols 630-645 for portal sprites in sprite layer
    break

# Check sprites near the wave→ship portal
print("\n=== Sprites near cols 620-660 ===")
for group in root.findall('.//objectgroup'):
    for obj in group.findall('object'):
        gid = obj.get('gid')
        if gid is None:
            continue
        x = float(obj.get('x'))
        y = float(obj.get('y'))
        col = int(x) // 16
        if 620 <= col <= 660:
            sid = (int(gid) - 257) & 0xFF  # sprite firstgid=257
            print(f"  col={col} X={int(x)} Y={int(y)} sid=0x{sid:02X} gid={gid}")

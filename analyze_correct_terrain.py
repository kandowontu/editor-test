import xml.etree.ElementTree as ET
import re

tree = ET.parse(r'c:\Editor Test\native-windows\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\deathmoon.tmx')
root = tree.getroot()
map_w = int(root.get('width'))

layers = root.findall('.//layer')
coll_layer = layers[0]
data = coll_layer.find('data')
tiles = [int(v.strip()) for v in data.text.strip().replace('\n',',').split(',') if v.strip()]

# Read MetatileCollisionTable from the C# source
with open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    src = f.read()
# Extract the mapping text
match = re.search(r'string mappingText = @"(.*?)"', src, re.DOTALL)
mapping_text = match.group(1)
lines_raw = mapping_text.strip().split('\n')
coll_table = {}
idx = 0
for line in lines_raw:
    line = line.strip()
    if not line:
        continue
    name = line.split(';')[0].strip()
    if name:
        coll_table[idx] = name
        idx += 1

# Verify key mappings
for tv in [0,1,2,3,5,6,7,67,69,79,80,137,138,144,145,146,147,148]:
    print(f'Tile {tv:3d} (0x{tv:02X}) -> {coll_table.get(tv,"??")}')

print()

ground = 3
short_names = {
    'COL_NONE':' . ','COL_FLOOR_CEIL':'FC ','COL_ALL':'## ','COL_BOTTOM':'BT ',
    'COL_TOP':'TP ','COL_LEFT':'LF ','COL_RIGHT':'RT ','COL_NO_SIDE':'NS ',
    'COL_SLOPE_RD45':'Rd ','COL_SLOPE_LD45':'Ld ','COL_SLOPE_RU45':'Ru ','COL_SLOPE_LU45':'Lu ',
    'COL_SLOPE_RD22_RIGHT':'R2R','COL_SLOPE_RD22_LEFT':'R2L',
    'COL_SLOPE_LD22_RIGHT':'L2R','COL_SLOPE_LD22_LEFT':'L2L',
    'COL_SLOPE_RU22_RIGHT':'r2R','COL_SLOPE_RU22_LEFT':'r2L',
    'COL_SLOPE_LU22_RIGHT':'l2R','COL_SLOPE_LU22_LEFT':'l2L',
    'COL_DEATH_TOP':'DT ','COL_DEATH_BOTTOM':'DB ','COL_DEATH':'DD ',
    'COL_DEATH_LEFT':'DL ','COL_DEATH_RIGHT':'DR ',
}

# Header
print(f'             ', end='')
for col in range(1240, 1256):
    print(f'c{col}', end='')
print()

for arr_row in range(24, 39):
    vis_row = arr_row - ground
    y_start = vis_row * 16
    y_end = y_start + 15
    print(f'a{arr_row:2d} v{vis_row:2d} {y_start:3d}-{y_end:<3d}', end='')
    for col in range(1240, 1256):
        idx = arr_row * map_w + col
        t = tiles[idx]
        cname = coll_table.get(t, f'?{t}')
        s = short_names.get(cname, f'{t:3d}')
        print(s, end='')
    print()

print()
# Show arr25 raw tile values
print("arr25 raw tile values (Y=352-367):")
for col in range(1240, 1256):
    idx = 25 * map_w + col
    t = tiles[idx]
    cname = coll_table.get(t, '??')
    print(f'  c{col}: tile={t} -> {cname}')

# Trace wave behavior at several X positions
print("\n=== WAVE PATH ANALYSIS ===")
for playerX_start in [19840, 19860, 19880, 19900, 19920, 19940, 19960]:
    col = playerX_start // 16
    print(f"\nplayerX={playerX_start} (col {col}):")
    # Find FC tiles in this column
    fc_rows = []
    for arr_row in range(24, 39):
        idx = arr_row * map_w + col
        t = tiles[idx]
        cname = coll_table.get(t, '??')
        if cname == 'COL_FLOOR_CEIL':
            vis = arr_row - ground
            fc_rows.append((arr_row, vis, vis*16, vis*16+15))
    print(f"  FC rows: {[(r[0], f'Y={r[2]}-{r[3]}') for r in fc_rows]}")
    
    # Check what the wave collision X probes would hit (velY>0 uses xOffset=4)
    collX_down = playerX_start + 4
    probes_down = [collX_down, collX_down + 4, collX_down + 8]
    print(f"  Down probes X: {probes_down} -> cols {[p//16 for p in probes_down]}")
    
    collX_up = playerX_start + 10
    probes_up = [collX_up, collX_up + 4, collX_up + 8]
    print(f"  Up probes X: {probes_up} -> cols {[p//16 for p in probes_up]}")

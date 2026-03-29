import xml.etree.ElementTree as ET, csv, io

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/bloodbath.tmx')
root = tree.getroot()

TILES_FIRSTGID = 1

# Collision table entries
col_table = {}
mapping_raw = """COL_NONE
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
COL_NONE"""

entries = [e.strip().split(';')[0] for e in mapping_raw.strip().split('\n') if e.strip()]
for i, e in enumerate(entries):
    col_table[i] = e

# Also add MapTileForCollision remapping
tile_remap = {0xFC: 0x00, 0xDF: 0x00, 0xE3: 0x00, 0xFE: 0x00, 0xFF: 0x00, 0xFD: 0x26}

for layer in root.findall('layer'):
    name = layer.get('name')
    if name == 'SP':
        continue
    width = int(layer.get('width'))
    data = layer.find('data')
    reader = csv.reader(io.StringIO(data.text.strip()))
    rows = [[int(x) for x in r if x.strip()] for r in reader if any(x.strip() for x in r)]

    print("Terrain at columns 1218-1230 (after firstgid subtraction):")
    print(f"{'Row':>3} | ", end='')
    for col in range(1218, 1230):
        print(f"Col{col:4d} ", end='')
    print()
    print("-" * (4 + 9 * 12))
    
    for row_idx, row in enumerate(rows):
        print(f"{row_idx:3d} | ", end='')
        for col in range(1218, 1230):
            if col < len(row) and row[col] > 0:
                tid = row[col] - TILES_FIRSTGID
                mtid = tile_remap.get(tid, tid)
                col_name = col_table.get(mtid, "???")
                # Short form
                short = col_name.replace("COL_", "")[:6]
                print(f"  {tid:3d}/{short:6s}", end='')
            else:
                print(f"       .     ", end='')
        print()

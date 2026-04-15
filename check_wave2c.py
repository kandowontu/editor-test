#!/usr/bin/env python3
import xml.etree.ElementTree as ET

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()

COL_NAMES = {0:'NONE',1:'ALL',2:'FLR_TOP',3:'DEATH',4:'DTH_TOP',5:'FL_CEIL',6:'SLU6B',7:'SLU6T',8:'SLD6B',9:'SLD6T',10:'SRU6B',11:'SRU6T',12:'SRD6B',13:'SRD6T'}

coll_map = {}
for ts in root.findall('.//tileset'):
    firstgid = int(ts.get('firstgid', 0))
    name = ts.get('name', '?')
    print(f"Tileset: firstgid={firstgid} name={name}")
    for tile in ts.findall('.//tile'):
        local_id = int(tile.get('id'))
        global_id = firstgid + local_id
        for prop in tile.findall('.//property'):
            if prop.get('name') == 'collision':
                val = int(prop.get('value'))
                coll_map[global_id] = val

# Check specific tiles found at the wave section entrance
check_tiles = [1, 7, 18, 19, 31, 48, 65, 72, 78, 79, 112, 161, 162, 163, 164]
print("\nTile collision types:")
for tid in check_tiles:
    ct = coll_map.get(tid)
    if ct is not None:
        print(f"  Tile {tid}: {ct} ({COL_NAMES.get(ct, '?')})")
    else:
        print(f"  Tile {tid}: NO COLLISION PROPERTY")

# Now redo the terrain map with proper collision lookup
layers = root.findall('layer')
tl = layers[0]
w = int(tl.get('width'))
h = int(tl.get('height'))
tiles = [int(x) for x in tl.find('data').text.strip().split(',')]

print(f"\nTerrain at cols 440-470, rows 4-22:")
hdr = "  R    Y"
for c in range(440, 471):
    hdr += f"  {c:>3}"
print(hdr)

for r in range(4, 23):
    line = f"{r:>3} {r*16:>4}"
    for c in range(440, 471):
        tid = tiles[r * w + c]
        if tid == 0:
            line += "    ."
        else:
            ct = coll_map.get(tid)
            if ct is None:
                line += f"  t{tid:>2}"  # tile with no collision
            elif ct == 0:
                line += "    ."  # NONE
            else:
                name = COL_NAMES.get(ct, f"?{ct}")[:4]
                line += f" {name:>4}"
    print(line)

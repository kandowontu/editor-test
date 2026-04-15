#!/usr/bin/env python3
import xml.etree.ElementTree as ET

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()

COL_NAMES = {0:'NONE',1:'ALL',2:'FLR_T',3:'DEATH',4:'DTH_T',5:'FL_CL',6:'SLU6B',7:'SLU6T',8:'SLD6B',9:'SLD6T',10:'SRU6B',11:'SRU6T',12:'SRD6B',13:'SRD6T'}

coll_map = {}
for ts in root.findall('.//tileset'):
    firstgid = int(ts.get('firstgid', 0))
    for tile in ts.findall('.//tile'):
        tid = firstgid + int(tile.get('id'))
        for prop in tile.findall('.//property'):
            if prop.get('name') == 'collision':
                coll_map[tid] = int(prop.get('value'))

layers = root.findall('layer')
terrain_layer = layers[0]
t_width = int(terrain_layer.get('width'))
t_height = int(terrain_layer.get('height'))
terrain_tiles = [int(x) for x in terrain_layer.find('data').text.strip().split(',')]

print(f"Map: {t_width}x{t_height}")
# Header
hdr = f"{'R':>3} {'Y':>4}"
for c in range(440, 471):
    hdr += f"  c{c}"
print(hdr)
for r in range(4, 24):
    line = f"{r:>3} {r*16:>4}"
    for c in range(440, 471):
        tid = terrain_tiles[r * t_width + c]
        ct = coll_map.get(tid, -1) if tid > 0 else -1
        if ct == -1:
            line += "     ."
        else:
            line += f" {COL_NAMES.get(ct, '?'):>5}"
    print(line)

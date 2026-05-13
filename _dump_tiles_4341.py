"""Dump collision/SP layer tiles around given X,Y range from shardscapes.tmx"""
import xml.etree.ElementTree as ET, sys, base64, zlib, struct

PATH = r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\shardscapes.tmx'
tree = ET.parse(PATH); root = tree.getroot()
TW = int(root.get('tilewidth')); TH = int(root.get('tileheight'))
W  = int(root.get('width'));     H  = int(root.get('height'))

def parse(layer):
    data = layer.find('data')
    txt = data.text.strip().replace('\n','').replace(' ','')
    arr = [int(x) for x in txt.split(',') if x]
    grid = [arr[r*W:(r+1)*W] for r in range(H)]
    return grid

layers = {(l.get('name') or 'noname'): parse(l) for l in root.findall('layer')}
print('layers:', list(layers))

# X=4341 Y=594 area; player width ~14 height ~16
cx = 4341 // TW; cy = 594 // TH
print(f'center tile = ({cx},{cy}), TW={TW} TH={TH} W={W} H={H}')
for name in ('noname','1','SP'):
    g = layers.get(name)
    if not g: continue
    print(f'--- layer "{name}" tiles around ({cx-3}..{cx+3}, {cy-2}..{cy+4}) ---')
    print('         ' + ' '.join(f'{x:>5}' for x in range(cx-3, cx+4)))
    for y in range(cy-2, cy+5):
        row = [str(g[y][x] & 0x1FFFFFFF) if 0 <= x < W and 0 <= y < H else '?' for x in range(cx-3, cx+4)]
        print(f'  y={y:3d}  ' + ' '.join(f'{v:>5}' for v in row))

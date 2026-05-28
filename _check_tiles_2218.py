import sys, os
sys.path.insert(0, r'C:\Editor Test\native-windows')
import xml.etree.ElementTree as ET

tmx = r'C:\Editor Test\aftercatabath.tmx'  # placeholder; correct?
# Actually level is dorabaebasic6.tmx
tmx = r'C:\Editor Test\famidash\levels\dorabaebasic6.tmx'
if not os.path.exists(tmx):
    import glob
    for f in glob.glob(r'C:\Editor Test\**\dorabaebasic6.tmx', recursive=True):
        tmx = f
        break
print('TMX:', tmx)

# Use TMX directly: find the slope tiles around tileX=335-345, tileY=10-14
import re
with open(tmx, 'r', encoding='utf-8') as f:
    txt = f.read()

# Parse all layers' data
root = ET.fromstring(txt)
W = int(root.attrib['width'])
print('Map W:', W)

# Find slope GIDs from tilesets (collision data is per-tile)
# Easier: find the "collision" layer or read tile GIDs and look up tilesets
for layer in root.findall('layer'):
    name = layer.attrib.get('name', '')
    data = layer.find('data')
    if data is None: continue
    csv_str = data.text
    rows = [r for r in csv_str.strip().split('\n') if r.strip()]
    print(f'\nLayer: {name}, rows={len(rows)}')
    for ty in range(10, 16):
        if ty < len(rows):
            vals = [v.strip() for v in rows[ty].split(',') if v.strip()]
            sub = vals[335:346]
            print(f'  ty={ty}: tx335-345 = {sub}')

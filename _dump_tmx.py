import xml.etree.ElementTree as ET
t = ET.parse(r'famidash\LEVELS\LEVEL DATA\lvlset_HUGE\shardscapes.tmx')
root = t.getroot()
mw = int(root.get('width')); mh = int(root.get('height'))
print(f'TMX size: {mw}x{mh}')
for layer in root.findall('.//layer'):
    name = layer.get('name')
    text = layer.find('data').text.strip()
    tiles = [int(x.strip()) for x in text.replace('\n',',').split(',') if x.strip()]
    print(f'\nLayer: {name}  count={len(tiles)}')
    # Player at world (8944, 476), robot top.  Tile X=559, Y=29-30
    for ry in range(27, 34):
        row = ' '.join(f'{tiles[ry*mw+cx]:3X}' for cx in range(555, 567))
        print(f'  row {ry} cols 555-566: {row}')

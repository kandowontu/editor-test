import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\BIG backup\lvlset_BIG\blastprocessing.tmx')
root = tree.getroot()

# Find collision/main layer
for layer in root.findall('.//layer'):
    name = layer.get('name','').lower()
    if name in ['collision','main','ground','fg','col','none'] or layer.get('name') is None:
        data = layer.find('data').text.strip()
        tiles = [int(x) for x in data.split(',')]
        w = int(layer.get('width'))
        # Show tiles around tileX=516-521, tileY=14-24
        for ty in range(14, 25):
            row = []
            for tx in range(516, 522):
                idx = ty * w + tx
                if idx < len(tiles):
                    row.append(f'{tiles[idx]:3d}')
                else:
                    row.append('  -')
            print(f'Y={ty:2d} ({ty*16:3d}px) |' + '|'.join(row) + f'|  tileX=516-521')
        print(f'Layer: {name}  width={w}')
        break

import xml.etree.ElementTree as ET
tree = ET.parse('aftercatabath.tmx')
root = tree.getroot()
layer = root.findall('.//layer')[0]
w = int(layer.get('width')); h = int(layer.get('height'))
print(f'BG layer: {w}x{h}')
data = layer.find('data').text
rows = [r for r in data.strip().split('\n') if r.strip()]
for ry in range(13, 22):
    cells = [c.strip() for c in rows[ry].split(',')]
    line = ' '.join(f'{int(cells[tx]):3d}' for tx in range(735, 745))
    print(f'row {ry:2d} cols 735-744: {line}')
# Also list ALL layers
print()
for lyr in root.findall('.//layer'):
    print(f'layer: name={lyr.get("name")} w={lyr.get("width")} h={lyr.get("height")}')

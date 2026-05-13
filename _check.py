import xml.etree.ElementTree as ET
tree = ET.parse('aftercatabath.tmx')
root = tree.getroot()
layer = root.findall('.//layer')[0]
w = int(layer.get('width')); h = int(layer.get('height'))
data = layer.find('data').text
rows = [r for r in data.strip().split('\n') if r.strip()]
# Find slope tiles (144=RD45) near player col 737 row 14
print(f'map: {w}x{h}')
for ry in range(max(0,12), min(h,17)):
    cells = [c.strip() for c in rows[ry].split(',')]
    line = ' '.join(f'{int(cells[tx]):3d}' for tx in range(730, 745))
    print(f'row {ry:2d} cols 730-744: {line}')
print()
# Show only ceilCheck probes for player Y=236, ceilCheckY=239 -> tileY=14
print('Tile at (737,14):', int(rows[14].split(",")[737].strip()))
print('Tile at (738,14):', int(rows[14].split(",")[738].strip()))

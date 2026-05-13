import xml.etree.ElementTree as ET
tree = ET.parse('aftercatabath.tmx')
root = tree.getroot()
layer = root.findall('.//layer')[0]
w = int(layer.get('width')); h = int(layer.get('height'))
data = layer.find('data').text
rows = [r for r in data.strip().split('\n') if r.strip()]
# Find slope tiles (144-163) near player col 737
for ry in range(h):
    cells = [c.strip() for c in rows[ry].split(',')]
    for tx in range(max(0,720), min(w,760)):
        tid = int(cells[tx])
        if 144 <= tid <= 163:
            print(f'  SLOPE tile {tid} at ({tx},{ry}) px=({tx*16},{ry*16})')

import xml.etree.ElementTree as ET
tree = ET.parse('aftercatabath.tmx')
root = tree.getroot()
layer = root.findall('.//layer')[0]
w = int(layer.get('width')); h = int(layer.get('height'))
data = layer.find('data').text
rows = [r for r in data.strip().split('\n') if r.strip()]
px = 11806; py = 247
TILE = 16
tx = px // TILE; ty = py // TILE
print(f'Player tile ({tx},{ty})')
print('cols:     ' + ' '.join(f'{c:>3d}' for c in range(max(0,tx-5), min(w,tx+12))))
for ry in range(0, min(h,ty+8)):
    cells = rows[ry].split(',')
    chunk = cells[max(0,tx-5):min(w,tx+12)]
    print(f'  row {ry:2d}: ' + ' '.join(f'{c.strip():>3}' for c in chunk))

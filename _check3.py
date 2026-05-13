import xml.etree.ElementTree as ET
tree = ET.parse('aftercatabath.tmx')
root = tree.getroot()
for li, layer in enumerate(root.findall('.//layer')):
    w = int(layer.get('width')); h = int(layer.get('height'))
    name = layer.get('name')
    data = layer.find('data').text
    rows = [r for r in data.strip().split('\n') if r.strip()]
    print(f'=== layer {li} name={name} ===')
    # Find tid 144 anywhere near cols 730-750
    for ry in range(h):
        cells = [int(c.strip()) for c in rows[ry].split(',')]
        for tx in range(720, 760):
            if cells[tx] == 144:
                print(f'  tid=144 at ({tx},{ry})')
            if cells[tx] == 147 and tx in (737, 738, 739):
                print(f'  tid=147 at ({tx},{ry})')

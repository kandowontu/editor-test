import xml.etree.ElementTree as ET
tree = ET.parse('aftercatabath.tmx')
root = tree.getroot()
for li, layer in enumerate(root.findall('.//layer')):
    w = int(layer.get('width')); h = int(layer.get('height'))
    name = layer.get('name')
    data = layer.find('data').text
    # split into single-row rows
    flat = [c.strip() for c in data.replace('\n',',').split(',') if c.strip()]
    print(f'layer {li} name={name} w={w} h={h} cells={len(flat)}')
    for ry in range(h):
        for tx in range(720, 760):
            tid = int(flat[ry*w+tx])
            if tid == 144 or (tid==147 and tx in (737,738,739)) or (tid in (35,1,147,144) and ry==17 and tx in (737,738)):
                print(f'  ({tx:3d},{ry:2d})={tid}')

import xml.etree.ElementTree as ET, re
tree = ET.parse(r'c:\Editor Test\lvlset_HUGE\cataclysm.tmx')
root = tree.getroot()
w = int(root.attrib['width']); h = int(root.attrib['height'])
print(f'map {w}x{h}')
# Find Terrain layer
for layer in root.iter('layer'):
    name = layer.attrib.get('name','?')
    if name.lower().startswith('terrain') or 'terrain' in name.lower():
        data = layer.find('data')
        text = data.text.strip()
        rows = [r.split(',') for r in text.strip().split('\n')]
        print(f'layer {name} rows={len(rows)} cols={len(rows[0])}')
        # tile coords (228,15)
        cx, cy = 228, 15
        for ty in range(max(0,cy-2), min(len(rows),cy+3)):
            line = []
            for tx in range(max(0,cx-4), min(len(rows[ty]),cx+8)):
                v = int(rows[ty][tx].strip() or '0')
                line.append(f'{v:>4}')
            print(f'  y={ty}: ' + ' '.join(line))
        break

import xml.etree.ElementTree as ET
tree = ET.parse(r'famidash\LEVELS\BIG backup\lvlset_BIG\dorabaebasic6.tmx')
root = tree.getroot()
# Find any layer with data
for layer in root.iter('layer'):
    name = layer.attrib.get('name')
    data = layer.find('data')
    if data is None or data.text is None: continue
    rows = [r.strip() for r in data.text.strip().split('\n') if r.strip()]
    print(f'\n=== Layer: {name} ({len(rows)} rows) ===')
    if len(rows) < 17: continue
    tx_range = list(range(330, 348))
    print('     ' + ' '.join(f'{x:4d}' for x in tx_range))
    for ty in [11, 12, 13, 14, 15, 16]:
        cells = rows[ty].rstrip(',').split(',')
        line = ' '.join(f'{int(cells[x]):4d}' for x in tx_range)
        print(f'ty={ty:2d}: {line}')

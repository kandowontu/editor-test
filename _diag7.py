import xml.etree.ElementTree as ET
root = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx').getroot()
for layer in root.findall('.//layer'):
    name = layer.get('name')
    data = layer.find('data').text
    rows = []
    for line in data.split('\n'):
        line = line.strip().rstrip(',').strip()
        if not line:
            continue
        rows.append([int(x) for x in line.split(',') if x.strip()])
    print(f'==== Layer {name} ({len(rows)}x{len(rows[0])}) ====')
    for r in range(28, 42):
        parts = [f'{rows[r][c]:>5}' for c in range(1860, 1872)]
        print(f' r{r:2}: ' + ' '.join(parts))

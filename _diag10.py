import xml.etree.ElementTree as ET
for fp in [r'famidash/LEVELS/LEVEL DATA/lvlset_C/everyend.tmx',
           r'famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx']:
    print('===', fp)
    t = ET.parse(fp).getroot()
    for layer in t.findall('.//layer'):
        name = layer.get('name')
        if name == 'SP':
            continue
        data = layer.find('data').text
        rows = []
        for line in data.split('\n'):
            line = line.strip().rstrip(',').strip()
            if not line:
                continue
            rows.append([int(x) for x in line.split(',') if x.strip()])
        print('  layer:', name, 'h=', len(rows), 'w=', len(rows[0]))
        for r in (32, 34, 35, 37):
            print(f'   r{r:2}:', ' '.join(f'{rows[r][c]:>3}' for c in range(1860, 1872)))

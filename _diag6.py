import xml.etree.ElementTree as ET
root = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx').getroot()
print('layers:')
for ly in root.findall('.//layer'):
    print(' L', ly.get('name'))
print('objectgroups:')
for og in root.findall('.//objectgroup'):
    name = og.get('name')
    objs = og.findall('object')
    print(f' OG {name}: {len(objs)} objects')
    for o in objs:
        x = float(o.get('x', 0)); y = float(o.get('y', 0))
        if 29700 <= x <= 30000 and 400 <= y <= 600:
            print('  near:', o.get('id'), o.get('type'), o.get('gid'), o.get('name'), 'at', x, y)

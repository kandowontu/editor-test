import re, xml.etree.ElementTree as ET
tree = ET.parse(r'famidash/LEVELS/LEVEL DATA/lvlset_HUGE/shardscapes.tmx')
root = tree.getroot()
for og in root.iter('objectgroup'):
    name = og.get('name', '')
    for obj in og.iter('object'):
        x = float(obj.get('x', 0)); y = float(obj.get('y', 0))
        if 3960 <= x <= 4020 and 560 <= y <= 660:
            t = obj.get('type', '') or obj.get('class', '')
            gid = obj.get('gid', '')
            on = obj.get('name', '')
            print('group=%s x=%d y=%d type=%s gid=%s name=%s' % (name, x, y, t, gid, on))
for ly in root.iter('layer'):
    nm = ly.get('name', '')
    w = int(ly.get('width'))
    data = ly.find('data').text
    csv = [int(t) for t in re.findall(r'-?\d+', data)]
    for r in range(34, 42):
        for c in range(246, 253):
            i = r * w + c
            if i < len(csv) and csv[i]:
                print('layer=%s col=%d row=%d (x=%d,y=%d) gid=%d' % (nm, c, r, c*16, r*16, csv[i]))

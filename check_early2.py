import xml.etree.ElementTree as ET
cs = open('native-windows/MetatileCollision.cs', encoding='utf-8').read()
s = cs.index('string mappingText = @"') + len('string mappingText = @"')
e = cs.index('";', s)
ct = [l.strip().split(';')[0].strip() for l in cs[s:e].split('\n') if l.strip() and not l.strip().startswith('//')]
gc = lambda g: 'NONE' if g==0 else ('OOB' if g-1>=len(ct) else ct[g-1].replace('COL_',''))
tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx')
root = tree.getroot()
w = int(root.get('width'))
data = root.find('.//layer/data').text.strip().split(',')
td = [int(x.strip()) for x in data]
for c in range(16, 22):
    rt=[]
    for r in range(0, 57):
        arr = r*w+c
        g = td[arr] if arr < len(td) else 0
        if g > 0: rt.append(f'r{r}(wr{r-3})={gc(g)}')
    if rt: print(f'c{c}(X={c*16}): {",".join(rt)}')

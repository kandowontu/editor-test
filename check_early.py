import xml.etree.ElementTree as ET
cs = open('native-windows/MetatileCollision.cs', encoding='utf-8').read()
s = cs.index('string mappingText = @"') + len('string mappingText = @"')
e = cs.index('";', s)
ct = [l.strip().split(';')[0].strip() for l in cs[s:e].split('\n') if l.strip() and not l.strip().startswith('//')]
gc = lambda g: 'NONE' if g==0 else ('OOB' if g-1>=len(ct) else ct[g-1].replace('COL_',''))
tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx')
w = int(tree.getroot().get('width'))
td = [int(x.strip()) for x in tree.getroot().find('.//layer/data').text.strip().split(',')]
for c in range(15,25):
    rt=[f'r{r}={gc(td[(r+3)*w+c])}' for r in range(40,57) if td[(r+3)*w+c]>0]
    if rt: print(f'c{c}(X={c*16}): {",".join(rt)}')

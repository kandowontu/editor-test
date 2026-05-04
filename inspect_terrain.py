import re, xml.etree.ElementTree as ET
src = open('native-windows/MetatileCollision.cs').read()
m = re.search(r'string mappingText = @"(.+?)";', src, re.DOTALL)
mapping = []
for line in m.group(1).split('\n'):
    line = line.strip()
    if not line: continue
    mm = re.search(r'COL_[A-Z0-9_]+', line)
    if mm: mapping.append(mm.group(0))
print('Total tiles:', len(mapping))

def sym(c):
    if c=='COL_NONE': return '.'
    if c=='COL_ALL': return '#'
    if c.startswith('COL_FLOOR') or c.startswith('COL_BOTTOM') or c.startswith('COL_TOP') or c.startswith('COL_LEFT') or c.startswith('COL_RIGHT') or c.startswith('COL_UP_') or c.startswith('COL_DOWN_'): return 'P'
    if 'DEATH' in c or 'SPIKE' in c: return 'X'
    return '?'

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx')
root = tree.getroot()
w = int(root.get('width')); h = int(root.get('height'))
def parse_csv(ly):
    return [int(x) for x in ly.find('data').text.replace('\n',',').split(',') if x.strip()]
terrain = parse_csv(root.findall('layer')[0])
def getcol(gid):
    if gid==0: return 'COL_NONE'
    t = (gid & 0x1FFFFFFF) - 1
    if 0<=t<len(mapping): return mapping[t]
    return '???'

print('TIle column ones digit:')
print('     ' + ''.join(f'{c%10}' for c in range(755,790)))
for r in range(40, 57):
    row = ''.join(sym(getcol(terrain[r*w+c])) for c in range(755,790))
    print(f'r{r:2d}: {row}')
print()
for rr in [45,46,47,48,49]:
    print(f'Row {rr} cols 760-772:')
    for c in range(760,772):
        g = terrain[rr*w+c]; t=(g&0x1FFFFFFF)-1
        print(f'  c{c} gid={g} tile={t} {getcol(g)}')

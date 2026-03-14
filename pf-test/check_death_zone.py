import xml.etree.ElementTree as ET, re, sys

with open(r'C:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    c = f.read()
s = c.find('mappingText = @"') + len('mappingText = @"')
e = c.find('";', s)
lines = [l for l in c[s:e].split('\n') if l.strip()]
rx = re.compile(r'COL_[A-Z0-9_]+')
ct = ['COL_NONE'] * 256
for i, raw in enumerate(lines):
    if i >= 256: break
    m = rx.search(raw.strip())
    if m: ct[i] = m.group()

tree = ET.parse(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep.tmx')
root = tree.getroot()
data = root.findall('layer')[0].find('data').text.strip()
rows = [r.strip() for r in data.split('\n') if r.strip()]

sys.stdout.write('Col:   ')
for c2 in range(449, 468):
    sys.stdout.write(f'{c2:5d}')
sys.stdout.write('\n')
for r in range(28, 36):
    wty = r - 3
    sys.stdout.write(f'r{r:2d}w{wty:2d}: ')
    cols = rows[r].split(',')
    for c2 in range(449, 468):
        gid = int(cols[c2])
        if gid == 0:
            sys.stdout.write('    .')
        else:
            ed = gid - 1
            col = ct[ed]
            short = col.replace('COL_', '').replace('DEATH_', 'D')[:5]
            sys.stdout.write(f'{short:>5s}')
    sys.stdout.write('\n')

sys.stdout.write('\nSprites at cols 449-467:\n')
sp_data = root.findall('layer')[1].find('data').text.strip()
sp_rows = [r.strip() for r in sp_data.split('\n') if r.strip()]
for r in range(28, 36):
    cols = sp_rows[r].split(',')
    for c2 in range(449, 468):
        gid = int(cols[c2])
        if gid > 0:
            sid = gid - 257
            sys.stdout.write(f'  r{r} w{r-3} col={c2} X={c2*16} sid=0x{sid:02X}({sid})\n')

import re
text = open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r', encoding='utf-8').read()
m = re.search(r'mappingText\s*=\s*@"(.*?)";', text, re.DOTALL)
s = m.group(1)
plines = [l.strip() for l in s.replace('\r','').split('\n') if l.strip()]
pf = []
rx = re.compile(r'COL_[A-Z0-9_]+')
for l in plines:
    mm = rx.search(l)
    pf.append(mm.group(0) if mm else 'COL_NONE')

# NES from crt0.lst
nes = ['COL_NONE'] * 256
nrx = re.compile(r'^\s*\d+\s+\(0x[0-9A-F]+\)\s+\d+\w+\s+\d\s+Metatile\s+"([^"]+)"[^,]*(?:,\s*\$[0-9A-Fa-f]+){4},\s*PAL_\d+,\s*(COL_[A-Z0-9_]+)')
nes_path = r'c:\Editor Test\famidash\BUILD\huge\crt0.lst'
idx = 0
with open(nes_path) as f:
    for line in f:
        if 'Metatile "' in line:
            matches = re.findall(r'COL_[A-Z0-9_]+', line)
            if matches and idx < 256:
                nes[idx] = matches[-1]
            idx += 1

print('Total NES metatiles:', idx)
print('PF len:', len(pf))
print()
diffs=0
for i in range(256):
    p = pf[i] if i < len(pf) else '<oob>'
    n = nes[i]
    if p != n:
        print(f'{i:>3} 0x{i:02X} PF={p:<32} NES={n}')
        diffs+=1
print('DIFFS:', diffs)

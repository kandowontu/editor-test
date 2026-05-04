import re
fp = r'famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx'
data = open(fp, 'rb').read()
if data[:2] == b'\xff\xfe': data = data.decode('utf-16').encode('utf-8')
text = data.decode('utf-8', errors='replace')
ms = list(re.finditer(r'<layer\b([^>]*)>.*?<data[^>]*>(.*?)</data>', text, re.DOTALL))
print('layers:', len(ms))
for m in ms:
    attrs = m.group(1)
    csv_text = m.group(2).strip()
    rows = [l.strip().rstrip(',') for l in csv_text.split('\n') if l.strip()]
    grid = [[int(x) for x in r.split(',') if x.strip()] for r in rows]
    print(f'== {attrs.strip()} ({len(grid)}x{len(grid[0])}) ==')
    for r in range(43, 53):
        if r >= len(grid): continue
        slc = grid[r][155:175]
        nz = [(i+155, v) for i, v in enumerate(slc) if v not in (0,)]
        if nz:
            print(f'  r={r}: {nz}')

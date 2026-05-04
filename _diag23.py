import re
fp = r'famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx'
data=open(fp,'rb').read()
if data[:2]==b'\xff\xfe': data=data.decode('utf-16').encode('utf-8')
text=data.decode('utf-8',errors='replace')
for m in re.finditer(r'<layer id="(\d+)"[^>]*name="([^"]*)"[^>]*>.*?<data[^>]*>(.*?)</data>', text, re.DOTALL):
    lid, lname, csv = m.group(1), m.group(2), m.group(3).strip()
    print(f'=== Layer id={lid} name={lname} ===')
    rows = [l.strip().rstrip(',') for l in csv.split('\n') if l.strip()]
    grid = [[int(x) for x in r.split(',') if x.strip()] for r in rows]
    for r in range(43, 53):
        row = grid[r]
        cells = [(c, row[c]) for c in range(155, 170) if row[c] != 0]
        if cells:
            print(f'  r={r}: ' + ' '.join(f'c{c}=gid{v}' for c,v in cells))

# Also layers without name
for m in re.finditer(r'<layer id="(\d+)"([^>]*)>.*?<data[^>]*>(.*?)</data>', text, re.DOTALL):
    lid, attrs, csv = m.group(1), m.group(2), m.group(3).strip()
    if 'name=' in attrs: continue
    print(f'=== Layer id={lid} (no name) ===')
    rows = [l.strip().rstrip(',') for l in csv.split('\n') if l.strip()]
    grid = [[int(x) for x in r.split(',') if x.strip()] for r in rows]
    for r in range(43, 53):
        row = grid[r]
        cells = [(c, row[c]) for c in range(155, 170) if row[c] != 0]
        if cells:
            print(f'  r={r}: ' + ' '.join(f'c{c}=gid{v}' for c,v in cells))

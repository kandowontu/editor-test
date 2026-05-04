import re, os
f = os.path.join(os.environ['TEMP'], 'famidash_trace_compare_20260503_234336.txt')
lines = open(f, encoding='utf-8', errors='replace').read().splitlines()
# find header
hdr = None
for i,l in enumerate(lines):
    if l.startswith('rom_f, sim_f'):
        hdr=i; break
print('header at', hdr)
rows = []
pat = re.compile(r'^(\d+),(\d+),\((-?\d+),(-?\d+)\),\((-?\d+),(-?\d+)\),(-?\d+),(-?\d+),(\d+),(\d+)')
for l in lines[hdr+1:]:
    m = pat.match(l)
    if not m: continue
    rows.append(tuple(int(x) for x in m.groups()))
print('rows:', len(rows))
# Find first |dy|>=2 (sustained), |dy|>=4
firsts = {2:None, 4:None, 8:None}
for r in rows:
    rf,sf,rx,ry,sx,sy,dx,dy,an,sa = r
    for thr in firsts:
        if firsts[thr] is None and abs(dy) >= thr:
            firsts[thr] = r
print('first |dy|>=2:', firsts[2])
print('first |dy|>=4:', firsts[4])
print('first |dy|>=8:', firsts[8])
# Show 30 rows around first |dy|>=4
if firsts[4]:
    target_rf = firsts[4][0]
    for r in rows:
        if r[0] >= target_rf-5 and r[0] <= target_rf+15:
            print(f'  rf={r[0]:>4} sf={r[1]:>4} rom=({r[2]:>5},{r[3]:>4}) sim=({r[4]:>5},{r[5]:>4}) dx={r[6]:>3} dy={r[7]:>3} a_next={r[8]} sim_a={r[9]}')

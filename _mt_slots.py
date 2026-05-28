import os, csv
T = os.environ['TEMP']
fn = T + r'\famidash_mesen_trace_cataclysm_20260520_145022_953.csv'
with open(fn, encoding='utf-8', errors='replace') as f:
    f.readline()  # skip metadata
    reader = csv.reader(f)
    hdr = next(reader)
    rows = list(reader)

# col indices
sim_c = hdr.index('sim_cursor')
px_c = hdr.index('px')
py_c = hdr.index('py')
vy_c = hdr.index('vel_y')
collmap_c = hdr.index('collmap_r8')

# Per-sim LAST row
from collections import OrderedDict
last = OrderedDict()
for r in rows:
    try:
        s = int(r[sim_c])
    except:
        continue
    last[s] = r

# show 2860-2935 with px deltas
prev = None
print(f'{"sim":>5} {"px":>6} {"dx":>4} {"py":>4} {"vy":>6}  collmap_r3_excerpt')
for s in range(2860, 2935):
    if s not in last: continue
    r = last[s]
    px = int(r[px_c])
    py = int(r[py_c])
    vy = int(r[vy_c])
    cm = r[collmap_c]
    # extract r3 slots
    r3 = ''
    for part in cm.split('|'):
        if part.startswith('r3:'):
            r3 = part[3:]
            break
    # show first 16 slot pairs (1 byte = 2 hex chars per slot)
    slots = [r3[i*2:i*2+2] for i in range(16)]
    dx = '' if prev is None else f'{px-prev:+d}'
    print(f'{s:>5} {px:>6} {dx:>4} {py:>4} {vy:>6}  ' + ' '.join(slots))
    prev = px

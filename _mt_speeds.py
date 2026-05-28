import os, csv
T = os.environ['TEMP']
MT_FILE = T + r'\famidash_mesen_trace_cataclysm_20260520_145022_953.csv'

# Find latest if name differs
import glob
mt_files = sorted(glob.glob(T + r'\famidash_mesen_trace_cataclysm_*.csv'), key=os.path.getmtime)
MT_FILE = mt_files[-1]
print('MT:', MT_FILE)

rows = []
with open(MT_FILE, encoding='utf-8', errors='replace') as f:
    reader = csv.reader(f)
    hdr = next(reader)
    for row in reader:
        rows.append(row)

# columns: try to detect sim_cursor and px_x columns
print('Header:', hdr[:25])

# Find rom_frame, sim_cursor, px_x columns by name
def col(name):
    for i,h in enumerate(hdr):
        if h.lower().replace('_','') == name.lower().replace('_',''):
            return i
    return -1

sim_c = col('sim_cursor')
if sim_c == -1: sim_c = col('simcursor')
px_c = col('player_x_px')
if px_c == -1: px_c = col('px')
if px_c == -1: px_c = col('pxx')
print(f'sim col={sim_c} px col={px_c}')
print('Sample row:', rows[100][:25])

# Use sim_cursor as integer; show last row per sim from 2870 to 2900
from collections import OrderedDict
last_per_sim = OrderedDict()
for row in rows:
    try:
        s = int(row[sim_c])
    except:
        continue
    last_per_sim[s] = row

print(f'{"sim":>5} {"px":>6} {"py":>4} {"vy":>6}')
prev_px = None
for s in range(2840, 2935):
    if s not in last_per_sim: continue
    r = last_per_sim[s]
    try:
        px = int(r[px_c])
    except:
        continue
    dx = '' if prev_px is None else f'{px-prev_px:+d}'
    print(f'{s:>5} {px:>6} {dx:>4}')
    prev_px = px

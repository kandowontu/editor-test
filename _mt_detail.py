import os, csv
T = os.environ['TEMP']
fn = T + r'\famidash_mesen_trace_cataclysm_20260520_145022_953.csv'
with open(fn, encoding='utf-8', errors='replace') as f:
    f.readline()
    reader = csv.reader(f)
    hdr = next(reader)
    rows = list(reader)
sim_c = hdr.index('sim_cursor')
rom_c = hdr.index('rom_frame')
px_c = hdr.index('px')
py_c = hdr.index('py')
vy_c = hdr.index('vel_y')
ac_c = hdr.index('a_cur')
an_c = hdr.index('a_next')
print("rom  sim  px   py  vy   ac an")
for r in rows:
    try:
        s = int(r[sim_c])
        rom = int(r[rom_c])
    except: continue
    if 2878 <= s <= 2895:
        print(f"{rom:>5} {s:>5} {r[px_c]:>5} {r[py_c]:>4} {r[vy_c]:>5} {r[ac_c]:>2} {r[an_c]:>2}")

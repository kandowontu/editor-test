import os, glob, csv
T = os.environ['TEMP']
fs = sorted(glob.glob(T + r'\famidash_pf_trace_cataclysm_*.csv'), key=os.path.getmtime)
fn = fs[-1]
print('PF:', fn)
with open(fn, encoding='utf-8', errors='replace') as f:
    reader = csv.reader(f)
    hdr = next(reader)
    rows = list(reader)
print(hdr)
fr_c = hdr.index('frame') if 'frame' in hdr else 0
px_c = -1; py_c = -1; vx_c = -1
for i,h in enumerate(hdr):
    if h.lower() in ('px','x_px','x'): px_c = i
    if h.lower() in ('py','y_px','y'): py_c = i
    if h.lower() in ('velx_fixed','vel_x','vx'): vx_c = i
print(f'fr={fr_c} px={px_c} py={py_c} vx={vx_c}')
print('frame  px   py   vx')
for r in rows:
    try:
        f = int(r[fr_c])
        if 2870 <= f <= 2900:
            print(f"{f:>5} {r[px_c]:>5} {r[py_c]:>4} {(r[vx_c] if vx_c>=0 else ''):>6}")
    except: pass

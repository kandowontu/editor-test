import os, glob, csv
T = os.environ['TEMP']
fs = sorted(glob.glob(T + r'\famidash_mesen_trace_cataclysm_*.csv'), key=os.path.getmtime)
fn = fs[-1]
print(fn)
with open(fn, encoding='utf-8', errors='replace') as f:
    lines = f.readlines()
hdr_line = None
for i, ln in enumerate(lines):
    if ln.startswith('rom_frame') or ln.startswith('frame') or ln.startswith('sim_cursor'):
        hdr_line = i; break
hdr = lines[hdr_line].strip().split(',')
print('hdr:', hdr[:8])
rows = [ln.strip().split(',') for ln in lines[hdr_line+1:] if ln.strip()]
print(f'rows: {len(rows)}')

# Find columns
def col(n):
    try: return hdr.index(n)
    except: return -1
sc = col('sim_cursor'); rf = col('rom_frame')
px = col('px'); py = col('py'); sx = col('scrollx')
print(f'sim_cursor={sc} rom={rf} px={px} py={py} sx={sx}')

prev_px = None; prev_sim = None
for r in rows:
    try:
        s = int(r[sc])
        if 2870 <= s <= 2905:
            cur_px = int(r[px])
            d = (cur_px - prev_px) if prev_px is not None else 0
            print(f'sim={s:4d} rom={r[rf]:>5} px={cur_px:>5} dx={d:+2d} py={r[py]:>4} sx={r[sx]:>5}')
            prev_px = cur_px
    except Exception as e: pass

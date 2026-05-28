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
sx_c = hdr.index('scrollx')
print("rom  sim  px   py  scrollx  scrx32  scrn_p_x")
for r in rows:
    try:
        s = int(r[sim_c])
        rom = int(r[rom_c])
        px = int(r[px_c])
        sx = int(r[sx_c])
    except: continue
    if 2867 <= s <= 2900:
        # scrollx may be signed 32-bit; treat as unsigned 16
        sx16 = sx & 0xFFFF if sx >= 0 else (sx + 0x100000000) & 0xFFFF
        # alternative: low byte only
        scrn = (px - sx16) & 0xFFFF
        print(f"{rom:>5} {s:>5} {px:>5} {r[py_c]:>3}  {sx:>8}  {sx16:>5}  {scrn:>5}")

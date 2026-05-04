import os
fp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_mesen_trace.csv'
rom_by_sc = {}
with open(fp) as fh:
    for line in fh:
        s = line.strip()
        if not s or s.startswith('nes_y_offset') or s.startswith('rom_frame') or s.startswith('#'):
            continue
        p = s.split(',')
        try:
            rf = int(p[0]); sc = int(p[1])
        except Exception:
            continue
        rom_by_sc[sc] = (int(p[2]), int(p[3]), int(p[10]))  # px, py, vel_y

fp2 = os.path.join(os.environ['TEMP'], 'famidash_pf_trace.csv')
data = open(fp2, 'rb').read()
if data[:2] == b'\xff\xfe':
    data = data.decode('utf-16').encode('utf-8')
lines = data.decode('utf-8', errors='replace').splitlines()

# Find the column header
hdr = lines[0].split(',')
print('PF cols:', hdr[:12])
xi = hdr.index('X_px')
yi = hdr.index('Y_px')
print('xi=', xi, 'yi=', yi)
# Show X divergence at increments
print('  pf_f| pf_x pf_y | rom_x rom_y | dx dy')
for l in lines[1:]:
    p = l.split(',')
    try:
        f = int(p[0])
    except Exception:
        continue
    if f % 500 != 0 and not (10780 <= f <= 10800):
        continue
    pfx = int(p[xi]) if xi else None
    pfy = int(p[yi]) if yi else None
    sc = f + 1
    if sc in rom_by_sc:
        rx, ry, _ = rom_by_sc[sc]
        # py in ROM trace already includes +8 (sprite center). PF yDisplayPx also +8?
        print(f'  {f:>5}|{pfx:>5}{pfy:>5} |{rx:>5}{ry:>5} | {pfx-rx:+3d} {pfy-ry:+3d}')

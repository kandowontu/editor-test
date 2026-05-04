import os, sys

# ROM rows
rom_rows = []
fp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_mesen_trace.csv'
with open(fp) as fh:
    for line in fh:
        s=line.strip()
        if not s or s.startswith('nes_y_offset') or s.startswith('rom_frame') or s.startswith('#'): continue
        p = s.split(',')
        try: rf=int(p[0]); sc=int(p[1])
        except: continue
        try:
            sx = int(p[8])  # scrollx
            sy = int(p[9])  # scrolly (linearized)
            sy_subpx = int(p[15])
            sy_raw = int(p[19])
        except: continue
        try: py = int(p[3])
        except: py = -1
        rom_rows.append((rf, sc, py, sy, sy_subpx, sy_raw))
print(f'rom rows: {len(rom_rows)}')

# PF trace
fp2 = os.path.join(os.environ['TEMP'], 'famidash_pf_trace.csv')
data = open(fp2, 'rb').read()
if data[:2] == b'\xff\xfe': data = data.decode('utf-16').encode('utf-8')
lines = data.decode('utf-8', errors='replace').splitlines()
pf = {}
for l in lines:
    p = l.split(',')
    try: f = int(p[0])
    except: continue
    pf[f] = (int(p[7]), int(p[9]), int(p[8]), int(p[20], 16))  # Y_px, CamY_px, onGround, Y_lowB

# Walk: for each ROM row, compute cam delta vs PF
# sim_cursor is 1-based, so PF frame = sc-1
print('  rf  sc | rom_y rom_cam rom_subpx | pf_y pf_cam pf_y_low | dy dcam')
last_dcam = 0
for rf, sc, py, sy, sub, sy_raw in rom_rows:
    si = sc - 1
    if si not in pf: continue
    pfy, pfcam, pfog, pflow = pf[si]
    dcam = pfcam - sy
    dy = pfy - py
    if abs(dcam - last_dcam) >= 1 or rf < 50 or rf%500==0:
        print(f' {rf:>5} {sc:>5} | {py:>4} {sy:>4} {sub:>3} | {pfy:>4} {pfcam:>4} 0x{pflow:02X} | {dy:>4} {dcam:>4}')
        last_dcam = dcam

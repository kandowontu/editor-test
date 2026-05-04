import csv

mp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_mesen_trace.csv'
rows = list(csv.reader(open(mp)))
hdr = rows[1]
sci  = hdr.index('sim_cursor')
pyi  = hdr.index('py')
syi  = hdr.index('scrolly')
sysi = hdr.index('scroll_y_subpx')
ryi  = hdr.index('raw_y')
cdi  = hdr.index('cube_data')
vyi  = hdr.index('vel_y')
rom = {}
for r in rows[2:]:
    if not r or not r[0].isdigit():
        continue
    sc = int(r[sci])
    if sc in rom:
        continue
    rom[sc] = (int(r[pyi]), int(r[syi]), int(r[sysi]), int(r[ryi]), int(r[cdi]), int(r[vyi]))

tp = r'C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv'
data = open(tp, 'rb').read()
if data[:2] == b'\xff\xfe':
    data = data.decode('utf-16').encode('utf-8')
lines = data.decode('utf-8', errors='replace').splitlines()
pfhdr = lines[0].split(',')
fi   = pfhdr.index('frame')
ylbi = pfhdr.index('Y_lowB')
clbi = pfhdr.index('CamY_lowB')
yfi  = pfhdr.index('Y_fixed')
cfi  = pfhdr.index('CamY_fixed')
ypi  = pfhdr.index('Y_px')
cpi  = pfhdr.index('CamY_px')
vyfi = pfhdr.index('VelY_fixed')
pf = {}
for ln in lines[1:]:
    p = ln.split(',')
    if not p[fi].isdigit():
        continue
    f = int(p[fi])
    pf[f] = {
        'Y_fixed':    int(p[yfi], 16),
        'CamY_fixed': int(p[cfi], 16),
        'Y_lowB':     int(p[ylbi], 16),
        'CamY_lowB':  int(p[clbi], 16),
        'Y_px':       int(p[ypi]),
        'CamY_px':    int(p[cpi]),
        'VelY':       int(p[vyfi], 16),
    }

# NES world Y full = currplayer_y_fixed + scroll_y_fixed
# currplayer_y_fixed = rawy (16.8)
# scroll_y_fixed     = (scrolly << 8) | scroll_y_subpx
# world subpixel = (rawy_low + scroll_y_subpx) mod 256

print('PF f | NES sc | NES py sy syx rawy_lo | nesWorldSub | PF Y.sub Cam.sub | dW dC | NES py vs PF Y_px | velY')
print('-' * 130)
divergences = []
for sc in sorted(rom.keys()):
    f = sc - 1
    if f not in pf:
        continue
    py, sy, sy_sub, rawy, cd, nvy = rom[sc]
    rawy_lo = rawy & 0xFF
    nes_world_sub = (rawy_lo + sy_sub) & 0xFF
    pfd = pf[f]
    ysub = pfd['Y_lowB']
    csub = pfd['CamY_lowB']
    dW = ysub - nes_world_sub
    dC = csub - sy_sub
    pf_pyi = pfd['Y_px']
    diverge_sub = (ysub != nes_world_sub) or (csub != sy_sub)
    diverge_pix = (pf_pyi != py)
    show = diverge_sub or diverge_pix or sc < 6 or sc in (924, 925, 926, 927, 928) or (sc % 100 == 0)
    if show:
        marker = ''
        if diverge_pix:
            marker = '*PIX*'
        elif diverge_sub:
            marker = 'sub'
        print(f'{f:>4} | {sc:>4} | {py:>4} {sy:>3} {sy_sub:>3} {rawy_lo:>3} | {nes_world_sub:>11} | 0x{ysub:02X}    0x{csub:02X}     | {dW:+4d} {dC:+4d} | {py:>4} vs {pf_pyi:>4} | {nvy:>5}  {marker}')
    if diverge_sub:
        divergences.append(sc)

print()
print(f'Total subpixel-divergent frames: {len(divergences)}')
if divergences:
    print(f'First divergence at sc={divergences[0]}')
    print(f'First 20: {divergences[:20]}')

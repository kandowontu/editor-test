import csv
mt = r'C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_dorabaebasic6_20260521_134808_213.csv'
pf = r'C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_dorabaebasic6_20260521_134003_153.csv'

def sgn16(x):
    return x - 0x10000 if x >= 0x8000 else x

def sgn32(x):
    return x - 0x100000000 if x >= 0x80000000 else x

with open(mt, 'r') as f:
    lines = f.readlines()
mtr = list(csv.DictReader(lines[1:]))
with open(pf, 'r') as f:
    pfr = list(csv.DictReader(f))
pfd = {}
for r in pfr:
    try: pfd[int(r['frame'])] = r
    except: pass
mtd = {}
for r in mtr:
    try: mtd[int(r['sim_cursor'])] = r
    except: pass

print('PF.f | sim | PFy PFvy | MTy MTvy | dY dVY | onG grF mode')
import sys
start = int(sys.argv[1]) if len(sys.argv) > 1 else 920
end = int(sys.argv[2]) if len(sys.argv) > 2 else 955
for f in range(start, end):
    p = pfd.get(f)
    m = mtd.get(f + 1)
    if p and m:
        py = int(p['Y_px'])
        mpy = int(m['py'])
        pvy = sgn32(int(p['VelY_fixed'], 16))
        mvy = sgn16(int(m['vel_y']))
        print(f"{f:4d} | {f+1:4d} | {py:4d} {pvy:+6d} | {mpy:4d} {mvy:+6d} | "
              f"{py-mpy:+4d} {pvy-mvy:+6d} | {p['onGround']} {p['gravFlipped']} {p['mode']}")

import csv

pf = r'C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_dorabaebasic6_20260521_152651_184.csv'
mt = r'C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_dorabaebasic6_20260521_153510_492.csv'

with open(pf, 'r') as f:
    pfr = list(csv.DictReader(f))
with open(mt, 'r') as f:
    lines = f.readlines()
mtr = list(csv.DictReader(lines[1:]))


def sgn32(x):
    return x - 0x100000000 if x >= 0x80000000 else x


def sgn16(x):
    return x - 0x10000 if x >= 0x8000 else x


pfd = {}
for r in pfr:
    try:
        pfd[int(r['frame'])] = r
    except Exception:
        pass

mtd = {}
for r in mtr:
    try:
        mtd[int(r['sim_cursor'])] = r
    except Exception:
        pass

common = sorted(set(pfd) & set((s - 1) for s in mtd))
rows = []
for f in common:
    p = pfd[f]
    m = mtd[f + 1]
    try:
        py = int(p['Y_px'])
        mpy = int(m['py'])
        pvy = sgn32(int(p['VelY_fixed'], 16))
        mvy = sgn16(int(m['vel_y']))
        pgf = int(p['gravFlipped'])
        # mt cp_gravity (1 = flipped) — check column
        mgf = int(m.get('cp_gravity', '0') or '0')
    except Exception:
        continue
    dY = py - mpy
    dVY = pvy - mvy
    rows.append((f, py, mpy, dY, pvy, mvy, dVY, int(p['mode']), int(m['gamemode']),
                 int(p['onGround']), pgf, mgf))

print(f'Total rows: {len(rows)}; max frame: {rows[-1][0] if rows else "n/a"}')

# Find ALL frames where |dY|>=1 or |dVY|>=1
print('\nFirst 50 frames with |dY|>=1 or |dVY|>=10:')
cnt = 0
for r in rows:
    if abs(r[3]) >= 1 or abs(r[6]) >= 10:
        print(f'  f={r[0]:5d} PFy={r[1]:4d} MTy={r[2]:4d} dY={r[3]:+4d} '
              f'PFvy={r[4]:+6d} MTvy={r[5]:+6d} dVY={r[6]:+6d} '
              f'pgm={r[7]} mgm={r[8]} onG={r[9]} pGrF={r[10]} mGrF={r[11]}')
        cnt += 1
        if cnt >= 50:
            break

# All events (>=1 frame |dY|>=1)
events = []
cur = None
for r in rows:
    if abs(r[3]) >= 1:
        if cur is None:
            cur = [r[0], r[0], r[3], r[3]]
        cur[1] = r[0]
        if abs(r[3]) > abs(cur[3]):
            cur[3] = r[3]
    else:
        if cur:
            events.append(cur)
            cur = None
if cur:
    events.append(cur)
print(f'\nTotal dY events: {len(events)}')
print('All events with peak|dY|>=2:')
for e in events:
    if abs(e[3]) >= 2:
        print(f'  f={e[0]}..{e[1]} len={e[1]-e[0]+1} peak={e[3]:+d}')

# Gravity-flip mismatch
print('\nGravFlipped mismatch frames (first 30):')
cnt = 0
for r in rows:
    if r[10] != r[11]:
        print(f'  f={r[0]:5d} pgm={r[7]} mgm={r[8]} pGrF={r[10]} mGrF={r[11]} PFy={r[1]} MTy={r[2]} dY={r[3]:+d}')
        cnt += 1
        if cnt >= 30:
            break


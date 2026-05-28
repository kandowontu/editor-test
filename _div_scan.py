import csv

pf = r'C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_dorabaebasic6_20260521_134003_153.csv'
mt = r'C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_dorabaebasic6_20260521_134808_213.csv'

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
    except Exception:
        continue
    dY = py - mpy
    dVY = pvy - mvy
    rows.append((f, py, mpy, dY, pvy, mvy, dVY, int(p['mode']), int(m['gamemode']),
                 int(p['onGround']), int(p['gravFlipped'])))

print(f'Total rows: {len(rows)}')

# Find ALL frames where |dY|>=1 or |dVY|>=1
print('\nFirst 30 frames with |dY|>=1 or |dVY|>=10:')
cnt = 0
for r in rows:
    if abs(r[3]) >= 1 or abs(r[6]) >= 10:
        print(f'  f={r[0]:5d} PFy={r[1]:4d} MTy={r[2]:4d} dY={r[3]:+4d} '
              f'PFvy={r[4]:+6d} MTvy={r[5]:+6d} dVY={r[6]:+6d} '
              f'pgm={r[7]} mgm={r[8]} onG={r[9]} grF={r[10]}')
        cnt += 1
        if cnt >= 30:
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

# Velocity events
vev = []
cur = None
for r in rows:
    if abs(r[6]) >= 1:
        if cur is None:
            cur = [r[0], r[0], r[6], r[6]]
        cur[1] = r[0]
        if abs(r[6]) > abs(cur[3]):
            cur[3] = r[6]
    else:
        if cur:
            vev.append(cur)
            cur = None
if cur:
    vev.append(cur)
print(f'\nTotal dVY events: {len(vev)}')
print('First 30 vy events (any):')
for e in vev[:30]:
    print(f'  f={e[0]}..{e[1]} len={e[1]-e[0]+1} peak={e[3]:+d}')

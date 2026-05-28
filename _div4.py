import os
T = os.environ['TEMP']
PF = T + r'\famidash_pf_trace_cataclysm_20260520_142348_350.csv'
MT = T + r'\famidash_mesen_trace_cataclysm_20260520_145022_953.csv'

pf = {}
with open(PF) as f:
    f.readline()
    for line in f:
        p = line.rstrip().split(',')
        try: fr = int(p[0])
        except: continue
        pf[fr] = p

mt = []  # list of (sim, parts) preserving duplicates
with open(MT) as f:
    f.readline(); f.readline()
    for line in f:
        parts = line.rstrip('\n').split(',')
        extra = len(parts) - 30
        if extra > 0:
            parts = parts[:22] + [','.join(parts[22:22+extra+1])] + parts[22+extra+1:]
        try: s = int(parts[1])
        except: continue
        mt.append((s, parts))

# Index MT by sim - take only LAST occurrence per sim (latest state per cursor)
mt_last = {}
for s, parts in mt:
    mt_last[s] = parts

print('sim |  PFx  PFy  PFvy |  MTx  MTy  MTvy | dX dY ')
for s in range(2860, 2935):
    if s not in pf or s not in mt_last: continue
    p = pf[s]
    m = mt_last[s]
    pfx = int(p[6]); pfy = int(p[7])
    pfvy = int(p[3], 16) if p[3].startswith('0x') else int(p[3])
    if pfvy >= 0x80000000: pfvy -= 0x100000000
    if pfvy >= 0x8000: pfvy_lo = pfvy - 0x10000 if pfvy >= 0x8000 else pfvy
    pfvy_lo = ((pfvy & 0xFFFF) ^ 0x8000) - 0x8000
    mtx = int(m[2]); mty = int(m[3]); mtvy = int(m[10])
    print(f'{s:4d}|  {pfx:5d} {pfy:4d} {pfvy_lo:6d} | {mtx:5d} {mty:4d} {mtvy:6d} | {mtx-pfx:+3d} {mty-pfy:+3d}')

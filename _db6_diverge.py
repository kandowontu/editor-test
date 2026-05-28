import csv, os, glob, sys

tmp = os.environ['TEMP']
pf_file = max(glob.glob(os.path.join(tmp, 'famidash_pf_trace_dorabaebasic6_*.csv')), key=os.path.getmtime)
mt_file = max(glob.glob(os.path.join(tmp, 'famidash_mesen_trace_dorabaebasic6_*.csv')), key=os.path.getmtime)
print('PF:', os.path.basename(pf_file))
print('MT:', os.path.basename(mt_file))

# Read PF
pf = {}
with open(pf_file, newline='') as f:
    r = csv.DictReader(f)
    for row in r:
        try:
            f_ = int(row['frame'])
        except:
            continue
        pf[f_] = row

# Read MT, skip first line "nes_y_offset,528"
with open(mt_file, newline='') as f:
    first = f.readline()
    r = csv.DictReader(f)
    mt = {}
    for row in r:
        try:
            sc = int(row['sim_cursor'])
        except:
            continue
        mt[sc] = row

# PF frame N == MT sim_cursor (N+1)
common = sorted(set(pf.keys()) & set(k-1 for k in mt.keys()))
print('aligned frames:', len(common), 'min', common[0] if common else None, 'max', common[-1] if common else None)

# Compare fields
def fmt(v): return v if v else '?'

last_match = -1
for f_ in common:
    p = pf[f_]
    m = mt[f_+1]
    def toi(v):
        v=v.strip()
        if v.startswith('0x') or v.startswith('-0x'):
            n = int(v,16)
            if n >= 0x80000000: n -= 0x100000000
            return n
        try: return int(v)
        except: return None
    px = toi(p['X_px']); py = toi(p['Y_px']); pvy = toi(p['VelY_fixed'])
    mx = toi(m['px']); my = toi(m['py']); mvy = toi(m['vel_y'])
    if None in (px,py,pvy,mx,my,mvy): continue
    if px != mx or py != my or pvy != mvy:
        print(f"DIVERGE f={f_} sim={f_+1}")
        print(f"  PF X={px} Y={py} vy={pvy} mode={p.get('mode')} grav={p.get('gravFlipped')} ground={p.get('onGround')}")
        print(f"  MT px={mx} py={my} vy={mvy} mode={m.get('gamemode')} gm={m.get('gravity_mod')} a_cur={m.get('a_cur')}")
        # Show 5 frames before
        print('--- previous frames ---')
        for ff in range(max(0,f_-6), f_):
            if ff in pf and (ff+1) in mt:
                pp = pf[ff]; mm = mt[ff+1]
                print(f"  f={ff} PF X={pp['X_px']} Y={pp['Y_px']} vy={pp['VelY_fixed']} mode={pp.get('mode')} | MT px={mm['px']} py={mm['py']} vy={mm['vel_y']} mode={mm.get('gamemode')} gm={mm.get('gravity_mod')}")
        # show 10 forward
        print('--- after ---')
        for ff in range(f_, min(f_+10, common[-1])):
            if ff in pf and (ff+1) in mt:
                pp = pf[ff]; mm = mt[ff+1]
                print(f"  f={ff} PF X={pp['X_px']} Y={pp['Y_px']} vy={pp['VelY_fixed']} mode={pp.get('mode')} | MT px={mm['px']} py={mm['py']} vy={mm['vel_y']} mode={mm.get('gamemode')} gm={mm.get('gravity_mod')}")
        break
    last_match = f_
print('last matching frame:', last_match)

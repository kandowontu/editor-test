import csv, os
T = os.environ['TEMP']
pf_path = T + r'\famidash_pf_trace_cataclysm_20260518_151738_650.csv'
mt_path = T + r'\famidash_mesen_trace_cataclysm_20260518_151724_006.csv'

pf = {}
with open(pf_path, newline='') as f:
    r = csv.reader(f)
    header = next(r)
    for row in r:
        try:
            fr = int(row[0])
        except: continue
        # X_fixed col1, Y_fixed col2, VelY_fixed col3 (hex strings), X_px col6, Y_px col7, mode col11, mini col23
        def h(s): return int(s, 16) if s.startswith('0x') else int(s)
        vy = h(row[3])
        if vy >= 0x8000: vy -= 0x10000  # signed 16
        pf[fr] = dict(x=int(row[6]), y=int(row[7]), vy=vy,
                      xf=h(row[1]), yf=h(row[2]),
                      mode=int(row[11]), mini=int(row[23]))

mt = {}
NCOLS = 30
# header column indices we need
COL = dict(rom_frame=0, sim_cursor=1, px=2, py=3, a_next=4, a_cur=5,
           raw_x=6, raw_y=7, scrollx=8, scrolly=9, vel_y=10,
           gamemode=14, death_pc=21, death_ctx=22, mini=24)
with open(mt_path) as f:
    f.readline()  # nes_y_offset,528
    f.readline()  # header
    for line in f:
        parts = line.rstrip('\n').split(',')
        # death_ctx (col 22) may contain commas; collapse extras into it
        extra = len(parts) - NCOLS
        if extra > 0:
            parts = parts[:22] + [','.join(parts[22:22+extra+1])] + parts[22+extra+1:]
        try:
            s = int(parts[COL['sim_cursor']])
        except: continue
        if s in mt: continue
        vy = int(parts[COL['vel_y']])
        if vy >= 0x8000: vy -= 0x10000
        mt[s] = dict(x=int(parts[COL['px']]), y=int(parts[COL['py']]), vy=vy,
                     rawx=int(parts[COL['raw_x']]), rawy=int(parts[COL['raw_y']]),
                     mode=int(parts[COL['gamemode']]), mini=int(parts[COL['mini']]),
                     a_next=int(parts[COL['a_next']]), a_cur=int(parts[COL['a_cur']]))

# find first divergences
fdx=fdy=fmode=fmini=fvy=None
for i in sorted(set(pf)&set(mt)):
    if i > 412: break
    p=pf[i]; m=mt[i]
    if fdx is None and p['x']!=m['x']: fdx=i
    if fdy is None and p['y']!=m['y']: fdy=i
    if fmode is None and p['mode']!=m['mode']: fmode=i
    if fmini is None and p['mini']!=m['mini']: fmini=i
    if fvy is None and p['vy']!=m['vy']: fvy=i

print(f'FIRST dx mismatch (x):    {fdx}')
print(f'FIRST dy mismatch (y):    {fdy}')
print(f'FIRST vy mismatch:        {fvy}')
print(f'FIRST mode mismatch:      {fmode}')
print(f'FIRST mini mismatch:      {fmini}')

def dump(rng, label):
    print(f'\n--- {label} ---')
    print(f'{"sim":>4} {"PFx":>5} {"MTx":>5} {"dx":>4} {"PFy":>4} {"MTy":>4} {"dy":>4} {"PFvy":>6} {"MTvy":>6} {"PFmode":>6} {"MTmode":>6} {"PFmini":>6} {"MTmini":>6}')
    for i in rng:
        if i in pf and i in mt:
            p=pf[i]; m=mt[i]
            print(f'{i:>4} {p["x"]:>5} {m["x"]:>5} {m["x"]-p["x"]:>4} {p["y"]:>4} {m["y"]:>4} {m["y"]-p["y"]:>4} {p["vy"]:>6} {m["vy"]:>6} {p["mode"]:>6} {m["mode"]:>6} {p["mini"]:>6} {m["mini"]:>6}')

if fdy is not None:
    dump(range(max(0,fdy-3), fdy+10), 'around first dy divergence')
if fvy is not None:
    dump(range(max(0,fvy-3), fvy+10), 'around first vy divergence')
if fmode is not None:
    dump(range(max(0,fmode-3), fmode+10), 'around first mode change divergence')
dump(range(385, 413), 'around death (sim 411)')

# Mesen log appears to sample 1 frame LATE relative to PF. Compare PF[N] vs MT[N+1].
print('\n\n========== SHIFTED COMPARISON: PF[N] vs MT[N+1] ==========')
def dump_shifted(rng, label):
    print(f'\n--- {label} (shifted by +1) ---')
    print(f'{"PFsim":>5} {"PFx":>5} {"MTx":>5} {"dx":>4} {"PFy":>4} {"MTy":>4} {"dy":>4}')
    for i in rng:
        if i in pf and (i+1) in mt:
            p=pf[i]; m=mt[i+1]
            print(f'{i:>5} {p["x"]:>5} {m["x"]:>5} {m["x"]-p["x"]:>4} {p["y"]:>4} {m["y"]:>4} {m["y"]-p["y"]:>4}')

fdx2=fdy2=None
for i in sorted(pf):
    if (i+1) not in mt: continue
    if i > 411: break
    p=pf[i]; m=mt[i+1]
    if fdx2 is None and p['x']!=m['x']: fdx2=i
    if fdy2 is None and p['y']!=m['y']: fdy2=i
print(f'\nShifted FIRST dx mismatch PF[N] vs MT[N+1]: {fdx2}')
print(f'Shifted FIRST dy mismatch PF[N] vs MT[N+1]: {fdy2}')
if fdy2 is not None:
    dump_shifted(range(max(0,fdy2-3), fdy2+10), 'around first shifted dy')
if fdx2 is not None and fdx2 != fdy2:
    dump_shifted(range(max(0,fdx2-3), fdx2+10), 'around first shifted dx')
dump_shifted(range(360, 412), 'around mode change + death (shifted)')

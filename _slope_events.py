import csv, re, sys
from collections import defaultdict

PF_CSV = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_dorabaebasic6_20260521_125754_255.csv"
MT_CSV = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_dorabaebasic6_20260521_130555_774.csv"
PF_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_dorabaebasic6_20260521_125754.txt"
MT_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_physics_debug_dorabaebasic6_20260521_130555_774.log"
OUT = r"C:\Editor Test\_slope_events.txt"

OFF_Y = 0  # will compute
OFF_X = 235

def parse_int(s):
    s=(s or "").strip()
    if not s: return None
    try:
        if s.lower().startswith("0x"): return int(s,16)
        if any(c in s.lower() for c in "abcdef"): return int(s,16)
        return int(s)
    except: return None

def s32(v):
    if v is None: return None
    if v>=0x80000000: v-=0x100000000
    return v

# Load PF
pf={}
with open(PF_CSV, newline='') as f:
    r=csv.DictReader(f)
    for row in r:
        try: fr=int(row['frame'])
        except: continue
        try: ypx=int(row['Y_px'])
        except: ypx=None
        try: xpx=int(row['X_px'])
        except: xpx=None
        pf[fr]={'X_px':xpx,'Y_px':ypx,
                'VelY_fixed': s32(parse_int(row.get('VelY_fixed'))),
                'onGround': (row.get('onGround') or '').strip(),
                'gravFlipped': (row.get('gravFlipped') or '').strip()}

# Load MT
mt={}
mt_cols=None
with open(MT_CSV, newline='') as f:
    meta=f.readline()
    r=csv.DictReader(f)
    mt_cols=r.fieldnames
    for row in r:
        sc=(row.get('sim_cursor') or '').strip()
        if not sc.isdigit(): continue
        fr=int(sc)-1
        try: py=int(row['py'])
        except: continue
        try:
            pxr=int(row['px'])
            if pxr>=0x80000000: pxr-=0x100000000
        except: pxr=0
        mt[fr]={'Y_px':py,'X_px':(pxr>>8)}

print(f"MT columns: {mt_cols}", file=sys.stderr)

# Compute off_y from 0..200
calib=[f for f in pf if f in mt and 0<=f<=200 and pf[f]['Y_px'] is not None]
if calib:
    OFF_Y=round(sum(pf[f]['Y_px']-mt[f]['Y_px'] for f in calib)/len(calib))
print(f"OFF_Y={OFF_Y}", file=sys.stderr)

def dY(f):
    if f in pf and f in mt and pf[f]['Y_px'] is not None:
        return pf[f]['Y_px']-(mt[f]['Y_px']+OFF_Y)
    return None

# Load PF log grouped by frame
log_by_frame=defaultdict(list)
fre=re.compile(r'\[PF f=(\d+)\]')
slope_re=re.compile(r'\[PF f=(\d+)\] \[SLOPE/')
eject_re=re.compile(r'\[PF f=(\d+)\].*\[EJECT-DIAG\]')

slope_frames=set()
slope_lines_by_frame=defaultdict(list)
eject_frames=set()
eject_lines_by_frame=defaultdict(list)

with open(PF_LOG,'r',errors='replace') as fh:
    for line in fh:
        m=fre.search(line)
        if not m: continue
        fr=int(m.group(1))
        log_by_frame[fr].append(line.rstrip())
        if '[SLOPE/' in line:
            slope_frames.add(fr)
            slope_lines_by_frame[fr].append(line.rstrip())
        if '[EJECT-DIAG]' in line:
            eject_frames.add(fr)
            eject_lines_by_frame[fr].append(line.rstrip())

# Build sessions: contiguous slope_frames with gap<=3
sframes=sorted(slope_frames)
sessions=[]
if sframes:
    cur_s=sframes[0]; cur_e=sframes[0]; sl_count=1
    for f in sframes[1:]:
        if f - cur_e <= 3:
            cur_e=f; sl_count+=1
        else:
            sessions.append((cur_s,cur_e,sl_count))
            cur_s=f; cur_e=f; sl_count=1
    sessions.append((cur_s,cur_e,sl_count))

# Analyze sessions
tile_re=re.compile(r'(tile=0x[0-9a-fA-F]+|lst=0x[0-9a-fA-F]+|sT=0x[0-9a-fA-F]+|sT=\d+)')
results=[]
for s,e,k in sessions:
    peak=0; peak_f=None
    for f in range(s, e+11):
        d=dY(f)
        if d is None: continue
        if abs(d)>abs(peak): peak=d; peak_f=f
    resid=dY(e+5)
    # representative line
    rep=slope_lines_by_frame.get(s,[''])[0]
    tile=''
    m=tile_re.search(rep)
    if m: tile=m.group(1)
    else:
        for f in range(s,e+1):
            for ln in slope_lines_by_frame.get(f,[]):
                m=tile_re.search(ln)
                if m: tile=m.group(1); break
            if tile: break
    # vel before/at
    vy_before = pf.get(s-1,{}).get('VelY_fixed')
    vy_at = pf.get(s,{}).get('VelY_fixed')
    results.append({'s':s,'e':e,'k':k,'peak':peak,'peak_f':peak_f,
                    'resid':resid,'tile':tile,'rep':rep,
                    'vy_before':vy_before,'vy_at':vy_at})

flagged=[r for r in results if r['peak'] is not None and abs(r['peak'])>=1]

# Eject diag sessions
efs=sorted(eject_frames)
ej_sessions=[]
if efs:
    cs=efs[0]; ce=efs[0]
    for f in efs[1:]:
        if f-ce<=3: ce=f
        else:
            ej_sessions.append((cs,ce)); cs=f; ce=f
    ej_sessions.append((cs,ce))

# Largest divergence f>=2200 - find max |dY|
large=None
for f in sorted(set(pf)&set(mt)):
    if f<2200: continue
    d=dY(f)
    if d is None: continue
    if large is None or abs(d)>abs(large[1]):
        large=(f,d)

GRAV_TAGS=['[BALL_GRAV]','[PORTAL_GAMEMODE]','[PORTAL_INV]','[BALL_FLIP_CHK]','[BE_ENTRY]','[BE_CEIL]','[BE_EXIT_U]','[BE_EXIT_D]','[APPLY_GRAV]','gravity_mod','GravityMod','gravFlipped','[GRAV_MOD]']

grav_lines=[]
for f in range(2295,2321):
    for ln in log_by_frame.get(f,[]):
        if any(t in ln for t in GRAV_TAGS):
            grav_lines.append(ln)

# Write report
with open(OUT,'w') as f:
    f.write("=== Slope events report (dorabaebasic6) ===\n")
    f.write(f"OFF_Y={OFF_Y} OFF_X={OFF_X}\n")
    f.write(f"MT columns: {mt_cols}\n")
    f.write(f"Total slope sessions: {len(sessions)}\n")
    f.write(f"Sessions with peak|dY|>=1: {len(flagged)}\n\n")
    f.write("=== All slope sessions ===\n")
    for i,r in enumerate(results,1):
        flag='*' if r in flagged else ' '
        pk = r['peak'] if r['peak'] is not None else 0
        rs = r['resid'] if r['resid'] is not None else 0
        f.write(f"{flag}{i:3d}. f={r['s']}-{r['e']} len={r['e']-r['s']+1} sl={r['k']} peak_dY={pk:+d}@f{r['peak_f']} resid={rs:+d} tile={r['tile']} vyBefore={r['vy_before']} vyAt={r['vy_at']}\n")
        f.write(f"      rep: {r['rep'][:240]}\n")
    f.write("\n=== Flagged session log excerpts ===\n")
    for r in flagged:
        f.write(f"\n--- session f={r['s']}-{r['e']} peak_dY={r['peak']:+d} ---\n")
        for fr in range(r['s']-1, min(r['e']+4, r['s']+8)):
            for ln in log_by_frame.get(fr,[])[:6]:
                f.write(f"  {ln}\n")
    f.write("\n=== EJECT-DIAG sessions ===\n")
    f.write(f"Total: {len(ej_sessions)}\n")
    for cs,ce in ej_sessions:
        peak=0
        for fr in range(cs-1,ce+6):
            d=dY(fr)
            if d is not None and abs(d)>abs(peak): peak=d
        f.write(f"  f={cs}-{ce} peak_dY={peak:+d}\n")
        for ln in eject_lines_by_frame.get(cs,[])[:2]:
            f.write(f"    {ln}\n")
    f.write("\n=== Largest divergence f>=2200 ===\n")
    if large:
        f.write(f"  f={large[0]} dY={large[1]:+d}\n")
    f.write("\n=== Gravity-mod context f=2295..2320 ===\n")
    f.write(f"Lines found: {len(grav_lines)}\n")
    for ln in grav_lines:
        f.write(f"  {ln}\n")

# Stdout summary
print(f"OFF_Y={OFF_Y} OFF_X={OFF_X}")
print(f"MT columns: {mt_cols}")
print(f"Total slope sessions: {len(sessions)}")
print(f"Sessions with peak|dY|>=1: {len(flagged)}")
print("Flagged sessions:")
for i,r in enumerate(flagged,1):
    rs = r['resid'] if r['resid'] is not None else 0
    print(f"  {i}: f={r['s']}-{r['e']} (len={r['e']-r['s']+1} slopelen={r['k']}) peak_dY={r['peak']:+d} residual={rs:+d} tile={r['tile']}")
    print(f"     tag='{r['rep'][:160]}'")
print(f"EJECT-DIAG sessions: {len(ej_sessions)}")
for cs,ce in ej_sessions[:10]:
    peak=0
    for fr in range(cs-1,ce+6):
        d=dY(fr)
        if d is not None and abs(d)>abs(peak): peak=d
    print(f"  ej f={cs}-{ce} peak_dY={peak:+d}")
if large: print(f"Largest divergence f>=2200: f={large[0]} dY={large[1]:+d}")
print(f"Gravity-mod context lines (f=2295..2320): {len(grav_lines)}")
for ln in grav_lines[:8]:
    print(f"  {ln[:200]}")
print(f"Report: {OUT}")

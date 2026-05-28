import csv, re
from collections import defaultdict

PF_TRACE = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_dorabaebasic6_20260521_134003_153.csv"
MT_TRACE = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_dorabaebasic6_20260521_134808_213.csv"
PF_DEBUG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_dorabaebasic6_20260521_134003.txt"
MT_PHYS  = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_physics_debug_dorabaebasic6_20260521_134808_213.log"
OUT      = r"C:\Editor Test\_slope_full.txt"

def s16(v):
    v&=0xFFFF
    return v-0x10000 if v>=0x8000 else v
def s32(v):
    v&=0xFFFFFFFF
    return v-0x100000000 if v>=0x80000000 else v

pf={}
with open(PF_TRACE,newline='') as f:
    for row in csv.DictReader(f):
        try: fr=int(row['frame'])
        except: continue
        try:
            y=int(row['Y_px']); x=int(row['X_px'])
        except: continue
        try: vy=s32(int(row.get('VelY_fixed','0').strip(),16))
        except: vy=0
        pf[fr]={'y':y,'x':x,'vy':vy}

mt={}
with open(MT_TRACE,newline='') as f:
    f.readline()
    for row in csv.DictReader(f):
        try: sc=int(row['sim_cursor'])
        except: continue
        fr=sc-1
        try: py=int(row['py'])
        except: continue
        try: px=s16(int(row['px']))>>8
        except: px=0
        try: vy=s16(int(row['vel_y']))
        except: vy=0
        mt[fr]={'y':py,'x':px,'vy':vy}

common=sorted(set(pf)&set(mt))
print(f"PF frames: {len(pf)}, MT frames: {len(mt)}, common: {len(common)}")

dys=[mt[f]['y']-pf[f]['y'] for f in common if f<200]
off_y=round(sum(dys)/len(dys)) if dys else 0
print(f"off_y={off_y}")

# PF slope: Y=0xHHHHHHHH (Npx) format
pf_slope_re=re.compile(r'\[PF f=(\d+)\] \[SLOPE/([^\]]+)\]\s*(.*)')
ypx_re=re.compile(r'\((-?\d+)px\)')
yhex_re=re.compile(r'\bY=0x([0-9A-Fa-f]+)')
sT_re=re.compile(r'sT=([0-9A-Fa-fxX]+)')

pf_slope_all=[]
with open(PF_DEBUG,errors='replace') as f:
    for line in f:
        m=pf_slope_re.search(line)
        if not m: continue
        fr=int(m.group(1)); sub=m.group(2); rest=m.group(3).rstrip()
        ty=None
        mpx=ypx_re.search(rest)
        if mpx: ty=int(mpx.group(1))
        else:
            mh=yhex_re.search(rest)
            if mh:
                yf=int(mh.group(1),16)
                if yf>=0x80000000: yf-=0x100000000
                ty=yf>>8
        pf_slope_all.append((fr,sub,rest,ty))

pf_slope_replay=defaultdict(list)
pf_slope_bfs=0
for fr,sub,rest,ty in pf_slope_all:
    if fr not in pf: continue
    if ty is None or abs(ty-pf[fr]['y'])<=2:
        pf_slope_replay[fr].append((sub,rest))
    else:
        pf_slope_bfs+=1
pf_slope_frames=set(pf_slope_replay)
print(f"PF slope tags total={len(pf_slope_all)} REPLAY frames={len(pf_slope_frames)} BFS ignored={pf_slope_bfs}")

# MT physics: log has routine entry/exit lines. Slope tiles on NES = entries containing slope routines.
# Search the log for any line whose routine name contains 'slope' OR tile indices that look like slope.
# Lines are like: "  bg_coll_D.out pc=... tidx=XX ...". Use any tidx in known slope range, or routine names.
# Slope tile indices in famidash: typically 0x22 (lu22/rd22/ld22/ru22) per task. Check tidx field.
mt_slope_lines=defaultdict(list)
slope_tidx=set([0x22,0x23,0x24,0x25,0x26,0x27])  # broad guess for slope
cur_frame=None
slope_route_re=re.compile(r'slope',re.IGNORECASE)
tidx_re=re.compile(r'tidx=([0-9A-Fa-f]{2})')
with open(MT_PHYS,errors='replace') as f:
    for line in f:
        line=line.rstrip()
        mf=re.match(r'F=(\d+)\s+sim=(\d+)',line)
        if mf:
            cur_frame=int(mf.group(2))-1  # use sim_cursor-1 as frame alignment
            continue
        if cur_frame is None: continue
        hit=False
        if slope_route_re.search(line):
            hit=True
        else:
            mt2=tidx_re.search(line)
            if mt2:
                ti=int(mt2.group(1),16)
                if ti in slope_tidx:
                    hit=True
        if hit:
            mt_slope_lines[cur_frame].append(line.strip())

nes_slope_frames=set(mt_slope_lines)
print(f"NES slope frames: {len(nes_slope_frames)} ({sum(len(v) for v in mt_slope_lines.values())} lines)")

union=sorted((pf_slope_frames|nes_slope_frames)&set(common))

def sessions(frames,gap=3):
    fr=sorted(frames); out=[]; cur=[]
    for f in fr:
        if not cur or f-cur[-1]<=gap: cur.append(f)
        else: out.append(cur); cur=[f]
    if cur: out.append(cur)
    return out

pf_sessions=sessions(pf_slope_frames&set(common))
nes_sessions=sessions(nes_slope_frames&set(common))

state_mismatch=[]; pos_mismatch=[]; vel_mismatch=[]
for f in union:
    in_pf=f in pf_slope_frames; in_nes=f in nes_slope_frames
    dy=mt[f]['y']-pf[f]['y']-off_y
    dvy=mt[f]['vy']-(pf[f]['vy']>>8)
    if in_pf!=in_nes: state_mismatch.append(f)
    else:
        if dy!=0: pos_mismatch.append((f,dy))
        if dvy!=0: vel_mismatch.append((f,dvy))
print(f"state mismatches: {len(state_mismatch)}, pos: {len(pos_mismatch)}, vel: {len(vel_mismatch)}")

def session_info(frs):
    pdy=0; pdvy=0; sTs=set()
    for f in frs:
        if f in pf and f in mt:
            dy=mt[f]['y']-pf[f]['y']-off_y
            dvy=mt[f]['vy']-(pf[f]['vy']>>8)
            if abs(dy)>abs(pdy): pdy=dy
            if abs(dvy)>abs(pdvy): pdvy=dvy
        for sub,rest in pf_slope_replay.get(f,[]):
            m=sT_re.search(rest)
            if m: sTs.add(m.group(1))
    return pdy,pdvy,sTs

with open(OUT,'w',encoding='utf-8') as o:
    o.write("SLOPE FULL DUMP - db6\n")
    o.write(f"off_y={off_y}\n")
    o.write(f"PF slope tags total={len(pf_slope_all)}, REPLAY frames={len(pf_slope_frames)}, BFS ignored={pf_slope_bfs}\n")
    o.write(f"NES slope frames={len(nes_slope_frames)}\n")
    o.write(f"Union slope frames={len(union)}\n")
    o.write(f"PF REPLAY sessions={len(pf_sessions)}, NES sessions={len(nes_sessions)}\n")
    o.write(f"State mismatches={len(state_mismatch)}, pos mismatches={len(pos_mismatch)}, vel mismatches={len(vel_mismatch)}\n\n")

    o.write("=== SECTION A: PF REPLAY slope sessions ===\n")
    for s in pf_sessions:
        pdy,pdvy,sTs=session_info(s)
        o.write(f"  f={s[0]}..{s[-1]} n={len(s)} peak|dY|={abs(pdy)} peak|dVY|={abs(pdvy)} sT={sorted(sTs)}\n")
    o.write("\n=== SECTION B: NES slope sessions ===\n")
    for s in nes_sessions:
        pdy,pdvy,_=session_info(s)
        o.write(f"  f={s[0]}..{s[-1]} n={len(s)} peak|dY|={abs(pdy)} peak|dVY|={abs(pdvy)}\n")

    o.write("\n=== SECTION C: SYMMETRIC DIFFERENCES (state mismatch) ===\n")
    for f in state_mismatch:
        side="PF-only" if f in pf_slope_frames else "NES-only"
        pf_tag="; ".join(f"[{s}] {r}" for s,r in pf_slope_replay.get(f,[]))
        mt_tag=" || ".join(mt_slope_lines.get(f,[]))
        dy=mt[f]['y']-pf[f]['y']-off_y
        dvy=mt[f]['vy']-(pf[f]['vy']>>8)
        o.write(f"  f={f} {side} dY={dy} dVY={dvy}\n")
        if pf_tag: o.write(f"      PF: {pf_tag}\n")
        if mt_tag: o.write(f"      MT: {mt_tag}\n")

    o.write("\n=== SECTION D: Frame-by-frame dump per union session ===\n")
    for s in sessions(union):
        o.write(f"\n-- session f={s[0]}..{s[-1]} ({len(s)} frames) --\n")
        for f in s:
            pfy=pf[f]['y']; mty=mt[f]['y']; dy=mty-pfy-off_y
            pfvy_cmp=pf[f]['vy']>>8; mtvy=mt[f]['vy']; dvy=mtvy-pfvy_cmp
            ip='Y' if f in pf_slope_frames else 'N'
            inn='Y' if f in nes_slope_frames else 'N'
            o.write(f"  f={f} PFy={pfy} MTy={mty} dY={dy} | PFvy={pfvy_cmp} MTvy={mtvy} dVY={dvy} | PF={ip} NES={inn}\n")
            for sub,rest in pf_slope_replay.get(f,[]):
                o.write(f"      PF[{sub}] {rest}\n")
            for ln in mt_slope_lines.get(f,[]):
                o.write(f"      MT: {ln}\n")

print(f"wrote {OUT}")
print("\n--- SUMMARY ---")
print(f"calibration off_y={off_y}")
print(f"PF REPLAY slope sessions = {len(pf_sessions)}")
print(f"NES slope sessions       = {len(nes_sessions)}")
print(f"state-disagreement frames= {len(state_mismatch)}")
agree_bad=set(f for f,_ in pos_mismatch)|set(f for f,_ in vel_mismatch)
print(f"agree-on-slope but dY/dVY!=0 = {len(agree_bad)}")
print("\nfirst PF REPLAY sessions:")
for s in pf_sessions[:10]:
    pdy,pdvy,sTs=session_info(s)
    print(f"  f={s[0]}..{s[-1]} n={len(s)} peak|dY|={abs(pdy)} peak|dVY|={abs(pdvy)} sT={sorted(sTs)}")
print("\nfirst 20 disagreement frames:")
for f in state_mismatch[:20]:
    side="PF-only" if f in pf_slope_frames else "NES-only"
    tag=""
    if f in pf_slope_replay: tag=pf_slope_replay[f][0][1][:80]
    elif f in mt_slope_lines: tag=mt_slope_lines[f][0][:80]
    print(f"  f={f} {side}  {tag}")

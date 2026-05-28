import csv, re, os, sys
from collections import defaultdict

PF_CSV = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_dorabaebasic6_20260521_134003_153.csv"
MT_CSV = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_dorabaebasic6_20260521_134808_213.csv"
PF_DBG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_dorabaebasic6_20260521_134003.txt"
MT_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_physics_debug_dorabaebasic6_20260521_134808_213.log"
OUT_TXT = r"C:\Editor Test\_slope_db6.txt"

def s16(v):
    v = int(v) & 0xFFFF
    return v-0x10000 if v & 0x8000 else v
def s32(v):
    v = int(v) & 0xFFFFFFFF
    return v-0x100000000 if v & 0x80000000 else v

# Load PF
pf = {}
with open(PF_CSV, newline='') as f:
    r = csv.DictReader(f)
    pf_fields = r.fieldnames
    for row in r:
        try: fr = int(row['frame'])
        except: continue
        pf[fr] = row
print("PF fields:", pf_fields[:20], "... count=", len(pf))

# Load MT - skip first line
mt = {}
with open(MT_CSV, newline='') as f:
    first = f.readline()
    r = csv.DictReader(f)
    mt_fields = r.fieldnames
    for row in r:
        try: sc = int(row['sim_cursor'])
        except: continue
        pf_frame = sc - 1
        mt[pf_frame] = row
print("MT fields:", mt_fields[:20], "... count=", len(mt))
print("skipped line:", first.strip())

# Find Y column in PF
def find_col(fields, candidates):
    for c in candidates:
        for f in fields:
            if f.lower() == c.lower(): return f
    for c in candidates:
        for f in fields:
            if c.lower() in f.lower(): return f
    return None

pf_y = find_col(pf_fields, ['Y_px','y_px','Y','py'])
pf_x = find_col(pf_fields, ['X_px','x_px','X','px'])
pf_vy = find_col(pf_fields, ['VelY_fixed','vel_y_fixed','VelY','vel_y'])
pf_og = find_col(pf_fields, ['onGround','on_ground'])
pf_gf = find_col(pf_fields, ['gravFlipped','grav_flipped','gravity_flipped'])
mt_py = find_col(mt_fields, ['py'])
mt_px = find_col(mt_fields, ['px'])
mt_vy = find_col(mt_fields, ['vel_y'])
mt_gm = find_col(mt_fields, ['gamemode'])
mt_gmod = find_col(mt_fields, ['gravity_mod'])
print("PF cols:", pf_y, pf_x, pf_vy, pf_og, pf_gf)
print("MT cols:", mt_py, mt_px, mt_vy, mt_gm, mt_gmod)

common = sorted(set(pf.keys()) & set(mt.keys()))
print("common frames:", len(common), "range", common[0], common[-1] if common else None)

def gi(s):
    s=str(s).strip()
    if s.startswith('0x') or s.startswith('0X'): return int(s,16)
    try: return int(s)
    except: return int(float(s))

# Calibration 0..200
dys=[]; dxs=[]
for fr in common:
    if fr>200: break
    if fr<0: continue
    try:
        py = gi(pf[fr][pf_y]); my = gi(mt[fr][mt_py])
        px_v = gi(pf[fr][pf_x]); mx = gi(mt[fr][mt_px])
        dys.append(py-my); dxs.append(px_v - (mx>>8))
    except Exception as e:
        pass
off_y = sum(dys)/len(dys) if dys else 0
off_x = sum(dxs)/len(dxs) if dxs else 0
off_y_i = round(off_y); off_x_i = round(off_x)
print(f"Calibration N={len(dys)}  off_y={off_y:.3f} ({off_y_i})  off_x={off_x:.3f} ({off_x_i})")

# Compute dY, dVY per frame
diffs = {}
for fr in common:
    try:
        py = gi(pf[fr][pf_y]); my = gi(mt[fr][mt_py])
        dY = py - (my + off_y_i)
        pfv = s32(gi(pf[fr][pf_vy])) if pf_vy else 0
        mtv = s16(gi(mt[fr][mt_vy]))
        dVY = pfv - mtv
        diffs[fr] = (dY, pfv, mtv, dVY)
    except: pass

# STEP 1: find slope sessions in PF debug
slope_re = re.compile(r'\[PF f=(\d+)\] \[SLOPE/')
slope_frames = []
slope_lines = defaultdict(list)
with open(PF_DBG, 'r', errors='replace') as f:
    for line in f:
        m = slope_re.search(line)
        if m:
            fr = int(m.group(1))
            slope_frames.append(fr)
            slope_lines[fr].append(line.rstrip())
sf_sorted = sorted(set(slope_frames))
sessions = []
if sf_sorted:
    cur = [sf_sorted[0], sf_sorted[0]]
    for fr in sf_sorted[1:]:
        if fr - cur[1] <= 3:
            cur[1] = fr
        else:
            sessions.append(tuple(cur))
            cur = [fr, fr]
    sessions.append(tuple(cur))
print(f"Slope sessions: {len(sessions)}")

# Also load all PF debug lines per frame for VelY/tags
pf_dbg_by_frame = defaultdict(list)
frame_re = re.compile(r'\[PF f=(\d+)\]')
with open(PF_DBG, 'r', errors='replace') as f:
    for line in f:
        m = frame_re.search(line)
        if m:
            pf_dbg_by_frame[int(m.group(1))].append(line.rstrip())

# STEP 3: find dY events |dY|>=1, length>=2
events = []
fr_sorted = sorted(diffs.keys())
i=0
while i < len(fr_sorted):
    fr = fr_sorted[i]
    if abs(diffs[fr][0]) >= 1:
        j = i
        while j+1 < len(fr_sorted) and fr_sorted[j+1]==fr_sorted[j]+1 and abs(diffs[fr_sorted[j+1]][0])>=1:
            j += 1
        if j - i + 1 >= 2:
            events.append((fr_sorted[i], fr_sorted[j]))
        i = j+1
    else:
        i+=1
print(f"dY events: {len(events)}")

# Map slope-caused events: start within session or up to 5 frames after end
def find_causing_session(ev_start, ev_end):
    # event start within [s-3..e+5] OR event range overlaps [s-3..e+5]
    for s,e in sessions:
        lo, hi = s-3, e+5
        if (lo <= ev_start <= hi) or (lo <= ev_end <= hi) or (ev_start <= lo and ev_end >= hi):
            return (s,e)
    return None

slope_events = []
for ev in events:
    sess = find_causing_session(ev[0], ev[1])
    if sess:
        slope_events.append((ev, sess))
print(f"Slope-caused dY events: {len(slope_events)}")

# Build report
out = []
def w(s=""):
    out.append(s)
    print(s)

w("="*80)
w("SLOPE DIVERGENCE ANALYSIS - dorabaebasic6")
w("="*80)
w(f"PF trace: {PF_CSV}")
w(f"MT trace: {MT_CSV}")
w(f"Calibration over frames 0..200: N={len(dys)}")
w(f"  off_y = {off_y:.4f}  (using int {off_y_i})")
w(f"  off_x = {off_x:.4f}  (using int {off_x_i})")
w(f"Common frame range: {common[0]} .. {common[-1]}")
w(f"Total slope sessions: {len(sessions)}")
w(f"Total dY events (|dY|>=1, len>=2): {len(events)}")
w(f"Slope-caused dY events: {len(slope_events)}")
w("")

def get_tags(s,e):
    tags = []
    for fr in range(s,e+1):
        for ln in slope_lines.get(fr, []):
            m = re.search(r'\[SLOPE/([^\]]+)\]', ln)
            if m: tags.append((fr, m.group(1)))
    return tags

w("="*80)
w("PER-SESSION SUMMARY")
w("="*80)
session_info = []
for idx,(s,e) in enumerate(sessions):
    lo = max(common[0], s-2); hi = min(common[-1], e+15)
    peak = 0; peak_fr = s
    for fr in range(lo, hi+1):
        if fr in diffs:
            if abs(diffs[fr][0]) > abs(peak):
                peak = diffs[fr][0]; peak_fr = fr
    resid_fr = min(e+15, common[-1])
    resid = diffs.get(resid_fr,(0,))[0] if resid_fr in diffs else None
    tags = get_tags(s,e)
    tag_counts = defaultdict(int)
    for _,t in tags: tag_counts[t]+=1
    top_tag = max(tag_counts.items(), key=lambda x:x[1])[0] if tag_counts else "?"
    persists = resid is not None and abs(resid)>=1
    session_info.append((idx,s,e,peak,peak_fr,resid,persists,top_tag,tag_counts))
    w(f"#{idx:3d} f={s}..{e} (len={e-s+1})  peak|dY|={abs(peak)} @f{peak_fr} (signed {peak})  resid@f{resid_fr}={resid}  persist={persists}  topTag={top_tag}")

w("")
w("="*80)
w("DETAILED SLOPE-CAUSED DIVERGENCES")
w("="*80)
for k,(ev, sess) in enumerate(slope_events):
    es, ee = ev; ss, se = sess
    w(f"\n--- Divergence #{k} ---")
    w(f"Event frames: {es}..{ee} (len={ee-es+1})")
    w(f"Caused by slope session: {ss}..{se}")
    peakd=0; peak_fr=es
    for fr in range(es,ee+1):
        if fr in diffs and abs(diffs[fr][0])>abs(peakd):
            peakd=diffs[fr][0]; peak_fr=fr
    resid_fr = min(se+20, common[-1])
    resid = diffs.get(resid_fr,(None,))[0]
    w(f"Peak dY in event: {peakd} @ f{peak_fr}")
    w(f"Residual dY @ f{resid_fr} (slope_end+20): {resid}")
    tags = get_tags(ss,se)
    w(f"SLOPE tags ({len(tags)}):")
    for fr,t in tags[:50]:
        w(f"  f{fr}: SLOPE/{t}")
    if len(tags)>50: w(f"  ... +{len(tags)-50} more")
    w("VelY comparison (PF fixed signed vs MT vel_y signed):")
    win_lo = max(common[0], ss-3); win_hi = min(common[-1], se+5)
    for fr in range(win_lo, win_hi+1):
        if fr in diffs:
            dY,pfv,mtv,dvy = diffs[fr]
            mark = " <-slope" if ss<=fr<=se else ""
            w(f"  f{fr}: dY={dY} PF.VelY={pfv} MT.vel_y={mtv} dVY={dvy}{mark}")
    # PF debug velY lines in window
    w("PF debug velY lines in window:")
    cnt=0
    for fr in range(win_lo, win_hi+1):
        for ln in pf_dbg_by_frame.get(fr, []):
            if 'velY=' in ln or 'velY ' in ln or 'VelY' in ln:
                w(f"  {ln}"); cnt+=1
                if cnt>=40: break
        if cnt>=40: break

w("")
w("="*80)
w("FINAL SUMMARY: PERSISTENT SLOPE-CAUSED DIVERGENCES (|resid|>=1 after slope ends)")
w("="*80)
persistent = []
for k,(ev,sess) in enumerate(slope_events):
    ss,se = sess
    resid_fr = min(se+20, common[-1])
    resid = diffs.get(resid_fr,(None,))[0]
    if resid is not None and abs(resid)>=1:
        persistent.append((k,ev,sess,resid_fr,resid))
w(f"Count: {len(persistent)}")
for k,ev,sess,rf,r in persistent:
    w(f"  Div#{k} ev=f{ev[0]}..{ev[1]} slope=f{sess[0]}..{sess[1]} resid@f{rf}={r}")

with open(OUT_TXT,'w',encoding='utf-8') as f:
    f.write('\n'.join(out))
print("\nSaved:", OUT_TXT)


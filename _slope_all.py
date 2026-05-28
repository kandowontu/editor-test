import csv, re, sys, statistics
from collections import defaultdict

PF_CSV = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_dorabaebasic6_20260521_125754_255.csv"
MT_CSV = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_dorabaebasic6_20260521_130555_774.csv"
PF_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_dorabaebasic6_20260521_125754.txt"
OUT = r"C:\Editor Test\_slope_all.txt"

def parse_hex_or_int(s):
    s = (s or "").strip()
    if not s: return None
    try:
        if s.lower().startswith("0x"): return int(s,16)
        # if contains hex chars only
        if any(c in s.lower() for c in "abcdef"):
            return int(s,16)
        return int(s)
    except:
        return None

def s32(v):
    if v is None: return None
    if v >= 0x80000000: v -= 0x100000000
    return v

# Load PF
pf = {}
with open(PF_CSV, newline='') as f:
    r = csv.DictReader(f)
    for row in r:
        try: frame = int(row['frame'])
        except: continue
        yf = s32(parse_hex_or_int(row.get('Y_fixed')))
        xf = s32(parse_hex_or_int(row.get('X_fixed')))
        vy = s32(parse_hex_or_int(row.get('VelY_fixed')))
        # Use direct PF X_px/Y_px columns
        try: xpx = int(row['X_px'])
        except: xpx = (xf>>8) if xf is not None else None
        try: ypx = int(row['Y_px'])
        except: ypx = (yf>>8) if yf is not None else None
        pf[frame] = {
            'X_px': xpx, 'Y_px': ypx, 'VelY_fixed': vy,
            'onGround': (row.get('onGround') or '').strip(),
            'gravFlipped': (row.get('gravFlipped') or '').strip(),
        }

# Load MT (skip first metadata line)
mt = {}
with open(MT_CSV, newline='') as f:
    header_meta = f.readline()
    m = re.search(r'(-?\d+)', header_meta)
    nes_y_offset = int(m.group(1)) if m else None
    r = csv.DictReader(f)
    for row in r:
        sc = (row.get('sim_cursor') or '').strip()
        if not sc.isdigit(): continue
        frame = int(sc) - 1
        try: py = int(row['py'])
        except: continue
        try: px_raw = int(row['px'])
        except: px_raw = 0
        # px is uint32 representing signed sub-pixel maybe; for pixel just use //256 of signed
        px_s = px_raw if px_raw < 0x80000000 else px_raw - 0x100000000
        xpx = px_s >> 8
        vy = 0
        try:
            v = int(row['vel_y'])
            if v >= 0x8000: v -= 0x10000
            vy = v
        except: pass
        # last row for each sim_cursor wins (overwrite)
        mt[frame] = {'Y_px': py, 'X_px': xpx, 'vel_y': vy, 'px_raw': px_s}

common = sorted(set(pf.keys()) & set(mt.keys()))
print(f"PF frames={len(pf)} MT frames={len(mt)} common={len(common)}", file=sys.stderr)

# Calibration f in [0..200]
calib = [f for f in common if 0 <= f <= 200]
dys = [pf[f]['Y_px'] - mt[f]['Y_px'] for f in calib if pf[f]['Y_px'] is not None]
dxs = [pf[f]['X_px'] - mt[f]['X_px'] for f in calib if pf[f]['X_px'] is not None]
mean_dy = statistics.mean(dys) if dys else 0
mean_dx = statistics.mean(dxs) if dxs else 0
off_y = round(mean_dy)
off_x = round(mean_dx)

# Diffs
diffs = []
for f in common:
    if pf[f]['Y_px'] is None: continue
    dY = pf[f]['Y_px'] - (mt[f]['Y_px'] + off_y)
    dX = pf[f]['X_px'] - (mt[f]['X_px'] + off_x)
    diffs.append((f, dY, dX))

total = len(diffs)
nz = sum(1 for _,dy,_ in diffs if abs(dy) >= 1)

# Events
events = []
i = 0
while i < len(diffs):
    if abs(diffs[i][1]) >= 1:
        j = i
        while j < len(diffs) and abs(diffs[j][1]) >= 1:
            j += 1
        seg = diffs[i:j]
        if len(seg) >= 2:
            frs = [s[0] for s in seg]
            dys_in = [s[1] for s in seg]
            peak = max(dys_in, key=abs)
            recovery = 'recovered' if j < len(diffs) and abs(diffs[j][1]) < 1 else 'persistent'
            events.append({
                'start': frs[0], 'end': frs[-1], 'duration': len(seg),
                'peak': peak, 'mean_sign': 1 if sum(dys_in)>0 else -1,
                'recovery': recovery,
            })
        i = j
    else:
        i += 1

# Load PF debug log
log_by_frame = defaultdict(list)
fre = re.compile(r'f=(\d+)')
with open(PF_LOG, 'r', errors='replace') as fh:
    for line in fh:
        m = fre.search(line)
        if m:
            log_by_frame[int(m.group(1))].append(line.rstrip())

SLOPE_TAGS = ['[SLOPE/', 'slope_jump', 'apply_slope', 'SLOPE_END', 'SLOPE_BEGIN',
              'slope_on', 'slope_off', 'sT=', 'swOn=', 'jH=', '[EJECT-DIAG]']

def classify(ev):
    tags = []
    lines = []
    for fr in range(ev['start']-3, ev['start']+4):
        for ln in log_by_frame.get(fr, []):
            for t in SLOPE_TAGS:
                if t in ln:
                    if t not in tags: tags.append(t)
                    lines.append(ln)
                    break
    return tags, lines

for e in events:
    t, l = classify(e)
    e['tags'] = t; e['lines'] = l; e['slope'] = bool(t)

slope_events = [e for e in events if e['slope']]

# Ground / grav mismatches (PF only, since MT lacks)
ground_mis = []
grav_mis = []
# Skip since MT has no onGround/gravFlipped

biggest = max(events, key=lambda e: (e['duration'], abs(e['peak']))) if events else None

with open(OUT, 'w') as f:
    f.write("=== Slope-all divergence report (dorabaebasic6) ===\n")
    f.write(f"PF: {PF_CSV}\nMT: {MT_CSV}\nLOG: {PF_LOG}\n\n")
    f.write(f"MT header nes_y_offset = {nes_y_offset}\n")
    f.write(f"Calibration (frames 0..200, n={len(calib)}):\n")
    f.write(f"  mean(PF.Y_px - MT.py) = {mean_dy:.3f} -> off_y = {off_y}\n")
    f.write(f"  mean(PF.X_px - MT.px>>8) = {mean_dx:.3f} -> off_x = {off_x}\n\n")
    f.write(f"Total common frames compared: {total}\n")
    f.write(f"Frames with |dY|>=1: {nz}\n")
    f.write(f"Divergence events (>=2 frames): {len(events)}\n")
    f.write(f"  slope-related: {len(slope_events)}\n")
    f.write(f"  non-slope: {len(events)-len(slope_events)}\n")
    f.write(f"(MT trace lacks onGround/gravFlipped columns; ground/grav mismatch skipped)\n\n")
    f.write("=== All events ===\n")
    for idx, e in enumerate(events, 1):
        cls = "SLOPE" if e['slope'] else "----"
        f.write(f"{idx:3d}. f={e['start']}..{e['end']} dur={e['duration']:4d} peak_dY={e['peak']:+d} sign={'+' if e['mean_sign']>0 else '-'} {e['recovery']:11s} [{cls}] tags={e['tags']}\n")
    f.write("\n=== Slope-related event details (up to 6 lines each) ===\n")
    for idx, e in enumerate(events, 1):
        if not e['slope']: continue
        f.write(f"\n#{idx} f={e['start']}..{e['end']} dur={e['duration']} peak_dY={e['peak']:+d} {e['recovery']} tags={e['tags']}\n")
        rel = list(dict.fromkeys(e['lines']))
        for fr in range(e['start']-1, e['start']+3):
            for ln in log_by_frame.get(fr, []):
                if 'VelY' in ln and ln not in rel:
                    rel.append(ln)
        for ln in rel[:6]:
            f.write(f"   {ln}\n")
    if biggest:
        f.write(f"\n=== Largest/most-persistent event ===\n")
        f.write(f"  f={biggest['start']}..{biggest['end']} dur={biggest['duration']} peak_dY={biggest['peak']:+d} {biggest['recovery']} slope={biggest['slope']} tags={biggest['tags']}\n")

# Stdout concise
print(f"Calibration: off_y={off_y} (mean {mean_dy:.3f}), off_x={off_x} (mean {mean_dx:.3f})")
print(f"Common frames={total}, |dY|>=1 frames={nz}")
print(f"Events total={len(events)}, slope-related={len(slope_events)}")
print("First 10 slope-related events:")
for e in slope_events[:10]:
    key = e['tags'][0] if e['tags'] else ''
    print(f"  f={e['start']}..{e['end']} dur={e['duration']} peak_dY={e['peak']:+d} tag={key}")
if biggest:
    print(f"Largest/most-persistent: f={biggest['start']}..{biggest['end']} dur={biggest['duration']} peak_dY={biggest['peak']:+d} {biggest['recovery']} slope={biggest['slope']} tags={biggest['tags']}")
print(f"Report: {OUT}")

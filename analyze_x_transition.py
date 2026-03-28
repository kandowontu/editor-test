import re

PF_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260328_135356.txt"
SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260328_140115.txt"

# Extract PF X keyed by frame
pf_x = {}
with open(PF_LOG, 'r', errors='replace') as f:
    for line in f:
        m = re.search(r'\[PF f=(\d+)\]', line)
        if not m:
            continue
        frame = int(m.group(1))
        for pat in [r'\[STEP_START\]\s*X=\s*(\d+)', r'\[MINISHIP_Y\]\s*X=\s*(\d+)', r'\[WAVE_PHYS\]\s*START\s*X=\s*(\d+)']:
            xm = re.search(pat, line)
            if xm:
                if frame not in pf_x or 'STEP_START' in pat:
                    pf_x[frame] = int(xm.group(1))
                break

# Extract SIM X keyed by step
sim_x = {}
step_idx = -1
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx += 1
            xm = re.search(r'playerX_fixed=0x[0-9A-Fa-f]+\s+\((\d+)px\)', line)
            if xm:
                sim_x[step_idx] = int(xm.group(1))

# Also extract PF fixed-point X (hex)
pf_x_hex = {}
with open(PF_LOG, 'r', errors='replace') as f:
    for line in f:
        m = re.search(r'\[PF f=(\d+)\]', line)
        if not m:
            continue
        frame = int(m.group(1))
        xm = re.search(r'\[STEP_START\]\s*X=\s*\d+.*playerX_fixed=(0x[0-9A-Fa-f]+)', line)
        if xm:
            pf_x_hex[frame] = xm.group(1)

# SIM fixed-point X
sim_x_hex = {}
step_idx2 = -1
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx2 += 1
            xm = re.search(r'playerX_fixed=(0x[0-9A-Fa-f]+)', line)
            if xm:
                sim_x_hex[step_idx2] = xm.group(1)

# Use offset 5 and find where dX transitions from 0 to non-zero
print("=== X OFFSET TRANSITION (offset=5, steps 895-1080) ===")
prev_dx = None
for step in range(895, 1081):
    pf_frame = step + 5
    px = pf_x.get(pf_frame)
    sx = sim_x.get(step)
    if px is not None and sx is not None:
        dx = px - sx
        if dx != prev_dx:
            ph = pf_x_hex.get(pf_frame, '?')
            sh = sim_x_hex.get(step, '?')
            print(f"  step={step:5d} PF f={pf_frame}: PF_X={px:5d} ({ph}) SIM_X={sx:5d} ({sh}) dX={dx:+d}  <-- CHANGED from {prev_dx}")
            prev_dx = dx
    elif px is None and prev_dx is not None:
        # Gap in PF data
        pass

# Also check: where exactly does the PF X data end in the wave section?
print("\n=== PF X data availability (frames 900-1090) ===")
for f in range(900, 1091):
    if f in pf_x:
        src = "STEP" if f in pf_x_hex else "OTHER"
        print(f"  f={f}: X={pf_x[f]} ({src})")
    else:
        # Check if this is a gap
        pass

# Count consecutive gaps
gaps = []
in_gap = False
gap_start = None
for f in range(900, 1091):
    if f not in pf_x:
        if not in_gap:
            gap_start = f
            in_gap = True
    else:
        if in_gap:
            gaps.append((gap_start, f-1, f - gap_start))
            in_gap = False

if in_gap:
    gaps.append((gap_start, 1090, 1091 - gap_start))

print(f"\n=== GAPS IN PF DATA (frames 900-1090): {len(gaps)} gaps ===")
for start, end, length in gaps:
    print(f"  gap: f={start}-{end} ({length} frames)")

# Check SIM fixed-point X values at key points to look for fractional differences
print("\n=== FIXED-POINT X VALUES AT KEY STEPS ===")
for step in [1050, 1055, 1056, 1057, 1060, 1065, 1070, 1075, 1080]:
    pf_frame = step + 5
    ph = pf_x_hex.get(pf_frame, 'N/A')
    sh = sim_x_hex.get(step, 'N/A')
    px = pf_x.get(pf_frame, -1)
    sx = sim_x.get(step, -1)
    print(f"  step={step:5d} (PF f={pf_frame}): PF_Xfixed={ph:>10s} ({px}px)  SIM_Xfixed={sh:>10s} ({sx}px)")

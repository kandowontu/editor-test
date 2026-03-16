import re, os

temp = os.environ.get('TEMP', '')
sim_log = os.path.join(temp, 'famidash_sim_debug_20260315_235305.txt')
pf_log = os.path.join(temp, 'famidash_pf_debug_20260315_234518.txt')

# Extract SIM: sequential frames from STEP_START lines
sim_frames = []  # list of (x, y)
with open(sim_log, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            m = re.search(r'playerX_fixed=0x[0-9A-Fa-f]+\s*\((\d+)px\).*?playerY_fixed=0x[0-9A-Fa-f]+\s*\((\d+)px\)', line)
            if m:
                sim_frames.append((int(m.group(1)), int(m.group(2))))
print(f"SIM: {len(sim_frames)} frames")

# Extract PF: frame number and Y from [PF f=NNN] X=... Y=...
pf_data = {}  # frame -> (x, y)
with open(pf_log, 'r', errors='replace') as f:
    for line in f:
        m = re.match(r'\[PF f=(\d+)\]\s+X=(\d+)\s+Y=(\d+)', line)
        if m:
            pf_data[int(m.group(1))] = (int(m.group(2)), int(m.group(3)))
print(f"PF: {len(pf_data)} frames")

# PF frames are indexed. Let's match them up.
# The PF f=0 should correspond to SIM frame 0, etc.
pf_frames_sorted = sorted(pf_data.keys())
if pf_frames_sorted:
    print(f"PF frame range: {pf_frames_sorted[0]} to {pf_frames_sorted[-1]}")

# Find first Y divergence
diverge = None
for pf_f in pf_frames_sorted:
    if pf_f >= len(sim_frames):
        break
    sx, sy = sim_frames[pf_f]
    px, py = pf_data[pf_f]
    if sy != py:
        diverge = pf_f
        break

if diverge is None:
    print("No Y divergence found!")
else:
    print(f"\nFirst Y divergence at frame {diverge}")
    start = max(0, diverge - 10)
    end = min(diverge + 25, pf_frames_sorted[-1])
    for f in range(start, end + 1):
        if f in pf_data and f < len(sim_frames):
            sx, sy = sim_frames[f]
            px, py = pf_data[f]
            mark = " <-- DIVERGE" if sy != py else ""
            print(f"  f={f}: SIM(X={sx},Y={sy}) PF(X={px},Y={py}) dY={sy-py}{mark}")
        elif f in pf_data:
            px, py = pf_data[f]
            print(f"  f={f}: SIM=? PF(X={px},Y={py})")

# Now show detailed SIM log lines around divergence
if diverge:
    print(f"\n=== SIM log around frame {diverge} ===")
    step_count = 0
    capture = False
    with open(sim_log, 'r', errors='replace') as f:
        for line in f:
            if '[STEP_START]' in line:
                step_count += 1  # 1-indexed now, so frame = step_count - 1
                if step_count - 1 >= diverge - 3:
                    capture = True
                if step_count - 1 > diverge + 5:
                    break
            if capture:
                print(f"  [SIM step {step_count-1}] {line.rstrip()}")
    
    print(f"\n=== PF log around frame {diverge} ===")
    with open(pf_log, 'r', errors='replace') as f:
        for line in f:
            m = re.match(r'\[PF f=(\d+)\]', line)
            if m:
                curr_f = int(m.group(1))
                if curr_f >= diverge - 3 and curr_f <= diverge + 5:
                    print(f"  {line.rstrip()}")

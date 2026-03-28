import re

SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260328_140115.txt"
PF_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260328_135356.txt"

# 1) Track ALL injections and find offset transitions
step_idx = -1
injections = []  # (step_idx, pf_frame, offset)
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx += 1
        m = re.search(r'\[PF\] Frame (\d+)', line)
        if m:
            pf_f = int(m.group(1))
            offset = pf_f - step_idx
            injections.append((step_idx, pf_f, offset))

print("=== ALL OFFSET TRANSITIONS ===")
prev_offset = None
for step, pf_f, offset in injections:
    if offset != prev_offset:
        print(f"  OFFSET CHANGE: {prev_offset} -> {offset} at SIM step {step} (PF frame {pf_f})")
        prev_offset = offset

print(f"\nTotal injections: {len(injections)}")
print(f"Offset range: {injections[0][2]} to {injections[-1][2]}")

# 2) For each offset transition, check context (what PF frame injection came before?)
print("\n=== CONTEXT AROUND OFFSET CHANGES ===")
prev_offset = None
for i, (step, pf_f, offset) in enumerate(injections):
    if offset != prev_offset:
        # Show 3 before and 3 after
        start = max(0, i-3)
        end = min(len(injections), i+4)
        print(f"\n  --- Offset {prev_offset} -> {offset} ---")
        for j in range(start, end):
            s, pf, o = injections[j]
            marker = " <<<" if j == i else ""
            print(f"    inj[{j:3d}] SIM step {s:5d} <- PF frame {pf:5d} (offset={o}){marker}")
        prev_offset = offset

# 3) Check SIM for any TELEPORT or PORTAL events near offset transitions
print("\n=== SIM EVENTS NEAR FIRST OFFSET CHANGE (step 75-143) ===")
step_idx = -1
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx += 1
        if 75 <= step_idx <= 145:
            if any(tag in line for tag in ['PORTAL', 'TELEPORT', 'MODE', 'SPEED', 'GRAVITY', 'GRAV_MOD']):
                print(f"  step {step_idx}: {line.rstrip()[-120:]}")

# 4) Check PF for what happens at frames 75-144  
print("\n=== PF EVENTS AT FRAMES 75-145 ===")
with open(PF_LOG, 'r', errors='replace') as f:
    for line in f:
        m = re.search(r'\[PF f=(\d+)\]', line)
        if m:
            frame = int(m.group(1))
            if 75 <= frame <= 145:
                if any(tag in line for tag in ['PORTAL', 'TELEPORT', 'MODE', 'GRAVITY', 'GRAV_MOD', 'SPRITE_HIT']):
                    print(f"  {line.rstrip()[:150]}")

# 5) Corrected X comparison with the actual offset at each point
print("\n=== CORRECTED X COMPARISON (using per-injection offset) ===")
# Build a map: for each SIM step, what's the current offset?
offset_at_step = {}
current_offset = 0
inj_dict = {step: offset for step, _, offset in injections}

for step in range(step_idx + 1):
    if step in inj_dict:
        current_offset = inj_dict[step]
    offset_at_step[step] = current_offset

# Re-read SIM X values
sim_x = {}
step_idx2 = -1
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx2 += 1
            m = re.search(r'X=\s*(\d+)', line)
            if m:
                sim_x[step_idx2] = int(m.group(1))

# Re-read PF X values
pf_x = {}
pf_step_re = re.compile(r'\[PF f=(\d+)\].*\[STEP_START\]\s*X=\s*(\d+)')
pf_miniship_re = re.compile(r'\[PF f=(\d+)\].*\[MINISHIP_Y\]\s*X=\s*(\d+)')
pf_wave_re = re.compile(r'\[PF f=(\d+)\].*\[WAVE_PHYS\]\s*START\s*X=\s*(\d+)')
with open(PF_LOG, 'r', errors='replace') as f:
    for line in f:
        for pat in [pf_step_re, pf_miniship_re, pf_wave_re]:
            m = pat.search(line)
            if m:
                frame = int(m.group(1))
                x = int(m.group(2))
                if frame not in pf_x or pat == pf_step_re:
                    pf_x[frame] = x
                break

# Compare at key checkpoints using corrected offset
print("  Comparing PF_X[step+offset] vs SIM_X[step]:")
for step in [0, 100, 200, 500, 800, 900, 1000, 1050, 1060, 1070, 1075, 1080, 1085, 1090, 1100, 1150, 1200, 1245]:
    offset = offset_at_step.get(step, 0)
    pf_frame = step + offset
    px = pf_x.get(pf_frame)
    sx = sim_x.get(step)
    if px is not None and sx is not None:
        dx = px - sx
        print(f"    step {step:5d} +off={offset} -> PF f={pf_frame:5d}: PF_X={px:5d} SIM_X={sx:5d} dX={dx:+d}")
    elif sx is not None:
        print(f"    step {step:5d} +off={offset} -> PF f={pf_frame:5d}: PF_X=  N/A SIM_X={sx:5d}")

# 6) Find ALL frames where both PF and SIM have X data, using offset correction
print("\n=== X OFFSET WITH CORRECTION (first 30 matches) ===")
count = 0
for step in sorted(sim_x.keys()):
    offset = offset_at_step.get(step, 0)
    pf_frame = step + offset
    px = pf_x.get(pf_frame)
    sx = sim_x.get(step)
    if px is not None and sx is not None:
        dx = px - sx
        print(f"    step {step:5d} (off={offset}) PF f={pf_frame:5d}: PF_X={px:5d} SIM_X={sx:5d} dX={dx:+d}")
        count += 1
        if count >= 50:
            break

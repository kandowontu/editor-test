import re

PF_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260328_135356.txt"
SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260328_140115.txt"

# Key finding: offset transitions
# offset 0->1 between SIM step 75 and 143  
# offset 1->2 between SIM step 259 and 271
# offset 2->4 between SIM step 387 and 456
# offset 4->5 between SIM step 668 and 706

# 1) Extract PF X from ALL sources: STEP_START, MINISHIP_Y, WAVE_PHYS, CUBE_JUMP etc
pf_x = {}
with open(PF_LOG, 'r', errors='replace') as f:
    for line in f:
        m = re.search(r'\[PF f=(\d+)\]', line)
        if not m:
            continue
        frame = int(m.group(1))
        # Try multiple X sources
        x_m = re.search(r'\[STEP_START\]\s*X=\s*(\d+)', line)
        if x_m:
            pf_x[frame] = int(x_m.group(1))
            continue
        x_m = re.search(r'\[MINISHIP_Y\]\s*X=\s*(\d+)', line)
        if x_m:
            if frame not in pf_x:
                pf_x[frame] = int(x_m.group(1))
            continue
        x_m = re.search(r'\[WAVE_PHYS\]\s*START\s*X=\s*(\d+)', line)
        if x_m:
            if frame not in pf_x:
                pf_x[frame] = int(x_m.group(1))
            continue

print(f"PF frames with X: {len(pf_x)}")
# Show ranges
if pf_x:
    frames = sorted(pf_x.keys())
    print(f"  Frame range: {frames[0]} .. {frames[-1]}")
    # Show first few and around mode transitions
    for f in frames[:5]:
        print(f"  f={f}: X={pf_x[f]}")
    print("  ...")
    # Show around the ship section
    ship_frames = [f for f in frames if 1075 <= f <= 1095]
    for f in ship_frames:
        print(f"  f={f}: X={pf_x[f]}")

# 2) Extract SIM X
sim_x = {}
step_idx = -1  
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx += 1
            x_m = re.search(r'playerX_fixed=0x[0-9A-Fa-f]+\s+\((\d+)px\)', line)
            if x_m:
                sim_x[step_idx] = int(x_m.group(1))

print(f"\nSIM steps with X: {len(sim_x)}")

# 3) The offset at each point (from previous analysis)
# offset = pf_frame - sim_step
# offset 0: steps 0-75
# offset 1: steps 143-259
# offset 2: steps 271-387
# offset 4: steps 456-668
# offset 5: steps 706-1245
def get_offset(step):
    if step < 143:
        return 0
    elif step < 271:
        return 1
    elif step < 456:
        return 2
    elif step < 706:
        return 4
    else:
        return 5

# 4) Direct comparison: for each SIM step, find corresponding PF frame and compare X
print("\n=== CORRECTED COMPARISON (SIM step -> PF frame = step + offset) ===")
matches = 0
for step in sorted(sim_x.keys()):
    offset = get_offset(step)
    pf_frame = step + offset
    if pf_frame in pf_x:
        dx = pf_x[pf_frame] - sim_x[step]
        print(f"  step={step:5d} off={offset} -> PF f={pf_frame:5d}: PF_X={pf_x[pf_frame]:5d} SIM_X={sim_x[step]:5d} dX={dx:+d}")
        matches += 1
        if matches >= 60:
            break

print(f"\nTotal matches found: {matches}")

# 5) Also compare WITHOUT offset to show the raw difference
print("\n=== RAW COMPARISON (same index, NO offset correction) ===")
matches = 0
for step in sorted(sim_x.keys()):
    if step in pf_x:
        dx = pf_x[step] - sim_x[step]
        print(f"  idx={step:5d}: PF_X={pf_x[step]:5d} SIM_X={sim_x[step]:5d} dX={dx:+d}")
        matches += 1
        if matches >= 60:
            break

# 6) Check: what modes does PF have X for? 
print("\n=== PF X data coverage ===")
# Check which frames have X data in ranges
ranges = [(0,100), (100,200), (200,300), (300,400), (400,500), (500,600), 
          (600,700), (700,800), (800,900), (900,1000), (1000,1100), (1100,1200), (1200,1300)]
for lo, hi in ranges:
    count = sum(1 for f in pf_x if lo <= f < hi)
    if count > 0:
        sample_f = min(f for f in pf_x if lo <= f < hi)
        print(f"  frames {lo:5d}-{hi:5d}: {count:4d} entries (first: f={sample_f} X={pf_x[sample_f]})")
    else:
        print(f"  frames {lo:5d}-{hi:5d}: 0 entries")

# 7) Find the exact PF frames where the offset transitions happen
# The transitions happen between PF injections, so look at what PF does differently
print("\n=== PF LOG AROUND FIRST OFFSET TRANSITION (f=75-144) ===")
print("Looking for any STEP_START in PF between f=75-150...")
with open(PF_LOG, 'r', errors='replace') as f:
    for line in f:
        m = re.search(r'\[PF f=(\d+)\]', line)
        if m:
            frame = int(m.group(1))
            if 75 <= frame <= 150 and '[STEP_START]' in line:
                print(f"  {line.rstrip()[:150]}")

import re

PF_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260328_135356.txt"
SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260328_140115.txt"

# 1) Find SIM step index for each speed portal hit
print("=== SIM SPEED PORTAL TIMING ===")
step_idx = -1
speed_events = []
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx += 1
        m = re.search(r'\[SPEED_PRE_P2\]\s*sid=(0x[\dA-Fa-f]+)\s*VelX\s*->\s*(0x[\dA-Fa-f]+)', line)
        if m:
            speed_events.append((step_idx, m.group(1), m.group(2)))

# Group by sid
from collections import defaultdict
by_sid = defaultdict(list)
for step, sid, vel in speed_events:
    by_sid[sid].append((step, vel))

for sid in sorted(by_sid.keys()):
    entries = by_sid[sid]
    print(f"  sid={sid}: {len(entries)} hits, step range [{entries[0][0]}..{entries[-1][0]}], VelX={entries[0][1]}")
    # Show first and last few
    for s, v in entries[:3]:
        print(f"    step {s}: VelX -> {v}")
    if len(entries) > 6:
        print(f"    ... ({len(entries)-6} more) ...")
    for s, v in entries[-3:]:
        print(f"    step {s}: VelX -> {v}")

# 2) Extract ALL PF X values from any log line that has X= after [PF f=NNN]
print("\n=== PF X tracking (all sources) ===")
pf_x_by_frame = {}
# Try STEP_START, MINISHIP_Y, WAVE_PHYS, WAVE_EJECT, CUBE_JUMP, etc
pf_step_re = re.compile(r'\[PF f=(\d+)\].*\[STEP_START\]\s*X=\s*(\d+)')
pf_miniship_re = re.compile(r'\[PF f=(\d+)\].*\[MINISHIP_Y\]\s*X=\s*(\d+)')
pf_wave_re = re.compile(r'\[PF f=(\d+)\].*\[WAVE_PHYS\]\s*START\s*X=\s*(\d+)')
pf_physics_re = re.compile(r'\[PF f=(\d+)\].*\[PHYSICS\]')
pf_any_x_re = re.compile(r'\[PF f=(\d+)\].*X[=:]?\s*(\d{2,})')  # broader match

with open(PF_LOG, 'r', errors='replace') as f:
    for line in f:
        for pat in [pf_step_re, pf_miniship_re, pf_wave_re]:
            m = pat.search(line)
            if m:
                frame = int(m.group(1))
                x = int(m.group(2))
                if frame not in pf_x_by_frame or pat == pf_step_re:  # prefer STEP_START
                    pf_x_by_frame[frame] = x
                break

# 3) Extract ALL SIM X values 
sim_x_by_step = {}
step_idx = -1
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx += 1
            m = re.search(r'X=\s*(\d+)', line)
            if m:
                sim_x_by_step[step_idx] = int(m.group(1))

# 4) Find the frame offset between PF and SIM
# We know SIM step 1070 -> PF frame 1074 (offset ~4)
# Let's check offset at various points
print("\n=== CHECKING FRAME OFFSET ===")
# Read PF frame injections from SIM log
pf_injections = []  # (step_idx, pf_frame)
step_idx = -1
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx += 1
        m = re.search(r'\[PF\] Frame (\d+)', line)
        if m:
            pf_injections.append((step_idx, int(m.group(1))))

# Show offset pattern
print(f"Total PF injections in SIM: {len(pf_injections)}")
offsets = [(pf_f - step, step, pf_f) for step, pf_f in pf_injections]
# Show first 10 and the range
for off, step, pf_f in offsets[:10]:
    print(f"  SIM step {step} <- PF frame {pf_f} (offset = {off})")
print("  ...")
for off, step, pf_f in offsets[-5:]:
    print(f"  SIM step {step} <- PF frame {pf_f} (offset = {off})")

# Check if offset is constant
unique_offsets = set(o for o, _, _ in offsets)
print(f"\nUnique offsets: {sorted(unique_offsets)}")

# 5) Corrected X comparison using offset
# If SIM step S maps to PF frame S+offset, compare PF_X[S+offset] with SIM_X[S]
print("\n=== X COMPARISON WITH OFFSET CORRECTION ===")
# Use majority offset
from collections import Counter
offset_counts = Counter(o for o, _, _ in offsets)
majority_offset = offset_counts.most_common(1)[0][0]
print(f"Majority offset: {majority_offset}")

# Compare using corrected mapping
print("\nCorrected comparisons (SIM_step -> PF_frame = step + offset):")
checkpoints = [0, 50, 100, 150, 200, 300, 400, 500, 600, 700, 800, 841, 850, 900, 950, 1000, 1050, 1070, 1075, 1080, 1085, 1090]
for step in checkpoints:
    pf_frame = step + majority_offset
    pf_x = pf_x_by_frame.get(pf_frame)
    sim_x = sim_x_by_step.get(step)
    if pf_x is not None and sim_x is not None:
        dx = pf_x - sim_x
        print(f"  SIM step {step:5d} (PF f={pf_frame:5d}): PF_X={pf_x:5d}  SIM_X={sim_x:5d}  dX={dx:+d}")
    else:
        pf_str = str(pf_x) if pf_x is not None else "N/A"
        sim_str = str(sim_x) if sim_x is not None else "N/A"
        print(f"  SIM step {step:5d} (PF f={pf_frame:5d}): PF_X={pf_str:>5s}  SIM_X={sim_str:>5s}  dX=N/A")

# 6) Find where X offset first appears and grows
print("\n=== X OFFSET GROWTH (every frame where both have data) ===")
prev_dx = None
transitions = []
for step in sorted(sim_x_by_step.keys()):
    pf_frame = step + majority_offset
    pf_x = pf_x_by_frame.get(pf_frame)
    sim_x = sim_x_by_step.get(step)
    if pf_x is not None and sim_x is not None:
        dx = pf_x - sim_x
        if prev_dx is None or dx != prev_dx:
            transitions.append((step, pf_frame, pf_x, sim_x, dx))
            prev_dx = dx

print(f"  X offset transitions ({len(transitions)} changes):")
for step, pf_f, px, sx, dx in transitions:
    print(f"    SIM step {step:5d} (PF f={pf_f:5d}): PF_X={px:5d}  SIM_X={sx:5d}  dX={dx:+d}")

# 7) Also check: does the SIM hit sid=0x16 at all?
print("\n=== SIM sid=0x16 speed events ===")
sid16 = by_sid.get('0x16', [])
if sid16:
    print(f"  sid=0x16 hits: {len(sid16)}")
    for s, v in sid16[:5]:
        print(f"    step {s}: VelX -> {v}")
else:
    print("  NO sid=0x16 events found in SIM!")

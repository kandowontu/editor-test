import re

PF_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260328_135356.txt"
SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260328_140115.txt"

# Extract PF X from all sources, keyed by PF frame number
pf_x = {}
with open(PF_LOG, 'r', errors='replace') as f:
    for line in f:
        m = re.search(r'\[PF f=(\d+)\]', line)
        if not m:
            continue
        frame = int(m.group(1))
        for pat in [
            r'\[STEP_START\]\s*X=\s*(\d+)',
            r'\[MINISHIP_Y\]\s*X=\s*(\d+)',
            r'\[WAVE_PHYS\]\s*START\s*X=\s*(\d+)',
        ]:
            xm = re.search(pat, line)
            if xm:
                if frame not in pf_x or 'STEP_START' in pat:
                    pf_x[frame] = int(xm.group(1))
                break

# Extract SIM X keyed by step index
sim_x = {}
step_idx = -1
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx += 1
            xm = re.search(r'playerX_fixed=0x[0-9A-Fa-f]+\s+\((\d+)px\)', line)
            if xm:
                sim_x[step_idx] = int(xm.group(1))

# Build injection-based offset map
injections = []
step_idx2 = -1
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx2 += 1
        m = re.search(r'\[PF\] Frame (\d+)', line)
        if m:
            pf_f = int(m.group(1))
            injections.append((step_idx2, pf_f, pf_f - step_idx2))

# Build interpolated offset map from injection points
offset_map = {}
for step, pf_f, offset in injections:
    offset_map[step] = offset

# For steps without injection, use last known offset
sorted_inj_steps = sorted(offset_map.keys())
def get_offset_at(step):
    # Find last injection at or before this step
    best = 0
    for s in sorted_inj_steps:
        if s <= step:
            best = offset_map[s]
        else:
            break
    return best

# Compare for the SHIP SECTION specifically (SIM steps 1070-1245)
print("=== CORRECTED X COMPARISON: SHIP SECTION (steps 1070-1200) ===")
for step in range(1070, min(1201, len(sim_x))):
    offset = get_offset_at(step)
    pf_frame = step + offset
    px = pf_x.get(pf_frame)
    sx = sim_x.get(step)
    if px is not None and sx is not None:
        dx = px - sx
        print(f"  step={step:5d} off={offset} -> PF f={pf_frame:5d}: PF_X={px:5d} SIM_X={sx:5d} dX={dx:+d}")
    elif sx is not None:
        print(f"  step={step:5d} off={offset} -> PF f={pf_frame:5d}: PF_X=  N/A SIM_X={sx:5d}")

# Also compare the WAVE section (steps 850-1070)
print("\n=== CORRECTED X COMPARISON: WAVE SECTION (steps 850-900) ===")
for step in range(850, 901):
    offset = get_offset_at(step)
    pf_frame = step + offset
    px = pf_x.get(pf_frame)
    sx = sim_x.get(step)
    if px is not None and sx is not None:
        dx = px - sx
        print(f"  step={step:5d} off={offset} -> PF f={pf_frame:5d}: PF_X={px:5d} SIM_X={sx:5d} dX={dx:+d}")

# And the Y comparison too
print("\n=== CORRECTED Y COMPARISON: SHIP SECTION (with offset) ===")
# Extract PF Y from MINISHIP_Y
pf_y = {}
pf_vy = {}
with open(PF_LOG, 'r', errors='replace') as f:
    for line in f:
        m = re.search(r'\[PF f=(\d+)\].*\[MINISHIP_Y\]\s*X=\d+\s+Y=(\d+)\s+velY=(0x[0-9A-Fa-f]+)', line)
        if m:
            frame = int(m.group(1))
            pf_y[frame] = int(m.group(2))
            pf_vy[frame] = m.group(3)

# Extract SIM Y from STEP_START
sim_y = {}
sim_vy = {}
step_idx3 = -1
with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx3 += 1
            ym = re.search(r'playerY_fixed=0x[0-9A-Fa-f]+\s+\((\d+)px\)', line)
            vm = re.search(r'playerVelY_fixed=(0x[0-9A-Fa-f]+)', line)
            if ym:
                sim_y[step_idx3] = int(ym.group(1))
            if vm:
                sim_vy[step_idx3] = vm.group(1)

# Compare Y during ship section
for step in range(1070, min(1201, len(sim_y))):
    offset = get_offset_at(step)
    pf_frame = step + offset
    py = pf_y.get(pf_frame)
    sy = sim_y.get(step)
    if py is not None and sy is not None:
        dy = py - sy
        pvy = pf_vy.get(pf_frame, '?')
        svy = sim_vy.get(step, '?')
        print(f"  step={step:5d} -> PF f={pf_frame:5d}: PF_Y={py:3d} SIM_Y={sy:3d} dY={dy:+d}  PF_velY={pvy} SIM_velY={svy}")

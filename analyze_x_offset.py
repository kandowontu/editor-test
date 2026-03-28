# -*- coding: utf-8 -*-
"""Trace X offset between PF and SIM from the start."""
import re

PF_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260328_135356.txt"
SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260328_140115.txt"

# Parse SIM STEP_START: step_index -> (x_px, y_px, velY)
sim_steps = {}
pf_frame_at_step = {}  # step_index -> closest PF frame number
step_count = 0
last_pf_frame = 0
with open(SIM_LOG, 'r') as f:
    for line in f:
        fm = re.search(r'\[PF\] Frame (\d+)', line)
        if fm:
            last_pf_frame = int(fm.group(1))
        
        m = re.search(r'\[STEP_START\]\s+playerX_fixed=(0x[0-9A-Fa-f]+)\s+\((\d+)px\),\s*playerY_fixed=(0x[0-9A-Fa-f]+)\s+\((\d+)px\),\s*playerVelY_fixed=(0x[0-9A-Fa-f]+)', line)
        if m:
            sim_steps[step_count] = {
                'x_px': int(m.group(2)),
                'y_px': int(m.group(4)),
                'velY': m.group(5),
                'pf_frame_before': last_pf_frame
            }
            step_count += 1

print(f"Total SIM steps: {step_count}")

# Show the PF frame mapping around the transition
print("\n=== SIM step vs PF frame mapping (steps 1070-1090) ===")
for i in range(1070, min(1090, step_count)):
    s = sim_steps[i]
    print(f"  step={i:5d}  X={s['x_px']:5d}  Y={s['y_px']:3d}  velY={s['velY']:>10s}  last_pf_frame={s['pf_frame_before']}")

# Now parse PF log - get X values at specific frame numbers for key checkpoints
pf_x_at_frame = {}
with open(PF_LOG, 'r') as f:
    for line in f:
        # Look for STEP_START, MINISHIP_Y, WAVE_PHYS START, or any position report
        fm = re.search(r'f=(\d+)', line)
        if not fm:
            continue
        frame = int(fm.group(1))
        
        # Get X from MINISHIP_Y
        m = re.search(r'\[MINISHIP_Y\]\s+X=(\d+)', line)
        if m:
            if frame not in pf_x_at_frame:
                pf_x_at_frame[frame] = int(m.group(1))
        
        # Get X from WAVE_PHYS START 
        m = re.search(r'\[WAVE_PHYS\]\s+START\s+X=(\d+)', line)
        if m:
            if frame not in pf_x_at_frame:
                pf_x_at_frame[frame] = int(m.group(1))
        
        # Get X from general STEP_START or position log
        m = re.search(r'\[PF_STEP\]\s+.*X=(\d+)', line)
        if m:
            if frame not in pf_x_at_frame:
                pf_x_at_frame[frame] = int(m.group(1))

print(f"\nPF frames with X data: {len(pf_x_at_frame)}")

# Compare at checkpoints
print("\n=== X COMPARISON AT CHECKPOINTS ===")
checkpoints = list(range(0, min(step_count, 1100), 100)) + list(range(1070, min(step_count, 1090)))
for cp in checkpoints:
    sim_x = sim_steps[cp]['x_px'] if cp in sim_steps else None
    pf_x = pf_x_at_frame.get(cp)
    if sim_x is not None:
        dx = (pf_x - sim_x) if pf_x is not None else None
        pf_str = str(pf_x) if pf_x is not None else "N/A"
        dx_str = str(dx) if dx is not None else "N/A"
        print(f"  Frame {cp:5d}: PF_X={pf_str:>6s}  SIM_X={sim_x:5d}  dX={dx_str}")

# Check for speed changes in PF log
print("\n=== PF SPEED CHANGES ===")
with open(PF_LOG, 'r') as f:
    for line in f:
        if 'SPEED' in line.upper() or 'VELX' in line.upper() or 'VEL_X' in line.upper():
            fm = re.search(r'f=(\d+)', line)
            if fm:
                frame = int(fm.group(1))
                if frame <= 1090:
                    print(f"  {line.strip()[:150]}")

# Check for speed changes in SIM log  
print("\n=== SIM SPEED CHANGES (first 20) ===")
count = 0
with open(SIM_LOG, 'r') as f:
    for line in f:
        if 'SPEED_PRE_P2' in line or 'VelX' in line.upper():
            if count < 20:
                print(f"  {line.strip()[:150]}")
                count += 1

# Check PF STEP_START or position info
print("\n=== PF START position samples ===")
with open(PF_LOG, 'r') as f:
    for line in f:
        fm = re.search(r'f=(\d+)', line)
        if not fm:
            continue
        frame = int(fm.group(1))
        if frame in [0, 1, 2, 100, 500, 1000, 1070, 1078, 1080]:
            if 'PHYSICS' in line or 'STEP' in line or 'POS' in line or 'GRAV_START' in line:
                print(f"  f={frame}: {line.strip()[:180]}")

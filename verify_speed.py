"""Verify the speed-portal overlap theory by tracing SIM fixed-point X step by step."""
import re

SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260328_140115.txt"

# Extract ALL SIM STEP_START fixed-point X values + speed events per step
sim_xfixed = {}
speed_events_by_step = {}
step_idx = -1

with open(SIM_LOG, 'r', errors='replace') as f:
    for line in f:
        if '[STEP_START]' in line:
            step_idx += 1
            xm = re.search(r'playerX_fixed=(0x[0-9A-Fa-f]+)', line)
            if xm:
                sim_xfixed[step_idx] = int(xm.group(1), 16)
        m = re.search(r'\[SPEED_PRE_P2\]\s*sid=(0x[\dA-Fa-f]+)\s*VelX\s*->\s*(0x[\dA-Fa-f]+)', line)
        if m:
            if step_idx not in speed_events_by_step:
                speed_events_by_step[step_idx] = []
            speed_events_by_step[step_idx].append((m.group(1), m.group(2)))

# Show step-by-step X advance and speed events from step 1050 to 1085
print("=== SIM STEP-BY-STEP X ADVANCE (steps 1050-1085) ===")
for step in range(1050, 1086):
    xf = sim_xfixed.get(step)
    prev_xf = sim_xfixed.get(step - 1)
    se = speed_events_by_step.get(step, [])

    dx_str = ""
    speed_used = ""
    if xf is not None and prev_xf is not None:
        dx = xf - prev_xf
        dx_str = f" advance=0x{dx:04X}({dx})"
        if dx == 0x0429:
            speed_used = " << SPEED_3X"
        elif dx == 0x0371:
            speed_used = " speed_2x"
        else:
            speed_used = f" ?? (not 0x0429 or 0x0371)"

    se_str = ""
    if se:
        se_str = " speed_portals:[" + ", ".join(f"sid={s} -> {v}" for s, v in se) + "]"

    px = (xf >> 8) if xf else 0
    print(f"  step {step:5d}: X_fixed=0x{xf:05X} ({px}px){dx_str}{speed_used}{se_str}")

# Count total excess X vs expected (all 0x0371)
print("\n=== EXCESS X ACCUMULATION ===")
expected_xf = sim_xfixed[1058]  # start from when 0x0371 should be in effect
actual_sum = 0
expected_sum = 0
for step in range(1058, 1081):
    dx = sim_xfixed[step + 1] - sim_xfixed[step]
    actual_sum += dx
    expected_sum += 0x0371
    excess = actual_sum - expected_sum
    if dx != 0x0371:
        print(f"  step {step}->{step+1}: advance={dx} (0x{dx:04X}), cumulative excess = {excess} ({excess/256:.2f}px)")

total_excess = actual_sum - expected_sum
print(f"\n  Total excess from step 1058 to 1081: {total_excess} fixed-point = {total_excess/256:.2f} pixels")

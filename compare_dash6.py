"""Align SIM to PF by matching X_fixed values to find true divergence."""
import re

SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260406_130042.txt"
PF_LOG  = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260406_123907.txt"

# Parse PF REPLAY entries: [REPLAY f=N] X=0xHEX (Npx) Y=0xHEX (Npx) velY=0xHEX mode=N
pf_re = re.compile(r'\[REPLAY f=(\d+)\] X=0x([0-9A-Fa-f]+) \((\d+)px\) Y=0x([0-9A-Fa-f]+) \((\d+)px\) velY=0x([0-9A-Fa-f]+) mode=(\d+)')
pf_by_x = {}  # xf -> (fnum, xf, yf, velY, mode)
pf_frames_list = []
with open(PF_LOG, 'r') as f:
    for line in f:
        m = pf_re.search(line)
        if m:
            fnum = int(m.group(1))
            xf = int(m.group(2), 16)
            yf = int(m.group(4), 16)
            velY_raw = int(m.group(6), 16)
            if velY_raw > 0x7FFFFFFF: velY_raw -= 0x100000000
            mode = int(m.group(7))
            pf_by_x[xf] = (fnum, xf, yf, velY_raw, mode)
            pf_frames_list.append((fnum, xf, yf, velY_raw, mode))

print(f"PF frames: {len(pf_frames_list)}")

# Parse SIM STEP_START
step_re = re.compile(r'\[STEP_START\] playerX_fixed=0x([0-9A-Fa-f]+) \((\d+)px\), playerY_fixed=0x([0-9A-Fa-f]+) \((\d+)px\), playerVelY_fixed=0x([0-9A-Fa-f]+)')
sim_steps = []
with open(SIM_LOG, 'r') as f:
    for line in f:
        m = step_re.search(line)
        if m:
            xf = int(m.group(1), 16)
            yf = int(m.group(3), 16)
            velY_raw = int(m.group(5), 16)
            if velY_raw > 0x7FFFFFFF: velY_raw -= 0x100000000
            sim_steps.append((xf, yf, velY_raw))

print(f"SIM steps: {len(sim_steps)}")

# Match by X_fixed value - for each SIM step, find the PF frame with matching X
print("\n--- Matching SIM to PF by X_fixed ---")
match_count = 0
mismatch_count = 0
first_y_div = None
for i, (sx, sy, sv) in enumerate(sim_steps):
    if sx in pf_by_x:
        pf = pf_by_x[sx]
        pf_fnum, pf_xf, pf_yf, pf_velY, pf_mode = pf
        dy = sy - pf_yf
        dv = sv - pf_velY
        if abs(dy) > 0 or abs(dv) > 0:
            mismatch_count += 1
            if first_y_div is None:
                first_y_div = i
            if mismatch_count <= 20:
                print(f"  SIM step {i} X=0x{sx:X}: SIM Y={sy>>8}px velY={sv} | PF f={pf_fnum} Y={pf_yf>>8}px velY={pf_velY} mode={pf_mode} | dY={dy} dVelY={dv}")
        else:
            match_count += 1
    else:
        # SIM has an X value not in PF - this is a dropped/extra step
        pass

print(f"\nX-matched: {match_count}, X-mismatched (Y/velY differ): {mismatch_count}")

# Also try sequential alignment with offset
# Determine offset: count the "double advances" to find how many steps were dropped
print("\n--- Building offset-compensated comparison ---")
offsets = []  # (sim_step_idx, cumulative_offset) 
cum_offset = 0
for i in range(1, len(sim_steps)):
    dx = sim_steps[i][0] - sim_steps[i-1][0]
    # Check if previous step had normal dX
    if i >= 2:
        prev_dx = sim_steps[i-1][0] - sim_steps[i-2][0]
        if prev_dx > 0 and dx > 0:
            ratio = dx / prev_dx
            if ratio >= 1.8 and ratio <= 3.5:
                extra = round(ratio) - 1
                cum_offset += extra
    offsets.append(cum_offset)

print(f"Total cumulative offset at end: {cum_offset}")

# Now compare SIM step i to PF frame i + cumulative_offset[i]
print("\n--- Offset-compensated comparison (first 20 divergences) ---")
div_count = 0
first_div_offset = None
for i in range(len(sim_steps)):
    off = offsets[i-1] if i > 0 else 0
    pf_idx = i + off
    if pf_idx >= len(pf_frames_list):
        break
    pf = pf_frames_list[pf_idx]
    sx, sy, sv = sim_steps[i]
    pf_fnum, pf_xf, pf_yf, pf_velY, pf_mode = pf
    dx = sx - pf_xf
    dy = sy - pf_yf
    dv = sv - pf_velY
    if abs(dx) > 0 or abs(dy) > 0:
        div_count += 1
        if first_div_offset is None:
            first_div_offset = i
        if div_count <= 30:
            print(f"  SIM step {i} (PF f={pf_idx}): SIM X={sx>>8} Y={sy>>8} velY={sv} | PF X={pf_xf>>8} Y={pf_yf>>8} velY={pf_velY} mode={pf_mode} | dX={dx} dY={dy}")

print(f"\nTotal diverged (offset-compensated): {div_count}")

if first_div_offset is not None:
    print(f"\n--- Context around first offset-compensated divergence (SIM step {first_div_offset}) ---")
    for i in range(max(0, first_div_offset - 5), min(len(sim_steps), first_div_offset + 10)):
        off = offsets[i-1] if i > 0 else 0
        pf_idx = i + off
        if pf_idx >= len(pf_frames_list):
            break
        pf = pf_frames_list[pf_idx]
        sx, sy, sv = sim_steps[i]
        pf_fnum, pf_xf, pf_yf, pf_velY, pf_mode = pf
        dx = sx - pf_xf
        dy = sy - pf_yf
        marker = " <<<" if abs(dx) > 0 or abs(dy) > 0 else ""
        print(f"  SIM step {i} → PF f={pf_fnum} (off={off}): SIM X={sx>>8} Y={sy>>8} velY={sv} | PF X={pf_xf>>8} Y={pf_yf>>8} velY={pf_velY} mode={pf_mode} | dX={dx} dY={dy}{marker}")

#!/usr/bin/env python3
"""Compare SIM vs PF (second BFS replay) trajectories for Dash level divergence."""
import re, os

TEMP = os.environ.get('TEMP', '/tmp')
sim_file = os.path.join(TEMP, 'famidash_sim_debug_20260406_030119.txt')
pf_file = os.path.join(TEMP, 'famidash_pf_debug_20260406_024417.txt')

# Extract SIM trajectory: each STEP_START = one frame
sim_frames = []
step_re = re.compile(r'\[STEP_START\] playerX_fixed=0x([0-9A-Fa-f]+).*playerY_fixed=0x([0-9A-Fa-f]+).*playerVelY_fixed=0x([0-9A-Fa-f]+)')
with open(sim_file, 'r') as f:
    for line in f:
        m = step_re.search(line)
        if m:
            x = int(m.group(1), 16)
            y = int(m.group(2), 16)
            vy = int(m.group(3), 16)
            # Handle signed velY (16-bit or 32-bit two's complement)
            if vy > 0x7FFFFFFF:
                vy -= 0x100000000
            sim_frames.append((x, y, vy))

print(f"SIM: {len(sim_frames)} frames")

# Extract PF trajectory from the SECOND BFS replay
# The PF log has two sets of REPLAY entries. Find the second "REPLAY f=0" to start.
pf_frames = []
replay_re = re.compile(r'\[REPLAY f=(\d+)\] X=0x([0-9A-Fa-f]+).*Y=0x([0-9A-Fa-f]+).*velY=0x([0-9A-Fa-f]+).*mode=(\d+)')
replay_start_count = 0
in_second_replay = False

with open(pf_file, 'r') as f:
    for line in f:
        m = replay_re.search(line)
        if m:
            frame = int(m.group(1))
            if frame == 0:
                replay_start_count += 1
                if replay_start_count == 2:
                    in_second_replay = True
                    pf_frames = []  # Reset
            if in_second_replay:
                x = int(m.group(2), 16)
                y = int(m.group(3), 16)
                vy = int(m.group(4), 16)
                mode = int(m.group(5))
                if vy > 0x7FFFFFFF:
                    vy -= 0x100000000
                pf_frames.append((frame, x, y, vy, mode))

print(f"PF (2nd replay): {len(pf_frames)} frames")

# Compare frame by frame
min_frames = min(len(sim_frames), len(pf_frames))
first_div = None
div_count = 0

for i in range(min_frames):
    sx, sy, svy = sim_frames[i]
    pf_f, px, py, pvy, pm = pf_frames[i]
    
    # Compare X and Y (fixed-point, 8 fractional bits)
    sx_px = sx >> 8
    sy_px = sy >> 8
    px_px = px >> 8
    py_px = py >> 8
    
    x_match = (sx == px)
    y_match = (sy == py)
    vy_match = (svy == pvy)
    
    if not (x_match and y_match):
        div_count += 1
        if first_div is None:
            first_div = i
        if div_count <= 20:
            print(f"DIV f={i}: SIM X=0x{sx:X}({sx_px}) Y=0x{sy:X}({sy_px}) VY=0x{svy&0xFFFFFFFF:X} | PF X=0x{px:X}({px_px}) Y=0x{py:X}({py_px}) VY=0x{pvy&0xFFFFFFFF:X} mode={pm}")
    elif not vy_match and div_count == 0:
        # VelY mismatch before position divergence
        print(f"VY_DIV f={i}: SIM VY=0x{svy&0xFFFFFFFF:X} PF VY=0x{pvy&0xFFFFFFFF:X} (X={sx_px} Y={sy_px} mode={pm})")

if first_div is not None:
    print(f"\nFirst position divergence at frame {first_div} (total: {div_count}/{min_frames})")
    # Show context: 5 frames before and after
    start = max(0, first_div - 5)
    end = min(min_frames, first_div + 10)
    print(f"\nContext frames {start}-{end}:")
    for i in range(start, end):
        sx, sy, svy = sim_frames[i]
        pf_f, px, py, pvy, pm = pf_frames[i]
        marker = " <<<" if i == first_div else ""
        x_diff = (sx >> 8) - (px >> 8)
        y_diff = (sy >> 8) - (py >> 8)
        print(f"  f={i}: SIM({sx>>8},{sy>>8},VY=0x{svy&0xFFFF:04X}) PF({px>>8},{py>>8},VY=0x{pvy&0xFFFF:04X}) mode={pm} xd={x_diff} yd={y_diff}{marker}")
else:
    print("\nNo position divergence found in overlapping frames!")

# Also check: what's happening around the SIM death
if len(sim_frames) > 0:
    last_sim = sim_frames[-1]
    print(f"\nSIM last frame (f={len(sim_frames)-1}): X={last_sim[0]>>8} Y={last_sim[1]>>8} VY=0x{last_sim[2]&0xFFFF:04X}")

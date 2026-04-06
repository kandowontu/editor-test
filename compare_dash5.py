"""Find the first Y divergence between SIM and PF by matching PF frame indices."""
import re

SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260406_130042.txt"
PF_LOG  = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260406_123907.txt"

# Parse PF REPLAY entries
pf_re = re.compile(r'\[REPLAY f=(\d+)\] X=0x([0-9A-Fa-f]+) \((\d+)px\) Y=0x([0-9A-Fa-f]+) \((\d+)px\) velY=0x([0-9A-Fa-f]+) mode=(\d+)')
pf_frames = {}
with open(PF_LOG, 'r') as f:
    for line in f:
        m = pf_re.search(line)
        if m:
            fnum = int(m.group(1))
            pf_frames[fnum] = {
                'xf': int(m.group(2), 16), 'xp': int(m.group(3)),
                'yf': int(m.group(4), 16), 'yp': int(m.group(5)),
                'velY': int(m.group(6), 16), 'mode': int(m.group(7))
            }

print(f"PF frames parsed: {len(pf_frames)}")

# Parse SIM STEP_START entries with their sequential index
# Also detect dropped frames by checking for physics entries without STEP_START
step_re = re.compile(r'\[STEP_START\] playerX_fixed=0x([0-9A-Fa-f]+) \((\d+)px\), playerY_fixed=0x([0-9A-Fa-f]+) \((\d+)px\), playerVelY_fixed=0x([0-9A-Fa-f]+)')

sim_steps = []  # Each entry is (xf, xp, yf, yp, velY)
with open(SIM_LOG, 'r') as f:
    for line in f:
        m = step_re.search(line)
        if m:
            xf = int(m.group(1), 16)
            xp = int(m.group(2))
            yf = int(m.group(3), 16)
            yp = int(m.group(4))
            velY = int(m.group(5), 16)
            sim_steps.append((xf, xp, yf, yp, velY))

print(f"SIM STEP_START entries: {len(sim_steps)}")

# Strategy: Map each SIM step to a PF frame by matching X values.
# Since some SIM STEP_STARTs are dropped, let's try a direct comparison first,
# then look at X-matched comparison.

# First, let's find where X values diverge between SIM and PF with direct indexing
# (allowing for the fact that some SIM entries are missing)
print("\n--- Direct index comparison (first 20 divergences) ---")
div_count = 0
first_div_frame = None
for i in range(min(len(sim_steps), len(pf_frames))):
    if i not in pf_frames:
        continue
    pf = pf_frames[i]
    sx, sxp, sy, syp, sv = sim_steps[i]
    dx = sx - pf['xf']
    dy = sy - pf['yf']
    if abs(dx) > 0 or abs(dy) > 0:
        div_count += 1 
        if first_div_frame is None:
            first_div_frame = i
        if div_count <= 20:
            dv = sv - pf['velY']
            print(f"  Frame {i}: SIM X={sxp} Y={syp} velY=0x{sv:X} | PF X={pf['xp']} Y={pf['yp']} velY=0x{pf['velY']:X} mode={pf['mode']} | dX={dx} dY={dy}")

print(f"Total diverged (direct index): {div_count}")

# Show context around first divergence
if first_div_frame is not None:
    print(f"\n--- Context around first divergence (frame {first_div_frame}) ---")
    for i in range(max(0, first_div_frame - 5), min(len(sim_steps), first_div_frame + 10)):
        if i not in pf_frames:
            continue
        pf = pf_frames[i]
        sx, sxp, sy, syp, sv = sim_steps[i]
        dx = sx - pf['xf']
        dy = sy - pf['yf']
        marker = " <<<" if abs(dx) > 0 or abs(dy) > 0 else ""
        # Also show X delta in SIM
        sim_dx = sx - sim_steps[i-1][0] if i > 0 else 0
        pf_prev = pf_frames.get(i-1)
        pf_dx = pf['xf'] - pf_prev['xf'] if pf_prev else 0
        print(f"  Frame {i}: SIM X={sxp}(dX={sim_dx}) Y={syp} velY=0x{sv:X} | PF X={pf['xp']}(dX={pf_dx}) Y={pf['yp']} velY=0x{pf['velY']:X} mode={pf['mode']} | dX={dx} dY={dy}{marker}")

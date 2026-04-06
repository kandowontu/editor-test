"""Compare latest SIM vs PF for Dash level - post volatile fix."""
import re, sys

SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260406_130042.txt"
PF_LOG  = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260406_123907.txt"

step_re = re.compile(r'\[STEP_START\] playerX_fixed=0x([0-9A-Fa-f]+) \((\d+)px\), playerY_fixed=0x([0-9A-Fa-f]+) \((\d+)px\)')
replay_re = re.compile(r'\[REPLAY\].*X_fixed=0x([0-9A-Fa-f]+) \((\d+)px\).*Y_fixed=0x([0-9A-Fa-f]+) \((\d+)px\)')
mode_re = re.compile(r'\[(CUBE|SHIP|BALL|UFO|WAVE|ROBOT|SPIDER|SWING)_')

def parse_sim(path):
    frames = []
    current_mode = "CUBE"
    with open(path, 'r') as f:
        for line in f:
            m = mode_re.search(line)
            if m:
                current_mode = m.group(1)
            m = step_re.search(line)
            if m:
                xf = int(m.group(1), 16)
                xp = int(m.group(2))
                yf = int(m.group(3), 16)
                yp = int(m.group(4))
                frames.append((xf, xp, yf, yp, current_mode))
    return frames

def parse_pf(path):
    frames = []
    current_mode = "CUBE"
    with open(path, 'r') as f:
        for line in f:
            m = mode_re.search(line)
            if m:
                current_mode = m.group(1)
            m = replay_re.search(line)
            if m:
                xf = int(m.group(1), 16)
                xp = int(m.group(2))
                yf = int(m.group(3), 16)
                yp = int(m.group(4))
                frames.append((xf, xp, yf, yp, current_mode))
    return frames

sim_frames = parse_sim(SIM_LOG)
pf_frames  = parse_pf(PF_LOG)
print(f"SIM frames: {len(sim_frames)}, PF frames: {len(pf_frames)}")

# Check for double/triple advances in SIM
doubles = []
for i in range(2, len(sim_frames)):
    dx = sim_frames[i][0] - sim_frames[i-1][0]
    prev_dx = sim_frames[i-1][0] - sim_frames[i-2][0]
    if prev_dx > 0 and dx > 0:
        ratio = dx / prev_dx
        if ratio >= 1.8:
            doubles.append((i, dx, prev_dx, ratio))

print(f"\nDouble/triple advance frames: {len(doubles)}")
for i, dx, prev_dx, ratio in doubles[:10]:
    print(f"  Frame {i}: dx=0x{dx:X} ({dx}), prevDx=0x{prev_dx:X} ({prev_dx}), ratio={ratio:.2f}, mode={sim_frames[i][4]}")

# Find first X divergence between SIM and PF
print(f"\n--- First divergences ---")
diverged = 0
for i in range(min(len(sim_frames), len(pf_frames))):
    sx, sxp, sy, syp, sm = sim_frames[i]
    px, pxp, py, pyp, pm = pf_frames[i]
    dx = sx - px
    dy = sy - py
    if abs(dx) > 0 or abs(dy) > 0:
        diverged += 1
        if diverged <= 20:
            print(f"  Frame {i}: SIM X={sxp} Y={syp} ({sm}) vs PF X={pxp} Y={pyp} ({pm}), dX={dx} dY={dy}")
        
print(f"\nTotal diverged frames: {diverged} / {min(len(sim_frames), len(pf_frames))}")

# Also show X deltas around first divergence
if diverged > 0:
    # Find first divergence frame
    for i in range(min(len(sim_frames), len(pf_frames))):
        sx, sxp, sy, syp, sm = sim_frames[i]
        px, pxp, py, pyp, pm = pf_frames[i]
        if abs(sx - px) > 0 or abs(sy - py) > 0:
            first_div = i
            break
    
    print(f"\n--- X deltas around first divergence (frame {first_div}) ---")
    start = max(1, first_div - 3)
    end = min(len(sim_frames), min(len(pf_frames), first_div + 5))
    for i in range(start, end):
        sim_dx = sim_frames[i][0] - sim_frames[i-1][0]
        pf_dx = pf_frames[i][0] - pf_frames[i-1][0]
        sx, sxp, sy, syp, sm = sim_frames[i]
        px, pxp, py, pyp, pm = pf_frames[i]
        marker = " <<<" if abs(sx - px) > 0 or abs(sy - py) > 0 else ""
        print(f"  Frame {i}: SIM dX=0x{sim_dx:X} ({sim_dx}) PF dX=0x{pf_dx:X} ({pf_dx}) | SIM X={sxp} PF X={pxp} | mode={sm}{marker}")

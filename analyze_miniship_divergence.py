"""Analyze PF vs SIM divergence in mini ship section."""
import re

PF_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260328_135356.txt"
SIM_LOG = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260328_140115.txt"

# 1. Parse all MINISHIP_Y from PF log
pf_data = []
with open(PF_LOG, 'r') as f:
    for line in f:
        m = re.search(r'\[MINISHIP_Y\]\s+X=(\d+)\s+Y=(\d+)\s+velY=(0x[0-9A-Fa-f]+)', line)
        if m:
            fm = re.search(r'f=(\d+)', line)
            frame = int(fm.group(1)) if fm else -1
            pf_data.append({
                'pf_frame': frame,
                'x': int(m.group(1)),
                'y': int(m.group(2)),
                'velY': m.group(3)
            })

print(f"=== PF MINISHIP_Y: {len(pf_data)} entries, PF frames {pf_data[0]['pf_frame']}-{pf_data[-1]['pf_frame']} ===")

# 2. Parse SIM STEP_START and [PF] Frame lines, plus SHIP_EJECT, COLL_DOWN, etc.
sim_steps = []  # sequential STEP_START entries (each = one game frame)
pf_injections = {}  # game_frame -> injection info
ship_ejects_sim = []
coll_down_sim = []
all_sim_events = []  # (line_no, line_text) for context

step_count = 0
with open(SIM_LOG, 'r') as f:
    for line_no, line in enumerate(f, 1):
        # [PF] Frame injection
        fm = re.search(r'\[PF\] Frame (\d+):\s+(.*)', line)
        if fm:
            game_frame = int(fm.group(1))
            detail = fm.group(2).strip()
            pf_injections[game_frame] = detail
        
        # STEP_START = one game frame
        m = re.search(r'\[STEP_START\]\s+playerX_fixed=(0x[0-9A-Fa-f]+)\s+\((\d+)px\),\s*playerY_fixed=(0x[0-9A-Fa-f]+)\s+\((\d+)px\),\s*playerVelY_fixed=(0x[0-9A-Fa-f]+)', line)
        if m:
            sim_steps.append({
                'game_frame': step_count,
                'x_px': int(m.group(2)),
                'y_px': int(m.group(4)),
                'x_fixed': m.group(1),
                'y_fixed': m.group(3),
                'velY': m.group(5),
                'line_no': line_no
            })
            step_count += 1
        
        # Ship eject events
        if '[SHIP_EJECT]' in line:
            ship_ejects_sim.append((step_count, line.strip()))
        if '[COLL_DOWN]' in line or '[COLL_UP]' in line:
            coll_down_sim.append((step_count, line.strip()))

print(f"Total SIM STEP_START (game frames): {len(sim_steps)}")
print(f"Total PF injections: {len(pf_injections)}")
print(f"Ship ejects in SIM: {len(ship_ejects_sim)}")
print(f"Coll up/down in SIM: {len(coll_down_sim)}")

# 3. Parse PF log for SHIP_EJECT events
pf_ship_ejects = []
pf_ship_events = []
with open(PF_LOG, 'r') as f:
    for line in f:
        fm = re.search(r'f=(\d+)', line)
        frame = int(fm.group(1)) if fm else -1
        if '[SHIP_EJECT]' in line:
            pf_ship_ejects.append((frame, line.strip()))
        if frame >= pf_data[0]['pf_frame'] - 5 and frame <= pf_data[-1]['pf_frame'] + 5:
            for tag in ['SHIP_EJECT', 'SHIP_THRUST', 'SHIP_GRAV', 'COLL_DOWN', 'COLL_UP',
                        'hold', 'HOLD', 'score', 'SCORE', 'CEILING', 'FLOOR', 'SNAP',
                        'EJECT', 'FWD_CHECK']:
                if tag.lower() in line.lower() and '[MINISHIP_Y]' not in line:
                    pf_ship_events.append((frame, line.strip()))
                    break

print(f"PF SHIP_EJECT events (in miniship range): {len([e for e in pf_ship_ejects if pf_data[0]['pf_frame']-5 <= e[0] <= pf_data[-1]['pf_frame']+5])}")

# 4. Map PF miniship frames to SIM game frames
# PF MINISHIP_Y entries are sequential game frames during the PF's BFS path
# Need to find which SIM game frame corresponds to the start
# The PF f= is the internal PF frame counter, not the game frame

# Method: match by X position ratio
# Check PF X step size vs SIM X step size
pf_x_steps = [pf_data[i+1]['x'] - pf_data[i]['x'] for i in range(min(10, len(pf_data)-1))]
print(f"\nPF X increments (first 10): {pf_x_steps}")

# SIM X steps around frame 1080
sim_1080_idx = None
for i, s in enumerate(sim_steps):
    if s['game_frame'] >= 1080:
        sim_1080_idx = i
        break
if sim_1080_idx:
    sim_x_steps = [sim_steps[sim_1080_idx+i+1]['x_px'] - sim_steps[sim_1080_idx+i]['x_px'] for i in range(min(10, len(sim_steps)-sim_1080_idx-1))]
    print(f"SIM X increments (from frame 1080): {sim_x_steps}")
    print(f"SIM X at frame 1080: {sim_steps[sim_1080_idx]['x_px']}px, Y={sim_steps[sim_1080_idx]['y_px']}px")

# Try to find X ratio: PF_X / SIM_X
if sim_1080_idx:
    pf_x_first = pf_data[0]['x']
    sim_x_first = sim_steps[sim_1080_idx]['x_px']
    ratio = pf_x_first / sim_x_first if sim_x_first > 0 else 0
    print(f"PF X first={pf_x_first}, SIM X at 1080={sim_x_first}, ratio={ratio:.4f}")

# 5. Direct comparison: assume PF MINISHIP_Y[i] = SIM game frame (1080+i)
# This is the most likely mapping since the PF logs one entry per simulated frame
print("\n=== DIRECT FRAME COMPARISON (PF miniship entry i → SIM game frame 1080+i) ===")
print(f"{'idx':>4} {'PF_f':>6} {'PF_X':>6} {'PF_Y':>4} {'PF_velY':>8} | {'SIM_gf':>6} {'SIM_X':>6} {'SIM_Y':>4} {'SIM_velY':>10} | {'dY':>3} {'dX':>5}")

start_sim_frame = 1080
first_diverge = None
for i, pf in enumerate(pf_data):
    sim_gf = start_sim_frame + i
    # Find SIM entry for this game frame
    sim_idx = None
    for j, s in enumerate(sim_steps):
        if s['game_frame'] == sim_gf:
            sim_idx = j
            break
    
    if sim_idx is not None:
        s = sim_steps[sim_idx]
        dy = pf['y'] - s['y_px']
        dx = pf['x'] - s['x_px']
        marker = ""
        if abs(dy) > 0 and first_diverge is None:
            first_diverge = (i, sim_gf, pf, s, dy)
            marker = " *** FIRST DIVERGE ***"
        elif abs(dy) > 0:
            marker = f" DIFF"
        
        print(f"{i:4d} {pf['pf_frame']:6d} {pf['x']:6d} {pf['y']:4d} {pf['velY']:>8s} | {sim_gf:6d} {s['x_px']:6d} {s['y_px']:4d} {s['velY']:>10s} | {dy:3d} {dx:5d}{marker}")
    else:
        print(f"{i:4d} {pf['pf_frame']:6d} {pf['x']:6d} {pf['y']:4d} {pf['velY']:>8s} | {sim_gf:6d} {'N/A':>6s} {'N/A':>4s} {'N/A':>10s}")

# 6. Report input injection during ship frames
print("\n=== PF INPUT INJECTIONS DURING SHIP FRAMES (1080-1300) ===")
for gf in sorted(pf_injections.keys()):
    if 1078 <= gf <= 1300:
        print(f"  Frame {gf:4d}: {pf_injections[gf]}")

# 7. PF ship events (ejects, collisions, etc.)
print("\n=== PF EVENTS DURING MINISHIP ===")
for frame, text in pf_ship_events[:50]:
    # Truncate long lines
    short = text[:200]
    print(f"  PF f={frame}: {short}")

# 8. SIM ship ejects and collisions near ship frames
print("\n=== SIM SHIP_EJECT events ===")
for sc, text in ship_ejects_sim:
    if 1070 <= sc <= 1300:
        print(f"  ~frame {sc}: {text[:200]}")

print("\n=== SIM COLL_UP/DOWN events (frames 1070-1300) ===")
for sc, text in coll_down_sim:
    if 1070 <= sc <= 1300:
        print(f"  ~frame {sc}: {text[:200]}")

# 9. Summary
if first_diverge:
    idx, gf, pf, sim, dy = first_diverge
    print(f"\n=== FIRST DIVERGENCE ===")
    print(f"  At miniship entry {idx}, SIM game frame {gf}")
    print(f"  PF: X={pf['x']}, Y={pf['y']}, velY={pf['velY']}")
    print(f"  SIM: X={sim['x_px']}px, Y={sim['y_px']}px, velY={sim['velY']}")
    print(f"  Y difference: {dy}")
else:
    print("\n=== NO Y DIVERGENCE FOUND ===")

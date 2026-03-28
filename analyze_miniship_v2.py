# -*- coding: utf-8 -*-
"""Analyze PF vs SIM divergence in mini ship section - v2."""
import re, sys

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

print(f"PF MINISHIP_Y: {len(pf_data)} entries, PF frames {pf_data[0]['pf_frame']}-{pf_data[-1]['pf_frame']}")

# Identify BFS branches: new branch when frame doesn't increment by 1
branches = []
current_branch = [pf_data[0]]
for i in range(1, len(pf_data)):
    if pf_data[i]['pf_frame'] == pf_data[i-1]['pf_frame'] + 1:
        current_branch.append(pf_data[i])
    else:
        branches.append(current_branch)
        current_branch = [pf_data[i]]
branches.append(current_branch)

print(f"Number of BFS branches: {len(branches)}")
for bi, br in enumerate(branches):
    print(f"  Branch {bi}: f={br[0]['pf_frame']}-{br[-1]['pf_frame']} ({len(br)} frames), X={br[0]['x']}-{br[-1]['x']}, Y={br[0]['y']}-{br[-1]['y']}")

# 2. Parse SIM log
sim_steps = []
pf_injections = {}
coll_events_sim = []

step_count = 0
with open(SIM_LOG, 'r') as f:
    for line_no, line in enumerate(f, 1):
        fm = re.search(r'\[PF\] Frame (\d+):\s+(.*)', line)
        if fm:
            pf_injections[int(fm.group(1))] = fm.group(2).strip()
        
        m = re.search(r'\[STEP_START\]\s+playerX_fixed=(0x[0-9A-Fa-f]+)\s+\((\d+)px\),\s*playerY_fixed=(0x[0-9A-Fa-f]+)\s+\((\d+)px\),\s*playerVelY_fixed=(0x[0-9A-Fa-f]+)', line)
        if m:
            sim_steps.append({
                'game_frame': step_count,
                'x_px': int(m.group(2)),
                'y_px': int(m.group(4)),
                'x_fixed': m.group(1),
                'y_fixed': m.group(3),
                'velY': m.group(5),
            })
            step_count += 1
        
        if re.search(r'\[COLL_(DOWN|UP)\]', line):
            coll_events_sim.append((step_count-1, line.strip()))

print(f"\nTotal SIM steps: {len(sim_steps)}")

# 3. The PF first branch starts at f=1080. But is f=1080 the same as SIM game frame 1080?
# Check X values: PF f=1080 X=3305, SIM game_frame=1080 X=3328
# Let's try matching by X proximity instead of frame number

first_branch = branches[0]
print(f"\nFirst PF branch: f={first_branch[0]['pf_frame']}-{first_branch[-1]['pf_frame']}, {len(first_branch)} frames")
print(f"  First entry: X={first_branch[0]['x']}, Y={first_branch[0]['y']}")

# Find SIM frame closest to PF X=3305
pf_x_start = first_branch[0]['x']
best_sim_match = None
for s in sim_steps:
    if abs(s['x_px'] - pf_x_start) < 10:
        if best_sim_match is None or abs(s['x_px'] - pf_x_start) < abs(best_sim_match['x_px'] - pf_x_start):
            best_sim_match = s

if best_sim_match:
    print(f"  Closest SIM match for PF X={pf_x_start}: SIM frame {best_sim_match['game_frame']}, X={best_sim_match['x_px']}px, Y={best_sim_match['y_px']}px")
    sim_offset = best_sim_match['game_frame'] - first_branch[0]['pf_frame']
    print(f"  Offset: SIM frame = PF frame + ({sim_offset})")

# Also check: SIM step 1080
if len(sim_steps) > 1080:
    s1080 = sim_steps[1080]
    print(f"\nSIM at step index 1080: X={s1080['x_px']}px, Y={s1080['y_px']}px, velY={s1080['velY']}")
    print(f"PF at f=1080:           X={first_branch[0]['x']}, Y={first_branch[0]['y']}, velY={first_branch[0]['velY']}")
    print(f"X diff: {first_branch[0]['x'] - s1080['x_px']}")
    print(f"Y diff: {first_branch[0]['y'] - s1080['y_px']}")

# 4. The game frame IS the PF f= value for the first branch (both start from 0)
# PF f=1080 represents game frame 1080
# Compare PF first branch (f=1080 ... f=last) with SIM steps[1080..last]
print(f"\n=== FRAME-BY-FRAME COMPARISON ===")
print(f"{'gf':>5} {'PF_X':>6} {'PF_Y':>4} {'PF_velY':>8} | {'SIM_X':>6} {'SIM_Y':>4} {'SIM_velY':>10} | {'dX':>4} {'dY':>3} {'inj':>5}")

first_diverge = None
for pf in first_branch:
    gf = pf['pf_frame']
    if gf >= len(sim_steps):
        print(f"{gf:5d} {pf['x']:6d} {pf['y']:4d} {pf['velY']:>8s} | {'N/A':>6s} {'N/A':>4s} {'N/A':>10s} |")
        continue
    s = sim_steps[gf]
    dy = pf['y'] - s['y_px']
    dx = pf['x'] - s['x_px']
    inj = "HOLD" if gf in pf_injections else "-"
    marker = ""
    if abs(dy) > 0 and first_diverge is None:
        first_diverge = (gf, pf, s, dy, dx)
        marker = " *** FIRST DIVERGE ***"
    elif abs(dy) > 1:
        marker = f"  DIFF"
    
    print(f"{gf:5d} {pf['x']:6d} {pf['y']:4d} {pf['velY']:>8s} | {s['x_px']:6d} {s['y_px']:4d} {s['velY']:>10s} | {dx:4d} {dy:3d} {inj:>5s}{marker}")

# 5. PF input injections during ship mode
print(f"\n=== PF INJECTIONS IN SHIP MODE (1078-1260) ===")
for gf in sorted(pf_injections.keys()):
    if 1078 <= gf <= 1260:
        print(f"  Frame {gf:4d}: {pf_injections[gf][:120]}")

# Which frames did PF NOT inject (= no hold)?
gf_start = first_branch[0]['pf_frame']
gf_end = min(first_branch[-1]['pf_frame'], len(sim_steps)-1, 1260)
no_hold = [gf for gf in range(gf_start, gf_end+1) if gf not in pf_injections]
print(f"\nNo-hold frames ({len(no_hold)}):")
# Print in groups
for i in range(0, len(no_hold), 20):
    chunk = no_hold[i:i+20]
    print(f"  {chunk}")

# 6. SIM COLL events
print(f"\n=== SIM COLL_UP/DOWN events (frames 1070-1260) ===")
for sc, text in coll_events_sim:
    if 1070 <= sc <= 1260:
        short = text[:180] if len(text) > 180 else text
        print(f"  frame {sc}: {short}")

# 7. PF events during first branch
print(f"\n=== PF EVENTS DURING MINISHIP (first branch) ===")
with open(PF_LOG, 'r') as f:
    for line in f:
        fm = re.search(r'f=(\d+)', line)
        if not fm:
            continue
        frame = int(fm.group(1))
        if frame < first_branch[0]['pf_frame'] - 2 or frame > first_branch[-1]['pf_frame'] + 2:
            continue
        if '[MINISHIP_Y]' in line:
            continue
        llow = line.lower()
        for tag in ['ship_eject', 'eject', 'coll_down', 'coll_up', 'ceiling', 'floor',
                    'snap', 'fwd_check', 'hold', 'thrust', 'grav', 'score', 'death', 'kill']:
            if tag in llow:
                short = line.strip()[:180]
                print(f"  PF f={frame}: {short}")
                break

# 8. Summary
if first_diverge:
    gf, pf_e, sim_e, dy, dx = first_diverge
    print(f"\n=== FIRST Y DIVERGENCE ===")
    print(f"  Game frame: {gf}")
    print(f"  PF:  X={pf_e['x']}, Y={pf_e['y']}, velY={pf_e['velY']}")
    print(f"  SIM: X={sim_e['x_px']}px, Y={sim_e['y_px']}px, velY={sim_e['velY']}")
    print(f"  dX={dx}, dY={dy}")
    
    print(f"\n  Context (5 frames before/after):")
    for offset in range(-5, 6):
        check_gf = gf + offset
        pf_entry = None
        for p in first_branch:
            if p['pf_frame'] == check_gf:
                pf_entry = p
                break
        if pf_entry and check_gf < len(sim_steps):
            s = sim_steps[check_gf]
            inj = "HOLD" if check_gf in pf_injections else "  - "
            tag = " <<<" if offset == 0 else ""
            print(f"    gf={check_gf} PF_Y={pf_entry['y']:3d} SIM_Y={s['y_px']:3d} dY={pf_entry['y']-s['y_px']:+3d} velY_PF={pf_entry['velY']} velY_SIM={s['velY']} {inj}{tag}")
else:
    print("\n=== NO Y DIVERGENCE FOUND ===")

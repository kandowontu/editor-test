import re
import sys
import os

PF_LOG = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260213_205337.txt')
SIM_LOG = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260213_205728.txt')

# Parse sim STEP_START lines
# Format: [STEP_START] playerX_fixed=0xNNNNN (NNNNpx), playerY_fixed=0xNNNN (NNpx), playerVelY_fixed=0xNNNN
sim_re = re.compile(r'\[STEP_START\] playerX_fixed=(0x[\dA-Fa-f]+) \((\d+)px\), playerY_fixed=(0x[\dA-Fa-f]+) \((\d+)px\), playerVelY_fixed=(0x[\dA-Fa-f]+)')

# Parse PF STEP_START lines
# Format: [PF f=NNNN] [STEP_START] X_fixed=0xNNNNN (NNNNpx) Y_fixed=0xNNNN (NNpx) VelY=0xNNNN mode=N gravFlipped=... gravMul=... onGround=... wasZeroed=... mini=...
pf_re = re.compile(r'\[PF f=(\d+)\] \[STEP_START\] X_fixed=(0x[\dA-Fa-f]+) \((\d+)px\) Y_fixed=(0x[\dA-Fa-f]+) \((\d+)px\) VelY=(0x[\dA-Fa-f]+) mode=(\d+) gravFlipped=(\w+) gravMul=(-?\d+) onGround=(\w+) wasZeroed=(\w+) mini=(\w+)')

print("=== STEP 1: Parse sim log ===")
sim_states = []
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for line in f:
        m = sim_re.search(line)
        if m:
            sim_states.append({
                'x_hex': m.group(1), 'x_px': int(m.group(2)),
                'y_hex': m.group(3), 'y_px': int(m.group(4)),
                'vely_hex': m.group(5),
                'line': line.strip()
            })

print(f"  Parsed {len(sim_states)} sim STEP_START entries")
print(f"  Sim X range: {sim_states[0]['x_px']}px to {sim_states[-1]['x_px']}px")

print("\n=== STEP 2: Parse PF log (last occurrence of each frame) ===")
# For PF, we need the LAST occurrence of each frame number (final backtracking path)
# Since the file is 119MB, we'll read and keep only the last occurrence per frame
pf_states = {}  # frame_num -> state dict
pf_line_map = {}  # frame_num -> original line
count = 0
with open(PF_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for line in f:
        m = pf_re.search(line)
        if m:
            count += 1
            fnum = int(m.group(1))
            pf_states[fnum] = {
                'frame': fnum,
                'x_hex': m.group(2), 'x_px': int(m.group(3)),
                'y_hex': m.group(4), 'y_px': int(m.group(5)),
                'vely_hex': m.group(6),
                'mode': int(m.group(7)),
                'gravFlipped': m.group(8),
                'gravMul': int(m.group(9)),
                'onGround': m.group(10),
                'wasZeroed': m.group(11),
                'mini': m.group(12),
                'line': line.strip()
            }

print(f"  Parsed {count} total PF STEP_START entries")
print(f"  Unique PF frames (last occurrence): {len(pf_states)}")

# Sort PF frames by frame number
pf_frames_sorted = sorted(pf_states.keys())
print(f"  PF frame range: {pf_frames_sorted[0]} to {pf_frames_sorted[-1]}")
print(f"  PF X range: {pf_states[pf_frames_sorted[0]]['x_px']}px to {pf_states[pf_frames_sorted[-1]]['x_px']}px")

print("\n=== STEP 3: Match sim frames to PF frames by X position ===")
# Build a lookup: for each sim index, find the PF frame with matching X
# First, build PF x_hex -> frame mapping (last frame with that X)
pf_by_x_hex = {}
for fnum in pf_frames_sorted:
    pf_by_x_hex[pf_states[fnum]['x_hex']] = fnum

matches = 0
mismatches = []
first_divergence = None

for sim_idx, sim_state in enumerate(sim_states):
    sim_x = sim_state['x_hex']
    if sim_x in pf_by_x_hex:
        pf_fnum = pf_by_x_hex[sim_x]
        pf_state = pf_states[pf_fnum]
        
        # Compare Y and VelY
        y_match = (sim_state['y_hex'] == pf_state['y_hex'])
        vely_match = (sim_state['vely_hex'] == pf_state['vely_hex'])
        
        if y_match and vely_match:
            matches += 1
        else:
            if first_divergence is None:
                first_divergence = (sim_idx, pf_fnum, sim_state, pf_state)
            mismatches.append((sim_idx, pf_fnum, sim_state, pf_state))
    else:
        # No PF frame with exactly matching X
        if first_divergence is None:
            # Find closest PF frame
            sim_x_val = int(sim_x, 16)
            closest_frame = min(pf_frames_sorted, key=lambda f: abs(int(pf_states[f]['x_hex'], 16) - sim_x_val))
            pf_state = pf_states[closest_frame]
            first_divergence = (sim_idx, closest_frame, sim_state, pf_state)
            mismatches.append((sim_idx, closest_frame, sim_state, pf_state))

print(f"  Matching frames: {matches}")
print(f"  Mismatching frames: {len(mismatches)}")

if first_divergence:
    sim_idx, pf_fnum, sim_s, pf_s = first_divergence
    print(f"\n=== FIRST DIVERGENCE ===")
    print(f"  Sim frame index: {sim_idx}")
    print(f"  PF frame number: {pf_fnum}")
    print(f"  Sim X: {sim_s['x_hex']} ({sim_s['x_px']}px)")
    print(f"  PF  X: {pf_s['x_hex']} ({pf_s['x_px']}px)")
    print(f"  Sim Y: {sim_s['y_hex']} ({sim_s['y_px']}px)")
    print(f"  PF  Y: {pf_s['y_hex']} ({pf_s['y_px']}px)")
    print(f"  Sim VelY: {sim_s['vely_hex']}")
    print(f"  PF  VelY: {pf_s['vely_hex']}")
    print(f"  PF mode={pf_s['mode']} gravFlipped={pf_s['gravFlipped']} gravMul={pf_s['gravMul']} onGround={pf_s['onGround']} wasZeroed={pf_s['wasZeroed']} mini={pf_s['mini']}")
    
    # Show context - few frames before and after divergence
    print(f"\n=== CONTEXT: 5 frames before and after divergence ===")
    print(f"\n--- Sim context (frames {max(0,sim_idx-5)} to {min(len(sim_states)-1, sim_idx+5)}) ---")
    for i in range(max(0, sim_idx-5), min(len(sim_states), sim_idx+6)):
        marker = " <<<< DIVERGE" if i == sim_idx else ""
        s = sim_states[i]
        print(f"  [sim idx={i}] X={s['x_hex']}({s['x_px']}px) Y={s['y_hex']}({s['y_px']}px) VelY={s['vely_hex']}{marker}")
    
    print(f"\n--- PF context (frames {max(0,pf_fnum-5)} to {min(pf_frames_sorted[-1], pf_fnum+5)}) ---")
    for fn in range(max(0, pf_fnum-5), min(pf_frames_sorted[-1]+1, pf_fnum+6)):
        if fn in pf_states:
            marker = " <<<< DIVERGE" if fn == pf_fnum else ""
            s = pf_states[fn]
            print(f"  [PF f={fn}] X={s['x_hex']}({s['x_px']}px) Y={s['y_hex']}({s['y_px']}px) VelY={s['vely_hex']} mode={s['mode']} grav={s['gravFlipped']} ground={s['onGround']}{marker}")

    # Check if sim_idx == pf_fnum (frame offset check)
    print(f"\n=== FRAME INDEX OFFSET CHECK ===")
    print(f"  Sim frame index at divergence: {sim_idx}")
    print(f"  PF frame number at divergence: {pf_fnum}")
    print(f"  Offset: {pf_fnum - sim_idx}")

    # Show additional mismatches
    print(f"\n=== NEXT 10 MISMATCHES ===")
    for j, (si, pf, ss, ps) in enumerate(mismatches[:10]):
        print(f"  [{j}] sim_idx={si} pf_f={pf} simX={ss['x_px']}px simY={ss['y_hex']} pfY={ps['y_hex']} simVelY={ss['vely_hex']} pfVelY={ps['vely_hex']}")

else:
    print("\n  NO DIVERGENCE FOUND in matching X positions!")

print("\n=== DONE ===")

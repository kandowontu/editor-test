import re
import os

PF_LOG = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260213_205337.txt')
SIM_LOG = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260213_205728.txt')

sim_re = re.compile(r'\[STEP_START\] playerX_fixed=(0x[\dA-Fa-f]+) \((\d+)px\), playerY_fixed=(0x[\dA-Fa-f]+) \((\d+)px\), playerVelY_fixed=(0x[\dA-Fa-f]+)')
pf_re = re.compile(r'\[PF f=(\d+)\] \[STEP_START\] X_fixed=(0x[\dA-Fa-f]+) \((\d+)px\) Y_fixed=(0x[\dA-Fa-f]+) \((\d+)px\) VelY=(0x[\dA-Fa-f]+) mode=(\d+) gravFlipped=(\w+) gravMul=(-?\d+) onGround=(\w+) wasZeroed=(\w+) mini=(\w+)')

def hex_to_int(h):
    """Convert hex string to int, handling signed 16-bit values"""
    v = int(h, 16)
    return v

print("=== Parsing sim log ===")
sim_states = []
sim_all_lines = {}  # line_number -> line content (for context retrieval)
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for lineno, line in enumerate(f):
        m = sim_re.search(line)
        if m:
            sim_states.append({
                'x_int': hex_to_int(m.group(1)), 'x_px': int(m.group(2)),
                'y_int': hex_to_int(m.group(3)), 'y_px': int(m.group(4)),
                'vely_int': hex_to_int(m.group(5)),
                'x_hex': m.group(1), 'y_hex': m.group(3), 'vely_hex': m.group(5),
                'lineno': lineno,
                'line': line.strip()
            })
print(f"  {len(sim_states)} sim frames, X: {sim_states[0]['x_px']}px to {sim_states[-1]['x_px']}px")

print("=== Parsing PF log (last occurrence per frame) ===")
pf_states = {}
pf_line_numbers = {}
count = 0
with open(PF_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for lineno, line in enumerate(f):
        m = pf_re.search(line)
        if m:
            count += 1
            fnum = int(m.group(1))
            pf_states[fnum] = {
                'frame': fnum,
                'x_int': hex_to_int(m.group(2)), 'x_px': int(m.group(3)),
                'y_int': hex_to_int(m.group(4)), 'y_px': int(m.group(5)),
                'vely_int': hex_to_int(m.group(6)),
                'x_hex': m.group(2), 'y_hex': m.group(4), 'vely_hex': m.group(6),
                'mode': int(m.group(7)),
                'gravFlipped': m.group(8),
                'gravMul': int(m.group(9)),
                'onGround': m.group(10),
                'wasZeroed': m.group(11),
                'mini': m.group(12),
                'lineno': lineno,
                'line': line.strip()
            }
            pf_line_numbers[fnum] = lineno

pf_sorted = sorted(pf_states.keys())
print(f"  {count} total PF entries, {len(pf_states)} unique frames")
print(f"  PF X: {pf_states[pf_sorted[0]]['x_px']}px to {pf_states[pf_sorted[-1]]['x_px']}px")

print("\n=== Matching by X_fixed (integer comparison) ===")
# Build PF lookup by x_int
pf_by_x_int = {}
for fnum in pf_sorted:
    pf_by_x_int[pf_states[fnum]['x_int']] = fnum

matched = 0
first_diverge = None
diverge_list = []

for sim_idx, ss in enumerate(sim_states):
    sx = ss['x_int']
    if sx in pf_by_x_int:
        pf_fnum = pf_by_x_int[sx]
        ps = pf_states[pf_fnum]
        
        y_match = (ss['y_int'] == ps['y_int'])
        vely_match = (ss['vely_int'] == ps['vely_int'])
        
        if y_match and vely_match:
            matched += 1
        else:
            if first_diverge is None:
                first_diverge = (sim_idx, pf_fnum, ss, ps)
            diverge_list.append((sim_idx, pf_fnum, ss, ps))
    else:
        # X not found in PF - this is also a divergence
        # Find closest PF frame
        closest = min(pf_sorted, key=lambda f: abs(pf_states[f]['x_int'] - sx))
        ps = pf_states[closest]
        if first_diverge is None:
            first_diverge = (sim_idx, closest, ss, ps)
        diverge_list.append((sim_idx, closest, ss, ps))

print(f"  Matching frames: {matched}")
print(f"  Divergent frames: {len(diverge_list)}")

if first_diverge:
    sim_idx, pf_fnum, ss, ps = first_diverge
    print(f"\n{'='*60}")
    print(f"FIRST DIVERGENCE")
    print(f"{'='*60}")
    print(f"  Sim frame index: {sim_idx}")
    print(f"  PF frame number: {pf_fnum}")
    print(f"  Frame offset: {pf_fnum - sim_idx}")
    print(f"  ---")
    print(f"  Sim: X={ss['x_hex']}({ss['x_px']}px) Y={ss['y_hex']}({ss['y_px']}px) VelY={ss['vely_hex']}")
    print(f"  PF:  X={ps['x_hex']}({ps['x_px']}px) Y={ps['y_hex']}({ps['y_px']}px) VelY={ps['vely_hex']}")
    print(f"  PF extra: mode={ps['mode']} gravFlipped={ps['gravFlipped']} gravMul={ps['gravMul']} onGround={ps['onGround']} wasZeroed={ps['wasZeroed']} mini={ps['mini']}")
    
    # Differences
    if ss['y_int'] != ps['y_int']:
        print(f"  ** Y MISMATCH: sim={ss['y_int']} pf={ps['y_int']} diff={ss['y_int']-ps['y_int']}")
    if ss['vely_int'] != ps['vely_int']:
        print(f"  ** VelY MISMATCH: sim={ss['vely_int']} pf={ps['vely_int']} diff={ss['vely_int']-ps['vely_int']}")

    # Context around divergence
    print(f"\n{'='*60}")
    print(f"CONTEXT: 10 frames before and after divergence")
    print(f"{'='*60}")
    
    print(f"\n--- SIM (diverge at idx={sim_idx}) ---")
    for i in range(max(0, sim_idx-10), min(len(sim_states), sim_idx+11)):
        s = sim_states[i]
        marker = " <<<< DIVERGE" if i == sim_idx else ""
        print(f"  [{i:4d}] X=0x{s['x_int']:05X}({s['x_px']:5d}px) Y=0x{s['y_int']:05X}({s['y_px']:3d}px) VelY=0x{s['vely_int']:04X}{marker}")

    print(f"\n--- PF (diverge at f={pf_fnum}) ---")
    for fn in range(max(0, pf_fnum-10), min(pf_sorted[-1]+1, pf_fnum+11)):
        if fn in pf_states:
            s = pf_states[fn]
            marker = " <<<< DIVERGE" if fn == pf_fnum else ""
            print(f"  [f={fn:4d}] X=0x{s['x_int']:05X}({s['x_px']:5d}px) Y=0x{s['y_int']:05X}({s['y_px']:3d}px) VelY=0x{s['vely_int']:04X} mode={s['mode']} grav={s['gravFlipped']} ground={s['onGround']}{marker}")

    print(f"\n{'='*60}")
    print(f"NEXT 20 DIVERGENCES")
    print(f"{'='*60}")
    for j, (si, pf, ss2, ps2) in enumerate(diverge_list[:20]):
        y_diff = ss2['y_int'] - ps2['y_int']
        v_diff = ss2['vely_int'] - ps2['vely_int']
        print(f"  [{j:3d}] simIdx={si:4d} pfF={pf:4d} offset={pf-si:+d} X={ss2['x_px']:5d}px simY=0x{ss2['y_int']:05X} pfY=0x{ps2['y_int']:05X} Ydiff={y_diff:+d} simV=0x{ss2['vely_int']:04X} pfV=0x{ps2['vely_int']:04X} Vdiff={v_diff:+d}")

else:
    print("\nNO DIVERGENCE FOUND - states match perfectly!")

# Also look for PF frame decisions around divergence
if first_diverge:
    sim_idx, pf_fnum, ss, ps = first_diverge
    print(f"\n{'='*60}")
    print(f"PF DECISIONS around divergence (searching PF log for frame {pf_fnum})")
    print(f"{'='*60}")
    
    # Search PF log for lines near the last occurrence of this frame
    target_lineno = pf_line_numbers[pf_fnum]
    with open(PF_LOG, 'r', encoding='utf-8', errors='replace') as f:
        lines = []
        for i, line in enumerate(f):
            if abs(i - target_lineno) <= 20:
                lines.append((i, line.rstrip()))
    
    for lno, line in lines:
        marker = " <<<<" if lno == target_lineno else ""
        # Strip timestamp
        clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line)
        print(f"  [L{lno}]{marker} {clean}")

    # Also search sim log for context
    print(f"\n{'='*60}")
    print(f"SIM detailed context around divergence (sim frame {sim_idx})")
    print(f"{'='*60}")
    
    target_lineno = ss['lineno']
    with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
        lines = []
        for i, line in enumerate(f):
            if abs(i - target_lineno) <= 20:
                lines.append((i, line.rstrip()))
    
    for lno, line in lines:
        marker = " <<<<" if lno == target_lineno else ""
        clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line)
        print(f"  [L{lno}]{marker} {clean}")

print("\n=== ANALYSIS COMPLETE ===")

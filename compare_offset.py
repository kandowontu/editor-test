import re
import os

PF_LOG = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260213_205337.txt')
SIM_LOG = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260213_205728.txt')

sim_re = re.compile(r'\[STEP_START\] playerX_fixed=(0x[\dA-Fa-f]+) \((\d+)px\), playerY_fixed=(0x[\dA-Fa-f]+) \((\d+)px\), playerVelY_fixed=(0x[\dA-Fa-f]+)')
pf_re = re.compile(r'\[PF f=(\d+)\] \[STEP_START\] X_fixed=(0x[\dA-Fa-f]+) \((\d+)px\) Y_fixed=(0x[\dA-Fa-f]+) \((\d+)px\) VelY=(0x[\dA-Fa-f]+)')

def h2i(h): return int(h, 16)

print("=== Parse sim ===")
sim_states = []
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for lineno, line in enumerate(f):
        m = sim_re.search(line)
        if m:
            sim_states.append({
                'x': h2i(m.group(1)), 'x_px': int(m.group(2)),
                'y': h2i(m.group(3)), 'y_px': int(m.group(4)),
                'vy': h2i(m.group(5)), 'lineno': lineno
            })
print(f"  {len(sim_states)} frames")

print("=== Parse PF (last occurrence) ===")
pf_states = {}
with open(PF_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for lineno, line in enumerate(f):
        m = pf_re.search(line)
        if m:
            fnum = int(m.group(1))
            pf_states[fnum] = {
                'frame': fnum,
                'x': h2i(m.group(2)), 'x_px': int(m.group(3)),
                'y': h2i(m.group(4)), 'y_px': int(m.group(5)),
                'vy': h2i(m.group(6)), 'lineno': lineno
            }
pf_sorted = sorted(pf_states.keys())
print(f"  {len(pf_states)} unique frames")

# Build PF lookup by x (int)
pf_by_x = {}
for fn in pf_sorted:
    pf_by_x[pf_states[fn]['x']] = fn

print("\n=== FRAME OFFSET ANALYSIS ===")
print("Tracking when sim_idx vs pf_frame_number first differ:")
prev_offset = 0
offset_changes = []
for sim_idx, ss in enumerate(sim_states):
    if ss['x'] in pf_by_x:
        pf_fn = pf_by_x[ss['x']]
        offset = pf_fn - sim_idx
        if offset != prev_offset:
            offset_changes.append((sim_idx, pf_fn, offset, ss))
            prev_offset = offset

for si, pf, off, ss in offset_changes:
    print(f"  sim_idx={si:4d} pf_f={pf:4d} offset={off:+d} X={ss['x_px']:5d}px Y={ss['y_px']:3d}px")

# Now check: for the first offset change, what happened in the PF?
if offset_changes:
    si0, pf0, off0, ss0 = offset_changes[0]
    print(f"\n=== FIRST OFFSET CHANGE at sim_idx={si0}, pf_f={pf0}, X={ss0['x_px']}px ===")
    print(f"  Previous offset was 0, now {off0:+d}")
    
    # Show frames around where the PF has extra frames
    # If offset went from 0 to +2, the PF has 2 extra frames before this point
    # Let's look at PF frames around pf0-5 to pf0+5 and sim around si0-5 to si0+5
    print(f"\n--- PF frames {max(0,pf0-8)} to {pf0+3} ---")
    for fn in range(max(0, pf0-8), pf0+4):
        if fn in pf_states:
            s = pf_states[fn]
            in_sim = "YES" if s['x'] in {sim_states[i]['x'] for i in range(max(0,si0-8), min(len(sim_states), si0+4))} else "no"
            print(f"  [PF f={fn:4d}] X=0x{s['x']:05X}({s['x_px']:5d}px) Y=0x{s['y']:05X}({s['y_px']:3d}px) VelY=0x{s['vy']:04X}  simMatch={in_sim}")

    print(f"\n--- Sim frames {max(0,si0-5)} to {min(len(sim_states)-1,si0+3)} ---")
    for i in range(max(0, si0-5), min(len(sim_states), si0+4)):
        s = sim_states[i]
        in_pf = "YES" if s['x'] in pf_by_x else "no"
        print(f"  [sim {i:4d}] X=0x{s['x']:05X}({s['x_px']:5d}px) Y=0x{s['y']:05X}({s['y_px']:3d}px) VelY=0x{s['vy']:04X}  pfMatch={in_pf}")

    # Find what X values exist in PF but not in sim near the offset change
    pf_x_set = set()
    sim_x_set = set()
    for fn in range(max(0, pf0-10), min(pf_sorted[-1]+1, pf0+5)):
        if fn in pf_states:
            pf_x_set.add(pf_states[fn]['x'])
    for i in range(max(0, si0-10), min(len(sim_states), si0+5)):
        sim_x_set.add(sim_states[i]['x'])
    
    extra_pf = pf_x_set - sim_x_set
    extra_sim = sim_x_set - pf_x_set
    if extra_pf:
        print(f"\n  X values in PF but NOT in sim:")
        for x in sorted(extra_pf):
            fn = pf_by_x[x]
            s = pf_states[fn]
            print(f"    PF f={fn}: X=0x{x:05X}({s['x_px']}px) Y=0x{s['y']:05X}({s['y_px']}px)")
    if extra_sim:
        print(f"\n  X values in sim but NOT in PF:")
        for x in sorted(extra_sim):
            print(f"    X=0x{x:05X}")

print("\n=== DONE ===")

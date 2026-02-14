import re
import os

PF_LOG = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260213_205337.txt')
SIM_LOG = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260213_205728.txt')

sim_re = re.compile(r'\[STEP_START\] playerX_fixed=(0x[\dA-Fa-f]+) \((\d+)px\), playerY_fixed=(0x[\dA-Fa-f]+) \((\d+)px\), playerVelY_fixed=(0x[\dA-Fa-f]+)')
pf_re = re.compile(r'\[PF f=(\d+)\] \[STEP_START\] X_fixed=(0x[\dA-Fa-f]+) \((\d+)px\) Y_fixed=(0x[\dA-Fa-f]+) \((\d+)px\) VelY=(0x[\dA-Fa-f]+) mode=(\d+) gravFlipped=(\w+) gravMul=(-?\d+) onGround=(\w+) wasZeroed=(\w+) mini=(\w+)')

def h2i(h): return int(h, 16)
def s16(v):
    """Interpret as signed 32-bit"""
    if v >= 0x80000000: return v - 0x100000000
    return v

print("=== Parse sim ===")
sim_states = []
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for lineno, line in enumerate(f):
        m = sim_re.search(line)
        if m:
            sim_states.append({
                'x': h2i(m.group(1)), 'x_px': int(m.group(2)),
                'y': h2i(m.group(3)), 'y_px': int(m.group(4)),
                'vy': h2i(m.group(5)), 'vy_s': s16(h2i(m.group(5))),
                'lineno': lineno
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
                'frame': fnum, 'x': h2i(m.group(2)), 'x_px': int(m.group(3)),
                'y': h2i(m.group(4)), 'y_px': int(m.group(5)),
                'vy': h2i(m.group(6)), 'vy_s': s16(h2i(m.group(6))),
                'mode': int(m.group(7)), 'gravFlipped': m.group(8),
                'gravMul': int(m.group(9)), 'onGround': m.group(10),
                'lineno': lineno
            }
pf_sorted = sorted(pf_states.keys())
print(f"  {len(pf_states)} unique frames")

# Focus on the area around X=10141 (sim idx ~3655)
# Show all matched frames from sim 3640 to 3672 with the PF equivalent
pf_by_x = {}
for fn in pf_sorted:
    pf_by_x[pf_states[fn]['x']] = fn

print("\n=== DETAILED COMPARISON: sim frames 3640-3672 (near death at X=10188) ===")
print(f"{'simIdx':>6} {'pfF':>5} {'off':>4} {'Xpx':>6} {'simY':>8} {'pfY':>8} {'Ymatch':>7} {'simVy':>8} {'pfVy':>8} {'Vmatch':>7} {'pfMode':>5} {'pfGrav':>8} {'pfGnd':>6}")
for si in range(max(0, 3640), min(len(sim_states), 3673)):
    ss = sim_states[si]
    if ss['x'] in pf_by_x:
        pfn = pf_by_x[ss['x']]
        ps = pf_states[pfn]
        ym = "OK" if ss['y'] == ps['y'] else f"DIFF({ss['y']-ps['y']:+d})"
        vm = "OK" if ss['vy'] == ps['vy'] else f"DIFF({ss['vy_s']-ps['vy_s']:+d})"
        print(f"{si:6d} {pfn:5d} {pfn-si:+4d} {ss['x_px']:6d} 0x{ss['y']:05X} 0x{ps['y']:05X} {ym:>7} {ss['vy_s']:>8d} {ps['vy_s']:>8d} {vm:>7} {ps['mode']:>5d} {ps['gravFlipped']:>8} {ps['onGround']:>6}")
    else:
        print(f"{si:6d}   N/A      {ss['x_px']:6d} 0x{ss['y']:05X}     N/A     --- {ss['vy_s']:>8d}      ---     ---   ---      ---    ---")

# Find the exact PF frame and sim frame where VelY first diverges (not just landing offset)
print("\n=== FINDING THE PERSISTENT VelY DIVERGENCE ===")
for si in range(3640, min(len(sim_states), 3673)):
    ss = sim_states[si]
    if ss['x'] in pf_by_x:
        pfn = pf_by_x[ss['x']]
        ps = pf_states[pfn]
        if ss['vy'] != ps['vy']:
            print(f"\nFIRST VelY mismatch in death zone:")
            print(f"  sim idx={si}, PF f={pfn}, X={ss['x_px']}px")
            print(f"  sim: Y=0x{ss['y']:05X}({ss['y_px']}px) VelY={ss['vy_s']} (0x{ss['vy']:08X})")
            print(f"  PF:  Y=0x{ps['y']:05X}({ps['y_px']}px) VelY={ps['vy_s']} (0x{ps['vy']:08X})")
            
            # Get detailed context from both logs
            # First the sim
            sim_target = ss['lineno']
            print(f"\n--- SIM detailed log around line {sim_target} ---")
            with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
                for i, line in enumerate(f):
                    if i >= sim_target - 40 and i <= sim_target + 10:
                        clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                        print(f"  [L{i}] {clean}")
            
            # Then the PF
            pf_target = ps['lineno']
            print(f"\n--- PF detailed log around line {pf_target} ---")
            with open(PF_LOG, 'r', encoding='utf-8', errors='replace') as f:
                for i, line in enumerate(f):
                    if i >= pf_target - 40 and i <= pf_target + 10:
                        clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                        print(f"  [L{i}] {clean}")
            break

# Also check the frame just BEFORE the divergence
print("\n=== FRAME JUST BEFORE DIVERGENCE ===")
for si in range(3654, 3640, -1):
    ss = sim_states[si]
    if ss['x'] in pf_by_x:
        pfn = pf_by_x[ss['x']]
        ps = pf_states[pfn]
        if ss['vy'] == ps['vy'] and ss['y'] == ps['y']:
            print(f"Last matching frame:")
            print(f"  sim idx={si}, PF f={pfn}, X={ss['x_px']}px")
            print(f"  Y=0x{ss['y']:05X}({ss['y_px']}px) VelY={ss['vy_s']} (0x{ss['vy']:08X})")
            
            # Get the sim context from this matching frame to the divergence
            sim_target = ss['lineno']
            print(f"\n--- SIM log from last match to divergence ---")
            with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
                for i, line in enumerate(f):
                    if i >= sim_target and i <= sim_target + 80:
                        clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                        print(f"  [L{i}] {clean}")
            
            # PF context
            pf_target = ps['lineno']
            print(f"\n--- PF log from last match to divergence ---")
            with open(PF_LOG, 'r', encoding='utf-8', errors='replace') as f:
                for i, line in enumerate(f):
                    if i >= pf_target and i <= pf_target + 80:
                        clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                        print(f"  [L{i}] {clean}")
            break

print("\n=== DONE ===")

import re, os

pf_path = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260310_181331.txt')
sim_path = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260310_182928.txt')

# Parse PF log - extract per-frame: posY after grav, posY after eject, velY, events
pf_frames = {}  # frame -> dict with keys: grav_posY, eject_posY, velY_before, velY_after, events, mini
with open(pf_path) as f:
    for line in f:
        line = line.strip()
        m = re.match(r'\[PF f=(\d+)\] \[CUBE_GRAV\] velY: (0x[0-9A-Fa-f]+) \+ (0x[0-9A-Fa-f]+) = (0x[0-9A-Fa-f]+), posY: (0x[0-9A-Fa-f]+) \((\d+)px\) -> (0x[0-9A-Fa-f]+) \((\d+)px\)', line)
        if m:
            f_num = int(m.group(1))
            if f_num not in pf_frames: pf_frames[f_num] = {'events': []}
            pf_frames[f_num]['velY_before'] = m.group(2)
            pf_frames[f_num]['velY_after'] = m.group(4)
            pf_frames[f_num]['grav_posY'] = int(m.group(8))
            pf_frames[f_num]['grav_posY_hex'] = m.group(7)
            continue
        m = re.match(r'\[PF f=(\d+)\] \[EJECT\] .* -> (\d+)', line)
        if m:
            f_num = int(m.group(1))
            if f_num not in pf_frames: pf_frames[f_num] = {'events': []}
            pf_frames[f_num]['eject_posY'] = int(m.group(2))
            continue
        m = re.match(r'\[PF f=(\d+)\] \[([A-Z_]+)\](.*)', line)
        if m:
            f_num = int(m.group(1))
            tag = m.group(2)
            rest = m.group(3)
            if tag != 'CUBE_GRAV':
                if f_num not in pf_frames: pf_frames[f_num] = {'events': []}
                pf_frames[f_num]['events'].append(f'[{tag}]{rest}')
                if 'mini=True' in rest:
                    pf_frames[f_num]['mini'] = True
                if 'mini=False' in rest:
                    pf_frames[f_num]['mini'] = False

# Parse SIM log - frame-by-frame using STEP_START
sim_frames = {}  # frame_idx -> dict
frame_idx = -1
ts_re = re.compile(r'^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z ')
with open(sim_path) as f:
    for line in f:
        line = ts_re.sub('', line.strip())
        
        m = re.match(r'\[STEP_START\] playerX_fixed=(0x[0-9A-Fa-f]+) \((\d+)px\), playerY_fixed=(0x[0-9A-Fa-f]+) \((\d+)px\), playerVelY_fixed=(0x[0-9A-Fa-f]+)', line)
        if m:
            frame_idx += 1
            sim_frames[frame_idx] = {
                'x_px': int(m.group(2)),
                'y_px': int(m.group(4)),
                'y_hex': m.group(3),
                'velY': m.group(5),
                'events': [],
                'mini': None,
                'grav_posY': None,
                'grav_posY_hex': None,
                'grav_velY': None,
            }
            continue
        
        if frame_idx < 0:
            continue
        
        m = re.match(r'\[GRAV_POS\] posY: .* = (0x[0-9A-Fa-f]+) \((\d+)px\)', line)
        if m:
            sim_frames[frame_idx]['grav_posY'] = int(m.group(2))
            sim_frames[frame_idx]['grav_posY_hex'] = m.group(1)
            continue
        
        m = re.match(r'\[GRAV_APPLY\] velY: .* = (0x[0-9A-Fa-f]+)', line)
        if m:
            sim_frames[frame_idx]['grav_velY'] = m.group(1)
            continue
        
        m = re.match(r'\[PHYSICS\] Mode=\d+, gravity=\d+, mini=(\d+)', line)
        if m:
            sim_frames[frame_idx]['mini'] = int(m.group(1))
            continue
        
        m = re.match(r'\[(EJECT|PAD_HIT|JUMP|ORB|SPRITE|PORTAL)', line)
        if m:
            sim_frames[frame_idx]['events'].append(line[:120])
        m = re.match(r'\[PF\] Frame.*JUMP', line)
        if m:
            sim_frames[frame_idx]['events'].append(line[:120])

# Find first PF frame with mini=True
pf_mini_start = None
for f in sorted(pf_frames.keys()):
    if pf_frames[f].get('mini') == True:
        pf_mini_start = f
        break

# Find first SIM frame with mini=1
sim_mini_start = None
for f in sorted(sim_frames.keys()):
    if sim_frames[f].get('mini') == 1:
        sim_mini_start = f
        break

print(f"PF mini starts at frame {pf_mini_start}")
print(f"SIM mini starts at frame index {sim_mini_start}")

if pf_mini_start is None or sim_mini_start is None:
    print("Could not find mini start in one or both logs")
    sys.exit(1)

# The PF frame numbers and SIM frame indices should match.
# Let's check alignment by comparing X positions around mini start.
# First verify the frames before mini match
for offset in range(-5, 1):
    pf_f = pf_mini_start + offset
    sim_f = pf_f  # assume they're aligned
    if sim_f in sim_frames and pf_f in pf_frames:
        pf_y = pf_frames[pf_f].get('eject_posY', pf_frames[pf_f].get('grav_posY', '?'))
        sim_y = sim_frames[sim_f]['y_px']  # start-of-frame = end of previous
        pf_velY = pf_frames[pf_f].get('velY_before', '?')
        sim_velY = sim_frames[sim_f]['velY']
        print(f"  f={pf_f}: PF_Y={pf_y} velY={pf_velY} | SIM_Y={sim_y} velY={sim_velY} mini_sim={sim_frames[sim_f].get('mini')}")

# If SIM and PF frame indices differ, adjust
# Let's try to find the alignment by matching the X position or event timing
# For now, assume PF f=N == SIM frame_idx N

print(f"\n{'='*80}")
print(f"COMPARING AFTER MINI PORTAL (starting from PF frame {pf_mini_start - 5})")
print(f"{'='*80}")

# Compare frame-by-frame after mini portal
diverge_count = 0
for f in range(pf_mini_start - 5, min(max(pf_frames.keys()), max(sim_frames.keys())) + 1):
    if f not in pf_frames or f not in sim_frames:
        continue
    
    pf_data = pf_frames[f]
    sim_data = sim_frames[f]
    
    # PF final Y for this frame
    pf_final_y = pf_data.get('eject_posY', pf_data.get('grav_posY'))
    # PF Y at start of this frame (= end of previous, or use grav input posY)
    pf_velY = pf_data.get('velY_after', '?')
    
    # SIM Y after gravity
    sim_grav_y = sim_data.get('grav_posY')
    sim_velY = sim_data.get('grav_velY', '?')
    
    # SIM start-of-frame Y (= end of previous frame after eject)
    sim_start_y = sim_data['y_px']
    sim_start_velY = sim_data['velY']
    
    # Compare grav posY (before eject)
    pf_grav_y = pf_data.get('grav_posY')
    
    # Compare start-of-next-frame Y
    if f + 1 in sim_frames:
        sim_next_y = sim_frames[f + 1]['y_px']
    else:
        sim_next_y = None
    
    if f + 1 in pf_frames:
        pf_next_start_velY = pf_frames[f + 1].get('velY_before', '?')
    else:
        pf_next_start_velY = '?'
    
    # Check divergence in grav_posY
    if pf_grav_y is not None and sim_grav_y is not None and pf_grav_y != sim_grav_y:
        diverge_count += 1
        if diverge_count <= 60:
            mini_pf = 'M' if pf_data.get('mini') == True else ' '
            mini_sim = 'M' if sim_data.get('mini') == 1 else ' '
            print(f"\nf={f} [{mini_pf}|{mini_sim}] GRAV_Y: PF={pf_grav_y}({pf_data.get('grav_posY_hex','?')}) SIM={sim_grav_y}({sim_data.get('grav_posY_hex','?')}) diff={pf_grav_y - sim_grav_y}")
            print(f"  velY_after: PF={pf_velY} SIM={sim_velY}")
            print(f"  start_velY: PF={pf_data.get('velY_before','?')} SIM={sim_start_velY}")
            print(f"  start_Y: PF=? SIM={sim_start_y}")
            if pf_data['events']:
                for e in pf_data['events'][:3]:
                    print(f"  PF: {e[:120]}")
            if sim_data['events']:
                for e in sim_data['events'][:3]:
                    print(f"  SIM: {e[:120]}")
    elif pf_grav_y is not None and sim_grav_y is not None and pf_grav_y == sim_grav_y and diverge_count > 0:
        # Check if velocities also match
        if pf_velY == sim_velY:
            if diverge_count > 0:
                print(f"\nf={f} CONVERGED: Y={pf_grav_y}, velY={pf_velY}")
                diverge_count = 0
    
    # Also check final Y (after eject) vs next frame start
    pf_final = pf_data.get('eject_posY', pf_data.get('grav_posY'))
    if pf_final is not None and sim_next_y is not None and pf_final != sim_next_y:
        if diverge_count == 0:  # only report if not already diverging on grav
            diverge_count += 1
            if diverge_count <= 60:
                print(f"\nf={f} EJECT_Y: PF_final={pf_final} SIM_next_start={sim_next_y} diff={pf_final - sim_next_y}")
                if pf_data['events']:
                    for e in pf_data['events'][:3]:
                        print(f"  PF: {e[:120]}")
                if sim_data['events']:
                    for e in sim_data['events'][:3]:
                        print(f"  SIM: {e[:120]}")

print(f"\nTotal divergent frames: {diverge_count}")

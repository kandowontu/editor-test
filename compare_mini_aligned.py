"""Compare PF vs SIM logs aligned by X position, starting after mini portal."""
import re, os

pf_path = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260310_181331.txt')
sim_path = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260310_182928.txt')

ts_re = re.compile(r'^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z ')

# Parse PF: each line starting with [PF f=N] is one physics step
# We need X position - but PF doesn't log X directly. PF logs posY.
# PF X is deterministic: X += VelX each frame. VelX = speed_fixed.
# For 1x speed in Clubstep, VelX_fixed = 0x2C4.
# Let's just extract frame, velY, posY and match by frame-level state.

# Actually: the logs SHOULD be aligned by step count since both do 1 step per frame
# at 1x speed. The SIM has 2102 GRAV_POS vs 1047 STEP_START. Let me check what 
# causes the extra GRAV_POS events.

# Parse SIM with full detail per STEP_START frame
sim_frames = []  # list of dicts, one per rendering frame
cur = None
with open(sim_path) as f:
    for line in f:
        line = ts_re.sub('', line.strip())
        m = re.match(r'\[STEP_START\] playerX_fixed=(0x[0-9A-Fa-f]+) \((\d+)px\), playerY_fixed=(0x[0-9A-Fa-f]+) \((\d+)px\), playerVelY_fixed=(0x[0-9A-Fa-f]+)', line)
        if m:
            if cur is not None:
                sim_frames.append(cur)
            cur = {
                'x_fixed': m.group(1), 'x_px': int(m.group(2)),
                'y_fixed': m.group(3), 'y_px': int(m.group(4)),
                'velY': m.group(5),
                'substeps': [],  # each substep: (velY_after, posY_px)
                'events': [],
                'mini': False,
            }
            continue
        if cur is None:
            continue
        m = re.match(r'\[GRAV_APPLY\] velY: .* = (0x[0-9A-Fa-f]+)', line)
        if m:
            cur['_last_velY'] = m.group(1)
            continue
        m = re.match(r'\[GRAV_POS\] posY: (0x[0-9A-Fa-f]+) \((\d+)px\).*= (0x[0-9A-Fa-f]+) \((\d+)px\)', line)
        if m:
            cur['substeps'].append({
                'posY_start': int(m.group(2)),
                'posY_end': int(m.group(4)),
                'posY_end_hex': m.group(3),
                'velY_after': cur.get('_last_velY', '?'),
            })
            continue
        if re.match(r'\[PHYSICS\].*mini=1', line):
            cur['mini'] = True
        if re.match(r'\[PF\] Frame.*JUMP', line):
            cur['events'].append(line[:120])
        m = re.match(r'\[(EJECT|PAD_HIT|PAD_MULTI|JUMP)', line)
        if m:
            cur['events'].append(line[:120])
    if cur:
        sim_frames.append(cur)

# Parse PF: one step per frame
pf_frames = []  # list of dicts
with open(pf_path) as f:
    cur_pf = None
    for line in f:
        line = line.strip()
        m = re.match(r'\[PF f=(\d+)\] \[CUBE_GRAV\] velY: (0x[0-9A-Fa-f]+) \+ (0x[0-9A-Fa-f]+) = (0x[0-9A-Fa-f]+), posY: (0x[0-9A-Fa-f]+) \((\d+)px\) -> (0x[0-9A-Fa-f]+) \((\d+)px\)', line)
        if m:
            if cur_pf is not None:
                pf_frames.append(cur_pf)
            cur_pf = {
                'frame': int(m.group(1)),
                'velY_before': m.group(2),
                'velY_after': m.group(4),
                'posY_start': int(m.group(6)),
                'posY_start_hex': m.group(5),
                'posY_end': int(m.group(8)),
                'posY_end_hex': m.group(7),
                'eject_posY': None,
                'events': [],
                'mini': False,
            }
            continue
        if cur_pf is None:
            continue
        m = re.match(r'\[PF f=\d+\] \[EJECT\] .* -> (\d+)', line)
        if m:
            cur_pf['eject_posY'] = int(m.group(1))
            continue
        m = re.match(r'\[PF f=\d+\] \[([A-Z_]+)\](.*)', line)
        if m and m.group(1) != 'CUBE_GRAV':
            event = f'[{m.group(1)}]{m.group(2)}'
            cur_pf['events'].append(event)
            if 'mini=True' in event:
                cur_pf['mini'] = True
    if cur_pf:
        pf_frames.append(cur_pf)

print(f"PF frames (physics steps): {len(pf_frames)}")
print(f"SIM rendering frames: {len(sim_frames)}")
total_sim_substeps = sum(len(f['substeps']) for f in sim_frames)
print(f"SIM total physics substeps: {total_sim_substeps}")

# The SIM runs ~2 substeps per rendering frame (1x speed with its advancement logic).
# The PF runs 1 step per PF frame.
# So PF step N should match SIM substep N (flattened).

# Flatten SIM substeps
sim_substeps = []  # (render_frame_idx, substep_idx, velY_after, posY_end, posY_end_hex, mini, events)
for fi, frame in enumerate(sim_frames):
    for si, sub in enumerate(frame['substeps']):
        sim_substeps.append({
            'render_frame': fi,
            'substep': si,
            'velY_after': sub['velY_after'],
            'posY_start': sub['posY_start'],
            'posY_end': sub['posY_end'],
            'posY_end_hex': sub['posY_end_hex'],
            'mini': frame['mini'],
            'events': frame['events'] if si == 0 else [],
            'x_px': frame['x_px'],
        })

print(f"SIM flattened substeps: {len(sim_substeps)}")

# Find mini portal in PF
pf_mini_idx = None
for i, f in enumerate(pf_frames):
    if f['mini']:
        pf_mini_idx = i
        break

# Find mini portal in SIM substeps
sim_mini_idx = None
for i, s in enumerate(sim_substeps):
    if s['mini']:
        sim_mini_idx = i
        break

print(f"\nPF mini at step index {pf_mini_idx} (frame {pf_frames[pf_mini_idx]['frame'] if pf_mini_idx else '?'})")
print(f"SIM mini at substep index {sim_mini_idx} (render frame {sim_substeps[sim_mini_idx]['render_frame'] if sim_mini_idx else '?'})")

if pf_mini_idx is None or sim_mini_idx is None:
    print("ERROR: Could not find mini portal in one/both logs")
    exit(1)

# Since the SIM and PF should be processing the same level in the same order,
# align by the mini portal event and compare from there.
# PF step (pf_mini_idx + offset) <-> SIM substep (sim_mini_idx + offset)

print(f"\n{'='*80}")
print(f"COMPARISON ALIGNED AT MINI PORTAL")
print(f"{'='*80}")

# Show context: 5 steps before mini, 80 steps after
diverge_count = 0
for offset in range(-5, 120):
    pi = pf_mini_idx + offset
    si = sim_mini_idx + offset
    if pi < 0 or si < 0 or pi >= len(pf_frames) or si >= len(sim_substeps):
        continue
    
    pf = pf_frames[pi]
    sim = sim_substeps[si]
    
    pf_y = pf['eject_posY'] if pf['eject_posY'] is not None else pf['posY_end']
    sim_y = sim['posY_end']
    
    marker = ''
    if pf['posY_end'] != sim['posY_end']:
        marker = ' *** GRAV_Y DIVERGE'
        diverge_count += 1
    
    pf_final = pf['eject_posY'] if pf['eject_posY'] is not None else pf['posY_end']
    
    mini_pf = 'M' if pf.get('mini') else ' '
    mini_sim = 'M' if sim['mini'] else ' '
    
    # Always print around mini portal and divergences
    if abs(offset) <= 5 or marker or (diverge_count > 0 and diverge_count <= 3):
        print(f"  off={offset:+3d} PF_f={pf['frame']:4d} SIM_rf={sim['render_frame']:4d}.{sim['substep']} [{mini_pf}|{mini_sim}]"
              f"  gravY: PF={pf['posY_end']:3d} SIM={sim['posY_end']:3d}"
              f"  velY: PF={pf['velY_after']:>12s} SIM={sim['velY_after']:>12s}"
              f"  eject: PF={str(pf['eject_posY']):>4s}"
              f"{marker}")
        if pf['events']:
            for e in pf['events'][:2]:
                print(f"         PF: {e[:110]}")
        if sim['events']:
            for e in sim['events'][:2]:
                print(f"         SIM: {e[:110]}")
    
    if diverge_count > 50:
        print("  ... (too many divergences, stopping)")
        break

print(f"\nTotal divergent gravity steps (first 120 after mini): {diverge_count}")

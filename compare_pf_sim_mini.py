import re, sys, os

pf_path = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260310_181331.txt')
sim_path = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260310_182928.txt')

# Focus: only compare after mini portal activation

# Parse PF: extract per-frame posY after gravity, and after eject
# Format: [PF f=0] [CUBE_GRAV] velY: 0x0 + 0x6B = 0x6B, posY: 0x17100 (369px) -> 0x1716B (369px)
# Format: [PF f=0] [EJECT] floor land Y: 369 -> 369 (floorTop=384)
pf_grav = {}  # frame -> (velY_hex, posY_after_hex)
pf_eject = {}  # frame -> posY_after_eject
pf_jumps = {}  # frame -> velY
pf_events = {}  # frame -> list of event strings

with open(pf_path) as f:
    for line in f:
        line = line.strip()
        m = re.match(r'\[PF f=(\d+)\] \[CUBE_GRAV\] velY: (0x[0-9A-Fa-f]+) \+ (0x[0-9A-Fa-f]+) = (0x[0-9A-Fa-f]+), posY: (0x[0-9A-Fa-f]+) \((\d+)px\) -> (0x[0-9A-Fa-f]+) \((\d+)px\)', line)
        if m:
            f_num = int(m.group(1))
            pf_grav[f_num] = (m.group(4), m.group(7), int(m.group(8)))  # velY_result, posY_hex, posY_px
            continue
        m = re.match(r'\[PF f=(\d+)\] \[EJECT\] .* -> (\d+)', line)
        if m:
            f_num = int(m.group(1))
            pf_eject[f_num] = int(m.group(2))
            continue
        m = re.match(r'\[PF f=(\d+)\] \[JUMP\]', line)
        if m:
            f_num = int(m.group(1))
            pf_events.setdefault(f_num, []).append('JUMP')
            continue
        m = re.match(r'\[PF f=(\d+)\] \[(ORB_ACTIVATE|PAD|SPRITE|PORTAL|MINI|SIZE|GRAVITY|MODE)\]', line)
        if m:
            f_num = int(m.group(1))
            pf_events.setdefault(f_num, []).append(line[line.index('] [')+2:])
            continue
        # Catch any other tagged events
        m = re.match(r'\[PF f=(\d+)\] \[([A-Z_]+)\]', line)
        if m:
            f_num = int(m.group(1))
            tag = m.group(2)
            if tag not in ('CUBE_GRAV',):
                pf_events.setdefault(f_num, []).append(f'{tag}: {line[line.index("] [")+2:]}')

# Parse SIM: STEP_START has frame posY, GRAV_POS has posY after gravity  
# Format: [STEP_START] playerX_fixed=0x0000 (0px), playerY_fixed=0x17100 (369px), playerVelY_fixed=0x0000
sim_start = {}  # frame_index -> (x_px, y_hex, y_px, velY_hex)
sim_grav = {}   # frame_index -> (posY_hex, posY_px)
sim_events = {} # frame_index -> list of event strings

frame_idx = -1
with open(sim_path) as f:
    for line in f:
        line = line.strip()
        # Strip timestamp prefix
        if ' [' in line:
            tag_start = line.index(' [')
            content = line[tag_start+1:]
        else:
            continue
        
        m = re.match(r'\[STEP_START\] playerX_fixed=(0x[0-9A-Fa-f]+) \((\d+)px\), playerY_fixed=(0x[0-9A-Fa-f]+) \((\d+)px\), playerVelY_fixed=(0x[0-9A-Fa-f]+)', content)
        if m:
            frame_idx += 1
            sim_start[frame_idx] = (int(m.group(2)), m.group(3), int(m.group(4)), m.group(5))
            continue
        
        m = re.match(r'\[GRAV_POS\] posY: (0x[0-9A-Fa-f]+) \((\d+)px\) \+ .* = (0x[0-9A-Fa-f]+) \((\d+)px\)', content)
        if m:
            sim_grav[frame_idx] = (m.group(3), int(m.group(4)))
            continue
        
        # Capture events
        m = re.match(r'\[(JUMP|PAD_HIT|ORB|SPRITE|PORTAL|MINI|GRAVITY|EJECT)', content)
        if m:
            sim_events.setdefault(frame_idx, []).append(content[:80])
            continue
        m = re.match(r'\[(EJECT)', content)
        if m:
            sim_events.setdefault(frame_idx, []).append(content[:120])

# Now compare frame by frame
# PF frames are numbered explicitly, SIM frames are sequential
# We need to match them. PF f=0 corresponds to SIM frame_idx=0

max_frame = min(max(pf_grav.keys()) if pf_grav else 0, max(sim_start.keys()) if sim_start else 0)
print(f"PF frames: {len(pf_grav)}, SIM frames: {len(sim_start)}, comparing up to frame {max_frame}")

diverged = False
diverge_count = 0
for f in range(max_frame + 1):
    pf_y = None
    sim_y = None
    
    # PF final Y = eject Y if available, else grav Y
    if f in pf_eject:
        pf_y = pf_eject[f]
    elif f in pf_grav:
        pf_y = pf_grav[f][2]  # posY_px
    
    # SIM final Y from STEP_START of NEXT frame, or from GRAV_POS
    if f + 1 in sim_start:
        sim_y = sim_start[f + 1][2]  # y_px at start of next frame = end of this frame
    elif f in sim_grav:
        sim_y = sim_grav[f][1]
    
    if pf_y is not None and sim_y is not None and pf_y != sim_y:
        if not diverged or diverge_count < 30:
            pf_vel = pf_grav[f][0] if f in pf_grav else '?'
            sim_vel = sim_start[f][3] if f in sim_start else '?'
            pf_ev = pf_events.get(f, [])
            sim_ev = sim_events.get(f, [])
            print(f"\n*** DIVERGE f={f}: PF_Y={pf_y} SIM_Y={sim_y} (diff={pf_y - sim_y})")
            print(f"    PF velY={pf_vel}, SIM velY={sim_vel}")
            if pf_ev:
                for e in pf_ev[:3]:
                    print(f"    PF event: {e[:100]}")
            if sim_ev:
                for e in sim_ev[:3]:
                    print(f"    SIM event: {e[:100]}")
            diverged = True
            diverge_count += 1
    elif pf_y is not None and sim_y is not None and pf_y == sim_y and diverged:
        print(f"\n*** CONVERGE f={f}: PF_Y={pf_y} SIM_Y={sim_y}")
        diverged = False

# Also show mini portal activation
print("\n--- PF mini-related events ---")
for f in sorted(pf_events.keys()):
    for e in pf_events[f]:
        if 'mini' in e.lower() or 'MINI' in e or 'SIZE' in e or 'SHRINK' in e or 'GROW' in e:
            print(f"  f={f}: {e[:120]}")

print("\n--- SIM mini-related events ---")
for f in sorted(sim_events.keys()):
    for e in sim_events[f]:
        if 'mini' in e.lower() or 'MINI' in e or 'SIZE' in e or 'SHRINK' in e or 'GROW' in e:
            print(f"  f={f}: {e[:120]}")

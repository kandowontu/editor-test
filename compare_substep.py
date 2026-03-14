import re, os

pf_path = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260310_181331.txt')
sim_path = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260310_182928.txt')

# PF: each frame = one physics step. Extract grav results sequentially.
pf_steps = []  # list of (frame, velY_after_hex, posY_after_hex, posY_px, eject_posY, events)
with open(pf_path) as f:
    cur_frame = None
    cur_velY = None
    cur_posY = None
    cur_posY_px = None
    cur_eject = None
    cur_events = []
    for line in f:
        line = line.strip()
        m = re.match(r'\[PF f=(\d+)\] \[CUBE_GRAV\] velY: (0x[0-9A-Fa-f]+) \+ (0x[0-9A-Fa-f]+) = (0x[0-9A-Fa-f]+), posY: (0x[0-9A-Fa-f]+) \((\d+)px\) -> (0x[0-9A-Fa-f]+) \((\d+)px\)', line)
        if m:
            # Save previous if exists
            if cur_frame is not None:
                pf_steps.append((cur_frame, cur_velY, cur_posY, cur_posY_px, cur_eject, cur_events))
            cur_frame = int(m.group(1))
            cur_velY = m.group(4)
            cur_posY = m.group(7)
            cur_posY_px = int(m.group(8))
            cur_eject = None
            cur_events = []
            continue
        m = re.match(r'\[PF f=(\d+)\] \[EJECT\] .* -> (\d+)', line)
        if m:
            cur_eject = int(m.group(2))
            continue
        m = re.match(r'\[PF f=(\d+)\] \[([A-Z_]+)\](.*)', line)
        if m and m.group(2) != 'CUBE_GRAV':
            cur_events.append(f'[{m.group(2)}]{m.group(3)}')
    if cur_frame is not None:
        pf_steps.append((cur_frame, cur_velY, cur_posY, cur_posY_px, cur_eject, cur_events))

# SIM: each GRAV_POS = one physics sub-step. Extract all sequentially.
sim_steps = []  # list of (sim_frame, substep, velY_after_hex, posY_after_hex, posY_px, events)
ts_re = re.compile(r'^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z ')
sim_frame = -1
substep = 0
cur_events_sim = []
with open(sim_path) as f:
    for line in f:
        line = ts_re.sub('', line.strip())
        if '[STEP_START]' in line:
            sim_frame += 1
            substep = 0
            cur_events_sim = []
            continue
        m = re.match(r'\[GRAV_POS\] posY: .* = (0x[0-9A-Fa-f]+) \((\d+)px\)', line)
        if m:
            posY_hex = m.group(1)
            posY_px = int(m.group(2))
            # Get the velY from the GRAV_APPLY that should precede this
            sim_steps.append((sim_frame, substep, None, posY_hex, posY_px, list(cur_events_sim)))
            substep += 1
            cur_events_sim = []
            continue
        m = re.match(r'\[GRAV_APPLY\] velY: .* = (0x[0-9A-Fa-f]+)', line)
        if m:
            # Store for the next GRAV_POS
            cur_velY_sim = m.group(1)
            if len(sim_steps) >= 0:
                # Will be picked up at next GRAV_POS
                pass
            continue
        m = re.match(r'\[(EJECT|PAD_HIT|JUMP|PF\]|SPRITE|PORTAL)', line)
        if m:
            cur_events_sim.append(line[:120])

# Reparse SIM to capture velY properly
sim_steps2 = []
sim_frame = -1
substep = 0
cur_velY_sim = None
cur_events_sim = []
with open(sim_path) as f:
    for line in f:
        line = ts_re.sub('', line.strip())
        if '[STEP_START]' in line:
            sim_frame += 1
            substep = 0
            cur_events_sim = []
            # Extract start velY
            m2 = re.search(r'playerVelY_fixed=(0x[0-9A-Fa-f]+)', line)
            if m2:
                cur_velY_sim = m2.group(1)
            continue
        m = re.match(r'\[GRAV_APPLY\] velY: .* = (0x[0-9A-Fa-f]+)', line)
        if m:
            cur_velY_sim = m.group(1)
            continue
        m = re.match(r'\[GRAV_POS\] posY: (0x[0-9A-Fa-f]+) \((\d+)px\) .* (0x[0-9A-Fa-f]+) \((\d+)px\)', line)
        if m:
            posY_start_hex = m.group(1)
            posY_start_px = int(m.group(2))
            posY_end_hex = m.group(3)
            posY_end_px = int(m.group(4))
            sim_steps2.append((sim_frame, substep, cur_velY_sim, posY_start_px, posY_end_hex, posY_end_px, list(cur_events_sim)))
            substep += 1
            cur_events_sim = []
            continue
        m = re.match(r'\[(EJECT|PAD_HIT|JUMP|PF\]|SPRITE|PORTAL)', line)
        if m:
            cur_events_sim.append(line[:120])

print(f"PF steps: {len(pf_steps)}")
print(f"SIM sub-steps: {len(sim_steps2)}")

# Now compare PF step i with SIM sub-step i
# They should match 1:1 since each is one physics gravity step
min_steps = min(len(pf_steps), len(sim_steps2))

# Find first mini step in PF
pf_mini_step = None
for i, (f, velY, posY, posY_px, eject, events) in enumerate(pf_steps):
    for e in events:
        if 'mini=True' in e:
            pf_mini_step = i
            break
    if pf_mini_step is not None:
        break

# Find first mini step in SIM (look for the PHYSICS Mode=0, gravity=00, mini=1 in events would require different parsing)
# For now just report the step where they diverge

print(f"\nPF first mini=True at step index {pf_mini_step} (PF frame {pf_steps[pf_mini_step][0] if pf_mini_step else '?'})")

# Compare starting from the beginning to find FIRST divergence
start = 0
diverge_count = 0
last_match = start

for i in range(start, min_steps):
    pf_f, pf_velY, pf_posY, pf_posY_px, pf_eject, pf_events = pf_steps[i]
    sim_f, sim_sub, sim_velY, sim_posY_start_px, sim_posY_hex, sim_posY_px, sim_events = sim_steps2[i]
    
    # Compare grav posY
    if pf_posY_px != sim_posY_px:
        diverge_count += 1
        if diverge_count <= 40:
            print(f"\nSTEP {i}: PF_f={pf_f} SIM_f={sim_f}.{sim_sub}")
            print(f"  GRAV_Y: PF={pf_posY_px} SIM={sim_posY_px} diff={pf_posY_px - sim_posY_px}")
            print(f"  velY: PF={pf_velY} SIM={sim_velY}")
            if pf_events:
                for e in pf_events[:3]:
                    print(f"  PF: {e[:120]}")
            if sim_events:
                for e in sim_events[:3]:
                    print(f"  SIM: {e[:120]}")
    else:
        if diverge_count > 0:
            print(f"\nSTEP {i}: MATCHED PF_f={pf_f} SIM_f={sim_f}.{sim_sub} Y={pf_posY_px} velY_PF={pf_velY} velY_SIM={sim_velY}")
            if pf_velY == sim_velY:
                print("  (fully converged)")
                diverge_count = 0
            else:
                print("  (Y matches but velY differs)")
        last_match = i
    
    # Also compare eject
    # Need to compare PF eject_posY with SIM's next step's start posY
    # This is complex so skip for now

print(f"\nTotal divergent sub-steps up to step {min_steps}: {diverge_count}")

# Also show where the speed changes (SIM frame has >1 substep)
print("\n--- Speed changes (SIM frames with >1 substep) ---")
frame_substeps = {}
for sim_f, sim_sub, *_ in sim_steps2:
    frame_substeps[sim_f] = max(frame_substeps.get(sim_f, 0), sim_sub + 1)
changes = []
prev = 1
for f in sorted(frame_substeps.keys()):
    n = frame_substeps[f]
    if n != prev:
        changes.append((f, prev, n))
        prev = n
for f, old, new in changes[:20]:
    print(f"  SIM frame {f}: {old} -> {new} substeps")

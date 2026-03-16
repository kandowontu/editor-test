import re, sys, os

temp = os.environ.get('TEMP', '')
sim_log = os.path.join(temp, 'famidash_sim_debug_20260315_235305.txt')
pf_log = os.path.join(temp, 'famidash_pf_debug_20260315_234518.txt')

def extract_frames(path, prefix):
    """Extract per-frame Y positions."""
    frames = {}
    frame_re = re.compile(rf'\[{prefix} f=(\d+)\]')
    # For SIM, look for GRAV_POS which has post-gravity posY
    # For PF, look for frame entries with Y info
    if prefix == 'SIM':
        pos_re = re.compile(r'\[GRAV_POS\].*?->\s*0x[0-9A-Fa-f]+\s*\((\d+)px\)')
    else:
        pos_re = None
    
    with open(path, 'r', errors='replace') as f:
        cur_frame = None
        for line in f:
            m = frame_re.search(line)
            if m:
                cur_frame = int(m.group(1))
            if cur_frame is not None:
                if prefix == 'SIM' and '[GRAV_POS]' in line:
                    m2 = re.search(r'->\s*0x[0-9A-Fa-f]+\s*\((\d+)px\)', line)
                    if m2:
                        frames[cur_frame] = int(m2.group(1))
    return frames

def extract_pf_y(path):
    """Extract PF per-frame Y from the frame lines."""
    frames = {}
    with open(path, 'r', errors='replace') as f:
        for line in f:
            m = re.match(r'\[PF f=(\d+)\]\s+X=\d+\s+Y=(\d+)', line)
            if m:
                frames[int(m.group(1))] = int(m.group(2))
    return frames

print("Extracting SIM Y values...")
sim_y = extract_frames(sim_log, 'SIM')
print(f"  Got {len(sim_y)} SIM frames")

print("Extracting PF Y values...")
pf_y = extract_pf_y(pf_log)
print(f"  Got {len(pf_y)} PF frames")

# Find first divergence
common = sorted(set(sim_y.keys()) & set(pf_y.keys()))
print(f"\nCommon frames: {len(common)}")

diverge_frame = None
for f in common:
    if sim_y[f] != pf_y[f]:
        if diverge_frame is None:
            diverge_frame = f
        if f <= (diverge_frame + 30 if diverge_frame else f + 30):
            print(f"  f={f}: SIM Y={sim_y[f]}, PF Y={pf_y[f]}, diff={sim_y[f]-pf_y[f]}")

if diverge_frame:
    print(f"\nFirst divergence at frame {diverge_frame}")
    # Show context around divergence
    start = diverge_frame - 5
    end = diverge_frame + 20
    print(f"\nContext frames {start}-{end}:")
    for f in range(start, end+1):
        sy = sim_y.get(f, '?')
        py = pf_y.get(f, '?')
        marker = " <-- DIVERGE" if sy != '?' and py != '?' and sy != py else ""
        print(f"  f={f}: SIM={sy}, PF={py}{marker}")
else:
    print("No divergence found in Y values!")

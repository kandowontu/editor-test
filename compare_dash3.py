"""Compare latest SIM vs PF logs for Dash level divergence."""
import re, sys

sim_path = r'C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260406_035655.txt'
pf_path  = r'C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260406_034144.txt'

# Extract SIM trajectory from STEP_START lines
sim_frames = []
with open(sim_path, 'r', errors='replace') as f:
    for line in f:
        m = re.search(r'\[STEP_START\] playerX_fixed=0x([0-9A-Fa-f]+) \((\d+)px\), playerY_fixed=0x([0-9A-Fa-f]+) \((\d+)px\)', line)
        if m:
            sim_frames.append({
                'x_fixed': int(m.group(1), 16),
                'x_px': int(m.group(2)),
                'y_fixed': int(m.group(3), 16),
                'y_px': int(m.group(4)),
            })

# Extract PF trajectory from REPLAY lines
pf_frames = []
with open(pf_path, 'r', errors='replace') as f:
    for line in f:
        m = re.search(r'\[REPLAY f=(\d+)\] X=0x([0-9A-Fa-f]+) \((\d+)px\) Y=0x([0-9A-Fa-f]+) \((\d+)px\) velY=0x([0-9A-Fa-f-]+) mode=(\d+) grav=([0-9A-Fa-f]+) mini=(\d+) inp=(\d+)', line)
        if m:
            pf_frames.append({
                'frame': int(m.group(1)),
                'x_fixed': int(m.group(2), 16),
                'x_px': int(m.group(3)),
                'y_fixed': int(m.group(4), 16),
                'y_px': int(m.group(5)),
                'mode': int(m.group(7)),
                'grav': m.group(8),
                'mini': int(m.group(9)),
                'inp': int(m.group(10)),
            })

print(f"SIM frames: {len(sim_frames)}, PF frames: {len(pf_frames)}")

# Compare frame by frame
n = min(len(sim_frames), len(pf_frames))
divergences = 0
first_div = None
for i in range(n):
    s = sim_frames[i]
    p = pf_frames[i]
    dx = s['x_px'] - p['x_px']
    dy = s['y_px'] - p['y_px']
    if dx != 0 or dy != 0:
        if first_div is None:
            first_div = i
        divergences += 1
        if divergences <= 20:
            print(f"  Frame {i}: SIM X={s['x_px']} Y={s['y_px']} | PF X={p['x_px']} Y={p['y_px']} | dX={dx:+d} dY={dy:+d} mode={p['mode']} grav={p['grav']} mini={p['mini']}")

if first_div is not None:
    print(f"\nFirst divergence at frame {first_div} (total: {divergences}/{n})")
    
    # Show context around first divergence
    print(f"\n=== Context around frame {first_div} ===")
    for i in range(max(0, first_div-3), min(n, first_div+5)):
        s = sim_frames[i]
        p = pf_frames[i]
        dx = s['x_px'] - p['x_px']
        dy = s['y_px'] - p['y_px']
        marker = " <<<" if dx != 0 or dy != 0 else ""
        print(f"  f{i}: SIM(X={s['x_px']},Y={s['y_px']}) PF(X={p['x_px']},Y={p['y_px']}) dX={dx:+d} dY={dy:+d} mode={p['mode']}{marker}")
    
    # Check X delta between consecutive frames around divergence
    print(f"\n=== X deltas around frame {first_div} ===")
    for i in range(max(1, first_div-3), min(n, first_div+5)):
        s_dx = sim_frames[i]['x_px'] - sim_frames[i-1]['x_px']
        p_dx = pf_frames[i]['x_px'] - pf_frames[i-1]['x_px']
        marker = " <<<" if s_dx != p_dx else ""
        print(f"  f{i}: SIM dX={s_dx} PF dX={p_dx}{marker}")
        
    # Check X_fixed deltas around divergence
    print(f"\n=== X_fixed deltas around frame {first_div} ===")
    for i in range(max(1, first_div-3), min(n, first_div+5)):
        s_dxf = sim_frames[i]['x_fixed'] - sim_frames[i-1]['x_fixed']
        p_dxf = pf_frames[i]['x_fixed'] - pf_frames[i-1]['x_fixed']
        marker = " <<<" if s_dxf != p_dxf else ""
        print(f"  f{i}: SIM dXf=0x{s_dxf:X} ({s_dxf}) PF dXf=0x{p_dxf:X} ({p_dxf}){marker}")
else:
    print("No divergences found!")

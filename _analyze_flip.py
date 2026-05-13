import csv, os

temp = os.environ['TEMP']
pf_file = os.path.join(temp, 'famidash_pf_trace.csv')
mesen_file = os.path.join(temp, 'famidash_mesen_trace.csv')

# Load PF trace
pf = {}
with open(pf_file, 'r') as f:
    lines = f.readlines()
    if lines:
        hdr = lines[0].strip().split(',')
        for line in lines[1:]:
            parts = line.strip().split(',')
            if not parts or not parts[0]:
                continue
            try:
                d = {hdr[i]: parts[i] for i in range(min(len(hdr), len(parts)))}
                frame = int(d.get('frame', -1))
                if frame > 0:
                    pf[frame] = d
            except:
                pass

# Load Mesen trace
mesen = {}
with open(mesen_file, 'r') as f:
    lines = f.readlines()
    if len(lines) > 1:
        hdr = lines[1].strip().split(',')
        for line in lines[2:]:
            parts = line.strip().split(',')
            if not parts or not parts[0]:
                continue
            try:
                d = {hdr[i]: parts[i] for i in range(min(len(hdr), len(parts)))}
                s = int(d.get('sim_cursor', -1))
                if s > 0:
                    mesen[s] = d
            except:
                pass

print("=" * 140)
print("DETAILED FRAME 4290-4296 ANALYSIS (Looking for gravity flip trigger)")
print("=" * 140)
print("Frame | PF_gravFlipped | PF_input | PF_py | Mesen_py | Mesen_cp_grav | Mesen_input | Mesen_gamemode | Delta_py")
print("-" * 140)

for f in range(4290, 4297):
    pf_row = pf.get(f)
    m_row = mesen.get(f)
    
    pf_grav = "?" if not pf_row else str(pf_row.get('gravFlipped', '?'))
    pf_input = "?" if not pf_row else str(pf_row.get('input', '?'))
    pf_py = "?" if not pf_row else str(pf_row.get('Y_px', '?'))
    pf_mode = "?" if not pf_row else str(pf_row.get('mode', '?'))
    
    m_py = "?" if not m_row else str(m_row.get('py', '?'))
    m_grav = "?" if not m_row else str(m_row.get('cp_gravity', '?'))
    m_input = "?" if not m_row else str(m_row.get('a_cur', '?'))
    m_mode = "?" if not m_row else str(m_row.get('gamemode', '?'))
    
    delta = "?"
    if pf_row and m_row:
        try:
            pf_y = int(pf_row.get('Y_px', 0))
            m_y = int(m_row.get('py', 0))
            delta = str(pf_y - m_y)
        except:
            pass
    
    print(f"{f:5d} | PF_grav={pf_grav:3s} mode={pf_mode:3s} | input={pf_input:3s} | Y={pf_py:6s} | {m_py:8s} | {m_grav:12s} | {m_input:11s} | mode={m_mode:14s} | {delta:8s}")

# Now look for what changed at frame 4292
print()
print("=" * 140)
print("WHAT'S DIFFERENT AT FRAME 4292 IN PF?")
print("=" * 140)

for frame in [4291, 4292]:
    pr = pf.get(frame)
    if pr:
        print(f"\nFrame {frame}:")
        for k in sorted(pr.keys()):
            if k not in ['X_fixed', 'Y_fixed', 'TgtCamY_px', 'Y_lowB', 'CamY_lowB', 'ScrollYSubpx']:
                print(f"  {k:25s} = {pr[k]}")

import csv, os

temp = os.environ['TEMP']
pf_file = os.path.join(temp, 'famidash_pf_trace.csv')
mesen_file = os.path.join(temp, 'famidash_mesen_trace.csv')

# Load last PF trace
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

# Load Mesen trace with proper header handling (skip 1st line, use 2nd as header)
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

print(f"PF trace: {len(pf)} frames (0-{max(pf.keys()) if pf else 0})")
print(f"Mesen trace: {len(mesen)} sims (1-{max(mesen.keys()) if mesen else 0})")
print()

# Compare window 4288-4300
print("=" * 100)
print("FRAME 4288-4300 COMPARISON (PF gravFlipped vs Mesen cp_gravity)")
print("=" * 100)
print("Frame | PF_gravFlipped | PF_py | Mesen_py | Mesen_cp_gravity | Mesen_input | Input_match | Delta_py")
print("-" * 100)

for f in range(4288, 4301):
    pf_row = pf.get(f)
    m_row = mesen.get(f)
    
    pf_grav = "?" if not pf_row else pf_row.get('gravFlipped', '?')
    pf_py = "?" if not pf_row else pf_row.get('Y_px', '?')
    
    m_py = "?" if not m_row else m_row.get('py', '?')
    m_grav = "?" if not m_row else m_row.get('cp_gravity', '?')
    m_input = "?" if not m_row else m_row.get('a_cur', '?')
    
    delta = "?"
    if pf_row and m_row:
        try:
            pf_y = int(pf_row.get('Y_px', 0))
            m_y = int(m_row.get('py', 0))
            delta = str(pf_y - m_y)
        except:
            pass
    
    match = "✓" if pf_row and m_row and pf_row.get('jump') == m_row.get('a_cur') else "✗"
    
    print(f"{f:5d} | {pf_grav:14s} | {pf_py:6s} | {m_py:8s} | {m_grav:16s} | {m_input:11s} | {match:11s} | {delta:8s}")

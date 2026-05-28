import csv

pf_path = r'C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_cataclysm_20260520_165934_832.csv'
mt_path = r'C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_cataclysm_20260520_171856_581.csv'

def load_pf(path):
    data = {}
    last_alive = 1
    fr = 0
    with open(path, 'r') as f:
        reader = csv.DictReader(f)
        for row in reader:
            if not row.get('frame'): continue
            fr = int(row['frame'])
            data[fr] = {
                'x': int(row['X_px']),
                'y': int(row['Y_px']),
                'alive': int(row['alive'])
            }
            last_alive = int(row['alive'])
    return data, fr, last_alive

def load_mt(path):
    data = {}
    last_death_pc = ""
    last_sim = 0
    with open(path, 'r', encoding='utf-8') as f:
        lines = f.readlines()
        
    start_idx = 0
    if 'nes_y_offset' in lines[0]:
        start_idx = 1
    
    reader = csv.DictReader(lines[start_idx:])
    for row in reader:
        if row.get('sim_cursor') is None: continue
        sim = int(row['sim_cursor'])
        data[sim] = {
            'x': int(row['px']),
            'y': int(row['py']),
            'death_pc': row.get('death_pc', '')
        }
        last_death_pc = row.get('death_pc', '')
        last_sim = sim
    return data, last_sim, last_death_pc

pf_data, pf_max_frame, pf_last_alive = load_pf(pf_path)
mt_data, mt_max_sim, mt_last_death = load_mt(mt_path)

print(f"PF max frame: {pf_max_frame}")
print(f"MT max sim_cursor: {mt_max_sim}")

divergence_frame = None
comparison_data = []

max_n = min(pf_max_frame, mt_max_sim - 1)

for n in range(max_n + 1):
    if n not in pf_data or (n+1) not in mt_data:
        continue
        
    pf_row = pf_data[n]
    mt_row = mt_data[n+1]
    
    pf_x, pf_y = pf_row['x'], pf_row['y']
    mt_x, mt_y = mt_row['x'], mt_row['y']
    if mt_x > 0x7FFFFFFF: mt_x -= 0x100000000
    if mt_y > 0x7FFFFFFF: mt_y -= 0x100000000
    
    # TOLERANCE Check: Only report if |diff| > 0
    if divergence_frame is None and (pf_x != mt_x or pf_y != mt_y):
        divergence_frame = n
    
    comparison_data.append({
        'n': n,
        'pf_x': pf_x, 'pf_y': pf_y,
        'mt_x': mt_x, 'mt_y': mt_y
    })

if divergence_frame is not None:
    print(f"Divergence found at frame {divergence_frame}")
    idx = -1
    for i, c in enumerate(comparison_data):
        if c['n'] == divergence_frame:
            idx = i
            break
    
    start = max(0, idx - 10)
    end = min(len(comparison_data), idx + 11)
    
    print(f"{'fr':>5} | {'PF X':>8} {'PF Y':>8} | {'MT X':>8} {'MT Y':>8} | {'Diff X':>5} {'Diff Y':>5}")
    for i in range(start, end):
        c = comparison_data[i]
        dx = c['pf_x'] - c['mt_x']
        dy = c['pf_y'] - c['mt_y']
        marker = "!!!" if c['n'] == divergence_frame else "   "
        print(f"{c['n']:5d} | {c['pf_x']:8d} {c['pf_y']:8d} | {c['mt_x']:8d} {c['mt_y']:8d} | {dx:5d} {dy:5d} {marker}")
else:
    print("No divergence found in the overlapping range.")

# Search for FIRST divergence where Y diff > 1 (if desired) or just stay with the first 1px diff.
# The prompt says "tolerate 0 px" - which usually means don't tolerate any difference.

print(f"PF Died: {pf_last_alive == 0}")
print(f"MT Died: {mt_last_death != '' and mt_last_death is not None}")

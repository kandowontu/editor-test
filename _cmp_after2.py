"""Find first PF/Mesen divergence in Y."""
import csv

pf_path = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_aftermath_20260518_040722_570.csv"
mes_path = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_aftermath_20260518_042452_804.csv"

pf_rows = list(csv.DictReader(open(pf_path)))

with open(mes_path) as f:
    lines = f.readlines()
header = lines[1].strip().split(',')
mes_by_sim = {}
for ln in lines[2:]:
    parts = ln.strip().split(',')
    if len(parts) < len(header): continue
    # death_ctx column may contain commas; we'll just parse first 22 columns by hand
    sim = int(parts[1])
    if sim in mes_by_sim: continue  # first occurrence
    mes_by_sim[sim] = {
        'px': int(parts[2]), 'py': int(parts[3]),
        'vy': int(parts[10]), 'mode': int(parts[14]),
        'death_pc': parts[21] if len(parts) > 21 else '',
    }

print(f"PF rows: {len(pf_rows)}  Mesen unique sims: {len(mes_by_sim)}")

# OFF=1: PF f corresponds to mesen sim (f+1)
# Find first Y divergence
first_div = None
for r in pf_rows:
    f = int(r['frame'])
    if f < 2: continue
    sc = f + 1
    if sc not in mes_by_sim: continue
    m = mes_by_sim[sc]
    pfy = int(r['Y_px'])
    pfx = int(r['X_px'])
    pfm = int(r['mode'])
    pfvy = int(r['VelY_fixed'], 16) if r['VelY_fixed'].startswith('0x') else int(r['VelY_fixed'])
    if pfvy >= 0x80000000: pfvy -= 0x100000000
    pfvy_s = pfvy >> 8  # high byte signed-ish... actually VelY_fixed is full fixed-point, mesen vel_y is 9-bit signed scaled
    if pfx != m['px'] or pfy != m['py'] or pfm != m['mode']:
        first_div = (f, sc, pfx, pfy, pfm, m)
        print(f"FIRST DIV  f={f} sim={sc}")
        print(f"  PF:  X={pfx} Y={pfy} mode={pfm} vy_fixed={pfvy}")
        print(f"  MES: X={m['px']} Y={m['py']} mode={m['mode']} vy={m['vy']}")
        break

# Show 5 frames before and after
if first_div:
    f0 = first_div[0]
    print("\n--- Context: 5 frames before/after ---")
    for r in pf_rows:
        f = int(r['frame'])
        if f < f0 - 5 or f > f0 + 5: continue
        sc = f + 1
        m = mes_by_sim.get(sc)
        if not m: continue
        pfvy = int(r['VelY_fixed'], 16) if r['VelY_fixed'].startswith('0x') else int(r['VelY_fixed'])
        if pfvy >= 0x80000000: pfvy -= 0x100000000
        marker = " <<<" if f == f0 else ""
        print(f"  f={f} sim={sc} PF(X={r['X_px']:>5} Y={r['Y_px']:>3} m={r['mode']} vy={pfvy:>6} inp={r['input']} grav={r['gravFlipped']}) MES(X={m['px']:>5} Y={m['py']:>3} m={m['mode']} vy={m['vy']:>6}){marker}")

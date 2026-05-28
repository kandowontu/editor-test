"""Compare PF vs Mesen aftermath: find first divergence and first death."""
import csv

pf_path = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_aftermath_20260518_040722_570.csv"
mes_path = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_aftermath_20260518_042452_804.csv"

# PF: frame,X_fixed,Y_fixed,VelY_fixed,input,alive,X_px,Y_px,onGround,CamY_px,...
pf_rows = []
with open(pf_path) as f:
    r = csv.DictReader(f)
    for row in r:
        pf_rows.append(row)

# Mesen: 2-row header
with open(mes_path) as f:
    lines = f.readlines()
header = lines[1].strip().split(',')
mes_rows = []
for ln in lines[2:]:
    parts = ln.strip().split(',')
    if len(parts) < len(header):
        continue
    d = dict(zip(header, parts))
    mes_rows.append(d)

print(f"PF rows: {len(pf_rows)}  Mesen rows: {len(mes_rows)}")

# Find first death in mesen
for r in mes_rows:
    if r.get('death_pc','0') not in ('0','','0x0','00'):
        sc = int(r['sim_cursor'])
        print(f"First mesen death: sim={sc} pc={r['death_pc']} ctx={r['death_ctx']} px={r['px']} py={r['py']} mode={r['gamemode']} grav={r['cp_gravity']}")
        break

# Find first PF death
for r in pf_rows:
    if r['alive'] == '0':
        print(f"First PF death: f={r['frame']} X={r['X_px']} Y={r['Y_px']}")
        break
else:
    print("No PF death.")

# OFF=1: PF f corresponds to mesen sim (f+1).  Look for first X or Y divergence in mode.
print("\nFirst divergence (px or py or mode or grav):")
for r in pf_rows:
    f = int(r['frame'])
    sc = f + 1
    if sc >= len(mes_rows): break
    m = mes_rows[sc] if sc < len(mes_rows) else None
    if m is None or m.get('sim_cursor') != str(sc): 
        # find matching
        m = next((x for x in mes_rows if int(x['sim_cursor'])==sc), None)
    if m is None: continue
    pfx, pfy = int(r['X_px']), int(r['Y_px'])
    mx, my = int(m['px']), int(m['py'])
    pfm = int(r['mode']); mm = int(m['gamemode'])
    pfg = 'FF' if r['gravFlipped']=='1' else '00'
    mg = m.get('cp_gravity','00')
    if pfx != mx or pfy != my or pfm != mm or (pfg != mg):
        print(f"f={f} sim={sc}  PF X={pfx} Y={pfy} m={pfm} g={pfg}  | MES X={mx} Y={my} m={mm} g={mg}")
        break

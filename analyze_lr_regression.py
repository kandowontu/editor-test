import csv, os
T = os.environ['TEMP']
# MES trace has nes_y_offset row first
with open(os.path.join(T,'famidash_mesen_trace.csv')) as f:
    lines = f.readlines()
hdr = lines[1].strip().split(',')
mes_rows = []
seen = set()
for ln in lines[2:]:
    parts = ln.rstrip('\n').split(',')
    if len(parts) < len(hdr): continue
    rec = dict(zip(hdr, parts))
    try:
        sim = int(rec['sim_cursor'])
    except: continue
    if sim in seen: continue
    seen.add(sim)
    mes_rows.append(rec)
print(f"MES unique sim rows: {len(mes_rows)}")

pf_rows = list(csv.DictReader(open(os.path.join(T,'famidash_pf_trace.csv'))))
print(f"PF rows: {len(pf_rows)}")
pf_map = {int(r['frame']): r for r in pf_rows}

# Mapping PF.f = sim - 1 (intro pre-roll in MES)
# Skip the pre-roll where MES px is fixed at 8
divs = 0
for m in mes_rows:
    sim = int(m['sim_cursor'])
    if sim < 14: continue  # pre-roll
    pf_f = sim - 1
    if pf_f not in pf_map: continue
    p = pf_map[pf_f]
    px, py = int(m['px']), int(m['py'])
    pX, pY = int(p['X_px']), int(p['Y_px'])
    if pX != px or pY != py:
        print(f"DIVERGE sim={sim} pf.f={pf_f} PF=({pX},{pY}) vy={p['VelY_fixed']} mode={p['mode']} gr={p['gravFlipped']} oG={p['onGround']} | MES=({px},{py}) vy={m['vel_y']} gm={m['gamemode']} a_cur={m['a_cur']} dpc={m['death_pc']}")
        divs += 1
        if divs >= 10: break
if divs == 0:
    print("NO DIVERGENCE up to MES end")
print(f"\nMES last sim={mes_rows[-1]['sim_cursor']} px={mes_rows[-1]['px']} py={mes_rows[-1]['py']} dpc={mes_rows[-1]['death_pc']}")

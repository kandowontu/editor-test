"""Diagnose UFO/cube state at funnygameholiday cube-portal divergence."""
import csv, os, sys

PF = os.path.expandvars(r"%TEMP%\famidash_pf_trace_funnygameholiday_20260521_002715_666.csv")
MT = os.path.expandvars(r"%TEMP%\famidash_mesen_trace_funnygameholiday_20260521_003233_485.csv")

def load_pf():
    rows = []
    with open(PF, newline='') as f:
        r = csv.DictReader(f)
        for row in r:
            rows.append(row)
    return rows

def load_mt():
    rows = []
    with open(MT, newline='') as f:
        first = f.readline()  # skip metadata
        header_line = f.readline().strip()
        headers = header_line.split(',')
        r = csv.DictReader(f, fieldnames=headers)
        for row in r:
            if row.get('sim_cursor') is None: continue
            rows.append(row)
    return rows

pf = load_pf()
mt = load_mt()

# Build sim_cursor -> mt row map (use first occurrence since sim_cursor repeats during phys-frame)
mt_by_sim = {}
for row in mt:
    k = int(row['sim_cursor'])
    if k not in mt_by_sim:
        mt_by_sim[k] = row

print("frame | PF X    Y   vy      mode mini gF subpx Y_low cam   | MT X    py  vel_y    mode mini cpg subpx scrolly")
for pfrow in pf:
    fr = int(pfrow['frame'])
    if fr < 3640 or fr > 3690: continue
    mtr = mt_by_sim.get(fr+1)  # sim_cursor is 1-indexed sometimes
    if not mtr:
        mtr = mt_by_sim.get(fr)
    mt_x = mtr['px'] if mtr else '?'
    mt_y = mtr['py'] if mtr else '?'
    mt_vy = mtr['vel_y'] if mtr else '?'
    mt_mode = mtr['gamemode'] if mtr else '?'
    mt_mini = mtr['mini'] if mtr else '?'
    mt_cpg = mtr['cp_gravity'] if mtr else '?'
    mt_sub = mtr['scroll_y_subpx'] if mtr else '?'
    mt_sc = mtr['scrolly'] if mtr else '?'
    vy = int(pfrow['VelY_fixed'], 16)
    if vy >= 0x80000000: vy -= 0x100000000
    print(f"{fr:5d} | {pfrow['X_px']:5s} {pfrow['Y_px']:3s} {vy:7d} {pfrow['mode']:4s} {pfrow['mini']:4s} {pfrow['gravFlipped']:2s} {pfrow['ScrollYSubpx']:5s} {pfrow['Y_lowB']:5s} {pfrow['CamY_fixed']:5s} | {mt_x:5s} {mt_y:3s} {mt_vy:8s} {mt_mode:4s} {mt_mini:4s} {mt_cpg:3s} {mt_sub:5s} {mt_sc:5s}")

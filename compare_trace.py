import csv, os
T = os.environ['TEMP']
mp = T + r'\famidash_mesen_trace_cataclysm_20260518_132239_775.csv'
pp = T + r'\famidash_pf_trace_cataclysm_20260518_124920_998.csv'

# Read Mesen: skip header line 1 (nes_y_offset,...), header is line 2
with open(mp) as f:
    f.readline()  # nes_y_offset
    r = csv.DictReader(f, restval='')
    mrows = [row for row in r if row.get('sim_cursor')]
with open(pp) as f:
    r = csv.DictReader(f)
    prows = list(r)

# Index mesen by sim_cursor (first occurrence)
m_by_sc = {}
for row in mrows:
    sc = int(row['sim_cursor'])
    if sc not in m_by_sc:
        m_by_sc[sc] = row

# For each PF frame, compare px/py/vel_y/gamemode/mini/gravity
print(f"{'frm':>4} {'PFpx':>5} {'Mpx':>5} {'PFpy':>4} {'Mpy':>4} {'PFvy':>7} {'Mvy':>6} {'PFm':>3} {'Mm':>3} {'PFg':>3} {'Mg':>3} {'PFmd':>3} {'Mmd':>3} {'death':>5}")
for prow in prows:
    fr = int(prow['frame'])
    if fr < 1100 or fr > 1135: continue
    mrow = m_by_sc.get(fr)
    if not mrow:
        print(f"{fr:>4}  no mesen")
        continue
    pf_vy = int(prow['VelY_fixed'], 16) if prow['VelY_fixed'].startswith('0x') else int(prow['VelY_fixed'])
    if pf_vy >= 0x80000000: pf_vy -= 0x100000000
    if pf_vy >= 0x8000: pf_vy -= 0x10000  # 16-bit signed for vel
    m_vy = int(mrow['vel_y'])
    death = mrow.get('death_pc','')
    print(f"{fr:>4} {prow['X_px']:>5} {mrow['px']:>5} {prow['Y_px']:>4} {mrow['py']:>4} {pf_vy:>7} {m_vy:>6} {prow['mini']:>3} {mrow['mini']:>3} {prow['gravFlipped']:>3} {('1' if mrow['cp_gravity']=='255' else '0'):>3} {prow['mode']:>3} {mrow['gamemode']:>3} {death:>5}")

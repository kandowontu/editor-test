import csv, os
T = os.environ['TEMP']
mp = T + r'\famidash_mesen_trace_cataclysm_20260518_132239_775.csv'
pp = T + r'\famidash_pf_trace_cataclysm_20260518_124920_998.csv'

mrows = []
with open(mp) as f:
    f.readline()
    r = csv.DictReader(f, restval='')
    mrows = [row for row in r if row.get('sim_cursor')]
prows = []
with open(pp) as f:
    r = csv.DictReader(f)
    prows = list(r)

m_by_sc = {}
for row in mrows:
    sc = int(row['sim_cursor'])
    if sc not in m_by_sc:
        m_by_sc[sc] = row

print('fr  PFx Mx  PFy My  PFvy   Mvy   PFi Macur Manxt')
for prow in prows:
    fr = int(prow['frame'])
    if fr < 1095 or fr > 1135:
        continue
    m = m_by_sc.get(fr + 1)  # shift: Mesen at sim_cursor=N+1 reflects input[N]
    if not m:
        continue
    pvy = int(prow['VelY_fixed'], 16) if prow['VelY_fixed'].startswith('0x') else int(prow['VelY_fixed'])
    if pvy >= 0x80000000: pvy -= 0x100000000
    if pvy >= 0x8000: pvy -= 0x10000
    print('%4d %4s %4s %4s %4s %7d %6s %3s %5s %5s' % (
        fr, prow['X_px'], m['px'], prow['Y_px'], m['py'], pvy, m['vel_y'], prow['input'], m['a_cur'], m['a_next']))

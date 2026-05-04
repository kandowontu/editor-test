import csv
fp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_mesen_trace.csv'
with open(fp, encoding='utf-8') as fh:
    rdr = csv.reader(fh)
    rows = list(rdr)
hdr=None
for r in rows:
    if r and r[0]=='rom_frame': hdr=r; break
idx={n:i for i,n in enumerate(hdr)}
last_sim = -1
stuck_at = None
for r in rows:
    if not r or not r[0].isdigit(): continue
    rf = int(r[0]); sc = int(r[idx['sim_cursor']])
    if sc < 20: continue  # skip preroll
    if sc == last_sim and stuck_at is None:
        stuck_at = (rf, sc); break
    last_sim = sc
print('first stuck:', stuck_at)
if not stuck_at:
    print('no stuck point - ROM ran full length')
    exit()
target = stuck_at[1]
for r in rows:
    if not r or not r[0].isdigit(): continue
    rf=int(r[0]); sc=int(r[idx['sim_cursor']])
    if abs(sc-target) <= 12:
        keys=['px','py','a_next','a_cur','vel_y','scrolly','gamemode','cube_data']
        print(f'rf={rf} sim={sc} ' + ' '.join(f'{k}={r[idx[k]]}' for k in keys))
    if sc > target+12: break

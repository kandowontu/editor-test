import csv
fp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_mesen_trace.csv'
with open(fp, encoding='utf-8') as fh:
    rdr = csv.reader(fh)
    rows = list(rdr)
hdr = next(r for r in rows if r and r[0]=='rom_frame')
idx = {n:i for i,n in enumerate(hdr)}
print('cols:', hdr)
keep = ['rom_frame','sim_cursor','px','py','vel_y','scrolly','table_idx','gravity_mod','dashing','gamemode','cube_data','collmap_r8','tgt_scroll_y']
last_sc = None
for r in rows:
    if not r or not r[0].isdigit(): continue
    sc = int(r[idx['sim_cursor']])
    if 920 < sc < 935:
        print(' '.join(f'{k}={r[idx[k]]}' for k in keep))

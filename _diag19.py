import csv
fp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_mesen_trace.csv'
with open(fp, encoding='utf-8') as fh:
    rdr = csv.reader(fh)
    rows = list(rdr)
hdr = next(r for r in rows if r and r[0]=='rom_frame')
idx = {n:i for i,n in enumerate(hdr)}
keep = ['rom_frame','sim_cursor','px','py','a_next','a_cur','vel_y','scrolly','scroll_y_subpx','tgt_scroll_y','table_idx','gravity_mod','dashing','gamemode','cube_data']
for r in rows:
    if not r or not r[0].isdigit(): continue
    sc = int(r[idx['sim_cursor']])
    if 10770 <= sc <= 10800:
        print(' '.join(f'{k}={r[idx[k]]}' for k in keep))

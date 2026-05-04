import csv
fp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_mesen_trace.csv'
with open(fp, encoding='utf-8') as fh:
    rdr = csv.reader(fh)
    rows = list(rdr)
hdr = None
for r in rows:
    if r and r[0]=='rom_frame':
        hdr=r; break
idx={n:i for i,n in enumerate(hdr)}
for r in rows:
    if not r or not r[0].isdigit(): continue
    rf=int(r[0])
    if 10780 <= rf <= 10800:
        sc = r[idx['sim_cursor']]
        an = r[idx['a_next']]
        ac = r[idx['a_cur']]
        py = r[idx['py']]
        vy = r[idx['vel_y']]
        sy = r[idx['scrolly']]
        print(f'rf={rf} sim={sc} a_next={an} a_cur={ac} py={py} vel_y={vy} scrolly={sy}')

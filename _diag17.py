import csv
fp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_mesen_trace.csv'
with open(fp, encoding='utf-8') as fh:
    rdr = csv.reader(fh)
    rows = list(rdr)
hdr = next(r for r in rows if r and r[0]=='rom_frame')
idx = {n:i for i,n in enumerate(hdr)}
# Find ALL stretches where sim_cursor stayed same for >5 consecutive frames
prev_sc = None
streak_start_rf = None
streak_len = 0
deaths = []
for r in rows:
    if not r or not r[0].isdigit(): continue
    rf = int(r[0]); sc = int(r[idx['sim_cursor']])
    if sc == prev_sc:
        streak_len += 1
    else:
        if streak_len >= 6 and prev_sc is not None:
            deaths.append((streak_start_rf, prev_sc, streak_len))
        prev_sc = sc
        streak_start_rf = rf
        streak_len = 1
if streak_len >= 6:
    deaths.append((streak_start_rf, prev_sc, streak_len))
print(f'Total stalls (>=6 frames):  {len(deaths)}')
for i, d in enumerate(deaths[:15]):
    print(f'  #{i}: rf~{d[0]} sc={d[1]} stalled {d[2]} frames')

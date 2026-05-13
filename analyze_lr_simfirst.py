import csv, os, io
T=os.environ['TEMP']
me=os.path.join(T,'famidash_mesen_trace.csv')
with open(me) as f: lines=f.readlines()
me_rows=list(csv.DictReader(io.StringIO(''.join(lines[1:]))))
# Find when sim_cursor first becomes K for K=1..10
seen={}
for r in me_rows:
    try: sc=int(r['sim_cursor'])
    except: continue
    if sc not in seen:
        seen[sc]=r
        if sc<=10:
            print(f"sim_cursor={sc} first appears at rom_frame={r['rom_frame']} px={r['px']} py={r['py']} gm={r['gamemode']} ac={r['a_cur']} an={r['a_next']}")

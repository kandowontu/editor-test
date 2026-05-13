import csv, os
T=os.environ['TEMP']
pf=os.path.join(T,'famidash_pf_trace.csv')
me=os.path.join(T,'famidash_mesen_trace.csv')

with open(pf) as f:
    pf_rows=list(csv.DictReader(f))

with open(me) as f:
    lines=f.readlines()
# skip first header line
import io
me_rows=list(csv.DictReader(io.StringIO(''.join(lines[1:]))))

print("MES columns:", list(me_rows[0].keys())[:30])
print("PF rows:", len(pf_rows), "MES rows:", len(me_rows))

# index PF by frame
pf_by_f={int(r['frame']):r for r in pf_rows}
# index MES by sim_cursor (latest rom_frame per sim_cursor)
me_by_sim={}
for r in me_rows:
    try:
        sc=int(r['sim_cursor'])
    except:
        continue
    me_by_sim[sc]=r  # last wins

# Find rom death
for r in me_rows[-30:]:
    print(f"rom={r['rom_frame']} sim={r['sim_cursor']} px={r['px']} py={r['py']} gm={r['gamemode']} ac={r['a_cur']} an={r['a_next']} cd={r['cube_data']} dpc={r['death_pc']} dctx={r['death_ctx']}")

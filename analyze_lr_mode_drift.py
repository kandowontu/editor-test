import csv, os, io
T=os.environ['TEMP']
me=os.path.join(T,'famidash_mesen_trace.csv')
pf=os.path.join(T,'famidash_pf_trace.csv')

with open(me) as f: lines=f.readlines()
me_rows=list(csv.DictReader(io.StringIO(''.join(lines[1:]))))
with open(pf) as f: pf_rows=list(csv.DictReader(f))

me_by_sim={}
for r in me_rows:
    try: sc=int(r['sim_cursor'])
    except: continue
    me_by_sim[sc]=r

# Find first frame where PF.mode != MES.gamemode
print("Mode mismatches:")
prev_match=True
for f, r in enumerate(pf_rows):
    m=me_by_sim.get(f)
    if not m: continue
    pf_mode=r['mode']; mes_mode=m['gamemode']
    if pf_mode != mes_mode:
        if prev_match:
            print(f"  f={f} PF.mode={pf_mode} MES.gm={mes_mode} PF.X={r['X_px']} Y={r['Y_px']} MES.px={m['px']} py={m['py']}")
        prev_match=False
    else:
        prev_match=True

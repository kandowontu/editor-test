import csv, os, io
T=os.environ['TEMP']
me=os.path.join(T,'famidash_mesen_trace.csv')
pf=os.path.join(T,'famidash_pf_trace.csv')

with open(me) as f: lines=f.readlines()
me_rows=list(csv.DictReader(io.StringIO(''.join(lines[1:]))))
with open(pf) as f: pf_rows=list(csv.DictReader(f))

# index PF by frame (PF.frame == sim_cursor? check by looking at PF X near death px=10486)
# find PF row near sim 3776..3788
print("PF rows around frame 3770-3800:")
for r in pf_rows[3770:3810]:
    print(f"f={r['frame']} X={r['X_px']} Y={r['Y_px']} mode={r['mode']} grav={r['gravFlipped']} vy={r['VelY_fixed']} onG={r['onGround']} input={r['input']}")

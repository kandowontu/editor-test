import csv, os
T=os.environ['TEMP']
pf=os.path.join(T,'famidash_pf_trace_dorabaebasic6_20260522_013436_781.csv')
mt=os.path.join(T,'famidash_mesen_trace_dorabaebasic6_20260522_014133_015.csv')

# PF rows around f=935
with open(pf) as f:
    r=csv.DictReader(f)
    print("=== PF ===")
    for row in r:
        try: fr=int(row['frame'])
        except: continue
        if 928<=fr<=945:
            print(f"f={fr} mode={row['mode']} Xf={row['X_fixed']} Yf={row['Y_fixed']} vy={row['VelY_fixed']} xpx={row['X_px']} ypx={row['Y_px']} OG={row['onGround']} mini={row['mini']}")

with open(mt) as f:
    lines=f.readlines()
hi=0
for i,ln in enumerate(lines):
    if ln.startswith('rom_frame'): hi=i; break
hdr=lines[hi].strip().split(',')
col={n:i for i,n in enumerate(hdr)}
print("\n=== MT ===")
for ln in lines[hi+1:]:
    p=ln.rstrip('\n').split(',')
    if len(p)<len(hdr): continue
    try: sim=int(p[col['sim_cursor']])
    except: continue
    if 929<=sim<=946:
        print(f"sim={sim} rom={p[col['rom_frame']]} mode={p[col['gamemode']]} mini={p[col['mini']]} px={p[col['px']]} py={p[col['py']]} cp_y={p[col['cp_y']]} vy={p[col['vel_y']]} gm={p[col['gravity_mod']]} a_cur={p[col['a_cur']]} a_next={p[col['a_next']]}")

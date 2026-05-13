import csv, os, io
T=os.environ['TEMP']
me=os.path.join(T,'famidash_mesen_trace.csv')
pf=os.path.join(T,'famidash_pf_trace.csv')

with open(me) as f: lines=f.readlines()
me_rows=list(csv.DictReader(io.StringIO(''.join(lines[1:]))))
with open(pf) as f: pf_rows=list(csv.DictReader(f))

# index Mesen by sim_cursor (final state)
me_by_sim={}
for r in me_rows:
    try: sc=int(r['sim_cursor'])
    except: continue
    me_by_sim[sc]=r

# Find first frame where game enters ball mode (gm=2)
for r in pf_rows:
    if r['mode']=='2':
        print(f"PF enters ball mode at f={r['frame']} X={r['X_px']} Y={r['Y_px']}")
        break

# Compare PF vs MES across all frames in ball mode after f=3600 region
# Find first divergence point of any size > 8 in Y while both ball mode
print("\nDivergence sweep (Ball mode, |dy|>8):")
last_print=0
for r in pf_rows:
    f=int(r['frame'])
    if r['mode']!='2': continue
    if f<2700: continue
    if f not in me_by_sim: continue
    m=me_by_sim[f]
    try:
        pfy=int(r['Y_px']); mpy=int(m['py'])
    except: continue
    # account for nes_y_offset=400 — PF Y is in world coords, NES py is screen
    # use scroll_y to convert: world_y_nes = py + scrolly
    try:
        scry=int(m['scrolly'])
    except:
        scry=0
    world_nes = mpy + scry
    pf_world = int(r['Y_px'])  # PF storage is world too with nes_y_offset=400 subtracted? Need consistency
    # crude: compare PF.Y_px vs (py+scrolly) - 400
    expected = mpy + scry - 400
    dy = pf_world - expected
    if abs(dy)>8 and f-last_print>5:
        last_print=f
        print(f"  f={f} PFy={pfy} MES py={mpy} scry={scry} expected_pf={expected} dy={dy} PFmode={r['mode']} grav={r['gravFlipped']} MESvy={m['vel_y']} PFvy={r['VelY_fixed']} input_PF={r['input']} ac={m['a_cur']}")
        if f>3800: break

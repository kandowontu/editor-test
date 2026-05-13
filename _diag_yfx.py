import csv, os, sys
pf={}; nes={}
with open(os.path.expandvars(r'%TEMP%\famidash_pf_trace.csv')) as f:
    for row in csv.DictReader(f): pf[int(row['frame'])]=row
with open(os.path.expandvars(r'%TEMP%\famidash_mesen_trace.csv')) as f:
    next(f)
    for row in csv.DictReader(f):
        v=row.get('sim_cursor')
        if not v: continue
        try: sc=int(v)
        except: continue
        if sc not in nes: nes[sc]=row

lo=int(sys.argv[1]); hi=int(sys.argv[2])
for f in range(lo,hi):
    p=pf.get(f); n=nes.get(f+1)
    if not p: continue
    yfx=int(p['Y_fixed'],16)
    vy=int(p['VelY_fixed'],16)
    if vy>=0x80000000: vy-=0x100000000
    nstr=''
    if n:
        nvy=int(n['vel_y'])
        nstr = f" | NES Y={n['py']} rawY=0x{int(n['raw_y']):X} vy={nvy} cd={n['cube_data']}"
    print(f"f={f} Y_fx=0x{yfx:X} (Y_px={p['Y_px']}) VelY={vy} onG={p['onGround']} inp={p['input']} grav={p['gravFlipped']} mode={p['mode']}{nstr}")

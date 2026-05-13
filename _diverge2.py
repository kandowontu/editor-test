import csv
nes=r'C:\Users\kando\Documents\Famidash Editor\Replays\shardscapes\famidash_mesen_trace_20260504_160452.csv'
pf=r'C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv'

nrows=list(csv.reader(open(nes)))
nh=nrows[1]
sci=nh.index('sim_cursor'); pxi=nh.index('px'); pyi=nh.index('py')
vyi=nh.index('vel_y'); cdi=nh.index('cube_data'); gmi=nh.index('gamemode')
nbysc={}
for r in nrows[2:]:
    if not r or not r[0].lstrip('-').isdigit(): continue
    sc=int(r[sci])
    if sc not in nbysc: nbysc[sc]=r

pdata=open(pf).read().splitlines()
ph=pdata[0].split(',')
fi=ph.index('frame'); xpi=ph.index('X_px'); ypi=ph.index('Y_px')
vyf=ph.index('VelY_fixed'); inp=ph.index('input'); ogi=ph.index('onGround')
pbyf={}
for ln in pdata[1:]:
    p=ln.split(',')
    if len(p)<len(ph) or not p[fi].isdigit(): continue
    f=int(p[fi])
    pbyf[f]={'x':int(p[xpi]),'y':int(p[ypi]),'vy':int(p[vyf],16),'in':p[inp],'og':p[ogi]}

# Track Y delta: pf_y - nes_y. Find sign flips.
prev_dy=None
flip_count=0
print('sc | NES (px,py,vy)  PF (px,py,vy,in,og)  | dy')
for sc in sorted(nbysc.keys()):
    if sc not in pbyf: continue
    r=nbysc[sc]
    nx=int(r[pxi]); ny=int(r[pyi]); nvy=int(r[vyi])
    pf_=pbyf[sc]; pvy=pf_['vy']; pvy_s=pvy if pvy<0x80000000 else pvy-0x100000000
    # convert pvy to 16-bit signed if it's huge
    if pvy_s < -0x8000:
        pvy_s = (pvy_s + 0x10000)
    dy = pf_['y'] - ny
    if prev_dy is not None and ((prev_dy <= 0 and dy > 0) or (prev_dy >= 0 and dy < 0) or abs(dy-prev_dy) >= 5):
        flip_count += 1
        print('SHIFT sc={:4} NES(x={} y={} vy={}) PF(x={} y={} vy={} in={} og={}) dy={} (prev={})'.format(
            sc, nx, ny, nvy, pf_['x'], pf_['y'], pvy_s, pf_['in'], pf_['og'], dy, prev_dy))
        if flip_count > 30:
            break
    prev_dy = dy

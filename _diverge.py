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

last_match=None
for sc in sorted(nbysc.keys()):
    if sc not in pbyf: continue
    r=nbysc[sc]
    nx=int(r[pxi]); ny=int(r[pyi]); nvy=int(r[vyi]); ncd=int(r[cdi]); ngm=int(r[gmi])
    pf_=pbyf[sc]; pvy=pf_['vy']; pvy_s=pvy if pvy<0x8000 else pvy-0x10000
    if abs(pf_['y']-ny)>=2 or abs(pf_['x']-nx)>=2:
        if last_match:
            sc0,nx0,ny0,nvy0,ncd0,px0,py0,pv0,pin0,pog0=last_match
            print('LAST OK  sc={} NES=(x={} y={} vy={} cd={}) PF=(x={} y={} vy={} in={} og={})'.format(sc0,nx0,ny0,nvy0,ncd0,px0,py0,pv0,pin0,pog0))
        print('DIVERGE  sc={} NES=(x={} y={} vy={} cd={} gm={}) PF=(x={} y={} vy={} in={} og={})'.format(sc,nx,ny,nvy,ncd,ngm,pf_['x'],pf_['y'],pvy_s,pf_['in'],pf_['og']))
        for d in range(1,15):
            sc2=sc+d
            if sc2 not in nbysc or sc2 not in pbyf: continue
            r2=nbysc[sc2]
            nx2=int(r2[pxi]); ny2=int(r2[pyi]); nvy2=int(r2[vyi]); ncd2=int(r2[cdi])
            pf2=pbyf[sc2]; pvy2=pf2['vy']; pvy2s=pvy2 if pvy2<0x8000 else pvy2-0x10000
            print('  sc={:4} NES=(x={:5} y={:4} vy={:5} cd={}) PF=(x={:5} y={:4} vy={:5} in={} og={})'.format(sc2,nx2,ny2,nvy2,ncd2,pf2['x'],pf2['y'],pvy2s,pf2['in'],pf2['og']))
        break
    last_match=(sc,nx,ny,nvy,ncd,pf_['x'],pf_['y'],pvy_s,pf_['in'],pf_['og'])

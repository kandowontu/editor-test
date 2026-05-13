import csv, os
T = os.environ['TEMP']
pf = open(os.path.join(T,'famidash_pf_trace.csv')).read().splitlines()
ph = pf[0].split(',')
fi=ph.index('frame'); xp=ph.index('X_px'); yp=ph.index('Y_px'); vy=ph.index('VelY_fixed')
inp=ph.index('input'); gf=ph.index('gravFlipped'); og=ph.index('onGround')
yfi=ph.index('Y_fixed'); md=ph.index('mode')
pfd={}
for ln in pf[1:]:
    p=ln.split(',')
    if not p[fi].lstrip('-').isdigit(): continue
    pfd[int(p[fi])]=p
rows=list(csv.reader(open(os.path.join(T,'famidash_mesen_trace.csv'))))
hdr=rows[1]
print('NES cols:', hdr)
nes={}
for r in rows[2:]:
    if not r or not r[0].lstrip('-').isdigit(): continue
    sc=int(r[1])
    if sc not in nes: nes[sc]=r
print()
for f in range(1770,1795):
    p=pfd.get(f); n=nes.get(f+1)
    if not p:
        print(f'f={f} PF missing'); continue
    if not n:
        print(f'f={f} NES sc={f+1} missing'); continue
    print(f'f={f:4d} PF X={p[xp]:>4} Y={p[yp]:>4} vy={p[vy]:>7} inp={p[inp]} gf={p[gf]} og={p[og]} mode={p[md]} || NES sc={f+1} X={n[2]:>4} Y={n[3]:>4} vy={n[10]:>6} ti={n[11]} gm={n[14]} cd={n[20]}')

"""Find first PF/MT divergence frame."""
import csv, os, glob
tmp=os.environ['TEMP']
pfs=sorted(glob.glob(os.path.join(tmp,'famidash_pf_trace_dorabaebasic6_*.csv')))
mts=sorted([p for p in glob.glob(os.path.join(tmp,'famidash_mesen_trace_dorabaebasic6_*.csv')) if os.path.getsize(p)>1000])
print('PF:',pfs[-1] if pfs else None)
print('MT:',mts[-1] if mts else None)
PF=pfs[-1]; MT=mts[-1]
pf=list(csv.DictReader(open(PF,newline='')))
mt={}
with open(MT,'r') as f:
    f.readline(); hdr=f.readline().strip().split(',')
    i_s=hdr.index('sim_cursor'); i_px=hdr.index('px'); i_py=hdr.index('py')
    i_vy=hdr.index('vel_y'); i_sub=hdr.index('scroll_y_subpx'); i_scr=hdr.index('scrolly')
    i_an=hdr.index('a_next'); i_ac=hdr.index('a_cur'); i_md=hdr.index('gamemode')
    i_g=hdr.index('gravity_mod')
    for L in f:
        p=L.split(',')
        if len(p)<16: continue
        try: s=int(p[i_s])
        except: continue
        mt[s]={'px':int(p[i_px]),'py':int(p[i_py]),'vy':int(p[i_vy]),'sub':int(p[i_sub]),'scr':int(p[i_scr]),'an':int(p[i_an]),'ac':int(p[i_ac]),'md':int(p[i_md]),'g':int(p[i_g])}

print('Loaded',len(pf),'PF rows,',len(mt),'MT sim rows')
first=None
for r in pf:
    fr=int(r['frame'])
    m=mt.get(fr+1)
    if not m: continue
    if int(r['X_px'])!=m['px'] or int(r['Y_px'])!=m['py']:
        first=(fr,int(r['X_px']),int(r['Y_px']),m['px'],m['py']); break
print('First divergence:',first)
if first:
    fr0=first[0]
    print('frame | PF X Y vy mode subpx scrY_lowB | MT X Y vy mode subpx scrolly a_n a_c g')
    for r in pf:
        fr=int(r['frame'])
        if fr<fr0-12 or fr>fr0+15: continue
        m=mt.get(fr+1,{})
        vy=int(r['VelY_fixed'],16); vy=vy-0x100000000 if vy>=0x80000000 else vy
        print("{:5d} | {:>5} {:>3} {:6d} {} {:>3} {:>3} | {:>5} {:>3} {:>6} {} {:>3} {:>5} {} {} {}".format(
            fr, r['X_px'], r['Y_px'], vy, r['mode'], r['ScrollYSubpx'], r['Y_lowB'],
            m.get('px','?'), m.get('py','?'), m.get('vy','?'), m.get('md','?'),
            m.get('sub','?'), m.get('scr','?'), m.get('an','?'), m.get('ac','?'), m.get('g','?')))

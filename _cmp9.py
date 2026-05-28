import csv
pf='C:/Users/kando/AppData/Local/Temp/famidash_pf_trace_silentcircles_20260518_030230_372.csv'
me='C:/Users/kando/AppData/Local/Temp/famidash_mesen_trace_silentcircles_20260518_030822_893.csv'
pfd={int(r['frame']):r for r in csv.DictReader(open(pf))}
mef=open(me); mef.readline()
rd=csv.DictReader(mef)
med={}
for r in rd:
    sc=r.get('sim_cursor')
    if sc and sc.isdigit():
        med[int(sc)]=r
OFF=1
# Find first gravity divergence
fg=None
for ff in sorted(pfd):
    m=med.get(ff+OFF)
    if not m: continue
    p=pfd[ff]
    pg = '255' if p['gravFlipped']=='1' else '0'
    mg = m.get('cp_gravity','?')
    if pg != mg:
        fg=ff; break
print('first gravity divergence pf=',fg,'sim=',fg+OFF if fg else None)
if fg:
    for ff in range(max(0,fg-5), fg+10):
        p=pfd.get(ff); m=med.get(ff+OFF)
        if p and m:
            pg = '255' if p['gravFlipped']=='1' else '0'
            print('pf=%d sim=%d pfXY=(%s,%s) meXY=(%s,%s) mode=%s/%s grav=%s/%s inp=%s/%s vy=%s/%s onG=%s' % (
                ff,ff+OFF,p['X_px'],p['Y_px'],m['px'],m['py'],p['mode'],m['gamemode'],pg,m.get('cp_gravity'),p['input'],m['a_cur'],p['VelY_fixed'],m['vel_y'],p.get('onGround')))

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
# find first mesen death
first_death=None
for sc in sorted(med):
    if med[sc].get('death_pc'):
        first_death=sc; break
print('first mesen death sim=',first_death,' pf=',first_death-OFF if first_death else None)
if first_death:
    print('ctx:', med[first_death].get('death_pc'), med[first_death].get('death_ctx'))
    for ff in range(first_death-OFF-10, first_death-OFF+3):
        p=pfd.get(ff); m=med.get(ff+OFF)
        if p and m:
            print('pf=%d sim=%d pfXY=(%s,%s) meXY=(%s,%s) mode=%s/%s mini=%s/%s grav=%s/%s inp=%s/%s vy=%s/%s D=%s' % (
                ff,ff+OFF,p['X_px'],p['Y_px'],m['px'],m['py'],p['mode'],m['gamemode'],p['mini'],m['mini'],p['gravFlipped'],m.get('cp_gravity','?'),p['input'],m['a_cur'],p['VelY_fixed'],m['vel_y'], '!' if m.get('death_pc') else ''))

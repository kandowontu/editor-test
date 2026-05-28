import csv
pf='C:/Users/kando/AppData/Local/Temp/famidash_pf_trace_silentcircles_20260518_014829_071.csv'
me='C:/Users/kando/AppData/Local/Temp/famidash_mesen_trace_silentcircles_20260518_015433_664.csv'
pfd={int(r['frame']):r for r in csv.DictReader(open(pf))}
mef=open(me); mef.readline()
rd=csv.DictReader(mef)
med={}
for r in rd:
    sc=r.get('sim_cursor')
    if sc and sc.isdigit():
        med[int(sc)]=r
# Look at NEW trace inputs leading up to ball mode transition + ball region
print('--- Pre-ball + ball region inputs/state (NEW pf 014829 vs NEW mesen 015433) ---')
for ff in range(2000, 2040):
    p=pfd.get(ff); m=med.get(ff+1)
    if p and m:
        print('pf=%d pfXY=(%s,%s) meXY=(%s,%s) mode=%s/%s inp=%s/%s aN=%s vy=%s/%s' % (
            ff,p['X_px'],p['Y_px'],m['px'],m['py'],p['mode'],m['gamemode'],
            p['input'],m['a_cur'],m['a_next'],p['VelY_fixed'],m['vel_y']))

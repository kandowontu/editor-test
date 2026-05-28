import csv
pf='C:/Users/kando/AppData/Local/Temp/famidash_pf_trace_silentcircles_20260518_012918_203.csv'
me='C:/Users/kando/AppData/Local/Temp/famidash_mesen_trace_silentcircles_20260518_013412_578.csv'
pfd={int(r['frame']):r for r in csv.DictReader(open(pf))}
_mf=open(me); _mf.readline()
med={}
for r in csv.DictReader(_mf):
    try: med[int(r['rom_frame'])]=r
    except: pass

import collections
sc=collections.Counter()
for f in range(0,3500):
    p=pfd.get(f)
    if not p: continue
    for o in [12,13,14,15,16]:
        m=med.get(f+o)
        if m and int(m['px'])==int(p['X_px']) and int(m['py'])==int(p['Y_px']):
            sc[o]+=1
print('offset counts:',sc.most_common())

OFF=sc.most_common(1)[0][0] if sc else 14
print('using OFF=',OFF)
last_match=-1
first_div=None
for f in sorted(pfd):
    m=med.get(f+OFF)
    if not m: continue
    if int(pfd[f]['X_px'])==int(m['px']) and int(pfd[f]['Y_px'])==int(m['py']):
        last_match=f
    else:
        if last_match>=0:
            first_div=f; break
print('last_match=',last_match,'first_div=',first_div)
if first_div:
    for ff in range(first_div-5, first_div+15):
        p=pfd.get(ff); mm=med.get(ff+OFF)
        if p and mm:
            print('pf=%d mes=%d pfX=%s pfY=%s mesX=%s mesY=%s pfM=%s mesM=%s pfMini=%s/%s in=%s/%s tidx=%s vy_me=%s pfVy=0x%s' %
                  (ff,ff+OFF,p['X_px'],p['Y_px'],mm['px'],mm['py'],p['mode'],mm['gamemode'],p['mini'],mm['mini'],p['input'],mm['a_cur'],mm['table_idx'],mm['vel_y'],p['VelY_fixed']))

# show pf end + mes end
print('PF last frame:',max(pfd),pfd[max(pfd)])
print('ME last frame:',max(med),med[max(med)].get('death_pc'),med[max(med)].get('death_ctx'))

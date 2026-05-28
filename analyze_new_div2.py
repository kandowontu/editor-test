import csv
pf='C:/Users/kando/AppData/Local/Temp/famidash_pf_trace_silentcircles_20260518_003823_882.csv'
me='C:/Users/kando/AppData/Local/Temp/famidash_mesen_trace_silentcircles_20260518_004314_968.csv'
pfd={int(r['frame']):r for r in csv.DictReader(open(pf))}
_mf=open(me); _mf.readline()
med={}
for r in csv.DictReader(_mf):
    try: med[int(r['rom_frame'])]=r
    except: pass
OFF=14
first=None
for f in sorted(pfd):
    m=med.get(f+OFF)
    if not m: continue
    if int(pfd[f]['X_px'])!=int(m['px']) or int(pfd[f]['Y_px'])!=int(m['py']):
        first=f; break
print('first div PF=',first,'Mes=',first+OFF if first else None)
if first:
    for ff in range(max(0,first-5), first+10):
        p=pfd.get(ff); mm=med.get(ff+OFF)
        if p and mm:
            print('pf=%d mes=%d pfX=%s pfY=%s mesX=%s mesY=%s pfMode=%s mesMode=%s pfMini=%s mesMini=%s in=%s/%s tidx=%s vy_mes=%s' %
                  (ff,ff+OFF,p['X_px'],p['Y_px'],mm['px'],mm['py'],p['mode'],mm['gamemode'],p['mini'],mm['mini'],p['input'],mm['a_cur'],mm['table_idx'],mm['vel_y']))

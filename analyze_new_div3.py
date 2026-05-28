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
last_match=-1
first_div=None
for f in sorted(pfd):
    m=med.get(f+OFF)
    if not m: continue
    if int(pfd[f]['X_px'])==int(m['px']) and int(pfd[f]['Y_px'])==int(m['py']):
        last_match=f
    else:
        if last_match>=0 and first_div is None and f>last_match:
            first_div=f
            break
print('last_match=',last_match,'first_div=',first_div)
if first_div:
    for ff in range(first_div-5, first_div+15):
        p=pfd.get(ff); mm=med.get(ff+OFF)
        if p and mm:
            print('pf=%d mes=%d pfX=%s pfY=%s mesX=%s mesY=%s pfM=%s mesM=%s pfMini=%s mesMini=%s in=%s/%s tidx=%s vy=%s' %
                  (ff,ff+OFF,p['X_px'],p['Y_px'],mm['px'],mm['py'],p['mode'],mm['gamemode'],p['mini'],mm['mini'],p['input'],mm['a_cur'],mm['table_idx'],mm['vel_y']))

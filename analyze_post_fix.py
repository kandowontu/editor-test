import csv
pf='C:/Users/kando/AppData/Local/Temp/famidash_pf_trace_untitled_20260518_003734_849.csv'
me='C:/Users/kando/AppData/Local/Temp/famidash_mesen_trace_silentcircles_20260517_221939_010.csv'
pfd={int(r['frame']):r for r in csv.DictReader(open(pf))}
_mf=open(me); _mf.readline()
med={}
for r in csv.DictReader(_mf):
    try: med[int(r['rom_frame'])]=r
    except: pass

# Find first frame where Y_px or X_px diverges (offset +13)
first=None
for f in sorted(pfd.keys()):
    m=med.get(f+13)
    if not m: continue
    if int(pfd[f]['Y_px'])!=int(m['py']) or int(pfd[f]['X_px'])!=int(m['px']):
        first=f; break
print('first divergence PF frame:', first, 'Mesen frame:', first+13 if first else None)
if first is not None:
    for f in range(max(0,first-3), first+8):
        p=pfd.get(f); m=med.get(f+13)
        if p and m:
            print(f'pf={f} mes={f+13} pfX={p["X_px"]} pfY={p["Y_px"]} mesX={m["px"]} mesY={m["py"]} tidx={m["table_idx"]}')

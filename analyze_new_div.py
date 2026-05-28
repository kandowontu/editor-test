import csv
pf='C:/Users/kando/AppData/Local/Temp/famidash_pf_trace_silentcircles_20260518_003823_882.csv'
me='C:/Users/kando/AppData/Local/Temp/famidash_mesen_trace_silentcircles_20260518_004314_968.csv'
pfd={int(r['frame']):r for r in csv.DictReader(open(pf))}
_mf=open(me); _mf.readline()
med={}
for r in csv.DictReader(_mf):
    try: med[int(r['rom_frame'])]=r
    except: pass

print('PF frames:', min(pfd), '-', max(pfd))
print('ME frames:', min(med), '-', max(med))

# Try several offsets
import collections
matches=collections.Counter()
for off in range(0,30):
    cnt=0; tot=0
    for f in pfd:
        m=med.get(f+off)
        if not m: continue
        tot+=1
        if int(pfd[f]['X_px'])==int(m['px']) and int(pfd[f]['Y_px'])==int(m['py']):
            cnt+=1
    matches[off]=(cnt,tot)
for off,(c,t) in sorted(matches.items(), key=lambda x:-x[1][0])[:5]:
    print(f'offset={off}: matches={c}/{t}')

# Use best offset
best_off=max(matches, key=lambda o:matches[o][0])
print('best offset:', best_off)
first=None
for f in sorted(pfd.keys()):
    m=med.get(f+best_off)
    if not m: continue
    if int(pfd[f]['Y_px'])!=int(m['py']) or int(pfd[f]['X_px'])!=int(m['px']):
        first=f; break
print('first divergence PF frame:', first, 'Mesen frame:', first+best_off if first else None)
if first is not None:
    for f in range(max(0,first-3), first+12):
        p=pfd.get(f); m=med.get(f+best_off)
        if p and m:
            print(f'pf={f} mes={f+best_off} pfX={p["X_px"]} pfY={p["Y_px"]} mesX={m["px"]} mesY={m["py"]} tidx={m["table_idx"]} mode={p["mode"]}/{m["gamemode"]} mini={p["mini"]}/{m["mini"]} grav={p["gravFlipped"]}/{m["gravity_mod"]} input={p["input"]}/{m["a_cur"]}')

# Find last alive frame in PF
print('\nPF last 10 frames:')
for f in sorted(pfd.keys())[-10:]:
    p=pfd[f]
    print(f'pf={f} X={p["X_px"]} Y={p["Y_px"]} mode={p["mode"]} alive={p["alive"]}')

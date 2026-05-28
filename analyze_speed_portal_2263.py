import csv
pf='C:/Users/kando/AppData/Local/Temp/famidash_pf_trace_silentcircles_20260517_221457_007.csv'
me='C:/Users/kando/AppData/Local/Temp/famidash_mesen_trace_silentcircles_20260517_221939_010.csv'
pfd={int(r['frame']):r for r in csv.DictReader(open(pf))}
_mf=open(me); _mf.readline()  # skip metadata
med={}
for r in csv.DictReader(_mf):
    try: med[int(r['rom_frame'])]=r
    except (ValueError, TypeError): pass
print('PF keys:', list(next(iter(pfd.values())).keys()))
print()
print('MESEN keys:', list(next(iter(med.values())).keys()))
print()
for f in range(2258,2272):
    p=pfd.get(f); m=med.get(f+13)
    if not (p and m): continue
    pf_y = p.get('Y_px') or p.get('y_px') or p.get('PlayerY')
    pf_x = p.get('X_px') or p.get('x_px') or p.get('PlayerX')
    pf_vy = p.get('VelY') or p.get('vel_y')
    pf_yf = p.get('Y_fixed') or p.get('y_fixed')
    pf_xf = p.get('X_fixed') or p.get('x_fixed')
    print(f'PF f={f}  X={pf_x} Y={pf_y} Yfix={pf_yf} Xfix={pf_xf} vy={pf_vy}')
    print(f'ME f={f+13} X={m.get("px")} Y={m.get("py")} rawY={m.get("raw_y")} rawX={m.get("raw_x")} vy={m.get("vel_y")} tidx={m.get("table_idx")} sx={m.get("scrollx")} sy={m.get("scrolly")}')
    print()

import csv
from pathlib import Path

base = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_152604_263.csv")
new = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_175050_795.csv")

b = {int(r['frame']): r for r in csv.DictReader(base.open(newline=''))}
n = {int(r['frame']): r for r in csv.DictReader(new.open(newline=''))}

first = None
for f in sorted(set(b) & set(n)):
    br = b[f]
    nr = n[f]
    keys = ['mode', 'input', 'X_fixed', 'Y_fixed', 'VelY_fixed', 'X_px', 'Y_px', 'gravFlipped', 'mini']
    if any(br[k] != nr[k] for k in keys):
        first = f
        print('first_pf_diff', f)
        for k in keys:
            if br[k] != nr[k]:
                print(f'  {k}: base={br[k]} new={nr[k]}')
        break

if first is not None:
    print('\nwindow around first diff:')
    for f in range(first - 3, first + 6):
        if f not in b or f not in n:
            continue
        br = b[f]
        nr = n[f]
        print(f"f={f} mode b/n={br['mode']}/{nr['mode']} in b/n={br['input']}/{nr['input']} X b/n={br['X_px']}/{nr['X_px']} Y b/n={br['Y_px']}/{nr['Y_px']} Vy b/n={br['VelY_fixed']}/{nr['VelY_fixed']}")
else:
    print('No diffs found between traces on compared keys.')

import csv
from pathlib import Path
pf=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_131605_527.csv")
print('pf_exists',pf.exists(), 'size', pf.stat().st_size if pf.exists() else -1)
if pf.exists():
    with pf.open(newline='') as f:
        r=csv.DictReader(f)
        print('pf_fields', r.fieldnames)
        for i,row in enumerate(r):
            if i<3: print('pf_row', {k:row[k] for k in (r.fieldnames[:8])})
            else: break
mes_files=sorted(Path(r"C:\Users\kando\AppData\Local\Temp").glob("famidash_mesen_trace_*silentcircles*.csv"), key=lambda p:p.stat().st_mtime, reverse=True)
print('mes_candidates', [p.name for p in mes_files[:5]])
if mes_files:
    p=mes_files[0]
    print('mes_pick',p,'size',p.stat().st_size)
    lines=p.read_text(errors='replace').splitlines()
    hdr=lines[1].split(',') if len(lines)>1 else []
    print('mes_fields',hdr)
    if len(lines)>2:
        print('mes_row', lines[2].split(',')[:8])

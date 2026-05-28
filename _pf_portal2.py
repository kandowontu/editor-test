import os, re, glob
T = os.environ['TEMP']
fs = sorted(glob.glob(T + r'\famidash_pf_debug_cataclysm_*.txt'), key=os.path.getmtime)
fn = fs[-1]
lines = open(fn, encoding='utf-8', errors='replace').readlines()
pat = re.compile(r'\[PF f=(\d+)\] \[PORTAL_SPEED\]')
for ln in lines:
    m = pat.search(ln)
    if m:
        f = int(m.group(1))
        if 2860 <= f <= 2900:
            print(ln.rstrip())

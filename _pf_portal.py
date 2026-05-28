import os, re, glob
T = os.environ['TEMP']
fs = sorted(glob.glob(T + r'\famidash_pf_debug_cataclysm_*.txt'), key=os.path.getmtime)
fn = fs[-1]
print('PF DBG:', fn)
with open(fn, encoding='utf-8', errors='replace') as f:
    lines = f.readlines()
cur_frame = None
for ln in lines:
    m = re.search(r'\[FRAME (\d+)\]', ln)
    if m:
        cur_frame = int(m.group(1))
    if cur_frame is not None and 2870 <= cur_frame <= 2900 and 'PORTAL_SPEED' in ln:
        print(f'f={cur_frame}: {ln.rstrip()}')

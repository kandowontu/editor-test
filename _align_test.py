import csv, os, sys
T = os.environ['TEMP']
pf_path = T + r'\famidash_pf_trace_cataclysm_20260518_151738_650.csv'
mt_path = T + r'\famidash_mesen_trace_cataclysm_20260518_151724_006.csv'

def h(s):
    s = s.strip()
    return int(s, 16) if s.startswith('0x') else int(s)

pf = {}
with open(pf_path) as f:
    f.readline()
    for line in f:
        p = line.rstrip().split(',')
        try: fr = int(p[0])
        except: continue
        pf[fr] = dict(subpx=int(p[22]), camY=h(p[19]), Y=h(p[2]), mode=int(p[11]))

mt = {}
with open(mt_path) as f:
    f.readline()
    f.readline()
    for line in f:
        parts = line.rstrip('\n').split(',')
        extra = len(parts) - 30
        if extra > 0:
            parts = parts[:22] + [','.join(parts[22:22+extra+1])] + parts[22+extra+1:]
        try: s = int(parts[1])
        except: continue
        if s in mt: continue
        mt[s] = dict(subpx=int(parts[15]), scrollY=int(parts[9]), rawY=int(parts[7]))

# Try various alignments
for shift in [0, 1, -1]:
    print(f'\n========== Alignment PF[N] vs MT[N+{shift}] ==========')
    first = None
    for i in sorted(pf):
        j = i + shift
        if j not in mt: continue
        if i > 30: break
        p = pf[i]; m = mt[j]
        cam_pf = p['camY'] >> 8
        cam_mt_528 = m['scrollY'] - 528
        sub_match = '✓' if p['subpx']==m['subpx'] else 'X'
        cam_match = '✓' if cam_pf==cam_mt_528 else 'X'
        if first is None and (sub_match=='X' or cam_match=='X'):
            first = i
        print(f'  PF[{i:>3}] cam={cam_pf:>4} sub={p["subpx"]:>3}  MT[{j:>3}] scrollY={m["scrollY"]:>4} (cam-pf-equiv={cam_mt_528:>4}) sub={m["subpx"]:>3}  cam{cam_match} sub{sub_match}')
    print(f'  >>>FIRST DIV at PF[{first}]<<<')

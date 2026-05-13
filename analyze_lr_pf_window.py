import csv, os
T=os.environ['TEMP']
pf=os.path.join(T,'famidash_pf_trace.csv')
with open(pf) as f: pf_rows=list(csv.DictReader(f))

def hs(s):
    v=int(s,16); 
    if v>=0x80000000: v-=0x100000000
    return v

print(f"{'f':>5} {'X':>5} {'Y':>4} {'mod':>3} {'gr':>2} {'oG':>2} {'in':>2} {'vy':>5}")
for f in range(3640, 3810):
    if f>=len(pf_rows): break
    r=pf_rows[f]
    print(f"{r['frame']:>5} {r['X_px']:>5} {r['Y_px']:>4} {r['mode']:>3} {r['gravFlipped']:>2} {r['onGround']:>2} {r['input']:>2} {hs(r['VelY_fixed']):>5}")

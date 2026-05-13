import os
T=os.environ['TEMP']
pf=open(os.path.join(T,'famidash_pf_trace.csv')).read().splitlines()
ph=pf[0].split(',')
ix={n:k for k,n in enumerate(ph)}
def g(p,n): return p[ix[n]]
print(f"{'f':>5} {'X':>5} {'Y':>5} {'vy':>10} inp gf og mode alive")
for ln in pf[1:]:
    p=ln.split(',')
    if not p[0].lstrip('-').isdigit(): continue
    f=int(p[0])
    if 1935<=f<=1948:
        print(f"{f:5d} {g(p,'X_px'):>5} {g(p,'Y_px'):>5} {g(p,'VelY_fixed'):>10} {g(p,'input')} {g(p,'gravFlipped')} {g(p,'onGround')} {g(p,'mode')} {g(p,'alive')}")

import csv, os
T=os.environ['TEMP']
def load(p):
    rows=[]
    with open(p) as f:
        r=csv.reader(f)
        h=next(r)
        for row in r:
            d={h[i]:row[i] for i in range(min(len(h),len(row)))}
            rows.append(d)
    return rows,h
pf,_=load(f'{T}\\famidash_pf_trace_dorabaebasic6_20260522_024425_084.csv')
# mode transitions for PF (alive only, up to 2760)
prev=None
print("PF mode transitions:")
for i,r in enumerate(pf[:2760]):
    m=r['mode']
    if m!=prev:
        print(f"  f={r['frame']:>5} mode={m} alive={r['alive']} X={r['X_px']} Y={r['Y_px']}")
        prev=m
print("PF alive=0 frames in 0..2760:")
for r in pf[:2760]:
    if r['alive']=='0':
        print(f"  f={r['frame']} X={r['X_px']} Y={r['Y_px']} mode={r['mode']}")
        break

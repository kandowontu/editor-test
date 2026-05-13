import csv, os
T=os.environ['TEMP']
pf=os.path.join(T,'famidash_pf_trace.csv')
me=os.path.join(T,'famidash_mesen_trace.csv')

with open(pf) as f:
    r=csv.DictReader(f)
    pf_rows=list(r)
with open(me) as f:
    r=csv.DictReader(f)
    me_rows=list(r)

print("PF columns:", list(pf_rows[0].keys()))
print("MES columns:", list(me_rows[0].keys()))

# find gravFlipped transitions in PF
prev=None
trans=[]
for row in pf_rows:
    g=row.get('gravFlipped')
    if prev is not None and g != prev:
        trans.append(row)
    prev=g
print(f"\nFound {len(trans)} gravFlipped transitions")
# show transitions near 2700-2800
for row in trans:
    f=int(row['frame'])
    if 0 <= f <= 3000:
        print(f"  f={f} X={row.get('X_px')} Y={row.get('Y_px')} mode={row.get('mode')} grav={row['gravFlipped']} vy={row.get('VelY_fixed')} onG={row.get('onGround')} mini={row.get('mini')} input={row.get('input')}")

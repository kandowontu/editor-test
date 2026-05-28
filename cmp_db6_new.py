import csv,os,sys
T=os.environ["TEMP"]
mtp=T+r"\famidash_mesen_trace_dorabaebasic6_20260525_011514_413.csv"
pfp=T+r"\famidash_pf_trace_dorabaebasic6_20260525_010640_278.csv"

mt={}
with open(mtp) as f:
    r=csv.reader(f); next(r); next(r)
    for row in r:
        try: s=int(row[1]); mt[s]=(int(row[2]),int(row[3]),int(row[10]),row[14],row[25],row[5])
        except: pass
pf={}
with open(pfp) as f:
    r=csv.reader(f); next(r)
    for row in r:
        try: fr=int(row[0]); pf[fr]=(int(row[6]),int(row[7]),int(row[3],16),row[11],row[12],row[4],row[8])
        except: pass

# find first divergence in ball or earlier
print(f"PF rows {min(pf)}-{max(pf)} ({len(pf)} frames)")
print(f"MT rows {min(mt)}-{max(mt)} ({len(mt)} frames)")
print()

# Alignment: PF f=N ~ MT sim=N+1.
# Check first divergence (X or Y or mode mismatch, ignoring small subpixel) in mode change frames
modes={}
for fr in sorted(pf):
    p=pf[fr]; m=mt.get(fr+1)
    if not m: continue
    if p[3] != modes.get('pf'):
        modes['pf']=p[3]; print(f"PF mode change at f={fr}: mode={p[3]}  PF=(X={p[0]},Y={p[1]})  MT=(X={m[0]},Y={m[1]},m={m[3]})")
print()

# scan divergence
fdiv=None
for fr in sorted(pf):
    p=pf[fr]; m=mt.get(fr+1)
    if not m: continue
    if abs(p[0]-m[0])>2 or abs(p[1]-m[1])>2 or p[3]!=m[3]:
        fdiv=fr; break
print(f"First sig divergence at f={fdiv}/sim={fdiv+1 if fdiv else 'NA'}")
if fdiv:
    for fr in range(max(0,fdiv-8), fdiv+10):
        p=pf.get(fr); m=mt.get(fr+1)
        if p and m:
            print(f"f={fr}/sim={fr+1} PF[X={p[0]} Y={p[1]} vy={p[2]} m={p[3]} gF={p[4]} inp={p[5]} onG={p[6]}]  MT[X={m[0]} Y={m[1]} vy={m[2]} m={m[3]} cpg={m[4]} ac={m[5]}]  dX={p[0]-m[0]} dY={p[1]-m[1]}")

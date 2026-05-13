import csv, os, io
T=os.environ['TEMP']
me=os.path.join(T,'famidash_mesen_trace.csv')
pf=os.path.join(T,'famidash_pf_trace.csv')
with open(pf) as f: pf_rows=list(csv.DictReader(f))
with open(me) as f: lines=f.readlines()
me_rows=list(csv.DictReader(io.StringIO(''.join(lines[1:]))))

print("PF first 8 rows:")
for r in pf_rows[:8]:
    print(f"  f={r['frame']} X={r['X_px']} Y={r['Y_px']} input={r['input']} mode={r['mode']}")
print("MES first 12 rows:")
for r in me_rows[:12]:
    print(f"  rom={r['rom_frame']} sim={r['sim_cursor']} px={r['px']} py={r['py']} ac={r['a_cur']} an={r['a_next']} gm={r['gamemode']}")

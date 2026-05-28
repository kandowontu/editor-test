import csv,os
T=os.environ["TEMP"]
mtp=T+r"\famidash_mesen_trace_dorabaebasic6_20260522_131300_285.csv"
pfp=T+r"\famidash_pf_trace_dorabaebasic6_20260522_130352_468.csv"

print("--- MT ball-mode rows sim 2260-2315 ---")
with open(mtp) as f:
    r=csv.reader(f); next(r); next(r)
    for row in r:
        try:
            s=int(row[1])
            if 2260<=s<=2315 and row[14]=='2':
                print(f"sim={s} rom={row[0]} px={row[2]} py={row[3]} vy={row[10]} a_n={row[4]} a_c={row[5]} cp_g={row[25]}")
        except Exception as e: pass

print()
print("--- PF f=2260-2315 mode=2 rows ---")
with open(pfp) as f:
    r=csv.reader(f); h=next(r)
    print("HDR:",h)
    for row in r:
        try:
            fr=int(row[0])
            if 2260<=fr<=2315 and row[11]=='2':
                print(f"f={fr} X={row[6]} Y={row[7]} vy={row[3]} inp={row[4]} gF={row[12]} onG={row[8]}")
        except: pass

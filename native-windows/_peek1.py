pf=open(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv").read().splitlines()
mes=open(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv").read().splitlines()

print("=== PF trace f=415..436 ===")
print("frame X Y VY onG mode grv mini input")
for L in pf[416:438]:
    p=L.split(',')
    print(f"{p[0]} {p[6]} {p[7]} {p[3]} {p[8]} {p[11]} {p[12]} {p[23]} {p[4]}")

# mesen header detection
print("\n=== Mesen header ===")
print(mes[0])
# find rows where rom_f in 425..445
print("\n=== Mesen rom_f 425..445 ===")
for L in mes:
    if not L or L.startswith('#'): continue
    parts=L.split(',')
    if len(parts)<5: continue
    try: rf=int(parts[0])
    except: continue
    if 425<=rf<=445:
        # rom,sim,px,py,a_next,a_cur,raw_x,raw_y,scrx,scry,vy,tidx,grav_mod,dash,gm,sy_subpx
        print(f"rom={parts[0]} sim={parts[1]} px={parts[2]} py={parts[3]} a_next={parts[4]} vy={parts[10]} grav={parts[12]} gm={parts[14]}")

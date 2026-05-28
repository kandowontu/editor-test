import re, os
T=os.environ['TEMP']
orb=os.path.join(T,'famidash_pf_orb_debug_dorabaebasic6_20260525_192426_022.log')

re_pf = re.compile(r'f=(\d+) ENTER mode=(\d+).*X_fixed=0x([0-9A-Fa-f]+) Y_fixed=0x([0-9A-Fa-f]+) VelY_fixed=0x([0-9A-Fa-f]+).*playerY_px=(-?\d+)')
prev_mode = -1
with open(orb,'r',encoding='utf-8',errors='ignore') as f:
    for ln in f:
        m = re_pf.search(ln)
        if m:
            fr = int(m.group(1)); mode = int(m.group(2))
            if mode != prev_mode:
                vy_h = m.group(5); vy = int(vy_h,16)
                if vy >= 0x80000000: vy -= 0x100000000
                py = int(m.group(6))
                print(f'f={fr} mode={mode} Y_px={py} Vy={vy}')
                prev_mode = mode

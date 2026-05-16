import re
with open(r'C:\Users\kando\AppData\Local\Temp\famidash_mesen_physics_debug.log',encoding='utf-8',errors='ignore') as fp:
    cur=-1; grab=False
    for L in fp:
        m=re.match(r'^F=(\d+)\s+sim=(\d+)',L)
        if m:
            cur=int(m.group(2)); grab = 3170 <= cur <= 3180
            if grab: print(L.rstrip())
            continue
        if grab:
            print(L.rstrip())

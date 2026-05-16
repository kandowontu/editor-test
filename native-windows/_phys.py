import re
f = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_physics_debug.log"
target_sim = set(range(3170, 3185))
out = []
with open(f, encoding="utf-8", errors="ignore") as fp:
    cur_sim = -1
    grab = False
    for L in fp:
        m = re.match(r"^F=(\d+)\s+sim=(\d+)", L)
        if m:
            cur_sim = int(m.group(2))
            grab = cur_sim in target_sim
            if grab: out.append(L.rstrip())
            continue
        if grab:
            # Filter to lines with mini/grav info
            if "mini" in L or "wave_movement" in L or "x_movement" in L or "_speed" in L.lower():
                out.append(L.rstrip())
print("\n".join(out[:200]))

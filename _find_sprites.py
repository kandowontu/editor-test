import re
T = open(r'lvlset_HUGE\dorabaebasic6.tmx',encoding='utf-8').read()
for m in re.finditer(r'<object[^>]*?(?:/>|>.*?</object>)', T, re.S):
    s = m.group(0)
    xm = re.search(r' x="(-?\d+(?:\.\d+)?)"', s)
    ym = re.search(r' y="(-?\d+(?:\.\d+)?)"', s)
    gm = re.search(r' gid="(\d+)"', s)
    nm = re.search(r' name="([^"]*)"', s)
    typ = re.search(r' type="([^"]*)"', s)
    if not xm: continue
    x=float(xm.group(1))
    if 5200<=x<=5500:
        print(f'x={x} y={ym.group(1) if ym else "?"} gid={gm.group(1) if gm else "?"} name={nm.group(1) if nm else ""} type={typ.group(1) if typ else ""}')

from pathlib import Path
p=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_physics_debug_silentcircles_20260517_124916_016.log")
lines=p.read_text(encoding='utf-8', errors='replace').splitlines()
keys=['F=1954 sim=1942','F=1955 sim=1943','F=1956 sim=1944','F=1957 sim=1945','F=1958 sim=1946','F=1959 sim=1947','F=1960 sim=1948','F=1961 sim=1949']
for k in keys:
    for i,l in enumerate(lines):
        if k in l:
            print('\n===',k,'===')
            for j in range(i, min(i+28, len(lines))):
                print(lines[j])
            break

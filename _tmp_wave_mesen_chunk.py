from pathlib import Path
p=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_physics_debug_silentcircles_20260517_143603_468.log")
lines=p.read_text(errors='replace').splitlines()
keys=['F=1896 sim=1884','F=1897 sim=1885','F=1898 sim=1886','F=1899 sim=1887','F=1900 sim=1888']
for k in keys:
    for i,l in enumerate(lines):
        if k in l:
            print('\n===',k,'===')
            for j in range(i,min(i+24,len(lines))):
                print(lines[j])
            break

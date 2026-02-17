import re

with open(r'C:\Editor Test\test9.tmx', 'r', encoding='utf-8') as f:
    content = f.read()

# Find data blocks between <data encoding="csv"> and </data>
pattern = r'<data encoding="csv">\s*(.*?)\s*</data>'
datas = re.findall(pattern, content, re.DOTALL)
print(f'Found {len(datas)} layers')

w = 800

for i, d in enumerate(datas):
    vals = [int(x.strip()) for x in d.split(',') if x.strip()]
    print(f'Layer {i}: {len(vals)} tiles ({len(vals)//w} rows)')
    
    if i == 0:  # tile layer - subtract 1 from GID to get metatile index (TMX firstgid=1)
        for r in [49, 50, 51, 52, 53, 54, 55, 56]:
            if r * w + 50 <= len(vals):
                row_vals = [(vals[r*w+c] & 0xFF) - 1 if (vals[r*w+c] & 0xFF) > 0 else -1 for c in range(60)]
                nz = [(c, hex(v) if v >= 0 else 'empty') for c, v in enumerate(row_vals) if v >= 0]
                print(f'  r{r} nonzero cols 0-59: {nz}')
    
    if i == 1:  # sprite layer - subtract SpriteFirstGid would be different, just show raw GID-1
        for r in [49, 50, 51, 52, 53, 54, 55, 56]:
            if r * w + 60 <= len(vals):
                row_vals = [(vals[r*w+c] & 0xFF) for c in range(60)]
                nz = [(c, hex(v)) for c, v in enumerate(row_vals) if v != 0]
                if nz:
                    print(f'  SP r{r} nonzero cols 0-59: {nz}')

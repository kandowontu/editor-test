import re

with open('native-windows/MetatileCollision.cs', 'r') as f:
    content = f.read()

match = re.search(r'new byte\[\]\s*\{([^}]+)\}', content, re.DOTALL)
if match:
    raw = match.group(1)
    vals = []
    for tok in raw.split(','):
        tok = tok.strip()
        if tok:
            vals.append(int(tok, 0))
    
    names = {0:'EMPTY', 1:'FLOOR_CEIL', 2:'TOP', 3:'BOTTOM', 4:'ALL', 5:'DEATH', 6:'NO_SIDE', 7:'RIGHT'}
    
    for tid in [1, 4, 17, 26, 54, 55, 117, 118, 120, 121, 123, 124, 126]:
        if tid < len(vals):
            v = vals[tid]
            cname = names.get(v, "UNKNOWN")
            print(f"Tile {tid:3d} = col {v} ({cname})")

import xml.etree.ElementTree as ET, csv, io, sys

print("Parsing TMX...", flush=True)
tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\deathmoon.tmx')
root = tree.getroot()
print("Parsed.", flush=True)

layers = root.findall('layer')
print(f"Layers: {len(layers)}", flush=True)
for l in layers:
    print(f"  name={l.get('name')} w={l.get('width')} h={l.get('height')}", flush=True)

# Collision table (simplified - key entries)
COL_NAMES = {
    0: 'NONE', 1: 'FC', 2: 'TOP', 3: 'BOT', 4: 'RIGHT', 5: 'LEFT',
    16: 'ALL', 17: 'DEATH', 126: 'ALL'
}

for layer in root.findall('layer'):
    name = layer.get('name') or ''
    if name == 'SP':
        continue  # Skip sprite layer
        w = int(layer.get('width'))
        h = int(layer.get('height'))
        data = layer.find('data').text.strip()
        rows = list(csv.reader(io.StringIO(data)))
        ground = 3
        print(f'Map {w}x{h}, ground={ground}')
        
        # Print tiles at tileX=1120-1135 for tileY=44-55
        print(f"\n{'tileY':>20s}", end='')
        for tx in range(1120, 1136):
            print(f'  {tx:5d}', end='')
        print()
        print(f"{'pixelX':>20s}", end='')
        for tx in range(1120, 1136):
            print(f'  {tx*16:5d}', end='')
        print()
        print('-' * 130)
        
        for ty in range(44, 56):
            ay = ty + ground
            if ay < len(rows):
                row = rows[ay]
                label = f'{ty}(pY {ty*16}-{ty*16+15})'
                print(f'{label:>20s}', end='')
                for tx in range(1120, 1136):
                    if tx < len(row):
                        tid = int(row[tx].strip()) if row[tx].strip() else 0
                    else:
                        tid = 0
                    if tid == 0:
                        print(f'  {"---":>5s}', end='')
                    else:
                        name = COL_NAMES.get(tid, f'?{tid}')
                        print(f'  {name:>5s}', end='')
                print()

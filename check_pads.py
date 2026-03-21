raw = open(r'C:\Editor Test\lvlset_HUGE_metadata.json5').read()
idx = raw.lower().find('clutterfunk')
end_idx = raw.find('level:', idx + 20)
section = raw[idx:end_idx] if end_idx > 0 else raw[idx:idx+3000]

# Check for the pad positions: (866,12), (875,16), (850,14)
for coord in ['866, 12', '875, 16', '850, 14', '[866,12]', '[875,16]', '[850,14]']:
    if coord in section:
        pos = section.find(coord)
        ctx = section[max(0,pos-40):pos+40]
        print(f'Found {coord}: ...{ctx}...')
    else:
        print(f'{coord}: NOT FOUND')

# Also check what objectOffset entries have offsetY
print()
print("Entries with offsetY in clutterfunk section:")
import re
for m in re.finditer(r'offsetY:\s*([+-]?\d+|0x[0-9a-fA-F]+)', section):
    start = max(0, m.start() - 200)
    chunk = section[start:m.end()+20]
    # Find coordinates in this chunk
    coords = re.findall(r'\[(\d+),\s*(\d+)\]', chunk)
    print(f"  offsetY={m.group(1)}, nearby coords: {coords[-5:] if len(coords) > 5 else coords}")

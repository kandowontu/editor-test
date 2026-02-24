import re

with open(r'c:\Editor Test\famidash\fan level collection\everyend.tmx', 'r', encoding='utf-8') as f:
    content = f.read()

# Find all layer data sections
layers = re.findall(r'<layer[^>]*name="([^"]*)"[^>]*>.*?<data encoding="csv">(.*?)</data>', content, re.DOTALL)
if not layers:
    # Try without name attribute
    layers = re.findall(r'<layer[^>]*>.*?<data encoding="([^"]*)">(.*?)</data>', content, re.DOTALL)
    print(f"Found {len(layers)} layers (no name)")
    for i, (enc, data) in enumerate(layers):
        tiles = [int(x.strip()) for x in data.replace('\n', ',').split(',') if x.strip()]
        print(f"  Layer {i}: encoding={enc}, tile count={len(tiles)}")
else:
    print(f"Found {len(layers)} named layers")

# Just find the csv data directly
match = re.search(r'<data\s+encoding\s*=\s*"csv"\s*>(.*?)</data>', content, re.DOTALL)
if match:
    csv_data = match.group(1).strip()
    tiles = [int(x.strip()) for x in csv_data.replace('\n', ',').split(',') if x.strip()]
    print(f"Total tiles: {len(tiles)}, expected: {4573*57}")
    
    print("\nTile IDs at requested positions:")
    print(f"{'arrayY':>6} {'tileX':>5} {'tileY':>5} {'worldX':>6} {'worldY':>6} {'index':>8} {'tid':>5} {'hex':>6}")
    print("-" * 60)
    for ay in range(47, 52):
        for tx in range(425, 429):
            idx = ay * 4573 + tx
            tid = tiles[idx]
            print(f"{ay:>6} {tx:>5} {ay-3:>5} {tx*16:>6} {(ay-3)*16:>6} {idx:>8} {tid:>5} 0x{tid:02X}")
        print()
else:
    print("No CSV data found!")
    # Show what data encodings exist
    encodings = re.findall(r'<data\s+encoding\s*=\s*"([^"]*)"', content)
    print(f"Encodings found: {encodings}")
    # Show first 500 chars around <data
    idx = content.find('<data')
    if idx >= 0:
        print(f"Around <data tag: {content[idx:idx+200]}")

import re

with open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\timemachine.tmx', 'r') as f:
    content = f.read()

data_blocks = re.findall(r'<data encoding="csv">\s*([\s\S]*?)\s*</data>', content)

W = 997
H = 27

# Coin sprite IDs: 0x07, 0x1A, 0x1B, 0x6E
coin_ids = {0x07, 0x1A, 0x1B, 0x6E}

vals = [int(v.strip()) for v in data_blocks[1].replace('\n', ',').split(',') if v.strip()]

print("=== ALL COIN SPRITES (0x07, 0x1A, 0x1B, 0x6E) in the level ===")
for r in range(H):
    for c in range(W):
        idx = r * W + c
        if idx < len(vals) and vals[idx] > 0:
            v = vals[idx]
            sid = v - 257 if v >= 257 else v
            if sid in coin_ids:
                print(f"  COIN sid=0x{sid:02X} at col={c} row={r} pixel=({c*16},{r*16})-({c*16+16},{r*16+16})")

# Now list ALL non-zero sprites in cols 355-385
print("\n=== ALL non-zero sprites in cols 355-385 ===")
for r in range(H):
    for c in range(355, 386):
        idx = r * W + c
        if idx < len(vals) and vals[idx] > 0:
            v = vals[idx]
            sid = v - 257 if v >= 257 else v
            print(f"  col={c} row={r} gid={v} sid=0x{sid:02X} pixel=({c*16},{r*16})-({c*16+16},{r*16+16})")

# Now show wider tile view - cols 360-380, rows 10-27
print("\n=== TILE LAYER: cols 360-385, rows 10-27 ===")
tvals = [int(v.strip()) for v in data_blocks[0].replace('\n', ',').split(',') if v.strip()]
cols = range(360, 386)
rows = range(10, 27)
hdr = 'R\\C '
for c in cols:
    hdr += f'{c:>4}'
print(hdr)
for r in rows:
    line = f'{r:>3} '
    for c in cols:
        idx = r * W + c
        if idx < len(tvals):
            v = tvals[idx]
            tid = v - 1 if v > 0 else -1
            if tid < 0:
                line += '   .'
            else:
                line += f'{tid:>4}'
        else:
            line += '  ??'
    print(line)

# Show which of these tile IDs are solid
print("\n=== TILE ID MEANINGS ===")
# Let's look at the unique tile IDs in this area
unique_tiles = set()
for r in rows:
    for c in cols:
        idx = r * W + c
        if idx < len(tvals) and tvals[idx] > 0:
            unique_tiles.add(tvals[idx] - 1)
print(f"Unique tile IDs in area: {sorted(unique_tiles)}")
print("Tile IDs 1,2,5,6,12,13 = solid blocks/floors")
print("Tile IDs 18,23,24,26,28 = various blocks/patterns")
print("Tile IDs 30,33,35,37,38,39,40,47 = decorative/structure")

import re

with open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\timemachine.tmx', 'r') as f:
    content = f.read()

data_blocks = re.findall(r'<data encoding="csv">\s*([\s\S]*?)\s*</data>', content)

W = 997
H = 27

tvals = [int(v.strip()) for v in data_blocks[0].replace('\n', ',').split(',') if v.strip()]
svals = [int(v.strip()) for v in data_blocks[1].replace('\n', ',').split(',') if v.strip()]

# Detailed view: cols 335-375, rows 15-26 - both tiles and sprites
print("=== TILE LAYER: cols 335-375, rows 15-26 ===")
cols = range(335, 376)
rows = range(15, 27)
hdr = 'R\\C '
for c in cols:
    hdr += f'{c:>4}'
print(hdr)
for r in rows:
    line = f'{r:>3} '
    for c in cols:
        idx = r * W + c
        v = tvals[idx]
        tid = v - 1 if v > 0 else -1
        if tid < 0:
            line += '   .'
        else:
            line += f'{tid:>4}'
    print(line)

print("\n=== SPRITE LAYER: cols 335-375, rows 15-26 ===") 
hdr = 'R\\C '
for c in cols:
    hdr += f'{c:>4}'
print(hdr)
for r in rows:
    line = f'{r:>3} '
    for c in cols:
        idx = r * W + c
        v = svals[idx]
        if v == 0:
            line += '   .'
        elif v >= 257:
            sid = v - 257
            line += f'  {sid:02X}'
        else:
            line += f'  ?{v}'
    print(line)

# Now check which tile IDs are solid (collidable) vs decorative
# Need to understand what tiles 13, 18, 26, 28, 33, 35, 47 etc actually are
print("\n=== SPRITE TYPE ANALYSIS ===")
sprite_types = {
    0x00: "yellow pad (up)",
    0x01: "yellow pad (up, alt)",
    0x02: "yellow pad (gravity)",  
    0x03: "yellow pad (gravity, alt)",
    0x04: "pink pad",
    0x05: "portal (cube?)",
    0x06: "portal",
    0x07: "coin (secret)",
    0x08: "gravity portal (normal)",
    0x09: "gravity portal (flip)",
    0x0A: "coin (regular)",
    0x0B: "portal (ship?)",
    0x0C: "portal",
    0x0D: "portal", 
    0x0E: "portal",
    0x1A: "coin (user/secret)",
    0x1B: "coin (user/secret 2)",
    0x2D: "deco",
    0x2E: "deco (ground)",
    0x2F: "deco (ceiling)",
    0x3D: "deco",
    0x6E: "mini coin",
}
print("\nSprites found in area cols 335-375:")
for r in rows:
    for c in cols:
        idx = r * W + c
        v = svals[idx]
        if v > 0:
            sid = v - 257 if v >= 257 else v
            name = sprite_types.get(sid, f"unknown")
            print(f"  ({c},{r}) px=({c*16},{r*16}) sid=0x{sid:02X} = {name}")

# Check the entry to the lower section more carefully
# Particularly cols 340-355, rows 16-22
print("\n=== ENTRY AREA DETAIL: cols 336-356, rows 15-25 ===")
print("Tiles:")
for r in range(15, 26):
    line = f"R{r:>2}: "
    for c in range(336, 357):
        idx = r * W + c
        v = tvals[idx]
        tid = v - 1 if v > 0 else -1
        if tid < 0:
            line += "  . "
        else:
            line += f"{tid:>3} "
    print(line)

# Now let's check - is there a clear horizontal path from the gap entrance
# down to the coin? Check rows 20-22 for solid blocks between col 354 and 368
print("\n=== HORIZONTAL PATH CHECK: rows 18-24, cols 354-370 ===")
for r in range(18, 25):
    line = f"R{r:>2}: "
    for c in range(354, 371):
        idx = r * W + c
        v = tvals[idx]
        tid = v - 1 if v > 0 else -1
        if tid < 0:
            line += " . "
        else:
            line += f"{tid:>2} "
    print(line)
    
# Check if any of the tiles in the box structure (rows 20-24, cols 366-370) are solid
print("\n=== BOX STRUCTURE around coin (cols 366-370, rows 20-24) ===")
for r in range(19, 25):
    for c in range(366, 371):
        idx = r * W + c
        v = tvals[idx]
        tid = v - 1 if v > 0 else -1
        if tid >= 0:
            print(f"  ({c},{r}) tile={tid}")

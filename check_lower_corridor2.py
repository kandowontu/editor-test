#!/usr/bin/env python3
"""Map the lower corridor terrain with CORRECT Y-to-row mapping.
C# code: tileIndexY = (worldY / 16) + groundRowsToReserve
So: TMX row = worldY // 16 + 3"""

import xml.etree.ElementTree as ET

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()
width = int(root.attrib['width'])  # 843
height = int(root.attrib['height'])  # 27

layer = root.findall('.//layer')[0]
data = layer.find('data').text.strip()
tiles = [int(x) for x in data.split(',')]

GRR = 3

# COLLISION TABLE (from MetatileCollision.cs)
COLL_NAMES = [
    "NONE", "FC", "FC", "BOT", "DT", "FC", "FC", "NONE",  # 0-7
    "DB", "DB", "DT", "DT", "DB", "DT", "DL", "DR",       # 8-15
    "ALL", "DEATH", "DB", "DB", "DT", "TCS", "ALL", "DT",  # 16-23
    "DB", "TOP", "DEATH", "DEATH", "DEATH", "DL", "DT", "DR", # 24-31
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE", # 32-47
    "ALL","ALL","ALL","ALL","NONE","NONE","NONE","TCS","ALL","ALL","ALL","DB","ALL","ALL","ALL","ALL", # 48-63
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE", # 64-79
    "ALL","ALL","ALL","ALL","TOP","TOP","TOP","TOP","BOT","BOT","BOT","DEATH","DB","DT","DR","DL", # 80-95
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE", # 96-111
    "ALL","ALL","ALL","ALL","DRS","DB","DLS","DR","NONE","DL","URS","DT","ULS","DEATH","ALL","DB", # 112-127
    "NONE","NONE","DEATH","DEATH","DB","DT","DB","DT","FC","FC","RIGHT","LEFT","RIGHT","LEFT","NONE","NS", # 128-143
    "SRD45","SLD45","SRU45","SLU45","SRD22R","SRD22L","SLD22R","SLD22L","SRU22R","SRU22L","SLU22R","SLU22L","SRD66T","SRD66B","SLD66B","SLD66T", # 144-159
    "SRU66T","SRU66B","SLU66B","SLU66T","NS","NS","NS","NS","LSB","RSB","BLS","BRS","BS","DLS2","DRS2","DBS", # 160-175
    "UL","UR","DwL","DwR","TOP","BOT","LEFT","RIGHT","TLS","TRS","BLS2","BRS2","TLBR","TRBL","ULS2","URS2", # 176-191
    "UBS","DTR","DTL","DBR","DBL","NONE","NONE","BCS","BCS","TOP","BOT","NONE","NONE","NONE","NONE","NONE", # 192-207
    "NONE","NONE","NONE","ALL","NONE","NONE","NONE","NONE","NONE","DRS3","DLS3","ULS3","URS3","LEFT","RIGHT","UR2", # 208-223
    "RIGHT2","TOP2","TOP3","UL2","TOP4","RIGHT3","BOT2","TOP5","NONE","NONE","NONE","NONE","NONE","NONE","NONE","NONE", # 224-239
    "DRS4","DLS4","URS4","ULS4","DRS5","DLS5","DEATH2","RIGHT4","LEFT2","URS5","ULS5","DEATH3","ALL2","LEFT3","DwR2","DwL2", # 240-255
]

def get_tile(col, row):
    if col < 0 or col >= width or row < 0 or row >= height:
        return 0
    return tiles[row * width + col]

def get_coll(col, row):
    """Get collision name for tile at (col, row)."""
    gid = get_tile(col, row)
    if gid == 0:
        return "."
    if gid > 256:
        return f"S{gid-257}"  # sprite
    idx = gid - 1
    if idx < len(COLL_NAMES):
        return COLL_NAMES[idx]
    return f"?{idx}"

def world_to_row(worldY):
    """Convert world Y pixel to TMX row."""
    return worldY // 16 + GRR

# Map key Y levels
print("=== TERRAIN CROSS-SECTIONS (CORRECTED Y→ROW MAPPING) ===")
print("Row = worldY//16 + 3")
print()

for y in [64, 80, 96, 112, 128, 144, 160, 176, 192, 208, 224, 240, 256, 272, 288, 304, 320, 336, 352, 368]:
    row = world_to_row(y)
    if row < 0 or row >= height:
        print(f"Y={y:3d} (row={row:2d}): OUT OF BOUNDS (ground FC)")
        continue
    
    # Scan cols 440-580 (X=7040-9280)
    segments = []
    prev_coll = None
    seg_start = 440
    for col in range(440, 581):
        c = get_coll(col, row)
        if c != prev_coll:
            if prev_coll is not None:
                segments.append((seg_start, col - 1, prev_coll))
            seg_start = col
            prev_coll = c
    if prev_coll is not None:
        segments.append((seg_start, 580, prev_coll))
    
    print(f"Y={y:3d} (row={row:2d}):")
    for start, end, c in segments:
        if c == "." and (end - start) > 20:
            print(f"  cols {start}-{end} (X={start*16}-{end*16}): EMPTY")
        elif c != ".":
            print(f"  cols {start}-{end} (X={start*16}-{end*16}): {c}")
    print()

# Detailed vertical slices at key X locations
print("=== VERTICAL SLICE AT X=8100-8200 (cols 506-512) ===")
print("(Where states die at X~8120)")
for col in range(506, 513):
    print(f"\nCol {col} (X={col*16}):")
    for y in range(0, 400, 16):
        row = world_to_row(y)
        if row < 0 or row >= height:
            print(f"  Y={y:3d}: GROUND-FC" if row >= height else f"  Y={y:3d}: OOB-TOP")
            continue
        c = get_coll(col, row)
        if c != ".":
            print(f"  Y={y:3d}: {c} (gid={get_tile(col, row)})")

print("\n\n=== VERTICAL SLICE AT COIN X=9040-9088 (cols 565-568) ===")
for col in range(565, 569):
    print(f"\nCol {col} (X={col*16}):")
    for y in range(0, 400, 16):
        row = world_to_row(y)
        if row < 0 or row >= height:
            if row >= height:
                print(f"  Y={y:3d}: GROUND-FC")
            continue
        c = get_coll(col, row)
        if c != ".":
            print(f"  Y={y:3d}: {c} (gid={get_tile(col, row)})")

# Now look specifically at the corridors near the coin
print("\n\n=== LOWER CORRIDOR TRAVERSABILITY CHECK ===")
print("Checking Y=224-320 for empty/passable tiles from X=7040-9280")
for y in range(224, 336, 16):
    row = world_to_row(y)
    if row >= height:
        print(f"Y={y}: GROUND")
        continue
    empty_ranges = []
    seg_start = None
    for col in range(440, 581):
        c = get_coll(col, row)
        is_empty = (c == ".")
        if is_empty and seg_start is None:
            seg_start = col
        elif not is_empty and seg_start is not None:
            empty_ranges.append((seg_start, col - 1))
            seg_start = None
    if seg_start is not None:
        empty_ranges.append((seg_start, 580))
    
    print(f"Y={y} (row={row}): empty ranges: ", end="")
    for start, end in empty_ranges:
        print(f"X={start*16}-{end*16} ", end="")
    print()

#!/usr/bin/env python3
"""Map the lower corridor terrain from X=7000 to X=9200 in detail.
Focus on what kills states at X~8120 Y=313, and whether there's any
traversable Y-band through the corridor."""

import xml.etree.ElementTree as ET

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()
width = int(root.attrib['width'])  # 843
height = int(root.attrib['height'])  # 27

# Parse terrain layer (first layer)
layer = root.findall('.//layer')[0]
data = layer.find('data').text.strip()
tiles = [int(x) for x in data.split(',')]

GRR = 3  # groundRowsToReserve

def get_tile(col, row):
    """Get tile GID at (col, row) in TMX grid."""
    if col < 0 or col >= width or row < 0 or row >= height:
        return 0
    return tiles[row * width + col]

def tile_type(gid):
    """Convert GID to collision type name."""
    if gid == 0: return "EMPTY"
    if gid <= 256:
        coll_idx = gid - 1
        # Key collision types
        TYPES = {
            0: "EMPTY", 1: "ALL", 2: "DTOP", 3: "DBOT", 
            4: "DLEFT", 5: "DRIGHT", 8: "FC", 14: "DEATH",
            15: "DEATH", 18: "S45UL", 19: "S45UR", 20: "S45DL", 21: "S45DR",
            22: "S22TUL", 23: "S22BUL", 24: "S22TUR", 25: "S22BUR",
            26: "S22TDL", 27: "S22BDL", 28: "S22TDR", 29: "S22BDR"
        }
        return TYPES.get(coll_idx, f"C{coll_idx}")
    elif gid <= 512:
        return f"SPR{gid-257}"
    return f"?{gid}"

# Focus on columns 440-580 (X=7040-9280) and rows corresponding to Y=128-320
# Y = (row + GRR) * 16, so row = Y/16 - GRR
# Y=128: row=5, Y=144: row=6, Y=160: row=7, Y=176: row=8, Y=192: row=9
# Y=224: row=11, Y=240: row=12, Y=256: row=13, Y=272: row=14
# Y=288: row=15, Y=304: row=16, Y=320: row=17

print("=== TERRAIN CROSS-SECTIONS AT KEY Y LEVELS ===")
print("Checking cols 440-580 (X=7040-9280)")
print()

# Check specific Y bands
y_levels = [128, 144, 160, 176, 192, 208, 224, 240, 248, 256, 272, 288, 304, 320]

for y in y_levels:
    row = y // 16 - GRR
    if row < 0 or row >= height:
        print(f"Y={y} (row={row}): OUT OF BOUNDS")
        continue
    
    # Find ranges of each tile type
    col_start = 440
    col_end = 580
    segments = []
    prev_type = None
    seg_start = col_start
    
    for col in range(col_start, col_end + 1):
        gid = get_tile(col, row)
        tt = tile_type(gid)
        if tt != prev_type:
            if prev_type is not None:
                segments.append((seg_start, col - 1, prev_type))
            seg_start = col
            prev_type = tt
    if prev_type is not None:
        segments.append((seg_start, col_end, prev_type))
    
    print(f"Y={y} (row={row}):")
    for start, end, tt in segments:
        if tt == "EMPTY" and (end - start) > 20:
            print(f"  cols {start}-{end} (X={start*16}-{end*16}): {tt}")
        elif tt != "EMPTY":
            print(f"  cols {start}-{end} (X={start*16}-{end*16}): {tt}")
    print()

# Now zoom in on X=8064-8192 (cols 504-512) at all Y levels
print("=== DETAILED TERRAIN AT X=8064-8192 (cols 504-512) ===")
print("This is where floor states die")
for row in range(4, 22):  # Y=112 to Y=384
    y = (row + GRR) * 16
    line = f"Y={y:3d} (r{row:2d}): "
    for col in range(500, 520):
        gid = get_tile(col, row)
        tt = tile_type(gid)
        if tt == "EMPTY":
            line += ". "
        elif tt == "DEATH":
            line += "X "
        elif tt == "ALL":
            line += "# "
        elif tt == "FC":
            line += "F "
        elif tt.startswith("D"):
            line += "D "
        elif tt.startswith("S"):
            line += "/ "
        else:
            line += "? "
    print(line)

print()
print("Legend: . = empty, X = death, # = ALL, F = FC, D = directional, / = slope")
print(f"Columns 500-519 correspond to X={500*16}-{519*16}")

# Check the exact spot where states die: around col 508 (X=8128)
print()
print("=== VERTICAL SLICE AT COLS 504-512 ===")
for col in range(504, 513):
    print(f"\nCol {col} (X={col*16}):")
    for row in range(4, 22):
        y = (row + GRR) * 16
        gid = get_tile(col, row)
        tt = tile_type(gid)
        if tt != "EMPTY":
            print(f"  Y={y} (row={row}): {tt} (gid={gid})")

# Also check around the coin at col 566 (X=9056)
print()
print("=== VERTICAL SLICE AT COIN LOCATION cols 564-568 ===")
for col in range(564, 569):
    print(f"\nCol {col} (X={col*16}):")
    for row in range(4, 22):
        y = (row + GRR) * 16
        gid = get_tile(col, row)
        tt = tile_type(gid)
        if tt != "EMPTY":
            print(f"  Y={y} (row={row}): {tt} (gid={gid})")

import re

with open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\timemachine.tmx', 'r') as f:
    content = f.read()

data_blocks = re.findall(r'<data encoding="csv">\s*([\s\S]*?)\s*</data>', content)

W = 997
H = 27

tvals = [int(v.strip()) for v in data_blocks[0].replace('\n', ',').split(',') if v.strip()]
svals = [int(v.strip()) for v in data_blocks[1].replace('\n', ',').split(',') if v.strip()]

# Show row 17 across a wide range to find any gaps
print("=== ROW 17 TILES (cols 350-395) - Looking for gaps in the floor ===")
for c in range(350, 396):
    idx = 17 * W + c
    v = tvals[idx]
    tid = v - 1 if v > 0 else -1
    if tid < 0:
        print(f"  col={c} pixel_x={c*16}: EMPTY (gap!)")
    else:
        pass  # solid
        
# Also show which cols are empty
print("\nRow 17 tile pattern (cols 350-395):")
line = ""
for c in range(350, 396):
    idx = 17 * W + c
    v = tvals[idx]
    tid = v - 1 if v > 0 else -1
    if tid < 0:
        line += "."
    elif tid == 13:
        line += "F"  # floor
    elif tid == 23:
        line += "P"  # pattern
    elif tid == 28:
        line += "["  # left structure
    elif tid == 26:
        line += "]"  # right structure
    else:
        line += str(tid % 10)
print(f"  cols 350-395: {line}")
print(f"  F=floor(13), P=pattern(23), [=struct(28), ]=struct(26), .=empty")

# Show rows 16-18 across cols 350-395 to see floor structure
print("\n=== ROWS 16-20 TILES (cols 350-395) - Floor and below ===")
for r in range(16, 21):
    line = f"R{r:>2}: "
    for c in range(350, 396):
        idx = r * W + c
        v = tvals[idx]
        tid = v - 1 if v > 0 else -1
        if tid < 0:
            line += "."
        elif tid <= 9:
            line += str(tid)
        else:
            line += chr(ord('A') + (tid - 10) % 26)
        
    print(line)
print("  Legend: .=empty, 1=t1, 2=t2, 5=t5, 6=t6, D=t13, N=t23")

# Now check: is there any gap in the floor between the ship entry and the coin?
# Look at rows 16-17 for continuous floor coverage
print("\n=== GAP ANALYSIS: Row 17 tiles from col 340 to 390 ===")
gaps = []
for c in range(340, 391):
    idx = 17 * W + c
    v = tvals[idx]
    if v == 0:
        gaps.append(c)
if gaps:
    print(f"  GAPS found at columns: {gaps}")
    print(f"  Gap pixel X ranges:")
    for c in gaps:
        print(f"    col {c}: X={c*16}-{c*16+16}")
else:
    print(f"  NO GAPS in floor at row 17 between cols 340-390")

# Check rows 16 too
print("\n=== GAP ANALYSIS: Row 16 tiles from col 340 to 390 ===")
gaps16 = []
for c in range(340, 391):
    idx = 16 * W + c
    v = tvals[idx]
    if v == 0:
        gaps16.append(c)
if gaps16:
    print(f"  GAPS found at columns: {gaps16}")
else:
    print(f"  NO GAPS at row 16")

# Now look at the full column at col 368 from top to bottom
print("\n=== COLUMN 368 (coin X) - ALL ROWS ===")
for r in range(H):
    tidx = r * W + 368
    sidx = r * W + 368
    tv = tvals[tidx]
    sv = svals[sidx]
    tid = tv - 1 if tv > 0 else -1
    sid = sv - 257 if sv >= 257 else (sv if sv > 0 else -1)
    tstr = f"tile={tid:>3}" if tid >= 0 else "tile=  ."
    sstr = f"spr=0x{sid:02X}" if sid >= 0 else "spr=  ."
    print(f"  row={r:>2} Y={r*16:>3}-{r*16+16:>3}: {tstr}  {sstr}")

# What type of tile is tile 13? Let's look at what tiles could be solid
# Check the structure around cols 377-382 where there's a transition at row 17
print("\n=== TRANSITION AREA at cols 377-385, rows 16-20 ===")
for r in range(15, 22):
    line = f"R{r:>2}: "
    for c in range(377, 386):
        idx = r * W + c
        v = tvals[idx]
        tid = v - 1 if v > 0 else -1
        line += f" {tid:>3}" if tid >= 0 else "   ."
    sv_line = f"     "
    for c in range(377, 386):
        idx = r * W + c
        v = svals[idx]
        sid = v - 257 if v >= 257 else -1
        sv_line += f" x{sid:02X}" if sid >= 0 else "   ."
    print(line)
    print(sv_line)

# Look for any mode portals or gravity changes near cols 355-385
print("\n=== SPRITES (all types) cols 340-395, rows 15-26 ===")
for r in range(15, 27):
    for c in range(340, 396):
        idx = r * W + c
        v = svals[idx]
        if v > 0:
            sid = v - 257 if v >= 257 else v
            print(f"  col={c} row={r} sid=0x{sid:02X} px=({c*16},{r*16})")

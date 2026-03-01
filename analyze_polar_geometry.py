"""Analyze polargeist.tmx tile geometry from col 480 to col 600, rows 20-26."""
import xml.etree.ElementTree as ET

tmx_path = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx"

tree = ET.parse(tmx_path)
root = tree.getroot()

map_width = int(root.attrib['width'])
map_height = int(root.attrib['height'])
print(f"Map dimensions: {map_width} x {map_height}")

# Find the tile layer
layer = root.find(".//layer/data")
csv_text = layer.text.strip()

# Parse all tiles into a 2D array
all_tiles = []
for line in csv_text.split('\n'):
    row_tiles = [int(t.strip()) for t in line.strip().rstrip(',').split(',') if t.strip()]
    all_tiles.extend(row_tiles)

print(f"Total tiles parsed: {len(all_tiles)}")
print(f"Expected: {map_width * map_height}")

# Build 2D grid
grid = []
for r in range(map_height):
    row_data = all_tiles[r * map_width : (r + 1) * map_width]
    grid.append(row_data)

# Collision type mapping
SOLID_TILES = {1, 49, 50, 55, 56, 63}
DEATH_TILES = {13, 14, 15, 16, 17, 18, 26}
PLATFORM_TILES = {2, 25}

def tile_type(tid):
    if tid == 0:
        return "air"
    if tid in SOLID_TILES:
        return "SOLID"
    if tid in DEATH_TILES:
        return "DEATH"
    if tid in PLATFORM_TILES:
        return "PLATFORM"
    return f"other({tid})"

# ============================================================
# 1. Print EVERY non-zero tile from col 480 to col 600, rows 20-26
# ============================================================
print("\n" + "="*80)
print("1. ALL NON-ZERO TILES: cols 480-600, rows 20-26")
print("="*80)
print(f"{'(row,col)':<15} {'tileID':<8} {'type':<15} {'gameX':<8} {'gameY'}")

nonzero_count = 0
for r in range(20, 27):
    for c in range(480, 601):
        if c < map_width:
            tid = grid[r][c]
            if tid != 0:
                game_x = c * 16
                game_y = (r - 3) * 16
                print(f"({r},{c})".ljust(15) + f"{tid}".ljust(8) + f"{tile_type(tid)}".ljust(15) + f"{game_x}".ljust(8) + f"{game_y}")
                nonzero_count += 1

print(f"\nTotal non-zero tiles in region: {nonzero_count}")

# ============================================================
# 2. Solid walls blocking horizontal movement
# ============================================================
print("\n" + "="*80)
print("2. SOLID WALLS (tiles 1,49,50,55,56,63) at rows 20-26, cols 480-600")
print("="*80)
for r in range(20, 27):
    solids_in_row = []
    for c in range(480, 601):
        if c < map_width:
            tid = grid[r][c]
            if tid in SOLID_TILES:
                solids_in_row.append((c, tid))
    if solids_in_row:
        print(f"  Row {r} (gameY={(r-3)*16}): {solids_in_row}")

# ============================================================
# 3. Ground-level path analysis (row 26)
# ============================================================
print("\n" + "="*80)
print("3. ROW 26 (ground level, gameY=368) ANALYSIS: cols 480-600")
print("="*80)
print("Column scan of row 26:")
for c in range(480, 601):
    if c < map_width:
        tid = grid[r][c]  # Oops, fix this
for c in range(480, 601):
    if c < map_width:
        tid = grid[26][c]
        if tid != 0:
            print(f"  col {c} (gameX={c*16}): tile={tid} ({tile_type(tid)})")

# Check continuous ground path
print("\nGround path continuity (row 26):")
segments = []
seg_start = None
prev_type = None
for c in range(480, 601):
    if c < map_width:
        tid = grid[26][c]
        cur_type = "empty" if tid == 0 else tile_type(tid)
        if cur_type != prev_type:
            if seg_start is not None:
                segments.append((seg_start, c-1, prev_type))
            seg_start = c
            prev_type = cur_type
if seg_start is not None:
    segments.append((seg_start, 600, prev_type))

for start, end, stype in segments:
    print(f"  cols {start}-{end} ({end-start+1} tiles): {stype}")

# ============================================================
# 4. Impassable columns at ground level
# ============================================================
print("\n" + "="*80)
print("4. IMPASSABLE COLUMNS AT GROUND LEVEL (row 25+26 combined)")
print("="*80)
for c in range(480, 601):
    if c < map_width:
        t25 = grid[25][c]
        t26 = grid[26][c]
        blocking = []
        if t25 in SOLID_TILES:
            blocking.append(f"row25=SOLID({t25})")
        if t25 in DEATH_TILES:
            blocking.append(f"row25=DEATH({t25})")
        if t26 in SOLID_TILES:
            blocking.append(f"row26=SOLID({t26})")
        if t26 in DEATH_TILES:
            blocking.append(f"row26=DEATH({t26})")
        if blocking:
            print(f"  col {c} (gameX={c*16}): {', '.join(blocking)}")

# ============================================================
# 5. Platforms between rows 20-25
# ============================================================
print("\n" + "="*80)
print("5. PLATFORMS (rows 20-25) that form staircase route")
print("="*80)
for r in range(20, 26):
    platforms = []
    for c in range(480, 601):
        if c < map_width:
            tid = grid[r][c]
            if tid in PLATFORM_TILES or tid in SOLID_TILES:
                platforms.append((c, tid, tile_type(tid)))
    if platforms:
        print(f"  Row {r} (gameY={(r-3)*16}):")
        # Group into contiguous segments
        segs = []
        seg = [platforms[0]]
        for i in range(1, len(platforms)):
            if platforms[i][0] == platforms[i-1][0] + 1:
                seg.append(platforms[i])
            else:
                segs.append(seg)
                seg = [platforms[i]]
        segs.append(seg)
        for s in segs:
            cols = [x[0] for x in s]
            tids = [x[1] for x in s]
            print(f"    cols {cols[0]}-{cols[-1]} ({len(cols)} tiles): tileIDs={set(tids)}")

# ============================================================
# 6. Vertical cross-section at key columns
# ============================================================
print("\n" + "="*80)
print("6. VERTICAL CROSS-SECTIONS at key columns (rows 15-26)")
print("="*80)
key_cols = [490, 500, 510, 520, 530, 540, 550, 560, 570, 580, 585, 590, 591, 592, 593, 594, 595, 600]
for c in key_cols:
    if c < map_width:
        print(f"\n  Column {c} (gameX={c*16}):")
        for r in range(15, 27):
            tid = grid[r][c]
            if tid != 0:
                print(f"    row {r} (gameY={(r-3)*16}): tile={tid} ({tile_type(tid)})")

# ============================================================
# 7. Row-by-row visual map of cols 560-600
# ============================================================
print("\n" + "="*80)
print("7. VISUAL MAP: rows 18-26, cols 570-600")
print("="*80)
print("    ", end="")
for c in range(570, 601):
    print(f"{c%100:3d}", end="")
print()
for r in range(18, 27):
    print(f"r{r:2d} ", end="")
    for c in range(570, 601):
        if c < map_width:
            tid = grid[r][c]
            if tid == 0:
                print("  .", end="")
            elif tid in SOLID_TILES:
                print(f" #{tid%100:1d}", end="")
            elif tid in DEATH_TILES:
                print(f" X{tid%10:1d}", end="")
            elif tid in PLATFORM_TILES:
                print(f" ={tid%10:1d}", end="")
            else:
                print(f"{tid:3d}", end="")
        else:
            print("   ", end="")
    print()

# Also show a wider view for context
print("\n" + "="*80)
print("8. VISUAL MAP: rows 20-26, cols 480-600 (wider)")
print("="*80)
# Print in chunks of 40 columns for readability
for chunk_start in range(480, 601, 40):
    chunk_end = min(chunk_start + 40, 601)
    print(f"\n--- Cols {chunk_start}-{chunk_end-1} ---")
    print("    ", end="")
    for c in range(chunk_start, chunk_end):
        print(f"{c%100:3d}", end="")
    print()
    for r in range(20, 27):
        print(f"r{r:2d} ", end="")
        for c in range(chunk_start, chunk_end):
            if c < map_width:
                tid = grid[r][c]
                if tid == 0:
                    print("  .", end="")
                elif tid in SOLID_TILES:
                    print(f" #{tid:1d}", end="")
                elif tid in DEATH_TILES:
                    print(f" X{tid%10:1d}", end="")
                elif tid in PLATFORM_TILES:
                    print(f" ={tid%10:1d}", end="")
                else:
                    print(f"{tid:3d}", end="")
            else:
                print("   ", end="")
        print()

# ============================================================
# 9. Death tiles analysis
# ============================================================
print("\n" + "="*80)
print("9. DEATH TILE LOCATIONS: cols 480-600, rows 15-26")
print("="*80)
for r in range(15, 27):
    deaths = []
    for c in range(480, 601):
        if c < map_width:
            tid = grid[r][c]
            if tid in DEATH_TILES:
                deaths.append((c, tid))
    if deaths:
        print(f"  Row {r} (gameY={(r-3)*16}): {deaths}")

# ============================================================
# 10. Summary: shortest bridge at ground level
# ============================================================
print("\n" + "="*80)
print("10. PATH ANALYSIS: Can ground level (row 26) reach col 592?")
print("="*80)
# Walk from left checking for obstacles
print("Walking row 26 from col 480 to col 600:")
first_obstacle = None
for c in range(480, 601):
    if c < map_width:
        tid = grid[26][c]
        if tid in SOLID_TILES:
            if first_obstacle is None:
                first_obstacle = c
            print(f"  SOLID WALL at col {c} (gameX={c*16}), tile={tid}")
        elif tid in DEATH_TILES:
            print(f"  DEATH at col {c} (gameX={c*16}), tile={tid}")

# Also check row 25 (one above ground)
print("\nWalking row 25 from col 480 to col 600:")
for c in range(480, 601):
    if c < map_width:
        tid = grid[25][c]
        if tid in SOLID_TILES or tid in DEATH_TILES:
            print(f"  {'SOLID' if tid in SOLID_TILES else 'DEATH'} at col {c} (gameX={c*16}), tile={tid}")

# Check what the player stands on at row 26 (look for the ground tile)
print("\n\nLooking for ground/floor tiles at row 26:")
ground_stretches = []
cur_start = None
for c in range(400, 650):
    if c < map_width:
        tid = grid[26][c]
        is_ground = tid in SOLID_TILES or tid in PLATFORM_TILES
        if is_ground and cur_start is None:
            cur_start = c
        elif not is_ground and cur_start is not None:
            ground_stretches.append((cur_start, c-1))
            cur_start = None
if cur_start is not None:
    ground_stretches.append((cur_start, min(649, map_width-1)))

for s, e in ground_stretches:
    print(f"  Ground: cols {s}-{e} (gameX {s*16}-{e*16}), length={e-s+1}")

print("\nDone!")

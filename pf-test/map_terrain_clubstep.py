import re
import traceback

OUTPUT_PATH = r"C:\Editor Test\pf-test\terrain_output.txt"
out = open(OUTPUT_PATH, "w", encoding="utf-8")

def P(*args, **kwargs):
    kwargs['file'] = out
    print(*args, **kwargs)
    out.flush()

try:
    # === 1. Parse collision table from MetatileCollision.cs ===
    cs_path = r"C:\Editor Test\native-windows\MetatileCollision.cs"
    with open(cs_path, 'r') as f:
        cs_text = f.read()

    match = re.search(r'string mappingText = @"(.*?)";', cs_text, re.DOTALL)
    mapping_text = match.group(1)

    lines = [l for l in mapping_text.split('\n') if l.strip()]

    col_rx = re.compile(r'COL_[A-Z0-9_]+')
    collision_table = ['COL_NONE'] * 256
    for idx, raw in enumerate(lines):
        if idx >= 256:
            break
        m = col_rx.search(raw.strip())
        if m:
            collision_table[idx] = m.group(0)

    # === 2. Character mapping ===
    def col_to_char(col_name):
        if col_name == 'COL_NONE':
            return '.'
        if col_name == 'COL_ALL':
            return 'A'
        if col_name == 'COL_TOP':
            return 'T'
        if col_name == 'COL_BOTTOM':
            return 'B'
        if col_name == 'COL_FLOOR_CEIL':
            return 'F'
        if 'DEATH' in col_name:
            return 'D'
        if 'SPIKE' in col_name:
            return 's'
        if 'SLOPE' in col_name:
            return '/'
        return 'X'

    # === 3. Parse CSV files ===
    # Terrain layer: clubstep_.csv (0-based tile indices, 0 = tile 0 which is COL_NONE = empty)
    # Sprite layer: clubstep_SP.csv (-1 = empty, otherwise sprite tile index)
    terrain_csv = r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep_.csv"
    sprite_csv = r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep_SP.csv"

    def parse_csv(path):
        with open(path, 'r') as f:
            text = f.read().strip()
        grid = []
        for line in text.split('\n'):
            row = [int(v) for v in line.strip().split(',') if v.strip() != '']
            grid.append(row)
        return grid

    terrain_grid = parse_csv(terrain_csv)
    sprite_grid = parse_csv(sprite_csv)

    map_height = len(terrain_grid)
    map_width = len(terrain_grid[0]) if terrain_grid else 0
    P(f"Map dimensions: {map_width} x {map_height}")

    # === 4. Output terrain map ===
    col_start, col_end = 400, 465
    row_start, row_end = 20, 38
    ground_rows_reserve = 3

    P("")
    P("=" * 80)
    P("TERRAIN MAP: Clubstep columns 400-465, TMX rows 20-38")
    P("groundRowsToReserve = 3, so worldTileY = tmxRow - 3")
    P("=" * 80)

    # Column header
    P("")
    hdr = "         "
    for c in range(col_start, col_end + 1):
        if c % 10 == 0:
            hdr += str(c // 100)
        else:
            hdr += " "
    P(hdr + "  (hundreds)")

    hdr = "         "
    for c in range(col_start, col_end + 1):
        if c % 10 == 0:
            hdr += str((c // 10) % 10)
        else:
            hdr += " "
    P(hdr + "  (tens)")

    hdr = "tmxR wY  "
    for c in range(col_start, col_end + 1):
        hdr += str(c % 10)
    P(hdr + "  (ones)")

    hdr = "---- --  "
    for c in range(col_start, col_end + 1):
        if c % 10 == 0:
            hdr += "|"
        elif c % 5 == 0:
            hdr += "+"
        else:
            hdr += "-"
    P(hdr)

    for tmx_row in range(row_start, row_end + 1):
        world_y = tmx_row - ground_rows_reserve
        line = f"{tmx_row:4d} {world_y:2d}  "
        for col in range(col_start, col_end + 1):
            tile_idx = terrain_grid[tmx_row][col]
            # In CSV format: tile_idx is 0-based. Tile 0 = COL_NONE (empty visual)
            if 0 <= tile_idx < 256:
                col_type = collision_table[tile_idx]
                ch = col_to_char(col_type)
            else:
                ch = '.'  # invalid/empty
            line += ch
        P(line)

    # === 5. Legend ===
    P("")
    P("LEGEND:")
    P("  . = empty/COL_NONE    A = COL_ALL (solid)     D = death (COL_DEATH*)")
    P("  T = COL_TOP            B = COL_BOTTOM          s = spike collision")
    P("  F = COL_FLOOR_CEIL     / = slope               X = other")

    # === 6. Tile index details ===
    P("")
    P("=" * 80)
    P("TILE INDEX DETAILS (non-COL_NONE terrain tiles in view):")
    P("=" * 80)
    tile_usage = {}
    for tmx_row in range(row_start, row_end + 1):
        for col in range(col_start, col_end + 1):
            tile_idx = terrain_grid[tmx_row][col]
            if 0 <= tile_idx < 256:
                col_type = collision_table[tile_idx]
                if col_type != 'COL_NONE':
                    key = (tile_idx, col_type)
                    tile_usage[key] = tile_usage.get(key, 0) + 1

    for (tile_idx, col_type), count in sorted(tile_usage.items()):
        P(f"  Tile {tile_idx:3d}: {col_type:30s} '{col_to_char(col_type)}' x{count}")

    # === 7. Sprite layer ===
    P("")
    P("=" * 80)
    P("SPRITE LAYER: Non-empty entries in columns 400-465 (all rows)")
    P("(-1 = empty in sprite CSV)")
    P("=" * 80)
    sprite_entries = []
    for tmx_row in range(map_height):
        for col in range(col_start, col_end + 1):
            if col < len(sprite_grid[tmx_row]):
                val = sprite_grid[tmx_row][col]
                if val != -1:
                    world_y = tmx_row - ground_rows_reserve
                    sprite_entries.append((tmx_row, world_y, col, val))

    if sprite_entries:
        P(f"Found {len(sprite_entries)} sprite entries:")
        P(f"  {'tmxRow':>6s} {'worldY':>6s} {'col':>5s} {'spriteIdx':>9s}")
        for tmx_row, world_y, col, val in sprite_entries:
            P(f"  {tmx_row:6d} {world_y:6d} {col:5d} {val:9d}")
    else:
        P("No sprite entries found in this column range.")

    P("")
    P("Done.")

except Exception as e:
    P(f"ERROR: {e}")
    P(traceback.format_exc())

out.close()

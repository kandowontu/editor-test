import xml.etree.ElementTree as ET
import re
import sys
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

    # === 3. Parse TMX ===
    tmx_path = r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep.tmx"
    tree = ET.parse(tmx_path)
    root = tree.getroot()

    map_width = int(root.attrib['width'])
    map_height = int(root.attrib['height'])

    layers = root.findall('layer')
    terrain_layer = layers[0]
    sprite_layer = layers[1] if len(layers) > 1 else None

    def parse_layer_csv(layer_elem):
        data_elem = layer_elem.find('data')
        csv_text = data_elem.text.strip()
        all_values = [int(v) for v in csv_text.replace('\n', ',').split(',') if v.strip()]
        grid = []
        for r in range(map_height):
            row = all_values[r * map_width : (r + 1) * map_width]
            grid.append(row)
        return grid

    terrain_grid = parse_layer_csv(terrain_layer)
    sprite_grid = parse_layer_csv(sprite_layer) if sprite_layer is not None else None

    # === 4. Output terrain map ===
    col_start, col_end = 400, 465
    row_start, row_end = 20, 38
    ground_rows_reserve = 3

    P("=" * 80)
    P("TERRAIN MAP: Clubstep columns 400-465, rows 20-38")
    P("groundRowsToReserve = 3, so worldTileY = tmxRow - 3")
    P("=" * 80)

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
            gid = terrain_grid[tmx_row][col]
            if gid == 0:
                ch = '.'
            else:
                tile_idx = gid - 1
                if tile_idx < 256:
                    col_type = collision_table[tile_idx]
                    ch = col_to_char(col_type)
                else:
                    ch = '?'
            line += ch
        P(line)

    P("")
    P("LEGEND:")
    P("  . = empty/COL_NONE")
    P("  A = COL_ALL (solid)")
    P("  D = death (COL_DEATH*)")
    P("  T = COL_TOP (one-way top)")
    P("  B = COL_BOTTOM")
    P("  s = spike collision")
    P("  F = COL_FLOOR_CEIL")
    P("  / = slope")
    P("  X = other (directional, stairs, etc.)")
    P("  ? = GID from sprites tileset (>=257)")

    P("")
    P("=" * 80)
    P("TILE INDEX DETAILS (non-empty terrain tiles in view):")
    P("=" * 80)
    tile_usage = {}
    for tmx_row in range(row_start, row_end + 1):
        for col in range(col_start, col_end + 1):
            gid = terrain_grid[tmx_row][col]
            if gid != 0:
                tile_idx = gid - 1
                col_type = collision_table[tile_idx] if tile_idx < 256 else "SPRITE_TILESET"
                key = (tile_idx, col_type)
                if key not in tile_usage:
                    tile_usage[key] = 0
                tile_usage[key] += 1

    for (tile_idx, col_type), count in sorted(tile_usage.items()):
        P(f"  Tile {tile_idx:3d} (GID {tile_idx+1:3d}): {col_type:30s} '{col_to_char(col_type)}' x{count}")

    P("")
    P("=" * 80)
    P("SPRITE LAYER (layer 1): Non-zero entries in columns 400-465, all rows")
    P("=" * 80)
    if sprite_grid is not None:
        sprite_entries = []
        for tmx_row in range(map_height):
            for col in range(col_start, col_end + 1):
                gid = sprite_grid[tmx_row][col]
                if gid != 0:
                    world_y = tmx_row - ground_rows_reserve
                    sprite_entries.append((tmx_row, world_y, col, gid))

        if sprite_entries:
            P(f"Found {len(sprite_entries)} sprite entries:")
            P(f"  {'tmxRow':>6s} {'worldY':>6s} {'col':>5s} {'GID':>5s} {'localID':>7s} {'tileset':>10s}")
            for tmx_row, world_y, col, gid in sprite_entries:
                if gid >= 257:
                    tileset = "sprites"
                    local_id = gid - 257
                else:
                    tileset = "famidash"
                    local_id = gid - 1
                P(f"  {tmx_row:6d} {world_y:6d} {col:5d} {gid:5d} {local_id:7d} {tileset:>10s}")
        else:
            P("No sprite entries found in this column range.")
    else:
        P("No sprite layer found in TMX.")

    P("")
    P("Done.")

except Exception as e:
    P(f"ERROR: {e}")
    P(traceback.format_exc())

out.close()

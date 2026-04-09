import xml.etree.ElementTree as ET

tmx = r'c:\Editor Test\native-windows\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\deathmoon.tmx'
tree = ET.parse(tmx)
root = tree.getroot()
map_w = int(root.get('width'))
map_h = int(root.get('height'))

layer = root.findall('.//layer')[0]
data = layer.find('data')
all_tiles = []
for val in data.text.strip().replace('\n', ',').split(','):
    val = val.strip()
    if val:
        all_tiles.append(int(val))

ground = 3

# Collision type lookup (metatile IDs from TMX, subtract firstgid=1)
col_names = {
    0: "COL_NONE", 1: "COL_FLOOR_CEIL", 2: "COL_FLOOR_CEIL", 3: "COL_BOTTOM",
    4: "COL_DEATH_TOP", 5: "COL_FLOOR_CEIL", 6: "COL_FLOOR_CEIL",
}

# Wave dying at X~19929, Y~406 and Y~518-520
# Tile columns: 19929/16 = 1245
# Y=406: row 406/16=25, arrayRow=25+3=28
# Y=518: row 518/16=32, arrayRow=32+3=35

print("=== Tiles at wave death area ===")
print(f"X~19929px, cols 1243-1248")
print()

for array_row in range(24, 40):
    visual_row = array_row - ground
    y_start = visual_row * 16
    line = f"ArrRow={array_row:2d} (vis={visual_row:2d}, Y={y_start:3d}-{y_start+15:3d}): "
    tiles_in_row = []
    for col in range(1243, 1249):
        idx = array_row * map_w + col
        if idx < len(all_tiles):
            t = all_tiles[idx]
            # TMX GID 1 = tile 0
            tile_id = t - 1 if t > 0 else -1
            tiles_in_row.append(f"{tile_id:3d}")
        else:
            tiles_in_row.append("OOB")
    line += " ".join(tiles_in_row)
    print(line)

# Also check the specific collision probe area
print()
print("=== Detailed check around wave corridor ===")
for y_px in [400, 404, 406, 408, 416, 500, 510, 516, 518, 520, 528, 536]:
    tile_row = y_px // 16
    arr_row = tile_row + ground
    col = 1245
    idx = arr_row * map_w + col
    if idx < len(all_tiles):
        t = all_tiles[idx]
        tile_id = t - 1 if t > 0 else -1
        cname = col_names.get(tile_id, f"id={tile_id}")
        print(f"Y={y_px:3d}: row={tile_row}, arrRow={arr_row}, tile_id={tile_id} ({cname})")

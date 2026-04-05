"""Check collision tiles at col 408-412 for all screen rows"""
import xml.etree.ElementTree as ET

tree = ET.parse(r"c:\Editor Test\eon2.tmx")
root = tree.getroot()

map_width = int(root.get("width"))
map_height = int(root.get("height"))
print(f"Map: {map_width}x{map_height}")

# Find the collision layer (first layer, usually "bg" or similar)
layers = root.findall(".//layer")
for layer in layers:
    name = layer.get("name", "?")
    data = layer.find("data")
    if data is None:
        continue
    tiles = [int(x) for x in data.text.strip().split(",")]
    print(f"\nLayer '{name}': {len(tiles)} tiles")
    
    # Check cols 405-415 for rows 15-25 (covers the death zone area)
    ground_rows_reserve = 3
    print(f"\n{'Row':>4} {'scrY':>5} {'arrR':>4}", end="")
    for c in range(405, 416):
        print(f" c{c:>3}", end="")
    print()
    
    for arr_row in range(15, 27):
        scr_row = arr_row - ground_rows_reserve
        scr_y = scr_row * 16
        print(f"{scr_row:>4} {scr_y:>5} {arr_row:>4}", end="")
        for c in range(405, 416):
            idx = arr_row * map_width + c
            if idx < len(tiles):
                gid = tiles[idx]
                # GID 0 = empty, GID >= 1 = tile (TilesFirstGid=1, so tid = GID-1)
                if gid == 0:
                    print("    .", end="")
                else:
                    tid = gid - 1  # tile index
                    print(f" {tid:>3}", end="")
            else:
                print("  OOB", end="")
        print()
    
    # Only process first layer
    break

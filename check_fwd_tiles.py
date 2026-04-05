import xml.etree.ElementTree as ET

tree = ET.parse("eon2.tmx")
root = tree.getroot()

# Find the collision layer
for layer in root.findall(".//layer"):
    name = layer.get("name", "")
    if name.lower() in ("collision", "metatiles", "bg", "background"):
        data = layer.find("data")
        if data is not None:
            width = int(layer.get("width"))
            height = int(layer.get("height"))
            tiles_text = data.text.strip()
            tiles = [int(x) for x in tiles_text.split(",")]
            
            print(f"Layer: {name}, {width}x{height}")
            
            # Check columns 404-420, rows 0-12
            print("\nTile IDs at cols 404-420, rows 0-12:")
            print("     ", " ".join(f"c{c:3d}" for c in range(404, 421)))
            for r in range(min(13, height)):
                row_tiles = []
                for c in range(404, 421):
                    if c < width:
                        idx = r * width + c
                        tid = tiles[idx] if idx < len(tiles) else 0
                        row_tiles.append(f" {tid:3X}")
                    else:
                        row_tiles.append("  --")
                print(f"r{r:2d}: ", " ".join(row_tiles))
            
            # Check what collision types are at the right edge X=6624 (col 414)
            print(f"\nCol 414 (X=6624, right edge of player at X=6609):")
            for r in range(min(height, 15)):
                idx = r * width + 414
                if idx < len(tiles):
                    tid = tiles[idx]
                    print(f"  row {r}: tile 0x{tid:02X}")
            
            break

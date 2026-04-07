"""Analyze the terrain around X=6000-6200 in eighto.tmx."""
import struct, sys

TMX_PATH = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\eighto.tmx"

# Parse TMX to get tiles/sprites/map dimensions
with open(TMX_PATH, 'rb') as f:
    content = f.read()

# Find layer data - look for CSV format in the TMX
import xml.etree.ElementTree as ET
tree = ET.parse(TMX_PATH)
root = tree.getroot()

width = int(root.get('width'))
height = int(root.get('height'))
print(f"Map: {width}x{height} tiles")

layers = root.findall('.//layer')
for layer in layers:
    name = layer.get('name', 'unnamed')
    data = layer.find('data')
    if data is not None:
        encoding = data.get('encoding', 'csv')
        if encoding == 'csv':
            tiles_str = data.text.strip()
            tiles = [int(x) for x in tiles_str.split(',')]
        elif encoding == 'base64':
            import base64, zlib
            raw = base64.b64decode(data.text.strip())
            compression = data.get('compression')
            if compression == 'zlib':
                raw = zlib.decompress(raw)
            elif compression == 'gzip':
                import gzip
                raw = gzip.decompress(raw)
            tiles = list(struct.unpack(f'<{len(raw)//4}I', raw))
        else:
            continue
        
        print(f"\nLayer '{name}': {len(tiles)} tiles")
        
        if name.lower() in ('tiles', 'tile', 'terrain', 'bg', 'background', 'tile layer 1', 'unnamed'):
            # Y range: player Y pixels 516-529 → tile rows 32-33 → array rows 35-36 (with 3 ground rows)
            # But also need to see the ceiling that the UFO should ride
            # Player Y ~520-530px → tiles 32-33, array 35-36
            # Show wider range to see full corridor
            print(f"\nTerrain at tile X=370-400 (pixels 5920-6400), Y rows 0-{min(height, 47)-1}:")
            for y in range(0, min(height, 47)):
                row = ""
                has_nonzero = False
                for x in range(370, min(width, 400)):
                    idx = y * width + x
                    if idx < len(tiles):
                        t = tiles[idx] & 0xFF
                        if t == 0:
                            row += ".."
                        else:
                            row += f"{t:02X}"
                            has_nonzero = True
                    else:
                        row += "??"
                    row += " "
                if has_nonzero:
                    print(f"  y={y:2d}: {row}")
        
        if name.lower() in ('sprites', 'sprite', 'objects', 'sprite layer 1', 'sp'):
            # Show sprites around X=375-390
            print(f"\nSprites at tile X=370-395, Y rows 28-36:")
            for y in range(28, min(height, 36)):
                row = ""
                for x in range(370, min(width, 395)):
                    idx = y * width + x
                    if idx < len(tiles):
                        t = tiles[idx] & 0xFF
                        if t == 0:
                            row += " .. "
                        else:
                            row += f" {t:02X} "
                    else:
                        row += " ?? "
                print(f"  y={y:2d}: {row}")

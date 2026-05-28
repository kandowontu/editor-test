"""Probe tile collision around ball-flip divergence X=6216 Y=240 (world)."""
import xml.etree.ElementTree as ET, base64, zlib, struct, csv

tmx = r"c:\Editor Test\famidash\levels\LEVEL DATA\lvlset_HUGE\silentcircles.tmx"
tree = ET.parse(tmx)
root = tree.getroot()
W = int(root.get('width'))
H = int(root.get('height'))
print(f"map {W}x{H}")

# Find main layer
for layer in root.findall('layer'):
    name = layer.get('name')
    data = layer.find('data')
    enc = data.get('encoding')
    comp = data.get('compression')
    if enc != 'base64':
        # CSV
        text = data.text.strip()
        tiles = [int(t) for t in text.replace('\n', '').split(',') if t.strip()]
        print(f"\nLayer '{name}' CSV len={len(tiles)}")
        px_world = 6216
        py_world = 240
        tx = px_world // 16
        ty = py_world // 16
        print(f"player tile ({tx},{ty})")
        for dy in range(-3, 4):
            row = []
            for dx in range(-2, 6):
                x = tx + dx
                y = ty + dy
                if 0 <= x < W and 0 <= y < H:
                    t = tiles[y * W + x] & 0x1FFFFFFF
                    row.append(f"{t:4d}")
                else:
                    row.append(" -- ")
            print(f"  y={ty+dy:3d}: " + " ".join(row))
        continue
    raw = base64.b64decode(data.text.strip())
    if comp == 'zlib':
        raw = zlib.decompress(raw)
    elif comp == 'gzip':
        import gzip
        raw = gzip.decompress(raw)
    tiles = struct.unpack('<' + 'I' * (len(raw) // 4), raw)
    # show area around player
    px_world = 6216
    py_world = 240
    tx = px_world // 16
    ty = py_world // 16
    print(f"\nLayer '{name}': player tile ({tx},{ty})")
    for dy in range(-3, 4):
        row = []
        for dx in range(-2, 6):
            x = tx + dx
            y = ty + dy
            if 0 <= x < W and 0 <= y < H:
                t = tiles[y * W + x] & 0x1FFFFFFF
                row.append(f"{t:3d}")
            else:
                row.append(" - ")
        print(f"  y={ty+dy:3d}: " + " ".join(row))

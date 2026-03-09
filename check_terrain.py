import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\cycles.tmx')
root = tree.getroot()

# Find terrain around coin 2 at col=517, row=19
for layer in root.findall('layer'):
    name = layer.get('name', '')
    data = layer.find('data').text.strip()
    tiles = [int(x) for x in data.split(',')]
    w = int(layer.get('width'))
    h = int(layer.get('height'))
    
    if name == '':  # terrain layer
        print("=== TERRAIN around coin (col=517 row=19) ===")
        print("Cols 510-525, rows 14-24:")
        header = "     " + " ".join(f"{c:>3}" for c in range(510, 526))
        print(header)
        for r in range(14, min(25, h)):
            row_data = []
            for c in range(510, 526):
                idx = r * w + c
                t = tiles[idx] if idx < len(tiles) else 0
                row_data.append(f"{t:>3}" if t > 0 else "  .")
            print(f"r{r:>2}: " + " ".join(row_data))
    
    if name == 'SP':  # sprites layer
        print("\n=== SPRITES around coin (col=517 row=19) ===")
        print("Cols 510-525, rows 14-24:")
        header = "     " + " ".join(f"{c:>3}" for c in range(510, 526))
        print(header)
        for r in range(14, min(25, h)):
            row_data = []
            for c in range(510, 526):
                idx = r * w + c
                t = tiles[idx] if idx < len(tiles) else 0
                sid = t - 257 if t >= 257 else -1
                if sid > 0:
                    row_data.append(f"{sid:>3x}")
                elif t > 0:
                    row_data.append(f"t{t:>2}")
                else:
                    row_data.append("  .")
            print(f"r{r:>2}: " + " ".join(row_data))

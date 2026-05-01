import xml.etree.ElementTree as ET
tree = ET.parse(r"native-windows\famidash\LEVELS\LEVEL DATA\lvlset_C\hi.tmx")
root = tree.getroot()
layers = [l for l in root.findall('layer') if l.get('name') != 'Image Layer 2']
layer = layers[0]
w = int(layer.get('width')); h = int(layer.get('height'))
data = [int(x) for x in layer.find('data').text.strip().split(',')]
GRR = 3

# Print full tile map for the corridor region cols 185-225, arrY=24-35
print('Full corridor tile map cols 185-225, arrY=24-35 (worldY = (arrY-3)*16):')
print(f"{'arrY':>4} {'worldY':>6} | ", end="")
for col in range(185, 226):
    print(f"{col:>4}", end="")
print()
print("-" * (12 + 41*4))

for arrY in range(24, 36):
    worldY = (arrY - GRR) * 16
    print(f"{arrY:>4} {worldY:>6} | ", end="")
    for col in range(185, 226):
        idx = arrY * w + col
        if 0 <= idx < len(data):
            t = data[idx] - 1 if data[idx] > 0 else -1
            if t == -1:
                print(f"{'__':>4}", end="")
            else:
                print(f"{t:>4}", end="")
        else:
            print(f"{'??':>4}", end="")
    print()

print()
print("Key tiles (collision categories):")
# Tile 47 = COL_NONE, tiles 33-52 area, need to know which are COL_ALL vs other
# Let me check a few specific tiles
interesting = set()
for arrY in range(24, 36):
    for col in range(185, 226):
        idx = arrY * w + col
        if 0 <= idx < len(data):
            t = data[idx] - 1 if data[idx] > 0 else -1
            if t > 0:
                interesting.add(t)
print(f"Tile IDs present: {sorted(interesting)}")

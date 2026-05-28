"""Probe aftermath tilemap around X=4080-4140 Y=290-340."""
import xml.etree.ElementTree as ET

tmx = r"c:\Editor Test\famidash\levels\LEVEL DATA\lvlset_HUGE\aftermath.tmx"
tree = ET.parse(tmx)
root = tree.getroot()
W = int(root.get('width'))
H = int(root.get('height'))
print(f"map {W}x{H}")

for layer in root.findall('layer'):
    name = layer.get('name')
    data = layer.find('data')
    text = data.text.strip()
    tiles = [int(t) for t in text.replace('\n','').split(',') if t.strip()]
    if len(tiles) != W*H:
        print(f"  layer {name} size mismatch {len(tiles)}")
        continue
    px=4094; py=300
    tx0 = px // 16
    ty0 = py // 16
    print(f"\nLayer '{name}': player center tile ({tx0},{ty0})")
    for dy in range(-2, 5):
        row=[]
        for dx in range(-2, 8):
            x=tx0+dx; y=ty0+dy
            if 0<=x<W and 0<=y<H:
                t = tiles[y*W+x] & 0x1FFFFFFF
                row.append(f"{t:4d}")
            else: row.append("  --")
        marker = " <" if dy==0 else ""
        print(f"  y={ty0+dy:3d}: "+" ".join(row)+marker)

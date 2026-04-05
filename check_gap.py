"""Check tiles at columns 404-414, rows 0-15 (display coords) in eon2.tmx to see the gap above the wall."""
import xml.etree.ElementTree as ET
import csv, io

tree = ET.parse(r"c:\Editor Test\eon2.tmx")
root = tree.getroot()
map_w = int(root.attrib["width"])
map_h = int(root.attrib["height"])

layer = root.findall(".//layer")[0]
text = layer.find("data").text
reader = csv.reader(io.StringIO(text))
tiles = []
for row in reader:
    for cell in row:
        cell = cell.strip()
        if cell:
            tiles.append(int(cell))

ground = 3  # GroundRowsToReserve

# MetatileCollision names for common types
COL_NAMES = {
    0x00: "NONE", 0x01: "FLRCL", 0x02: "DEATH", 0x03: "NOST", 
    0x10: "ALL", 0x11: "DEATH", 0xB6: "LEFT", 0xB7: "RIGHT",
    0xBA: "BL_ST", 0xB2: "TOP",
}

print(f"Map: {map_w}x{map_h}, ground={ground}")
print(f"\nColumns 404-414, display rows -3 to 16 (tileArrayY 0..19):")
print(f"{'':>6}", end="")
for c in range(404, 415):
    print(f" c{c:>3}", end="")
print()

for disp_r in range(-3, 17):
    tay = disp_r + ground
    if tay < 0 or tay >= map_h:
        continue
    print(f"r{disp_r:>3}: ", end="")
    for c in range(404, 415):
        idx = tay * map_w + c
        gid = tiles[idx] if idx < len(tiles) else 0
        tid = (gid - 1) & 0xFF if gid > 1 else 0
        if tid == 0 and gid <= 1:
            print("  -- ", end="")
        else:
            name = COL_NAMES.get(tid, f"{tid:02X}")
            print(f" {name:>4}", end="")
    print(f"  (Y={disp_r*16}..{disp_r*16+15})")

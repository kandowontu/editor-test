import xml.etree.ElementTree as ET
import csv, io

tree = ET.parse(r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep.tmx")
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

layers = root.findall('layer')
sdata = layers[1].find('data')
reader = csv.reader(io.StringIO(sdata.text.strip()))
sprites = []
for row in reader:
    for val in row:
        val = val.strip()
        if val:
            sprites.append(int(val))

# Sprite IDs from the game
sprite_names = {
    0x00: "Spike", 0x01: "Spike2", 0x02: "Spike3", 0x03: "YellowOrb",
    0x04: "PinkPad", 0x05: "YellowPad", 0x06: "BluePad", 0x07: "BlueOrb",
    0x08: "GravPortalDown", 0x09: "GravPortalUp", 0x0A: "MirrorEntry",
    0x0B: "PinkOrb", 0x0C: "GreenOrb", 0x0D: "GreenPad",
    0x0E: "CubePortal", 0x0F: "ShipPortal", 0x10: "BallPortal",
    0x11: "UFOPortal", 0x12: "WavePortal",
    0x13: "MiniPortal", 0x14: "NormalPortal",
}

GROUND_ROWS = 3

# Scan sprites in ball section: cols 400-470
print("=== Sprites in ball section (cols 400-470) ===")
for ar in range(25, 38):
    wty = ar - GROUND_ROWS
    py = wty * 16
    for c in range(400, 471):
        idx = ar * width + c
        gid = sprites[idx]
        if gid != 0:
            sid = gid - 257
            name = sprite_names.get(sid, f"Unknown_0x{sid:02X}")
            px = c * 16
            print(f"  col={c} ({px}px) wTY={wty} ({py}px) sprite=0x{sid:02X} ({name})")

# Focus on death zone sprites: cols 450-470
print("\n=== Sprite map near wall (cols 455-470) ===")
print(f"{'':10s}", end='')
for c in range(455, 471):
    print(f"  c{c:3d}", end='')
print()
for ar in range(25, 38):
    wty = ar - GROUND_ROWS
    row_data = []
    has_sprite = False
    for c in range(455, 471):
        idx = ar * width + c
        gid = sprites[idx]
        if gid != 0:
            sid = gid - 257
            row_data.append(f" s{sid:02X} ")
            has_sprite = True
        else:
            row_data.append("  __ ")
    if has_sprite:
        print(f"wTY{wty:2d}/{wty*16:3d} ", end='')
        for d in row_data:
            print(d, end='')
        print()

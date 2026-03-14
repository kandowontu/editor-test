import xml.etree.ElementTree as ET
import csv, io, re

tree = ET.parse(r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep.tmx")
root = tree.getroot()
width = int(root.attrib['width'])

layers = root.findall('layer')
data = layers[0].find('data')
reader = csv.reader(io.StringIO(data.text.strip()))
tiles = []
for row in reader:
    for val in row:
        val = val.strip()
        if val:
            tiles.append(int(val))

col_src = open(r"C:\Editor Test\native-windows\MetatileCollision.cs").read()
m = re.search(r'mappingText\s*=\s*@"(.*?)"', col_src, re.DOTALL)
col_regex = re.compile(r'COL_[A-Z0-9_]+')
ct = {}
idx = 0
for line in m.group(1).split('\n'):
    line = line.strip()
    if not line: continue
    mm = col_regex.match(line)
    if mm:
        ct[idx] = mm.group()
        idx += 1

def map_tile(tid):
    if tid in (0xFC, 0xDF, 0xE3, 0xFE, 0xFF): return 0
    if tid == 0xFD: return 0x26
    return tid

short = {'COL_NONE': '____', 'COL_ALL': 'ALL_', 'COL_FLOOR_CEIL': 'FC__',
    'COL_BOTTOM': 'BOT_', 'COL_TOP': 'TOP_', 'COL_DEATH': 'DEAD',
    'COL_DEATH_TOP': 'DTOP', 'COL_DEATH_BOTTOM': 'DBOT',
    'COL_DEATH_LEFT': 'DLFT', 'COL_DEATH_RIGHT': 'DRGT',
    'COL_TOP_CENTER_SPIKE': 'TCSP', 'COL_NO_SIDE': 'NOSD'}

GROUND_ROWS = 3

# Show corridor layout for cols 420-470 every 5 columns
for c in range(420, 471, 5):
    print(f"\n--- Col {c} (X={c*16}px) ---")
    for ar in range(25, 38):
        wty = ar - GROUND_ROWS
        py = wty * 16
        idx2 = ar * width + c
        gid = tiles[idx2]
        if gid == 0:
            print(f"  wTY{wty:2d} ({py:3d}px): empty")
        else:
            tid = gid - 1
            mapped = map_tile(tid)
            name = ct.get(mapped, f'UNK_0x{mapped:02X}')
            sn = short.get(name, name[:4])
            print(f"  wTY{wty:2d} ({py:3d}px): {sn}/x{tid:02X} ({name})")

# Sprites
sdata = layers[1].find('data')
reader2 = csv.reader(io.StringIO(sdata.text.strip()))
sprites = []
for row in reader2:
    for val in row:
        val = val.strip()
        if val:
            sprites.append(int(val))

sprite_names = {
    0x00: "Spike", 0x01: "Spike2", 0x02: "Spike3", 0x03: "YellowOrb",
    0x04: "PinkPad", 0x05: "YellowPad", 0x06: "BluePad", 0x07: "BlueOrb",
    0x08: "GravPortalDown", 0x09: "GravPortalUp",
    0x0B: "PinkOrb", 0x0C: "GreenOrb",
    0x0E: "CubePortal", 0x0F: "ShipPortal", 0x10: "BallPortal",
    0x11: "UFOPortal", 0x25: "RedPad",
}

print("\n\n=== All sprites cols 400-470 ===")
for ar in range(22, 40):
    wty = ar - GROUND_ROWS
    py = wty * 16
    for c in range(400, 471):
        idx2 = ar * width + c
        gid = sprites[idx2]
        if gid != 0:
            sid = gid - 257
            name = sprite_names.get(sid, f"Unk_0x{sid:02X}")
            print(f"  col={c} X={c*16}px wTY={wty} Y={py}px: {name} (0x{sid:02X})")

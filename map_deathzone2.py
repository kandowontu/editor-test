import xml.etree.ElementTree as ET
tree = ET.parse(r"c:\Editor Test\eon2.tmx")
root = tree.getroot()
width = int(root.attrib['width'])
layers = root.findall('.//layer')
sp = [int(x) for x in layers[1].find('data').text.strip().split(',')]
tiles = [int(x) for x in layers[0].find('data').text.strip().split(',')]

NAMES = {0x00:'CUBE_PORT',0x01:'SHIP_PORT',0x02:'BALL_PORT',0x03:'UFO_PORT',0x04:'WAVE_PORT',
         0x05:'SPEED05',0x06:'SPEED1',0x07:'SPEED2',0x08:'SPEED3',0x09:'GRAV_DOWN',0x0A:'GRAV_UP',
         0x1F:'JUMP_RING',0x20:'JUMP_PAD',0x24:'BLUE_ORB',0x25:'BLUE_PAD',0x26:'YELLOW_PAD',
         0x27:'GREEN_ORB',0x28:'YELLOW_ORB',0x2B:'PURPLE_ORB',0xF8:'H_BLOCK',0xF9:'F_BLOCK',
         0x22:'MIRROR_ON',0x23:'MIRROR_OFF'}

print("ALL sprites cols 385-420:")
for i, gid in enumerate(sp):
    raw = gid & 0x3FFFFFFF
    if raw >= 257:
        r, c = divmod(i, width)
        if 385 <= c <= 420:
            sid = raw - 257
            name = NAMES.get(sid, f'0x{sid:02X}')
            print(f"  col={c:3d} row={r:2d} X={c*16:5d} Y={r*16:3d} sprite={name}")

COL = {0:'NONE',1:'FL_CL',2:'FL_CL',3:'BOTT',4:'D_TOP',5:'FL_CL',6:'FL_CL',7:'NONE',
       16:'DEATH',17:'TOP',18:'D_BOT',19:'TOP',20:'FL_CL',52:'NONE',
       178:'DN_L',179:'DN_R',180:'TOP',181:'BOTT',182:'LEFT',183:'RIGHT',
       186:'BS_L',187:'BS_R',173:'DL_SP'}

print("\nTerrain cols 398-415, rows 15-22:")
hdr = "        "
for c in range(398,416):
    hdr += f" c{c:3d} "
print(hdr)
for r in range(15, 23):
    Y = r * 16
    line = f"r{r:2d} Y={Y:3d}"
    for c in range(398, 416):
        idx = r * width + c
        gid = tiles[idx] & 0x3FFFFFFF
        if gid == 0:
            name = " --- "
        else:
            tid = gid - 1
            name = COL.get(tid, f"x{tid:02X}")
        line += f" {name:>5s}"
    print(line)

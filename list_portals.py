import xml.etree.ElementTree as ET
tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()
portals = {0x0C:'ship',0x0D:'ball',0x0E:'ufo',0x0F:'wave',0x10:'robot',0x11:'spider',0x12:'swing',0x22:'dual',0x23:'single',0x24:'mini',0x25:'growth',0x26:'gravflip',0x27:'gravnorm'}
for group in root.findall('.//objectgroup'):
    for obj in group.findall('object'):
        gid = obj.get('gid')
        if gid is None:
            continue
        x = float(obj.get('x'))
        y = float(obj.get('y'))
        col = int(x) // 16
        sid = (int(gid) - 257) & 0xFF
        if sid in portals:
            print(f"col={col:4d} X={int(x):5d} Y={int(y):3d} {portals[sid]}")

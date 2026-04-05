import xml.etree.ElementTree as ET
root = ET.parse(r'c:\Editor Test\eon2.tmx').getroot()
# Get sprite layer
layers = root.findall('.//layer')
sp_layer = None
for l in layers:
    if l.get('name') == 'SP':
        sp_layer = l
        break

w = int(root.find('.//layer').get('width'))
h = int(root.find('.//layer').get('height'))

if sp_layer:
    data = sp_layer.find('data')
    csv = data.text.strip()
    gids = [int(x) for x in csv.split(',')]
    print(f"Sprites in X range 6000-6700 (cols 375-419):")
    for i, gid in enumerate(gids):
        if gid > 0:
            row = i // w
            col = i % w
            x_px = col * 16
            if 6000 <= x_px <= 6700:
                sid = (gid - 257) & 0xFF  # sprite firstgid = 257
                print(f"  col={col} row={row} X={x_px} Y={row*16} sid=0x{sid:02X} gid={gid}")

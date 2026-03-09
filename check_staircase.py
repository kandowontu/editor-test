import xml.etree.ElementTree as ET

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_A/stereomadness.tmx')
root = tree.getroot()
w = int(root.attrib['width'])
h = int(root.attrib['height'])

# Collision table mapping (gid-1 = index)
col_names = {0: "NONE", 12: "D_BOT", 24: "D_BOT", 25: "TOP", 33: "ALL", 47: "NONE"}

for layer in root.findall('layer'):
    name = layer.attrib.get('name', '')
    data = layer.find('data')
    vals = [int(x) for x in data.text.strip().split(',')]
    
    if name != 'SP':
        print("=== TILE LAYER '%s' (cols 125-155, rows 19-27) ===" % name)
        header = "row "
        for c in range(125, 156):
            header += "%5d" % c
        print(header)
        for r in range(19, 27):
            line = "%3d " % r
            for c in range(125, 156):
                idx = r * w + c
                gid = vals[idx] if idx < len(vals) else 0
                tile_id = gid - 1 if gid > 0 else -1
                cn = col_names.get(tile_id, str(tile_id))
                line += "%5s" % cn
            print(line)
        print()
    
    if name == 'SP':
        print("=== SPRITE LAYER (cols 125-155, rows 19-27) ===")
        header = "row "
        for c in range(125, 156):
            header += "%5d" % c
        print(header)
        for r in range(19, 27):
            line = "%3d " % r
            for c in range(125, 156):
                idx = r * w + c
                gid = vals[idx] if idx < len(vals) else 0
                sid = gid - 257 if gid >= 257 else 0
                line += "%5d" % sid
            print(line)
        print()

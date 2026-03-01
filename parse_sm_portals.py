import re
from collections import Counter

data = open(r'c:\Editor Test\pf-test\stereomadness.tmx').read()
layers = re.findall(r'<layer.*?<data encoding="csv">\s*(.*?)\s*</data>', data, re.DOTALL)
print(f'Found {len(layers)} layers')

# Game mode portal sprite IDs
gm_sids = {0x00: 'cube', 0x01: 'ship', 0x02: 'ball', 0x03: 'UFO',
            0x04: 'wave', 0x17: 'robot', 0x24: 'spider',
            0x4B: 'mode7', 0x58: 'mode8', 0x6A: 'mode9', 0x6B: 'mode10', 0x6C: 'mode11'}

MAP_W = 895

for li, layer_data in enumerate(layers):
    vals = [int(x) for x in layer_data.replace('\n', ',').split(',') if x.strip()]
    sprites = [v for v in vals if v >= 257]
    nonzero = [v for v in vals if v != 0]
    print(f'\nLayer {li}: total={len(vals)}, sprites(>=257)={len(sprites)}, nonzero={len(nonzero)}')
    
    if sprites:
        c = Counter(sprites)
        for val, cnt in sorted(c.items()):
            sid = val - 257
            gm = gm_sids.get(sid, '')
            print(f'  tile={val} spriteId=0x{sid:02X}({sid}) count={cnt} {gm}')
    
    # Find game mode portals specifically
    portals = []
    for idx, v in enumerate(vals):
        if v >= 257:
            sid = v - 257
            if sid in gm_sids:
                row = idx // MAP_W
                col = idx % MAP_W
                px_x = col * 16
                px_y = row * 16
                portals.append((px_x, px_y, sid, gm_sids[sid], idx))
    
    if portals:
        print(f'  GAME MODE PORTALS:')
        for px_x, px_y, sid, name, idx in sorted(portals):
            print(f'    X={px_x} Y={px_y} spriteId=0x{sid:02X} mode={name} idx={idx}')
    else:
        print(f'  No game mode portals found in this layer')

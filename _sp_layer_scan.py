import xml.etree.ElementTree as ET
fn = r'C:\Editor Test\lvlset_HUGE\cataclysm.tmx'
tree = ET.parse(fn)
root = tree.getroot()

# Find tilesets to map GIDs to sprite IDs
ts_info = []
for ts in root.findall('tileset'):
    firstgid = int(ts.get('firstgid'))
    name = ts.get('name','')
    ts_info.append((firstgid, name))
print('Tilesets:', ts_info)
ts_info.sort()

# Find SP layer
for layer in root.findall('layer'):
    if layer.get('name')!='SP':
        continue
    w = int(layer.get('width'))
    h = int(layer.get('height'))
    data = layer.find('data').text.strip()
    # CSV
    rows = data.split('\n')
    tile = []
    for r in rows:
        for v in r.strip().rstrip(',').split(','):
            v = v.strip()
            if v:
                tile.append(int(v))
    # tile[y*w + x]; check X range 8500-8900 px -> tile col 8500/16=531 to 8900/16=556
    cmin, cmax = 530, 560
    rmin, rmax = 0, h
    print(f'Layer {w}x{h}, scanning cols {cmin}-{cmax}')
    found = []
    for y in range(rmin, rmax):
        for x in range(cmin, cmax):
            gid = tile[y*w+x]
            if gid == 0: continue
            # mask out flip flags
            real = gid & 0x1FFFFFFF
            sid_in_ts = None
            tsn = None
            for fg, name in reversed(ts_info):
                if real >= fg:
                    sid_in_ts = real - fg
                    tsn = name
                    break
            found.append((x, y, sid_in_ts, tsn, real))
    # only sprites tileset
    for x,y,sid,tsn,real in found:
        if tsn == 'sprites':
            print(f'  px=({x*16},{y*16}) tile=({x},{y}) sid=0x{sid:02X} ts={tsn}')

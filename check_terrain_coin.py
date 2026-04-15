import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
map_w = int(root.get('width'))
for layer in root.findall('layer'):
    if layer.get('name') is None:
        bg = list(map(int, layer.find('data').text.strip().split(',')))

CT = [0]*256
CT[1]=1;CT[2]=2;CT[3]=3;CT[4]=4;CT[5]=5;CT[6]=6;CT[7]=0;CT[8]=7;CT[9]=1;CT[0xA]=8
for t in range(0xB,0x1B): CT[t]=2
CT[0x1C]=3;CT[0x1D]=8;CT[0x1E]=2;CT[0x1F]=2
for t in range(0x20,0x26): CT[t]=5
CT[0x27]=5;CT[0x28]=5;CT[0x2E]=5;CT[0x2F]=5;CT[0x30]=5;CT[0x35]=5
CT[0x36]=2;CT[0x37]=2;CT[0x38]=2
CT[0x4F]=9;CT[0x50]=9
N={0:'NONE',1:'FC',2:'ALL',3:'DEATH',4:'D_TOP',5:'D_BOT',6:'D_LEFT',7:'D_RIGHT',8:'TOP',9:'NO_SIDE'}

# Precise terrain near coin: cols 555-575, rows 12-20
print('Precise terrain cols 555-575, rows 12-20:')
for c in range(555, 576):
    X = c * 16
    tiles = []
    for r in range(12, 21):
        idx = r * map_w + c
        tid = bg[idx] & 0xFF
        col = CT[tid]
        nm = N.get(col, '?%d' % col)
        if nm != 'NONE':
            tiles.append('r%d=%s(0x%02X)' % (r, nm, tid))
    if tiles:
        print('  c%d X=%d: %s' % (c, X, ' | '.join(tiles)))
    else:
        print('  c%d X=%d: all NONE' % (c, X))

# Also check SP layer sprites near coin
print()
print('SP layer sprites at X=8800-9200:')
for layer in root.findall('.//objectgroup'):
    for obj in layer.findall('object'):
        gid = int(obj.get('gid', 0))
        x = float(obj.get('x', 0))
        y = float(obj.get('y', 0))
        if 8800 <= x <= 9200:
            sid = (gid - 257) & 0xFF if gid >= 257 else gid
            print('  X=%.0f Y=%.0f gid=%d sid=0x%02X' % (x, y, gid, sid))

# Show terrain summary for the approach path: cols 540-570, rows 12-16
print()
print('Approach path cols 540-570, rows 12-16:')
for c in range(540, 571):
    X = c * 16
    row_info = []
    for r in range(12, 17):
        idx = r * map_w + c
        tid = bg[idx] & 0xFF
        col = CT[tid]
        nm = N.get(col, '?%d' % col)
        row_info.append('r%d=%s' % (r, nm[:3]))
    print('  c%d X=%d: %s' % (c, X, '  '.join(row_info)))

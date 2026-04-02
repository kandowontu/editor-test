import xml.etree.ElementTree as ET, os
tree = ET.parse(os.path.join('..', 'famidash', 'levels', 'LEVEL DATA', 'lvlset_HUGE', 'eon.tmx'))
root = tree.getroot()
w, h = int(root.get('width')), int(root.get('height'))
tiles = [int(x) for x in root.findall('layer')[0].find('data').text.strip().split(',')]
sptiles = [int(x) for x in root.findall('layer')[1].find('data').text.strip().split(',')]
grr = 3
cn = {0:'NONE', 16:'ALL', 17:'HALF', 18:'DBOTTM', 27:'DEATH', 30:'DTOP', 52:'DECO',
      176:'CUL', 177:'CUR', 178:'CDL', 179:'CDR', 180:'TOP', 181:'BTM',
      182:'LEFT', 183:'RIGHT', 184:'STL', 185:'STR', 186:'SBL', 187:'SBR'}
sn = {0x28:'Y_ORB', 0x05:'B_ORB', 0xF8:'H_BLK', 0xF7:'J_BLK', 0xF6:'F_BLK',
      0x54:'SP_UP', 0x55:'SP_DN'}

with open(r'c:\Editor Test\route_result.txt', 'w') as out:
    out.write('TERRAIN cols 1828-1850 rows 18-27\n')
    for r in range(18, 28):
        wy = (r - grr) * 16
        parts = []
        for c in range(1828, 1851):
            g = tiles[r * w + c] if r < h else 0
            e = g - 1 if g > 0 else 0
            parts.append(cn.get(e, str(e))[:5].rjust(5))
        out.write(f'r{r:02d} wY={wy:3d}|{"".join(parts)}\n')

    out.write('\nSPRITES cols 1828-1850 rows 18-27\n')
    for r in range(18, 28):
        wy = (r - grr) * 16
        parts = []
        for c in range(1828, 1851):
            s = sptiles[r * w + c] if r < h else 0
            sid = s - 257 if s > 256 else -1
            if sid >= 0:
                parts.append(sn.get(sid, f's{sid:02X}')[:5].rjust(5))
            else:
                parts.append('    .')
        out.write(f'r{r:02d} wY={wy:3d}|{"".join(parts)}\n')

    out.write('\nFWD COLLISION AT COL 1847\n')
    for py in range(270, 340, 2):
        cy = py + 6
        ty = cy // 16
        ay = ty + grr
        g = tiles[ay * w + 1847] if 0 <= ay < h else 0
        e = g - 1 if g > 0 else 0
        ly = cy - ty * 16
        nm = cn.get(e, str(e))
        occ = e == 16
        dk = (e == 27 and 4 <= ly <= 11) or (e == 30 and ly < 6)
        res = 'KILL' if occ else ('DPOSS' if dk else 'PASS')
        out.write(f'  Y={py:3d} cY={cy:3d} aR={ay:2d} ed={e:3d}({nm:>6s}) lY={ly:2d} {res}\n')

    out.write('\nFWD COLLISION AT COL 1833\n')
    for py in range(280, 340, 2):
        cy = py + 6
        ty = cy // 16
        ay = ty + grr
        g = tiles[ay * w + 1833] if 0 <= ay < h else 0
        e = g - 1 if g > 0 else 0
        ly = cy - ty * 16
        nm = cn.get(e, str(e))
        occ = e == 16
        dk = (e == 27 and 4 <= ly <= 11) or (e == 30 and ly < 6)
        res = 'KILL' if occ else ('DPOSS' if dk else 'PASS')
        out.write(f'  Y={py:3d} cY={cy:3d} aR={ay:2d} ed={e:3d}({nm:>6s}) lY={ly:2d} {res}\n')

#!/usr/bin/env python3
"""Check terrain at Y=320-335 column by column near death zone."""
import xml.etree.ElementTree as ET
tree = ET.parse(r"c:\Editor Test\eon2.tmx")
root = tree.getroot()
width = int(root.attrib['width'])
layers = root.findall('.//layer')
tiles = [int(x) for x in layers[0].find('data').text.strip().split(',')]

COL = {0:'NONE',1:'FC',2:'FC',3:'BOT',4:'DT',5:'FC',6:'FC',7:'NONE',
       8:'DB',9:'DB',10:'DT',11:'DT',12:'DB',13:'DT',14:'DL',15:'DR',
       16:'DEATH',17:'TOP',18:'DB',19:'TOP',20:'FC',24:'FC',25:'DEATH',26:'DEATH',
       52:'NONE',180:'TOP',181:'BOT',182:'LEFT',183:'RIGHT',
       178:'DL_HALF',179:'DR_HALF',173:'DLS'}

def gc(c,r):
    idx=r*width+c
    gid=tiles[idx]&0x3FFFFFFF
    if gid==0: return 'EMPTY'
    return COL.get(gid-1,f'?{gid-1}')

# For Y=320-335 (row 20), a cube hitbox at Y=320 spans rows 20-21
# Center point death check at Y=328 checks row 20
# Floor at Y=335 checks row 20 (still within same tile row)
# Row 21 = Y=336 would only be checked if hitbox overlaps (Y+16=336)
print("Terrain at rows 17-23 for cols 405-418 (Y=272-368):")
print(f"{'col':>5s}", end='')
for r in range(17,24):
    print(f"  r{r}(Y={r*16:3d})", end='')
print()
for c in range(405,419):
    print(f"{c:5d}", end='')
    for r in range(17,24):
        val = gc(c,r)
        print(f"  {val:>10s}", end='')
    print()

# Danger analysis for a cube at various Y positions
print("\nDanger analysis for cube traveling right through cols 405-417:")
for y in [288, 304, 320, 336]:
    print(f"\n  Cube at Y={y} (hitbox Y={y}-{y+15}):")
    r_top = y // 16          # top tile row
    r_bot = (y+15) // 16     # bottom tile row
    r_center = (y+8) // 16   # center tile row
    for c in range(405,418):
        dangers = []
        # Center death check: tile at (center.x, y+8)
        center_col = gc(c, r_center)
        if 'DEATH' in center_col or 'DT' in center_col or 'DB' in center_col or 'DL' in center_col or 'DR' in center_col:
            dangers.append(f"CENTER:{center_col}")
        # Forward collision: right edge at various Y
        for r in range(r_top, r_bot+1):
            col_val = gc(c, r)
            if col_val in ('FC','LEFT','RIGHT','BOT','TOP'):
                dangers.append(f"FWD_r{r}:{col_val}")
        status = ', '.join(dangers) if dangers else 'safe'
        print(f"    col {c} (X={c*16}): {status}")

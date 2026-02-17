import xml.etree.ElementTree as ET
import sys

FILE = r"c:\Editor Test\test9.tmx"

tree = ET.parse(FILE)
root = tree.getroot()

# Map dimensions
w = int(root.attrib['width'])
h = int(root.attrib['height'])
tw = int(root.attrib['tilewidth'])
th = int(root.attrib['tileheight'])
print(f"=== MAP DIMENSIONS ===")
print(f"  Tiles: {w} x {h}  ({w*tw} x {h*th} px)  tile={tw}x{th}")

# Tilesets
print(f"\n=== TILESETS ===")
for ts in root.findall('tileset'):
    print(f"  {ts.attrib.get('name','?'):12s}  firstgid={ts.attrib['firstgid']}  tilecount={ts.attrib.get('tilecount','?')}  columns={ts.attrib.get('columns','?')}")

# Image layers
print(f"\n=== IMAGE LAYERS ===")
for il in root.findall('imagelayer'):
    img = il.find('image')
    src = img.attrib.get('source','') if img is not None else ''
    print(f"  {il.attrib.get('name','?')}  offset=({il.attrib.get('offsetx','0')},{il.attrib.get('offsety','0')})  parallax=({il.attrib.get('parallaxx','1')},{il.attrib.get('parallaxy','1')})  src={src}")

# Tile layers
TILE_NAMES = {
    0: '.',    # empty (GID 0 or GID 1 with firstgid=1 → index 0)
    16: '#',   # COL_ALL (solid)
    17: 'X',   # COL_DEATH (spike)
}

for layer in root.findall('layer'):
    lid = layer.attrib.get('id','?')
    lname = layer.attrib.get('name','?')
    lw = int(layer.attrib.get('width', w))
    lh = int(layer.attrib.get('height', h))
    
    data_el = layer.find('data')
    encoding = data_el.attrib.get('encoding','')
    
    print(f"\n=== TILE LAYER: '{lname}' (id={lid}, {lw}x{lh}, encoding={encoding}) ===")
    
    if encoding != 'csv':
        print("  [unsupported encoding]")
        continue
    
    csv_text = data_el.text.strip()
    gids = [int(x.strip()) for x in csv_text.split(',') if x.strip()]
    
    # Build 2D grid
    grid = []
    for row in range(lh):
        grid.append(gids[row*lw : (row+1)*lw])
    
    # Parse all tilesets for GID resolution
    tilesets = []
    for ts in root.findall('tileset'):
        tilesets.append((int(ts.attrib['firstgid']), ts.attrib.get('name','?')))
    tilesets.sort(key=lambda x: x[0])
    
    def resolve_gid(gid):
        """Returns (tileset_name, tile_index) for a GID"""
        if gid == 0:
            return ('none', -1)
        ts_name = '?'
        fgid = 1
        for fg, tn in tilesets:
            if fg <= gid:
                fgid = fg
                ts_name = tn
        return (ts_name, gid - fgid)
    
    # Find non-empty columns and rows
    non_empty_rows = set()
    non_empty_cols = set()
    tile_counts = {}
    
    for r in range(lh):
        for c in range(lw):
            gid = grid[r][c]
            ts_name, idx = resolve_gid(gid)
            if gid != 0 and idx > 0:  # not empty
                non_empty_rows.add(r)
                non_empty_cols.add(c)
                key = (ts_name, idx)
                tile_counts[key] = tile_counts.get(key, 0) + 1
    
    if not non_empty_rows:
        print("  [all empty]")
        continue
    
    print(f"  Non-empty tiles found: {len(tile_counts)} distinct types")
    print(f"  Tile counts:")
    for (ts_name, idx) in sorted(tile_counts.keys()):
        name = TILE_NAMES.get(idx, f'0x{idx:02X}')
        print(f"    [{ts_name}] index {idx:3d} (0x{idx:02X}) = {name:5s} : {tile_counts[(ts_name,idx)]} tiles")
    
    min_r = min(non_empty_rows)
    max_r = max(non_empty_rows)
    min_c = min(non_empty_cols)
    max_c = max(non_empty_cols)
    
    print(f"  Bounding box: rows {min_r}-{max_r}, cols {min_c}-{max_c}")
    print(f"  Active area: {max_c - min_c + 1} x {max_r - min_r + 1} tiles")
    
    # Visual grid of active area
    print(f"\n  --- Visual Map (rows {min_r}-{max_r}, cols {min_c}-{max_c}) ---")
    print(f"  Legend: .=empty #=solid(16) X=death(17) other=hex")
    print(f"  Row numbers are absolute tile rows.")
    print()
    
    # Print column ruler
    col_range = range(min_c, max_c + 1)
    if max_c - min_c < 200:  # only show ruler if reasonable
        ruler = "      "
        for c in col_range:
            if c % 10 == 0:
                ruler += str(c // 10 % 10)
            else:
                ruler += " "
        print(ruler)
        ruler2 = "      "
        for c in col_range:
            ruler2 += str(c % 10)
        print(ruler2)
    
    for r in range(min_r, max_r + 1):
        line = f"  {r:3d}: "
        for c in col_range:
            gid = grid[r][c]
            ts_name, idx = resolve_gid(gid)
            
            if gid == 0 or idx <= 0:
                line += '.'
            elif idx in TILE_NAMES:
                line += TILE_NAMES[idx]
            else:
                if idx < 16:
                    line += format(idx, 'x')
                else:
                    line += '?'
        print(line)

# Object groups
print(f"\n=== OBJECT LAYERS ===")
obj_groups = root.findall('objectgroup')
if not obj_groups:
    print("  [none]")
for og in obj_groups:
    ogname = og.attrib.get('name', '?')
    print(f"\n  Object Group: '{ogname}'")
    for obj in og.findall('object'):
        oid = obj.attrib.get('id','?')
        oname = obj.attrib.get('name','')
        otype = obj.attrib.get('type', obj.attrib.get('class',''))
        ox = float(obj.attrib.get('x','0'))
        oy = float(obj.attrib.get('y','0'))
        ow = obj.attrib.get('width','')
        oh = obj.attrib.get('height','')
        ogid = obj.attrib.get('gid','')
        
        desc = f"    id={oid}"
        if oname: desc += f" name='{oname}'"
        if otype: desc += f" type='{otype}'"
        desc += f" pos=({ox},{oy})"
        if ow: desc += f" size=({ow}x{oh})"
        if ogid: desc += f" gid={ogid}"
        print(desc)
        
        # Custom properties
        props = obj.find('properties')
        if props is not None:
            for p in props.findall('property'):
                pname = p.attrib.get('name','')
                pval = p.attrib.get('value', p.text or '')
                ptype = p.attrib.get('type','')
                print(f"      prop: {pname} = {pval} ({ptype})" if ptype else f"      prop: {pname} = {pval}")

print("\n=== DONE ===")

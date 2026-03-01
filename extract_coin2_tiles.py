import xml.etree.ElementTree as ET
import sys

tmx_path = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx"
tree = ET.parse(tmx_path)
root = tree.getroot()

COL_START = 560
COL_END = 600
width = int(root.get('width'))
height = int(root.get('height'))

print(f"Map size: {width} x {height}")
print(f"Extracting columns {COL_START}-{COL_END}")
print()

# Print tileset info
for ts in root.findall('tileset'):
    print(f"Tileset: {ts.get('name')}, firstgid={ts.get('firstgid')}, tilecount={ts.get('tilecount')}")
print()

# Process each layer
for layer in root.findall('layer'):
    layer_name = layer.get('name', layer.get('id'))
    layer_id = layer.get('id')
    print(f"=== Layer: '{layer_name}' (id={layer_id}) ===")
    
    data_elem = layer.find('data')
    encoding = data_elem.get('encoding')
    csv_text = data_elem.text.strip()
    
    rows = csv_text.split('\n')
    
    non_zero_tiles = []
    
    for row_idx, row_text in enumerate(rows):
        vals = [int(v.strip()) for v in row_text.strip().rstrip(',').split(',')]
        for col_idx in range(COL_START, min(COL_END + 1, len(vals))):
            tile_id = vals[col_idx]
            if tile_id != 0:
                # Determine which tileset
                if tile_id >= 257:
                    ts_name = "sprites"
                    local_id = tile_id - 257
                else:
                    ts_name = "famidash"
                    local_id = tile_id - 1
                non_zero_tiles.append((row_idx, col_idx, tile_id, ts_name, local_id))
    
    if non_zero_tiles:
        print(f"  Non-zero tiles found: {len(non_zero_tiles)}")
        for row, col, tid, ts, lid in non_zero_tiles:
            game_x = col * 16
            game_y = row * 16
            hex_lid = f"0x{lid:02X}"
            print(f"  Row={row:2d}, Col={col:3d}  tile_id={tid:3d}  tileset={ts:8s}  local_id={lid:3d} ({hex_lid})  gameXY=({game_x},{game_y})")
    else:
        print("  No non-zero tiles found in this range.")
    print()

# Check for object groups
for og in root.findall('.//objectgroup'):
    print(f"=== Object Group: '{og.get('name')}' ===")
    for obj in og.findall('object'):
        x = float(obj.get('x', 0))
        y = float(obj.get('y', 0))
        game_col = int(x / 16)
        if COL_START <= game_col <= COL_END:
            print(f"  Object id={obj.get('id')} name={obj.get('name')} x={x} y={y} type={obj.get('type')}")
    print()

# Also check for any properties on tilesets
for ts in root.findall('tileset'):
    for tile in ts.findall('tile'):
        props = tile.find('properties')
        if props is not None:
            tid = int(tile.get('id'))
            print(f"Tile {tid} in tileset {ts.get('name')} has properties:")
            for p in props.findall('property'):
                print(f"  {p.get('name')} = {p.get('value')}")

print("\n=== COMPLETE GRID (Layer 1 - main) for cols 560-600, including zeros ===")
for layer in root.findall('layer'):
    if layer.get('id') == '1':
        data_elem = layer.find('data')
        csv_text = data_elem.text.strip()
        rows = csv_text.split('\n')
        
        # Print header
        header = "     "
        for c in range(COL_START, COL_END + 1):
            header += f"{c:4d}"
        print(header)
        
        for row_idx, row_text in enumerate(rows):
            vals = [int(v.strip()) for v in row_text.strip().rstrip(',').split(',')]
            line = f"R{row_idx:02d}: "
            has_nonzero = False
            for col_idx in range(COL_START, min(COL_END + 1, len(vals))):
                v = vals[col_idx]
                if v != 0:
                    has_nonzero = True
                line += f"{v:4d}"
            if has_nonzero:
                print(line)

print("\n=== COMPLETE GRID (Layer 2 - SP) for cols 560-600 ===")
for layer in root.findall('layer'):
    if layer.get('id') == '2':
        data_elem = layer.find('data')
        csv_text = data_elem.text.strip()
        rows = csv_text.split('\n')
        
        header = "     "
        for c in range(COL_START, COL_END + 1):
            header += f"{c:4d}"
        print(header)
        
        for row_idx, row_text in enumerate(rows):
            vals = [int(v.strip()) for v in row_text.strip().rstrip(',').split(',')]
            line = f"R{row_idx:02d}: "
            has_nonzero = False
            for col_idx in range(COL_START, min(COL_END + 1, len(vals))):
                v = vals[col_idx]
                if v != 0:
                    has_nonzero = True
                line += f"{v:4d}"
            if has_nonzero:
                print(line)

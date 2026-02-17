import xml.etree.ElementTree as ET, base64, zlib, struct

tree = ET.parse('test9.tmx')
root = tree.getroot()
w = int(root.attrib['width'])
h = int(root.attrib['height'])
print(f'Map: {w}x{h} tiles')

for layer in root.findall('layer'):
    name = layer.attrib.get('name', f'id_{layer.attrib["id"]}')
    data_el = layer.find('data')
    enc = data_el.attrib.get('encoding','')
    comp = data_el.attrib.get('compression','')
    if enc == 'csv':
        vals = [int(x.strip()) for x in data_el.text.strip().split(',') if x.strip()]
    elif enc == 'base64':
        raw = base64.b64decode(data_el.text.strip())
        if comp == 'zlib':
            raw = zlib.decompress(raw)
        vals = list(struct.unpack(f'<{len(raw)//4}I', raw))
    else:
        vals = []
    
    # Convert GIDs to indices (firstgid=1 for famidash tileset)
    tiles = []
    for v in vals:
        if v == 0:
            tiles.append(0)
        elif v < 257:
            tiles.append(v - 1)
        else:
            tiles.append(v)
    
    active_rows = []
    for row in range(h):
        row_tiles = tiles[row*w:(row+1)*w]
        nonzero = [(c, t) for c, t in enumerate(row_tiles) if t != 0]
        if nonzero:
            active_rows.append((row, nonzero))
    
    if not active_rows:
        print(f'Layer {name}: empty')
        continue
    
    min_row = active_rows[0][0]
    max_row = active_rows[-1][0]
    min_col = min(c for _, nz in active_rows for c, _ in nz)
    max_col = max(c for _, nz in active_rows for c, _ in nz)
    print(f'Layer "{name}": active rows {min_row}-{max_row}, cols {min_col}-{max_col}')
    
    for row_idx, nonzero in active_rows:
        row_tiles = tiles[row_idx*w:(row_idx+1)*w]
        line = ''
        for c in range(min_col, min(max_col+1, min_col+80)):
            t = row_tiles[c]
            if t == 0: line += '.'
            elif t == 16: line += '#'
            elif t == 17: line += 'X'
            else: line += f'{t:02x}'[0]
        print(f'  r{row_idx:2d} c{min_col:2d}: {line}')

# Also parse sprite layer
print('\nSprite layers:')
for layer in root.findall('layer'):
    name = layer.attrib.get('name', f'id_{layer.attrib["id"]}')
    if 'sp' not in name.lower() and 'sprite' not in name.lower():
        continue
    data_el = layer.find('data')
    enc = data_el.attrib.get('encoding','')
    comp = data_el.attrib.get('compression','')
    if enc == 'csv':
        vals = [int(x.strip()) for x in data_el.text.strip().split(',') if x.strip()]
    elif enc == 'base64':
        raw = base64.b64decode(data_el.text.strip())
        if comp == 'zlib':
            raw = zlib.decompress(raw)
        vals = list(struct.unpack(f'<{len(raw)//4}I', raw))
    else:
        vals = []
    
    for i, v in enumerate(vals):
        if v > 0:
            row = i // w
            col = i % w
            sprite_idx = v - 257 if v >= 257 else v - 1
            print(f'  [{row},{col}] GID={v} spriteIdx=0x{sprite_idx:02X} ({sprite_idx})')

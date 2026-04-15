import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
GRR = 3

COL = {0x00:'NONE',0x01:'FC',0x02:'FC',0x03:'DEATH',0x04:'FC_L',0x05:'FC',0x06:'FC',
       0x6D:'ALL',0x6E:'ALL',0x6F:'NONE',0x0A:'DEATH',0x0B:'DEATH',0x35:'SL_L',0x36:'SL_R'}

for layer in root.findall('.//layer'):
    name = layer.get('name', '')
    if name == 'SP':
        continue
    data = layer.find('data')
    if data is None:
        continue
    rows = data.text.strip().split('\n')
    for col in range(505, 520):
        tiles_in_col = []
        for row_idx, row in enumerate(rows):
            tiles = row.rstrip(',').split(',')
            if col < len(tiles):
                gid = int(tiles[col].strip())
                if gid > 0 and gid <= 256:
                    tile_id = gid - 1
                    world_y = (row_idx - GRR) * 16
                    ct = COL.get(tile_id, f'0x{tile_id:02X}')
                    tiles_in_col.append(f'Y={world_y}:t{tile_id:02X}={ct}')
        if tiles_in_col:
            sep = ' | '
            print(f'col={col} X={col*16}: {sep.join(tiles_in_col)}')

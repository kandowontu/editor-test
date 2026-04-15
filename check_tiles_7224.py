import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
width = int(root.get('width'))
height = int(root.get('height'))

# collision table
COLLISION_NAMES = {
    0x00: "PASSABLE", 0x01: "COL_SOLID", 0x02: "COL_FLOOR_CEIL",
    0x03: "COL_DEATH_LEFT", 0x04: "COL_DEATH_ALL", 0x05: "COL_DEATH_BOTTOM",
    0x10: "COL_SLOPE_RD45", 0x11: "COL_SLOPE_RU45", 0x12: "COL_SLOPE_LD45",
    0x13: "COL_SLOPE_LU45"
}

# Find metatile layer
for layer in root.findall('layer'):
    if layer.get('name') == 'metatile-layer':
        data = layer.find('data').text.strip()
        gids = [int(x) for x in data.split(',')]
        
        col = 7224 // 16  # 451
        print(f'Column {col} (X={col*16}-{col*16+15}), width={width}, height={height}')
        for row in range(10, min(height, 22)):
            idx = row * width + col
            if idx < len(gids):
                gid = gids[idx]
                tile_id = gid - 1 if gid > 0 else -1
                print(f'  row {row} (Y={row*16}-{row*16+15}): tile={tile_id}')
        
        # Also check adjacent columns
        for c in [450, 451, 452]:
            print(f'\nColumn {c} (X={c*16}-{c*16+15}):')
            for row in range(12, min(height, 21)):
                idx = row * width + c
                if idx < len(gids):
                    gid = gids[idx]
                    tile_id = gid - 1 if gid > 0 else -1
                    print(f'  row {row} (Y={row*16}-{row*16+15}): tile={tile_id}')

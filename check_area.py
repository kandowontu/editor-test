import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\cycles.tmx')
root = tree.getroot()

# Check ALL sprites/objects from X=10500 to X=11600
print("=== All objects/sprites from X=10500 to X=11600 ===")
for og in root.findall('.//objectgroup'):
    ogname = og.get('name', 'unnamed')
    for obj in og.findall('object'):
        x = float(obj.get('x', 0))
        if 10500 < x < 11600:
            y = float(obj.get('y', 0))
            gid = int(obj.get('gid', 0))
            name = obj.get('name', '')
            otype = obj.get('type', '')
            sid = gid - 1 if gid > 0 else 0
            print(f'  layer={ogname} x={x} y={y} gid={gid} sid=0x{sid:02X} name={name} type={otype}')

# Also check tile data for death tiles  
print("\n=== Death/spike tiles in cols 690-720 ===")
tw = int(root.get('tilewidth', 16))
th = int(root.get('tileheight', 16))
mw = int(root.get('width'))

# Collision table (first 32 entries)
col_table = [
    "NONE", "FLOOR_CEIL", "FLOOR_CEIL", "BOTTOM", "DEATH_TOP", "FLOOR_CEIL", "FLOOR_CEIL", "NONE",
    "DEATH_BOTTOM", "DEATH_BOTTOM", "DEATH_TOP", "DEATH_TOP", "DEATH_BOTTOM", "DEATH_TOP", "DEATH_LEFT", "DEATH_RIGHT",
    "ALL", "DEATH", "DEATH_BOTTOM", "DEATH_BOTTOM", "DEATH_TOP", "TOP_CENTER_SPIKE", "ALL", "DEATH_TOP",
    "DEATH_BOTTOM", "TOP", "DEATH", "DEATH", "DEATH", "DEATH_LEFT", "DEATH_TOP", "DEATH_RIGHT",
]

for layer in root.findall('.//layer'):
    data = layer.find('data')
    if data is not None and data.get('encoding') == 'csv':
        tiles = [int(x) for x in data.text.strip().split(',')]
        rows = [tiles[r*mw:(r+1)*mw] for r in range(len(tiles)//mw)]
        for col in range(690, 720):
            x_px = col * tw
            for r in range(len(rows)):
                if col < len(rows[r]) and rows[r][col] != 0:
                    tmx_id = rows[r][col]
                    game_id = tmx_id - 1
                    y_px = r * th
                    if game_id < len(col_table):
                        col_name = col_table[game_id]
                    else:
                        col_name = f"G{game_id}"
                    if "DEATH" in col_name or "SPIKE" in col_name:
                        print(f'  Col {col} Row {r} (X={x_px}, Y={y_px}): TMX=0x{tmx_id:02X} Game=0x{game_id:02X} Col={col_name}')

import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\cycles.tmx')
root = tree.getroot()
tw = int(root.get('tilewidth', 16))
mw = int(root.get('width'))

col_table = [
    "NONE", "FL_CL", "FL_CL", "BTM", "DTH_T", "FL_CL", "FL_CL", "NONE",
    "DTH_B", "DTH_B", "DTH_T", "DTH_T", "DTH_B", "DTH_T", "DTH_L", "DTH_R",
    "ALL", "DEATH", "DTH_B", "DTH_B", "DTH_T", "TCSPI", "ALL", "DTH_T",
    "DTH_B", "TOP", "DEATH", "DEATH", "DEATH", "DTH_L", "DTH_T", "DTH_R",
]

for layer in root.findall('.//layer'):
    data = layer.find('data')
    if data is not None and data.get('encoding') == 'csv':
        tiles = [int(x) for x in data.text.strip().split(',')]
        rows = [tiles[r*mw:(r+1)*mw] for r in range(len(tiles)//mw)]
        
        # Print rows 16-27 for columns around the second ball area
        print("      ", end="")
        for col in [670, 672, 674, 676, 678, 680, 685, 690, 695, 700, 705, 707, 708, 709, 710, 711, 712, 713, 714, 715, 716]:
            print(f" {col*tw:5}", end="")
        print()
        for r in range(16, 28):
            print(f"R{r:2} Y={r*16:3}:", end="")
            for col in [670, 672, 674, 676, 678, 680, 685, 690, 695, 700, 705, 707, 708, 709, 710, 711, 712, 713, 714, 715, 716]:
                if col < len(rows[r]) and rows[r][col] != 0:
                    tmx_id = rows[r][col]
                    game_id = tmx_id - 1
                    if game_id < len(col_table):
                        cn = col_table[game_id]
                    else:
                        cn = f"G{game_id:02X}"
                    print(f" {cn:>5}", end="")
                else:
                    print("     .", end="")
            print()

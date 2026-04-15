import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
GRR = 3

# Full collision table
coll_names = [
    "NONE","FC","FC","BOT","DTOP","FC","FC","NONE","DBOT","DBOT","DTOP","DTOP","DBOT","DTOP","DLEFT","DRIGHT",
    "ALL","DEATH","DBOT","DBOT","DTOP","TCS","ALL","DTOP","DBOT","TOP","DEATH","DEATH","DEATH","DLEFT","DTOP","DRIGHT",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","NONE","NONE","NONE","TCS","ALL","ALL","ALL","DBOT","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","TOP","TOP","TOP","TOP","BOT","BOT","BOT","DEATH","DCBOT","DTOP","DRIGHT","DLEFT",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","DRSP","DBOT","DLSP","DRIGHT","NONE","DLEFT","URSP","DTOP","ULSP","DEATH","ALL","DBOT",
    "NONE","NONE","DEATH","DEATH","DBOT","DTOP","DBOT","DTOP","FC","FC","RIGHT","LEFT","RIGHT","LEFT","NONE","NOSIDE",
    "SRD45","SLD45","SRU45","SLU45","SRD22R","SRD22L","SLD22R","SLD22L","SRU22R","SRU22L","SLU22R","SLU22L","SRD66T","SRD66B","SLD66B","SLD66T",
    "SRU66T","SRU66B","SLU66B","SLU66T","NOSIDE","NOSIDE","NOSIDE","NOSIDE","LSPB","RSPB","BLSP","BRSP","BSPS","DLSP","DRSP","DBSPS",
    "UL","UR","DL","DR","TOP","BOT","LEFT","RIGHT","TLST","TRST","BLST","BRST","TLBR","TRBL","ULSP","URSP",
    "UBSPS","DTR","DTL","DBR","DBL","NONE","NONE","BCS","BCS","TOP","BOT","NONE","NONE","NONE","NONE","NONE",
    "NONE","NONE","NONE","ALL","NONE","NONE","NONE","NONE","NONE","DRSP","DLSP","ULSP","URSP","LEFT","RIGHT","UR",
    "RIGHT","TOP","TOP","UL","TOP","RIGHT","BOT","TOP","NONE","NONE","NONE","NONE","NONE","NONE","NONE","NONE",
    "DRSP","DLSP","URSP","ULSP","DRSP","DLSP","DEATH","RIGHT","LEFT","URSP","ULSP","DEATH","ALL","LEFT","DR","DL"
]

# We want to check Y=224 to Y=272 (lower corridor) from col 453 to col 566 (coin at col 566=9056/16)
coin_col = 9056 // 16  # 566

for layer in root.findall('.//layer'):
    name = layer.get('name', '')
    if name == 'SP': continue
    data = layer.find('data')
    if data is None: continue
    rows = data.text.strip().split('\n')
    
    print("=== Terrain at Y=224-272 from cols 453-570 ===")
    for col in range(453, 571):
        issues = []
        for row_idx, row in enumerate(rows):
            tiles = row.rstrip(',').split(',')
            if col < len(tiles):
                gid = int(tiles[col].strip())
                if gid > 0 and gid <= 256:
                    tid = gid - 1
                    wy = (row_idx - GRR) * 16
                    if wy >= 224 and wy <= 272:
                        cn = coll_names[tid] if tid < len(coll_names) else f"t{tid:02X}"
                        if cn != "NONE":
                            issues.append(f"Y={wy}:{cn}")
        if issues:
            sep = ' | '
            marker = " <<<COIN" if col == coin_col else ""
            print(f"col={col} X={col*16}: {sep.join(issues)}{marker}")

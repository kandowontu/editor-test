import xml.etree.ElementTree as ET, csv, io

tree = ET.parse(r'famidash/LEVELS/LEVEL DATA/lvlset_HUGE/bloodbath.tmx')
root = tree.getroot()

TILES_FIRSTGID = 1
TILE_SIZE = 16
GROUND_ROWS_RESERVE = 3

# Parse terrain layer
for layer in root.findall('layer'):
    name = layer.get('name')
    if name == 'SP':
        continue
    width = int(layer.get('width'))
    height = int(layer.get('height'))
    data = layer.find('data')
    reader = csv.reader(io.StringIO(data.text.strip()))
    rows = []
    for r in reader:
        row = [int(x) for x in r if x.strip()]
        if row:
            rows.append(row)
    
    print(f"Map: {width}x{height} rows={len(rows)}")
    print(f"GroundRowsToReserve: {GROUND_ROWS_RESERVE}")
    print()
    
    # Collision table (first 48 entries from MetatileCollision.cs)
    col_names = [
        "COL_NONE", "COL_FLOOR_CEIL", "COL_FLOOR_CEIL", "COL_BOTTOM",
        "COL_DEATH_TOP", "COL_FLOOR_CEIL", "COL_FLOOR_CEIL", "COL_NONE",
        "COL_DEATH_BOTTOM", "COL_DEATH_BOTTOM", "COL_DEATH_TOP", "COL_DEATH_TOP",
        "COL_DEATH_BOTTOM", "COL_DEATH_TOP", "COL_DEATH_LEFT", "COL_DEATH_RIGHT",
        "COL_ALL", "COL_DEATH", "COL_DEATH_BOTTOM", "COL_DEATH_BOTTOM",
        "COL_DEATH_TOP", "COL_TOP_CENTER_SPIKE", "COL_ALL", "COL_DEATH_TOP",
        "COL_DEATH_BOTTOM", "COL_TOP", "COL_DEATH", "COL_DEATH",
        "COL_DEATH", "COL_DEATH_LEFT", "COL_DEATH_TOP", "COL_DEATH_RIGHT",
        "COL_ALL", "COL_ALL", "COL_ALL", "COL_ALL",
        "COL_ALL", "COL_ALL", "COL_ALL", "COL_ALL",
        "COL_ALL", "COL_ALL", "COL_ALL", "COL_ALL",
        "COL_ALL", "COL_ALL", "COL_ALL", "COL_NONE",
    ]
    # Extend to 256 (rest COL_NONE or COL_ALL etc)
    while len(col_names) < 256:
        col_names.append("COL_NONE")
    
    # Player death position: X=19544px, rightEdge=19552 (mini hbW=8)
    playerX_px = 19544
    hbW_mini = 8
    hbW_full = 15
    rightEdge_mini = playerX_px + hbW_mini   # 19552
    rightEdge_full = playerX_px + hbW_full   # 19559
    
    probeCol_mini = rightEdge_mini // TILE_SIZE  # 1222
    probeCol_full = rightEdge_full // TILE_SIZE  # 1222
    
    localX_mini = rightEdge_mini - probeCol_mini * TILE_SIZE  # 0
    localX_full = rightEdge_full - probeCol_full * TILE_SIZE  # 7
    
    print(f"Player X={playerX_px}px")
    print(f"Mini: rightEdge={rightEdge_mini} tileCol={probeCol_mini} localX={localX_mini}")
    print(f"Full: rightEdge={rightEdge_full} tileCol={probeCol_full} localX={localX_full}")
    print()
    
    # For each player Y from 3 to 367, check what tile forward collision would probe
    # Forward collision checks centerY, not playerY directly
    # Mini cube gravFlipped: centerY = playerY + 4 + 3 + 3 = playerY + 10
    # Mini cube normal: centerY = playerY + 4 + 3 - 2 = playerY + 5
    
    print("=== Forward collision analysis at X=19544 (mini cube, grav flipped) ===")
    print("probeCol = 1222")
    print()
    
    prev_col_name = None
    block_start_y = 0
    
    for playerY in range(0, 380):
        centerY = playerY + 10  # mini cube grav flipped
        tileY = centerY // TILE_SIZE
        tileArrayY = tileY + GROUND_ROWS_RESERVE
        
        if tileArrayY < 0 or tileArrayY >= height:
            col_name = "OOB_SOLID"
        else:
            tmx_gid = rows[tileArrayY][probeCol_mini] if probeCol_mini < len(rows[tileArrayY]) else 0
            tile_id = tmx_gid - TILES_FIRSTGID if tmx_gid > 0 else -1
            if tile_id < 0:
                col_name = "EMPTY(-1)"
            elif tile_id < len(col_names):
                col_name = f"{col_names[tile_id]}(tid={tile_id})"
            else:
                col_name = f"UNKNOWN(tid={tile_id})"
        
        if col_name != prev_col_name:
            if prev_col_name is not None:
                print(f"  playerY [{block_start_y}..{playerY-1}] centerY [{block_start_y+10}..{playerY+9}] → {prev_col_name}")
            block_start_y = playerY
            prev_col_name = col_name
    
    if prev_col_name is not None:
        print(f"  playerY [{block_start_y}..379] centerY [{block_start_y+10}..389] → {prev_col_name}")
    
    print()
    print("=== Specifically checking passage area ===")
    for playerY in range(240, 370, 10):
        centerY = playerY + 10
        tileY = centerY // TILE_SIZE
        tileArrayY = tileY + GROUND_ROWS_RESERVE
        tmx_gid = rows[tileArrayY][probeCol_mini] if (0 <= tileArrayY < height and probeCol_mini < len(rows[tileArrayY])) else 0
        tile_id = tmx_gid - TILES_FIRSTGID if tmx_gid > 0 else -1
        col = col_names[tile_id] if 0 <= tile_id < len(col_names) else "?"
        print(f"  playerY={playerY} centerY={centerY} tileY={tileY} arrY={tileArrayY} tmxGid={tmx_gid} tid={tile_id} → {col}")

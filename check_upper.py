import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
layers = root.findall('layer')
layer = layers[0]
data = layer.find('data').text.strip()
rows_data = [r.strip() for r in data.split('\n') if r.strip()]

# Collision names (metatile index -> name)
col_names = [
    "NONE","FC","FC","BOT","DtT","FC","FC","NONE","DtB","DtB","DtT","DtT","DtB","DtT","DtL","DtR",
    "ALL","DTH","DtB","DtB","DtT","TCS","ALL","DtT","DtB","TOP","DTH","DTH","DTH","DtL","DtT","DtR",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","NONE","NONE","NONE","TCS","ALL","ALL","ALL","DtB","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","TOP","TOP","TOP","TOP","BOT","BOT","BOT","DTH","DtB","DtT","DtR","DtL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","DRS","DtB","DLS","DtR","NONE","DtL","URS","DtT","ULS","DTH","ALL","DtB",
    "NONE","NONE","DTH","DTH","DtB","DtT","DtB","DtT","FC","FC","RT","LT","RT","LT","NONE","NSD",
    "SRD","SLD","SRU","SLU","SRD22R","SRD22L","SLD22R","SLD22L","SRU22R","SRU22L","SLU22R","SLU22L","SRD66T","SRD66B","SLD66B","SLD66T",
    "SRU66T","SRU66B","SLU66B","SLU66T","NSD","NSD","NSD","NSD","LSB","RSB","BLS","BRS","BSP","DLS2","DRS2","DBS",
]

def get_col_name(tmx_tile):
    if tmx_tile == 0:
        return "   "
    mt = tmx_tile - 1
    if mt < len(col_names):
        return col_names[mt][:3]
    return f"?{mt}"

# Check rows 7-12 for cols 460-475 (the upper section near the beam failure)
print("Upper section terrain (rows 7-12, cols 460-475):")
print(f"X values: ", end='')
for c in range(460, 476):
    print(f"c{c:3d}", end=' ')
print()
for c in range(460, 476):
    print(f"X{c*16:4d}", end='')
print()

for r in range(7, 13):
    tiles = rows_data[r].split(',')
    print(f"r{r:2d} Y{r*16:3d}: ", end='')
    for c in range(460, 476):
        tid = int(tiles[c]) if c < len(tiles) else 0
        cn = get_col_name(tid)
        print(f"{cn:4s}", end=' ')
    print()

# Also check a wider X range at row 9 specifically
print("\nRow 9 (Y=144) tiles cols 453-475:")
tiles = rows_data[9].split(',')
for c in range(453, 476):
    tid = int(tiles[c]) if c < len(tiles) else 0
    cn = get_col_name(tid)
    if cn.strip() != "NON" and cn.strip() != "":
        print(f"  col {c} (X={c*16}): TMX {tid} => {cn}")
        
# Check row 8 too
print("\nRow 8 (Y=128) tiles cols 453-475:")
tiles = rows_data[8].split(',')
for c in range(453, 476):
    tid = int(tiles[c]) if c < len(tiles) else 0
    cn = get_col_name(tid)
    if cn.strip() != "NON" and cn.strip() != "":
        print(f"  col {c} (X={c*16}): TMX {tid} => {cn}")

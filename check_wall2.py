import xml.etree.ElementTree as ET
import csv, io
tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()
layer = root.findall('.//layer')[0]
w = int(layer.get('width')); h = int(layer.get('height'))
data = layer.find('data')
reader = csv.reader(io.StringIO(data.text.strip()))
tiles = []
for row in reader:
    tiles.append([int(x.strip()) for x in row if x.strip()])

collision_names = [
    "NONE","FC","FC","BOTTOM","DEATH_TOP","FC","FC","NONE","DEATH_BOT","DEATH_BOT","DEATH_TOP","DEATH_TOP","DEATH_BOT","DEATH_TOP","DEATH_LEFT","DEATH_RIGHT",
    "ALL","DEATH","DEATH_BOT","DEATH_BOT","DEATH_TOP","TOP_SPIKE","ALL","DEATH_TOP","DEATH_BOT","TOP","DEATH","DEATH","DEATH","DEATH_LEFT","DEATH_TOP","DEATH_RIGHT",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","NONE","NONE","NONE","TOP_SPIKE","ALL","ALL","ALL","DEATH_BOT","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","TOP","TOP","TOP","TOP","BOTTOM","BOTTOM","BOTTOM","DEATH","DEATH_BOT","DEATH_TOP","DEATH_RIGHT","DEATH_LEFT",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","DN_R_SP","DEATH_BOT","DN_L_SP","DEATH_RIGHT","NONE","DEATH_LEFT","UP_R_SP","DEATH_TOP","UP_L_SP","DEATH","ALL","DEATH_BOT",
    "NONE","NONE","DEATH","DEATH","DEATH_BOT","DEATH_TOP","DEATH_BOT","DEATH_TOP","FC","FC","RIGHT","LEFT","RIGHT","LEFT","NONE","NO_SIDE",
    "SL_RD45","SL_LD45","SL_RU45","SL_LU45","SL_RD22R","SL_RD22L","SL_LD22R","SL_LD22L","SL_RU22R","SL_RU22L","SL_LU22R","SL_LU22L","SL_RD66T","SL_RD66B","SL_LD66B","SL_LD66T",
    "SL_RU66T","SL_RU66B","SL_LU66B","SL_LU66T","NO_SIDE","NO_SIDE","NO_SIDE","NO_SIDE","L_SP_BLK","R_SP_BLK","BL_SP","BR_SP","BOT_SP","DN_L_SP","DN_R_SP","DN_B_SP",
    "UP_LEFT","UP_RIGHT","DN_LEFT","DN_RIGHT","TOP","BOTTOM","LEFT","RIGHT","TL_STAIRS","TR_STAIRS","BL_STAIRS","BR_STAIRS","TL_BR","TR_BL","UP_L_SP","UP_R_SP",
    "UP_B_SP","DEATH_TR","DEATH_TL","DEATH_BR","DEATH_BL","NONE","NONE","BOT_C_SP","BOT_C_SP","TOP","BOTTOM","NONE","NONE","NONE","NONE","NONE",
    "NONE","NONE","NONE","ALL","NONE","NONE","NONE","NONE","NONE","DN_R_SP","DN_L_SP","UP_L_SP","UP_R_SP","LEFT","RIGHT","UP_RIGHT",
    "RIGHT","TOP","TOP","UP_LEFT","TOP","RIGHT","BOTTOM","TOP","NONE","NONE","NONE","NONE","NONE","NONE","NONE","NONE",
    "DN_R_SP","DN_L_SP","UP_R_SP","UP_L_SP","DN_R_SP","DN_L_SP","DEATH","RIGHT","LEFT","UP_R_SP","UP_L_SP","DEATH","ALL","LEFT","DN_RIGHT","DN_LEFT"
]

def get_col(mt):
    if mt < 0 or mt >= len(collision_names): return "???"
    return collision_names[mt]

def short(c):
    if c == "NONE": return ".."
    if c == "ALL": return "##"
    if c == "FC": return "FC"
    if c.startswith("DEATH"): return "DD"
    if c.startswith("SL_"): return "//"
    if c.startswith("TOP"): return "TT"
    if c.startswith("BOT"): return "BB"
    if c.startswith("NO_"): return "NS"
    if c.startswith("DN_"): return "D/"
    if c.startswith("UP_"): return "U/"
    return c[:2]

# Wide scan at cols 780-810, rows 13-22 (engine Y=160-304) - the gap zone
print("Terrain at cols 780-810, rows 13-22 (engine Y=160-304):")
print("         ", end="")
for c in range(780, 811):
    print(f"c{c} ", end="")
print()
for r in range(13, 23):
    ey = (r-3)*16
    print(f"r{r:2d} Y={ey:3d}: ", end="")
    for c in range(780, 811):
        tid = tiles[r][c]
        mt = tid - 1 if tid > 0 else -1
        cn = get_col(mt)
        print(f"{short(cn):3s} ", end="")
    print()

# Also check full column 796
print("\nFull col 796:")
for r in range(0, 27):
    ey = (r-3)*16
    tid = tiles[r][796]
    mt = tid-1 if tid > 0 else -1
    cn = get_col(mt)
    print(f"  r{r:2d} Y={ey:4d}: {cn}")

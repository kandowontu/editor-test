import re
import sys
import os

file_path = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_cataclysm_20260520_165934.txt"
output_path = r"C:\Users\kando\AppData\Local\Temp\f880_events.txt"

if not os.path.exists(file_path):
    sys.exit(1)

patterns_880 = [" f=880", "[PF f=880]", "[BFS] f=880", " fr=880"]
patterns_others = [" f=879", " f=881"]
keywords = ["PORTAL_MODE", "MODE_PORTAL", "GM_PORTAL", "GAMEMODE", "MINI_GROWTH", "PORTAL_SPEED_PRESCAN", "GRAV_PRESCAN"]

f875_pos = -1
f890_pos = -1

results = []

with open(file_path, 'r', encoding='utf-8', errors='replace') as f:
    # First pass to find f=875 and f=890 positions
    line_num = 0
    while True:
        pos = f.tell()
        line = f.readline()
        if not line:
            break
        line_num += 1
        # Use substring match per requirements
        if f875_pos == -1 and "f=875" in line:
            f875_pos = pos
        if f890_pos == -1 and "f=890" in line:
            f890_pos = pos
            
    # Reset to start for pattern searching
    f.seek(0)
    line_num = 0
    while True:
        pos = f.tell()
        line = f.readline()
        if not line:
            break
        line_num += 1
        
        # Check patterns for 880, 879, 881
        match_880 = any(p in line for p in patterns_880)
        match_others = any(p in line for p in patterns_others)
        
        # Condition 4: keywords between first f=875 and first f=890
        match_keyword = False
        if f875_pos != -1 and f890_pos != -1:
            if pos >= f875_pos and pos < f890_pos:
                if any(kw in line for kw in keywords):
                    match_keyword = True
        
        if match_880 or match_others or match_keyword:
            results.append(f"{line_num}: {line.strip()}")

with open(output_path, 'w', encoding='utf-8') as out:
    for r in results:
        out.write(r + "\n")

print(f"Total matches: {len(results)}")
for r in results[:200]:
    print(r)

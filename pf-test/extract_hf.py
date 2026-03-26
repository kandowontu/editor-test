import re

lines = open(r'c:\Editor Test\pf-test\hf_full.txt', 'r').readlines()

print(f"=== TOTAL LINES: {len(lines)} ===\n")

# 1. [BFS] lines with frames 1000-1300
print("=== [BFS] LINES MENTIONING FRAMES 1000-1300 ===")
for i, line in enumerate(lines, 1):
    if '[BFS]' in line:
        # Match f=1000, f=1100, f=1200, f=1300 etc.
        m = re.search(r'f=(1[0-2]\d\d|1300)\b', line)
        if m:
            print(f"L{i}: {line.rstrip()}")
print()

# 2. All [DEATH_DBG] lines
print("=== [DEATH_DBG] LINES ===")
for i, line in enumerate(lines, 1):
    if '[DEATH_DBG]' in line:
        print(f"L{i}: {line.rstrip()}")
print()

# 3. Result: and Message: lines
print("=== Result: AND Message: LINES ===")
for i, line in enumerate(lines, 1):
    stripped = line.strip()
    if stripped.startswith('Result:') or stripped.startswith('Message:'):
        print(f"L{i}: {line.rstrip()}")
print()

# 4. Last 10 lines
print("=== LAST 10 LINES ===")
for i, line in enumerate(lines[-10:], len(lines) - 9):
    print(f"L{i}: {line.rstrip()}")
print()

# 5. Lines containing "ALL DEAD" or "heuristic"
print('=== LINES WITH "ALL DEAD" OR "heuristic" ===')
for i, line in enumerate(lines, 1):
    if 'ALL DEAD' in line or 'heuristic' in line.lower():
        print(f"L{i}: {line.rstrip()}")
print()

# 6. [BFS] lines matching f=1[012][0-9][0-9] (frames 1000-1299)
print("=== [BFS] LINES WITH f=1[012][0-9][0-9] (frames 1000-1299) ===")
for i, line in enumerate(lines, 1):
    if '[BFS]' in line:
        m = re.search(r'f=1[012]\d\d\b', line)
        if m:
            print(f"L{i}: {line.rstrip()}")
print()

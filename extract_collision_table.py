import re

# Read the MetatileCollision.cs file and extract the lookup table
with open('native-windows/MetatileCollision.cs', 'r') as f:
    content = f.read()

# Find the mapping text between the @" and closing "
start = content.find('string mappingText = @"') + len('string mappingText = @"')
end = content.find('";', start)
mapping = content[start:end]

# Parse like the C# code does
table = ['COL_NONE'] * 256
lines = [l for l in mapping.split('\n') if l.strip()]
idx = 0
rx = re.compile(r'COL_[A-Z0-9_]+')
for raw in lines:
    if idx >= 256: break
    line = raw.strip()
    m = rx.search(line)
    if m:
        name = m.group()
        # Check for ;$XX index override
        dollar = line.find(';$')
        if dollar >= 0:
            hexval = line[dollar+2:].strip()
            idx = int(hexval, 16)
        if idx < 256:
            table[idx] = name
        idx += 1

# Print the tiles we care about
for t in [0x00, 0x01, 0x02, 0x05, 0x06, 0x6D, 0x6E, 0x6F]:
    print(f'0x{t:02X}: {table[t]}')

print()
# Also build full table for use in terrain checks
print("Full table for key ranges:")
for i in range(0x60, 0x70):
    print(f'  0x{i:02X}: {table[i]}')

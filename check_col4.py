with open('native-windows/MetatileCollision.cs', 'r') as f:
    content = f.read()

start = content.find('string mappingText = @"')
end = content.find('";', start + 22)
# Extract content AFTER the @" opening quote (skip the " itself)
quote_end = content.index('"', start + 21)  # find the actual " after @
mapping_text = content[quote_end+1:end]

import re
rx = re.compile(r'COL_[A-Z0-9_]+')
lines = [l for l in mapping_text.split('\n') if l.strip()]
idx = 0
cmap = {}
for line in lines:
    if idx >= 256:
        break
    line = line.strip()
    m = rx.search(line)
    if m:
        cmap[idx] = m.group(0)
    else:
        cmap[idx] = 'COL_NONE'  # no match = stays default
    idx += 1

print(f"Total tiles mapped: {idx}")

# Verify with anchor: tile 0x80 should have ;$80 comment
for t in [0x00, 0x01, 0x06, 0x07, 0x36, 0x50, 0x64, 0x70, 0x8A]:
    print(f"Tile 0x{t:02X} ({t:3d}): {cmap.get(t, 'UNKNOWN')}")

# Show tiles 0x6E-0x75 for verification
print("\nTiles 0x6E-0x76:")
for t in range(0x6E, 0x76):
    print(f"  0x{t:02X}: {cmap.get(t, '?')}")

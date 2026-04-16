with open('native-windows/MetatileCollision.cs', 'r') as f:
    content = f.read()

start = content.find('string mappingText = @"')
end = content.find('";', start + 22)
mapping_text = content[start+22:end]

lines = mapping_text.strip().split('\n')
tile_idx = 0
entries = []
for line in lines:
    line = line.strip()
    if not line:
        continue
    name = line.split(';')[0].strip()
    entries.append((tile_idx, name))
    tile_idx += 1

# Print first 20 entries to verify correctness
for idx, name in entries[:20]:
    print(f"  [{idx:3d}] 0x{idx:02X}: {name}")

print(f"... total={len(entries)}")

# Check specific ones
print()
for t in [0x01, 0x06, 0x07, 0x36, 0x50, 0x64, 0x70, 0x8A]:
    for idx, name in entries:
        if idx == t:
            print(f"Tile 0x{t:02X}: {name}")
            break

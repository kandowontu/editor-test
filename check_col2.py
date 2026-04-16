with open('native-windows/MetatileCollision.cs', 'r') as f:
    content = f.read()

start = content.find('string mappingText = @"')
end = content.find('";', start + 22)
mapping_text = content[start+22:end]

lines = mapping_text.strip().split('\n')
tile_idx = 0
cmap = {}
for line in lines:
    line = line.strip()
    if not line:
        continue
    name = line.split(';')[0].strip()
    cmap[tile_idx] = name
    tile_idx += 1

print(f"Total tiles mapped: {tile_idx}")
for t in [0x01, 0x06, 0x07, 0x36, 0x50, 0x64, 0x70, 0x8A]:
    print(f"Tile 0x{t:02X} ({t:3d}): {cmap.get(t, 'UNKNOWN')}")

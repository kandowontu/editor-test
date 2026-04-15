import struct

# Collision table from MetatileCollision.cs
COL_NAMES = {
    0x00: 'NONE', 0x6F: 'NONE', 0x6D: 'ALL', 0xB6: 'DEATH',
    0x01: 'FC', 0x02: 'FC', 0x05: 'FC', 0x06: 'FC', 0x88: 'FC', 0x89: 'FC',
}

with open('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/kratos.tmx', 'rb') as f:
    w = struct.unpack('<H', f.read(2))[0]
    h = struct.unpack('<H', f.read(2))[0]
    grr = struct.unpack('<H', f.read(2))[0]
    metatiles = list(f.read(w * h))

print(f'Map: {w}x{h}, groundRowsToReserve={grr}')
print()

# Check cols 708-716
for c in range(708, 717):
    print(f'Col {c} (X={c*16}):')
    for r in range(h):
        t = metatiles[r * w + c]
        engine_y = (r - grr) * 16
        name = COL_NAMES.get(t, f'0x{t:02X}')
        if t != 0x6F:  # skip passthrough filler
            print(f'  row={r} engineY={engine_y} tile=0x{t:02X} ({name})')
    print()

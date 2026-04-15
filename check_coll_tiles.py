import re

with open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()

m = re.search(r'new byte\[\]\s*\{([^}]+)\}', content, re.DOTALL)
if m:
    vals_str = m.group(1).replace('\n', ' ').replace('\r', ' ')
    vals = [int(x.strip(), 16) for x in vals_str.split(',') if x.strip()]
    print(f'Total entries: {len(vals)}')
    for tid in [0x11, 0x1B, 0x2F, 0x40, 0x4D, 0x4F, 0x50, 0x52, 0x70, 0x72, 
                0x77, 0x78, 0x79, 0x7A, 0x7B, 0x7C, 0x7F, 0x91, 0x92, 
                0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xBB]:
        if tid < len(vals):
            v = vals[tid]
            print(f'tile 0x{tid:02X}: coll=0x{v:02X}')

import re, os
tmx_path = r'c:\Editor Test\lvlset_HUGE\dorabaebasic6.tmx'
T = open(tmx_path, encoding='utf-8').read()

m = re.search(r'<layer[^>]*name="SP"[^>]*>(.*?)</layer>', T, re.DOTALL)
data_block = m.group(1)
mdata = re.search(r'<data[^>]*>(.*?)</data>', data_block, re.DOTALL)
encoding = re.search(r'encoding="([^"]+)"', mdata.group(0)).group(1)
print('encoding:', encoding)

W, H = 899, 27
content = mdata.group(1)
if encoding == 'csv':
    nums = [int(x) for x in re.findall(r'-?\d+', content)]
elif encoding == 'base64':
    import base64, struct
    raw = base64.b64decode(content.strip())
    nums = list(struct.unpack(f'<{len(raw)//4}I', raw))

print('tiles:', len(nums))

print('\n=== Sprites at cols 320-365 ===')
for row in range(H):
    for col in range(320, 366):
        gid = nums[row*W + col] & 0x1FFFFFFF
        if gid != 0:
            sid = gid - 257
            print(f'  col={col} row={row} px=({col*16},{row*16}) gid={gid} sid=0x{sid:02X}')

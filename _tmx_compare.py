import re, os, sys
for path in [
    r'c:\Editor Test\lvlset_HUGE\dorabaebasic6.tmx',
    r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\dorabaebasic6.tmx',
    r'c:\Editor Test\sim-famidash\LEVELS\LEVEL DATA\lvlset_D\dorabaebasic6.tmx',
]:
    if not os.path.exists(path):
        continue
    print('=====', path, '=====')
    T = open(path, encoding='utf-8').read()
    # Check SP layer dims and look for sid=0x73 in cols 320-360
    for tsm in re.finditer(r'<tileset\s+firstgid="(\d+)"[^>]*name="sprites"', T):
        sfg = int(tsm.group(1)); print('sprite firstgid', sfg)
        break
    else:
        sfg = 257
    m = re.search(r'<layer[^>]*name="SP"[^>]*width="(\d+)"[^>]*>(.*?)</layer>', T, re.DOTALL)
    if not m:
        print('no SP layer'); continue
    W = int(m.group(1))
    mdata = re.search(r'<data[^>]*>(.*?)</data>', m.group(2), re.DOTALL)
    nums = [int(x) for x in re.findall(r'-?\d+', mdata.group(1))]
    H = len(nums) // W
    print(f'W={W} H={H}')
    # search for sid 0x73 anywhere near tile cols 280-360
    found = False
    for row in range(H):
        for col in range(280, min(W, 365)):
            gid = nums[row*W + col] & 0x1FFFFFFF
            if gid != 0:
                sid = gid - sfg
                if 0x70 <= sid <= 0x74 or sid == 0x12 or sid == 0x02:
                    print(f'  col={col} row={row} px=({col*16},{row*16}) sid=0x{sid:02X}')
                    found = True
    if not found:
        print('  (no relevant sprites in 280-360)')

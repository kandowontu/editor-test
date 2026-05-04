import os
fp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_replay.csv'
data=open(fp,'rb').read()
if data[:2]==b'\xff\xfe': data=data.decode('utf-16').encode('utf-8')
text=data.decode('utf-8',errors='replace')
lines=text.split('\n')
# real header is line 1 (line 0 is "nes_y_offset,48")
hdr=lines[1].rstrip().split(',')
print('cols:', hdr)
for ln in lines[2:]:
    p=ln.rstrip().split(',')
    if not p or not p[0].isdigit(): continue
    f=int(p[0])
    if 920 <= f <= 935:
        print(' | '.join(f'{hdr[i]}={p[i]}' for i in range(min(len(hdr),len(p)))))

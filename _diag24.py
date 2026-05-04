import csv
fp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_mesen_trace.csv'
rows = list(csv.reader(open(fp)))
print('row[0]:', rows[0])
print('row[1]:', rows[1])
print('row[2]:', rows[2])
hdr = rows[1]
ci = hdr.index('cube_data')
sci = hdr.index('sim_cursor')
rfi = hdr.index('rom_frame')
pxi = hdr.index('px'); pyi = hdr.index('py')
vyi = hdr.index('vel_y')
sxi = hdr.index('scrollx'); syi = hdr.index('scrolly')
rxi = hdr.index('raw_x'); ryi = hdr.index('raw_y')
ti = hdr.index('table_idx')
gmi = hdr.index('gamemode')
for r in rows[2:]:
    if not r or not r[0].isdigit(): continue
    sc = int(r[sci]); rf = int(r[rfi])
    if 920 <= sc <= 935 or 935 <= rf <= 990:
        print(f'rf={rf} sc={sc} px={r[pxi]} py={r[pyi]} rawx={r[rxi]} rawy={r[ryi]} sx={r[sxi]} sy={r[syi]} vy={r[vyi]} cube={r[ci]} ti={r[ti]} gm={r[gmi]}')

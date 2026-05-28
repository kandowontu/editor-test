import csv

path = r'C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_test_20260516_034242_884.csv'
with open(path, newline='') as f:
    rdr = csv.DictReader(f)
    count = 0
    for row in rdr:
        if row.get('mode') == '2' and row.get('gravFlipped') == '1':
            xpx = int(row.get('X_px', 0))
            if 740 <= xpx <= 870:
                ypx = int(row.get('Y_px', 0))
                vy = int(row.get('VelY_fixed', '0'), 16)
                camY = row.get('CamY_px', '?')
                fr = row.get('frame', '?')
                yfixed = int(row.get('Y_fixed', '0'), 16)
                camyfixed = int(row.get('CamY_fixed', '0'), 16)
                screenY_high = (yfixed - camyfixed) >> 8
                onground = row.get('onGround', '?')
                print(f"f={fr:>5} X={xpx:>4} worldY={ypx:>4} camY={camY:>4} screenY={screenY_high:>4} VY=0x{vy & 0xFFFFFFFF:08X} og={onground}")
                count += 1
                if count >= 20:
                    break

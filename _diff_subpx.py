import csv, os, re
T = os.environ['TEMP']
pf_path = T + r'\famidash_pf_trace_cataclysm_20260518_151738_650.csv'
mt_path = T + r'\famidash_mesen_trace_cataclysm_20260518_151724_006.csv'

# PF: ScrollYSubpx is col index 22 (frame,X_fixed,Y_fixed,VelY_fixed,input,alive,X_px,Y_px,onGround,CamY_px,TgtCamY_px,mode,gravFlipped,...,CamY_fixed,Y_lowB,CamY_lowB,ScrollYSubpx,mini)
def h(s):
    s = s.strip()
    return int(s, 16) if s.startswith('0x') else int(s)

pf = {}
with open(pf_path) as f:
    f.readline()
    for line in f:
        p = line.rstrip().split(',')
        try: fr = int(p[0])
        except: continue
        pf[fr] = dict(
            subpx=int(p[22]),
            camY=h(p[19]),
            Y=h(p[2]),
            Y_low=h(p[20]),
            camY_low=h(p[21]),
            mode=int(p[11]),
        )

mt = {}
with open(mt_path) as f:
    f.readline()  # nes_y_offset,528
    f.readline()  # header
    for line in f:
        parts = line.rstrip('\n').split(',')
        extra = len(parts) - 30
        if extra > 0:
            parts = parts[:22] + [','.join(parts[22:22+extra+1])] + parts[22+extra+1:]
        try: s = int(parts[1])
        except: continue
        if s in mt: continue
        mt[s] = dict(
            subpx=int(parts[15]),  # scroll_y_subpx
            scrollY=int(parts[9]),
            rawY=int(parts[7]),
            scrollY_raw=int(parts[19]),
            mode=int(parts[14]),
        )

# Shifted alignment: PF[N] vs MT[N+1]
print(f'{"PFsim":>5} {"PFsub":>5} {"MTsub":>5} {"dSub":>5} {"PFcamLB":>7} {"PFcam":>6} {"MTscr":>6} {"PFmode":>4} {"MTmode":>4}')
firstDiv = None
for i in sorted(pf):
    if (i+1) not in mt: continue
    if i > 412: break
    p = pf[i]; m = mt[i+1]
    d = m['subpx'] - p['subpx']
    if firstDiv is None and d != 0:
        firstDiv = i
    if i <= 5 or (firstDiv is not None and i <= firstDiv + 5) or (i >= 360 and i <= 380):
        print(f'{i:>5} {p["subpx"]:>5} {m["subpx"]:>5} {d:>+5} {p["camY_low"]:>7} {p["camY"]>>8:>6} {m["scrollY"]:>6} {p["mode"]:>4} {m["mode"]:>4}')
print(f'\nFIRST subpx divergence at PFsim={firstDiv}')

# Find first cam-Y divergence too
firstCam = None
print('\n--- CAM-Y divergence sweep (PF.cam vs MT.scrollY-528) ---')
print(f'{"PFsim":>5} {"PFcam":>6} {"MTscr":>6} {"MTscr-528":>9} {"dCam":>5} {"PFsub":>5} {"MTsub":>5}')
for i in sorted(pf):
    if (i+1) not in mt: continue
    if i > 280: break
    p = pf[i]; m = mt[i+1]
    cam_pf = p['camY'] >> 8
    cam_mt = m['scrollY'] - 528
    dcam = cam_mt - cam_pf
    if firstCam is None and dcam != 0:
        firstCam = i
        # print 10 frames before and after
        print(f'>>>FIRST CAM DIVERGENCE at PFsim={i}<<<')
    if i <= 5 or (firstCam is not None and abs(i - firstCam) <= 8):
        print(f'{i:>5} {cam_pf:>6} {m["scrollY"]:>6} {cam_mt:>9} {dcam:>+5} {p["subpx"]:>5} {m["subpx"]:>5}')



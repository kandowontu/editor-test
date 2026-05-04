"""Convert tas_capture.csv into a tas_inputs.txt file for pf-test --tas mode.
Inputs are inferred per cube physics: input=True on the frame the player launches a jump
(detected by VelY transitioning to a strong negative when previously on ground or stationary)."""
import csv

rows = list(csv.reader(open('tas_capture.csv')))
hdr = rows[0]; rows = rows[1:]
ix_f=hdr.index('frame'); ix_vy=hdr.index('VelY_fixed'); ix_og=hdr.index('onGround')

inputs = []
prev_vy_signed = 0
prev_og = 1
for r in rows:
    f=int(r[ix_f])
    vy=int(r[ix_vy])
    if vy >= 0x8000: vy_s = vy - 0x10000
    else: vy_s = vy
    og=int(r[ix_og])
    # Detect jump launch: vy went strongly negative this frame, was on ground previously
    inp = (vy_s < -1000 and prev_vy_signed >= -1000)
    inputs.append('1' if inp else '0')
    prev_vy_signed = vy_s
    prev_og = og

IDLE = '|..|........|........'
JUMP = '|..|.U......|........'  # any non-idle pattern triggers input
with open('tas_inputs.txt','w') as fp:
    fp.write('\n'.join(JUMP if x=='1' else IDLE for x in inputs))
print(f'Wrote {len(inputs)} input frames; jumps={inputs.count("1")}')

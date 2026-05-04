import os
exec(open('_diag2.py').read().split('# Find first dy != 0')[0])
# Find first 50 nonzero
n=0
print('first 50 nonzero dy:')
for r in diverges:
    if r[0]>=30 and r[7]!=0:
        print(f'  rf={r[0]:>5} sf={r[1]:>5} rom=({r[2]:>5},{r[3]:>4}) sim=({r[4]:>5},{r[5]:>4}) dx={r[6]:>4} dy={r[7]:>4} a_n={r[8]} sim_a={r[9]}')
        n+=1
        if n>=50: break
print()
# transitions: where dy magnitude changes
print('dy>=2 events (rf, dy):')
prev=0
for r in diverges:
    if r[0]>=30 and abs(r[7])>=2 and abs(prev)<2:
        print(f'  rf={r[0]:>5} sf={r[1]:>5} dy={r[7]:>4}  rom_y={r[3]} sim_y={r[5]}')
    prev=r[7]

import re
import os

SIM_LOG = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260213_205728.txt')
PF_LOG = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260213_205337.txt')

# Get EXACT sim log: everything between L87136 and L87170 
# (frame 3666→3667 transition in the sim)
print("=== SIM: EXACT lines L87136-L87175 ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 87136 <= i <= 87175:
            clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
            print(f"  L{i}: {clean}")

# Also find: does the sim EVER do a COLL_UP or ceiling collision in flipped gravity?
print("\n=== SIM: Searching for COLL_UP or ceiling collision near death zone ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 86000 <= i <= 87200:
            if 'COLL_UP' in line or 'ceiling' in line or 'EJECT' in line:
                clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                print(f"  L{i}: {clean}")

# Search sim for wasZeroedByCollision=True near the death zone
print("\n=== SIM: wasZeroedByCollision=True in death zone ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 86000 <= i <= 87200:
            if 'wasZeroedByCollision=True' in line:
                clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                print(f"  L{i}: {clean}")

# Check what the sim pfFrameIndex says vs what PF frame number is used
print("\n=== SIM: PF Frame index injections around divergence ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 87050 <= i <= 87200:
            if '[PF] Frame' in line:
                clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                print(f"  L{i}: {clean}")

# Also check: where does the sim FIRST show the player on the ceiling in this section?
# Look for wasZeroed=True or velY=0 while in flipped gravity
print("\n=== SIM: Jump sequences in X range 10000-10188 ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 85000 <= i <= 87200:
            if 'JUMP TRIGGERED' in line or 'COLL_DOWN' in line or 'COLL_UP' in line:
                clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                print(f"  L{i}: {clean}")

# Look for gravity flipped slope checks: what does the sim check for ceiling?
print("\n=== SIM: SLOPE checks in flipped gravity (X 10100-10200) ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 86800 <= i <= 87200:
            if 'SLOPE' in line or 'COLL' in line or 'bg_coll' in line:
                clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                print(f"  L{i}: {clean}")

print("\n=== DONE ===")

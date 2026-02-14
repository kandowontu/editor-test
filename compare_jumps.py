import re
import os

SIM_LOG = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260213_205728.txt')
PF_LOG = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260213_205337.txt')

# Get context around the second jump (L86660) - a successful ceiling bounce in flipped gravity
print("=== SIM: Context around previous successful jump at L86660 ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 86630 <= i <= 86700:
            clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
            print(f"  L{i}: {clean}")

# Get context around the first jump (L85979)
print("\n=== SIM: Context around first successful jump at L85979 ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 85950 <= i <= 86010:
            clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
            print(f"  L{i}: {clean}")

# Also look at the PF's DECIDE info around the same frames to understand what inputs the PF sends
# PF frame 3667 at the divergence - what does the PF actually DECIDE?
print("\n=== PF: DECIDE context around f=3666-3667 ===")
pf_target_line = 1154755  # PF f=3667 STEP_START
with open(PF_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 1154750 <= i <= 1154770:
            clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
            print(f"  L{i}: {clean}")

# Check: In the sim, what does the jump check ACTUALLY test? 
# Let me look at frames where the PF sends input=True but no jump happens
# vs frames where jump DOES happen
print("\n=== SIM: All PF-JUMP logs between lines 85900-87200 ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 85900 <= i <= 87200:
            if 'PF-JUMP' in line:
                clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                print(f"  L{i}: {clean}")

# Also check: what is the gravity flip transition? When did gravity flip?
print("\n=== SIM: Gravity flip transitions around X=10000 ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 85800 <= i <= 86100:
            if 'FLIPPED' in line or 'GRAV_PORTAL' in line or 'portal' in line.lower():
                clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                print(f"  L{i}: {clean}")

print("\n=== DONE ===")

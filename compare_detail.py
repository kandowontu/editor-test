import re
import os

PF_LOG = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260213_205337.txt')
SIM_LOG = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260213_205728.txt')

# Find the sim STEP_START line numbers for frames around idx 69-70
sim_re = re.compile(r'\[STEP_START\] playerX_fixed=(0x[\dA-Fa-f]+) \((\d+)px\)')
print("=== SIM context around frame 69-70 (X=190px to 196px) ===")
sim_step_lines = []
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for lineno, line in enumerate(f):
        m = sim_re.search(line)
        if m:
            sim_step_lines.append((lineno, int(m.group(2))))

# Find the step around sim idx 69
target_lineno = None
for i, (lno, xpx) in enumerate(sim_step_lines):
    if i >= 67 and i <= 73:
        print(f"  sim_idx={i} lineno={lno} X={xpx}px")
        if i == 69:
            target_lineno = lno

# Print 30 lines of context around sim idx 69
if target_lineno:
    print(f"\n--- Sim detailed log around lineno {target_lineno} (sim idx=69, X=190px) ---")
    with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
        for i, line in enumerate(f):
            if i >= target_lineno - 5 and i <= target_lineno + 45:
                clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                print(f"  [L{i}] {clean}")

# Now do the same for PF - find context around PF f=69 and f=70
print(f"\n=== PF context around f=69-70 (X=190px to 193px) ===")
pf_step_re = re.compile(r'\[PF f=(\d+)\] \[STEP_START\]')
# Find the LAST occurrence of f=69
pf_target = None
with open(PF_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        m = pf_step_re.search(line)
        if m and int(m.group(1)) == 69:
            pf_target = i

if pf_target:
    print(f"--- PF detailed log around lineno {pf_target} (PF f=69) ---")
    with open(PF_LOG, 'r', encoding='utf-8', errors='replace') as f:
        for i, line in enumerate(f):
            if i >= pf_target - 5 and i <= pf_target + 45:
                clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                print(f"  [L{i}] {clean}")

# Also check: what X value does the sim have that's NOT in PF? (0x0A5F0)
print(f"\n=== What is sim X=0x0A5F0? ===")
for i, (lno, xpx) in enumerate(sim_step_lines):
    if xpx >= 160 and xpx <= 175:
        print(f"  sim_idx={i} lineno={lno} X={xpx}px")

# Find context around 0xA5F0
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if '0x0A5F0' in line or '0xA5F0' in line:
            clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
            if 'STEP_START' in line:
                print(f"  [L{i}] {clean}")
                break

print("\n=== DONE ===")

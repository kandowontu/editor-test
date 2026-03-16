import re, sys, os

sim_path = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260315_222722.txt')
pf_path = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260315_221659.txt')

# Parse SIM log: extract PF Frame number and STEP_START Y values
sim_frames = {}  # frame_num -> (Y_px, velY_hex, X_px)
current_frame = None
step_re = re.compile(r'\[STEP_START\] playerX_fixed=0x([0-9A-Fa-f]+) \((\d+)px\), playerY_fixed=0x([0-9A-Fa-f]+) \((\d+)px\), playerVelY_fixed=(0x[0-9A-Fa-f]+)')
pf_frame_re = re.compile(r'\[PF\] Frame (\d+):.*velY=(0x[0-9A-Fa-f]+), wasZeroed=(\w+)')
grav_pos_re = re.compile(r'\[GRAV_POS\] posY: 0x[0-9A-Fa-f]+ \((\d+)px\) -> 0x[0-9A-Fa-f]+ \((\d+)px\), velY: (0x[0-9A-Fa-f]+) -> (0x[0-9A-Fa-f]+)')
physics_re = re.compile(r'\[PHYSICS\].*Mode=(\d+)')
eject_re = re.compile(r'bg_coll_[DU].*eject|EJECT|eject_D=|eject_U=')
robot_re = re.compile(r'\[ROBOT\].*jumpPressed=(\w+).*holdJump=(\w+)')

print("=== SIM Frame Analysis (last 80 frames) ===")
with open(sim_path, 'r') as f:
    lines = f.readlines()

# Find last 80 STEP_START entries
step_starts = []
for i, line in enumerate(lines):
    if '[STEP_START]' in line:
        step_starts.append(i)

for start_idx in step_starts[-80:]:
    # Read this frame's block (up to next STEP_START or end)
    frame_num = None
    y_px = None
    x_px = None
    vel_y = None
    mode = None
    post_y = None
    post_vel = None
    robot_info = None
    
    for j in range(start_idx, min(start_idx + 25, len(lines))):
        line = lines[j].strip()
        # Remove timestamp
        if '] ' in line:
            line = line.split('Z ', 1)[-1] if 'Z ' in line else line
        
        m = step_re.search(lines[j])
        if m:
            x_px = int(m.group(2))
            y_px = int(m.group(4))
            vel_y = m.group(5)
        
        m = pf_frame_re.search(lines[j])
        if m:
            frame_num = int(m.group(1))
            
        m = physics_re.search(lines[j])
        if m:
            mode = int(m.group(1))
            
        m = grav_pos_re.search(lines[j])
        if m:
            post_y = int(m.group(2))
            post_vel = m.group(4)
            
        m = robot_re.search(lines[j])
        if m:
            robot_info = f"press={m.group(1)} hold={m.group(2)}"
            
        if j > start_idx and '[STEP_START]' in lines[j]:
            break
    
    if frame_num and mode == 4:
        extra = f" robot:{robot_info}" if robot_info else ""
        post = f" -> Y={post_y} velY={post_vel}" if post_y is not None else ""
        print(f"  SIM f={frame_num}: X={x_px} Y={y_px} velY={vel_y}{post}{extra}")

# Parse PF log for same frames
print("\n=== PF Frame Analysis (robot section near death) ===")
pf_step_re = re.compile(r'\[PF f=(\d+)\].*X=(\d+).*Y=(\d+).*VelY=([-0-9x]+|0x[0-9A-Fa-f]+)')
pf_input_re = re.compile(r'\[PF f=(\d+)\] \[INPUT\] (JUMP|NONE|HOLD)')
pf_eject_re = re.compile(r'\[PF f=(\d+)\].*eject|EJECT')
pf_land_re = re.compile(r'\[PF f=(\d+)\].*LAND|floor_hit|floorHit')

with open(pf_path, 'r') as f:
    pf_lines = f.readlines()

# Find robot section frames 2500-2620
for line in pf_lines:
    m = re.search(r'\[PF f=(\d+)\]', line)
    if m:
        fn = int(m.group(1))
        if 2590 <= fn <= 2620:
            print(f"  {line.strip()}")

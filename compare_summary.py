import re
import os

# Verify the tile layout at X=10141, Y=256 area
# the ceiling should be at tile row 15 (Y=240-256) based on our analysis

PF_LOG = os.path.join(os.environ['TEMP'], 'famidash_pf_debug_20260213_205337.txt')
SIM_LOG = os.path.join(os.environ['TEMP'], 'famidash_sim_debug_20260213_205728.txt')

# Search for GROUND_LOST at X~10141 in PF - this tells where the ceiling surface is
print("=== PF: GROUND_LOST around X=10141 ===")
with open(PF_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 1154650 <= i <= 1154770:
            if 'EJECT' in line or 'GROUND_LOST' in line or 'ceiling' in line.lower():
                clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
                print(f"  L{i}: {clean}")

# Check the SIM's previous ceiling landing to understand eject behavior
# At L86646, sim shows Y=0xF000 (240px), VelY=0. Before that: Y moved to 237px (0xED20)
# Let's trace what happened between GRAV_POS at 237 and STEP_START at 240
print("\n=== SIM: Tracing first ceiling landing (L86637 to L86646) ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if 86630 <= i <= 86660:
            clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
            print(f"  L{i}: {clean}")

# Verify the sim DOES zero velocity at the divergence frame via the extra check
# The key: L87149 shows GRAV_POS → 256px, then L87150 shows PF-JUMP with velY=0
# There's NOTHING between those two lines that logs the zeroing
# This confirms it's the UNCOMMENTED extra check at lines 110-128 of CubePhysics_Fresh

# Let me also check what groundRowsToReserve is for this level
print("\n=== SIM: Ground layer info ===")
with open(SIM_LOG, 'r', encoding='utf-8', errors='replace') as f:
    for i, line in enumerate(f):
        if i > 200:
            break
        if 'ground' in line.lower() or 'groundRow' in line or 'hasGround' in line:
            clean = re.sub(r'^\d{4}-\d{2}-\d{2}T[\d:.]+Z ', '', line.rstrip())
            print(f"  L{i}: {clean}")

# Summary of the divergence
print("""
============================================================
ROOT CAUSE ANALYSIS - COMPLETE
============================================================

DIVERGENCE POINT: X = 10141px (0x279D8C)
  Sim frame: idx=3654 -> 3655 (pfFrameIndex 3666 -> 3667)
  PF frame:  f=3666 -> f=3667
  Frame offset: +12 (PF has 12 extra frames from earlier cliff edges)

WHAT HAPPENS:
  Both start at X=10138: Y=0x10443(260px), VelY=-823 (matching perfectly)
  Gravity applied: VelY -823 + (-107) = -930, Y: 0x10443 + (-930) = 0x100A1 (256.63px)
  
  SIM: After gravity, extra CheckCollisionUp at Y=(256>>0)-1=255 finds ceiling tile
       → VelY zeroed to 0 (velocity zeroed but position NOT snapped)
       → Jump check: VelY==0, input=hold → JUMP TRIGGERED → VelY = +0x0590 (1424)
       → STEP_START: Y=0x100A1(256px), VelY=+1424 (bouncing away from ceiling)
  
  PF:  After gravity, goes directly to CubeEject
       → CubeEject's CheckCeiling at Y=256: topY(256) < colBottom(256) is FALSE
       → No collision detected, velocity stays at -930
       → Jump check: VelY=-930 ≠ 0 → no jump
       → STEP_START: Y=0x100A1(256px), VelY=-930 (still moving toward ceiling)

THE BUG:
  The SIM has an extra CheckCollisionUp call in ProcessCubePhysics_Fresh()
  (CubePhysics_Fresh.partial.cs lines 110-128) that runs BETWEEN the gravity
  routine and CubeEject. This check tests at Y-1 (one pixel above the player),
  detecting a ceiling tile at pixel 255 (tile row 15, Y=240-256).
  
  The PF (PathfinderEngine.cs) does NOT have this extra check. It only checks
  ceiling collision inside CubeEject(), which tests at Y=256 where the boundary
  check (topY < colBottom_px) evaluates 256 < 256 = FALSE.
  
  SIM code (CubePhysics_Fresh.partial.cs line 117):
    int testY_check = (playerY_fixed >> 8) + hitboxOffsetY_check - 1;
    var (collided_check, _) = CheckCollisionUp(collisionX_check, testY_check, ...);
    if (collided_check && playerVelY_fixed < 0)
        playerVelY_fixed = 0;  // ← ZEROES VELOCITY, ENABLES JUMP
  
  PF code (PathfinderEngine.cs) - MISSING the equivalent check.
  The PF only has CubeEject → CheckCeiling which fails at the boundary.

CONSEQUENCE:
  - At X=10141: sim jumps (VelY=+1424), PF continues falling (VelY=-930)
  - From X=10141 onward: sim stays alive bouncing off ceiling until X=10188
  - PF falls past the ceiling at X=10144 (Y=252→eject to 256) - one frame late
  - After PF recovers, physics are permanently offset by one ceiling bounce
  - PF survives longer (to X=11825) because its trajectory is slightly different
  - Sim dies at X=10188 because it's on a different trajectory

FIX:
  Add the equivalent ceiling proximity check in PathfinderEngine.cs CubeEject()
  or add a post-gravity ceiling check matching lines 110-128 of CubePhysics_Fresh.
""")

using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private void UpdateNinjaPhysics(bool holdingJump, bool pressedJump)
        {
            int tableIdx = GameModePhysics.GetTableIdx(gravityFlipped, miniMode);
            
            // Apply gravity
            int gravity = GameModePhysics.CUBE_GRAVITY(tableIdx);
            int maxFallSpeed = GameModePhysics.CUBE_MAX_FALLSPEED(tableIdx, CUBE_MAX_FALLSPEED);
            
            ApplyGravity(gravity, maxFallSpeed);
            
            // Apply velocity
            playerY_fixed += velocityY;
            
            // Simple collision
            int collisionTop = (int)(playerY_fixed / 256.0);
            int collisionBottom = collisionTop + 15;
            int collisionLeft = (int)(playerX_fixed / 256.0);
            int collisionRight = collisionLeft + 15;
            
            CheckCollisionAndAdjust(ref playerY_fixed, ref velocityY, collisionLeft, collisionRight, collisionTop, collisionBottom);
            
            // Reset ninja jumps when grounded
            if (velocityY == 0)
            {
                ninjaJumps = 3;
                ninjaJumpedThisFrame = false;
            }
            
            // Ninja triple jump logic
            if (pressedJump && ninjaJumps > 0 && !ninjaJumpedThisFrame)
            {
                velocityY = GameModePhysics.JUMP_VEL(tableIdx);
                ninjaJumps--;
                ninjaJumpedThisFrame = true;
            }
            
            if (!pressedJump)
            {
                ninjaJumpedThisFrame = false;
            }
        }
    }
}

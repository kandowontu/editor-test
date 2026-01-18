using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private void UpdateUfoPhysics(bool holdingJump, bool pressedJump)
        {
            int tableIdx = GameModePhysics.GetTableIdx(gravityFlipped, miniMode);
            
            // Apply gravity
            int gravity = GameModePhysics.UFO_GRAVITY(tableIdx);
            int maxFallSpeed = GameModePhysics.UFO_MAX_FALLSPEED(tableIdx);
            
            ApplyGravity(gravity, maxFallSpeed);
            
            // UFO jump impulse on click
            if (pressedJump)
            {
                velocityY = GameModePhysics.UFO_JUMP_VEL(tableIdx);
            }
            
            // Apply velocity
            playerY_fixed += velocityY;
            
            // Simple collision
            int collisionTop = (int)(playerY_fixed / 256.0);
            int collisionBottom = collisionTop + 15;
            int collisionLeft = (int)(playerX_fixed / 256.0);
            int collisionRight = collisionLeft + 15;
            
            // Check up collision
            if (CheckCollisionUp(collisionLeft, collisionRight, collisionTop))
            {
                playerY_fixed = (collisionTop + 1) * 256;
                velocityY = 0;
            }
            
            // Check down collision
            if (CheckCollisionDown(collisionLeft, collisionRight, collisionBottom))
            {
                playerY_fixed = (collisionBottom - 15) * 256;
                velocityY = 0;
            }
        }
    }
}

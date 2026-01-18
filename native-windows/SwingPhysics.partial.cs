using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private void UpdateSwingPhysics(bool holdingJump, bool pressedJump)
        {
            int tableIdx = GameModePhysics.GetTableIdx(gravityFlipped, miniMode);
            
            // Apply gravity with swing constants
            int gravity = GameModePhysics.SWING_GRAVITY(tableIdx);
            int maxFallSpeed = GameModePhysics.SWING_MAX_FALLSPEED(tableIdx);
            
            ApplyGravity(gravity, maxFallSpeed);
            
            // Apply velocity
            playerY_fixed += velocityY;
            
            // Swing collision (same as ball - offset 1 pixel)
            int collisionTop = (int)(playerY_fixed / 256.0);
            int collisionBottom = collisionTop + 15;
            int collisionLeft = (int)(playerX_fixed / 256.0);
            int collisionRight = collisionLeft + 15;
            
            // Offset collision check
            int checkTop = gravityFlipped ? collisionTop - 1 : collisionTop;
            int checkBottom = gravityFlipped ? collisionBottom : collisionBottom + 1;
            
            // Check up collision
            bool hitUp = CheckCollisionUp(collisionLeft, collisionRight, checkTop);
            if (hitUp)
            {
                playerY_fixed = (checkTop + 1) * 256;
                velocityY = 0;
            }
            
            // Check down collision
            bool hitDown = CheckCollisionDown(collisionLeft, collisionRight, checkBottom);
            if (hitDown)
            {
                playerY_fixed = (checkBottom - 15) * 256;
                velocityY = 0;
            }
            
            // Swing gravity flip on press (not hold like ball)
            if (pressedJump)
            {
                gravityFlipped = !gravityFlipped;
            }
        }
    }
}

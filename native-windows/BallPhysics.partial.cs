using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private void UpdateBallPhysics(bool holdingJump, bool pressedJump)
        {
            int tableIdx = GameModePhysics.GetTableIdx(gravityFlipped, miniMode);
            
            // Apply gravity
            int gravity = GameModePhysics.BALL_GRAVITY(tableIdx);
            int maxFallSpeed = GameModePhysics.BALL_MAX_FALLSPEED(tableIdx);
            
            ApplyGravity(gravity, maxFallSpeed);
            
            // Apply velocity
            playerY_fixed += velocityY;
            
            // Ball collision (offset 1 pixel based on gravity)
            int collisionTop = (int)(playerY_fixed / 256.0);
            int collisionBottom = collisionTop + 15;
            int collisionLeft = (int)(playerX_fixed / 256.0);
            int collisionRight = collisionLeft + 15;
            
            // Offset collision check (NES does this for vel reset every frame)
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
            
            // Ball gravity flip on ground
            if (holdingJump && velocityY == 0)
            {
                bool switched = ballSwitched[0]; // Use first player for now
                if (!switched)
                {
                    // Flip gravity
                    gravityFlipped = !gravityFlipped;
                    tableIdx = GameModePhysics.GetTableIdx(gravityFlipped, miniMode);
                    velocityY = GameModePhysics.BALL_SWITCH_VEL(tableIdx);
                    ballSwitched[0] = true;
                }
            }
            else if (!holdingJump)
            {
                ballSwitched[0] = false;
            }
        }
    }
}

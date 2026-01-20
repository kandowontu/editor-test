using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private void UpdateRobotPhysics(bool holdingJump, bool pressedJump)
        {
            int tableIdx = GameModePhysics.GetTableIdx(gravityFlipped, miniMode);
            
            // Apply gravity
            int gravity = GameModePhysics.CUBE_GRAVITY(tableIdx);
            int maxFallSpeed = GameModePhysics.CUBE_MAX_FALLSPEED(tableIdx, CUBE_MAX_FALLSPEED);
            
            ApplyGravity(gravity, maxFallSpeed);
            
            // Apply velocity
            playerY_fixed += velocityY;
            
            // Simple collision (robot doesn't use slopes)
            int collisionTop = (int)(playerY_fixed / 256.0);
            int collisionBottom = collisionTop + 15;
            int collisionLeft = (int)(playerX_fixed / 256.0);
            int collisionRight = collisionLeft + 15;
            
            CheckCollisionAndAdjust(ref playerY_fixed, ref velocityY, collisionLeft, collisionRight, collisionTop, collisionBottom);
            
            // Robot jump logic
            if (pressedJump)
            {
                robotJumpPressed = true;
            }
            
            // Check if on ground and can jump
            bool onGround = velocityY == 0 && CheckGrounded(collisionLeft, collisionRight, collisionBottom);
            
            if (onGround && robotJumpPressed)
            {
                if (holdingJump)
                {
                    if (pressedJump)
                    {
                        // Initial jump
                        velocityY = GameModePhysics.ROBOT_JUMP_VEL(tableIdx);
                        robotJumpTime[0] = GameModePhysics.ROBOT_JUMP_TIME; // 60fps
                        robotJumpFrame[0] = 1;
                    }
                }
            }
            else if (robotJumpTime[0] > 0)
            {
                // Continue holding jump
                robotJumpTime[0]--;
                if (holdingJump)
                {
                    velocityY = GameModePhysics.ROBOT_JUMP_VEL(tableIdx);
                }
                else
                {
                    robotJumpTime[0] = 0;
                }
            }
            
            if (!holdingJump)
            {
                robotJumpPressed = false;
            }
        }
        
        private bool CheckGrounded(int left, int right, int bottom)
        {
            // Check 1 pixel below
            for (int x = left; x <= right; x++)
            {
                byte tile = GetTileAt(x, bottom + 1);
                if (IsSolidTile(tile))
                    return true;
            }
            return false;
        }
    }
}

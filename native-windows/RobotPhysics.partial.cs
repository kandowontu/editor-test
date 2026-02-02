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
            
            // Collision hitbox - matches collision.h
            // Mini mode: 8x7 hitbox, offset 4 pixels down (0x10-0x07)>>1 = 4
            // Normal mode: 15x15 hitbox
            int hitboxW = miniMode ? 8 : 15;
            int hitboxH = miniMode ? 7 : 15;
            int yOffset = miniMode ? ((0x10 - hitboxH) >> 1) : 0;  // 4 for mini, 0 for normal
            
            int collisionTop = (int)(playerY_fixed / 256.0) + yOffset;
            int collisionBottom = collisionTop + hitboxH;
            int collisionLeft = (int)(playerX_fixed / 256.0);
            int collisionRight = collisionLeft + hitboxW;
            
            CheckCollisionAndAdjust(ref playerY_fixed, ref velocityY, collisionLeft, collisionRight, collisionTop, collisionBottom);
            
            // Robot jump logic
            if (pressedJump)
            {
                robotJumpPressed = true;
            }
            
            // Check if on ground and can jump
            bool onGround = velocityY == 0 && CheckGroundedRobot(collisionLeft, collisionRight, collisionBottom);
            
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
        
        private bool CheckGroundedRobot(int left, int right, int bottom)
        {
            // Check 1 pixel below the bottom of hitbox
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

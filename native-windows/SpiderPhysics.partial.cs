using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private void UpdateSpiderPhysics(bool holdingJump, bool pressedJump)
        {
            int tableIdx = GameModePhysics.GetTableIdx(gravityFlipped, miniMode);
            
            // Apply gravity
            int gravity = GameModePhysics.SPIDER_GRAVITY(tableIdx);
            int maxFallSpeed = GameModePhysics.SPIDER_MAX_FALLSPEED(tableIdx);
            
            ApplyGravity(gravity, maxFallSpeed);
            
            // Apply velocity
            playerY_fixed += velocityY;
            
            // Spider collision (offset based on gravity)
            int collisionTop = (int)(playerY_fixed / 256.0);
            int collisionBottom = collisionTop + 15;
            int collisionLeft = (int)(playerX_fixed / 256.0);
            int collisionRight = collisionLeft + 15;
            
            // Offset collision check
            int checkTop = gravityFlipped ? collisionTop - 2 : collisionTop;
            int checkBottom = gravityFlipped ? collisionBottom : collisionBottom + 1;
            
            // Check collision based on gravity
            if (!gravityFlipped)
            {
                // Normal gravity - check down
                bool hitDown = CheckCollisionDown(collisionLeft, collisionRight, checkBottom);
                if (hitDown)
                {
                    playerY_fixed = (checkBottom - 15) * 256;
                    velocityY = 0;
                }
                
                // Spider teleport to ceiling on press
                if (pressedJump && velocityY == 0)
                {
                    // Flip gravity and teleport up
                    gravityFlipped = true;
                    SpiderTeleportUp(collisionLeft, collisionRight);
                    velocityY = 0;
                }
            }
            else
            {
                // Inverted gravity - check up
                bool hitUp = CheckCollisionUp(collisionLeft, collisionRight, checkTop);
                if (hitUp)
                {
                    playerY_fixed = (checkTop + 1) * 256;
                    velocityY = 0;
                }
                
                // Spider teleport to floor on press
                if (pressedJump && velocityY == 0)
                {
                    // Flip gravity and teleport down
                    gravityFlipped = false;
                    SpiderTeleportDown(collisionLeft, collisionRight);
                    velocityY = 0;
                }
            }
        }
        
        private void SpiderTeleportUp(int left, int right)
        {
            // Find ceiling above
            int currentY = (int)(playerY_fixed / 256.0);
            for (int y = currentY - 1; y >= 0; y--)
            {
                bool foundCeiling = false;
                for (int x = left; x <= right; x++)
                {
                    if (IsSolidTile(GetTileAt(x, y)))
                    {
                        foundCeiling = true;
                        break;
                    }
                }
                
                if (foundCeiling)
                {
                    // Position just below ceiling
                    playerY_fixed = (y + 1) * 256;
                    return;
                }
            }
        }
        
        private void SpiderTeleportDown(int left, int right)
        {
            // Find floor below
            int currentY = (int)(playerY_fixed / 256.0) + 15;
            for (int y = currentY + 1; y < 256; y++)
            {
                bool foundFloor = false;
                for (int x = left; x <= right; x++)
                {
                    if (IsSolidTile(GetTileAt(x, y)))
                    {
                        foundFloor = true;
                        break;
                    }
                }
                
                if (foundFloor)
                {
                    // Position on top of floor
                    playerY_fixed = (y - 15) * 256;
                    return;
                }
            }
        }
    }
}

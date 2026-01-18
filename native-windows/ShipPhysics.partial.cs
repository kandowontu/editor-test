using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private void UpdateShipPhysics(bool holdingJump, bool pressedJump)
        {
            int tableIdx = GameModePhysics.GetTableIdx(gravityFlipped, miniMode);
            
            // Ship has complex gravity logic based on input and falling state
            bool isFalling = gravityFlipped ? (velocityY < 0) : (velocityY > 0);
            
            int gravity;
            if (holdingJump)
            {
                if (isFalling)
                {
                    // Holding while falling
                    gravity = (currplayer_mini != 0 ? GameModePhysics.MINI_SHIP_GRAVITY_HOLD_FALL : GameModePhysics.SHIP_GRAVITY_HOLD_FALL);
                }
                else
                {
                    // Holding while rising
                    gravity = (currplayer_mini != 0 ? GameModePhysics.MINI_SHIP_GRAVITY_BASE : GameModePhysics.SHIP_GRAVITY_BASE);
                }
            }
            else if (!holdingJump && !isFalling)
            {
                // Not holding and not falling (released after rising)
                gravity = (currplayer_mini != 0 ? GameModePhysics.MINI_SHIP_GRAVITY_AFTER_HOLD : GameModePhysics.SHIP_GRAVITY_AFTER_HOLD);
            }
            else
            {
                // Default gravity
                gravity = GameModePhysics.SHIP_GRAVITY(tableIdx);
            }
            
            // Invert gravity if holding and gravity direction doesn't match
            if (holdingJump ^ (gravityFlipped ? true : false))
            {
                gravity = -gravity;
            }
            
            // Apply gravity
            velocityY += gravity;
            
            // Ship max fallspeed is 0x380
            if (gravityFlipped)
            {
                if (velocityY < -0x380)
                    velocityY = -0x380;
                if (velocityY > 0x380)
                    velocityY = 0x380;
            }
            else
            {
                if (velocityY < -0x380)
                    velocityY = -0x380;
                if (velocityY > 0x380)
                    velocityY = 0x380;
            }
            
            // Apply velocity
            playerY_fixed += velocityY;
            
            // Simple collision (no slopes for ship)
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
        
        private bool CheckCollisionUp(int left, int right, int top)
        {
            for (int x = left; x <= right; x++)
            {
                byte tile = GetTileAt(x, top);
                if (IsSolidTile(tile))
                    return true;
            }
            return false;
        }
        
        private bool CheckCollisionDown(int left, int right, int bottom)
        {
            for (int x = left; x <= right; x++)
            {
                byte tile = GetTileAt(x, bottom);
                if (IsSolidTile(tile))
                    return true;
            }
            return false;
        }
    }
}

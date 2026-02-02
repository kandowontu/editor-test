using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private void UpdateWavePhysics(bool holdingJump, bool pressedJump)
        {
            // Wave uses horizontal velocity for vertical movement
            // vel_y = +/- vel_x based on input and mini mode
            
            // Assume vel_x from speed (need to get from speed system)
            int vel_x = 0x0300; // Default speed, should come from CUBE_SPEED
            
            if (miniMode)
            {
                vel_x = vel_x << 1; // Double for mini
            }
            
            // Calculate vel_y based on gravity and input
            if (gravityFlipped)
            {
                velocityY = -vel_x;
            }
            else
            {
                velocityY = vel_x;
            }
            
            // Invert if holding jump
            if (holdingJump)
            {
                velocityY = -velocityY;
            }
            
            // Apply velocity
            playerY_fixed += velocityY;
            
            // Collision hitbox - matches collision.h
            // Mini mode: 8x7 hitbox, offset 4 pixels down (0x10-0x07)>>1 = 4
            // Normal mode: 15x15 hitbox
            // Wave has narrower X hitbox (offset +4 pixels, width reduced)
            int hitboxW = miniMode ? 8 : 15;
            int hitboxH = miniMode ? 7 : 15;
            int yOffset = miniMode ? ((0x10 - hitboxH) >> 1) : 0;  // 4 for mini, 0 for normal
            
            int collisionTop = (int)(playerY_fixed / 256.0) + yOffset;
            int collisionBottom = collisionTop + hitboxH;
            int collisionLeft = (int)(playerX_fixed / 256.0) + 4; // Wave is narrower
            int collisionRight = collisionLeft + 7;
            
            int checkY = (velocityY < 0) ? collisionTop + 2 : collisionBottom - 2;
            
            // Check collision in movement direction
            if (velocityY < 0)
            {
                // Moving up
                bool hitUp = false;
                for (int x = collisionLeft; x <= collisionRight; x++)
                {
                    if (IsSolidTile(GetTileAt(x, collisionTop)))
                    {
                        hitUp = true;
                        break;
                    }
                }
                
                if (hitUp)
                {
                    playerY_fixed = (collisionTop + 1) * 256;
                    velocityY = 0;
                }
            }
            else
            {
                // Moving down
                bool hitDown = false;
                for (int x = collisionLeft; x <= collisionRight; x++)
                {
                    if (IsSolidTile(GetTileAt(x, collisionBottom)))
                    {
                        hitDown = true;
                        break;
                    }
                }
                
                if (hitDown)
                {
                    playerY_fixed = (collisionBottom - 15) * 256;
                    velocityY = 0;
                }
            }
        }
    }
}

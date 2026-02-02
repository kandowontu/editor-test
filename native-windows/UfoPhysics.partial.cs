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
            
            // Check up collision
            if (CheckCollisionUp(collisionLeft, collisionRight, collisionTop))
            {
                playerY_fixed = (collisionTop - yOffset + 1) * 256;
                velocityY = 0;
            }
            
            // Check down collision
            if (CheckCollisionDown(collisionLeft, collisionRight, collisionBottom))
            {
                playerY_fixed = (collisionBottom - hitboxH - yOffset) * 256;
                velocityY = 0;
            }
        }
    }
}

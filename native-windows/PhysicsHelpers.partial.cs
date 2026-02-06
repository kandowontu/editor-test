using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ========================================================================
        // SHARED PHYSICS HELPER METHODS (used by all game modes)
        // ========================================================================
        
        /// <summary>
        /// Apply gravity and clamp to max fall speed
        /// From common_gravity_routine() in gamemode_cube.h
        /// </summary>
        private void ApplyGravity(int gravity, int maxFallSpeed)
        {
            // Apply gravity acceleration with multiplier from gravity mod portals
            int tmpaccel = (int)(gravity * gravityMultiplier);
            
            // Check if at max fall speed
            bool atMaxFall = gravityFlipped ? 
                (velocityY <= maxFallSpeed) : 
                (velocityY >= maxFallSpeed);
            
            if (atMaxFall)
            {
                // Already at max - invert gravity to slow down slightly
                tmpaccel = -tmpaccel;
            }
            
            // Apply acceleration
            velocityY += tmpaccel;
            
            AppendSimDebug($"[GRAVITY] Applied: accel={tmpaccel} (base={gravity}, mult={gravityMultiplier:F2}), newVelY={velocityY}, maxFall={maxFallSpeed}");
        }
        
        /// <summary>
        /// Get tile at world position (handles both normal and ground layers)
        /// </summary>
        private byte GetTileAt(int x, int y)
        {
            try
            {
                if (x < 0 || y < 0 || x >= 256 || y >= 256)
                    return 0;
                
                // Check ground layer first if it exists (ground layer would be handled in collision system)
                // For now just check normal layer
                int row = y / 16;
                int column = x / 16;
                int tileIdx = row * 16 + column;
                
                if (tileIdx >= 0 && tileIdx < tiles.Length)
                {
                    return (byte)tiles[tileIdx];
                }
                
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        
        /// <summary>
        /// Check if tile is solid (for collision)
        /// </summary>
        private bool IsSolidTile(byte tile)
        {
            try
            {
                if (tile == 0) return false;
                
                // Get collision bounds - if tile has any solid area, it's solid
                var collision = MetatileCollisionTable.GetCollision(tile);
                
                // Empty collision = not solid
                if (collision == MetatileCollision.COL_NONE)
                    return false;
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Unified collision check and adjustment (used by non-cube modes)
        /// </summary>
        private void CheckCollisionAndAdjust(ref int posY, ref int velY, int left, int right, int top, int bottom)
        {
            try
            {
                // Check down collision
                bool hitDown = false;
                for (int x = left; x <= right; x++)
                {
                    if (IsSolidTile(GetTileAt(x, bottom)))
                    {
                        hitDown = true;
                        break;
                    }
                }
                
                if (hitDown)
                {
                    posY = (bottom - 15) * 256;
                    velY = 0;
                    AppendSimDebug($"[COLLISION] Hit down at y={bottom}");
                }
                
                // Check up collision
                bool hitUp = false;
                for (int x = left; x <= right; x++)
                {
                    if (IsSolidTile(GetTileAt(x, top)))
                    {
                        hitUp = true;
                        break;
                    }
                }
                
                if (hitUp)
                {
                    posY = (top + 1) * 256;
                    velY = 0;
                    AppendSimDebug($"[COLLISION] Hit up at y={top}");
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[COLLISION] Error: {ex.Message}");
            }
        }
    }
}


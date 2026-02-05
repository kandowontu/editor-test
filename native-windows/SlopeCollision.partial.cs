using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ========================================================================
        // SLOPE COLLISION SYSTEM
        // 1:1 Port from famidash/SAUCE/functions/collision.h bg_coll_slope()
        // ========================================================================
        
        // Slope type constants (from collision.h)
        private const int SLOPE_NONE = 0b0000;
        private const int SLOPE_45DEG = 0b01;
        private const int SLOPE_22DEG = 0b10;
        private const int SLOPE_66DEG = 0b11;
        private const int SLOPE_RISING = 0b0100;
        private const int SLOPE_UPSIDEDOWN = 0b1000;
        private const int SLOPE_RISING_MASK = 0b0111;
        private const int SLOPE_DEGREES_MASK = 0b0011;
        
        // Slope type combinations
        private const int SLOPE_45DEG_UP = SLOPE_45DEG | SLOPE_RISING;           // 0101
        private const int SLOPE_45DEG_DOWN = SLOPE_45DEG;                        // 0001
        private const int SLOPE_22DEG_UP = SLOPE_22DEG | SLOPE_RISING;           // 0110
        private const int SLOPE_22DEG_DOWN = SLOPE_22DEG;                        // 0010
        private const int SLOPE_66DEG_UP = SLOPE_66DEG | SLOPE_RISING;           // 0111
        private const int SLOPE_66DEG_DOWN = SLOPE_66DEG;                        // 0011
        
        private const int SLOPE_45DEG_UP_UD = SLOPE_45DEG | SLOPE_RISING | SLOPE_UPSIDEDOWN;    // 1101
        private const int SLOPE_45DEG_DOWN_UD = SLOPE_45DEG | SLOPE_UPSIDEDOWN;                 // 1001
        private const int SLOPE_22DEG_UP_UD = SLOPE_22DEG | SLOPE_RISING | SLOPE_UPSIDEDOWN;    // 1110
        private const int SLOPE_22DEG_DOWN_UD = SLOPE_22DEG | SLOPE_UPSIDEDOWN;                 // 1010
        private const int SLOPE_66DEG_UP_UD = SLOPE_66DEG | SLOPE_RISING | SLOPE_UPSIDEDOWN;    // 1111
        private const int SLOPE_66DEG_DOWN_UD = SLOPE_66DEG | SLOPE_UPSIDEDOWN;                 // 1011
        
        // State variables for slope handling
        private int currplayer_slope_type = 0;
        private int currplayer_slope_frames = 0;
        private int currplayer_was_on_slope_counter = 0;
        private bool make_cube_jump_higher = false;
        
        /// <summary>
        /// Check if player is colliding with a slope tile
        /// Exact 1:1 port from bg_coll_slope() in collision.h
        /// Returns: (collided, ejectionAmount) where ejectionAmount is the Y offset to apply
        /// </summary>
        private (bool collided, int ejectionAmount) CheckSlopeCollision(int temp_x, int temp_y, MetatileCollision collision)
        {
            // Only process slope collision types
            if (collision < MetatileCollision.COL_SLOPE_RD45 || collision > MetatileCollision.COL_SLOPE_LU66_TOP)
            {
                return (false, 0);
            }
            
            int tmp7 = 0;  // X component for slope calculation
            int tmp4 = 0;  // Y component for slope calculation
            int tmp8 = (temp_y) & 0x0f;
            
            // Switch based on collision type - matches the jump table in collision.h
            switch (collision)
            {
                // === 45 DEGREE SLOPES ===
                
                case MetatileCollision.COL_SLOPE_LU45:
                    tmp7 = (temp_x & 0x0f);
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_45DEG_DOWN_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_LD45:
                    tmp7 = (temp_x & 0x0f);
                    tmp4 = (temp_y & 0x0f);
                    currplayer_slope_type = SLOPE_45DEG_DOWN;
                    break;
                
                case MetatileCollision.COL_SLOPE_RU45:
                    tmp7 = (temp_x & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_45DEG_UP_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_RD45:
                    tmp7 = (temp_x & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f);
                    currplayer_slope_type = SLOPE_45DEG_UP;
                    break;
                
                // === 22 DEGREE SLOPES ===
                
                case MetatileCollision.COL_SLOPE_RU22_RIGHT:
                    tmp7 = ((temp_x >> 1) & 0x07) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_22DEG_UP_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_RU22_LEFT:
                    tmp7 = (((temp_x >> 1) | 0x8) & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_22DEG_UP_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_RD22_RIGHT:
                    tmp7 = ((temp_x >> 1) & 0x07) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f);
                    currplayer_slope_type = SLOPE_22DEG_UP;
                    break;
                
                case MetatileCollision.COL_SLOPE_RD22_LEFT:
                    tmp7 = (((temp_x >> 1) | 0x8) & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f);
                    currplayer_slope_type = SLOPE_22DEG_UP;
                    break;
                
                case MetatileCollision.COL_SLOPE_LU22_RIGHT:
                    tmp7 = ((temp_x >> 1) & 0x07);
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_22DEG_DOWN_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_LU22_LEFT:
                    tmp7 = (((temp_x >> 1) | 0x8) & 0x0f);
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_22DEG_DOWN_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_LD22_RIGHT:
                    tmp7 = ((temp_x >> 1) & 0x07);
                    tmp4 = (temp_y & 0x0f);
                    currplayer_slope_type = SLOPE_22DEG_DOWN;
                    break;
                
                case MetatileCollision.COL_SLOPE_LD22_LEFT:
                    tmp7 = (((temp_x >> 1) | 0x8) & 0x0f);
                    tmp4 = (temp_y & 0x0f);
                    currplayer_slope_type = SLOPE_22DEG_DOWN;
                    break;
                
                // === 66 DEGREE SLOPES ===
                
                case MetatileCollision.COL_SLOPE_RD66_TOP:
                    if ((byte)(temp_x & 0x0f) < 0x08) return (false, 0);
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = ((temp_y) & 0x0f);
                    currplayer_slope_type = SLOPE_66DEG_UP;
                    break;
                
                case MetatileCollision.COL_SLOPE_RD66_BOT:
                    if ((byte)(temp_x & 0x0f) >= 0x08) return (true, 0);  // Solid if in right half
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = ((temp_y) & 0x0f);
                    currplayer_slope_type = SLOPE_66DEG_UP;
                    break;
                
                case MetatileCollision.COL_SLOPE_LD66_TOP:
                    if ((byte)(temp_x & 0x0f) >= 0x08) return (false, 0);
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f);
                    tmp4 = ((temp_y) & 0x0f);
                    currplayer_slope_type = SLOPE_66DEG_DOWN;
                    break;
                
                case MetatileCollision.COL_SLOPE_LD66_BOT:
                    if ((byte)(temp_x & 0x0f) < 0x08) return (true, 0);  // Solid if in left half
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f);
                    tmp4 = ((temp_y) & 0x0f);
                    currplayer_slope_type = SLOPE_66DEG_DOWN;
                    break;
                
                case MetatileCollision.COL_SLOPE_RU66_TOP:
                    if ((byte)(temp_x & 0x0f) < 0x08) return (false, 0);
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = ((temp_y) & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_UP_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_RU66_BOT:
                    if ((byte)(temp_x & 0x0f) >= 0x08) return (true, 0);  // Solid if in right half
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = ((temp_y) & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_UP_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_LU66_TOP:
                    if ((byte)(temp_x & 0x0f) >= 0x08) return (false, 0);
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f);
                    tmp4 = ((temp_y) & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_DOWN_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_LU66_BOT:
                    if ((byte)(temp_x & 0x0f) < 0x08) return (true, 0);  // Solid if in left half
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f);
                    tmp4 = ((temp_y) & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_DOWN_UD;
                    break;
                
                default:
                    return (false, 0);
            }
            
            // col_end: Calculate if player is on the slope
            if ((byte)(tmp4) >= (byte)(tmp7))
            {
                tmp8 = tmp4 - tmp7;
                
                // For cube mode: handle jump buffering and slope frames
                // Signal that we're on a slope
                currplayer_slope_frames = 1;
                currplayer_was_on_slope_counter = 3;
                
                // Check if we should make the jump higher when jumping from slope
                if (keyXPressedCount > 0)  // If A/X is held
                {
                    make_cube_jump_higher = true;
                }
                
                // Return tmp8 which is the ejection amount (how far player is BELOW the slope surface)
                // In NES: high_byte(currplayer_y) -= eject_D (moves player UP by this amount)
                return (true, tmp8);
            }
            else if (currplayer_was_on_slope_counter == 0)
            {
                currplayer_slope_type = 0;
                tmp8 = 0;
            }
            
            return (false, 0);
        }
        
        /// <summary>
        /// Wrapper that also returns the slope type for filtering LEFT/RIGHT checks
        /// </summary>
        private (bool collided, int ejectionAmount, int slopeType) CheckSlopeCollision_WithType(int temp_x, int temp_y, MetatileCollision collision)
        {
            var (collided, ejectionAmount) = CheckSlopeCollision(temp_x, temp_y, collision);
            return (collided, ejectionAmount, currplayer_slope_type);
        }
        
        /// <summary>
        /// Apply slope jump velocity bonus
        /// Matches slope_jump_check() from gamemode_cube.h
        /// </summary>
        private void SlopeJumpCheck_Fresh()
        {
            if (make_cube_jump_higher)
            {
                if ((currplayer_slope_type & SLOPE_DEGREES_MASK) != SLOPE_22DEG)
                {
                    // Add extra velocity for slope jumps (not on 22deg slopes)
                    // MAKE_CUBE_JUMP_HIGHER is typically -0x100 to -0x200
                    int bonus = currplayer_mini == 0 ? -0x100 : -0xC0;
                    playerVelY_fixed[currplayer] += bonus;
                }
                make_cube_jump_higher = false;
            }
        }
        
        /// <summary>
        /// Decrement slope counter each frame
        /// </summary>
        private void UpdateSlopeCounters_Fresh()
        {
            if (currplayer_was_on_slope_counter > 0)
            {
                currplayer_was_on_slope_counter--;
            }
            if (currplayer_slope_frames > 0)
            {
                currplayer_slope_frames = 0;  // Reset each frame
            }
        }
        
        /// <summary>
        /// bg_coll_slope() - 1:1 port from collision.h line 459
        /// Returns true if colliding with slope, sets tmp8 to ejection amount
        /// </summary>
        private bool bg_coll_slope(int temp_x, int temp_y, MetatileCollision collision)
        {
            tmp8 = temp_y & 0x0f;
            
            if (collision < MetatileCollision.COL_SLOPE_RD45 || collision > MetatileCollision.COL_SLOPE_LU66_BOT)
            {
                return false;
            }
            
            int tmp7 = 0;
            int tmp4 = 0;
            
            // Jump table implementation from collision.h
            switch (collision)
            {
                case MetatileCollision.COL_SLOPE_LU45:
                    tmp7 = temp_x & 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_45DEG | SLOPE_UPSIDEDOWN; // SLOPE_45DEG_DOWN_UD
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD45:
                    tmp7 = temp_x & 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_45DEG; // SLOPE_45DEG_DOWN
                    break;
                    
                case MetatileCollision.COL_SLOPE_RU45:
                    tmp7 = (temp_x & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_45DEG | SLOPE_RISING | SLOPE_UPSIDEDOWN; // SLOPE_45DEG_UP_UD
                    break;
                    
                case MetatileCollision.COL_SLOPE_RD45:
                    tmp7 = (temp_x & 0x0f) ^ 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_45DEG | SLOPE_RISING; // SLOPE_45DEG_UP
                    break;
                    
                // 22 degree slopes
                case MetatileCollision.COL_SLOPE_RU22_RIGHT:
                    tmp7 = ((temp_x >> 1) & 0x07) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_22DEG | SLOPE_RISING | SLOPE_UPSIDEDOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RU22_LEFT:
                    tmp7 = (((temp_x >> 1) | 0x8) & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_22DEG | SLOPE_RISING | SLOPE_UPSIDEDOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RD22_RIGHT:
                    tmp7 = ((temp_x >> 1) & 0x07) ^ 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_22DEG | SLOPE_RISING;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RD22_LEFT:
                    tmp7 = (((temp_x >> 1) | 0x8) & 0x0f) ^ 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_22DEG | SLOPE_RISING;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LU22_RIGHT:
                    tmp7 = (temp_x >> 1) & 0x07;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_22DEG | SLOPE_UPSIDEDOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LU22_LEFT:
                    tmp7 = ((temp_x >> 1) | 0x8) & 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_22DEG | SLOPE_UPSIDEDOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD22_RIGHT:
                    tmp7 = (temp_x >> 1) & 0x07;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_22DEG;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD22_LEFT:
                    tmp7 = ((temp_x >> 1) | 0x8) & 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_22DEG;
                    break;
                    
                // 66 degree slopes
                case MetatileCollision.COL_SLOPE_RD66_TOP:
                    if ((temp_x & 0x0f) < 0x08) return false;
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_RISING;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RD66_BOT:
                    if ((temp_x & 0x0f) >= 0x08) return true;
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_RISING;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD66_TOP:
                    if ((temp_x & 0x0f) >= 0x08) return false;
                    tmp7 = ((temp_x & 0x07) << 1) & 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_66DEG;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD66_BOT:
                    if ((temp_x & 0x0f) < 0x08) return true;
                    tmp7 = ((temp_x & 0x0f) << 1) & 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_66DEG;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RU66_TOP:
                    if ((temp_x & 0x0f) < 0x08) return false;
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_RISING | SLOPE_UPSIDEDOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RU66_BOT:
                    if ((temp_x & 0x0f) >= 0x08) return true;
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_RISING | SLOPE_UPSIDEDOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LU66_TOP:
                    if ((temp_x & 0x0f) >= 0x08) return false;
                    tmp7 = ((temp_x & 0x07) << 1) & 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_UPSIDEDOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LU66_BOT:
                    if ((temp_x & 0x0f) < 0x08) return true;
                    tmp7 = ((temp_x & 0x0f) << 1) & 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_UPSIDEDOWN;
                    break;
                    
                default:
                    return false;
            }
            
            // col_end: (line 692 in collision.h)
            if (tmp4 >= tmp7)
            {
                tmp8 = tmp4 - tmp7;
                currplayer_slope_frames = 1;
                currplayer_was_on_slope_counter = 3;
                make_cube_jump_higher = false;
                AppendSimDebug($"[SLOPE] Collision YES: tmp4={tmp4}, tmp7={tmp7}, tmp8={tmp8}");
                return true;
            }
            else if (currplayer_was_on_slope_counter == 0)
            {
                currplayer_slope_type = 0;
                tmp8 = 0;
                AppendSimDebug($"[SLOPE] Collision NO: tmp4={tmp4} < tmp7={tmp7}");
            }
            else
            {
                AppendSimDebug($"[SLOPE] Collision NO but counter active: tmp4={tmp4} < tmp7={tmp7}, counter={currplayer_was_on_slope_counter}");
            }
            
            return false;
        }
        
        /// <summary>
        /// bg_coll_return_slope_D() - 1:1 port from collision.h line 769
        /// Filters slope collision by LEFT/RIGHT and sets eject_D
        /// </summary>
        private bool bg_coll_return_slope_D(int temp_x, int temp_y, MetatileCollision collision, int tmp2)
        {
            bool tmp1 = bg_coll_slope(temp_x, temp_y, collision);
            
            AppendSimDebug($"[SLOPE] Filter: tmp2={tmp2}, tmp1={tmp1}, slopeType={currplayer_slope_type:X2}, hasRISING={(currplayer_slope_type & SLOPE_RISING) != 0}");
            
            if (tmp2 == 0)
            {
                // LEFT CHECK
                if ((currplayer_slope_type & SLOPE_RISING) != 0)
                {
                    AppendSimDebug($"[SLOPE] Filter: LEFT rejects RISING slope");
                    currplayer_slope_type = currplayer_last_slope_type;
                    return false;
                }
            }
            else
            {
                // RIGHT CHECK  
                if ((currplayer_slope_type & SLOPE_RISING) == 0)
                {
                    AppendSimDebug($"[SLOPE] Filter: RIGHT rejects non-RISING slope");
                    currplayer_slope_type = currplayer_last_slope_type;
                    return false;
                }
            }
            
            if (tmp1)
            {
                if ((currplayer_last_slope_type & SLOPE_RISING) != 0 && (currplayer_slope_type & SLOPE_RISING) == 0)
                {
                    if (currplayer_last_slope_type != 0 && currplayer_slope_type != 0)
                    {
                        currplayer_slope_type = currplayer_last_slope_type;
                        tmp8 = 0x03; // Approximation of horizontal speed >> 8
                    }
                }
                if (currplayer_slope_type != 0)
                {
                    currplayer_last_slope_type = currplayer_slope_type;
                }
                eject_D = tmp8;
            }
            
            return tmp1;
        }
        
        /// <summary>
        /// bg_coll_D() slopes portion - 1:1 port from collision.h line 920
        /// Checks slopes at Y + height - 2, loops for LEFT and RIGHT edges
        /// Returns true if slope collision found, sets eject_D
        /// </summary>
        private bool bg_coll_D_slopes()
        {
            int playerX_px = playerX_fixed[currplayer] >> 8;
            int playerY_px = playerY_fixed[currplayer] >> 8;
            int cameraX_px = cameraX_fixed >> 8;
            int screenX = playerX_px - cameraX_px;
            
            AppendSimDebug($"[SLOPE] POSITION: playerX_world={playerX_px}, playerX_screen={screenX}, cameraX={cameraX_px}, playerY={playerY_px}");
            
            if (playerX_px < 0x10)
            {
                AppendSimDebug($"[SLOPE] X too low, returning false");
                return false;
            }
            
            // Account for ground layer offset (same as flat collision)
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            // temp_y = Generic.y + Generic.height - 1
            int temp_y = playerY_px + CUBE_HITBOX_H - 1;
            
            int tmp2 = 0;
            int tmp3_low = 0;
            
            do
            {
                // temp_x = Generic.x (player world X)
                int temp_x = playerX_px + (tmp2 * CUBE_HITBOX_W);
                
                // Get tile position
                int tileX = temp_x / TILE;
                int tileY = temp_y / TILE;
                
                // Apply ground layer offset to array index
                int tileArrayY = tileY + groundRowsToReserve;
                
                if (tileX >= 0 && tileX < mapWidth && tileY >= 0 && tileY < mapHeight && tileArrayY < mapHeight)
                {
                    int tileIdx = tileArrayY * mapWidth + tileX;
                    if (tileIdx >= 0 && tileIdx < tiles.Length)
                    {
                        byte tileValue = (byte)tiles[tileIdx];
                        MetatileCollision collision = MetatileCollisionTable.GetCollision(tileValue);
                        
                        AppendSimDebug($"[SLOPE] Check: tmp2={tmp2}, tempX={temp_x}, tempY={temp_y}, tile=[{tileX},{tileY}], arrayY={tileArrayY}, idx={tileIdx}, tileVal=0x{tileValue:X2}, collision={collision}");
                        
                        if (collision >= MetatileCollision.COL_SLOPE_RD45 && collision <= MetatileCollision.COL_SLOPE_LU66_BOT)
                        {
                            // bg_coll_return_slope_D()
                            if (bg_coll_return_slope_D(temp_x, temp_y, collision, tmp2))
                            {
                                tmp3_low = 1;
                                AppendSimDebug($"[SLOPE] HIT! eject_D={eject_D}, tmp8={tmp8}, slopeType={currplayer_slope_type:X2}");
                            }
                        }
                    }
                }
                
                tmp2++;
            } while (tmp2 < 2);
            
            return tmp3_low != 0;
        }
        
        /// <summary>
        /// Update slope counter each frame
        /// </summary>
        private void UpdateSlopeCounters()
        {
            if (currplayer_was_on_slope_counter > 0)
            {
                currplayer_was_on_slope_counter--;
            }
            if (currplayer_slope_frames > 0)
            {
                currplayer_slope_frames = 0;
            }
        }
    }
}

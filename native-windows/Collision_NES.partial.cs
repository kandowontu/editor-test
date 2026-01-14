using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ========================================================================
        // NES COLLISION SYSTEM - Direct 1:1 Port from collision.h
        // ========================================================================
        
        // Generic collision variables (like NES Generic struct)
        private int Generic_x = 0;
        private int Generic_y = 0;
        private int Generic_width = 15;
        private int Generic_height = 15;
        
        // Collision result variables
        private int eject_D = 0;  // Ejection amount for downward collision
        private int eject_U = 0;  // Ejection amount for upward collision
        private byte collision = 0;  // Current tile collision type
        private int temp_x = 0;
        private int temp_y = 0;
        private int temp_room = 0;
        
        // Temp variables used in collision math
        private int tmp1 = 0;
        private int tmp2 = 0;
        private int tmp3 = 0;
        private int tmp4 = 0;
        private int tmp7 = 0;
        private int tmp8 = 0;
        
        // Last slope type tracking
        private int currplayer_last_slope_type = 0;
        
        /// <summary>
        /// bg_collision_sub - Get collision type at temp_x, temp_y
        /// </summary>
        private void bg_collision_sub()
        {
            // Convert world position to tile coordinates
            int tileX = temp_x / 16;
            int tileY = temp_y / 16;
            
            // Bounds check
            if (tileX < 0 || tileX >= mapWidth || tileY < 0)
            {
                collision = 0;
                return;
            }
            
            // Check if past bottom of map (ground layer)
            if (tileY >= mapHeight)
            {
                collision = (byte)MetatileCollision.COL_ALL;  // Ground is solid
                return;
            }
            
            // Get tile directly from map (no offset)
            int tileIdx = tileY * mapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= tiles.Length)
            {
                collision = 0;
                return;
            }
            
            int tileId = tiles[tileIdx];
            collision = (byte)MetatileCollisionTable.GetCollision((byte)tileId);
        }
        
        /// <summary>
        /// bg_coll_slope - Main slope collision calculation
        /// Direct port from collision.h lines 500-760
        /// </summary>
        private bool bg_coll_slope()
        {
            tmp8 = temp_y & 0x0f;
            
            // Only process slope collision types
            if (collision < (byte)MetatileCollision.COL_SLOPE_RD45 || 
                collision > (byte)MetatileCollision.COL_SLOPE_LU66_TOP)
            {
                return false;
            }
            
            // Calculate tmp4 and tmp7 based on slope type
            switch ((MetatileCollision)collision)
            {
                // 45 degree slopes
                case MetatileCollision.COL_SLOPE_LU45:
                    tmp7 = temp_x & 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_45DEG_DOWN_UD;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD45:
                    tmp7 = temp_x & 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_45DEG_DOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RU45:
                    tmp7 = (temp_x & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_45DEG_UP_UD;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RD45:
                    tmp7 = (temp_x & 0x0f) ^ 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_45DEG_UP;
                    break;
                
                // 22 degree slopes
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
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_22DEG_UP;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RD22_LEFT:
                    tmp7 = (((temp_x >> 1) | 0x8) & 0x0f) ^ 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_22DEG_UP;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LU22_RIGHT:
                    tmp7 = (temp_x >> 1) & 0x07;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_22DEG_DOWN_UD;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LU22_LEFT:
                    tmp7 = ((temp_x >> 1) | 0x8) & 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_22DEG_DOWN_UD;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD22_RIGHT:
                    tmp7 = (temp_x >> 1) & 0x07;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_22DEG_DOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD22_LEFT:
                    tmp7 = ((temp_x >> 1) | 0x8) & 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_22DEG_DOWN;
                    break;
                
                // 66 degree slopes
                case MetatileCollision.COL_SLOPE_RD66_TOP:
                    if ((byte)(temp_x & 0x0f) < 0x08) return false;
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_UP;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RD66_BOT:
                    if ((byte)(temp_x & 0x0f) >= 0x08) return true;
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_UP;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD66_TOP:
                    if ((byte)(temp_x & 0x0f) >= 0x08) return false;
                    tmp7 = ((temp_x & 0x07) << 1) & 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_DOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD66_BOT:
                    if ((byte)(temp_x & 0x0f) < 0x08) return true;
                    tmp7 = ((temp_x & 0x0f) << 1) & 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_DOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RU66_TOP:
                    if ((byte)(temp_x & 0x0f) < 0x08) return false;
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_UP_UD;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RU66_BOT:
                    if ((byte)(temp_x & 0x0f) >= 0x08) return true;
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_UP_UD;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LU66_TOP:
                    if ((byte)(temp_x & 0x0f) >= 0x08) return false;
                    tmp7 = ((temp_x & 0x07) << 1) & 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_DOWN_UD;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LU66_BOT:
                    if ((byte)(temp_x & 0x0f) < 0x08) return true;
                    tmp7 = ((temp_x & 0x0f) << 1) & 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_DOWN_UD;
                    break;
                    
                default:
                    return false;
            }
            
            // col_end: Check if player is on slope surface
            if ((byte)tmp4 >= (byte)tmp7)
            {
                tmp8 = tmp4 - tmp7;
                
                // For cube mode: signal we're on a slope
                currplayer_slope_frames = 1;
                currplayer_was_on_slope_counter = 3;
                
                // Check if we should make jump higher
                if (keyXPressedCount > 0)
                {
                    make_cube_jump_higher = true;
                }
                
                return true;
            }
            else if (currplayer_was_on_slope_counter == 0)
            {
                currplayer_slope_type = 0;
                tmp8 = 0;
            }
            
            return false;
        }
        
        /// <summary>
        /// bg_coll_return_slope_D - Slope collision check with LEFT/RIGHT logic
        /// </summary>
        private bool bg_coll_return_slope_D()
        {
            tmp1 = bg_coll_slope() ? 1 : 0;
            
            if (tmp2 == 0)
            {
                // LEFT CHECK
                if ((currplayer_slope_type & SLOPE_RISING) != 0)
                {
                    currplayer_slope_type = currplayer_last_slope_type;
                    return false;
                }
            }
            else
            {
                // RIGHT CHECK
                if ((currplayer_slope_type & SLOPE_RISING) == 0)
                {
                    currplayer_slope_type = currplayer_last_slope_type;
                    return false;
                }
            }
            
            if (tmp1 != 0)
            {
                if ((currplayer_last_slope_type & SLOPE_RISING) != 0 && 
                    (currplayer_slope_type & SLOPE_RISING) == 0)
                {
                    if (currplayer_last_slope_type != 0 && currplayer_slope_type != 0)
                    {
                        currplayer_slope_type = currplayer_last_slope_type;
                        // Use horizontal velocity if available, otherwise 0
                        tmp8 = 0;  // Simplified for now
                    }
                }
                
                if (currplayer_slope_type != 0)
                {
                    currplayer_last_slope_type = currplayer_slope_type;
                }
                
                eject_D = tmp8;
            }
            
            return tmp1 != 0;
        }
        
        /// <summary>
        /// bg_coll_D - Downward collision check (exact port from collision.h lines 920-970)
        /// </summary>
        private bool bg_coll_D()
        {
            // Slopes check
            if ((playerX_fixed >> 8) >= 0x10)
            {
                temp_y = Generic_y + Generic_height - 2;
                temp_x = Generic_x;
                
                tmp2 = 0;
                tmp3 = 0;
                
                do
                {
                    bg_collision_sub();
                    
                    if (collision != 0)
                    {
                        if (bg_coll_return_slope_D())
                        {
                            tmp3 |= 1;
                        }
                    }
                    
                    temp_x += Generic_width;
                } while (++tmp2 < 2);
                
                if (tmp3 != 0) return true;
            }
            
            // Regular collision (only if velocity >= 0)
            if ((playerVelY_fixed & 0x8000) == 0)  // velocity not negative
            {
                temp_x = Generic_x;
                temp_y = Generic_y + Generic_height;
                
                tmp8 = temp_y & 0x0f;
                
                // Check 3 points: left, middle, right
                // Point 1: left
                bg_collision_sub();
                if (collision != 0 && collision < (byte)MetatileCollision.COL_SLOPE_RD45)
                {
                    eject_D = tmp8;
                    return true;
                }
                
                // Point 2: middle
                temp_x += Generic_width >> 1;
                bg_collision_sub();
                if (collision != 0 && collision < (byte)MetatileCollision.COL_SLOPE_RD45)
                {
                    eject_D = tmp8;
                    return true;
                }
                
                // Point 3: right
                temp_x = Generic_x + Generic_width;
                bg_collision_sub();
                if (collision != 0 && collision < (byte)MetatileCollision.COL_SLOPE_RD45)
                {
                    eject_D = tmp8;
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// bg_coll_U - Upward collision check
        /// </summary>
        private bool bg_coll_U()
        {
            // Simplified version - just check top edge
            if ((playerVelY_fixed & 0x8000) != 0)  // velocity negative (moving up)
            {
                temp_x = Generic_x;
                temp_y = Generic_y - 1;
                
                tmp8 = 16 - (temp_y & 0x0f);
                
                // Check 3 points: left, middle, right
                bg_collision_sub();
                if (collision != 0 && collision < (byte)MetatileCollision.COL_SLOPE_RD45)
                {
                    eject_U = -tmp8;
                    return true;
                }
                
                temp_x += Generic_width >> 1;
                bg_collision_sub();
                if (collision != 0 && collision < (byte)MetatileCollision.COL_SLOPE_RD45)
                {
                    eject_U = -tmp8;
                    return true;
                }
                
                temp_x = Generic_x + Generic_width;
                bg_collision_sub();
                if (collision != 0 && collision < (byte)MetatileCollision.COL_SLOPE_RD45)
                {
                    eject_U = -tmp8;
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// cube_eject - Cube collision and ejection (exact port from gamemode_cube.h)
        /// </summary>
        private void cube_eject_NES()
        {
            if (currplayer_gravity == 0)
            {
                // Normal gravity - check below
                if (bg_coll_D())
                {
                    int currentY = playerY_fixed >> 8;
                    currentY -= eject_D;
                    playerY_fixed = currentY << 8;
                    playerVelY_fixed = 0;
                }
            }
            else
            {
                // Reversed gravity - check above
                if (bg_coll_U())
                {
                    int currentY = playerY_fixed >> 8;
                    currentY -= eject_U;
                    playerY_fixed = currentY << 8;
                    playerVelY_fixed = 0;
                }
            }
        }
    }
}

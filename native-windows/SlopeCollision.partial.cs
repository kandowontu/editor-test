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
        
        // State variables for slope handling - dual-mode arrays [2] indexed by currplayer
        // Matching NES famidash.h: slope_frames[2], slope_type[2], was_on_slope_counter[2], last_slope_type[2]
        private int[] slope_type_arr = new int[2];
        private int[] slope_frames_arr = new int[2];
        private int[] was_on_slope_counter_arr = new int[2];
        private int[] last_slope_type_arr = new int[2];
        private bool make_cube_jump_higher = false;
        
        // NES rounding_slope_table from nesdash.s - maps slope_type to cube rotation frame offset
        // Indexed by slope_type (0-15). Values are frame indices for the 7-frame cube rotation system.
        // Derived from NES 24-frame table: {$09,$08,$09,$00,$03,$04,$08,$00,$09,$16,$1a,$00,$03,$1a,$17}
        // Converted proportionally: NES_frame * 7 / 24, with UD slopes using symmetric offsets
        // since C# GetCubeSpriteFrame applies gravity flip (6 - frame) for inverted gravity.
        private static readonly int[] slopeRotationFrame_7 = new int[16]
        {
            0,  // 0: SLOPE_NONE
            3,  // 1: SLOPE_45DEG_DOWN (NES $09 → frame 3)
            2,  // 2: SLOPE_22DEG_DOWN (NES $08 → frame 2)
            3,  // 3: SLOPE_66DEG_DOWN (NES $09 → frame 3)
            0,  // 4: unused
            1,  // 5: SLOPE_45DEG_UP (NES $03 → frame 1)
            1,  // 6: SLOPE_22DEG_UP (NES $04 → frame 1)
            2,  // 7: SLOPE_66DEG_UP (NES $08 → frame 2)
            0,  // 8: padding
            3,  // 9: SLOPE_45DEG_DOWN_UD (same offset, gravity flip handles visual)
            2,  // 10: SLOPE_22DEG_DOWN_UD
            3,  // 11: SLOPE_66DEG_DOWN_UD
            0,  // 12: padding
            1,  // 13: SLOPE_45DEG_UP_UD
            1,  // 14: SLOPE_22DEG_UP_UD
            2,  // 15: SLOPE_66DEG_UP_UD
        };
        
        // Same table for football's 24-frame rotation system
        // NES raw offset values applied to frame index 0-23
        private static readonly int[] slopeRotationFrame_24 = new int[16]
        {
            0,   // 0: SLOPE_NONE
            9,   // 1: SLOPE_45DEG_DOWN (NES $09)
            8,   // 2: SLOPE_22DEG_DOWN (NES $08)
            9,   // 3: SLOPE_66DEG_DOWN (NES $09)
            0,   // 4: unused
            3,   // 5: SLOPE_45DEG_UP (NES $03)
            4,   // 6: SLOPE_22DEG_UP (NES $04)
            8,   // 7: SLOPE_66DEG_UP (NES $08)
            0,   // 8: padding
            9,   // 9: SLOPE_45DEG_DOWN_UD
            8,   // 10: SLOPE_22DEG_DOWN_UD
            9,   // 11: SLOPE_66DEG_DOWN_UD
            0,   // 12: padding
            3,   // 13: SLOPE_45DEG_UP_UD
            4,   // 14: SLOPE_22DEG_UP_UD
            8,   // 15: SLOPE_66DEG_UP_UD
        };
        
        // currplayer accessors (these read/write the arrays at currplayer index)
        private int currplayer_slope_type
        {
            get => slope_type_arr[currplayer];
            set => slope_type_arr[currplayer] = value;
        }
        private int currplayer_slope_frames
        {
            get => slope_frames_arr[currplayer];
            set => slope_frames_arr[currplayer] = value;
        }
        private int currplayer_was_on_slope_counter
        {
            get => was_on_slope_counter_arr[currplayer];
            set => was_on_slope_counter_arr[currplayer] = value;
        }
        
        /// <summary>
        /// clear_slope_stuff() from sprite_loading.h line 194
        /// Called by pad_stuff() and sprite_gamemode_main() before applying pad/orb velocity.
        /// Clears slope state for the CURRENT player only so residual slope exit velocity
        /// doesn't corrupt the pad/orb velocity in UpdateSlopeCounters_Fresh.
        /// </summary>
        private void ClearSlopeStuff()
        {
            currplayer_was_on_slope_counter = 0;
            currplayer_slope_frames = 0;
            currplayer_slope_type = 0;
            currplayer_last_slope_type = 0;
        }

        /// <summary>
        /// Reset all slope state variables (called on level restart)
        /// Matches reset_level.h slope section
        /// </summary>
        private void ResetSlopeState()
        {
            slope_type_arr[0] = SLOPE_NONE;
            slope_type_arr[1] = SLOPE_NONE;
            slope_frames_arr[0] = 0;
            slope_frames_arr[1] = 0;
            was_on_slope_counter_arr[0] = 0;
            was_on_slope_counter_arr[1] = 0;
            last_slope_type_arr[0] = SLOPE_NONE;
            last_slope_type_arr[1] = SLOPE_NONE;
            currplayer_last_slope_type = SLOPE_NONE;
            make_cube_jump_higher = false;
            AppendSimDebug("[SLOPE] All slope state reset");
        }
        
        /// <summary>
        /// Save current player's slope state to arrays before switching players
        /// Matches state_game.h player swap
        /// </summary>
        private void SaveSlopeStateForPlayer(int player)
        {
            slope_frames_arr[player] = currplayer_slope_frames;
            was_on_slope_counter_arr[player] = currplayer_was_on_slope_counter;
            slope_type_arr[player] = currplayer_slope_type;
            last_slope_type_arr[player] = currplayer_last_slope_type;
        }
        
        /// <summary>
        /// Load a player's slope state from arrays after switching players
        /// Matches state_game.h player swap
        /// </summary>
        private void LoadSlopeStateForPlayer(int player)
        {
            // currplayer is already set to the new player index, so the property accessors work
            currplayer_slope_frames = slope_frames_arr[player];
            currplayer_was_on_slope_counter = was_on_slope_counter_arr[player];
            currplayer_slope_type = slope_type_arr[player];
            currplayer_last_slope_type = last_slope_type_arr[player];
        }
        
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
                    currplayer_slope_type = SLOPE_66DEG_UP;
                    if ((byte)(temp_x & 0x0f) >= 0x08) return (true, temp_y & 0x0f);  // Solid — NES: tmp8 = temp_y & 0x0f
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = ((temp_y) & 0x0f);
                    break;
                
                case MetatileCollision.COL_SLOPE_LD66_TOP:
                    if ((byte)(temp_x & 0x0f) >= 0x08) return (false, 0);
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f);
                    tmp4 = ((temp_y) & 0x0f);
                    currplayer_slope_type = SLOPE_66DEG_DOWN;
                    break;
                
                case MetatileCollision.COL_SLOPE_LD66_BOT:
                    currplayer_slope_type = SLOPE_66DEG_DOWN;
                    if ((byte)(temp_x & 0x0f) < 0x08) return (true, temp_y & 0x0f);  // Solid — NES: tmp8 = temp_y & 0x0f
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f);
                    tmp4 = ((temp_y) & 0x0f);
                    break;
                
                case MetatileCollision.COL_SLOPE_RU66_TOP:
                    if ((byte)(temp_x & 0x0f) < 0x08) return (false, 0);
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = ((temp_y) & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_UP_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_RU66_BOT:
                    currplayer_slope_type = SLOPE_66DEG_UP_UD;
                    if ((byte)(temp_x & 0x0f) >= 0x08) return (true, temp_y & 0x0f);  // Solid — NES: tmp8 = temp_y & 0x0f
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = ((temp_y) & 0x0f) ^ 0x0f;
                    break;
                
                case MetatileCollision.COL_SLOPE_LU66_TOP:
                    if ((byte)(temp_x & 0x0f) >= 0x08) return (false, 0);
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f);
                    tmp4 = ((temp_y) & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG_DOWN_UD;
                    break;
                
                case MetatileCollision.COL_SLOPE_LU66_BOT:
                    currplayer_slope_type = SLOPE_66DEG_DOWN_UD;
                    if ((byte)(temp_x & 0x0f) < 0x08) return (true, temp_y & 0x0f);  // Solid — NES: tmp8 = temp_y & 0x0f
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f);
                    tmp4 = ((temp_y) & 0x0f) ^ 0x0f;
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
        /// Thin wrapper over SharedPhysics.SlopeJumpCheck.
        /// </summary>
        private void SlopeJumpCheck_Fresh()
        {
            bool sjh = make_cube_jump_higher;
            SharedPhysics.SlopeJumpCheck(ref playerVelY_fixed, ref sjh,
                currplayer_slope_type, currplayer_mini != 0, currplayer_gravity != 0);
            make_cube_jump_higher = sjh;
        }
        
        // ========================================================================
        // SLOPE VELOCITY SYSTEM
        // 1:1 port from x_movement.h slope_vel() / apply_slope_vel()
        // ========================================================================
        
        /// <summary>
        /// slope_vel() from x_movement.h line 4
        /// Calculates slope velocity component based on degree
        /// tmp8 = slope type, result in tmp5
        /// </summary>
        private int slope_vel_calc(int slopeType)
        {
            int velX = playerVelX_fixed; // currplayer_vel_x
            switch (slopeType & SLOPE_DEGREES_MASK)
            {
                case SLOPE_22DEG: return velX >> 1;
                case SLOPE_45DEG: return velX;
                case SLOPE_66DEG: return velX << 1;
                default: return 0;
            }
        }
        
        /// <summary>
        /// apply_slope_vel() from x_movement.h line 17
        /// Applies slope-derived velocity to Y based on rising/upsidedown flags
        /// </summary>
        private void apply_slope_vel()
        {
            int tmp5 = slope_vel_calc(currplayer_slope_type);
            
            if ((currplayer_slope_type & SLOPE_RISING) != 0)
            {
                if ((currplayer_slope_type & SLOPE_UPSIDEDOWN) != 0)
                {
                    playerVelY_fixed = tmp5;
                }
                else
                {
                    playerVelY_fixed = -tmp5;
                }
            }
            else
            {
                if ((currplayer_slope_type & SLOPE_UPSIDEDOWN) != 0)
                {
                    playerVelY_fixed = -tmp5;
                }
                else
                {
                    playerVelY_fixed = tmp5;
                }
            }
            AppendSimDebug($"[SLOPE] apply_slope_vel: slopeType=0x{currplayer_slope_type:X2}, velY={playerVelY_fixed}");
        }
        
        // EXIT_SLOPE velocity tables from physics_table_defines.cmp.h
        // Format: fxp8 vel [grav_index] — grav_index = currplayer_table_idx
        // Table indices: 0=normal, 1=flipped, 2=normal, 3=flipped, 4=mini_normal, 5=mini_flipped, 6=mini_normal, 7=mini_flipped
        private static readonly short[] EXIT_SLOPE_BALL_22 = { unchecked((short)0xFFA0), 0x0060, unchecked((short)0xFFA0), 0x0060, unchecked((short)0xFFB0), 0x0050, unchecked((short)0xFFB0), 0x0050 };
        private static readonly short[] EXIT_SLOPE_BALL_66 = { 0x016D, unchecked((short)0xFE93), 0x016D, unchecked((short)0xFE93), 0x01B0, unchecked((short)0xFE50), 0x01B0, unchecked((short)0xFE50) };
        private static readonly short[] EXIT_SLOPE_CUBE_22 = { unchecked((short)0xFECD), 0x0133, unchecked((short)0xFECD), 0x0133, unchecked((short)0xFF00), 0x0100, unchecked((short)0xFF00), 0x0100 };
        
        /// <summary>
        /// x_movement_coll slope_frames handling — called AFTER eject.
        /// Matches NES x_movement_coll() slope section:
        ///   - Only decrements slope_frames (NOT was_on_slope_counter)
        ///   - When slope_frames was >0 and slope_type is set: apply_slope_vel()
        /// Thin wrapper over SharedPhysics.UpdateSlopeCountersFresh.
        /// </summary>
        private void UpdateSlopeCounters_Fresh()
        {
            int slopeFrames = currplayer_slope_frames;
            SharedPhysics.UpdateSlopeCountersFresh(
                ref slopeFrames, currplayer_slope_type,
                ref playerVelY_fixed, playerVelX_fixed);
            currplayer_slope_frames = slopeFrames;
        }
        
        /// <summary>
        /// bg_coll_slope() - 1:1 port from collision.h line 459
        /// Returns true if colliding with slope, sets tmp8 to ejection amount
        /// </summary>
        private bool bg_coll_slope(int temp_x, int temp_y, MetatileCollision collision)
        {
            tmp8 = temp_y & 0x0f;
            
            if (collision < MetatileCollision.COL_SLOPE_RD45 || collision > MetatileCollision.COL_SLOPE_LU66_TOP)
            {
                return false;
            }
            
            int tmp7 = 0;
            int tmp4 = 0;
            
            // Jump table implementation from collision.h
            switch (collision)
            {
                case MetatileCollision.COL_SLOPE_LU45:
                    // NES: wave mode (non-mini) skips LU45 slopes
                    if (currentGameMode == 6 && currplayer_mini == 0)
                        return false;
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
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_RISING;
                    if ((temp_x & 0x0f) >= 0x08) return true;
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = temp_y & 0x0f;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD66_TOP:
                    if ((temp_x & 0x0f) >= 0x08) return false;
                    tmp7 = ((temp_x & 0x07) << 1) & 0x0f;
                    tmp4 = temp_y & 0x0f;
                    currplayer_slope_type = SLOPE_66DEG;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LD66_BOT:
                    currplayer_slope_type = SLOPE_66DEG;
                    if ((temp_x & 0x0f) < 0x08) return true;
                    tmp7 = ((temp_x & 0x0f) << 1) & 0x0f;
                    tmp4 = temp_y & 0x0f;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RU66_TOP:
                    if ((temp_x & 0x0f) < 0x08) return false;
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_RISING | SLOPE_UPSIDEDOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_RU66_BOT:
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_RISING | SLOPE_UPSIDEDOWN;
                    if ((temp_x & 0x0f) >= 0x08) return true;
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LU66_TOP:
                    // NES: wave mode (mini) skips LU66 slopes
                    if (currentGameMode == 6 && currplayer_mini != 0)
                        return false;
                    if ((temp_x & 0x0f) >= 0x08) return false;
                    tmp7 = ((temp_x & 0x07) << 1) & 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_UPSIDEDOWN;
                    break;
                    
                case MetatileCollision.COL_SLOPE_LU66_BOT:
                    // NES: wave mode (mini) skips LU66 slopes
                    if (currentGameMode == 6 && currplayer_mini != 0)
                        return false;
                    currplayer_slope_type = SLOPE_66DEG | SLOPE_UPSIDEDOWN;
                    if ((temp_x & 0x0f) < 0x08) return true;
                    tmp7 = ((temp_x & 0x0f) << 1) & 0x0f;
                    tmp4 = (temp_y & 0x0f) ^ 0x0f;
                    break;
                    
                default:
                    return false;
            }
            
            // col_end: (line 692 in collision.h)
            // NES checks if player pixel is at or below slope surface
            if (tmp4 >= tmp7)
            {
                tmp8 = tmp4 - tmp7;
                
                // NES col_end game mode handling:
                // Cube/Robot/Ninja: if A/UP held → make_cube_jump_higher = 1
                //                   else → slope_frames = 1, was_on_slope_counter = 3
                // Other modes: use a_check_lookup table for unstick logic
                if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8 || currentGameMode == 11) // Cube, Robot, Ninja, Football
                {
                    if (IsXDownAsync() || keyXHeld || upHeld) // NES: controllingplayer->hold & (PAD_A | PAD_UP)
                    {
                        make_cube_jump_higher = true;
                        AppendSimDebug($"[SLOPE] col_end: Cube/Robot/Ninja mode, A held → make_cube_jump_higher");
                    }
                    else
                    {
                        currplayer_slope_frames = 1;
                        currplayer_was_on_slope_counter = 3;
                    }
                }
                else
                {
                    // Ship/Ball/UFO/Wave/etc: NES uses a_check_lookup table + unstick()
                    // a_check_lookup indexed by (RISING?4:0)|(UD?2:0)|(gravity?1:0)
                    int aIdx = 0;
                    if ((currplayer_slope_type & SLOPE_RISING) != 0) aIdx |= 0b100;
                    if ((currplayer_slope_type & SLOPE_UPSIDEDOWN) != 0) aIdx |= 0b010;
                    if (currplayer_gravity != 0) aIdx |= 0b001;
                    
                    // a_check_lookup = {1, 0, 0, 1, 1, 0, 0, 1}
                    bool aCheckResult = (aIdx == 0 || aIdx == 3 || aIdx == 4 || aIdx == 7);
                    bool holdingInput = IsXDownAsync() || keyXHeld || upHeld;
                    
                    if (aCheckResult)
                    {
                        if (holdingInput) tmp8 = 4; // unstick
                    }
                    else
                    {
                        if (!holdingInput) tmp8 = 4; // unstick
                    }
                    
                    currplayer_slope_frames = 1;
                    currplayer_was_on_slope_counter = 3;
                }
                
                AppendSimDebug($"[SLOPE] Collision YES: tmp4={tmp4}, tmp7={tmp7}, tmp8={tmp8}, slopeType=0x{currplayer_slope_type:X2}, mode={currentGameMode}");
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
        /// bg_coll_return_slope_D() - 1:1 port from collision.h line 769
        /// Filters slope collision by LEFT/RIGHT and sets eject_D
        /// </summary>
        private bool bg_coll_return_slope_D(int temp_x, int temp_y, MetatileCollision collision, int tmp2)
        {
            // Fix 33: Save slope counters before bg_coll_slope may modify them.
            // When pathfinder is active and direction filter rejects, restore them
            // to prevent phantom apply_slope_vel events that PF doesn't produce.
            int saved_slope_frames = currplayer_slope_frames;
            int saved_was_on_slope_counter = currplayer_was_on_slope_counter;
            
            bool tmp1 = bg_coll_slope(temp_x, temp_y, collision);
            
            AppendSimDebug($"[SLOPE] Filter: tmp2={tmp2}, tmp1={tmp1}, slopeType={currplayer_slope_type:X2}, hasRISING={(currplayer_slope_type & SLOPE_RISING) != 0}");
            
            if (tmp2 == 0)
            {
                // LEFT CHECK
                if ((currplayer_slope_type & SLOPE_RISING) != 0)
                {
                    AppendSimDebug($"[SLOPE] Filter: LEFT rejects RISING slope");
                    currplayer_slope_type = currplayer_last_slope_type;
                    // Fix 33: Undo slope counter writes from bg_coll_slope on direction rejection
                    if (pathfinderEnabled)
                    {
                        currplayer_slope_frames = saved_slope_frames;
                        currplayer_was_on_slope_counter = saved_was_on_slope_counter;
                    }
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
                    // Fix 33: Undo slope counter writes from bg_coll_slope on direction rejection
                    if (pathfinderEnabled)
                    {
                        currplayer_slope_frames = saved_slope_frames;
                        currplayer_was_on_slope_counter = saved_was_on_slope_counter;
                    }
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
                        tmp8 = playerVelX_fixed >> 8; // NES: high_byte(currplayer_vel_x)
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
        private bool bg_coll_D_slopes(int? genericYOverride_px = null)
        {
            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            int cameraX_px = cameraX_fixed >> 8;
            int screenX = playerX_px - cameraX_px;
            
            // Wave/Snake use different Generic values than cube/ship/etc
            // NES: wave_movement sets Generic.x = playerX + 4, Generic.y = playerY + (mini?0:4)
            //       Generic.width = 8, Generic.height = 8
            // NES: bg_coll_D slope section uses temp_x based on Generic.x directly.
            bool isWaveMode = (currentGameMode == 6 || currentGameMode == 10);
            
            int checkBaseX, checkWidth, checkBaseY, hitboxH;
            
            if (isWaveMode)
            {
                // Match NES wave Generic setup
                // NES: WAVE_HEIGHT = 0x08 for all wave modes (mini and non-mini)
                int waveMiniBaseAdj = (currplayer_mini != 0) ? 0 : 4;
                int genericY = playerY_px + waveMiniBaseAdj;
                const int genericHeight = 8; // NES WAVE_HEIGHT = 0x08
                int miniYAdj = (currplayer_mini != 0) ? ((0x10 - genericHeight) >> 1) : 0;
                
                checkBaseX = playerX_px + 4;  // NES: Generic.x = playerX + 4 for wave/snake
                checkWidth = 8;               // NES: Generic.width = 8 for wave
                checkBaseY = genericY + genericHeight - 2 + miniYAdj;
                hitboxH = genericHeight;
            }
            else
            {
                // Use correct hitbox dimensions for mini mode
                int hitboxW_local = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_W : CUBE_HITBOX_W;
                hitboxH = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;
                
                // NES bg_coll_D slope probe offset:
                // Generic.y + Generic.height - 2 + (currplayer_mini ? byte(0x10 - Generic.height) >> 1 : 0)
                // The mini offset is (0x10 - height) >> 1 = 4 for ALL modes (not mode-specific).
                int hitboxOffsetY = (currplayer_mini != 0) ? ((0x10 - hitboxH) >> 1) : 0;
                int genericY = genericYOverride_px ?? playerY_px;
                int adjustedPlayerY = genericY + hitboxOffsetY;
                
                checkBaseX = playerX_px;
                checkWidth = hitboxW_local;
                checkBaseY = adjustedPlayerY + hitboxH - 2;
            }
            
            AppendSimDebug($"[SLOPE] bg_coll_D_slopes: playerX={playerX_px}, checkY={checkBaseY}, checkX={checkBaseX}, checkW={checkWidth}, mini={currplayer_mini != 0}");
            
            if (playerX_px < 0x10)
            {
                return false;
            }
            
            // Account for ground layer offset (same as flat collision)
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            int temp_y = checkBaseY;
            
            int tmp2 = 0;
            int tmp3_low = 0;
            
            do
            {
                // temp_x = Generic.x + (tmp2 * Generic.width)
                int temp_x = checkBaseX + (tmp2 * checkWidth);
                
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
                        byte tileValue = (byte)SharedPhysics.MapTileForCollision(tiles[tileIdx]);
                        MetatileCollision collision = MetatileCollisionTable.GetCollision(tileValue);
                        
                        AppendSimDebug($"[SLOPE] Check: tmp2={tmp2}, tempX={temp_x}, tempY={temp_y}, tile=[{tileX},{tileY}], arrayY={tileArrayY}, idx={tileIdx}, tileVal=0x{tileValue:X2}, collision={collision}");
                        
                        if (collision >= MetatileCollision.COL_SLOPE_RD45 && collision <= MetatileCollision.COL_SLOPE_LU66_TOP)
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
        /// bg_coll_return_slope_U() - 1:1 port from collision.h line 805
        /// Filters slope collision by LEFT/RIGHT and sets eject_U = -tmp8
        /// </summary>
        private bool bg_coll_return_slope_U(int temp_x, int temp_y, MetatileCollision collision, int tmp2)
        {
            int saved_slope_frames = currplayer_slope_frames;
            int saved_was_on_slope_counter = currplayer_was_on_slope_counter;
            
            bool tmp1 = bg_coll_slope(temp_x, temp_y, collision);
            
            AppendSimDebug($"[SLOPE_U] Filter: tmp2={tmp2}, tmp1={tmp1}, slopeType={currplayer_slope_type:X2}, hasRISING={(currplayer_slope_type & SLOPE_RISING) != 0}");
            
            if (tmp2 == 0)
            {
                // LEFT CHECK
                if ((currplayer_slope_type & SLOPE_RISING) != 0)
                {
                    currplayer_slope_type = currplayer_last_slope_type;
                    if (pathfinderEnabled)
                    {
                        currplayer_slope_frames = saved_slope_frames;
                        currplayer_was_on_slope_counter = saved_was_on_slope_counter;
                    }
                    return false;
                }
            }
            else
            {
                // RIGHT CHECK  
                if ((currplayer_slope_type & SLOPE_RISING) == 0)
                {
                    currplayer_slope_type = currplayer_last_slope_type;
                    if (pathfinderEnabled)
                    {
                        currplayer_slope_frames = saved_slope_frames;
                        currplayer_was_on_slope_counter = saved_was_on_slope_counter;
                    }
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
                        tmp8 = playerVelX_fixed >> 8;
                    }
                }
                if (currplayer_slope_type != 0)
                {
                    currplayer_last_slope_type = currplayer_slope_type;
                }
                eject_U = -tmp8;  // NES: eject_U = -tmp8 (opposite of eject_D = tmp8)
            }
            
            return tmp1;
        }
        
        /// <summary>
        /// bg_coll_U() slopes portion - 1:1 port from collision.h line 863
        /// Checks slopes at the top of the hitbox, loops for LEFT and RIGHT edges.
        /// NES probe Y: Generic.y + (byte(0x10 - Generic.height) >> 1)
        ///              + (currplayer_mini ? 1 : 2) + (gamemode == GAMEMODE_SHIP ? 1 : 0)
        /// Returns true if slope collision found, sets eject_U
        /// </summary>
        private bool bg_coll_U_slopes(int? genericYOverride_px = null)
        {
            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            
            bool isWaveMode = (currentGameMode == 6 || currentGameMode == 10);
            
            int checkBaseX, checkWidth, checkBaseY;
            
            if (isWaveMode)
            {
                // NES: WAVE_HEIGHT = 0x08 for all wave modes (mini and non-mini)
                // NES bg_coll_U: centering ((0x10-height)>>1) is applied UNCONDITIONALLY
                int waveMiniBaseAdj = (currplayer_mini != 0) ? 0 : 4;
                int genericY = playerY_px + waveMiniBaseAdj;
                const int genericHeight = 8; // NES WAVE_HEIGHT = 0x08
                int centerAdj = (0x10 - genericHeight) >> 1; // Always applied for bg_coll_U
                
                checkBaseX = playerX_px + 4;
                checkWidth = 8;
                // NES bg_coll_U: Generic.y + (0x10-height)>>1 + (mini?1:2) + (ship?1:0)
                // Wave is not ship, so ship offset = 0
                checkBaseY = genericY + centerAdj + (currplayer_mini != 0 ? 1 : 2);
            }
            else
            {
                int hitboxW_local = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_W : CUBE_HITBOX_W;
                int hitboxH = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;
                int hitboxOffsetY = (currplayer_mini != 0) ? ((0x10 - hitboxH) >> 1) : 0;
                int genericY = genericYOverride_px ?? playerY_px;
                
                checkBaseX = playerX_px;
                checkWidth = hitboxW_local;
                // NES: Generic.y + (byte(0x10 - Generic.height) >> 1) + (mini?1:2) + (ship?1:0)
                checkBaseY = genericY + hitboxOffsetY + (currplayer_mini != 0 ? 1 : 2) + (currentGameMode == 1 ? 1 : 0);
            }
            
            AppendSimDebug($"[SLOPE_U] bg_coll_U_slopes: playerX={playerX_px}, checkY={checkBaseY}, checkX={checkBaseX}, checkW={checkWidth}, mini={currplayer_mini != 0}");
            
            if (playerX_px < 0x10)
            {
                return false;
            }
            
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            int temp_y_val = checkBaseY;
            int tmp2_dir = 0;
            int tmp3_low = 0;
            
            do
            {
                int temp_x_val = checkBaseX + (tmp2_dir * checkWidth);
                
                int tileX = temp_x_val / TILE;
                int tileY = temp_y_val / TILE;
                int tileArrayY = tileY + groundRowsToReserve;
                
                if (tileX >= 0 && tileX < mapWidth && tileY >= 0 && tileY < mapHeight && tileArrayY < mapHeight)
                {
                    int tileIdx = tileArrayY * mapWidth + tileX;
                    if (tileIdx >= 0 && tileIdx < tiles.Length)
                    {
                        byte tileValue = (byte)SharedPhysics.MapTileForCollision(tiles[tileIdx]);
                        MetatileCollision coll = MetatileCollisionTable.GetCollision(tileValue);
                        
                        AppendSimDebug($"[SLOPE_U] Check: tmp2={tmp2_dir}, tempX={temp_x_val}, tempY={temp_y_val}, tile=[{tileX},{tileY}], tileVal=0x{tileValue:X2}, collision={coll}");
                        
                        if (coll >= MetatileCollision.COL_SLOPE_RD45 && coll <= MetatileCollision.COL_SLOPE_LU66_TOP)
                        {
                            if (bg_coll_return_slope_U(temp_x_val, temp_y_val, coll, tmp2_dir))
                            {
                                tmp3_low = 1;
                                AppendSimDebug($"[SLOPE_U] HIT! eject_U={eject_U}, tmp8={tmp8}, slopeType={currplayer_slope_type:X2}");
                            }
                        }
                    }
                }
                
                tmp2_dir++;
            } while (tmp2_dir < 2);
            
            return tmp3_low != 0;
        }
        
        /// <summary>
        /// decrement_was_on_slope() — called at top of eject, BEFORE slope probes.
        /// Matches NES decrement_was_on_slope() from titlescreen.c:
        ///   - Only decrements was_on_slope_counter (NOT slope_frames)
        ///   - When counter reaches 0: applies EXIT_SLOPE velocity tables, clears slope_type
        ///   - When already 0: clears last_slope_type and slope_type
        /// Thin wrapper over SharedPhysics.UpdateSlopeCounters.
        /// </summary>
        private void UpdateSlopeCounters(bool applyPosition = true)
        {
            int slopeWasOnCounter = currplayer_was_on_slope_counter;
            int slopeType = currplayer_slope_type;
            int lastSlopeType = currplayer_last_slope_type;
            SharedPhysics.UpdateSlopeCounters(
                ref slopeWasOnCounter, ref slopeType,
                ref playerVelY_fixed, ref playerY_fixed,
                currentGameMode,
                currplayer_gravity != 0, currplayer_mini != 0,
                ref lastSlopeType, applyPosition);
            currplayer_was_on_slope_counter = slopeWasOnCounter;
            currplayer_slope_type = slopeType;
            currplayer_last_slope_type = lastSlopeType;
        }
        
        /// <summary>
        /// Check if slope side collision should be skipped
        /// Matches NES bg_side_coll_common: if on a slope, skip side collision
        /// </summary>
        private bool ShouldSkipSideCollisionForSlope()
        {
            return (currplayer_was_on_slope_counter | currplayer_slope_frames) != 0;
        }
    }
}



using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ========================================================================
        // CUBE PHYSICS - Fresh 1:1 Port from cleaned gamemode_cube.h
        // ========================================================================

        // Physics constants from physics_table_defines.cmp.h
        // Table index 4 = normal size (mini=0), normal gravity (gravity=0), framerate=0
        private const int CUBE_GRAVITY_NORMAL = 0x6B;
        private const int CUBE_GRAVITY_MINI = 0x6F;
        private const int CUBE_MAX_FALLSPEED_NORMAL = 0x0600;
        private const int CUBE_MAX_FALLSPEED_MINI = 0x0600;
        private const int JUMP_VEL_NORMAL = -0x590;  // 0xFA70 as signed 16-bit
        private const int JUMP_VEL_MINI = -0x4D0;    // 0xFB30 as signed 16-bit
        
        // Cube hitbox dimensions
        private const int CUBE_HITBOX_W = 15;
        private const int CUBE_HITBOX_H = 15;
        
        // State variables from famidash.h
        private byte currplayer_mini = 0;      // 0 = normal, 1 = mini
        private byte currplayer_gravity = 0;   // 0 = down, 0xFF = up
        
        // Temporary variables used in cube_movement()
        private int tmpgravity = 0;
        private int tmpfallspeed = 0;
        
        /// <summary>
        /// Main cube physics routine - matches cube_movement() from gamemode_cube.h
        /// </summary>
        private void ProcessCubePhysics_Fresh()
        {
            try
            {
                int groundRowsCalc = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                AppendSimDebug($"[CUBE] Start: velY={playerVelY_fixed}, posY={playerY_fixed >> 8}, gravity={currplayer_gravity:X2}");
                
                // Set physics constants based on mini state
                // From gamemode_cube.h lines 16-22
                tmpfallspeed = (currplayer_mini == 0) ? CUBE_MAX_FALLSPEED_NORMAL : CUBE_MAX_FALLSPEED_MINI;
                tmpgravity = (currplayer_mini == 0) ? CUBE_GRAVITY_NORMAL : CUBE_GRAVITY_MINI;
                
                AppendSimDebug($"[CUBE] Constants: gravity={tmpgravity:X}, fallspeed={tmpfallspeed:X}");
                
                // STEP 1: Jump input handling (MUST be before gravity!)
                // From gamemode_cube.h lines 70-103
                // Check: gamemode == GAMEMODE_CUBE && currplayer_vel_y == 0 && dashing == 0
                if (playerVelY_fixed == 0)
                {
                    // Read input
                    bool holdJump = IsXDownAsync() || keyXHeld;
                    int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
                    bool pressJump = pressCount > 0;
                    
                    AppendSimDebug($"[CUBE] Input check: hold={holdJump}, press={pressJump}, pressCount={pressCount}");
                    
                    // Two jump paths from gamemode_cube.h:
                    // Path 1 (lines 81-91): Hold A to buffer jump (no jblocked/fblocked)
                    // Path 2 (lines 92-102): Press A for immediate jump (with jblocked/fblocked)
                    // For now, simplified: either hold or press allows jump
                    
                    if (holdJump || pressJump)
                    {
                        AppendSimDebug($"[CUBE] JUMP TRIGGERED!");
                        
                        // Apply jump velocity
                        int jumpVel = (currplayer_mini == 0) ? JUMP_VEL_NORMAL : JUMP_VEL_MINI;
                        
                        // From gamemode_cube.h: currplayer_vel_y = JUMP_VEL(currplayer_table_idx);
                        // Jump velocity is negative for upward (normal gravity)
                        // For reversed gravity, velocity direction is already handled by the value
                        if (currplayer_gravity == 0)
                        {
                            // Normal gravity: negative velocity = jump up
                            playerVelY_fixed = jumpVel;
                        }
                        else
                        {
                            // Reversed gravity: positive velocity = jump up
                            playerVelY_fixed = -jumpVel;
                        }
                        
                        AppendSimDebug($"[CUBE] Jump applied: velY now = {playerVelY_fixed}");
                    }
                }
                else
                {
                    AppendSimDebug($"[CUBE] Cannot jump: velY={playerVelY_fixed} (must be 0)");
                }
                
                // STEP 2: common_gravity_routine() - applies gravity and integrates velocity
                CommonGravityRoutine_Fresh();
                
                AppendSimDebug($"[CUBE] After gravity: velY={playerVelY_fixed}, posY={playerY_fixed >> 8}");
                
                // STEP 3: cube_eject() - collision detection and ejection
                CubeEject_Fresh();
                
                AppendSimDebug($"[CUBE] After collision: velY={playerVelY_fixed}, posY={playerY_fixed >> 8}");
                
                // Record position for trail AFTER physics completes (for smooth visualization)
                try
                {
                    int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                    int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                    recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                }
                catch { }
            }
            catch (Exception ex)
            {
                try { AppendSimDebug($"ProcessCubePhysics_Fresh error: {ex.Message}"); } catch { }
            }
        }
        
        /// <summary>
        /// common_gravity_routine() from gamemode_cube.h lines 177-206
        /// Applies gravity and integrates velocity into position
        /// </summary>
        private void CommonGravityRoutine_Fresh()
        {
            // From gamemode_cube.h line 178: register int16_t tmpaccel;
            int tmpaccel;
            
            // From gamemode_cube.h line 179: tmp1 = dashing[currplayer];
            // We don't have dashing, so assume tmp1 = 0 (not dashing)
            int tmp1 = 0;
            
            if (tmp1 == 0)
            {
                // From gamemode_cube.h line 181: tmpaccel = tmpgravity;
                tmpaccel = tmpgravity;
                
                // When gravity is reversed, we need to apply acceleration in the opposite direction
                if (currplayer_gravity != 0)
                {
                    tmpaccel = -tmpaccel;
                }
                
                // From gamemode_cube.h line 182-184: check if at max fall speed
                // if((!currplayer_gravity ? currplayer_vel_y > tmpfallspeed : currplayer_vel_y < tmpfallspeed))
                bool atMaxFallSpeed;
                if (currplayer_gravity == 0)
                {
                    // Normal gravity: falling down is positive velocity
                    atMaxFallSpeed = (playerVelY_fixed > tmpfallspeed);
                }
                else
                {
                    // Reversed gravity: falling up is negative velocity  
                    atMaxFallSpeed = (playerVelY_fixed < -tmpfallspeed);
                }
                
                if (atMaxFallSpeed)
                {
                    // From gamemode_cube.h line 185: tmpaccel = -tmpaccel;
                    tmpaccel = -tmpaccel;
                }
                
                // gravity_mod handling (lines 186-192) - skip for now, assume gravity_mod = 0
                
                // From gamemode_cube.h line 194: currplayer_vel_y += tmpaccel;
                playerVelY_fixed += tmpaccel;
            }
            // Other dashing cases (lines 195-203) skipped
            
            // From gamemode_cube.h line 205: currplayer_y += currplayer_vel_y;
            playerY_fixed += playerVelY_fixed;
            
            // Clamp to world bounds
            if (playerY_fixed < 0) playerY_fixed = 0;
            int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
            if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
        }
        
        /// <summary>
        /// cube_eject() from gamemode_cube.h lines 209-254
        /// Collision detection and position/velocity correction
        /// </summary>
        private void CubeEject_Fresh()
        {
            // Calculate hitbox in pixels
            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            
            // Cube collision detection based on gravity direction
            if (currplayer_gravity == 0)
            {
                // Normal gravity: check collision below
                var (collided, collisionTopY) = CheckCollisionDown(playerX_px, playerY_px, CUBE_HITBOX_W, CUBE_HITBOX_H);
                AppendSimDebug($"[CUBE]   Collision check down: {collided}, collisionTop={collisionTopY}");
                if (collided)
                {
                    // Snap player to rest position above the collision surface
                    int newY = collisionTopY - CUBE_HITBOX_H - 1;
                    AppendSimDebug($"[CUBE]     Eject down: collisionTop={collisionTopY}, newY={newY} (was {playerY_px})");
                    playerY_fixed = newY << 8;
                    playerVelY_fixed = 0;
                }
            }
            else
            {
                // Reversed gravity: check collision above
                var (collided, collisionBottomY) = CheckCollisionUp(playerX_px, playerY_px, CUBE_HITBOX_W, CUBE_HITBOX_H);
                if (collided)
                {
                    // Snap player to rest position below the collision surface
                    int newY = collisionBottomY;
                    AppendSimDebug($"[CUBE]     Eject up: collisionBottom={collisionBottomY}, newY={newY} (was {playerY_px})");
                    playerY_fixed = newY << 8;
                    playerVelY_fixed = 0;
                }
            }
        }
        
        /// <summary>
        /// Initialize cube physics state
        /// </summary>
        private void EnableCubePhysics_Fresh()
        {
            try
            {
                currplayer_mini = 0;      // Normal size
                currplayer_gravity = 0;   // Normal gravity (down)
            }
            catch (Exception ex)
            {
                try { AppendSimDebug($"EnableCubePhysics_Fresh error: {ex.Message}"); } catch { }
            }
        }
    }
}

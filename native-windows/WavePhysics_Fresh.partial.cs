using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// wave_movement() from gamemode_wave.h - 1:1 port
        /// Note: Wave mode doesn't use traditional gravity/fallspeed constants.
        /// It calculates velocity based on direction and playerVelX_fixed.
        /// </summary>
        private void WavePhysics_Fresh()
        {
            // Skip all physics if death already triggered
            if (deathTriggered || paused) return;
            
            // NES wave_movement: always recalculates vel_y from vel_x each frame
            // (no wasZeroedByCollisionLastFrame check in NES wave code)
            
            // Check for orb activation
            {
                bool holdJump_orb = IsXDownAsync() || keyXHeld;
                int pressCount_orb = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                bool pressJump_orb = pressCount_orb > 0;
                bool gravityInverted_orb = (currplayer_gravity != 0);
                int playerX_px_orb = playerX_fixed >> 8;
                int playerY_px_orb = playerY_fixed >> 8;
                int hitboxW_orb = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH_orb = (currplayer_mini != 0) ? 7 : 15;  // Correct: 8x7 for mini
                
                // Adjust Y position for mini mode collision box (bottom-left alignment)
                if ((currplayer_mini != 0) && !gravityInverted_orb)
                {
                    playerY_px_orb += 9;
                }
                
                int scrollX_px_orb = 0;
                
                int tempVelY = playerVelY_fixed;
                var (orbActivated, _) = UpdateOrbSystem(6, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                   scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                   (currplayer_mini != 0), ref tempVelY);
                if (orbActivated)
                {
                    playerVelY_fixed = tempVelY;
                    AppendSimDebug($"[WAVE] Orb activated! New velY={playerVelY_fixed}");
                    
                    // Consume the X press if it was used for orb
                    if (pressJump_orb)
                        Interlocked.Exchange(ref keyXPressedCount, 0);
                }
                
                // Clear orb buffer when X is released
                if (!holdJump_orb)
                    ClearOrbBuffer();
            }
            
            // tmp1 = dashing (we don't have dashing, assume 0)
            int tmp1 = 0;
            
            switch (tmp1) {
                case 0:
                    // Calculate vel_y based on vel_x — NES always recalculates, no "just landed" guard
                    if (!miniMode) {
                        playerVelY_fixed = gravityFlipped ? -playerVelX_fixed : playerVelX_fixed;
                    } else {
                        playerVelY_fixed = gravityFlipped ? -(playerVelX_fixed << 1) : (playerVelX_fixed << 1);
                    }
                    
                    // Input handling - use same system as cube
                    bool holding = IsXDownAsync() || keyXHeld;
                    if (holding) playerVelY_fixed = -playerVelY_fixed;
                    
                    // Apply movement
                    // Wave/snake with dblocked: skip slope freeze so wave can traverse multi-tile slopes
                    // (cube uses gravity to naturally fall into next slope tile; wave needs movement to do same)
                    bool skipSlopeFreeze = dblocked;
                    if (skipSlopeFreeze || (currplayer_slope_frames == 0 && currplayer_was_on_slope_counter == 0)) {
                        if (isFullSpeed)
                            playerY_fixed += playerVelY_fixed;
                        else
                            playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                    } else {
                        playerVelY_fixed = 0;
                    }
                    break;
                case 1: 
                    playerVelY_fixed = 1; 
                    break;
                case 2: 
                    playerVelY_fixed = -playerVelX_fixed; 
                    if (isFullSpeed)
                        playerY_fixed += playerVelY_fixed;
                    else
                        playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale); 
                    break;
                case 3: 
                    playerVelY_fixed = playerVelX_fixed; 
                    if (isFullSpeed)
                        playerY_fixed += playerVelY_fixed;
                    else
                        playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale); 
                    break;
                case 4: 
                    playerVelY_fixed = playerVelX_fixed; 
                    if (isFullSpeed)
                        playerY_fixed -= playerVelY_fixed;
                    else
                        playerY_fixed -= (int)Math.Round(playerVelY_fixed * simTimeScale); 
                    break;
                case 5: 
                    playerVelY_fixed = playerVelX_fixed; 
                    if (isFullSpeed)
                        playerY_fixed += playerVelY_fixed;
                    else
                        playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale); 
                    break;
            }
            
            // Offset collision based on vel_y direction and gravity
            // Normal gravity: moving down = -2, moving up = +2
            // Inverted gravity: moving up (negative vel) = -2, moving down (positive vel) = +2
            int offsetY = (playerY_fixed >> 8) + ((playerVelY_fixed > 0) ? -2 : 2);
            
            // Only run collision if death hasn't been triggered yet
            if (!deathTriggered)
            {
                WaveEject_Fresh(offsetY);
                
                // Update slope exit velocity counters (NES: called after eject in process_cube)
                UpdateSlopeCounters_Fresh();
            }
            
            // Record position for trail - only record when moving horizontally to create clean line
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                
                // Move path down 8 pixels when mini and gravity is normal
                bool isMini = (miniMode);
                bool gravityInverted = gravityFlipped;
                if (isMini && !gravityInverted)
                {
                    playerWorldCenterY_px += 8;
                }
                
                // Only record when X moves significantly (4+ pixels) to avoid capturing Y jags
                // This creates a smooth horizontal line when wave walks on floor
                var pathList = (currplayer == 0) ? recordedPlayerPath : (dual ? recordedPlayer2Path : null);
                if (pathList != null && (pathList.Count == 0 || 
                    Math.Abs(playerWorldCenterX_px - pathList.Last().Item1) >= 4))
                {
                    pathList.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                }
            }
            catch { }
        }
        
        /// <summary>
        /// wave_eject() - Use collision detection without velocity restrictions
        /// Check collision based on VELOCITY direction, not gravity
        /// For mini wave: use top 8x8 quadrant when moving UP, bottom 8x8 when moving DOWN
        /// (Direction is relative to current gravity state)
        /// </summary>
        private void WaveEject_Fresh(int offsetY)
        {
            bool isMini = (miniMode);
            bool gravityInverted = gravityFlipped;
            
            // Update slope counters each frame
            UpdateSlopeCounters();
            
            // Check slopes BEFORE wave collision
            // NES: bg_coll_D() includes slopes. wave_eject checks dblocked on hit.
            if (playerVelY_fixed >= 0)
            {
                bool slopeHit = bg_coll_D_slopes();
                if (slopeHit)
                {
                    if (dblocked)
                    {
                        // NES wave_eject: dblocked → eject upward, zero vel
                        // Slope counters stay set (re-set by bg_coll_slope each frame)
                        // so wave movement is frozen, but X advances and eject pushes up
                        if (eject_D > 0)
                        {
                            int currentY = playerY_fixed >> 8;
                            currentY -= eject_D;
                            playerY_fixed = currentY << 8;
                        }
                        playerVelY_fixed = 0;
                        AppendSimDebug($"[WAVE_SLOPE_D] dblocked climb, eject_D={eject_D}");
                        return;
                    }
                    else if (!MainWindow.Option_NoDeath)
                    {
                        deathTriggered = true;
                        paused = true;
                        _ = StopMusicAsync();
                        AppendSimDebug($"[WAVE_SLOPE_DEATH] Slope collision without dblocked - death!");
                        return;
                    }
                }
            }
            
            // Set up Generic struct for collision detection
            // Wave has special X offsets: +10 when moving UP, +4 when moving DOWN
            // X offset is based on raw velocity sign
            int xOffset = (playerVelY_fixed < 0) ? 10 : 4;
            Generic_x = (playerX_fixed >> 8) + xOffset;
            
            // For mini wave: adjust hitbox based on DIRECTION (relative to gravity)
            // Not gravity state itself, but direction the wave is actually moving
            // With normal gravity: up = negative vel, down = positive vel
            // With inverted gravity: up = positive vel, down = negative vel
            int miniOffset = 0;
            if (isMini)
            {
                bool isMovingUp = gravityInverted ? (playerVelY_fixed > 0) : (playerVelY_fixed < 0);
                miniOffset = isMovingUp ? 0 : 8;  // 0 for up direction, 8 for down direction
            }
            
            Generic_y = (playerY_fixed >> 8) + miniOffset;
            Generic_width = 8;
            Generic_height = isMini ? 8 : 16;
            
            // Check collision based on VELOCITY direction
            // NES wave_eject: if dblocked → eject + zero vel; else → death
            // NES bg_coll_U_D_checks: COL_FLOOR_CEIL auto-sets dblocked (tiles 0x01,0x02,0x05,0x06,0x88,0x89)
            if ((playerVelY_fixed & 0x8000) != 0)  // Velocity is negative (moving UP)
            {
                if (wave_coll_U())
                {
                    // NES: COL_FLOOR_CEIL tiles auto-set dblocked in bg_coll_U_D_checks
                    if ((MetatileCollision)collision == MetatileCollision.COL_FLOOR_CEIL)
                        dblocked = true;
                    
                    AppendSimDebug($"[WAVE_COLL_U] collision=0x{collision:X2} ({(MetatileCollision)collision}), dblocked={dblocked}");
                    
                    if (dblocked)
                    {
                        int currentY = playerY_fixed >> 8;
                        currentY -= eject_U;
                        playerY_fixed = currentY << 8;
                        playerVelY_fixed = 0;
                    }
                    else if (!MainWindow.Option_NoDeath)
                    {
                        deathTriggered = true;
                        paused = true;
                        _ = StopMusicAsync();
                        AppendSimDebug($"[WAVE_DEATH] Upward collision without dblocked");
                    }
                    return;
                }
            }
            else  // Velocity is non-negative (moving DOWN)
            {
                if (wave_coll_D())
                {
                    // NES: COL_FLOOR_CEIL tiles auto-set dblocked in bg_coll_U_D_checks
                    if ((MetatileCollision)collision == MetatileCollision.COL_FLOOR_CEIL)
                        dblocked = true;
                    
                    AppendSimDebug($"[WAVE_COLL_D] collision=0x{collision:X2} ({(MetatileCollision)collision}), dblocked={dblocked}");
                    
                    if (dblocked)
                    {
                        int currentY = playerY_fixed >> 8;
                        currentY -= eject_D;
                        playerY_fixed = currentY << 8;
                        playerVelY_fixed = 0;
                    }
                    else if (!MainWindow.Option_NoDeath)
                    {
                        deathTriggered = true;
                        paused = true;
                        _ = StopMusicAsync();
                        AppendSimDebug($"[WAVE_DEATH] Downward collision without dblocked");
                    }
                    return;
                }
            }
            
            // No collision — clear wasZeroed so other systems know
            wasZeroedByCollisionLastFrame = false;
        }
    }
}




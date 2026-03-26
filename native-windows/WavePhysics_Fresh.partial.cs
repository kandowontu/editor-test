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
            
            // NOTE: Do NOT clear wasZeroedByCollisionLastFrame here.
            // The NES checks the flag first (to skip velY recalculation after eject),
            // then clears it inside the velocity calculation block below.
            
            AppendSimDebug($"[WAVE_PHYS] START X={playerX_fixed >> 8} Y={playerY_fixed >> 8} velY=0x{playerVelY_fixed:X} wasZeroed={wasZeroedByCollisionLastFrame} dblocked={dblocked} mini={miniMode} grav={gravityFlipped}");
            
            // Check for orb activation
            {
                bool holdJump_orb = IsXDownAsync() || keyXHeld;
                int pressCount_orb = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                bool pressJump_orb = pressCount_orb > 0;
                bool gravityInverted_orb = (currplayer_gravity != 0);
                int playerX_px_orb = (playerX_fixed >> 8) + 1;
                int playerY_px_orb = playerY_fixed >> 8;
                int hitboxW_orb = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH_orb = (currplayer_mini != 0) ? 7 : 15;
                
                // NES: Generic.y += ((0x10 - height) >> 1); Normal: +0, Mini: +4
                if ((currplayer_mini != 0) && !gravityInverted_orb)
                {
                    playerY_px_orb += 4;
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
                    // Calculate vel_y based on vel_x UNLESS we just landed
                    // NES: check the flag BEFORE clearing it so the wave stays at velY=0
                    // for one frame after eject (allows surface-sliding)
                    if (!wasZeroedByCollisionLastFrame)
                    {
                        if (!miniMode) {
                            playerVelY_fixed = gravityFlipped ? -playerVelX_fixed : playerVelX_fixed;
                        } else {
                            playerVelY_fixed = gravityFlipped ? -(playerVelX_fixed << 1) : (playerVelX_fixed << 1);
                        }
                    }
                    wasZeroedByCollisionLastFrame = false; // Clear AFTER checking — matches NES/PF
                    
                    // Input handling - use same system as cube
                    bool holding = IsXDownAsync() || keyXHeld;
                    if (holding) playerVelY_fixed = -playerVelY_fixed;
                    
                    AppendSimDebug($"[WAVE_PHYS] postCalc velY=0x{playerVelY_fixed:X} hold={holding} slopeF={currplayer_slope_frames} slopeW={currplayer_was_on_slope_counter}");
                    
                    // Apply movement
                    // Wave/snake with dblocked: skip slope freeze so wave can traverse multi-tile slopes
                    bool skipSlopeFreeze = dblocked;
                    if (skipSlopeFreeze || (currplayer_slope_frames == 0 && currplayer_was_on_slope_counter == 0)) {
                        if (isFullSpeed)
                            playerY_fixed += playerVelY_fixed;
                        else
                            playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                    } else {
                        AppendSimDebug($"[WAVE_PHYS] SLOPE_FREEZE velY zeroed (slopeF={currplayer_slope_frames} slopeW={currplayer_was_on_slope_counter})");
                        playerVelY_fixed = 0;
                    }
                    AppendSimDebug($"[WAVE_PHYS] postMove Y={playerY_fixed >> 8} Y_fixed=0x{playerY_fixed:X}");
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

                // Update slope exit velocity counters
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
            // NES: bg_coll_U/bg_coll_D each have a slope section that runs
            // regardless of velocity.  wave_eject calls bg_coll_U when moving
            // up and bg_coll_D when moving down, so both directions are covered.
            if (playerVelY_fixed >= 0)
            {
                bool slopeHit = bg_coll_D_slopes();
                if (slopeHit)
                {
                    AppendSimDebug($"[WAVE_EJECT] slopeHit=true dblocked={dblocked} eject_D={eject_D}");
                    if (dblocked)
                    {
                        if (eject_D > 0)
                        {
                            int currentY = playerY_fixed >> 8;
                            currentY -= eject_D;
                            playerY_fixed = currentY << 8;
                        }
                        playerVelY_fixed = 0;
                        wasZeroedByCollisionLastFrame = true;
                        AppendSimDebug($"[WAVE_EJECT] slope eject Y={playerY_fixed >> 8}");
                        return;
                    }
                    else if (!MainWindow.Option_NoDeath)
                    {
                        AppendSimDebug($"[WAVE_DEATH] slope death (dblocked=false) X={playerX_fixed >> 8} Y={playerY_fixed >> 8}");
                        deathTriggered = true;
                        if (!pfSimulating)
                        {
                            paused = true;
                            _ = StopMusicAsync();
                            try
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                    if (this.Owner is MainWindow mw)
                                    {
                                        try { mw.PauseSimulatorPlayback(); } catch { }
                                    }
                                }));
                            }
                            catch { }
                        }
                        return;
                    }
                }
            }
            else // velY < 0 — moving UP: check ceiling slopes (bg_coll_U_slopes)
            {
                bool slopeHit = bg_coll_U_slopes();
                if (slopeHit)
                {
                    AppendSimDebug($"[WAVE_EJECT] U_slopeHit=true dblocked={dblocked} eject_U={eject_U}");
                    if (dblocked)
                    {
                        // eject_U is negative (from bg_coll_return_slope_U: eject_U = -tmp8).
                        // playerY -= eject_U → moves DOWN (away from ceiling slope).
                        int currentY = playerY_fixed >> 8;
                        currentY -= eject_U;
                        playerY_fixed = currentY << 8;
                        playerVelY_fixed = 0;
                        wasZeroedByCollisionLastFrame = true;
                        AppendSimDebug($"[WAVE_EJECT] U slope eject Y={playerY_fixed >> 8}");
                        return;
                    }
                    else if (!MainWindow.Option_NoDeath)
                    {
                        AppendSimDebug($"[WAVE_DEATH] U slope death (dblocked=false) X={playerX_fixed >> 8} Y={playerY_fixed >> 8}");
                        deathTriggered = true;
                        if (!pfSimulating)
                        {
                            paused = true;
                            _ = StopMusicAsync();
                            try
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                    if (this.Owner is MainWindow mw)
                                    {
                                        try { mw.PauseSimulatorPlayback(); } catch { }
                                    }
                                }));
                            }
                            catch { }
                        }
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
            int miniOffset = 0;
            if (isMini)
            {
                bool isMovingUp = gravityInverted ? (playerVelY_fixed > 0) : (playerVelY_fixed < 0);
                miniOffset = isMovingUp ? 0 : 8;
            }
            
            Generic_y = (playerY_fixed >> 8) + miniOffset;
            Generic_width = 8;
            Generic_height = isMini ? 8 : 16;
            
            AppendSimDebug($"[WAVE_EJECT] Generic=({Generic_x},{Generic_y}) {Generic_width}x{Generic_height} velY=0x{playerVelY_fixed:X} miniOff={miniOffset}");
            
            // Check collision based on VELOCITY direction
            // When velY == 0 (wasZeroed frame), skip collision — wave is resting on surface
            if (playerVelY_fixed < 0)  // Velocity is negative (moving UP)
            {
                if (wave_coll_U())
                {
                    var colType = (MetatileCollision)collision;
                    // NES: only COL_FLOOR_CEIL sets dblocked — all other solid tiles kill wave
                    if (colType == MetatileCollision.COL_FLOOR_CEIL)
                        dblocked = true;

                    AppendSimDebug($"[WAVE_EJECT] coll_U hit tile={colType} dblocked={dblocked} eject_U={eject_U}");

                    if (dblocked)
                    {
                        int currentY = playerY_fixed >> 8;
                        currentY -= eject_U;
                        playerY_fixed = currentY << 8;
                        playerVelY_fixed = 0;
                        wasZeroedByCollisionLastFrame = true;
                        AppendSimDebug($"[WAVE_EJECT] UP eject Y={playerY_fixed >> 8}");
                    }
                    else if (!MainWindow.Option_NoDeath)
                    {
                        AppendSimDebug($"[WAVE_DEATH] UP non-walkable tile={colType} X={playerX_fixed >> 8} Y={playerY_fixed >> 8} probe=({Generic_x},{Generic_y - 1})");
                        deathTriggered = true;
                        if (!pfSimulating)
                        {
                            paused = true;
                            _ = StopMusicAsync();
                            try
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                    if (this.Owner is MainWindow mw)
                                    {
                                        try { mw.PauseSimulatorPlayback(); } catch { }
                                    }
                                }));
                            }
                            catch { }
                        }
                    }
                    return;
                }
            }
            else if (playerVelY_fixed > 0)  // Velocity is positive (moving DOWN)
            {
                if (wave_coll_D())
                {
                    var colType = (MetatileCollision)collision;
                    // NES: only COL_FLOOR_CEIL sets dblocked — all other solid tiles kill wave
                    if (colType == MetatileCollision.COL_FLOOR_CEIL)
                        dblocked = true;

                    AppendSimDebug($"[WAVE_EJECT] coll_D hit tile={colType} dblocked={dblocked} eject_D={eject_D}");

                    if (dblocked)
                    {
                        int currentY = playerY_fixed >> 8;
                        currentY -= eject_D;
                        playerY_fixed = currentY << 8;
                        playerVelY_fixed = 0;
                        wasZeroedByCollisionLastFrame = true;
                        AppendSimDebug($"[WAVE_EJECT] DOWN eject Y={playerY_fixed >> 8}");
                    }
                    else if (!MainWindow.Option_NoDeath)
                    {
                        AppendSimDebug($"[WAVE_DEATH] DOWN non-walkable tile={colType} X={playerX_fixed >> 8} Y={playerY_fixed >> 8} probe=({Generic_x},{Generic_y + Generic_height})");
                        deathTriggered = true;
                        if (!pfSimulating)
                        {
                            paused = true;
                            _ = StopMusicAsync();
                            try
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                    if (this.Owner is MainWindow mw)
                                    {
                                        try { mw.PauseSimulatorPlayback(); } catch { }
                                    }
                                }));
                            }
                            catch { }
                        }
                    }
                    return;
                }
            }
            else
            {
                AppendSimDebug($"[WAVE_EJECT] velY==0 skip collision");
            }
        }
    }
}




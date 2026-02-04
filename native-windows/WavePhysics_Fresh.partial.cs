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
                if (currplayer_mini != 0 && !gravityInverted_orb)
                {
                    playerY_px_orb += 9;
                }
                
                int scrollX_px_orb = 0;
                
                int tempVelY = playerVelY_fixed;
                var (orbActivated, _) = UpdateOrbSystem(4, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
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
                    // Calculate vel_y based on vel_x
                    if (currplayer_mini == 0) {
                        playerVelY_fixed = currplayer_gravity != 0 ? -playerVelX_fixed : playerVelX_fixed;
                    } else {
                        playerVelY_fixed = currplayer_gravity != 0 ? -(playerVelX_fixed << 1) : (playerVelX_fixed << 1);
                    }
                    
                    // Input handling - use same system as cube
                    bool holding = IsXDownAsync() || keyXHeld;
                    if (holding) playerVelY_fixed = -playerVelY_fixed;
                    
                    // Apply movement
                    if (currplayer_slope_frames == 0 && currplayer_was_on_slope_counter == 0) {
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
                    playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale); 
                    break;
                case 3: 
                    playerVelY_fixed = playerVelX_fixed; 
                    playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale); 
                    break;
                case 4: 
                    playerVelY_fixed = playerVelX_fixed; 
                    playerY_fixed -= (int)Math.Round(playerVelY_fixed * simTimeScale); 
                    break;
                case 5: 
                    playerVelY_fixed = playerVelX_fixed; 
                    playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale); 
                    break;
            }
            
            // Offset collision 2 pixels based on vel_y direction
            int offsetY = (playerY_fixed >> 8) + ((playerVelY_fixed < 0) ? 2 : -2);
            
            // Only run collision if death hasn't been triggered yet
            if (!deathTriggered)
            {
                WaveEject_Fresh(offsetY);
            }
            
            // Record position for trail - only record when moving horizontally to create clean line
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                
                // Move path down 8 pixels when mini and gravity is normal
                bool isMini = (currplayer_mini != 0);
                bool gravityInverted = (currplayer_gravity != 0);
                if (isMini && !gravityInverted)
                {
                    playerWorldCenterY_px += 8;
                }
                
                // Only record when X moves significantly (4+ pixels) to avoid capturing Y jags
                // This creates a smooth horizontal line when wave walks on floor
                if (recordedPlayerPath.Count == 0 || 
                    Math.Abs(playerWorldCenterX_px - recordedPlayerPath.Last().Item1) >= 4)
                {
                    recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                }
            }
            catch { }
        }
        
        /// <summary>
        /// wave_eject() - land ONLY on safe tiles or D-blocks
        /// Wave dies on any non-safe tile
        /// </summary>
        private void WaveEject_Fresh(int offsetY)
        {
            bool isMini = (currplayer_mini != 0);
            int playerX_px = (playerX_fixed >> 8);
            int playerY_px = (playerY_fixed >> 8);
            bool gravityInverted = (currplayer_gravity != 0);
            
            // Wave hitbox: 8 pixels wide, 8 pixels tall (or 8x7 for mini)
            int hitboxW = 8;
            int hitboxH = isMini ? 7 : 8;
            
            // Wave uses different X offsets for collision based on velocity direction
            // When moving UP (velY < 0): offset +10, When moving DOWN (velY > 0): offset +4
            int collisionXOffset = (playerVelY_fixed < 0) ? 10 : 4;
            int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
            
            int playerLeft_px = playerX_px + collisionXOffset;
            int playerRight_px = playerX_px + collisionXOffset + hitboxW - 1;
            
            // Check collision based on VELOCITY DIRECTION, not gravity
            bool isMovingDown = (playerVelY_fixed > 0);
            
            if (isMovingDown)
            {
                // Moving DOWN: check below at offsetY
                int checkY_px = offsetY + hitboxH;
                int checkTileY = checkY_px / TILE;
                
                // Calculate ground layer offset
                int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                
                // Scan for tiles - check if SAFE
                bool foundAnyTile = false;
                bool isSafeTile = false;
                
                if (checkTileY >= 0 && checkTileY < mapHeight)
                {
                    for (int tx = playerLeft_px / TILE; tx <= playerRight_px / TILE; tx++)
                    {
                        if (tx >= 0 && tx < mapWidth)
                        {
                            int checkTileArrayY = checkTileY + groundRowsToReserve;
                            if (checkTileArrayY >= mapHeight) continue;
                            
                            int tileIdx = checkTileArrayY * mapWidth + tx;
                            if (tileIdx >= 0 && tileIdx < tiles.Length)
                            {
                                int tileId = tiles[tileIdx];
                                // Check if non-empty tile
                                if (tileId != 0 && tileId != 0xFF)
                                {
                                    foundAnyTile = true;
                                    // Check if it's a safe tile or if we're D-blocked
                                    if (IsWaveSafeTile(tileId) || dblocked)
                                    {
                                        isSafeTile = true;
                                        AppendSimDebug($"[WAVE] Found safe tile 0x{tileId:X2} below, dblocked={dblocked}");
                                    }
                                    else
                                    {
                                        AppendSimDebug($"[WAVE] Found UNSAFE tile 0x{tileId:X2} below, dblocked={dblocked}");
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                
                if (foundAnyTile)
                {
                    if (isSafeTile)
                    {
                        // Land on safe tile
                        int landY = (checkTileY * TILE) - hitboxH - hitboxOffsetY;
                        playerY_fixed = landY << 8;
                        playerVelY_fixed = 0;
                        wasZeroedByCollisionLastFrame = true;
                        AppendSimDebug($"[WAVE] LANDED on safe tile at Y={landY}");
                    }
                    else
                    {
                        // Death on unsafe tile - reposition to tile first, then die
                        int landY = (checkTileY * TILE) - hitboxH - hitboxOffsetY;
                        playerY_fixed = landY << 8;
                        playerVelY_fixed = 0;
                        
                        if (!MainWindow.Option_NoDeath)
                        {
                            AppendSimDebug($"[WAVE_DEATH] Unsafe tile collision, repositioned to Y={landY}");
                            deathTriggered = true;
                            deathTileX = playerX_px;
                            deathTileY = playerY_px;
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
                                        try { mw.AddDeathMarker(playerX_px, playerY_px); } catch { }
                                    }
                                }));
                            }
                            catch { }
                        }
                    }
                }
            }
            else
            {
                // Moving UP: check above at offsetY
                int checkY_px = offsetY - 1;
                int checkTileY = checkY_px / TILE;
                
                // Calculate ground layer offset
                int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                
                // Scan for tiles - check if SAFE
                bool foundAnyTile = false;
                bool isSafeTile = false;
                
                if (checkTileY >= 0 && checkTileY < mapHeight)
                {
                    for (int tx = playerLeft_px / TILE; tx <= playerRight_px / TILE; tx++)
                    {
                        if (tx >= 0 && tx < mapWidth)
                        {
                            int checkTileArrayY = checkTileY + groundRowsToReserve;
                            if (checkTileArrayY >= mapHeight) continue;
                            
                            int tileIdx = checkTileArrayY * mapWidth + tx;
                            if (tileIdx >= 0 && tileIdx < tiles.Length)
                            {
                                int tileId = tiles[tileIdx];
                                // Check if non-empty tile
                                if (tileId != 0 && tileId != 0xFF)
                                {
                                    foundAnyTile = true;
                                    // Check if it's a safe tile or if we're D-blocked
                                    if (IsWaveSafeTile(tileId) || dblocked)
                                    {
                                        isSafeTile = true;
                                        AppendSimDebug($"[WAVE] Found safe tile 0x{tileId:X2} above, dblocked={dblocked}");
                                    }
                                    else
                                    {
                                        AppendSimDebug($"[WAVE] Found UNSAFE tile 0x{tileId:X2} above, dblocked={dblocked}");
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                
                if (foundAnyTile)
                {
                    if (isSafeTile)
                    {
                        // Land on safe tile
                        int landY = ((checkTileY + 1) * TILE);
                        playerY_fixed = landY << 8;
                        playerVelY_fixed = 0;
                        wasZeroedByCollisionLastFrame = true;
                        AppendSimDebug($"[WAVE] LANDED on safe tile at Y={landY} (inverted)");
                    }
                    else
                    {
                        // Death on unsafe tile - reposition to tile first, then die
                        int landY = ((checkTileY + 1) * TILE);
                        playerY_fixed = landY << 8;
                        playerVelY_fixed = 0;
                        
                        if (!MainWindow.Option_NoDeath)
                        {
                            AppendSimDebug($"[WAVE_DEATH] Unsafe tile collision (inverted), repositioned to Y={landY}");
                            deathTriggered = true;
                            deathTileX = playerX_px;
                            deathTileY = playerY_px;
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
                                        try { mw.AddDeathMarker(playerX_px, playerY_px); } catch { }
                                    }
                                }));
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Check if a tile ID is safe for wave to land on
        /// Safe tiles: 0x01, 0x05, 0x88, 0x89, or ground layer
        /// </summary>
        private bool IsWaveSafeTile(int tileId)
        {
            return tileId == 0x01 || tileId == 0x05 || tileId == 0x88 || tileId == 0x89;
        }
    }
}

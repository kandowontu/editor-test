using System;
using System.Collections.Generic;
using System.Linq;

namespace FamidashEditor
{
    /// <summary>
    /// Orb system implementation for the simulator.
    /// Orbs modify player velocity and can reverse gravity.
    /// </summary>
    public partial class SimulatorWindow
    {
        // Orb sprite type constants (from objdefines.h)
        private const byte BLUE_ORB = 0x05;
        private const byte PINK_ORB = 0x06;
        private const byte YELLOW_ORB = 0x0B;
        private const byte YELLOW_ORB_BIGGER = 0x1F;
        private const byte GREEN_ORB = 0x27;
        private const byte RED_ORB = 0x28;
        private const byte YELLOW_ORB_SMALLER = 0x29;
        private const byte BLACK_ORB = 0x44;
        private const byte DASH_ORB = 0x45;
        private const byte DASH_GRAVITY_ORB = 0x46;
        private const byte DASH_ORB_45DEG_UP = 0x4C;
        private const byte DASH_GRAVITY_ORB_45DEG_UP = 0x4D;
        private const byte DASH_ORB_45DEG_DOWN = 0x50;
        private const byte DASH_GRAVITY_ORB_45DEG_DOWN = 0x51;
        private const byte SPIDER_ORB_UP = 0x54;
        private const byte SPIDER_ORB_DOWN = 0x55;
        private const byte SPIDER_PAD_UP = 0x56;
        private const byte SPIDER_PAD_DOWN = 0x57;
        private const byte DASH_ORB_UPWARDS = 0x5B;
        private const byte DASH_GRAVITY_ORB_UPWARDS = 0x5C;
        private const byte DASH_ORB_DOWNWARDS = 0x5D;
        private const byte DASH_GRAVITY_ORB_DOWNWARDS = 0x5E;
        private const byte TELEPORT_ORB_ENTER = 0x59;
        private const byte TELEPORT_ORB_EXIT = 0x5A;
        private const byte WHITE_ORB = 0x7A;
        private const byte BLUE_ORB_MULTI = 0x7B;
        private const byte GREEN_ORB_MULTI = 0x7C;

        // Blue orb special constants
        private const short PAD_HEIGHT_BLUE_normal = -0x3A0;
        private const short PAD_HEIGHT_BLUE_mini = -0x160;
        private const short ORB_BALL_HEIGHT_BLUE_normal = -0x1A0;
        private const short ORB_BALL_HEIGHT_BLUE_mini = -0x60;

        // Orb activation state tracking
        // Note: orbBufferActive, orbHoldConsumedKeyStillDown, orbHoldSuppressing
        // are already defined in SimulatorWindow.xaml.cs
        private Dictionary<int, bool> orbActivated = new Dictionary<int, bool>();
        private bool keyXHeldStartedOnGround = false; // Track if X hold started while grounded

        /// <summary>
        /// Clear orb buffer when X is released
        /// </summary>
        private void ClearOrbBuffer()
        {
            orbBufferActive[currplayer] = false;
            orbHoldConsumedKeyStillDown[currplayer] = false;
            keyXHeldStartedOnGround = false;
        }
        
        /// <summary>
        /// Update orb hold suppression based on ground state
        /// X hold from a ground jump should not activate orbs
        /// </summary>
        private void UpdateOrbHoldSuppression(bool xHeld, bool isGrounded)
        {
            if (xHeld && isGrounded)
            {
                keyXHeldStartedOnGround = true;
                orbHoldSuppressing[currplayer] = true;
            }
            else if (!xHeld)
            {
                keyXHeldStartedOnGround = false;
                orbHoldSuppressing[currplayer] = false;
            }
            else if (!isGrounded && keyXHeldStartedOnGround)
            {
                // Still in air from ground jump, suppress orbs
                orbHoldSuppressing[currplayer] = true;
            }
        }

        /// <summary>
        /// Check if player hitbox overlaps with orb sprite hitbox
        /// Uses the same logic as SpriteIntersectsPlayer for consistency
        /// </summary>
        private bool CheckOrbCollision(int idx, int spriteType, int playerLeft_px, int playerRight_px, 
                                       int playerTop_px, int playerBottom_px)
        {
            try
            {
                int storageTileX = idx % mapWidth;
                int storageTileY = idx / mapWidth;

                // Get sprite geometry from tables
                int id_for_geom = spriteType & 0xFF;
                int anchorKey = -1;
                if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                {
                    anchorKey = anchor.anchorTileY * mapWidth + anchor.anchorTileX;
                    if (anchorKey >= 0 && anchorKey < sprites.Length)
                    {
                        int anchoredId = sprites[anchorKey];
                        if (anchoredId >= 0 && anchoredId < 256) id_for_geom = anchoredId & 0xFF;
                    }
                }

                int hw = (id_for_geom >= 0 && id_for_geom < sprite_widths.Length) ? sprite_widths[id_for_geom] : TILE;
                int hh = (id_for_geom >= 0 && id_for_geom < sprite_heights.Length) ? sprite_heights[id_for_geom] : TILE;
                int hxoff = (id_for_geom >= 0 && id_for_geom < sprite_x_offset.Length) ? sprite_x_offset[id_for_geom] : 0;
                int hyoff = (id_for_geom >= 0 && id_for_geom < sprite_y_offset.Length) ? sprite_y_offset[id_for_geom] : 0;

                // Per-position pixel offset
                int pxOff = 0; int pyOff = 0;
                if (anchorKey >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var aoffs2))
                {
                    pxOff = aoffs2.offsetX; pyOff = aoffs2.offsetY;
                }
                else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs2))
                {
                    pxOff = offs2.offsetX; pyOff = offs2.offsetY;
                }

                // Compute world-space sprite rectangle
                int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                int spriteLeft_world_px = storageTileX * TILE + hxoff + pxOff;
                int spriteTop_world_px = (storageTileY - groundRowsToReserve_local) * TILE + hyoff + pyOff;
                int spriteRight_world_px = spriteLeft_world_px + Math.Max(1, hw) - 1;
                int spriteBottom_world_px = spriteTop_world_px + Math.Max(1, hh) - 1;

                // Check AABB overlap
                return !(playerLeft_px > spriteRight_world_px ||
                         playerRight_px < spriteLeft_world_px ||
                         playerTop_px > spriteBottom_world_px ||
                         playerBottom_px < spriteTop_world_px);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check for orb collisions and activate orbs based on input
        /// Returns (orbActivated, orbType) - orbType is the sprite type that was activated, or -1 if none
        /// </summary>
        private (bool activated, int orbType) UpdateOrbSystem(int gamemode, int playerX_px, int playerY_px, int playerW, int playerH,
                                      int scrollX_px, bool xPressed, bool xHeld, bool gravityInverted, bool mini,
                                      ref int velocityY)
        {
            bool canBuffer = CanBufferOrb(gamemode);
            bool orbActivatedThisFrame = false;
            int activatedOrbType = -1;
            
            // Player bounding box
            int playerLeft_px = playerX_px;
            int playerRight_px = playerX_px + playerW - 1;
            int playerTop_px = playerY_px;
            int playerBottom_px = playerY_px + playerH - 1;

            // Scan all sprites for orb collisions
            for (int idx = 0; idx < sprites.Length; idx++)
            {
                int spriteType = sprites[idx];
                if (spriteType == -1) continue; // Empty
                
                // Check if this is an orb sprite type
                if (!IsOrbSprite(spriteType)) continue;
                
                // CRITICAL: Do not activate orbs while dashing (from dash orb)
                // Dash state prevents all orb activations until dash ends
                if (dashing[currplayer] != 0) continue;
                
                // Check collision with player using same method as pads/portals
                if (!CheckOrbCollision(idx, spriteType, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    continue;
                    
                // Check if already activated (except multi orbs and dual mode)
                // In dual mode, each player can independently activate the same orb
                bool isMultiOrb = (spriteType == BLUE_ORB_MULTI || spriteType == GREEN_ORB_MULTI);
                if (!isMultiOrb && !dual && orbActivated.ContainsKey(idx) && orbActivated[idx])
                    continue;
                    
                // Handle activation based on gamemode and input
                bool shouldActivate = false;
                
                if (canBuffer)
                {
                    // Bufferable modes: can hold X before hitting orb
                    // Set buffer when X is first pressed (not from ground)
                    if (xPressed && !orbHoldConsumedKeyStillDown[currplayer] && !orbHoldSuppressing[currplayer])
                    {
                        shouldActivate = true;
                        orbBufferActive[currplayer] = true;
                    }
                    // While X is held and buffer is active, continue activating orbs
                    else if (xHeld && orbBufferActive[currplayer] && !orbHoldSuppressing[currplayer])
                    {
                        shouldActivate = true;
                        // Keep buffer active - will be cleared on activation or release
                    }
                    // If X is held but buffer not active yet, set it
                    else if (xHeld && !orbBufferActive[currplayer] && !orbHoldSuppressing[currplayer] && !orbHoldConsumedKeyStillDown[currplayer])
                    {
                        orbBufferActive[currplayer] = true;
                        shouldActivate = true;
                    }
                }
                else
                {
                    // Non-bufferable modes: require fresh X press while overlapping
                    if (xPressed)
                    {
                        shouldActivate = true;
                    }
                }
                
                if (shouldActivate)
                {
                    try { AppendSimDebug($"[ORB] ACTIVATING orb 0x{spriteType:X2}!"); } catch { }
                    
                    // Activate the orb!
                    ActivateOrb(spriteType, gamemode, gravityInverted, mini, ref velocityY);
                    
                    // Mark as activated (but not in dual mode - each player can activate independently)
                    if (!isMultiOrb && !dual)
                    {
                        orbActivated[idx] = true;
                    }
                    
                    orbActivatedThisFrame = true;
                    activatedOrbType = spriteType;
                    orbHoldConsumedKeyStillDown[currplayer] = true;
                    // Clear buffer on orb activation - require fresh press/hold for next orb
                    orbBufferActive[currplayer] = false;
                    
                    // Only one orb per frame
                    return (true, activatedOrbType);
                }
                else
                {
                    try { AppendSimDebug($"[ORB] Orb NOT activated (collision but no input): xPressed={xPressed}, xHeld={xHeld}, canBuffer={canBuffer}, orbBufferActive[currplayer] ={orbBufferActive}, orbHoldSuppressing[currplayer] ={orbHoldSuppressing}, orbHoldConsumedKeyStillDown[currplayer] ={orbHoldConsumedKeyStillDown}, alreadyActivated={orbActivated[idx]}"); } catch { }
                }
            }
            
            return (orbActivatedThisFrame, activatedOrbType);
        }
        
        /// <summary>
        /// Check if sprite type is an orb
        /// </summary>
        private bool IsOrbSprite(int spriteType)
        {
            return spriteType == PINK_ORB ||
                   spriteType == YELLOW_ORB ||
                   spriteType == RED_ORB ||
                   spriteType == YELLOW_ORB_BIGGER ||
                   spriteType == YELLOW_ORB_SMALLER ||
                   spriteType == BLACK_ORB ||
                   spriteType == BLUE_ORB ||
                   spriteType == GREEN_ORB ||
                   spriteType == WHITE_ORB ||
                   spriteType == BLUE_ORB_MULTI ||
                   spriteType == GREEN_ORB_MULTI ||
                   spriteType == TELEPORT_ORB_ENTER;
                   // Spider orbs/pads (0x54-0x57) are handled separately
                   // TELEPORT_ORB_EXIT (0x5A) is NOT activatable - only provides Y position
        }
        
        /// <summary>
        /// Activate an orb and apply its effect
        /// </summary>
        private void ActivateOrb(int orbType, int gamemode, bool gravityInverted, bool mini, ref int velocityY)
        {
            // Map gamemodes to their orb/pad interaction base modes
            int modeCol = gamemode;
            if (modeCol == 8) modeCol = 0; // Ninja uses cube values
            if (modeCol == 9) modeCol = 7; // Pogo uses swing values
            if (modeCol == 10) modeCol = 0; // Football uses cube values
            if (modeCol == 6 || modeCol == 11) { modeCol = 6; } // Wave (6) and Snake (11) use wave values
            if (modeCol < 0 || modeCol >= 8) return;
            
            // Wave and Snake special case: only blue orbs and spider orbs/pads work, just reverse gravity
            bool isWaveOrSnake = (gamemode == 6 || gamemode == 11);
            if (isWaveOrSnake && orbType != BLUE_ORB && orbType != BLUE_ORB_MULTI && 
                orbType != SPIDER_ORB_UP && orbType != SPIDER_ORB_DOWN && 
                orbType != SPIDER_PAD_UP && orbType != SPIDER_PAD_DOWN)
            {
                // Wave/Snake ignore non-blue, non-spider orbs
                return;
            }
            
            switch (orbType)
            {
                case BLUE_ORB:
                case BLUE_ORB_MULTI:
                case SPIDER_ORB_UP:
                case SPIDER_ORB_DOWN:
                case SPIDER_PAD_UP:
                case SPIDER_PAD_DOWN:
                    // Blue orb and spider orbs/pads: reverse gravity
                    gravityInverted = !gravityInverted;
                    gravityFlipped = gravityInverted;
                    gravityReversed = gravityInverted;
                    currplayer_gravity = (byte)(gravityInverted ? 0xFF : 0x00);
                    UpdateEffectiveGravity();
                    UpdatePlayerIconFlip();
                    
                    // Wave and Snake: no velocity change, just reverse gravity
                    if (isWaveOrSnake)
                    {
                        // Velocity remains unchanged, will be set by wave/snake movement code
                        break;
                    }
                    
                    // Other modes: Set velocity based on gamemode and mini state
                    // Ball mode uses ORB_BALL_HEIGHT_BLUE, others use PAD_HEIGHT_BLUE
                    if (gamemode == 2) // BALL_MODE
                    {
                        velocityY = mini ? ORB_BALL_HEIGHT_BLUE_mini : ORB_BALL_HEIGHT_BLUE_normal;
                    }
                    else
                    {
                        velocityY = mini ? PAD_HEIGHT_BLUE_mini : PAD_HEIGHT_BLUE_normal;
                    }
                    
                    // Velocity direction based on NEW gravity state AFTER inversion
                    // If gravity is now inverted (up): velocity should be negative (upwards)
                    // If gravity is now normal (down): velocity should be positive (downwards)
                    // Constants are already negative, so negate them when gravity is normal
                    if (!gravityInverted)
                        velocityY = -velocityY;
                    break;
                    
                case WHITE_ORB:
                    // White orb: reset Y velocity to 0
                    velocityY = 0;
                    break;
                    
                case TELEPORT_ORB_ENTER:
                    // Teleport orb: find corresponding exit orb (0x5A) on visible screen and teleport player there
                    // Only exit orbs currently visible on screen are active
                    bool foundExit = false;
                    int exitY_px = 0;
                    
                    // Calculate visible screen bounds
                    int cameraLeft_px = cameraX_fixed >> 8;
                    int cameraTop_px = cameraY_fixed >> 8;
                    int cameraRight_px = cameraLeft_px + (NES_W * TILE);
                    int cameraBottom_px = cameraTop_px + (NES_H * TILE);
                    
                    // Search for exit orb (0x5A) on visible screen
                    // If multiple exits are visible, the last one loaded will be used
                    for (int idx = 0; idx < sprites.Length; idx++)
                    {
                        if (sprites[idx] == TELEPORT_ORB_EXIT)
                        {
                            // Get exit orb world position
                            int exitTileX = idx % mapWidth;
                            int exitTileY = idx / mapWidth;
                            int exitWorldX_px = exitTileX * TILE;
                            int exitWorldY_px = exitTileY * TILE;
                            
                            // Apply sprite anchoring/offset if present
                            if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                            {
                                exitWorldX_px = anchor.anchorTileX * TILE;
                                exitWorldY_px = anchor.anchorTileY * TILE;
                            }
                            
                            // Apply pixel offsets if present
                            if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offset))
                            {
                                exitWorldY_px += offset.offsetY;
                            }
                            
                            // Check if this exit orb is on the visible screen
                            if (exitWorldX_px >= cameraLeft_px && exitWorldX_px < cameraRight_px &&
                                exitWorldY_px >= cameraTop_px && exitWorldY_px < cameraBottom_px)
                            {
                                // This exit is visible - use it (if multiple visible, last one wins)
                                exitY_px = exitWorldY_px;
                                foundExit = true;
                                AppendSimDebug($"[TELEPORT_ORB] Found visible exit at ({exitWorldX_px}, {exitWorldY_px})");
                            }
                        }
                    }
                    
                    if (foundExit)
                    {
                        // Apply 3-tile ground offset (same as all collision detection)
                        // The map reserves 3 tiles at the bottom for the ground layer
                        exitY_px -= (3 * TILE);
                        
                        // Teleport player Y position only (keep X position unchanged)
                        playerY_fixed = exitY_px << 8;
                        
                        // Update camera to follow the teleport (center on player Y)
                        int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                        cameraY_fixed = Math.Max(0, Math.Min(maxCameraY_fixed, playerY_fixed - ((NES_H * TILE / 2) << 8)));
                        
                        // Reset velocity and set orbed flag
                        velocityY = 0;
                        playerVelY_fixed = 0;
                        orbed[currplayer] = true;
                        
                        AppendSimDebug($"[TELEPORT_ORB] Teleported to Y={exitY_px} (camera Y={cameraY_fixed >> 8})");
                    }
                    else
                    {
                        AppendSimDebug($"[TELEPORT_ORB] WARNING: No exit orb (0x5A) visible on screen!");
                    }
                    break;
                    
                case GREEN_ORB:
                case GREEN_ORB_MULTI:
                    // Green orb: reverse gravity then apply yellow orb velocity
                    gravityInverted = !gravityInverted;
                    gravityFlipped = gravityInverted;
                    gravityReversed = gravityInverted;
                    currplayer_gravity = (byte)(gravityInverted ? 0xFF : 0x00);
                    UpdateEffectiveGravity();
                    UpdatePlayerIconFlip();
                    
                    // Use yellow orb row (0) from PadOrbHeights
                    int greenBaseVel = mini ? PadOrbHeights_Mini[0][modeCol] : PadOrbHeights[0][modeCol];
                    // After gravity flip: launch against new gravity direction
                    int greenGravityMult = gravityInverted ? 1 : -1;
                    velocityY = greenBaseVel * greenGravityMult;
                    break;
                    
                default:
                    // Regular orbs: get row from PadOrbHeights
                    int padRow = GetOrbPadRow((byte)orbType);
                    if (padRow >= 0 && padRow < 9)
                    {
                        int baseVel = mini ? PadOrbHeights_Mini[padRow][modeCol] : PadOrbHeights[padRow][modeCol];
                        
                        // Black orb goes opposite direction of other orbs
                        bool isBlackOrb = (orbType == BLACK_ORB);
                        
                        // Regular orbs: launch against gravity (upward for normal, downward for inverted)
                        // Black orb: launch with gravity (downward for normal, upward for inverted)
                        int gravityMult = gravityInverted ? 1 : -1;
                        // Note: Black orb does NOT negate - it uses the same multiplier as regular orbs
                        // but the velocity direction is already opposite in the table
                        
                        velocityY = baseVel * gravityMult;
                    }
                    break;
            }
        }

        /// <summary>
        /// Get PadOrbHeights row for specific orb type
        /// </summary>
        private int GetOrbPadRow(byte orbType)
        {
            switch (orbType)
            {
                case YELLOW_ORB: return 0; // yellow orb
                case PINK_ORB: return 2; // pink orb
                case RED_ORB: return 4; // red orb
                case YELLOW_ORB_BIGGER: return 5; // yellow orb bigger
                case BLACK_ORB: return 6; // black orb
                case YELLOW_ORB_SMALLER: return 7; // yellow orb smaller
                default: return 0; // default to yellow orb
            }
        }

        /// <summary>
        /// Check if gamemode can buffer orb activation (hold X before hitting orb)
        /// </summary>
        private bool CanBufferOrb(int gamemode)
        {
            // Cube, ball, robot, ninja, spider, swing can buffer
            return gamemode == 0 || // CUBE
                   gamemode == 2 || // BALL
                   gamemode == 4 || // ROBOT
                   gamemode == 8 || // NINJA
                   gamemode == 5 || // SPIDER
                   gamemode >= 7;   // SWING and above
        }

        /// <summary>
        /// Reset orb activation state (called when level resets)
        /// </summary>
        private void ResetOrbSystem()
        {
            orbActivated.Clear();
            orbBufferActive[currplayer] = false;
            orbHoldConsumedKeyStillDown[currplayer] = false;
            orbHoldSuppressing[currplayer] = false;
            
            // Debug: Log all orbs in the level
            try
            {
                int orbCount = 0;
                AppendSimDebug($"[ORB_INIT] Scanning level for orbs (mapWidth={mapWidth}, mapHeight={mapHeight})...");
                for (int y = 0; y < mapHeight; y++)
                {
                    for (int x = 0; x < mapWidth; x++)
                    {
                        int idx = y * mapWidth + x;
                        if (idx >= 0 && idx < sprites.Length)
                        {
                            int spriteType = sprites[idx];
                            if (spriteType != -1 && IsOrbSprite(spriteType))
                            {
                                orbCount++;
                                AppendSimDebug($"[ORB_INIT] Found orb 0x{spriteType:X2} at tile ({x},{y}) = world pos ({x * TILE},{y * TILE})");
                            }
                        }
                    }
                }
                AppendSimDebug($"[ORB_INIT] Total orbs found: {orbCount}");
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[ORB_INIT] Error scanning for orbs: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for dash orb collisions and activate them
        /// Dash orbs set player velocity based on their type and optionally flip gravity
        /// </summary>
        private void CheckDashOrbCollision()
        {
            if (sprites == null || mapWidth <= 0 || mapHeight <= 0) return;

            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            
            // Use actual collision hitbox size, not visual size
            int hitboxW = miniMode ? 8 : 15;
            int hitboxH = miniMode ? 7 : 15;
            
            // Apply mini mode offset: bottom-left for normal, top-left for inverted
            if (miniMode && !gravityFlipped)
            {
                playerY_px += 9;
            }

            int playerLeft_px = playerX_px;
            int playerRight_px = playerX_px + hitboxW - 1;
            int playerTop_px = playerY_px;
            int playerBottom_px = playerY_px + hitboxH - 1;

            for (int idx = 0; idx < sprites.Length; idx++)
            {
                int spriteType = sprites[idx];
                if (spriteType == -1) continue;

                // Check if this is a dash orb
                bool isDashOrb = spriteType == DASH_ORB || spriteType == DASH_GRAVITY_ORB ||
                                spriteType == DASH_ORB_45DEG_UP || spriteType == DASH_GRAVITY_ORB_45DEG_UP ||
                                spriteType == DASH_ORB_45DEG_DOWN || spriteType == DASH_GRAVITY_ORB_45DEG_DOWN ||
                                spriteType == DASH_ORB_UPWARDS || spriteType == DASH_GRAVITY_ORB_UPWARDS ||
                                spriteType == DASH_ORB_DOWNWARDS || spriteType == DASH_GRAVITY_ORB_DOWNWARDS;

                if (!isDashOrb) continue;

                // Check for collision
                if (!CheckOrbCollision(idx, spriteType, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    continue;

                // Check if already activated (prevent reactivation)
                // In dual mode, each player can independently activate the same orb
                if (!dual && orbActivated.ContainsKey(idx) && orbActivated[idx])
                    continue;

                // Check for press/hold/buffer like regular orbs
                bool xPressed = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0) > 0;
                bool xHeld = IsXDownAsync() || keyXHeld;
                bool canBuffer = CanBufferOrb(currentGameMode);
                
                bool shouldActivate = false;
                
                if (canBuffer)
                {
                    // Bufferable modes: can hold X before hitting orb
                    if (xPressed && !orbHoldConsumedKeyStillDown[currplayer] && !orbHoldSuppressing[currplayer])
                    {
                        shouldActivate = true;
                        orbBufferActive[currplayer] = true;
                    }
                    else if (xHeld && orbBufferActive[currplayer] && !orbHoldSuppressing[currplayer])
                    {
                        shouldActivate = true;
                    }
                    else if (xHeld && !orbBufferActive[currplayer] && !orbHoldSuppressing[currplayer] && !orbHoldConsumedKeyStillDown[currplayer])
                    {
                        orbBufferActive[currplayer] = true;
                        shouldActivate = true;
                    }
                }
                else
                {
                    // Non-bufferable modes: require fresh X press
                    if (xPressed)
                    {
                        shouldActivate = true;
                    }
                }
                
                if (!shouldActivate) continue;
                
                // Mark as activated (but not in dual mode - each player can activate independently)
                if (!dual)
                {
                    orbActivated[idx] = true;
                }
                orbHoldConsumedKeyStillDown[currplayer] = true;
                
                // Consume the X press if it was used
                if (xPressed)
                    Interlocked.Exchange(ref keyXPressedCount, 0);

                // Handle gravity dash orbs (flip gravity first)
                bool isGravityDash = spriteType == DASH_GRAVITY_ORB ||
                                    spriteType == DASH_GRAVITY_ORB_45DEG_UP ||
                                    spriteType == DASH_GRAVITY_ORB_45DEG_DOWN ||
                                    spriteType == DASH_GRAVITY_ORB_UPWARDS ||
                                    spriteType == DASH_GRAVITY_ORB_DOWNWARDS;

                if (isGravityDash && dashing[currplayer] == 0)
                {
                    // Flip gravity (common_dash_orb_routine)
                    gravityFlipped = !gravityFlipped;
                    gravityReversed = !gravityReversed;
                    currplayer_gravity = (byte)(gravityReversed ? 0xFF : 0x00);
                    UpdateEffectiveGravity();
                    AppendSimDebug($"[DASH_ORB] Gravity flipped to {(gravityFlipped ? "UP" : "DOWN")} by orb 0x{spriteType:X2}");
                }

                // Set dash state and velocity based on orb type
                if (spriteType == DASH_ORB || spriteType == DASH_GRAVITY_ORB)
                {
                    // Horizontal dash (right)
                    velocityY = 0;
                    dashing[currplayer] = 1;
                    AppendSimDebug($"[DASH_ORB] Horizontal dash activated (0x{spriteType:X2})");
                }
                else if (spriteType == DASH_ORB_45DEG_UP || spriteType == DASH_GRAVITY_ORB_45DEG_UP)
                {
                    // 45 degree upward dash
                    velocityY = -velocityX;  // currplayer_vel_y = -currplayer_vel_x
                    dashing[currplayer] = 2;
                    AppendSimDebug($"[DASH_ORB] 45deg upward dash activated (0x{spriteType:X2}), vely={velocityY}");
                }
                else if (spriteType == DASH_ORB_45DEG_DOWN || spriteType == DASH_GRAVITY_ORB_45DEG_DOWN)
                {
                    // 45 degree downward dash
                    velocityY = velocityX;  // currplayer_vel_y = currplayer_vel_x
                    dashing[currplayer] = 3;
                    AppendSimDebug($"[DASH_ORB] 45deg downward dash activated (0x{spriteType:X2}), vely={velocityY}");
                }
                else if (spriteType == DASH_ORB_UPWARDS || spriteType == DASH_GRAVITY_ORB_UPWARDS)
                {
                    // Upward dash (vertical)
                    velocityY = velocityX * 4;  // currplayer_vel_y = currplayer_vel_x * 4
                    dashing[currplayer] = 4;
                    AppendSimDebug($"[DASH_ORB] Upward dash activated (0x{spriteType:X2}), vely={velocityY}");
                }
                else if (spriteType == DASH_ORB_DOWNWARDS || spriteType == DASH_GRAVITY_ORB_DOWNWARDS)
                {
                    // Downward dash (vertical)
                    velocityY = -velocityX * 4;  // currplayer_vel_y = -currplayer_vel_x * 4
                    dashing[currplayer] = 5;
                    AppendSimDebug($"[DASH_ORB] Downward dash activated (0x{spriteType:X2}), vely={velocityY}");
                }

                // Only activate one dash orb per frame
                break;
            }
        }
    }
}




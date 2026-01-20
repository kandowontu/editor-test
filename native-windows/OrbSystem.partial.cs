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
            orbBufferActive = false;
            orbHoldConsumedKeyStillDown = false;
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
                orbHoldSuppressing = true;
            }
            else if (!xHeld)
            {
                keyXHeldStartedOnGround = false;
                orbHoldSuppressing = false;
            }
            else if (!isGrounded && keyXHeldStartedOnGround)
            {
                // Still in air from ground jump, suppress orbs
                orbHoldSuppressing = true;
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
                
                // Check collision with player using same method as pads/portals
                if (!CheckOrbCollision(idx, spriteType, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    continue;
                    
                // Check if already activated (except multi orbs)
                bool isMultiOrb = (spriteType == BLUE_ORB_MULTI || spriteType == GREEN_ORB_MULTI);
                if (!isMultiOrb && orbActivated.ContainsKey(idx) && orbActivated[idx])
                    continue;
                    
                // Handle activation based on gamemode and input
                bool shouldActivate = false;
                
                if (canBuffer)
                {
                    // Bufferable modes: can hold X before hitting orb
                    // Set buffer when X is first pressed (not from ground)
                    if (xPressed && !orbHoldConsumedKeyStillDown && !orbHoldSuppressing)
                    {
                        shouldActivate = true;
                        orbBufferActive = true;
                    }
                    // While X is held and buffer is active, continue activating orbs
                    else if (xHeld && orbBufferActive && !orbHoldSuppressing)
                    {
                        shouldActivate = true;
                        // Keep buffer active - will be cleared on activation or release
                    }
                    // If X is held but buffer not active yet, set it
                    else if (xHeld && !orbBufferActive && !orbHoldSuppressing && !orbHoldConsumedKeyStillDown)
                    {
                        orbBufferActive = true;
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
                    
                    // Mark as activated
                    if (!isMultiOrb)
                    {
                        orbActivated[idx] = true;
                    }
                    
                    orbActivatedThisFrame = true;
                    activatedOrbType = spriteType;
                    orbHoldConsumedKeyStillDown = true;
                    // Don't clear orbBufferActive here - keep it active while X is held
                    // The physics mode will clear it on release or activation
                    
                    // Only one orb per frame
                    return (true, activatedOrbType);
                }
                else
                {
                    try { AppendSimDebug($"[ORB] Orb NOT activated (collision but no input): xPressed={xPressed}, xHeld={xHeld}, canBuffer={canBuffer}, orbBufferActive={orbBufferActive}, orbHoldSuppressing={orbHoldSuppressing}, orbHoldConsumedKeyStillDown={orbHoldConsumedKeyStillDown}, alreadyActivated={orbActivated[idx]}"); } catch { }
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
                   spriteType == BLUE_ORB_MULTI ||
                   spriteType == GREEN_ORB_MULTI;
        }
        
        /// <summary>
        /// Activate an orb and apply its effect
        /// </summary>
        private void ActivateOrb(int orbType, int gamemode, bool gravityInverted, bool mini, ref int velocityY)
        {
            // Clamp gamemode (ninja uses cube values)
            int modeCol = gamemode;
            if (modeCol == 8) modeCol = 0; // Ninja uses cube values
            if (modeCol < 0 || modeCol >= 8) return;
            
            switch (orbType)
            {
                case BLUE_ORB:
                case BLUE_ORB_MULTI:
                    // Blue orb: reverse gravity then apply velocity
                    gravityInverted = !gravityInverted;
                    currplayer_gravity = (byte)(gravityInverted ? 1 : 0);
                    gravityReversed = gravityInverted;
                    gravityFlipped = gravityInverted;
                    UpdatePlayerIconFlip();
                    
                    // Set velocity based on gamemode and mini state
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
                    
                case GREEN_ORB:
                case GREEN_ORB_MULTI:
                    // Green orb: reverse gravity then apply yellow orb velocity
                    gravityInverted = !gravityInverted;
                    currplayer_gravity = (byte)(gravityInverted ? 1 : 0);
                    gravityReversed = gravityInverted;
                    gravityFlipped = gravityInverted;
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
            orbBufferActive = false;
            orbHoldConsumedKeyStillDown = false;
            orbHoldSuppressing = false;
            
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
    }
}

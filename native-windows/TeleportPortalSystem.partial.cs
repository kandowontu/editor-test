using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // Teleport portal sprite IDs
        // Vertical portals (3 tiles tall, exit is middle tile)
        private const byte TELEPORT_PORTAL_VERTICAL_ENTER = 0x4E;  // Vertical entrance portal
        private const byte TELEPORT_PORTAL_VERTICAL_EXIT = 0x4F;   // Vertical exit portal
        
        // Horizontal portals (exit Y position based on anchor placement)
        private const byte TELEPORT_PORTAL_HORIZONTAL_ENTER_1 = 0x66;  // Horizontal entrance portal 1
        private const byte TELEPORT_PORTAL_HORIZONTAL_EXIT_BOTTOM = 0x67;  // Horizontal exit portal (bottom row)
        private const byte TELEPORT_PORTAL_HORIZONTAL_ENTER_2 = 0x68;  // Horizontal entrance portal 2
        private const byte TELEPORT_PORTAL_HORIZONTAL_EXIT_TOP = 0x69;  // Horizontal exit portal (top row)
        
        // Invisible horizontal portals (same behavior as 0x66-0x69, but invisible in simulator)
        private const byte TELEPORT_PORTAL_INVISIBLE_ENTER_1 = 0x75;  // Invisible entrance portal 1
        private const byte TELEPORT_PORTAL_INVISIBLE_EXIT_BOTTOM = 0x76;  // Invisible exit portal (bottom row)
        private const byte TELEPORT_PORTAL_INVISIBLE_ENTER_2 = 0x77;  // Invisible entrance portal 2
        private const byte TELEPORT_PORTAL_INVISIBLE_EXIT_TOP = 0x78;  // Invisible exit portal (top row)

        /// <summary>
        /// Check for teleport portal collision and activate if touched
        /// 
        /// VERTICAL PORTALS:
        /// - Entrance: 0x4E (3 tiles tall, collision-based)
        /// - Exit: 0x4F (3 tiles tall, exit point is MIDDLE tile)
        /// 
        /// HORIZONTAL PORTALS:
        /// - Entrance: 0x66, 0x68 (collision-based)
        /// - Exit: 0x67 (bottom row of anchor), 0x69 (top row of anchor)
        /// 
        /// Only exits on visible screen are active (last visible wins)
        /// </summary>
        private void CheckTeleportPortals()
        {
            try
            {
                int playerX_px = playerX_fixed >> 8;
                int playerY_px = playerY_fixed >> 8;

                // Use actual collision hitbox size, not visual size
                int hitboxW = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH = (currplayer_mini != 0) ? 7 : 15;

                // Apply mini mode offset matching terrain collision conventions
                playerY_px += GetMiniSpriteOffsetY();

                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;

                AppendSimDebug($"[TELEPORT_PORTAL] Checking collision: player ({playerLeft_px},{playerTop_px})-({playerRight_px},{playerBottom_px}) size={hitboxW}x{hitboxH}");

                // Iterate through ALL sprites and check for entrance portals
                for (int _si = 0; _si < nonEmptySpriteIndices.Length; _si++)
                {
                    int idx = nonEmptySpriteIndices[_si]; int sid = sprites[idx];
                    
                    // Check if this is any type of entrance portal
                    bool isVerticalEnter = (sid == TELEPORT_PORTAL_VERTICAL_ENTER);
                    bool isHorizontalEnter = (sid == TELEPORT_PORTAL_HORIZONTAL_ENTER_1 || sid == TELEPORT_PORTAL_HORIZONTAL_ENTER_2 ||
                                              sid == TELEPORT_PORTAL_INVISIBLE_ENTER_1 || sid == TELEPORT_PORTAL_INVISIBLE_ENTER_2);
                    
                    if (!isVerticalEnter && !isHorizontalEnter) continue;

                    // Check if already activated this portal
                    if (processedTeleportPortals.Contains(idx)) continue;

                    // Check if player intersects this entrance portal
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        AppendSimDebug($"[TELEPORT_PORTAL] Colliding with entrance portal 0x{sid:X2} at idx={idx}");

                        // Found entrance portal - now find corresponding exit portal on visible screen
                        bool foundExit = false;
                        int exitY_px = 0;

                        // Calculate visible screen X bounds
                        // NES stores teleport_output when exit sprite is loaded (column-based),
                        // so only X visibility matters — no Y restriction needed.
                        int cameraLeft_px = cameraX_fixed >> 8;
                        int cameraRight_px = cameraLeft_px + (NES_W * TILE);

                        // Search for exit portal on visible screen
                        // For vertical: look for 0x4F
                        // For horizontal: look for 0x67 or 0x69
                        // If multiple exits are visible, the last one loaded will be used
                        for (int exitIdx = 0; exitIdx < sprites.Length; exitIdx++)
                        {
                            int exitSid = sprites[exitIdx];
                            
                            // Check if this is a matching exit portal
                            bool isValidExit = false;
                            bool isBottomRowExit = false;  // For 0x67 (bottom row)
                            bool isTopRowExit = false;     // For 0x69 (top row)
                            
                            if (isVerticalEnter && exitSid == TELEPORT_PORTAL_VERTICAL_EXIT)
                            {
                                isValidExit = true;
                            }
                            else if (isHorizontalEnter)
                            {
                                if (exitSid == TELEPORT_PORTAL_HORIZONTAL_EXIT_BOTTOM || exitSid == TELEPORT_PORTAL_INVISIBLE_EXIT_BOTTOM)
                                {
                                    isValidExit = true;
                                    isBottomRowExit = true;
                                }
                                else if (exitSid == TELEPORT_PORTAL_HORIZONTAL_EXIT_TOP || exitSid == TELEPORT_PORTAL_INVISIBLE_EXIT_TOP)
                                {
                                    isValidExit = true;
                                    isTopRowExit = true;
                                }
                            }
                            
                            if (!isValidExit) continue;

                            // Get exit portal world position
                            int exitTileX = exitIdx % mapWidth;
                            int exitTileY = exitIdx / mapWidth;
                            int exitWorldX_px = exitTileX * TILE;
                            int exitWorldY_px = exitTileY * TILE;

                            // Apply sprite anchoring/offset if present
                            if (spriteAnchors != null && spriteAnchors.TryGetValue(exitIdx, out var anchor))
                            {
                                exitWorldX_px = anchor.anchorTileX * TILE;
                                exitWorldY_px = anchor.anchorTileY * TILE;
                            }

                            // Apply pixel offsets if present
                            if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(exitIdx, out var offset))
                            {
                                exitWorldY_px += offset.offsetY;
                            }

                            // Check if this exit portal is on the visible screen (X only, matching NES column-based sprite loading)
                            if (exitWorldX_px >= cameraLeft_px && exitWorldX_px < cameraRight_px)
                            {
                                // This exit is visible - calculate Y position based on portal type
                                if (isVerticalEnter)
                                {
                                    // Vertical portals are 3 tiles tall, exit point is the MIDDLE tile
                                    // The sprite position is the top tile, so add 1 tile (16 pixels) for middle
                                    exitY_px = exitWorldY_px + TILE;
                                }
                                else if (isBottomRowExit)
                                {
                                    // 0x67: Bottom row of tiles where anchor is placed
                                    // Anchor position is already the desired Y
                                    exitY_px = exitWorldY_px;
                                }
                                else if (isTopRowExit)
                                {
                                    // 0x69: Top row of tiles where anchor is placed
                                    // Anchor position is already the desired Y
                                    exitY_px = exitWorldY_px;
                                }
                                
                                foundExit = true;
                                AppendSimDebug($"[TELEPORT_PORTAL] Found visible exit 0x{exitSid:X2} at ({exitWorldX_px}, {exitWorldY_px}), exit Y={exitY_px}");
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

                            AppendSimDebug($"[TELEPORT_PORTAL] Teleported to Y={exitY_px} (camera Y={cameraY_fixed >> 8})");

                            // Mark this entrance portal as processed
                            processedTeleportPortals.Add(idx);
                        }
                        else
                        {
                            AppendSimDebug($"[TELEPORT_PORTAL] WARNING: No exit portal visible on screen!");
                        }

                        // Only process one entrance portal per frame
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[TELEPORT_PORTAL] ERROR: {ex.Message}");
            }
        }

        // Track which teleport portals have been processed to prevent re-activation
        private System.Collections.Generic.HashSet<int> processedTeleportPortals = new System.Collections.Generic.HashSet<int>();
    }
}

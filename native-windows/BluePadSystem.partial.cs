using System;
using System.Collections.Generic;

namespace FamidashEditor
{
    /// <summary>
    /// Blue pad system implementation for the simulator.
    /// Blue pads reverse/normalize gravity and apply velocity.
    /// Bottom pads activate when gravity is normal and reverse it.
    /// Top pads activate when gravity is reversed and normalize it.
    /// Uses inline AABB matching PF's ProcessSprites exactly (no SpriteIntersectsPlayer).
    /// </summary>
    public partial class SimulatorWindow
    {
        // Blue pad sprite constants
        private const byte BOTTOM_BLUE_PAD = 0x0D;
        private const byte TOP_BLUE_PAD = 0x0E;
        private const byte BOTTOM_BLUE_PAD_MULTI = 0xFD;
        private const byte TOP_BLUE_PAD_MULTI = 0xFE;

        /// <summary>
        /// Check for blue pad collision and apply gravity change + velocity.
        /// Uses inline AABB computation matching PF's ProcessSprites exactly:
        /// - NO SpriteIntersectsPlayer (avoids hh>=0xFC skip and bitmap override)
        /// - Uses anchor-overridden id_for_geom for hitbox table lookup
        /// - Pads fire every overlapping frame (no activation tracking), matching PF
        /// </summary>
        private void CheckBluePadCollision()
        {
            try
            {
                int playerX_px = playerX_fixed >> 8;
                int playerY_px = playerY_fixed >> 8;

                bool isMini = (currplayer_mini != 0);
                bool gravityInverted = (currplayer_gravity != 0);
                int hitboxW = SharedPhysics.GetCubeHitboxW(isMini);
                int hitboxH = SharedPhysics.GetCubeHitboxH(isMini);

                // Apply mini mode offset matching PF's GetHitboxOffsetY
                playerY_px += GetMiniSpriteOffsetY();

                // NES sprite_collide() sets Generic.x = high_byte(currplayer_x) + 1
                // ONCE for the whole pass -- applies to blue pads too. (Legacy SIM
                // had a separate path with no +1; that was wrong vs NES.)
                int padLeft = playerX_px + 1;
                int padRight = padLeft + hitboxW;        // exclusive (matches PF)
                int playerTop = playerY_px;
                int playerBottom = playerTop + hitboxH;  // exclusive (matches PF)

                // Ground row adjustment matching PF's groundRowsToReserve
                int groundRowsLocal = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;

                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;

                    bool isBottomPad = (sid == BOTTOM_BLUE_PAD || sid == BOTTOM_BLUE_PAD_MULTI);
                    bool isTopPad = (sid == TOP_BLUE_PAD || sid == TOP_BLUE_PAD_MULTI);

                    if (!isBottomPad && !isTopPad)
                        continue;

                    // Gravity gate is checked AFTER overlap so we still mark
                    // gate-mismatched but overlapping pads as activated --
                    // NES `spcl_gvdn_pd` / `spcl_gvup_pd` (sprite_loading.h)
                    // unconditionally `idx8_inc(activesprites_activated, index)`
                    // on collision, even when the gate skips the flip.
                    // Without this, two stacked blue pads (e.g. dreamer.tmx
                    // idx 23976 0xFD + 23977 0xFE) both overlap the player
                    // on the same frame: the first pad flips gravity, then
                    // on the next frame the second pad's gate now matches
                    // and it fires a spurious second flip.

                    // Already activated check (non-dual mode only)
                    if (!dual && orbActivated.TryGetValue(idx, out var alreadyActivated) && alreadyActivated)
                        continue;

                    // Inline AABB matching PF's ProcessSprites:
                    // Use anchor-overridden id_for_geom for hitbox lookup (same as PF)
                    bool useRawNesRecord = IsSimulatorNesRawDispatchIndex(idx);
                    int id_for_geom = sid & 0xFF;
                    int anchorKey = -1;
                    if (!useRawNesRecord &&
                        spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                    {
                        anchorKey = anchor.anchorTileY * mapWidth + anchor.anchorTileX;
                        if (anchorKey >= 0 && anchorKey < sprites.Length)
                        {
                            int anchoredId = sprites[anchorKey];
                            if (anchoredId >= 0 && anchoredId < 256) id_for_geom = anchoredId & 0xFF;
                        }
                    }

                    // Use the shared exact NES runtime geometry.
                    int hw = (id_for_geom >= 0 && id_for_geom < SharedPhysics.sprite_widths.Length) ? SharedPhysics.sprite_widths[id_for_geom] : TILE;
                    int hh = (id_for_geom >= 0 && id_for_geom < SharedPhysics.sprite_heights.Length) ? SharedPhysics.sprite_heights[id_for_geom] : TILE;
                    // NO hh >= 0xFC skip — PF doesn't skip sentinels for pad detection
                    int hxoff = (id_for_geom >= 0 && id_for_geom < SharedPhysics.sprite_x_offset.Length) ? SharedPhysics.sprite_x_offset[id_for_geom] : 0;
                    int hyoff = (id_for_geom >= 0 && id_for_geom < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[id_for_geom] : 0;

                    // Per-position pixel offset (matching PF's spritePixelOffsets lookup)
                    int pxOff = 0, pyOff = 0;
                    if (!useRawNesRecord &&
                        anchorKey >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var aoffs))
                    {
                        pxOff = aoffs.offsetX; pyOff = aoffs.offsetY;
                    }
                    else if (!useRawNesRecord &&
                             spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs))
                    {
                        pxOff = offs.offsetX; pyOff = offs.offsetY;
                    }

                    int storageTileX = idx % mapWidth;
                    int storageTileY = idx / mapWidth;
                    // NO bitmap override — PF uses table-based hitbox w/h only
                    int spriteLeft = (useRawNesRecord
                        ? simulatorNesSpriteWorldX[idx]
                        : storageTileX * TILE + pxOff) + hxoff;
                    int spriteTop = (useRawNesRecord
                        ? SimulatorNesDispatchWorldY()
                        : (storageTileY - groundRowsLocal) * TILE + pyOff) + hyoff - 1;
                    int spriteRight = spriteLeft + Math.Max(1, hw);
                    int spriteBottom = spriteTop + Math.Max(1, hh);

                    bool xOverlap;
                    bool yOverlap;
                    if (useRawNesRecord)
                    {
                        int scrollX_px = Math.Max(0, (playerX_fixed >> 8) - 0x50);
                        int playerLeft_screen_px = padLeft - scrollX_px;
                        int playerTop_screen_px = playerTop - (cameraY_fixed >> 8);
                        spriteLeft = SimulatorNesSaturatingOffset(
                            SimulatorNesDispatchRealX(), hxoff);
                        spriteTop = SimulatorNesSaturatingOffset(
                            SimulatorNesDispatchRealY(), hyoff);
                        spriteRight = spriteLeft + Math.Max(1, hw);
                        spriteBottom = spriteTop + Math.Max(1, hh);
                        xOverlap = SimulatorNesAxisOverlaps(
                            playerLeft_screen_px, hitboxW, spriteLeft, hw);
                        yOverlap = SimulatorNesAxisOverlaps(
                            playerTop_screen_px, hitboxH, spriteTop, hh);
                        padLeft = playerLeft_screen_px;
                        padRight = padLeft + hitboxW;
                        playerTop = playerTop_screen_px;
                        playerBottom = playerTop + hitboxH;
                    }
                    else
                    {
                        // Overlap check matching PF's ProcessSprites exactly
                        xOverlap = !(padRight < spriteLeft || spriteRight < padLeft);
                        yOverlap = !(playerBottom < spriteTop || spriteBottom < playerTop);
                    }

                    // DEBUG: trace blue pad collision math in critical X range
                    if (isBottomPad && padLeft >= 4500 && padLeft <= 4560)
                        AppendSimDebug($"[BPAD_DBG] idx={idx} sid=0x{sid:X2} pad=({padLeft},{playerTop})-({padRight},{playerBottom}) spr=({spriteLeft},{spriteTop})-({spriteRight},{spriteBottom}) xO={xOverlap} yO={yOverlap} tX={storageTileX} tY={storageTileY} grR={groundRowsLocal} hw={hw} hh={hh} hxo={hxoff} hyo={hyoff} pxO={pxOff} pyO={pyOff} gInv={gravityInverted}");

					if (xOverlap && yOverlap)
					{
						// Both NES blue-pad handlers call clear_slope_stuff()
						// before their gravity gate.  Even an already-correct
						// gravity contact cancels pending slope state.
						ClearSlopeStuff();

						// NES marks pad activated on any collision, even
						// when gate mismatch skips the flip.
						bool gateOk = !((isBottomPad && gravityInverted) || (isTopPad && !gravityInverted));

						if (gateOk)
						{
							gravityInverted = !gravityInverted;
                            gravityFlipped = gravityInverted;
                            gravityReversed = gravityInverted;

                            try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }

                            int baseVel = isMini ? PAD_HEIGHT_BLUE_mini : PAD_HEIGHT_BLUE_normal;
                            int newVel = gravityInverted ? baseVel : -baseVel;

                            playerVelY_fixed = newVel;
                            orbhitonthisframe[currplayer] = true;
                        }
                        if (!dual)
                            orbActivated[idx] = true;
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Reset blue pad activation state when starting/restarting level
        /// </summary>
        private void ResetBluePadSystem()
        {
            // No per-pad tracking needed — pads fire every overlapping frame (matching PF/NES)
        }
    }
}



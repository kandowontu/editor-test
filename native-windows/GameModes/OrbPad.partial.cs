using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // Handles numeric-path orb and pad activations (post-integration).
        // Expects to be called from the numeric sim with local copies available.
        private void OrbPad_HandleNumericActivations(int pendingPresses_num, int pendingPressStartedOnGround, bool keyXHeld_local, ref bool jumpAppliedThisStep_local, int pendingPresses_forLater)
        {
            try
            {
                // Yellow-orb numeric activation: one-shot orbs (sprite 0x0B)
                try
                {
                    const int ORB_HIT_W = 14; const int ORB_HIT_H = 14;
                    int playerCenter_px_orb = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                    int playerLeft_px_orb = playerCenter_px_orb - (ORB_HIT_W / 2);
                    int playerRight_px_orb = playerLeft_px_orb + (ORB_HIT_W - 1);
                    int playerTop_px_orb = (playerY_fixed >> 8);
                    int playerBottom_px_orb = playerTop_px_orb + (ORB_HIT_H - 1);

                    var candidates = new System.Collections.Generic.List<(int idx, int sid, int orbRow, bool isBlueOrb, bool isGreenOrb, int tileCenterX_px)>();
                    int playerCenter_px_orb_local = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                    for (int idx = 0; idx < sprites.Length; idx++)
                    {
                        int sid = sprites[idx];
                        if (sid < 0) continue;
                        int orbRow = -1;
                        bool isBlueOrb = false;
                        bool isGreenOrb = false;
                        if (sid == 0x0B) orbRow = 0; // yellow orb (original)
                        else if (sid == 0x06) orbRow = 2; // pink orb
                        else if (sid == 0x28) orbRow = 4; // red orb
                        else if (sid == 0x1F) orbRow = 5; // yellow orb bigger
                        else if (sid == 0x44) orbRow = 6; // black orb
                        else if (sid == 0x29) orbRow = 7; // yellow orb smaller
                        else if (sid == 0x27) { orbRow = 0; isGreenOrb = true; }
                        else if (sid == 0x05 || sid == 0x7B) { isBlueOrb = true; }
                        if (orbRow < 0 && !isBlueOrb) continue;
                        if (sid != 0x7B && processedOrbs.Contains(idx)) continue;
                        if (!SpriteIntersectsPlayer(idx, sid, playerLeft_px_orb, playerRight_px_orb, playerTop_px_orb, playerBottom_px_orb)) continue;

                        int tileX = idx % mapWidth;
                        int tileCenterX_px = tileX * TILE + (TILE / 2);
                        candidates.Add((idx, sid, orbRow, isBlueOrb, isGreenOrb, tileCenterX_px));
                    }

                    if (candidates.Count > 0 && !orbActivationConsumedThisPress)
                    {
                        var best = candidates[0];
                        int bestDist = Math.Abs(playerCenter_px_orb_local - best.tileCenterX_px);
                        for (int i = 1; i < candidates.Count; i++)
                        {
                            var c = candidates[i];
                            int d = Math.Abs(playerCenter_px_orb_local - c.tileCenterX_px);
                            if (d < bestDist) { best = c; bestDist = d; }
                        }

                        bool freshPressAvailable = (pendingPresses_num > 0) && (pendingPressStartedOnGround == 0) && !jumpAppliedThisStep_local && !(currentGameMode == 0 && pendingPresses_forLater > 0) && !orbHoldSuppressing;
                        bool holdAvailable = orbBufferActive && (pendingPresses_num == 0) && !jumpAppliedThisStep_local && !orbHoldConsumed;
                        bool activated = false;
                        bool usedHold = false;
                        if (currentGameMode == 1 || currentGameMode == 3)
                        {
                            if (freshPressAvailable) { activated = true; usedHold = false; }
                        }
                        else
                        {
                            if (freshPressAvailable) { activated = true; usedHold = false; }
                            else if (holdAvailable) { activated = true; usedHold = true; }
                        }

                        if (activated)
                        {
                            try { Interlocked.Exchange(ref keyXPressedCount, 0); } catch { }
                            orbBufferActive = false;
                            if (usedHold)
                            {
                                orbHoldConsumed = true;
                                orbHoldConsumedKeyStillDown = (keyXHeld_local || IsXDownAsync());
                                orbHoldSuppressing = true;
                            }

                            int idx = best.idx; int sid = best.sid; int orbRow = best.orbRow; bool isBlueOrb = best.isBlueOrb; bool isGreenOrb = best.isGreenOrb;
                            if (isBlueOrb)
                            {
                                try
                                {
                                    int mag = (currentGameMode == 2) ? 0x01F3 : 0x04FB;
                                    bool numericInvert_local = gravityReversed || effectiveInvertedByW;
                                    if (numericInvert_local)
                                    {
                                        playerVelY_fixed = mag;
                                        try { gravityReversed = false; effectiveInvertedByW = gravityReversed; } catch { }
                                    }
                                    else
                                    {
                                        playerVelY_fixed = -mag;
                                        try { gravityReversed = true; effectiveInvertedByW = gravityReversed; } catch { }
                                    }
                                    try { UpdateEffectiveGravity(); } catch { }
                                    try { UpdatePlayerImageForMode(); } catch { }
                                }
                                catch { }
                            }
                            else
                            {
                                if (isGreenOrb)
                                {
                                    try { gravityReversed = !gravityReversed; effectiveInvertedByW = gravityReversed; } catch { }
                                    try { UpdateEffectiveGravity(); } catch { }
                                    try { UpdatePlayerImageForMode(); } catch { }
                                }
                                int vel = 0;
                                if (PadOrbHeights.Length > orbRow && PadOrbHeights[orbRow].Length > currentGameMode && currentGameMode >= 0)
                                    vel = PadOrbHeights[orbRow][currentGameMode];
                                else
                                    vel = PadOrbHeights[0][0];
                                bool numericInvert = gravityReversed || effectiveInvertedByW;
                                if (!numericInvert) vel = -vel;
                                playerVelY_fixed = vel;
                            }
                            physicsEnabled = true;
                            onGround = false;
                            if (!isBlueOrb || sid == 0x05) processedOrbs.Add(idx);
                            try
                            {
                                foreach (var c in candidates)
                                {
                                    if (c.idx == idx) continue;
                                    if (!c.isBlueOrb || c.sid == 0x05) processedOrbs.Add(c.idx);
                                }
                            }
                            catch { }
                            orbActivationConsumedThisPress = true;
                        }
                    }
                }
                catch { }

                // Clear orb buffer when X released and no pending presses.
                try
                {
                    if (!(keyXHeld_local || pendingPresses_num > 0))
                    {
                        orbBufferActive = false;
                        orbHoldConsumed = false;
                        orbHoldConsumedKeyStillDown = false;
                        orbActivationConsumedThisPress = false;
                    }
                }
                catch { }

                // Pad/orb numeric activation (post-integration): ensure pads apply during sim
                try
                {
                    const int PAD_HIT_W_NUM2 = 14; const int PAD_HIT_H_NUM2 = 14;
                    int playerCenter_px_pad2 = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                    int playerLeft_px_pad2 = playerCenter_px_pad2 - (PAD_HIT_W_NUM2 / 2);
                    int playerRight_px_pad2 = playerLeft_px_pad2 + (PAD_HIT_W_NUM2 - 1);
                    int playerTop_px_pad2 = (playerY_fixed >> 8);
                    int playerBottom_px_pad2 = playerTop_px_pad2 + (PAD_HIT_H_NUM2 - 1);

                    for (int idx = 0; idx < sprites.Length; idx++)
                    {
                        int sid = sprites[idx];
                        if (sid < 0) continue;
                        if (sid == 0x0D || sid == 0xFD)
                        {
                            if (!SpriteIntersectsPlayer(idx, sid, playerLeft_px_pad2, playerRight_px_pad2, playerTop_px_pad2, playerBottom_px_pad2)) continue;
                            if (!gravityReversed)
                            {
                                try { playerVelY_fixed = -0x04FB; } catch { playerVelY_fixed = -0x04FB; }
                                physicsEnabled = true;
                                onGround = false;
                                try { gravityReversed = true; effectiveInvertedByW = gravityReversed; } catch { }
                                try { UpdateEffectiveGravity(); } catch { }
                                try { UpdatePlayerImageForMode(); } catch { }
                            }
                            break;
                        }
                        else if (sid == 0x0E || sid == 0xFE)
                        {
                            if (!SpriteIntersectsPlayer(idx, sid, playerLeft_px_pad2, playerRight_px_pad2, playerTop_px_pad2, playerBottom_px_pad2)) continue;
                            if (gravityReversed)
                            {
                                try { playerVelY_fixed = 0x04FB; } catch { playerVelY_fixed = 0x04FB; }
                                physicsEnabled = true;
                                onGround = false;
                                try { gravityReversed = false; effectiveInvertedByW = gravityReversed; } catch { }
                                try { UpdateEffectiveGravity(); } catch { }
                                try { UpdatePlayerImageForMode(); } catch { }
                            }
                            break;
                        }
                        int padRow2 = -1;
                        if (sid == 0x0A || sid == 0x0C) padRow2 = 1; // yellow pad
                        else if (sid == 0x25 || sid == 0x26) padRow2 = 3; // pink pad
                        else if (sid == 0x52 || sid == 0x53) padRow2 = 8; // red pad
                        if (padRow2 < 0) continue;
                        if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_pad2, playerRight_px_pad2, playerTop_px_pad2, playerBottom_px_pad2))
                        {
                            try
                            {
                                int vel = 0;
                                if (PadOrbHeights.Length > padRow2 && PadOrbHeights[padRow2].Length > currentGameMode && currentGameMode >= 0)
                                    vel = PadOrbHeights[padRow2][currentGameMode];
                                else
                                    vel = PadOrbHeights[1][0];
                                bool numericInvert = gravityReversed || effectiveInvertedByW;
                                if (!numericInvert) vel = -vel;
                                try { AppendSimDebug($"PadHit(num) idx={idx} sid=0x{sid:X2} row={padRow2} gm={currentGameMode} vel=0x{vel:X} numericInvert={numericInvert} playerY_fixed=0x{playerY_fixed:X}"); } catch { }
                                playerVelY_fixed = vel;
                                physicsEnabled = true;
                                onGround = false;
                            }
                            catch { }
                            break;
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        // UI-path pad handling: run in UI or non-numeric sim path to apply pads
        private void OrbPad_HandleUIPads()
        {
            try
            {
                const int PAD_HIT_W = 14; const int PAD_HIT_H = 14;
                int playerCenter_px_pad = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerLeft_px_pad = playerCenter_px_pad - (PAD_HIT_W / 2);
                int playerRight_px_pad = playerLeft_px_pad + (PAD_HIT_W - 1);
                int playerTop_px_pad = (playerY_fixed >> 8);
                int playerBottom_px_pad = playerTop_px_pad + (PAD_HIT_H - 1);

                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;
                    // Support multiple pad kinds: yellow (0x0A/0x0C), pink (0x25/0x26), red (0x52/0x53)
                    int padRow = -1;
                    if (sid == 0x0A || sid == 0x0C) padRow = 1; // yellow pad row
                    else if (sid == 0x25 || sid == 0x26) padRow = 3; // pink pad row
                    else if (sid == 0x52 || sid == 0x53) padRow = 8; // red pad row
                    if (padRow < 0) continue;

                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_pad, playerRight_px_pad, playerTop_px_pad, playerBottom_px_pad))
                    {
                        try
                        {
                            int vel = 0;
                            if (PadOrbHeights.Length > padRow && PadOrbHeights[padRow].Length > currentGameMode && currentGameMode >= 0)
                                vel = PadOrbHeights[padRow][currentGameMode];
                            else
                                vel = PadOrbHeights[1][0];
                            bool numericInvert = gravityReversed || effectiveInvertedByW;
                            if (!numericInvert) vel = -vel; // table authored for reverse gravity
                            try { AppendSimDebug($"PadHit idx={idx} sid=0x{sid:X2} row={padRow} gm={currentGameMode} vel=0x{vel:X} numericInvert={numericInvert} playerY_fixed=0x{playerY_fixed:X}"); } catch { }
                            playerVelY_fixed = vel;
                            physicsEnabled = true;
                            onGround = false;
                        }
                        catch { }
                        break; // only apply one pad per frame
                    }
                }
            }
            catch { }
        }
    }
}

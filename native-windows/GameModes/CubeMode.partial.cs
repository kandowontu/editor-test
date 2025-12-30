using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // Cube mode X-edge behavior: standard jump buffering and edge press handling.
        private void ProcessCubeXEdge()
        {
            try
            {
                Interlocked.Increment(ref keyXPressedCount);
                try { jumpBufferCounter = JUMP_BUFFER_FRAMES; } catch { }
                physicsEnabled = true;
                jumpedOnce = true;
                try
                {
                    int onG_local = onGround ? 1 : 0;
                    Interlocked.Exchange(ref keyXPressStartedOnGroundInt, onG_local);
                    Interlocked.Exchange(ref keyXHeldStartedOnGroundInt, onG_local);
                }
                catch { }
            }
            catch { }
        }

        // Convenience wrapper for legacy code paths that don't have numeric locals.
        // Reads shared counters/flags and forwards to the detailed handler.
        private void Cube_HandleCeilingCollision_NoLocals()
        {
            try
            {
                int jumpBuffered_local = 0;
                try { jumpBuffered_local = Interlocked.CompareExchange(ref jumpBufferCounter, 0, 0); } catch { jumpBuffered_local = 0; }
                int pendingKeyX_local = 0;
                try { pendingKeyX_local = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0); } catch { pendingKeyX_local = 0; }
                bool keyXHeld_local = false;
                try { keyXHeld_local = keyXHeld; } catch { keyXHeld_local = false; }
                int pendingPresses_num = 0;
                int pendingPressStartedOnGround = 0;
                int pendingPresses_forLater = 0;
                bool jumpAppliedThisStep_local = false;
                try { Cube_HandleCeilingCollision(jumpBuffered_local, pendingKeyX_local, keyXHeld_local, pendingPresses_num, pendingPressStartedOnGround, pendingPresses_forLater, ref jumpAppliedThisStep_local); } catch { }
            }
            catch { }
        }

        // Wrapper for UI landing path: reads shared counters and delegates to Cube_HandleUILanding.
        private void Cube_HandleUILanding_NoLocals()
        {
            try
            {
                int buffered_ui = 0;
                try { buffered_ui = Interlocked.CompareExchange(ref jumpBufferCounter, 0, 0); } catch { buffered_ui = 0; }
                int pendingPressesNow = 0;
                try { pendingPressesNow = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0); } catch { pendingPressesNow = 0; }
                bool keyXHeld_local = false;
                try { keyXHeld_local = keyXHeld; } catch { keyXHeld_local = false; }
                try { Cube_HandleUILanding(buffered_ui, pendingPressesNow, keyXHeld_local); } catch { }
            }
            catch { }
        }

        // Handle pending presses read by numeric sim: defers or applies cube jumps as needed.
        private void Cube_HandlePendingPresses_NoLocals(int pendingPresses_num, int pendingPressStartedOnGround, ref int pendingPresses_forLater, ref bool jumpAppliedThisStep_local)
        {
            try
            {
                pendingPresses_forLater = 0;
                if (pendingPresses_num <= 0) return;

                if (currentGameMode == 0)
                {
                    if (gravityReversed)
                    {
                        bool effectiveOnGround_local = onGround || groundStabilizeCounter > 0 || invertedCeilingHoldCounter > 0;
                        bool touchingCeiling_local = (gravityReversed || effectiveInvertedByW) && IsTouchingCeilingStrict();
                        if (effectiveOnGround_local || touchingCeiling_local)
                        {
                            try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                            physicsEnabled = true;
                            onGround = false;
                            jumpAppliedThisStep_local = true;
                            orbBufferActive = false;
                            jumpedOnce = true;
                        }
                    }
                    else
                    {
                        if (pendingPressStartedOnGround != 0)
                        {
                            pendingPresses_forLater = pendingPresses_num;
                        }
                        else
                        {
                            // started in air: do nothing here (fresh press handled elsewhere)
                        }
                    }
                }
                else if (currentGameMode == 3)
                {
                    try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                    physicsEnabled = true;
                    onGround = false;
                    jumpAppliedThisStep_local = true;
                    orbBufferActive = false;
                    jumpedOnce = true;
                }
            }
            catch { }
        }

        // Handles ceiling collision / ejection / pass-through semantics for Cube mode.
        private void Cube_HandleCeilingCollision(int jumpBuffered_local, int pendingKeyX_local, bool keyXHeld_local, int pendingPresses_num, int pendingPressStartedOnGround, int pendingPresses_forLater, ref bool jumpAppliedThisStep_local)
        {
            try
            {
                if (playerVelY_fixed < 0)
                {
                    const int HITBOX_W_LOCAL = 15;
                    int playerCenter_px_local = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                    int playerLeft_px_local = playerCenter_px_local - (HITBOX_W_LOCAL / 2);
                    int playerRight_px_local = playerLeft_px_local + (HITBOX_W_LOCAL - 1);
                    int headWorldY_px_local = (playerY_fixed >> 8);

                    int tileAboveY_world = headWorldY_px_local / TILE;
                    int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                    int tileIndexY = tileAboveY_world + groundRowsToReserve_local;

                    if (tileIndexY >= 0 && tileIndexY < mapHeight)
                    {
                        // Top-death check (cube mode, normal gravity only, center 2x2 pixels)
                        try
                        {
                            if (!MainWindow.Option_NoDeath && !deathTriggered && currentGameMode == 0 && !gravityReversed)
                            {
                                int playerCenterX = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                                int playerCenterY = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                                const int SAMPLE_Y_OFFSET = -2; // pixels (negative = up)
                                int sampledCenterY = playerCenterY + SAMPLE_Y_OFFSET;
                                int centerTileY = sampledCenterY / TILE;
                                int groundRowsToReserve_local2 = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                                int tileIndexY_center = centerTileY + groundRowsToReserve_local2;
                                if (tileIndexY_center >= 0 && tileIndexY_center < mapHeight)
                                {
                                    bool blocked_center = false;
                                    int[] dxs = new int[] { -1, 0 };
                                    int[] dys = new int[] { -1, 0 };
                                    foreach (var dx in dxs)
                                    {
                                        foreach (var dy in dys)
                                        {
                                            int px = playerCenterX + dx;
                                            int py = sampledCenterY + dy;
                                            int tx_local = px / TILE;
                                            if (tx_local < 0 || tx_local >= mapWidth) continue;
                                            int tid_local = tiles[tileIndexY_center * mapWidth + tx_local];
                                            int useTidForAnim_local = MapAnimatedTileIndex(tid_local);
                                            int collisionTid_local = useTidForAnim_local;
                                            if (useTidForAnim_local >= 1000)
                                            {
                                                if (useTidForAnim_local >= 1000 && useTidForAnim_local <= 1007)
                                                {
                                                    collisionTid_local = 0x08 + ((useTidForAnim_local - 1000) % 4);
                                                }
                                                else if (useTidForAnim_local >= 1010 && useTidForAnim_local <= 1015)
                                                {
                                                    int group_local = (useTidForAnim_local - 1010) % 3;
                                                    collisionTid_local = (group_local == 0) ? 0x04 : (group_local == 1) ? 0x7D : 0x7F;
                                                }
                                                else if (useTidForAnim_local >= 1020 && useTidForAnim_local <= 1037)
                                                {
                                                    collisionTid_local = 0x74 + ((useTidForAnim_local - 1020) % 9);
                                                }
                                                else
                                                {
                                                    collisionTid_local = tid_local;
                                                }
                                            }
                                            var col_center = MetatileCollisionTable.GetCollision((byte)collisionTid_local);
                                            int tileStartX_local = tx_local * TILE;
                                            int localX_local = Math.Max(0, Math.Min(TILE - 1, px - tileStartX_local));
                                            int tileStartY_local = centerTileY * TILE;
                                            int localY_local = Math.Max(0, Math.Min(TILE - 1, py - tileStartY_local));
                                            if (TileOccupiesPixel(col_center, localX_local, localY_local)) { blocked_center = true; break; }
                                        }
                                        if (blocked_center) break;
                                    }

                                    if (blocked_center)
                                    {
                                        try
                                        {
                                            AppendSimDebug($"TopDeath: center=({playerCenterX},{playerCenterY}) gravityReversed={gravityReversed} Option_NoDeath={MainWindow.Option_NoDeath} playerY={playerY_fixed} vel={playerVelY_fixed}");
                                            deathTriggered = true;
                                            paused = true;
                                            try
                                            {
                                                Dispatcher.BeginInvoke(new Action(() =>
                                                {
                                                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                                    if (this.Owner is MainWindow mw)
                                                    {
                                                        try { mw.PauseSimulatorPlayback(); } catch { }
                                                        try { mw.AddDeathMarker(playerCenterX, playerCenterY); } catch { }
                                                    }
                                                }));
                                            }
                                            catch { }
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        catch { }

                        int leftTileX_local = playerLeft_px_local / TILE;
                        int rightTileX_local = playerRight_px_local / TILE;
                        bool blocked = false;
                        int blockingTileWorldBottom_px = int.MaxValue;
                        for (int tx_local = leftTileX_local; tx_local <= rightTileX_local; tx_local++)
                        {
                            if (tx_local < 0 || tx_local >= mapWidth) continue;
                            int tid_local = tiles[tileIndexY * mapWidth + tx_local];
                            int useTidForAnim_local = MapAnimatedTileIndex(tid_local);
                            int collisionTid_local = useTidForAnim_local;
                            if (useTidForAnim_local >= 1000)
                            {
                                if (useTidForAnim_local >= 1000 && useTidForAnim_local <= 1007)
                                {
                                    collisionTid_local = 0x08 + ((useTidForAnim_local - 1000) % 4);
                                }
                                else if (useTidForAnim_local >= 1010 && useTidForAnim_local <= 1015)
                                {
                                    int group_local = (useTidForAnim_local - 1010) % 3;
                                    collisionTid_local = (group_local == 0) ? 0x04 : (group_local == 1) ? 0x7D : 0x7F;
                                }
                                else if (useTidForAnim_local >= 1020 && useTidForAnim_local <= 1037)
                                {
                                    collisionTid_local = 0x74 + ((useTidForAnim_local - 1020) % 9);
                                }
                                else
                                {
                                    collisionTid_local = tid_local;
                                }
                            }
                            var col_local = MetatileCollisionTable.GetCollision((byte)collisionTid_local);

                            int tileStartX_local = tx_local * TILE;
                            int localLeft_local = Math.Max(0, playerLeft_px_local - tileStartX_local);
                            int localRight_local = Math.Min(TILE - 1, playerRight_px_local - tileStartX_local);

                            for (int lx_local = localLeft_local; lx_local <= localRight_local; lx_local++)
                            {
                                if (!MainWindow.Option_NoDeath)
                                {
                                    int worldPxLocal = tileStartX_local + lx_local;
                                    if (worldPxLocal >= playerCenter_px_local) continue;
                                }
                                if (BlocksCeilingAtColumn(col_local, lx_local))
                                {
                                    blocked = true;
                                    int tileWorldBottom_px = (tileAboveY_world + 1) * TILE;
                                    if (tileWorldBottom_px < blockingTileWorldBottom_px) blockingTileWorldBottom_px = tileWorldBottom_px;
                                    break;
                                }
                            }
                            if (blocked) break;
                        }

                        if (blocked && blockingTileWorldBottom_px < int.MaxValue)
                        {
                            if (currentGameMode == 0 && !gravityReversed && !MainWindow.Option_NoDeath)
                            {
                                AppendSimDebug($"CeilCollision: SKIP_PASS_THROUGH normal-gravity currentGameMode={currentGameMode} gravityReversed={gravityReversed} Option_NoDeath={MainWindow.Option_NoDeath} playerY={playerY_fixed} vel={playerVelY_fixed}");
                            }
                            else
                            {
                                AppendSimDebug($"CeilCollision: EJECTING currentGameMode={currentGameMode} gravityReversed={gravityReversed} Option_NoDeath={MainWindow.Option_NoDeath} playerY={playerY_fixed} vel={playerVelY_fixed}");
                                int desiredTop_px = blockingTileWorldBottom_px;
                                int desiredPlayerY_fixed = desiredTop_px << 8;
                                if (desiredPlayerY_fixed < 0) desiredPlayerY_fixed = 0;
                                playerY_fixed = desiredPlayerY_fixed;
                                bool skipEjection_local = false;
                                try
                                {
                                    if (currentGameMode == 2)
                                    {
                                        int gravityDir_local = ballGoingDown ? 1 : -1;
                                        if (gravityDir_local < 0) skipEjection_local = true;
                                    }
                                }
                                catch { }

                                if (skipEjection_local)
                                {
                                    onGround = true;
                                    groundStabilizeCounter = 2;
                                    try { playerVelY_fixed = 0; } catch { }

                                    try
                                    {
                                        if (currentGameMode == 0)
                                        {
                                            if (jumpBuffered_local > 0 || pendingKeyX_local > 0 || (gravityReversed && (keyXHeld_local || IsXDownAsync())))
                                            {
                                                try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                                                physicsEnabled = true;
                                                onGround = false;
                                                jumpAppliedThisStep_local = true;
                                                orbBufferActive = false;
                                                jumpedOnce = true;
                                                Interlocked.Exchange(ref keyXPressedCount, 0);
                                            }
                                        }
                                    }
                                    catch { }

                                    try
                                    {
                                        int buffered_toggle_local2 = Interlocked.Exchange(ref ballToggleRequested, 0);
                                        if (currentGameMode == 2 && buffered_toggle_local2 > 0)
                                        {
                                            ballGoingDown = !ballGoingDown;
                                            try { gravityReversed = !ballGoingDown; effectiveInvertedByW = gravityReversed; } catch { }
                                            try { playerVelY_fixed = (int)Math.Round((ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL) * simTimeScale); } catch { playerVelY_fixed = ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL; }
                                            onGround = false;
                                            jumpedOnce = true;
                                            LogBallEvent($"NUM-CeilCollision: consumed queued toggle; ballGoingDown={ballGoingDown} gravityReversed={gravityReversed}");
                                        }
                                    }
                                    catch { }

                                    try { Interlocked.Exchange(ref ballToggleLocked, 0); } catch { }
                                    LogBallEvent($"NUM-CeilCollision: unlock (ballToggleLocked=0) onGround={onGround}");
                                }
                                else
                                {
                                    if (currentGameMode == 0)
                                    {
                                        onGround = true;
                                        groundStabilizeCounter = 2;
                                        try { playerVelY_fixed = 0; } catch { }

                                        try
                                        {
                                            if (jumpBuffered_local > 0 || pendingKeyX_local > 0 || (gravityReversed && (keyXHeld_local || IsXDownAsync())))
                                            {
                                                try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                                                physicsEnabled = true;
                                                onGround = false;
                                                jumpAppliedThisStep_local = true;
                                                orbBufferActive = false;
                                                jumpedOnce = true;
                                                Interlocked.Exchange(ref keyXPressedCount, 0);
                                            }
                                        }
                                        catch { }
                                    }
                                    else
                                    {
                                        bool numericInvert_local = gravityReversed;
                                        if (!numericInvert_local)
                                            playerVelY_fixed = Math.Min(effectiveGravity_fixed, effectiveMaxFall_fixed);
                                        else
                                            playerVelY_fixed = Math.Max(effectiveGravity_fixed, effectiveMaxFall_fixed);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // Apply deferred cube jump (pendingPresses_forLater) after integration if conditions met
        private void Cube_HandleDeferredJump(int pendingPresses_forLater, ref bool jumpAppliedThisStep_local)
        {
            try
            {
                if (pendingPresses_forLater > 0 && currentGameMode == 0)
                {
                    bool touchingCeiling_local = (gravityReversed || effectiveInvertedByW) && IsTouchingCeilingStrict();
                    if (onGround || touchingCeiling_local)
                    {
                        try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                        physicsEnabled = true;
                        onGround = false;
                        jumpAppliedThisStep_local = true; // consumed this step
                        jumpedOnce = true;
                    }
                }
            }
            catch { }
        }

        // Tolerant held-X jump handler for Cube mode (per-pixel head sampling)
        private void Cube_HandleHeldJump(int jumpBuffered_local, int pendingPresses_num, bool keyXHeld_local, ref bool jumpAppliedThisStep_local)
        {
            try
            {
                if ((gravityReversed || effectiveInvertedByW) && currentGameMode == 0 && !jumpAppliedThisStep_local)
                {
                    bool headOverBlocking = false;
                    try
                    {
                        const int HITBOX_W_LOCAL = 15;
                        int playerCenter_px_local = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                        int playerLeft_px_local = playerCenter_px_local - (HITBOX_W_LOCAL / 2);
                        int playerRight_px_local = playerLeft_px_local + (HITBOX_W_LOCAL - 1);
                        int headWorldY_px_local = (playerY_fixed >> 8);
                        int headTileY_local = headWorldY_px_local / TILE;
                        int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;

                        for (int tx_local = playerLeft_px_local / TILE; tx_local <= playerRight_px_local / TILE; tx_local++)
                        {
                            if (tx_local < 0 || tx_local >= mapWidth) continue;
                            for (int ty_local = headTileY_local - 1; ty_local <= headTileY_local; ty_local++)
                            {
                                int tileIndexY_local = ty_local + groundRowsToReserve_local;
                                if (tileIndexY_local < 0 || tileIndexY_local >= mapHeight) continue;
                                int tid_local = tiles[tileIndexY_local * mapWidth + tx_local];
                                int useTidForAnim_local = MapAnimatedTileIndex(tid_local);
                                int collisionTid_local = useTidForAnim_local;
                                if (useTidForAnim_local >= 1000)
                                {
                                    if (useTidForAnim_local >= 1000 && useTidForAnim_local <= 1007)
                                        collisionTid_local = 0x08 + ((useTidForAnim_local - 1000) % 4);
                                    else if (useTidForAnim_local >= 1010 && useTidForAnim_local <= 1015)
                                    {
                                        int group_local = (useTidForAnim_local - 1010) % 3;
                                        collisionTid_local = (group_local == 0) ? 0x04 : (group_local == 1) ? 0x7D : 0x7F;
                                    }
                                    else if (useTidForAnim_local >= 1020 && useTidForAnim_local <= 1037)
                                        collisionTid_local = 0x74 + ((useTidForAnim_local - 1020) % 9);
                                    else
                                        collisionTid_local = tid_local;
                                }
                                var col_local = MetatileCollisionTable.GetCollision((byte)collisionTid_local);
                                int tileStartX_local = tx_local * TILE;
                                int localLeft_local = Math.Max(0, playerLeft_px_local - tileStartX_local);
                                int localRight_local = Math.Min(TILE - 1, playerRight_px_local - tileStartX_local);
                                int tileStartY_local = ty_local * TILE;
                                int localY_local = Math.Max(0, Math.Min(TILE - 1, headWorldY_px_local - tileStartY_local));
                                for (int lx_local = localLeft_local; lx_local <= localRight_local; lx_local++)
                                {
                                    int worldPx_local = tileStartX_local + lx_local;
                                    if (!MainWindow.Option_NoDeath && worldPx_local >= playerCenter_px_local) continue;
                                    if (TileOccupiesPixel(col_local, lx_local, localY_local)) { headOverBlocking = true; break; }
                                }
                                if (headOverBlocking) break;
                            }
                            if (headOverBlocking) break;
                        }
                    }
                    catch { headOverBlocking = IsTouchingCeilingStrict(); }

                    if (headOverBlocking)
                    {
                        if (jumpBuffered_local > 0 || pendingPresses_num > 0 || keyXHeld_local || IsXDownAsync())
                        {
                            try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                            physicsEnabled = true;
                            onGround = false;
                            jumpAppliedThisStep_local = true;
                            jumpedOnce = true;
                            orbBufferActive = false;
                            try { invertedCeilingHoldCounter = 0; } catch { }
                            try
                            {
                                int sign = Math.Sign(playerVelY_fixed);
                                if (sign == 0) sign = (gravityReversed || effectiveInvertedByW) ? 1 : -1;
                                playerY_fixed += (sign * 6) << 8;
                            }
                            catch { }
                            Interlocked.Exchange(ref keyXPressedCount, 0);
                        }
                    }
                }
            }
            catch { }
        }

        // UI-side immediate jump when pressing X in cube mode while touching ceiling/ground
        private void Cube_HandleUIImmediateJump()
        {
            try
            {
                if (currentGameMode == 0 && (gravityReversed || effectiveInvertedByW) && (IsTouchingCeilingStrict() || onGround))
                {
                    Interlocked.Exchange(ref keyXPressedCount, 0);
                    try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                    physicsEnabled = true;
                    onGround = false;
                    groundStabilizeCounter = 0;
                    jumpedOnce = true;
                }
            }
            catch { }
        }

        // Handle landing jump behavior invoked from UI-landing code path
        private void Cube_HandleUILanding(int buffered_ui, int pendingPressesNow, bool keyXHeld_local)
        {
            try
            {
                if (buffered_ui > 0 || pendingPressesNow > 0 || keyXHeld_local || IsXDownAsync())
                {
                    try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                    onGround = false;
                    jumpedOnce = true;
                    Interlocked.Exchange(ref keyXPressedCount, 0);
                }
            }
            catch { }
        }

        // Handle immediate pending-press in sim path (simTimer==null)
        private void Cube_HandleSimPendingPress(int pendingPress, bool effectiveOnGround_local, ref bool jumpAppliedThisFrame)
        {
            try
            {
                // Cube: only allow immediate jump when on ground (preserve jump-buffer semantics)
                if (currentGameMode == 0 && !effectiveOnGround_local)
                {
                    // Ignore immediate jump while mid-air for cube; buffer will be consumed on landing elsewhere.
                    return;
                }

                // Reset vertical velocity to the jump impulse (do not stack)
                try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                physicsEnabled = true;
                onGround = false;
                jumpAppliedThisFrame = true;
                jumpedOnce = true;
            }
            catch { }
        }

        // Per-frame ceiling stabilization for Cube mode extracted from the numeric sim.
        // Keeps the player's head snapped to a blocking ceiling row when inverted.
        private void Cube_HandleCeilingStabilization()
        {
            // Ensure this handler only runs for Cube mode when numeric inversion/ceiling logic applies.
            try { if (currentGameMode != 0) return; } catch { return; }
            try { if (!(gravityReversed || effectiveInvertedByW)) return; } catch { return; }
            try
            {
                int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerLeft_px = playerCenter_px - (15 / 2);
                int playerRight_px = playerLeft_px + (15 - 1);
                int headWorldY_px = (playerY_fixed >> 8);
                int headTileY = headWorldY_px / TILE;
                int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;

                int nearestBlockingTileBottom_px = int.MaxValue;
                bool anyBlocking = false;

                for (int px = playerLeft_px; px <= playerRight_px; px++)
                {
                    int tx = px / TILE;
                    if (tx < 0 || tx >= mapWidth) continue;

                    // Respect right-side pass-through when deaths are enabled
                    if (!MainWindow.Option_NoDeath)
                    {
                        if (px >= playerCenter_px) continue;
                    }

                    for (int ty = headTileY - 1; ty <= headTileY; ty++)
                    {
                        int tileIndexY = ty + groundRowsToReserve_local;
                        if (tileIndexY < 0 || tileIndexY >= mapHeight) continue;
                        int tid = tiles[tileIndexY * mapWidth + tx];
                        int useTidForAnim = MapAnimatedTileIndex(tid);
                        int collisionTid = useTidForAnim;
                        if (useTidForAnim >= 1000)
                        {
                            if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                            else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                            {
                                int group = (useTidForAnim - 1010) % 3;
                                collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                            }
                            else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                            else
                                collisionTid = tid;
                        }

                        var col = MetatileCollisionTable.GetCollision((byte)collisionTid);
                        int tileStartX = tx * TILE;
                        int localX = Math.Max(0, Math.Min(TILE - 1, px - tileStartX));
                        if (BlocksCeilingAtColumn(col, localX))
                        {
                            int tileWorldTop_px = ty * TILE;
                            int tileWorldBottom_px = (ty + 1) * TILE;
                            if (headWorldY_px >= tileWorldTop_px && headWorldY_px < tileWorldBottom_px)
                            {
                                anyBlocking = true;
                                if (tileWorldBottom_px < nearestBlockingTileBottom_px) nearestBlockingTileBottom_px = tileWorldBottom_px;
                            }
                        }
                    }
                }

                if (anyBlocking && nearestBlockingTileBottom_px < int.MaxValue)
                {
                    int desiredTop_px = nearestBlockingTileBottom_px;
                    int desiredPlayerY_fixed = desiredTop_px << 8;
                    if (desiredPlayerY_fixed < 0) desiredPlayerY_fixed = 0;
                    playerY_fixed = desiredPlayerY_fixed;
                    playerVelY_fixed = 0;
                    onGround = true;
                    invertedCeilingHoldCounter = 4;
                }
                else
                {
                    if (invertedCeilingHoldCounter > 0) invertedCeilingHoldCounter--;
                }
            }
            catch { playerVelY_fixed = 0; onGround = true; }
        }

        // Handles landing/ejection and reversed-gravity landing logic for Cube mode.
        private void Cube_HandleLandingAndReversed(int jumpBuffered_local, int pendingKeyX_local, bool keyXHeld_local, int pendingPresses_num, int pendingPressStartedOnGround, int pendingPresses_forLater, ref bool jumpAppliedThisStep_local)
        {
            try
            {
                const int HITBOX_W_LOCAL = 15;
                const int HITBOX_H_LOCAL = 15;

                int playerCenter_px_local = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerLeft_px_local = playerCenter_px_local - (HITBOX_W_LOCAL / 2);
                int playerRight_px_local = playerLeft_px_local + (HITBOX_W_LOCAL - 1);
                int footWorldY_px_local = (playerY_fixed >> 8) + HITBOX_H_LOCAL;

                int tileBelowY_local = footWorldY_px_local / TILE;
                try
                {
                    int groundRowsToReserve_local = 0;
                    if (hasGroundLayer && groundTileRows > 0) groundRowsToReserve_local = Math.Min(3, groundTileRows);
                    tileBelowY_local = tileBelowY_local + groundRowsToReserve_local;
                }
                catch { }
                int floorDetected_local = 0;
                int floorTopWorldY_px_local = (mapHeight * TILE);
                int maxPlayerY_fixed_local = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;

                if (tileBelowY_local >= 0 && tileBelowY_local < mapHeight)
                {
                    int leftTileX_local = playerLeft_px_local / TILE;
                    int rightTileX_local = playerRight_px_local / TILE;

                    for (int tx_local = leftTileX_local; tx_local <= rightTileX_local; tx_local++)
                    {
                        if (tx_local < 0 || tx_local >= mapWidth) continue;
                        int tid_local = tiles[tileBelowY_local * mapWidth + tx_local];

                        int useTidForAnim_local = MapAnimatedTileIndex(tid_local);
                        int collisionTid_local = useTidForAnim_local;
                        if (useTidForAnim_local >= 1000)
                        {
                            if (useTidForAnim_local >= 1000 && useTidForAnim_local <= 1007)
                            {
                                collisionTid_local = 0x08 + ((useTidForAnim_local - 1000) % 4);
                            }
                            else if (useTidForAnim_local >= 1010 && useTidForAnim_local <= 1015)
                            {
                                int group_local = (useTidForAnim_local - 1010) % 3;
                                collisionTid_local = (group_local == 0) ? 0x04 : (group_local == 1) ? 0x7D : 0x7F;
                            }
                            else if (useTidForAnim_local >= 1020 && useTidForAnim_local <= 1037)
                            {
                                collisionTid_local = 0x74 + ((useTidForAnim_local - 1020) % 9);
                            }
                            else
                            {
                                collisionTid_local = tid_local;
                            }
                        }
                        var col_local = MetatileCollisionTable.GetCollision((byte)collisionTid_local);

                        int tileStartX_local = tx_local * TILE;
                        int localLeft_local = Math.Max(0, playerLeft_px_local - tileStartX_local);
                        int localRight_local = Math.Min(TILE - 1, playerRight_px_local - tileStartX_local);

                        int bestTopOffset_local = int.MaxValue;
                        bool any_local = false;
                        for (int lx_local = localLeft_local; lx_local <= localRight_local; lx_local++)
                        {
                            if (ProvidesFloorAtColumnStatic(col_local, lx_local, out int off_local))
                            {
                                any_local = true;
                                if (off_local < bestTopOffset_local) bestTopOffset_local = off_local;
                                continue;
                            }

                            if (!MainWindow.Option_NoDeath)
                            {
                                int worldPxLocal = tileStartX_local + lx_local;
                                if (worldPxLocal >= playerCenter_px_local) continue;
                            }
                        }
                        if (any_local)
                        {
                            int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int candidateTop_local = (tileBelowY_local - groundRowsToReserve_local) * TILE + bestTopOffset_local;
                            if (candidateTop_local < floorTopWorldY_px_local) floorTopWorldY_px_local = candidateTop_local;
                            floorDetected_local = 1;
                        }
                    }
                }

                if (floorDetected_local == 0)
                {
                    int groundRowsToReserve_local2 = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                    if (groundRowsToReserve_local2 > 0)
                    {
                        int topOfGround_px_local = (mapHeight - groundRowsToReserve_local2) * TILE;
                        if (topOfGround_px_local < floorTopWorldY_px_local) floorTopWorldY_px_local = topOfGround_px_local;
                        floorDetected_local = 1;
                    }
                }

                int floorTop_fixed_local = (floorDetected_local == 1) ? ((floorTopWorldY_px_local - HITBOX_H_LOCAL) << 8) : maxPlayerY_fixed_local;

                if (!gravityReversed)
                {
                    try
                    {
                        if (!MainWindow.Option_NoDeath && !deathTriggered)
                        {
                            int playerRightEdge_px_chk = (playerX_fixed >> 8) + playerVisualWidth - 1;
                            int playerCenterY_px_chk = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                            int sampledX_chk = playerRightEdge_px_chk;
                            int sampledY_chk = playerCenterY_px_chk;
                            int sampleTileX_chk = sampledX_chk / TILE;
                            int sampleTileY_chk = sampledY_chk / TILE;
                            int groundRowsToReserve_chk = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int tileIndexY_center_chk = sampleTileY_chk + groundRowsToReserve_chk;
                            if (tileIndexY_center_chk >= 0 && tileIndexY_center_chk < mapHeight && sampleTileX_chk >= 0 && sampleTileX_chk < mapWidth)
                            {
                                int tid_chk = tiles[tileIndexY_center_chk * mapWidth + sampleTileX_chk];
                                int useTidForAnim_chk = MapAnimatedTileIndex(tid_chk);
                                int collisionTid_chk = useTidForAnim_chk;
                                if (useTidForAnim_chk >= 1000)
                                {
                                    if (useTidForAnim_chk >= 1000 && useTidForAnim_chk <= 1007)
                                    {
                                        collisionTid_chk = 0x08 + ((useTidForAnim_chk - 1000) % 4);
                                    }
                                    else if (useTidForAnim_chk >= 1010 && useTidForAnim_chk <= 1015)
                                    {
                                        int group_chk = (useTidForAnim_chk - 1010) % 3;
                                        collisionTid_chk = (group_chk == 0) ? 0x04 : (group_chk == 1) ? 0x7D : 0x7F;
                                    }
                                    else if (useTidForAnim_chk >= 1020 && useTidForAnim_chk <= 1037)
                                    {
                                        collisionTid_chk = 0x74 + ((useTidForAnim_chk - 1020) % 9);
                                    }
                                    else
                                    {
                                        collisionTid_chk = tid_chk;
                                    }
                                }
                                var col_center_chk = MetatileCollisionTable.GetCollision((byte)collisionTid_chk);
                                int tileStartX_chk = sampleTileX_chk * TILE;
                                int localX_chk = Math.Max(0, Math.Min(TILE - 1, sampledX_chk - tileStartX_chk));
                                int tileStartY_chk = sampleTileY_chk * TILE;
                                int localY_chk = Math.Max(0, Math.Min(TILE - 1, sampledY_chk - tileStartY_chk));
                                bool blocked_center_chk = TileOccupiesPixel(col_center_chk, localX_chk, localY_chk);
                                if (blocked_center_chk)
                                {
                                    try
                                    {
                                        AppendSimDebug($"PreSnapRightEdgeDeath: sample=({sampledX_chk},{sampledY_chk}) Option_NoDeath={MainWindow.Option_NoDeath} playerY={playerY_fixed} vel={playerVelY_fixed}");
                                        deathTriggered = true;
                                        paused = true;
                                        try
                                        {
                                            Dispatcher.BeginInvoke(new Action(() =>
                                            {
                                                try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                                if (this.Owner is MainWindow mw)
                                                {
                                                    try { mw.PauseSimulatorPlayback(); } catch { }
                                                    try { mw.AddDeathMarker(sampledX_chk, sampledY_chk); } catch { }
                                                }
                                            }));
                                        }
                                        catch { }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    catch { }

                    if (!deathTriggered && playerY_fixed >= floorTop_fixed_local - LAND_EPS_FIXED && playerVelY_fixed >= 0)
                    {
                        int nudged = floorTop_fixed_local - (1 << 8);
                        if (nudged < 0) nudged = 0;
                        playerY_fixed = nudged;
                        playerVelY_fixed = 0;
                        onGround = true;

                        if (currentGameMode == 0 && (jumpBuffered_local > 0 || pendingKeyX_local > 0 || keyXHeld_local || IsXDownAsync()))
                        {
                            try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                            onGround = false;
                            jumpedOnce = true;
                            Interlocked.Exchange(ref keyXPressedCount, 0);
                        }
                        else
                        {
                            int buffered_toggle_local = Interlocked.Exchange(ref ballToggleRequested, 0);
                            if (currentGameMode == 2 && buffered_toggle_local > 0)
                            {
                                ballGoingDown = !ballGoingDown;
                                try { gravityReversed = !ballGoingDown; effectiveInvertedByW = gravityReversed; } catch { }
                                try { playerVelY_fixed = (int)Math.Round((ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL) * simTimeScale); } catch { playerVelY_fixed = ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL; }
                                onGround = false;
                                jumpedOnce = true;
                                LogBallEvent($"NUM-Landing: consumed queued toggle; ballGoingDown={ballGoingDown} gravityReversed={gravityReversed}");
                            }
                            try { Interlocked.Exchange(ref ballToggleLocked, 0); } catch { }
                            LogBallEvent($"NUM-Landing: unlock (ballToggleLocked=0) onGround={onGround}");
                        }
                    }
                    else
                    {
                        onGround = false;
                    }
                }
                else
                {
                    if (currentGameMode == 0 && gravityReversed && !MainWindow.Option_NoDeath && !deathTriggered)
                    {
                        try
                        {
                            int playerTop_px_local = (playerY_fixed >> 8);
                            const int SAMPLE_Y_OFFSET_REV = 1;
                            int sampledCenterY_rev = playerTop_px_local - SAMPLE_Y_OFFSET_REV;
                            int centerTileY_rev = sampledCenterY_rev / TILE;
                            int groundRowsToReserve_rev = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int tileIndexY_center_rev = centerTileY_rev + groundRowsToReserve_rev;
                            if (tileIndexY_center_rev >= 0 && tileIndexY_center_rev < mapHeight)
                            {
                                bool floor_center = false;
                                int foundTid_local = -1;
                                MetatileCollision foundCol_local = MetatileCollision.COL_NONE;
                                int foundSampleTileY = int.MinValue;
                                int foundTileX = int.MinValue;
                                int prevPlayerY_fixed = playerY_fixed - playerVelY_fixed;
                                int prevPlayerTop = (prevPlayerY_fixed >> 8);
                                int prevSampleY = prevPlayerTop - SAMPLE_Y_OFFSET_REV;
                                int currSampleY = sampledCenterY_rev;
                                int deltaY = currSampleY - prevSampleY;
                                int absDelta = Math.Abs(deltaY);
                                int steps = Math.Max(1, Math.Min(16, absDelta));

                                int[] dxs_rev = new int[] { -1, 0 };
                                int[] dys_rev = new int[] { -1, 0 };
                                for (int s = 0; s <= steps && !floor_center; s++)
                                {
                                    int sampleY = prevSampleY + (deltaY * s) / Math.Max(1, steps);
                                    foreach (var dx in dxs_rev)
                                    {
                                        foreach (var dy in dys_rev)
                                        {
                                            int px = playerCenter_px_local + dx;
                                            if (!MainWindow.Option_NoDeath && px >= playerCenter_px_local) continue;
                                            int py = sampleY + dy;
                                            int tx_local = px / TILE;
                                            if (tx_local < 0 || tx_local >= mapWidth) continue;

                                            int sampleTileY = py / TILE;
                                            int tileIndexY_sample = sampleTileY + groundRowsToReserve_rev;
                                            if (tileIndexY_sample < 0 || tileIndexY_sample >= mapHeight) continue;

                                            int tid_local = tiles[tileIndexY_sample * mapWidth + tx_local];
                                            int useTidForAnim_local = MapAnimatedTileIndex(tid_local);
                                            int collisionTid_local = useTidForAnim_local;
                                            if (useTidForAnim_local >= 1000)
                                            {
                                                if (useTidForAnim_local >= 1000 && useTidForAnim_local <= 1007)
                                                {
                                                    collisionTid_local = 0x08 + ((useTidForAnim_local - 1000) % 4);
                                                }
                                                else if (useTidForAnim_local >= 1010 && useTidForAnim_local <= 1015)
                                                {
                                                    int group_local = (useTidForAnim_local - 1010) % 3;
                                                    collisionTid_local = (group_local == 0) ? 0x04 : (group_local == 1) ? 0x7D : 0x7F;
                                                }
                                                else if (useTidForAnim_local >= 1020 && useTidForAnim_local <= 1037)
                                                {
                                                    collisionTid_local = 0x74 + ((useTidForAnim_local - 1020) % 9);
                                                }
                                                else
                                                {
                                                    collisionTid_local = tid_local;
                                                }
                                            }
                                            var col_center = MetatileCollisionTable.GetCollision((byte)collisionTid_local);
                                            int tileStartX_local = tx_local * TILE;
                                            int localX_local = Math.Max(0, Math.Min(TILE - 1, px - tileStartX_local));
                                            int tileStartY_local = sampleTileY * TILE;
                                            int localY_local = Math.Max(0, Math.Min(TILE - 1, py - tileStartY_local));

                                            bool occupied = TileOccupiesPixel(col_center, localX_local, localY_local);
                                            if (!occupied)
                                            {
                                                if (localY_local > 0)
                                                {
                                                    if (TileOccupiesPixel(col_center, localX_local, localY_local - 1)) occupied = true;
                                                }
                                                if (!occupied && localY_local < TILE - 1)
                                                {
                                                    if (TileOccupiesPixel(col_center, localX_local, localY_local + 1)) occupied = true;
                                                }
                                            }

                                            if (!occupied)
                                            {
                                                if (col_center == MetatileCollision.COL_TOP
                                                    || col_center == MetatileCollision.COL_UP_LEFT
                                                    || col_center == MetatileCollision.COL_UP_RIGHT
                                                    || col_center == MetatileCollision.COL_TOP_LEFT_STAIRS
                                                    || col_center == MetatileCollision.COL_TOP_RIGHT_STAIRS
                                                    || col_center == MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT
                                                    || col_center == MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT)
                                                {
                                                    int tileTop = tileStartY_local;
                                                    int topHalfLimit = tileTop + (TILE / 2);
                                                    if (py >= tileTop && py < topHalfLimit)
                                                    {
                                                        occupied = true;
                                                        try { if (Math.Abs(playerVelY_fixed) >= 0x400) AppendSimDebug($"REV_SAMP (heur) px={px} py={py} tx={tx_local} ty={sampleTileY} tid=0x{collisionTid_local:X2} col={(int)col_center} lx={localX_local} ly={localY_local} occ={occupied}"); } catch { }
                                                    }
                                                }
                                            }

                                            try { if (Math.Abs(playerVelY_fixed) >= 0x400) AppendSimDebug($"REV_SAMP px={px} py={py} tx={tx_local} ty={sampleTileY} tid=0x{collisionTid_local:X2} col={(int)col_center} lx={localX_local} ly={localY_local} occ={occupied}"); } catch { }

                                            if (occupied)
                                            {
                                                floor_center = true;
                                                foundTid_local = collisionTid_local;
                                                foundCol_local = col_center;
                                                foundSampleTileY = sampleTileY;
                                                foundTileX = tx_local;
                                                break;
                                            }
                                        }
                                        if (floor_center) break;
                                    }
                                }

                                if (floor_center)
                                {
                                    try
                                    {
                                        bool isTopOrUp = (foundCol_local == MetatileCollision.COL_TOP
                                            || foundCol_local == MetatileCollision.COL_TOP_LEFT_STAIRS
                                            || foundCol_local == MetatileCollision.COL_TOP_RIGHT_STAIRS
                                            || foundCol_local == MetatileCollision.COL_UP_LEFT
                                            || foundCol_local == MetatileCollision.COL_UP_RIGHT
                                            || foundCol_local == MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT
                                            || foundCol_local == MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT);

                                        if (isTopOrUp)
                                        {
                                            if (foundSampleTileY != int.MinValue)
                                            {
                                                int tileTopWorld = foundSampleTileY * TILE;
                                                int desiredPlayerTop = tileTopWorld + (TILE / 2) - 1; // tileTop + 7
                                                playerY_fixed = (desiredPlayerTop << 8);
                                            }
                                            else
                                            {
                                                playerY_fixed = floorTop_fixed_local + (1 << 8);
                                            }
                                            playerVelY_fixed = 0;
                                            onGround = true;
                                            if (currentGameMode == 0 && (jumpBuffered_local > 0 || pendingKeyX_local > 0 || keyXHeld_local || IsXDownAsync()))
                                            {
                                                try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                                                physicsEnabled = true;
                                                onGround = false;
                                                jumpAppliedThisStep_local = true;
                                                orbBufferActive = false;
                                                jumpedOnce = true;
                                                try { invertedCeilingHoldCounter = 0; } catch { }
                                                try
                                                {
                                                    int sign = Math.Sign(playerVelY_fixed);
                                                    if (sign == 0) sign = (gravityReversed || effectiveInvertedByW) ? 1 : -1;
                                                    playerY_fixed += (sign * 6) << 8;
                                                }
                                                catch { }
                                                Interlocked.Exchange(ref keyXPressedCount, 0);
                                            }
                                            else
                                            {
                                                int buffered_toggle_local = Interlocked.Exchange(ref ballToggleRequested, 0);
                                                if (currentGameMode == 2 && buffered_toggle_local > 0)
                                                {
                                                    ballGoingDown = !ballGoingDown;
                                                    try { gravityReversed = !ballGoingDown; effectiveInvertedByW = gravityReversed; } catch { }
                                                    try { playerVelY_fixed = (int)Math.Round((ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL) * simTimeScale); } catch { playerVelY_fixed = ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL; }
                                                }
                                            }
                                        }
                                        else
                                        {
                                            bool fallbackFound = false;
                                            int fallbackTileY = int.MinValue;
                                            int fallbackTileX = int.MinValue;
                                            MetatileCollision fallbackCol = MetatileCollision.COL_NONE;
                                            try
                                            {
                                                int fallback_playerTop_px = (playerY_fixed >> 8);
                                                int playerCenter_px_local_local = playerCenter_px_local;
                                                int fallback_playerLeft_px = playerCenter_px_local_local - (15 / 2);
                                                int fallback_playerRight_px = fallback_playerLeft_px + (15 - 1);
                                                for (int tx_check = playerLeft_px_local / TILE; tx_check <= playerRight_px_local / TILE && !fallbackFound; tx_check++)
                                                {
                                                    if (tx_check < 0 || tx_check >= mapWidth) continue;
                                                    for (int ty_check = centerTileY_rev; ty_check >= centerTileY_rev - 1 && !fallbackFound; ty_check--)
                                                    {
                                                        int tileIndexY_check = ty_check + groundRowsToReserve_rev;
                                                        if (tileIndexY_check < 0 || tileIndexY_check >= mapHeight) continue;
                                                        int tid_check = tiles[tileIndexY_check * mapWidth + tx_check];
                                                        int useTid_check = MapAnimatedTileIndex(tid_check);
                                                        int collisionTid_check = useTid_check;
                                                        if (useTid_check >= 1000)
                                                        {
                                                            if (useTid_check >= 1000 && useTid_check <= 1007) collisionTid_check = 0x08 + ((useTid_check - 1000) % 4);
                                                            else if (useTid_check >= 1010 && useTid_check <= 1015) { int group = (useTid_check - 1010) % 3; collisionTid_check = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F; }
                                                            else if (useTid_check >= 1020 && useTid_check <= 1037) collisionTid_check = 0x74 + ((useTid_check - 1020) % 9);
                                                            else collisionTid_check = tid_check;
                                                        }
                                                        var col_check = MetatileCollisionTable.GetCollision((byte)collisionTid_check);
                                                        int tileStartX_check = tx_check * TILE;
                                                        int localLeft_check = Math.Max(0, fallback_playerLeft_px - tileStartX_check);
                                                        int localRight_check = Math.Min(TILE - 1, fallback_playerRight_px - tileStartX_check);
                                                        int tileStartY_check = ty_check * TILE;
                                                        int localY_check = Math.Max(0, Math.Min(TILE - 1, fallback_playerTop_px - tileStartY_check));
                                                        for (int lx_check = localLeft_check; lx_check <= localRight_check; lx_check++)
                                                        {
                                                            int worldPx_check = tileStartX_check + lx_check;
                                                            if (!MainWindow.Option_NoDeath && worldPx_check >= playerCenter_px_local) continue;
                                                            if (TileOccupiesPixel(col_check, lx_check, localY_check)) { fallbackFound = true; fallbackTileY = ty_check; fallbackTileX = tx_check; fallbackCol = col_check; break; }
                                                            if (localY_check > 0 && TileOccupiesPixel(col_check, lx_check, localY_check - 1)) { fallbackFound = true; fallbackTileY = ty_check; fallbackTileX = tx_check; fallbackCol = col_check; break; }
                                                            if (localY_check < TILE - 1 && TileOccupiesPixel(col_check, lx_check, localY_check + 1)) { fallbackFound = true; fallbackTileY = ty_check; fallbackTileX = tx_check; fallbackCol = col_check; break; }
                                                        }
                                                    }
                                                }
                                            }
                                            catch { fallbackFound = false; }

                                            if (fallbackFound)
                                            {
                                                int tileWorldBottom_px = (fallbackTileY + 1) * TILE;
                                                int desiredTop_px = tileWorldBottom_px;
                                                int desiredPlayerY_fixed = desiredTop_px << 8;
                                                if (desiredPlayerY_fixed < 0) desiredPlayerY_fixed = 0;
                                                playerY_fixed = desiredPlayerY_fixed;
                                                playerVelY_fixed = 0;
                                                onGround = true;
                                                groundStabilizeCounter = 2;

                                                if (currentGameMode == 0 && (jumpBuffered_local > 0 || pendingKeyX_local > 0 || keyXHeld_local || IsXDownAsync()))
                                                {
                                                    try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                                                    physicsEnabled = true;
                                                    onGround = false;
                                                    jumpAppliedThisStep_local = true;
                                                    orbBufferActive = false;
                                                    jumpedOnce = true;
                                                    Interlocked.Exchange(ref keyXPressedCount, 0);
                                                }
                                            }
                                            else
                                            {
                                                int playerCenterY = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                                                AppendSimDebug($"BottomDeath: center=({playerCenter_px_local},{playerCenterY}) gravityReversed={gravityReversed} Option_NoDeath={MainWindow.Option_NoDeath} playerY={playerY_fixed} vel={playerVelY_fixed} tid={foundTid_local} col={foundCol_local}");
                                                deathTriggered = true;
                                                paused = true;
                                                try
                                                {
                                                    Dispatcher.BeginInvoke(new Action(() =>
                                                    {
                                                        try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                                        if (this.Owner is MainWindow mw)
                                                        {
                                                            try { mw.PauseSimulatorPlayback(); } catch { }
                                                            try { mw.AddDeathMarker(playerCenter_px_local, playerCenterY); } catch { }
                                                        }
                                                    }));
                                                }
                                                catch { }
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }

                        AppendSimDebug($"UI-Landing: REVERSED_GRAVITY_NO_SKIP currentGameMode={currentGameMode} gravityReversed={gravityReversed} Option_NoDeath={MainWindow.Option_NoDeath} playerY={playerY_fixed} vel={playerVelY_fixed}");
                    }
                    else
                    {
                        if (playerY_fixed <= floorTop_fixed_local + LAND_EPS_FIXED && playerVelY_fixed <= 0)
                        {
                            int nudged = floorTop_fixed_local + (1 << 8);
                            playerY_fixed = nudged;
                            playerVelY_fixed = 0;
                            onGround = true;
                            AppendSimDebug($"NUM-REVERSED-LAND: mode={currentGameMode} gravityReversed={gravityReversed} playerY={playerY_fixed} vel={playerVelY_fixed}");
                            groundStabilizeCounter = 2;

                            if (currentGameMode == 0 && jumpBuffered_local > 0)
                            {
                                try { playerVelY_fixed = (int)Math.Round(effectiveJumpVel_fixed * simTimeScale); } catch { playerVelY_fixed = effectiveJumpVel_fixed; }
                                onGround = false;
                                jumpedOnce = true;
                                Interlocked.Exchange(ref keyXPressedCount, 0);
                            }
                        }
                        else
                        {
                            onGround = false;
                        }
                    }
                }
            }
            catch { /* keep previous behavior on error */ }
        }
    }
}

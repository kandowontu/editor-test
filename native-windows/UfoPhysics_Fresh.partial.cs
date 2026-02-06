using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// ufo_movement() from gamemode_ufo.h - 1:1 port
        /// </summary>
        private void UfoPhysics_Fresh()
        {
            // Check for orb activation
            {
                bool holdJump_orb = IsXDownAsync() || keyXHeld;
                int pressCount_orb = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                bool pressJump_orb = pressCount_orb > 0;
                bool gravityInverted_orb = (!gravityFlipped);
                int playerX_px_orb = playerX_fixed >> 8;
                int playerY_px_orb = playerY_fixed >> 8;
                int hitboxW_orb = (miniMode) ? 8 : 15;
                int hitboxH_orb = (miniMode) ? 7 : 15;  // Correct: 8x7 for mini
                
                // Adjust Y position for mini mode collision box (bottom-left alignment)
                if (miniMode && !gravityInverted_orb)
                {
                    playerY_px_orb += 9;
                }
                
                int scrollX_px_orb = 0;
                
                int tempVelY = playerVelY_fixed;
                var (orbActivated, _) = UpdateOrbSystem(6, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                   scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                   (miniMode), ref tempVelY);
                if (orbActivated)
                {
                    playerVelY_fixed = tempVelY;
                    AppendSimDebug($"[UFO] Orb activated! New velY={playerVelY_fixed}");
                    
                    // Consume the X press if it was used for orb
                    if (pressJump_orb)
                        Interlocked.Exchange(ref keyXPressedCount, 0);
                }
                
                // Clear orb buffer when X is released
                if (!holdJump_orb)
                    ClearOrbBuffer();
            }
            
            // Get base physics values (always from down-gravity index)
            int baseTableIdx = (miniMode ? 4 : 0);
            bool gravityInverted = (!gravityFlipped);
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            tmpfallspeed = GameModePhysics.UFO_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
            tmpgravity = GameModePhysics.UFO_GRAVITY(baseTableIdx) * gravityMultiplier;
            AppendSimDebug($"[UFO] table_idx={currplayer_table_idx}, gravity={tmpgravity}, fallspeed={tmpfallspeed}");
            CommonGravityRoutine_Fresh();
            
            // If grounded with inverted gravity, prevent velocity from pulling into ceiling
            if (!gravityFlipped) {
                bool isMini = (miniMode);
                int hitboxW = isMini ? 8 : 15;
                int hitboxH = isMini ? 7 : 15;
                int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
                int collisionX = (playerX_fixed >> 8);
                int testY = (playerY_fixed >> 8) + hitboxOffsetY - 1;
                var (collided, _) = CheckCollisionUp(collisionX, testY, hitboxW, hitboxH);
                
                if (collided && playerVelY_fixed < 0) {
                    playerVelY_fixed = 0;
                    AppendSimDebug($"[UFO] Ceiling grounded - zeroed velocity");
                }
            }
            
            // No collision offset - use exact position
            UfoShipEject_Fresh();
            
            // Check for jump input (press, not hold) - read without consuming first
            int pressCount = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
            bool pressedJump = pressCount > 0;
            
            AppendSimDebug($"[UFO] Input: pressCount={pressCount}, pressedJump={pressedJump}, ufoOrbed={ufoOrbed}");
            
            if (pressedJump && !ufoOrbed) {
                // Consume the press count now that we're using it
                Interlocked.Exchange(ref keyXPressedCount, 0);
                int baseJumpIdx = (miniMode ? 4 : 0);
                bool jumpGravityInverted = (!gravityFlipped);
                int jumpGravityMultiplier = jumpGravityInverted ? -1 : 1;
                int jumpVel = GameModePhysics.UFO_JUMP_VEL(baseJumpIdx) * jumpGravityMultiplier;
                playerVelY_fixed = jumpVel; // JUMP
                AppendSimDebug($"[UFO] JUMP! table_idx={currplayer_table_idx}, jumpVel={jumpVel}, velY={playerVelY_fixed}");
            }
            ufoOrbed = false;
            
            // Record position for trail
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerY_px_trail = playerY_fixed >> 8;
                // Apply mini mode offset for trail to match visual position
                bool isMini_trail = (miniMode);
                bool gravityInverted_trail = (!gravityFlipped);
                if (isMini_trail && !gravityInverted_trail)
                {
                    playerY_px_trail += 4;  // Adjust for visual position offset
                }
                int playerWorldCenterY_px = playerY_px_trail + (playerVisualHeight / 2);
                // Record to appropriate path list based on which player is active
                if (currplayer == 0)
                    recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                else if (dual)
                    recordedPlayer2Path.Add((playerWorldCenterX_px, playerWorldCenterY_px));
            }
            catch { }
        }
    }
}




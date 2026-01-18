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
            // Get base physics values (always from down-gravity index)
            int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
            bool gravityInverted = (currplayer_gravity != 0);
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            tmpfallspeed = GameModePhysics.UFO_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
            tmpgravity = GameModePhysics.UFO_GRAVITY(baseTableIdx) * gravityMultiplier;
            AppendSimDebug($"[UFO] table_idx={currplayer_table_idx}, gravity={tmpgravity}, fallspeed={tmpfallspeed}");
            CommonGravityRoutine_Fresh();
            
            // If grounded with inverted gravity, prevent velocity from pulling into ceiling
            if (currplayer_gravity != 0) {
                int hitboxW = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH = (currplayer_mini != 0) ? 8 : 15;
                int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
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
                int baseJumpIdx = (currplayer_mini != 0 ? 4 : 0);
                bool jumpGravityInverted = (currplayer_gravity != 0);
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
                int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
            }
            catch { }
        }
    }
}

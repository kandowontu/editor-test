using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// Ninja physics - uses cube physics but with triple jump
        /// From gamemode_cube.h lines 70-140
        /// </summary>
        private void NinjaPhysics_Fresh()
        {
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
                var (orbActivated, _) = UpdateOrbSystem(8, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                   scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                   (currplayer_mini != 0), ref tempVelY);
                if (orbActivated)
                {
                    playerVelY_fixed = tempVelY;
                    AppendSimDebug($"[NINJA] Orb activated! New velY={playerVelY_fixed}");
                    
                    // Consume the X press if it was used for orb
                    if (pressJump_orb)
                        Interlocked.Exchange(ref keyXPressedCount, 0);
                }
                
                // Clear orb buffer when X is released
                if (!holdJump_orb)
                    ClearOrbBuffer();
            }
            
            // Get base physics values (always from down-gravity index)
            int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
            bool gravityInverted = (currplayer_gravity != 0);
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            // Ninja uses cube gravity and fallspeed
            tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(baseTableIdx, CUBE_MAX_FALLSPEED) * gravityMultiplier;
            tmpgravity = GameModePhysics.CUBE_GRAVITY(baseTableIdx) * gravityMultiplier;
            
            // Reset jumped flag at start of frame
            ninjaJumpedThisFrame = false;
            
            // Reset triple jump when grounded - be more strict to avoid resetting mid-jump
            // Only reset if velocity is near zero (not actively jumping)
            bool isGrounded = (playerVelY_fixed > -8 && playerVelY_fixed < 8);
            if (isGrounded) {
                ninjaJumps = 3;
                AppendSimDebug($"[NINJA] Grounded - reset jumps to 3");
            }
            
            // Read input
            bool holdJump = IsXDownAsync() || keyXHeld;
            int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
            bool pressJump = pressCount > 0;
            
            // Ninja can jump if:
            // 1. Grounded (vel_y == 0), OR
            // 2. In air with jumps remaining and not already jumped this frame
            if (pressJump && ninjaJumps > 0 && !ninjaJumpedThisFrame && !orbed && dashing == 0) {
                int baseJumpIdx = (currplayer_mini != 0 ? 4 : 0);
                bool jumpGravityInverted = (currplayer_gravity != 0);
                int jumpGravityMultiplier = jumpGravityInverted ? -1 : 1;
                playerVelY_fixed = GameModePhysics.JUMP_VEL(baseJumpIdx) * jumpGravityMultiplier;
                
                // Only decrement jumps if in air
                if (playerVelY_fixed != 0) {
                    ninjaJumps--;
                }
                
                ninjaJumpedThisFrame = true;
                AppendSimDebug($"[NINJA] Jump! Remaining={ninjaJumps}, vel={playerVelY_fixed}");
            }
            
            // Apply gravity (applies simTimeScale internally)
            CommonGravityRoutine_Fresh();
            
            // If grounded with inverted gravity, prevent velocity from pulling into ceiling
            if (currplayer_gravity != 0) {
                bool isMini_check = (currplayer_mini != 0);
                int hitboxW_check = isMini_check ? 8 : 15;
                int hitboxH_check = isMini_check ? 7 : 15;
                int hitboxOffsetY_check = isMini_check ? ((0x10 - hitboxH_check) >> 1) : 0;
                int collisionX_check = (playerX_fixed >> 8);
                int testY_check = (playerY_fixed >> 8) + hitboxOffsetY_check - 1;
                var (collided_check, _) = CheckCollisionUp(collisionX_check, testY_check, hitboxW_check, hitboxH_check);
                
                if (collided_check && playerVelY_fixed < 0) {
                    playerVelY_fixed = 0;
                    AppendSimDebug($"[NINJA] Ceiling grounded - zeroed velocity");
                }
            }
            
            // Collision
            CubeEject_Fresh();
            
            // Record trail
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerY_px_trail = playerY_fixed >> 8;
                // Apply mini mode offset for trail to match visual position
                bool isMini_trail = (currplayer_mini != 0);
                bool gravityInverted_trail = (currplayer_gravity != 0);
                if (isMini_trail && !gravityInverted_trail)
                {
                    playerY_px_trail += 8;
                }
                int playerWorldCenterY_px = playerY_px_trail + (playerVisualHeight / 2);
                recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
            }
            catch { }
        }
    }
}

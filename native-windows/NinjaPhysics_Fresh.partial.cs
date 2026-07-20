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
                int playerX_px_orb = (playerX_fixed >> 8) + 1;
                int playerY_px_orb = playerY_fixed >> 8;
                int hitboxW_orb = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH_orb = (currplayer_mini != 0) ? 7 : 15;
                
                // NES: Generic.y += ((0x10 - height) >> 1); Normal: +0, Mini: +4
if (currplayer_mini != 0)
                {
                    playerY_px_orb += 4;
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
            
            // Apply gravity (applies simTimeScale internally)
            CommonGravityRoutine_Fresh();
            
            // Collision
            byte gravityAtFrameStart = currplayer_gravity;
            CubeEject_Fresh();
            
            // Read input
            bool holdJump = IsXDownAsync() || keyXHeld || upHeld;
            int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
            bool pressJump = pressCount > 0;
            
            // Reset after cube_eject from the resulting zero velocity.
            bool ninjaGrounded = playerVelY_fixed == 0;
            if (ninjaGrounded)
            {
                ninjajumps[currplayer] = 3;
                AppendSimDebug($"[NINJA] Grounded - reset jumps to 3");
            }

            // Current gamemode_cube.h gives grounded Ninja the same held-input
            // buffer as cube. Air jumps use the fresh-press branch and decrement
            // ninjajumps; J/F blocks also force that branch.
            bool bufferedGroundJump = holdJump && !jblocked && !fblocked &&
                ninjaGrounded && !orbed[currplayer];
            bool freshNinjaJump = !bufferedGroundJump && pressJump &&
                (jblocked || fblocked ||
                 (ninjajumps[currplayer] > 0 && !ninjaJumpedThisFrame && !orbed[currplayer]));
            if (bufferedGroundJump || freshNinjaJump) {
                int baseJumpIdx = (currplayer_mini != 0 ? 4 : 0);
                bool jumpGravityInverted = (currplayer_gravity != 0);
                int jumpGravityMultiplier = jumpGravityInverted ? -1 : 1;
                playerVelY_fixed = GameModePhysics.JUMP_VEL(baseJumpIdx) * jumpGravityMultiplier;

                if (freshNinjaJump)
                    ninjajumps[currplayer] = (ninjajumps[currplayer] - 1) & 0xFF;
                
                // NES slope_jump_check: add extra velocity when jumping off a slope
                SlopeJumpCheck_Fresh();
                
                ninjaJumpedThisFrame = true;
                AppendSimDebug($"[NINJA] Jump! buffered={bufferedGroundJump}, remaining={ninjajumps[currplayer]}, vel={playerVelY_fixed}");
            }
            
            // NES x_movement_coll: decrement slope_frames + apply_slope_vel
            UpdateSlopeCounters_Fresh();
            
            // Record trail (skip during pathfinder speculative simulation)
            if (!pfSimulating)
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerY_px_trail = playerY_fixed >> 8;
                // Apply mini mode offset for trail to match visual position
                bool isMini_trail = (currplayer_mini != 0);
                if (isMini_trail)
                {
                    playerY_px_trail += 4;
                }
                int playerWorldCenterY_px = playerY_px_trail + (playerVisualHeight / 2);
                // Record to appropriate path list based on which player is active
                if (currplayer == 0)
                    recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                else if (dual)
                    RecordP2PathPoint(playerWorldCenterX_px, playerWorldCenterY_px);
            }
            catch { }
        }
    }
}



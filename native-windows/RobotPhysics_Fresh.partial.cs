using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// Robot physics - uses cube physics but with hold-to-jump mechanics
        /// From gamemode_cube.h lines 103-148
        /// </summary>
        private void RobotPhysics_Fresh()
        {
            // Check for orb activation FIRST (before any input consumption)
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
                if (currplayer_mini != 0 && !gravityInverted_orb)
                {
                    playerY_px_orb += 4;
                }
                
                int scrollX_px_orb = 0;
                
                int tempVelY = playerVelY_fixed;
                var (orbActivated, _) = UpdateOrbSystem(4, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                   scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                   (currplayer_mini != 0), ref tempVelY);
                if (orbActivated)
                {
                    playerVelY_fixed = tempVelY;
                    AppendSimDebug($"[ROBOT] Orb activated! New velY={playerVelY_fixed}");
                    
                    // NES does NOT consume the press after orb activation in robot mode.
                    // cube_data is read once at the top of cube_movement() and both orb
                    // activation and robot jump-start use the same snapshot.  After a
                    // gravity-flip orb, CubeEject can zero velY, and the robot jump-start
                    // check (press && velY==0) legitimately fires on the same frame.
                }
                
                // Clear orb buffer when X is released
                if (!holdJump_orb)
                    ClearOrbBuffer();
            }
            
            // Get base physics values (always from down-gravity index)
            int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
            bool gravityInverted = (currplayer_gravity != 0);
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            // Robot uses cube gravity and fallspeed
            tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(baseTableIdx, CUBE_MAX_FALLSPEED) * gravityMultiplier;
            tmpgravity = GameModePhysics.CUBE_GRAVITY(baseTableIdx) * gravityMultiplier;
            
            // Read input
            bool holdJump = IsXDownAsync() || keyXHeld;
            int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
            bool pressJump = pressCount > 0;
            
            // Robot jump logic: must press X to set flag, then hold to continue jumping
            if (pressJump) {
                robotJumpPressed = true;
                AppendSimDebug($"[ROBOT] Press detected - flag set");
            }
            
            AppendSimDebug($"[ROBOT] jumpPressed={robotJumpPressed}, holdJump={holdJump}, orbed={orbed[currplayer]}, dashing={dashing[currplayer]}");
            
            // Continue jump if timer active and holding
            if (robotJumpTime[0] > 0 && !orbed[currplayer] && dashing[currplayer] == 0) {
                robotJumpTime[0]--;
                if (holdJump) {
                    playerVelY_fixed = GameModePhysics.ROBOT_JUMP_VEL(baseTableIdx) * gravityMultiplier;
                    AppendSimDebug($"[ROBOT] Jump continue: vel={playerVelY_fixed}, time={robotJumpTime[0]}");
                } else {
                    robotJumpTime[0] = 0; // Released - stop jump
                    AppendSimDebug($"[ROBOT] Jump cancelled - released button");
                }
            }
            
            // Apply gravity (applies simTimeScale internally)
            // Famidash always applies gravity every frame, even on pad/orb hit frames
            CommonGravityRoutine_Fresh();
            
            // Ceiling proximity check (same as cube — needed for flipped gravity).
            // After gravity, if gravity is flipped and player is moving toward ceiling,
            // check collision at Y-1 to detect boundary hits that CubeEject would miss
            // (CheckCeiling uses strict < comparison, so playerTop == ceilBottom misses).
            if (currplayer_gravity != 0)
            {
                bool isMini_check = (currplayer_mini != 0);
                int hitboxW_check = isMini_check ? 8 : 15;
                int hitboxH_check = isMini_check ? 7 : 15;
                int hitboxOffsetY_check = SharedPhysics.GetMiniCenterOffsetY(isMini_check);
                int collisionX_check = (playerX_fixed >> 8);
                int testY_check = (playerY_fixed >> 8) + hitboxOffsetY_check - 1;
                var (collided_check, collisionBottomY_check) = CheckCollisionUp(collisionX_check, testY_check, hitboxW_check, hitboxH_check);
                
                if (collided_check && playerVelY_fixed < 0)
                {
                    int newY_prox = collisionBottomY_check - hitboxOffsetY_check - 1;
                    playerY_fixed = newY_prox << 8;
                    playerVelY_fixed = 0;
                }
            }
            
            // Collision
            byte gravityAtFrameStart = currplayer_gravity;
            CubeEject_Fresh();
            
            // Robot jump start - check after collision (when we know if grounded)
            if (playerVelY_fixed == 0 && robotJumpPressed && !orbed[currplayer] && dashing[currplayer] == 0) {
                robotJumpPressed = false; // Clear flag
                if (holdJump) {
                    if (pressJump) {
                        // Just pressed - start jump
                        playerVelY_fixed = GameModePhysics.ROBOT_JUMP_VEL(baseTableIdx) * gravityMultiplier;
                        robotJumpTime[0] = 19; // ROBOT_JUMP_TIME for 60fps (0x13)
                        // NES slope_jump_check: add extra velocity when jumping off a slope
                        SlopeJumpCheck_Fresh();
                        AppendSimDebug($"[ROBOT] Jump started: vel={playerVelY_fixed}, time={robotJumpTime[0]}");
                    }
                }
            }
            
            // NES x_movement_coll: decrement slope_frames + apply_slope_vel
            UpdateSlopeCounters_Fresh();
            
            // Clear alphabet block flags at end of movement (gamemode_cube.h line 161-163).
            // These are set by ProcessSprites each frame and consumed during physics.
            // Must clear here — cube mode clears in CubePhysics_Fresh but robot mode
            // needs its own clearing since it's a separate function.
            jblocked = false;
            fblocked = false;
            hblocked = false;
            
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



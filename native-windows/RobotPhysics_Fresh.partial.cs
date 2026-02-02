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
                var (orbActivated, _) = UpdateOrbSystem(3, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                   scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                   (currplayer_mini != 0), ref tempVelY);
                if (orbActivated)
                {
                    playerVelY_fixed = tempVelY;
                    AppendSimDebug($"[ROBOT] Orb activated! New velY={playerVelY_fixed}");
                    
                    // Set orbed flag for robot mode (matches famidash line 432)
                    orbed = true;
                    
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
            
            // Check if grounded with collision check (test 1 pixel in gravity direction)
            bool isGrounded = false;
            bool isMini = (currplayer_mini != 0);
            int hitboxW = isMini ? 8 : 15;
            int hitboxH = isMini ? 7 : 15;
            int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
            int collisionX = (playerX_fixed >> 8);
            
            if (currplayer_gravity == 0) {
                // Normal gravity - check 1 pixel down
                int testY = (playerY_fixed >> 8) + 1 + hitboxOffsetY;
                var (collided, _) = CheckCollisionDown(collisionX, testY, hitboxW, hitboxH);
                isGrounded = collided;
            } else {
                // Inverted gravity - check 1 pixel up
                int testY = (playerY_fixed >> 8) - 1 + hitboxOffsetY;
                var (collided, _) = CheckCollisionUp(collisionX, testY, hitboxW, hitboxH);
                isGrounded = collided;
            }
            
            AppendSimDebug($"[ROBOT] grounded={isGrounded}, jumpPressed={robotJumpPressed}, holdJump={holdJump}, orbed={orbed}, dashing={dashing}");
            
            if (isGrounded && robotJumpPressed && !orbed && dashing == 0) {
                robotJumpPressed = false; // Clear flag
                if (holdJump) {
                    if (pressJump) {
                        // Just pressed - start jump
                        playerVelY_fixed = GameModePhysics.ROBOT_JUMP_VEL(baseTableIdx) * gravityMultiplier;
                        robotJumpTime[0] = 19; // ROBOT_JUMP_TIME for 60fps (0x13)
                        AppendSimDebug($"[ROBOT] Jump started: vel={playerVelY_fixed}, time={robotJumpTime[0]}");
                    }
                }
            }
            // Continue jump if timer active and holding
            else if (robotJumpTime[0] > 0 && !orbed && dashing == 0) {
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
                    AppendSimDebug($"[ROBOT] Ceiling grounded - zeroed velocity");
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

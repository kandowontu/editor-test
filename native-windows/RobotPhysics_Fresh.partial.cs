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
            
            // Grounding check - velocity-based only (matches Cube and Football)
            // Only use velocity to determine if grounded. Collision detection will handle actual grounding
            bool isGrounded = (playerVelY_fixed >= -16 && playerVelY_fixed <= 16);
            onGround = isGrounded;
            
            AppendSimDebug($"[ROBOT] grounded={isGrounded}, jumpPressed={robotJumpPressed}, holdJump={holdJump}, orbed={orbed}, dashing={dashing}");
            
            // Note: Unlike earlier code, we do NOT zero velocity when grounded (matches Cube behavior)
            // Gravity will be applied naturally, and collision detection will handle grounding
            
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
            // BUT skip gravity acceleration if a pad/orb was just hit this frame - let the pad velocity apply first
            if (!orbhitonthisframe)
            {
                CommonGravityRoutine_Fresh();
            }
            else
            {
                // Pad/orb hit: skip gravity acceleration but still integrate velocity into position
                AppendSimDebug($"[ROBOT] Pad/orb hit this frame - skipping gravity acceleration but integrating velocity. velY=0x{playerVelY_fixed:X4}");
                playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                AppendSimDebug($"[ROBOT] Position integrated: posY=0x{playerY_fixed:X4} ({playerY_fixed >> 8}px)");
            }
            
            // Collision
            byte gravityAtFrameStart = currplayer_gravity;
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
                    playerY_px_trail += 4;  // Adjust for visual position offset
                }
                int playerWorldCenterY_px = playerY_px_trail + (playerVisualHeight / 2);
                recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
            }
            catch { }
        }
    }
}

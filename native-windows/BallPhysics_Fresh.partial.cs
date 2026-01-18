using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// ball_movement() from gamemode_ball.h - 1:1 port
        /// Handles both ball and swing modes
        /// </summary>
        private void BallPhysics_Fresh()
        {
            // Get base physics values (always from down-gravity index)
            int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
            bool gravityInverted = (currplayer_gravity != 0);
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            if (currentGameMode == 7) { // GAMEMODE_SWING
                tmpfallspeed = GameModePhysics.SWING_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
                tmpgravity = GameModePhysics.SWING_GRAVITY(baseTableIdx) * gravityMultiplier;
            } else {
                tmpfallspeed = GameModePhysics.BALL_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
                tmpgravity = GameModePhysics.BALL_GRAVITY(baseTableIdx) * gravityMultiplier;
            }
            
            // Ball gravity flip logic - BEFORE gravity/collision
            if (currentGameMode == 2) { // GAMEMODE_BALL
                // Read input - use press instead of hold
                int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
                bool pressJump = pressCount > 0;
                
                // Check if grounded by doing a collision check
                int savedY = playerY_fixed;
                
                // Check collision by testing 1 pixel in gravity direction
                bool isGrounded = false;
                int hitboxW = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH = (currplayer_mini != 0) ? 8 : 15;
                int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
                int collisionX = (playerX_fixed >> 8);
                
                if (currplayer_gravity == 0) {
                    // Normal gravity - check 1 pixel down
                    int testY = (playerY_fixed >> 8) + hitboxH + hitboxOffsetY + 1;
                    var (collided, _) = CheckCollisionDown(collisionX, testY, hitboxW, hitboxH);
                    isGrounded = collided;
                    AppendSimDebug($"[BALL] Grounded check (normal): Y={savedY >> 8}, testY={testY}, isGrounded={isGrounded}");
                } else {
                    // Inverted gravity - check 1 pixel up from top of hitbox
                    int testY = (playerY_fixed >> 8) + hitboxOffsetY - 1;
                    var (collided, collisionBottomY) = CheckCollisionUp(collisionX, testY, hitboxW, hitboxH);
                    isGrounded = collided;
                    AppendSimDebug($"[BALL] Grounded check (inverted): Y={savedY >> 8}, testY={testY}, collisionBottomY={collisionBottomY}, isGrounded={isGrounded}");
                }
                
                AppendSimDebug($"[BALL] press={pressJump}, ballSwitched={ballSwitched[0]}, velY={playerVelY_fixed}, grounded={isGrounded}");
                
                // Ball flips gravity when pressing X and grounded
                if (pressJump && !ballSwitched[0] && isGrounded) {
                    AppendSimDebug($"[BALL] FLIPPING GRAVITY!");
                    InvertGravity_Fresh();
                    UpdateCurrplayerTableIdx_Fresh(); // Must update table_idx AFTER gravity flip!
                    
                    // Recalculate physics values with new gravity
                    gravityInverted = (currplayer_gravity != 0);
                    gravityMultiplier = gravityInverted ? -1 : 1;
                    tmpfallspeed = GameModePhysics.BALL_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
                    tmpgravity = GameModePhysics.BALL_GRAVITY(baseTableIdx) * gravityMultiplier;
                    
                    ballSwitched[0] = true;
                    playerVelY_fixed = GameModePhysics.BALL_SWITCH_VEL(currplayer_table_idx);
                    
                    // Skip collision checks for 2 frames after flip to prevent stutter
                    ballFlipCooldown = 2;
                }
                
                if (ballSwitched[0] && pressCount == 0) {
                    ballSwitched[0] = false;
                }
            }
            
            CommonGravityRoutine_Fresh();
            
            // If grounded with inverted gravity, prevent velocity from pulling into ceiling
            if (currentGameMode == 2 && currplayer_gravity != 0) {
                // Re-check if grounded (after gravity has been applied)
                int hitboxW = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH = (currplayer_mini != 0) ? 8 : 15;
                int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
                int collisionX = (playerX_fixed >> 8);
                int testY = (playerY_fixed >> 8) + hitboxOffsetY - 1;
                var (collided, _) = CheckCollisionUp(collisionX, testY, hitboxW, hitboxH);
                
                if (collided && playerVelY_fixed < 0) {
                    // Grounded on ceiling, prevent upward velocity accumulation
                    playerVelY_fixed = 0;
                    AppendSimDebug($"[BALL] Ceiling grounded - zeroed velocity");
                }
            }
            
            // Decrement cooldown
            if (ballFlipCooldown > 0) {
                ballFlipCooldown--;
            }
            
            // Only do collision ejection if not in cooldown
            if (ballFlipCooldown == 0) {
                BallEject_Fresh();
            }
            
            // Swing gravity flip logic
            if (currentGameMode == 7) { // GAMEMODE_SWING
                int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
                bool pressedJump = pressCount > 0;
                
                if (pressedJump && !ufoOrbed) {
                    InvertGravity_Fresh();
                    UpdateCurrplayerTableIdx_Fresh(); // Must update table_idx AFTER gravity flip!
                    // Swing does NOT apply velocity like Ball does - just flips gravity
                }
            }
            
            // Record position for trail
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
                int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2) + hitboxOffsetY;
                recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
            }
            catch { }
        }
        
        /// <summary>
        /// ball_eject() helper
        /// </summary>
        private void BallEject_Fresh()
        {
            int hitboxW = (currplayer_mini != 0) ? 8 : 15;
            int hitboxH = (currplayer_mini != 0) ? 8 : 15;
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
            int collisionX = (playerX_fixed >> 8);
            int collisionY = (playerY_fixed >> 8) + hitboxOffsetY;
            
            if (currplayer_gravity == 0) {
                // Skip downward collision if we just flipped and are moving up
                if (playerVelY_fixed >= 0) {
                    var (collided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                    if (collided) {
                        // Calculate where player Y should be so that bottom lands correctly
                        // Player bottom = playerY + hitboxOffsetY + hitboxH
                        // We want: playerY + hitboxOffsetY + hitboxH = collisionTopY - 1
                        // So: playerY = collisionTopY - 1 - hitboxOffsetY - hitboxH
                        int newY = collisionTopY - hitboxH - hitboxOffsetY - 1;
                        int oldY = playerY_fixed >> 8;
                        AppendSimDebug($"[BALL_EJECT_D] collisionTopY={collisionTopY}, hitboxH={hitboxH}, hitboxOffsetY={hitboxOffsetY}, oldY={oldY}, newY={newY}");
                        playerY_fixed = (newY << 8);
                        playerVelY_fixed = 0;
                    }
                }
            } else {
                // Skip upward collision if we just flipped and are moving down
                if (playerVelY_fixed <= 0) {
                    var (collided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                    if (collided) {
                        // Place player directly at collision surface
                        int newY = collisionBottomY - hitboxOffsetY;
                        int oldY = playerY_fixed >> 8;
                        AppendSimDebug($"[BALL_EJECT_U] collisionBottomY={collisionBottomY}, hitboxOffsetY={hitboxOffsetY}, oldY={oldY}, newY={newY}");
                        playerY_fixed = (newY << 8);
                        playerVelY_fixed = 0;
                    }
                }
            }
        }
        
        /// <summary>
        /// Helper to invert gravity
        /// </summary>
        private void InvertGravity_Fresh()
        {
            currplayer_gravity = (byte)(currplayer_gravity == 0 ? 0xFF : 0);
            UpdateCurrplayerTableIdx_Fresh();
        }
        
        private void UpdateCurrplayerTableIdx_Fresh()
        {
            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
        }
    }
}

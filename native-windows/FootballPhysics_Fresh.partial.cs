using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private int footballChargeFrames = 0;
        private bool footballOrbed = false;
        private bool footballWasHeld = false;  // Track previous frame's hold state
        
        /// <summary>
        /// Football mode - Cube physics with charge on release mechanic
        /// Applies Cube gravity via CommonGravityRoutine_Fresh, then handles charge/jump logic
        /// Hold X to charge (max 50 frames before overcharge), max chargepower at 45 frames
        /// Frames 1-45: charging increases power, frames 46-50: power caps at frame 45 level
        /// Velocity = min(chargeFrames, 45) * 0x004C (if gravity inverted) or min(chargeFrames, 45) * -0x004C (if gravity normal)
        /// Has permanent headbonking like H block (ejects from ceiling with velocity instead of zero)
        /// </summary>
        private void FootballPhysics_Fresh()
        {
            int baseTableIdx = (miniMode ? 4 : 0);
            bool gravityInverted = gravityFlipped;
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            // Use Cube gravity/fallspeed
            tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(baseTableIdx, CUBE_MAX_FALLSPEED) * gravityMultiplier;
            tmpgravity = GameModePhysics.CUBE_GRAVITY(baseTableIdx) * gravityMultiplier;
            
            // Apply gravity using common routine
            // BUT skip gravity if a pad/orb was just hit this frame - let the pad velocity apply first
            if (!orbhitonthisframe[currplayer])
            {
                CommonGravityRoutine_Fresh();
            }
            else
            {
                AppendSimDebug($"[FOOTBALL] Pad/orb hit this frame - skipping gravity. velY=0x{playerVelY_fixed:X4}");
            }
            
            // Football always has headbonking enabled (like H block)
            // This will be handled in CubeEject_Fresh by temporarily setting hblocked
            hblocked = true;
            
            // Handle collision detection like Cube does
            CubeEject_Fresh();
            
            // Clear hblocked flag at end of frame
            hblocked = false;
            
            // Charge mechanic (matching gamemode_cube.h logic)
            bool xHeld = IsXDownAsync() || keyXHeld;
            
            AppendSimDebug($"[FOOTBALL] xHeld={xHeld}, wasHeld={footballWasHeld}, chargeFrames={footballChargeFrames}, orbed={footballOrbed}");
            
            // While holding X and not already orbed, accumulate charge
            if (xHeld && !footballOrbed)
            {
                footballChargeFrames++;
                AppendSimDebug($"[FOOTBALL] Charging: {footballChargeFrames}/50 (power maxes at 45)");
                
                if (footballChargeFrames >= 50)
                {
                    footballOrbed = true;  // Hit max charge frames - can't charge anymore
                    AppendSimDebug($"[FOOTBALL] MAX CHARGE - orbed=true, charge capped at frame 45 power");
                }
                footballWasHeld = true;
            }
            // When X transitions from held to released
            else if (!xHeld && footballWasHeld)
            {
                AppendSimDebug($"[FOOTBALL] RELEASE DETECTED: chargeFrames={footballChargeFrames}, wasHeld={footballWasHeld}, xHeld={xHeld}");
                
                footballOrbed = false;  // Clear orbed state on release (allows recharging next time)
                footballWasHeld = false;  // Mark that we've processed this release
                
                // Use existing onGround state instead of rechecking collisions
                AppendSimDebug($"[FOOTBALL] Released: chargeFrames={footballChargeFrames}, grounded={Math.Abs(playerVelY_fixed) <= 0x006B}, velY_before=0x{playerVelY_fixed:X4}");
                
                // Apply jump velocity ONLY if:
                // 1. Charged (chargeFrames > 0)
                // 2. NOT overcharged (chargeFrames < 50) - at 50 frames the charge is dead
                // 3. On ground
                if (footballChargeFrames > 0 && footballChargeFrames < 50 && Math.Abs(playerVelY_fixed) <= 0x006B)
                {
                    // Clamp charge power to 45 frames max (frames 46-50 don't add more power, and 50 is overcharge = no jump)
                    int effectiveCharge = Math.Min(footballChargeFrames, 45);
                    int chargeVelocity = effectiveCharge * 0x004C;
                    // Apply velocity with proper sign based on gravity direction
                    playerVelY_fixed = gravityInverted ? chargeVelocity : -chargeVelocity;
                    AppendSimDebug($"[FOOTBALL] JUMP! chargeFrames={footballChargeFrames}, effectiveCharge={effectiveCharge}, velocity_set=0x{playerVelY_fixed:X4}");
                }
                else if (footballChargeFrames == 0)
                {
                    AppendSimDebug($"[FOOTBALL] NO CHARGE - not jumping");
                }
                else if (footballChargeFrames >= 50)
                {
                    AppendSimDebug($"[FOOTBALL] OVERCHARGED (>=50) - charge is dead, no jump");
                }
                else if (Math.Abs(playerVelY_fixed) > 0x006B)
                {
                    AppendSimDebug($"[FOOTBALL] NOT ON GROUND (velY=0x{playerVelY_fixed:X4}) - not jumping");
                }
                
                footballChargeFrames = 0;  // Reset charge on release
            }
            else if (!xHeld)
            {
                // Still not holding X - keep wasHeld false
                footballWasHeld = false;
            }
            
            // Record position for trail (skip during pathfinder speculative simulation)
            if (!pfSimulating)
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




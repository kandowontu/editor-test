using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// ship_movement() from gamemode_ship.h - 1:1 port
        /// </summary>
        private void ShipPhysics_Fresh()
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
                if (currplayer_mini != 0 && !gravityInverted_orb)
                {
                    playerY_px_orb += 4;
                }
                
                int scrollX_px_orb = 0;
                
                int tempVelY = playerVelY_fixed;
                var (orbActivated, _) = UpdateOrbSystem(1, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                   scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                   (currplayer_mini != 0), ref tempVelY);
                if (orbActivated)
                {
                    playerVelY_fixed = tempVelY;
                    AppendSimDebug($"[SHIP] Orb activated! New velY={playerVelY_fixed}");
                    
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
            
            // is_player_falling()
            bool tmp1 = currplayer_gravity != 0 ? (playerVelY_fixed < 0) : (playerVelY_fixed > 0);
            
            // tmp2 = input (holding A or UP) - use same system as cube
            bool tmp2 = IsXDownAsync() || keyXHeld;
            
            if (tmp2) { // holding
                tmpgravity = (currplayer_mini != 0 ? GameModePhysics.MINI_SHIP_GRAVITY_BASE : GameModePhysics.SHIP_GRAVITY_BASE) * gravityMultiplier;
            } else if (!tmp2 && !tmp1) { // not holding and not falling
                tmpgravity = (currplayer_mini != 0 ? GameModePhysics.MINI_SHIP_GRAVITY_AFTER_HOLD : GameModePhysics.SHIP_GRAVITY_AFTER_HOLD) * gravityMultiplier;
            } else {
                tmpgravity = GameModePhysics.SHIP_GRAVITY(baseTableIdx) * gravityMultiplier;
            }
            
            if (tmp2 && tmp1) { // holding and falling
                tmpgravity = (currplayer_mini != 0 ? GameModePhysics.MINI_SHIP_GRAVITY_HOLD_FALL : GameModePhysics.SHIP_GRAVITY_HOLD_FALL) * gravityMultiplier;
            }
            
            // Negate gravity when holding input (thrust in opposite direction)
            // Don't check currplayer_gravity here since we already applied gravityMultiplier
            if (tmp2) {
                tmpgravity = -tmpgravity;
            }
            
            tmpfallspeed = GameModePhysics.SHIP_MAX_FALLSPEED_HOLD(baseTableIdx) * gravityMultiplier;
            
            CommonGravityRoutine_Fresh();
            
            // If grounded with inverted gravity, prevent velocity from pulling into ceiling
            if (currplayer_gravity != 0) {
                bool isMini = (currplayer_mini != 0);
                int hitboxW = isMini ? 8 : 15;
                int hitboxH = isMini ? 7 : 15;
                int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
                int collisionX = (playerX_fixed >> 8);
                int testY = (playerY_fixed >> 8) + hitboxOffsetY - 1;
                var (collided, collisionBottomY_prox) = CheckCollisionUp(collisionX, testY, hitboxW, hitboxH);
                
                if (collided && playerVelY_fixed < 0) {
                    // Snap Y to ceiling surface (same formula as UfoShipEject_Fresh)
                    // Prevents sub-pixel drift at exact boundary conditions.
                    int newY_prox = collisionBottomY_prox - hitboxOffsetY;
                    playerY_fixed = newY_prox << 8;
                    playerVelY_fixed = 0;
                    AppendSimDebug($"[SHIP] Ceiling grounded - snapped Y to {newY_prox}");
                }
            }
            
            // Max speed clamping (apply gravity multiplier to the limits)
            int maxUpSpeed = -GameModePhysics.SHIP_MAX_FALLSPEED_HOLD(baseTableIdx) * gravityMultiplier;   // Going up (against gravity) - hold speed
            int maxDownSpeed = GameModePhysics.SHIP_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;  // Going down (with gravity) - normal speed
            
            if (gravityMultiplier > 0) {
                // Normal gravity: up is negative, down is positive
                if (playerVelY_fixed < maxUpSpeed) playerVelY_fixed = maxUpSpeed;
                if (playerVelY_fixed > maxDownSpeed) playerVelY_fixed = maxDownSpeed;
            } else {
                // Inverted gravity: up is positive, down is negative
                if (playerVelY_fixed > maxUpSpeed) playerVelY_fixed = maxUpSpeed;
                if (playerVelY_fixed < maxDownSpeed) playerVelY_fixed = maxDownSpeed;
            }
            
            UfoShipEject_Fresh();

            // Update slope exit velocity counters (NES: called after eject)
            UpdateSlopeCounters_Fresh();
        }
        
        /// <summary>
        /// ufo_ship_eject() from gamemode_ship.h
        /// </summary>
        private void UfoShipEject_Fresh()
        {
            bool isMini = (currplayer_mini != 0);
            int hitboxW = isMini ? 8 : 15;
            int hitboxH = isMini ? 7 : 15;
            int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
            int collisionX = (playerX_fixed >> 8);
            int collisionY = (playerY_fixed >> 8) + hitboxOffsetY;

            // Update slope counters each frame
            UpdateSlopeCounters();
            
            // NES bg_coll_U: check ceiling slopes BEFORE flat ceiling collision
            bool ceilingSlopeHit = bg_coll_U_slopes();
            if (ceilingSlopeHit)
            {
                // NES: high_byte(Y) = high_byte(Y) - eject_U - 1
                // eject_U = -tmp8, so this becomes Y_px = Y_px + tmp8 - 1 (push down, away from ceiling slope)
                int newPixelY = (playerY_fixed >> 8) - eject_U - 1;
                playerY_fixed = newPixelY << 8;
                playerVelY_fixed = 0;
                AppendSimDebug($"[SHIP] Ceiling slope eject: eject_U={eject_U}, newY={newPixelY}");
            }
            else
            {
                // NES ufo_ship_eject: flat ceiling check, NO velocity guard
                var (collidedUp, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                if (collidedUp) {
                    playerY_fixed = ((collisionBottomY - hitboxOffsetY) << 8);
                    playerVelY_fixed = 0;
                }
            }

            // Check slopes BEFORE flat downward collision
            bool slopeHit = bg_coll_D_slopes();
            if (slopeHit)
            {
                if (eject_D > 0)
                {
                    int newPixelY = (playerY_fixed >> 8) - eject_D;
                    playerY_fixed = newPixelY << 8;
                }
                playerVelY_fixed = 0;
                // NOTE: Ship does NOT set wasZeroedByCollisionLastFrame — GRAV_SKIP would block thrust
            }
            else
            {
                // NES ufo_ship_eject: NO velocity guard on floor eject
                var (collidedDown, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                if (collidedDown) {
                    playerY_fixed = ((collisionTopY - hitboxH - hitboxOffsetY) << 8);
                    playerVelY_fixed = 0;
                }
                // NOTE: No wasZeroed changes — ship never managed this flag
            }
            
            // Record position for trail (skip during pathfinder speculative simulation)
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



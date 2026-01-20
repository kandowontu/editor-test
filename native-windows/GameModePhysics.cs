using System;

namespace FamidashEditor
{
    /// <summary>
    /// All game mode physics constants from famidash/SAUCE/defines/physics_defines.h
    /// These are the 60fps constants used by Famidash
    /// </summary>
    public static class GameModePhysics
    {
        // Get table index from gravity and mini flags
        public static int GetTableIdx(bool gravityUp, bool mini)
        {
            // Not used with new direct constants, kept for compatibility
            return (gravityUp ? 1 : 0) | (mini ? 4 : 0);
        }

        #region CUBE MODE
        // JUMP_VEL - Cube jump velocity
        public static int JUMP_VEL(int table_idx)
        {
            bool mini = (table_idx & 4) != 0;
            return mini ? -0x4D0 : -0x590;
        }

        // CUBE_MAX_FALLSPEED
        // NOTE: The actual max fall speed is level-configurable (0x06 or 0x07)
        // Callers should pass the configured CUBE_MAX_FALLSPEED value
        public static int CUBE_MAX_FALLSPEED(int table_idx, int configuredMaxFallSpeed)
        {
            return configuredMaxFallSpeed; // Use level-configured value (0x600 or 0x700)
        }

        // CUBE_GRAVITY
        public static int CUBE_GRAVITY(int table_idx)
        {
            bool mini = (table_idx & 4) != 0;
            return mini ? 0x6F : 0x6B;
        }
        #endregion

        #region ROBOT MODE
        // ROBOT_JUMP_VEL - Robot jump velocity
        public static int ROBOT_JUMP_VEL(int table_idx)
        {
            return -0x2B0; // Same for normal and mini
        }

        // ROBOT_JUMP_TIME - How long robot can hold jump
        public const int ROBOT_JUMP_TIME = 19;

        // Robot uses CUBE_GRAVITY and CUBE_MAX_FALLSPEED
        #endregion

        #region SHIP MODE
        // SHIP_GRAVITY - 60fps values (indices 4-7)
        // [0x30, 0xD0, 0x39, 0xC7, 0x22, 0xDE, 0x27, 0xD9] (lo)
        // [0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0xFF] (hi)
        public static int SHIP_GRAVITY(int table_idx)
        {
            // For 60fps: use indices 4-7
            // table_idx 0 = 60fps normal, 1 = 60fps inverted, 4 = 60fps mini normal, 5 = 60fps mini inverted
            // Map to actual array indices 4, 5, 6, 7
            int actualIdx = 4 + (table_idx & 3);
            int[] values = { 0x30, unchecked((short)0xFFD0), 0x39, unchecked((short)0xFFC7), 0x22, unchecked((short)0xFFDE), 0x27, unchecked((short)0xFFD9) };
            return values[actualIdx];
        }

        // SHIP_MAX_FALLSPEED - 60fps values
        // [0x69, 0x97, 0x03, 0xFD, 0xD7, 0x29, 0x57, 0xA9] (lo)
        // [0x03, 0xFC, 0x04, 0xFB, 0x02, 0xFD, 0x03, 0xFC] (hi)
        public static int SHIP_MAX_FALLSPEED(int table_idx)
        {
            int actualIdx = 4 + (table_idx & 3);
            int[] values = { 0x0369, unchecked((short)0xFC97), 0x0403, unchecked((short)0xFBFD), 0x02D7, unchecked((short)0xFD29), 0x0357, unchecked((short)0xFCA9) };
            return values[actualIdx];
        }

        // SHIP_MAX_FALLSPEED_HOLD - 60fps values
        // [0x43, 0xBD, 0x03, 0xFD, 0x8D, 0x73, 0x2D, 0xD3] (lo)
        // [0x04, 0xFB, 0x05, 0xFA, 0x03, 0xFC, 0x04, 0xFB] (hi)
        public static int SHIP_MAX_FALLSPEED_HOLD(int table_idx)
        {
            int actualIdx = 4 + (table_idx & 3);
            int[] values = { 0x0443, unchecked((short)0xFBBD), 0x0503, unchecked((short)0xFAFD), 0x038D, unchecked((short)0xFC73), 0x042D, unchecked((short)0xFBD3) };
            return values[actualIdx];
        }

        // Ship gravity variants - 60fps values (not inverted, use table lookup)
        // SHIP_GRAVITY_BASE: [0x3C, 0xC4, 0x47, 0xB9, 0x2A, 0xD6, 0x31, 0xCF]
        public const int SHIP_GRAVITY_BASE = 0x2A;           // Index 4: 60fps normal
        public const int MINI_SHIP_GRAVITY_BASE = 0x31;      // Index 6: 60fps mini
        // SHIP_GRAVITY_AFTER_HOLD: [0x49, 0xB7, 0x55, 0xAB, 0x32, 0xCE, 0x3B, 0xC5]
        public const int SHIP_GRAVITY_AFTER_HOLD = 0x32;     // Index 4: 60fps normal
        public const int MINI_SHIP_GRAVITY_AFTER_HOLD = 0x3B; // Index 6: 60fps mini
        // SHIP_GRAVITY_HOLD_FALL: [0x4C, 0xB4, 0x59, 0xA7, 0x34, 0xCC, 0x3E, 0xC2]
        public const int SHIP_GRAVITY_HOLD_FALL = 0x34;     // Index 4: 60fps normal
        public const int MINI_SHIP_GRAVITY_HOLD_FALL = 0x3E; // Index 6: 60fps mini
        #endregion

        #region BALL MODE
        // BALL_GRAVITY
        public static int BALL_GRAVITY(int table_idx)
        {
            bool mini = (table_idx & 4) != 0;
            return mini ? 0x57 : 0x47;
        }

        // BALL_MAX_FALLSPEED
        public static int BALL_MAX_FALLSPEED(int table_idx)
        {
            return 0x600; // Same for normal and mini
        }

        // BALL_SWITCH_VEL - Velocity when switching gravity
        // Returns signed velocity: negative for upward (inverted gravity), positive for downward (normal gravity)
        public static int BALL_SWITCH_VEL(int table_idx)
        {
            bool mini = (table_idx & 4) != 0;
            bool inverted = (table_idx & 1) != 0;
            int baseVel = mini ? 0x120 : 0x200;
            return inverted ? -baseVel : baseVel;
        }
        #endregion

        #region UFO MODE
        // UFO_JUMP_VEL - Velocity on each click
        public static int UFO_JUMP_VEL(int table_idx)
        {
            bool mini = (table_idx & 4) != 0;
            return mini ? -0x2D0 : -0x330;
        }

        // UFO_GRAVITY
        public static int UFO_GRAVITY(int table_idx)
        {
            return 0x32; // Same for normal and mini
        }

        // UFO_MAX_FALLSPEED
        public static int UFO_MAX_FALLSPEED(int table_idx)
        {
            bool mini = (table_idx & 4) != 0;
            return mini ? 0x350 : 0x320;
        }
        #endregion

        #region SPIDER MODE
        // SPIDER_GRAVITY
        public static int SPIDER_GRAVITY(int table_idx)
        {
            return 0x4B; // Same for normal and mini
        }

        // SPIDER_MAX_FALLSPEED
        public static int SPIDER_MAX_FALLSPEED(int table_idx)
        {
            return 0x600; // Same for normal and mini
        }
        #endregion

        #region WAVE MODE
        // Wave uses currplayer_vel_x for movement (velocity = +/- vel_x based on input)
        // No traditional gravity/fallspeed
        #endregion

        #region SWING MODE (Swingcopter)
        // SWING_GRAVITY
        public static int SWING_GRAVITY(int table_idx)
        {
            bool mini = (table_idx & 4) != 0;
            return mini ? 0x38 : 0x32;
        }

        // SWING_MAX_FALLSPEED
        public static int SWING_MAX_FALLSPEED(int table_idx)
        {
            return 0x430; // Same for normal and mini
        }
        #endregion

        #region NINJA MODE
        // Ninja uses CUBE physics with triple jump counter
        // ninjajumps[] = 3 when grounded, decrements on each air jump
        #endregion
    }
}

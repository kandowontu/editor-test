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
        public static int CUBE_MAX_FALLSPEED(int table_idx)
        {
            return 0x600; // Same for normal and mini
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
        // SHIP_GRAVITY
        public static int SHIP_GRAVITY(int table_idx)
        {
            bool mini = (table_idx & 4) != 0;
            return mini ? 0x2C : 0x2A;
        }

        // SHIP_MAX_FALLSPEED
        public static int SHIP_MAX_FALLSPEED(int table_idx)
        {
            return 0x380; // Same for normal and mini
        }

        // Ship gravity variants (not indexed by table)
        public const int SHIP_GRAVITY_BASE = 0x2A;
        public const int MINI_SHIP_GRAVITY_BASE = 0x2C;
        public const int SHIP_GRAVITY_AFTER_HOLD = 0x2A;
        public const int MINI_SHIP_GRAVITY_AFTER_HOLD = 0x2C;
        public const int SHIP_GRAVITY_HOLD_FALL = 0x2A;
        public const int MINI_SHIP_GRAVITY_HOLD_FALL = 0x2C;
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

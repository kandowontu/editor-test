using System;

namespace FamidashEditor
{
    /// <summary>
    /// Shared physics routines used by both the Simulator (SimulatorWindow) and
    /// the Pathfinder (PathfinderEngine).  By calling the same static methods,
    /// both code-paths produce identical physics results and cannot drift apart.
    /// </summary>
    internal static class SharedPhysics
    {
        // ════════════════════════════════════════════════════════════════════
        //  TILE CONSTANT
        // ════════════════════════════════════════════════════════════════════
        internal const int TILE = 16;

        // ════════════════════════════════════════════════════════════════════
        //  COLLISION MAP (lightweight struct for tile map access)
        // ════════════════════════════════════════════════════════════════════
        internal readonly struct CollisionMap
        {
            public readonly int[] Tiles;
            public readonly int MapWidth;
            public readonly int MapHeight;
            public readonly int GroundRowsToReserve;
            public CollisionMap(int[] tiles, int mapWidth, int mapHeight, int groundRowsToReserve)
            {
                Tiles = tiles;
                MapWidth = mapWidth;
                MapHeight = mapHeight;
                GroundRowsToReserve = groundRowsToReserve;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  CUBE CONSTANTS  (from physics_table_defines.cmp.h / physics_defines.h)
        // ════════════════════════════════════════════════════════════════════
        internal const int CUBE_GRAVITY_NORMAL = 0x6B;
        internal const int CUBE_GRAVITY_MINI   = 0x6F;
        internal const int CUBE_MAX_FALLSPEED  = 0x0600;
        internal const int JUMP_VEL_NORMAL     = -0x590;
        internal const int JUMP_VEL_MINI       = -0x4D0;

        internal const int CUBE_HITBOX_W      = 15;
        internal const int CUBE_HITBOX_H      = 15;
        internal const int MINI_CUBE_HITBOX_W = 8;
        internal const int MINI_CUBE_HITBOX_H = 7;   // 8×7 for mini

        // ════════════════════════════════════════════════════════════════════
        //  BALL CONSTANTS
        // ════════════════════════════════════════════════════════════════════
        internal static int BallGravity(bool mini) => mini ? 0x57 : 0x47;
        internal static int BallSwitchVel(bool mini) => mini ? 0x120 : 0x200;
        internal static int BallMaxFallSpeed(bool mini) => 0x600;

        // ════════════════════════════════════════════════════════════════════
        //  SHIP CONSTANTS
        // ════════════════════════════════════════════════════════════════════
        internal static int ShipGravityBase(bool mini) => mini ? 0x31 : 0x2A;
        internal static int ShipGravityAfterHold(bool mini) => mini ? 0x3B : 0x32;
        internal static int ShipGravityHoldFall(bool mini) => mini ? 0x3E : 0x34;
        // SIM's SHIP_GRAVITY(baseTableIdx) uses (table_idx & 3) which strips the
        // mini bit, so mini and non-mini both map to values[4] = 0x22.
        // Match the SIM's actual behavior (not the NES table values[6] = 0x27).
        internal static int ShipGravity(bool mini) => 0x22;
        // SIM's SHIP_MAX_FALLSPEED / _HOLD use the same (table_idx & 3) masking,
        // so mini and non-mini both map to values[4].
        internal static int ShipMaxFallSpeed(bool mini) => 0x02D7;
        internal static int ShipMaxFallSpeedHold(bool mini) => 0x038D;

        // ════════════════════════════════════════════════════════════════════
        //  UFO CONSTANTS
        // ════════════════════════════════════════════════════════════════════
        internal static int UfoGravity(bool mini) => 0x32;
        internal static int UfoJumpVel(bool mini) => mini ? 0x2D0 : 0x330;
        internal static int UfoMaxFallSpeed(bool mini) => mini ? 0x350 : 0x320;

        // ════════════════════════════════════════════════════════════════════
        //  SPEED CONSTANTS
        // ════════════════════════════════════════════════════════════════════
        internal const int CUBE_SPEED_X05  = 0x23B;
        internal const int CUBE_SPEED_X1   = 0x02C4;
        internal const int CUBE_SPEED_X2   = 0x0371;
        internal const int CUBE_SPEED_X3   = 0x0429;
        internal const int CUBE_SPEED_X4   = 0x051E;
        internal const int CUBE_SPEED_SLOW = 0x16E;

        // ════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Cube gravity constant for normal vs mini.</summary>
        internal static int GetCubeGravity(bool mini) => mini ? CUBE_GRAVITY_MINI : CUBE_GRAVITY_NORMAL;

        /// <summary>Cube jump velocity for normal vs mini.</summary>
        internal static int GetCubeJumpVel(bool mini) => mini ? JUMP_VEL_MINI : JUMP_VEL_NORMAL;

        /// <summary>Cube hitbox width for normal vs mini.</summary>
        internal static int GetCubeHitboxW(bool mini) => mini ? MINI_CUBE_HITBOX_W : CUBE_HITBOX_W;

        /// <summary>Cube hitbox height for normal vs mini.</summary>
        internal static int GetCubeHitboxH(bool mini) => mini ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;

        /// <summary>
        /// Cube hitbox Y-offset for collision.
        /// Mini + normal gravity → 9 (centres the 7px hitbox within 16px tile from below).
        /// All other combos → 0.
        /// </summary>
        internal static int GetCubeHitboxOffsetY(bool mini, bool gravityUp)
            => (mini && !gravityUp) ? 9 : 0;

        /// <summary>
        /// Generic mini centering offset — centres the mini hitbox vertically within the 16px tile.
        /// Used by NES bg_coll_floor_spikes and the cube ceiling proximity check.
        /// Distinct from GetCubeHitboxOffsetY (which uses 9 for normal-gravity cube).
        /// Returns (0x10 - hitboxH) >> 1 = 4 when mini, 0 when normal.
        /// </summary>
        internal static int GetMiniCenterOffsetY(bool mini)
            => mini ? ((0x10 - GetCubeHitboxH(true)) >> 1) : 0;

        /// <summary>
        /// General hitbox Y-offset for any game mode.
        /// Cube/Robot/Ninja (modes 0,4,8): uses GetCubeHitboxOffsetY (9 for mini+normal grav).
        /// All other modes (Ball,Ship,UFO,Spider,Wave,etc.): GetMiniCenterOffsetY (4 when mini).
        /// </summary>
        internal static int GetHitboxOffsetY(int gameMode, bool mini, bool gravFlipped)
        {
            if (!mini) return 0;
            if (gameMode == 0 || gameMode == 4 || gameMode == 8) // cube, robot, ninja
                return GetCubeHitboxOffsetY(true, gravFlipped);
            return GetMiniCenterOffsetY(true);
        }

        // ════════════════════════════════════════════════════════════════════
        //  BLUE PAD / ORB CONSTANTS  (from pad_orb_defines.h)
        // ════════════════════════════════════════════════════════════════════
        internal const int PAD_HEIGHT_BLUE_NORMAL = 0x3A0;   // magnitude (unsigned)
        internal const int PAD_HEIGHT_BLUE_MINI   = 0x160;
        internal const int ORB_BALL_HEIGHT_BLUE_NORMAL = 0x1A0;
        internal const int ORB_BALL_HEIGHT_BLUE_MINI   = 0x60;

        /// <summary>Blue pad velocity magnitude for normal vs mini.</summary>
        internal static int BluePadVel(bool mini) => mini ? PAD_HEIGHT_BLUE_MINI : PAD_HEIGHT_BLUE_NORMAL;

        /// <summary>Blue orb velocity magnitude, accounting for ball mode using smaller velocity.</summary>
        internal static int BlueOrbVel(bool mini, int gameMode)
            => (gameMode == 2) ? (mini ? ORB_BALL_HEIGHT_BLUE_MINI : ORB_BALL_HEIGHT_BLUE_NORMAL)
                               : (mini ? PAD_HEIGHT_BLUE_MINI : PAD_HEIGHT_BLUE_NORMAL);

        // ════════════════════════════════════════════════════════════════════
        //  SPEED / MODE LOOKUPS
        // ════════════════════════════════════════════════════════════════════
        internal static int SpeedUiIndexToFixed(int uiIndex)
        {
            return uiIndex switch
            {
                0 => CUBE_SPEED_X05,
                1 => CUBE_SPEED_X1,
                2 => CUBE_SPEED_X2,
                3 => CUBE_SPEED_X3,
                4 => CUBE_SPEED_X4,
                _ => CUBE_SPEED_X1
            };
        }

        internal static int SpriteIdToSpeedFixed(int sid)
        {
            return sid switch
            {
                0x6D => CUBE_SPEED_SLOW,
                0x14 => CUBE_SPEED_X05,
                0x15 => CUBE_SPEED_X1,
                0x16 => CUBE_SPEED_X2,
                0x20 => CUBE_SPEED_X3,
                0x21 => CUBE_SPEED_X4,
                _ => -1
            };
        }

        internal static int SpriteIdToGameMode(int sid)
        {
            return sid switch
            {
                0x00 => 0, 0x01 => 1, 0x02 => 2, 0x03 => 3,
                0x04 => 4, 0x17 => 5, 0x24 => 6, 0x4B => 7,
                0x58 => 8, 0x6A => 9, 0x6B => 10, 0x6C => 11,
                _ => -1
            };
        }

        // ════════════════════════════════════════════════════════════════════
        //  SPRITE CLASSIFIERS
        // ════════════════════════════════════════════════════════════════════
        internal static bool IsSpeedPortal(int sid) => SpriteIdToSpeedFixed(sid) >= 0;
        internal static bool IsGameModePortal(int sid) => SpriteIdToGameMode(sid) >= 0;
        internal static bool IsGravityPortal(int sid) =>
            sid == 0x08 || sid == 0x09 || sid == 0x10 || sid == 0x11 ||
            sid == 0x12 || sid == 0x13 || sid == 0xFB || sid == 0xFC;
        internal static bool IsReverseGravity(int sid) =>
            sid == 0x09 || sid == 0x12 || sid == 0x13 || sid == 0xFB;
        internal static bool IsMiniGrowthPortal(int sid) => sid == 0x18 || sid == 0x19;
        internal static bool IsEndLevel(int sid) => sid == 0x0F;
        internal static bool IsYellowPad(int sid) => sid == 0x0A || sid == 0x0C;
        internal static bool IsPinkPad(int sid) => sid == 0x25 || sid == 0x26;
        internal static bool IsRedPad(int sid) => sid == 0x52 || sid == 0x53;
        internal static bool IsBluePad(int sid) => sid == 0x0D || sid == 0x0E || sid == 0xFD || sid == 0xFE;
        internal static bool IsGreenPad(int sid) => sid == 0x65;
        internal static bool IsAnyPad(int sid) =>
            IsYellowPad(sid) || IsPinkPad(sid) || IsRedPad(sid) || IsBluePad(sid) || IsGreenPad(sid);
        internal static bool IsYellowOrb(int sid) => sid == 0x0B;
        internal static bool IsYellowOrbBigger(int sid) => sid == 0x1F;
        internal static bool IsYellowOrbSmaller(int sid) => sid == 0x29;
        internal static bool IsPinkOrb(int sid) => sid == 0x06;
        internal static bool IsRedOrb(int sid) => sid == 0x28;
        internal static bool IsBlueOrb(int sid) => sid == 0x05 || sid == 0x7B;
        internal static bool IsGreenOrb(int sid) => sid == 0x27 || sid == 0x7C;
        internal static bool IsBlackOrb(int sid) => sid == 0x44;
        internal static bool IsWhiteOrb(int sid) => sid == 0x7A;
        internal static bool IsVelocityOrb(int sid) =>
            IsYellowOrb(sid) || IsYellowOrbBigger(sid) || IsYellowOrbSmaller(sid) ||
            IsPinkOrb(sid) || IsRedOrb(sid) || IsBlackOrb(sid);
        internal static bool IsGravityOrb(int sid) => IsBlueOrb(sid) || IsGreenOrb(sid);
        internal static bool IsOrbSprite(int sid) =>
            IsVelocityOrb(sid) || IsGravityOrb(sid) || IsWhiteOrb(sid);
        internal static bool IsCoinSprite(int sid) => sid == 0x07 || sid == 0x1A || sid == 0x1B;

        // ════════════════════════════════════════════════════════════════════
        //  PAD/ORB VELOCITY TABLES
        //  Columns: 0=Cube,1=Ship,2=Ball,3=UFO,4=Robot,5=Spider,6=Wave,7=Swing
        //  Rows: 0=YellowOrb,1=YellowPad,2=PinkOrb,3=PinkPad,4=RedOrb,
        //        5=YellowBigger,6=BlackOrb,7=YellowSmaller,8=RedPad
        // ════════════════════════════════════════════════════════════════════
        internal static readonly int[][] PadOrbHeights = new int[][] {
            new int[] { 0x590, 0x450, 0x410, 0x3B0, 0x590, 0x440, 0x000, 0x3A0 },
            new int[] { 0x7C0, 0x3C0, 0x4F0, 0x330, 0x8B0, 0x500, 0x000, 0x450 },
            new int[] { 0x3D0, 0x200, 0x330, 0x220, 0x450, 0x350, 0x000, 0x2D0 },
            new int[] { 0x510, 0x270, 0x360, 0x250, 0x550, 0x350, 0x000, 0x360 },
            new int[] { 0x750, 0x5D0, 0x550, 0x510, 0x750, 0x500, 0x000, 0x4D0 },
            new int[] { 0x590, 0x590, 0x5D0, 0x590, 0x590, 0x590, 0x000, 0x5D0 },
            new int[] { -0x990, -0x990, -0x970, -0x990, -0x990, -0x990, 0x000, -0x970 },
            new int[] { 0x540, 0x540, 0x472, 0x4B0, 0x770, 0x4B0, 0x000, 0x472 },
            new int[] { 0x9F0, 0x620, 0x630, 0x400, 0xA50, 0x690, 0x000, 0x660 }
        };
        internal static readonly int[][] PadOrbHeights_Mini = new int[][] {
            new int[] { 0x4D0, 0x4A0, 0x450, 0x3D0, 0x470, 0x350, 0x000, 0x2A0 },
            new int[] { 0x680, 0x430, 0x4D0, 0x3A0, 0x730, 0x400, 0x000, 0x340 },
            new int[] { 0x350, 0x1E0, 0x350, 0x1B0, 0x370, 0x230, 0x000, 0x1F0 },
            new int[] { 0x3F0, 0x1E0, 0x390, 0x150, 0x350, 0x350, 0x000, 0x220 },
            new int[] { 0x650, 0x670, 0x500, 0x550, 0x650, 0x470, 0x000, 0x350 },
            new int[] { 0x590, 0x590, 0x560, 0x590, 0x590, 0x590, 0x000, 0x560 },
            new int[] { -0x990, -0x990, -0x970, -0x990, -0x990, -0x990, 0x000, -0x970 },
            new int[] { 0x540, 0x540, 0x472, 0x4B0, 0x770, 0x4B0, 0x000, 0x472 },
            new int[] { 0x830, 0x6C0, 0x5B0, 0x550, 0x8D0, 0x550, 0x000, 0x3A0 }
        };

        internal static int GetPadOrbModeCol(int gameMode)
        {
            if (gameMode == 8) return 0;  // Ninja → Cube
            if (gameMode == 9) return 7;  // Pogo → Swingcopter
            if (gameMode == 11) return 0; // Football → Cube
            return gameMode;
        }

        internal static int GetPadOrbVel(int row, bool mini, int gameMode)
        {
            int col = GetPadOrbModeCol(gameMode);
            return mini ? PadOrbHeights_Mini[row][col] : PadOrbHeights[row][col];
        }

        // ════════════════════════════════════════════════════════════════════
        //  SPRITE GEOMETRY TABLES (matching NES sprite collision data)
        // ════════════════════════════════════════════════════════════════════
        internal static readonly int[] sprite_widths = {
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x0e,0x0e,0x0F,0x10,0x0F,0x0F,0x0F,0x10,
            0x28,0x28,0x28,0x28,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x0F,0x0F,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x0e,
            0x0e,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x0F,0x0F,0x10,0x10,0x0F,0x0F,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x0E,0x30,0x30,
            0x30,0x30,0x10,0x10,0x10,0x10,0x08,0x10,
            0x10,0x10,0x10,0x10,0x10,0x30,0x30,0x30,
            0x30,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x08,0x1B,0x10,0x10,0x0E,0x0E,0x10
        };
        internal static readonly int[] sprite_heights = {
            0x34,0x34,0x34,0x34,0x34,0x12,0x12,0x10,
            0x28,0x28,0x03,0x12,0x03,0x03,0x03,0x10,
            0x0e,0x0e,0x0e,0x0e,0x24,0x24,0x24,0x34,
            0x34,0x34,0x10,0x10,0x10,0x10,0x10,0x12,
            0x24,0x24,0x34,0x34,0x34,0x03,0x03,0x12,
            0x12,0x12,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x12,0x12,0x12,0x28,
            0x28,0x10,0x10,0x34,0x12,0x12,0x30,0x10,
            0x12,0x12,0x03,0x03,0x12,0x12,0x03,0x03,
            0x34,0x10,0x10,0x12,0x12,0x12,0x12,0x34,
            0x34,0x34,0x34,0x34,0x34,0x02,0x10,0x10,
            0x10,0x10,0x34,0x34,0x34,0x20,0x08,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x12,0x12,0x12,0x12,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x00,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x00,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x00,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x00,0x00,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10,
            0x10,0x10,0x1F,0x10,0x10,0x03,0x03,0x00
        };
        internal static readonly int[] sprite_x_offset = {
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x01,0x01,0x00,0x00,0x00,0x00,0x00,0x00,
            0x04,0x04,0x04,0x04,0x00,0x00,0x00,0x00,
            0x08,0x08,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x01,
            0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x04,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x08,-0x07,0x00,0x00,0x00,0x00,0x00
        };
        internal static readonly int[] sprite_y_offset = {
            -0x02,-0x02,-0x02,-0x02,-0x02,-0x01,-0x01,0x00,
            0x04,0x04,0x0D,-0x01,0x00,0x0D,0x00,0x00,
            0x01,0x01,0x01,0x01,-0x02,-0x02,-0x02,-0x02,
            -0x02,-0x02,0x00,0x00,0x00,0x00,0x00,-0x01,
            -0x02,-0x02,-0x02,-0x02,-0x02,0x0D,0x00,-0x01,
            -0x01,-0x01,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,-0x01,-0x01,-0x01,0x04,
            0x04,0x00,0x00,-0x02,0x00,-0x01,-0x01,0x00,
            -0x01,-0x01,0x0D,0x00,-0x01,-0x01,0x0D,0x00,
            -0x02,0x00,0x00,-0x01,-0x01,-0x01,-0x01,-0x02,
            -0x02,-0x02,-0x02,-0x02,-0x02,0x00,0x00,0x00,
            0x00,0x00,-0x02,-0x02,-0x02,0x00,0x04,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,-0x01,-0x01,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,-0x07,0x00,0x00,0x0D,0x02,0x00
        };

        // ════════════════════════════════════════════════════════════════════
        //  COLLISION SHAPE METHODS (pure computation, no tile map access)
        // ════════════════════════════════════════════════════════════════════

        internal static (int left, int top, int right, int bottom) GetCollisionBounds(MetatileCollision col)
        {
            switch (col)
            {
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                case MetatileCollision.COL_NO_SIDE:
                    return (0, 0, 16, 16);
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    return (0, 0, 16, 8);
                case MetatileCollision.COL_BOTTOM:
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                case MetatileCollision.COL_BOTTOM_SPIKES:
                    return (0, 8, 16, 16);
                case MetatileCollision.COL_LEFT:
                    return (0, 0, 8, 16);
                case MetatileCollision.COL_RIGHT:
                    return (8, 0, 16, 16);
                case MetatileCollision.COL_UP_LEFT:
                    return (0, 0, 8, 8);
                case MetatileCollision.COL_UP_RIGHT:
                    return (8, 0, 16, 8);
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    return (0, 8, 8, 16);
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return (8, 8, 16, 16);
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    return (0, 0, 16, 16);
                default:
                    return (16, 16, 0, 0);
            }
        }

        internal static bool ProvidesFloorAtColumn(MetatileCollision col, int localX)
        {
            return ProvidesFloorAtColumn(col, localX, out _);
        }

        internal static bool ProvidesFloorAtColumn(MetatileCollision col, int localX, out int topOffsetPx)
        {
            topOffsetPx = int.MaxValue;
            bool inLeft = (localX >= 0 && localX <= 7);
            bool inRight = (localX >= 8 && localX <= 15);

            switch (col)
            {
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                case MetatileCollision.COL_NO_SIDE:
                    topOffsetPx = 0; return true;
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    topOffsetPx = 0; return true;
                case MetatileCollision.COL_BOTTOM:
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                case MetatileCollision.COL_BOTTOM_SPIKES:
                    topOffsetPx = 8; return true;
                case MetatileCollision.COL_LEFT:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    if (inLeft) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_RIGHT:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    if (inRight) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    if (inRight) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_UP_LEFT:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_UP_RIGHT:
                    if (inRight) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    if (inLeft) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    if (inRight) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    if (inRight) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    if (inRight) { topOffsetPx = 0; return true; }
                    if (inLeft) { topOffsetPx = 8; return true; }
                    break;
                default:
                    break;
            }

            if (col == MetatileCollision.COL_BOTTOM_LEFT_STAIRS)
            {
                if (inLeft) { topOffsetPx = 0; return true; }
                if (inRight) { topOffsetPx = 8; return true; }
            }
            if (col == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS)
            {
                if (inRight) { topOffsetPx = 0; return true; }
                if (inLeft) { topOffsetPx = 8; return true; }
            }

            return false;
        }

        internal static bool IsSlopeTile(MetatileCollision col)
        {
            return col >= MetatileCollision.COL_SLOPE_RD45 && col <= MetatileCollision.COL_SLOPE_LU66_BOT;
        }

        internal static bool IsMiniBlockType(MetatileCollision col)
        {
            switch (col)
            {
                case MetatileCollision.COL_UP_LEFT:
                case MetatileCollision.COL_UP_RIGHT:
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsMiniBlockFloorHit(MetatileCollision col, int localX, int localY)
        {
            switch (col)
            {
                case MetatileCollision.COL_UP_LEFT:
                    return (localY < 8) && (localX < 8);
                case MetatileCollision.COL_UP_RIGHT:
                    return (localY < 8) && (localX >= 8);
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    return (localY >= 8) && (localX < 8);
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return (localY >= 8) && (localX >= 8);
                default:
                    return false;
            }
        }

        internal static int GetMiniBlockFloorSurface(MetatileCollision col)
        {
            switch (col)
            {
                case MetatileCollision.COL_UP_LEFT:
                case MetatileCollision.COL_UP_RIGHT:
                    return 0;
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return 8;
                default:
                    return 0;
            }
        }

        internal static bool TileOccupiesPixel(MetatileCollision col, int localX, int localY)
        {
            switch (col)
            {
                case MetatileCollision.COL_DEATH:
                case MetatileCollision.COL_DEATH_BOTTOM:
                case MetatileCollision.COL_DEATH_TOP:
                case MetatileCollision.COL_DEATH_LEFT:
                case MetatileCollision.COL_DEATH_RIGHT:
                case MetatileCollision.COL_DEATH_TOP_RIGHT:
                case MetatileCollision.COL_DEATH_TOP_LEFT:
                case MetatileCollision.COL_DEATH_BOTTOM_RIGHT:
                case MetatileCollision.COL_DEATH_BOTTOM_LEFT:
                case MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT:
                case MetatileCollision.COL_DEATH_TOP_BOTTOM:
                case MetatileCollision.COL_DEATH_LEFT_RIGHT:
                case MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM:
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                case MetatileCollision.COL_UP_BOTH_SPIKES:
                case MetatileCollision.COL_DOWN_LEFT_SPIKE:
                case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
                case MetatileCollision.COL_DOWN_BOTH_SPIKES:
                case MetatileCollision.COL_FLOOR_CEIL:
                case MetatileCollision.COL_NO_SIDE:
                    return false;
                default:
                    break;
            }

            bool inLeft = (localX >= 0 && localX <= 7);
            bool inRight = (localX >= 8 && localX <= 15);

            switch (col)
            {
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    return (localY <= 7);
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                    return (localY <= 7) || (localX <= 7);
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                    return (localY <= 7) || (localX >= 8);
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    return (localX <= 7) || (localX >= 8 && localY >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    return (localX >= 8) || (localX <= 7 && localY >= 8);
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    return (localY >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    return (localY >= 8);
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    return (localX < 8 && localY >= 8);
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return (localX >= 8 && localY >= 8);
                case MetatileCollision.COL_UP_LEFT:
                    return (inLeft && localY <= 7);
                case MetatileCollision.COL_UP_RIGHT:
                    return (inRight && localY <= 7);
                case MetatileCollision.COL_RIGHT:
                    return inRight;
                case MetatileCollision.COL_LEFT:
                    return inLeft;
                case MetatileCollision.COL_DOWN_LEFT:
                    return (inLeft && localY >= 8);
                case MetatileCollision.COL_DOWN_RIGHT:
                    return (inRight && localY >= 8);
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                    if (inLeft) return (localY <= 7);
                    if (inRight) return (localY >= 8);
                    break;
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    if (inRight) return (localY <= 7);
                    if (inLeft) return (localY >= 8);
                    break;
                default:
                    break;
            }

            if (ProvidesFloorAtColumn(col, localX, out int topOffsetPx))
            {
                return (localY >= topOffsetPx);
            }
            if (IsSlopeTile(col))
                return false;
            if (col == MetatileCollision.COL_DEATH)
                return false;
            if (col != MetatileCollision.COL_NONE) return true;
            return false;
        }

        internal static int MapTileForCollision(int tid)
        {
            switch (tid)
            {
                case 0xFC: case 0xDF: case 0xE3: case 0xFE: case 0xFF: return 0x00;
                case 0xFD: return 0x26;
                default: return tid;
            }
        }

        /// <summary>
        /// Returns true if the collision type is any death/spike type.
        /// </summary>
        internal static bool IsDeathCollision(MetatileCollision col)
        {
            return col == MetatileCollision.COL_DEATH ||
                   col == MetatileCollision.COL_DEATH_TOP ||
                   col == MetatileCollision.COL_DEATH_BOTTOM ||
                   col == MetatileCollision.COL_DEATH_LEFT ||
                   col == MetatileCollision.COL_DEATH_RIGHT ||
                   col == MetatileCollision.COL_DEATH_TOP_RIGHT ||
                   col == MetatileCollision.COL_DEATH_TOP_LEFT ||
                   col == MetatileCollision.COL_DEATH_BOTTOM_RIGHT ||
                   col == MetatileCollision.COL_DEATH_BOTTOM_LEFT ||
                   col == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT ||
                   col == MetatileCollision.COL_DEATH_TOP_BOTTOM ||
                   col == MetatileCollision.COL_DEATH_LEFT_RIGHT ||
                   col == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM ||
                   col == MetatileCollision.COL_TOP_CENTER_SPIKE ||
                   col == MetatileCollision.COL_BOTTOM_CENTER_SPIKE ||
                   col == MetatileCollision.COL_UP_LEFT_SPIKE ||
                   col == MetatileCollision.COL_UP_RIGHT_SPIKE ||
                   col == MetatileCollision.COL_UP_BOTH_SPIKES ||
                   col == MetatileCollision.COL_DOWN_LEFT_SPIKE ||
                   col == MetatileCollision.COL_DOWN_RIGHT_SPIKE ||
                   col == MetatileCollision.COL_DOWN_BOTH_SPIKES ||
                   col == MetatileCollision.COL_LEFT_SPIKE_BLOCK ||
                   col == MetatileCollision.COL_RIGHT_SPIKE_BLOCK ||
                   col == MetatileCollision.COL_BOTTOM_LEFT_SPIKE ||
                   col == MetatileCollision.COL_BOTTOM_RIGHT_SPIKE ||
                   col == MetatileCollision.COL_BOTTOM_SPIKES;
        }

        /// <summary>
        /// Verify floor support at current position using centered 15px hitbox.
        /// Normal gravity: check below feet. Reversed: check above head.
        /// </summary>
        internal static bool VerifyGroundSupport(
            in CollisionMap map, int playerX_px, int playerY_px,
            int gameMode, bool mini, bool gravFlipped)
        {
            int hbW = GetCubeHitboxW(mini);
            int hbH = GetCubeHitboxH(mini);
            int hbOffY = (mini && !gravFlipped) ? 9 : (mini ? ((0x10 - hbH) >> 1) : 0);
            if (gameMode != 0 && gameMode != 4 && gameMode != 8 && mini)
                hbOffY = (0x10 - hbH) >> 1;

            int playerCenter_px = playerX_px + (TILE / 2);
            int playerLeft_px = playerCenter_px - (hbW / 2);
            int playerRight_px = playerLeft_px + (hbW - 1);

            if (!gravFlipped)
            {
                int footY_px = playerY_px + hbOffY + hbH;
                if (gameMode == 2) footY_px += 1; // Ball mode offset
                int tileBelowY = footY_px / TILE;
                int tileArrayY = tileBelowY + map.GroundRowsToReserve;

                if (tileArrayY >= map.MapHeight)
                    return true; // Ground layer

                if (tileArrayY < 0) return false;

                for (int tx = playerLeft_px / TILE; tx <= playerRight_px / TILE; tx++)
                {
                    if (tx < 0 || tx >= map.MapWidth) continue;
                    int idx = tileArrayY * map.MapWidth + tx;
                    if (idx < 0 || idx >= map.Tiles.Length) continue;
                    int tid = map.Tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(tid));
                    if (col == MetatileCollision.COL_NONE) continue;

                    int tileStartX = tx * TILE;
                    int localX = Math.Max(0, Math.Min(TILE - 1, playerCenter_px - tileStartX));
                    if (ProvidesFloorAtColumn(col, localX))
                        return true;
                }
                return false;
            }
            else
            {
                int headY_px = playerY_px + hbOffY;
                int valGS = headY_px - 1;
                int tileAboveY = valGS >= 0 ? valGS / TILE : (valGS - TILE + 1) / TILE;
                if (tileAboveY < 0) return false;

                int tileArrayY = tileAboveY + map.GroundRowsToReserve;
                if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return false;

                for (int tx = playerLeft_px / TILE; tx <= playerRight_px / TILE; tx++)
                {
                    if (tx < 0 || tx >= map.MapWidth) continue;
                    int idx = tileArrayY * map.MapWidth + tx;
                    if (idx < 0 || idx >= map.Tiles.Length) continue;
                    int tid = map.Tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(tid));
                    if (col == MetatileCollision.COL_NONE) continue;

                    int tileStartX = tx * TILE;
                    int localX = Math.Max(0, Math.Min(TILE - 1, playerCenter_px - tileStartX));
                    if (ProvidesFloorAtColumn(col, localX))
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Ball grounded-probe spike death: probes 3 X-points at the ball's
        /// grounded-probe position for spike death (matching NES bg_coll_U_D_checks).
        /// </summary>
        internal static bool BallGroundedProbeSpikeDeath(
            in CollisionMap map, int playerX_px, int playerY_px,
            int hbW, int hbH, int hbOffY, bool gravFlipped)
        {
            int collX = playerX_px;
            int checkY;
            if (!gravFlipped)
            {
                checkY = playerY_px + hbOffY + hbH + 2;
            }
            else
            {
                checkY = playerY_px + hbOffY - 1;
            }

            for (int cpIdx = 0; cpIdx < 3; cpIdx++)
            {
                int px = cpIdx == 0 ? collX
                       : cpIdx == 1 ? collX + hbW / 2
                       : collX + hbW;
                int tileX = px / TILE;
                int tileY = checkY / TILE;

                if (tileX < 0 || tileX >= map.MapWidth || tileY < 0 || tileY >= map.MapHeight) continue;

                int tileArrayY = tileY + map.GroundRowsToReserve;
                if (tileArrayY < 0 || tileArrayY >= map.MapHeight) continue;

                int tileIdx = tileArrayY * map.MapWidth + tileX;
                if (tileIdx < 0 || tileIdx >= map.Tiles.Length) continue;

                int tileId = map.Tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);

                int localX = px % TILE;
                int localY = checkY % TILE;

                if ((collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM) &&
                    MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                {
                    return true;
                }
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        //  TILE MAP ACCESS (require CollisionMap)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get tile collision type at world tile coordinates.
        /// Returns COL_ALL for implicit ground layer, COL_NONE for out of bounds.
        /// </summary>
        internal static MetatileCollision GetTileCollision(in CollisionMap map, int tileX, int tileY)
        {
            int tileArrayY = tileY + map.GroundRowsToReserve;
            if (tileX < 0 || tileX >= map.MapWidth) return MetatileCollision.COL_NONE;
            if (tileArrayY < 0) return MetatileCollision.COL_NONE;
            if (tileArrayY >= map.MapHeight) return MetatileCollision.COL_ALL;
            int idx = tileArrayY * map.MapWidth + tileX;
            if (idx < 0 || idx >= map.Tiles.Length) return MetatileCollision.COL_NONE;
            int tid = map.Tiles[idx];
            return MetatileCollisionTable.GetCollision((byte)tid);
        }

        /// <summary>
        /// Check if a pixel kills the player via spike/death tile collision.
        /// </summary>
        internal static bool PointKillsPlayer(in CollisionMap map, int px, int py)
        {
            int tileX = px / TILE;
            int tileY = py / TILE;
            int tileArrayY = tileY + map.GroundRowsToReserve;

            if (tileX < 0 || tileX >= map.MapWidth) return false;
            if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return false;

            int tileIdx = tileArrayY * map.MapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= map.Tiles.Length) return false;

            int tid = map.Tiles[tileIdx];
            int mappedTid = MapTileForCollision(tid);
            var col = MetatileCollisionTable.GetCollision((byte)mappedTid);

            int tileStartX = tileX * TILE;
            int tileStartY = (tileArrayY - map.GroundRowsToReserve) * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, px - tileStartX));
            int localY = Math.Max(0, Math.Min(TILE - 1, py - tileStartY));

            return MetatileCollisionTable.TileKillsAtPixel(col, localX, localY);
        }

        /// <summary>
        /// Check if a pixel kills the player, with debug output parameters.
        /// </summary>
        internal static bool PointKillsPlayer(in CollisionMap map, int px, int py,
            out int dbg_tid, out int dbg_mappedTid, out MetatileCollision dbg_col,
            out int dbg_localX, out int dbg_localY)
        {
            dbg_tid = 0; dbg_mappedTid = 0; dbg_col = 0; dbg_localX = 0; dbg_localY = 0;

            int tileX = px / TILE;
            int tileY = py / TILE;
            int tileArrayY = tileY + map.GroundRowsToReserve;

            if (tileX < 0 || tileX >= map.MapWidth) return false;
            if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return false;

            int tileIdx = tileArrayY * map.MapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= map.Tiles.Length) return false;

            int tid = map.Tiles[tileIdx];
            int mappedTid = MapTileForCollision(tid);
            var col = MetatileCollisionTable.GetCollision((byte)mappedTid);

            int tileStartX = tileX * TILE;
            int tileStartY = (tileArrayY - map.GroundRowsToReserve) * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, px - tileStartX));
            int localY = Math.Max(0, Math.Min(TILE - 1, py - tileStartY));

            dbg_tid = tid; dbg_mappedTid = mappedTid; dbg_col = col; dbg_localX = localX; dbg_localY = localY;
            return MetatileCollisionTable.TileKillsAtPixel(col, localX, localY);
        }

        /// <summary>
        /// Check if there are slope tiles near the player's feet.
        /// Used as heuristic for slope-skip side collision logic.
        /// </summary>
        internal static bool HasSlopeNearFeet(in CollisionMap map,
            int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY)
        {
            int bottomY_px = playerY_px + hbOffY + hbH;
            int leftX_px = playerX_px;
            int rightX_px = playerX_px + hbW;

            int tileLeft = leftX_px / TILE;
            int tileRight = rightX_px / TILE;
            int tileYBase = bottomY_px / TILE;

            for (int dy = 0; dy <= 1; dy++)
            {
                int ty = tileYBase + dy;
                int tileArrayY = ty + map.GroundRowsToReserve;
                if (tileArrayY < 0 || tileArrayY >= map.MapHeight) continue;
                for (int tx = tileLeft; tx <= tileRight; tx++)
                {
                    if (tx < 0 || tx >= map.MapWidth) continue;
                    int idx = tileArrayY * map.MapWidth + tx;
                    if (idx < 0 || idx >= map.Tiles.Length) continue;
                    int tid = map.Tiles[idx];
                    int mapped = MapTileForCollision(tid);
                    var col = MetatileCollisionTable.GetCollision((byte)mapped);
                    if (IsSlopeTile(col)) return true;
                }
            }
            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        //  COMPLEX COLLISION — L-shapes, diagonals
        //  Single source of truth: used by both SIM (CheckCollisionDown/Up)
        //  and PF (CheckFloor/CheckCeiling).
        // ════════════════════════════════════════════════════════════════════

        internal static bool CheckComplexCollision(MetatileCollision collision, int localX, int localY)
        {
            switch (collision)
            {
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                    return (localY < 8) || (localX < 8);
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                    return (localY < 8) || (localX >= 8);
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    return (localX < 8) || (localX >= 8 && localY >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    return (localX >= 8) || (localX < 8 && localY >= 8);
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                    return (localX < 8 && localY < 8) || (localX >= 8 && localY >= 8);
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    return (localX >= 8 && localY < 8) || (localX < 8 && localY >= 8);
                default:
                    return false;
            }
        }

        internal static bool IsComplexCollisionType(MetatileCollision collision)
        {
            switch (collision)
            {
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    return true;
                default:
                    return false;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  FLOOR / CEILING COLLISION — single source of truth
        //  Algorithm is identical to SimulatorWindow.CheckCollisionDown/Up.
        //  SIM wraps these with death-UI side effects; PF calls directly.
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Check floor collision (downward).
        /// This IS the CheckCollisionDown algorithm — SIM and PF both call this.
        /// Returns (hit, surfaceY, spikeDeath).
        /// </summary>
        internal static (bool hit, int surfaceY, bool spikeDeath) CheckFloor(
            in CollisionMap map, int collX, int collY, int collW, int collH)
        {
            int playerBottom_px = collY + collH;
            int tileBelowY = playerBottom_px / TILE;
            int playerLeft_px = collX;
            int playerRight_px = collX + collW;

            if (tileBelowY < 0 || tileBelowY >= map.MapHeight) return (false, 0, false);

            int tileArrayYFloor = tileBelowY + map.GroundRowsToReserve;

            // Ground layer is always solid (clears any pending death)
            if (tileArrayYFloor >= map.MapHeight)
            {
                int groundTop = tileBelowY * TILE;
                return (true, groundTop, false);
            }

            // --- NES-accurate interleaved 3-probe check ---
            // NES bg_coll_D uses COLL_CHECK_BOTTOM for each of 3 X-probes.
            // At each probe: spike → set death flag; floor → CLEAR death flag & eject.
            // A floor at any probe cancels a spike at an earlier probe.
            bool deathPending = false;

            for (int probeIdx = 0; probeIdx < 3; probeIdx++)
            {
                int px = probeIdx == 0 ? playerLeft_px
                       : probeIdx == 1 ? playerLeft_px + collW / 2
                       : playerRight_px;
                int tileX = px / TILE;

                if (tileX < 0 || tileX >= map.MapWidth) continue;

                int tileIdx = tileArrayYFloor * map.MapWidth + tileX;
                if (tileIdx < 0 || tileIdx >= map.Tiles.Length) continue;

                int tileId = map.Tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                if (collision == MetatileCollision.COL_NONE) continue;

                int localX = px % TILE;
                int localY = playerBottom_px % TILE;

                // NES bg_coll_D spike check: bg_coll_U_D_checks ONLY handles
                // COL_DEATH_TOP and COL_DEATH_BOTTOM.  Other death types (COL_DEATH,
                // COL_DEATH_LEFT, COL_DEATH_RIGHT, etc.) are NOT checked in the
                // floor collision path — they provide no floor and no spike death.
                // Death tiles always skip the floor check (unconditional continue).
                if (collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM)
                {
                    if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                    {
                        deathPending = true;
                    }
                    // Death tiles provide no floor — continue to next probe
                    continue;
                }

                // Floor check: mini-block tiles require both localX and localY match
                if (IsMiniBlockType(collision))
                {
                    if (IsMiniBlockFloorHit(collision, localX, localY))
                    {
                        int tileWorldY = tileBelowY * TILE;
                        int surfaceY = tileWorldY + GetMiniBlockFloorSurface(collision);
                        if (playerBottom_px >= surfaceY)
                        {
                            // Floor found → clears death (COLL_CHECK_BOTTOM: cube_data &= ~1)
                            return (true, surfaceY, false);
                        }
                    }
                }
                else if (ProvidesFloorAtColumn(collision, localX, out int topOffsetPx))
                {
                    int tileWorldY = tileBelowY * TILE;
                    int surfaceY = tileWorldY + topOffsetPx;
                    if (playerBottom_px >= surfaceY)
                    {
                        return (true, surfaceY, false);
                    }
                }

                // Complex collision types (L-shapes, diagonals)
                if (IsComplexCollisionType(collision))
                {
                    int tileWorldX = tileX * TILE;
                    int tileWorldY = tileBelowY * TILE;
                    int checkLocalY = playerBottom_px - tileWorldY;

                    if (checkLocalY >= -1 && checkLocalY < 16 && CheckComplexCollision(collision, localX, Math.Max(0, checkLocalY)))
                    {
                        int collisionTop = tileWorldY;
                        for (int y = 0; y < 16; y++)
                        {
                            if (CheckComplexCollision(collision, localX, y))
                            {
                                collisionTop = tileWorldY + y;
                                break;
                            }
                        }
                        return (true, collisionTop, false);
                    }
                }
            }

            // No floor found — if spike death was pending, report it
            if (deathPending) return (false, 0, true);

            return (false, 0, false);
        }

        /// <summary>
        /// Check ceiling collision (upward).
        /// This IS the CheckCollisionUp algorithm — SIM and PF both call this.
        /// Returns (hit, ceilingBottomY, spikeDeath).
        /// </summary>
        internal static (bool hit, int ceilingBottomY, bool spikeDeath) CheckCeiling(
            in CollisionMap map, int collX, int collY, int collW, int collH)
        {
            int playerTop_px = collY;
            int playerLeft_px = collX;
            int playerRight_px = collX + collW;

            // --- Spike death pre-check: 3 X-points at Y = top + 1 ---
            // NES bg_coll_U checks left, left+width/2, left+width.
            // Important: do NOT return early here — the solid ceiling detection
            // must still run so callers can snap the player position.  On the NES,
            // bg_coll_U finds the solid surface first, then bg_coll_floor_spikes
            // checks for spikes at the post-snap position.  Returning early would
            // prevent the snap and leave the player at a pre-snap Y where floor-
            // spike probes hit the same death tile, causing an incorrect death.
            bool spikeFound = false;
            {
                int checkY = playerTop_px + 1;
                for (int cpIdx = 0; cpIdx < 3; cpIdx++)
                {
                    int px = cpIdx == 0 ? playerLeft_px
                           : cpIdx == 1 ? playerLeft_px + collW / 2
                           : playerRight_px;
                    int tileX = px / TILE;
                    int tileY = checkY / TILE;

                    if (tileX < 0 || tileX >= map.MapWidth || tileY < 0 || tileY >= map.MapHeight) continue;

                    int checkTileArrayY = tileY + map.GroundRowsToReserve;
                    if (checkTileArrayY < 0 || checkTileArrayY >= map.MapHeight) continue;

                    int checkTileIdx = checkTileArrayY * map.MapWidth + tileX;
                    if (checkTileIdx < 0 || checkTileIdx >= map.Tiles.Length) continue;

                    int checkTileId = map.Tiles[checkTileIdx];
                    var checkCollision = MetatileCollisionTable.GetCollision((byte)checkTileId);

                    int localX = px % TILE;
                    int localY = checkY % TILE;

                    // NES bg_coll_U spike check only handles COL_DEATH_TOP/BOTTOM
                    if ((checkCollision == MetatileCollision.COL_DEATH_TOP || checkCollision == MetatileCollision.COL_DEATH_BOTTOM) &&
                        MetatileCollisionTable.TileKillsAtPixel(checkCollision, localX, localY))
                    {
                        spikeFound = true;
                        break;
                    }
                }
            }

            int tileAboveY = (playerTop_px - 1) >= 0
                ? (playerTop_px - 1) / TILE
                : ((playerTop_px - 1) - TILE + 1) / TILE;

            if (tileAboveY < 0 || tileAboveY >= map.MapHeight) return (false, 0, spikeFound);

            int tileArrayY = tileAboveY + map.GroundRowsToReserve;
            if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return (false, 0, spikeFound);

            int tileLeftX = playerLeft_px / TILE;
            int tileRightX = playerRight_px / TILE;

            for (int tx = tileLeftX; tx <= tileRightX; tx++)
            {
                if (tx < 0 || tx >= map.MapWidth) continue;

                int tileIdx = tileArrayY * map.MapWidth + tx;
                if (tileIdx < 0 || tileIdx >= map.Tiles.Length) continue;

                int tileId = map.Tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);

                if (collision == MetatileCollision.COL_NONE) continue;

                // Complex collision types (L-shapes, diagonals)
                if (IsComplexCollisionType(collision))
                {
                    int tileWorldX = tx * TILE;
                    int tileWorldY = tileAboveY * TILE;

                    for (int px = Math.Max(playerLeft_px, tileWorldX); px <= Math.Min(playerRight_px, tileWorldX + 15); px++)
                    {
                        int localX = px - tileWorldX;
                        int localY = playerTop_px - tileWorldY;

                        if (localY >= 0 && localY < 16 && CheckComplexCollision(collision, localX, localY))
                        {
                            // Find the bottom of the solid region at this X position
                            int collisionBottom = tileWorldY + 15;
                            for (int y = 15; y >= 0; y--)
                            {
                                if (CheckComplexCollision(collision, localX, y))
                                {
                                    collisionBottom = tileWorldY + y + 1;
                                    break;
                                }
                            }
                            return (true, collisionBottom, spikeFound);
                        }
                    }
                }
                else
                {
                    // Simple rectangular collision
                    var (colLeft, colTop, colRight, colBottom) = GetCollisionBounds(collision);
                    if (colRight <= colLeft || colBottom <= colTop) continue;

                    int tileWorldX = tx * TILE;
                    int tileWorldY = tileAboveY * TILE;
                    int collisionTop_px = tileWorldY + colTop;
                    int collisionBottom_px = tileWorldY + colBottom;
                    int collisionLeft_px = tileWorldX + colLeft;
                    int collisionRight_px = tileWorldX + colRight;

                    // NES bg_coll_U behaviour — same mini-block distinction as floor
                    bool isMiniBlock = collision == MetatileCollision.COL_UP_LEFT ||
                                       collision == MetatileCollision.COL_UP_RIGHT ||
                                       collision == MetatileCollision.COL_DOWN_LEFT ||
                                       collision == MetatileCollision.COL_DOWN_RIGHT ||
                                       collision == MetatileCollision.COL_LEFT_SPIKE_BLOCK ||
                                       collision == MetatileCollision.COL_RIGHT_SPIKE_BLOCK;

                    bool yHit = isMiniBlock
                        ? (playerTop_px >= collisionTop_px && playerTop_px < collisionBottom_px)
                        : (playerTop_px < collisionBottom_px);

                    if (yHit)
                    {
                        if (playerRight_px >= collisionLeft_px && playerLeft_px < collisionRight_px)
                        {
                            return (true, collisionBottom_px, spikeFound);
                        }
                    }
                }
            }

            return (false, 0, spikeFound);
        }

        /// <summary>
        /// CheckCenterPointDeath — single center pixel death check.
        /// Center point: (x + (w>>1) - 1, y + (h>>1) + hitboxOffsetY)
        /// </summary>
        internal static bool CheckCenterPointDeath(
            in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY)
        {
            int centerX_px = playerX_px + (hbW >> 1) - 1;
            int centerY_px = playerY_px + (hbH >> 1) + hbOffY;

            int tileX = centerX_px / TILE;
            int tileY = centerY_px / TILE;

            if (tileX < 0 || tileX >= map.MapWidth || tileY < 0 || tileY >= map.MapHeight) return false;

            int tileArrayY = tileY + map.GroundRowsToReserve;
            if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return false;

            int tileIdx = tileArrayY * map.MapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= map.Tiles.Length) return false;

            int tileId = map.Tiles[tileIdx];
            int mappedTid = MapTileForCollision(tileId);
            var collision = MetatileCollisionTable.GetCollision((byte)mappedTid);

            int tileStartX = tileX * TILE;
            int tileStartY = (tileArrayY - map.GroundRowsToReserve) * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, centerX_px - tileStartX));
            int localY = Math.Max(0, Math.Min(TILE - 1, centerY_px - tileStartY));

            return MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY);
        }

        /// <summary>
        /// CheckDeathCollision — center-point death check matching NES bg_coll_death.
        /// </summary>
        internal static bool CheckDeathCollision(
            in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY)
        {
            int centerX = playerX_px + (hbW >> 1) - 1;
            int centerY = playerY_px + (hbH / 2) + hbOffY;

            int tileX = centerX / TILE;
            int tileY = centerY / TILE;
            int tileArrayY = tileY + map.GroundRowsToReserve;

            if (tileX < 0 || tileX >= map.MapWidth) return false;
            if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return false;

            int tileIdx = tileArrayY * map.MapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= map.Tiles.Length) return false;

            int tid = map.Tiles[tileIdx];
            int mappedTid = MapTileForCollision(tid);
            var col = MetatileCollisionTable.GetCollision((byte)mappedTid);

            int tileStartX = tileX * TILE;
            int tileStartY = (tileArrayY - map.GroundRowsToReserve) * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, centerX - tileStartX));
            int localY = Math.Max(0, Math.Min(TILE - 1, centerY - tileStartY));

            return MetatileCollisionTable.TileKillsAtPixel(col, localX, localY);
        }

        /// <summary>
        /// CheckFloorSpikes — 4-corner spike check matching NES bg_coll_floor_spikes.
        /// </summary>
        internal static bool CheckFloorSpikes(
            in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, bool mini)
        {
            int miniOffY = mini ? ((0x10 - hbH) >> 1) : 0;
            int topRowY = playerY_px + miniOffY + hbH - 2;
            int botRowY = playerY_px + (mini ? miniOffY : 2);
            int leftX = playerX_px + 3;
            int rightX = playerX_px + hbW - 3;

            if (PointKillsPlayer(map, leftX, topRowY)) return true;
            if (PointKillsPlayer(map, rightX, topRowY)) return true;
            if (PointKillsPlayer(map, leftX, botRowY)) return true;
            if (PointKillsPlayer(map, rightX, botRowY)) return true;

            return false;
        }

        /// <summary>
        /// CheckForwardCollision — right edge middle pixel for solid/spike collision.
        /// Returns true if blocked or spike death at forward edge.
        /// </summary>
        internal static bool CheckForwardCollision(
            in CollisionMap map, int playerX_px, int playerY_px,
            int hbW, int hbH, int hbOffY, int gameMode, bool mini, bool gravFlipped)
        {
            if (HasSlopeNearFeet(map, playerX_px, playerY_px, hbW, hbH, hbOffY))
                return false;

            int rightEdge_px = playerX_px + hbW;
            int centerY_px;
            if (mini)
            {
                int miniTopOffset = (0x10 - hbH) >> 1;
                centerY_px = playerY_px + miniTopOffset + (hbH >> 1);
                if (gameMode == 0 || gameMode == 4 || gameMode == 8)
                    centerY_px += gravFlipped ? 3 : -2;
            }
            else
            {
                centerY_px = playerY_px + (hbH >> 1);
            }

            int tileX = rightEdge_px / TILE;
            int tileY = centerY_px / TILE;
            int tileArrayY = tileY + map.GroundRowsToReserve;

            if (tileX < 0 || tileX >= map.MapWidth) return false;
            if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return false;

            int tileIdx = tileArrayY * map.MapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= map.Tiles.Length) return false;

            int tileId = map.Tiles[tileIdx];
            int mappedTid = MapTileForCollision(tileId);
            var collision = MetatileCollisionTable.GetCollision((byte)mappedTid);

            int tileWorldX = tileX * TILE;
            int tileWorldY = tileY * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, rightEdge_px - tileWorldX));
            int localY = Math.Max(0, Math.Min(TILE - 1, centerY_px - tileWorldY));

            if (TileOccupiesPixel(collision, localX, localY))
                return true;

            if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                return true;

            return false;
        }

        // ════════════════════════════════════════════════════════════════════
        //  COMMON GRAVITY ROUTINE
        //  1:1 match of common_gravity_routine() from gamemode_cube.h lines 177-278.
        //  Both SIM and PF call this with their respective state.
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Applies gravity acceleration to velocity and integrates position.
        /// Exact port of NES common_gravity_routine().
        /// </summary>
        /// <param name="velY">Player Y velocity (fixed-point 8.8).</param>
        /// <param name="posY">Player Y position (fixed-point 8.8).</param>
        /// <param name="tmpgravity">Direction-adjusted gravity constant (positive = downward).</param>
        /// <param name="tmpfallspeed">Direction-adjusted max fall speed.</param>
        /// <param name="gravityDir">currplayer_gravity: 0 = normal, non-zero = inverted.</param>
        /// <param name="dashMode">dashing[currplayer]: 0 = normal, 1-5 = orb dash modes.</param>
        /// <param name="gravityMod">Gravity portal modifier (normally 1.0).</param>
        /// <param name="timeScale">Sim time scale (normally 1.0).</param>
        /// <param name="isFullSpeed">True when running at full speed (no slow-motion).</param>
        /// <param name="velocityX">X velocity (used for dash modes 2-5).</param>
        /// <param name="clampMaxY">Maximum Y position (fixed-point), or int.MaxValue to skip clamp.</param>
        internal static void CommonGravityRoutine(
            ref int velY,
            ref int posY,
            int tmpgravity,
            int tmpfallspeed,
            int gravityDir,
            int dashMode,
            double gravityMod,
            double timeScale,
            bool isFullSpeed,
            int velocityX,
            int clampMaxY)
        {
            // ── dash mode dispatch (gamemode_cube.h lines 249-276) ──
            if (dashMode == 0)
            {
                // Normal: apply gravity with max-fall-speed deceleration
                int tmpaccel = tmpgravity;

                bool atMaxFallSpeed;
                if (gravityDir == 0)
                    atMaxFallSpeed = (velY > tmpfallspeed);
                else
                    atMaxFallSpeed = (velY < tmpfallspeed);

                if (atMaxFallSpeed)
                    tmpaccel = -tmpaccel;

                // gravity_mod scaling (portals 0x5F-0x63)
                tmpaccel = (int)(tmpaccel * gravityMod);

                // Apply acceleration with time scale
                if (isFullSpeed)
                    velY += tmpaccel;
                else
                    velY += (int)Math.Round(tmpaccel * timeScale);
            }
            else if (dashMode == 2)
            {
                // 45° up dash: vel_y = -vel_x
                velY = -velocityX;
            }
            else if (dashMode == 3)
            {
                // 45° down dash: vel_y = vel_x
                velY = velocityX;
            }
            else if (dashMode == 4)
            {
                // Upward dash: vel_y = vel_x * 2, subtract from Y and return early
                velY = velocityX * 2;
                if (isFullSpeed)
                    posY -= velY;
                else
                    posY -= (int)Math.Round(velY * timeScale);
                return; // early return — position already updated
            }
            else if (dashMode == 5)
            {
                // Downward dash: vel_y = vel_x * 2
                velY = velocityX * 2;
            }
            else // dashMode == 1
            {
                // Horizontal dash: vel_y = gravity ? -1 : 1, integrate and return early
                velY = (gravityDir != 0) ? -1 : 1;
                if (isFullSpeed)
                    posY += velY;
                else
                    posY += (int)Math.Round(velY * timeScale);
                return; // early return — position already updated
            }

            // ── integrate Y position (gamemode_cube.h line 278) ──
            if (isFullSpeed)
                posY += velY;
            else
                posY += (int)Math.Round(velY * timeScale);

            // ── clamp to world bottom ──
            if (posY > clampMaxY)
                posY = clampMaxY;
        }

        // ════════════════════════════════════════════════════════════════════
        //  BALL GROUNDED PROBE
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fresh ball grounded probe — re-probes collision every frame instead of
        /// relying on a persistent OnGround flag.  Detects when the ball has slid
        /// past the edge of a platform.
        /// Normal gravity:  CheckFloor  at playerBottom,     height 2.
        /// Inverted gravity: CheckCeiling at playerTop − 2, height 2.
        /// </summary>
        internal static bool BallIsGrounded(in CollisionMap map,
            int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY,
            bool gravFlipped)
        {
            if (!gravFlipped)
            {
                int playerBottom = playerY_px + hbOffY + hbH;
                var (hit, _, spikeDeath) = CheckFloor(map, playerX_px, playerBottom, hbW, 2);
                // Match SIM: CheckCollisionDown returns (false, 0) when spike
                // is detected, so spikes are not considered ground.
                return hit && !spikeDeath;
            }
            else
            {
                int playerTop = playerY_px + hbOffY;
                var (hit, _, spikeDeath) = CheckCeiling(map, playerX_px, playerTop - 2, hbW, 2);
                return hit && !spikeDeath;
            }
        }
    }
}

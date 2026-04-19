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
        /// NES uses byte(0x10 - Generic.height) >> 1 = 4 for ALL mini modes,
        /// regardless of game mode or gravity direction.  This centres the 7px
        /// hitbox vertically within the 16px sprite tile — matching the NES
        /// miniOffset used in bg_coll_D, bg_coll_U, bg_coll_death, etc.
        /// </summary>
        internal static int GetCubeHitboxOffsetY(bool mini, bool gravityUp)
            => GetMiniCenterOffsetY(mini);

        /// <summary>
        /// Generic mini centering offset — centres the mini hitbox vertically within the 16px tile.
        /// Matches NES: byte(0x10 - Generic.height) >> 1 = 4 when mini, 0 when normal.
        /// Used consistently by ALL collision functions (floor, ceiling, death, floor spikes, forward).
        /// </summary>
        internal static int GetMiniCenterOffsetY(bool mini)
            => mini ? ((0x10 - GetCubeHitboxH(true)) >> 1) : 0;

        /// <summary>
        /// General hitbox Y-offset for any game mode.
        /// All modes use GetMiniCenterOffsetY (4 when mini, 0 when normal),
        /// matching the NES miniOffset = byte(0x10 - Generic.height) >> 1.
        /// </summary>
        internal static int GetHitboxOffsetY(int gameMode, bool mini, bool gravFlipped)
        {
            if (!mini) return 0;
            return GetMiniCenterOffsetY(true);
        }

        // ════════════════════════════════════════════════════════════════════
        //  BLUE PAD / ORB CONSTANTS  (from pad_orb_defines.h)
        // ════════════════════════════════════════════════════════════════════
        internal const int PAD_HEIGHT_BLUE_NORMAL = 0x3A0;   // magnitude (unsigned)
        internal const int PAD_HEIGHT_BLUE_MINI   = 0x3A0;   // NES: [g] = gravity-only, same for mini & normal
        internal const int ORB_BALL_HEIGHT_BLUE_NORMAL = 0x1A0;
        internal const int ORB_BALL_HEIGHT_BLUE_MINI   = 0x1A0;   // NES: [g] = gravity-only, same for mini & normal

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
        internal static bool IsMiniCoinSprite(int sid) => sid == 0x6E;

        // Dash orb sprite IDs (all 10 variants)
        internal static bool IsDashOrb(int sid) =>
            sid == 0x45 || sid == 0x46 ||   // horizontal / gravity-horizontal
            sid == 0x4C || sid == 0x4D ||   // 45° up / gravity-45° up
            sid == 0x50 || sid == 0x51 ||   // 45° down / gravity-45° down
            sid == 0x5B || sid == 0x5C ||   // straight up / gravity-straight up
            sid == 0x5D || sid == 0x5E;     // straight down / gravity-straight down
        internal static bool IsGravityDashOrb(int sid) =>
            sid == 0x46 || sid == 0x4D || sid == 0x51 || sid == 0x5C || sid == 0x5E;

        /// <summary>Returns dash mode (1-5) for a dash orb sprite, or 0 if not a dash orb.</summary>
        internal static int DashOrbMode(int sid)
        {
            if (sid == 0x45 || sid == 0x46) return 1; // horizontal
            if (sid == 0x4C || sid == 0x4D) return 2; // 45° up
            if (sid == 0x50 || sid == 0x51) return 3; // 45° down
            if (sid == 0x5B || sid == 0x5C) return 4; // straight up
            if (sid == 0x5D || sid == 0x5E) return 5; // straight down
            return 0;
        }

        // Spider orbs/pads
        internal static bool IsSpiderOrb(int sid) => sid == 0x54 || sid == 0x55;
        internal static bool IsSpiderPad(int sid) => sid == 0x56 || sid == 0x57;

        // Teleport portal sprite IDs
        internal static bool IsTeleportPortalEntrance(int sid) =>
            sid == 0x4E || sid == 0x66 || sid == 0x68 || sid == 0x75 || sid == 0x77;
        internal static bool IsTeleportPortalExit(int sid) =>
            sid == 0x4F || sid == 0x67 || sid == 0x69 || sid == 0x76 || sid == 0x78;
        internal static bool IsVerticalTeleportEntrance(int sid) => sid == 0x4E;
        internal static bool IsBottomRowTeleportExit(int sid) => sid == 0x67 || sid == 0x76;

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
            0x00,0x00,0x00,0x00,0x00,0x00,-0x08,0x00, // 38-3F (0x3E: -8 from NES globalObjectOffset for right medium post)
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
            -0x02,-0x02,-0x02,-0x02,-0x02,-0x01,-0x01,0x00, // 00-07
            0x04,0x04,0x0D,-0x01,0x00,0x0D,0x00,0x00, // 08-0F (0x0A,0x0D: +8 from NES globalObjectOffset for bottom pads)
            0x01,0x01,0x01,0x01,-0x02,-0x02,-0x02,-0x02, // 10-17
            -0x02,-0x02,0x00,0x00,0x00,0x00,0x00,-0x01, // 18-1F
            -0x02,-0x02,-0x02,-0x02,-0x02,0x0D,0x00,-0x01, // 20-27 (0x25: +8 from NES globalObjectOffset for bottom pads)
            -0x01,-0x01,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,-0x01,-0x01,-0x01,0x04,
            0x04,0x00,0x00,-0x02,0x00,-0x01,-0x01,0x00,
            -0x01,-0x01,0x0D,0x00,-0x01,-0x01,0x0D,0x00, // 50-57 (0x52,0x56: +8 from NES globalObjectOffset for bottom pads)
            -0x02,0x00,0x00,-0x01,-0x01,-0x01,-0x01,-0x02, // 58-5F
            -0x02,-0x02,-0x02,-0x02,-0x02,0x00,0x00,0x00,
            0x00,0x00,-0x02,-0x02,-0x02,0x00,0x04,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,-0x01,-0x01,-0x01,-0x01,0x00,0x00,0x00,
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
            0x00,0x00,-0x07,0x00,0x00,0x0D,0x02,0x00  // F8-FF (0xFD: +8 from NES globalObjectOffset for bottom pads)
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
            return col >= MetatileCollision.COL_SLOPE_RD45 && col <= MetatileCollision.COL_SLOPE_LU66_TOP;
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
                case 0xDF: case 0xE3: case 0xFE: case 0xFF: return 0x00;
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

                if (tileX < 0 || tileX >= map.MapWidth) continue;

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

            int tileArrayYFloor = tileBelowY + map.GroundRowsToReserve;
            if (tileArrayYFloor < 0) return (false, 0, false);

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

            // NES bg_coll_U probes at Generic_y - 1 (one pixel above hitbox top).
            // Use probeY for overlap checks so an exact-touch case (playerTop == tileBottom) is detected.
            int probeY = playerTop_px - 1;

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

                    if (tileX < 0 || tileX >= map.MapWidth) continue;

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

            // Validate using array Y (world Y + groundRowsToReserve), not raw world Y,
            // because negative world tile rows are valid when groundRowsToReserve > 0.
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
                        int localY = probeY - tileWorldY;

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
                        ? (probeY >= collisionTop_px && probeY < collisionBottom_px)
                        : (probeY < collisionBottom_px);

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

            if (tileX < 0 || tileX >= map.MapWidth) return false;

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
        /// gameMode: current game mode (pass -1 to skip solid-block check for backward compat).
        /// </summary>
        internal static bool CheckDeathCollision(
            in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY,
            int gameMode = -1)
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
        /// Checks BL, BR, TL, TR (matching NES order).
        /// Returns true if any corner hits a spike, with the killing corner coordinates.
        /// </summary>
        internal static bool CheckFloorSpikes(
            in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, bool mini,
            out int deathX, out int deathY)
        {
            deathX = 0;
            deathY = 0;

            int miniOffY = mini ? ((0x10 - hbH) >> 1) : 0;
            int rowBottomY = playerY_px + miniOffY + hbH - 2;
            int rowTopY = playerY_px + (mini ? miniOffY : 2);
            int leftX = playerX_px + 3;
            int rightX = playerX_px + hbW - 3;

            // NES order: BL, BR, TL, TR
            if (PointKillsPlayer(map, leftX, rowBottomY))  { deathX = leftX;  deathY = rowBottomY; return true; }
            if (PointKillsPlayer(map, rightX, rowBottomY)) { deathX = rightX; deathY = rowBottomY; return true; }
            if (PointKillsPlayer(map, leftX, rowTopY))     { deathX = leftX;  deathY = rowTopY;    return true; }
            if (PointKillsPlayer(map, rightX, rowTopY))    { deathX = rightX; deathY = rowTopY;    return true; }

            return false;
        }

        /// <summary>
        /// CheckFloorSpikes overload with debug output for PF logging.
        /// </summary>
        internal static bool CheckFloorSpikes(
            in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, bool mini,
            out int deathX, out int deathY,
            out string cornerName, out int dbg_tid, out int dbg_mappedTid,
            out MetatileCollision dbg_col, out int dbg_localX, out int dbg_localY)
        {
            deathX = 0; deathY = 0;
            cornerName = ""; dbg_tid = 0; dbg_mappedTid = 0;
            dbg_col = 0; dbg_localX = 0; dbg_localY = 0;

            int miniOffY = mini ? ((0x10 - hbH) >> 1) : 0;
            int rowBottomY = playerY_px + miniOffY + hbH - 2;
            int rowTopY = playerY_px + (mini ? miniOffY : 2);
            int leftX = playerX_px + 3;
            int rightX = playerX_px + hbW - 3;

            int _tid, _mtid, _lx, _ly; MetatileCollision _col;

            if (PointKillsPlayer(map, leftX, rowBottomY, out _tid, out _mtid, out _col, out _lx, out _ly))
            { deathX = leftX; deathY = rowBottomY; cornerName = "BL"; dbg_tid = _tid; dbg_mappedTid = _mtid; dbg_col = _col; dbg_localX = _lx; dbg_localY = _ly; return true; }
            if (PointKillsPlayer(map, rightX, rowBottomY, out _tid, out _mtid, out _col, out _lx, out _ly))
            { deathX = rightX; deathY = rowBottomY; cornerName = "BR"; dbg_tid = _tid; dbg_mappedTid = _mtid; dbg_col = _col; dbg_localX = _lx; dbg_localY = _ly; return true; }
            if (PointKillsPlayer(map, leftX, rowTopY, out _tid, out _mtid, out _col, out _lx, out _ly))
            { deathX = leftX; deathY = rowTopY; cornerName = "TL"; dbg_tid = _tid; dbg_mappedTid = _mtid; dbg_col = _col; dbg_localX = _lx; dbg_localY = _ly; return true; }
            if (PointKillsPlayer(map, rightX, rowTopY, out _tid, out _mtid, out _col, out _lx, out _ly))
            { deathX = rightX; deathY = rowTopY; cornerName = "TR"; dbg_tid = _tid; dbg_mappedTid = _mtid; dbg_col = _col; dbg_localX = _lx; dbg_localY = _ly; return true; }

            return false;
        }

        /// <summary>
        /// CheckForwardCollision — right edge middle pixel for solid/spike collision.
        /// Returns true if blocked or spike death at forward edge.
        /// </summary>
        internal static bool CheckForwardCollision(
            in CollisionMap map, int playerX_px, int playerY_px,
            int hbW, int hbH, int hbOffY, int gameMode, bool mini, bool gravFlipped,
            bool skipSlopeCheck = false)
        {
            if (!skipSlopeCheck && HasSlopeNearFeet(map, playerX_px, playerY_px, hbW, hbH, hbOffY))
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
            // Above the map = solid ceiling; below the map = solid ground
            // (matches SIM's CheckPixelCollision OOB behavior)
            if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return true;

            int tileIdx = tileArrayY * map.MapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= map.Tiles.Length) return false;

            int tileId = map.Tiles[tileIdx];
            int mappedTid = MapTileForCollision(tileId);
            var collision = MetatileCollisionTable.GetCollision((byte)mappedTid);

            // NES bg_coll_sides() returns 0 for COL_FLOOR_CEIL (falls through switch)
            // and COL_NO_SIDE has no case in bg_coll_sides or bg_coll_mini_blocks.
            // These tiles only block vertically, never horizontally.
            if (collision == MetatileCollision.COL_FLOOR_CEIL ||
                collision == MetatileCollision.COL_NO_SIDE)
                return false;

            // NES bg_coll_sides/bg_coll_mini_blocks never return 1 for slope tiles.
            // Slopes only affect floor/ceiling collision, not side/forward collision.
            // NES bg_side_coll_common applies a ±2 Y nudge when a slope is detected
            // at the forward probe (handled by caller via slopeNudgeY out param).
            if (IsSlopeTile(collision))
                return false;

            int tileWorldX = tileX * TILE;
            int tileWorldY = tileY * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, rightEdge_px - tileWorldX));
            int localY = Math.Max(0, Math.Min(TILE - 1, centerY_px - tileWorldY));

            if (TileOccupiesPixel(collision, localX, localY) ||
                    MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                return true;

            return false;
        }

        /// <summary>
        /// NES bg_side_coll_common slope nudge: when the forward probe hits a slope tile,
        /// Y is adjusted by ±2 to help the player navigate past slopes.
        /// Returns the Y nudge to apply (0 if no slope at probe, +2 for upside-down slopes, -2 for normal).
        /// Only call when slopeWasOnCounter == 0 (NES skips nudge if already on a slope).
        /// </summary>
        internal static int GetForwardSlopeNudge(
            in CollisionMap map, int playerX_px, int playerY_px,
            int hbW, int hbH, int hbOffY, int gameMode, bool mini, bool gravFlipped)
        {
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

            if (tileX < 0 || tileX >= map.MapWidth) return 0;
            if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return 0;

            int tileIdx = tileArrayY * map.MapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= map.Tiles.Length) return 0;

            int tileId = map.Tiles[tileIdx];
            int mappedTid = MapTileForCollision(tileId);
            var collision = MetatileCollisionTable.GetCollision((byte)mappedTid);

            if (!IsSlopeTile(collision)) return 0;

            // NES: SLOPE_UPSIDEDOWN slopes nudge +2 (push away from ceiling slope),
            // regular (floor) slopes nudge -2 (push away from floor slope).
            bool isUpsideDown = (collision == MetatileCollision.COL_SLOPE_RU45 ||
                                 collision == MetatileCollision.COL_SLOPE_LU45) ||
                                (collision >= MetatileCollision.COL_SLOPE_RU22_RIGHT &&
                                 collision <= MetatileCollision.COL_SLOPE_LU22_LEFT) ||
                                (collision >= MetatileCollision.COL_SLOPE_RU66_TOP &&
                                 collision <= MetatileCollision.COL_SLOPE_LU66_TOP);
            return isUpsideDown ? 2 : -2;
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
        /// NOTE: This does NOT detect slopes — slopes are handled by ball_eject
        /// which sets OnGround.  Callers that need slope-grounded detection
        /// (e.g. ball flip) should also check the OnGround flag.
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

        // ════════════════════════════════════════════════════════════════════
        //  SLOPE COLLISION SYSTEM
        //  Shared between PF and SIM — eliminates drift in slope calculation,
        //  direction filtering, and counter management.
        // ════════════════════════════════════════════════════════════════════

        internal const int SLOPE_22DEG      = 0b0010;
        internal const int SLOPE_45DEG      = 0b0001;
        internal const int SLOPE_66DEG      = 0b0011;
        internal const int SLOPE_RISING     = 0b0100;
        internal const int SLOPE_UD         = 0b1000;
        internal const int SLOPE_DEGREES_MASK = 0b0011;

        internal static readonly short[] EXIT_SLOPE_BALL_22 = { unchecked((short)0xFFA0), 0x0060, unchecked((short)0xFFA0), 0x0060, unchecked((short)0xFFB0), 0x0050, unchecked((short)0xFFB0), 0x0050 };
        internal static readonly short[] EXIT_SLOPE_BALL_66 = { 0x016D, unchecked((short)0xFE93), 0x016D, unchecked((short)0xFE93), 0x01B0, unchecked((short)0xFE50), 0x01B0, unchecked((short)0xFE50) };
        internal static readonly short[] EXIT_SLOPE_CUBE_22 = { unchecked((short)0xFECD), 0x0133, unchecked((short)0xFECD), 0x0133, unchecked((short)0xFF00), 0x0100, unchecked((short)0xFF00), 0x0100 };

        /// <summary>
        /// Per-tile slope surface calc — 1:1 port of NES bg_coll_slope().
        /// </summary>
        internal static (bool hit, int ejection, int slopeType) SlopeCalc(int temp_x, int temp_y, MetatileCollision collision)
        {
            if (collision < MetatileCollision.COL_SLOPE_RD45 || collision > MetatileCollision.COL_SLOPE_LU66_TOP)
                return (false, 0, 0);

            int tmp7, tmp4;
            int slopeType;

            switch (collision)
            {
                case MetatileCollision.COL_SLOPE_LU45:
                    tmp7 = temp_x & 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1001; break;
                case MetatileCollision.COL_SLOPE_LD45:
                    tmp7 = temp_x & 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0001; break;
                case MetatileCollision.COL_SLOPE_RU45:
                    tmp7 = (temp_x & 0x0f) ^ 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1101; break;
                case MetatileCollision.COL_SLOPE_RD45:
                    tmp7 = (temp_x & 0x0f) ^ 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0101; break;

                case MetatileCollision.COL_SLOPE_RU22_RIGHT:
                    tmp7 = ((temp_x >> 1) & 0x07) ^ 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1110; break;
                case MetatileCollision.COL_SLOPE_RU22_LEFT:
                    tmp7 = (((temp_x >> 1) | 0x8) & 0x0f) ^ 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1110; break;
                case MetatileCollision.COL_SLOPE_RD22_RIGHT:
                    tmp7 = ((temp_x >> 1) & 0x07) ^ 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0110; break;
                case MetatileCollision.COL_SLOPE_RD22_LEFT:
                    tmp7 = (((temp_x >> 1) | 0x8) & 0x0f) ^ 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0110; break;
                case MetatileCollision.COL_SLOPE_LU22_RIGHT:
                    tmp7 = (temp_x >> 1) & 0x07; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1010; break;
                case MetatileCollision.COL_SLOPE_LU22_LEFT:
                    tmp7 = ((temp_x >> 1) | 0x8) & 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1010; break;
                case MetatileCollision.COL_SLOPE_LD22_RIGHT:
                    tmp7 = (temp_x >> 1) & 0x07; tmp4 = temp_y & 0x0f; slopeType = 0b0010; break;
                case MetatileCollision.COL_SLOPE_LD22_LEFT:
                    tmp7 = ((temp_x >> 1) | 0x8) & 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0010; break;

                case MetatileCollision.COL_SLOPE_RD66_TOP:
                    if ((temp_x & 0x0f) < 0x08) return (false, 0, 0);
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0111; break;
                case MetatileCollision.COL_SLOPE_RD66_BOT:
                    if ((temp_x & 0x0f) >= 0x08) return (true, temp_y & 0x0f, 0b0111);
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0111; break;
                case MetatileCollision.COL_SLOPE_LD66_TOP:
                    if ((temp_x & 0x0f) >= 0x08) return (false, 0, 0);
                    tmp7 = ((temp_x & 0x07) << 1) & 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0011; break;
                case MetatileCollision.COL_SLOPE_LD66_BOT:
                    if ((temp_x & 0x0f) < 0x08) return (true, temp_y & 0x0f, 0b0011);
                    tmp7 = ((temp_x & 0x0f) << 1) & 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0011; break;
                case MetatileCollision.COL_SLOPE_RU66_TOP:
                    if ((temp_x & 0x0f) < 0x08) return (false, 0, 0);
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1111; break;
                case MetatileCollision.COL_SLOPE_RU66_BOT:
                    if ((temp_x & 0x0f) >= 0x08) return (true, temp_y & 0x0f, 0b1111);
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1111; break;
                case MetatileCollision.COL_SLOPE_LU66_TOP:
                    if ((temp_x & 0x0f) >= 0x08) return (false, 0, 0);
                    tmp7 = ((temp_x & 0x07) << 1) & 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1011; break;
                case MetatileCollision.COL_SLOPE_LU66_BOT:
                    if ((temp_x & 0x0f) < 0x08) return (true, temp_y & 0x0f, 0b1011);
                    tmp7 = ((temp_x & 0x0f) << 1) & 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1011; break;

                default: return (false, 0, 0);
            }

            if (tmp4 >= tmp7)
                return (true, tmp4 - tmp7, slopeType);
            return (false, 0, 0);
        }

        /// <summary>
        /// Floor slope check at LEFT and RIGHT hitbox edges.
        /// Mirrors NES bg_coll_D_slopes() with direction filtering.
        /// </summary>
        internal static (bool hit, int ejection, int slopeType) CheckSlopesDown(
            in CollisionMap map,
            int playerX_px, int checkBaseX, int checkBaseY, int checkWidth,
            bool inputHeld, int gameMode, bool gravFlipped, int velX_fixed,
            ref int lastSlopeType, ref bool slopeJumpHigher)
        {
            if (playerX_px < 0x10) return (false, 0, 0);

            int bestEjection = 0, bestSlopeType = 0;
            bool anyHit = false;

            for (int probe = 0; probe < 2; probe++)
            {
                int temp_x = checkBaseX + (probe * checkWidth);
                int tileX = temp_x / TILE;
                int tileY = checkBaseY / TILE;

                var collision = GetTileCollision(in map, tileX, tileY);
                if (collision < MetatileCollision.COL_SLOPE_RD45 || collision > MetatileCollision.COL_SLOPE_LU66_TOP)
                    continue;

                var (hit, ejection, slopeType) = SlopeCalc(temp_x, checkBaseY, collision);

                if (probe == 0 && (slopeType & SLOPE_RISING) != 0)
                {
                    if (hit && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8 || gameMode == 11))
                        slopeJumpHigher = true;
                    continue;
                }
                if (probe == 1 && (slopeType & SLOPE_RISING) == 0)
                {
                    if (hit && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8 || gameMode == 11))
                        slopeJumpHigher = true;
                    continue;
                }

                if (hit)
                {
                    if ((lastSlopeType & SLOPE_RISING) != 0 && (slopeType & SLOPE_RISING) == 0)
                    {
                        if (lastSlopeType != 0 && slopeType != 0)
                        {
                            slopeType = lastSlopeType;
                            ejection = velX_fixed >> 8;
                        }
                    }
                    if (slopeType != 0) lastSlopeType = slopeType;

                    if (gameMode == 1 || gameMode == 3)
                    {
                        int aIdx = ((slopeType & SLOPE_RISING) != 0 ? 4 : 0)
                                 | ((slopeType & SLOPE_UD) != 0 ? 2 : 0)
                                 | (gravFlipped ? 1 : 0);
                        bool aCheck = (aIdx == 0 || aIdx == 3 || aIdx == 4 || aIdx == 7);
                        if (aCheck ? inputHeld : !inputHeld) ejection = 4;
                    }

                    bestEjection = ejection; bestSlopeType = slopeType; anyHit = true;
                }
            }

            return (anyHit, bestEjection, bestSlopeType);
        }

        /// <summary>
        /// Ceiling slope check at LEFT and RIGHT hitbox edges.
        /// Mirrors NES bg_coll_U_slopes().
        /// </summary>
        internal static (bool hit, int ejection, int slopeType) CheckSlopesUp(
            in CollisionMap map,
            int playerX_px, int checkBaseX, int checkBaseY, int checkWidth,
            bool inputHeld, int gameMode, bool gravFlipped, int velX_fixed,
            ref int lastSlopeType, ref bool slopeJumpHigher)
        {
            if (playerX_px < 0x10) return (false, 0, 0);

            int bestEjection = 0, bestSlopeType = 0;
            bool anyHit = false;

            for (int probe = 0; probe < 2; probe++)
            {
                int temp_x = checkBaseX + (probe * checkWidth);
                int tileX = temp_x / TILE;
                int tileY = checkBaseY / TILE;

                var collision = GetTileCollision(in map, tileX, tileY);
                if (collision < MetatileCollision.COL_SLOPE_RD45 || collision > MetatileCollision.COL_SLOPE_LU66_TOP)
                    continue;

                var (hit, ejection, slopeType) = SlopeCalc(temp_x, checkBaseY, collision);

                if (probe == 0 && (slopeType & SLOPE_RISING) != 0)
                {
                    if (hit && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8 || gameMode == 11))
                        slopeJumpHigher = true;
                    continue;
                }
                if (probe == 1 && (slopeType & SLOPE_RISING) == 0)
                {
                    if (hit && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8 || gameMode == 11))
                        slopeJumpHigher = true;
                    continue;
                }

                if (hit)
                {
                    if ((lastSlopeType & SLOPE_RISING) != 0 && (slopeType & SLOPE_RISING) == 0)
                    {
                        if (lastSlopeType != 0 && slopeType != 0)
                        {
                            slopeType = lastSlopeType;
                            ejection = velX_fixed >> 8;
                        }
                    }
                    if (slopeType != 0) lastSlopeType = slopeType;

                    if (gameMode == 1 || gameMode == 3)
                    {
                        int aIdx = ((slopeType & SLOPE_RISING) != 0 ? 4 : 0)
                                 | ((slopeType & SLOPE_UD) != 0 ? 2 : 0)
                                 | (gravFlipped ? 1 : 0);
                        bool aCheck = (aIdx == 0 || aIdx == 3 || aIdx == 4 || aIdx == 7);
                        if (aCheck ? inputHeld : !inputHeld) ejection = 4;
                    }

                    bestEjection = ejection; bestSlopeType = slopeType; anyHit = true;
                }
            }

            return (anyHit, bestEjection, bestSlopeType);
        }

        /// <summary>
        /// Slope counter decrement — called at START of eject.
        /// Mirrors NES decrement_was_on_slope().
        /// </summary>
        internal static void UpdateSlopeCounters(
            ref int slopeWasOnCounter, ref int slopeType, ref int velY_fixed,
            int gameMode, bool gravFlipped, bool mini, ref int lastSlopeType)
        {
            if (slopeWasOnCounter > 0)
            {
                slopeWasOnCounter--;
                if (slopeWasOnCounter == 0)
                {
                    int tableIdx = (gravFlipped ? 1 : 0) | (mini ? 4 : 0);
                    if (gameMode == 2 || gameMode == 9)
                    {
                        int st = slopeType & 0b1111;
                        if (st == 0b0110 || st == 0b1110)
                            velY_fixed += EXIT_SLOPE_BALL_22[tableIdx];
                        else if (st == 0b0111 || st == 0b1111)
                            velY_fixed += EXIT_SLOPE_BALL_66[tableIdx];
                    }
                    else if (gameMode == 0 || gameMode == 11)
                    {
                        int st = slopeType & 0b1111;
                        if (st == 0b0110 || st == 0b1110)
                            velY_fixed += EXIT_SLOPE_CUBE_22[tableIdx];
                    }
                    slopeType = 0;
                }
            }
            else
            {
                lastSlopeType = 0;
                slopeType = 0;
            }
        }

        /// <summary>
        /// Post-eject slope counter (x_movement_coll).
        /// </summary>
        internal static void UpdateSlopeCountersFresh(
            ref int slopeFrames, int slopeType, ref int velY_fixed, int velX_fixed)
        {
            if (slopeFrames > 0)
            {
                slopeFrames--;
                if (slopeType != 0)
                    ApplySlopeVelocity(ref velY_fixed, slopeType, velX_fixed);
            }
        }

        /// <summary>apply_slope_vel() from x_movement.h.</summary>
        internal static void ApplySlopeVelocity(ref int velY_fixed, int slopeType, int velX_fixed)
        {
            if (slopeType == 0) return;
            int degrees = slopeType & SLOPE_DEGREES_MASK;
            int velComponent;
            switch (degrees)
            {
                case SLOPE_22DEG: velComponent = velX_fixed >> 1; break;
                case SLOPE_45DEG: velComponent = velX_fixed; break;
                case SLOPE_66DEG: velComponent = velX_fixed << 1; break;
                default: return;
            }
            bool rising = (slopeType & SLOPE_RISING) != 0;
            bool ud = (slopeType & SLOPE_UD) != 0;
            velY_fixed = (rising == ud) ? velComponent : -velComponent;
        }

        /// <summary>slope_jump_check — bonus velocity when jumping off slopes.
        /// NES MAKE_CUBE_JUMP_HIGHER table: normal=-0x9A/+0x9A, mini=-0x80/+0x80 (sign flips with gravity).</summary>
        internal static void SlopeJumpCheck(ref int velY_fixed, ref bool slopeJumpHigher, int slopeType, bool mini, bool gravFlipped)
        {
            if (!slopeJumpHigher) return;
            if ((slopeType & SLOPE_DEGREES_MASK) != SLOPE_22DEG)
            {
                int boost = mini ? 0x80 : 0x9A;
                velY_fixed += gravFlipped ? boost : -boost;
            }
            slopeJumpHigher = false;
        }

        // ════════════════════════════════════════════════════════════════════
        //  SHARED EJECTION SYSTEM
        // ════════════════════════════════════════════════════════════════════

        internal struct EjectResult
        {
            public int NewY_fixed;
            public int NewVelY_fixed;
            public bool OnGround;
            public bool Died;
            public bool WasZeroed;
            public int SlopeType;
            public int SlopeFrames;
            public int SlopeWasOnCounter;
            public bool SlopeJumpHigher;
            public int LastSlopeType;
        }

        /// <summary>
        /// Shared cube ejection — slope + flat floor/ceiling + position snap.
        /// Used by cube(0), robot(4), ninja(8), football(11).
        /// Caller handles hblocked/fblocked head-bonk AFTER this returns.
        /// </summary>
        internal static EjectResult CubeEject(
            in CollisionMap map,
            int playerX_fixed, int playerY_fixed, int velY_fixed, int velX_fixed,
            bool gravFlipped, bool mini, int gameMode, bool inputHeld,
            int slopeWasOnCounter, int slopeFrames, int slopeType,
            bool slopeJumpHigher, int lastSlopeType)
        {
            var r = new EjectResult {
                NewY_fixed = playerY_fixed, NewVelY_fixed = velY_fixed,
                SlopeType = slopeType, SlopeFrames = slopeFrames,
                SlopeWasOnCounter = slopeWasOnCounter,
                SlopeJumpHigher = slopeJumpHigher, LastSlopeType = lastSlopeType,
            };

            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            int hbW = GetCubeHitboxW(mini);
            int hbH = GetCubeHitboxH(mini);
            int hbOffY = GetHitboxOffsetY(gameMode, mini, gravFlipped);

            UpdateSlopeCounters(ref r.SlopeWasOnCounter, ref r.SlopeType,
                                ref r.NewVelY_fixed, gameMode, gravFlipped, mini, ref r.LastSlopeType);

            if (!gravFlipped)
            {
                int slopeHbOffY = mini ? ((0x10 - hbH) >> 1) : 0;
                int slopeCheckY = playerY_px + slopeHbOffY + hbH - 2;

                var (slopeHit, slopeEject, newSlopeType) = CheckSlopesDown(
                    in map, playerX_px, playerX_px, slopeCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);

                if (slopeHit)
                {
                    if (slopeEject > 0)
                        r.NewY_fixed = ((r.NewY_fixed >> 8) - slopeEject) << 8;
                    r.NewVelY_fixed = 0; r.WasZeroed = true;
                    r.SlopeType = newSlopeType; r.OnGround = true;
                    if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8 || gameMode == 11))
                        r.SlopeJumpHigher = true;
                    else { r.SlopeFrames = 1; r.SlopeWasOnCounter = 3; }
                }
                else if (r.NewVelY_fixed >= 0)
                {
                    var (floorHit, floorTopY, spike) = CheckFloor(
                        in map, playerX_px, playerY_px + hbOffY, hbW, hbH);
                    if (spike) { r.Died = true; return r; }
                    if (floorHit)
                    {
                        r.NewY_fixed = (floorTopY - hbH - hbOffY) << 8;
                        r.NewVelY_fixed = 0; r.WasZeroed = true; r.OnGround = true;
                    }
                }
            }
            else
            {
                int slopeHbOffY = mini ? ((0x10 - hbH) >> 1) : 0;
                int slopeCheckY = playerY_px + slopeHbOffY + (mini ? 1 : 2) + (gameMode == 1 ? 1 : 0);

                var (ceilSlopeHit, ceilSlopeEject, ceilSlopeType) = CheckSlopesUp(
                    in map, playerX_px, playerX_px, slopeCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);

                if (ceilSlopeHit)
                {
                    r.NewY_fixed = ((r.NewY_fixed >> 8) + ceilSlopeEject - 1) << 8;
                    r.NewVelY_fixed = 0; r.WasZeroed = true;
                    r.OnGround = true; r.SlopeType = ceilSlopeType;
                    if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8 || gameMode == 11))
                        r.SlopeJumpHigher = true;
                    else { r.SlopeFrames = 1; r.SlopeWasOnCounter = 3; }
                }
                else if (r.NewVelY_fixed <= 0)
                {
                    var (ceilHit, ceilBotY, _) = CheckCeiling(
                        in map, playerX_px, playerY_px + hbOffY, hbW, hbH);
                    if (ceilHit)
                    {
                        r.NewY_fixed = (ceilBotY - hbOffY - 1) << 8;
                        r.NewVelY_fixed = 0; r.WasZeroed = true; r.OnGround = true;
                    }
                }
            }

            return r;
        }

        /// <summary>
        /// Shared ball ejection — 1px Y offset, slope + flat floor/ceiling.
        /// Used by ball(2) and pogo(9) modes.
        /// </summary>
        internal static EjectResult BallEject(
            in CollisionMap map,
            int playerX_fixed, int playerY_fixed, int velY_fixed, int velX_fixed,
            bool gravFlipped, bool mini, int gameMode, bool inputHeld,
            int slopeWasOnCounter, int slopeFrames, int slopeType,
            bool slopeJumpHigher, int lastSlopeType)
        {
            var r = new EjectResult {
                NewY_fixed = playerY_fixed, NewVelY_fixed = velY_fixed,
                SlopeType = slopeType, SlopeFrames = slopeFrames,
                SlopeWasOnCounter = slopeWasOnCounter,
                SlopeJumpHigher = slopeJumpHigher, LastSlopeType = lastSlopeType,
            };

            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            int hbW = GetCubeHitboxW(mini);
            int hbH = GetCubeHitboxH(mini);
            int miniOffset = GetMiniCenterOffsetY(mini);
            int ballYOffset = gravFlipped ? -1 : 1;
            int collisionY = playerY_px + miniOffset + ballYOffset;

            UpdateSlopeCounters(ref r.SlopeWasOnCounter, ref r.SlopeType,
                                ref r.NewVelY_fixed, gameMode, gravFlipped, mini, ref r.LastSlopeType);

            if (gravFlipped)
            {
                if (r.NewVelY_fixed <= 0)
                {
                    var (hit, ceilBotY, _) = CheckCeiling(in map, playerX_px, collisionY, hbW, hbH);
                    if (hit)
                    {
                        r.NewY_fixed = (ceilBotY - miniOffset) << 8;
                        r.NewVelY_fixed = 0; r.OnGround = true;
                    }
                }
            }
            else
            {
                int slopeHbOffY = mini ? ((0x10 - hbH) >> 1) : 0;
                int slopeCheckY = playerY_px + slopeHbOffY + hbH - 2;

                var (slopeHit, slopeEject, newSlopeType) = CheckSlopesDown(
                    in map, playerX_px, playerX_px, slopeCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);

                if (slopeHit)
                {
                    if (slopeEject > 0)
                        r.NewY_fixed = ((r.NewY_fixed >> 8) - slopeEject) << 8;
                    r.NewVelY_fixed = 0;
                    r.SlopeFrames = 1; r.SlopeWasOnCounter = 3;
                    r.SlopeType = newSlopeType; r.OnGround = true;
                }
                else if (r.NewVelY_fixed >= 0)
                {
                    var (hit, surfY, spike) = CheckFloor(in map, playerX_px, collisionY, hbW, hbH);
                    if (spike) { r.Died = true; return r; }
                    if (hit)
                    {
                        r.NewY_fixed = (surfY - hbH - miniOffset - ballYOffset) << 8;
                        r.NewVelY_fixed = 0; r.OnGround = true;
                    }
                }
            }

            return r;
        }

        /// <summary>
        /// Shared ship/UFO ejection — checks BOTH ceiling AND floor with NES velocity gates.
        /// NES bg_coll_U only fires when VelY < 0 (moving up toward ceiling).
        /// NES bg_coll_D only fires when VelY >= 0 (moving down toward floor).
        /// Used by ship(1), UFO(3), swingcopter(7).
        /// </summary>
        internal static EjectResult ShipUfoEject(
            in CollisionMap map,
            int playerX_fixed, int playerY_fixed, int velY_fixed, int velX_fixed,
            bool gravFlipped, bool mini, int gameMode, bool inputHeld,
            int slopeWasOnCounter, int slopeFrames, int slopeType,
            bool slopeJumpHigher, int lastSlopeType)
        {
            var r = new EjectResult {
                NewY_fixed = playerY_fixed, NewVelY_fixed = velY_fixed,
                SlopeType = slopeType, SlopeFrames = slopeFrames,
                SlopeWasOnCounter = slopeWasOnCounter,
                SlopeJumpHigher = slopeJumpHigher, LastSlopeType = lastSlopeType,
            };

            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            int hbW = GetCubeHitboxW(mini);
            int hbH = GetCubeHitboxH(mini);
            int hbOffY = GetHitboxOffsetY(gameMode, mini, gravFlipped);
            int collX = playerX_px;
            int collY = playerY_px + hbOffY;

            UpdateSlopeCounters(ref r.SlopeWasOnCounter, ref r.SlopeType,
                                ref r.NewVelY_fixed, gameMode, gravFlipped, mini, ref r.LastSlopeType);

            int slopeHbOffY = mini ? ((0x10 - hbH) >> 1) : 0;

            // NES velocity gates: inside bg_coll_U / bg_coll_D the SLOPE phase
            // always runs regardless of velocity, but the TILE collision phase
            // is gated:
            //   bg_coll_U tile phase: only when high_byte(vel_y) & 0x80 → VelY < 0
            //   bg_coll_D tile phase: only when !(high_byte(vel_y) & 0x80) → VelY >= 0
            // NES ufo_ship_eject runs bg_coll_U then bg_coll_D sequentially.
            // If bg_coll_U zeros currplayer_vel_y, bg_coll_D sees the ZEROED
            // velocity (>= 0) and runs its tile phase.  Must re-derive the
            // gate booleans after each phase so the second check uses live
            // velocity, exactly as the NES does.
            bool velMovingUp   = velY_fixed < 0;
            bool velMovingDown = velY_fixed >= 0;

            if (!gravFlipped)
            {
                // Normal gravity: ceiling first (secondary), floor last (primary landing surface).

                // Ceiling slopes (always run, no velocity gate — matches NES)
                int ceilCheckY = playerY_px + slopeHbOffY + (mini ? 1 : 2) + (gameMode == 1 ? 1 : 0);
                var (ceilSlopeHit, ceilSlopeEject, ceilSlopeType) = CheckSlopesUp(
                    in map, playerX_px, playerX_px, ceilCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);
                if (ceilSlopeHit)
                {
                    r.NewY_fixed = ((r.NewY_fixed >> 8) + ceilSlopeEject - 1) << 8;
                    r.NewVelY_fixed = 0;
                    r.SlopeFrames = 1; r.SlopeWasOnCounter = 3;
                    r.SlopeType = ceilSlopeType;
                }
                else if (velMovingUp)
                {
                    // Ceiling tile collision — only when moving up
                    var (ceilHit, ceilBotY, _) = CheckCeiling(in map, collX, collY, hbW, hbH);
                    if (ceilHit)
                    {
                        r.NewY_fixed = (ceilBotY - hbOffY) << 8;
                        r.NewVelY_fixed = 0;
                    }
                }

                // Re-derive velocity gates from (potentially zeroed) velocity
                // so bg_coll_D sees the live value just as NES does.
                velMovingUp   = r.NewVelY_fixed < 0;
                velMovingDown = r.NewVelY_fixed >= 0;

                // Floor slopes (always run, no velocity gate — matches NES)
                int floorCheckY = playerY_px + slopeHbOffY + hbH - 2;
                var (floorSlopeHit, floorSlopeEject, floorSlopeType) = CheckSlopesDown(
                    in map, playerX_px, playerX_px, floorCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);
                if (floorSlopeHit)
                {
                    if (floorSlopeEject > 0)
                        r.NewY_fixed = ((r.NewY_fixed >> 8) - floorSlopeEject) << 8;
                    r.NewVelY_fixed = 0;
                    r.SlopeFrames = 1; r.SlopeWasOnCounter = 3;
                    r.SlopeType = floorSlopeType;
                }
                else if (velMovingDown)
                {
                    // Floor tile collision — only when moving down
                    int updatedCollY = (r.NewY_fixed >> 8) + hbOffY;
                    var (floorHit, floorTopY, spike) = CheckFloor(in map, collX, updatedCollY, hbW, hbH);
                    if (spike) { r.Died = true; return r; }
                    if (floorHit)
                    {
                        r.NewY_fixed = (floorTopY - hbH - hbOffY) << 8;
                        r.NewVelY_fixed = 0;
                    }
                }
            }
            else
            {
                // Flipped gravity: floor first (secondary), ceiling last (primary landing surface).

                // Floor slopes (always run, no velocity gate — matches NES)
                int floorCheckY = playerY_px + slopeHbOffY + hbH - 2;
                var (floorSlopeHit, floorSlopeEject, floorSlopeType) = CheckSlopesDown(
                    in map, playerX_px, playerX_px, floorCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);
                if (floorSlopeHit)
                {
                    if (floorSlopeEject > 0)
                        r.NewY_fixed = ((r.NewY_fixed >> 8) - floorSlopeEject) << 8;
                    r.NewVelY_fixed = 0;
                    r.SlopeFrames = 1; r.SlopeWasOnCounter = 3;
                    r.SlopeType = floorSlopeType;
                }
                else if (velMovingDown)
                {
                    // Floor tile collision — only when moving down (secondary for flipped)
                    var (floorHit, floorTopY, spike) = CheckFloor(in map, collX, collY, hbW, hbH);
                    if (spike) { r.Died = true; return r; }
                    if (floorHit)
                    {
                        r.NewY_fixed = (floorTopY - hbH - hbOffY) << 8;
                        r.NewVelY_fixed = 0;
                    }
                }

                // Re-derive velocity gates from (potentially zeroed) velocity
                velMovingUp   = r.NewVelY_fixed < 0;
                velMovingDown = r.NewVelY_fixed >= 0;

                // Ceiling slopes (always run, no velocity gate — matches NES)
                int ceilCheckY = playerY_px + slopeHbOffY + (mini ? 1 : 2) + (gameMode == 1 ? 1 : 0);
                var (ceilSlopeHit, ceilSlopeEject, ceilSlopeType) = CheckSlopesUp(
                    in map, playerX_px, playerX_px, ceilCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);
                if (ceilSlopeHit)
                {
                    r.NewY_fixed = ((r.NewY_fixed >> 8) + ceilSlopeEject - 1) << 8;
                    r.NewVelY_fixed = 0;
                    r.SlopeFrames = 1; r.SlopeWasOnCounter = 3;
                    r.SlopeType = ceilSlopeType;
                }
                else if (velMovingUp)
                {
                    // Ceiling tile collision — only when moving up (primary for flipped)
                    int updatedCollY = (r.NewY_fixed >> 8) + hbOffY;
                    var (ceilHit, ceilBotY, _) = CheckCeiling(in map, collX, updatedCollY, hbW, hbH);
                    if (ceilHit)
                    {
                        r.NewY_fixed = (ceilBotY - hbOffY) << 8;
                        r.NewVelY_fixed = 0;
                    }
                }
            }

            return r;
        }
    }
}

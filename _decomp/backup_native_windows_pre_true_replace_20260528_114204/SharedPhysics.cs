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
        //  DIAGNOSTIC LOGGING HOOKS (set by callers to receive per-frame
        //  debug info from inside shared physics routines).  Null = disabled.
        // ════════════════════════════════════════════════════════════════════
        internal static Action<string>? BallEjectDiagLog;
        internal static Action<string>? SlopeDiagLog;

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
        // NES BALL_MAX_FALLSPEED table (physics_table_defines.cmp.h):
        //   BALL_MAX_FALLSPEED_lo = {0x33,0xCD, 0x00,0x00, 0x00,0x00, 0x00,0x00}
        //   BALL_MAX_FALLSPEED_hi = {0x07,0xF8, 0x06,0xFA, 0x06,0xFA, 0x05,0xFB}
        //   idx 4 (NTSC big   normal-grav)  = 0x0600 ( 1536)
        //   idx 5 (NTSC big   inverted-grav)= 0xFA00 (-1536)
        //   idx 6 (NTSC mini  normal-grav)  = 0x0500 ( 1280)
        //   idx 7 (NTSC mini  inverted-grav)= 0xFB00 (-1280)
        // PF uses NTSC values (matching BallGravity above).  Mini cap is
        // genuinely smaller (|0x500| vs |0x600|); for non-mini both grav
        // directions share magnitude 0x600 so a single value suffices.
        // For mini, normal & inverted both use magnitude 0x500 (idx 6/7)
        // so a single per-mini value still suffices — gravFlipped negation
        // happens at the call site.
        // Observed regression at lvl=electrodynamix table_idx=6, vy
        // alternating 1237/1324 (cap=1280), confirms |0x500| for mini.
        internal static int BallMaxFallSpeed(bool mini) => mini ? 0x500 : 0x600;

        // ════════════════════════════════════════════════════════════════════
        //  SHIP CONSTANTS
        // ════════════════════════════════════════════════════════════════════
        internal static int ShipGravityBase(bool mini) => mini ? 0x31 : 0x2A;
        internal static int ShipGravityAfterHold(bool mini) => mini ? 0x3B : 0x32;
        internal static int ShipGravityHoldFall(bool mini) => mini ? 0x3E : 0x34;
        // NES SHIP_GRAVITY table indexed by currplayer_table_idx (no mini-bit
        // masking).  values[] = {0x30,0xD0,0x39,0xC7,0x22,0xDE,0x27,0xD9}.
        // NTSC normal grav: idx=4 → 0x22.  NTSC mini normal grav: idx=6 → 0x27.
        // Older comment claimed SIM masks bit 2 — that masking is itself a SIM
        // bug; NES famidash does NOT mask, and we MUST match famidash 1:1.
        internal static int ShipGravity(bool mini) => mini ? 0x27 : 0x22;
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

        // All gamemode portals should share one collision geometry profile.
        // Use the canonical cube-portal table entry so mode-specific portal art
        // IDs do not drift in trigger timing.
        internal const int GAMEMODE_PORTAL_GEOM_SID = 0x00;

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

        internal static int NormalizePortalGeometrySid(int sid)
        {
            int sid8 = sid & 0xFF;
            if (IsGameModePortal(sid8)) return GAMEMODE_PORTAL_GEOM_SID;
            return sid8;
        }

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
        //  NES sprite_gamemode_adjust_heights — full 8 × 9 × 8 table
        //  (BUILD/vs-sys/famidash.lst, BE-decoded uint16 → signed int16).
        //  Outer index = currplayer_table_idx (0..7):
        //      bit2 = NTSC (TBLIDX_NTSC, set ⇒ 60 Hz)
        //      bit1 = MINI (TBLIDX_MINI)
        //      bit0 = GRAV (TBLIDX_GRAV, set ⇒ inverted gravity)
        //  Order matches NES `_sprite_gamemode_adjust_heights[]` ptr table
        //  (50_mg, 50_mG, 50_Mg, 50_MG, 60_mg, 60_mG, 60_Mg, 60_MG).
        //  Inner: 9 rows × 8 cols, indexed by table_offset|gamemode_remap.
        //  Rows  : 0=ylw_orb, 1=ylw_pad, 2=pnk_orb, 3=pnk_pad, 4=red_orb,
        //          5=ylw_bigger, 6=blk_orb, 7=ylw_smaller, 8=red_pad.
        //  Cols  : gamemode 0..7 (after trampoline pogo→swing, snake→wave,
        //          football→cube; ninja & retro-mode-robot use col 0).
        // ════════════════════════════════════════════════════════════════════
        internal static readonly short[][][] SpriteGamemodeAdjustHeights = new short[][][] {
            // [0] 50_mg
            new short[][] {
                new short[] { -1709, -1325, -1248, -1133, -1709, -1306,     0, -1114 },
                new short[] { -2381, -1152, -1517,  -979, -2669, -1536,     0, -1325 },
                new short[] { -1171,  -614,  -979,  -653, -1325, -1018,     0,  -864 },
                new short[] { -1555,  -749, -1037,  -710, -1632, -1018,     0, -1037 },
                new short[] { -2246, -1786, -1632, -1555, -2246, -1536,     0, -1478 },
                new short[] { -1709, -1709, -1786, -1709, -1709, -1709,     0, -1786 },
                new short[] {  2938,  2938,  2899,  2938,  2938,  2938,     0,  2899 },
                new short[] { -1613, -1613, -1366, -1440, -2285, -1440,     0, -1366 },
                new short[] { -3053, -1882, -1901, -1229, -3168, -2016,     0, -1958 },
            },
            // [1] 50_mG
            new short[][] {
                new short[] {  1709,  1325,  1248,  1133,  1709,  1306,     0,  1114 },
                new short[] {  2381,  1152,  1517,   979,  2669,  1536,     0,  1325 },
                new short[] {  1171,   614,   979,   653,  1325,  1018,     0,   864 },
                new short[] {  1555,   749,  1037,   710,  1632,  1018,     0,  1037 },
                new short[] {  2246,  1786,  1632,  1555,  2246,  1536,     0,  1478 },
                new short[] {  1709,  1709,  1786,  1709,  1709,  1709,     0,  1786 },
                new short[] { -2938, -2938, -2899, -2938, -2938, -2938,     0, -2899 },
                new short[] {  1613,  1613,  1366,  1440,  2285,  1440,     0,  1366 },
                new short[] {  3053,  1882,  1901,  1229,  3168,  2016,     0,  1958 },
            },
            // [2] 50_Mg
            new short[][] {
                new short[] { -1478, -1421, -1325, -1171, -1363, -1018,     0,  -806 },
                new short[] { -1997, -1286, -1478, -1114, -2208, -1229,     0,  -998 },
                new short[] { -1018,  -576, -1018,  -518, -1056,  -672,     0,  -595 },
                new short[] { -1210,  -576, -1094,  -403, -1018, -1018,     0,  -653 },
                new short[] { -1939, -1978, -1536, -1632, -1939, -1363,     0, -1018 },
                new short[] { -1709, -1709, -1651, -1709, -1709, -1709,     0, -1651 },
                new short[] {  2938,  2938,  2899,  2938,  2938,  2938,     0,  2899 },
                new short[] { -1613, -1613, -1366, -1440, -2285, -1440,     0, -1366 },
                new short[] { -2515, -2074, -1747, -1632, -2707, -1632,     0, -1114 },
            },
            // [3] 50_MG
            new short[][] {
                new short[] {  1478,  1421,  1325,  1171,  1363,  1018,     0,   806 },
                new short[] {  1997,  1286,  1478,  1114,  2208,  1229,     0,   998 },
                new short[] {  1018,   576,  1018,   518,  1056,   672,     0,   595 },
                new short[] {  1210,   576,  1094,   403,  1018,  1018,     0,   653 },
                new short[] {  1939,  1978,  1536,  1632,  1939,  1363,     0,  1018 },
                new short[] {  1709,  1709,  1651,  1709,  1709,  1709,     0,  1651 },
                new short[] { -2938, -2938, -2899, -2938, -2938, -2938,     0, -2899 },
                new short[] {  1613,  1613,  1366,  1440,  2285,  1440,     0,  1366 },
                new short[] {  2515,  2074,  1747,  1632,  2707,  1632,     0,  1114 },
            },
            // [4] 60_mg
            new short[][] {
                new short[] { -1424, -1104, -1040,  -944, -1424, -1088,     0,  -928 },
                new short[] { -1984,  -960, -1264,  -816, -2224, -1280,     0, -1104 },
                new short[] {  -976,  -512,  -816,  -544, -1104,  -848,     0,  -720 },
                new short[] { -1296,  -624,  -864,  -592, -1360,  -848,     0,  -864 },
                new short[] { -1872, -1488, -1360, -1296, -1872, -1280,     0, -1232 },
                new short[] { -1424, -1424, -1488, -1424, -1424, -1424,     0, -1488 },
                new short[] {  2448,  2448,  2416,  2448,  2448,  2448,     0,  2416 },
                new short[] { -1344, -1344, -1138, -1200, -1904, -1200,     0, -1138 },
                new short[] { -2544, -1568, -1584, -1024, -2640, -1680,     0, -1632 },
            },
            // [5] 60_mG
            new short[][] {
                new short[] {  1424,  1104,  1040,   944,  1424,  1088,     0,   928 },
                new short[] {  1984,   960,  1264,   816,  2224,  1280,     0,  1104 },
                new short[] {   976,   512,   816,   544,  1104,   848,     0,   720 },
                new short[] {  1296,   624,   864,   592,  1360,   848,     0,   864 },
                new short[] {  1872,  1488,  1360,  1296,  1872,  1280,     0,  1232 },
                new short[] {  1424,  1424,  1488,  1424,  1424,  1424,     0,  1488 },
                new short[] { -2448, -2448, -2416, -2448, -2448, -2448,     0, -2416 },
                new short[] {  1344,  1344,  1138,  1200,  1904,  1200,     0,  1138 },
                new short[] {  2544,  1568,  1584,  1024,  2640,  1680,     0,  1632 },
            },
            // [6] 60_Mg
            new short[][] {
                new short[] { -1232, -1184, -1104,  -976, -1136,  -848,     0,  -672 },
                new short[] { -1664, -1072, -1232,  -928, -1840, -1024,     0,  -832 },
                new short[] {  -848,  -480,  -848,  -432,  -880,  -560,     0,  -496 },
                new short[] { -1008,  -480,  -912,  -336,  -848,  -848,     0,  -544 },
                new short[] { -1616, -1648, -1280, -1360, -1616, -1136,     0,  -848 },
                new short[] { -1424, -1424, -1376, -1424, -1424, -1424,     0, -1376 },
                new short[] {  2448,  2448,  2416,  2448,  2448,  2448,     0,  2416 },
                new short[] { -1344, -1344, -1138, -1200, -1904, -1200,     0, -1138 },
                new short[] { -2096, -1728, -1456, -1360, -2256, -1360,     0,  -928 },
            },
            // [7] 60_MG
            new short[][] {
                new short[] {  1232,  1184,  1104,   976,  1136,   848,     0,   672 },
                new short[] {  1664,  1072,  1232,   928,  1840,  1024,     0,   832 },
                new short[] {   848,   480,   848,   432,   880,   560,     0,   496 },
                new short[] {  1008,   480,   912,   336,   848,   848,     0,   544 },
                new short[] {  1616,  1648,  1280,  1360,  1616,  1136,     0,   848 },
                new short[] {  1424,  1424,  1376,  1424,  1424,  1424,     0,  1376 },
                new short[] { -2448, -2448, -2416, -2448, -2448, -2448,     0, -2416 },
                new short[] {  1344,  1344,  1138,  1200,  1904,  1200,     0,  1138 },
                new short[] {  2096,  1728,  1456,  1360,  2256,  1360,     0,   928 },
            },
        };

        // NES table_offset constants (from sprite_loading.h L405-413).
        //   table_offset = sprite_class << 3.  Acts as a row index when divided by 8.
        internal const int SGAH_YELLOW_ORB    = 0;   // 0x00 << 3
        internal const int SGAH_YELLOW_PAD    = 8;   // 0x01 << 3
        internal const int SGAH_PINK_ORB      = 16;  // 0x02 << 3
        internal const int SGAH_PINK_PAD      = 24;  // 0x03 << 3
        internal const int SGAH_RED_ORB       = 32;  // 0x04 << 3
        internal const int SGAH_YELLOW_BIGGER = 40;  // 0x05 << 3
        internal const int SGAH_BLACK_ORB     = 48;  // 0x06 << 3
        internal const int SGAH_YELLOW_SMALLER= 56;  // 0x07 << 3
        internal const int SGAH_RED_PAD       = 64;  // 0x08 << 3

        /// <summary>
        /// NES `_sprite_gamemode_y_adjust()` (sprite_loading.h L420-456).
        /// Returns the launch velocity for a sprite class (`tableOffset`) given
        /// the current player's table-idx, gamemode, and retro-mode flag.
        /// Trampoline remap (POGO→SWING, SNAKE→WAVE, FOOTBALL→CUBE) is applied
        /// before indexing.  NINJA and (retro && ROBOT) collapse onto col 0.
        /// </summary>
        internal static int SpriteGamemodeYAdjust(int cpTableIdx, int gamemode, int tableOffset, bool retroMode = false)
        {
            // NES trampoline (sprite_loading.h L432-451)
            int gm = gamemode;
            if (gm == 9) gm = 7;       // POGO     → SWING
            else if (gm == 10) gm = 6; // SNAKE    → WAVE
            else if (gm == 11) gm = 0; // FOOTBALL → CUBE

            // NES: ((retro_mode && gm==ROBOT) || gm==NINJA) ? table_offset : gm | table_offset
            int idx;
            if ((retroMode && gm == 4) || gm == 8)
                idx = tableOffset;        // collapses to col 0 of that row
            else
                idx = gm | tableOffset;

            int row = idx >> 3;
            int col = idx & 7;
            return SpriteGamemodeAdjustHeights[cpTableIdx & 7][row][col];
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

        // NES bg_coll_mini_blocks() STAIRS cases (collision.h L373-410): the eject
        // value is `tmp8 = (temp_y & 0x0f) & 0x07` — overshoot mod 8, not mod 16.
        // Then bg_coll_return_D does `eject_D = tmp8` and the eject path applies
        // `high_byte(Y) -= eject_D`, snapping the player's feet to the nearest
        // 8-pixel boundary at or above their current position INSIDE the tile.
        // Floor surface is therefore tileTop+0 when feet are in the upper half
        // (localY < 8) and tileTop+8 when feet are in the lower half (localY >= 8).
        // Previously PF treated stairs as full-tile-top floor (topOffsetPx=0),
        // which over-ejected by 8px whenever the player overshot by >= 8.
        // dorabaebasic6 f=2293: ball overshoot localY=14 → NES eject 6 (Yhi 238→232)
        // vs PF snap-to-tile-top (Yhi 238→224) → 8-px Y divergence cascading to
        // a death/no-death split at f=2303+.
        internal static bool IsStairsType(MetatileCollision col)
        {
            return col == MetatileCollision.COL_TOP_LEFT_STAIRS
                || col == MetatileCollision.COL_TOP_RIGHT_STAIRS
                || col == MetatileCollision.COL_BOTTOM_LEFT_STAIRS
                || col == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS;
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
            // Empty/sentinel: tile arrays use -1 for "no collision". Normalize to 0x00.
            if (tid < 0) return 0x00;
            return tid;
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
            int probeCenter_px = playerLeft_px + (hbW / 2);
            int[] probeXs = new int[] { playerLeft_px, probeCenter_px, playerRight_px };

            if (!gravFlipped)
            {
                int footY_px = playerY_px + hbOffY + hbH;
                if (gameMode == 2) footY_px += 1; // Ball mode offset
                int tileBelowY = footY_px / TILE;
                int tileArrayY = tileBelowY + map.GroundRowsToReserve;

                if (tileArrayY >= map.MapHeight)
                    return true; // Ground layer

                if (tileArrayY < 0) return false;

                int localY = ((footY_px % TILE) + TILE) % TILE;
                foreach (int px in probeXs)
                {
                    int tx = px / TILE;
                    if (tx < 0 || tx >= map.MapWidth) continue;
                    int idx = tileArrayY * map.MapWidth + tx;
                    if (idx < 0 || idx >= map.Tiles.Length) continue;
                    int tid = map.Tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(tid));
                    if (col == MetatileCollision.COL_NONE) continue;

                    int tileStartX = tx * TILE;
                    int localX = Math.Max(0, Math.Min(TILE - 1, px - tileStartX));
                    // Require the foot probe pixel to actually lie inside the tile's
                    // solid region. Without TileOccupiesPixel, half-slabs (COL_TOP)
                    // would falsely report support when the player's feet are below
                    // the solid 8-pixel cap.
                    if (ProvidesFloorAtColumn(col, localX) &&
                        TileOccupiesPixel(col, localX, localY))
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

                int localY = ((valGS % TILE) + TILE) % TILE;
                foreach (int px in probeXs)
                {
                    int tx = px / TILE;
                    if (tx < 0 || tx >= map.MapWidth) continue;
                    int idx = tileArrayY * map.MapWidth + tx;
                    if (idx < 0 || idx >= map.Tiles.Length) continue;
                    int tid = map.Tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(tid));
                    if (col == MetatileCollision.COL_NONE) continue;

                    int tileStartX = tx * TILE;
                    int localX = Math.Max(0, Math.Min(TILE - 1, px - tileStartX));
                    if (ProvidesFloorAtColumn(col, localX) &&
                        TileOccupiesPixel(col, localX, localY))
                        return true;
                }
                return false;
            }
        }

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
                int mappedTileId = MapTileForCollision(tileId);
                var collision = MetatileCollisionTable.GetCollision((byte)mappedTileId);

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
            int mappedTid = MapTileForCollision(tid);
            var col = MetatileCollisionTable.GetCollision((byte)mappedTid);
            // NES bg_coll_U_D_checks: COL_ALL returns 0 when high_byte(currplayer_x) < 0x10.
            // In the PF/SIM coordinate system, tileX == 0 corresponds to the first column
            // where the player starts. The NES player starts at screen X=17 (past column 0),
            // so solid blocks in column 0 never cause floor/ceiling collision.
            if (tileX == 0 && col == MetatileCollision.COL_ALL) return MetatileCollision.COL_NONE;
            return col;
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
        /// 
        /// NES bg_coll_D (collision.h ~line 950) gates the rectangular floor probes
        /// behind `!(high_byte(currplayer_vel_y) & 0x80)` — i.e., the probes only
        /// run when the player is NOT ascending. When velY_fixed &lt; 0 the player
        /// is moving upward and bg_coll_D returns 0 (no floor), allowing
        /// bg_coll_R/bg_coll_L (called by x_movement_coll) to fire on tiles like
        /// COL_TOP whose top edge the cube's body is overlapping. Without this
        /// guard, an ascending cube whose body intersects the side of a COL_TOP
        /// slab gets snapped up onto the slab top instead of dying — a
        /// SIM/PF-only divergence from Famidash.
        /// 
        /// `velY_fixed` is the player's signed 8.8 fixed-point Y velocity
        /// (negative = upward in normal gravity). The default 0 preserves the
        /// pre-fix behavior for callers that don't track velocity (e.g. wave/snake
        /// scans).
        /// </summary>
        internal static (bool hit, int surfaceY, bool spikeDeath) CheckFloor(
            in CollisionMap map, int collX, int collY, int collW, int collH,
            int velY_fixed = 0)
        {
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("CheckFloor.in", $"cx={collX} cy={collY} w={collW} h={collH} vyf={velY_fixed}");
#endif
            // NES bg_coll_D guard: skip the rectangular floor probes entirely
            // while ascending. Slopes are handled by a separate code path in
            // both SIM and PF, so the slope-detection portion of bg_coll_D has
            // no analog here to preserve.
            if (velY_fixed < 0)
                return (false, 0, false);

            int playerBottom_px = collY + collH;
            int tileBelowY = playerBottom_px / TILE;
            int playerLeft_px = collX;
            // NES bg_coll_D uses Generic.width = CUBE_WIDTH[mini] = {0x0F, 0x08}
            // (collision.h x_movement.h, physics_table_defines.cmp.h).  Right probe
            // = X + Generic.width = X+15 for non-mini, X+8 for mini.  Center probe
            // = X + (Generic.width >> 1) = X+7 for non-mini, X+4 for mini.
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
                // NES probes: left = X+0, center = X + (Generic.width >> 1) = X+7,
                // right = X + Generic.width = X+15 (non-mini).
                int px = probeIdx == 0 ? playerLeft_px
                       : probeIdx == 1 ? playerLeft_px + (collW >> 1)
                       : playerRight_px;
                int tileX = px / TILE;

                if (tileX < 0 || tileX >= map.MapWidth) continue;

                int tileIdx = tileArrayYFloor * map.MapWidth + tileX;
                if (tileIdx < 0 || tileIdx >= map.Tiles.Length) continue;

                int tileId = map.Tiles[tileIdx];
                int mappedTileId = MapTileForCollision(tileId);
                var collision = MetatileCollisionTable.GetCollision((byte)mappedTileId);
                if (collision == MetatileCollision.COL_NONE) continue;

                int localX = px % TILE;
                int localY = playerBottom_px % TILE;

                // NES bg_coll_D spike check: bg_coll_U_D_checks ONLY handles
                // COL_DEATH_TOP and COL_DEATH_BOTTOM.  Other death types (COL_DEATH,
                // COL_DEATH_LEFT, COL_DEATH_RIGHT, etc.) are NOT checked in the
                // floor collision path — they provide no floor and no spike death.
                // Death tiles always skip the floor check (unconditional continue).
                // NOTE: Spike detection sets deathPending but does NOT prevent
                // continued searching — only if ALL probes fail to find a floor
                // AND a spike was detected do we return spike death. If a floor
                // is found, the spike is overridden (COLL_CHECK_BOTTOM line 840:
                // cube_data &= 0b1111110 clears the death bit).
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
                    // NES STAIRS use mod-8 eject (see IsStairsType comment): floor
                    // surface depends on which 8-px half the player's feet are in.
                    if (IsStairsType(collision))
                        topOffsetPx = (localY >= 8) ? 8 : 0;
                    int surfaceY = tileWorldY + topOffsetPx;
                    // NES bg_coll_D semantics: a floor is only registered when the
                    // probe pixel actually lies inside the tile's solid region.
                    // For half-slabs like COL_TOP (occupies localY 0..7) this prevents
                    // snapping the player up onto the surface when their feet have
                    // already fallen through the slab (localY > 7). Without this
                    // upper bound, the player can teleport ~8-15px back upward and
                    // survive a gap that Famidash treats as fatal.
                    if (playerBottom_px >= surfaceY &&
                        TileOccupiesPixel(collision, localX, localY))
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

        internal static (bool hit, int surfaceY, bool spikeDeath, int ejectD, MetatileCollision collisionType) CheckFloorDetailed(
            in CollisionMap map, int collX, int collY, int collW, int collH,
            int velY_fixed = 0)
        {
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("CheckFloorDetailed.in", $"cx={collX} cy={collY} w={collW} h={collH} vyf={velY_fixed}");
#endif
            if (velY_fixed < 0)
                return (false, 0, false, 0, MetatileCollision.COL_NONE);

            int playerBottom_px = collY + collH;
            int tileBelowY = playerBottom_px / TILE;
            int playerLeft_px = collX;
            int playerRight_px = collX + collW;

            int tileArrayYFloor = tileBelowY + map.GroundRowsToReserve;
            if (tileArrayYFloor < 0) return (false, 0, false, 0, MetatileCollision.COL_NONE);

            if (tileArrayYFloor >= map.MapHeight)
            {
                int groundTop = tileBelowY * TILE;
                return (true, groundTop, false, playerBottom_px & 0x0F, MetatileCollision.COL_ALL);
            }

            bool deathPending = false;

            for (int probeIdx = 0; probeIdx < 3; probeIdx++)
            {
                int px = probeIdx == 0 ? playerLeft_px
                       : probeIdx == 1 ? playerLeft_px + (collW >> 1)
                       : playerRight_px;
                int tileX = px / TILE;

                if (tileX < 0 || tileX >= map.MapWidth) continue;

                int tileIdx = tileArrayYFloor * map.MapWidth + tileX;
                if (tileIdx < 0 || tileIdx >= map.Tiles.Length) continue;

                int tileId = map.Tiles[tileIdx];
                int mappedTileId = MapTileForCollision(tileId);
                var collision = MetatileCollisionTable.GetCollision((byte)mappedTileId);
                if (collision == MetatileCollision.COL_NONE) continue;

                int localX = px % TILE;
                int localY = playerBottom_px % TILE;

                if (collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM)
                {
                    if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                        deathPending = true;
                    continue;
                }

                if (IsMiniBlockType(collision))
                {
                    if (IsMiniBlockFloorHit(collision, localX, localY))
                    {
                        int tileWorldY = tileBelowY * TILE;
                        int surfaceY = tileWorldY + GetMiniBlockFloorSurface(collision);
                        if (playerBottom_px >= surfaceY)
                        {
                            int ejectD = collision switch
                            {
                                MetatileCollision.COL_UP_LEFT or
                                MetatileCollision.COL_UP_RIGHT or
                                MetatileCollision.COL_DOWN_LEFT or
                                MetatileCollision.COL_DOWN_RIGHT or
                                MetatileCollision.COL_LEFT_SPIKE_BLOCK or
                                MetatileCollision.COL_RIGHT_SPIKE_BLOCK or
                                MetatileCollision.COL_TOP or
                                MetatileCollision.COL_BOTTOM or
                                MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT or
                                MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT => localY & 0x07,
                                MetatileCollision.COL_LEFT or
                                MetatileCollision.COL_RIGHT => localY & 0x0F,
                                _ => localY & 0x0F,
                            };
                            return (true, surfaceY, false, ejectD, collision);
                        }
                    }
                }
                else if (ProvidesFloorAtColumn(collision, localX, out int topOffsetPx))
                {
                    int tileWorldY = tileBelowY * TILE;
                    // NES STAIRS use mod-8 eject (see IsStairsType comment).
                    bool stairs = IsStairsType(collision);
                    if (stairs) topOffsetPx = (localY >= 8) ? 8 : 0;
                    int surfaceY = tileWorldY + topOffsetPx;
                    if (playerBottom_px >= surfaceY &&
                        TileOccupiesPixel(collision, localX, localY))
                    {
                        int ejectD = stairs ? (localY & 0x07) : (localY & 0x0F);
                        return (true, surfaceY, false, ejectD, collision);
                    }
                }

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
                        return (true, collisionTop, false, localY & 0x0F, collision);
                    }
                }
            }

            if (deathPending) return (false, 0, true, 0, MetatileCollision.COL_NONE);

            return (false, 0, false, 0, MetatileCollision.COL_NONE);
        }

        /// <summary>
        /// Check ceiling collision (upward).
        /// This IS the CheckCollisionUp algorithm — SIM and PF both call this.
        /// Returns (hit, ceilingBottomY, spikeDeath).
        /// 
        /// NES bg_coll_U (collision.h ~line 894) gates the rectangular ceiling
        /// probes behind `(high_byte(currplayer_vel_y) & 0x80)` — i.e., the probes
        /// only run when the player is moving upward (velY &lt; 0). When velY is
        /// non-negative the probes are skipped, allowing bg_coll_R/bg_coll_L to
        /// fire on the side of overhanging COL_BOTTOM-style slabs the player's
        /// body is intersecting from below. See CheckFloor for the symmetric
        /// COL_TOP case. `velY_fixed` is the player's signed 8.8 fixed-point Y
        /// velocity. Default 0 preserves pre-fix behavior for callers that don't
        /// track velocity (wave/snake scans).
        /// </summary>
        internal static (bool hit, int ceilingBottomY, bool spikeDeath, MetatileCollision hitCollision) CheckCeiling(
            in CollisionMap map, int collX, int collY, int collW, int collH,
            int velY_fixed = -1)
        {
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("CheckCeiling.in", $"cx={collX} cy={collY} w={collW} h={collH} vyf={velY_fixed}");
#endif
            // NES bg_coll_U guard: skip the rectangular ceiling probes entirely
            // when not ascending. Slope handling lives elsewhere in our codebase
            // so the slope portion of bg_coll_U has no analog here.
            if (velY_fixed >= 0)
                return (false, 0, false, MetatileCollision.COL_NONE);

            int playerTop_px = collY;
            int playerLeft_px = collX;
            // NES bg_coll_U uses Generic.width = CUBE_WIDTH[mini] = {0x0F, 0x08}
            // (collision.h, physics_table_defines.cmp.h).  Right probe = X+15 (non-mini).
            int playerRight_px = collX + collW;

            // NES bg_coll_U probe Y (collision.h:893):
            //   temp_y = Generic.y + (mini ? (0x10-h)>>1 : 0) + 1
            // For non-mini: temp_y = top + 1.  For mini: temp_y = top + 4 + 1 = top + 5
            // (the caller passes collY = playerY + hbOffY so for both cases the probe
            // is at collY + 1 — i.e. ONE pixel BELOW the player's hitbox top, which
            // means INSIDE the player's body.)  Both the spike and solid-tile probes
            // share this Y exactly; using top - 1 (1px above) detects the wrong row.
            int probeY = playerTop_px + 1;

            // --- Spike death pre-check: 3 X-points at probeY (= top + 1) ---
            // NES bg_coll_U checks left, left+width/2, left+width — same Y as the
            // solid-tile probe below.
            bool spikeFound = false;
            {
                int checkY = probeY;
                for (int cpIdx = 0; cpIdx < 3; cpIdx++)
                {
                    // NES probes: left=X+0, center=X+(Generic.width>>1)=X+7, right=X+Generic.width=X+15
                    int px = cpIdx == 0 ? playerLeft_px
                           : cpIdx == 1 ? playerLeft_px + (collW >> 1)
                           : playerRight_px;
                    int tileX = px / TILE;
                    int tileY = checkY / TILE;

                    if (tileX < 0 || tileX >= map.MapWidth) continue;

                    int checkTileArrayY = tileY + map.GroundRowsToReserve;
                    if (checkTileArrayY < 0 || checkTileArrayY >= map.MapHeight) continue;

                    int checkTileIdx = checkTileArrayY * map.MapWidth + tileX;
                    if (checkTileIdx < 0 || checkTileIdx >= map.Tiles.Length) continue;

                    int checkTileId = map.Tiles[checkTileIdx];
                    int mappedCheckTileId = MapTileForCollision(checkTileId);
                    var checkCollision = MetatileCollisionTable.GetCollision((byte)mappedCheckTileId);

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

            int tileAboveY = probeY >= 0
                ? probeY / TILE
                : (probeY - TILE + 1) / TILE;

            // Validate using array Y (world Y + groundRowsToReserve), not raw world Y,
            // because negative world tile rows are valid when groundRowsToReserve > 0.
            int tileArrayY = tileAboveY + map.GroundRowsToReserve;
            if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return (false, 0, spikeFound, MetatileCollision.COL_NONE);

            // NES bg_coll_U solid-tile probe: 3 specific X-points (left, mid, right),
            // not column iteration.  Iterating every column the player overlaps
            // false-detects ceiling tiles that the NES probes miss when the
            // player's hitbox straddles a tile boundary but none of the 3 probes
            // land in the solid tile (e.g. xstep mini ball entering the wave
            // section: PF f=3059 falsely ejected because column tx+1 had COL_ALL,
            // even though all 3 NES probes at X=playerX/+4/+8 were in column tx).
            for (int cpIdx = 0; cpIdx < 3; cpIdx++)
            {
                int probeX = cpIdx == 0 ? playerLeft_px
                           : cpIdx == 1 ? playerLeft_px + (collW >> 1)
                                        : playerRight_px;
                int tx = probeX / TILE;
                if (tx < 0 || tx >= map.MapWidth) continue;

                int tileIdx = tileArrayY * map.MapWidth + tx;
                if (tileIdx < 0 || tileIdx >= map.Tiles.Length) continue;

                int tileId = map.Tiles[tileIdx];
                int mappedTileId = MapTileForCollision(tileId);
                var collision = MetatileCollisionTable.GetCollision((byte)mappedTileId);

                if (collision == MetatileCollision.COL_NONE) continue;

                int tileWorldX = tx * TILE;
                int tileWorldY = tileAboveY * TILE;
                int localX = ((probeX % TILE) + TILE) % TILE;
                int localY = ((probeY % TILE) + TILE) % TILE;

                // Complex collision types (L-shapes, diagonals)
                if (IsComplexCollisionType(collision))
                {
                    if (CheckComplexCollision(collision, localX, localY))
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
                        return (true, collisionBottom, spikeFound, collision);
                    }
                }
                else
                {
                    // Simple rectangular collision
                    var (colLeft, colTop, colRight, colBottom) = GetCollisionBounds(collision);
                    if (colRight <= colLeft || colBottom <= colTop) continue;

                    int collisionTop_px = tileWorldY + colTop;
                    int collisionBottom_px = tileWorldY + colBottom;

                    // NES bg_coll_U behavior: same mini-block distinction as floor.
                    bool isMiniBlock = collision == MetatileCollision.COL_UP_LEFT ||
                                       collision == MetatileCollision.COL_UP_RIGHT ||
                                       collision == MetatileCollision.COL_DOWN_LEFT ||
                                       collision == MetatileCollision.COL_DOWN_RIGHT ||
                                       collision == MetatileCollision.COL_LEFT_SPIKE_BLOCK ||
                                       collision == MetatileCollision.COL_RIGHT_SPIKE_BLOCK;

                    // Probe pixel must be inside the tile's solid X-range too
                    // (NES bg_collision_sub returns the tile's collision type
                    // only at the precise probe pixel; bg_coll_U_D_checks /
                    // bg_coll_mini_blocks then test the local position).
                    if (localX < colLeft || localX >= colRight) continue;

                    // Probe pixel must be inside the tile's solid Y-range too.
                    // NES bg_coll_U_D_checks doesn't handle COL_BOTTOM (its solid
                    // region is localY 8..15); bg_coll_mini_blocks for COL_BOTTOM
                    // requires `(temp_y & 0x0f) >= 8`. Without the lower-bound
                    // probeY >= collisionTop_px guard, PF false-hits a stack of
                    // COL_BOTTOMs when the player's ceiling probe is in the
                    // empty top half of the lower tile (electroman UFO regression).
                    bool yHit = isMiniBlock
                        ? (probeY >= collisionTop_px && probeY < collisionBottom_px)
                        : (probeY >= collisionTop_px && probeY < collisionBottom_px);

                    if (yHit)
                    {
                        return (true, collisionBottom_px, spikeFound, collision);
                    }
                }
            }

            return (false, 0, spikeFound, MetatileCollision.COL_NONE);
        }

        /// <summary>
        /// CheckCenterPointDeath — single center pixel death check.
        /// Center point: (x + (w>>1) - 1, y + (h>>1) + hitboxOffsetY)
        ///
        /// NES bg_coll_death calls
        ///   bg_coll_U_D_checks() || bg_coll_mini_blocks() || bg_coll_spikes() || bg_coll_slope()
        /// at the center point.  bg_coll_mini_blocks kills when the center pixel is in
        /// the SOLID half of COL_TOP / COL_BOTTOM / COL_DOWN_LEFT etc. — without this,
        /// horizontally walking into the side of a half-slab is survived.
        /// </summary>
        internal static bool CheckCenterPointDeath(
            in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY)
        {
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("CheckCenterPointDeath.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} hbOffY={hbOffY}");
#endif
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

            // bg_coll_spikes — pure spike/death tiles
            if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                return true;

            // bg_coll_mini_blocks + bg_coll_U_D_checks — partial-collision and
            // full-block tiles.  Center inside the SOLID region = death.
            // Now includes COL_ALL / COL_FLOOR_CEIL / COL_NO_SIDE: NES
            // bg_coll_U_D_checks returns 1 for these full-block cases, so
            // center-inside-block kills on NES.  Now that bg_coll_D probes
            // match NES (collW, not collW+1), eject snap distance also matches
            // and the previously-feared false positives no longer occur.
            switch (collision)
            {
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                case MetatileCollision.COL_NO_SIDE:
                case MetatileCollision.COL_BOTTOM:
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_LEFT:
                case MetatileCollision.COL_RIGHT:
                case MetatileCollision.COL_UP_LEFT:
                case MetatileCollision.COL_UP_RIGHT:
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    if (TileOccupiesPixel(collision, localX, localY))
                        return true;
                    break;
            }

            return false;
        }

        /// <summary>
        /// CheckDeathCollision — center-point death check matching NES bg_coll_death.
        /// gameMode: current game mode (pass -1 to skip solid-block check for backward compat).
        /// </summary>
        internal static bool CheckDeathCollision(
            in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY,
            int gameMode = -1, bool dblocked = false)
        {
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("CheckDeathCollision.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} mode={gameMode} dblk={(dblocked?1:0)}");
#endif
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

            if (MetatileCollisionTable.TileKillsAtPixel(col, localX, localY))
                return true;

            // NES bg_coll_death also calls bg_coll_mini_blocks() and
            // bg_coll_U_D_checks() at the center point.  bg_coll_U_D_checks
            // returns 1 for the full-block cases COL_ALL / COL_FLOOR_CEIL /
            // COL_NO_SIDE — center-inside-block kills on NES.  Now that
            // bg_coll_D probes match NES (collW, not collW+1), eject snap
            // distance matches and previous false positives no longer occur.
            // NES bg_coll_death special case:
            // when dblocked is set and mode is wave, it skips bg_coll_U_D_checks()
            // and only evaluates mini-blocks/top-bottom/spikes/slope.
            // That means center-inside full-block classes COL_ALL/COL_FLOOR_CEIL/
            // COL_NO_SIDE must NOT kill in this specific path.
            bool waveDbSkipUdChecks = (gameMode == 6 && dblocked);

            switch (col)
            {
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                case MetatileCollision.COL_NO_SIDE:
                    if (waveDbSkipUdChecks)
                        break;
                    if (TileOccupiesPixel(col, localX, localY))
                        return true;
                    break;

                case MetatileCollision.COL_BOTTOM:
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_LEFT:
                case MetatileCollision.COL_RIGHT:
                case MetatileCollision.COL_UP_LEFT:
                case MetatileCollision.COL_UP_RIGHT:
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    if (TileOccupiesPixel(col, localX, localY))
                        return true;
                    break;
            }

            return false;
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
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("CheckFloorSpikes.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} mini={(mini?1:0)}");
#endif

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
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("CheckFloorSpikes2.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} mini={(mini?1:0)}");
#endif

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
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("CheckForwardCollision.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} hbOffY={hbOffY} mode={gameMode} mini={(mini?1:0)} gF={(gravFlipped?1:0)} skipSlope={(skipSlopeCheck?1:0)}");
#endif
            if (!skipSlopeCheck && HasSlopeNearFeet(map, playerX_px, playerY_px, hbW, hbH, hbOffY))
                return false;

            int rightEdge_px = playerX_px + hbW;
            // NES bg_side_coll_common: tmp1 = Y + (mini ? (0x10-h)/2 : 0) + h/2
            // Generic.height = CUBE_HEIGHT[mini] (15 / 7) or WAVE_HEIGHT (8) — *hitbox* height.
            // For non-mini cube: h=15 -> Y + 7 (NOT Y + 8 — that's sprite center, wrong).
            // For mini cube: (16-7)/2 + 7/2 = 4 + 3 = Y + 7, then ± grav adj for cube/robot/ninja.
            int centerY_px = playerY_px + (mini ? (((0x10 - hbH) >> 1) + (hbH >> 1)) : (hbH >> 1));
            if (mini && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                centerY_px += gravFlipped ? 3 : -2;

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
        /// Returns the Y nudge to apply (0 if no slope at probe, +2 for upside-down slopes, -2 for normal)
        /// along with the slopeType encoded by NES bg_coll_slope (0 if no hit).  When slopeType != 0
        /// the caller must propagate SlopeType / SlopeFrames=1 / SlopeWasOnCounter=3 (NES col_end)
        /// — or SlopeJumpHigher=1 when input is held for cube/robot/ninja.  Without that propagation
        /// the next frame's x_movement_coll equivalent (PfUpdateSlopeCounters_Fresh) will not fire
        /// apply_slope_vel and the cube silently misses the side-slope velocity push.
        /// Only call when slopeWasOnCounter == 0 (NES skips nudge if already on a slope).
        /// </summary>
        internal static (int nudge, int slopeType) GetForwardSlopeNudge(
            in CollisionMap map, int playerX_px, int playerY_px,
            int hbW, int hbH, int hbOffY, int gameMode, bool mini, bool gravFlipped)
        {
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("GetForwardSlopeNudge.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} hbOffY={hbOffY} mode={gameMode} mini={(mini?1:0)} gF={(gravFlipped?1:0)}");
#endif
            int rightEdge_px = playerX_px + hbW;
            // Match NES bg_side_coll_common: probe Y uses *hitbox* height, not sprite height.
            int centerY_px = playerY_px + (mini ? (((0x10 - hbH) >> 1) + (hbH >> 1)) : (hbH >> 1));
            if (mini && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                centerY_px += gravFlipped ? 3 : -2;

            int tileX = rightEdge_px / TILE;
            int tileY = centerY_px / TILE;
            int tileArrayY = tileY + map.GroundRowsToReserve;

            if (tileX < 0 || tileX >= map.MapWidth) return (0, 0);
            if (tileArrayY < 0 || tileArrayY >= map.MapHeight) return (0, 0);

            int tileIdx = tileArrayY * map.MapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= map.Tiles.Length) return (0, 0);

            int tileId = map.Tiles[tileIdx];
            int mappedTid = MapTileForCollision(tileId);
            var collision = MetatileCollisionTable.GetCollision((byte)mappedTid);

            if (!IsSlopeTile(collision)) return (0, 0);

            // NES bg_side_coll_common dispatches to bg_coll_slope() which does the
            // wedge test (tmp4 >= tmp7).  The nudge is only applied if the probe
            // pixel is INSIDE the slope's solid region.  Without this gate, PF
            // false-nudges +2 every time the probe lands in the empty half of a
            // ceiling slope tile (e.g. dreamer wave-portal entry, COL_SLOPE_LU45
            // at probe localX=14,localY=14 → tmp7=14, tmp4=1 → no hit on NES).
            var (wedgeHit, _, hitSlopeType) = SlopeCalc(rightEdge_px, centerY_px, collision);
            if (!wedgeHit) return (0, 0);

            // NES: SLOPE_UPSIDEDOWN slopes nudge +2 (push away from ceiling slope),
            // regular (floor) slopes nudge -2 (push away from floor slope).
            bool isUpsideDown = (collision == MetatileCollision.COL_SLOPE_RU45 ||
                                 collision == MetatileCollision.COL_SLOPE_LU45) ||
                                (collision >= MetatileCollision.COL_SLOPE_RU22_RIGHT &&
                                 collision <= MetatileCollision.COL_SLOPE_LU22_LEFT) ||
                                (collision >= MetatileCollision.COL_SLOPE_RU66_TOP &&
                                 collision <= MetatileCollision.COL_SLOPE_LU66_TOP);
            return (isUpsideDown ? 2 : -2, hitSlopeType);
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
#if !DISABLE_DEBUG_LOGGING
            int _cgrVy = velY, _cgrY = posY;
#endif
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
                var (hit, _, spikeDeath, _) = CheckCeiling(map, playerX_px, playerTop - 2, hbW, 2);
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
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("SlopeCalc.in", $"tx={temp_x} ty={temp_y} col={(int)collision}");
#endif
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
                    // NES bg_coll_slope early-return-1: does NOT update currplayer_slope_type.
                    // Return slopeType=0 sentinel so the caller's side gate runs against the
                    // prior lastSlopeType (matches NES bg_coll_return_slope_* exactly).
                    if ((temp_x & 0x0f) >= 0x08) return (true, temp_y & 0x0f, 0);
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0111; break;
                case MetatileCollision.COL_SLOPE_LD66_TOP:
                    if ((temp_x & 0x0f) >= 0x08) return (false, 0, 0);
                    tmp7 = ((temp_x & 0x07) << 1) & 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0011; break;
                case MetatileCollision.COL_SLOPE_LD66_BOT:
                    // NES early-return-1: slope_type unchanged. Return slopeType=0 sentinel.
                    if ((temp_x & 0x0f) < 0x08) return (true, temp_y & 0x0f, 0);
                    tmp7 = ((temp_x & 0x0f) << 1) & 0x0f; tmp4 = temp_y & 0x0f; slopeType = 0b0011; break;
                case MetatileCollision.COL_SLOPE_RU66_TOP:
                    if ((temp_x & 0x0f) < 0x08) return (false, 0, 0);
                    tmp7 = (((temp_x & 0x07) << 1) & 0x0f) ^ 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1111; break;
                case MetatileCollision.COL_SLOPE_RU66_BOT:
                    // NES early-return-1: slope_type unchanged. Return slopeType=0 sentinel.
                    if ((temp_x & 0x0f) >= 0x08) return (true, temp_y & 0x0f, 0);
                    tmp7 = (((temp_x & 0x0f) << 1) & 0x0f) ^ 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1111; break;
                case MetatileCollision.COL_SLOPE_LU66_TOP:
                    if ((temp_x & 0x0f) >= 0x08) return (false, 0, 0);
                    tmp7 = ((temp_x & 0x07) << 1) & 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1011; break;
                case MetatileCollision.COL_SLOPE_LU66_BOT:
                    // NES early-return-1: slope_type unchanged. Return slopeType=0 sentinel.
                    if ((temp_x & 0x0f) < 0x08) return (true, temp_y & 0x0f, 0);
                    tmp7 = ((temp_x & 0x0f) << 1) & 0x0f; tmp4 = (temp_y & 0x0f) ^ 0x0f; slopeType = 0b1011; break;

                default: return (false, 0, 0);
            }

            if (tmp4 >= tmp7)
                return (true, tmp4 - tmp7, slopeType);
            // NES bg_coll_slope: jump-table case body assigns currplayer_slope_type
            // BEFORE the `tmp4 >= tmp7` wedge test. On miss (tmp1=0), slope_type stays
            // updated — propagates to apply_slope_vel via the caller's side gate.
            // Return slopeType on miss so CheckSlopes{Down,Up} can mirror this.
            return (false, 0, slopeType);
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
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("CheckSlopesDown.in", $"X={playerX_px} bx={checkBaseX} by={checkBaseY} w={checkWidth} mode={gameMode} gF={(gravFlipped?1:0)} vx={velX_fixed} lst={lastSlopeType}");
#endif
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

                // NES bg_coll_return_slope_D side gate reads `currplayer_slope_type` AFTER
                // bg_coll_slope ran. The early-return-1 sentinel paths (RD66_BOT/LD66_BOT/
                // RU66_BOT/LU66_BOT outside the wedge half) do NOT update slope_type — so
                // NES evaluates the gate against the PRIOR value (currplayer_last_slope_type
                // or the prior probe's update). SlopeCalc returns slopeType=0 to signal this
                // sentinel; for the gate check only, fall back to lastSlopeType.
                int gateSlopeType = (hit && slopeType == 0) ? lastSlopeType : slopeType;

                if (probe == 0 && (gateSlopeType & SLOPE_RISING) != 0)
                {
                    // NES bg_coll_slope col_end: GAMEMODE_CUBE/ROBOT/NINJA (0/4/8) only.
                    // Football (11) is NOT in the NES branch — do not include.
                    if (hit && slopeType != 0 && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                        slopeJumpHigher = true;
                    continue;
                }
                if (probe == 1 && (gateSlopeType & SLOPE_RISING) == 0)
                {
                    if (hit && slopeType != 0 && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                        slopeJumpHigher = true;
                    continue;
                }

                if (hit)
                {
                    // NES order (bg_coll_slope → bg_coll_return_slope_*):
                    //   1) bg_coll_slope() may call unstick() → tmp8 = 4 for non-cube modes
                    //      (this is the "ship/UFO ejection = 4" override below).
                    //   2) bg_coll_return_slope_* then runs the lastSlopeType swap which can
                    //      OVERRIDE tmp8 with high_byte(vel_x).
                    // Apply in the same order — ship/UFO override FIRST, swap LAST — so the
                    // swap wins when both conditions fire (matches NES exactly).
                    // NES col_end's "else" branch fires for ALL non-cube/robot/ninja modes
                    // (ship/ball/UFO/spider/wave/swing/pogo/snake/football).  Anything not
                    // in {0,4,8} gets the unstick conditional.
                    // slopeType==0 sentinel: bg_coll_slope early-return-1 path (66°_BOT outside
                    // the wedge half). NES never enters col_end → no unstick override.
                    if (slopeType != 0 && gameMode != 0 && gameMode != 4 && gameMode != 8)
                    {
                        int aIdx = ((slopeType & SLOPE_RISING) != 0 ? 4 : 0)
                                 | ((slopeType & SLOPE_UD) != 0 ? 2 : 0)
                                 | (gravFlipped ? 1 : 0);
                        bool aCheck = (aIdx == 0 || aIdx == 3 || aIdx == 4 || aIdx == 7);
                        if (aCheck ? inputHeld : !inputHeld) ejection = 4;
                    }

                    if ((lastSlopeType & SLOPE_RISING) != 0 && (slopeType & SLOPE_RISING) == 0)
                    {
                        if (lastSlopeType != 0 && slopeType != 0)
                        {
                            slopeType = lastSlopeType;
                            ejection = velX_fixed >> 8;
                        }
                    }
                    if (slopeType != 0) lastSlopeType = slopeType;

                    bestEjection = ejection; bestSlopeType = slopeType; anyHit = true;
                }
                else
                {
                    // NES `bg_coll_slope` ALWAYS sets currplayer_slope_type per the tile
                    // (jump-table case body runs before the tmp4>=tmp7 wedge test).
                    // `bg_coll_return_slope_D` only RESETS slope_type when the probe-side
                    // mismatches direction (already handled by the gate-continue above).
                    // When the gate passes BUT the wedge misses, NES leaves
                    // currplayer_slope_type at the new tile's value (last_slope_type and
                    // eject_D are NOT updated since tmp1==0). This side effect propagates
                    // to apply_slope_vel which uses the FINAL slope_type for vy sign.
                    // Mirror: update bestSlopeType (NOT lastSlopeType / bestEjection / anyHit).
                    // slopeType==0 sentinel (66°_BOT early-return-1): bg_coll_slope did NOT
                    // update slope_type, so PF must not either — skip.
                    if (slopeType != 0) bestSlopeType = slopeType;
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
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("CheckSlopesUp.in", $"X={playerX_px} bx={checkBaseX} by={checkBaseY} w={checkWidth} mode={gameMode} gF={(gravFlipped?1:0)} vx={velX_fixed} lst={lastSlopeType}");
#endif
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

                // See CheckSlopesDown: NES early-return-1 sentinel preserves slope_type;
                // gate must run against lastSlopeType.
                int gateSlopeType = (hit && slopeType == 0) ? lastSlopeType : slopeType;

                if (probe == 0 && (gateSlopeType & SLOPE_RISING) != 0)
                {
                    // NES bg_coll_slope col_end: GAMEMODE_CUBE/ROBOT/NINJA (0/4/8) only.
                    if (hit && slopeType != 0 && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                        slopeJumpHigher = true;
                    continue;
                }
                if (probe == 1 && (gateSlopeType & SLOPE_RISING) == 0)
                {
                    if (hit && slopeType != 0 && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                        slopeJumpHigher = true;
                    continue;
                }

                if (hit)
                {
                    // NES order (bg_coll_slope → bg_coll_return_slope_*):
                    //   1) bg_coll_slope() may call unstick() → tmp8 = 4 for non-cube modes
                    //      (this is the "ship/UFO ejection = 4" override below).
                    //   2) bg_coll_return_slope_* then runs the lastSlopeType swap which can
                    //      OVERRIDE tmp8 with high_byte(vel_x).
                    // Apply in the same order — ship/UFO override FIRST, swap LAST — so the
                    // swap wins when both conditions fire (matches NES exactly).
                    // NES col_end's "else" branch fires for ALL non-cube/robot/ninja modes.
                    // slopeType==0 sentinel: bg_coll_slope early-return-1 path (66°_BOT outside
                    // the wedge half). NES never enters col_end → no unstick override.
                    if (slopeType != 0 && gameMode != 0 && gameMode != 4 && gameMode != 8)
                    {
                        int aIdx = ((slopeType & SLOPE_RISING) != 0 ? 4 : 0)
                                 | ((slopeType & SLOPE_UD) != 0 ? 2 : 0)
                                 | (gravFlipped ? 1 : 0);
                        bool aCheck = (aIdx == 0 || aIdx == 3 || aIdx == 4 || aIdx == 7);
                        if (aCheck ? inputHeld : !inputHeld) ejection = 4;
                    }

                    if ((lastSlopeType & SLOPE_RISING) != 0 && (slopeType & SLOPE_RISING) == 0)
                    {
                        if (lastSlopeType != 0 && slopeType != 0)
                        {
                            slopeType = lastSlopeType;
                            ejection = velX_fixed >> 8;
                        }
                    }
                    if (slopeType != 0) lastSlopeType = slopeType;

                    bestEjection = ejection; bestSlopeType = slopeType; anyHit = true;
                }
                else
                {
                    // See CheckSlopesDown: NES bg_coll_slope unconditionally sets
                    // currplayer_slope_type per the tile; the gate only resets on
                    // side-mismatch. Wedge miss with gate-passed → slope_type propagates.
                    if (slopeType != 0) bestSlopeType = slopeType;
                }
            }

            return (anyHit, bestEjection, bestSlopeType);
        }

        /// <summary>
        /// State-only slope wedge probe — mirrors the SIDE EFFECTS of NES
        /// bg_coll_slope() + bg_coll_return_slope_U/D filter without doing
        /// position/velocity ejection.
        ///
        /// NES `bg_coll_slope` sets `slope_frames=1` + `was_on_slope_counter=3`
        /// + `slope_type=&lt;new&gt;` whenever the wedge passes — and those side
        /// effects PERSIST even when `bg_coll_return_slope_U/D` asymmetric
        /// filter rejects (the filter only restores slope_type from
        /// last_slope_type and returns 0 from the caller's perspective).
        ///
        /// Used by:
        ///   - BallEject normal-grav branch to mirror NES bg_coll_U slope
        ///     section (runs unconditionally on every ball_eject frame).
        ///   - Any code path needing the unconditional slope_frames flag
        ///     propagation that NES does for free.
        ///
        /// Probes 2 X-points (LEFT, RIGHT) at given Y. Returns true if any
        /// wedge passed (caller can ignore — useful for diagnostics).
        /// </summary>
        internal static bool ApplySlopeWedgeStateOnly(
            in CollisionMap map,
            int playerX_px, int checkBaseX, int checkBaseY, int checkWidth,
            bool inputHeld, int gameMode, bool gravFlipped,
            ref int slopeFrames, ref int slopeWasOnCounter, ref int slopeType,
            ref int lastSlopeType, ref bool slopeJumpHigher)
        {
            var slog = SlopeDiagLog;
            if (slog != null)
                slog($"[SLOPE/WEDGE_IN] pX={playerX_px} baseX={checkBaseX} baseY={checkBaseY} W={checkWidth} input={inputHeld} gm={gameMode} gF={gravFlipped} | sF={slopeFrames} swOn={slopeWasOnCounter} sT={slopeType} lst={lastSlopeType} jH={slopeJumpHigher}");
            if (playerX_px < 0x10) { if (slog != null) slog("[SLOPE/WEDGE_OUT] reject pX<0x10"); return false; }
            bool anyWedge = false;

            for (int probe = 0; probe < 2; probe++)
            {
                int temp_x = checkBaseX + (probe * checkWidth);
                int tileX = temp_x / TILE;
                int tileY = checkBaseY / TILE;

                var collision = GetTileCollision(in map, tileX, tileY);
                if (collision < MetatileCollision.COL_SLOPE_RD45 || collision > MetatileCollision.COL_SLOPE_LU66_TOP)
                {
                    if (slog != null) slog($"[SLOPE/WEDGE_PROBE{probe}] X={temp_x} Y={checkBaseY} tile=({tileX},{tileY}) coll={collision} -> NOT_SLOPE");
                    continue;
                }

                var (hit, _, newSlopeType) = SlopeCalc(temp_x, checkBaseY, collision);
                if (!hit)
                {
                    if (slog != null) slog($"[SLOPE/WEDGE_PROBE{probe}] X={temp_x} Y={checkBaseY} tile=({tileX},{tileY}) coll={collision} -> WEDGE_MISS");
                    continue;
                }
                if (newSlopeType == 0)
                {
                    if (slog != null) slog($"[SLOPE/WEDGE_PROBE{probe}] X={temp_x} Y={checkBaseY} tile=({tileX},{tileY}) coll={collision} -> SENTINEL_HIT_NO_COL_END");
                    continue;
                }

                if (slog != null) slog($"[SLOPE/WEDGE_PROBE{probe}] X={temp_x} Y={checkBaseY} tile=({tileX},{tileY}) coll={collision} -> WEDGE_PASS newSlopeT={newSlopeType}");
                anyWedge = true;

                // NES bg_coll_slope col_end side effects fire WHENEVER tmp4 >= tmp7:
                //   - For CUBE/ROBOT/NINJA with input held: make_cube_jump_higher = 1
                //     (no slope_frames update).
                //   - Otherwise: slope_frames = 1, was_on_slope_counter = 3.
                //     unstick() may also fire (a_check_lookup), but we ignore
                //     position-side unstick here — only counter state matters
                //     for the next x_movement_coll apply_slope_vel.
                if (gameMode == 0 || gameMode == 4 || gameMode == 8)
                {
                    if (inputHeld)
                    {
                        slopeJumpHigher = true;
                        if (slog != null) slog($"[SLOPE/WEDGE_STATE] probe={probe} CUBE+input -> jumpHigher=true (no sF/swOn change)");
                    }
                    else
                    {
                        slopeFrames = 1;
                        slopeWasOnCounter = 3;
                        if (slog != null) slog($"[SLOPE/WEDGE_STATE] probe={probe} CUBE+!input -> sF:=1 swOn:=3");
                    }
                }
                else
                {
                    slopeFrames = 1;
                    slopeWasOnCounter = 3;
                    if (slog != null) slog($"[SLOPE/WEDGE_STATE] probe={probe} OTHER(gm={gameMode}) -> sF:=1 swOn:=3");
                }

                // NES bg_coll_slope sets currplayer_slope_type tentatively.
                int tentativeSlopeType = newSlopeType;

                // bg_coll_return_slope_U/D asymmetric filter:
                //   probe 0 (LEFT)  + RISING        → restore from last, "return 0" (state side effects keep)
                //   probe 1 (RIGHT) + !RISING       → restore from last, "return 0"
                bool filterReject =
                    (probe == 0 && (tentativeSlopeType & SLOPE_RISING) != 0) ||
                    (probe == 1 && (tentativeSlopeType & SLOPE_RISING) == 0);

                if (filterReject)
                {
                    if (slog != null) slog($"[SLOPE/WEDGE_FILTER] probe={probe} tentT={tentativeSlopeType} REJECT slopeT:={lastSlopeType} (lst)");
                    slopeType = lastSlopeType;
                }
                else
                {
                    int oldT = tentativeSlopeType;
                    // Filter passed — apply lastSlopeType swap (mirrors
                    // bg_coll_return_slope_*'s RISING→!RISING transition handler).
                    if ((lastSlopeType & SLOPE_RISING) != 0 && (tentativeSlopeType & SLOPE_RISING) == 0)
                    {
                        if (lastSlopeType != 0 && tentativeSlopeType != 0)
                            tentativeSlopeType = lastSlopeType;
                    }
                    slopeType = tentativeSlopeType;
                    if (tentativeSlopeType != 0) lastSlopeType = tentativeSlopeType;
                    if (slog != null) slog($"[SLOPE/WEDGE_FILTER] probe={probe} tentT={oldT} ACCEPT slopeT:={tentativeSlopeType} lst:={lastSlopeType}");
                }
            }

            if (slog != null)
                slog($"[SLOPE/WEDGE_OUT] anyWedge={anyWedge} | sF={slopeFrames} swOn={slopeWasOnCounter} sT={slopeType} lst={lastSlopeType} jH={slopeJumpHigher}");
            return anyWedge;
        }

        /// <summary>
        /// Slope counter decrement — called at START of eject.
        /// Mirrors NES decrement_was_on_slope().
        /// </summary>
        /// <param name="posY_fixed">
        /// Player Y in fixed-point. On EXIT (counter reaches 0) the vy delta
        /// is added here as well, to mirror the NES ordering where
        /// decrement_was_on_slope() applies the delta BEFORE the subsequent
        /// common_gravity_routine integrates `posY += vy`. PF runs
        /// CubeGravity (which already did `posY += vy0+accel`) BEFORE this
        /// call, so without bumping posY by `delta` the PF posY would lag
        /// NES by exactly `delta` (= ±0x100 = ±1 px for CUBE_22 NTSC).
        /// Observed at dorabaebasic6 f=884 (cube slope EXIT, PF Y=330 vs
        /// MT Y=329, persistent 3 frames until next ground contact).
        /// </param>
        internal static void UpdateSlopeCounters(
            ref int slopeWasOnCounter, ref int slopeType, ref int velY_fixed,
            ref int posY_fixed,
            int gameMode, bool gravFlipped, bool mini, ref int lastSlopeType)
        {
            var slog = SlopeDiagLog;
            int swOn_in = slopeWasOnCounter, sT_in = slopeType, lst_in = lastSlopeType, vy_in = velY_fixed;
            int py_in = posY_fixed;
            if (slopeWasOnCounter > 0)
            {
                slopeWasOnCounter--;
                if (slopeWasOnCounter == 0)
                {
                    // NES decrement_was_on_slope (bgmtest_huge.c L446-486) only
                    // applies EXIT_SLOPE_* for GAMEMODE_BALL (2) and GAMEMODE_CUBE (0).
                    // Pogo (9) and football (11) are NOT in the NES switch — do not include.
                    //
                    // NES asm trick replaces the gravity bit of table_idx with the
                    // slope's UPSIDEDOWN bit:
                    //   tableIdx = (table_idx & ~TBLIDX_GRAV) | (slope_type & SLOPE_UPSIDEDOWN ? 1 : 0)
                    // PF previously used gravFlipped here, which produces the wrong sign of
                    // EXIT_SLOPE_CUBE_22 when slope_UD differs from gravFlipped (e.g.
                    // gravity-flipped player on a non-UD slope or vice versa).
                    //
                    // NES table_idx layout: bit0=GRAV(replaced by slope_UD here),
                    // bit1=MINI, bit2=NTSC. Editor always runs NTSC, so bit2 must
                    // be set unconditionally. Previously PF placed MINI at bit2,
                    // selecting PAL slot 0/1 (EXIT_SLOPE_CUBE_22 = ±0x133 = ±307)
                    // instead of NTSC slot 4/5 (±0x100 = ±256), producing a 51-
                    // unit vy overshoot on every slope exit (dorabaebasic6 f=884).
                    const int SLOPE_UPSIDEDOWN = 0b1000;
                    bool slopeUd = (slopeType & SLOPE_UPSIDEDOWN) != 0;
                    int tableIdx = (slopeUd ? 1 : 0) | (mini ? 2 : 0) | 4; // |4 = TBLIDX_NTSC
                    int delta = 0;
                    string tableTag = "NONE";
                    if (gameMode == 2)
                    {
                        int st = slopeType & 0b1111;
                        if (st == 0b0110 || st == 0b1110)      { delta = EXIT_SLOPE_BALL_22[tableIdx]; tableTag = "BALL_22"; }
                        else if (st == 0b0111 || st == 0b1111) { delta = EXIT_SLOPE_BALL_66[tableIdx]; tableTag = "BALL_66"; }
                    }
                    else if (gameMode == 0)
                    {
                        int st = slopeType & 0b1111;
                        if (st == 0b0110 || st == 0b1110)      { delta = EXIT_SLOPE_CUBE_22[tableIdx]; tableTag = "CUBE_22"; }
                    }
                    velY_fixed += delta;
                    posY_fixed += delta; // NES ordering: posY integrates post-delta vy
                    if (slog != null) slog($"[SLOPE/EXIT] swOn:{swOn_in}->0 sT={sT_in} gm={gameMode} mini={mini} gF={gravFlipped} tblIdx={tableIdx} tbl={tableTag} delta={delta} vy:{vy_in}->{velY_fixed} py:{py_in}->{posY_fixed} sT:=0");
                    slopeType = 0;
                }
                else
                {
                    if (slog != null) slog($"[SLOPE/COUNTER_DEC] swOn:{swOn_in}->{slopeWasOnCounter} sT={slopeType} (no exit yet)");
                }
            }
            else
            {
                lastSlopeType = 0;
                slopeType = 0;
                if (slog != null && (sT_in != 0 || lst_in != 0))
                    slog($"[SLOPE/CLEAR] swOn=0 -> lst:{lst_in}->0 sT:{sT_in}->0");
            }
        }

        /// <summary>
        /// Post-eject slope counter (x_movement_coll).
        /// </summary>
        internal static void UpdateSlopeCountersFresh(
            ref int slopeFrames, int slopeType, ref int velY_fixed, int velX_fixed)
        {
            var slog = SlopeDiagLog;
            int sF_in = slopeFrames, vy_in = velY_fixed;
            if (slopeFrames > 0)
            {
                slopeFrames--;
                if (slopeType != 0)
                {
                    ApplySlopeVelocity(ref velY_fixed, slopeType, velX_fixed);
                    if (slog != null) slog($"[SLOPE/FRESH_APPLY] sF:{sF_in}->{slopeFrames} sT={slopeType} vx={velX_fixed} vy:{vy_in}->{velY_fixed}");
                }
                else if (slog != null)
                {
                    slog($"[SLOPE/FRESH_DEC] sF:{sF_in}->{slopeFrames} sT=0 (no apply)");
                }
            }
            else if (slog != null && slopeType != 0)
            {
                slog($"[SLOPE/FRESH_NOP] sF=0 sT={slopeType} (no-op)");
            }
        }

        /// <summary>apply_slope_vel() from x_movement.h.</summary>
        internal static void ApplySlopeVelocity(ref int velY_fixed, int slopeType, int velX_fixed)
        {
            if (slopeType == 0) return;
            int vy_in = velY_fixed;
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
            var slog = SlopeDiagLog;
            if (slog != null) slog($"[SLOPE/VEL] sT={slopeType} deg={degrees} rising={rising} ud={ud} vx={velX_fixed} component={velComponent} vy:{vy_in}->{velY_fixed}");
        }

        /// <summary>slope_jump_check — bonus velocity when jumping off slopes.
        /// NES MAKE_CUBE_JUMP_HIGHER table is indexed by currplayer_table_idx, whose
        /// bit 2 (TBLIDX_NTSC) selects the framerate slot:
        ///   table_idx 0..3 (PAL/50fps): ±0x9A for both normal and mini
        ///   table_idx 4..7 (NTSC/60fps): ±0x80 for both normal and mini
        /// The editor exports/runs NTSC ROMs exclusively, so always use 0x80.
        /// Sign flips with gravity (matches table layout: hi byte alternates 0xFF/0x00).</summary>
        internal static void SlopeJumpCheck(ref int velY_fixed, ref bool slopeJumpHigher, int slopeType, bool mini, bool gravFlipped)
        {
            if (!slopeJumpHigher) return;
            var slog = SlopeDiagLog;
            int vy_in = velY_fixed;
            if ((slopeType & SLOPE_DEGREES_MASK) != SLOPE_22DEG)
            {
                const int boost = 0x80; // NTSC value (table_idx 4..7); editor is always NTSC
                velY_fixed += gravFlipped ? boost : -boost;
                if (slog != null) slog($"[SLOPE/JUMP_BOOST] sT={slopeType} mini={mini} gF={gravFlipped} boost={(gravFlipped?boost:-boost)} vy:{vy_in}->{velY_fixed} jH->false");
            }
            else if (slog != null)
            {
                slog($"[SLOPE/JUMP_NOBOOST] sT={slopeType} (22deg, no boost) jH->false");
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
            public bool DebugCeilSlopeHit;
            public bool DebugCeilTileHit;
            public bool DebugCeilSpike;
            public bool DebugFloorSlopeHit;
            public bool DebugFloorTileHit;
            public bool DebugFloorSpike;
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
            bool slopeJumpHigher, int lastSlopeType,
            int camY_fixed = 0)
        {
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("SP.CubeEject.in", $"X={playerX_fixed>>8} Y={playerY_fixed>>8} vy={velY_fixed} vx={velX_fixed} gF={(gravFlipped?1:0)} mini={(mini?1:0)} mode={gameMode} sWOn={slopeWasOnCounter} sF={slopeFrames} sT={slopeType} lst={lastSlopeType}");
#endif
            var r = new EjectResult {
                NewY_fixed = playerY_fixed, NewVelY_fixed = velY_fixed,
                SlopeType = slopeType, SlopeFrames = slopeFrames,
                SlopeWasOnCounter = slopeWasOnCounter,
                SlopeJumpHigher = slopeJumpHigher, LastSlopeType = lastSlopeType,
            };

            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            // NES bg_coll_D probes at `Generic.y + Generic.height` where
            // Generic.y = high_byte(currplayer_y) and currplayer_y is
            // SCREEN-relative.  PF Y_fixed is in WORLD coords, so derive the
            // NES probe world tile row as scroll_int + screen_int — which is
            // `((Y - cam) >> 8) + (cam >> 8)`.  This naturally subtracts the
            // sub-pixel borrow that NES does NOT see (NES tracks screen_y as a
            // separate uint16 whose .high is unaffected by scroll_y subpx).
            int playerY_px_nes = ((playerY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
            int hbW = GetCubeHitboxW(mini);
            int hbH = GetCubeHitboxH(mini);
            int hbOffY = GetHitboxOffsetY(gameMode, mini, gravFlipped);

            UpdateSlopeCounters(ref r.SlopeWasOnCounter, ref r.SlopeType,
                                ref r.NewVelY_fixed, ref r.NewY_fixed,
                                gameMode, gravFlipped, mini, ref r.LastSlopeType);

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
                    // NES `low_byte(currplayer_y) = 0` zeroes the SCREEN-relative
                    // sub-pixel UNCONDITIONALLY when bg_coll_D fires (including
                    // when eject_D == 0, e.g. player exactly at slope surface).
                    // In PF (world coords) world_subpx = scroll_subpx + screen_subpx,
                    // so post-eject world_subpx = camY_fixed.low.
                    // Previously the Y write was gated on `slopeEject > 0`, which
                    // left Y_fixed.low at its post-gravity value and caused a
                    // sub-pixel borrow in the cube cam-follow ptr1 calculation —
                    // surfaced as scroll_y_subpx desync in cataclysm (sim 271).
                    r.NewY_fixed = (((r.NewY_fixed >> 8) - slopeEject) << 8) | (camY_fixed & 0xFF);
                    r.NewVelY_fixed = 0; r.WasZeroed = true;
                    r.SlopeType = newSlopeType; r.OnGround = true;
                    r.DebugFloorSlopeHit = true;
                    // NES bg_coll_slope col_end CUBE/ROBOT/NINJA branch (0/4/8 only).
                    if (newSlopeType != 0)
                    {
                        if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                            r.SlopeJumpHigher = true;
                        else { r.SlopeFrames = 1; r.SlopeWasOnCounter = 3; }
                    }
                }
                else if (r.NewVelY_fixed >= 0)
                {
                    var (floorHit, floorTopY, spike) = CheckFloor(
                        in map, playerX_px, playerY_px_nes + hbOffY, hbW, hbH);
                    if (spike) { r.Died = true; r.DebugFloorSpike = true; return r; }
                    if (floorHit)
                    {
                        // NES cube_eject (gamemode_cube.h ~L210):
                        //   high_byte(currplayer_y) -= eject_D;
                        //   low_byte(currplayer_y) = 0;       ← SCREEN-rel subpx
                        //   currplayer_vel_y = 0;
                        // World subpx = scroll_subpx + screen_subpx, so set
                        // Y_fixed.low = camY_fixed.low after eject.
                        r.NewY_fixed = ((floorTopY - hbH - hbOffY) << 8) | (camY_fixed & 0xFF);
                        r.NewVelY_fixed = 0; r.WasZeroed = true; r.OnGround = true;
                        r.DebugFloorTileHit = true;
                    }
                }
            }
            else
            {
                // NES bg_coll_U slope probe: `Generic.y + (byte(0x10-Generic.height)>>1) + ...`
                // The vertical-center offset term is UNCONDITIONAL in bg_coll_U
                // (unlike bg_coll_D where it is gated on currplayer_mini).  For
                // non-mini ship/wave/snake (height=7) this adds +4px to the probe Y;
                // for non-mini cube (height=15) it is 0, matching prior behavior.
                int slopeHbOffY = (0x10 - hbH) >> 1;
                int slopeCheckY = playerY_px + slopeHbOffY + (mini ? 1 : 2) + (gameMode == 1 ? 1 : 0);

                var (ceilSlopeHit, ceilSlopeEject, ceilSlopeType) = CheckSlopesUp(
                    in map, playerX_px, playerX_px, slopeCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);

                if (ceilSlopeHit)
                {
                    // NES cube_eject: bg_coll_return_slope_U sets eject_U = -tmp8,
                    // then high_byte(currplayer_y) -= eject_U. This snaps by exactly
                    // tmp8 pixels; the extra -1 belongs to ship ceiling ejection, not cube.
                    r.NewY_fixed = (((r.NewY_fixed >> 8) + ceilSlopeEject) << 8) | (camY_fixed & 0xFF);
                    r.NewVelY_fixed = 0; r.WasZeroed = true;
                    r.OnGround = true; r.SlopeType = ceilSlopeType;
                    // NES bg_coll_slope col_end CUBE/ROBOT/NINJA branch (0/4/8 only).
                    if (ceilSlopeType != 0)
                    {
                        if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                            r.SlopeJumpHigher = true;
                        else { r.SlopeFrames = 1; r.SlopeWasOnCounter = 3; }
                    }
                }
                else if (r.NewVelY_fixed <= 0)
                {
                    var (ceilHit, ceilBotY, ceilSpike, _) = CheckCeiling(
                        in map, playerX_px, playerY_px_nes + hbOffY, hbW, hbH);
                    // NES bg_coll_U calls bg_coll_return_U → bg_coll_spikes which
                    // sets cube_data[currplayer]=1 (death) when a COL_DEATH_TOP/BOTTOM
                    // tile's kill region is hit.  Mirror NES asymmetry-free behavior:
                    // the gravity-down branch already propagates `spike` as Died,
                    // so do the same here.
                    if (ceilSpike) { r.Died = true; r.DebugFloorSpike = true; return r; }
                    if (ceilHit)
                    {
                        // NES cube_eject ceiling branch: low_byte(currplayer_y) = 0
                        // → world subpx = camY_fixed.low.
                        r.NewY_fixed = ((ceilBotY - hbOffY - 1) << 8) | (camY_fixed & 0xFF);
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
            bool slopeJumpHigher, int lastSlopeType,
            int camY_fixed = 0)
        {
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("SP.BallEject.in", $"X={playerX_fixed>>8} Y={playerY_fixed>>8} vy={velY_fixed} vx={velX_fixed} gF={(gravFlipped?1:0)} mini={(mini?1:0)} mode={gameMode} sWOn={slopeWasOnCounter} sF={slopeFrames} sT={slopeType} lst={lastSlopeType}");
#endif
            var r = new EjectResult {
                NewY_fixed = playerY_fixed, NewVelY_fixed = velY_fixed,
                SlopeType = slopeType, SlopeFrames = slopeFrames,
                SlopeWasOnCounter = slopeWasOnCounter,
                SlopeJumpHigher = slopeJumpHigher, LastSlopeType = lastSlopeType,
            };

            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            // NES `bg_coll_U / bg_coll_D` decisions are made in SCREEN-relative            // coordinates: `Generic.y = high_byte(currplayer_y)` where
            // `currplayer_y` is integrated purely from velocity (no scroll
            // adjustment).  When the player's SCREEN sub-pixel underflows
            // during gravity, screen_high decrements and the ceiling probe
            // catches the ceiling tile that frame.  PF's Y_fixed is in WORLD
            // coords; if scroll_y has a non-zero sub-pixel component, the
            // WORLD low does NOT underflow at the same moment SCREEN low does
            // and PF detects the ceiling one frame late.  Replicate the NES
            // SCREEN-high computation:  
            //   screen_y = player_y_world - cam_y_world (with proper byte borrow)
            //   probe_world_y = screen_high + cam_high (= NES Generic.y + scroll_y)
            // (xstep ball A-press divergence rom_f=4045 / sim_f=4033.)
            int playerY_px_nes = ((playerY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
            int hbW = GetCubeHitboxW(mini);
            int hbH = GetCubeHitboxH(mini);
            int miniOffset = GetMiniCenterOffsetY(mini);
            int ballYOffset = gravFlipped ? -1 : 1;
            int collisionY = playerY_px_nes + miniOffset + ballYOffset;

            UpdateSlopeCounters(ref r.SlopeWasOnCounter, ref r.SlopeType,
                                ref r.NewVelY_fixed, ref r.NewY_fixed,
                                gameMode, gravFlipped, mini, ref r.LastSlopeType);

            var dlog = BallEjectDiagLog;
            if (dlog != null)
            {
                int camLow_ = camY_fixed & 0xFF;
                int yLow_ = playerY_fixed & 0xFF;
                int screenY_low_ = (playerY_fixed - camY_fixed) & 0xFF;
                int screenY_high_ = (playerY_fixed - camY_fixed) >> 8;
                int borrow_ = (yLow_ < camLow_) ? 1 : 0;
                dlog($"[BE_ENTRY] Yf=0x{playerY_fixed:X8} Yhi={playerY_fixed >> 8} Ylo=0x{yLow_:X2} camYf=0x{camY_fixed:X8} camHi={camY_fixed >> 8} camLo=0x{camLow_:X2} scrHi={screenY_high_} scrLo=0x{screenY_low_:X2} borrow={borrow_} Ypx_nes={playerY_px_nes} collY={collisionY} velY={velY_fixed} gravF={gravFlipped} mini={mini} gm={gameMode} input={inputHeld} slopeT={r.SlopeType}/{r.SlopeFrames}");
            }

            // ────────────────────────────────────────────────────────────────
            // NOTE on NES ball_eject() structure (gamemode_ball.h L88-130):
            //   The NES source runs BOTH bg_coll_U() AND bg_coll_D() every
            //   frame regardless of gravity, with vel-sign gates inside each.
            //   Each hit also clears orbactive and most cube_data flags as a
            //   side effect.
            //   PF does NOT model those side effects, and a previous attempt
            //   to run both probes unconditionally produced a major regression
            //   (rom_f=3559) — likely because the missing side effects let
            //   stale orb state cascade.  Until those side-effects are wired
            //   up, keep the gravFlipped gate so only the surface the player
            //   is actually moving toward is probed (matches behaviour PF
            //   relied on for parity through f=4044).
            // ────────────────────────────────────────────────────────────────

            // NES `currplayer_y` is SCREEN-relative; ball_eject modifies its
            // HIGH byte only.  PF stores Y_fixed in WORLD coords, and the
            // displayed/probe SCREEN-Y has a sub-byte borrow whenever
            // `Y_fixed.low < camY_fixed.low`.  To match NES we must:
            //   1. Preserve the SCREEN low byte (matches NES "low_byte
            //      (currplayer_y) untouched").
            //   2. Set the SCREEN high byte directly (matches NES eject
            //      adjusting currplayer_y_high).
            // Writing world_high while preserving world_low corrupts SCREEN
            // high by ±1 every time the borrow flips, which surfaced as a
            // chronic 1-px Y offset in inverted-grav rest at xstep
            // (rom_f=3560+) and ultimately a missed spike at rom_f=4089.
            int camHigh = camY_fixed >> 8;
            int screenLow_old = (r.NewY_fixed - camY_fixed) & 0xFF;

            if (gravFlipped)
            {
                bool hadUpTileHit = false;

                // ─────────────────────────────────────────────────────────
                // FULL NES bg_coll_U slope section mirror — runs UNCONDITIONALLY
                // (collision.h L889 `if (high_byte(currplayer_x) >= 0x10)` —
                // no vy gate, no gravity gate).  When wedge passes, NES sets:
                //   - cpsF=1, cpswOn=3, cpsT=<new>
                //   - eject_U = -tmp8  (cpy_high += tmp8 → ball moves DOWN screen)
                //   - cpvy = 0  (set by ball_eject after bg_coll_U returns 1)
                // and returns 1 EARLY, skipping COLL_CHECK_TOP.
                //
                // dorabaebasic6 f=2437..2456: ball is gravFlipped, gliding
                // under an overhead SLOPE_22DEG_UP_UD wedge.  NES applies
                // these ejects each frame the wedge passes, keeping vy=0
                // momentarily so apply_slope_vel (next x_movement_coll) can
                // set vy = +354 cleanly.  PF previously skipped this entirely
                // in the gravFlipped branch (only CheckCeiling for solid tiles)
                // → vy grew unchecked to -1499 → cascading Y divergence.
                // ─────────────────────────────────────────────────────────
                int slopeHbOffY_FU = (0x10 - hbH) >> 1; // unconditional in bg_coll_U
                // NES bg_coll_U probe Y = Generic.y + offset, where ball_movement
                // sets `Generic.y = high_byte(currplayer_y) - 1` for gravFlipped
                // (gamemode_ball.h L26).  Mirror that by using
                // playerY_px_nes + ballYOffset (= playerY_px_nes - 1 when flipped)
                // as the equivalent of NES Generic.y.  Using `playerY_px` here
                // (world high byte) lost the -1 offset and probed 1 row LOWER
                // than NES, missing slope tiles in the ceiling row and causing
                // PF/NES collision cadence to drift out of phase (db6 f=2302
                // false BALL_FLIP).
                int slopeUpProbeY_F = playerY_px_nes + ballYOffset + slopeHbOffY_FU
                                    + (mini ? 1 : 2)
                                    + (gameMode == 1 ? 1 : 0);
                var (slopeUHit, slopeUEject, slopeUType) = CheckSlopesUp(
                    in map, playerX_px, playerX_px, slopeUpProbeY_F, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);

                if (slopeUHit)
                {
                    // NES eject_U = -tmp8; cpy_high -= eject_U → cpy_high += tmp8.
                    // Y moves DOWN (away from overhead slope when gravFlipped).
                    int curSH_SU = (r.NewY_fixed - camY_fixed) >> 8;
                    int newSH_SU = curSH_SU + slopeUEject;
                    r.NewY_fixed = camY_fixed + (newSH_SU << 8) + screenLow_old;
                    r.NewVelY_fixed = 0; r.OnGround = true;
                    r.SlopeType = slopeUType;
                    if (slopeUType != 0)
                    {
                        if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                            r.SlopeJumpHigher = true;
                        else { r.SlopeFrames = 1; r.SlopeWasOnCounter = 3; }
                    }
                    hadUpTileHit = true;
                    if (dlog != null) dlog($"[BE_CEIL_SLOPE] probeY={slopeUpProbeY_F} eject={slopeUEject} sT={slopeUType} scrHi:{curSH_SU}->{newSH_SU} newYf=0x{r.NewY_fixed:X8} ({r.NewY_fixed>>8}px)");
                }

                // NES bg_coll_U tile gate: `if (high_byte(vel_y) & 0x80)` —
                // sign bit set, i.e. STRICTLY < 0.  Using <= 0 wiped the
                // low-byte sub-pixel state at rest, putting PF's eject
                // alternation one frame out of phase with NES (xstep ball
                // A-press divergence f=4045).
                // NES order: bg_coll_U returns EARLY after slope section if
                // it hit (low_byte(tmp3)) → COLL_CHECK_TOP is SKIPPED.
                if (!slopeUHit && r.NewVelY_fixed < 0)
                {
                    var (hit, ceilBotY, ceilSpike, ceilCollision) = CheckCeiling(in map, playerX_px, collisionY, hbW, hbH);
                    if (dlog != null) dlog($"[BE_CEIL] probeX={playerX_px} probeY={collisionY} hbW={hbW} hbH={hbH} -> hit={hit} ceilBotY={ceilBotY} spike={ceilSpike} coll={ceilCollision}");
                    // NES bg_coll_U → bg_coll_U_D_checks: COL_DEATH_TOP at the
                    // ceiling probe invokes col_death_top_routine which sets
                    // cube_data |= 1 (kill) when (localY<6 && localX in [3,9]).
                    // CheckCeiling already evaluates this for the 3 NES probe
                    // X-points and returns it via spikeDeath; the floor branch
                    // honours its analogue via CheckFloor's spike result.
                    // Previously this was discarded, letting grav-flipped balls
                    // pass through ↑-spikes that NES kills on (lightningroad
                    // sim=1310 first ball section, PC=$A4CC).
                    if (ceilSpike) { r.Died = true; return r; }
                    if (hit)
                    {
                        hadUpTileHit = true;
                        // NES tmp8-based eject (NOT geometric snap):
                        //   bg_coll_U computes tmp8 = (probeY world Y) & 0x0f,
                        //   eject_U = 0xf0 | tmp8 = signed -(16-tmp8),
                        //   high(currplayer_y) -= eject_U  →  Y_high += (16-tmp8).
                        // The geometric snap (ceilBotY - miniOffset) puts the
                        // player flush with the tile bottom (fully outside the
                        // tile), but NES leaves the player partially INSIDE
                        // the ceiling tile because the chained bg_coll_D
                        // immediately pulls Y_high back down by tmp8_d.  The
                        // residual overlap is what triggers x_movement_coll
                        // bg_coll_R death on the next step (Acropolis xstep
                        // mini ball regression at sim 3060).
                        int probeY_U_world = collisionY + 1; // CheckCeiling probe Y
                        int tmp8_u = ((probeY_U_world % 16) + 16) % 16;
                        // NES bg_coll_return_U:
                        //   eject_U = (tmp3 ? 0xF0 : 0xF8) | tmp8
                        // where tmp3 is bg_coll_U_D_checks() hit class
                        // (COL_NO_SIDE / COL_ALL / COL_FLOOR_CEIL).
                        bool udChecksPath = ceilCollision == MetatileCollision.COL_NO_SIDE ||
                                            ceilCollision == MetatileCollision.COL_ALL ||
                                            ceilCollision == MetatileCollision.COL_FLOOR_CEIL;
                        int ejectU = (udChecksPath ? 0xF0 : 0xF8) | tmp8_u;
                        int signedEjectU = unchecked((sbyte)(byte)ejectU);
                        int curScreenHigh_U = (r.NewY_fixed - camY_fixed) >> 8;
                        int screenHigh_after_U = curScreenHigh_U - signedEjectU;
                        r.NewY_fixed = camY_fixed + (screenHigh_after_U << 8) + screenLow_old;
                        r.NewVelY_fixed = 0; r.OnGround = true;
                        if (dlog != null) dlog($"[BE_CEIL_EJECT] probeY_U={probeY_U_world} tmp8_u={tmp8_u} udChecks={udChecksPath} ejectU=0x{ejectU:X2} signed={signedEjectU} scrHi:{curScreenHigh_U}->{screenHigh_after_U} newYf=0x{r.NewY_fixed:X8} ({r.NewY_fixed>>8}px)");
                    }
                }

                // NES ball_eject() calls bg_coll_D() immediately after bg_coll_U()
                // in the same frame.  Generic.y is NOT updated by the U eject
                // (only currplayer_y_high is), so bg_coll_D probes at the
                // ORIGINAL Generic.y position — which is the SAME tile the U
                // eject just bumped against, but now from below.  The result
                // is a partial pull-down: net U+D = (16 - tmp8_u - tmp8_d).
                // For mini ball with row-aligned ceiling this nets +4 (NOT
                // the full +13 of the U eject alone), leaving the player
                // partially inside the ceiling tile so x_movement_coll's
                // bg_coll_R can detect the overlap and kill (Acropolis
                // sim 3060: PF was surviving by ejecting fully clear).
                if (hadUpTileHit)
                {
                    // NES bg_coll_D probe Y = Generic.y + height + miniadj.
                    // Generic.y_world == playerY_px_nes + ballYOffset (pre-U).
                    // probe_D_world = playerY_px_nes + ballYOffset + hbH + miniOffset.
                    int probeY_D_world = playerY_px_nes + ballYOffset + hbH + miniOffset;
                    int tileRowD = probeY_D_world >= 0
                        ? probeY_D_world / TILE
                        : (probeY_D_world - TILE + 1) / TILE;
                    int tileArrayY_D = tileRowD + map.GroundRowsToReserve;
                    bool dHit = false;
                    if (tileArrayY_D >= 0 && tileArrayY_D < map.MapHeight)
                    {
                        // Probe at 3 NES X-points (left, mid, right) — same as bg_coll_D.
                        int playerLeft = playerX_px;
                        int playerRight = playerX_px + hbW;
                        for (int cpIdx = 0; cpIdx < 3 && !dHit; cpIdx++)
                        {
                            int probeX = cpIdx == 0 ? playerLeft
                                       : cpIdx == 1 ? playerLeft + (hbW >> 1)
                                                    : playerRight;
                            int tx = probeX / TILE;
                            if (tx < 0 || tx >= map.MapWidth) continue;
                            int tileIdx = tileArrayY_D * map.MapWidth + tx;
                            if (tileIdx < 0 || tileIdx >= map.Tiles.Length) continue;
                            int tileId = map.Tiles[tileIdx];
                            int mappedTid = MapTileForCollision(tileId);
                            var coll = MetatileCollisionTable.GetCollision((byte)mappedTid);
                            if (coll == MetatileCollision.COL_NONE) continue;
                            int localX = ((probeX % TILE) + TILE) % TILE;
                            int localY = ((probeY_D_world % TILE) + TILE) % TILE;
                            if (TileOccupiesPixel(coll, localX, localY))
                            {
                                dHit = true;
                            }
                        }
                    }
                    if (dHit)
                    {
                        int tmp8_d = ((probeY_D_world % 16) + 16) % 16;
                        int ejectD_high = tmp8_d;
                        int curScreenHigh_D = (r.NewY_fixed - camY_fixed) >> 8;
                        int screenHigh_after_D = curScreenHigh_D - ejectD_high;
                        r.NewY_fixed = camY_fixed + (screenHigh_after_D << 8) + screenLow_old;
                        r.NewVelY_fixed = 0; r.OnGround = true;
                        if (dlog != null) dlog($"[BE_CEIL_D] probeY_D={probeY_D_world} tmp8_d={tmp8_d} scrHi:{curScreenHigh_D}->{screenHigh_after_D} newYf=0x{r.NewY_fixed:X8} ({r.NewY_fixed>>8}px)");
                    }
                }
                // ─────────────────────────────────────────────────────────
                // FULL NES bg_coll_D slope section mirror — runs UNCONDITIONALLY
                // after bg_coll_U (collision.h L948).  When wedge passes:
                //   - cpsF=1, cpswOn=3, cpsT=<new>
                //   - eject_D = +tmp8 (cpy_high -= eject_D → ball moves UP screen)
                //   - cpvy = 0
                // ball_eject() runs bg_coll_D AFTER bg_coll_U: Generic.y is
                // unchanged from pre-U (only cpy_high was modified), so the D
                // probe Y in NES uses the SAME pre-U Generic.y.  We mirror that
                // by computing slopeDownProbeY_F off `playerY_px` (pre-mutation).
                // ─────────────────────────────────────────────────────────
                int slopeHbOffY_FD = mini ? ((0x10 - hbH) >> 1) : 0; // gated on mini in bg_coll_D
                // NES bg_coll_D probe Y uses the SAME Generic.y as bg_coll_U
                // (ball_movement.h: Generic.y = high_byte(cpy) - 1 for flipped
                // is reapplied identically before ball_eject re-runs).  Use
                // playerY_px_nes + ballYOffset for parity (see slopeUpProbeY_F
                // comment).
                int slopeDownProbeY_F = playerY_px_nes + ballYOffset + slopeHbOffY_FD + hbH - 2;
                var (slopeDHit, slopeDEject, slopeDType) = CheckSlopesDown(
                    in map, playerX_px, playerX_px, slopeDownProbeY_F, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);
                if (slopeDHit)
                {
                    int curSH_SD = (r.NewY_fixed - camY_fixed) >> 8;
                    int newSH_SD = curSH_SD - slopeDEject;
                    r.NewY_fixed = camY_fixed + (newSH_SD << 8) + screenLow_old;
                    r.NewVelY_fixed = 0; r.OnGround = true;
                    r.SlopeType = slopeDType;
                    if (slopeDType != 0)
                    {
                        if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                            r.SlopeJumpHigher = true;
                        else { r.SlopeFrames = 1; r.SlopeWasOnCounter = 3; }
                    }
                    if (dlog != null) dlog($"[BE_FLOOR_SLOPE_F] probeY={slopeDownProbeY_F} eject={slopeDEject} sT={slopeDType} scrHi:{curSH_SD}->{newSH_SD} newYf=0x{r.NewY_fixed:X8} ({r.NewY_fixed>>8}px)");
                }
                else if (slopeDType != 0 && r.SlopeWasOnCounter > 0)
                {
                    // bg_coll_slope writes currplayer_slope_type before the wedge test.
                    // If the wedge misses while the slope grace counter is active, NES
                    // keeps that state-only slope type even though bg_coll_D returns 0.
                    r.SlopeType = slopeDType;
                }

                if (dlog != null) dlog($"[BE_EXIT_U] outYf=0x{r.NewY_fixed:X8} ({r.NewY_fixed>>8}px) outVelY={r.NewVelY_fixed} onG={r.OnGround} hadUpHit={hadUpTileHit} slopeU={slopeUHit} slopeD={slopeDHit} sF={r.SlopeFrames} sT={r.SlopeType} swOn={r.SlopeWasOnCounter}");
            }
            else
            {
                int slopeHbOffY = mini ? ((0x10 - hbH) >> 1) : 0;
                // NES bg_coll_D slope probe Y (collision.h L951):
                //   add_scroll_y(Generic.y + Generic.height - 2
                //                + (mini ? (0x10 - height) >> 1 : 0), scroll_y)
                // For ball normal-grav: Generic.y = high_byte(cpy) + 1.
                // Therefore probe Y world = playerY_px_nes + ballYOffset(+1)
                //                         + slopeHbOffY + hbH - 2.
                // PF previously omitted ballYOffset → probe sat 1 px above
                // NES, causing wedge formula to read a different localY/tile
                // and OVER-detect 22° slopes by one row (dorabaebasic6
                // f=2490 phantom slope eject before NES death at sim=2493).
                int slopeCheckY = playerY_px_nes + ballYOffset + slopeHbOffY + hbH - 2;

                // ─────────────────────────────────────────────────────────
                // NES bg_coll_U slope section (collision.h L889-908) runs
                // UNCONDITIONALLY every ball_eject frame (no velY gate).
                // When the wedge passes, slope_frames=1 + was_on_slope_counter=3
                // are set as SIDE EFFECTS of bg_coll_slope — these persist
                // even when bg_coll_return_slope_U's asymmetric filter rejects.
                //
                // PF previously skipped this entirely in normal-grav, causing
                // missed apply_slope_vel applications when the ball hovered
                // above an RD45/LD45 wedge with its CEILING probe (probe
                // Y = playerY + 2 for non-mini ball, since the slope is
                // above the ball's bottom but still within the wedge's
                // diagonal extent).
                //
                // Example: dorabaebasic6 f=2207 ball at X=5403, Y after
                // gravity=213. bg_coll_U slope probe-0 X=5403, Y=215,
                // tile (337,13)=COL_SLOPE_RD45.  Wedge: localX=11, tmp7=4,
                // localY=7, tmp4=7 → passes. NES sets slope_frames=1.
                // Filter rejects probe-0 (LEFT + RISING) but slope_frames
                // stays 1. Next x_movement_coll fires apply_slope_vel →
                // velY = -velX = -708.  PF missed this → -424 → +57px
                // death cascade at f=2303-2316.
                // ─────────────────────────────────────────────────────────
                // NES Generic.y = high_byte(cpy) + 1 for normal gravity.
                // Mirror via playerY_px_nes + ballYOffset (= +1 here).
                // bg_coll_U slope probe offset for ball non-mini = 0 + 2 = 2;
                // for ship = 0 + 2 + 1 = 3 (gameMode==1 extra +1).
                // slope-section center-offset `(0x10-hbH)>>1` is UNCONDITIONAL
                // in NES bg_coll_U (no mini gate, unlike bg_coll_D).  Drop the
                // erroneous `mini ?` gate to match NES.
                int slopeUpProbeY = playerY_px_nes + ballYOffset
                                  + ((0x10 - hbH) >> 1)
                                  + (mini ? 1 : 2)
                                  + (gameMode == 1 ? 1 : 0); // GAMEMODE_SHIP=1
                // NES ball_eject runs bg_coll_U/D BEFORE x_movement_coll
                // (gamemode_ball.h order: common_gravity_routine → ball_eject
                // → ... → runthecolls → x_movement_coll).  Therefore the
                // wedge probe uses Generic.x = high_byte(cpx PRE-x-movement)
                // = OLD X.  Do NOT advance by velX_fixed here.
                //
                // (Earlier we tried `(playerX_fixed + velX_fixed) >> 8`
                // hoping to mirror an imagined post-x bg_coll_D — but NES
                // only calls bg_coll_D from ball_eject, which is pre-x.
                // The NEW-X probe caused PF to over-detect 22° slopes at
                // dorabaebasic6 f=2490 (probe X +3 px → different localX
                // in COL_SLOPE_RD22_RIGHT wedge → false PASS), ejecting
                // before NES death at sim=2493.)
                int slopeProbeX_nes = playerX_fixed >> 8;
                ApplySlopeWedgeStateOnly(
                    in map, slopeProbeX_nes, slopeProbeX_nes, slopeUpProbeY, hbW,
                    inputHeld, gameMode, gravFlipped,
                    ref r.SlopeFrames, ref r.SlopeWasOnCounter, ref r.SlopeType,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);

                // ─────────────────────────────────────────────────────────
                // NES bg_coll_U + ball_eject ceiling-slope EJECT (the actual
                // Y push, not just state).  bg_coll_return_slope_U sets
                // eject_U = -tmp8 when the asymmetric filter passes; ball_eject
                // then does `high_byte(cpy) -= eject_U`  → Y_hi += tmp8 (ball
                // is pushed DOWN, flush against the ceiling-slope wedge).
                //
                // dorabaebasic6 f=2517 → NES sim=2518: ball just bounced off
                // a SLOPE_22DEG_UP_UD wedge at f=2516 with gF=1, then gravity
                // flipped to 0.  At sim=2518 NES runs bg_coll_U with no vy
                // gate (slope section is unconditional), wedge still passes
                // (probe Y=174 falls in the wedge), bg_coll_return_slope_U
                // returns 1 with eject_U=-4, ball_eject pushes Y_hi += 4
                // (Y world 171.62 → 175.62) and sets cpvy=0.  Then
                // apply_slope_vel sets cpvy=354.  PF normal-grav branch
                // previously did the state-only side effect (above) but
                // SKIPPED the Y eject entirely, leaving PF Y stuck 4 px
                // above NES from f=2518 onward.  Mirror the gravFlipped
                // branch's CheckSlopesUp + eject path.
                // ─────────────────────────────────────────────────────────
                var (slopeUHit_N, slopeUEject_N, slopeUType_N) = CheckSlopesUp(
                    in map, playerX_px, slopeProbeX_nes, slopeUpProbeY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);
                if (slopeUHit_N)
                {
                    int curSH_SU_N = (r.NewY_fixed - camY_fixed) >> 8;
                    int newSH_SU_N = curSH_SU_N + slopeUEject_N; // Y moves DOWN
                    r.NewY_fixed = camY_fixed + (newSH_SU_N << 8) + screenLow_old;
                    r.NewVelY_fixed = 0; r.OnGround = true;
                    r.SlopeType = slopeUType_N;
                    if (slopeUType_N != 0)
                    {
                        if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
                            r.SlopeJumpHigher = true;
                        else { r.SlopeFrames = 1; r.SlopeWasOnCounter = 3; }
                    }
                    if (dlog != null) dlog($"[BE_CEIL_SLOPE_N] probeY={slopeUpProbeY} eject={slopeUEject_N} sT={slopeUType_N} scrHi:{curSH_SU_N}->{newSH_SU_N} newYf=0x{r.NewY_fixed:X8} ({r.NewY_fixed>>8}px)");
                }

                // ─────────────────────────────────────────────────────────
                // NES bg_coll_D slope section (collision.h) also runs
                // bg_coll_slope UNCONDITIONALLY every ball_eject frame at
                // the DOWN probe row.  The state-only side effects
                // (slope_frames=1 + was_on_slope_counter=3 + slope_type)
                // persist even when bg_coll_return_slope_D's asymmetric
                // filter rejects the eject, so the NEXT x_movement_coll
                // fires apply_slope_vel.
                //
                // Without this, dorabaebasic6 f=2202 (ball gm=4 over an
                // RD/LD wedge at the BOTTOM probe row) misses the state-
                // side propagation: NES sets slope_frames at sim=2202
                // ball_eject → applies slope_vel at sim=2203
                // x_movement_coll → vy=-1416.  PF sees no slope at f=2202
                // → vy=-133 (single-frame phase offset that cascades to
                // downstream slope timings).
                // ─────────────────────────────────────────────────────────
                ApplySlopeWedgeStateOnly(
                    in map, slopeProbeX_nes, slopeProbeX_nes, slopeCheckY, hbW,
                    inputHeld, gameMode, gravFlipped,
                    ref r.SlopeFrames, ref r.SlopeWasOnCounter, ref r.SlopeType,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);

                var (slopeHit, slopeEject, newSlopeType) = CheckSlopesDown(
                    in map, slopeProbeX_nes, slopeProbeX_nes, slopeCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);

                if (slopeHit)
                {
                    if (slopeEject > 0)
                    {
                        // Slope eject is a relative SCREEN-high adjustment.
                        // Preserve SCREEN low byte by routing through screen
                        // coords (same world/screen-borrow risk as ceiling).
                        int curScreenHigh = camHigh + ((r.NewY_fixed - camY_fixed) >> 8);
                        int screenHigh_new = curScreenHigh - slopeEject - camHigh;
                        r.NewY_fixed = camY_fixed + (screenHigh_new << 8) + screenLow_old;
                    }
                    r.NewVelY_fixed = 0;
                    r.SlopeFrames = 1; r.SlopeWasOnCounter = 3;
                    r.SlopeType = newSlopeType; r.OnGround = true;
                }
                else if (r.NewVelY_fixed >= 0)
                {
                    var (hit, surfY, spike) = CheckFloor(in map, playerX_px, collisionY, hbW, hbH);
                    if (dlog != null)
                    {
                        // Diagnostic dump: show actual tile data being read at floor probes
                        int _pb = collisionY + hbH;
                        int _tby = _pb / 16;
                        int _arrY = _tby + map.GroundRowsToReserve;
                        string _tdump = $"pb={_pb} tby={_tby} arrY={_arrY} hbH={hbH} grdRsv={map.GroundRowsToReserve}";
                        if (_arrY >= 0 && _arrY < map.MapHeight)
                        {
                            int _txL = playerX_px / 16;
                            int _txM = (playerX_px + (hbW >> 1)) / 16;
                            int _txR = (playerX_px + hbW) / 16;
                            int _idL = (_txL>=0 && _txL<map.MapWidth) ? map.Tiles[_arrY*map.MapWidth + _txL] : -1;
                            int _idM = (_txM>=0 && _txM<map.MapWidth) ? map.Tiles[_arrY*map.MapWidth + _txM] : -1;
                            int _idR = (_txR>=0 && _txR<map.MapWidth) ? map.Tiles[_arrY*map.MapWidth + _txR] : -1;
                            var _cL = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(_idL));
                            var _cM = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(_idM));
                            var _cR = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(_idR));
                            _tdump += $" L(tx={_txL},id={_idL},{_cL}) M(tx={_txM},id={_idM},{_cM}) R(tx={_txR},id={_idR},{_cR})";
                        }
                        dlog($"[BE_FLOOR] probeX={playerX_px} probeY={collisionY} -> hit={hit} surfY={surfY} spike={spike} | {_tdump}");
                    }
                    if (spike) { r.Died = true; return r; }
                    if (hit)
                    {
                        // Set SCREEN high = surfY - hbH - miniOffset - ballYOffset - camHigh,
                        // preserve SCREEN low byte.
                        int screenHigh_new = surfY - hbH - miniOffset - ballYOffset - camHigh;
                        r.NewY_fixed = camY_fixed + (screenHigh_new << 8) + screenLow_old;
                        r.NewVelY_fixed = 0; r.OnGround = true;
                    }
                }
                if (dlog != null) dlog($"[BE_EXIT_D] outYf=0x{r.NewY_fixed:X8} ({r.NewY_fixed>>8}px) outVelY={r.NewVelY_fixed} onG={r.OnGround} slopeHit={slopeHit} slopeT={r.SlopeType}");
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
            bool slopeJumpHigher, int lastSlopeType,
            int camY_fixed = 0)
        {
#if !DISABLE_DEBUG_LOGGING
            PfTrace.Event("SP.ShipUfoEject.in", $"X={playerX_fixed>>8} Y={playerY_fixed>>8} vy={velY_fixed} vx={velX_fixed} gF={(gravFlipped?1:0)} mini={(mini?1:0)} mode={gameMode} sWOn={slopeWasOnCounter} sF={slopeFrames} sT={slopeType} lst={lastSlopeType}");
#endif
            var r = new EjectResult {
                NewY_fixed = playerY_fixed, NewVelY_fixed = velY_fixed,
                SlopeType = slopeType, SlopeFrames = slopeFrames,
                SlopeWasOnCounter = slopeWasOnCounter,
                SlopeJumpHigher = slopeJumpHigher, LastSlopeType = lastSlopeType,
            };

            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            // NES bg_coll_U/D probe at `Generic.y + ...` where Generic.y is
            // SCREEN-relative high byte.  Replicate NES sub-pixel-borrow.
            int playerY_px_nes = ((playerY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
            int hbW = GetCubeHitboxW(mini);
            int hbH = GetCubeHitboxH(mini);
            int hbOffY = GetHitboxOffsetY(gameMode, mini, gravFlipped);
            int collX = playerX_px;
            int collY = playerY_px_nes + hbOffY;

            UpdateSlopeCounters(ref r.SlopeWasOnCounter, ref r.SlopeType,
                                ref r.NewVelY_fixed, ref r.NewY_fixed,
                                gameMode, gravFlipped, mini, ref r.LastSlopeType);

            // NES bg_coll_D slope probe: `Generic.y + Generic.height - 2 + (currplayer_mini ? (0x10-h)>>1 : 0)`
            //   → the center-offset term IS gated on currplayer_mini.
            // NES bg_coll_U slope probe: `Generic.y + (byte(0x10-Generic.height)>>1) + ...`
            //   → the center-offset term is UNCONDITIONAL.
            // Use separate variables: floor-slope honors mini gate; ceil-slope does not.
            // Critical for non-mini ship/wave/snake where height=7 → +4 ceiling probe Y.
            int floorSlopeHbOffY = mini ? ((0x10 - hbH) >> 1) : 0;
            int ceilSlopeHbOffY  = (0x10 - hbH) >> 1;

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
            bool velMovingUp   = r.NewVelY_fixed < 0;
            bool velMovingDown = r.NewVelY_fixed >= 0;

            if (!gravFlipped)
            {
                // Normal gravity: ceiling first (secondary), floor last (primary landing surface).
                bool hadCeilHit = false;
                // Ceiling slopes (always run, no velocity gate — matches NES)
                int ceilCheckY = playerY_px_nes + ceilSlopeHbOffY + (mini ? 1 : 2) + (gameMode == 1 ? 1 : 0);
                var (ceilSlopeHit, ceilSlopeEject, ceilSlopeType) = CheckSlopesUp(
                    in map, playerX_px, playerX_px, ceilCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);
                if (ceilSlopeHit)
                {
                    hadCeilHit = true;
                    r.DebugCeilSlopeHit = true;
                    // NES preserves low-byte (sub-pixel) of currplayer_y; only the
                    // high byte is modified by the eject.  Compose Y_fixed so that
                    // the NES sprite-Y formula `(Cam>>8)+((Y-Cam)>>8)` yields newPxY
                    // exactly: Y_fixed = Cam + ((newPxY - Cam.high) << 8) + RAWL.
                    // Just `(newPxY<<8) | RAWL` would borrow whenever RAWL<Cam.low,
                    // making display = newPxY-1 (off-by-one ceiling/floor eject).
                    int currNesTop = ((r.NewY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
                    int newPxY = currNesTop + ceilSlopeEject - 1;
                    int newSub = (r.NewY_fixed - camY_fixed) & 0xFF;
                    r.NewY_fixed = camY_fixed + ((newPxY - (camY_fixed >> 8)) << 8) + newSub;
                    r.NewVelY_fixed = 0;
                    r.SlopeFrames = 1; r.SlopeWasOnCounter = 3;
                    r.SlopeType = ceilSlopeType;
                }
                else if (velMovingUp)
                {
                    // Ceiling tile collision — only when moving up
                    var (ceilHit, ceilBotY, ceilSpike, ceilCollision) = CheckCeiling(in map, collX, collY, hbW, hbH);
                    if (ceilSpike) { r.DebugCeilSpike = true; r.Died = true; return r; }
                    if (ceilHit)
                    {
                        r.DebugCeilTileHit = true;
                        hadCeilHit = true;
                        int currNesTop = ((r.NewY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
                        int newPxY;

                        // NES ship/UFO path uses `Y_high = Y_high - eject_U - 1`
                        // where eject_U comes from bg_coll_return_U() and depends on
                        // probeY low nibble (`tmp8 = temp_y & 0x0F`).  Using this
                        // directly avoids the mini-UFO snap/death mismatch at tight
                        // overhangs where pure ceilBot geometry lands 1-2px off.
                        if (gameMode == 1 || gameMode == 3)
                        {
                            int probeY = currNesTop + hbOffY + 1;
                            int tmp8 = ((probeY % TILE) + TILE) % TILE;
                            bool udChecksPath = ceilCollision == MetatileCollision.COL_NO_SIDE ||
                                                ceilCollision == MetatileCollision.COL_ALL ||
                                                ceilCollision == MetatileCollision.COL_FLOOR_CEIL;
                            int ejectU = (udChecksPath ? 0xF0 : 0xF8) | tmp8;
                            int signedEjectU = unchecked((sbyte)(byte)ejectU);
                            newPxY = currNesTop - signedEjectU - 1;
                        }
                        else
                        {
                            // Compose Y_fixed via cam so display formula yields newPxY.
                            newPxY = ceilBotY - hbOffY - 2;
                        }

                        int newSub = (r.NewY_fixed - camY_fixed) & 0xFF;
                        r.NewY_fixed = camY_fixed + ((newPxY - (camY_fixed >> 8)) << 8) + newSub;
                        r.NewVelY_fixed = 0;
                    }
                }

                // Re-derive velocity gates from (potentially zeroed) velocity
                // so bg_coll_D sees the live value just as NES does.
                velMovingUp   = r.NewVelY_fixed < 0;
                velMovingDown = r.NewVelY_fixed >= 0;

                // Floor slopes (always run, no velocity gate — matches NES)
                int floorCheckY = playerY_px_nes + floorSlopeHbOffY + hbH - 2;
                var (floorSlopeHit, floorSlopeEject, floorSlopeType) = CheckSlopesDown(
                    in map, playerX_px, playerX_px, floorCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);
                // NES bg_coll_slope unconditionally writes currplayer_slope_type whenever
                // a probe lands on a slope tile (jump-table case body), regardless of wedge
                // pass/fail. Since NES runs bg_coll_U then bg_coll_D, the LATER (D) write
                // overwrites the earlier (U). PF must propagate the floor probe's slope_type
                // even on wedge miss so the next frame's ApplySlopeVelocity uses the correct
                // UD bit. Without this, the prior ceiling slope_type (e.g. RD45=5) leaks
                // through and flips velY sign (dorabaebasic6 f=3994 ship-mode divergence).
                if (floorSlopeType != 0) r.SlopeType = floorSlopeType;
                if (floorSlopeHit)
                {
                    r.DebugFloorSlopeHit = true;
                    if (floorSlopeEject > 0)
                    {
                        int currNesTop = ((r.NewY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
                        int newPxY = currNesTop - floorSlopeEject;
                        int newSub = (r.NewY_fixed - camY_fixed) & 0xFF;
                        r.NewY_fixed = camY_fixed + ((newPxY - (camY_fixed >> 8)) << 8) + newSub;
                    }
                    r.NewVelY_fixed = 0;
                    r.SlopeFrames = 1; r.SlopeWasOnCounter = 3;
                    r.SlopeType = floorSlopeType;
                }
                else if (velMovingDown)
                {
                    // Floor tile collision — only when moving down
                    // NES ufo_ship_eject calls bg_coll_U then bg_coll_D without
                    // reloading Generic.y. The second probe still uses the
                    // original Generic.y from function entry, not the post-U
                    // snapped Y. Keep collY fixed to that pre-eject value.
                    var (floorHit, floorTopY, spike, ejectD, floorCollision) = CheckFloorDetailed(in map, collX, collY, hbW, hbH);
                    if (spike) { r.DebugFloorSpike = true; r.Died = true; return r; }
                    if (floorHit)
                    {
                        r.DebugFloorTileHit = true;
                        int newPxY;
                        if (hadCeilHit)
                        {
                            // NES ufo_ship_eject always runs bg_coll_D after bg_coll_U.
                            // When the U pass zeroes velY, D still evaluates and applies
                            // high_byte(currplayer_y) -= eject_D on any D collision type
                            // returned by bg_coll_return_D (including COL_ALL), not just
                            // mini-block geometries. Using geometric floorTop snap here can
                            // miss the exact NES post-U+D Y and drift side-collision death.
                            int currNesTop = ((r.NewY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
                            newPxY = currNesTop - ejectD;
                        }
                        else
                        {
                            // Compose Y_fixed via cam so display formula yields newPxY.
                            newPxY = floorTopY - hbH - hbOffY;
                        }
                        int newSub = (r.NewY_fixed - camY_fixed) & 0xFF;
                        r.NewY_fixed = camY_fixed + ((newPxY - (camY_fixed >> 8)) << 8) + newSub;
                        r.NewVelY_fixed = 0;
                    }
                }
            }
            else
            {
                // NES ufo_ship_eject is gravity-agnostic: bg_coll_U then bg_coll_D.
                bool hadCeilHit = false;

                // Ceiling slopes (always run, no velocity gate — matches NES)
                int ceilCheckY = playerY_px_nes + ceilSlopeHbOffY + (mini ? 1 : 2) + (gameMode == 1 ? 1 : 0);
                var (ceilSlopeHit, ceilSlopeEject, ceilSlopeType) = CheckSlopesUp(
                    in map, playerX_px, playerX_px, ceilCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);
                if (ceilSlopeType != 0) r.SlopeType = ceilSlopeType;
                if (ceilSlopeHit)
                {
                    hadCeilHit = true;
                    r.DebugCeilSlopeHit = true;
                    int currNesTop = ((r.NewY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
                    int newPxY = currNesTop + ceilSlopeEject - 1;
                    int newSub = (r.NewY_fixed - camY_fixed) & 0xFF;
                    r.NewY_fixed = camY_fixed + ((newPxY - (camY_fixed >> 8)) << 8) + newSub;
                    r.NewVelY_fixed = 0;
                    r.SlopeFrames = 1; r.SlopeWasOnCounter = 3;
                    r.SlopeType = ceilSlopeType;
                }
                else if (velMovingUp)
                {
                    // Ceiling tile collision — only when moving up (primary for flipped)
                    // Match NES Generic.y behavior (see normal-gravity branch):
                    // second probe in ufo_ship_eject uses pre-eject Generic.y.
                    var (ceilHit, ceilBotY, ceilSpike, ceilCollision) = CheckCeiling(in map, collX, collY, hbW, hbH);
                    if (ceilSpike) { r.DebugCeilSpike = true; r.Died = true; return r; }
                    if (ceilHit)
                    {
                        hadCeilHit = true;
                        r.DebugCeilTileHit = true;
                        int currNesTop = ((r.NewY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
                        int newPxY;
                        if (gameMode == 1 || gameMode == 3)
                        {
                            int probeY = currNesTop + hbOffY + 1;
                            int tmp8 = ((probeY % TILE) + TILE) % TILE;
                            bool udChecksPath = ceilCollision == MetatileCollision.COL_NO_SIDE ||
                                                ceilCollision == MetatileCollision.COL_ALL ||
                                                ceilCollision == MetatileCollision.COL_FLOOR_CEIL;
                            int ejectU = (udChecksPath ? 0xF0 : 0xF8) | tmp8;
                            int signedEjectU = unchecked((sbyte)(byte)ejectU);
                            newPxY = currNesTop - signedEjectU - 1;
                        }
                        else
                        {
                            newPxY = ceilBotY - hbOffY - 2;
                        }
                        int newSub = (r.NewY_fixed - camY_fixed) & 0xFF;
                        r.NewY_fixed = camY_fixed + ((newPxY - (camY_fixed >> 8)) << 8) + newSub;
                        r.NewVelY_fixed = 0;
                    }
                }

                velMovingUp   = r.NewVelY_fixed < 0;
                velMovingDown = r.NewVelY_fixed >= 0;

                // Floor slopes (always run, no velocity gate — matches NES)
                int floorCheckY = playerY_px_nes + floorSlopeHbOffY + hbH - 2;
                var (floorSlopeHit, floorSlopeEject, floorSlopeType) = CheckSlopesDown(
                    in map, playerX_px, playerX_px, floorCheckY, hbW,
                    inputHeld, gameMode, gravFlipped, velX_fixed,
                    ref r.LastSlopeType, ref r.SlopeJumpHigher);
                if (floorSlopeType != 0) r.SlopeType = floorSlopeType;
                if (floorSlopeHit)
                {
                    r.DebugFloorSlopeHit = true;
                    if (floorSlopeEject > 0)
                    {
                        int currNesTop = ((r.NewY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
                        int newPxY = currNesTop - floorSlopeEject;
                        int newSub = (r.NewY_fixed - camY_fixed) & 0xFF;
                        r.NewY_fixed = camY_fixed + ((newPxY - (camY_fixed >> 8)) << 8) + newSub;
                    }
                    r.NewVelY_fixed = 0;
                    r.SlopeFrames = 1; r.SlopeWasOnCounter = 3;
                    r.SlopeType = floorSlopeType;
                }
                else if (velMovingDown)
                {
                    var (floorHit, floorTopY, spike, ejectD, floorCollision) = CheckFloorDetailed(in map, collX, collY, hbW, hbH);
                    if (spike) { r.DebugFloorSpike = true; r.Died = true; return r; }
                    if (floorHit)
                    {
                        r.DebugFloorTileHit = true;
                        int newPxY;
                        if (hadCeilHit)
                        {
                            int currNesTop = ((r.NewY_fixed - camY_fixed) >> 8) + (camY_fixed >> 8);
                            newPxY = currNesTop - ejectD;
                        }
                        else
                        {
                            newPxY = floorTopY - hbH - hbOffY;
                        }
                        int newSub = (r.NewY_fixed - camY_fixed) & 0xFF;
                        r.NewY_fixed = camY_fixed + ((newPxY - (camY_fixed >> 8)) << 8) + newSub;
                        r.NewVelY_fixed = 0;
                    }
                }
            }

            return r;
        }
    }
}

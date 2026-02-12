using System;
using System.Collections.Generic;
using System.Linq;

namespace FamidashEditor
{
    /// <summary>
    /// Standalone offline pathfinder engine that pre-calculates the optimal input sequence
    /// for a level using greedy lookahead. Runs in the editor (no real-time pressure).
    /// 
    /// Physics ordering matches the REAL simulator (SimulateNumericStep + ProcessCubePhysics_Fresh):
    ///   1. Advance X
    ///   2. Ground support verification (after X advance)
    ///   3. Sprite interaction checks (portals, pads, end trigger)
    ///   4. Physics dispatch (cube mode):
    ///      a. CommonGravityRoutine (gravity + Y integration; always applies)
    ///      b. CheckCenterPointDeath (single center pixel)
    ///      c. CubeEject (floor/ceiling collision + snap)
    ///      d. Jump input (ONLY when velY == 0 after ejection)
    ///   5. Forward collision (right-edge death for cube/robot/ninja)
    ///   6. CheckDeathCollision (5-point hitbox corners + right-center)
    /// </summary>
    public class PathfinderEngine
    {
        // ── Constants ──────────────────────────────────────────────────────
        private const int TILE = 16;
        private const int LOOKAHEAD_HORIZON = 90;
        private const int CORRIDOR_LOOK_AHEAD_TILES = 10; // scan ahead for obstacles (160px ≈ 58 frames at 1x)
        private const int MAX_FRAMES = 60 * 60 * 5; // 5 minutes at 60fps

        /// <summary>
        /// Jump timing bias: 0.0 = earliest viable jump, 0.5 = middle (default), 1.0 = latest viable jump.
        /// </summary>
        public double JumpTimingBias { get; set; } = 0.5;

        // Cube hitbox (from collision.h)
        private const int CUBE_HITBOX_W = 15;
        private const int CUBE_HITBOX_H = 15;
        private const int MINI_CUBE_HITBOX_W = 8;
        private const int MINI_CUBE_HITBOX_H = 7;

        // ── Speed lookup ───────────────────────────────────────────────────
        private static int SpeedUiIndexToFixed(int uiIndex)
        {
            return uiIndex switch
            {
                0 => 0x23B, // 0.5x
                1 => 0x2C4, // 1x
                2 => 0x371, // 2x
                3 => 0x429, // 3x
                4 => 0x51E, // 4x
                _ => 0x2C4
            };
        }

        private static int SpriteIdToSpeedFixed(int sid)
        {
            return sid switch
            {
                0x6D => 0x16E,
                0x14 => 0x23B,
                0x15 => 0x2C4,
                0x16 => 0x371,
                0x20 => 0x429,
                0x21 => 0x51E,
                _ => -1
            };
        }

        private static int SpriteIdToGameMode(int sid)
        {
            return sid switch
            {
                0x00 => 0, 0x01 => 1, 0x02 => 2, 0x03 => 3,
                0x04 => 4, 0x17 => 5, 0x24 => 6, 0x4B => 7,
                0x58 => 8, 0x6A => 9, 0x6B => 10, 0x6C => 11,
                _ => -1
            };
        }

        private static int GetGravity(bool mini) => mini ? 0x6F : 0x6B;
        private static int GetJumpVel(bool mini) => mini ? -0x4D0 : -0x590;
        private static int GetHitboxW(bool mini) => mini ? MINI_CUBE_HITBOX_W : CUBE_HITBOX_W;
        private static int GetHitboxH(bool mini) => mini ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;
        private static int GetHitboxOffsetY(bool mini, bool gravFlipped) =>
            (mini && !gravFlipped) ? 9 : 0;

        // Ship physics constants (from GameModePhysics.cs, 60fps values)
        private static int ShipGravityBase(bool mini) => mini ? 0x31 : 0x2A;
        private static int ShipGravityAfterHold(bool mini) => mini ? 0x3B : 0x32;
        private static int ShipGravityHoldFall(bool mini) => mini ? 0x3E : 0x34;
        private static int ShipGravity(bool mini) => 0x22; // SHIP_GRAVITY at 60fps (same for mini and normal)
        private static int ShipMaxFallSpeed(bool mini) => 0x02D7;   // SHIP_MAX_FALLSPEED at 60fps
        private static int ShipMaxFallSpeedHold(bool mini) => 0x038D; // SHIP_MAX_FALLSPEED_HOLD at 60fps

        // ── Sprite classification ──────────────────────────────────────────
        private static bool IsSpeedPortal(int sid) => SpriteIdToSpeedFixed(sid) >= 0;
        private static bool IsGameModePortal(int sid) => SpriteIdToGameMode(sid) >= 0;
        private static bool IsGravityPortal(int sid) =>
            sid == 0x08 || sid == 0x09 || sid == 0x10 || sid == 0x11 ||
            sid == 0x12 || sid == 0x13 || sid == 0xFB || sid == 0xFC;
        private static bool IsReverseGravity(int sid) =>
            sid == 0x09 || sid == 0x12 || sid == 0x13 || sid == 0xFB;
        private static bool IsMiniGrowthPortal(int sid) => sid == 0x18 || sid == 0x19;
        private static bool IsEndLevel(int sid) => sid == 0x0F;
        private static bool IsYellowPad(int sid) => sid == 0x0A || sid == 0x0C;
        private static bool IsPinkPad(int sid) => sid == 0x25 || sid == 0x26;
        private static bool IsRedPad(int sid) => sid == 0x52 || sid == 0x53;
        private static bool IsBluePad(int sid) => sid == 0x0D || sid == 0x0E || sid == 0xFD || sid == 0xFE;
        private static bool IsGreenPad(int sid) => sid == 0x65;

        // ── Input data (immutable after construction) ──────────────────────
        private readonly int[] tiles;       // SANITIZED: negatives → 0, matching simulator
        private readonly int[] sprites;
        private readonly Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors;
        private readonly int mapWidth, mapHeight;
        private readonly int groundRowsToReserve;
        private readonly int maxFallSpeed;

        // Pre-sorted sprite list for efficient processing
        private readonly List<SpriteEntry> allSprites;

        private struct SpriteEntry
        {
            public int Index;
            public int SpriteId;
            public int AnchorX_px;
            public int AnchorY_px;
            // Pre-computed world-space hitbox (matches simulator's SpriteIntersectsPlayer)
            public int HitLeft;
            public int HitTop;
            public int HitRight;   // exclusive (NES-style)
            public int HitBottom;  // exclusive (NES-style)
        }

        // ── Sprite geometry tables (mirrored from SimulatorWindow) ─────────
        private static readonly int[] sprite_widths = {
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 00-07
            0x0e,0x0e,0x0F,0x10,0x0F,0x0F,0x0F,0x10, // 08-0F
            0x28,0x28,0x28,0x28,0x10,0x10,0x10,0x10, // 10-17
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 18-1F
            0x10,0x10,0x10,0x10,0x10,0x0F,0x0F,0x10, // 20-27
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 28-2F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 30-37
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 38-3F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x0e, // 40-47
            0x0e,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 48-4F
            0x10,0x10,0x0F,0x0F,0x10,0x10,0x0F,0x0F, // 50-57
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 58-5F
            0x10,0x10,0x10,0x10,0x10,0x0E,0x30,0x30, // 60-67
            0x30,0x30,0x10,0x10,0x10,0x10,0x08,0x10, // 68-6F
            0x10,0x10,0x10,0x10,0x10,0x30,0x30,0x30, // 70-77
            0x30,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 78-7F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 80-87
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 88-8F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 90-97
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 98-9F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // A0-A7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // A8-AF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // B0-B7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // B8-BF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // C0-C7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // C8-CF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // D0-D7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // D8-DF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // E0-E7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // E8-EF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // F0-F7
            0x10,0x08,0x1B,0x10,0x10,0x0E,0x0E,0x10  // F8-FF
        };
        private static readonly int[] sprite_heights = {
            0x34,0x34,0x34,0x34,0x34,0x12,0x12,0x10, // 00-07
            0x28,0x28,0x03,0x12,0x03,0x03,0x03,0x10, // 08-0F
            0x0e,0x0e,0x0e,0x0e,0x24,0x24,0x24,0x34, // 10-17
            0x34,0x34,0x10,0x10,0x10,0x10,0x10,0x12, // 18-1F
            0x24,0x24,0x34,0x34,0x34,0x03,0x03,0x12, // 20-27
            0x12,0x12,0x10,0x10,0x10,0x10,0x10,0x10, // 28-2F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 30-37
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 38-3F
            0x10,0x10,0x10,0x10,0x12,0x12,0x12,0x28, // 40-47
            0x28,0x10,0x10,0x34,0x12,0x12,0x30,0x10, // 48-4F
            0x12,0x12,0x03,0x03,0x12,0x12,0x03,0x03, // 50-57
            0x34,0x10,0x10,0x12,0x12,0x12,0x12,0x34, // 58-5F
            0x34,0x34,0x34,0x34,0x34,0x02,0x10,0x10, // 60-67
            0x10,0x10,0x34,0x34,0x34,0x20,0x08,0x10, // 68-6F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 70-77
            0x10,0x12,0x12,0x12,0x12,0x10,0x10,0x10, // 78-7F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 80-87
            0x10,0x10,0x10,0x10,0x10,0x00,0x10,0x10, // 88-8F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 90-97
            0x10,0x10,0x10,0x10,0x10,0x00,0x10,0x10, // 98-9F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // A0-A7
            0x10,0x10,0x10,0x10,0x10,0x00,0x10,0x10, // A8-AF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // B0-B7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // B8-BF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // C0-C7
            0x10,0x10,0x10,0x10,0x10,0x00,0x00,0x10, // C8-CF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // D0-D7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // D8-DF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // E0-E7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // E8-EF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // F0-F7
            0x10,0x10,0x1F,0x10,0x10,0x03,0x03,0x00  // F8-FF
        };
        private static readonly int[] sprite_x_offset = {
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 00-07
            0x01,0x01,0x00,0x00,0x00,0x00,0x00,0x00, // 08-0F
            0x04,0x04,0x04,0x04,0x00,0x00,0x00,0x00, // 10-17
            0x08,0x08,0x00,0x00,0x00,0x00,0x00,0x00, // 18-1F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 20-27
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 28-2F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 30-37
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 38-3F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x01, // 40-47
            0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 48-4F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 50-57
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 58-5F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 60-67
            0x00,0x00,0x00,0x00,0x00,0x00,0x04,0x00, // 68-6F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 70-77
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 78-7F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 80-87
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 88-8F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 90-97
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 98-9F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A0-A7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A8-AF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B0-B7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B8-BF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C0-C7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C8-CF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D0-D7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D8-DF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E0-E7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E8-EF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // F0-F7
            0x00,0x08,-0x07,0x00,0x00,0x00,0x00,0x00  // F8-FF
        };
        private static readonly int[] sprite_y_offset = {
            -0x02,-0x02,-0x02,-0x02,-0x02,-0x01,-0x01,0x00, // 00-07
            0x04,0x04,0x05,-0x01,0x00,0x05,0x00,0x00, // 08-0F
            0x01,0x01,0x01,0x01,-0x02,-0x02,-0x02,-0x02, // 10-17
            -0x02,-0x02,0x00,0x00,0x00,0x00,0x00,-0x01, // 18-1F
            -0x02,-0x02,-0x02,-0x02,-0x02,0x05,0x00,-0x01, // 20-27
            -0x01,-0x01,0x00,0x00,0x00,0x00,0x00,0x00, // 28-2F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 30-37
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 38-3F
            0x00,0x00,0x00,0x00,-0x01,-0x01,-0x01,0x04, // 40-47
            0x04,0x00,0x00,-0x02,0x00,-0x01,-0x01,0x00, // 48-4F
            -0x01,-0x01,0x05,0x00,-0x01,-0x01,0x0D,0x00, // 50-57
            -0x02,0x00,0x00,-0x01,-0x01,-0x01,-0x01,-0x02, // 58-5F
            -0x02,-0x02,-0x02,-0x02,-0x02,0x00,0x00,0x00, // 60-67
            0x00,0x00,-0x02,-0x02,-0x02,0x00,0x04,0x00, // 68-6F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 70-77
            0x00,-0x01,-0x01,0x00,0x00,0x00,0x00,0x00, // 78-7F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 80-87
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 88-8F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 90-97
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 98-9F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A0-A7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A8-AF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B0-B7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B8-BF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C0-C7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C8-CF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D0-D7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D8-DF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E0-E7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E8-EF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // F0-F7
            0x00,0x00,-0x07,0x00,0x00,0x05,0x02,0x00  // F8-FF
        };

        // ── Simulation state ───────────────────────────────────────────────
        private struct SimState
        {
            public int X_fixed;
            public int Y_fixed;
            public int VelY_fixed;
            public int VelX_fixed;
            public int GameMode;
            public bool GravFlipped;
            public bool Mini;
            public int GravMul;                 // +1 or -1
            public bool WasZeroedByCollision;   // set by eject when landing
            public bool OnGround;               // separate from wasZeroed (cleared by jump)
            public HashSet<int> ProcessedSprites;

            public SimState Clone()
            {
                var c = this;
                c.ProcessedSprites = new HashSet<int>(ProcessedSprites);
                return c;
            }
        }

        // ── Output ─────────────────────────────────────────────────────────
        public List<(int x, int y)> PathPoints { get; private set; }
        public List<bool> Inputs { get; private set; }
        public bool Success { get; private set; }
        public string ResultMessage { get; private set; } = "";

        // ── Constructor ────────────────────────────────────────────────────
        public PathfinderEngine(
            int[] tiles,
            int[] sprites,
            Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors,
            int mapWidth, int mapHeight,
            bool hasGroundLayer, int groundTileRows,
            int maxFallSpeed = 0x06)
        {
            // CRITICAL: Sanitize tiles the same way SimulatorWindow does.
            // Negative tile IDs (sentinel -1 meaning "empty") must become 0.
            this.tiles = (tiles ?? Array.Empty<int>()).Select(t => t < 0 ? 0 : t).ToArray();
            this.sprites = sprites ?? Array.Empty<int>();
            this.spriteAnchors = spriteAnchors ?? new Dictionary<int, (int, int)>();
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;

            // Convert maxFallSpeed the same way the simulator does:
            // small codes (< 0x100) are shifted left 8; values >= 0x100 are used directly.
            if (maxFallSpeed >= 0x100)
                this.maxFallSpeed = maxFallSpeed;
            else
                this.maxFallSpeed = maxFallSpeed << 8;

            // Build sorted sprite list
            allSprites = new List<SpriteEntry>();
            for (int idx = 0; idx < this.sprites.Length; idx++)
            {
                int sid = this.sprites[idx];
                if (sid == -1) continue;

                int atx, aty;
                if (this.spriteAnchors.TryGetValue(idx, out var anchor))
                {
                    atx = anchor.anchorTileX;
                    aty = anchor.anchorTileY;
                }
                else
                {
                    atx = idx % mapWidth;
                    aty = idx / mapWidth;
                }

                // Look up per-sprite hitbox geometry (matching simulator's tables)
                int sid8 = sid & 0xFF;
                int hw = (sid8 >= 0 && sid8 < sprite_widths.Length) ? sprite_widths[sid8] : TILE;
                int hh = (sid8 >= 0 && sid8 < sprite_heights.Length) ? sprite_heights[sid8] : TILE;
                int hxoff = (sid8 >= 0 && sid8 < sprite_x_offset.Length) ? sprite_x_offset[sid8] : 0;
                int hyoff = (sid8 >= 0 && sid8 < sprite_y_offset.Length) ? sprite_y_offset[sid8] : 0;

                // World-space hitbox: same formula as simulator's SpriteIntersectsPlayer
                int hitLeft = atx * TILE + hxoff;
                int hitTop = (aty - groundRowsToReserve) * TILE + hyoff;
                int hitRight = hitLeft + Math.Max(1, hw);    // exclusive (NES-style)
                int hitBottom = hitTop + Math.Max(1, hh);    // exclusive (NES-style)

                allSprites.Add(new SpriteEntry
                {
                    Index = idx,
                    SpriteId = sid,
                    AnchorX_px = atx * TILE + TILE / 2,
                    AnchorY_px = (aty - groundRowsToReserve) * TILE + TILE / 2,
                    HitLeft = hitLeft,
                    HitTop = hitTop,
                    HitRight = hitRight,
                    HitBottom = hitBottom
                });
            }

            allSprites.Sort((a, b) => a.AnchorX_px.CompareTo(b.AnchorX_px));

            PathPoints = new List<(int, int)>();
            Inputs = new List<bool>();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ═══════════════════════════════════════════════════════════════════

        public void Run(int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode,
                        bool startGravFlipped, bool startMini)
        {
            var state = new SimState
            {
                X_fixed = startX_px << 8,
                Y_fixed = startY_px << 8,
                VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex),
                VelY_fixed = 0,
                GameMode = startGameMode,
                GravFlipped = startGravFlipped,
                Mini = startMini,
                GravMul = startGravFlipped ? -1 : 1,
                WasZeroedByCollision = true,
                OnGround = true,
                ProcessedSprites = new HashSet<int>()
            };

            ApplyPortalsUpTo(ref state, startX_px);

            PathPoints.Clear();
            Inputs.Clear();

            for (int frame = 0; frame < MAX_FRAMES; frame++)
            {
                // Record path at hitbox center (matching simulator's recordedPlayerPath)
                int hbW = GetHitboxW(state.Mini);
                int hbH = GetHitboxH(state.Mini);
                int hbOffY = GetHitboxOffsetY(state.Mini, state.GravFlipped);
                PathPoints.Add(((state.X_fixed >> 8) + hbW / 2,
                                (state.Y_fixed >> 8) + hbH / 2 + hbOffY));

                bool input = DecideInput(state);
                Inputs.Add(input);

                bool alive = StepFrame(ref state, input, out bool endLevel);
                if (!alive)
                {
                    Success = false;
                    ResultMessage = $"Died at frame {frame}, X={state.X_fixed >> 8}px";
                    return;
                }
                if (endLevel)
                {
                    hbW = GetHitboxW(state.Mini);
                    hbH = GetHitboxH(state.Mini);
                    hbOffY = GetHitboxOffsetY(state.Mini, state.GravFlipped);
                    PathPoints.Add(((state.X_fixed >> 8) + hbW / 2,
                                    (state.Y_fixed >> 8) + hbH / 2 + hbOffY));
                    Success = true;
                    ResultMessage = $"Completed in {frame} frames ({PathPoints.Count} path points)";
                    return;
                }
            }

            Success = false;
            ResultMessage = $"Timeout after {MAX_FRAMES} frames";
        }

        // ═══════════════════════════════════════════════════════════════════
        //  DECISION LOGIC
        // ═══════════════════════════════════════════════════════════════════

        private bool DecideInput(SimState state)
        {
            if (state.GameMode == 1) return DecideShipInput(state);
            if (state.GameMode != 0) return false; // only cube/ship for now

            // Can only jump when velY == 0 (matching real game's jump check)
            if (state.VelY_fixed != 0) return false;

            // How long does the cube survive without ever jumping?
            int noPressFrames = SimulateForwardWithJumpAt(state, -1);

            // ── Bias-controlled danger detection horizon ──
            // Instead of using a countdown (which commits to a timing that may
            // not account for subsequent obstacles), we control WHEN danger
            // is detected. Earlier biases detect sooner → jump sooner.
            // Later biases detect later → jump later.
            // The natural re-evaluation each frame converges the jump to the
            // optimal immediate timing for each detection window.
            //
            // Earliest (0.0): detects danger when noPressFrames < 90 (full horizon)
            // Latest  (1.0): detects danger when noPressFrames < 20 (tight window)
            const int MIN_DETECTION = 20;
            int detectionHorizon = LOOKAHEAD_HORIZON - (int)((LOOKAHEAD_HORIZON - MIN_DETECTION) * JumpTimingBias);
            if (noPressFrames >= detectionHorizon) return false; // not in danger yet for this bias

            // Test different jump timings: jump at delay 0 (now), 1, 2, ...
            // Collect all viable delays (those that survive longer than no-jump).
            int bestSurvival = noPressFrames;
            var viableDelays = new List<(int delay, int survival)>();

            int maxDelay = Math.Min(noPressFrames, 35); // don't test beyond death or full arc
            for (int delay = 0; delay < maxDelay; delay++)
            {
                int survival = SimulateForwardWithJumpAt(state, delay);
                if (survival > bestSurvival)
                    bestSurvival = survival;
                if (survival > noPressFrames)
                    viableDelays.Add((delay, survival));
            }

            if (viableDelays.Count == 0) return false; // no jump helps

            // Filter to only delays that achieve best (or near-best) survival
            int threshold = bestSurvival - 2; // allow 2 frames tolerance
            var bestDelays = viableDelays.Where(d => d.survival >= threshold).ToList();
            if (bestDelays.Count == 0) bestDelays = viableDelays;

            // Always jump at the earliest viable delay.
            // The timing difference comes from WHEN we start evaluating
            // (controlled by detectionHorizon above), not from which delay we pick.
            return bestDelays[0].delay == 0;
        }

        /// <summary>
        /// Ship decision: track the corridor center using simple proportional control.
        /// Hold (thrust up in normal gravity) if below the target, release if above.
        /// Uses 1-frame safety check: if the chosen action causes death, flip.
        /// This replaces the previous greedy lookahead which had subtle bugs causing
        /// the ship to stay at ground level and never climb.
        /// </summary>
        private bool DecideShipInput(SimState state)
        {
            // Detect corridor center at the ship's current position
            // (scans ahead CORRIDOR_LOOK_AHEAD_TILES for obstacles)
            int targetY = FindCorridorCenter(ref state);
            int currentY = state.Y_fixed >> 8;

            // Simple proportional control: fly toward corridor center
            // Normal gravity (GravMul>0): hold thrusts UP (negative Y)
            // Reversed gravity (GravMul<0): hold thrusts DOWN (positive Y)
            bool shouldHold = (state.GravMul > 0) ? (currentY > targetY) : (currentY < targetY);

            // Test 1-frame survival for both options
            var sH = state.Clone();
            var sR = state.Clone();
            bool aliveH = StepFrame(ref sH, true, out bool endH);
            bool aliveR = StepFrame(ref sR, false, out bool endR);

            // If one path reaches end-of-level, take it
            if (endH) return true;
            if (endR) return false;

            // If only one survives, pick it regardless of corridor target
            if (aliveH && !aliveR) return true;
            if (!aliveH && aliveR) return false;
            if (!aliveH && !aliveR) return false; // both die, doesn't matter

            // Both survive: if our chosen action (corridor tracking) survives, use it
            // Otherwise flip
            if (shouldHold && !aliveH) return false;
            if (!shouldHold && !aliveR) return true;
            return shouldHold;
        }

        /// <summary>
        /// Scan vertically from the ship's center to find the nearest solid ceiling above
        /// and nearest solid floor below. Also scans AHEAD horizontally (CORRIDOR_LOOK_AHEAD_TILES)
        /// to detect upcoming obstacles and proactively adjust the corridor target.
        /// Uses the tightest ceiling/floor constraint across all scanned columns, so the ship
        /// steers early to clear walls, blocks, and narrow passages ahead.
        /// </summary>
        private int FindCorridorCenter(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int centerX_px = playerX_px + hbW / 2;
            int centerY_tile = (playerY_px + hbH / 2) / TILE;

            int worldBottom = (mapHeight - groundRowsToReserve) * TILE;

            // Start with full world bounds
            int ceilingY = 0;       // default: top of world
            int floorY = worldBottom; // default: ground layer

            // Compute all tile rows that the ship's hitbox currently spans
            int topTile = playerY_px / TILE;
            int botTile = (playerY_px + hbH - 1) / TILE;

            // Scan vertically at current X AND ahead (CORRIDOR_LOOK_AHEAD_TILES columns)
            int startTileX = centerX_px / TILE;
            int endTileX = Math.Min(startTileX + CORRIDOR_LOOK_AHEAD_TILES, mapWidth - 1);

            for (int tx = startTileX; tx <= endTileX; tx++)
            {
                // Scan upward for ceiling at this column (from above the hitbox top)
                for (int ty = topTile - 1; ty >= 0; ty--)
                {
                    var col = GetTileCollision(tx, ty);
                    if (col != MetatileCollision.COL_NONE)
                    {
                        var (cL, cT, cR, cB) = GetCollisionBounds(col);
                        if (cR > cL)
                        {
                            int thisCeiling = ty * TILE + cB;
                            if (thisCeiling > ceilingY) ceilingY = thisCeiling;
                            break;
                        }
                    }
                }

                // Scan downward for floor at this column (from below the hitbox bottom)
                for (int ty = botTile + 1; ty < mapHeight - groundRowsToReserve; ty++)
                {
                    var col = GetTileCollision(tx, ty);
                    if (col != MetatileCollision.COL_NONE)
                    {
                        var (cL, cT, cR, cB) = GetCollisionBounds(col);
                        if (cR > cL)
                        {
                            int thisFloor = ty * TILE + cT;
                            if (thisFloor < floorY) floorY = thisFloor;
                            break;
                        }
                    }
                }

                // For forward columns: check ALL tile rows the hitbox spans for obstacles
                // This catches walls at any row the ship currently occupies
                if (tx > startTileX)
                {
                    for (int ty = topTile; ty <= botTile; ty++)
                    {
                        var col = GetTileCollision(tx, ty);
                        if (col != MetatileCollision.COL_NONE)
                        {
                            var (cL, cT, cR, cB) = GetCollisionBounds(col);
                            if (cR > cL) // solid block in the ship's path
                            {
                                int blockTop = ty * TILE + cT;
                                int blockBottom = ty * TILE + cB;

                                // Determine if the ship should go above or below this obstacle.
                                // Check available space above vs below the block.
                                int spaceAbove = blockTop - ceilingY;
                                int spaceBelow = floorY - blockBottom;

                                if (spaceAbove >= hbH && (spaceAbove >= spaceBelow || spaceBelow < hbH))
                                {
                                    // Fly over: treat block top as floor
                                    if (blockTop < floorY) floorY = blockTop;
                                }
                                else
                                {
                                    // Fly under: treat block bottom as ceiling
                                    if (blockBottom > ceilingY) ceilingY = blockBottom;
                                }
                            }
                        }
                    }

                    // Also check 1 tile above and below the hitbox span for obstacles
                    // that the ship would encounter if it adjusts vertically
                    int[] marginTiles = { topTile - 1, botTile + 1 };
                    foreach (int ty in marginTiles)
                    {
                        if (ty < 0 || ty >= mapHeight - groundRowsToReserve) continue;
                        if (ty >= topTile && ty <= botTile) continue; // already checked in main scan
                        var col = GetTileCollision(tx, ty);
                        if (col != MetatileCollision.COL_NONE)
                        {
                            var (cL, cT, cR, cB) = GetCollisionBounds(col);
                            if (cR > cL)
                            {
                                int blockTop = ty * TILE + cT;
                                int blockBottom = ty * TILE + cB;
                                // If block is above → tighten ceiling
                                if (ty < topTile && blockBottom > ceilingY) ceilingY = blockBottom;
                                // If block is below → tighten floor
                                if (ty > botTile && blockTop < floorY) floorY = blockTop;
                            }
                        }
                    }
                }
            }

            // Target: center of tightest corridor, offset so top-left Y puts center at midpoint
            return (ceilingY + floorY) / 2 - hbH / 2;
        }

        /// <summary>
        /// Greedy lookahead for ship: each frame, pick hold vs release based
        /// on which one-step result survives longer in a secondary lookahead.
        /// Returns number of frames survived (up to horizon).
        /// </summary>
        private int GreedyShipLookahead(SimState start, int horizon)
        {
            var s = start.Clone();

            for (int f = 0; f < horizon; f++)
            {
                // Detect corridor center at current position for tiebreaking
                int targetY = FindCorridorCenter(ref s);

                // Quick 1-step test for each option
                var sH = s.Clone();
                var sR = s.Clone();
                bool aliveH = StepFrame(ref sH, true, out bool endH);
                bool aliveR = StepFrame(ref sR, false, out bool endR);

                if (!aliveH && !aliveR) return f; // both die
                if (endH) return horizon; // hold reaches end
                if (endR) return horizon; // release reaches end

                bool pickHold;
                if (aliveH && !aliveR) pickHold = true;
                else if (!aliveH && aliveR) pickHold = false;
                else
                {
                    // Both survive — pick the one closer to corridor center
                    int hDist = Math.Abs((sH.Y_fixed >> 8) - targetY);
                    int rDist = Math.Abs((sR.Y_fixed >> 8) - targetY);
                    pickHold = hDist <= rDist;
                }

                // Advance with picked input
                bool alive = StepFrame(ref s, pickHold, out bool end);
                if (!alive) return f;
                if (end) return horizon;
            }
            return horizon;
        }

        private (int survival, int finalY) SimulateShipForward(SimState state, bool hold)
        {
            var s = state.Clone();
            for (int f = 0; f < LOOKAHEAD_HORIZON; f++)
            {
                bool alive = StepFrame(ref s, hold, out bool endLevel);
                if (!alive) return (f, s.Y_fixed);
                if (endLevel) return (LOOKAHEAD_HORIZON, s.Y_fixed);
            }
            return (LOOKAHEAD_HORIZON, s.Y_fixed);
        }

        /// <summary>
        /// Simulate forward, pressing jump on exactly one frame (jumpFrame).
        /// Pass jumpFrame = -1 to never jump (pure no-input simulation).
        /// </summary>
        private int SimulateForwardWithJumpAt(SimState state, int jumpFrame)
        {
            var s = state.Clone();

            for (int f = 0; f < LOOKAHEAD_HORIZON; f++)
            {
                bool input = (f == jumpFrame && s.VelY_fixed == 0);
                bool alive = StepFrame(ref s, input, out bool endLevel);
                if (!alive) return f;
                if (endLevel) return LOOKAHEAD_HORIZON;
            }

            return LOOKAHEAD_HORIZON;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  FRAME SIMULATION — matches SimulateNumericStep + CubePhysics_Fresh
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Simulate one frame with EXACT ordering matching the real simulator:
        /// Matching SimulatorWindow.SimulateNumericStep order:
        ///   1. sprite_collide()        — portals, pads at current X
        ///   2. x_movement()            — X advance (for ground support only)
        ///   3. (ground support check)  — verify floor at new X
        ///   4. REVERT to OLD X
        ///   5. movement()              — Y physics + eject at OLD X
        ///   6. x_movement_coll()       — forward wall check at OLD X, post-eject Y
        ///   7. RESTORE NEW X
        ///   8. bg_coll_death()         — death check at new X
        /// </summary>
        private bool StepFrame(ref SimState s, bool input, out bool endLevel)
        {
            endLevel = false;

            int oldX_fixed = s.X_fixed;
            int oldX_px = oldX_fixed >> 8;

            // ── STEP 1: PROCESS SPRITES at current X (sprite_collide) ──
            // We check sprites the player will cross this frame by peeking ahead
            int peekNewX = (s.X_fixed + s.VelX_fixed) >> 8;
            endLevel = ProcessSprites(ref s, oldX_px, peekNewX);
            if (endLevel) return true;

            // ── STEP 2: X ADVANCE for ground support check ──
            int newX_fixed = s.X_fixed + s.VelX_fixed;
            s.X_fixed = newX_fixed;

            // ── STEP 3: GROUND SUPPORT CHECK at new X ──
            // Simulator checks ground support at NEW X BEFORE physics,
            // so walking off a ledge clears OnGround before gravity runs.
            if (s.OnGround)
            {
                if (!VerifyGroundSupport(ref s))
                {
                    s.OnGround = false;
                    s.WasZeroedByCollision = false;
                }
            }

            // ── STEP 4: REVERT to OLD X for physics ──
            // Simulator reverts: playerX_fixed = preAdvancePlayerX_fixed
            s.X_fixed = oldX_fixed;

            // ── STEP 5: Y PHYSICS + EJECT at OLD X (movement) ──
            if (s.GameMode == 0) // Cube mode
            {
                CubeGravity(ref s);

                if (CheckCenterPointDeath(ref s))
                    return false;

                bool ejectDied = false;
                CubeEject(ref s, out ejectDied);
                if (ejectDied)
                    return false;

                // Jump input (ONLY when velY == 0, matching gamemode_cube.h)
                if (input && s.VelY_fixed == 0)
                {
                    s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
                    s.OnGround = false;
                }
            }
            else if (s.GameMode == 1) // Ship mode
            {
                ShipGravityAndThrust(ref s, input);

                if (CheckCenterPointDeath(ref s))
                    return false;

                ShipEject(ref s, out bool shipDied);
                if (shipDied)
                    return false;
            }

            // ── STEP 6: FORWARD COLLISION at OLD X, post-eject Y (x_movement_coll / bg_coll_R) ──
            // Already at OLD X (step 4 reverted). NES x_movement_coll() refreshes
            // Generic.y = high_byte(currplayer_y) AFTER eject, so bg_coll_R sees post-eject Y.
            if (s.GameMode == 0 || s.GameMode == 1 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 10)
            {
                if (CheckForwardCollision(ref s))
                    return false;
            }

            // ── STEP 7: RESTORE NEW X ──
            // Simulator restores: playerX_fixed = attemptedPlayerX_fixed
            s.X_fixed = newX_fixed;

            // ── STEP 8: DEATH CHECK at new X (bg_coll_death) ──
            if (CheckDeathCollision(ref s))
                return false;

            // ── STEP 9: MAP BOUNDS CHECK ──
            int playerY_px = s.Y_fixed >> 8;
            int worldBottom = (mapHeight - groundRowsToReserve) * TILE;
            if (playerY_px + TILE < -TILE || playerY_px >= worldBottom + TILE)
                return false;

            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CUBE PHYSICS (matching ProcessCubePhysics_Fresh)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// CommonGravityRoutine_Fresh — apply gravity and integrate Y.
        /// Gravity always applies (no grounded skip). CubeEject handles
        /// snapping back to the floor surface so the net pixel-level
        /// position stays consistent with the simulator's actual behavior.
        /// </summary>
        private void CubeGravity(ref SimState s)
        {
            // Clear wasZeroed flag when velocity is non-zero (player left ground)
            if (s.VelY_fixed != 0)
                s.WasZeroedByCollision = false;

            // Calculate gravity acceleration
            int gravity = GetGravity(s.Mini);
            int accel = gravity * s.GravMul;

            // Check past max fall speed → decelerate
            if (s.GravMul > 0)
            {
                if (s.VelY_fixed > maxFallSpeed)
                    accel = -accel;
            }
            else
            {
                if (s.VelY_fixed < -maxFallSpeed)
                    accel = -accel;
            }

            // Apply acceleration
            s.VelY_fixed += accel;

            // Integrate Y
            s.Y_fixed += s.VelY_fixed;

            // Clamp Y to world bounds
            int maxY_fixed = Math.Max(0, (mapHeight * TILE - TILE)) << 8;
            if (s.Y_fixed > maxY_fixed) s.Y_fixed = maxY_fixed;
        }

        /// <summary>
        /// CubeEject_Fresh — collision detection and position/velocity correction.
        /// Normal gravity: check floor (snap down), then ceiling (headbonk).
        /// Reversed gravity: check ceiling (snap up), then floor (headbonk).
        /// </summary>
        private void CubeEject(ref SimState s, out bool died)
        {
            died = false;

            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);
            int collX = playerX_px;
            int collY = playerY_px + hbOffY;

            if (!s.GravFlipped) // Normal gravity
            {
                // Floor collision (landing): match CubeEject_Fresh flat collision fallback
                var (floorHit, floorTopY, floorSpikeDeath) = CheckFloor(collX, collY, hbW, hbH);
                if (floorSpikeDeath)
                {
                    died = true;
                    return;
                }
                if (floorHit && s.VelY_fixed >= 0)
                {
                    // Snap to rest on floor
                    int newY = floorTopY - hbH - hbOffY;
                    s.Y_fixed = newY << 8;
                    s.VelY_fixed = 0;
                    s.WasZeroedByCollision = true;
                    s.OnGround = true;

                    // Update collision Y for ceiling check
                    collY = newY + hbOffY;
                }

                // Ceiling collision (headbonk): only for modes 0, 4, 8
                if (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8)
                {
                    var (ceilHit, ceilBotY) = CheckCeiling(collX, collY, hbW, hbH);
                    if (ceilHit && s.VelY_fixed < 0)
                    {
                        int newY = ceilBotY - hbOffY;
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = 1; // headbonk: small positive push, not 0
                    }
                }
            }
            else // Reversed gravity
            {
                // Ceiling landing (player falls up toward ceiling)
                if (s.VelY_fixed <= 0)
                {
                    var (ceilHit, ceilBotY) = CheckCeiling(collX, collY, hbW, hbH);
                    if (ceilHit)
                    {
                        int newY = ceilBotY - hbOffY;
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = 0;
                        s.WasZeroedByCollision = true;
                        s.OnGround = true;

                        collY = newY + hbOffY;
                    }
                }

                // Floor collision (headbonk from above): only for modes 0, 4, 8
                if (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8)
                {
                    var (floorHit, floorTopY, floorSpikeDeath) = CheckFloor(collX, collY, hbW, hbH);
                    if (floorSpikeDeath)
                    {
                        died = true;
                        return;
                    }
                    if (floorHit && s.VelY_fixed > 0)
                    {
                        int newY = floorTopY - hbH - hbOffY;
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = unchecked((int)0xFFFF); // -1 headbonk
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  SHIP PHYSICS (matching ShipPhysics_Fresh + UfoShipEject_Fresh)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ship gravity: 4 gravity variants depending on hold state and fall direction.
        /// When holding, gravity is negated (thrust opposite to gravity direction).
        /// Matching ShipPhysics_Fresh in ShipPhysics_Fresh.partial.cs.
        /// </summary>
        private void ShipGravityAndThrust(ref SimState s, bool holding)
        {
            // GRAV_SKIP: match CommonGravityRoutine_Fresh — skip gravity when
            // velocity was zeroed by collision and is still 0.
            // Ship mode (1) is NOT excluded from this check (only modes 8,9 are).
            if (s.VelY_fixed == 0 && s.WasZeroedByCollision)
                return;

            // Clear wasZeroed when velocity is non-zero (player left ground)
            if (s.VelY_fixed != 0)
                s.WasZeroedByCollision = false;

            int gravMul = s.GravMul;
            bool falling = gravMul > 0 ? (s.VelY_fixed > 0) : (s.VelY_fixed < 0);

            int gravity;
            if (holding && falling)
                gravity = ShipGravityHoldFall(s.Mini) * gravMul;
            else if (holding)
                gravity = ShipGravityBase(s.Mini) * gravMul;
            else if (!falling)
                gravity = ShipGravityAfterHold(s.Mini) * gravMul;
            else
                gravity = ShipGravity(s.Mini) * gravMul;

            // Negate when holding (thrust opposite to gravity)
            if (holding)
                gravity = -gravity;

            s.VelY_fixed += gravity;
            s.Y_fixed += s.VelY_fixed;

            // Clamp velocity to ship max speeds
            int maxDown = ShipMaxFallSpeed(s.Mini) * gravMul;
            int maxUp = -ShipMaxFallSpeedHold(s.Mini) * gravMul;

            if (gravMul > 0)
            {
                if (s.VelY_fixed < maxUp) s.VelY_fixed = maxUp;
                if (s.VelY_fixed > maxDown) s.VelY_fixed = maxDown;
            }
            else
            {
                if (s.VelY_fixed > maxUp) s.VelY_fixed = maxUp;
                if (s.VelY_fixed < maxDown) s.VelY_fixed = maxDown;
            }

            // Clamp Y to world bounds
            int maxY_fixed = Math.Max(0, (mapHeight * TILE - TILE)) << 8;
            if (s.Y_fixed > maxY_fixed) s.Y_fixed = maxY_fixed;
            if (s.Y_fixed < 0) s.Y_fixed = 0;
        }

        /// <summary>
        /// Ship eject: ceiling + floor collision (matching UfoShipEject_Fresh).
        /// No slopes — just flat collision.
        /// </summary>
        private void ShipEject(ref SimState s, out bool died)
        {
            died = false;
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);
            int collX = playerX_px;
            int collY = playerY_px + hbOffY;

            // Ceiling check — NES ufo_ship_eject has NO velocity guard
            var (ceilHit, ceilBotY) = CheckCeiling(collX, collY, hbW, hbH);
            if (ceilHit)
            {
                int newY = ceilBotY - hbOffY;
                s.Y_fixed = newY << 8;
                s.VelY_fixed = 0;
                collY = newY + hbOffY;
            }

            // Floor check — NES ufo_ship_eject has NO velocity guard
            var (floorHit, floorTopY, spikeDeath) = CheckFloor(collX, collY, hbW, hbH);
            if (spikeDeath) { died = true; return; }
            if (floorHit)
            {
                int newY = floorTopY - hbH - hbOffY;
                s.Y_fixed = newY << 8;
                s.VelY_fixed = 0;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  GROUND SUPPORT VERIFICATION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verify that the player still has floor support at the current position.
        /// Called after X advance to detect walking off ledges.
        /// Matches SimulateNumericStep's ground support check.
        /// </summary>
        private bool VerifyGroundSupport(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);

            if (!s.GravFlipped)
            {
                // Normal gravity: check below feet
                int footY_px = playerY_px + hbOffY + hbH;
                int tileBelowY = footY_px / TILE;
                int tileArrayY = tileBelowY + groundRowsToReserve;

                // Ground layer always provides support
                if (tileArrayY >= mapHeight)
                    return true;

                if (tileArrayY < 0) return false;

                int tileLeftX = playerX_px / TILE;
                int tileRightX = (playerX_px + hbW - 1) / TILE;

                for (int tx = tileLeftX; tx <= tileRightX; tx++)
                {
                    if (tx < 0 || tx >= mapWidth) continue;
                    int idx = tileArrayY * mapWidth + tx;
                    if (idx < 0 || idx >= tiles.Length) continue;
                    int tid = tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)tid);
                    if (col == MetatileCollision.COL_NONE) continue;

                    // Check if this tile provides floor support
                    var (cLeft, cTop, cRight, cBottom) = GetCollisionBounds(col);
                    if (cRight <= cLeft) continue;

                    int tileWorldX = tx * TILE;
                    int colLeft_px = tileWorldX + cLeft;
                    int colRight_px = tileWorldX + cRight;

                    // Check horizontal overlap
                    if ((playerX_px + hbW - 1) >= colLeft_px && playerX_px < colRight_px)
                    {
                        // Check if foot is near the collision top
                        int tileWorldY = tileBelowY * TILE;
                        int collisionTop_px = tileWorldY + cTop;
                        if (footY_px >= collisionTop_px - 1 && footY_px <= tileWorldY + cBottom)
                            return true;
                    }
                }

                return false;
            }
            else
            {
                // Reversed gravity: check above head
                int headY_px = playerY_px + hbOffY;
                int tileAboveY = (headY_px - 1) / TILE;
                if (tileAboveY < 0) return false;

                int tileArrayY = tileAboveY + groundRowsToReserve;
                if (tileArrayY < 0 || tileArrayY >= mapHeight) return false;

                int tileLeftX = playerX_px / TILE;
                int tileRightX = (playerX_px + hbW - 1) / TILE;

                for (int tx = tileLeftX; tx <= tileRightX; tx++)
                {
                    if (tx < 0 || tx >= mapWidth) continue;
                    int idx = tileArrayY * mapWidth + tx;
                    if (idx < 0 || idx >= tiles.Length) continue;
                    int tid = tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)tid);
                    if (col == MetatileCollision.COL_NONE) continue;

                    var (cLeft, cTop, cRight, cBottom) = GetCollisionBounds(col);
                    if (cRight <= cLeft) continue;
                    int tileWorldX = tx * TILE;
                    int colLeft_px = tileWorldX + cLeft;
                    int colRight_px = tileWorldX + cRight;
                    if ((playerX_px + hbW - 1) >= colLeft_px && playerX_px < colRight_px)
                        return true;
                }

                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  SPRITE PROCESSING
        // ═══════════════════════════════════════════════════════════════════

        private void ApplyPortalsUpTo(ref SimState s, int targetX_px)
        {
            int? lastGameModeSid = null, lastGameModeX = null;
            int? lastMiniSid = null, lastMiniX = null;
            int? lastGravSid = null, lastGravX = null;
            int? lastSpeedSid = null, lastSpeedX = null;

            foreach (var sp in allSprites)
            {
                if (sp.AnchorX_px > targetX_px) break;

                int sid = sp.SpriteId;

                if (IsGameModePortal(sid))
                {
                    if (!lastGameModeX.HasValue || sp.AnchorX_px > lastGameModeX.Value)
                    { lastGameModeSid = sid; lastGameModeX = sp.AnchorX_px; }
                }
                else if (IsMiniGrowthPortal(sid))
                {
                    if (!lastMiniX.HasValue || sp.AnchorX_px > lastMiniX.Value)
                    { lastMiniSid = sid; lastMiniX = sp.AnchorX_px; }
                }
                else if (IsGravityPortal(sid))
                {
                    if (!lastGravX.HasValue || sp.AnchorX_px > lastGravX.Value)
                    { lastGravSid = sid; lastGravX = sp.AnchorX_px; }
                }
                else if (IsSpeedPortal(sid))
                {
                    if (!lastSpeedX.HasValue || sp.AnchorX_px > lastSpeedX.Value)
                    { lastSpeedSid = sid; lastSpeedX = sp.AnchorX_px; }
                }

                s.ProcessedSprites.Add(sp.Index);
            }

            if (lastGameModeSid.HasValue)
            {
                int mode = SpriteIdToGameMode(lastGameModeSid.Value);
                if (mode >= 0) s.GameMode = mode;
            }
            if (lastMiniSid.HasValue)
                s.Mini = (lastMiniSid.Value == 0x18);
            if (lastGravSid.HasValue)
            {
                bool rev = IsReverseGravity(lastGravSid.Value);
                s.GravFlipped = rev;
                s.GravMul = rev ? -1 : 1;
            }
            if (lastSpeedSid.HasValue)
            {
                int spd = SpriteIdToSpeedFixed(lastSpeedSid.Value);
                if (spd > 0) s.VelX_fixed = spd;
            }
        }

        private bool ProcessSprites(ref SimState s, int prevX_px, int newX_px)
        {
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);
            int playerTop = playerY_px + hbOffY;
            int playerBottom = playerTop + hbH;
            int playerRight = newX_px + hbW;

            foreach (var sp in allSprites)
            {
                if (s.ProcessedSprites.Contains(sp.Index)) continue;
                if (sp.AnchorX_px + TILE < prevX_px) continue;
                if (sp.AnchorX_px - TILE > playerRight + TILE) break;

                int sid = sp.SpriteId;

                // Portal/end detection — requires hitbox overlap (X AND Y)
                // Uses pre-computed per-sprite hitbox rect from geometry tables
                if (IsGameModePortal(sid) || IsSpeedPortal(sid) || IsGravityPortal(sid) ||
                    IsMiniGrowthPortal(sid) || IsEndLevel(sid))
                {
                    bool xOverlap = playerRight > sp.HitLeft && newX_px < sp.HitRight;
                    bool yOverlap = playerBottom > sp.HitTop && playerTop < sp.HitBottom;

                    if (xOverlap && yOverlap)
                    {
                        s.ProcessedSprites.Add(sp.Index);
                        ApplyPortalSprite(ref s, sid);
                        if (IsEndLevel(sid)) return true;
                    }
                    continue;
                }

                // Pad detection (requires hitbox overlap)
                if (IsYellowPad(sid) || IsPinkPad(sid) || IsRedPad(sid) || IsBluePad(sid) || IsGreenPad(sid))
                {
                    bool xOverlap = (newX_px < sp.HitRight && playerRight > sp.HitLeft);
                    bool yOverlap = (playerTop < sp.HitBottom && playerBottom > sp.HitTop);

                    if (xOverlap && yOverlap)
                    {
                        s.ProcessedSprites.Add(sp.Index);
                        ApplyPadSprite(ref s, sid);
                    }
                    continue;
                }
            }

            return false;
        }

        private void ApplyPortalSprite(ref SimState s, int sid)
        {
            if (IsEndLevel(sid)) return;

            if (IsGameModePortal(sid))
            {
                int mode = SpriteIdToGameMode(sid);
                if (mode >= 0 && mode != s.GameMode)
                {
                    s.GameMode = mode;
                    // Simulator halves Y velocity on game mode change
                    // (matching: playerVelY_fixed[0] = playerVelY_fixed[0] / 2)
                    s.VelY_fixed /= 2;
                }
            }
            else if (IsSpeedPortal(sid))
            {
                int spd = SpriteIdToSpeedFixed(sid);
                if (spd > 0) s.VelX_fixed = spd;
            }
            else if (IsGravityPortal(sid))
            {
                bool rev = IsReverseGravity(sid);
                s.GravFlipped = rev;
                s.GravMul = rev ? -1 : 1;
            }
            else if (IsMiniGrowthPortal(sid))
            {
                s.Mini = (sid == 0x18);
            }
        }

        private void ApplyPadSprite(ref SimState s, int sid)
        {
            if (IsYellowPad(sid))
            {
                s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsPinkPad(sid))
            {
                int pinkVel = s.Mini ? -0x680 : -0x7C0;
                s.VelY_fixed = pinkVel * s.GravMul;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsRedPad(sid))
            {
                int redVel = s.Mini ? -0x800 : -0x990;
                s.VelY_fixed = redVel * s.GravMul;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsBluePad(sid))
            {
                bool isBottomPad = (sid == 0x0D || sid == 0xFD);
                s.GravFlipped = isBottomPad;
                s.GravMul = isBottomPad ? -1 : 1;
                s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsGreenPad(sid))
            {
                s.GravFlipped = !s.GravFlipped;
                s.GravMul = s.GravFlipped ? -1 : 1;
                s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  COLLISION DETECTION (matching CollisionDetection.partial.cs)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get tile collision type at world tile coordinates.
        /// Returns COL_ALL for implicit ground layer, COL_NONE for out of bounds.
        /// </summary>
        private MetatileCollision GetTileCollision(int tileX, int tileY)
        {
            int tileArrayY = tileY + groundRowsToReserve;
            if (tileX < 0 || tileX >= mapWidth) return MetatileCollision.COL_NONE;
            if (tileArrayY < 0) return MetatileCollision.COL_NONE;
            if (tileArrayY >= mapHeight) return MetatileCollision.COL_ALL; // ground layer = solid
            int idx = tileArrayY * mapWidth + tileX;
            if (idx < 0 || idx >= tiles.Length) return MetatileCollision.COL_NONE;
            int tid = tiles[idx];
            return MetatileCollisionTable.GetCollision((byte)tid);
        }

        private static (int left, int top, int right, int bottom) GetCollisionBounds(MetatileCollision col)
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
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                    return (0, 0, 8, 8);
                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                    return (8, 0, 16, 8);
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    return (0, 0, 16, 16); // complex shapes

                // Pure death / slopes / none — no solid collision
                default:
                    return (16, 16, 0, 0);
            }
        }

        /// <summary>
        /// Check floor collision (downward). Matching CheckCollisionDown in CollisionDetection.partial.cs.
        /// Returns (hit, surfaceY, spikeDeath).
        /// 
        /// The spike death pre-check uses 3 X-points at the player's bottom edge,
        /// only checking COL_DEATH_TOP and COL_DEATH_BOTTOM (matching NES bg_coll_D).
        /// </summary>
        private (bool hit, int surfaceY, bool spikeDeath) CheckFloor(int collX, int collY, int collW, int collH)
        {
            int playerBottom_px = collY + collH;
            int tileBelowY = playerBottom_px / TILE;
            int playerLeft_px = collX;
            int playerRight_px = collX + collW - 1;

            // ── SPIKE DEATH PRE-CHECK (3 points at bottom edge) ──
            // Matches CheckCollisionDown's spike detection
            {
                int checkY = playerBottom_px;
                for (int cpIdx = 0; cpIdx < 3; cpIdx++)
                {
                    int px = cpIdx == 0 ? playerLeft_px
                           : cpIdx == 1 ? playerLeft_px + collW / 2
                           : playerLeft_px + collW;
                    int tileX = px / TILE;
                    int tileY = checkY / TILE;

                    if (tileX < 0 || tileX >= mapWidth || tileY < 0 || tileY >= mapHeight) continue;

                    int tileArrayY = tileY + groundRowsToReserve;
                    if (tileArrayY < 0 || tileArrayY >= mapHeight) continue;

                    int tileIdx = tileArrayY * mapWidth + tileX;
                    if (tileIdx < 0 || tileIdx >= tiles.Length) continue;

                    int tileId = tiles[tileIdx];
                    var collision = MetatileCollisionTable.GetCollision((byte)tileId);

                    int localX = px % TILE;
                    int localY = checkY % TILE;

                    // Only COL_DEATH_TOP and COL_DEATH_BOTTOM are checked here
                    // (matching NES bg_coll_D → bg_coll_U_D_checks)
                    if ((collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM) &&
                        MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                    {
                        return (false, 0, true); // spike death!
                    }
                }
            }

            // ── BOUNDS CHECK ──
            if (tileBelowY < 0 || tileBelowY >= mapHeight) return (false, 0, false);

            int tileArrayYFloor = tileBelowY + groundRowsToReserve;

            // Ground layer is always solid
            if (tileArrayYFloor >= mapHeight)
            {
                int groundTop = tileBelowY * TILE;
                return (true, groundTop, false);
            }

            // ── TILE SCAN (left to right) ──
            int tileLeftX = playerLeft_px / TILE;
            int tileRightX = playerRight_px / TILE;

            for (int tx = tileLeftX; tx <= tileRightX; tx++)
            {
                if (tx < 0 || tx >= mapWidth) continue;

                int tileIdx = tileArrayYFloor * mapWidth + tx;
                if (tileIdx < 0 || tileIdx >= tiles.Length) continue;

                int tileId = tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                if (collision == MetatileCollision.COL_NONE) continue;

                var (cLeft, cTop, cRight, cBottom) = GetCollisionBounds(collision);
                if (cRight <= cLeft || cBottom <= cTop) continue; // no solid

                int tileWorldX = tx * TILE;
                int tileWorldY = tileBelowY * TILE;
                int collisionTop_px = tileWorldY + cTop;
                int collisionBottom_px = tileWorldY + cBottom;
                int colLeft_px = tileWorldX + cLeft;
                int colRight_px = tileWorldX + cRight;

                if (playerBottom_px >= collisionTop_px - 1 && playerBottom_px <= collisionBottom_px)
                {
                    if (playerRight_px >= colLeft_px && playerLeft_px < colRight_px)
                    {
                        return (true, collisionTop_px, false);
                    }
                }
            }

            // Also check implicit ground floor
            if (groundRowsToReserve > 0)
            {
                int groundTopWorld_px = (mapHeight - groundRowsToReserve) * TILE;
                if (playerBottom_px >= groundTopWorld_px - 1)
                    return (true, groundTopWorld_px, false);
            }

            return (false, 0, false);
        }

        /// <summary>
        /// Check ceiling collision (upward). Returns (hit, ceilingBottomY).
        /// Matching CheckCollisionUp in CollisionDetection.partial.cs.
        /// </summary>
        private (bool hit, int ceilingBottomY) CheckCeiling(int collX, int collY, int collW, int collH)
        {
            int topY = collY;
            int tileAboveY = (topY - 1) / TILE;
            if (tileAboveY < 0) return (false, 0);

            int tileLeftX = collX / TILE;
            int tileRightX = (collX + collW - 1) / TILE;
            int tileArrayY = tileAboveY + groundRowsToReserve;
            if (tileArrayY < 0 || tileArrayY >= mapHeight) return (false, 0);

            for (int tx = tileLeftX; tx <= tileRightX; tx++)
            {
                if (tx < 0 || tx >= mapWidth) continue;
                int tileIdx = tileArrayY * mapWidth + tx;
                if (tileIdx < 0 || tileIdx >= tiles.Length) continue;

                int tileId = tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                if (collision == MetatileCollision.COL_NONE) continue;

                var (cLeft, cTop, cRight, cBottom) = GetCollisionBounds(collision);
                if (cRight <= cLeft) continue;

                int tileWorldX = tx * TILE;
                int tileWorldY = tileAboveY * TILE;
                int colBottom_px = tileWorldY + cBottom;
                int colLeft_px = tileWorldX + cLeft;
                int colRight_px = tileWorldX + cRight;

                if ((collX + collW - 1) >= colLeft_px && collX < colRight_px)
                {
                    if (topY >= tileWorldY + cTop && topY < colBottom_px)
                        return (true, colBottom_px);
                }
            }

            return (false, 0);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  DEATH CHECKS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// CheckCenterPointDeath_Fresh — single center pixel death check.
        /// Matches CubePhysics_Fresh.partial.cs bg_coll_death().
        /// Center point: (x + (w>>1) - 1, y + (h>>1) + hitboxOffsetY)
        /// </summary>
        private bool CheckCenterPointDeath(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);

            int centerX_px = playerX_px + (hbW >> 1) - 1;
            int centerY_px = playerY_px + (hbH >> 1) + hbOffY;

            int tileX = centerX_px / TILE;
            int tileY = centerY_px / TILE;

            if (tileX < 0 || tileX >= mapWidth || tileY < 0 || tileY >= mapHeight) return false;

            int tileArrayY = tileY + groundRowsToReserve;
            if (tileArrayY < 0 || tileArrayY >= mapHeight) return false;

            int tileIdx = tileArrayY * mapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= tiles.Length) return false;

            int tileId = tiles[tileIdx];
            var collision = MetatileCollisionTable.GetCollision((byte)tileId);

            int localX = centerX_px % TILE;
            int localY = centerY_px % TILE;

            return MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY);
        }

        /// <summary>
        /// CheckDeathCollision — 5-point death check matching SimulatorWindow's CheckDeathCollision.
        /// Points: 4 hitbox corners (inset 3px sides, 2px top/bottom) + right-center (side spikes).
        /// </summary>
        private bool CheckDeathCollision(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);

            // 5 check points matching CheckDeathCollision in SimulatorWindow:
            int leftX = playerX_px + 3;
            int rightX = playerX_px + hbW - 3;
            int rightEdgeX = playerX_px + hbW - 1;
            int topY = playerY_px + hbH - 2 + hbOffY;      // near bottom of hitbox
            int bottomY = playerY_px + (s.Mini ? 0 : 2) + hbOffY; // near top of hitbox
            int centerY = playerY_px + (hbH / 2) + hbOffY;

            // Point 0: (leftX, topY) — bottom-left
            // Point 1: (rightX, topY) — bottom-right
            // Point 2: (leftX, bottomY) — top-left
            // Point 3: (rightX, bottomY) — top-right
            // Point 4: (rightEdgeX, centerY) — right-center (side spikes)
            int px, py;
            for (int i = 0; i < 5; i++)
            {
                switch (i)
                {
                    case 0: px = leftX; py = topY; break;
                    case 1: px = rightX; py = topY; break;
                    case 2: px = leftX; py = bottomY; break;
                    case 3: px = rightX; py = bottomY; break;
                    default: px = rightEdgeX; py = centerY; break;
                }

                int tileX = px / TILE;
                int tileY = py / TILE;
                int tileArrayY = tileY + groundRowsToReserve;

                if (tileX < 0 || tileX >= mapWidth) continue;
                if (tileArrayY < 0 || tileArrayY >= mapHeight) continue;

                int tileIdx = tileArrayY * mapWidth + tileX;
                if (tileIdx < 0 || tileIdx >= tiles.Length) continue;

                int tid = tiles[tileIdx];
                var col = MetatileCollisionTable.GetCollision((byte)tid);

                int localX = px % TILE;
                int localY = py % TILE;

                if (MetatileCollisionTable.TileKillsAtPixel(col, localX, localY))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Forward collision check — right edge middle pixel for solid collision.
        /// Matches SimulateNumericStep step 24 (x_movement_coll).
        /// This detects running into a wall and triggers death.
        /// </summary>
        private bool CheckForwardCollision(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);

            // NES bg_coll_R checks at Generic.x + Generic.width (one pixel PAST the hitbox right edge)
            int rightEdge_px = playerX_px + hbW;
            // NES bg_side_coll_common: Generic.y + (mini ? (0x10-height)>>1 : 0) + (height>>1)
            // then for mini cube/robot/ninja: += gravity ? 3 : -2
            int centerY_px;
            if (s.Mini)
            {
                int miniTopOffset = (0x10 - hbH) >> 1;  // (16-7)>>1 = 4
                centerY_px = playerY_px + miniTopOffset + (hbH >> 1);  // +4 +3 = +7
                // Mini cube/robot/ninja adjustment
                if (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8)
                    centerY_px += s.GravFlipped ? 3 : -2;
            }
            else
            {
                centerY_px = playerY_px + (hbH >> 1);  // +7 for normal cube
            }

            int tileX = rightEdge_px / TILE;
            int tileY = centerY_px / TILE;
            int tileArrayY = tileY + groundRowsToReserve;

            if (tileX < 0 || tileX >= mapWidth) return false;
            if (tileArrayY < 0 || tileArrayY >= mapHeight) return false;

            int tileIdx = tileArrayY * mapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= tiles.Length) return false;

            int tileId = tiles[tileIdx];
            var collision = MetatileCollisionTable.GetCollision((byte)tileId);

            // Check if the collision type is solid (provides a wall)
            var (cLeft, cTop, cRight, cBottom) = GetCollisionBounds(collision);
            if (cRight <= cLeft) return false; // not solid

            int tileWorldX = tileX * TILE;
            int tileWorldY = tileY * TILE;
            int localX = rightEdge_px - tileWorldX;
            int localY = centerY_px - tileWorldY;

            // Check if the right edge pixel is inside the solid region
            if (localX >= cLeft && localX < cRight && localY >= cTop && localY < cBottom)
                return true; // blocked → death

            return false;
        }
    }
}

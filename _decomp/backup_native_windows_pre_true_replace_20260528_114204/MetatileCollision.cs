using System;

namespace FamidashEditor
{
    // Collision categories used by the editor/runtime. Extend as needed.
    public enum MetatileCollision
    {
        COL_NONE,
        COL_ALL,
        COL_TOP,
        COL_BOTTOM,
        COL_LEFT,
        COL_RIGHT,
        COL_UP_LEFT,
        COL_UP_RIGHT,
        COL_DOWN_LEFT,
        COL_DOWN_RIGHT,
        COL_FLOOR_CEIL,
        COL_DEATH,
        COL_DEATH_TOP,
        COL_DEATH_BOTTOM,
        COL_DEATH_LEFT,
        COL_DEATH_RIGHT,
        COL_DEATH_TOP_RIGHT,
        COL_DEATH_TOP_LEFT,
        COL_DEATH_BOTTOM_RIGHT,
        COL_DEATH_BOTTOM_LEFT,
        COL_DEATH_TOP_RIGHT_LEFT,
        COL_DEATH_TOP_BOTTOM,
        COL_DEATH_LEFT_RIGHT,
        COL_DEATH_TOP_LEFT_BOTTOM,
        COL_TOP_CENTER_SPIKE,
        COL_BOTTOM_CENTER_SPIKE,
        COL_DOWN_RIGHT_SPIKE,
        COL_DOWN_LEFT_SPIKE,
        COL_UP_LEFT_SPIKE,
        COL_UP_RIGHT_SPIKE,
        COL_UP_BOTH_SPIKES,
        COL_DOWN_BOTH_SPIKES,
        COL_LEFT_SPIKE_BLOCK,
        COL_RIGHT_SPIKE_BLOCK,
        COL_BOTTOM_LEFT_SPIKE,
        COL_BOTTOM_RIGHT_SPIKE,
        COL_BOTTOM_SPIKES,
        COL_TOP_LEFT_STAIRS,
        COL_TOP_RIGHT_STAIRS,
        COL_BOTTOM_LEFT_STAIRS,
        COL_BOTTOM_RIGHT_STAIRS,
        COL_TOP_LEFT_BOTTOM_RIGHT,
        COL_TOP_RIGHT_BOTTOM_LEFT,
        COL_NO_SIDE,
        COL_SLOPE_RD45,
        COL_SLOPE_LD45,
        COL_SLOPE_RU45,
        COL_SLOPE_LU45,
        COL_SLOPE_RD22_RIGHT,
        COL_SLOPE_RD22_LEFT,
        COL_SLOPE_LD22_RIGHT,
        COL_SLOPE_LD22_LEFT,
        COL_SLOPE_RU22_RIGHT,
        COL_SLOPE_RU22_LEFT,
        COL_SLOPE_LU22_RIGHT,
        COL_SLOPE_LU22_LEFT,
        COL_SLOPE_RD66_TOP,
        COL_SLOPE_RD66_BOT,
        COL_SLOPE_LD66_BOT,
        COL_SLOPE_LD66_TOP,
        COL_SLOPE_RU66_TOP,
        COL_SLOPE_RU66_BOT,
        COL_SLOPE_LU66_BOT,
        COL_SLOPE_LU66_TOP
    }

    public static class MetatileCollisionTable
    {
        // Lookup table for 256 metatiles. Index is metatile id (0..255).
        // We'll initialize from the user-provided listing below. Unknown or malformed lines default to COL_NONE.
        private static readonly MetatileCollision[] table = new MetatileCollision[256];

        /// <summary>
        /// Check if a specific pixel inside a metatile with given collision type causes death.
        /// localX, localY are 0..15 (pixel coordinates within the 16x16 tile).
        /// Matches the original collision.h death routines from Famidash.
        /// </summary>
        public static bool TileKillsAtPixel(MetatileCollision col, int localX, int localY)
        {
            // Helper: check if value is in range [min, max] inclusive
            bool InRange(int val, int min, int max) => val >= min && val <= max;

            switch (col)
            {
                // Pure death tiles
                case MetatileCollision.COL_DEATH:
                    // Center spike: NES checks Y in [4,11], X in [4,8]
                    return InRange(localY, 0x04, 0x0b) && InRange(localX, 0x04, 0x08);

                case MetatileCollision.COL_DEATH_TOP:
                    // NES col_death_top_routine: y < 0x06, x in [0x03, 0x09] (asm: sbc #0x03; sbc #0x07; bcs skip)
                    return (localY < 0x06) && InRange(localX, 0x03, 0x09);

                case MetatileCollision.COL_DEATH_BOTTOM:
                    // NES col_death_bottom_routine: y > 10, x in [0x03, 0x09] (asm: sbc #0x03; sbc #0x07; bcs skip)
                    // which is equivalent to: x >= 0x03 && x <= 0x09
                    return (localY > 0x0a) && InRange(localX, 0x03, 0x09);

                case MetatileCollision.COL_DEATH_LEFT:
                    // NES col_death_left_routine: x < 0x06, y in [0x03, 0x09] (asm: sbc #0x03; sbc #0x07; bcs skip)
                    return (localX < 0x06) && InRange(localY, 0x03, 0x09);

                case MetatileCollision.COL_DEATH_RIGHT:
                    // NES col_death_right_routine: x >= 0x0a, y in [0x03, 0x09] (asm: sbc #0x03; sbc #0x07; bcs skip)
                    return (localX >= 0x0a) && InRange(localY, 0x03, 0x09);

                // Diagonal death tiles
                case MetatileCollision.COL_DEATH_BOTTOM_LEFT:
                          // NES: col_death_left_routine() | col_death_bottom_routine()
                          return ((localX < 0x06) && InRange(localY, 0x03, 0x09)) ||
                              ((localY > 0x0a) && InRange(localX, 0x03, 0x09));

                case MetatileCollision.COL_DEATH_BOTTOM_RIGHT:
                          // NES: col_death_right_routine() | col_death_bottom_routine()
                          return ((localX >= 0x0a) && InRange(localY, 0x03, 0x09)) ||
                              ((localY > 0x0a) && InRange(localX, 0x03, 0x09));

                case MetatileCollision.COL_DEATH_TOP_LEFT:
                          // NES: col_death_left_routine() | col_death_top_routine()
                          return ((localX < 0x06) && InRange(localY, 0x03, 0x09)) ||
                              ((localY < 0x06) && InRange(localX, 0x03, 0x09));

                case MetatileCollision.COL_DEATH_TOP_RIGHT:
                          // NES: col_death_right_routine() | col_death_top_routine()
                          return ((localX >= 0x0a) && InRange(localY, 0x03, 0x09)) ||
                              ((localY < 0x06) && InRange(localX, 0x03, 0x09));

                // Combo death tiles (no NES tiles currently mapped, included for completeness)
                case MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT:
                          return ((localY < 0x06) && InRange(localX, 0x03, 0x09)) ||
                              ((localX >= 0x0a) && InRange(localY, 0x03, 0x09)) ||
                              ((localX < 0x06) && InRange(localY, 0x03, 0x09));

                case MetatileCollision.COL_DEATH_TOP_BOTTOM:
                          return ((localY < 0x06) && InRange(localX, 0x03, 0x09)) ||
                              ((localY > 0x0a) && InRange(localX, 0x03, 0x09));

                case MetatileCollision.COL_DEATH_LEFT_RIGHT:
                          return ((localX < 0x06) && InRange(localY, 0x03, 0x09)) ||
                              ((localX >= 0x0a) && InRange(localY, 0x03, 0x09));

                case MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM:
                          return ((localY < 0x06) && InRange(localX, 0x03, 0x09)) ||
                              ((localX < 0x06) && InRange(localY, 0x03, 0x09)) ||
                              ((localY > 0x0a) && InRange(localX, 0x03, 0x09));

                // Pure death spike tiles (no solid collision)
                // UP spikes = spikes pointing up from bottom, death in TOP half
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                    // Top half with left spike (X range 2-5)
                    return (localY < 0x08) && InRange(localX, 0x02, 0x05);

                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                    // Top half with right spike (X range 10-12)
                    return (localY < 0x08) && InRange(localX, 0x0a, 0x0c);

                case MetatileCollision.COL_UP_BOTH_SPIKES:
                    // Top half with repeating spikes
                    return (localY < 0x08) && InRange(localX & 0x07, 0x02, 0x05);

                // DOWN spikes = spikes pointing down from top, death in BOTTOM half
                case MetatileCollision.COL_DOWN_LEFT_SPIKE:
                    // Bottom half with left spike (X range 2-5)
                    return (localY >= 0x08) && InRange(localX, 0x02, 0x05);

                case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
                    // Bottom half with right spike (X range 10-12)
                    return (localY >= 0x08) && InRange(localX, 0x0a, 0x0c);

                case MetatileCollision.COL_DOWN_BOTH_SPIKES:
                    // Bottom half with repeating spikes
                    return (localY >= 0x08) && InRange(localX & 0x07, 0x02, 0x05);

                // Mixed solid/death tiles (solid collision on one side, death on the other)
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    // NES bg_coll_spikes(COL_TOP_CENTER_SPIKE/COL_TOP_SPIKES) dispatches
                    // to col_death_bottom_routine which kills when:
                    //   (temp_y & 0x0f) > 0x0a   AND   in_range(temp_x & 0x0f, 0x03, 0x09)
                    // Verified against sim-famidash/BUILD/main/famidash.lst at $A48E
                    // (sbc #$03 / sbc #$09-$03+1 → bcs skip).  The previous PF range
                    // [5,8] was too narrow and let a real spike kill (tx=$F3 →
                    // localX=3) survive at shardscapes sc=1943.
                    return (localY > 0x0a) && InRange(localX, 0x03, 0x09);

                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                    // Bottom half has solid collision, top center has death spike
                    return (localY < 0x08) && InRange(localX, 0x07, 0x0a);

                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    // Bottom-left quadrant has solid collision, top half with narrow spike (X range 2-5)
                    return (localY < 0x08) && InRange(localX, 0x02, 0x05);

                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    // Bottom-right quadrant has solid collision, top half with narrow spike (X range 10-12)
                    return (localY < 0x08) && InRange(localX, 0x0a, 0x0c);

                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    // Bottom half solid collision, top half with narrow spike (X range 2-5)
                    return (localY < 0x08) && InRange(localX, 0x02, 0x05);

                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    // Bottom half solid collision, top half with narrow spike (X range 10-12)
                    return (localY < 0x08) && InRange(localX, 0x0a, 0x0c);

                case MetatileCollision.COL_BOTTOM_SPIKES:
                    // Bottom half solid collision, top has repeating death spikes
                    return (localY < 0x08) && InRange(localX & 0x07, 0x02, 0x05);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Side-probe death check — mirrors NES bg_coll_spikes() called from
        /// bg_side_coll_common BEFORE bg_coll_sides. This is what kills the
        /// player when their forward (right/left) edge probe enters a tile.
        ///
        /// Critically, COL_TOP/COL_BOTTOM are SAFE for top/bottom landing
        /// (handled via bg_coll_D/bg_coll_U) but DEADLY when hit from the side
        /// in their solid half. TileKillsAtPixel returns false for these
        /// because it's also used for floor-corner spike checks where they
        /// must remain safe — so this method exists to model the side path.
        /// </summary>
        public static bool TileKillsAtSideProbe(MetatileCollision col, int localX, int localY)
        {
            // First: anything TileKillsAtPixel kills, the side probe also kills.
            if (TileKillsAtPixel(col, localX, localY)) return true;

            // NES bg_coll_spikes additional cases (collision.h ~line 280-320)
            int ly = localY & 0x0f;
            int ly_low3 = ly & 0x07;
            int lx = localX & 0x0f;

            switch (col)
            {
                // case COL_BOTTOM_*: kills when ly >= 8 (i.e. bits 4..7 nonzero in ly)
                case MetatileCollision.COL_BOTTOM:
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                case MetatileCollision.COL_BOTTOM_SPIKES:
                    return ly_low3 != ly;  // == ly >= 8

                // case COL_TOP_*: kills when ly < 8
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    return ly_low3 == ly;  // == ly < 8

                // COL_DOWN_LEFT / COL_LEFT_SPIKE_BLOCK: ly>=8 && lx<8
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    return (ly >= 8) && (lx < 8);

                // COL_DOWN_RIGHT / COL_RIGHT_SPIKE_BLOCK: ly>=8 && lx>=8
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return (ly >= 8) && (lx >= 8);

                case MetatileCollision.COL_LEFT:
                    return lx < 8;
                case MetatileCollision.COL_RIGHT:
                    return lx >= 8;

                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                    return (lx < 8) ? (ly < 8) : (ly >= 8);
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    return (lx < 8) ? (ly >= 8) : (ly < 8);

                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                    return !(ly >= 8 && lx < 8);
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                    return !(ly >= 8 && lx >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    return !(ly < 8 && lx < 8);
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    return !(ly < 8 && lx >= 8);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Models exactly bg_coll_sides() || bg_coll_mini_blocks() from the NES source.
        /// Unlike TileKillsAtSideProbe, this does NOT include spike-kill zones from
        /// bg_coll_spikes(). When bg_coll_spikes() fires, bg_side_coll_common returns 0
        /// (not blocked), so pure spike tiles must return false here.
        /// COL_ALL blocking is handled externally in CheckForwardCollision.
        /// </summary>
        public static bool TileBlocksAtSideProbe(MetatileCollision col, int localX, int localY)
        {
            int ly = localY & 0x0f;
            int ly_low3 = ly & 0x07;
            int lx = localX & 0x0f;

            // bg_coll_sides(): COL_BOTTOM and COL_TOP (COL_ALL handled externally)
            if (col == MetatileCollision.COL_BOTTOM) return ly_low3 != ly;  // ly >= 8
            if (col == MetatileCollision.COL_TOP)    return ly_low3 == ly;  // ly < 8

            // bg_coll_mini_blocks():
            switch (col)
            {
                case MetatileCollision.COL_UP_LEFT:
                    return (ly < 8) && (lx < 8);
                case MetatileCollision.COL_UP_RIGHT:
                    return (ly < 8) && (lx >= 8);

                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    return (ly >= 8) && (lx < 8);

                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return (ly >= 8) && (lx >= 8);

                // Hybrid tiles: bottom half is solid (bg_coll_mini_blocks), top half is spike (bg_coll_spikes)
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                case MetatileCollision.COL_BOTTOM_SPIKES:
                    return ly_low3 != ly;  // ly >= 8

                // Hybrid tiles: top half is solid (bg_coll_mini_blocks), bottom half is spike (bg_coll_spikes)
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    return ly_low3 == ly;  // ly < 8

                case MetatileCollision.COL_LEFT:
                    return lx < 8;
                case MetatileCollision.COL_RIGHT:
                    return lx >= 8;

                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                    return (lx < 8) ? (ly < 8) : (ly >= 8);
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    return (lx < 8) ? (ly >= 8) : (ly < 8);

                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                    return !(ly >= 8 && lx < 8);
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                    return !(ly >= 8 && lx >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    return !(ly < 8 && lx < 8);
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    return !(ly < 8 && lx >= 8);

                // Pure spike tiles (COL_DEATH, COL_DEATH_*, COL_DOWN_LEFT_SPIKE, etc.):
                // bg_coll_spikes() handles these and causes bg_side_coll_common to return 0 (not blocked).
                // They do not appear in bg_coll_sides() or bg_coll_mini_blocks(), so default = false.
                default:
                    return false;
            }
        }

        static MetatileCollisionTable()
        {
            for (int i = 0; i < table.Length; i++) table[i] = MetatileCollision.COL_NONE;

            string mappingText = @"
COL_NONE
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_NONE
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_LEFT
COL_DEATH_RIGHT

COL_ALL
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_TOP_CENTER_SPIKE
COL_ALL
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_TOP
COL_DEATH
COL_DEATH
COL_DEATH
COL_DEATH_LEFT
COL_DEATH_TOP
COL_DEATH_RIGHT

COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE

COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_TOP_CENTER_SPIKE
COL_ALL
COL_ALL
COL_ALL
COL_DEATH_BOTTOM
COL_ALL
COL_ALL
COL_ALL
COL_ALL

COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE

COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_TOP
COL_TOP
COL_TOP
COL_TOP
COL_BOTTOM
COL_BOTTOM
COL_BOTTOM
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_RIGHT
COL_DEATH_LEFT

COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE

COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_DOWN_RIGHT_SPIKE
COL_DEATH_BOTTOM
COL_DOWN_LEFT_SPIKE
COL_DEATH_RIGHT
COL_NONE
COL_DEATH_LEFT
COL_UP_RIGHT_SPIKE
COL_DEATH_TOP
COL_UP_LEFT_SPIKE
COL_DEATH
COL_ALL
COL_DEATH_BOTTOM

COL_NONE;$80
COL_NONE
COL_DEATH
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_RIGHT
COL_LEFT
COL_RIGHT
COL_LEFT
COL_NONE
COL_NO_SIDE

COL_SLOPE_RD45
COL_SLOPE_LD45
COL_SLOPE_RU45
COL_SLOPE_LU45
COL_SLOPE_RD22_RIGHT
COL_SLOPE_RD22_LEFT
COL_SLOPE_LD22_RIGHT
COL_SLOPE_LD22_LEFT
COL_SLOPE_RU22_RIGHT
COL_SLOPE_RU22_LEFT
COL_SLOPE_LU22_RIGHT
COL_SLOPE_LU22_LEFT
COL_SLOPE_RD66_TOP
COL_SLOPE_RD66_BOT
COL_SLOPE_LD66_BOT
COL_SLOPE_LD66_TOP

COL_SLOPE_RU66_TOP
COL_SLOPE_RU66_BOT
COL_SLOPE_LU66_BOT
COL_SLOPE_LU66_TOP
COL_NO_SIDE
COL_NO_SIDE
COL_NO_SIDE
COL_NO_SIDE
COL_LEFT_SPIKE_BLOCK
COL_RIGHT_SPIKE_BLOCK
COL_BOTTOM_LEFT_SPIKE
COL_BOTTOM_RIGHT_SPIKE
COL_BOTTOM_SPIKES
COL_DOWN_LEFT_SPIKE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_BOTH_SPIKES

COL_UP_LEFT
COL_UP_RIGHT
COL_DOWN_LEFT
COL_DOWN_RIGHT
COL_TOP
COL_BOTTOM
COL_LEFT
COL_RIGHT
COL_TOP_LEFT_STAIRS
COL_TOP_RIGHT_STAIRS
COL_BOTTOM_LEFT_STAIRS
COL_BOTTOM_RIGHT_STAIRS
COL_TOP_LEFT_BOTTOM_RIGHT
COL_TOP_RIGHT_BOTTOM_LEFT
COL_UP_LEFT_SPIKE
COL_UP_RIGHT_SPIKE

COL_UP_BOTH_SPIKES
COL_DEATH_TOP_RIGHT
COL_DEATH_TOP_LEFT
COL_DEATH_BOTTOM_RIGHT
COL_DEATH_BOTTOM_LEFT
COL_NONE
COL_NONE
COL_BOTTOM_CENTER_SPIKE
COL_BOTTOM_CENTER_SPIKE
COL_TOP
COL_BOTTOM
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE

COL_NONE
COL_NONE
COL_NONE
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_UP_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_LEFT
COL_RIGHT
COL_UP_RIGHT

COL_RIGHT
COL_TOP
COL_TOP
COL_UP_LEFT
COL_TOP
COL_RIGHT
COL_BOTTOM
COL_TOP
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE

COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_UP_LEFT_SPIKE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_DEATH
COL_RIGHT
COL_LEFT
COL_UP_RIGHT_SPIKE
COL_UP_LEFT_SPIKE
COL_DEATH
COL_ALL
COL_LEFT
COL_DOWN_RIGHT
COL_DOWN_LEFT
";

            var rx = new System.Text.RegularExpressions.Regex(@"COL_[A-Z0-9_]+");
            var lines = mappingText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            int idx = 0;
            foreach (var raw in lines)
            {
                if (idx >= table.Length) break;
                var line = raw.Trim();
                var m = rx.Match(line);
                if (m.Success)
                {
                    if (Enum.TryParse<MetatileCollision>(m.Value, out var c))
                        table[idx] = c;
                }
                idx++;
            }
        }

        public static MetatileCollision GetCollision(byte index)
        {
            return table[index];
        }
    }
}

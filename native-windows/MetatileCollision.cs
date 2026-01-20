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
                    // Center 4x4 pixels (matching original col_death routine)
                    return InRange(localX, 0x06, 0x09) && InRange(localY, 0x06, 0x09);

                case MetatileCollision.COL_DEATH_TOP:
                    // Top portion: localY < 6, localX in center range
                    return (localY < 0x06) && InRange(localX, 0x05, 0x0a);

                case MetatileCollision.COL_DEATH_BOTTOM:
                    // Bottom portion: localY > 10, localX in center range
                    return (localY > 0x0a) && InRange(localX, 0x05, 0x0a);

                case MetatileCollision.COL_DEATH_LEFT:
                    // Left portion: localX < 6, localY in center range
                    return (localX < 0x06) && InRange(localY, 0x06, 0x09);

                case MetatileCollision.COL_DEATH_RIGHT:
                    // Right portion: localX >= 10, localY in center range
                    return (localX >= 0x0a) && InRange(localY, 0x06, 0x09);

                // Diagonal death tiles
                case MetatileCollision.COL_DEATH_BOTTOM_LEFT:
                    // Bottom-left quadrant
                    return (localX < 0x08) && (localY >= 0x08);

                case MetatileCollision.COL_DEATH_BOTTOM_RIGHT:
                    // Bottom-right quadrant
                    return (localX >= 0x08) && (localY >= 0x08);

                case MetatileCollision.COL_DEATH_TOP_LEFT:
                    // Top-left quadrant
                    return (localX < 0x08) && (localY < 0x08);

                case MetatileCollision.COL_DEATH_TOP_RIGHT:
                    // Top-right quadrant
                    return (localX >= 0x08) && (localY < 0x08);

                // Pure death spike tiles (no solid collision)
                // UP spikes = spikes pointing up from bottom, death in TOP half
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                    // Top-left quadrant has death
                    return (localY < 0x08) && (localX < 0x08);

                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                    // Top-right quadrant has death
                    return (localY < 0x08) && (localX >= 0x08);

                case MetatileCollision.COL_UP_BOTH_SPIKES:
                    // Top half with repeating spikes
                    return (localY < 0x08) && InRange(localX & 0x07, 0x02, 0x05);

                // DOWN spikes = spikes pointing down from top, death in BOTTOM half
                case MetatileCollision.COL_DOWN_LEFT_SPIKE:
                    // Bottom-left quadrant has death
                    return (localY >= 0x08) && (localX < 0x08);

                case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
                    // Bottom-right quadrant has death
                    return (localY >= 0x08) && (localX >= 0x08);

                case MetatileCollision.COL_DOWN_BOTH_SPIKES:
                    // Bottom half with repeating spikes
                    return (localY >= 0x08) && InRange(localX & 0x07, 0x02, 0x05);

                // Mixed solid/death tiles (solid collision on one side, death on the other)
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    // Top half has solid collision, bottom center has death spike (uses col_death_bottom_routine)
                    return (localY > 0x0a) && InRange(localX, 0x05, 0x0a);

                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                    // Bottom half has solid collision, top center has death spike
                    return (localY < 0x08) && InRange(localX, 0x07, 0x0a);

                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    // Bottom-left quadrant has solid collision, top-left quadrant has death
                    return (localY < 0x08) && (localX < 0x08);

                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    // Bottom-right quadrant has solid collision, top-right quadrant has death
                    return (localY < 0x08) && (localX >= 0x08);

                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    // Bottom half solid collision, top-left quadrant has death
                    return (localY < 0x08) && (localX < 0x08);

                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    // Bottom half solid collision, top-right quadrant has death
                    return (localY < 0x08) && (localX >= 0x08);

                case MetatileCollision.COL_BOTTOM_SPIKES:
                    // Bottom half solid collision, top has repeating death spikes
                    return (localY < 0x08) && InRange(localX & 0x07, 0x02, 0x05);

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
,PAL_0,

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

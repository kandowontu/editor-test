using System;

namespace CollisionTest
{
    // Minimal copy of MetatileCollision enum (values not important for this test order)
    enum MetatileCollision
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

    class Program
    {
        static bool ProvidesFloorAtColumnStatic(MetatileCollision col, int localX, out int topOffsetPx)
        {
            topOffsetPx = int.MaxValue;
            bool inLeft = (localX >= 0 && localX <= 7);
            bool inRight = (localX >= 8 && localX <= 15);

            switch (col)
            {
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                    topOffsetPx = 0; return true;
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                    topOffsetPx = 0; return true;
                case MetatileCollision.COL_BOTTOM:
                    topOffsetPx = 8; return true;
                case MetatileCollision.COL_LEFT:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_RIGHT:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    if (inRight) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_UP_LEFT:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_UP_RIGHT:
                    if (inRight) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_DOWN_LEFT:
                    if (inLeft) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_DOWN_RIGHT:
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

        static void Main(string[] args)
        {
            var typesToCheck = new[] {
                MetatileCollision.COL_ALL,
                MetatileCollision.COL_TOP,
                MetatileCollision.COL_BOTTOM,
                MetatileCollision.COL_LEFT,
                MetatileCollision.COL_RIGHT,
                MetatileCollision.COL_UP_LEFT,
                MetatileCollision.COL_UP_RIGHT,
                MetatileCollision.COL_DOWN_LEFT,
                MetatileCollision.COL_DOWN_RIGHT,
                MetatileCollision.COL_TOP_LEFT_STAIRS,
                MetatileCollision.COL_TOP_RIGHT_STAIRS,
                MetatileCollision.COL_BOTTOM_LEFT_STAIRS,
                MetatileCollision.COL_BOTTOM_RIGHT_STAIRS,
                MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT,
                MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT
            };

            Console.WriteLine("localX: 0..15 columns; value is topOffset or '.' if no floor");
            foreach (var t in typesToCheck)
            {
                Console.Write($"{t,-30}: ");
                for (int x = 0; x < 16; x++)
                {
                    if (ProvidesFloorAtColumnStatic(t, x, out int off)) Console.Write(off);
                    else Console.Write('.');
                    if (x < 15) Console.Write(',');
                }
                Console.WriteLine();
            }
        }
    }
}

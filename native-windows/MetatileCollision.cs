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
        // Values below are assigned based on the list you provided. Unknown indices default to COL_NONE.
        private static readonly MetatileCollision[] table = new MetatileCollision[256]
        {
            // Entries 0x00 .. 0x0F
            MetatileCollision.COL_NONE,              // 0x00 EMPTY
            MetatileCollision.COL_NONE,              // 0x01
            MetatileCollision.COL_ALL,               // 0x02 DOUBLE_WAVY_PLATFORM_LEFT / top platform variants
            MetatileCollision.COL_ALL,               // 0x03
            MetatileCollision.COL_ALL,               // 0x04
            MetatileCollision.COL_UP_BOTH_SPIKES,    // 0x05 SMALL_SPIKE_UPSIDEDOWN_BOTH
            MetatileCollision.COL_DEATH_BOTTOM,      // 0x06 PITSPIKES_TOP / WAVYPIT_BOTTOM_SPIKEPIT_TOP
            MetatileCollision.COL_DEATH_TOP,         // 0x07 PITSPIKES_TOP paired
            MetatileCollision.COL_DEATH_LEFT,        // 0x08 PITSPIKES_LEFT / diag spikes
            MetatileCollision.COL_DEATH_RIGHT,       // 0x09 PITSPIKES_RIGHT
            MetatileCollision.COL_FLOOR_CEIL,        // 0x0A GROUND_EDGE_TOP
            MetatileCollision.COL_FLOOR_CEIL,        // 0x0B GROUND_TOP
            MetatileCollision.COL_DEATH_BOTTOM,      // 0x0C BOTTOM_BACKGROUND_SPIKES / HALF_SPIKE_BACKGROUND
            MetatileCollision.COL_DEATH_TOP,         // 0x0D
            MetatileCollision.COL_DEATH_TOP,         // 0x0E TOP_BACKGROUND_SPIKES
            MetatileCollision.COL_DEATH_TOP,         // 0x0F

            // 0x10 .. 0x1F
            MetatileCollision.COL_ALL,               // 0x10 (various black/solid patterns)
            MetatileCollision.COL_NONE,              // 0x11
            MetatileCollision.COL_NONE,              // 0x12
            MetatileCollision.COL_ALL,               // 0x13
            MetatileCollision.COL_ALL,               // 0x14
            MetatileCollision.COL_NONE,              // 0x15
            MetatileCollision.COL_BOTTOM_LEFT_SPIKE, // 0x16 PITSPIKES
            MetatileCollision.COL_BOTTOM_RIGHT_SPIKE,// 0x17 PITSPIKES
            MetatileCollision.COL_DEATH_LEFT,        // 0x18
            MetatileCollision.COL_DEATH_RIGHT,       // 0x19
            MetatileCollision.COL_FLOOR_CEIL,        // 0x1A GROUND_EDGE_BOTTOM / GROUND variants
            MetatileCollision.COL_FLOOR_CEIL,        // 0x1B
            MetatileCollision.COL_DEATH_BOTTOM,      // 0x1C
            MetatileCollision.COL_DEATH_BOTTOM,      // 0x1D
            MetatileCollision.COL_DEATH_TOP,         // 0x1E
            MetatileCollision.COL_DEATH_TOP,         // 0x1F

            // 0x20 .. 0x2F
            MetatileCollision.COL_ALL,               // 0x20 FAKE_BLOCK / BLOCK
            MetatileCollision.COL_ALL,               // 0x21
            MetatileCollision.COL_DEATH,             // 0x22 SPIKE_UP / FAKE_SPIKE_UP
            MetatileCollision.COL_DEATH,             // 0x23
            MetatileCollision.COL_DEATH,             // 0x24 SPIKE_TOP / FAKE_SPIKE_TOP
            MetatileCollision.COL_DEATH,             // 0x25
            MetatileCollision.COL_DEATH_TOP,         // 0x26 SPIKE_LEFT
            MetatileCollision.COL_DEATH,             // 0x27
            MetatileCollision.COL_DEATH,             // 0x28 SPIKE_RIGHT
            MetatileCollision.COL_DEATH,             // 0x29
            MetatileCollision.COL_ALL,               // 0x2A
            MetatileCollision.COL_ALL,               // 0x2B
            MetatileCollision.COL_DEATH_TOP,         // 0x2C HALF_SPIKE_TOP etc.
            MetatileCollision.COL_DEATH_TOP,         // 0x2D
            MetatileCollision.COL_DEATH_RIGHT,       // 0x2E HALF_SPIKE_LEFT variant
            MetatileCollision.COL_DEATH_RIGHT,       // 0x2F

            // 0x30 .. 0x3F
            MetatileCollision.COL_ALL,               // 0x30 FAKE_BLOCK/BLOCK
            MetatileCollision.COL_ALL,               // 0x31
            MetatileCollision.COL_ALL,               // 0x32
            MetatileCollision.COL_ALL,               // 0x33
            MetatileCollision.COL_DEATH,             // 0x34 SPIKE_TOP variants
            MetatileCollision.COL_DEATH,             // 0x35
            MetatileCollision.COL_DEATH,             // 0x36
            MetatileCollision.COL_DEATH,             // 0x37
            MetatileCollision.COL_DEATH,             // 0x38
            MetatileCollision.COL_DEATH,             // 0x39
            MetatileCollision.COL_TOP,               // 0x3A PLATFORM / top slab
            MetatileCollision.COL_TOP,               // 0x3B
            MetatileCollision.COL_DEATH_BOTTOM,      // 0x3C SPIKE_HALF
            MetatileCollision.COL_DEATH_BOTTOM,      // 0x3D
            MetatileCollision.COL_DEATH_TOP,         // 0x3E
            MetatileCollision.COL_DEATH_RIGHT,       // 0x3F

            // 0x40 .. 0x4F
            MetatileCollision.COL_ALL,               // 0x40 CHECKERBOARD
            MetatileCollision.COL_ALL,               // 0x41
            MetatileCollision.COL_ALL,               // 0x42
            MetatileCollision.COL_ALL,               // 0x43
            MetatileCollision.COL_ALL,               // 0x44
            MetatileCollision.COL_ALL,               // 0x45
            MetatileCollision.COL_ALL,               // 0x46
            MetatileCollision.COL_ALL,               // 0x47
            MetatileCollision.COL_ALL,               // 0x48
            MetatileCollision.COL_ALL,               // 0x49
            MetatileCollision.COL_ALL,               // 0x4A
            MetatileCollision.COL_ALL,               // 0x4B
            MetatileCollision.COL_ALL,               // 0x4C
            MetatileCollision.COL_ALL,               // 0x4D
            MetatileCollision.COL_ALL,               // 0x4E
            MetatileCollision.COL_ALL,               // 0x4F

            // 0x50 .. 0x5F
            MetatileCollision.COL_ALL,               // 0x50
            MetatileCollision.COL_ALL,               // 0x51
            MetatileCollision.COL_ALL,               // 0x52
            MetatileCollision.COL_ALL,               // 0x53
            MetatileCollision.COL_ALL,               // 0x54
            MetatileCollision.COL_ALL,               // 0x55
            MetatileCollision.COL_ALL,               // 0x56
            MetatileCollision.COL_ALL,               // 0x57
            MetatileCollision.COL_ALL,               // 0x58
            MetatileCollision.COL_ALL,               // 0x59
            MetatileCollision.COL_ALL,               // 0x5A
            MetatileCollision.COL_ALL,               // 0x5B
            MetatileCollision.COL_ALL,               // 0x5C
            MetatileCollision.COL_ALL,               // 0x5D
            MetatileCollision.COL_ALL,               // 0x5E
            MetatileCollision.COL_NONE,              // 0x5F BLACK_NONE

            // 0x60 .. 0x6F
            MetatileCollision.COL_ALL,               // 0x60 X_FULL_OUTLINE etc.
            MetatileCollision.COL_ALL,               // 0x61
            MetatileCollision.COL_ALL,               // 0x62
            MetatileCollision.COL_ALL,               // 0x63
            MetatileCollision.COL_ALL,               // 0x64
            MetatileCollision.COL_ALL,               // 0x65
            MetatileCollision.COL_ALL,               // 0x66
            MetatileCollision.COL_ALL,               // 0x67
            MetatileCollision.COL_ALL,               // 0x68
            MetatileCollision.COL_ALL,               // 0x69
            MetatileCollision.COL_ALL,               // 0x6A CHIPPED_BLOCK
            MetatileCollision.COL_ALL,               // 0x6B
            MetatileCollision.COL_ALL,               // 0x6C BIG_SAW_MIDDLE (none)
            MetatileCollision.COL_NONE,              // 0x6D
            MetatileCollision.COL_TOP,               // 0x6E XSTEP_SPECIAL / WAVYPIT variants
            MetatileCollision.COL_NONE,              // 0x6F

            // 0x70 .. 0x7F
            MetatileCollision.COL_ALL,               // 0x70
            MetatileCollision.COL_ALL,               // 0x71
            MetatileCollision.COL_ALL,               // 0x72
            MetatileCollision.COL_ALL,               // 0x73
            MetatileCollision.COL_ALL,               // 0x74
            MetatileCollision.COL_ALL,               // 0x75
            MetatileCollision.COL_ALL,               // 0x76
            MetatileCollision.COL_ALL,               // 0x77
            MetatileCollision.COL_DEATH_TOP,         // 0x78 maybe
            MetatileCollision.COL_NONE,              // 0x79
            MetatileCollision.COL_NONE,              // 0x7A
            MetatileCollision.COL_NONE,              // 0x7B
            MetatileCollision.COL_DEATH_TOP,         // 0x7C
            MetatileCollision.COL_DEATH_TOP,         // 0x7D
            MetatileCollision.COL_DEATH,             // 0x7E TOP_WAVYPIT
            MetatileCollision.COL_DEATH,             // 0x7F

            // 0x80 .. 0x8F
            MetatileCollision.COL_NONE,              // 0x80
            MetatileCollision.COL_DEATH_LEFT,        // 0x81
            MetatileCollision.COL_DEATH_RIGHT,       // 0x82
            MetatileCollision.COL_DEATH_LEFT,        // 0x83
            MetatileCollision.COL_NONE,              // 0x84
            MetatileCollision.COL_NONE,              // 0x85
            MetatileCollision.COL_NONE,              // 0x86
            MetatileCollision.COL_NONE,              // 0x87
            MetatileCollision.COL_NONE,              // 0x88
            MetatileCollision.COL_NONE,              // 0x89
            MetatileCollision.COL_NONE,              // 0x8A
            MetatileCollision.COL_NONE,              // 0x8B
            MetatileCollision.COL_NONE,              // 0x8C
            MetatileCollision.COL_NONE,              // 0x8D
            MetatileCollision.COL_NONE,              // 0x8E
            MetatileCollision.COL_NONE,              // 0x8F

            // 0x90 .. 0x9F
            MetatileCollision.COL_NONE,              // 0x90
            MetatileCollision.COL_NONE,              // 0x91
            MetatileCollision.COL_NONE,              // 0x92
            MetatileCollision.COL_NONE,              // 0x93
            MetatileCollision.COL_NONE,              // 0x94
            MetatileCollision.COL_NONE,              // 0x95
            MetatileCollision.COL_NONE,              // 0x96
            MetatileCollision.COL_NONE,              // 0x97
            MetatileCollision.COL_NONE,              // 0x98
            MetatileCollision.COL_NONE,              // 0x99
            MetatileCollision.COL_NONE,              // 0x9A
            MetatileCollision.COL_NONE,              // 0x9B
            MetatileCollision.COL_NONE,              // 0x9C
            MetatileCollision.COL_NONE,              // 0x9D
            MetatileCollision.COL_NONE,              // 0x9E
            MetatileCollision.COL_NONE,              // 0x9F

            // 0xA0 .. 0xAF
            MetatileCollision.COL_NONE,              // 0xA0
            MetatileCollision.COL_NONE,              // 0xA1
            MetatileCollision.COL_NONE,              // 0xA2
            MetatileCollision.COL_NONE,              // 0xA3
            MetatileCollision.COL_NONE,              // 0xA4
            MetatileCollision.COL_NONE,              // 0xA5
            MetatileCollision.COL_NONE,              // 0xA6
            MetatileCollision.COL_NONE,              // 0xA7
            MetatileCollision.COL_NONE,              // 0xA8
            MetatileCollision.COL_NONE,              // 0xA9
            MetatileCollision.COL_NONE,              // 0xAA
            MetatileCollision.COL_NONE,              // 0xAB
            MetatileCollision.COL_NONE,              // 0xAC
            MetatileCollision.COL_NONE,              // 0xAD
            MetatileCollision.COL_NONE,              // 0xAE
            MetatileCollision.COL_NONE,              // 0xAF

            // 0xB0 .. 0xBF
            MetatileCollision.COL_NONE,              // 0xB0
            MetatileCollision.COL_NONE,              // 0xB1
            MetatileCollision.COL_NONE,              // 0xB2
            MetatileCollision.COL_NONE,              // 0xB3
            MetatileCollision.COL_NONE,              // 0xB4
            MetatileCollision.COL_NONE,              // 0xB5
            MetatileCollision.COL_NONE,              // 0xB6
            MetatileCollision.COL_NONE,              // 0xB7
            MetatileCollision.COL_NONE,              // 0xB8
            MetatileCollision.COL_NONE,              // 0xB9
            MetatileCollision.COL_NONE,              // 0xBA
            MetatileCollision.COL_NONE,              // 0xBB
            MetatileCollision.COL_NONE,              // 0xBC
            MetatileCollision.COL_NONE,              // 0xBD
            MetatileCollision.COL_NONE,              // 0xBE
            MetatileCollision.COL_NONE,              // 0xBF

            // 0xC0 .. 0xCF
            MetatileCollision.COL_DEATH,             // 0xC0 SMALL_SAW
            MetatileCollision.COL_NONE,              // 0xC1
            MetatileCollision.COL_DEATH_BOTTOM,      // 0xC2
            MetatileCollision.COL_DEATH_BOTTOM,      // 0xC3
            MetatileCollision.COL_NONE,              // 0xC4
            MetatileCollision.COL_NONE,              // 0xC5
            MetatileCollision.COL_NONE,              // 0xC6
            MetatileCollision.COL_NONE,              // 0xC7
            MetatileCollision.COL_NONE,              // 0xC8
            MetatileCollision.COL_NONE,              // 0xC9
            MetatileCollision.COL_NONE,              // 0xCA
            MetatileCollision.COL_NONE,              // 0xCB
            MetatileCollision.COL_DEATH_TOP,         // 0xCC
            MetatileCollision.COL_DEATH_TOP,         // 0xCD
            MetatileCollision.COL_DEATH_TOP,         // 0xCE
            MetatileCollision.COL_NONE,              // 0xCF

            // 0xD0 .. 0xDF
            MetatileCollision.COL_NONE,              // 0xD0
            MetatileCollision.COL_NONE,              // 0xD1
            MetatileCollision.COL_NONE,              // 0xD2
            MetatileCollision.COL_NONE,              // 0xD3
            MetatileCollision.COL_NONE,              // 0xD4
            MetatileCollision.COL_NONE,              // 0xD5
            MetatileCollision.COL_NONE,              // 0xD6
            MetatileCollision.COL_NONE,              // 0xD7
            MetatileCollision.COL_NONE,              // 0xD8
            MetatileCollision.COL_NONE,              // 0xD9
            MetatileCollision.COL_NONE,              // 0xDA
            MetatileCollision.COL_NONE,              // 0xDB
            MetatileCollision.COL_NONE,              // 0xDC
            MetatileCollision.COL_NONE,              // 0xDD
            MetatileCollision.COL_NONE,              // 0xDE
            MetatileCollision.COL_NONE,              // 0xDF

            // 0xE0 .. 0xEF
            MetatileCollision.COL_DEATH_BOTTOM,      // 0xE0 MED_SAW_TOP_LEFT
            MetatileCollision.COL_DEATH_BOTTOM,      // 0xE1
            MetatileCollision.COL_DEATH_BOTTOM,      // 0xE2 MED_SAW_TOP_RIGHT
            MetatileCollision.COL_DEATH_BOTTOM,      // 0xE3
            MetatileCollision.COL_DEATH_BOTTOM,      // 0xE4
            MetatileCollision.COL_DEATH_BOTTOM,      // 0xE5
            MetatileCollision.COL_DEATH_TOP,         // 0xE6
            MetatileCollision.COL_DEATH_TOP,         // 0xE7
            MetatileCollision.COL_DEATH_TOP,         // 0xE8
            MetatileCollision.COL_DEATH_TOP,         // 0xE9
            MetatileCollision.COL_DEATH_TOP,         // 0xEA
            MetatileCollision.COL_DEATH_TOP,         // 0xEB
            MetatileCollision.COL_DEATH_TOP,         // 0xEC
            MetatileCollision.COL_DEATH_TOP,         // 0xED
            MetatileCollision.COL_DEATH_TOP,         // 0xEE
            MetatileCollision.COL_DEATH_TOP,         // 0xEF

            // 0xF0 .. 0xFF
            MetatileCollision.COL_NONE,              // 0xF0
            MetatileCollision.COL_NONE,              // 0xF1
            MetatileCollision.COL_NONE,              // 0xF2
            MetatileCollision.COL_NONE,              // 0xF3
            MetatileCollision.COL_NONE,              // 0xF4
            MetatileCollision.COL_NONE,              // 0xF5
            MetatileCollision.COL_NONE,              // 0xF6
            MetatileCollision.COL_NONE,              // 0xF7
            MetatileCollision.COL_NONE,              // 0xF8
            MetatileCollision.COL_NONE,              // 0xF9
            MetatileCollision.COL_NONE,              // 0xFA
            MetatileCollision.COL_NONE,              // 0xFB
            MetatileCollision.COL_NONE,              // 0xFC
            MetatileCollision.COL_NONE,              // 0xFD
            MetatileCollision.COL_NONE,              // 0xFE
            MetatileCollision.COL_NONE               // 0xFF
        };

        public static MetatileCollision GetCollision(byte index)
        {
            return table[index];
        }
    }
}

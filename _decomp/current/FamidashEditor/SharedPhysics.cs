using System;

namespace FamidashEditor;

internal static class SharedPhysics
{
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

	internal static Action<string>? BallEjectDiagLog;

	internal static Action<string>? SlopeDiagLog;

	internal const int TILE = 16;

	internal const int CUBE_GRAVITY_NORMAL = 107;

	internal const int CUBE_GRAVITY_MINI = 111;

	internal const int CUBE_MAX_FALLSPEED = 1536;

	internal const int JUMP_VEL_NORMAL = -1424;

	internal const int JUMP_VEL_MINI = -1232;

	internal const int CUBE_HITBOX_W = 15;

	internal const int CUBE_HITBOX_H = 15;

	internal const int MINI_CUBE_HITBOX_W = 8;

	internal const int MINI_CUBE_HITBOX_H = 7;

	internal const int CUBE_SPEED_X05 = 571;

	internal const int CUBE_SPEED_X1 = 708;

	internal const int CUBE_SPEED_X2 = 881;

	internal const int CUBE_SPEED_X3 = 1065;

	internal const int CUBE_SPEED_X4 = 1310;

	internal const int CUBE_SPEED_SLOW = 366;

	internal const int PAD_HEIGHT_BLUE_NORMAL = 928;

	internal const int PAD_HEIGHT_BLUE_MINI = 928;

	internal const int ORB_BALL_HEIGHT_BLUE_NORMAL = 416;

	internal const int ORB_BALL_HEIGHT_BLUE_MINI = 416;

	internal const int GAMEMODE_PORTAL_GEOM_SID = 0;

	internal static readonly int[][] PadOrbHeights = new int[9][]
	{
		new int[8] { 1424, 1104, 1040, 944, 1424, 1088, 0, 928 },
		new int[8] { 1984, 960, 1264, 816, 2224, 1280, 0, 1104 },
		new int[8] { 976, 512, 816, 544, 1104, 848, 0, 720 },
		new int[8] { 1296, 624, 864, 592, 1360, 848, 0, 864 },
		new int[8] { 1872, 1488, 1360, 1296, 1872, 1280, 0, 1232 },
		new int[8] { 1424, 1424, 1488, 1424, 1424, 1424, 0, 1488 },
		new int[8] { -2448, -2448, -2416, -2448, -2448, -2448, 0, -2416 },
		new int[8] { 1344, 1344, 1138, 1200, 1904, 1200, 0, 1138 },
		new int[8] { 2544, 1568, 1584, 1024, 2640, 1680, 0, 1632 }
	};

	internal static readonly int[][] PadOrbHeights_Mini = new int[9][]
	{
		new int[8] { 1232, 1184, 1104, 976, 1136, 848, 0, 672 },
		new int[8] { 1664, 1072, 1232, 928, 1840, 1024, 0, 832 },
		new int[8] { 848, 480, 848, 432, 880, 560, 0, 496 },
		new int[8] { 1008, 480, 912, 336, 848, 848, 0, 544 },
		new int[8] { 1616, 1648, 1280, 1360, 1616, 1136, 0, 848 },
		new int[8] { 1424, 1424, 1376, 1424, 1424, 1424, 0, 1376 },
		new int[8] { -2448, -2448, -2416, -2448, -2448, -2448, 0, -2416 },
		new int[8] { 1344, 1344, 1138, 1200, 1904, 1200, 0, 1138 },
		new int[8] { 2096, 1728, 1456, 1360, 2256, 1360, 0, 928 }
	};

	internal static readonly short[][][] SpriteGamemodeAdjustHeights = new short[8][][]
	{
		new short[9][]
		{
			new short[8] { -1709, -1325, -1248, -1133, -1709, -1306, 0, -1114 },
			new short[8] { -2381, -1152, -1517, -979, -2669, -1536, 0, -1325 },
			new short[8] { -1171, -614, -979, -653, -1325, -1018, 0, -864 },
			new short[8] { -1555, -749, -1037, -710, -1632, -1018, 0, -1037 },
			new short[8] { -2246, -1786, -1632, -1555, -2246, -1536, 0, -1478 },
			new short[8] { -1709, -1709, -1786, -1709, -1709, -1709, 0, -1786 },
			new short[8] { 2938, 2938, 2899, 2938, 2938, 2938, 0, 2899 },
			new short[8] { -1613, -1613, -1366, -1440, -2285, -1440, 0, -1366 },
			new short[8] { -3053, -1882, -1901, -1229, -3168, -2016, 0, -1958 }
		},
		new short[9][]
		{
			new short[8] { 1709, 1325, 1248, 1133, 1709, 1306, 0, 1114 },
			new short[8] { 2381, 1152, 1517, 979, 2669, 1536, 0, 1325 },
			new short[8] { 1171, 614, 979, 653, 1325, 1018, 0, 864 },
			new short[8] { 1555, 749, 1037, 710, 1632, 1018, 0, 1037 },
			new short[8] { 2246, 1786, 1632, 1555, 2246, 1536, 0, 1478 },
			new short[8] { 1709, 1709, 1786, 1709, 1709, 1709, 0, 1786 },
			new short[8] { -2938, -2938, -2899, -2938, -2938, -2938, 0, -2899 },
			new short[8] { 1613, 1613, 1366, 1440, 2285, 1440, 0, 1366 },
			new short[8] { 3053, 1882, 1901, 1229, 3168, 2016, 0, 1958 }
		},
		new short[9][]
		{
			new short[8] { -1478, -1421, -1325, -1171, -1363, -1018, 0, -806 },
			new short[8] { -1997, -1286, -1478, -1114, -2208, -1229, 0, -998 },
			new short[8] { -1018, -576, -1018, -518, -1056, -672, 0, -595 },
			new short[8] { -1210, -576, -1094, -403, -1018, -1018, 0, -653 },
			new short[8] { -1939, -1978, -1536, -1632, -1939, -1363, 0, -1018 },
			new short[8] { -1709, -1709, -1651, -1709, -1709, -1709, 0, -1651 },
			new short[8] { 2938, 2938, 2899, 2938, 2938, 2938, 0, 2899 },
			new short[8] { -1613, -1613, -1366, -1440, -2285, -1440, 0, -1366 },
			new short[8] { -2515, -2074, -1747, -1632, -2707, -1632, 0, -1114 }
		},
		new short[9][]
		{
			new short[8] { 1478, 1421, 1325, 1171, 1363, 1018, 0, 806 },
			new short[8] { 1997, 1286, 1478, 1114, 2208, 1229, 0, 998 },
			new short[8] { 1018, 576, 1018, 518, 1056, 672, 0, 595 },
			new short[8] { 1210, 576, 1094, 403, 1018, 1018, 0, 653 },
			new short[8] { 1939, 1978, 1536, 1632, 1939, 1363, 0, 1018 },
			new short[8] { 1709, 1709, 1651, 1709, 1709, 1709, 0, 1651 },
			new short[8] { -2938, -2938, -2899, -2938, -2938, -2938, 0, -2899 },
			new short[8] { 1613, 1613, 1366, 1440, 2285, 1440, 0, 1366 },
			new short[8] { 2515, 2074, 1747, 1632, 2707, 1632, 0, 1114 }
		},
		new short[9][]
		{
			new short[8] { -1424, -1104, -1040, -944, -1424, -1088, 0, -928 },
			new short[8] { -1984, -960, -1264, -816, -2224, -1280, 0, -1104 },
			new short[8] { -976, -512, -816, -544, -1104, -848, 0, -720 },
			new short[8] { -1296, -624, -864, -592, -1360, -848, 0, -864 },
			new short[8] { -1872, -1488, -1360, -1296, -1872, -1280, 0, -1232 },
			new short[8] { -1424, -1424, -1488, -1424, -1424, -1424, 0, -1488 },
			new short[8] { 2448, 2448, 2416, 2448, 2448, 2448, 0, 2416 },
			new short[8] { -1344, -1344, -1138, -1200, -1904, -1200, 0, -1138 },
			new short[8] { -2544, -1568, -1584, -1024, -2640, -1680, 0, -1632 }
		},
		new short[9][]
		{
			new short[8] { 1424, 1104, 1040, 944, 1424, 1088, 0, 928 },
			new short[8] { 1984, 960, 1264, 816, 2224, 1280, 0, 1104 },
			new short[8] { 976, 512, 816, 544, 1104, 848, 0, 720 },
			new short[8] { 1296, 624, 864, 592, 1360, 848, 0, 864 },
			new short[8] { 1872, 1488, 1360, 1296, 1872, 1280, 0, 1232 },
			new short[8] { 1424, 1424, 1488, 1424, 1424, 1424, 0, 1488 },
			new short[8] { -2448, -2448, -2416, -2448, -2448, -2448, 0, -2416 },
			new short[8] { 1344, 1344, 1138, 1200, 1904, 1200, 0, 1138 },
			new short[8] { 2544, 1568, 1584, 1024, 2640, 1680, 0, 1632 }
		},
		new short[9][]
		{
			new short[8] { -1232, -1184, -1104, -976, -1136, -848, 0, -672 },
			new short[8] { -1664, -1072, -1232, -928, -1840, -1024, 0, -832 },
			new short[8] { -848, -480, -848, -432, -880, -560, 0, -496 },
			new short[8] { -1008, -480, -912, -336, -848, -848, 0, -544 },
			new short[8] { -1616, -1648, -1280, -1360, -1616, -1136, 0, -848 },
			new short[8] { -1424, -1424, -1376, -1424, -1424, -1424, 0, -1376 },
			new short[8] { 2448, 2448, 2416, 2448, 2448, 2448, 0, 2416 },
			new short[8] { -1344, -1344, -1138, -1200, -1904, -1200, 0, -1138 },
			new short[8] { -2096, -1728, -1456, -1360, -2256, -1360, 0, -928 }
		},
		new short[9][]
		{
			new short[8] { 1232, 1184, 1104, 976, 1136, 848, 0, 672 },
			new short[8] { 1664, 1072, 1232, 928, 1840, 1024, 0, 832 },
			new short[8] { 848, 480, 848, 432, 880, 560, 0, 496 },
			new short[8] { 1008, 480, 912, 336, 848, 848, 0, 544 },
			new short[8] { 1616, 1648, 1280, 1360, 1616, 1136, 0, 848 },
			new short[8] { 1424, 1424, 1376, 1424, 1424, 1424, 0, 1376 },
			new short[8] { -2448, -2448, -2416, -2448, -2448, -2448, 0, -2416 },
			new short[8] { 1344, 1344, 1138, 1200, 1904, 1200, 0, 1138 },
			new short[8] { 2096, 1728, 1456, 1360, 2256, 1360, 0, 928 }
		}
	};

	internal const int SGAH_YELLOW_ORB = 0;

	internal const int SGAH_YELLOW_PAD = 8;

	internal const int SGAH_PINK_ORB = 16;

	internal const int SGAH_PINK_PAD = 24;

	internal const int SGAH_RED_ORB = 32;

	internal const int SGAH_YELLOW_BIGGER = 40;

	internal const int SGAH_BLACK_ORB = 48;

	internal const int SGAH_YELLOW_SMALLER = 56;

	internal const int SGAH_RED_PAD = 64;

	internal static readonly int[] sprite_widths = new int[256]
	{
		16, 16, 16, 16, 16, 16, 16, 16, 14, 14,
		15, 16, 15, 15, 15, 16, 40, 40, 40, 40,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 15, 15, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 14, 14, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 15, 15, 16, 16, 15, 15, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 14, 48, 48, 48, 48, 16, 16, 16, 16,
		8, 16, 16, 16, 16, 16, 16, 48, 48, 48,
		48, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 8,
		27, 16, 16, 14, 14, 16
	};

	internal static readonly int[] sprite_heights = new int[256]
	{
		52, 52, 52, 52, 52, 18, 18, 16, 40, 40,
		3, 18, 3, 3, 3, 16, 14, 14, 14, 14,
		36, 36, 36, 52, 52, 52, 16, 16, 16, 16,
		16, 18, 36, 36, 52, 52, 52, 3, 3, 18,
		18, 18, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 18, 18,
		18, 40, 40, 16, 16, 52, 18, 18, 48, 16,
		18, 18, 3, 3, 18, 18, 3, 3, 52, 16,
		16, 18, 18, 18, 18, 52, 52, 52, 52, 52,
		52, 2, 16, 16, 16, 16, 52, 52, 52, 32,
		8, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 18, 18, 18, 18, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 0, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 0, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 0, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 0, 0, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
		31, 16, 16, 3, 3, 0
	};

	internal static readonly int[] sprite_x_offset = new int[256]
	{
		0, 0, 0, 0, 0, 0, 0, 0, 1, 1,
		0, 0, 0, 0, 0, 0, 4, 4, 4, 4,
		0, 0, 0, 0, 8, 8, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, -8, 0, 0, 0, 0, 0, 0, 0,
		0, 1, 1, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		4, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 8,
		-7, 0, 0, 0, 0, 0
	};

	internal static readonly int[] sprite_y_offset = new int[256]
	{
		-2, -2, -2, -2, -2, -1, -1, 0, 4, 4,
		13, -1, 0, 13, 0, 0, 1, 1, 1, 1,
		-2, -2, -2, -2, -2, -2, 0, 0, 0, 0,
		0, -1, -2, -2, -2, -2, -2, 13, 0, -1,
		-1, -1, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, -1, -1,
		-1, 4, 4, 0, 0, -2, 0, -1, -1, 0,
		-1, -1, 13, 0, -1, -1, 13, 0, -2, 0,
		0, -1, -1, -1, -1, -2, -2, -2, -2, -2,
		-2, 0, 0, 0, 0, 0, -2, -2, -2, 0,
		4, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, -1, -1, -1, -1, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
		-7, 0, 0, 13, 2, 0
	};

	internal const int SLOPE_22DEG = 2;

	internal const int SLOPE_45DEG = 1;

	internal const int SLOPE_66DEG = 3;

	internal const int SLOPE_RISING = 4;

	internal const int SLOPE_UD = 8;

	internal const int SLOPE_DEGREES_MASK = 3;

	internal static readonly short[] EXIT_SLOPE_BALL_22 = new short[8] { -96, 96, -96, 96, -80, 80, -80, 80 };

	internal static readonly short[] EXIT_SLOPE_BALL_66 = new short[8] { 365, -365, 365, -365, 432, -432, 432, -432 };

	internal static readonly short[] EXIT_SLOPE_CUBE_22 = new short[8] { -307, 307, -307, 307, -256, 256, -256, 256 };

	internal static int BallGravity(bool mini)
	{
		return mini ? 87 : 71;
	}

	internal static int BallSwitchVel(bool mini)
	{
		return mini ? 288 : 512;
	}

	internal static int BallMaxFallSpeed(bool mini)
	{
		return mini ? 1280 : 1536;
	}

	internal static int ShipGravityBase(bool mini)
	{
		return mini ? 49 : 42;
	}

	internal static int ShipGravityAfterHold(bool mini)
	{
		return mini ? 59 : 50;
	}

	internal static int ShipGravityHoldFall(bool mini)
	{
		return mini ? 62 : 52;
	}

	internal static int ShipGravity(bool mini)
	{
		return mini ? 39 : 34;
	}

	internal static int ShipMaxFallSpeed(bool mini)
	{
		return 727;
	}

	internal static int ShipMaxFallSpeedHold(bool mini)
	{
		return 909;
	}

	internal static int UfoGravity(bool mini)
	{
		return 50;
	}

	internal static int UfoJumpVel(bool mini)
	{
		return mini ? 720 : 816;
	}

	internal static int UfoMaxFallSpeed(bool mini)
	{
		return mini ? 848 : 800;
	}

	internal static int GetCubeGravity(bool mini)
	{
		return mini ? 111 : 107;
	}

	internal static int GetCubeJumpVel(bool mini)
	{
		return mini ? (-1232) : (-1424);
	}

	internal static int GetCubeHitboxW(bool mini)
	{
		return mini ? 8 : 15;
	}

	internal static int GetCubeHitboxH(bool mini)
	{
		return mini ? 7 : 15;
	}

	internal static int GetCubeHitboxOffsetY(bool mini, bool gravityUp)
	{
		return GetMiniCenterOffsetY(mini);
	}

	internal static int GetMiniCenterOffsetY(bool mini)
	{
		return mini ? (16 - GetCubeHitboxH(mini: true) >> 1) : 0;
	}

	internal static int GetHitboxOffsetY(int gameMode, bool mini, bool gravFlipped)
	{
		if (!mini)
		{
			return 0;
		}
		return GetMiniCenterOffsetY(mini: true);
	}

	internal static int BluePadVel(bool mini)
	{
		return mini ? 928 : 928;
	}

	internal static int BlueOrbVel(bool mini, int gameMode)
	{
		return (gameMode != 2) ? (mini ? 928 : 928) : (mini ? 416 : 416);
	}

	internal static int SpeedUiIndexToFixed(int uiIndex)
	{
		if (1 == 0)
		{
		}
		int result = uiIndex switch
		{
			0 => 571, 
			1 => 708, 
			2 => 881, 
			3 => 1065, 
			4 => 1310, 
			_ => 708, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	internal static int SpriteIdToSpeedFixed(int sid)
	{
		if (1 == 0)
		{
		}
		int result = sid switch
		{
			109 => 366, 
			20 => 571, 
			21 => 708, 
			22 => 881, 
			32 => 1065, 
			33 => 1310, 
			_ => -1, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	internal static int SpriteIdToGameMode(int sid)
	{
		if (1 == 0)
		{
		}
		int result = sid switch
		{
			0 => 0, 
			1 => 1, 
			2 => 2, 
			3 => 3, 
			4 => 4, 
			23 => 5, 
			36 => 6, 
			75 => 7, 
			88 => 8, 
			106 => 9, 
			107 => 10, 
			108 => 11, 
			_ => -1, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	internal static bool IsSpeedPortal(int sid)
	{
		return SpriteIdToSpeedFixed(sid) >= 0;
	}

	internal static bool IsGameModePortal(int sid)
	{
		return SpriteIdToGameMode(sid) >= 0;
	}

	internal static bool IsGravityPortal(int sid)
	{
		return sid == 8 || sid == 9 || sid == 16 || sid == 17 || sid == 18 || sid == 19 || sid == 251 || sid == 252;
	}

	internal static bool IsReverseGravity(int sid)
	{
		return sid == 9 || sid == 18 || sid == 19 || sid == 251;
	}

	internal static bool IsMiniGrowthPortal(int sid)
	{
		return sid == 24 || sid == 25;
	}

	internal static bool IsEndLevel(int sid)
	{
		return sid == 15;
	}

	internal static bool IsYellowPad(int sid)
	{
		return sid == 10 || sid == 12;
	}

	internal static bool IsPinkPad(int sid)
	{
		return sid == 37 || sid == 38;
	}

	internal static bool IsRedPad(int sid)
	{
		return sid == 82 || sid == 83;
	}

	internal static bool IsBluePad(int sid)
	{
		return sid == 13 || sid == 14 || sid == 253 || sid == 254;
	}

	internal static bool IsGreenPad(int sid)
	{
		return sid == 101;
	}

	internal static bool IsAnyPad(int sid)
	{
		return IsYellowPad(sid) || IsPinkPad(sid) || IsRedPad(sid) || IsBluePad(sid) || IsGreenPad(sid);
	}

	internal static bool IsYellowOrb(int sid)
	{
		return sid == 11;
	}

	internal static bool IsYellowOrbBigger(int sid)
	{
		return sid == 31;
	}

	internal static bool IsYellowOrbSmaller(int sid)
	{
		return sid == 41;
	}

	internal static bool IsPinkOrb(int sid)
	{
		return sid == 6;
	}

	internal static bool IsRedOrb(int sid)
	{
		return sid == 40;
	}

	internal static bool IsBlueOrb(int sid)
	{
		return sid == 5 || sid == 123;
	}

	internal static bool IsGreenOrb(int sid)
	{
		return sid == 39 || sid == 124;
	}

	internal static bool IsBlackOrb(int sid)
	{
		return sid == 68;
	}

	internal static bool IsWhiteOrb(int sid)
	{
		return sid == 122;
	}

	internal static bool IsVelocityOrb(int sid)
	{
		return IsYellowOrb(sid) || IsYellowOrbBigger(sid) || IsYellowOrbSmaller(sid) || IsPinkOrb(sid) || IsRedOrb(sid) || IsBlackOrb(sid);
	}

	internal static bool IsGravityOrb(int sid)
	{
		return IsBlueOrb(sid) || IsGreenOrb(sid);
	}

	internal static bool IsOrbSprite(int sid)
	{
		return IsVelocityOrb(sid) || IsGravityOrb(sid) || IsWhiteOrb(sid);
	}

	internal static bool IsCoinSprite(int sid)
	{
		return sid == 7 || sid == 26 || sid == 27;
	}

	internal static bool IsMiniCoinSprite(int sid)
	{
		return sid == 110;
	}

	internal static int NormalizePortalGeometrySid(int sid)
	{
		int num = sid & 0xFF;
		if (IsGameModePortal(num))
		{
			return 0;
		}
		return num;
	}

	internal static bool IsDashOrb(int sid)
	{
		return sid == 69 || sid == 70 || sid == 76 || sid == 77 || sid == 80 || sid == 81 || sid == 91 || sid == 92 || sid == 93 || sid == 94;
	}

	internal static bool IsGravityDashOrb(int sid)
	{
		return sid == 70 || sid == 77 || sid == 81 || sid == 92 || sid == 94;
	}

	internal static int DashOrbMode(int sid)
	{
		if (sid == 69 || sid == 70)
		{
			return 1;
		}
		if (sid == 76 || sid == 77)
		{
			return 2;
		}
		if (sid == 80 || sid == 81)
		{
			return 3;
		}
		if (sid == 91 || sid == 92)
		{
			return 4;
		}
		if (sid == 93 || sid == 94)
		{
			return 5;
		}
		return 0;
	}

	internal static bool IsSpiderOrb(int sid)
	{
		return sid == 84 || sid == 85;
	}

	internal static bool IsSpiderPad(int sid)
	{
		return sid == 86 || sid == 87;
	}

	internal static bool IsTeleportPortalEntrance(int sid)
	{
		return sid == 78 || sid == 102 || sid == 104 || sid == 117 || sid == 119;
	}

	internal static bool IsTeleportPortalExit(int sid)
	{
		return sid == 79 || sid == 103 || sid == 105 || sid == 118 || sid == 120;
	}

	internal static bool IsVerticalTeleportEntrance(int sid)
	{
		return sid == 78;
	}

	internal static bool IsBottomRowTeleportExit(int sid)
	{
		return sid == 103 || sid == 118;
	}

	internal static int GetPadOrbModeCol(int gameMode)
	{
		return gameMode switch
		{
			8 => 0, 
			9 => 7, 
			11 => 0, 
			_ => gameMode, 
		};
	}

	internal static int GetPadOrbVel(int row, bool mini, int gameMode)
	{
		int padOrbModeCol = GetPadOrbModeCol(gameMode);
		return mini ? PadOrbHeights_Mini[row][padOrbModeCol] : PadOrbHeights[row][padOrbModeCol];
	}

	internal static int SpriteGamemodeYAdjust(int cpTableIdx, int gamemode, int tableOffset, bool retroMode = false)
	{
		int num = gamemode;
		switch (num)
		{
		case 9:
			num = 7;
			break;
		case 10:
			num = 6;
			break;
		case 11:
			num = 0;
			break;
		}
		int num2 = (((!retroMode || num != 4) && num != 8) ? (num | tableOffset) : tableOffset);
		int num3 = num2 >> 3;
		int num4 = num2 & 7;
		return SpriteGamemodeAdjustHeights[cpTableIdx & 7][num3][num4];
	}

	internal static (int left, int top, int right, int bottom) GetCollisionBounds(MetatileCollision col)
	{
		switch (col)
		{
		case MetatileCollision.COL_ALL:
		case MetatileCollision.COL_FLOOR_CEIL:
		case MetatileCollision.COL_NO_SIDE:
			return (left: 0, top: 0, right: 16, bottom: 16);
		case MetatileCollision.COL_TOP:
		case MetatileCollision.COL_TOP_CENTER_SPIKE:
			return (left: 0, top: 0, right: 16, bottom: 8);
		case MetatileCollision.COL_BOTTOM:
		case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
		case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
		case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
		case MetatileCollision.COL_BOTTOM_SPIKES:
			return (left: 0, top: 8, right: 16, bottom: 16);
		case MetatileCollision.COL_LEFT:
			return (left: 0, top: 0, right: 8, bottom: 16);
		case MetatileCollision.COL_RIGHT:
			return (left: 8, top: 0, right: 16, bottom: 16);
		case MetatileCollision.COL_UP_LEFT:
			return (left: 0, top: 0, right: 8, bottom: 8);
		case MetatileCollision.COL_UP_RIGHT:
			return (left: 8, top: 0, right: 16, bottom: 8);
		case MetatileCollision.COL_DOWN_LEFT:
		case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
			return (left: 0, top: 8, right: 8, bottom: 16);
		case MetatileCollision.COL_DOWN_RIGHT:
		case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
			return (left: 8, top: 8, right: 16, bottom: 16);
		case MetatileCollision.COL_TOP_LEFT_STAIRS:
		case MetatileCollision.COL_TOP_RIGHT_STAIRS:
		case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
		case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
		case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
		case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
			return (left: 0, top: 0, right: 16, bottom: 16);
		default:
			return (left: 16, top: 16, right: 0, bottom: 0);
		}
	}

	internal static bool ProvidesFloorAtColumn(MetatileCollision col, int localX)
	{
		int topOffsetPx;
		return ProvidesFloorAtColumn(col, localX, out topOffsetPx);
	}

	internal static bool ProvidesFloorAtColumn(MetatileCollision col, int localX, out int topOffsetPx)
	{
		topOffsetPx = int.MaxValue;
		bool flag = localX >= 0 && localX <= 7;
		bool flag2 = localX >= 8 && localX <= 15;
		switch (col)
		{
		case MetatileCollision.COL_ALL:
		case MetatileCollision.COL_FLOOR_CEIL:
		case MetatileCollision.COL_NO_SIDE:
			topOffsetPx = 0;
			return true;
		case MetatileCollision.COL_TOP:
		case MetatileCollision.COL_TOP_CENTER_SPIKE:
		case MetatileCollision.COL_TOP_LEFT_STAIRS:
		case MetatileCollision.COL_TOP_RIGHT_STAIRS:
			topOffsetPx = 0;
			return true;
		case MetatileCollision.COL_BOTTOM:
		case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
		case MetatileCollision.COL_BOTTOM_SPIKES:
			topOffsetPx = 8;
			return true;
		case MetatileCollision.COL_LEFT:
		case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
			if (flag)
			{
				topOffsetPx = 0;
				return true;
			}
			break;
		case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
			if (flag)
			{
				topOffsetPx = 8;
				return true;
			}
			break;
		case MetatileCollision.COL_RIGHT:
		case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
			if (flag2)
			{
				topOffsetPx = 0;
				return true;
			}
			break;
		case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
			if (flag2)
			{
				topOffsetPx = 8;
				return true;
			}
			break;
		case MetatileCollision.COL_UP_LEFT:
			if (flag)
			{
				topOffsetPx = 0;
				return true;
			}
			break;
		case MetatileCollision.COL_UP_RIGHT:
			if (flag2)
			{
				topOffsetPx = 0;
				return true;
			}
			break;
		case MetatileCollision.COL_DOWN_LEFT:
		case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
			if (flag)
			{
				topOffsetPx = 8;
				return true;
			}
			break;
		case MetatileCollision.COL_DOWN_RIGHT:
		case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
			if (flag2)
			{
				topOffsetPx = 8;
				return true;
			}
			break;
		case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
			if (flag)
			{
				topOffsetPx = 0;
				return true;
			}
			if (flag2)
			{
				topOffsetPx = 8;
				return true;
			}
			break;
		case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
			if (flag2)
			{
				topOffsetPx = 0;
				return true;
			}
			if (flag)
			{
				topOffsetPx = 8;
				return true;
			}
			break;
		}
		if (col == MetatileCollision.COL_BOTTOM_LEFT_STAIRS)
		{
			if (flag)
			{
				topOffsetPx = 0;
				return true;
			}
			if (flag2)
			{
				topOffsetPx = 8;
				return true;
			}
		}
		if (col == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS)
		{
			if (flag2)
			{
				topOffsetPx = 0;
				return true;
			}
			if (flag)
			{
				topOffsetPx = 8;
				return true;
			}
		}
		return false;
	}

	internal static bool IsSlopeTile(MetatileCollision col)
	{
		return col >= MetatileCollision.COL_SLOPE_RD45 && col <= MetatileCollision.COL_SLOPE_LU66_TOP;
	}

	internal static bool IsMiniBlockType(MetatileCollision col)
	{
		if ((uint)(col - 6) <= 3u || (uint)(col - 32) <= 1u)
		{
			return true;
		}
		return false;
	}

	internal static bool IsStairsType(MetatileCollision col)
	{
		return col == MetatileCollision.COL_TOP_LEFT_STAIRS || col == MetatileCollision.COL_TOP_RIGHT_STAIRS || col == MetatileCollision.COL_BOTTOM_LEFT_STAIRS || col == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS;
	}

	internal static bool IsMiniBlockFloorHit(MetatileCollision col, int localX, int localY)
	{
		switch (col)
		{
		case MetatileCollision.COL_UP_LEFT:
			return localY < 8 && localX < 8;
		case MetatileCollision.COL_UP_RIGHT:
			return localY < 8 && localX >= 8;
		case MetatileCollision.COL_DOWN_LEFT:
		case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
			return localY >= 8 && localX < 8;
		case MetatileCollision.COL_DOWN_RIGHT:
		case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
			return localY >= 8 && localX >= 8;
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
		case MetatileCollision.COL_DOWN_RIGHT:
		case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
		case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
			return 8;
		default:
			return 0;
		}
	}

	internal static bool TileOccupiesPixel(MetatileCollision col, int localX, int localY)
	{
		if ((uint)(col - 11) <= 12u || (uint)(col - 26) <= 5u)
		{
			return false;
		}
		bool flag = localX >= 0 && localX <= 7;
		bool flag2 = localX >= 8 && localX <= 15;
		switch (col)
		{
		case MetatileCollision.COL_TOP:
		case MetatileCollision.COL_TOP_CENTER_SPIKE:
			return localY <= 7;
		case MetatileCollision.COL_TOP_LEFT_STAIRS:
			return localY <= 7 || localX <= 7;
		case MetatileCollision.COL_TOP_RIGHT_STAIRS:
			return localY <= 7 || localX >= 8;
		case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
			return localX <= 7 || (localX >= 8 && localY >= 8);
		case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
			return localX >= 8 || (localX <= 7 && localY >= 8);
		case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
			return localY >= 8;
		case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
			return localY >= 8;
		case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
			return localX < 8 && localY >= 8;
		case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
			return localX >= 8 && localY >= 8;
		case MetatileCollision.COL_UP_LEFT:
			return flag && localY <= 7;
		case MetatileCollision.COL_UP_RIGHT:
			return flag2 && localY <= 7;
		case MetatileCollision.COL_RIGHT:
			return flag2;
		case MetatileCollision.COL_LEFT:
			return flag;
		case MetatileCollision.COL_DOWN_LEFT:
			return flag && localY >= 8;
		case MetatileCollision.COL_DOWN_RIGHT:
			return flag2 && localY >= 8;
		case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
			if (flag)
			{
				return localY <= 7;
			}
			if (flag2)
			{
				return localY >= 8;
			}
			break;
		case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
			if (flag2)
			{
				return localY <= 7;
			}
			if (flag)
			{
				return localY >= 8;
			}
			break;
		}
		if (ProvidesFloorAtColumn(col, localX, out var topOffsetPx))
		{
			return localY >= topOffsetPx;
		}
		if (IsSlopeTile(col))
		{
			return false;
		}
		return col switch
		{
			MetatileCollision.COL_DEATH => false, 
			MetatileCollision.COL_NONE => false, 
			_ => true, 
		};
	}

	internal static int MapTileForCollision(int tid)
	{
		if (tid < 0)
		{
			return 0;
		}
		return tid;
	}

	internal static bool IsDeathCollision(MetatileCollision col)
	{
		return col == MetatileCollision.COL_DEATH || col == MetatileCollision.COL_DEATH_TOP || col == MetatileCollision.COL_DEATH_BOTTOM || col == MetatileCollision.COL_DEATH_LEFT || col == MetatileCollision.COL_DEATH_RIGHT || col == MetatileCollision.COL_DEATH_TOP_RIGHT || col == MetatileCollision.COL_DEATH_TOP_LEFT || col == MetatileCollision.COL_DEATH_BOTTOM_RIGHT || col == MetatileCollision.COL_DEATH_BOTTOM_LEFT || col == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT || col == MetatileCollision.COL_DEATH_TOP_BOTTOM || col == MetatileCollision.COL_DEATH_LEFT_RIGHT || col == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM || col == MetatileCollision.COL_TOP_CENTER_SPIKE || col == MetatileCollision.COL_BOTTOM_CENTER_SPIKE || col == MetatileCollision.COL_UP_LEFT_SPIKE || col == MetatileCollision.COL_UP_RIGHT_SPIKE || col == MetatileCollision.COL_UP_BOTH_SPIKES || col == MetatileCollision.COL_DOWN_LEFT_SPIKE || col == MetatileCollision.COL_DOWN_RIGHT_SPIKE || col == MetatileCollision.COL_DOWN_BOTH_SPIKES || col == MetatileCollision.COL_LEFT_SPIKE_BLOCK || col == MetatileCollision.COL_RIGHT_SPIKE_BLOCK || col == MetatileCollision.COL_BOTTOM_LEFT_SPIKE || col == MetatileCollision.COL_BOTTOM_RIGHT_SPIKE || col == MetatileCollision.COL_BOTTOM_SPIKES;
	}

	internal static bool VerifyGroundSupport(in CollisionMap map, int playerX_px, int playerY_px, int gameMode, bool mini, bool gravFlipped)
	{
		int cubeHitboxW = GetCubeHitboxW(mini);
		int cubeHitboxH = GetCubeHitboxH(mini);
		int num = ((mini && !gravFlipped) ? 9 : (mini ? (16 - cubeHitboxH >> 1) : 0));
		if (gameMode != 0 && gameMode != 4 && gameMode != 8 && mini)
		{
			num = 16 - cubeHitboxH >> 1;
		}
		int num2 = playerX_px + 8;
		int num3 = num2 - cubeHitboxW / 2;
		int num4 = num3 + (cubeHitboxW - 1);
		int num5 = num3 + cubeHitboxW / 2;
		int[] array = new int[3] { num3, num5, num4 };
		if (!gravFlipped)
		{
			int num6 = playerY_px + num + cubeHitboxH;
			if (gameMode == 2)
			{
				num6++;
			}
			int num7 = num6 / 16;
			int num8 = num7 + map.GroundRowsToReserve;
			if (num8 >= map.MapHeight)
			{
				return true;
			}
			if (num8 < 0)
			{
				return false;
			}
			int localY = (num6 % 16 + 16) % 16;
			int[] array2 = array;
			foreach (int num9 in array2)
			{
				int num10 = num9 / 16;
				if (num10 < 0 || num10 >= map.MapWidth)
				{
					continue;
				}
				int num11 = num8 * map.MapWidth + num10;
				if (num11 < 0 || num11 >= map.Tiles.Length)
				{
					continue;
				}
				int tid = map.Tiles[num11];
				MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(tid));
				if (collision != MetatileCollision.COL_NONE)
				{
					int num12 = num10 * 16;
					int localX = Math.Max(0, Math.Min(15, num9 - num12));
					if (ProvidesFloorAtColumn(collision, localX) && TileOccupiesPixel(collision, localX, localY))
					{
						return true;
					}
				}
			}
			return false;
		}
		int num13 = playerY_px + num;
		int num14 = num13 - 1;
		int num15 = ((num14 >= 0) ? (num14 / 16) : ((num14 - 16 + 1) / 16));
		int num16 = num15 + map.GroundRowsToReserve;
		if (num16 < 0 || num16 >= map.MapHeight)
		{
			return false;
		}
		int localY2 = (num14 % 16 + 16) % 16;
		int[] array3 = array;
		foreach (int num17 in array3)
		{
			int num18 = num17 / 16;
			if (num18 < 0 || num18 >= map.MapWidth)
			{
				continue;
			}
			int num19 = num16 * map.MapWidth + num18;
			if (num19 < 0 || num19 >= map.Tiles.Length)
			{
				continue;
			}
			int tid2 = map.Tiles[num19];
			MetatileCollision collision2 = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(tid2));
			if (collision2 != MetatileCollision.COL_NONE)
			{
				int num20 = num18 * 16;
				int localX2 = Math.Max(0, Math.Min(15, num17 - num20));
				if (ProvidesFloorAtColumn(collision2, localX2) && TileOccupiesPixel(collision2, localX2, localY2))
				{
					return true;
				}
			}
		}
		return false;
	}

	internal static bool BallGroundedProbeSpikeDeath(in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY, bool gravFlipped)
	{
		int num = (gravFlipped ? (playerY_px + hbOffY - 1) : (playerY_px + hbOffY + hbH + 2));
		for (int i = 0; i < 3; i++)
		{
			int num2 = i switch
			{
				1 => playerX_px + hbW / 2, 
				0 => playerX_px, 
				_ => playerX_px + hbW, 
			};
			int num3 = num2 / 16;
			int num4 = num / 16;
			if (num3 < 0 || num3 >= map.MapWidth)
			{
				continue;
			}
			int num5 = num4 + map.GroundRowsToReserve;
			if (num5 < 0 || num5 >= map.MapHeight)
			{
				continue;
			}
			int num6 = num5 * map.MapWidth + num3;
			if (num6 >= 0 && num6 < map.Tiles.Length)
			{
				int tid = map.Tiles[num6];
				int num7 = MapTileForCollision(tid);
				MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num7);
				int localX = num2 % 16;
				int localY = num % 16;
				if ((collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM) && MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
				{
					return true;
				}
			}
		}
		return false;
	}

	internal static MetatileCollision GetTileCollision(in CollisionMap map, int tileX, int tileY)
	{
		int num = tileY + map.GroundRowsToReserve;
		if (tileX < 0 || tileX >= map.MapWidth)
		{
			return MetatileCollision.COL_NONE;
		}
		if (num < 0)
		{
			return MetatileCollision.COL_NONE;
		}
		if (num >= map.MapHeight)
		{
			return MetatileCollision.COL_ALL;
		}
		int num2 = num * map.MapWidth + tileX;
		if (num2 < 0 || num2 >= map.Tiles.Length)
		{
			return MetatileCollision.COL_NONE;
		}
		int tid = map.Tiles[num2];
		int num3 = MapTileForCollision(tid);
		MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num3);
		if (tileX == 0 && collision == MetatileCollision.COL_ALL)
		{
			return MetatileCollision.COL_NONE;
		}
		return collision;
	}

	internal static bool PointKillsPlayer(in CollisionMap map, int px, int py)
	{
		int num = px / 16;
		int num2 = py / 16;
		int num3 = num2 + map.GroundRowsToReserve;
		if (num < 0 || num >= map.MapWidth)
		{
			return false;
		}
		if (num3 < 0 || num3 >= map.MapHeight)
		{
			return false;
		}
		int num4 = num3 * map.MapWidth + num;
		if (num4 < 0 || num4 >= map.Tiles.Length)
		{
			return false;
		}
		int tid = map.Tiles[num4];
		int num5 = MapTileForCollision(tid);
		MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num5);
		int num6 = num * 16;
		int num7 = (num3 - map.GroundRowsToReserve) * 16;
		int localX = Math.Max(0, Math.Min(15, px - num6));
		int localY = Math.Max(0, Math.Min(15, py - num7));
		return MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY);
	}

	internal static bool PointKillsPlayer(in CollisionMap map, int px, int py, out int dbg_tid, out int dbg_mappedTid, out MetatileCollision dbg_col, out int dbg_localX, out int dbg_localY)
	{
		dbg_tid = 0;
		dbg_mappedTid = 0;
		dbg_col = MetatileCollision.COL_NONE;
		dbg_localX = 0;
		dbg_localY = 0;
		int num = px / 16;
		int num2 = py / 16;
		int num3 = num2 + map.GroundRowsToReserve;
		if (num < 0 || num >= map.MapWidth)
		{
			return false;
		}
		if (num3 < 0 || num3 >= map.MapHeight)
		{
			return false;
		}
		int num4 = num3 * map.MapWidth + num;
		if (num4 < 0 || num4 >= map.Tiles.Length)
		{
			return false;
		}
		int num5 = map.Tiles[num4];
		int num6 = MapTileForCollision(num5);
		MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num6);
		int num7 = num * 16;
		int num8 = (num3 - map.GroundRowsToReserve) * 16;
		int num9 = Math.Max(0, Math.Min(15, px - num7));
		int num10 = Math.Max(0, Math.Min(15, py - num8));
		dbg_tid = num5;
		dbg_mappedTid = num6;
		dbg_col = collision;
		dbg_localX = num9;
		dbg_localY = num10;
		return MetatileCollisionTable.TileKillsAtPixel(collision, num9, num10);
	}

	internal static bool HasSlopeNearFeet(in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY)
	{
		int num = playerY_px + hbOffY + hbH;
		int num2 = playerX_px + hbW;
		int num3 = playerX_px / 16;
		int num4 = num2 / 16;
		int num5 = num / 16;
		for (int i = 0; i <= 1; i++)
		{
			int num6 = num5 + i;
			int num7 = num6 + map.GroundRowsToReserve;
			if (num7 < 0 || num7 >= map.MapHeight)
			{
				continue;
			}
			for (int j = num3; j <= num4; j++)
			{
				if (j < 0 || j >= map.MapWidth)
				{
					continue;
				}
				int num8 = num7 * map.MapWidth + j;
				if (num8 >= 0 && num8 < map.Tiles.Length)
				{
					int tid = map.Tiles[num8];
					int num9 = MapTileForCollision(tid);
					MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num9);
					if (IsSlopeTile(collision))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	internal static bool CheckComplexCollision(MetatileCollision collision, int localX, int localY)
	{
		return collision switch
		{
			MetatileCollision.COL_TOP_LEFT_STAIRS => localY < 8 || localX < 8, 
			MetatileCollision.COL_TOP_RIGHT_STAIRS => localY < 8 || localX >= 8, 
			MetatileCollision.COL_BOTTOM_LEFT_STAIRS => localX < 8 || (localX >= 8 && localY >= 8), 
			MetatileCollision.COL_BOTTOM_RIGHT_STAIRS => localX >= 8 || (localX < 8 && localY >= 8), 
			MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT => (localX < 8 && localY < 8) || (localX >= 8 && localY >= 8), 
			MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT => (localX >= 8 && localY < 8) || (localX < 8 && localY >= 8), 
			_ => false, 
		};
	}

	internal static bool IsComplexCollisionType(MetatileCollision collision)
	{
		if ((uint)(collision - 37) <= 5u)
		{
			return true;
		}
		return false;
	}

	internal static (bool hit, int surfaceY, bool spikeDeath) CheckFloor(in CollisionMap map, int collX, int collY, int collW, int collH, int velY_fixed = 0)
	{
		PfTrace.Event("CheckFloor.in", $"cx={collX} cy={collY} w={collW} h={collH} vyf={velY_fixed}");
		if (velY_fixed < 0)
		{
			return (hit: false, surfaceY: 0, spikeDeath: false);
		}
		int num = collY + collH;
		int num2 = num / 16;
		int num3 = collX + collW;
		int num4 = num2 + map.GroundRowsToReserve;
		if (num4 < 0)
		{
			return (hit: false, surfaceY: 0, spikeDeath: false);
		}
		if (num4 >= map.MapHeight)
		{
			int item = num2 * 16;
			return (hit: true, surfaceY: item, spikeDeath: false);
		}
		bool flag = false;
		for (int i = 0; i < 3; i++)
		{
			int num5 = i switch
			{
				1 => collX + (collW >> 1), 
				0 => collX, 
				_ => num3, 
			};
			int num6 = num5 / 16;
			if (num6 < 0 || num6 >= map.MapWidth)
			{
				continue;
			}
			int num7 = num4 * map.MapWidth + num6;
			if (num7 < 0 || num7 >= map.Tiles.Length)
			{
				continue;
			}
			int tid = map.Tiles[num7];
			int num8 = MapTileForCollision(tid);
			MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num8);
			if (collision == MetatileCollision.COL_NONE)
			{
				continue;
			}
			int localX = num5 % 16;
			int num9 = num % 16;
			if (collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM)
			{
				if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, num9))
				{
					flag = true;
				}
				continue;
			}
			int topOffsetPx;
			if (IsMiniBlockType(collision))
			{
				if (IsMiniBlockFloorHit(collision, localX, num9))
				{
					int num10 = num2 * 16;
					int num11 = num10 + GetMiniBlockFloorSurface(collision);
					if (num >= num11)
					{
						return (hit: true, surfaceY: num11, spikeDeath: false);
					}
				}
			}
			else if (ProvidesFloorAtColumn(collision, localX, out topOffsetPx))
			{
				int num12 = num2 * 16;
				if (IsStairsType(collision))
				{
					topOffsetPx = ((num9 >= 8) ? 8 : 0);
				}
				int num13 = num12 + topOffsetPx;
				if (num >= num13 && TileOccupiesPixel(collision, localX, num9))
				{
					return (hit: true, surfaceY: num13, spikeDeath: false);
				}
			}
			if (!IsComplexCollisionType(collision))
			{
				continue;
			}
			int num14 = num6 * 16;
			int num15 = num2 * 16;
			int num16 = num - num15;
			if (num16 < -1 || num16 >= 16 || !CheckComplexCollision(collision, localX, Math.Max(0, num16)))
			{
				continue;
			}
			int item2 = num15;
			for (int j = 0; j < 16; j++)
			{
				if (CheckComplexCollision(collision, localX, j))
				{
					item2 = num15 + j;
					break;
				}
			}
			return (hit: true, surfaceY: item2, spikeDeath: false);
		}
		if (flag)
		{
			return (hit: false, surfaceY: 0, spikeDeath: true);
		}
		return (hit: false, surfaceY: 0, spikeDeath: false);
	}

	internal static (bool hit, int surfaceY, bool spikeDeath, int ejectD, MetatileCollision collisionType) CheckFloorDetailed(in CollisionMap map, int collX, int collY, int collW, int collH, int velY_fixed = 0)
	{
		PfTrace.Event("CheckFloorDetailed.in", $"cx={collX} cy={collY} w={collW} h={collH} vyf={velY_fixed}");
		if (velY_fixed < 0)
		{
			return (hit: false, surfaceY: 0, spikeDeath: false, ejectD: 0, collisionType: MetatileCollision.COL_NONE);
		}
		int num = collY + collH;
		int num2 = num / 16;
		int num3 = collX + collW;
		int num4 = num2 + map.GroundRowsToReserve;
		if (num4 < 0)
		{
			return (hit: false, surfaceY: 0, spikeDeath: false, ejectD: 0, collisionType: MetatileCollision.COL_NONE);
		}
		if (num4 >= map.MapHeight)
		{
			int item = num2 * 16;
			return (hit: true, surfaceY: item, spikeDeath: false, ejectD: num & 0xF, collisionType: MetatileCollision.COL_ALL);
		}
		bool flag = false;
		for (int i = 0; i < 3; i++)
		{
			int num5 = i switch
			{
				1 => collX + (collW >> 1), 
				0 => collX, 
				_ => num3, 
			};
			int num6 = num5 / 16;
			if (num6 < 0 || num6 >= map.MapWidth)
			{
				continue;
			}
			int num7 = num4 * map.MapWidth + num6;
			if (num7 < 0 || num7 >= map.Tiles.Length)
			{
				continue;
			}
			int tid = map.Tiles[num7];
			int num8 = MapTileForCollision(tid);
			MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num8);
			if (collision == MetatileCollision.COL_NONE)
			{
				continue;
			}
			int localX = num5 % 16;
			int num9 = num % 16;
			if (collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM)
			{
				if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, num9))
				{
					flag = true;
				}
				continue;
			}
			int topOffsetPx;
			if (IsMiniBlockType(collision))
			{
				if (IsMiniBlockFloorHit(collision, localX, num9))
				{
					int num10 = num2 * 16;
					int num11 = num10 + GetMiniBlockFloorSurface(collision);
					if (num >= num11)
					{
						if (1 == 0)
						{
						}
						int num12;
						switch (collision)
						{
						case MetatileCollision.COL_TOP:
						case MetatileCollision.COL_BOTTOM:
						case MetatileCollision.COL_UP_LEFT:
						case MetatileCollision.COL_UP_RIGHT:
						case MetatileCollision.COL_DOWN_LEFT:
						case MetatileCollision.COL_DOWN_RIGHT:
						case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
						case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
						case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
						case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
							num12 = num9 & 7;
							break;
						case MetatileCollision.COL_LEFT:
						case MetatileCollision.COL_RIGHT:
							num12 = num9 & 0xF;
							break;
						default:
							num12 = num9 & 0xF;
							break;
						}
						if (1 == 0)
						{
						}
						int item2 = num12;
						return (hit: true, surfaceY: num11, spikeDeath: false, ejectD: item2, collisionType: collision);
					}
				}
			}
			else if (ProvidesFloorAtColumn(collision, localX, out topOffsetPx))
			{
				int num13 = num2 * 16;
				bool flag2 = IsStairsType(collision);
				if (flag2)
				{
					topOffsetPx = ((num9 >= 8) ? 8 : 0);
				}
				int num14 = num13 + topOffsetPx;
				if (num >= num14 && TileOccupiesPixel(collision, localX, num9))
				{
					int item3 = (flag2 ? (num9 & 7) : (num9 & 0xF));
					return (hit: true, surfaceY: num14, spikeDeath: false, ejectD: item3, collisionType: collision);
				}
			}
			if (!IsComplexCollisionType(collision))
			{
				continue;
			}
			int num15 = num6 * 16;
			int num16 = num2 * 16;
			int num17 = num - num16;
			if (num17 < -1 || num17 >= 16 || !CheckComplexCollision(collision, localX, Math.Max(0, num17)))
			{
				continue;
			}
			int item4 = num16;
			for (int j = 0; j < 16; j++)
			{
				if (CheckComplexCollision(collision, localX, j))
				{
					item4 = num16 + j;
					break;
				}
			}
			return (hit: true, surfaceY: item4, spikeDeath: false, ejectD: num9 & 0xF, collisionType: collision);
		}
		if (flag)
		{
			return (hit: false, surfaceY: 0, spikeDeath: true, ejectD: 0, collisionType: MetatileCollision.COL_NONE);
		}
		return (hit: false, surfaceY: 0, spikeDeath: false, ejectD: 0, collisionType: MetatileCollision.COL_NONE);
	}

	internal static (bool hit, int ceilingBottomY, bool spikeDeath, MetatileCollision hitCollision) CheckCeiling(in CollisionMap map, int collX, int collY, int collW, int collH, int velY_fixed = -1)
	{
		PfTrace.Event("CheckCeiling.in", $"cx={collX} cy={collY} w={collW} h={collH} vyf={velY_fixed}");
		if (velY_fixed >= 0)
		{
			return (hit: false, ceilingBottomY: 0, spikeDeath: false, hitCollision: MetatileCollision.COL_NONE);
		}
		int num = collX + collW;
		int num2 = collY + 1;
		bool item = false;
		int num3 = num2;
		for (int i = 0; i < 3; i++)
		{
			int num4 = i switch
			{
				1 => collX + (collW >> 1), 
				0 => collX, 
				_ => num, 
			};
			int num5 = num4 / 16;
			int num6 = num3 / 16;
			if (num5 < 0 || num5 >= map.MapWidth)
			{
				continue;
			}
			int num7 = num6 + map.GroundRowsToReserve;
			if (num7 < 0 || num7 >= map.MapHeight)
			{
				continue;
			}
			int num8 = num7 * map.MapWidth + num5;
			if (num8 >= 0 && num8 < map.Tiles.Length)
			{
				int tid = map.Tiles[num8];
				int num9 = MapTileForCollision(tid);
				MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num9);
				int localX = num4 % 16;
				int localY = num3 % 16;
				if ((collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM) && MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
				{
					item = true;
					break;
				}
			}
		}
		int num10 = ((num2 >= 0) ? (num2 / 16) : ((num2 - 16 + 1) / 16));
		int num11 = num10 + map.GroundRowsToReserve;
		if (num11 < 0 || num11 >= map.MapHeight)
		{
			return (hit: false, ceilingBottomY: 0, spikeDeath: item, hitCollision: MetatileCollision.COL_NONE);
		}
		for (int j = 0; j < 3; j++)
		{
			int num12 = j switch
			{
				1 => collX + (collW >> 1), 
				0 => collX, 
				_ => num, 
			};
			int num13 = num12 / 16;
			if (num13 < 0 || num13 >= map.MapWidth)
			{
				continue;
			}
			int num14 = num11 * map.MapWidth + num13;
			if (num14 < 0 || num14 >= map.Tiles.Length)
			{
				continue;
			}
			int tid2 = map.Tiles[num14];
			int num15 = MapTileForCollision(tid2);
			MetatileCollision collision2 = MetatileCollisionTable.GetCollision((byte)num15);
			if (collision2 == MetatileCollision.COL_NONE)
			{
				continue;
			}
			int num16 = num13 * 16;
			int num17 = num10 * 16;
			int num18 = (num12 % 16 + 16) % 16;
			int localY2 = (num2 % 16 + 16) % 16;
			if (IsComplexCollisionType(collision2))
			{
				if (!CheckComplexCollision(collision2, num18, localY2))
				{
					continue;
				}
				int item2 = num17 + 15;
				for (int num19 = 15; num19 >= 0; num19--)
				{
					if (CheckComplexCollision(collision2, num18, num19))
					{
						item2 = num17 + num19 + 1;
						break;
					}
				}
				return (hit: true, ceilingBottomY: item2, spikeDeath: item, hitCollision: collision2);
			}
			var (num20, num21, num22, num23) = GetCollisionBounds(collision2);
			if (num22 <= num20 || num23 <= num21)
			{
				continue;
			}
			int num24 = num17 + num21;
			int num25 = num17 + num23;
			bool flag = collision2 == MetatileCollision.COL_UP_LEFT || collision2 == MetatileCollision.COL_UP_RIGHT || collision2 == MetatileCollision.COL_DOWN_LEFT || collision2 == MetatileCollision.COL_DOWN_RIGHT || collision2 == MetatileCollision.COL_LEFT_SPIKE_BLOCK || collision2 == MetatileCollision.COL_RIGHT_SPIKE_BLOCK;
			if (num18 < num20 || num18 >= num22)
			{
				continue;
			}
			bool num26;
			if (!flag)
			{
				if (num2 < num24)
				{
					continue;
				}
				num26 = num2 < num25;
			}
			else
			{
				if (num2 < num24)
				{
					continue;
				}
				num26 = num2 < num25;
			}
			if (!num26)
			{
				continue;
			}
			return (hit: true, ceilingBottomY: num25, spikeDeath: item, hitCollision: collision2);
		}
		return (hit: false, ceilingBottomY: 0, spikeDeath: item, hitCollision: MetatileCollision.COL_NONE);
	}

	internal static bool CheckCenterPointDeath(in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY)
	{
		PfTrace.Event("CheckCenterPointDeath.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} hbOffY={hbOffY}");
		int num = playerX_px + (hbW >> 1) - 1;
		int num2 = playerY_px + (hbH >> 1) + hbOffY;
		int num3 = num / 16;
		int num4 = num2 / 16;
		if (num3 < 0 || num3 >= map.MapWidth)
		{
			return false;
		}
		int num5 = num4 + map.GroundRowsToReserve;
		if (num5 < 0 || num5 >= map.MapHeight)
		{
			return false;
		}
		int num6 = num5 * map.MapWidth + num3;
		if (num6 < 0 || num6 >= map.Tiles.Length)
		{
			return false;
		}
		int tid = map.Tiles[num6];
		int num7 = MapTileForCollision(tid);
		MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num7);
		int num8 = num3 * 16;
		int num9 = (num5 - map.GroundRowsToReserve) * 16;
		int localX = Math.Max(0, Math.Min(15, num - num8));
		int localY = Math.Max(0, Math.Min(15, num2 - num9));
		if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
		{
			return true;
		}
		MetatileCollision metatileCollision = collision;
		MetatileCollision metatileCollision2 = metatileCollision;
		if (((uint)(metatileCollision2 - 1) <= 9u || (uint)(metatileCollision2 - 32) <= 1u || (uint)(metatileCollision2 - 37) <= 6u) && TileOccupiesPixel(collision, localX, localY))
		{
			return true;
		}
		return false;
	}

	internal static bool CheckDeathCollision(in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY, int gameMode = -1, bool dblocked = false)
	{
		PfTrace.Event("CheckDeathCollision.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} mode={gameMode} dblk={(dblocked ? 1 : 0)}");
		int num = playerX_px + (hbW >> 1) - 1;
		int num2 = playerY_px + hbH / 2 + hbOffY;
		int num3 = num / 16;
		int num4 = num2 / 16;
		int num5 = num4 + map.GroundRowsToReserve;
		if (num3 < 0 || num3 >= map.MapWidth)
		{
			return false;
		}
		if (num5 < 0 || num5 >= map.MapHeight)
		{
			return false;
		}
		int num6 = num5 * map.MapWidth + num3;
		if (num6 < 0 || num6 >= map.Tiles.Length)
		{
			return false;
		}
		int tid = map.Tiles[num6];
		int num7 = MapTileForCollision(tid);
		MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num7);
		int num8 = num3 * 16;
		int num9 = (num5 - map.GroundRowsToReserve) * 16;
		int localX = Math.Max(0, Math.Min(15, num - num8));
		int localY = Math.Max(0, Math.Min(15, num2 - num9));
		if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
		{
			return true;
		}
		bool flag = gameMode == 6 && dblocked;
		switch (collision)
		{
		case MetatileCollision.COL_ALL:
		case MetatileCollision.COL_FLOOR_CEIL:
		case MetatileCollision.COL_NO_SIDE:
			if (flag || !TileOccupiesPixel(collision, localX, localY))
			{
				break;
			}
			return true;
		case MetatileCollision.COL_TOP:
		case MetatileCollision.COL_BOTTOM:
		case MetatileCollision.COL_LEFT:
		case MetatileCollision.COL_RIGHT:
		case MetatileCollision.COL_UP_LEFT:
		case MetatileCollision.COL_UP_RIGHT:
		case MetatileCollision.COL_DOWN_LEFT:
		case MetatileCollision.COL_DOWN_RIGHT:
		case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
		case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
		case MetatileCollision.COL_TOP_LEFT_STAIRS:
		case MetatileCollision.COL_TOP_RIGHT_STAIRS:
		case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
		case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
		case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
		case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
			if (TileOccupiesPixel(collision, localX, localY))
			{
				return true;
			}
			break;
		}
		return false;
	}

	internal static bool CheckFloorSpikes(in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, bool mini, out int deathX, out int deathY)
	{
		deathX = 0;
		deathY = 0;
		PfTrace.Event("CheckFloorSpikes.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} mini={(mini ? 1 : 0)}");
		int num = (mini ? (16 - hbH >> 1) : 0);
		int num2 = playerY_px + num + hbH - 2;
		int num3 = playerY_px + (mini ? num : 2);
		int num4 = playerX_px + 3;
		int num5 = playerX_px + hbW - 3;
		if (PointKillsPlayer(in map, num4, num2))
		{
			deathX = num4;
			deathY = num2;
			return true;
		}
		if (PointKillsPlayer(in map, num5, num2))
		{
			deathX = num5;
			deathY = num2;
			return true;
		}
		if (PointKillsPlayer(in map, num4, num3))
		{
			deathX = num4;
			deathY = num3;
			return true;
		}
		if (PointKillsPlayer(in map, num5, num3))
		{
			deathX = num5;
			deathY = num3;
			return true;
		}
		return false;
	}

	internal static bool CheckFloorSpikes(in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, bool mini, out int deathX, out int deathY, out string cornerName, out int dbg_tid, out int dbg_mappedTid, out MetatileCollision dbg_col, out int dbg_localX, out int dbg_localY)
	{
		deathX = 0;
		deathY = 0;
		cornerName = "";
		dbg_tid = 0;
		dbg_mappedTid = 0;
		dbg_col = MetatileCollision.COL_NONE;
		dbg_localX = 0;
		dbg_localY = 0;
		PfTrace.Event("CheckFloorSpikes2.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} mini={(mini ? 1 : 0)}");
		int num = (mini ? (16 - hbH >> 1) : 0);
		int num2 = playerY_px + num + hbH - 2;
		int num3 = playerY_px + (mini ? num : 2);
		int num4 = playerX_px + 3;
		int num5 = playerX_px + hbW - 3;
		if (PointKillsPlayer(in map, num4, num2, out var dbg_tid2, out var dbg_mappedTid2, out var dbg_col2, out var dbg_localX2, out var dbg_localY2))
		{
			deathX = num4;
			deathY = num2;
			cornerName = "BL";
			dbg_tid = dbg_tid2;
			dbg_mappedTid = dbg_mappedTid2;
			dbg_col = dbg_col2;
			dbg_localX = dbg_localX2;
			dbg_localY = dbg_localY2;
			return true;
		}
		if (PointKillsPlayer(in map, num5, num2, out dbg_tid2, out dbg_mappedTid2, out dbg_col2, out dbg_localX2, out dbg_localY2))
		{
			deathX = num5;
			deathY = num2;
			cornerName = "BR";
			dbg_tid = dbg_tid2;
			dbg_mappedTid = dbg_mappedTid2;
			dbg_col = dbg_col2;
			dbg_localX = dbg_localX2;
			dbg_localY = dbg_localY2;
			return true;
		}
		if (PointKillsPlayer(in map, num4, num3, out dbg_tid2, out dbg_mappedTid2, out dbg_col2, out dbg_localX2, out dbg_localY2))
		{
			deathX = num4;
			deathY = num3;
			cornerName = "TL";
			dbg_tid = dbg_tid2;
			dbg_mappedTid = dbg_mappedTid2;
			dbg_col = dbg_col2;
			dbg_localX = dbg_localX2;
			dbg_localY = dbg_localY2;
			return true;
		}
		if (PointKillsPlayer(in map, num5, num3, out dbg_tid2, out dbg_mappedTid2, out dbg_col2, out dbg_localX2, out dbg_localY2))
		{
			deathX = num5;
			deathY = num3;
			cornerName = "TR";
			dbg_tid = dbg_tid2;
			dbg_mappedTid = dbg_mappedTid2;
			dbg_col = dbg_col2;
			dbg_localX = dbg_localX2;
			dbg_localY = dbg_localY2;
			return true;
		}
		return false;
	}

	internal static bool CheckForwardCollision(in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY, int gameMode, bool mini, bool gravFlipped, bool skipSlopeCheck = false)
	{
		PfTrace.Event("CheckForwardCollision.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} hbOffY={hbOffY} mode={gameMode} mini={(mini ? 1 : 0)} gF={(gravFlipped ? 1 : 0)} skipSlope={(skipSlopeCheck ? 1 : 0)}");
		if (!skipSlopeCheck && HasSlopeNearFeet(in map, playerX_px, playerY_px, hbW, hbH, hbOffY))
		{
			return false;
		}
		int num = playerX_px + hbW;
		int num2 = playerY_px + (mini ? ((16 - hbH >> 1) + (hbH >> 1)) : (hbH >> 1));
		if (mini && (gameMode == 0 || gameMode == 4 || gameMode == 8))
		{
			num2 += (gravFlipped ? 3 : (-2));
		}
		int num3 = num / 16;
		int num4 = num2 / 16;
		int num5 = num4 + map.GroundRowsToReserve;
		if (num3 < 0 || num3 >= map.MapWidth)
		{
			return false;
		}
		if (num5 < 0 || num5 >= map.MapHeight)
		{
			return true;
		}
		int num6 = num5 * map.MapWidth + num3;
		if (num6 < 0 || num6 >= map.Tiles.Length)
		{
			return false;
		}
		int tid = map.Tiles[num6];
		int num7 = MapTileForCollision(tid);
		MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num7);
		if (collision == MetatileCollision.COL_FLOOR_CEIL || collision == MetatileCollision.COL_NO_SIDE)
		{
			return false;
		}
		if (IsSlopeTile(collision))
		{
			return false;
		}
		int num8 = num3 * 16;
		int num9 = num4 * 16;
		int localX = Math.Max(0, Math.Min(15, num - num8));
		int localY = Math.Max(0, Math.Min(15, num2 - num9));
		if (TileOccupiesPixel(collision, localX, localY) || MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
		{
			return true;
		}
		return false;
	}

	internal static (int nudge, int slopeType) GetForwardSlopeNudge(in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY, int gameMode, bool mini, bool gravFlipped)
	{
		PfTrace.Event("GetForwardSlopeNudge.in", $"X={playerX_px} Y={playerY_px} hbW={hbW} hbH={hbH} hbOffY={hbOffY} mode={gameMode} mini={(mini ? 1 : 0)} gF={(gravFlipped ? 1 : 0)}");
		int num = playerX_px + hbW;
		int num2 = playerY_px + (mini ? ((16 - hbH >> 1) + (hbH >> 1)) : (hbH >> 1));
		if (mini && (gameMode == 0 || gameMode == 4 || gameMode == 8))
		{
			num2 += (gravFlipped ? 3 : (-2));
		}
		int num3 = num / 16;
		int num4 = num2 / 16;
		int num5 = num4 + map.GroundRowsToReserve;
		if (num3 < 0 || num3 >= map.MapWidth)
		{
			return (nudge: 0, slopeType: 0);
		}
		if (num5 < 0 || num5 >= map.MapHeight)
		{
			return (nudge: 0, slopeType: 0);
		}
		int num6 = num5 * map.MapWidth + num3;
		if (num6 < 0 || num6 >= map.Tiles.Length)
		{
			return (nudge: 0, slopeType: 0);
		}
		int tid = map.Tiles[num6];
		int num7 = MapTileForCollision(tid);
		MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num7);
		if (!IsSlopeTile(collision))
		{
			return (nudge: 0, slopeType: 0);
		}
		var (flag, _, item) = SlopeCalc(num, num2, collision);
		if (!flag)
		{
			return (nudge: 0, slopeType: 0);
		}
		return (nudge: (collision == MetatileCollision.COL_SLOPE_RU45 || collision == MetatileCollision.COL_SLOPE_LU45 || (collision >= MetatileCollision.COL_SLOPE_RU22_RIGHT && collision <= MetatileCollision.COL_SLOPE_LU22_LEFT) || (collision >= MetatileCollision.COL_SLOPE_RU66_TOP && collision <= MetatileCollision.COL_SLOPE_LU66_TOP)) ? 2 : (-2), slopeType: item);
	}

	internal static void CommonGravityRoutine(ref int velY, ref int posY, int tmpgravity, int tmpfallspeed, int gravityDir, int dashMode, double gravityMod, double timeScale, bool isFullSpeed, int velocityX, int clampMaxY)
	{
		int num = velY;
		int num2 = posY;
		switch (dashMode)
		{
		case 0:
		{
			int num3 = tmpgravity;
			if ((gravityDir != 0) ? (velY < tmpfallspeed) : (velY > tmpfallspeed))
			{
				num3 = -num3;
			}
			num3 = (int)((double)num3 * gravityMod);
			if (isFullSpeed)
			{
				velY += num3;
			}
			else
			{
				velY += (int)Math.Round((double)num3 * timeScale);
			}
			break;
		}
		case 2:
			velY = -velocityX;
			break;
		case 3:
			velY = velocityX;
			break;
		case 4:
			velY = velocityX * 2;
			if (isFullSpeed)
			{
				posY -= velY;
			}
			else
			{
				posY -= (int)Math.Round((double)velY * timeScale);
			}
			return;
		case 5:
			velY = velocityX * 2;
			break;
		default:
			velY = ((gravityDir == 0) ? 1 : (-1));
			if (isFullSpeed)
			{
				posY += velY;
			}
			else
			{
				posY += (int)Math.Round((double)velY * timeScale);
			}
			return;
		}
		if (isFullSpeed)
		{
			posY += velY;
		}
		else
		{
			posY += (int)Math.Round((double)velY * timeScale);
		}
		if (posY > clampMaxY)
		{
			posY = clampMaxY;
		}
	}

	internal static bool BallIsGrounded(in CollisionMap map, int playerX_px, int playerY_px, int hbW, int hbH, int hbOffY, bool gravFlipped)
	{
		if (!gravFlipped)
		{
			int collY = playerY_px + hbOffY + hbH;
			var (flag, _, flag2) = CheckFloor(in map, playerX_px, collY, hbW, 2);
			return flag && !flag2;
		}
		int num = playerY_px + hbOffY;
		var (flag3, _, flag4, _) = CheckCeiling(in map, playerX_px, num - 2, hbW, 2);
		return flag3 && !flag4;
	}

	internal static (bool hit, int ejection, int slopeType) SlopeCalc(int temp_x, int temp_y, MetatileCollision collision)
	{
		PfTrace.Event("SlopeCalc.in", $"tx={temp_x} ty={temp_y} col={collision}");
		if (collision < MetatileCollision.COL_SLOPE_RD45 || collision > MetatileCollision.COL_SLOPE_LU66_TOP)
		{
			return (hit: false, ejection: 0, slopeType: 0);
		}
		int num;
		int num2;
		int item;
		switch (collision)
		{
		case MetatileCollision.COL_SLOPE_LU45:
			num = temp_x & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			item = 9;
			break;
		case MetatileCollision.COL_SLOPE_LD45:
			num = temp_x & 0xF;
			num2 = temp_y & 0xF;
			item = 1;
			break;
		case MetatileCollision.COL_SLOPE_RU45:
			num = (temp_x & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			item = 13;
			break;
		case MetatileCollision.COL_SLOPE_RD45:
			num = (temp_x & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			item = 5;
			break;
		case MetatileCollision.COL_SLOPE_RU22_RIGHT:
			num = ((temp_x >> 1) & 7) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			item = 14;
			break;
		case MetatileCollision.COL_SLOPE_RU22_LEFT:
			num = (((temp_x >> 1) | 8) & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			item = 14;
			break;
		case MetatileCollision.COL_SLOPE_RD22_RIGHT:
			num = ((temp_x >> 1) & 7) ^ 0xF;
			num2 = temp_y & 0xF;
			item = 6;
			break;
		case MetatileCollision.COL_SLOPE_RD22_LEFT:
			num = (((temp_x >> 1) | 8) & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			item = 6;
			break;
		case MetatileCollision.COL_SLOPE_LU22_RIGHT:
			num = (temp_x >> 1) & 7;
			num2 = (temp_y & 0xF) ^ 0xF;
			item = 10;
			break;
		case MetatileCollision.COL_SLOPE_LU22_LEFT:
			num = ((temp_x >> 1) | 8) & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			item = 10;
			break;
		case MetatileCollision.COL_SLOPE_LD22_RIGHT:
			num = (temp_x >> 1) & 7;
			num2 = temp_y & 0xF;
			item = 2;
			break;
		case MetatileCollision.COL_SLOPE_LD22_LEFT:
			num = ((temp_x >> 1) | 8) & 0xF;
			num2 = temp_y & 0xF;
			item = 2;
			break;
		case MetatileCollision.COL_SLOPE_RD66_TOP:
			if ((temp_x & 0xF) < 8)
			{
				return (hit: false, ejection: 0, slopeType: 0);
			}
			num = (((temp_x & 7) << 1) & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			item = 7;
			break;
		case MetatileCollision.COL_SLOPE_RD66_BOT:
			if ((temp_x & 0xF) >= 8)
			{
				return (hit: true, ejection: temp_y & 0xF, slopeType: 0);
			}
			num = (((temp_x & 0xF) << 1) & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			item = 7;
			break;
		case MetatileCollision.COL_SLOPE_LD66_TOP:
			if ((temp_x & 0xF) >= 8)
			{
				return (hit: false, ejection: 0, slopeType: 0);
			}
			num = ((temp_x & 7) << 1) & 0xF;
			num2 = temp_y & 0xF;
			item = 3;
			break;
		case MetatileCollision.COL_SLOPE_LD66_BOT:
			if ((temp_x & 0xF) < 8)
			{
				return (hit: true, ejection: temp_y & 0xF, slopeType: 0);
			}
			num = ((temp_x & 0xF) << 1) & 0xF;
			num2 = temp_y & 0xF;
			item = 3;
			break;
		case MetatileCollision.COL_SLOPE_RU66_TOP:
			if ((temp_x & 0xF) < 8)
			{
				return (hit: false, ejection: 0, slopeType: 0);
			}
			num = (((temp_x & 7) << 1) & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			item = 15;
			break;
		case MetatileCollision.COL_SLOPE_RU66_BOT:
			if ((temp_x & 0xF) >= 8)
			{
				return (hit: true, ejection: temp_y & 0xF, slopeType: 0);
			}
			num = (((temp_x & 0xF) << 1) & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			item = 15;
			break;
		case MetatileCollision.COL_SLOPE_LU66_TOP:
			if ((temp_x & 0xF) >= 8)
			{
				return (hit: false, ejection: 0, slopeType: 0);
			}
			num = ((temp_x & 7) << 1) & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			item = 11;
			break;
		case MetatileCollision.COL_SLOPE_LU66_BOT:
			if ((temp_x & 0xF) < 8)
			{
				return (hit: true, ejection: temp_y & 0xF, slopeType: 0);
			}
			num = ((temp_x & 0xF) << 1) & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			item = 11;
			break;
		default:
			return (hit: false, ejection: 0, slopeType: 0);
		}
		if (num2 >= num)
		{
			return (hit: true, ejection: num2 - num, slopeType: item);
		}
		return (hit: false, ejection: 0, slopeType: item);
	}

	internal static (bool hit, int ejection, int slopeType) CheckSlopesDown(in CollisionMap map, int playerX_px, int checkBaseX, int checkBaseY, int checkWidth, bool inputHeld, int gameMode, bool gravFlipped, int velX_fixed, ref int lastSlopeType, ref bool slopeJumpHigher)
	{
		PfTrace.Event("CheckSlopesDown.in", $"X={playerX_px} bx={checkBaseX} by={checkBaseY} w={checkWidth} mode={gameMode} gF={(gravFlipped ? 1 : 0)} vx={velX_fixed} lst={lastSlopeType}");
		if (playerX_px < 16)
		{
			return (hit: false, ejection: 0, slopeType: 0);
		}
		int item = 0;
		int item2 = 0;
		bool item3 = false;
		for (int i = 0; i < 2; i++)
		{
			int num = checkBaseX + i * checkWidth;
			int tileX = num / 16;
			int tileY = checkBaseY / 16;
			MetatileCollision tileCollision = GetTileCollision(in map, tileX, tileY);
			if (tileCollision < MetatileCollision.COL_SLOPE_RD45 || tileCollision > MetatileCollision.COL_SLOPE_LU66_TOP)
			{
				continue;
			}
			(bool hit, int ejection, int slopeType) tuple = SlopeCalc(num, checkBaseY, tileCollision);
			bool item4 = tuple.hit;
			int num2 = tuple.ejection;
			int num3 = tuple.slopeType;
			int num4 = ((item4 && num3 == 0) ? lastSlopeType : num3);
			if (i == 0 && (num4 & 4) != 0)
			{
				if (item4 && num3 != 0 && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
				{
					slopeJumpHigher = true;
				}
			}
			else if (i == 1 && (num4 & 4) == 0)
			{
				if (item4 && num3 != 0 && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
				{
					slopeJumpHigher = true;
				}
			}
			else if (item4)
			{
				if (num3 != 0 && gameMode != 0 && gameMode != 4 && gameMode != 8)
				{
					int num5 = (((num3 & 4) != 0) ? 4 : 0) | (((num3 & 8) != 0) ? 2 : 0) | (gravFlipped ? 1 : 0);
					if ((num5 == 0 || num5 == 3 || num5 == 4 || num5 == 7) ? inputHeld : (!inputHeld))
					{
						num2 = 4;
					}
				}
				if ((lastSlopeType & 4) != 0 && (num3 & 4) == 0 && lastSlopeType != 0 && num3 != 0)
				{
					num3 = lastSlopeType;
					num2 = velX_fixed >> 8;
				}
				if (num3 != 0)
				{
					lastSlopeType = num3;
				}
				item = num2;
				item2 = num3;
				item3 = true;
			}
			else if (num3 != 0)
			{
				item2 = num3;
			}
		}
		return (hit: item3, ejection: item, slopeType: item2);
	}

	internal static (bool hit, int ejection, int slopeType) CheckSlopesUp(in CollisionMap map, int playerX_px, int checkBaseX, int checkBaseY, int checkWidth, bool inputHeld, int gameMode, bool gravFlipped, int velX_fixed, ref int lastSlopeType, ref bool slopeJumpHigher)
	{
		PfTrace.Event("CheckSlopesUp.in", $"X={playerX_px} bx={checkBaseX} by={checkBaseY} w={checkWidth} mode={gameMode} gF={(gravFlipped ? 1 : 0)} vx={velX_fixed} lst={lastSlopeType}");
		if (playerX_px < 16)
		{
			return (hit: false, ejection: 0, slopeType: 0);
		}
		int item = 0;
		int item2 = 0;
		bool item3 = false;
		for (int i = 0; i < 2; i++)
		{
			int num = checkBaseX + i * checkWidth;
			int tileX = num / 16;
			int tileY = checkBaseY / 16;
			MetatileCollision tileCollision = GetTileCollision(in map, tileX, tileY);
			if (tileCollision < MetatileCollision.COL_SLOPE_RD45 || tileCollision > MetatileCollision.COL_SLOPE_LU66_TOP)
			{
				continue;
			}
			(bool hit, int ejection, int slopeType) tuple = SlopeCalc(num, checkBaseY, tileCollision);
			bool item4 = tuple.hit;
			int num2 = tuple.ejection;
			int num3 = tuple.slopeType;
			int num4 = ((item4 && num3 == 0) ? lastSlopeType : num3);
			if (i == 0 && (num4 & 4) != 0)
			{
				if (item4 && num3 != 0 && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
				{
					slopeJumpHigher = true;
				}
			}
			else if (i == 1 && (num4 & 4) == 0)
			{
				if (item4 && num3 != 0 && inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
				{
					slopeJumpHigher = true;
				}
			}
			else if (item4)
			{
				if (num3 != 0 && gameMode != 0 && gameMode != 4 && gameMode != 8)
				{
					int num5 = (((num3 & 4) != 0) ? 4 : 0) | (((num3 & 8) != 0) ? 2 : 0) | (gravFlipped ? 1 : 0);
					if ((num5 == 0 || num5 == 3 || num5 == 4 || num5 == 7) ? inputHeld : (!inputHeld))
					{
						num2 = 4;
					}
				}
				if ((lastSlopeType & 4) != 0 && (num3 & 4) == 0 && lastSlopeType != 0 && num3 != 0)
				{
					num3 = lastSlopeType;
					num2 = velX_fixed >> 8;
				}
				if (num3 != 0)
				{
					lastSlopeType = num3;
				}
				item = num2;
				item2 = num3;
				item3 = true;
			}
			else if (num3 != 0)
			{
				item2 = num3;
			}
		}
		return (hit: item3, ejection: item, slopeType: item2);
	}

	internal static bool ApplySlopeWedgeStateOnly(in CollisionMap map, int playerX_px, int checkBaseX, int checkBaseY, int checkWidth, bool inputHeld, int gameMode, bool gravFlipped, ref int slopeFrames, ref int slopeWasOnCounter, ref int slopeType, ref int lastSlopeType, ref bool slopeJumpHigher)
	{
		Action<string> slopeDiagLog = SlopeDiagLog;
		slopeDiagLog?.Invoke($"[SLOPE/WEDGE_IN] pX={playerX_px} baseX={checkBaseX} baseY={checkBaseY} W={checkWidth} input={inputHeld} gm={gameMode} gF={gravFlipped} | sF={slopeFrames} swOn={slopeWasOnCounter} sT={slopeType} lst={lastSlopeType} jH={slopeJumpHigher}");
		if (playerX_px < 16)
		{
			slopeDiagLog?.Invoke("[SLOPE/WEDGE_OUT] reject pX<0x10");
			return false;
		}
		bool flag = false;
		for (int i = 0; i < 2; i++)
		{
			int num = checkBaseX + i * checkWidth;
			int num2 = num / 16;
			int num3 = checkBaseY / 16;
			MetatileCollision tileCollision = GetTileCollision(in map, num2, num3);
			if (tileCollision < MetatileCollision.COL_SLOPE_RD45 || tileCollision > MetatileCollision.COL_SLOPE_LU66_TOP)
			{
				slopeDiagLog?.Invoke($"[SLOPE/WEDGE_PROBE{i}] X={num} Y={checkBaseY} tile=({num2},{num3}) coll={tileCollision} -> NOT_SLOPE");
				continue;
			}
			var (flag2, _, num4) = SlopeCalc(num, checkBaseY, tileCollision);
			if (!flag2)
			{
				slopeDiagLog?.Invoke($"[SLOPE/WEDGE_PROBE{i}] X={num} Y={checkBaseY} tile=({num2},{num3}) coll={tileCollision} -> WEDGE_MISS");
				continue;
			}
			if (num4 == 0)
			{
				slopeDiagLog?.Invoke($"[SLOPE/WEDGE_PROBE{i}] X={num} Y={checkBaseY} tile=({num2},{num3}) coll={tileCollision} -> SENTINEL_HIT_NO_COL_END");
				continue;
			}
			slopeDiagLog?.Invoke($"[SLOPE/WEDGE_PROBE{i}] X={num} Y={checkBaseY} tile=({num2},{num3}) coll={tileCollision} -> WEDGE_PASS newSlopeT={num4}");
			flag = true;
			if (gameMode == 0 || gameMode == 4 || gameMode == 8)
			{
				if (inputHeld)
				{
					slopeJumpHigher = true;
					slopeDiagLog?.Invoke($"[SLOPE/WEDGE_STATE] probe={i} CUBE+input -> jumpHigher=true (no sF/swOn change)");
				}
				else
				{
					slopeFrames = 1;
					slopeWasOnCounter = 3;
					slopeDiagLog?.Invoke($"[SLOPE/WEDGE_STATE] probe={i} CUBE+!input -> sF:=1 swOn:=3");
				}
			}
			else
			{
				slopeFrames = 1;
				slopeWasOnCounter = 3;
				slopeDiagLog?.Invoke($"[SLOPE/WEDGE_STATE] probe={i} OTHER(gm={gameMode}) -> sF:=1 swOn:=3");
			}
			int num5 = num4;
			if ((i == 0 && (num5 & 4) != 0) || (i == 1 && (num5 & 4) == 0))
			{
				slopeDiagLog?.Invoke($"[SLOPE/WEDGE_FILTER] probe={i} tentT={num5} REJECT slopeT:={lastSlopeType} (lst)");
				slopeType = lastSlopeType;
				continue;
			}
			int value = num5;
			if ((lastSlopeType & 4) != 0 && (num5 & 4) == 0 && lastSlopeType != 0 && num5 != 0)
			{
				num5 = lastSlopeType;
			}
			slopeType = num5;
			if (num5 != 0)
			{
				lastSlopeType = num5;
			}
			slopeDiagLog?.Invoke($"[SLOPE/WEDGE_FILTER] probe={i} tentT={value} ACCEPT slopeT:={num5} lst:={lastSlopeType}");
		}
		slopeDiagLog?.Invoke($"[SLOPE/WEDGE_OUT] anyWedge={flag} | sF={slopeFrames} swOn={slopeWasOnCounter} sT={slopeType} lst={lastSlopeType} jH={slopeJumpHigher}");
		return flag;
	}

	internal static void UpdateSlopeCounters(ref int slopeWasOnCounter, ref int slopeType, ref int velY_fixed, ref int posY_fixed, int gameMode, bool gravFlipped, bool mini, ref int lastSlopeType)
	{
		Action<string> slopeDiagLog = SlopeDiagLog;
		int value = slopeWasOnCounter;
		int num = slopeType;
		int num2 = lastSlopeType;
		int value2 = velY_fixed;
		int value3 = posY_fixed;
		if (slopeWasOnCounter > 0)
		{
			slopeWasOnCounter--;
			if (slopeWasOnCounter == 0)
			{
				int num3 = (((slopeType & 8) != 0) ? 1 : 0) | (mini ? 2 : 0) | 4;
				int num4 = 0;
				string value4 = "NONE";
				switch (gameMode)
				{
				case 2:
				{
					int num6 = slopeType & 0xF;
					if (num6 == 6 || num6 == 14)
					{
						num4 = EXIT_SLOPE_BALL_22[num3];
						value4 = "BALL_22";
					}
					else if (num6 == 7 || num6 == 15)
					{
						num4 = EXIT_SLOPE_BALL_66[num3];
						value4 = "BALL_66";
					}
					break;
				}
				case 0:
				{
					int num5 = slopeType & 0xF;
					if (num5 == 6 || num5 == 14)
					{
						num4 = EXIT_SLOPE_CUBE_22[num3];
						value4 = "CUBE_22";
					}
					break;
				}
				}
				velY_fixed += num4;
				posY_fixed += num4;
				slopeDiagLog?.Invoke($"[SLOPE/EXIT] swOn:{value}->0 sT={num} gm={gameMode} mini={mini} gF={gravFlipped} tblIdx={num3} tbl={value4} delta={num4} vy:{value2}->{velY_fixed} py:{value3}->{posY_fixed} sT:=0");
				slopeType = 0;
			}
			else
			{
				slopeDiagLog?.Invoke($"[SLOPE/COUNTER_DEC] swOn:{value}->{slopeWasOnCounter} sT={slopeType} (no exit yet)");
			}
		}
		else
		{
			lastSlopeType = 0;
			slopeType = 0;
			if (slopeDiagLog != null && (num != 0 || num2 != 0))
			{
				slopeDiagLog($"[SLOPE/CLEAR] swOn=0 -> lst:{num2}->0 sT:{num}->0");
			}
		}
	}

	internal static void UpdateSlopeCountersFresh(ref int slopeFrames, int slopeType, ref int velY_fixed, int velX_fixed)
	{
		Action<string> slopeDiagLog = SlopeDiagLog;
		int value = slopeFrames;
		int value2 = velY_fixed;
		if (slopeFrames > 0)
		{
			slopeFrames--;
			if (slopeType != 0)
			{
				ApplySlopeVelocity(ref velY_fixed, slopeType, velX_fixed);
				slopeDiagLog?.Invoke($"[SLOPE/FRESH_APPLY] sF:{value}->{slopeFrames} sT={slopeType} vx={velX_fixed} vy:{value2}->{velY_fixed}");
			}
			else
			{
				slopeDiagLog?.Invoke($"[SLOPE/FRESH_DEC] sF:{value}->{slopeFrames} sT=0 (no apply)");
			}
		}
		else if (slopeDiagLog != null && slopeType != 0)
		{
			slopeDiagLog($"[SLOPE/FRESH_NOP] sF=0 sT={slopeType} (no-op)");
		}
	}

	internal static void ApplySlopeVelocity(ref int velY_fixed, int slopeType, int velX_fixed)
	{
		if (slopeType != 0)
		{
			int value = velY_fixed;
			int num = slopeType & 3;
			int num2;
			switch (num)
			{
			default:
				return;
			case 2:
				num2 = velX_fixed >> 1;
				break;
			case 1:
				num2 = velX_fixed;
				break;
			case 3:
				num2 = velX_fixed << 1;
				break;
			}
			bool flag = (slopeType & 4) != 0;
			bool flag2 = (slopeType & 8) != 0;
			velY_fixed = ((flag == flag2) ? num2 : (-num2));
			SlopeDiagLog?.Invoke($"[SLOPE/VEL] sT={slopeType} deg={num} rising={flag} ud={flag2} vx={velX_fixed} component={num2} vy:{value}->{velY_fixed}");
		}
	}

	internal static void SlopeJumpCheck(ref int velY_fixed, ref bool slopeJumpHigher, int slopeType, bool mini, bool gravFlipped)
	{
		if (slopeJumpHigher)
		{
			Action<string> slopeDiagLog = SlopeDiagLog;
			int value = velY_fixed;
			if ((slopeType & 3) != 2)
			{
				velY_fixed += (gravFlipped ? 128 : (-128));
				slopeDiagLog?.Invoke($"[SLOPE/JUMP_BOOST] sT={slopeType} mini={mini} gF={gravFlipped} boost={(gravFlipped ? 128 : (-128))} vy:{value}->{velY_fixed} jH->false");
			}
			else
			{
				slopeDiagLog?.Invoke($"[SLOPE/JUMP_NOBOOST] sT={slopeType} (22deg, no boost) jH->false");
			}
			slopeJumpHigher = false;
		}
	}

	internal static EjectResult CubeEject(in CollisionMap map, int playerX_fixed, int playerY_fixed, int velY_fixed, int velX_fixed, bool gravFlipped, bool mini, int gameMode, bool inputHeld, int slopeWasOnCounter, int slopeFrames, int slopeType, bool slopeJumpHigher, int lastSlopeType, int camY_fixed = 0)
	{
		PfTrace.Event("SP.CubeEject.in", $"X={playerX_fixed >> 8} Y={playerY_fixed >> 8} vy={velY_fixed} vx={velX_fixed} gF={(gravFlipped ? 1 : 0)} mini={(mini ? 1 : 0)} mode={gameMode} sWOn={slopeWasOnCounter} sF={slopeFrames} sT={slopeType} lst={lastSlopeType}");
		EjectResult result = new EjectResult
		{
			NewY_fixed = playerY_fixed,
			NewVelY_fixed = velY_fixed,
			SlopeType = slopeType,
			SlopeFrames = slopeFrames,
			SlopeWasOnCounter = slopeWasOnCounter,
			SlopeJumpHigher = slopeJumpHigher,
			LastSlopeType = lastSlopeType
		};
		int num = playerX_fixed >> 8;
		int num2 = playerY_fixed >> 8;
		int num3 = (playerY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
		int cubeHitboxW = GetCubeHitboxW(mini);
		int cubeHitboxH = GetCubeHitboxH(mini);
		int hitboxOffsetY = GetHitboxOffsetY(gameMode, mini, gravFlipped);
		UpdateSlopeCounters(ref result.SlopeWasOnCounter, ref result.SlopeType, ref result.NewVelY_fixed, ref result.NewY_fixed, gameMode, gravFlipped, mini, ref result.LastSlopeType);
		if (!gravFlipped)
		{
			int num4 = (mini ? (16 - cubeHitboxH >> 1) : 0);
			int checkBaseY = num2 + num4 + cubeHitboxH - 2;
			var (flag, num5, num6) = CheckSlopesDown(in map, num, num, checkBaseY, cubeHitboxW, inputHeld, gameMode, gravFlipped, velX_fixed, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			if (flag)
			{
				result.NewY_fixed = ((result.NewY_fixed >> 8) - num5 << 8) | (camY_fixed & 0xFF);
				result.NewVelY_fixed = 0;
				result.WasZeroed = true;
				result.SlopeType = num6;
				result.OnGround = true;
				result.DebugFloorSlopeHit = true;
				if (num6 != 0)
				{
					if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
					{
						result.SlopeJumpHigher = true;
					}
					else
					{
						result.SlopeFrames = 1;
						result.SlopeWasOnCounter = 3;
					}
				}
			}
			else if (result.NewVelY_fixed >= 0)
			{
				(bool hit, int surfaceY, bool spikeDeath) tuple2 = CheckFloor(in map, num, num3 + hitboxOffsetY, cubeHitboxW, cubeHitboxH);
				var (flag2, num7, _) = tuple2;
				if (tuple2.spikeDeath)
				{
					result.Died = true;
					result.DebugFloorSpike = true;
					return result;
				}
				if (flag2)
				{
					result.NewY_fixed = (num7 - cubeHitboxH - hitboxOffsetY << 8) | (camY_fixed & 0xFF);
					result.NewVelY_fixed = 0;
					result.WasZeroed = true;
					result.OnGround = true;
					result.DebugFloorTileHit = true;
				}
			}
		}
		else
		{
			int num8 = 16 - cubeHitboxH >> 1;
			int checkBaseY2 = num2 + num8 + (mini ? 1 : 2) + ((gameMode == 1) ? 1 : 0);
			var (flag3, num9, num10) = CheckSlopesUp(in map, num, num, checkBaseY2, cubeHitboxW, inputHeld, gameMode, gravFlipped, velX_fixed, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			if (flag3)
			{
				result.NewY_fixed = ((result.NewY_fixed >> 8) + num9 << 8) | (camY_fixed & 0xFF);
				result.NewVelY_fixed = 0;
				result.WasZeroed = true;
				result.OnGround = true;
				result.SlopeType = num10;
				if (num10 != 0)
				{
					if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
					{
						result.SlopeJumpHigher = true;
					}
					else
					{
						result.SlopeFrames = 1;
						result.SlopeWasOnCounter = 3;
					}
				}
			}
			else if (result.NewVelY_fixed <= 0)
			{
				(bool hit, int ceilingBottomY, bool spikeDeath, MetatileCollision hitCollision) tuple5 = CheckCeiling(in map, num, num3 + hitboxOffsetY, cubeHitboxW, cubeHitboxH);
				var (flag4, num11, _, _) = tuple5;
				if (tuple5.spikeDeath)
				{
					result.Died = true;
					result.DebugFloorSpike = true;
					return result;
				}
				if (flag4)
				{
					result.NewY_fixed = (num11 - hitboxOffsetY - 1 << 8) | (camY_fixed & 0xFF);
					result.NewVelY_fixed = 0;
					result.WasZeroed = true;
					result.OnGround = true;
				}
			}
		}
		return result;
	}

	internal static EjectResult BallEject(in CollisionMap map, int playerX_fixed, int playerY_fixed, int velY_fixed, int velX_fixed, bool gravFlipped, bool mini, int gameMode, bool inputHeld, int slopeWasOnCounter, int slopeFrames, int slopeType, bool slopeJumpHigher, int lastSlopeType, int camY_fixed = 0)
	{
		PfTrace.Event("SP.BallEject.in", $"X={playerX_fixed >> 8} Y={playerY_fixed >> 8} vy={velY_fixed} vx={velX_fixed} gF={(gravFlipped ? 1 : 0)} mini={(mini ? 1 : 0)} mode={gameMode} sWOn={slopeWasOnCounter} sF={slopeFrames} sT={slopeType} lst={lastSlopeType}");
		EjectResult result = new EjectResult
		{
			NewY_fixed = playerY_fixed,
			NewVelY_fixed = velY_fixed,
			SlopeType = slopeType,
			SlopeFrames = slopeFrames,
			SlopeWasOnCounter = slopeWasOnCounter,
			SlopeJumpHigher = slopeJumpHigher,
			LastSlopeType = lastSlopeType
		};
		int num = playerX_fixed >> 8;
		int num2 = playerY_fixed >> 8;
		int num3 = (playerY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
		int cubeHitboxW = GetCubeHitboxW(mini);
		int cubeHitboxH = GetCubeHitboxH(mini);
		int miniCenterOffsetY = GetMiniCenterOffsetY(mini);
		int num4 = ((!gravFlipped) ? 1 : (-1));
		int num5 = num3 + miniCenterOffsetY + num4;
		UpdateSlopeCounters(ref result.SlopeWasOnCounter, ref result.SlopeType, ref result.NewVelY_fixed, ref result.NewY_fixed, gameMode, gravFlipped, mini, ref result.LastSlopeType);
		Action<string> ballEjectDiagLog = BallEjectDiagLog;
		if (ballEjectDiagLog != null)
		{
			int num6 = camY_fixed & 0xFF;
			int num7 = playerY_fixed & 0xFF;
			int value = (playerY_fixed - camY_fixed) & 0xFF;
			int value2 = playerY_fixed - camY_fixed >> 8;
			int value3 = ((num7 < num6) ? 1 : 0);
			ballEjectDiagLog($"[BE_ENTRY] Yf=0x{playerY_fixed:X8} Yhi={playerY_fixed >> 8} Ylo=0x{num7:X2} camYf=0x{camY_fixed:X8} camHi={camY_fixed >> 8} camLo=0x{num6:X2} scrHi={value2} scrLo=0x{value:X2} borrow={value3} Ypx_nes={num3} collY={num5} velY={velY_fixed} gravF={gravFlipped} mini={mini} gm={gameMode} input={inputHeld} slopeT={result.SlopeType}/{result.SlopeFrames}");
		}
		int num8 = camY_fixed >> 8;
		int num9 = (result.NewY_fixed - camY_fixed) & 0xFF;
		if (gravFlipped)
		{
			bool flag = false;
			int num10 = 16 - cubeHitboxH >> 1;
			int num11 = num3 + num4 + num10 + (mini ? 1 : 2) + ((gameMode == 1) ? 1 : 0);
			var (flag2, num12, num13) = CheckSlopesUp(in map, num, num, num11, cubeHitboxW, inputHeld, gameMode, gravFlipped, velX_fixed, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			if (flag2)
			{
				int num14 = result.NewY_fixed - camY_fixed >> 8;
				int num15 = num14 + num12;
				result.NewY_fixed = camY_fixed + (num15 << 8) + num9;
				result.NewVelY_fixed = 0;
				result.OnGround = true;
				result.SlopeType = num13;
				if (num13 != 0)
				{
					if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
					{
						result.SlopeJumpHigher = true;
					}
					else
					{
						result.SlopeFrames = 1;
						result.SlopeWasOnCounter = 3;
					}
				}
				flag = true;
				ballEjectDiagLog?.Invoke($"[BE_CEIL_SLOPE] probeY={num11} eject={num12} sT={num13} scrHi:{num14}->{num15} newYf=0x{result.NewY_fixed:X8} ({result.NewY_fixed >> 8}px)");
			}
			if (!flag2 && result.NewVelY_fixed < 0)
			{
				var (flag3, value4, flag4, metatileCollision) = CheckCeiling(in map, num, num5, cubeHitboxW, cubeHitboxH);
				ballEjectDiagLog?.Invoke($"[BE_CEIL] probeX={num} probeY={num5} hbW={cubeHitboxW} hbH={cubeHitboxH} -> hit={flag3} ceilBotY={value4} spike={flag4} coll={metatileCollision}");
				if (flag4)
				{
					result.Died = true;
					return result;
				}
				if (flag3)
				{
					flag = true;
					int num16 = num5 + 1;
					int num17 = (num16 % 16 + 16) % 16;
					bool flag5 = metatileCollision == MetatileCollision.COL_NO_SIDE || metatileCollision == MetatileCollision.COL_ALL || metatileCollision == MetatileCollision.COL_FLOOR_CEIL;
					int num18 = (flag5 ? 240 : 248) | num17;
					int num19 = (sbyte)(byte)num18;
					int num20 = result.NewY_fixed - camY_fixed >> 8;
					int num21 = num20 - num19;
					result.NewY_fixed = camY_fixed + (num21 << 8) + num9;
					result.NewVelY_fixed = 0;
					result.OnGround = true;
					ballEjectDiagLog?.Invoke($"[BE_CEIL_EJECT] probeY_U={num16} tmp8_u={num17} udChecks={flag5} ejectU=0x{num18:X2} signed={num19} scrHi:{num20}->{num21} newYf=0x{result.NewY_fixed:X8} ({result.NewY_fixed >> 8}px)");
				}
			}
			if (flag)
			{
				int num22 = num3 + num4 + cubeHitboxH + miniCenterOffsetY;
				int num23 = ((num22 >= 0) ? (num22 / 16) : ((num22 - 16 + 1) / 16));
				int num24 = num23 + map.GroundRowsToReserve;
				bool flag6 = false;
				if (num24 >= 0 && num24 < map.MapHeight)
				{
					int num25 = num;
					int num26 = num + cubeHitboxW;
					for (int i = 0; i < 3; i++)
					{
						if (flag6)
						{
							break;
						}
						int num27 = i switch
						{
							1 => num25 + (cubeHitboxW >> 1), 
							0 => num25, 
							_ => num26, 
						};
						int num28 = num27 / 16;
						if (num28 < 0 || num28 >= map.MapWidth)
						{
							continue;
						}
						int num29 = num24 * map.MapWidth + num28;
						if (num29 < 0 || num29 >= map.Tiles.Length)
						{
							continue;
						}
						int tid = map.Tiles[num29];
						int num30 = MapTileForCollision(tid);
						MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num30);
						if (collision != MetatileCollision.COL_NONE)
						{
							int localX = (num27 % 16 + 16) % 16;
							int localY = (num22 % 16 + 16) % 16;
							if (TileOccupiesPixel(collision, localX, localY))
							{
								flag6 = true;
							}
						}
					}
				}
				if (flag6)
				{
					int num31 = (num22 % 16 + 16) % 16;
					int num32 = num31;
					int num33 = result.NewY_fixed - camY_fixed >> 8;
					int num34 = num33 - num32;
					result.NewY_fixed = camY_fixed + (num34 << 8) + num9;
					result.NewVelY_fixed = 0;
					result.OnGround = true;
					ballEjectDiagLog?.Invoke($"[BE_CEIL_D] probeY_D={num22} tmp8_d={num31} scrHi:{num33}->{num34} newYf=0x{result.NewY_fixed:X8} ({result.NewY_fixed >> 8}px)");
				}
			}
			int num35 = (mini ? (16 - cubeHitboxH >> 1) : 0);
			int num36 = num3 + num4 + num35 + cubeHitboxH - 2;
			var (flag7, num37, num38) = CheckSlopesDown(in map, num, num, num36, cubeHitboxW, inputHeld, gameMode, gravFlipped, velX_fixed, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			if (flag7)
			{
				int num39 = result.NewY_fixed - camY_fixed >> 8;
				int num40 = num39 - num37;
				result.NewY_fixed = camY_fixed + (num40 << 8) + num9;
				result.NewVelY_fixed = 0;
				result.OnGround = true;
				result.SlopeType = num38;
				if (num38 != 0)
				{
					if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
					{
						result.SlopeJumpHigher = true;
					}
					else
					{
						result.SlopeFrames = 1;
						result.SlopeWasOnCounter = 3;
					}
				}
				ballEjectDiagLog?.Invoke($"[BE_FLOOR_SLOPE_F] probeY={num36} eject={num37} sT={num38} scrHi:{num39}->{num40} newYf=0x{result.NewY_fixed:X8} ({result.NewY_fixed >> 8}px)");
			}
			else if (num38 != 0 && result.SlopeWasOnCounter > 0)
			{
				result.SlopeType = num38;
			}
			ballEjectDiagLog?.Invoke($"[BE_EXIT_U] outYf=0x{result.NewY_fixed:X8} ({result.NewY_fixed >> 8}px) outVelY={result.NewVelY_fixed} onG={result.OnGround} hadUpHit={flag} slopeU={flag2} slopeD={flag7} sF={result.SlopeFrames} sT={result.SlopeType} swOn={result.SlopeWasOnCounter}");
		}
		else
		{
			int num41 = (mini ? (16 - cubeHitboxH >> 1) : 0);
			int checkBaseY = num3 + num4 + num41 + cubeHitboxH - 2;
			int num42 = num3 + num4 + (16 - cubeHitboxH >> 1) + (mini ? 1 : 2) + ((gameMode == 1) ? 1 : 0);
			int num43 = playerX_fixed >> 8;
			ApplySlopeWedgeStateOnly(in map, num43, num43, num42, cubeHitboxW, inputHeld, gameMode, gravFlipped, ref result.SlopeFrames, ref result.SlopeWasOnCounter, ref result.SlopeType, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			var (flag8, num44, num45) = CheckSlopesUp(in map, num, num43, num42, cubeHitboxW, inputHeld, gameMode, gravFlipped, velX_fixed, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			if (flag8)
			{
				int num46 = result.NewY_fixed - camY_fixed >> 8;
				int num47 = num46 + num44;
				result.NewY_fixed = camY_fixed + (num47 << 8) + num9;
				result.NewVelY_fixed = 0;
				result.OnGround = true;
				result.SlopeType = num45;
				if (num45 != 0)
				{
					if (inputHeld && (gameMode == 0 || gameMode == 4 || gameMode == 8))
					{
						result.SlopeJumpHigher = true;
					}
					else
					{
						result.SlopeFrames = 1;
						result.SlopeWasOnCounter = 3;
					}
				}
				ballEjectDiagLog?.Invoke($"[BE_CEIL_SLOPE_N] probeY={num42} eject={num44} sT={num45} scrHi:{num46}->{num47} newYf=0x{result.NewY_fixed:X8} ({result.NewY_fixed >> 8}px)");
			}
			ApplySlopeWedgeStateOnly(in map, num43, num43, checkBaseY, cubeHitboxW, inputHeld, gameMode, gravFlipped, ref result.SlopeFrames, ref result.SlopeWasOnCounter, ref result.SlopeType, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			var (flag9, num48, slopeType2) = CheckSlopesDown(in map, num43, num43, checkBaseY, cubeHitboxW, inputHeld, gameMode, gravFlipped, velX_fixed, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			if (flag9)
			{
				if (num48 > 0)
				{
					int num49 = num8 + (result.NewY_fixed - camY_fixed >> 8);
					int num50 = num49 - num48 - num8;
					result.NewY_fixed = camY_fixed + (num50 << 8) + num9;
				}
				result.NewVelY_fixed = 0;
				result.SlopeFrames = 1;
				result.SlopeWasOnCounter = 3;
				result.SlopeType = slopeType2;
				result.OnGround = true;
			}
			else if (result.NewVelY_fixed >= 0)
			{
				var (flag10, num51, flag11) = CheckFloor(in map, num, num5, cubeHitboxW, cubeHitboxH);
				if (ballEjectDiagLog != null)
				{
					int num52 = num5 + cubeHitboxH;
					int num53 = num52 / 16;
					int num54 = num53 + map.GroundRowsToReserve;
					string text = $"pb={num52} tby={num53} arrY={num54} hbH={cubeHitboxH} grdRsv={map.GroundRowsToReserve}";
					if (num54 >= 0 && num54 < map.MapHeight)
					{
						int num55 = num / 16;
						int num56 = (num + (cubeHitboxW >> 1)) / 16;
						int num57 = (num + cubeHitboxW) / 16;
						int num58 = ((num55 >= 0 && num55 < map.MapWidth) ? map.Tiles[num54 * map.MapWidth + num55] : (-1));
						int num59 = ((num56 >= 0 && num56 < map.MapWidth) ? map.Tiles[num54 * map.MapWidth + num56] : (-1));
						int num60 = ((num57 >= 0 && num57 < map.MapWidth) ? map.Tiles[num54 * map.MapWidth + num57] : (-1));
						MetatileCollision collision2 = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(num58));
						MetatileCollision collision3 = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(num59));
						MetatileCollision collision4 = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(num60));
						text += $" L(tx={num55},id={num58},{collision2}) M(tx={num56},id={num59},{collision3}) R(tx={num57},id={num60},{collision4})";
					}
					ballEjectDiagLog($"[BE_FLOOR] probeX={num} probeY={num5} -> hit={flag10} surfY={num51} spike={flag11} | {text}");
				}
				if (flag11)
				{
					result.Died = true;
					return result;
				}
				if (flag10)
				{
					int num61 = num51 - cubeHitboxH - miniCenterOffsetY - num4 - num8;
					result.NewY_fixed = camY_fixed + (num61 << 8) + num9;
					result.NewVelY_fixed = 0;
					result.OnGround = true;
				}
			}
			ballEjectDiagLog?.Invoke($"[BE_EXIT_D] outYf=0x{result.NewY_fixed:X8} ({result.NewY_fixed >> 8}px) outVelY={result.NewVelY_fixed} onG={result.OnGround} slopeHit={flag9} slopeT={result.SlopeType}");
		}
		return result;
	}

	internal static EjectResult ShipUfoEject(in CollisionMap map, int playerX_fixed, int playerY_fixed, int velY_fixed, int velX_fixed, bool gravFlipped, bool mini, int gameMode, bool inputHeld, int slopeWasOnCounter, int slopeFrames, int slopeType, bool slopeJumpHigher, int lastSlopeType, int camY_fixed = 0)
	{
		PfTrace.Event("SP.ShipUfoEject.in", $"X={playerX_fixed >> 8} Y={playerY_fixed >> 8} vy={velY_fixed} vx={velX_fixed} gF={(gravFlipped ? 1 : 0)} mini={(mini ? 1 : 0)} mode={gameMode} sWOn={slopeWasOnCounter} sF={slopeFrames} sT={slopeType} lst={lastSlopeType}");
		EjectResult result = new EjectResult
		{
			NewY_fixed = playerY_fixed,
			NewVelY_fixed = velY_fixed,
			SlopeType = slopeType,
			SlopeFrames = slopeFrames,
			SlopeWasOnCounter = slopeWasOnCounter,
			SlopeJumpHigher = slopeJumpHigher,
			LastSlopeType = lastSlopeType
		};
		int num = playerX_fixed >> 8;
		int num2 = playerY_fixed >> 8;
		int num3 = (playerY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
		int cubeHitboxW = GetCubeHitboxW(mini);
		int cubeHitboxH = GetCubeHitboxH(mini);
		int hitboxOffsetY = GetHitboxOffsetY(gameMode, mini, gravFlipped);
		int collX = num;
		int collY = num3 + hitboxOffsetY;
		UpdateSlopeCounters(ref result.SlopeWasOnCounter, ref result.SlopeType, ref result.NewVelY_fixed, ref result.NewY_fixed, gameMode, gravFlipped, mini, ref result.LastSlopeType);
		int num4 = (mini ? (16 - cubeHitboxH >> 1) : 0);
		int num5 = 16 - cubeHitboxH >> 1;
		bool flag = result.NewVelY_fixed < 0;
		bool flag2 = result.NewVelY_fixed >= 0;
		if (!gravFlipped)
		{
			bool flag3 = false;
			int checkBaseY = num3 + num5 + (mini ? 1 : 2) + ((gameMode == 1) ? 1 : 0);
			var (flag4, num6, slopeType2) = CheckSlopesUp(in map, num, num, checkBaseY, cubeHitboxW, inputHeld, gameMode, gravFlipped, velX_fixed, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			if (flag4)
			{
				flag3 = true;
				result.DebugCeilSlopeHit = true;
				int num7 = (result.NewY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
				int num8 = num7 + num6 - 1;
				int num9 = (result.NewY_fixed - camY_fixed) & 0xFF;
				result.NewY_fixed = camY_fixed + (num8 - (camY_fixed >> 8) << 8) + num9;
				result.NewVelY_fixed = 0;
				result.SlopeFrames = 1;
				result.SlopeWasOnCounter = 3;
				result.SlopeType = slopeType2;
			}
			else if (flag)
			{
				var (flag5, num10, flag6, metatileCollision) = CheckCeiling(in map, collX, collY, cubeHitboxW, cubeHitboxH);
				if (flag6)
				{
					result.DebugCeilSpike = true;
					result.Died = true;
					return result;
				}
				if (flag5)
				{
					result.DebugCeilTileHit = true;
					flag3 = true;
					int num11 = (result.NewY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
					int num16;
					if (gameMode == 1 || gameMode == 3)
					{
						int num12 = num11 + hitboxOffsetY + 1;
						int num13 = (num12 % 16 + 16) % 16;
						int num14 = ((metatileCollision == MetatileCollision.COL_NO_SIDE || metatileCollision == MetatileCollision.COL_ALL || metatileCollision == MetatileCollision.COL_FLOOR_CEIL) ? 240 : 248) | num13;
						int num15 = (sbyte)(byte)num14;
						num16 = num11 - num15 - 1;
					}
					else
					{
						num16 = num10 - hitboxOffsetY - 2;
					}
					int num17 = (result.NewY_fixed - camY_fixed) & 0xFF;
					result.NewY_fixed = camY_fixed + (num16 - (camY_fixed >> 8) << 8) + num17;
					result.NewVelY_fixed = 0;
				}
			}
			flag = result.NewVelY_fixed < 0;
			flag2 = result.NewVelY_fixed >= 0;
			int checkBaseY2 = num3 + num4 + cubeHitboxH - 2;
			var (flag7, num18, num19) = CheckSlopesDown(in map, num, num, checkBaseY2, cubeHitboxW, inputHeld, gameMode, gravFlipped, velX_fixed, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			if (num19 != 0)
			{
				result.SlopeType = num19;
			}
			if (flag7)
			{
				result.DebugFloorSlopeHit = true;
				if (num18 > 0)
				{
					int num20 = (result.NewY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
					int num21 = num20 - num18;
					int num22 = (result.NewY_fixed - camY_fixed) & 0xFF;
					result.NewY_fixed = camY_fixed + (num21 - (camY_fixed >> 8) << 8) + num22;
				}
				result.NewVelY_fixed = 0;
				result.SlopeFrames = 1;
				result.SlopeWasOnCounter = 3;
				result.SlopeType = num19;
			}
			else if (flag2)
			{
				var (flag8, num23, flag9, num24, metatileCollision2) = CheckFloorDetailed(in map, collX, collY, cubeHitboxW, cubeHitboxH);
				if (flag9)
				{
					result.DebugFloorSpike = true;
					result.Died = true;
					return result;
				}
				if (flag8)
				{
					result.DebugFloorTileHit = true;
					int num26;
					if (flag3)
					{
						int num25 = (result.NewY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
						num26 = num25 - num24;
					}
					else
					{
						num26 = num23 - cubeHitboxH - hitboxOffsetY;
					}
					int num27 = (result.NewY_fixed - camY_fixed) & 0xFF;
					result.NewY_fixed = camY_fixed + (num26 - (camY_fixed >> 8) << 8) + num27;
					result.NewVelY_fixed = 0;
				}
			}
		}
		else
		{
			bool flag10 = false;
			int checkBaseY3 = num3 + num5 + (mini ? 1 : 2) + ((gameMode == 1) ? 1 : 0);
			var (flag11, num28, num29) = CheckSlopesUp(in map, num, num, checkBaseY3, cubeHitboxW, inputHeld, gameMode, gravFlipped, velX_fixed, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			if (num29 != 0)
			{
				result.SlopeType = num29;
			}
			if (flag11)
			{
				flag10 = true;
				result.DebugCeilSlopeHit = true;
				int num30 = (result.NewY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
				int num31 = num30 + num28 - 1;
				int num32 = (result.NewY_fixed - camY_fixed) & 0xFF;
				result.NewY_fixed = camY_fixed + (num31 - (camY_fixed >> 8) << 8) + num32;
				result.NewVelY_fixed = 0;
				result.SlopeFrames = 1;
				result.SlopeWasOnCounter = 3;
				result.SlopeType = num29;
			}
			else if (flag)
			{
				var (flag12, num33, flag13, metatileCollision3) = CheckCeiling(in map, collX, collY, cubeHitboxW, cubeHitboxH);
				if (flag13)
				{
					result.DebugCeilSpike = true;
					result.Died = true;
					return result;
				}
				if (flag12)
				{
					flag10 = true;
					result.DebugCeilTileHit = true;
					int num34 = (result.NewY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
					int num39;
					if (gameMode == 1 || gameMode == 3)
					{
						int num35 = num34 + hitboxOffsetY + 1;
						int num36 = (num35 % 16 + 16) % 16;
						int num37 = ((metatileCollision3 == MetatileCollision.COL_NO_SIDE || metatileCollision3 == MetatileCollision.COL_ALL || metatileCollision3 == MetatileCollision.COL_FLOOR_CEIL) ? 240 : 248) | num36;
						int num38 = (sbyte)(byte)num37;
						num39 = num34 - num38 - 1;
					}
					else
					{
						num39 = num33 - hitboxOffsetY - 2;
					}
					int num40 = (result.NewY_fixed - camY_fixed) & 0xFF;
					result.NewY_fixed = camY_fixed + (num39 - (camY_fixed >> 8) << 8) + num40;
					result.NewVelY_fixed = 0;
				}
			}
			flag = result.NewVelY_fixed < 0;
			flag2 = result.NewVelY_fixed >= 0;
			int checkBaseY4 = num3 + num4 + cubeHitboxH - 2;
			var (flag14, num41, num42) = CheckSlopesDown(in map, num, num, checkBaseY4, cubeHitboxW, inputHeld, gameMode, gravFlipped, velX_fixed, ref result.LastSlopeType, ref result.SlopeJumpHigher);
			if (num42 != 0)
			{
				result.SlopeType = num42;
			}
			if (flag14)
			{
				result.DebugFloorSlopeHit = true;
				if (num41 > 0)
				{
					int num43 = (result.NewY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
					int num44 = num43 - num41;
					int num45 = (result.NewY_fixed - camY_fixed) & 0xFF;
					result.NewY_fixed = camY_fixed + (num44 - (camY_fixed >> 8) << 8) + num45;
				}
				result.NewVelY_fixed = 0;
				result.SlopeFrames = 1;
				result.SlopeWasOnCounter = 3;
				result.SlopeType = num42;
			}
			else if (flag2)
			{
				var (flag15, num46, flag16, num47, metatileCollision4) = CheckFloorDetailed(in map, collX, collY, cubeHitboxW, cubeHitboxH);
				if (flag16)
				{
					result.DebugFloorSpike = true;
					result.Died = true;
					return result;
				}
				if (flag15)
				{
					result.DebugFloorTileHit = true;
					int num49;
					if (flag10)
					{
						int num48 = (result.NewY_fixed - camY_fixed >> 8) + (camY_fixed >> 8);
						num49 = num48 - num47;
					}
					else
					{
						num49 = num46 - cubeHitboxH - hitboxOffsetY;
					}
					int num50 = (result.NewY_fixed - camY_fixed) & 0xFF;
					result.NewY_fixed = camY_fixed + (num49 - (camY_fixed >> 8) << 8) + num50;
					result.NewVelY_fixed = 0;
				}
			}
		}
		return result;
	}
}

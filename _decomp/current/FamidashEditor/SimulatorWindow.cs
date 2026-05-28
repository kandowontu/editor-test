#define DEBUG
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FamidashEditor;

public class SimulatorWindow : Window, IComponentConnector
{
	private const byte BOTTOM_BLUE_PAD = 13;

	private const byte TOP_BLUE_PAD = 14;

	private const byte BOTTOM_BLUE_PAD_MULTI = 253;

	private const byte TOP_BLUE_PAD_MULTI = 254;

	private int Generic_x = 0;

	private int Generic_y = 0;

	private int Generic_width = 15;

	private int Generic_height = 15;

	private int eject_D = 0;

	private int eject_U = 0;

	private byte collision = 0;

	private int temp_x = 0;

	private int temp_y = 0;

	private int temp_room = 0;

	private int tmp1 = 0;

	private int tmp2 = 0;

	private int tmp3 = 0;

	private int tmp4 = 0;

	private int tmp7 = 0;

	private int tmp8 = 0;

	private const int CUBE_GRAVITY_NORMAL = 107;

	private const int CUBE_GRAVITY_MINI = 111;

	private const int CUBE_MAX_FALLSPEED_NORMAL = 1536;

	private const int CUBE_MAX_FALLSPEED_MINI = 1536;

	private const int JUMP_VEL_NORMAL = -1424;

	private const int JUMP_VEL_MINI = -1232;

	private const int CUBE_HITBOX_W = 15;

	private const int CUBE_HITBOX_H = 15;

	private const int MINI_CUBE_HITBOX_W = 8;

	private const int MINI_CUBE_HITBOX_H = 7;

	private byte currplayer_mini = 0;

	private byte currplayer_gravity = 0;

	private bool wasZeroedByCollisionLastFrame = true;

	private int tmpgravity = 0;

	private int tmpfallspeed = 0;

	private int footballChargeFrames = 0;

	private bool footballOrbed = false;

	private bool footballWasHeld = false;

	private int currplayer_table_idx = 0;

	private int playerVelX_fixed = 708;

	private const byte BLUE_ORB = 5;

	private const byte PINK_ORB = 6;

	private const byte YELLOW_ORB = 11;

	private const byte YELLOW_ORB_BIGGER = 31;

	private const byte GREEN_ORB = 39;

	private const byte RED_ORB = 40;

	private const byte YELLOW_ORB_SMALLER = 41;

	private const byte BLACK_ORB = 68;

	private const byte DASH_ORB = 69;

	private const byte DASH_GRAVITY_ORB = 70;

	private const byte DASH_ORB_45DEG_UP = 76;

	private const byte DASH_GRAVITY_ORB_45DEG_UP = 77;

	private const byte DASH_ORB_45DEG_DOWN = 80;

	private const byte DASH_GRAVITY_ORB_45DEG_DOWN = 81;

	private const byte SPIDER_ORB_UP = 84;

	private const byte SPIDER_ORB_DOWN = 85;

	private const byte SPIDER_PAD_UP = 86;

	private const byte SPIDER_PAD_DOWN = 87;

	private const byte DASH_ORB_UPWARDS = 91;

	private const byte DASH_GRAVITY_ORB_UPWARDS = 92;

	private const byte DASH_ORB_DOWNWARDS = 93;

	private const byte DASH_GRAVITY_ORB_DOWNWARDS = 94;

	private const byte TELEPORT_ORB_ENTER = 89;

	private const byte TELEPORT_ORB_EXIT = 90;

	private const byte WHITE_ORB = 122;

	private const byte BLUE_ORB_MULTI = 123;

	private const byte GREEN_ORB_MULTI = 124;

	private const short PAD_HEIGHT_BLUE_normal = -928;

	private const short PAD_HEIGHT_BLUE_mini = -928;

	private const short ORB_BALL_HEIGHT_BLUE_normal = -416;

	private const short ORB_BALL_HEIGHT_BLUE_mini = -416;

	private Dictionary<int, bool> orbActivated = new Dictionary<int, bool>();

	private HashSet<int>[] playerProcessedOrbs = new HashSet<int>[2]
	{
		new HashSet<int>(),
		new HashSet<int>()
	};

	private bool keyXHeldStartedOnGround = false;

	private volatile bool pathfinderEnabled = false;

	private static bool? _pathfinderUserPref = null;

	private volatile bool pfSimulating = false;

	private List<bool>? pfInputSequence = null;

	private int pfFrameIndex = 0;

	private int pfHoldCounter = 0;

	private bool pfBallHoldContinuation = false;

	private bool pfRawSequenceInput = false;

	private const int PF_BALL_HOLD_FRAMES = 8;

	private const int PF_FRAME_DELAY = 0;

	private long pfTickGeneration = 0L;

	private long pfLastAdvancedTick = -1L;

	private bool pfWasPhantomStep = false;

	private bool pfPrevInjectedPress = false;

	private static readonly object s_resourceCacheLock = new object();

	private static Dictionary<string, BitmapImage?>? s_resourceImageCache;

	private static string[]? s_resourceNames;

	private static readonly string[] s_cubeFrameNames = new string[7] { "cube_00_frame_0_upright.png", "cube_01_frame_1_45cw.png", "cube_02_frame_2_90cw_side.png", "cube_03_frame_3_135cw.png", "cube_04_frame_4_180_upside.png", "cube_05_frame_5_225cw.png", "cube_06_frame_6_270cw_opposite.png" };

	private static readonly string[] s_ninjaFrameNames = new string[7] { "ninja_00_frame_0.png", "ninja_01_frame_1.png", "ninja_02_frame_2.png", "ninja_03_frame_3.png", "ninja_04_frame_4.png", "ninja_05_frame_5.png", "ninja_06_frame_6.png" };

	private static readonly HashSet<int> s_padDownIds = new HashSet<int> { 82, 10, 13, 37, 253 };

	private int currplayer = 0;

	private string baseWindowTitle = "Simulator";

	private bool hideTriggerSprites = false;

	private int startingSpeedUiIndex = 1;

	private readonly List<string> simDebugBuffer = new List<string>();

	private const int SIM_DEBUG_BUFFER_MAX = 4096;

	private readonly bool simDebugWriteToFile = true;

	private readonly string simDebugLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"famidash_sim_debug_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");

	private static readonly object simDebugFileLock = new object();

	private string _levelName = "";

	private bool _levelNameLogged;

	private static readonly byte[] drawcube_sprite_table = new byte[24]
	{
		0, 1, 2, 3, 4, 5, 6, 133, 132, 131,
		130, 129, 192, 193, 194, 195, 196, 197, 198, 69,
		68, 67, 66, 65
	};

	private static readonly int[] drawcube_rounding_table = new int[13]
	{
		0, -1, -2, 3, 2, 1, 0, -1, -2, 3,
		2, 1, -24
	};

	private static readonly int[] s_footballFlipTable = new int[24]
	{
		0, 1, 2, 3, 4, 5, 6, 133, 132, 131,
		130, 129, 192, 193, 194, 195, 196, 197, 198, 69,
		68, 67, 66, 65
	};

	private static readonly int[] sprite_heights = new int[256]
	{
		52, 52, 52, 52, 52, 18, 18, 255, 40, 40,
		3, 18, 3, 3, 3, 255, 14, 14, 14, 14,
		36, 36, 36, 52, 52, 52, 255, 255, 255, 255,
		255, 18, 36, 36, 52, 52, 52, 3, 3, 18,
		18, 18, 254, 254, 254, 254, 254, 254, 254, 254,
		254, 254, 254, 254, 254, 254, 254, 254, 254, 254,
		254, 254, 254, 254, 254, 254, 254, 254, 18, 18,
		18, 40, 40, 254, 254, 52, 18, 18, 48, 255,
		18, 18, 3, 3, 18, 18, 3, 3, 52, 16,
		255, 18, 18, 18, 18, 52, 52, 52, 52, 52,
		52, 2, 16, 255, 16, 255, 52, 52, 52, 32,
		8, 255, 255, 255, 255, 255, 255, 16, 255, 16,
		255, 18, 18, 18, 18, 255, 255, 255, 253, 253,
		253, 253, 253, 253, 253, 253, 253, 253, 253, 253,
		253, 0, 255, 253, 253, 253, 253, 253, 253, 253,
		253, 253, 253, 253, 253, 253, 253, 0, 255, 253,
		253, 253, 253, 253, 253, 253, 253, 253, 253, 253,
		253, 253, 253, 0, 253, 252, 252, 252, 252, 252,
		252, 252, 252, 252, 252, 252, 252, 252, 252, 252,
		252, 252, 253, 253, 253, 253, 253, 253, 253, 253,
		253, 253, 253, 253, 253, 0, 0, 253, 253, 253,
		253, 253, 253, 253, 253, 253, 253, 253, 253, 253,
		253, 255, 255, 0, 253, 253, 253, 253, 253, 253,
		253, 253, 253, 253, 253, 253, 253, 255, 0, 0,
		255, 255, 255, 255, 255, 255, 16, 16, 16, 16,
		31, 16, 16, 3, 3, 0
	};

	private static readonly int[] sprite_widths = new int[256]
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

	private static readonly int[] sprite_x_offset = new int[256]
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

	private static readonly int[] sprite_y_offset = new int[256]
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

	private static readonly int[][] PadOrbHeights = SharedPhysics.PadOrbHeights;

	private static readonly int[][] PadOrbHeights_Mini = SharedPhysics.PadOrbHeights_Mini;

	private List<Rectangle> hitboxPool = new List<Rectangle>();

	private int hitboxesInUse = 0;

	private List<Rectangle> tileHitboxPool = new List<Rectangle>();

	private int tileHitboxesInUse = 0;

	private Dictionary<int, (int left, int top, int right, int bottom, int frame)> hitboxWorldCache = new Dictionary<int, (int, int, int, int, int)>();

	private int renderFrameCounter = 0;

	private int cubeRotate_fixed = 0;

	private int shipRotate_fixed = 0;

	private int swingcopterRotate_fixed = 0;

	private int footballRotate_fixed = 0;

	private int[] player_cubeRotate = new int[2];

	private int[] player_cubeRotateMini = new int[2];

	private int[] player_shipRotate = new int[2];

	private int[] player_swingRotate = new int[2];

	private int[] player_footballRotate = new int[2];

	private static readonly int[] DrawcubeRoundingTable = new int[13]
	{
		0, -1, -2, 3, 2, 1, 0, -1, -2, 3,
		2, 1, -24
	};

	private static readonly int[] DrawcubeSpriteTable = new int[24]
	{
		0, 1, 2, 3, 4, 5, 6, 133, 132, 131,
		130, 129, 192, 193, 194, 195, 196, 197, 198, 69,
		68, 67, 66, 65
	};

	private int cubeRotateMini_fixed = 0;

	private static readonly int[] DrawcubeMiniSpriteTable = new int[24]
	{
		0, 1, 1, 2, 2, 3, 3, 4, 4, 0,
		1, 1, 2, 2, 3, 3, 4, 4, 0, 1,
		1, 2, 2, 3
	};

	private List<(int x, int y)> recordedPlayerPath = new List<(int, int)>();

	private List<(int x, int y)> recordedPlayer2Path = new List<(int, int)>();

	private bool _prevDualActiveForP2Path;

	private const int INTERACTION_LINE_FIXED = 20480;

	private int playerX_fixed = 0;

	private int playerY_fixed = 0;

	private int[] player_x_fixed = new int[2];

	private int[] player_y_fixed = new int[2];

	private int[] player_vel_y_fixed = new int[2];

	private bool[] player_mini = new bool[2];

	private byte[] player_gravity = new byte[2];

	private bool[] player_wasZeroed = new bool[2] { true, false };

	private bool[] player_onGround = new bool[2] { true, false };

	private int[] player_groundStabilize = new int[2];

	private bool dual = false;

	private bool singlePortalExitPending = false;

	private bool applyPlayer2Colors = false;

	private Dictionary<string, BitmapSource> playerColorCache = new Dictionary<string, BitmapSource>();

	private Dictionary<string, BitmapSource> player2ColorCache = new Dictionary<string, BitmapSource>();

	private bool twoplayer = false;

	private int playerVisualWidth = 16;

	private int playerVisualHeight = 16;

	private bool playbackStartPending = false;

	private volatile bool restartInProgress = false;

	private volatile bool windowClosed = false;

	private Image? playerImage = null;

	private Rectangle? playerRect = null;

	private Image? player2Image = null;

	private Rectangle? player2Rect = null;

	private Image?[] trailGhosts = new Image[3];

	private int interactionScreenOffset_px = -1;

	private readonly int[] tiles;

	private readonly int[] sprites;

	private readonly int[] nonEmptySpriteIndices;

	private readonly int mapWidth;

	private readonly int mapHeight;

	private readonly ImageSource?[]? tileImages;

	private ImageSource?[]? tileTonedImages;

	private readonly ImageSource?[]? spriteImages;

	private readonly Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets;

	private readonly Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors;

	private Color backgroundTint;

	private Color groundTint;

	private bool backgroundForceSolidBlack = false;

	private readonly Color playerPlaceholderGreen = Color.FromArgb(byte.MaxValue, byte.MaxValue, 50, 43);

	private Color tileTint;

	private readonly Color playerTint;

	private readonly bool playerTintEnabled;

	private readonly bool forcePreviewMode;

	private readonly bool hideColorTriggers;

	private readonly int gridRenderShiftYPx;

	private readonly Dictionary<int, ImageSource?>? previewSpriteMap;

	private ImageSource? chainUpsideSimImage = null;

	private readonly Dictionary<int, ImageSource?[]>? animationFrames;

	private ImageSource?[]? sawFrame1TilesTinted;

	private ImageSource?[]? sawFrame2TilesTinted;

	private ImageSource?[]? smallSawFrame1TilesTinted;

	private ImageSource?[]? smallSawFrame2TilesTinted;

	private ImageSource?[]? largeSawFrame1TilesTinted;

	private ImageSource?[]? largeSawFrame2TilesTinted;

	private ImageSource?[]? sawFrame1TilesOrig;

	private ImageSource?[]? sawFrame2TilesOrig;

	private ImageSource?[]? smallSawFrame1TilesOrig;

	private ImageSource?[]? smallSawFrame2TilesOrig;

	private ImageSource?[]? largeSawFrame1TilesOrig;

	private ImageSource?[]? largeSawFrame2TilesOrig;

	private const int NES_W = 16;

	private const int NES_H = 15;

	private const int TILE = 16;

	private int? configSpawnYHi = null;

	private int? configSpawnYLo = null;

	private int? configScrollYHi = null;

	private int? configScrollYLo = null;

	private ImageSource?[]? parallaxImages;

	private ImageSource?[]? parallaxTonedImages;

	private ImageSource? parallaxBitmap = null;

	private ImageSource? parallaxBitmapToned = null;

	private double parallaxX = 1.0;

	private double parallaxY = 1.0;

	private bool parallaxRepeatX = true;

	private bool parallaxRepeatY = true;

	private bool hasParallaxLayer = false;

	private ImageSource?[]? groundImages;

	private ImageSource?[]? groundTonedImages;

	private double groundOffsetY = 0.0;

	private bool groundRepeatX = true;

	private bool hasGroundLayer = false;

	private int groundTileRows = 0;

	private int cameraX_fixed = 0;

	private int cameraY_fixed = 0;

	private const int SPEED_FIXED = 708;

	private int currentSpeed_fixed = 708;

	private const int CUBE_SPEED_X05 = 571;

	private const int CUBE_SPEED_X1 = 708;

	private const int CUBE_SPEED_X2 = 881;

	private const int CUBE_SPEED_X3 = 1065;

	private const int CUBE_SPEED_X4 = 1310;

	private const int CUBE_SPEED_SLOW = 366;

	private int CUBE_MAX_FALLSPEED = 1536;

	private const int CUBE_GRAVITY = 107;

	private const int CUBE_JUMP_VEL = -1424;

	private const int ROBOT_JUMP_VEL = -688;

	private const int UFO_GRAVITY = 50;

	private const int UFO_MAX_FALLSPEED = 800;

	private const int UFO_JUMP_VEL = -816;

	private const int SHIP_MAX_FALLSPEED = 873;

	private const int SHIP_MAX_FALLSPEED_HOLD = 1091;

	private const int BALL_GRAVITY = 102;

	private const int BALL_MAX_FALLSPEED = 1843;

	private const int BALL_IMMEDIATE_VEL = 614;

	private const int SHIP_GRAVITY_BASE = 60;

	private const int SHIP_GRAVITY = 48;

	private const int SHIP_GRAVITY_AFTER_HOLD = 73;

	private const int SHIP_GRAVITY_HOLD_FALL = 76;

	private int currentGameMode = 0;

	private int _levelStartGameMode = 0;

	private int velocityY = 0;

	private int velocityX = 768;

	private bool gravityFlipped = false;

	private bool miniMode = false;

	private int speed = 1;

	private bool previousJumpState = false;

	private int effectiveGravity_fixed;

	private int effectiveJumpVel_fixed;

	private int effectiveMaxFall_fixed;

	private bool gravityReversed = false;

	private double gravityMultiplier = 1.0;

	private bool effectiveInvertedByW = false;

	private bool gravityFlippedThisFrame = false;

	private HashSet<int> processedGravityPortals = new HashSet<int>();

	private HashSet<int> processedGravityModPortals = new HashSet<int>();

	private HashSet<int> processedGameModePortals = new HashSet<int>();

	private HashSet<int> processedMiniPortals = new HashSet<int>();

	private HashSet<int> processedRandomPortals = new HashSet<int>();

	private HashSet<int> processedSpeedPortals = new HashSet<int>();

	private HashSet<int> processedCamLockPortals = new HashSet<int>();

	private bool nocamlockforced = false;

	private bool wrapMode = false;

	private HashSet<int> processedWrapPortals = new HashSet<int>();

	private bool slowMode = false;

	private HashSet<int> processedTimewarpTriggers = new HashSet<int>();

	private bool playerInvis = false;

	private HashSet<int> processedPlayerInvisTriggers = new HashSet<int>();

	private int forcedTrails = 0;

	private HashSet<int> processedTrailTriggers = new HashSet<int>();

	private int[] playerOldPosY = new int[9];

	private int simTickCount = 0;

	private int targetCameraY_fixed = 0;

	private const int PORTAL_TO_TOP_DIFF_PX = 58;

	private const int SHIP_SCROLL_SPEED_DOWN_FIXED = 768;

	private const int SHIP_SCROLL_SPEED_UP_FIXED = 512;

	private int _sim_nesCoordOffset;

	private bool _suppressDebugBarEvents = false;

	private HashSet<int> processedOrbs = new HashSet<int>();

	private bool[] orbBufferActive = new bool[2];

	private int[] ballInputBufferCountdown = new int[2];

	private bool[] orbActivationConsumedThisPress = new bool[2];

	private bool[] orbHoldConsumed = new bool[2];

	private bool[] orbHoldConsumedKeyStillDown = new bool[2];

	private bool[] orbHoldSuppressing = new bool[2];

	private bool[] orbhitonthisframe = new bool[2];

	private int playerVelY_fixed = 0;

	private bool physicsEnabled = false;

	private const int LAND_EPS_FIXED = 256;

	private bool onGround = true;

	private int groundStabilizeCounter = 0;

	private int invertedCeilingHoldCounter = 0;

	private int jumpBufferCounter = 0;

	private int pogoBounceAnimationCounter = 0;

	private double pogoBounceAnimationFrameAccum = 0.0;

	private int ballAnimationFrameCounter = 0;

	private double ballAnimationFrameAccum = 0.0;

	private int robotAnimationFrameCounter = 0;

	private double robotAnimationFrameAccum = 0.0;

	private int spiderAnimationFrameCounter = 0;

	private double spiderAnimationFrameAccum = 0.0;

	private bool showYVelocityOverlay = false;

	private TextBlock? yVelTextBlock = null;

	private double simTimeScale = 1.0;

	private bool isFullSpeed = true;

	private const int JUMP_BUFFER_FRAMES = 6;

	private bool useRefactoredPhysics = true;

	private int ballToggleRequested = 0;

	private const int BALL_BUFFER_FRAMES = 6;

	private bool ballGoingDown = true;

	private bool[] ballSwitched = new bool[2];

	private int ballFlipCooldown = 0;

	private bool ballWasGroundedBeforeFlip = false;

	private int[] player_ballFlipCooldown = new int[2];

	private bool[] player_ballWasGroundedBeforeFlip = new bool[2];

	private int[] ballFlipBuffer = new int[2];

	private bool pfInputThisFrame = false;

	private int p2BallHoldCounter = 0;

	private bool ufoOrbed = false;

	private bool[] orbed = new bool[2];

	private bool blackOrbed = false;

	private int[] dashing = new int[2];

	private bool hblocked = false;

	private bool jblocked = false;

	private bool dblocked = false;

	private bool fblocked = false;

	private int invincibleCounter = 0;

	private int[] ninjajumps = new int[2] { 3, 3 };

	private int[] robotJumpTime = new int[2];

	private int[] robotJumpFrame = new int[2];

	private int[] chargepower = new int[2];

	private bool[] robotJumpPressed = new bool[2];

	private int ninjaJumps = 3;

	private bool ninjaJumpedThisFrame = false;

	private bool swingSwitched = false;

	private static readonly uint currentProcessId = (uint)Process.GetCurrentProcess().Id;

	private readonly Dictionary<int, int> speedPortalMap = new Dictionary<int, int>
	{
		{ 20, 571 },
		{ 21, 708 },
		{ 22, 881 },
		{ 32, 1065 },
		{ 33, 1310 },
		{ 109, 366 }
	};

	private readonly DispatcherTimer timer;

	private Timer? simTimer;

	private readonly object simLock = new object();

	private Stopwatch simStopwatch = new Stopwatch();

	private double simLastMs = 0.0;

	private double simAccumulatedMs = 0.0;

	private const double SIM_STEP_MS = 16.666666666666668;

	private double uiAnimLastMs = 0.0;

	private double uiAnimAccumulatedMs = 0.0;

	private int pendingBgIdx = -1;

	private int pendingBgSid = -1;

	private int pendingTileIdx = -1;

	private int pendingTileSid = -1;

	private int pendingGroundIdx = -1;

	private int pendingGroundSid = -1;

	private bool pendingTintChange = false;

	private bool pendingTintChangeIsStartup = false;

	private bool startupTintApplied = false;

	private Stopwatch renderStopwatch = new Stopwatch();

	private int animationFrame = 0;

	private static readonly HashSet<int> slowAnimatedSpriteIds = new HashSet<int> { 7, 26, 27, 110 };

	private Dictionary<int, int> spriteFrameOffsets = new Dictionary<int, int>();

	private Random spriteAnimationRandom = new Random();

	private RenderTargetBitmap? tileLayerCache = null;

	private int cachedStartTileX = int.MinValue;

	private int cachedStartTileY = int.MinValue;

	private Image? tileLayerImage = null;

	private Image? spriteLayerImage = null;

	private Rectangle? bgRectPersistent = null;

	private Rectangle? groundRectPersistent = null;

	private ImageBrush? _cachedParallaxBrush = null;

	private TranslateTransform? _cachedParallaxTransform = null;

	private ImageSource? _cachedParallaxSource = null;

	private SolidColorBrush? _cachedBgTintBrush = null;

	private Color _cachedBgTintColor;

	private List<Image> spritePool = new List<Image>();

	private int spritesInUse = 0;

	private bool lastCacheHadAnimatedTiles = false;

	private int lastCacheAnimationFrame = -1;

	private readonly HashSet<int> decorationSpriteIds = new HashSet<int>
	{
		54, 50, 51, 52, 53, 55, 44, 60, 45, 61,
		46, 47, 48, 49, 56, 57, 62, 63, 43, 59,
		42, 58, 73, 74
	};

	private readonly HashSet<int> nonPlayerTintSpriteIds = new HashSet<int> { 7, 26, 27, 110 };

	private readonly Dictionary<long, ImageSource?> tintedSpriteCache = new Dictionary<long, ImageSource>();

	private readonly Dictionary<int, bool> imageTopHeavyCache = new Dictionary<int, bool>();

	private readonly Dictionary<string, ImageSource?> spriteBackgroundCompositeCache = new Dictionary<string, ImageSource>();

	private readonly Dictionary<long, ImageSource?> groundTintedTileCache = new Dictionary<long, ImageSource>();

	private readonly Dictionary<long, ImageSource?> blackMaskedTileCache = new Dictionary<long, ImageSource>();

	private HashSet<int> processedColorTriggers = new HashSet<int>();

	private ImageSource?[]? cachedTileTonedImages = null;

	private ImageSource?[]? cachedParallaxTonedImages = null;

	private ImageSource?[]? cachedGroundTonedImages = null;

	private Color cachedBackgroundTint = Color.FromArgb(0, 0, 0, 0);

	private Color cachedTileTint = Color.FromArgb(0, 0, 0, 0);

	private Color cachedGroundTint = Color.FromArgb(0, 0, 0, 0);

	private bool cachedBackgroundForceSolidBlack = false;

	private HashSet<int> decoLogged = new HashSet<int>();

	private HashSet<int> triggerLogged = new HashSet<int>();

	private int tileSelectionLogCount = 0;

	private const int TILE_SELECTION_LOG_LIMIT = 64;

	private Dictionary<int, int> decoLastSelectedFrame = new Dictionary<int, int>();

	private bool enableSimulatorDebugLogging = false;

	private bool runtimePerFrameLog = false;

	private readonly string simDebugFilePath = "C:\\Editor Test\\native-windows\\sim_debug.txt";

	private bool simDebugLoggedFirstFrame = false;

	private bool upHeld = false;

	private bool downHeld = false;

	private bool jumpedOnce = false;

	private bool prevKeyXDown = false;

	private int keyXPressedCount = 0;

	private bool keyXHeld = false;

	private int keyXPressStartedOnGroundInt = 0;

	private int keyXHeldStartedOnGroundInt = 0;

	private bool paused = true;

	private bool deathTriggered = false;

	private bool levelCompleteTriggered = false;

	private HashSet<int> processedEndLevelTriggers = new HashSet<int>();

	private HashSet<int> collectedCoins = new HashSet<int>();

	private List<(int spriteIndex, int spriteId)> collectedCoinInfo = new List<(int, int)>();

	private int deathTileX = -1;

	private int deathTileY = -1;

	private Ellipse? deathPlayerDot = null;

	private Ellipse? deathTileDot = null;

	private DebugInfoWindow? debugWindow = null;

	private bool camModeActive = false;

	private int tabSpeedMultiplier = 1;

	private bool hasAppliedStartPos = false;

	private int startPosX_forMusicSeek = 0;

	private const int SLOPE_NONE = 0;

	private const int SLOPE_45DEG = 1;

	private const int SLOPE_22DEG = 2;

	private const int SLOPE_66DEG = 3;

	private const int SLOPE_RISING = 4;

	private const int SLOPE_UPSIDEDOWN = 8;

	private const int SLOPE_RISING_MASK = 7;

	private const int SLOPE_DEGREES_MASK = 3;

	private const int SLOPE_45DEG_UP = 5;

	private const int SLOPE_45DEG_DOWN = 1;

	private const int SLOPE_22DEG_UP = 6;

	private const int SLOPE_22DEG_DOWN = 2;

	private const int SLOPE_66DEG_UP = 7;

	private const int SLOPE_66DEG_DOWN = 3;

	private const int SLOPE_45DEG_UP_UD = 13;

	private const int SLOPE_45DEG_DOWN_UD = 9;

	private const int SLOPE_22DEG_UP_UD = 14;

	private const int SLOPE_22DEG_DOWN_UD = 10;

	private const int SLOPE_66DEG_UP_UD = 15;

	private const int SLOPE_66DEG_DOWN_UD = 11;

	private int[] slope_type_arr = new int[2];

	private int[] slope_frames_arr = new int[2];

	private int[] was_on_slope_counter_arr = new int[2];

	private int[] last_slope_type_arr = new int[2];

	private bool make_cube_jump_higher = false;

	private static readonly int[] slopeRotationFrame_7 = new int[16]
	{
		0, 3, 2, 3, 0, 1, 1, 2, 0, 3,
		2, 3, 0, 1, 1, 2
	};

	private static readonly int[] slopeRotationFrame_24 = new int[16]
	{
		0, 9, 8, 9, 0, 3, 4, 8, 0, 9,
		8, 9, 0, 3, 4, 8
	};

	private static readonly short[] EXIT_SLOPE_BALL_22 = new short[8] { -96, 96, -96, 96, -80, 80, -80, 80 };

	private static readonly short[] EXIT_SLOPE_BALL_66 = new short[8] { 365, -365, 365, -365, 432, -432, 432, -432 };

	private static readonly short[] EXIT_SLOPE_CUBE_22 = new short[8] { -307, 307, -307, 307, -256, 256, -256, 256 };

	private const byte TELEPORT_PORTAL_VERTICAL_ENTER = 78;

	private const byte TELEPORT_PORTAL_VERTICAL_EXIT = 79;

	private const byte TELEPORT_PORTAL_HORIZONTAL_ENTER_1 = 102;

	private const byte TELEPORT_PORTAL_HORIZONTAL_EXIT_BOTTOM = 103;

	private const byte TELEPORT_PORTAL_HORIZONTAL_ENTER_2 = 104;

	private const byte TELEPORT_PORTAL_HORIZONTAL_EXIT_TOP = 105;

	private const byte TELEPORT_PORTAL_INVISIBLE_ENTER_1 = 117;

	private const byte TELEPORT_PORTAL_INVISIBLE_EXIT_BOTTOM = 118;

	private const byte TELEPORT_PORTAL_INVISIBLE_ENTER_2 = 119;

	private const byte TELEPORT_PORTAL_INVISIBLE_EXIT_TOP = 120;

	private HashSet<int> processedTeleportPortals = new HashSet<int>();

	internal StackPanel SettingsPanel;

	internal ComboBox GameModeComboBox;

	internal Button RestartButton;

	internal CheckBox MiniCheckBox;

	internal CheckBox InvertedCheckBox;

	internal CheckBox PathfinderCheckBox;

	internal ComboBox SpeedComboBox;

	internal Canvas RenderCanvas;

	internal Grid PauseOverlay;

	internal Ellipse PauseOverlayCircle;

	internal StackPanel PauseOverlayIcon;

	internal TextBlock PauseOverlayText;

	internal Grid LevelCompleteOverlay;

	internal StackPanel CoinDisplayPanel;

	private bool _contentLoaded;

	private int currplayer_last_slope_type
	{
		get
		{
			return last_slope_type_arr[currplayer];
		}
		set
		{
			last_slope_type_arr[currplayer] = value;
		}
	}

	public bool ShowSpriteHitboxes { get; set; } = false;

	public bool ShowTileHitboxes { get; set; } = false;

	public string LevelName
	{
		get
		{
			return _levelName;
		}
		set
		{
			_levelName = value ?? "";
		}
	}

	private int currplayer_slope_type
	{
		get
		{
			return slope_type_arr[currplayer];
		}
		set
		{
			slope_type_arr[currplayer] = value;
		}
	}

	private int currplayer_slope_frames
	{
		get
		{
			return slope_frames_arr[currplayer];
		}
		set
		{
			slope_frames_arr[currplayer] = value;
		}
	}

	private int currplayer_was_on_slope_counter
	{
		get
		{
			return was_on_slope_counter_arr[currplayer];
		}
		set
		{
			was_on_slope_counter_arr[currplayer] = value;
		}
	}

	private void BallPhysics_Fresh()
	{
		int table_idx = ((currplayer_mini != 0) ? 4 : 0);
		int num = ((currplayer_gravity == 0) ? 1 : (-1));
		if (currentGameMode == 7 || currentGameMode == 9)
		{
			tmpfallspeed = GameModePhysics.SWING_MAX_FALLSPEED(table_idx) * num;
			tmpgravity = GameModePhysics.SWING_GRAVITY(table_idx) * num;
		}
		else
		{
			tmpfallspeed = GameModePhysics.BALL_MAX_FALLSPEED(table_idx) * num;
			tmpgravity = GameModePhysics.BALL_GRAVITY(table_idx) * num;
		}
		if (currentGameMode == 2)
		{
			bool flag = IsXDownAsync() || keyXHeld;
			int num2 = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
			bool flag2 = num2 > 0;
			bool gravityInverted = currplayer_gravity != 0;
			int playerX_px = (playerX_fixed >> 8) + 1;
			int num3 = playerY_fixed >> 8;
			int playerW = ((currplayer_mini != 0) ? 8 : 15);
			int playerH = ((currplayer_mini != 0) ? 7 : 15);
			if (currplayer_mini != 0)
			{
				num3 += 4;
			}
			int scrollX_px = 0;
			int num4 = playerVelY_fixed;
			if (UpdateOrbSystem(2, playerX_px, num3, playerW, playerH, scrollX_px, flag2, flag, gravityInverted, currplayer_mini != 0, ref num4).activated)
			{
				playerVelY_fixed = num4;
				orbhitonthisframe[currplayer] = true;
				AppendSimDebug($"[BALL] Orb activated! New velY={playerVelY_fixed}");
				if (flag2)
				{
					Interlocked.Exchange(ref keyXPressedCount, 0);
				}
				ClearOrbBuffer();
			}
			if (!flag)
			{
				ClearOrbBuffer();
			}
			bool flag3 = currplayer_gravity != 0;
			num = ((!flag3) ? 1 : (-1));
			tmpfallspeed = GameModePhysics.BALL_MAX_FALLSPEED(table_idx) * num;
			tmpgravity = GameModePhysics.BALL_GRAVITY(table_idx) * num;
			bool flag4 = IsXDownAsync() || keyXHeld;
			int num5 = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
			bool flag5 = num5 > 0;
			int num6 = Interlocked.CompareExchange(ref ballToggleRequested, 0, 0);
			if (num6 > 0)
			{
				flag5 = true;
				Interlocked.Exchange(ref ballToggleRequested, 0);
				LogBallEvent("[BALL] Consumed ballToggleRequested flag");
			}
			int num7 = playerY_fixed;
			bool flag6 = false;
			bool flag7 = currplayer_mini != 0;
			int num8 = (flag7 ? 8 : 15);
			int num9 = (flag7 ? 7 : 15);
			int num10 = (flag7 ? (16 - num9 >> 1) : 0);
			if (ballFlipCooldown > 0)
			{
				flag6 = ballWasGroundedBeforeFlip;
				AppendSimDebug($"[BALL] Using cached grounded state during cooldown: isGrounded={flag6}");
			}
			else
			{
				if (!flag3)
				{
					int collY = (playerY_fixed >> 8) + num10 + num9;
					int groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
					if (SharedPhysics.CheckFloor(new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve), playerX_fixed >> 8, collY, num8, 2).spikeDeath && !MainWindow.Option_NoDeath)
					{
						AppendSimDebug($"[BALL_GROUNDED_SPIKE_DEATH] X={playerX_fixed >> 8} Y={playerY_fixed >> 8}");
						deathTriggered = true;
						deathTileX = playerX_fixed >> 8;
						deathTileY = collY;
						paused = true;
						StopMusicAsync();
						try
						{
							base.Dispatcher.BeginInvoke((Action)delegate
							{
								try
								{
									PauseOverlay.Visibility = Visibility.Collapsed;
								}
								catch
								{
								}
								if (base.Owner is MainWindow mainWindow)
								{
									try
									{
										mainWindow.PauseSimulatorPlayback();
									}
									catch
									{
									}
									try
									{
										mainWindow.AddDeathMarker(deathTileX, deathTileY);
									}
									catch
									{
									}
								}
							});
							return;
						}
						catch
						{
							return;
						}
					}
				}
				int groundRowsToReserve2 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
				flag6 = SharedPhysics.BallIsGrounded(new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve2), playerX_fixed >> 8, playerY_fixed >> 8, num8, num9, num10, flag3);
				if (!flag6 && currplayer_was_on_slope_counter > 0)
				{
					flag6 = true;
				}
				AppendSimDebug($"[BALL] Grounded check: isGrounded={flag6}");
			}
			AppendSimDebug($"[BALL] press={flag5}, hold={flag4}, ballSwitched={ballSwitched[currplayer]}, velY={playerVelY_fixed}, grounded={flag6}");
			bool flag8 = false;
			AppendSimDebug($"[BALL_FLIP_CHECK] pressJump={flag5} holdJump={flag4} isGrounded={flag6} currplayer={currplayer} orbHoldConsumed={orbHoldConsumedKeyStillDown[currplayer]} orbHoldSuppress={orbHoldSuppressing[currplayer]} ballSwitched={ballSwitched[currplayer]} ballFlipBuffer={ballFlipBuffer[currplayer]}");
			if (flag5 && !orbhitonthisframe[currplayer] && !orbed[currplayer] && !orbHoldConsumedKeyStillDown[currplayer] && !orbHoldSuppressing[currplayer])
			{
				if (flag6)
				{
					flag8 = true;
				}
				else
				{
					ballFlipBuffer[currplayer] = 8;
				}
				orbBufferActive[currplayer] = true;
			}
			else if (ballFlipBuffer[currplayer] > 0 && !orbhitonthisframe[currplayer] && !orbed[currplayer] && !ballSwitched[currplayer] && flag6)
			{
				flag8 = true;
			}
			if (flag8)
			{
				AppendSimDebug("[BALL] FLIPPING GRAVITY!");
				InvertGravity_Fresh();
				UpdateCurrplayerTableIdx_Fresh();
				num = ((currplayer_gravity == 0) ? 1 : (-1));
				tmpfallspeed = GameModePhysics.BALL_MAX_FALLSPEED(table_idx) * num;
				tmpgravity = GameModePhysics.BALL_GRAVITY(table_idx) * num;
				ballSwitched[currplayer] = true;
				playerVelY_fixed = GameModePhysics.BALL_SWITCH_VEL(currplayer_table_idx);
				ballFlipCooldown = 2;
				ballWasGroundedBeforeFlip = true;
				Interlocked.Exchange(ref keyXPressedCount, 0);
				orbHoldConsumedKeyStillDown[currplayer] = true;
				ballFlipBuffer[currplayer] = 0;
				ClearOrbBuffer();
			}
			else if (ballFlipBuffer[currplayer] > 0)
			{
				ballFlipBuffer[currplayer]--;
			}
			if (ballSwitched[currplayer] && !flag4)
			{
				ballSwitched[currplayer] = false;
			}
		}
		if (currentGameMode == 7 || currentGameMode == 9)
		{
			bool flag9 = IsXDownAsync() || keyXHeld;
			int num11 = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
			bool flag10 = num11 > 0;
			bool gravityInverted2 = currplayer_gravity != 0;
			int playerX_px2 = (playerX_fixed >> 8) + 1;
			int num12 = playerY_fixed >> 8;
			int playerW2 = ((currplayer_mini != 0) ? 8 : 15);
			int playerH2 = ((currplayer_mini != 0) ? 7 : 15);
			if (currplayer_mini != 0)
			{
				num12 += 4;
			}
			int scrollX_px2 = 0;
			int num13 = playerVelY_fixed;
			int gamemode = ((currentGameMode == 9) ? 7 : currentGameMode);
			if (UpdateOrbSystem(gamemode, playerX_px2, num12, playerW2, playerH2, scrollX_px2, flag10, flag9, gravityInverted2, currplayer_mini != 0, ref num13).activated)
			{
				playerVelY_fixed = num13;
				orbhitonthisframe[currplayer] = true;
				AppendSimDebug($"[SWING/POGO] Orb/Pad activated! New velY={playerVelY_fixed}");
				if (flag10)
				{
					Interlocked.Exchange(ref keyXPressedCount, 0);
				}
			}
			if (!flag9)
			{
				ClearOrbBuffer();
			}
		}
		CommonGravityRoutine_Fresh();
		if (ballFlipCooldown > 0)
		{
			ballFlipCooldown--;
			AppendSimDebug($"[BALL] Skipping velocity/collision checks during cooldown: {ballFlipCooldown} frames remaining");
			if (ballFlipCooldown == 0)
			{
				ballWasGroundedBeforeFlip = false;
			}
			return;
		}
		if (currentGameMode == 2 || currentGameMode == 9)
		{
			bool flag11 = currplayer_mini != 0;
			int width = (flag11 ? 8 : 15);
			int num14 = (flag11 ? 7 : 15);
			int num15 = (flag11 ? (16 - num14 >> 1) : 0);
			int playerX_px3 = playerX_fixed >> 8;
			if (currplayer_gravity == 0)
			{
				if (playerVelY_fixed < 0)
				{
					int playerY_px = (playerY_fixed >> 8) + num15 - 1;
					var (flag12, num16) = CheckCollisionUp(playerX_px3, playerY_px, width, num14);
					if (flag12)
					{
						int num17 = num16 - num15;
						playerY_fixed = num17 << 8;
						playerVelY_fixed = 0;
						AppendSimDebug($"[BALL] Ceiling grounded - snapped Y to {num17}");
					}
				}
			}
			else if (playerVelY_fixed > 0)
			{
				int num18 = (playerY_fixed >> 8) + num15 + num14;
				int num19 = 2;
				int playerY_px2 = num18 - num19;
				var (flag13, num20) = CheckCollisionDown(playerX_px3, playerY_px2, width, num19);
				if (flag13)
				{
					int num21 = num20 - num14 - num15;
					playerY_fixed = num21 << 8;
					playerVelY_fixed = 0;
					AppendSimDebug($"[BALL] Floor grounded - zeroed downward velocity, snapped Y to {num21}");
				}
			}
		}
		if (currentGameMode == 7)
		{
			UfoShipEject_Fresh();
		}
		else
		{
			BallEject_Fresh();
		}
		UpdateSlopeCounters_Fresh();
		if (currentGameMode == 7)
		{
			int num22 = Interlocked.Exchange(ref keyXPressedCount, 0);
			bool flag14 = num22 > 0;
			AppendSimDebug($"[SWING] pressCount={num22} pressedJump={flag14} ufoOrbed={ufoOrbed}");
			if (flag14 && !ufoOrbed)
			{
				AppendSimDebug("[SWING] FLIPPING GRAVITY!");
				InvertGravity_Fresh();
				UpdateCurrplayerTableIdx_Fresh();
			}
		}
		if (currentGameMode == 9)
		{
			int num23 = Interlocked.Exchange(ref keyXPressedCount, 0);
			if (num23 > 0 && !orbhitonthisframe[currplayer] && !orbHoldConsumedKeyStillDown[currplayer] && !orbHoldSuppressing[currplayer])
			{
				int num24 = ((currplayer_mini != 0) ? PadOrbHeights_Mini[6][7] : PadOrbHeights[6][7]);
				int num25 = ((currplayer_gravity != 0) ? 1 : (-1));
				playerVelY_fixed = num24 * num25;
				AppendSimDebug($"[POGO_BLACKORB] X pressed! velY set to 0x{playerVelY_fixed:X4}");
				orbHoldConsumedKeyStillDown[currplayer] = true;
				ClearOrbBuffer();
			}
		}
		if (pfSimulating)
		{
			return;
		}
		try
		{
			int num26 = (playerX_fixed >> 8) + playerVisualWidth / 2;
			bool flag15 = currplayer_mini != 0;
			int num27 = (flag15 ? 7 : 15);
			int num28 = (flag15 ? (16 - num27 >> 1) : 0);
			int num29 = (playerY_fixed >> 8) + playerVisualHeight / 2 + num28;
			if (currplayer == 0)
			{
				recordedPlayerPath.Add((num26, num29));
			}
			else if (dual)
			{
				RecordP2PathPoint(num26, num29);
			}
		}
		catch
		{
		}
	}

	private void BallEject_Fresh()
	{
		bool flag = currplayer_mini != 0;
		bool flag2 = currplayer_gravity != 0;
		bool inputHeld = IsXDownAsync() || keyXHeld || upHeld;
		int groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		SharedPhysics.CollisionMap map = new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve);
		int num = playerVelY_fixed;
		SharedPhysics.EjectResult ejectResult = SharedPhysics.BallEject(in map, playerX_fixed, playerY_fixed, playerVelY_fixed, playerVelX_fixed, flag2, flag, currentGameMode, inputHeld, currplayer_was_on_slope_counter, currplayer_slope_frames, currplayer_slope_type, make_cube_jump_higher, currplayer_last_slope_type, cameraY_fixed);
		playerY_fixed = ejectResult.NewY_fixed;
		playerVelY_fixed = ejectResult.NewVelY_fixed;
		onGround = ejectResult.OnGround;
		currplayer_slope_type = ejectResult.SlopeType;
		currplayer_slope_frames = ejectResult.SlopeFrames;
		currplayer_was_on_slope_counter = ejectResult.SlopeWasOnCounter;
		make_cube_jump_higher = ejectResult.SlopeJumpHigher;
		currplayer_last_slope_type = ejectResult.LastSlopeType;
		if (ejectResult.Died && !MainWindow.Option_NoDeath)
		{
			AppendSimDebug("[DEATH] Floor spike detected (SharedPhysics.BallEject)");
			deathTriggered = true;
			deathTileX = playerX_fixed >> 8;
			deathTileY = (playerY_fixed >> 8) + SharedPhysics.GetCubeHitboxH(flag);
			paused = true;
			StopMusicAsync();
			try
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					try
					{
						PauseOverlay.Visibility = Visibility.Collapsed;
					}
					catch
					{
					}
					if (base.Owner is MainWindow mainWindow)
					{
						try
						{
							mainWindow.PauseSimulatorPlayback();
						}
						catch
						{
						}
						try
						{
							mainWindow.AddDeathMarker(deathTileX, deathTileY);
						}
						catch
						{
						}
					}
				});
				return;
			}
			catch
			{
				return;
			}
		}
		if (currentGameMode != 9 || !ejectResult.OnGround || orbhitonthisframe[currplayer])
		{
			return;
		}
		int num2 = -num / 3 * 2;
		int num3 = (flag ? PadOrbHeights_Mini[1][7] : PadOrbHeights[1][7]);
		if (!flag2)
		{
			int num4 = num3 * -1;
			if (num2 > num4)
			{
				num2 = num4;
			}
		}
		else
		{
			int num5 = num3;
			if (num2 < num5)
			{
				num2 = num5;
			}
		}
		playerVelY_fixed = num2;
		pogoBounceAnimationCounter = 8;
		AppendSimDebug($"[POGO_BOUNCE] velY: old=0x{num:X4} -> new=0x{num2:X4}");
	}

	private void InvertGravity_Fresh()
	{
		gravityFlipped = !gravityFlipped;
		gravityReversed = gravityFlipped;
		currplayer_gravity = (byte)(gravityFlipped ? 255u : 0u);
		UpdateCurrplayerTableIdx_Fresh();
		UpdatePlayerIconFlip();
	}

	private void UpdateCurrplayerTableIdx_Fresh()
	{
		currplayer_table_idx = ((currplayer_gravity == 0) ? 1 : 0) | ((currplayer_mini != 0) ? 4 : 0);
	}

	private void CheckBluePadCollision()
	{
		try
		{
			int num = playerX_fixed >> 8;
			int num2 = playerY_fixed >> 8;
			bool flag = currplayer_mini != 0;
			bool flag2 = currplayer_gravity != 0;
			int cubeHitboxW = SharedPhysics.GetCubeHitboxW(flag);
			int cubeHitboxH = SharedPhysics.GetCubeHitboxH(flag);
			num2 += GetMiniSpriteOffsetY();
			int num3 = num + 1;
			int num4 = num3 + cubeHitboxW;
			int num5 = num2;
			int num6 = num5 + cubeHitboxH;
			int num7 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			bool value = default(bool);
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num8 = nonEmptySpriteIndices[i];
				int num9 = sprites[num8];
				if (num9 < 0)
				{
					continue;
				}
				bool flag3 = num9 == 13 || num9 == 253;
				bool flag4 = num9 == 14 || num9 == 254;
				if ((!flag3 && !flag4) || (!dual && orbActivated.TryGetValue(num8, out value) && value))
				{
					continue;
				}
				int num10 = num9 & 0xFF;
				int num11 = -1;
				if (spriteAnchors != null && spriteAnchors.TryGetValue(num8, out (int, int) value2))
				{
					num11 = value2.Item2 * mapWidth + value2.Item1;
					if (num11 >= 0 && num11 < sprites.Length)
					{
						int num12 = sprites[num11];
						if (num12 >= 0 && num12 < 256)
						{
							num10 = num12 & 0xFF;
						}
					}
				}
				int num13 = ((num10 >= 0 && num10 < SharedPhysics.sprite_widths.Length) ? SharedPhysics.sprite_widths[num10] : 16);
				int num14 = ((num10 >= 0 && num10 < SharedPhysics.sprite_heights.Length) ? SharedPhysics.sprite_heights[num10] : 16);
				int num15 = ((num10 >= 0 && num10 < SharedPhysics.sprite_x_offset.Length) ? SharedPhysics.sprite_x_offset[num10] : 0);
				int num16 = ((num10 >= 0 && num10 < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[num10] : 0);
				int num17 = 0;
				int num18 = 0;
				(int, int) value4;
				if (num11 >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num11, out (int, int) value3))
				{
					(num17, num18) = value3;
				}
				else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num8, out value4))
				{
					(num17, num18) = value4;
				}
				int num19 = num8 % mapWidth;
				int num20 = num8 / mapWidth;
				int num21 = num19 * 16 + num15 + num17;
				int num22 = (num20 - num7) * 16 + num16 + num18 - 1;
				int num23 = num21 + Math.Max(1, num13);
				int num24 = num22 + Math.Max(1, num14);
				bool flag5 = num4 >= num21 && num23 >= num3;
				bool flag6 = num6 >= num22 && num24 >= num5;
				if (flag3 && num3 >= 4500 && num3 <= 4560)
				{
					AppendSimDebug($"[BPAD_DBG] idx={num8} sid=0x{num9:X2} pad=({num3},{num5})-({num4},{num6}) spr=({num21},{num22})-({num23},{num24}) xO={flag5} yO={flag6} tX={num19} tY={num20} grR={num7} hw={num13} hh={num14} hxo={num15} hyo={num16} pxO={num17} pyO={num18} gInv={flag2}");
				}
				if (!(flag5 && flag6))
				{
					continue;
				}
				if (!(flag3 && flag2) && (!flag4 || flag2))
				{
					ClearSlopeStuff();
					flag2 = (gravityReversed = (gravityFlipped = !flag2));
					try
					{
						base.Dispatcher?.BeginInvoke((Action)delegate
						{
							UpdatePlayerIconFlip();
						});
					}
					catch
					{
					}
					int num25 = (flag ? (-928) : (-928));
					int num26 = (flag2 ? num25 : (-num25));
					playerVelY_fixed = num26;
					orbhitonthisframe[currplayer] = true;
				}
				if (!dual)
				{
					orbActivated[num8] = true;
				}
			}
		}
		catch
		{
		}
	}

	private void ResetBluePadSystem()
	{
	}

	private bool CheckComplexCollision(MetatileCollision collision, int localX, int localY)
	{
		return SharedPhysics.CheckComplexCollision(collision, localX, localY);
	}

	private (int left, int top, int right, int bottom) GetCollisionBounds(MetatileCollision collision)
	{
		switch (collision)
		{
		case MetatileCollision.COL_UP_LEFT_SPIKE:
			return (left: 0, top: 0, right: 8, bottom: 8);
		case MetatileCollision.COL_UP_RIGHT_SPIKE:
			return (left: 8, top: 0, right: 16, bottom: 8);
		case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
		case MetatileCollision.COL_DOWN_LEFT_SPIKE:
		case MetatileCollision.COL_UP_BOTH_SPIKES:
		case MetatileCollision.COL_DOWN_BOTH_SPIKES:
			return (left: 0, top: 0, right: 0, bottom: 0);
		default:
			return SharedPhysics.GetCollisionBounds(collision);
		}
	}

	private (bool collided, int collisionTopY) CheckCollisionDown(int playerX_px, int playerY_px, int width, int height)
	{
		int groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		(bool hit, int surfaceY, bool spikeDeath) tuple = SharedPhysics.CheckFloor(new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve), playerX_px, playerY_px, width, height, playerVelY_fixed);
		var (flag, num, _) = tuple;
		if (tuple.spikeDeath && !MainWindow.Option_NoDeath)
		{
			AppendSimDebug("[DEATH] Floor spike detected (SharedPhysics.CheckFloor)");
			deathTriggered = true;
			deathTileX = playerX_px;
			deathTileY = playerY_px + height;
			paused = true;
			StopMusicAsync();
			try
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					try
					{
						PauseOverlay.Visibility = Visibility.Collapsed;
					}
					catch
					{
					}
					if (base.Owner is MainWindow mainWindow)
					{
						try
						{
							mainWindow.PauseSimulatorPlayback();
						}
						catch
						{
						}
						try
						{
							mainWindow.AddDeathMarker(deathTileX, deathTileY);
						}
						catch
						{
						}
					}
				});
			}
			catch
			{
			}
			return (collided: false, collisionTopY: 0);
		}
		if (flag)
		{
			AppendSimDebug($"[COLL_DOWN] Hit! collisionTop_px={num}, playerBottom={playerY_px + height}");
		}
		return (collided: flag, collisionTopY: num);
	}

	private (bool collided, int collisionBottomY) CheckCollisionUp(int playerX_px, int playerY_px, int width, int height)
	{
		int groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		var (item, item2, _, _) = SharedPhysics.CheckCeiling(new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve), playerX_px, playerY_px, width, height, playerVelY_fixed);
		return (collided: item, collisionBottomY: item2);
	}

	private (bool collided, int collisionRightX) CheckCollisionLeft(int playerX_px, int playerY_px, int width, int height)
	{
		int num = playerX_px / 16;
		if (num < 0 || num >= mapWidth)
		{
			return (collided: false, collisionRightX: 0);
		}
		int num2 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		int num3 = playerY_px + height - 1;
		int num4 = playerY_px / 16;
		int num5 = num3 / 16;
		for (int i = num4; i <= num5; i++)
		{
			if (i < 0 || i >= mapHeight)
			{
				continue;
			}
			int num6 = i + num2;
			if (num6 >= mapHeight)
			{
				continue;
			}
			int num7 = num6 * mapWidth + num;
			if (num7 < 0 || num7 >= tiles.Length)
			{
				continue;
			}
			int tid = tiles[num7];
			MetatileCollision metatileCollision = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid));
			if (metatileCollision == MetatileCollision.COL_NONE)
			{
				continue;
			}
			if (metatileCollision == MetatileCollision.COL_TOP_LEFT_STAIRS || metatileCollision == MetatileCollision.COL_TOP_RIGHT_STAIRS || metatileCollision == MetatileCollision.COL_BOTTOM_LEFT_STAIRS || metatileCollision == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS || metatileCollision == MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT || metatileCollision == MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT)
			{
				int num8 = num * 16;
				int num9 = i * 16;
				for (int j = Math.Max(playerY_px, num9); j <= Math.Min(num3, num9 + 15); j++)
				{
					int num10 = playerX_px - num8;
					int localY = j - num9;
					if (num10 < 0 || num10 >= 16 || !CheckComplexCollision(metatileCollision, num10, localY))
					{
						continue;
					}
					int item = num8;
					for (int num11 = 15; num11 >= 0; num11--)
					{
						if (CheckComplexCollision(metatileCollision, num11, localY))
						{
							item = num8 + num11 + 1;
							break;
						}
					}
					return (collided: true, collisionRightX: item);
				}
				continue;
			}
			var (num12, num13, num14, num15) = GetCollisionBounds(metatileCollision);
			if (num14 > num12 && num15 > num13)
			{
				int num16 = num * 16;
				int num17 = i * 16;
				int num18 = num17 + num13;
				int num19 = num17 + num15;
				int num20 = num16 + num12;
				int num21 = num16 + num14;
				if (playerX_px >= num20 && playerX_px < num21 && num3 >= num18 && playerY_px < num19)
				{
					return (collided: true, collisionRightX: num21);
				}
			}
		}
		return (collided: false, collisionRightX: 0);
	}

	private (bool collided, int collisionLeftX) CheckCollisionRight(int playerX_px, int playerY_px, int width, int height)
	{
		int num = playerX_px + width - 1;
		int num2 = num / 16;
		if (num2 < 0 || num2 >= mapWidth)
		{
			return (collided: false, collisionLeftX: 0);
		}
		int num3 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		int num4 = playerY_px + height - 1;
		int num5 = playerY_px / 16;
		int num6 = num4 / 16;
		for (int i = num5; i <= num6; i++)
		{
			if (i < 0 || i >= mapHeight)
			{
				continue;
			}
			int num7 = i + num3;
			if (num7 >= mapHeight)
			{
				continue;
			}
			int num8 = num7 * mapWidth + num2;
			if (num8 < 0 || num8 >= tiles.Length)
			{
				continue;
			}
			int tid = tiles[num8];
			MetatileCollision metatileCollision = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid));
			if (metatileCollision == MetatileCollision.COL_NONE)
			{
				continue;
			}
			if (metatileCollision == MetatileCollision.COL_TOP_LEFT_STAIRS || metatileCollision == MetatileCollision.COL_TOP_RIGHT_STAIRS || metatileCollision == MetatileCollision.COL_BOTTOM_LEFT_STAIRS || metatileCollision == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS || metatileCollision == MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT || metatileCollision == MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT)
			{
				int num9 = num2 * 16;
				int num10 = i * 16;
				for (int j = Math.Max(playerY_px, num10); j <= Math.Min(num4, num10 + 15); j++)
				{
					int num11 = num - num9;
					int localY = j - num10;
					if (num11 < 0 || num11 >= 16 || !CheckComplexCollision(metatileCollision, num11, localY))
					{
						continue;
					}
					int item = num9 + 15;
					for (int k = 0; k < 16; k++)
					{
						if (CheckComplexCollision(metatileCollision, k, localY))
						{
							item = num9 + k;
							break;
						}
					}
					return (collided: true, collisionLeftX: item);
				}
				continue;
			}
			var (num12, num13, num14, num15) = GetCollisionBounds(metatileCollision);
			if (num14 > num12 && num15 > num13)
			{
				int num16 = num2 * 16;
				int num17 = i * 16;
				int num18 = num17 + num13;
				int num19 = num17 + num15;
				int num20 = num16 + num12;
				int num21 = num16 + num14;
				if (num >= num20 && num < num21 && num4 >= num18 && playerY_px < num19)
				{
					return (collided: true, collisionLeftX: num20);
				}
			}
		}
		return (collided: false, collisionLeftX: 0);
	}

	private int GetMiniSpriteOffsetY()
	{
		if (currplayer_mini == 0)
		{
			return 0;
		}
		return 4;
	}

	private bool CheckCollisionAtPoint(int worldX, int worldY, MetatileCollision collision)
	{
		if (collision == MetatileCollision.COL_NONE)
		{
			return false;
		}
		if (collision >= MetatileCollision.COL_SLOPE_RD45)
		{
			return false;
		}
		int num = worldX / 16;
		int num2 = worldY / 16;
		int num3 = worldX - num * 16;
		int num4 = worldY - num2 * 16;
		var (num5, num6, num7, num8) = GetCollisionBoundsForType(collision);
		return num3 >= num5 && num3 < num7 && num4 >= num6 && num4 < num8;
	}

	private (int left, int top, int right, int bottom) GetCollisionBoundsForType(MetatileCollision collision)
	{
		switch (collision)
		{
		case MetatileCollision.COL_ALL:
		case MetatileCollision.COL_FLOOR_CEIL:
		case MetatileCollision.COL_NO_SIDE:
			return (left: 0, top: 0, right: 16, bottom: 16);
		case MetatileCollision.COL_TOP:
			return (left: 0, top: 0, right: 16, bottom: 8);
		case MetatileCollision.COL_BOTTOM:
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
			return (left: 0, top: 8, right: 8, bottom: 16);
		case MetatileCollision.COL_DOWN_RIGHT:
			return (left: 8, top: 8, right: 16, bottom: 16);
		default:
			return (left: 16, top: 16, right: 0, bottom: 0);
		}
	}

	private void bg_collision_sub()
	{
		int num = temp_x / 16;
		int num2 = temp_y / 16;
		if (num < 0 || num >= mapWidth || num2 < 0)
		{
			collision = 0;
			return;
		}
		int num3 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		int num4 = num2 + num3;
		if (num4 >= mapHeight)
		{
			collision = 10;
			return;
		}
		int num5 = num4 * mapWidth + num;
		if (num5 < 0 || num5 >= tiles.Length)
		{
			collision = 0;
			return;
		}
		int tid = tiles[num5];
		collision = (byte)MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid));
	}

	private bool bg_coll_slope()
	{
		tmp8 = temp_y & 0xF;
		if (collision < 44 || collision > 63)
		{
			return false;
		}
		switch ((MetatileCollision)collision)
		{
		case MetatileCollision.COL_SLOPE_LU45:
			tmp7 = temp_x & 0xF;
			tmp4 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 9;
			break;
		case MetatileCollision.COL_SLOPE_LD45:
			tmp7 = temp_x & 0xF;
			tmp4 = temp_y & 0xF;
			currplayer_slope_type = 1;
			break;
		case MetatileCollision.COL_SLOPE_RU45:
			tmp7 = (temp_x & 0xF) ^ 0xF;
			tmp4 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 13;
			break;
		case MetatileCollision.COL_SLOPE_RD45:
			tmp7 = (temp_x & 0xF) ^ 0xF;
			tmp4 = temp_y & 0xF;
			currplayer_slope_type = 5;
			break;
		case MetatileCollision.COL_SLOPE_RU22_RIGHT:
			tmp7 = ((temp_x >> 1) & 7) ^ 0xF;
			tmp4 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 14;
			break;
		case MetatileCollision.COL_SLOPE_RU22_LEFT:
			tmp7 = (((temp_x >> 1) | 8) & 0xF) ^ 0xF;
			tmp4 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 14;
			break;
		case MetatileCollision.COL_SLOPE_RD22_RIGHT:
			tmp7 = ((temp_x >> 1) & 7) ^ 0xF;
			tmp4 = temp_y & 0xF;
			currplayer_slope_type = 6;
			break;
		case MetatileCollision.COL_SLOPE_RD22_LEFT:
			tmp7 = (((temp_x >> 1) | 8) & 0xF) ^ 0xF;
			tmp4 = temp_y & 0xF;
			currplayer_slope_type = 6;
			break;
		case MetatileCollision.COL_SLOPE_LU22_RIGHT:
			tmp7 = (temp_x >> 1) & 7;
			tmp4 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 10;
			break;
		case MetatileCollision.COL_SLOPE_LU22_LEFT:
			tmp7 = ((temp_x >> 1) | 8) & 0xF;
			tmp4 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 10;
			break;
		case MetatileCollision.COL_SLOPE_LD22_RIGHT:
			tmp7 = (temp_x >> 1) & 7;
			tmp4 = temp_y & 0xF;
			currplayer_slope_type = 2;
			break;
		case MetatileCollision.COL_SLOPE_LD22_LEFT:
			tmp7 = ((temp_x >> 1) | 8) & 0xF;
			tmp4 = temp_y & 0xF;
			currplayer_slope_type = 2;
			break;
		case MetatileCollision.COL_SLOPE_RD66_TOP:
			if ((byte)(temp_x & 0xF) < 8)
			{
				return false;
			}
			tmp7 = (((temp_x & 7) << 1) & 0xF) ^ 0xF;
			tmp4 = temp_y & 0xF;
			currplayer_slope_type = 7;
			break;
		case MetatileCollision.COL_SLOPE_RD66_BOT:
			if ((byte)(temp_x & 0xF) >= 8)
			{
				return true;
			}
			tmp7 = (((temp_x & 0xF) << 1) & 0xF) ^ 0xF;
			tmp4 = temp_y & 0xF;
			currplayer_slope_type = 7;
			break;
		case MetatileCollision.COL_SLOPE_LD66_TOP:
			if ((byte)(temp_x & 0xF) >= 8)
			{
				return false;
			}
			tmp7 = ((temp_x & 7) << 1) & 0xF;
			tmp4 = temp_y & 0xF;
			currplayer_slope_type = 3;
			break;
		case MetatileCollision.COL_SLOPE_LD66_BOT:
			if ((byte)(temp_x & 0xF) < 8)
			{
				return true;
			}
			tmp7 = ((temp_x & 0xF) << 1) & 0xF;
			tmp4 = temp_y & 0xF;
			currplayer_slope_type = 3;
			break;
		case MetatileCollision.COL_SLOPE_RU66_TOP:
			if ((byte)(temp_x & 0xF) < 8)
			{
				return false;
			}
			tmp7 = (((temp_x & 7) << 1) & 0xF) ^ 0xF;
			tmp4 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 15;
			break;
		case MetatileCollision.COL_SLOPE_RU66_BOT:
			if ((byte)(temp_x & 0xF) >= 8)
			{
				return true;
			}
			tmp7 = (((temp_x & 0xF) << 1) & 0xF) ^ 0xF;
			tmp4 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 15;
			break;
		case MetatileCollision.COL_SLOPE_LU66_TOP:
			if ((byte)(temp_x & 0xF) >= 8)
			{
				return false;
			}
			tmp7 = ((temp_x & 7) << 1) & 0xF;
			tmp4 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 11;
			break;
		case MetatileCollision.COL_SLOPE_LU66_BOT:
			if ((byte)(temp_x & 0xF) < 8)
			{
				return true;
			}
			tmp7 = ((temp_x & 0xF) << 1) & 0xF;
			tmp4 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 11;
			break;
		default:
			return false;
		}
		if ((byte)tmp4 >= (byte)tmp7)
		{
			tmp8 = tmp4 - tmp7;
			currplayer_slope_frames = 1;
			currplayer_was_on_slope_counter = 3;
			if (keyXPressedCount > 0)
			{
				make_cube_jump_higher = true;
			}
			return true;
		}
		if (currplayer_was_on_slope_counter == 0)
		{
			currplayer_slope_type = 0;
			tmp8 = 0;
		}
		return false;
	}

	private bool bg_coll_return_slope_D()
	{
		tmp1 = (bg_coll_slope() ? 1 : 0);
		if (tmp2 == 0)
		{
			if ((currplayer_slope_type & 4) != 0)
			{
				currplayer_slope_type = currplayer_last_slope_type;
				return false;
			}
		}
		else if ((currplayer_slope_type & 4) == 0)
		{
			currplayer_slope_type = currplayer_last_slope_type;
			return false;
		}
		if (tmp1 != 0)
		{
			if ((currplayer_last_slope_type & 4) != 0 && (currplayer_slope_type & 4) == 0 && currplayer_last_slope_type != 0 && currplayer_slope_type != 0)
			{
				currplayer_slope_type = currplayer_last_slope_type;
				tmp8 = 0;
			}
			if (currplayer_slope_type != 0)
			{
				currplayer_last_slope_type = currplayer_slope_type;
			}
			eject_D = tmp8;
		}
		return tmp1 != 0;
	}

	private bool bg_coll_D()
	{
		if (playerX_fixed >> 8 >= 16)
		{
			temp_y = Generic_y + Generic_height - 2;
			temp_x = Generic_x;
			tmp2 = 0;
			tmp3 = 0;
			do
			{
				bg_collision_sub();
				if (collision != 0 && bg_coll_return_slope_D())
				{
					tmp3 |= 1;
				}
				temp_x += Generic_width;
			}
			while (++tmp2 < 2);
			if (tmp3 != 0)
			{
				return true;
			}
		}
		if ((playerVelY_fixed & 0x8000) == 0)
		{
			temp_x = Generic_x;
			temp_y = Generic_y + Generic_height;
			tmp8 = temp_y & 0xF;
			bg_collision_sub();
			if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
			{
				(int left, int top, int right, int bottom) collisionBoundsForType = GetCollisionBoundsForType((MetatileCollision)collision);
				int item = collisionBoundsForType.left;
				int item2 = collisionBoundsForType.top;
				int item3 = collisionBoundsForType.right;
				int item4 = collisionBoundsForType.bottom;
				int num = temp_y / 16;
				int num2 = num * 16;
				int num3 = num2 + item2;
				eject_D = temp_y - num3;
				AppendSimDebug($"[COLL_D] Pt1: temp_y={temp_y}, tileY={num}, tileWorldTop={num2}, colTop={item2}, collisionTop={num3}, eject_D={eject_D}, collision={collision}");
				return true;
			}
			temp_x += Generic_width >> 1;
			bg_collision_sub();
			if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
			{
				(int left, int top, int right, int bottom) collisionBoundsForType2 = GetCollisionBoundsForType((MetatileCollision)collision);
				int item5 = collisionBoundsForType2.left;
				int item6 = collisionBoundsForType2.top;
				int item7 = collisionBoundsForType2.right;
				int item8 = collisionBoundsForType2.bottom;
				int num4 = temp_y / 16;
				int num5 = num4 * 16;
				int num6 = num5 + item6;
				eject_D = temp_y - num6;
				return true;
			}
			temp_x = Generic_x + Generic_width;
			bg_collision_sub();
			if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
			{
				(int left, int top, int right, int bottom) collisionBoundsForType3 = GetCollisionBoundsForType((MetatileCollision)collision);
				int item9 = collisionBoundsForType3.left;
				int item10 = collisionBoundsForType3.top;
				int item11 = collisionBoundsForType3.right;
				int item12 = collisionBoundsForType3.bottom;
				int num7 = temp_y / 16;
				int num8 = num7 * 16;
				int num9 = num8 + item10;
				eject_D = temp_y - num9;
				return true;
			}
		}
		return false;
	}

	private bool bg_coll_U()
	{
		if ((playerVelY_fixed & 0x8000) != 0)
		{
			temp_x = Generic_x;
			temp_y = Generic_y - 1;
			tmp8 = 16 - (temp_y & 0xF);
			bg_collision_sub();
			if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
			{
				(int left, int top, int right, int bottom) collisionBoundsForType = GetCollisionBoundsForType((MetatileCollision)collision);
				int item = collisionBoundsForType.left;
				int item2 = collisionBoundsForType.top;
				int item3 = collisionBoundsForType.right;
				int item4 = collisionBoundsForType.bottom;
				int num = temp_y / 16;
				int num2 = num * 16;
				int num3 = num2 + item4;
				eject_U = -(num3 - temp_y);
				return true;
			}
			temp_x += Generic_width >> 1;
			bg_collision_sub();
			if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
			{
				(int left, int top, int right, int bottom) collisionBoundsForType2 = GetCollisionBoundsForType((MetatileCollision)collision);
				int item5 = collisionBoundsForType2.left;
				int item6 = collisionBoundsForType2.top;
				int item7 = collisionBoundsForType2.right;
				int item8 = collisionBoundsForType2.bottom;
				int num4 = temp_y / 16;
				int num5 = num4 * 16;
				int num6 = num5 + item8;
				eject_U = -(num6 - temp_y);
				return true;
			}
			temp_x = Generic_x + Generic_width;
			bg_collision_sub();
			if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
			{
				(int left, int top, int right, int bottom) collisionBoundsForType3 = GetCollisionBoundsForType((MetatileCollision)collision);
				int item9 = collisionBoundsForType3.left;
				int item10 = collisionBoundsForType3.top;
				int item11 = collisionBoundsForType3.right;
				int item12 = collisionBoundsForType3.bottom;
				int num7 = temp_y / 16;
				int num8 = num7 * 16;
				int num9 = num8 + item12;
				eject_U = -(num9 - temp_y);
				return true;
			}
		}
		return false;
	}

	private bool wave_coll_D()
	{
		temp_x = Generic_x + 4;
		temp_y = Generic_y + Generic_height;
		tmp8 = temp_y & 0xF;
		bg_collision_sub();
		if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
		{
			(int left, int top, int right, int bottom) collisionBoundsForType = GetCollisionBoundsForType((MetatileCollision)collision);
			int item = collisionBoundsForType.left;
			int item2 = collisionBoundsForType.top;
			int item3 = collisionBoundsForType.right;
			int item4 = collisionBoundsForType.bottom;
			int num = temp_y / 16;
			int num2 = num * 16;
			int num3 = num2 + item2;
			eject_D = temp_y - num3;
			return true;
		}
		temp_x += Generic_width >> 1;
		bg_collision_sub();
		if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
		{
			(int left, int top, int right, int bottom) collisionBoundsForType2 = GetCollisionBoundsForType((MetatileCollision)collision);
			int item5 = collisionBoundsForType2.left;
			int item6 = collisionBoundsForType2.top;
			int item7 = collisionBoundsForType2.right;
			int item8 = collisionBoundsForType2.bottom;
			int num4 = temp_y / 16;
			int num5 = num4 * 16;
			int num6 = num5 + item6;
			eject_D = temp_y - num6;
			return true;
		}
		temp_x = Generic_x + 4 + Generic_width;
		bg_collision_sub();
		if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
		{
			(int left, int top, int right, int bottom) collisionBoundsForType3 = GetCollisionBoundsForType((MetatileCollision)collision);
			int item9 = collisionBoundsForType3.left;
			int item10 = collisionBoundsForType3.top;
			int item11 = collisionBoundsForType3.right;
			int item12 = collisionBoundsForType3.bottom;
			int num7 = temp_y / 16;
			int num8 = num7 * 16;
			int num9 = num8 + item10;
			eject_D = temp_y - num9;
			return true;
		}
		return false;
	}

	private bool wave_coll_U()
	{
		temp_x = Generic_x + 4;
		int num = ((currplayer_mini != 0) ? (16 - Generic_height >> 1) : 0);
		temp_y = Generic_y + num;
		tmp8 = 16 - (temp_y & 0xF);
		bg_collision_sub();
		if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
		{
			(int left, int top, int right, int bottom) collisionBoundsForType = GetCollisionBoundsForType((MetatileCollision)collision);
			int item = collisionBoundsForType.left;
			int item2 = collisionBoundsForType.top;
			int item3 = collisionBoundsForType.right;
			int item4 = collisionBoundsForType.bottom;
			int num2 = temp_y / 16;
			int num3 = num2 * 16;
			int num4 = num3 + item4;
			eject_U = -(num4 - temp_y);
			return true;
		}
		temp_x += Generic_width >> 1;
		bg_collision_sub();
		if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
		{
			(int left, int top, int right, int bottom) collisionBoundsForType2 = GetCollisionBoundsForType((MetatileCollision)collision);
			int item5 = collisionBoundsForType2.left;
			int item6 = collisionBoundsForType2.top;
			int item7 = collisionBoundsForType2.right;
			int item8 = collisionBoundsForType2.bottom;
			int num5 = temp_y / 16;
			int num6 = num5 * 16;
			int num7 = num6 + item8;
			eject_U = -(num7 - temp_y);
			return true;
		}
		temp_x = Generic_x + Generic_width;
		bg_collision_sub();
		if (CheckCollisionAtPoint(temp_x, temp_y, (MetatileCollision)collision))
		{
			(int left, int top, int right, int bottom) collisionBoundsForType3 = GetCollisionBoundsForType((MetatileCollision)collision);
			int item9 = collisionBoundsForType3.left;
			int item10 = collisionBoundsForType3.top;
			int item11 = collisionBoundsForType3.right;
			int item12 = collisionBoundsForType3.bottom;
			int num8 = temp_y / 16;
			int num9 = num8 * 16;
			int num10 = num9 + item12;
			eject_U = -(num10 - temp_y);
			return true;
		}
		return false;
	}

	private void cube_eject_NES()
	{
		if (!gravityFlipped)
		{
			if (bg_coll_D())
			{
				int num = playerY_fixed >> 8;
				num -= eject_D;
				playerY_fixed = num << 8;
				playerVelY_fixed = 0;
			}
		}
		else if (bg_coll_U())
		{
			int num2 = playerY_fixed >> 8;
			num2 -= eject_U;
			playerY_fixed = num2 << 8;
			playerVelY_fixed = 0;
		}
	}

	private void ProcessCubePhysics_Fresh()
	{
		try
		{
			int num = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			AppendSimDebug($"[CUBE_START] posY=0x{playerY_fixed:X4} ({playerY_fixed >> 8}px), velY=0x{playerVelY_fixed:X4}, gravity=0x{currplayer_gravity:X2}, mini={currplayer_mini}");
			bool flag = IsXDownAsync() || keyXHeld;
			int num2 = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
			bool xPressed = num2 > 0;
			bool gravityInverted = currplayer_gravity != 0;
			int playerX_px = (playerX_fixed >> 8) + 1;
			int num3 = playerY_fixed >> 8;
			int playerW = ((currplayer_mini != 0) ? 8 : 15);
			int playerH = ((currplayer_mini != 0) ? 7 : 15);
			if (currplayer_mini != 0)
			{
				num3 += 4;
			}
			int scrollX_px = 0;
			int num4 = playerVelY_fixed;
			if (UpdateOrbSystem(0, playerX_px, num3, playerW, playerH, scrollX_px, xPressed, flag, gravityInverted, currplayer_mini != 0, ref num4).activated)
			{
				playerVelY_fixed = num4;
			}
			if (!flag)
			{
				ClearOrbBuffer();
			}
			bool flag2 = playerVelY_fixed >= -16 && playerVelY_fixed <= 16;
			tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(currplayer_table_idx, CUBE_MAX_FALLSPEED);
			tmpgravity = GameModePhysics.CUBE_GRAVITY(currplayer_table_idx);
			int table_idx = ((currplayer_mini != 0) ? 4 : 0);
			int num5 = ((currplayer_gravity == 0) ? 1 : (-1));
			tmpgravity = GameModePhysics.CUBE_GRAVITY(table_idx) * num5;
			tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(table_idx, CUBE_MAX_FALLSPEED) * num5;
			AppendSimDebug($"[CUBE_PRE_GRAV] tmpgravity=0x{tmpgravity:X}, tmpfallspeed=0x{tmpfallspeed:X}");
			CommonGravityRoutine_Fresh();
			if (currplayer_gravity != 0)
			{
				bool flag3 = currplayer_mini != 0;
				int width = (flag3 ? 8 : 15);
				int height = (flag3 ? 7 : 15);
				int miniCenterOffsetY = SharedPhysics.GetMiniCenterOffsetY(flag3);
				int playerX_px2 = playerX_fixed >> 8;
				int playerY_px = (playerY_fixed >> 8) + miniCenterOffsetY - 1;
				var (flag4, num6) = CheckCollisionUp(playerX_px2, playerY_px, width, height);
				if (flag4 && playerVelY_fixed < 0)
				{
					int num7 = num6 - miniCenterOffsetY - 1;
					playerY_fixed = num7 << 8;
					playerVelY_fixed = 0;
				}
			}
			CubeEject_Fresh();
			CheckCenterPointDeath_Fresh();
			if (playerVelY_fixed == 0 && currentGameMode != 11)
			{
				bool flag5 = IsXDownAsync() || keyXHeld;
				int num8 = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
				bool flag6 = num8 > 0;
				if (pathfinderEnabled && (flag5 || flag6))
				{
					AppendSimDebug($"[PF-JUMP] hold={flag5}, press={flag6}, velY=0x{playerVelY_fixed:X4}, orbed={orbed[currplayer]}, dashing={dashing[currplayer]}, wasZeroed={wasZeroedByCollisionLastFrame}, pfIdx={pfFrameIndex}");
				}
				bool flag7 = playerVelY_fixed >= -16 && playerVelY_fixed <= 16;
				bool flag8 = false;
				if ((pathfinderEnabled ? flag6 : flag5) && !jblocked && !fblocked && flag7 && !orbed[currplayer] && dashing[currplayer] == 0)
				{
					flag8 = true;
				}
				else if (flag6 && (jblocked || fblocked) && flag7 && dashing[currplayer] == 0)
				{
					flag8 = true;
				}
				if (flag8)
				{
					if (pathfinderEnabled)
					{
						AppendSimDebug($"[PF-JUMP] JUMP TRIGGERED! velY → 0x{GameModePhysics.JUMP_VEL((currplayer_mini != 0) ? 4 : 0) * ((currplayer_gravity == 0) ? 1 : (-1)):X4}");
					}
					Interlocked.Exchange(ref keyXPressedCount, 0);
					int table_idx2 = ((currplayer_mini != 0) ? 4 : 0);
					int num9 = ((currplayer_gravity == 0) ? 1 : (-1));
					int num10 = GameModePhysics.JUMP_VEL(table_idx2) * num9;
					playerVelY_fixed = num10;
					SlopeJumpCheck_Fresh();
				}
				UpdateOrbHoldSuppression(flag5, flag7);
			}
			if (currentGameMode == 11 && (IsXDownAsync() || keyXHeld) && !orbed[currplayer])
			{
				chargepower[0]++;
				if (chargepower[0] >= 45)
				{
					chargepower[0] = 0;
					orbed[currplayer] = true;
				}
				AppendSimDebug($"[FOOTBALL] Charging: chargepower={chargepower[0]}, orbed={orbed[currplayer]}");
			}
			if (currentGameMode == 11)
			{
				bool flag9 = IsXDownAsync() || keyXHeld;
				if (!flag9)
				{
					int num11 = chargepower[0];
					AppendSimDebug($"[FOOTBALL] Release triggered: tmp3={num11}, holdX={flag9}");
					orbed[currplayer] = false;
					int num12 = ((currplayer_mini != 0) ? 4 : 0);
					bool flag10 = currplayer_gravity != 0;
					int num13 = (flag10 ? 1 : (-1));
					int value = num11 * (76 * num13);
					bool flag11 = playerVelY_fixed == 0 || (!flag10 && playerVelY_fixed > 0 && playerVelY_fixed < 256) || (flag10 && playerVelY_fixed < 0 && playerVelY_fixed > -256);
					if (num11 > 0 && flag11)
					{
						playerVelY_fixed = value;
						SlopeJumpCheck_Fresh();
						AppendSimDebug($"[FOOTBALL] Released! chargepower={num11}, tmpA=0x{value:X4}, velY now = {playerVelY_fixed}");
					}
					chargepower[0] = 0;
				}
			}
			jblocked = false;
			fblocked = false;
			hblocked = false;
			UpdateSlopeCounters_Fresh();
			if (!pfSimulating)
			{
				try
				{
					int num14 = (playerX_fixed >> 8) + playerVisualWidth / 2;
					int num15 = (playerY_fixed >> 8) + playerVisualHeight / 2;
					if (currplayer_mini != 0)
					{
						num15 += 4;
					}
					if (currplayer == 0)
					{
						recordedPlayerPath.Add((num14, num15));
					}
					else if (dual)
					{
						RecordP2PathPoint(num14, num15);
					}
				}
				catch
				{
				}
			}
			if (!onGround || playerVelY_fixed != 0)
			{
				return;
			}
			try
			{
				bool flag12 = false;
				if (currplayer_gravity == 0)
				{
					int num16 = (playerX_fixed >> 8) + 8;
					int num17 = num16 - 7;
					int num18 = num17 + 14;
					int num19 = ((currplayer_mini != 0) ? 7 : 15);
					int num20 = ((currplayer_mini != 0) ? 9 : 0);
					int num21 = (playerY_fixed >> 8) + num20 + num19;
					int num22 = num21 / 16;
					int num23 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
					int num24 = num22 + num23;
					if (num24 >= mapHeight)
					{
						flag12 = true;
					}
					else if (num24 >= 0)
					{
						int localY = (num21 % 16 + 16) % 16;
						for (int i = num17 / 16; i <= num18 / 16; i++)
						{
							if (i >= 0 && i < mapWidth)
							{
								int tid = tiles[num24 * mapWidth + i];
								MetatileCollision col = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid));
								int num25 = i * 16;
								int localX = Math.Max(0, Math.Min(15, num16 - num25));
								if (ProvidesFloorAtColumnStatic(col, localX, out var _) && SharedPhysics.TileOccupiesPixel(col, localX, localY))
								{
									flag12 = true;
									break;
								}
							}
						}
					}
				}
				else
				{
					flag12 = IsTouchingCeiling();
				}
				if (!flag12)
				{
					onGround = false;
					wasZeroedByCollisionLastFrame = false;
					AppendSimDebug("[CUBE] Ground support lost - clearing onGround flag and collision flag");
				}
			}
			catch
			{
				onGround = false;
			}
		}
		catch (Exception ex)
		{
			try
			{
				AppendSimDebug("ProcessCubePhysics_Fresh error: " + ex.Message);
			}
			catch
			{
			}
		}
	}

	private void CommonGravityRoutine_Fresh()
	{
		AppendSimDebug($"[GRAV_START] velY=0x{playerVelY_fixed:X4}, posY=0x{playerY_fixed:X4} ({playerY_fixed >> 8}px), dashing={dashing[currplayer]}, wasZeroedByCollision={wasZeroedByCollisionLastFrame}, mode={currentGameMode}");
		int value = playerVelY_fixed;
		int num = playerY_fixed;
		int clampMaxY = Math.Max(0, mapHeight * 16 - playerVisualHeight) << 8;
		SharedPhysics.CommonGravityRoutine(ref playerVelY_fixed, ref playerY_fixed, tmpgravity, tmpfallspeed, currplayer_gravity, dashing[currplayer], gravityMultiplier, simTimeScale, isFullSpeed, playerVelX_fixed, clampMaxY);
		AppendSimDebug($"[GRAV_POS] posY: 0x{num:X4} ({num >> 8}px) -> 0x{playerY_fixed:X4} ({playerY_fixed >> 8}px), velY: 0x{value:X4} -> 0x{playerVelY_fixed:X4}");
	}

	private void CubeEject_Fresh()
	{
		bool mini = currplayer_mini != 0;
		bool flag = currplayer_gravity != 0;
		bool inputHeld = IsXDownAsync() || keyXHeld || upHeld;
		int groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		SharedPhysics.EjectResult ejectResult = SharedPhysics.CubeEject(new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve), playerX_fixed, playerY_fixed, playerVelY_fixed, playerVelX_fixed, flag, mini, currentGameMode, inputHeld, currplayer_was_on_slope_counter, currplayer_slope_frames, currplayer_slope_type, make_cube_jump_higher, currplayer_last_slope_type, cameraY_fixed);
		playerY_fixed = ejectResult.NewY_fixed;
		playerVelY_fixed = ejectResult.NewVelY_fixed;
		onGround = ejectResult.OnGround;
		wasZeroedByCollisionLastFrame = ejectResult.WasZeroed;
		currplayer_slope_type = ejectResult.SlopeType;
		currplayer_slope_frames = ejectResult.SlopeFrames;
		currplayer_was_on_slope_counter = ejectResult.SlopeWasOnCounter;
		make_cube_jump_higher = ejectResult.SlopeJumpHigher;
		currplayer_last_slope_type = ejectResult.LastSlopeType;
		if (ejectResult.Died && !MainWindow.Option_NoDeath)
		{
			AppendSimDebug("[DEATH] Floor spike detected (SharedPhysics.CubeEject)");
			deathTriggered = true;
			deathTileX = playerX_fixed >> 8;
			deathTileY = (playerY_fixed >> 8) + SharedPhysics.GetCubeHitboxH(mini);
			paused = true;
			StopMusicAsync();
			try
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					try
					{
						PauseOverlay.Visibility = Visibility.Collapsed;
					}
					catch
					{
					}
					if (base.Owner is MainWindow mainWindow)
					{
						try
						{
							mainWindow.PauseSimulatorPlayback();
						}
						catch
						{
						}
						try
						{
							mainWindow.AddDeathMarker(deathTileX, deathTileY);
						}
						catch
						{
						}
					}
				});
				return;
			}
			catch
			{
				return;
			}
		}
		if ((currentGameMode != 0 && currentGameMode != 4 && currentGameMode != 8 && currentGameMode != 11) || (!hblocked && !fblocked))
		{
			return;
		}
		int cubeHitboxW = SharedPhysics.GetCubeHitboxW(mini);
		int cubeHitboxH = SharedPhysics.GetCubeHitboxH(mini);
		int hitboxOffsetY = SharedPhysics.GetHitboxOffsetY(currentGameMode, mini, flag);
		int playerX_px = playerX_fixed >> 8;
		if (!flag)
		{
			if ((short)(playerVelY_fixed & 0xFFFF) >= 0)
			{
				return;
			}
			int playerY_px = (playerY_fixed >> 8) + hitboxOffsetY;
			var (flag2, num) = CheckCollisionUp(playerX_px, playerY_px, cubeHitboxW, cubeHitboxH);
			if (flag2)
			{
				int num2 = num - hitboxOffsetY;
				AppendSimDebug($"[CUBE]     H/F_BLOCK ceiling eject: Y {playerY_fixed >> 8} -> {num2}, velY -> {(hblocked ? "1" : "0")}");
				playerY_fixed = num2 << 8;
				playerVelY_fixed = (hblocked ? 1 : 0);
				if (!hblocked)
				{
					onGround = true;
				}
				orbed[currplayer] = false;
				if (fblocked)
				{
					currplayer_gravity = byte.MaxValue;
					gravityFlipped = true;
					gravityReversed = true;
					currplayer_table_idx = ((currplayer_gravity != 0) ? 1 : 0) | ((currplayer_mini != 0) ? 4 : 0);
				}
			}
		}
		else
		{
			if ((short)(playerVelY_fixed & 0xFFFF) < 0)
			{
				return;
			}
			int playerY_px2 = (playerY_fixed >> 8) + hitboxOffsetY;
			var (flag3, num3) = CheckCollisionDown(playerX_px, playerY_px2, cubeHitboxW, cubeHitboxH);
			if (flag3)
			{
				int num4 = num3 - cubeHitboxH - hitboxOffsetY;
				AppendSimDebug($"[CUBE]     H/F_BLOCK floor eject: Y {playerY_fixed >> 8} -> {num4}, velY -> {(hblocked ? "-1" : "0")}");
				playerY_fixed = num4 << 8;
				playerVelY_fixed = (hblocked ? (-1) : 0);
				if (!hblocked)
				{
					onGround = true;
				}
				orbed[currplayer] = false;
				if (fblocked)
				{
					currplayer_gravity = 0;
					gravityFlipped = false;
					gravityReversed = false;
					currplayer_table_idx = ((currplayer_gravity != 0) ? 1 : 0) | ((currplayer_mini != 0) ? 4 : 0);
				}
			}
		}
	}

	private void CheckCenterPointDeath_Fresh()
	{
		if (MainWindow.Option_NoDeath || deathTriggered)
		{
			return;
		}
		int num = playerX_fixed >> 8;
		int num2 = playerY_fixed >> 8;
		int num3 = ((currplayer_mini != 0) ? 8 : 15);
		int num4 = ((currplayer_mini != 0) ? 7 : 15);
		int cubeHitboxOffsetY = SharedPhysics.GetCubeHitboxOffsetY(currplayer_mini != 0, currplayer_gravity != 0);
		int centerX_px = num + (num3 >> 1) - 1;
		int centerY_px = num2 + (num4 >> 1) + cubeHitboxOffsetY;
		int num5 = centerX_px / 16;
		int num6 = centerY_px / 16;
		if (num5 < 0 || num5 >= mapWidth || num6 < 0 || num6 >= mapHeight)
		{
			return;
		}
		int num7 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		int num8 = num6 + num7;
		if (num8 >= mapHeight)
		{
			return;
		}
		int num9 = num8 * mapWidth + num5;
		if (num9 < 0 || num9 >= tiles.Length)
		{
			return;
		}
		int num10 = tiles[num9];
		MetatileCollision metatileCollision = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(num10));
		int localX = centerX_px % 16;
		int localY = centerY_px % 16;
		bool flag = MetatileCollisionTable.TileKillsAtPixel(metatileCollision, localX, localY);
		if (!flag)
		{
			MetatileCollision metatileCollision2 = metatileCollision;
			MetatileCollision metatileCollision3 = metatileCollision2;
			if ((uint)(metatileCollision3 - 1) <= 9u || (uint)(metatileCollision3 - 32) <= 1u || (uint)(metatileCollision3 - 37) <= 6u)
			{
				flag = SharedPhysics.TileOccupiesPixel(metatileCollision, localX, localY);
			}
		}
		if (!flag)
		{
			return;
		}
		AppendSimDebug($"[DEATH] Center point spike at ({centerX_px},{centerY_px}) tile={num10:X2} col={metatileCollision}");
		deathTriggered = true;
		deathTileX = centerX_px;
		deathTileY = centerY_px;
		if (pfSimulating)
		{
			return;
		}
		paused = true;
		StopMusicAsync();
		try
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				try
				{
					PauseOverlay.Visibility = Visibility.Collapsed;
				}
				catch
				{
				}
				if (base.Owner is MainWindow mainWindow)
				{
					try
					{
						mainWindow.PauseSimulatorPlayback();
					}
					catch
					{
					}
					try
					{
						mainWindow.AddDeathMarker(centerX_px, centerY_px);
					}
					catch
					{
					}
				}
			});
		}
		catch
		{
		}
	}

	private void EnableCubePhysics_Fresh()
	{
		try
		{
			currplayer_mini = 0;
			currplayer_gravity = 0;
		}
		catch (Exception ex)
		{
			try
			{
				AppendSimDebug("EnableCubePhysics_Fresh error: " + ex.Message);
			}
			catch
			{
			}
		}
	}

	private void FootballPhysics_Fresh()
	{
		int table_idx = (miniMode ? 4 : 0);
		bool flag = gravityFlipped;
		int num = ((!flag) ? 1 : (-1));
		tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(table_idx, CUBE_MAX_FALLSPEED) * num;
		tmpgravity = GameModePhysics.CUBE_GRAVITY(table_idx) * num;
		CommonGravityRoutine_Fresh();
		hblocked = true;
		CubeEject_Fresh();
		hblocked = false;
		UpdateSlopeCounters_Fresh();
		bool flag2 = IsXDownAsync() || keyXHeld;
		AppendSimDebug($"[FOOTBALL] xHeld={flag2}, wasHeld={footballWasHeld}, chargeFrames={footballChargeFrames}, orbed={footballOrbed}");
		if (flag2 && !footballOrbed)
		{
			footballChargeFrames++;
			AppendSimDebug($"[FOOTBALL] Charging: {footballChargeFrames}/50 (power maxes at 45)");
			if (footballChargeFrames >= 50)
			{
				footballOrbed = true;
				AppendSimDebug("[FOOTBALL] MAX CHARGE - orbed=true, charge capped at frame 45 power");
			}
			footballWasHeld = true;
		}
		else if (!flag2 && footballWasHeld)
		{
			AppendSimDebug($"[FOOTBALL] RELEASE DETECTED: chargeFrames={footballChargeFrames}, wasHeld={footballWasHeld}, xHeld={flag2}");
			footballOrbed = false;
			footballWasHeld = false;
			AppendSimDebug($"[FOOTBALL] Released: chargeFrames={footballChargeFrames}, grounded={Math.Abs(playerVelY_fixed) <= 107}, velY_before=0x{playerVelY_fixed:X4}");
			if (footballChargeFrames > 0 && footballChargeFrames < 50 && Math.Abs(playerVelY_fixed) <= 107)
			{
				int num2 = Math.Min(footballChargeFrames, 45);
				int num3 = num2 * 76;
				playerVelY_fixed = (flag ? num3 : (-num3));
				AppendSimDebug($"[FOOTBALL] JUMP! chargeFrames={footballChargeFrames}, effectiveCharge={num2}, velocity_set=0x{playerVelY_fixed:X4}");
			}
			else if (footballChargeFrames == 0)
			{
				AppendSimDebug("[FOOTBALL] NO CHARGE - not jumping");
			}
			else if (footballChargeFrames >= 50)
			{
				AppendSimDebug("[FOOTBALL] OVERCHARGED (>=50) - charge is dead, no jump");
			}
			else if (Math.Abs(playerVelY_fixed) > 107)
			{
				AppendSimDebug($"[FOOTBALL] NOT ON GROUND (velY=0x{playerVelY_fixed:X4}) - not jumping");
			}
			footballChargeFrames = 0;
		}
		else if (!flag2)
		{
			footballWasHeld = false;
		}
		if (pfSimulating)
		{
			return;
		}
		try
		{
			int num4 = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num5 = playerY_fixed >> 8;
			if (miniMode)
			{
				num5 += 4;
			}
			int num6 = num5 + playerVisualHeight / 2;
			if (currplayer == 0)
			{
				recordedPlayerPath.Add((num4, num6));
			}
			else if (dual)
			{
				RecordP2PathPoint(num4, num6);
			}
		}
		catch
		{
		}
	}

	private bool BgCollD_Fresh()
	{
		Generic_x = playerX_fixed >> 8;
		Generic_y = playerY_fixed >> 8;
		Generic_width = ((currplayer_mini != 0) ? 8 : 15);
		Generic_height = ((currplayer_mini != 0) ? 7 : 15);
		if (currplayer_mini != 0)
		{
			int num = 16 - Generic_height >> 1;
			if (currplayer_gravity == 0)
			{
				Generic_y += num;
			}
		}
		return bg_coll_D();
	}

	private bool BgCollU_Fresh()
	{
		Generic_x = playerX_fixed >> 8;
		Generic_y = playerY_fixed >> 8;
		Generic_width = ((currplayer_mini != 0) ? 8 : 15);
		Generic_height = ((currplayer_mini != 0) ? 7 : 15);
		if (currplayer_mini != 0)
		{
			int num = 16 - Generic_height >> 1;
			if (currplayer_gravity == 0)
			{
				Generic_y += num;
			}
		}
		return bg_coll_U();
	}

	private void NinjaPhysics_Fresh()
	{
		bool flag = IsXDownAsync() || keyXHeld;
		int num = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
		bool flag2 = num > 0;
		bool gravityInverted = currplayer_gravity != 0;
		int playerX_px = (playerX_fixed >> 8) + 1;
		int num2 = playerY_fixed >> 8;
		int playerW = ((currplayer_mini != 0) ? 8 : 15);
		int playerH = ((currplayer_mini != 0) ? 7 : 15);
		if (currplayer_mini != 0)
		{
			num2 += 4;
		}
		int scrollX_px = 0;
		int num3 = playerVelY_fixed;
		if (UpdateOrbSystem(8, playerX_px, num2, playerW, playerH, scrollX_px, flag2, flag, gravityInverted, currplayer_mini != 0, ref num3).activated)
		{
			playerVelY_fixed = num3;
			AppendSimDebug($"[NINJA] Orb activated! New velY={playerVelY_fixed}");
			if (flag2)
			{
				Interlocked.Exchange(ref keyXPressedCount, 0);
			}
		}
		if (!flag)
		{
			ClearOrbBuffer();
		}
		int table_idx = ((currplayer_mini != 0) ? 4 : 0);
		int num4 = ((currplayer_gravity == 0) ? 1 : (-1));
		tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(table_idx, CUBE_MAX_FALLSPEED) * num4;
		tmpgravity = GameModePhysics.CUBE_GRAVITY(table_idx) * num4;
		ninjaJumpedThisFrame = false;
		CommonGravityRoutine_Fresh();
		if (currplayer_gravity != 0)
		{
			bool flag3 = currplayer_mini != 0;
			int width = (flag3 ? 8 : 15);
			int height = (flag3 ? 7 : 15);
			int miniCenterOffsetY = SharedPhysics.GetMiniCenterOffsetY(flag3);
			int playerX_px2 = playerX_fixed >> 8;
			int playerY_px = (playerY_fixed >> 8) + miniCenterOffsetY - 1;
			var (flag4, num5) = CheckCollisionUp(playerX_px2, playerY_px, width, height);
			if (flag4 && playerVelY_fixed < 0)
			{
				int num6 = num5 - miniCenterOffsetY - 1;
				playerY_fixed = num6 << 8;
				playerVelY_fixed = 0;
			}
		}
		byte b = currplayer_gravity;
		CubeEject_Fresh();
		bool flag5 = IsXDownAsync() || keyXHeld;
		int num7 = Interlocked.Exchange(ref keyXPressedCount, 0);
		bool flag6 = num7 > 0;
		if (onGround && !flag6)
		{
			ninjajumps[currplayer] = 3;
			AppendSimDebug("[NINJA] Grounded - reset jumps to 3");
		}
		if (flag6 && ninjajumps[currplayer] > 0 && !ninjaJumpedThisFrame && !orbed[currplayer] && dashing[currplayer] == 0)
		{
			int table_idx2 = ((currplayer_mini != 0) ? 4 : 0);
			int num8 = ((currplayer_gravity == 0) ? 1 : (-1));
			playerVelY_fixed = GameModePhysics.JUMP_VEL(table_idx2) * num8;
			ninjajumps[currplayer]--;
			SlopeJumpCheck_Fresh();
			ninjaJumpedThisFrame = true;
			AppendSimDebug($"[NINJA] Jump! Remaining={ninjajumps[currplayer]}, vel={playerVelY_fixed}");
		}
		UpdateSlopeCounters_Fresh();
		if (pfSimulating)
		{
			return;
		}
		try
		{
			int num9 = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num10 = playerY_fixed >> 8;
			if (currplayer_mini != 0)
			{
				num10 += 4;
			}
			int num11 = num10 + playerVisualHeight / 2;
			if (currplayer == 0)
			{
				recordedPlayerPath.Add((num9, num11));
			}
			else if (dual)
			{
				RecordP2PathPoint(num9, num11);
			}
		}
		catch
		{
		}
	}

	private void ClearOrbBuffer()
	{
		orbBufferActive[currplayer] = false;
		orbHoldConsumedKeyStillDown[currplayer] = false;
		ballInputBufferCountdown[currplayer] = 0;
		keyXHeldStartedOnGround = false;
	}

	private void UpdateOrbHoldSuppression(bool xHeld, bool isGrounded)
	{
		if (xHeld && isGrounded)
		{
			keyXHeldStartedOnGround = true;
			orbHoldSuppressing[currplayer] = true;
		}
		else if (!xHeld)
		{
			keyXHeldStartedOnGround = false;
			orbHoldSuppressing[currplayer] = false;
		}
		else if (!isGrounded && keyXHeldStartedOnGround)
		{
			orbHoldSuppressing[currplayer] = true;
		}
	}

	private bool CheckOrbCollision(int idx, int spriteType, int playerLeft_px, int playerRight_px, int playerTop_px, int playerBottom_px)
	{
		try
		{
			int num = idx % mapWidth;
			int num2 = idx / mapWidth;
			int num3 = spriteType & 0xFF;
			int num4 = -1;
			if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out (int, int) value))
			{
				num4 = value.Item2 * mapWidth + value.Item1;
				if (num4 >= 0 && num4 < sprites.Length)
				{
					int num5 = sprites[num4];
					if (num5 >= 0 && num5 < 256)
					{
						num3 = num5 & 0xFF;
					}
				}
			}
			int val = ((num3 >= 0 && num3 < sprite_widths.Length) ? sprite_widths[num3] : 16);
			int num6 = ((num3 >= 0 && num3 < sprite_heights.Length) ? sprite_heights[num3] : 16);
			if (num6 >= 252)
			{
				return false;
			}
			int num7 = ((num3 >= 0 && num3 < sprite_x_offset.Length) ? sprite_x_offset[num3] : 0);
			int num8 = ((num3 >= 0 && num3 < sprite_y_offset.Length) ? sprite_y_offset[num3] : 0);
			int num9 = 0;
			int num10 = 0;
			(int, int) value3;
			if (num4 >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num4, out (int, int) value2))
			{
				(num9, num10) = value2;
			}
			else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out value3))
			{
				(num9, num10) = value3;
			}
			int num11 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			int num12 = num * 16 + num7 + num9;
			int num13 = (num2 - num11) * 16 + num8 + num10 - 1;
			int num14 = num12 + Math.Max(1, val);
			int num15 = num13 + Math.Max(1, num6);
			return playerRight_px + 1 >= num12 && num14 >= playerLeft_px && playerBottom_px + 1 >= num13 && num15 >= playerTop_px;
		}
		catch
		{
			return false;
		}
	}

	private (bool activated, int orbType) UpdateOrbSystem(int gamemode, int playerX_px, int playerY_px, int playerW, int playerH, int scrollX_px, bool xPressed, bool xHeld, bool gravityInverted, bool mini, ref int velocityY)
	{
		bool flag = CanBufferOrb(gamemode);
		bool item = false;
		int item2 = -1;
		int playerRight_px = playerX_px + playerW - 1;
		int playerBottom_px = playerY_px + playerH - 1;
		int num = -1;
		int num2 = -1;
		int num3 = -1;
		bool flag2 = false;
		bool flag3 = false;
		for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
		{
			int num4 = nonEmptySpriteIndices[i];
			int num5 = sprites[num4];
			if (num5 == -1 || (spriteAnchors != null && spriteAnchors.ContainsKey(num4)) || !IsOrbSprite(num5) || dashing[currplayer] != 0 || !CheckOrbCollision(num4, num5, playerX_px, playerRight_px, playerY_px, playerBottom_px))
			{
				continue;
			}
			bool flag4 = num5 == 123 || num5 == 124;
			if (!flag4 && playerProcessedOrbs[currplayer].Contains(num4))
			{
				continue;
			}
			bool flag5 = false;
			if (flag)
			{
				if (xPressed && !orbHoldConsumedKeyStillDown[currplayer] && !orbHoldSuppressing[currplayer])
				{
					flag5 = true;
				}
				else if (xHeld && orbBufferActive[currplayer] && !orbHoldSuppressing[currplayer])
				{
					flag5 = true;
				}
				else if (xHeld && !orbBufferActive[currplayer] && !orbHoldSuppressing[currplayer] && !orbHoldConsumedKeyStillDown[currplayer])
				{
					flag5 = true;
				}
			}
			else if (xPressed)
			{
				flag5 = true;
			}
			if (flag5)
			{
				num = num4;
				num2 = i;
				num3 = num5;
				flag2 = flag4;
				flag3 = true;
			}
		}
		if (flag3)
		{
			try
			{
				AppendSimDebug($"[ORB] ACTIVATING orb 0x{num3:X2}!");
			}
			catch
			{
			}
			ClearSlopeStuff();
			ActivateOrb(num3, gamemode, gravityInverted, mini, ref velocityY);
			if (flag)
			{
				orbBufferActive[currplayer] = true;
			}
			if (!flag2)
			{
				playerProcessedOrbs[currplayer].Add(num);
				if (!dual)
				{
					orbActivated[num] = true;
				}
				for (int j = 0; j < nonEmptySpriteIndices.Length; j++)
				{
					if (j == num2)
					{
						continue;
					}
					int num6 = nonEmptySpriteIndices[j];
					if (sprites[num6] == num3 && (spriteAnchors == null || !spriteAnchors.ContainsKey(num6)) && !playerProcessedOrbs[currplayer].Contains(num6) && CheckOrbCollision(num6, num3, playerX_px, playerRight_px, playerY_px, playerBottom_px))
					{
						playerProcessedOrbs[currplayer].Add(num6);
						if (!dual)
						{
							orbActivated[num6] = true;
						}
					}
				}
			}
			item = true;
			item2 = num3;
			orbHoldConsumedKeyStillDown[currplayer] = true;
			orbBufferActive[currplayer] = false;
			ballInputBufferCountdown[currplayer] = 0;
			return (activated: true, orbType: item2);
		}
		return (activated: item, orbType: item2);
	}

	private bool IsOrbSprite(int spriteType)
	{
		return spriteType == 6 || spriteType == 11 || spriteType == 40 || spriteType == 31 || spriteType == 41 || spriteType == 68 || spriteType == 5 || spriteType == 39 || spriteType == 122 || spriteType == 123 || spriteType == 124 || spriteType == 89;
	}

	private void ActivateOrb(int orbType, int gamemode, bool gravityInverted, bool mini, ref int velocityY)
	{
		int num = gamemode;
		if (num == 8)
		{
			num = 0;
		}
		if (num == 9)
		{
			num = 7;
		}
		if (num == 10)
		{
			num = 0;
		}
		if (num == 6 || num == 11)
		{
			num = 6;
		}
		if (num < 0 || num >= 8)
		{
			return;
		}
		bool flag = gamemode == 6 || gamemode == 11;
		if (flag && orbType != 5 && orbType != 123 && orbType != 84 && orbType != 85 && orbType != 86 && orbType != 87 && orbType != 39 && orbType != 124)
		{
			return;
		}
		switch (orbType)
		{
		case 5:
		case 84:
		case 85:
		case 86:
		case 87:
		case 123:
			gravityInverted = !gravityInverted;
			gravityFlipped = gravityInverted;
			gravityReversed = gravityInverted;
			currplayer_gravity = (byte)(gravityInverted ? 255u : 0u);
			UpdateEffectiveGravity();
			UpdatePlayerIconFlip();
			if (dual)
			{
				player_gravity[0] ^= byte.MaxValue;
				player_gravity[1] ^= byte.MaxValue;
				int num14 = currplayer ^ 1;
				player_vel_y_fixed[num14] /= 2;
				AppendSimDebug($"[DUAL_CAP_CHECK] Blue orb: flipped both gravs, halved player[{num14}] vel to 0x{player_vel_y_fixed[num14]:X4}");
			}
			if (!flag)
			{
				if (gamemode == 2)
				{
					velocityY = (mini ? (-416) : (-416));
				}
				else
				{
					velocityY = (mini ? (-928) : (-928));
				}
				if (!gravityInverted)
				{
					velocityY = -velocityY;
				}
			}
			break;
		case 122:
			velocityY = 0;
			break;
		case 89:
		{
			bool flag3 = false;
			int num4 = 0;
			int num5 = cameraX_fixed >> 8;
			int num6 = cameraY_fixed >> 8;
			int num7 = num5 + 256;
			int num8 = num6 + 240;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num9 = nonEmptySpriteIndices[i];
				if (sprites[num9] == 90)
				{
					int num10 = num9 % mapWidth;
					int num11 = num9 / mapWidth;
					int num12 = num10 * 16;
					int num13 = num11 * 16;
					if (spriteAnchors != null && spriteAnchors.TryGetValue(num9, out (int, int) value))
					{
						num12 = value.Item1 * 16;
						num13 = value.Item2 * 16;
					}
					if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num9, out (int, int) value2))
					{
						num13 += value2.Item2;
					}
					if (num12 >= num5 && num12 < num7 && num13 >= num6 && num13 < num8)
					{
						num4 = num13;
						flag3 = true;
						AppendSimDebug($"[TELEPORT_ORB] Found visible exit at ({num12}, {num13})");
					}
				}
			}
			if (flag3)
			{
				num4 -= 48;
				playerY_fixed = num4 << 8;
				int val = Math.Max(0, (mapHeight - 15) * 16) << 8;
				cameraY_fixed = Math.Max(0, Math.Min(val, playerY_fixed - 30720));
				velocityY = 0;
				playerVelY_fixed = 0;
				orbed[currplayer] = true;
				AppendSimDebug($"[TELEPORT_ORB] Teleported to Y={num4} (camera Y={cameraY_fixed >> 8})");
			}
			else
			{
				AppendSimDebug("[TELEPORT_ORB] WARNING: No exit orb (0x5A) visible on screen!");
			}
			break;
		}
		case 39:
		case 124:
			gravityInverted = !gravityInverted;
			gravityFlipped = gravityInverted;
			gravityReversed = gravityInverted;
			currplayer_gravity = (byte)(gravityInverted ? 255u : 0u);
			UpdateEffectiveGravity();
			UpdatePlayerIconFlip();
			if (dual)
			{
				player_gravity[0] ^= byte.MaxValue;
				player_gravity[1] ^= byte.MaxValue;
				int num15 = currplayer ^ 1;
				player_vel_y_fixed[num15] /= 2;
				AppendSimDebug($"[DUAL_CAP_CHECK] Green orb: flipped both gravs, halved player[{num15}] vel to 0x{player_vel_y_fixed[num15]:X4}");
			}
			if (!flag)
			{
				int num16 = (mini ? PadOrbHeights_Mini[0][num] : PadOrbHeights[0][num]);
				int num17 = (gravityInverted ? 1 : (-1));
				velocityY = num16 * num17;
			}
			break;
		default:
		{
			int orbPadRow = GetOrbPadRow((byte)orbType);
			if (orbPadRow >= 0 && orbPadRow < 9)
			{
				int num2 = (mini ? PadOrbHeights_Mini[orbPadRow][num] : PadOrbHeights[orbPadRow][num]);
				bool flag2 = orbType == 68;
				int num3 = (gravityInverted ? 1 : (-1));
				velocityY = num2 * num3;
			}
			break;
		}
		}
	}

	private int GetOrbPadRow(byte orbType)
	{
		return orbType switch
		{
			11 => 0, 
			6 => 2, 
			40 => 4, 
			31 => 5, 
			68 => 6, 
			41 => 7, 
			_ => 0, 
		};
	}

	private bool CanBufferOrb(int gamemode)
	{
		return gamemode == 0 || gamemode == 2 || gamemode == 4 || gamemode == 8 || gamemode == 5 || gamemode >= 7;
	}

	private void ResetOrbSystem()
	{
		orbActivated.Clear();
		playerProcessedOrbs[0].Clear();
		playerProcessedOrbs[1].Clear();
		orbBufferActive[currplayer] = false;
		orbHoldConsumedKeyStillDown[currplayer] = false;
		orbHoldSuppressing[currplayer] = false;
		ballInputBufferCountdown[currplayer] = 0;
		try
		{
			int num = 0;
			AppendSimDebug($"[ORB_INIT] Scanning level for orbs (mapWidth={mapWidth}, mapHeight={mapHeight})...");
			for (int i = 0; i < mapHeight; i++)
			{
				for (int j = 0; j < mapWidth; j++)
				{
					int num2 = i * mapWidth + j;
					if (num2 >= 0 && num2 < sprites.Length)
					{
						int num3 = sprites[num2];
						if (num3 != -1 && IsOrbSprite(num3))
						{
							num++;
							AppendSimDebug($"[ORB_INIT] Found orb 0x{num3:X2} at tile ({j},{i}) = world pos ({j * 16},{i * 16})");
						}
					}
				}
			}
			AppendSimDebug($"[ORB_INIT] Total orbs found: {num}");
		}
		catch (Exception ex)
		{
			AppendSimDebug("[ORB_INIT] Error scanning for orbs: " + ex.Message);
		}
	}

	private void CheckDashOrbCollision()
	{
		if (sprites == null || mapWidth <= 0 || mapHeight <= 0)
		{
			return;
		}
		int num = (playerX_fixed >> 8) + 1;
		int num2 = playerY_fixed >> 8;
		int num3 = (miniMode ? 8 : 15);
		int num4 = (miniMode ? 7 : 15);
		num2 += GetMiniSpriteOffsetY();
		int playerLeft_px = num;
		int playerRight_px = num + num3 - 1;
		int playerTop_px = num2;
		int playerBottom_px = num2 + num4 - 1;
		for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
		{
			int num5 = nonEmptySpriteIndices[i];
			int num6 = sprites[num5];
			if (num6 == -1 || (spriteAnchors != null && spriteAnchors.ContainsKey(num5)) || (num6 != 69 && num6 != 70 && num6 != 76 && num6 != 77 && num6 != 80 && num6 != 81 && num6 != 91 && num6 != 92 && num6 != 93 && num6 != 94) || dashing[currplayer] != 0 || !CheckOrbCollision(num5, num6, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px) || (!dual && orbActivated.ContainsKey(num5) && orbActivated[num5]))
			{
				continue;
			}
			bool flag = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0) > 0;
			bool flag2 = IsXDownAsync() || keyXHeld;
			bool flag3 = CanBufferOrb(currentGameMode);
			bool flag4 = false;
			if (flag3)
			{
				if (flag && !orbHoldConsumedKeyStillDown[currplayer] && !orbHoldSuppressing[currplayer])
				{
					flag4 = true;
					orbBufferActive[currplayer] = true;
				}
				else if (flag2 && orbBufferActive[currplayer] && !orbHoldSuppressing[currplayer])
				{
					flag4 = true;
				}
				else if (flag2 && !orbBufferActive[currplayer] && !orbHoldSuppressing[currplayer] && !orbHoldConsumedKeyStillDown[currplayer])
				{
					orbBufferActive[currplayer] = true;
					flag4 = true;
				}
			}
			else if (flag)
			{
				flag4 = true;
			}
			if (flag4)
			{
				ClearSlopeStuff();
				if (!dual)
				{
					orbActivated[num5] = true;
				}
				orbHoldConsumedKeyStillDown[currplayer] = true;
				if (flag)
				{
					Interlocked.Exchange(ref keyXPressedCount, 0);
				}
				if ((num6 == 70 || num6 == 77 || num6 == 81 || num6 == 92 || num6 == 94) && dashing[currplayer] == 0)
				{
					gravityFlipped = !gravityFlipped;
					gravityReversed = !gravityReversed;
					currplayer_gravity = (byte)(gravityReversed ? 255u : 0u);
					UpdateEffectiveGravity();
					AppendSimDebug($"[DASH_ORB] Gravity flipped to {(gravityFlipped ? "UP" : "DOWN")} by orb 0x{num6:X2}");
				}
				if (num6 == 69 || num6 == 70)
				{
					velocityY = 0;
					dashing[currplayer] = 1;
					AppendSimDebug($"[DASH_ORB] Horizontal dash activated (0x{num6:X2})");
				}
				else if (num6 == 76 || num6 == 77)
				{
					velocityY = -playerVelX_fixed;
					dashing[currplayer] = 2;
					AppendSimDebug($"[DASH_ORB] 45deg upward dash activated (0x{num6:X2}), vely={velocityY}");
				}
				else if (num6 == 80 || num6 == 81)
				{
					velocityY = playerVelX_fixed;
					dashing[currplayer] = 3;
					AppendSimDebug($"[DASH_ORB] 45deg downward dash activated (0x{num6:X2}), vely={velocityY}");
				}
				else if (num6 == 91 || num6 == 92)
				{
					velocityY = playerVelX_fixed * 4;
					dashing[currplayer] = 4;
					AppendSimDebug($"[DASH_ORB] Upward dash activated (0x{num6:X2}), vely={velocityY}");
				}
				else if (num6 == 93 || num6 == 94)
				{
					velocityY = -playerVelX_fixed * 4;
					dashing[currplayer] = 5;
					AppendSimDebug($"[DASH_ORB] Downward dash activated (0x{num6:X2}), vely={velocityY}");
				}
				break;
			}
		}
	}

	private bool PF_GetInput()
	{
		bool flag = pfBallHoldContinuation;
		pfBallHoldContinuation = false;
		pfWasPhantomStep = false;
		int num = pfFrameIndex;
		pfRawSequenceInput = pfInputSequence != null && num >= 0 && num < pfInputSequence.Count && pfInputSequence[num];
		if (pfLastAdvancedTick == pfTickGeneration)
		{
			pfWasPhantomStep = true;
			if (pfHoldCounter > 0)
			{
				return true;
			}
			if (pfInputSequence == null)
			{
				return false;
			}
			int num2 = pfFrameIndex - 1;
			return num2 >= 0 && num2 < pfInputSequence.Count && pfInputSequence[num2];
		}
		pfLastAdvancedTick = pfTickGeneration;
		if (pfHoldCounter > 0)
		{
			if (currentGameMode != 2 || ballFlipCooldown <= 0)
			{
				pfHoldCounter--;
				pfFrameIndex++;
				if (currentGameMode == 2)
				{
					pfBallHoldContinuation = true;
				}
				return true;
			}
			pfHoldCounter = 0;
		}
		if (currentGameMode == 2 && flag)
		{
			pfPrevInjectedPress = false;
		}
		if (pfInputSequence == null)
		{
			return false;
		}
		int num3 = pfFrameIndex;
		pfFrameIndex++;
		if (num3 < 0 || num3 >= pfInputSequence.Count)
		{
			return false;
		}
		bool flag2 = pfInputSequence[num3];
		if (flag2)
		{
			bool flag3 = currentGameMode == 1 || currentGameMode == 3;
			bool flag4 = currentGameMode == 0;
			bool flag5 = currentGameMode == 2;
			bool flag6 = currentGameMode == 4;
			bool flag7 = currentGameMode == 5;
			bool flag8 = currentGameMode == 6;
			bool flag9 = currentGameMode == 7;
			bool flag10 = currentGameMode == 8;
			bool flag11 = currentGameMode == 9;
			if (flag5)
			{
				pfHoldCounter = 0;
			}
			else if (!flag3 && !flag4 && !flag6 && !flag7 && !flag8 && !flag9 && !flag10 && !flag11)
			{
				pfHoldCounter = 1;
			}
		}
		return flag2;
	}

	private void PF_InjectInput(bool press)
	{
		Interlocked.Exchange(ref keyXPressedCount, 0);
		Interlocked.Exchange(ref ballToggleRequested, 0);
		if (press)
		{
			if (pfBallHoldContinuation)
			{
				keyXHeld = true;
				prevKeyXDown = true;
				pfPrevInjectedPress = true;
				try
				{
					orbHoldSuppressing[currplayer] = true;
					return;
				}
				catch
				{
					return;
				}
			}
			if (!pfPrevInjectedPress || !CanBufferOrb(currentGameMode) || currentGameMode == 0)
			{
				Interlocked.Exchange(ref keyXPressedCount, 1);
				if (currentGameMode == 2)
				{
					Interlocked.Exchange(ref ballToggleRequested, 1);
				}
			}
			keyXHeld = true;
			prevKeyXDown = true;
			pfPrevInjectedPress = true;
			try
			{
				orbHoldSuppressing[currplayer] = false;
			}
			catch
			{
			}
			try
			{
				orbHoldConsumedKeyStillDown[currplayer] = false;
			}
			catch
			{
			}
			try
			{
				orbBufferActive[currplayer] = true;
				return;
			}
			catch
			{
				return;
			}
		}
		keyXHeld = false;
		prevKeyXDown = false;
		pfPrevInjectedPress = false;
		try
		{
			orbHoldSuppressing[currplayer] = false;
		}
		catch
		{
		}
		try
		{
			orbHoldConsumedKeyStillDown[currplayer] = false;
		}
		catch
		{
		}
	}

	private void PF_LoadPrecomputedInputs()
	{
		pfFrameIndex = 0;
		pfHoldCounter = 0;
		pfBallHoldContinuation = false;
		pfRawSequenceInput = false;
		pfLastAdvancedTick = -1L;
		pfPrevInjectedPress = false;
		pfInputSequence = null;
		try
		{
			if (base.Owner is MainWindow { PrecomputedPathfinderInputs: not null } mainWindow)
			{
				pfInputSequence = new List<bool>(mainWindow.PrecomputedPathfinderInputs);
			}
		}
		catch
		{
		}
	}

	private bool PF_HasInputData()
	{
		return pfInputSequence != null && pfInputSequence.Count > 0;
	}

	private void ApplyGravity(int gravity, int maxFallSpeed)
	{
		int num = (int)((double)gravity * gravityMultiplier);
		if (gravityFlipped ? (velocityY <= maxFallSpeed) : (velocityY >= maxFallSpeed))
		{
			num = -num;
		}
		velocityY += num;
		AppendSimDebug($"[GRAVITY] Applied: accel={num} (base={gravity}, mult={gravityMultiplier:F2}), newVelY={velocityY}, maxFall={maxFallSpeed}");
	}

	private byte GetTileAt(int x, int y)
	{
		try
		{
			if (x < 0 || y < 0 || x >= 256 || y >= 256)
			{
				return 0;
			}
			int num = y / 16;
			int num2 = x / 16;
			int num3 = num * 16 + num2;
			if (num3 >= 0 && num3 < tiles.Length)
			{
				return (byte)SharedPhysics.MapTileForCollision(tiles[num3]);
			}
			return 0;
		}
		catch
		{
			return 0;
		}
	}

	private bool IsSolidTile(byte tile)
	{
		try
		{
			if (tile == 0)
			{
				return false;
			}
			if (MetatileCollisionTable.GetCollision(tile) == MetatileCollision.COL_NONE)
			{
				return false;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void CheckCollisionAndAdjust(ref int posY, ref int velY, int left, int right, int top, int bottom)
	{
		try
		{
			bool flag = false;
			for (int i = left; i <= right; i++)
			{
				if (IsSolidTile(GetTileAt(i, bottom)))
				{
					flag = true;
					break;
				}
			}
			if (flag)
			{
				posY = (bottom - 15) * 256;
				velY = 0;
				AppendSimDebug($"[COLLISION] Hit down at y={bottom}");
			}
			bool flag2 = false;
			for (int j = left; j <= right; j++)
			{
				if (IsSolidTile(GetTileAt(j, top)))
				{
					flag2 = true;
					break;
				}
			}
			if (flag2)
			{
				posY = (top + 1) * 256;
				velY = 0;
				AppendSimDebug($"[COLLISION] Hit up at y={top}");
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[COLLISION] Error: " + ex.Message);
		}
	}

	private void PogoPhysics_Fresh()
	{
	}

	private void RobotPhysics_Fresh()
	{
		bool flag = IsXDownAsync() || keyXHeld;
		int num = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
		bool xPressed = num > 0;
		bool gravityInverted = currplayer_gravity != 0;
		int playerX_px = (playerX_fixed >> 8) + 1;
		int num2 = playerY_fixed >> 8;
		int playerW = ((currplayer_mini != 0) ? 8 : 15);
		int playerH = ((currplayer_mini != 0) ? 7 : 15);
		if (currplayer_mini != 0)
		{
			num2 += 4;
		}
		int scrollX_px = 0;
		int num3 = playerVelY_fixed;
		if (UpdateOrbSystem(4, playerX_px, num2, playerW, playerH, scrollX_px, xPressed, flag, gravityInverted, currplayer_mini != 0, ref num3).activated)
		{
			playerVelY_fixed = num3;
			AppendSimDebug($"[ROBOT] Orb activated! New velY={playerVelY_fixed}");
		}
		if (!flag)
		{
			ClearOrbBuffer();
		}
		int table_idx = ((currplayer_mini != 0) ? 4 : 0);
		int num4 = ((currplayer_gravity == 0) ? 1 : (-1));
		tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(table_idx, CUBE_MAX_FALLSPEED) * num4;
		tmpgravity = GameModePhysics.CUBE_GRAVITY(table_idx) * num4;
		bool flag2 = IsXDownAsync() || keyXHeld;
		int num5 = Interlocked.Exchange(ref keyXPressedCount, 0);
		bool flag3 = num5 > 0;
		if (flag3)
		{
			robotJumpPressed[currplayer] = true;
			AppendSimDebug("[ROBOT] Press detected - flag set");
		}
		AppendSimDebug($"[ROBOT] jumpPressed={robotJumpPressed[currplayer]}, holdJump={flag2}, orbed={orbed[currplayer]}, dashing={dashing[currplayer]}");
		if (robotJumpTime[currplayer] > 0 && !orbed[currplayer] && dashing[currplayer] == 0)
		{
			robotJumpTime[currplayer]--;
			if (flag2)
			{
				playerVelY_fixed = GameModePhysics.ROBOT_JUMP_VEL(table_idx) * num4;
				AppendSimDebug($"[ROBOT] Jump continue: vel={playerVelY_fixed}, time={robotJumpTime[currplayer]}");
			}
			else
			{
				robotJumpTime[currplayer] = 0;
				AppendSimDebug("[ROBOT] Jump cancelled - released button");
			}
		}
		CommonGravityRoutine_Fresh();
		if (currplayer_gravity != 0)
		{
			bool flag4 = currplayer_mini != 0;
			int width = (flag4 ? 8 : 15);
			int height = (flag4 ? 7 : 15);
			int miniCenterOffsetY = SharedPhysics.GetMiniCenterOffsetY(flag4);
			int playerX_px2 = playerX_fixed >> 8;
			int playerY_px = (playerY_fixed >> 8) + miniCenterOffsetY - 1;
			var (flag5, num6) = CheckCollisionUp(playerX_px2, playerY_px, width, height);
			if (flag5 && playerVelY_fixed < 0)
			{
				int num7 = num6 - miniCenterOffsetY - 1;
				playerY_fixed = num7 << 8;
				playerVelY_fixed = 0;
			}
		}
		byte b = currplayer_gravity;
		CubeEject_Fresh();
		if (playerVelY_fixed == 0 && robotJumpPressed[currplayer] && !orbed[currplayer] && dashing[currplayer] == 0)
		{
			robotJumpPressed[currplayer] = false;
			if (flag2 && flag3)
			{
				playerVelY_fixed = GameModePhysics.ROBOT_JUMP_VEL(table_idx) * num4;
				robotJumpTime[currplayer] = 19;
				SlopeJumpCheck_Fresh();
				AppendSimDebug($"[ROBOT] Jump started: vel={playerVelY_fixed}, time={robotJumpTime[currplayer]}");
			}
		}
		UpdateSlopeCounters_Fresh();
		jblocked = false;
		fblocked = false;
		hblocked = false;
		if (pfSimulating)
		{
			return;
		}
		try
		{
			int num8 = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num9 = playerY_fixed >> 8;
			if (currplayer_mini != 0)
			{
				num9 += 4;
			}
			int num10 = num9 + playerVisualHeight / 2;
			if (currplayer == 0)
			{
				recordedPlayerPath.Add((num8, num10));
			}
			else if (dual)
			{
				RecordP2PathPoint(num8, num10);
			}
		}
		catch
		{
		}
	}

	private void ShipPhysics_Fresh()
	{
		bool flag = IsXDownAsync() || keyXHeld;
		int num = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
		bool flag2 = num > 0;
		bool gravityInverted = currplayer_gravity != 0;
		int playerX_px = (playerX_fixed >> 8) + 1;
		int num2 = playerY_fixed >> 8;
		int playerW = ((currplayer_mini != 0) ? 8 : 15);
		int playerH = ((currplayer_mini != 0) ? 7 : 15);
		if (currplayer_mini != 0)
		{
			num2 += 4;
		}
		int scrollX_px = 0;
		int num3 = playerVelY_fixed;
		if (UpdateOrbSystem(1, playerX_px, num2, playerW, playerH, scrollX_px, flag2, flag, gravityInverted, currplayer_mini != 0, ref num3).activated)
		{
			playerVelY_fixed = num3;
			AppendSimDebug($"[SHIP] Orb activated! New velY={playerVelY_fixed}");
			if (flag2)
			{
				Interlocked.Exchange(ref keyXPressedCount, 0);
			}
		}
		if (!flag)
		{
			ClearOrbBuffer();
		}
		int table_idx = ((currplayer_mini != 0) ? 4 : 0);
		bool flag3 = currplayer_gravity != 0;
		int num4 = ((!flag3) ? 1 : (-1));
		bool flag4 = ((currplayer_gravity != 0) ? (playerVelY_fixed < 0) : (playerVelY_fixed > 0));
		bool flag5 = IsXDownAsync() || keyXHeld;
		if (flag5)
		{
			tmpgravity = ((currplayer_mini != 0) ? 49 : 42) * num4;
		}
		else if (!flag5 && !flag4)
		{
			tmpgravity = ((currplayer_mini != 0) ? 59 : 50) * num4;
		}
		else
		{
			tmpgravity = GameModePhysics.SHIP_GRAVITY(table_idx) * num4;
		}
		if (flag5 && flag4)
		{
			tmpgravity = ((currplayer_mini != 0) ? 62 : 52) * num4;
		}
		if (flag3 ^ flag5)
		{
			tmpgravity = -tmpgravity;
		}
		tmpfallspeed = 17475;
		CommonGravityRoutine_Fresh();
		if (currplayer_gravity == 0)
		{
			if (playerVelY_fixed < -1091)
			{
				playerVelY_fixed = -1091;
			}
			if (playerVelY_fixed > 873)
			{
				playerVelY_fixed = 873;
			}
		}
		else
		{
			if (playerVelY_fixed < -873)
			{
				playerVelY_fixed = -873;
			}
			if (playerVelY_fixed > 1091)
			{
				playerVelY_fixed = 1091;
			}
		}
		UfoShipEject_Fresh();
		UpdateSlopeCounters_Fresh();
	}

	private void UfoShipEject_Fresh()
	{
		bool mini = currplayer_mini != 0;
		bool gravFlipped = currplayer_gravity != 0;
		bool inputHeld = IsXDownAsync() || keyXHeld || upHeld;
		int groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		SharedPhysics.EjectResult ejectResult = SharedPhysics.ShipUfoEject(new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve), playerX_fixed, playerY_fixed, playerVelY_fixed, playerVelX_fixed, gravFlipped, mini, currentGameMode, inputHeld, currplayer_was_on_slope_counter, currplayer_slope_frames, currplayer_slope_type, make_cube_jump_higher, currplayer_last_slope_type, cameraY_fixed);
		playerY_fixed = ejectResult.NewY_fixed;
		playerVelY_fixed = ejectResult.NewVelY_fixed;
		currplayer_slope_type = ejectResult.SlopeType;
		currplayer_slope_frames = ejectResult.SlopeFrames;
		currplayer_was_on_slope_counter = ejectResult.SlopeWasOnCounter;
		make_cube_jump_higher = ejectResult.SlopeJumpHigher;
		currplayer_last_slope_type = ejectResult.LastSlopeType;
		if (ejectResult.Died && !MainWindow.Option_NoDeath)
		{
			AppendSimDebug("[DEATH] Floor spike detected (SharedPhysics.ShipUfoEject)");
			deathTriggered = true;
			deathTileX = playerX_fixed >> 8;
			deathTileY = (playerY_fixed >> 8) + SharedPhysics.GetCubeHitboxH(mini);
			paused = true;
			StopMusicAsync();
			try
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					try
					{
						PauseOverlay.Visibility = Visibility.Collapsed;
					}
					catch
					{
					}
					if (base.Owner is MainWindow mainWindow)
					{
						try
						{
							mainWindow.PauseSimulatorPlayback();
						}
						catch
						{
						}
						try
						{
							mainWindow.AddDeathMarker(deathTileX, deathTileY);
						}
						catch
						{
						}
					}
				});
				return;
			}
			catch
			{
				return;
			}
		}
		if (pfSimulating)
		{
			return;
		}
		try
		{
			int num = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num2 = playerY_fixed >> 8;
			if (currplayer_mini != 0)
			{
				num2 += 4;
			}
			int num3 = num2 + playerVisualHeight / 2;
			if (currplayer == 0)
			{
				recordedPlayerPath.Add((num, num3));
			}
			else if (dual)
			{
				RecordP2PathPoint(num, num3);
			}
		}
		catch
		{
		}
	}

	private static BitmapImage? LoadCachedResourceImage(string suffix)
	{
		lock (s_resourceCacheLock)
		{
			if (s_resourceImageCache == null)
			{
				s_resourceImageCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
			}
			if (s_resourceImageCache.TryGetValue(suffix, out BitmapImage value))
			{
				return value;
			}
			if (s_resourceNames == null)
			{
				s_resourceNames = Assembly.GetExecutingAssembly().GetManifestResourceNames();
			}
			string text = Array.Find(s_resourceNames, (string n) => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
			BitmapImage bitmapImage = null;
			if (text != null)
			{
				using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(text);
				if (stream != null)
				{
					BitmapImage bitmapImage2 = new BitmapImage();
					bitmapImage2.BeginInit();
					bitmapImage2.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage2.StreamSource = stream;
					bitmapImage2.EndInit();
					bitmapImage2.Freeze();
					bitmapImage = bitmapImage2;
				}
			}
			s_resourceImageCache[suffix] = bitmapImage;
			return bitmapImage;
		}
	}

	public void SetHideTriggerSprites(bool v)
	{
		try
		{
			hideTriggerSprites = v;
		}
		catch
		{
		}
	}

	public void SetStartingSpeedUiIndex(int idx)
	{
		try
		{
			startingSpeedUiIndex = idx;
			speed = idx;
			try
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					try
					{
						UpdateSpeedDisplay();
					}
					catch
					{
					}
					try
					{
						RenderFrame();
					}
					catch
					{
					}
				});
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void UpdateSimTitle()
	{
		try
		{
			string title = $"{baseWindowTitle} — Sim {simTimeScale * 100.0:F0}%";
			try
			{
				base.Title = title;
			}
			catch
			{
			}
			orbHoldSuppressing[currplayer] = false;
		}
		catch
		{
		}
	}

	private bool IsHiddenTriggerSprite(int s)
	{
		try
		{
			if (s == 221 || s == 237)
			{
				return true;
			}
			if (s == 142 || s == 158)
			{
				return true;
			}
			if (s == 244 || s == 245)
			{
				return true;
			}
			if (s == 111 || s == 127)
			{
				return true;
			}
			if (s == 242 || s == 243)
			{
				return true;
			}
			if (s >= 117 && s <= 120)
			{
				return true;
			}
			if (!hideTriggerSprites)
			{
				return false;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	private void AppendSimDebug(string msg)
	{
		if (pfSimulating || (!simDebugWriteToFile && simDebugBuffer.Count == 0))
		{
			return;
		}
		try
		{
			string text = DateTime.UtcNow.ToString("o") + " " + msg;
			lock (simDebugBuffer)
			{
				simDebugBuffer.Add(text);
				if (simDebugBuffer.Count > 4096)
				{
					int count = simDebugBuffer.Count - 4096;
					simDebugBuffer.RemoveRange(0, count);
				}
			}
			try
			{
				Debug.WriteLine(text);
			}
			catch
			{
			}
			if (!simDebugWriteToFile)
			{
				return;
			}
			lock (simDebugFileLock)
			{
				try
				{
					File.AppendAllText(simDebugLogPath, text + Environment.NewLine);
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	public string[] GetSimDebugSnapshot()
	{
		try
		{
			lock (simDebugBuffer)
			{
				return simDebugBuffer.ToArray();
			}
		}
		catch
		{
			return new string[0];
		}
	}

	public string GetSimDebugLogPath()
	{
		try
		{
			return simDebugLogPath;
		}
		catch
		{
			return string.Empty;
		}
	}

	private void UpdateEffectiveGravity()
	{
		try
		{
			switch (currentGameMode)
			{
			case 1:
				effectiveGravity_fixed = 48;
				effectiveJumpVel_fixed = 0;
				effectiveMaxFall_fixed = 873;
				break;
			case 2:
				effectiveGravity_fixed = 102;
				effectiveJumpVel_fixed = 614;
				effectiveMaxFall_fixed = 1843;
				break;
			case 3:
				effectiveGravity_fixed = 50;
				effectiveJumpVel_fixed = -816;
				effectiveMaxFall_fixed = 800;
				break;
			case 6:
				effectiveGravity_fixed = 0;
				effectiveJumpVel_fixed = 0;
				effectiveMaxFall_fixed = 0;
				break;
			case 9:
				effectiveGravity_fixed = 102;
				effectiveJumpVel_fixed = 614;
				effectiveMaxFall_fixed = 1843;
				break;
			case 10:
				effectiveGravity_fixed = 0;
				effectiveJumpVel_fixed = 0;
				effectiveMaxFall_fixed = 0;
				break;
			case 11:
				effectiveGravity_fixed = 107;
				effectiveJumpVel_fixed = -1424;
				effectiveMaxFall_fixed = CUBE_MAX_FALLSPEED;
				break;
			default:
				effectiveGravity_fixed = 107;
				effectiveJumpVel_fixed = -1424;
				effectiveMaxFall_fixed = CUBE_MAX_FALLSPEED;
				break;
			}
			if (gravityReversed || effectiveInvertedByW)
			{
				effectiveGravity_fixed = -effectiveGravity_fixed;
				effectiveJumpVel_fixed = -effectiveJumpVel_fixed;
				effectiveMaxFall_fixed = -effectiveMaxFall_fixed;
			}
			try
			{
				Debug.WriteLine($"UpdateEffectiveGravity: gravityReversed={gravityReversed} effectiveGravity={effectiveGravity_fixed} effectiveJump={effectiveJumpVel_fixed} effectiveMaxFall={effectiveMaxFall_fixed}");
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void UpdatePlayerSpeed()
	{
		try
		{
			int[] array = new int[5] { 571, 708, 881, 1065, 1310 };
			if (speed >= 0 && speed < array.Length)
			{
				playerVelX_fixed = array[speed];
			}
		}
		catch
		{
		}
	}

	private void UpdateCubeRotation()
	{
		try
		{
			int num = (cubeRotate_fixed >> 8) & 0xFF;
			int num2 = cubeRotate_fixed & 0xFF;
			if ((currplayer_slope_frames > 0 || currplayer_slope_type != 0) && currplayer_slope_type > 0 && currplayer_slope_type < slopeRotationFrame_24.Length)
			{
				int num3 = slopeRotationFrame_24[currplayer_slope_type];
				cubeRotate_fixed = num3 << 8;
				AppendSimDebug($"[CUBE_ROT] Slope rotation: slope_type=0x{currplayer_slope_type:X2} -> frame {num3}");
				return;
			}
			if (playerVelY_fixed == 0)
			{
				int num4 = num;
				if (num4 >= 12)
				{
					num4 -= 12;
				}
				if (num4 < 0)
				{
					num4 = 0;
				}
				if (num4 > 12)
				{
					num4 = 12;
				}
				int num5 = num + drawcube_rounding_table[num4];
				if (num5 >= 24)
				{
					num5 -= 24;
				}
				if (num5 < 0)
				{
					num5 += 24;
				}
				cubeRotate_fixed = num5 << 8;
				return;
			}
			int num6 = GameModePhysics.CUBE_GRAVITY(currplayer_table_idx);
			if (gravityFlipped)
			{
				num6 = -num6;
			}
			num2 += num6;
			AppendSimDebug($"[CUBE_ROT] gravityFlipped={gravityFlipped} increment={num6:X2} subFrame={num2} frameIndex={num}");
			if (num2 >= 256)
			{
				num++;
				num2 -= 256;
				if (num >= 24)
				{
					num = 0;
				}
			}
			else if (num2 < 0)
			{
				num--;
				num2 += 256;
				if (num < 0)
				{
					num = 23;
				}
			}
			cubeRotate_fixed = (num << 8) | num2;
		}
		catch
		{
		}
	}

	private void UpdateShipRotation()
	{
		try
		{
			int num = playerVelY_fixed;
			if (num > -128 && num < 128)
			{
				num = 256;
			}
			int num2 = 1024 - num;
			int num3 = (num2 >> 8) & 0xFF;
			if (num3 >= 8)
			{
				num2 = ((num3 < 128) ? 2047 : 0);
			}
			shipRotate_fixed = num2;
			int value = (shipRotate_fixed >> 8) & 0xFF;
			AppendSimDebug($"[SHIP_ROT] velY=0x{playerVelY_fixed:X4}, rotate=0x{num2:X4}, frame={value}");
		}
		catch
		{
		}
	}

	private void UpdateSwingcopterRotation()
	{
		try
		{
			int num = playerVelY_fixed;
			if (num > -128 && num < 128)
			{
				num = 256;
			}
			int num2 = 1024 - num;
			int num3 = (num2 >> 8) & 0xFF;
			if (num3 >= 8)
			{
				num2 = ((num3 < 128) ? 2047 : 0);
			}
			swingcopterRotate_fixed = num2;
			int value = (swingcopterRotate_fixed >> 8) & 0xFF;
			AppendSimDebug($"[SWING_ROT] velY=0x{playerVelY_fixed:X4}, rotate=0x{num2:X4}, frame={value}");
		}
		catch
		{
		}
	}

	private void UpdateFootballRotation()
	{
		try
		{
			if ((currplayer_slope_frames > 0 || currplayer_slope_type != 0) && currplayer_slope_type > 0 && currplayer_slope_type < slopeRotationFrame_24.Length)
			{
				int num = slopeRotationFrame_24[currplayer_slope_type];
				footballRotate_fixed = num << 8;
				AppendSimDebug($"[FOOTBALL_ROT] SLOPE: slope_type=0x{currplayer_slope_type:X2} -> frame {num}");
				return;
			}
			if (playerVelY_fixed == 0)
			{
				if (footballChargeFrames > 0)
				{
					int num2 = ((footballChargeFrames < 10) ? 23 : ((footballChargeFrames < 20) ? 22 : ((footballChargeFrames < 30) ? 21 : ((footballChargeFrames < 38) ? 20 : ((footballChargeFrames >= 50) ? 6 : 20)))));
					footballRotate_fixed = num2 << 8;
					AppendSimDebug($"[FOOTBALL_ROT] CHARGE: chargeFrames={footballChargeFrames} -> frame={num2}");
				}
				else
				{
					footballRotate_fixed = 0;
				}
				return;
			}
			int num3 = (footballRotate_fixed >> 8) & 0xFF;
			int num4 = footballRotate_fixed & 0xFF;
			int num5 = GameModePhysics.CUBE_GRAVITY(currplayer_table_idx);
			if (gravityFlipped)
			{
				num5 = -num5;
			}
			num4 += num5;
			if (num4 >= 256)
			{
				num3++;
				num4 -= 256;
				if (num3 >= 24)
				{
					num3 = 0;
				}
			}
			else if (num4 < 0)
			{
				num3--;
				num4 += 256;
				if (num3 < 0)
				{
					num3 = 23;
				}
			}
			footballRotate_fixed = (num3 << 8) | (num4 & 0xFF);
			AppendSimDebug($"[FOOTBALL_ROT] AIR: frame={num3} sub={num4 & 0xFF} (grav=0x{num5:X2})");
		}
		catch
		{
		}
	}

	private int GetCubeSpriteFrame()
	{
		try
		{
			int num = (cubeRotate_fixed >> 8) & 0xFF;
			if (num < 0 || num >= 24)
			{
				num = 0;
			}
			return drawcube_sprite_table[num];
		}
		catch
		{
			return 0;
		}
	}

	private void UpdateCubeRotationMini()
	{
		try
		{
			int num = (cubeRotateMini_fixed >> 8) & 0xFF;
			int num2 = cubeRotateMini_fixed & 0xFF;
			if ((currplayer_slope_frames > 0 || currplayer_slope_type != 0) && currplayer_slope_type > 0 && currplayer_slope_type < slopeRotationFrame_24.Length)
			{
				int num3 = slopeRotationFrame_24[currplayer_slope_type];
				cubeRotateMini_fixed = num3 << 8;
				return;
			}
			if (playerVelY_fixed == 0)
			{
				int num4 = num;
				if (num4 >= 12)
				{
					num4 -= 12;
				}
				if (num4 < 0)
				{
					num4 = 0;
				}
				if (num4 > 12)
				{
					num4 = 12;
				}
				int num5 = num + drawcube_rounding_table[num4];
				if (num5 >= 24)
				{
					num5 -= 24;
				}
				if (num5 < 0)
				{
					num5 += 24;
				}
				cubeRotateMini_fixed = num5 << 8;
				return;
			}
			int num6 = GameModePhysics.CUBE_GRAVITY(currplayer_table_idx);
			if (gravityFlipped)
			{
				num6 = -num6;
			}
			num2 += num6;
			if (num2 >= 256)
			{
				num++;
				num2 -= 256;
				if (num >= 24)
				{
					num = 0;
				}
			}
			else if (num2 < 0)
			{
				num--;
				num2 += 256;
				if (num < 0)
				{
					num = 23;
				}
			}
			cubeRotateMini_fixed = (num << 8) | num2;
		}
		catch
		{
		}
	}

	private int GetCubeSpriteMiniFrame()
	{
		try
		{
			int num = (cubeRotateMini_fixed >> 8) & 0xFF;
			if (num < 0 || num >= 24)
			{
				num = 0;
			}
			int num2 = drawcube_sprite_table[num] & 7;
			if (num2 == 0)
			{
				return 0;
			}
			if (num2 <= 2)
			{
				return 1;
			}
			if (num2 <= 4)
			{
				return 2;
			}
			return 3;
		}
		catch
		{
			return 0;
		}
	}

	private int GetShipSpriteFrame()
	{
		try
		{
			int num = (shipRotate_fixed >> 8) & 0xFF;
			if (num > 7)
			{
				num = 7;
			}
			if (num < 0)
			{
				num = 0;
			}
			if (currplayer_gravity != 0)
			{
				num = 7 - num;
			}
			return num;
		}
		catch
		{
			return 0;
		}
	}

	private int GetSwingcopterSpriteFrame()
	{
		try
		{
			int num = (swingcopterRotate_fixed >> 8) & 0xFF;
			if (num > 7)
			{
				num = 7;
			}
			if (num < 0)
			{
				num = 0;
			}
			if (currplayer_gravity != 0)
			{
				num = 7 - num;
			}
			return num;
		}
		catch
		{
			return 0;
		}
	}

	private int GetFootballSpriteFrameAndFlip()
	{
		try
		{
			int num = (footballRotate_fixed >> 8) & 0xFF;
			if (num > 23)
			{
				num = 23;
			}
			if (num < 0)
			{
				num = 0;
			}
			return s_footballFlipTable[num];
		}
		catch
		{
			return 0;
		}
	}

	private int ResolveSimulatorTileIndex(int idx)
	{
		try
		{
			if (idx >= 1000 || idx < 0)
			{
				return idx;
			}
			switch (idx)
			{
			case 217:
			case 218:
				return 17;
			case 219:
			case 220:
				return 27;
			case 143:
				return 47;
			case 223:
			case 227:
			case 252:
			case 254:
			case 255:
				return 0;
			default:
				return idx;
			}
		}
		catch
		{
			return idx;
		}
	}

	private bool SpriteIntersectsPlayerTouching(int idx, int sid, int playerLeft_px, int playerRight_px, int playerTop_px, int playerBottom_px)
	{
		try
		{
			int num = idx % mapWidth;
			int num2 = idx / mapWidth;
			int num3 = sid & 0xFF;
			int sid2 = num3;
			int num4 = -1;
			bool flag = !SharedPhysics.IsSpeedPortal(num3) && !SharedPhysics.IsGameModePortal(num3) && !SharedPhysics.IsGravityPortal(num3) && !SharedPhysics.IsMiniGrowthPortal(num3);
			if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out (int, int) value))
			{
				num4 = value.Item2 * mapWidth + value.Item1;
				if (flag && num4 >= 0 && num4 < sprites.Length)
				{
					int num5 = sprites[num4];
					if (num5 >= 0 && num5 < 256)
					{
						sid2 = num5 & 0xFF;
					}
				}
			}
			sid2 = SharedPhysics.NormalizePortalGeometrySid(sid2);
			int num6 = ((sid2 >= 0 && sid2 < sprite_widths.Length) ? sprite_widths[sid2] : 16);
			int num7 = ((sid2 >= 0 && sid2 < sprite_heights.Length) ? sprite_heights[sid2] : 16);
			if (num7 >= 252)
			{
				return false;
			}
			int num8 = ((sid2 >= 0 && sid2 < sprite_x_offset.Length) ? sprite_x_offset[sid2] : 0);
			int num9 = ((sid2 >= 0 && sid2 < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[sid2] : 0);
			int num10 = 0;
			int num11 = 0;
			(int, int) value3;
			if (num4 >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num4, out (int, int) value2))
			{
				(num10, num11) = value2;
			}
			else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out value3))
			{
				(num10, num11) = value3;
			}
			int num12 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			int num13 = num * 16 + num8 + num10;
			int num14 = (num2 - num12) * 16 + num9 + num11 - 1;
			int num15 = num13 + Math.Max(1, num6);
			int num16 = num14 + Math.Max(1, num7);
			try
			{
				if (num6 == 16 && num7 == 16)
				{
					BitmapSource bitmapSource = null;
					int num17 = sid2 & 0xFF;
					int num18 = sid & 0xFF;
					ImageSource value5;
					if (previewSpriteMap != null && previewSpriteMap.TryGetValue(num17, out ImageSource value4) && value4 is BitmapSource bitmapSource2)
					{
						bitmapSource = bitmapSource2;
					}
					else if (previewSpriteMap != null && previewSpriteMap.TryGetValue(num18, out value5) && value5 is BitmapSource bitmapSource3)
					{
						bitmapSource = bitmapSource3;
					}
					else if (spriteImages != null && num17 >= 0 && num17 < spriteImages.Length && spriteImages[num17] is BitmapSource bitmapSource4)
					{
						bitmapSource = bitmapSource4;
					}
					else if (spriteImages != null && num18 >= 0 && num18 < spriteImages.Length && spriteImages[num18] is BitmapSource bitmapSource5)
					{
						bitmapSource = bitmapSource5;
					}
					if (bitmapSource != null)
					{
						num6 = Math.Max(1, bitmapSource.PixelWidth);
						num7 = Math.Max(1, bitmapSource.PixelHeight);
						num15 = num13 + num6;
						num16 = num14 + num7;
					}
				}
			}
			catch
			{
			}
			return playerRight_px + 2 >= num13 && num15 + 1 >= playerLeft_px && playerBottom_px + 2 >= num14 && num16 + 1 >= playerTop_px;
		}
		catch
		{
			return false;
		}
	}

	private bool SpriteIntersectsPlayer(int idx, int sid, int playerLeft_px, int playerRight_px, int playerTop_px, int playerBottom_px, bool ignoreSentinels = false)
	{
		try
		{
			int num = idx % mapWidth;
			int num2 = idx / mapWidth;
			int num3 = sid & 0xFF;
			int sid2 = num3;
			int num4 = -1;
			bool flag = !SharedPhysics.IsSpeedPortal(num3) && !SharedPhysics.IsGameModePortal(num3) && !SharedPhysics.IsGravityPortal(num3) && !SharedPhysics.IsMiniGrowthPortal(num3);
			if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out (int, int) value))
			{
				num4 = value.Item2 * mapWidth + value.Item1;
				if (flag && num4 >= 0 && num4 < sprites.Length)
				{
					int num5 = sprites[num4];
					if (num5 >= 0 && num5 < 256)
					{
						sid2 = num5 & 0xFF;
					}
				}
			}
			sid2 = SharedPhysics.NormalizePortalGeometrySid(sid2);
			int val = ((sid2 >= 0 && sid2 < sprite_widths.Length) ? sprite_widths[sid2] : 16);
			int num6 = ((sid2 >= 0 && sid2 < sprite_heights.Length) ? sprite_heights[sid2] : 16);
			if (!ignoreSentinels && num6 >= 252)
			{
				return false;
			}
			int num7 = ((sid2 >= 0 && sid2 < sprite_x_offset.Length) ? sprite_x_offset[sid2] : 0);
			int num8 = ((sid2 >= 0 && sid2 < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[sid2] : 0);
			int num9 = 0;
			int num10 = 0;
			(int, int) value3;
			if (num4 >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num4, out (int, int) value2))
			{
				(num9, num10) = value2;
			}
			else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out value3))
			{
				(num9, num10) = value3;
			}
			int num11 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			int num12 = num * 16 + num7 + num9;
			int num13 = (num2 - num11) * 16 + num8 + num10 - 1;
			int num14 = num12 + Math.Max(1, val);
			int num15 = num13 + Math.Max(1, num6);
			return playerRight_px + 1 >= num12 && num14 >= playerLeft_px && playerBottom_px + 1 >= num13 && num15 >= playerTop_px;
		}
		catch
		{
			return false;
		}
	}

	private void RecordP2PathPoint(int x, int y)
	{
		if (!_prevDualActiveForP2Path && recordedPlayer2Path.Count > 0)
		{
			recordedPlayer2Path.Add((-1, -1));
		}
		recordedPlayer2Path.Add((x, y));
		_prevDualActiveForP2Path = true;
	}

	public void SetSpawnScrollConfig(int? spawnHi, int? spawnLo, int? scrollHi, int? scrollLo)
	{
		configSpawnYHi = spawnHi;
		configSpawnYLo = spawnLo;
		configScrollYHi = scrollHi;
		configScrollYLo = scrollLo;
	}

	private int? ComputeSpawnYFixed()
	{
		if (!configSpawnYHi.HasValue)
		{
			return null;
		}
		int num = configSpawnYHi.Value & 0xFF;
		int num2 = (configSpawnYLo.HasValue ? configSpawnYLo.Value : 0) & 0xFF;
		int num3 = (num << 8) | num2;
		int num4 = (configScrollYHi ?? 2) & 0xFF;
		int num5 = (configScrollYLo ?? 239) & 0xFF;
		int num6 = num4 * 240 + num5;
		int num7 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		int num8 = (57 - mapHeight + num7) * 16;
		return num3 + (num6 - num8 << 8);
	}

	private int? ComputeScrollYFixed()
	{
		if (!configScrollYHi.HasValue)
		{
			return null;
		}
		int num = configScrollYHi.Value & 0xFF;
		int num2 = (configScrollYLo.HasValue ? configScrollYLo.Value : 0) & 0xFF;
		int num3 = num * 240 + num2;
		int num4 = 719;
		int num5 = num4 - num3;
		int num6 = (mapHeight - 15) * 16;
		int val = Math.Max(0, num6 - num5);
		return Math.Min(num6, val) << 8;
	}

	private static bool ProvidesFloorAtColumnStatic(MetatileCollision col, int localX, out int topOffsetPx)
	{
		topOffsetPx = int.MaxValue;
		bool flag = localX >= 0 && localX <= 7;
		bool flag2 = localX >= 8 && localX <= 15;
		switch (col)
		{
		case MetatileCollision.COL_ALL:
		case MetatileCollision.COL_FLOOR_CEIL:
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

	private static bool BlocksCeilingAtColumn(MetatileCollision col, int localX)
	{
		switch (col)
		{
		case MetatileCollision.COL_NONE:
			return false;
		case MetatileCollision.COL_TOP:
			return false;
		default:
			if (SharedPhysics.IsDeathCollision(col))
			{
				return false;
			}
			return true;
		}
	}

	private static bool TileOccupiesPixel(MetatileCollision col, int localX, int localY)
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
		if (ProvidesFloorAtColumnStatic(col, localX, out var topOffsetPx))
		{
			if (localY >= topOffsetPx)
			{
				return true;
			}
			return false;
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

	private static bool IsSlopeTile(MetatileCollision col)
	{
		return SharedPhysics.IsSlopeTile(col);
	}

	private static int GetSlopeFloorAtX(MetatileCollision col, int localX)
	{
		localX = Math.Max(0, Math.Min(15, localX));
		return col switch
		{
			MetatileCollision.COL_SLOPE_RD45 => localX, 
			MetatileCollision.COL_SLOPE_LD45 => 15 - localX, 
			MetatileCollision.COL_SLOPE_RU45 => 15 - localX, 
			MetatileCollision.COL_SLOPE_LU45 => localX, 
			MetatileCollision.COL_SLOPE_RD22_RIGHT => localX / 2, 
			MetatileCollision.COL_SLOPE_RD22_LEFT => 8 + localX / 2, 
			MetatileCollision.COL_SLOPE_LD22_RIGHT => (15 - localX) / 2, 
			MetatileCollision.COL_SLOPE_LD22_LEFT => 8 + (15 - localX) / 2, 
			MetatileCollision.COL_SLOPE_RU22_RIGHT => 15 - localX / 2, 
			MetatileCollision.COL_SLOPE_RU22_LEFT => 7 - localX / 2, 
			MetatileCollision.COL_SLOPE_LU22_RIGHT => 15 - (15 - localX) / 2, 
			MetatileCollision.COL_SLOPE_LU22_LEFT => 7 - (15 - localX) / 2, 
			MetatileCollision.COL_SLOPE_RD66_TOP => Math.Min(15, localX * 2), 
			MetatileCollision.COL_SLOPE_RD66_BOT => Math.Max(0, localX * 2 - 16), 
			MetatileCollision.COL_SLOPE_LD66_TOP => Math.Min(15, (15 - localX) * 2), 
			MetatileCollision.COL_SLOPE_LD66_BOT => Math.Max(0, (15 - localX) * 2 - 16), 
			MetatileCollision.COL_SLOPE_RU66_TOP => Math.Max(0, 15 - localX * 2), 
			MetatileCollision.COL_SLOPE_RU66_BOT => Math.Min(15, 31 - localX * 2), 
			MetatileCollision.COL_SLOPE_LU66_TOP => Math.Max(0, 15 - (15 - localX) * 2), 
			MetatileCollision.COL_SLOPE_LU66_BOT => Math.Min(15, 31 - (15 - localX) * 2), 
			_ => 0, 
		};
	}

	private static bool IsSlopeSolidAtPixel(MetatileCollision col, int localX, int localY)
	{
		int num;
		int num2;
		switch (col)
		{
		case MetatileCollision.COL_SLOPE_RD45:
			num = (localX & 0xF) ^ 0xF;
			num2 = localY & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LD45:
			num = localX & 0xF;
			num2 = localY & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RU45:
			num = (localX & 0xF) ^ 0xF;
			num2 = (localY & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LU45:
			num = localX & 0xF;
			num2 = (localY & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RD22_RIGHT:
			num = ((localX >> 1) & 7) ^ 0xF;
			num2 = localY & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RD22_LEFT:
			num = (((localX >> 1) | 8) & 0xF) ^ 0xF;
			num2 = localY & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LD22_RIGHT:
			num = (localX >> 1) & 7;
			num2 = localY & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LD22_LEFT:
			num = ((localX >> 1) | 8) & 0xF;
			num2 = localY & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RU22_RIGHT:
			num = ((localX >> 1) & 7) ^ 0xF;
			num2 = (localY & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RU22_LEFT:
			num = (((localX >> 1) | 8) & 0xF) ^ 0xF;
			num2 = (localY & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LU22_RIGHT:
			num = (localX >> 1) & 7;
			num2 = (localY & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LU22_LEFT:
			num = ((localX >> 1) | 8) & 0xF;
			num2 = (localY & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RD66_TOP:
			if ((localX & 0xF) < 8)
			{
				return false;
			}
			num = (((localX & 7) << 1) & 0xF) ^ 0xF;
			num2 = localY & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RD66_BOT:
			if ((localX & 0xF) >= 8)
			{
				return true;
			}
			num = (((localX & 0xF) << 1) & 0xF) ^ 0xF;
			num2 = localY & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LD66_TOP:
			if ((localX & 0xF) >= 8)
			{
				return false;
			}
			num = ((localX & 7) << 1) & 0xF;
			num2 = localY & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LD66_BOT:
			if ((localX & 0xF) < 8)
			{
				return true;
			}
			num = ((localX & 0xF) << 1) & 0xF;
			num2 = localY & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RU66_TOP:
			if ((localX & 0xF) < 8)
			{
				return false;
			}
			num = (((localX & 7) << 1) & 0xF) ^ 0xF;
			num2 = (localY & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RU66_BOT:
			if ((localX & 0xF) >= 8)
			{
				return true;
			}
			num = (((localX & 0xF) << 1) & 0xF) ^ 0xF;
			num2 = (localY & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LU66_TOP:
			if ((localX & 0xF) >= 8)
			{
				return false;
			}
			num = ((localX & 7) << 1) & 0xF;
			num2 = (localY & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LU66_BOT:
			if ((localX & 0xF) < 8)
			{
				return true;
			}
			num = ((localX & 0xF) << 1) & 0xF;
			num2 = (localY & 0xF) ^ 0xF;
			break;
		default:
			return false;
		}
		return (byte)num2 >= (byte)num;
	}

	private bool CheckPixelCollision(int worldX_px, int worldY_px, int groundRowsToReserve)
	{
		int num = worldX_px / 16;
		int num2 = worldY_px / 16;
		int num3 = num2 + groundRowsToReserve;
		if (num < 0 || num >= mapWidth)
		{
			return false;
		}
		if (num3 < 0)
		{
			return true;
		}
		if (num3 >= mapHeight)
		{
			return true;
		}
		int num4 = tiles[num3 * mapWidth + num];
		int num5 = MapAnimatedTileIndex(num4);
		int num6 = num5;
		if (num5 >= 1000)
		{
			num6 = ((num5 >= 1000 && num5 <= 1007) ? (8 + (num5 - 1000) % 4) : ((num5 >= 1010 && num5 <= 1015) ? (((num5 - 1010) % 3) switch
			{
				1 => 125, 
				0 => 4, 
				_ => 127, 
			}) : ((num5 < 1020 || num5 > 1037) ? num4 : (116 + (num5 - 1020) % 9))));
		}
		MetatileCollision col = MetatileCollisionTable.GetCollision((byte)num6);
		int num7 = num * 16;
		int localX = Math.Max(0, Math.Min(15, worldX_px - num7));
		int num8 = num2 * 16;
		int localY = Math.Max(0, Math.Min(15, worldY_px - num8));
		return TileOccupiesPixel(col, localX, localY);
	}

	private bool PointHitsSpikeFloor(int px, int py, int groundRowsReserve)
	{
		int num = px / 16;
		int num2 = py / 16;
		int num3 = num2 + groundRowsReserve;
		if (num < 0 || num >= mapWidth || num3 < 0 || num3 >= mapHeight)
		{
			return false;
		}
		int num4 = num3 * mapWidth + num;
		if (num4 < 0 || num4 >= tiles.Length)
		{
			return false;
		}
		int num5 = tiles[num4];
		int num6 = MapAnimatedTileIndex(num5);
		int num7 = num6;
		if (num6 >= 1000)
		{
			num7 = ((num6 >= 1000 && num6 <= 1007) ? (8 + (num6 - 1000) % 4) : ((num6 >= 1010 && num6 <= 1015) ? (((num6 - 1010) % 3) switch
			{
				1 => 125, 
				0 => 4, 
				_ => 127, 
			}) : ((num6 < 1020 || num6 > 1037) ? num5 : (116 + (num6 - 1020) % 9))));
		}
		MetatileCollision metatileCollision = MetatileCollisionTable.GetCollision((byte)num7);
		if (metatileCollision == MetatileCollision.COL_NONE)
		{
			return false;
		}
		int num8 = num * 16;
		int localX = Math.Max(0, Math.Min(15, px - num8));
		int num9 = num2 * 16;
		int localY = Math.Max(0, Math.Min(15, py - num9));
		return MetatileCollisionTable.TileKillsAtPixel(metatileCollision, localX, localY);
	}

	private bool CheckFloorSpikes(int playerX_px, int playerY_px, out int deathX, out int deathY)
	{
		bool flag = currplayer_mini != 0;
		int hbW = (flag ? 8 : 15);
		int hbH = (flag ? 7 : 15);
		int groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		bool flag2 = SharedPhysics.CheckFloorSpikes(new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve), playerX_px, playerY_px, hbW, hbH, flag, out deathX, out deathY);
		if (flag2)
		{
			AppendSimDebug($"[FLOOR_SPIKE] Death at ({deathX},{deathY})");
		}
		return flag2;
	}

	private bool CheckDeathCollision(out int deathX_px, out int deathY_px)
	{
		deathX_px = 0;
		deathY_px = 0;
		if (MainWindow.Option_NoDeath)
		{
			return false;
		}
		if (deathTriggered)
		{
			return false;
		}
		int num = playerX_fixed >> 8;
		int num2 = playerY_fixed >> 8;
		bool flag = currplayer_mini != 0;
		int num3;
		int num4;
		if (currentGameMode == 6 || currentGameMode == 7)
		{
			num3 = 8;
			num4 = 8;
		}
		else
		{
			num3 = (flag ? 8 : 15);
			num4 = (flag ? 7 : 15);
		}
		if (miniMode && currentGameMode != 6 && currentGameMode != 7)
		{
			num2 += 16 - num4 >> 1;
		}
		int num5 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		int num6 = num + (num3 >> 1) - 1;
		int num7 = num2 + num4 / 2;
		int num8 = num6 / 16;
		int num9 = num7 / 16;
		int num10 = num9 + num5;
		if (num10 < 0 || num10 >= mapHeight || num8 < 0 || num8 >= mapWidth)
		{
			return false;
		}
		int num11 = tiles[num10 * mapWidth + num8];
		int num12 = MapAnimatedTileIndex(num11);
		int num13 = num12;
		try
		{
			AppendSimDebug($"[DEATH_CHECK] Point ({num6},{num7}) -> Tile({num8},{num9}) TID={num11}");
		}
		catch
		{
		}
		if (num12 >= 1000)
		{
			num13 = ((num12 >= 1000 && num12 <= 1007) ? (8 + (num12 - 1000) % 4) : ((num12 >= 1010 && num12 <= 1015) ? (((num12 - 1010) % 3) switch
			{
				1 => 125, 
				0 => 4, 
				_ => 127, 
			}) : ((num12 < 1020 || num12 > 1037) ? num11 : (116 + (num12 - 1020) % 9))));
		}
		MetatileCollision metatileCollision = MetatileCollisionTable.GetCollision((byte)num13);
		int num14 = num8 * 16;
		int localX = Math.Max(0, Math.Min(15, num6 - num14));
		int num15 = num9 * 16;
		int localY = Math.Max(0, Math.Min(15, num7 - num15));
		if (MetatileCollisionTable.TileKillsAtPixel(metatileCollision, localX, localY))
		{
			deathX_px = num6;
			deathY_px = num7;
			return true;
		}
		MetatileCollision metatileCollision2 = metatileCollision;
		MetatileCollision metatileCollision3 = metatileCollision2;
		if (((uint)(metatileCollision3 - 1) <= 9u || (uint)(metatileCollision3 - 32) <= 1u || (uint)(metatileCollision3 - 37) <= 6u) && SharedPhysics.TileOccupiesPixel(metatileCollision, localX, localY))
		{
			deathX_px = num6;
			deathY_px = num7;
			return true;
		}
		return false;
	}

	private async Task StopMusicAsync()
	{
		try
		{
			Window owner = base.Owner;
			if (owner is MainWindow mw)
			{
				AppendSimDebug("[MUSIC] Stopping music");
				await mw.StopSimulatorPlaybackAsync();
			}
		}
		catch
		{
		}
	}

	private void CheckGravityPortals()
	{
		try
		{
			int num = (playerX_fixed >> 8) + 1;
			int num2 = playerY_fixed >> 8;
			int num3 = (miniMode ? 8 : 15);
			int num4 = (miniMode ? 7 : 15);
			num2 += GetMiniSpriteOffsetY();
			int num5 = num;
			int num6 = num + num3 - 1;
			int num7 = num2;
			int num8 = num2 + num4 - 1;
			AppendSimDebug($"[GRAV_PORTAL_CHECK] mini={miniMode} grav={gravityFlipped} playerBox=({num5},{num7})-({num6},{num8}) size={num3}x{num4}");
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num9 = nonEmptySpriteIndices[i];
				int num10 = sprites[num9];
				if (num10 < 0)
				{
					continue;
				}
				bool flag = num10 == 8 || num10 == 16 || num10 == 17 || num10 == 252;
				bool flag2 = num10 == 9 || num10 == 18 || num10 == 19 || num10 == 251;
				if ((!flag && !flag2) || processedGravityPortals.Contains(num9) || !SpriteIntersectsPlayer(num9, num10, num5, num6, num7, num8))
				{
					continue;
				}
				bool flag3 = false;
				if (flag2 && !gravityReversed)
				{
					gravityReversed = true;
					gravityFlipped = true;
					currplayer_gravity = byte.MaxValue;
					wasZeroedByCollisionLastFrame = false;
					flag3 = true;
					AppendSimDebug($"[GRAV_PORTAL] REVERSE ACTIVATED: mini={miniMode}/{currplayer_mini} grav={gravityFlipped}/{currplayer_gravity:X2} reversed={gravityReversed}");
				}
				else if (flag && gravityReversed)
				{
					gravityReversed = false;
					gravityFlipped = false;
					currplayer_gravity = 0;
					wasZeroedByCollisionLastFrame = false;
					flag3 = true;
					AppendSimDebug($"[GRAV_PORTAL] NORMAL ACTIVATED: mini={miniMode}/{currplayer_mini} grav={gravityFlipped}/{currplayer_gravity:X2} reversed={gravityReversed}");
				}
				if (!flag3)
				{
					continue;
				}
				try
				{
					base.Dispatcher?.BeginInvoke((Action)delegate
					{
						UpdatePlayerIconFlip();
						InvertedCheckBox.IsChecked = gravityReversed;
					});
				}
				catch
				{
				}
				playerVelY_fixed >>= 1;
				AppendSimDebug($"[GRAV_PORTAL] POST-ACTIVATION: currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed}");
				processedGravityPortals.Add(num9);
				break;
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[GRAVITY PORTAL] Error: " + ex.Message);
		}
	}

	private void CheckGameModePortals()
	{
		try
		{
			int num = (playerX_fixed >> 8) + 1;
			int num2 = playerY_fixed >> 8;
			bool flag = currentGameMode == 6 || currentGameMode == 10;
			int num3 = (flag ? 8 : (miniMode ? 8 : 15));
			int num4 = (flag ? 8 : (miniMode ? 7 : 15));
			num2 = ((!flag) ? (num2 + GetMiniSpriteOffsetY()) : (num2 + 4));
			int playerLeft_px = num;
			int playerRight_px = num + num3 - 1;
			int playerTop_px = num2;
			int playerBottom_px = num2 + num4 - 1;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num5 = nonEmptySpriteIndices[i];
				int num6 = sprites[num5];
				if (num6 < 0)
				{
					continue;
				}
				if (num6 == 0 || num6 == 1 || num6 == 2 || num6 == 3 || num6 == 4 || num6 == 23 || num6 == 36 || num6 == 75 || num6 == 88 || num6 == 106 || num6 == 107 || num6 == 108)
				{
					if (processedGameModePortals.Contains(num5) || !SpriteIntersectsPlayer(num5, num6, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
					{
						continue;
					}
					processedGameModePortals.Add(num5);
					int num7 = currentGameMode;
					if (1 == 0)
					{
					}
					int num8 = num6 switch
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
						_ => currentGameMode, 
					};
					if (1 == 0)
					{
					}
					int num9 = num8;
					if (num9 != num7)
					{
						currentGameMode = num9;
						pfHoldCounter = 0;
						p2BallHoldCounter = 0;
						wasZeroedByCollisionLastFrame = false;
						ballFlipBuffer[0] = 0;
						ballFlipBuffer[1] = 0;
						Interlocked.Exchange(ref ballToggleRequested, 0);
						ClearSlopeStuff();
						try
						{
							UpdateGameModeDisplay();
						}
						catch
						{
						}
						try
						{
							UpdateEffectiveGravity();
						}
						catch
						{
						}
						try
						{
							bool flag2 = num7 == 6 || num7 == 10;
							switch (num9)
							{
							case 1:
							case 2:
							case 3:
								playerVelY_fixed >>= 1;
								break;
							case 4:
								if (flag2)
								{
									playerVelY_fixed >>= 1;
								}
								break;
							case 0:
							case 5:
							case 7:
							case 8:
							case 9:
								if (flag2)
								{
									playerVelY_fixed = 0;
								}
								break;
							case 6:
								break;
							}
						}
						catch
						{
						}
					}
					if ((!dual || twoplayer) && num6 != 0 && num6 != 4 && num6 != 88)
					{
						try
						{
							int num10 = num5 / mapWidth;
							int num11 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
							int portalWorldY_px = (num10 - num11) * 16;
							targetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
						}
						catch
						{
						}
					}
					try
					{
						base.Dispatcher?.BeginInvoke((Action)delegate
						{
							try
							{
								UpdatePlayerImageForMode();
							}
							catch
							{
							}
						});
					}
					catch
					{
					}
					if (num9 != num7)
					{
						break;
					}
				}
				else
				{
					if ((num6 != 100 && num6 != 126) || processedRandomPortals.Contains(num5) || !SpriteIntersectsPlayer(num5, num6, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
					{
						continue;
					}
					int num12 = currentGameMode;
					int num13 = ((num6 == 100) ? new Random().Next(0, 8) : new Random().Next(0, 12));
					if (num13 != num12)
					{
						currentGameMode = num13;
						pfHoldCounter = 0;
						p2BallHoldCounter = 0;
						wasZeroedByCollisionLastFrame = false;
						ballFlipBuffer[0] = 0;
						ballFlipBuffer[1] = 0;
						Interlocked.Exchange(ref ballToggleRequested, 0);
						ClearSlopeStuff();
						try
						{
							UpdateGameModeDisplay();
						}
						catch
						{
						}
						try
						{
							UpdateEffectiveGravity();
						}
						catch
						{
						}
						try
						{
							bool flag3 = num12 == 6 || num12 == 10;
							switch (num13)
							{
							case 1:
							case 2:
							case 3:
								playerVelY_fixed >>= 1;
								break;
							case 4:
								if (flag3)
								{
									playerVelY_fixed >>= 1;
								}
								break;
							case 0:
							case 5:
							case 7:
							case 8:
							case 9:
								if (flag3)
								{
									playerVelY_fixed = 0;
								}
								break;
							case 6:
								break;
							}
						}
						catch
						{
						}
					}
					try
					{
						base.Dispatcher?.BeginInvoke((Action)delegate
						{
							try
							{
								UpdatePlayerImageForMode();
							}
							catch
							{
							}
						});
					}
					catch
					{
					}
					processedRandomPortals.Add(num5);
					break;
				}
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[GAMEMODE PORTAL] Error: " + ex.Message);
		}
	}

	private void CheckGravityModPortals()
	{
		try
		{
			AppendSimDebug($"[GRAV_MOD] Checking portals - playerX={playerX_fixed >> 8}, playerY={playerY_fixed >> 8}, gravMult={gravityMultiplier:F3}");
			int num = (playerX_fixed >> 8) + 1;
			int num2 = playerY_fixed >> 8;
			int num3 = (miniMode ? 8 : 15);
			int num4 = (miniMode ? 7 : 15);
			num2 += GetMiniSpriteOffsetY();
			int playerLeft_px = num;
			int playerRight_px = num + num3 - 1;
			int playerTop_px = num2;
			int playerBottom_px = num2 + num4 - 1;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num5 = nonEmptySpriteIndices[i];
				int num6 = sprites[num5];
				if (num6 >= 0 && num6 >= 95 && num6 <= 99 && !processedGravityModPortals.Contains(num5) && SpriteIntersectsPlayer(num5, num6, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
				{
					double num7 = 1.0;
					switch (num6)
					{
					case 95:
						num7 = 1.0 / 3.0;
						break;
					case 96:
						num7 = 0.5;
						break;
					case 97:
						num7 = 2.0 / 3.0;
						break;
					case 98:
						num7 = 2.0;
						break;
					case 99:
						num7 = 1.0;
						break;
					}
					gravityMultiplier = num7;
					UpdateEffectiveGravity();
					AppendSimDebug($"[GRAV_MOD] Portal 0x{num6:X2} activated: multiplier set to {gravityMultiplier:F3}x, effectiveGravity={effectiveGravity_fixed}");
					processedGravityModPortals.Add(num5);
					break;
				}
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[GRAV_MOD] Error: " + ex.Message);
		}
	}

	private void CheckGravityModTriggers(int prevPlayerCenter_fixed, int attemptedPlayerCenter_fixed)
	{
		try
		{
			int num = playerX_fixed + 4096;
			bool flag = prevPlayerCenter_fixed < 20480 && attemptedPlayerCenter_fixed >= 20480;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num2 = nonEmptySpriteIndices[i];
				int num3 = sprites[num2];
				if (num3 < 0 || !IsGravityModTrigger(num3))
				{
					continue;
				}
				int num4;
				if (spriteAnchors != null && spriteAnchors.TryGetValue(num2, out (int, int) value))
				{
					(num4, _) = value;
				}
				else
				{
					num4 = num2 % mapWidth;
				}
				int num5 = num4 * 16 + 8 << 8;
				if (flag)
				{
					if (num5 > prevPlayerCenter_fixed && num5 <= 20480)
					{
						if (!processedGravityModPortals.Contains(num2))
						{
							double num6 = 1.0;
							switch (num3)
							{
							case 112:
								num6 = 1.0 / 3.0;
								break;
							case 113:
								num6 = 0.5;
								break;
							case 114:
								num6 = 2.0 / 3.0;
								break;
							case 115:
								num6 = 2.0;
								break;
							case 116:
								num6 = 1.0;
								break;
							}
							gravityMultiplier = num6;
							UpdateEffectiveGravity();
							AppendSimDebug($"[GRAV_MOD_TRIG] Trigger 0x{num3:X2} activated at X={num4 * 16}: multiplier={gravityMultiplier:F3}x");
							processedGravityModPortals.Add(num2);
						}
					}
					else if (processedGravityModPortals.Contains(num2) && num5 > 20480)
					{
						processedGravityModPortals.Remove(num2);
					}
				}
				else if (num5 <= num)
				{
					if (!processedGravityModPortals.Contains(num2))
					{
						double num7 = 1.0;
						switch (num3)
						{
						case 112:
							num7 = 1.0 / 3.0;
							break;
						case 113:
							num7 = 0.5;
							break;
						case 114:
							num7 = 2.0 / 3.0;
							break;
						case 115:
							num7 = 2.0;
							break;
						case 116:
							num7 = 1.0;
							break;
						}
						gravityMultiplier = num7;
						UpdateEffectiveGravity();
						AppendSimDebug($"[GRAV_MOD_TRIG] Trigger 0x{num3:X2} activated at X={num4 * 16}: multiplier={gravityMultiplier:F3}x");
						processedGravityModPortals.Add(num2);
					}
				}
				else if (processedGravityModPortals.Contains(num2))
				{
					processedGravityModPortals.Remove(num2);
				}
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[GRAV_MOD_TRIG] Error: " + ex.Message);
		}
	}

	private void CheckMiniGrowthPortals()
	{
		try
		{
			int num = (playerX_fixed >> 8) + 1;
			int num2 = playerY_fixed >> 8;
			bool flag = currplayer_mini != 0;
			int num3 = (flag ? 8 : 15);
			int num4 = (flag ? 7 : 15);
			num2 += GetMiniSpriteOffsetY();
			int playerLeft_px = num;
			int playerRight_px = num + num3 - 1;
			int playerTop_px = num2;
			int playerBottom_px = num2 + num4 - 1;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num5 = nonEmptySpriteIndices[i];
				int num6 = sprites[num5];
				if (num6 < 0)
				{
					continue;
				}
				bool flag2 = num6 == 24;
				bool flag3 = num6 == 25;
				if ((!flag2 && !flag3) || processedMiniPortals.Contains(num5) || !SpriteIntersectsPlayer(num5, num6, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px) || miniMode == flag2)
				{
					continue;
				}
				miniMode = flag2;
				currplayer_mini = (byte)(miniMode ? 1u : 0u);
				if (dual)
				{
					player_mini[0] = miniMode;
					player_mini[1] = miniMode;
				}
				try
				{
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						if (MiniCheckBox != null)
						{
							MiniCheckBox.IsChecked = miniMode;
						}
					});
				}
				catch
				{
				}
				try
				{
					base.Dispatcher?.BeginInvoke((Action)delegate
					{
						try
						{
							UpdatePlayerImageForMode();
						}
						catch
						{
						}
					});
				}
				catch
				{
				}
				try
				{
					base.Dispatcher?.BeginInvoke((Action)delegate
					{
						try
						{
							UpdatePlayerVisualSizeForMode();
						}
						catch
						{
						}
					});
				}
				catch
				{
				}
				processedMiniPortals.Add(num5);
				break;
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[MINI/GROWTH PORTAL] Error: " + ex.Message);
		}
	}

	private void CheckCamLockPortals(int prevPlayerCenter_fixed, int attemptedPlayerCenter_fixed)
	{
		try
		{
			int num = cameraX_fixed + 32768;
			bool flag = prevPlayerCenter_fixed < 20480 && attemptedPlayerCenter_fixed >= 20480;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num2 = nonEmptySpriteIndices[i];
				int num3 = sprites[num2];
				if (num3 < 0)
				{
					continue;
				}
				bool flag2 = num3 == 221;
				bool flag3 = num3 == 237;
				if (!flag2 && !flag3)
				{
					continue;
				}
				int num4;
				if (spriteAnchors != null && spriteAnchors.TryGetValue(num2, out (int, int) value))
				{
					(num4, _) = value;
				}
				else
				{
					num4 = num2 % mapWidth;
				}
				int num5 = num4 * 16 + 8 << 8;
				if (flag)
				{
					if (num5 > prevPlayerCenter_fixed && num5 <= 20480)
					{
						if (!processedCamLockPortals.Contains(num2))
						{
							nocamlockforced = flag2;
							processedCamLockPortals.Add(num2);
							AppendSimDebug($"[CAM_LOCK] nocamlockforced={nocamlockforced} at idx={num2} (crossing)");
						}
					}
					else if (num5 > 20480)
					{
						processedCamLockPortals.Remove(num2);
					}
				}
				else if (num5 <= num)
				{
					if (!processedCamLockPortals.Contains(num2))
					{
						nocamlockforced = flag2;
						processedCamLockPortals.Add(num2);
						AppendSimDebug($"[CAM_LOCK] nocamlockforced={nocamlockforced} at idx={num2} (center)");
					}
				}
				else
				{
					processedCamLockPortals.Remove(num2);
				}
			}
			int num6 = (playerX_fixed >> 8) + 1;
			int num7 = playerY_fixed >> 8;
			bool flag4 = currplayer_mini != 0;
			int num8 = (flag4 ? 8 : 15);
			int num9 = (flag4 ? 7 : 15);
			num7 += GetMiniSpriteOffsetY();
			int playerLeft_px = num6;
			int playerRight_px = num6 + num8 - 1;
			int playerTop_px = num7;
			int playerBottom_px = num7 + num9 - 1;
			for (int j = 0; j < nonEmptySpriteIndices.Length; j++)
			{
				int num10 = nonEmptySpriteIndices[j];
				int num11 = sprites[num10];
				if (num11 >= 0)
				{
					bool flag5 = num11 == 142;
					bool flag6 = num11 == 158;
					if ((flag5 || flag6) && !processedWrapPortals.Contains(num10) && SpriteIntersectsPlayer(num10, num11, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px, ignoreSentinels: true))
					{
						wrapMode = flag5;
						processedWrapPortals.Add(num10);
						AppendSimDebug($"[WRAP] wrapMode={wrapMode} at idx={num10}");
					}
				}
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[CAM_LOCK] Error: " + ex.Message);
		}
	}

	private void CheckMiscTriggers()
	{
		try
		{
			int num = (playerX_fixed >> 8) + 1;
			int num2 = playerY_fixed >> 8;
			bool flag = currplayer_mini != 0;
			int num3 = (flag ? 8 : 15);
			int num4 = (flag ? 7 : 15);
			num2 += GetMiniSpriteOffsetY();
			int playerLeft_px = num;
			int playerRight_px = num + num3 - 1;
			int playerTop_px = num2;
			int playerBottom_px = num2 + num4 - 1;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num5 = nonEmptySpriteIndices[i];
				int num6 = sprites[num5];
				if (num6 < 0)
				{
					continue;
				}
				if (num6 == 244 || num6 == 245)
				{
					if (!processedTimewarpTriggers.Contains(num5) && SpriteIntersectsPlayer(num5, num6, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px, ignoreSentinels: true))
					{
						slowMode = num6 == 244;
						processedTimewarpTriggers.Add(num5);
					}
				}
				else if ((num6 == 242 || num6 == 243) && !processedTrailTriggers.Contains(num5) && SpriteIntersectsPlayer(num5, num6, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px, ignoreSentinels: true))
				{
					forcedTrails = ((num6 == 242) ? 2 : 0);
					processedTrailTriggers.Add(num5);
				}
			}
		}
		catch
		{
		}
	}

	private void CheckDualPortal()
	{
		try
		{
			if (dual)
			{
				return;
			}
			int num = (playerX_fixed >> 8) + 1;
			int num2 = playerY_fixed >> 8;
			int num3 = (miniMode ? 8 : 15);
			int num4 = (miniMode ? 7 : 15);
			num2 += GetMiniSpriteOffsetY();
			int playerLeft_px = num;
			int playerRight_px = num + num3 - 1;
			int playerTop_px = num2;
			int playerBottom_px = num2 + num4 - 1;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num5 = nonEmptySpriteIndices[i];
				int num6 = sprites[num5];
				if (num6 == 34 && !processedMiniPortals.Contains(num5) && SpriteIntersectsPlayer(num5, num6, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
				{
					dual = true;
					twoplayer = false;
					player_x_fixed[0] = playerX_fixed;
					player_y_fixed[0] = playerY_fixed;
					player_vel_y_fixed[0] = playerVelY_fixed;
					player_mini[0] = miniMode;
					player_gravity[0] = currplayer_gravity;
					player_x_fixed[1] = playerX_fixed;
					player_y_fixed[1] = playerY_fixed;
					player_vel_y_fixed[1] = -playerVelY_fixed;
					player_mini[1] = miniMode;
					player_gravity[1] = (byte)(currplayer_gravity ^ 0xFF);
					player_wasZeroed[1] = false;
					player_onGround[1] = false;
					player_groundStabilize[1] = 0;
					player_ballFlipCooldown[1] = 0;
					player_ballWasGroundedBeforeFlip[1] = false;
					ballSwitched[1] = false;
					ballFlipBuffer[1] = 0;
					orbBufferActive[1] = false;
					orbHoldSuppressing[1] = false;
					orbHoldConsumedKeyStillDown[1] = false;
					p2BallHoldCounter = 0;
					player_cubeRotate[1] = cubeRotate_fixed;
					player_cubeRotateMini[1] = cubeRotateMini_fixed;
					player_shipRotate[1] = shipRotate_fixed;
					player_swingRotate[1] = swingcopterRotate_fixed;
					player_footballRotate[1] = footballRotate_fixed;
					AppendSimDebug($"[DUAL_PORTAL] Activated! Player 2 spawned: X={player_x_fixed[1] >> 8} Y={player_y_fixed[1] >> 8} velY={player_vel_y_fixed[1]:X4} gravity={player_gravity[1]:X2}");
					try
					{
						int num7 = num5 / mapWidth;
						int num8 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
						int portalWorldY_px = (num7 - num8) * 16;
						targetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
						AppendSimDebug($"[DUAL_PORTAL] Set targetCameraY={targetCameraY_fixed >> 8}px from portal at tileY={num7}");
					}
					catch
					{
					}
					processedMiniPortals.Add(num5);
					break;
				}
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[DUAL PORTAL] Error: " + ex.Message);
		}
	}

	private void CheckSinglePortal()
	{
		try
		{
			if (!dual)
			{
				return;
			}
			int num = currplayer;
			int num2 = (player_x_fixed[num] >> 8) + 1;
			int num3 = player_y_fixed[num] >> 8;
			int num4 = (miniMode ? 8 : 15);
			int num5 = (miniMode ? 7 : 15);
			num3 += GetMiniSpriteOffsetY();
			int playerLeft_px = num2;
			int playerRight_px = num2 + num4 - 1;
			int playerTop_px = num3;
			int playerBottom_px = num3 + num5 - 1;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num6 = nonEmptySpriteIndices[i];
				int num7 = sprites[num6];
				if (num7 == 35 && !processedMiniPortals.Contains(num6) && SpriteIntersectsPlayer(num6, num7, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
				{
					try
					{
						int num8 = num6 / mapWidth;
						int num9 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
						int portalWorldY_px = (num8 - num9) * 16;
						targetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
						AppendSimDebug($"[SINGLE_PORTAL] Set targetCameraY={targetCameraY_fixed >> 8}px from portal at tileY={num8}");
					}
					catch
					{
					}
					if (currplayer == 0)
					{
						dual = false;
						_prevDualActiveForP2Path = false;
						AppendSimDebug($"[SINGLE_PORTAL] P1 hit portal — immediate dual=false idx={num6}");
					}
					else
					{
						player_y_fixed[0] = playerY_fixed;
						player_gravity[0] = currplayer_gravity;
						player_vel_y_fixed[0] = playerVelY_fixed;
						singlePortalExitPending = true;
						AppendSimDebug($"[SINGLE_PORTAL] P2 hit portal — synced P2 state to P1: Y={playerY_fixed >> 8} vel=0x{playerVelY_fixed:X4} grav={currplayer_gravity:X2} idx={num6}");
					}
					processedMiniPortals.Add(num6);
					break;
				}
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[SINGLE PORTAL] Error: " + ex.Message);
		}
	}

	private void CheckAlphabetBlocks()
	{
		try
		{
			int num = (playerX_fixed >> 8) + 1;
			int num2 = playerY_fixed >> 8;
			int num3 = (miniMode ? 8 : 15);
			int num4 = (miniMode ? 7 : 15);
			num2 += GetMiniSpriteOffsetY();
			int num5 = num + num3 - 1;
			int num6 = num2;
			int num7 = num2 + num4 - 1;
			int num8 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num9 = nonEmptySpriteIndices[i];
				int num10 = sprites[num9];
				if (num10 < 0)
				{
					continue;
				}
				bool flag = num10 == 249;
				bool flag2 = num10 == 250;
				bool flag3 = num10 == 248;
				bool flag4 = num10 == 247;
				bool flag5 = num10 == 246;
				if (!flag && !flag2 && !flag3 && !flag4 && !flag5)
				{
					continue;
				}
				int num11 = num9 % mapWidth;
				int num12 = num9 / mapWidth;
				int num13 = num10 & 0xFF;
				if (spriteAnchors != null && spriteAnchors.TryGetValue(num9, out (int, int) value))
				{
					int num14 = value.Item2 * mapWidth + value.Item1;
					if (num14 >= 0 && num14 < sprites.Length)
					{
						int num15 = sprites[num14];
						if (num15 >= 0 && num15 < 256)
						{
							num13 = num15 & 0xFF;
						}
					}
				}
				int val = ((num13 >= 0 && num13 < sprite_widths.Length) ? sprite_widths[num13] : 16);
				int num16 = ((num13 >= 0 && num13 < sprite_heights.Length) ? sprite_heights[num13] : 16);
				if (num16 >= 252)
				{
					continue;
				}
				int num17 = ((num13 >= 0 && num13 < sprite_x_offset.Length) ? sprite_x_offset[num13] : 0);
				int num18 = ((num13 >= 0 && num13 < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[num13] : 0);
				int num19 = 0;
				int num20 = 0;
				int num21 = -1;
				if (spriteAnchors != null && spriteAnchors.TryGetValue(num9, out (int, int) value2))
				{
					num21 = value2.Item2 * mapWidth + value2.Item1;
				}
				(int, int) value4;
				if (num21 >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num21, out (int, int) value3))
				{
					(num19, num20) = value3;
				}
				else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num9, out value4))
				{
					(num19, num20) = value4;
				}
				int num22 = num11 * 16 + num17 + num19;
				int num23 = (num12 - num8) * 16 + num18 + num20 - 1;
				int num24 = num22 + Math.Max(1, val);
				int num25 = num23 + Math.Max(1, num16);
				bool flag6 = num5 >= num22 && num24 >= num;
				bool flag7 = num7 >= num23 && num25 >= num6;
				if (flag6 && flag7)
				{
					if (flag && dashing[currplayer] != 0)
					{
						dashing[currplayer] = 0;
						orbed[currplayer] = true;
						playerVelY_fixed = 0;
						velocityY = 0;
						AppendSimDebug("[S_BLOCK] Stopped dash, orbed=true, velocityY=0");
					}
					else if (flag2)
					{
						dblocked = true;
						AppendSimDebug("[D_BLOCK] dblocked=true");
					}
					else if (flag3)
					{
						hblocked = true;
						AppendSimDebug("[H_BLOCK] hblocked=true (headbonk)");
					}
					else if (flag4)
					{
						jblocked = true;
						orbed[currplayer] = true;
						AppendSimDebug("[J_BLOCK] jblocked=true, orbed=true");
					}
					else if (flag5)
					{
						fblocked = true;
						AppendSimDebug("[F_BLOCK] fblocked=true");
					}
				}
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[ALPHABET BLOCKS] Error: " + ex.Message);
		}
	}

	private static bool IsCoinSprite(int sid)
	{
		return SharedPhysics.IsCoinSprite(sid);
	}

	private void CheckCoinCollision()
	{
		try
		{
			int num = (playerX_fixed >> 8) + 1;
			int num2 = playerY_fixed >> 8;
			int num3 = ((currplayer_mini != 0) ? 8 : 15);
			int num4 = ((currplayer_mini != 0) ? 7 : 15);
			num2 += GetMiniSpriteOffsetY();
			int num5 = num;
			int num6 = num + num3 - 1;
			int num7 = num2;
			int num8 = num2 + num4 - 1;
			int num9 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num10 = nonEmptySpriteIndices[i];
				int num11 = sprites[num10];
				if (num11 >= 0 && (IsCoinSprite(num11) || SharedPhysics.IsMiniCoinSprite(num11)) && !collectedCoins.Contains(num10))
				{
					int num12 = num10 % mapWidth;
					int num13 = num10 / mapWidth;
					int num14 = 0;
					int num15 = 0;
					int num16 = -1;
					if (spriteAnchors != null && spriteAnchors.TryGetValue(num10, out (int, int) value))
					{
						num16 = value.Item2 * mapWidth + value.Item1;
					}
					(int, int) value3;
					if (num16 >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num16, out (int, int) value2))
					{
						(num14, num15) = value2;
					}
					else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num10, out value3))
					{
						(num14, num15) = value3;
					}
					int num17 = num12 * 16 + num14;
					int num18 = (num13 - num9) * 16 + num15 - 1;
					int num19 = num17 + 16;
					int num20 = num18 + 16;
					bool flag = num6 + 1 >= num17 && num19 >= num5;
					bool flag2 = num8 + 1 >= num18 && num20 >= num7;
					if (flag && flag2)
					{
						collectedCoins.Add(num10);
						collectedCoinInfo.Add((num10, num11));
						sprites[num10] = -1;
						AppendSimDebug($"[COIN] Collected coin 0x{num11:X2} at sprite index {num10}");
					}
				}
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[COIN] Error: " + ex.Message);
		}
	}

	private void CheckPadCollision()
	{
		try
		{
			int num = (playerX_fixed >> 8) + 1;
			if (currentGameMode == 4)
			{
				AppendSimDebug($"[ROBOT_PAD_CHECK] Frame: playerX_px={num}, playerY_px={playerY_fixed >> 8}");
			}
			int num2 = playerY_fixed >> 8;
			int num3 = ((currplayer_mini != 0) ? 8 : 15);
			int num4 = ((currplayer_mini != 0) ? 7 : 15);
			num2 += GetMiniSpriteOffsetY();
			int num5 = num;
			int num6 = num + num3 - 1;
			int num7 = num2;
			int num8 = num2 + num4 - 1;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num9 = nonEmptySpriteIndices[i];
				int num10 = sprites[num9];
				if (num10 < 0)
				{
					continue;
				}
				int num11 = -1;
				bool flag = false;
				if (num10 == 10 || num10 == 12)
				{
					num11 = 1;
					flag = num10 == 10;
				}
				else if (num10 == 37 || num10 == 38)
				{
					num11 = 3;
					flag = num10 == 37;
				}
				else
				{
					if (num10 != 82 && num10 != 83)
					{
						if (num10 != 101 || (orbActivated.ContainsKey(num9) && orbActivated[num9]) || !SpriteIntersectsPlayer(num9, num10, num5, num6, num7, num8))
						{
							continue;
						}
						if (!gravityReversed)
						{
							gravityReversed = true;
							currplayer_gravity = byte.MaxValue;
							gravityFlipped = true;
							AppendSimDebug($"[GREEN_PAD] REVERSE ACTIVATED at idx {num9}");
						}
						else
						{
							gravityReversed = false;
							currplayer_gravity = 0;
							gravityFlipped = false;
							AppendSimDebug($"[GREEN_PAD] NORMAL ACTIVATED at idx {num9}");
						}
						UpdateEffectiveGravity();
						try
						{
							base.Dispatcher?.BeginInvoke((Action)delegate
							{
								UpdatePlayerIconFlip();
								InvertedCheckBox.IsChecked = gravityReversed;
							});
						}
						catch
						{
						}
						ClearSlopeStuff();
						int num12 = currentGameMode;
						if (num12 == 8)
						{
							num12 = 0;
						}
						if (num12 == 9)
						{
							num12 = 7;
						}
						if (num12 == 11)
						{
							num12 = 0;
						}
						if (num12 >= 0 && num12 <= 11)
						{
							int num13 = ((currplayer_mini != 0) ? PadOrbHeights_Mini[0][num12] : PadOrbHeights[0][num12]);
							int num14 = (gravityReversed ? 1 : (-1));
							int value = (playerVelY_fixed = num13 * num14);
							orbhitonthisframe[currplayer] = true;
							AppendSimDebug($"[GREEN_PAD] Applied yellow orb velocity: baseVel=0x{num13:X4}, newVel=0x{value:X4}, gravityReversed={gravityReversed}");
						}
						orbActivated[num9] = true;
						continue;
					}
					num11 = 8;
					flag = num10 == 82;
				}
				if (SpriteIntersectsPlayer(num9, num10, num5, num6, num7, num8))
				{
					if (currentGameMode == 4)
					{
						AppendSimDebug($"[ROBOT_PAD_HIT] sid=0x{num10:X2}, idx={num9}, padRow={num11}");
					}
					ClearSlopeStuff();
					int num15 = num9 % mapWidth;
					int num16 = num9 / mapWidth;
					int value2 = num15 * 16;
					int value3 = num16 * 16;
					AppendSimDebug($"[PAD_PRE] Frame overlap detected! sid=0x{num10:X2}, padRow={num11}, velY_before=0x{playerVelY_fixed:X4}");
					AppendSimDebug($"[PAD_COLLISION] Player: L={num5} R={num6} T={num7} B={num8}, Pad: tileX={num15} tileY={num16} worldX={value2} worldY={value3}");
					int num17 = currentGameMode;
					if (num17 == 8)
					{
						num17 = 0;
					}
					if (num17 == 9)
					{
						num17 = 7;
					}
					if (num17 == 11)
					{
						num17 = 0;
					}
					if (num17 >= 0 && num17 <= 11 && num11 >= 0 && num11 < 9)
					{
						bool flag2 = currplayer_mini != 0;
						int num18 = (flag2 ? PadOrbHeights_Mini[num11][num17] : PadOrbHeights[num11][num17]);
						int num19 = ((currplayer_gravity != 0) ? 1 : (-1));
						int value4 = (playerVelY_fixed = num18 * num19);
						orbhitonthisframe[currplayer] = true;
						AppendSimDebug($"[PAD_SET] sid=0x{num10:X2}, row={num11}, mode={num17}, mini={flag2}, baseVel=0x{num18:X4}, newVel=0x{value4:X4}, velY_after=0x{playerVelY_fixed:X4}");
					}
				}
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[PAD COLLISION] Error: " + ex.Message);
		}
	}

	private void CheckSpiderOrbPadCollision()
	{
		try
		{
			int num = (playerX_fixed >> 8) + 1;
			int num2 = playerY_fixed >> 8;
			int num3 = (miniMode ? 8 : 15);
			int num4 = (miniMode ? 7 : 15);
			num2 += GetMiniSpriteOffsetY();
			int playerLeft_px = num;
			int playerRight_px = num + num3 - 1;
			int playerTop_px = num2;
			int playerBottom_px = num2 + num4 - 1;
			bool flag = IsXDownAsync() || keyXHeld;
			int num5 = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
			bool flag2 = num5 > 0;
			int num6 = 0;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num7 = nonEmptySpriteIndices[i];
				int num8 = sprites[num7];
				if (num8 < 0)
				{
					continue;
				}
				bool flag3 = num8 == 84;
				bool flag4 = num8 == 85;
				bool flag5 = num8 == 86;
				bool flag6 = num8 == 87;
				if (!flag3 && !flag4 && !flag5 && !flag6)
				{
					continue;
				}
				num6++;
				bool flag7 = flag3 || flag4;
				if (flag7 && playerProcessedOrbs[currplayer].Contains(num7))
				{
					continue;
				}
				bool flag8 = false;
				try
				{
					flag8 = CheckOrbCollision(num7, num8, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px);
				}
				catch (Exception ex)
				{
					AppendSimDebug($"[SPIDER_ORB/PAD] CheckOrbCollision error for 0x{num8:X2} at idx {num7}: {ex.Message}");
					continue;
				}
				if (!flag8)
				{
					continue;
				}
				AppendSimDebug($"[SPIDER_ORB/PAD] *** COLLISION DETECTED *** with 0x{num8:X2} at idx {num7}, isOrb={flag7}, holdJump={flag}, pressJump={flag2}");
				bool flag9 = false;
				if (flag7 && (flag2 || flag))
				{
					flag9 = true;
				}
				else if (!flag7)
				{
					flag9 = true;
				}
				if (flag9)
				{
					AppendSimDebug($"[SPIDER_ORB/PAD] ACTIVATING 0x{num8:X2}");
					ClearSlopeStuff();
					int groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
					bool flag10 = currplayer_mini != 0;
					int width = (flag10 ? 8 : 15);
					int num9 = (flag10 ? 7 : 15);
					int num10 = (flag10 ? (16 - num9 >> 1) : 0);
					if (flag3 || flag5)
					{
						AppendSimDebug("[SPIDER_ORB/PAD] Teleporting UP");
						var (flag11, num11) = BgCollD_Spider(num, num2 + num10, width, num9, groundRowsToReserve);
						if (flag11)
						{
							playerY_fixed -= num11 << 8;
							AppendSimDebug($"[SPIDER_ORB/PAD] Ejected from floor by {num11}px");
						}
						playerVelY_fixed = 0;
						currplayer_gravity = byte.MaxValue;
						gravityReversed = true;
						gravityFlipped = true;
						wasZeroedByCollisionLastFrame = false;
						UpdateCurrplayerTableIdx_Fresh();
						SpiderUpWait_Fresh();
						playerVelY_fixed = 0;
						orbed[currplayer] = true;
						try
						{
							base.Dispatcher?.BeginInvoke((Action)delegate
							{
								UpdatePlayerIconFlip();
							});
						}
						catch
						{
						}
						AppendSimDebug($"[SPIDER_ORB/PAD] Teleported to ceiling Y={playerY_fixed >> 8}");
					}
					else
					{
						AppendSimDebug("[SPIDER_ORB/PAD] Teleporting DOWN");
						var (flag12, num12) = BgCollU_Spider(num, num2, width, num9, groundRowsToReserve);
						if (flag12)
						{
							playerY_fixed += num12 + 1 << 8;
							AppendSimDebug($"[SPIDER_ORB/PAD] Ejected from ceiling by {num12 + 1}px");
						}
						playerVelY_fixed = 0;
						currplayer_gravity = 0;
						gravityReversed = false;
						gravityFlipped = false;
						wasZeroedByCollisionLastFrame = false;
						UpdateCurrplayerTableIdx_Fresh();
						SpiderDownWait_Fresh();
						playerVelY_fixed = 0;
						orbed[currplayer] = true;
						try
						{
							base.Dispatcher?.BeginInvoke((Action)delegate
							{
								UpdatePlayerIconFlip();
							});
						}
						catch
						{
						}
						AppendSimDebug($"[SPIDER_ORB/PAD] Teleported to floor Y={playerY_fixed >> 8}");
					}
					if (flag7)
					{
						playerProcessedOrbs[currplayer].Add(num7);
						if (!dual)
						{
							orbActivated[num7] = true;
						}
					}
					if (flag7 && flag2)
					{
						Interlocked.Exchange(ref keyXPressedCount, 0);
					}
					return;
				}
				AppendSimDebug($"[SPIDER_ORB/PAD] Did NOT activate - isOrb={flag7}, pressJump={flag2}, holdJump={flag}");
			}
			if (num6 > 0)
			{
				AppendSimDebug($"[SPIDER_ORB/PAD] Scanned {num6} spider orbs/pads in level");
			}
		}
		catch (Exception ex2)
		{
			AppendSimDebug("[SPIDER_ORB/PAD COLLISION] Error: " + ex2.Message);
		}
	}

	private bool IsTouchingCeiling()
	{
		try
		{
			int num = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num2 = num - 7;
			int num3 = num2 + 14;
			int num4 = playerY_fixed >> 8;
			int num5 = num4 / 16;
			int num6 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			int num7 = num5 + num6;
			if (num7 < 0 || num7 >= mapHeight)
			{
				return false;
			}
			for (int i = num2 / 16; i <= num3 / 16; i++)
			{
				if (i < 0 || i >= mapWidth)
				{
					continue;
				}
				int num8 = tiles[num7 * mapWidth + i];
				int num9 = MapAnimatedTileIndex(num8);
				int num10 = num9;
				if (num9 >= 1000)
				{
					num10 = ((num9 >= 1000 && num9 <= 1007) ? (8 + (num9 - 1000) % 4) : ((num9 >= 1010 && num9 <= 1015) ? (((num9 - 1010) % 3) switch
					{
						1 => 125, 
						0 => 4, 
						_ => 127, 
					}) : ((num9 < 1020 || num9 > 1037) ? num8 : (116 + (num9 - 1020) % 9))));
				}
				MetatileCollision col = MetatileCollisionTable.GetCollision((byte)num10);
				int num11 = i * 16;
				int num12 = Math.Max(0, num2 - num11);
				int num13 = Math.Min(15, num3 - num11);
				for (int j = num12; j <= num13; j++)
				{
					int num14 = num11 + j;
					int num15 = (playerX_fixed >> 8) + playerVisualWidth / 2;
					if ((MainWindow.Option_NoDeath || num14 < num15) && BlocksCeilingAtColumn(col, j))
					{
						return true;
					}
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	private bool IsTouchingCeilingStrict()
	{
		try
		{
			int num = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num2 = num - 7;
			int num3 = num2 + 14;
			int num4 = playerY_fixed >> 8;
			int num5 = num4 / 16;
			int num6 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			int num7 = num5 + num6;
			if (num7 < 0 || num7 >= mapHeight)
			{
				return false;
			}
			for (int i = num2 / 16; i <= num3 / 16; i++)
			{
				if (i < 0 || i >= mapWidth)
				{
					continue;
				}
				int num8 = tiles[num7 * mapWidth + i];
				int num9 = MapAnimatedTileIndex(num8);
				int num10 = num9;
				if (num9 >= 1000)
				{
					num10 = ((num9 >= 1000 && num9 <= 1007) ? (8 + (num9 - 1000) % 4) : ((num9 >= 1010 && num9 <= 1015) ? (((num9 - 1010) % 3) switch
					{
						1 => 125, 
						0 => 4, 
						_ => 127, 
					}) : ((num9 < 1020 || num9 > 1037) ? num8 : (116 + (num9 - 1020) % 9))));
				}
				MetatileCollision col = MetatileCollisionTable.GetCollision((byte)num10);
				int num11 = i * 16;
				int num12 = Math.Max(0, num2 - num11);
				int num13 = Math.Min(15, num3 - num11);
				int num14 = num5 * 16;
				int localY = Math.Max(0, Math.Min(15, num4 - num14));
				for (int j = num12; j <= num13; j++)
				{
					try
					{
						if (TileOccupiesPixel(col, j, localY))
						{
							return true;
						}
					}
					catch
					{
						if (BlocksCeilingAtColumn(col, j))
						{
							return true;
						}
					}
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	private int NesNtCameraTarget_fixed(int portalWorldY_px)
	{
		int num = portalWorldY_px - 58;
		int num2 = num + _sim_nesCoordOffset;
		if (num2 < 256)
		{
			return Math.Max(0, num << 8);
		}
		if ((num2 & 0xFF) >= 240)
		{
			num2 += 16;
		}
		int num3 = num2 >> 8;
		int num4 = num2 & 0xFF;
		int num5 = num3 * 240 + num4;
		int num6 = num5 - _sim_nesCoordOffset;
		return Math.Max(0, num6 << 8);
	}

	private void UpdatePlayerImageForMode()
	{
		try
		{
			if (playerImage == null)
			{
				return;
			}
			string path = AppDomain.CurrentDomain.BaseDirectory ?? ".";
			string text = "cube.png";
			if (miniMode && currentGameMode == 0)
			{
				text = "cube-mini.png";
			}
			else if (miniMode && currentGameMode == 1)
			{
				text = "ship-mini.png";
			}
			else if (miniMode && currentGameMode == 2)
			{
				text = "ball-mini.png";
			}
			else if (miniMode && currentGameMode == 3)
			{
				text = "ufo-mini.png";
			}
			else if (miniMode && currentGameMode == 4)
			{
				text = "robot-mini.png";
			}
			else if (miniMode && currentGameMode == 5)
			{
				text = "spider-mini.png";
			}
			else if (miniMode && currentGameMode == 6)
			{
				text = "wave-mini.png";
			}
			else if (miniMode && currentGameMode == 7)
			{
				text = "swingcopter-mini.png";
			}
			else if (miniMode && currentGameMode == 8)
			{
				text = "ninja-mini.png";
			}
			else if (miniMode && currentGameMode == 9)
			{
				text = ((pogoBounceAnimationCounter <= 0) ? "pogo-mini.png" : "pogo-mini2.png");
			}
			else if (miniMode && currentGameMode == 10)
			{
				text = "snake-mini.png";
			}
			else if (miniMode && currentGameMode == 11)
			{
				text = "football-mini.png";
			}
			else if (currentGameMode == 1)
			{
				int shipSpriteFrame = GetShipSpriteFrame();
				int[] array = new int[8] { 0, 0, 1, 2, 3, 4, 5, 6 };
				int num = array[shipSpriteFrame & 7];
				string text3;
				if (miniMode)
				{
					if (1 == 0)
					{
					}
					string text2 = num switch
					{
						0 => "ship-mini.png", 
						1 => "ship-mini1.png", 
						2 => "ship-mini2.png", 
						3 => "ship-mini3.png", 
						4 => "ship-mini4.png", 
						5 => "ship-mini5.png", 
						6 => "ship-mini6.png", 
						_ => "ship-mini.png", 
					};
					if (1 == 0)
					{
					}
					text3 = text2;
				}
				else
				{
					if (1 == 0)
					{
					}
					string text2 = num switch
					{
						0 => "ship.png", 
						1 => "ship2.png", 
						2 => "ship3.png", 
						3 => "ship4.png", 
						4 => "ship5.png", 
						5 => "ship6.png", 
						6 => "ship7.png", 
						_ => "ship.png", 
					};
					if (1 == 0)
					{
					}
					text3 = text2;
				}
				text = text3;
			}
			else if (currentGameMode == 2)
			{
				text = ((ballAnimationFrameCounter >= 6) ? "ball2.png" : "ball.png");
			}
			else if (currentGameMode == 3)
			{
				text = "ufo.png";
			}
			else if (currentGameMode == 4)
			{
				text = "";
			}
			else if (currentGameMode == 5)
			{
				text = "";
			}
			else if (currentGameMode == 6)
			{
				text = ((Math.Abs(playerVelY_fixed) <= 768) ? "wave2.png" : ((playerVelY_fixed >= 0) ? "wave.png" : "wave.png"));
			}
			else if (currentGameMode == 7)
			{
				int swingcopterSpriteFrame = GetSwingcopterSpriteFrame();
				int[] array2 = new int[8] { 0, 0, 1, 2, 2, 3, 4, 4 };
				int num2 = array2[swingcopterSpriteFrame & 7];
				string text4;
				if (miniMode)
				{
					if (1 == 0)
					{
					}
					string text2 = num2 switch
					{
						0 => "swingcopter-mini.png", 
						1 => "swingcopter-mini1.png", 
						2 => "swingcopter-mini2.png", 
						3 => "swingcopter-mini3.png", 
						4 => "swingcopter-mini4.png", 
						_ => "swingcopter-mini.png", 
					};
					if (1 == 0)
					{
					}
					text4 = text2;
				}
				else
				{
					if (1 == 0)
					{
					}
					string text2 = num2 switch
					{
						0 => "swingcopter.png", 
						1 => "swingcopter1.png", 
						2 => "swingcopter2.png", 
						3 => "swingcopter3.png", 
						4 => "swingcopter4.png", 
						_ => "swingcopter.png", 
					};
					if (1 == 0)
					{
					}
					text4 = text2;
				}
				text = text4;
			}
			else if (currentGameMode == 8)
			{
				text = "ninja.png";
			}
			else if (currentGameMode == 9)
			{
				text = ((pogoBounceAnimationCounter <= 0) ? "pogo.png" : "pogo2.png");
			}
			else if (currentGameMode == 10)
			{
				text = "snake.png";
			}
			else if (currentGameMode == 11)
			{
				int footballSpriteFrameAndFlip = GetFootballSpriteFrameAndFlip();
				int num3 = footballSpriteFrameAndFlip & 0xF;
				int num4 = footballSpriteFrameAndFlip & 0xC0;
				text = (miniMode ? ("football-mini" + ((num3 > 0) ? num3.ToString() : "") + ".png") : ("football" + ((num3 > 0) ? num3.ToString() : "") + ".png"));
			}
			if (currentGameMode == 2)
			{
				AppendSimDebug($"[BALL_MODE] gameMode=2, counter={ballAnimationFrameCounter}, choice={text}");
			}
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			BitmapSource bitmapSource = null;
			try
			{
				string text5 = System.IO.Path.Combine(path, text);
				if (File.Exists(text5))
				{
					BitmapImage bitmapImage = new BitmapImage();
					bitmapImage.BeginInit();
					bitmapImage.UriSource = new Uri(text5);
					bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage.EndInit();
					bitmapImage.Freeze();
					bitmapSource = bitmapImage;
				}
			}
			catch
			{
				bitmapSource = null;
			}
			if (bitmapSource == null)
			{
				try
				{
					string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(path, "..\\..\\..\\..\\" + text));
					if (File.Exists(fullPath))
					{
						BitmapImage bitmapImage2 = new BitmapImage();
						bitmapImage2.BeginInit();
						bitmapImage2.UriSource = new Uri(fullPath);
						bitmapImage2.CacheOption = BitmapCacheOption.OnLoad;
						bitmapImage2.EndInit();
						bitmapImage2.Freeze();
						bitmapSource = bitmapImage2;
					}
				}
				catch
				{
				}
			}
			if (bitmapSource == null)
			{
				try
				{
					BitmapImage bitmapImage3 = LoadCachedResourceImage("." + text);
					if (currentGameMode == 2)
					{
						AppendSimDebug($"[BALL_RES] Looking for '{text}', cached: {bitmapImage3 != null}");
					}
					if (bitmapImage3 != null)
					{
						bitmapSource = App.EnsureUnfrozenForRender(bitmapImage3) ?? bitmapImage3;
					}
				}
				catch
				{
				}
			}
			if (bitmapSource != null)
			{
				AppendSimDebug($"[IMAGE_LOAD] Loading image, applyPlayer2Colors={applyPlayer2Colors}, choice={text}");
				if (applyPlayer2Colors)
				{
					string text6 = text.ToString();
					if (!player2ColorCache.ContainsKey(text6))
					{
						AppendSimDebug("[IMAGE_LOAD] Creating recolored P2 cache for " + text6);
						player2ColorCache[text6] = ReplaceColorsForPlayer2(bitmapSource);
					}
					else
					{
						AppendSimDebug("[IMAGE_LOAD] Using cached P2 colors for " + text6);
					}
					bitmapSource = player2ColorCache[text6];
				}
				else
				{
					string text7 = text.ToString();
					if (!playerColorCache.ContainsKey(text7))
					{
						AppendSimDebug("[IMAGE_LOAD] Caching P1 original colors for " + text7);
						playerColorCache[text7] = bitmapSource;
					}
					else
					{
						bitmapSource = playerColorCache[text7];
					}
				}
				playerImage.Source = App.EnsureUnfrozenForRender(bitmapSource) ?? bitmapSource;
				playerImage.Tag = text;
				playerImage.Width = bitmapSource.PixelWidth;
				playerImage.Height = bitmapSource.PixelHeight;
				playerImage.Visibility = Visibility.Visible;
				if (playerRect != null)
				{
					playerRect.Visibility = Visibility.Collapsed;
				}
				if (miniMode)
				{
					playerVisualWidth = 8;
					playerVisualHeight = 8;
				}
				else
				{
					playerVisualWidth = (int)Math.Ceiling(playerImage.Width);
					playerVisualHeight = (int)Math.Ceiling(playerImage.Height);
				}
				try
				{
					if (gravityReversed && currentGameMode != 0 && currentGameMode != 8)
					{
						playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
						playerImage.RenderTransform = new ScaleTransform(1.0, -1.0);
					}
					else
					{
						playerImage.RenderTransform = Transform.Identity;
					}
					return;
				}
				catch
				{
					return;
				}
			}
			if (playerRect == null)
			{
				playerRect = new Rectangle
				{
					Width = 16.0,
					Height = 16.0,
					Fill = new SolidColorBrush(Colors.Magenta)
				};
				Panel.SetZIndex(playerRect, 1000);
				RenderCanvas.Children.Add(playerRect);
			}
			playerRect.Visibility = Visibility.Visible;
			if (playerImage != null)
			{
				playerImage.Visibility = Visibility.Collapsed;
			}
			playerVisualWidth = (int)Math.Ceiling(playerRect.Width);
			playerVisualHeight = (int)Math.Ceiling(playerRect.Height);
		}
		catch
		{
		}
	}

	private void UpdatePlayerVisualSizeForMode()
	{
		try
		{
			if (miniMode)
			{
				playerVisualWidth = 8;
				playerVisualHeight = 8;
			}
			else if (playerImage != null && playerImage.Source != null && playerImage.Visibility == Visibility.Visible)
			{
				playerVisualWidth = (int)Math.Ceiling(playerImage.Width);
				playerVisualHeight = (int)Math.Ceiling(playerImage.Height);
			}
			else if (playerRect != null && playerRect.Visibility == Visibility.Visible)
			{
				playerVisualWidth = (int)Math.Ceiling(playerRect.Width);
				playerVisualHeight = (int)Math.Ceiling(playerRect.Height);
			}
		}
		catch
		{
		}
	}

	private void UpdatePlayerIconFlip()
	{
		try
		{
			if (InvertedCheckBox != null)
			{
				InvertedCheckBox.IsChecked = gravityReversed;
			}
			if (playerImage == null || playerImage.Source == null || currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8 || currentGameMode == 11)
			{
				return;
			}
			if (currentGameMode == 6)
			{
				if (playerVelY_fixed < 0)
				{
					playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
					playerImage.RenderTransform = new ScaleTransform(1.0, -1.0);
				}
				else
				{
					playerImage.RenderTransform = Transform.Identity;
				}
			}
			else if (gravityReversed)
			{
				playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
				playerImage.RenderTransform = new ScaleTransform(1.0, -1.0);
			}
			else
			{
				playerImage.RenderTransform = Transform.Identity;
			}
		}
		catch
		{
		}
	}

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

	private bool IsXDownAsync()
	{
		if (pfSimulating || pathfinderEnabled)
		{
			return false;
		}
		try
		{
			IntPtr foregroundWindow = GetForegroundWindow();
			if (foregroundWindow == IntPtr.Zero)
			{
				return false;
			}
			GetWindowThreadProcessId(foregroundWindow, out var processId);
			if (processId != currentProcessId)
			{
				return false;
			}
		}
		catch
		{
			return false;
		}
		return (GetAsyncKeyState(88) & 0x8000) != 0 || (GetAsyncKeyState(38) & 0x8000) != 0 || (GetAsyncKeyState(32) & 0x8000) != 0;
	}

	private double CalculateMusicTimeToPosition(int targetX_px)
	{
		try
		{
			int num = targetX_px + playerVisualWidth / 2;
			int num2 = 708;
			int num3 = 0;
			double num4 = 0.0;
			List<(int, int)> list = new List<(int, int)>();
			if (sprites != null && spriteAnchors != null)
			{
				for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
				{
					int num5 = nonEmptySpriteIndices[i];
					int num6 = sprites[num5];
					if (num6 != -1 && speedPortalMap.ContainsKey(num6))
					{
						int num7;
						if (spriteAnchors.TryGetValue(num5, out (int, int) value))
						{
							(num7, _) = value;
						}
						else
						{
							num7 = num5 % mapWidth;
						}
						int num8 = num7;
						int num9 = num8 * 16 + 8;
						if (num9 <= num)
						{
							list.Add((num9, speedPortalMap[num6]));
						}
					}
				}
			}
			list.Sort(((int x, int speed) a, (int x, int speed) b) => a.x.CompareTo(b.x));
			foreach (var item in list)
			{
				int num10 = item.Item1 - num3;
				if (num10 > 0)
				{
					double num11 = (double)num2 / 256.0 * 60.0;
					if (num11 > 0.0)
					{
						num4 += (double)num10 / num11;
					}
				}
				num2 = item.Item2;
				(num3, _) = item;
			}
			int num12 = num - num3;
			if (num12 > 0)
			{
				double num13 = (double)num2 / 256.0 * 60.0;
				if (num13 > 0.0)
				{
					num4 += (double)num12 / num13;
				}
			}
			return num4;
		}
		catch
		{
			return 0.0;
		}
	}

	private void ApplyColorTriggersUpToPosition(int targetX_px)
	{
		try
		{
			if (sprites == null || spriteAnchors == null)
			{
				return;
			}
			int? num = null;
			int? num2 = null;
			int? num3 = null;
			int? num4 = null;
			int? num5 = null;
			int? num6 = null;
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num7 = nonEmptySpriteIndices[i];
				int num8 = sprites[num7];
				if (num8 == -1)
				{
					continue;
				}
				int num9;
				if (spriteAnchors.TryGetValue(num7, out (int, int) value))
				{
					(num9, _) = value;
				}
				else
				{
					num9 = num7 % mapWidth;
				}
				int num10 = num9;
				int num11 = num10 * 16 + 8;
				if (num11 <= targetX_px)
				{
					if (IsBackgroundTrigger(num8))
					{
						num = num7;
						num4 = num8;
					}
					else if (IsTileTrigger(num8))
					{
						num2 = num7;
						num5 = num8;
					}
					else if (IsGroundTrigger(num8))
					{
						num3 = num7;
						num6 = num8;
					}
				}
			}
			if (num.HasValue && num4.HasValue)
			{
				Color color = ColorFromTrigger(num4.Value);
				backgroundTint = color;
				backgroundForceSolidBlack = num4.Value == 143;
				processedColorTriggers.Add(num.Value);
			}
			if (num2.HasValue && num5.HasValue)
			{
				Color color2 = ColorFromTrigger(num5.Value);
				tileTint = color2;
				processedColorTriggers.Add(num2.Value);
			}
			if (num3.HasValue && num6.HasValue)
			{
				Color color3 = ColorFromTrigger(num6.Value);
				if (num6.Value == 207)
				{
					groundTint = Color.FromArgb(byte.MaxValue, 0, 0, 0);
				}
				else
				{
					groundTint = color3;
				}
				processedColorTriggers.Add(num3.Value);
			}
		}
		catch
		{
		}
	}

	private int GetEditorAnimationFrameValue()
	{
		try
		{
			if (base.Owner is MainWindow { EditorPreviewMode: not false, EditorAnimationFrame: var editorAnimationFrame })
			{
				return editorAnimationFrame;
			}
		}
		catch
		{
		}
		return animationFrame;
	}

	private void WriteTempLog(string message)
	{
	}

	private void LogBallEvent(string evt)
	{
	}

	public void StartSimulation()
	{
		try
		{
			if (simTimer != null)
			{
				return;
			}
			simStopwatch.Restart();
			simLastMs = simStopwatch.Elapsed.TotalMilliseconds;
			simAccumulatedMs = 0.0;
			simTimer = new Timer(delegate
			{
				try
				{
					TimerSimulationLoop();
				}
				catch
				{
				}
				try
				{
					simTimer?.Change(10, -1);
				}
				catch
				{
				}
			}, null, 0, -1);
			try
			{
				if (enableSimulatorDebugLogging)
				{
					string contents = DateTime.UtcNow.ToString("o") + " SIM_LOG_START\n";
					Console.WriteLine("SIM_DEBUG_PATH: " + simDebugFilePath);
					File.AppendAllText(simDebugFilePath, contents);
					simDebugLoggedFirstFrame = true;
				}
			}
			catch
			{
			}
			try
			{
				if (base.Owner is MainWindow mainWindow)
				{
					try
					{
						mainWindow.ClearSimulatorPathOnly();
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			try
			{
				recordedPlayerPath.Clear();
			}
			catch
			{
			}
			try
			{
				recordedPlayer2Path.Clear();
				_prevDualActiveForP2Path = false;
			}
			catch
			{
			}
			try
			{
				camModeActive = base.Owner is MainWindow && MainWindow.Option_CamMode;
				if (camModeActive)
				{
					physicsEnabled = false;
					jumpedOnce = false;
					try
					{
						if (SettingsPanel != null)
						{
							SettingsPanel.Visibility = Visibility.Collapsed;
						}
					}
					catch
					{
					}
				}
				else
				{
					physicsEnabled = true;
					jumpedOnce = true;
					try
					{
						if (SettingsPanel != null)
						{
							SettingsPanel.Visibility = Visibility.Visible;
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			try
			{
				ShowTileHitboxes = base.Owner is MainWindow && MainWindow.Option_ShowTileHitboxes;
			}
			catch
			{
			}
			try
			{
				ShowSpriteHitboxes = base.Owner is MainWindow && MainWindow.Option_ShowSimulatorSpriteHitboxes;
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	public void StopSimulation()
	{
		try
		{
			simTimer?.Dispose();
		}
		catch
		{
		}
		simTimer = null;
		try
		{
			simStopwatch.Stop();
		}
		catch
		{
		}
	}

	protected override void OnClosed(EventArgs e)
	{
		try
		{
			base.OnClosed(e);
			try
			{
				if (base.Owner is MainWindow mainWindow)
				{
					List<(int, int)> path = new List<(int, int)>(recordedPlayerPath);
					List<(int, int)> path2 = new List<(int, int)>(recordedPlayer2Path);
					mainWindow.ShowPlayerPathsFromSimulator(path, path2, dual);
				}
			}
			catch
			{
			}
		}
		catch
		{
			base.OnClosed(e);
		}
	}

	private void TimerSimulationLoop()
	{
		if (restartInProgress || windowClosed)
		{
			return;
		}
		try
		{
			double totalMilliseconds = simStopwatch.Elapsed.TotalMilliseconds;
			double num = Math.Max(0.0, totalMilliseconds - simLastMs);
			if (simLastMs <= 0.0)
			{
				num = 0.0;
			}
			simLastMs = totalMilliseconds;
			simAccumulatedMs += num * simTimeScale;
			int num2 = 0;
			pfTickGeneration++;
			while (simAccumulatedMs >= 16.666666666666668)
			{
				num2++;
				if (num2 > 1)
				{
					simAccumulatedMs = 0.0;
					break;
				}
				try
				{
					SimulateNumericStep();
				}
				catch
				{
				}
				simAccumulatedMs -= 16.666666666666668;
				try
				{
					bool flag = currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8 || currentGameMode == 9 || nocamlockforced;
					if (physicsEnabled && jumpedOnce && !paused)
					{
						if ((!dual || twoplayer) && flag)
						{
							int num3 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
							int num4 = -(num3 * 16) << 8;
							int num5 = playerY_fixed - cameraY_fixed;
							if (num5 < 16384)
							{
								int num6 = 16384 - num5;
								cameraY_fixed -= num6;
								if (cameraY_fixed < num4)
								{
									cameraY_fixed = num4;
								}
							}
							else if (num5 >> 8 >= 160)
							{
								int num7 = num5 - 40960;
								int num8 = Math.Max(0, (mapHeight - 15) * 16) << 8;
								cameraY_fixed += num7;
								if (cameraY_fixed > num8)
								{
									cameraY_fixed = num8;
								}
							}
						}
						else
						{
							int num9 = cameraY_fixed >> 8;
							int num10 = targetCameraY_fixed >> 8;
							if (num10 > num9)
							{
								cameraY_fixed += 512;
							}
							else if (num10 < num9)
							{
								cameraY_fixed -= 768;
								playerY_fixed -= 256;
							}
							int num11 = Math.Max(0, (mapHeight - 15) * 16) << 8;
							int num12 = -(((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0) * 16) << 8;
							if (cameraY_fixed < num12)
							{
								cameraY_fixed = num12;
							}
							if (cameraY_fixed > num11)
							{
								cameraY_fixed = num11;
							}
						}
					}
				}
				catch
				{
				}
				try
				{
					if (!physicsEnabled || !jumpedOnce || paused || deathTriggered || MainWindow.Option_NoDeath)
					{
						continue;
					}
					int num13 = playerY_fixed - cameraY_fixed;
					if (!wrapMode)
					{
						if (num13 >= 1536 && num13 <= 63744)
						{
							continue;
						}
						AppendSimDebug($"[DEATH] OOB {((num13 < 1536) ? "top" : "bottom")}: screenRelY=0x{num13:X4}");
						deathTriggered = true;
						paused = true;
						StopMusicAsync();
						try
						{
							base.Dispatcher?.BeginInvoke((Action)delegate
							{
								try
								{
									PauseOverlay.Visibility = Visibility.Collapsed;
								}
								catch
								{
								}
								if (base.Owner is MainWindow mainWindow)
								{
									try
									{
										mainWindow.PauseSimulatorPlayback();
									}
									catch
									{
									}
									try
									{
										mainWindow.AddDeathMarker(playerX_fixed >> 8, playerY_fixed >> 8);
									}
									catch
									{
									}
								}
							});
						}
						catch
						{
						}
						continue;
					}
					if (num13 < 1536)
					{
						playerY_fixed = cameraY_fixed + 63744;
						AppendSimDebug($"[WRAP] top->bottom: newY=0x{playerY_fixed:X4}");
					}
					else if (num13 > 63744)
					{
						playerY_fixed = cameraY_fixed + 1536;
						AppendSimDebug($"[WRAP] bottom->top: newY=0x{playerY_fixed:X4}");
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	public SimulatorWindow(int[] tiles, int[] sprites, int mapWidth, int mapHeight, ImageSource?[]? tileImages, ImageSource?[]? tileTonedImages, ImageSource?[]? spriteImages, Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets, Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors, Color backgroundTint, Color groundTint, Color tileTint, Color playerTint, bool playerTintEnabled, int gridRenderShiftYPx, bool forcePreviewMode = true, bool hideColorTriggers = false, Dictionary<int, ImageSource?>? previewSpriteMap = null, Dictionary<int, ImageSource?[]>? animationFrames = null, ImageSource?[]? sawFrame1TilesTinted = null, ImageSource?[]? sawFrame2TilesTinted = null, ImageSource?[]? smallSawFrame1TilesTinted = null, ImageSource?[]? smallSawFrame2TilesTinted = null, ImageSource?[]? largeSawFrame1TilesTinted = null, ImageSource?[]? largeSawFrame2TilesTinted = null, ImageSource? parallaxBitmap = null, ImageSource?[]? parallaxImages = null, ImageSource?[]? parallaxTonedImages = null, double parallaxX = 1.0, double parallaxY = 1.0, bool parallaxRepeatX = true, bool parallaxRepeatY = true, bool hasParallaxLayer = false, ImageSource?[]? groundImages = null, ImageSource?[]? groundTonedImages = null, double groundOffsetY = 0.0, bool groundRepeatX = true, bool hasGroundLayer = false, int groundTileRows = 0, int? startingBackgroundColorCode = null, int? startingGroundColorCode = null, int simulatorScale = 1, int? maxFallSpeed = null, int startingGameMode = 0)
	{
		InitializeComponent();
		simTimeScale = 1.0;
		Interlocked.Exchange(ref ballToggleRequested, 0);
		try
		{
			baseWindowTitle = base.Title ?? "Simulator";
		}
		catch
		{
			baseWindowTitle = "Simulator";
		}
		try
		{
			PauseOverlay.Visibility = ((!paused) ? Visibility.Collapsed : Visibility.Visible);
		}
		catch
		{
		}
		this.tiles = tiles.ToArray();
		this.sprites = sprites.ToArray();
		List<int> list = new List<int>();
		for (int i = 0; i < this.sprites.Length; i++)
		{
			if (this.sprites[i] >= 0)
			{
				list.Add(i);
			}
		}
		nonEmptySpriteIndices = list.ToArray();
		this.mapWidth = mapWidth;
		this.mapHeight = mapHeight;
		this.tileImages = tileImages;
		this.tileTonedImages = tileTonedImages;
		this.spriteImages = spriteImages;
		this.spritePixelOffsets = new Dictionary<int, (int, int)>(spritePixelOffsets);
		this.spriteAnchors = new Dictionary<int, (int, int)>(spriteAnchors);
		sawFrame1TilesOrig = ((sawFrame1TilesTinted != null) ? ((ImageSource[])sawFrame1TilesTinted.Clone()) : null);
		sawFrame2TilesOrig = ((sawFrame2TilesTinted != null) ? ((ImageSource[])sawFrame2TilesTinted.Clone()) : null);
		smallSawFrame1TilesOrig = ((smallSawFrame1TilesTinted != null) ? ((ImageSource[])smallSawFrame1TilesTinted.Clone()) : null);
		smallSawFrame2TilesOrig = ((smallSawFrame2TilesTinted != null) ? ((ImageSource[])smallSawFrame2TilesTinted.Clone()) : null);
		largeSawFrame1TilesOrig = ((largeSawFrame1TilesTinted != null) ? ((ImageSource[])largeSawFrame1TilesTinted.Clone()) : null);
		largeSawFrame2TilesOrig = ((largeSawFrame2TilesTinted != null) ? ((ImageSource[])largeSawFrame2TilesTinted.Clone()) : null);
		if (backgroundTint.A == byte.MaxValue && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
		{
			this.sawFrame1TilesTinted = CreateSolidBlackImages(sawFrame1TilesOrig);
			this.sawFrame2TilesTinted = CreateSolidBlackImages(sawFrame2TilesOrig);
			this.smallSawFrame1TilesTinted = CreateSolidBlackImages(smallSawFrame1TilesOrig);
			this.smallSawFrame2TilesTinted = CreateSolidBlackImages(smallSawFrame2TilesOrig);
			this.largeSawFrame1TilesTinted = CreateSolidBlackImages(largeSawFrame1TilesOrig);
			this.largeSawFrame2TilesTinted = CreateSolidBlackImages(largeSawFrame2TilesOrig);
		}
		else
		{
			this.sawFrame1TilesTinted = CreateHslShiftedImages(sawFrame1TilesOrig, backgroundTint, tileTint);
			this.sawFrame2TilesTinted = CreateHslShiftedImages(sawFrame2TilesOrig, backgroundTint, tileTint);
			this.smallSawFrame1TilesTinted = CreateHslShiftedImages(smallSawFrame1TilesOrig, backgroundTint, tileTint);
			this.smallSawFrame2TilesTinted = CreateHslShiftedImages(smallSawFrame2TilesOrig, backgroundTint, tileTint);
			this.largeSawFrame1TilesTinted = CreateHslShiftedImages(largeSawFrame1TilesOrig, backgroundTint, tileTint);
			this.largeSawFrame2TilesTinted = CreateHslShiftedImages(largeSawFrame2TilesOrig, backgroundTint, tileTint);
		}
		try
		{
			currentGameMode = startingGameMode;
			_levelStartGameMode = startingGameMode;
		}
		catch
		{
			currentGameMode = 0;
		}
		try
		{
			UpdatePlayerImageForMode();
		}
		catch
		{
		}
		try
		{
			if (this.spritePixelOffsets.ContainsKey(43))
			{
				(int, int) tuple = this.spritePixelOffsets[43];
				this.spritePixelOffsets[43] = (tuple.Item1, tuple.Item2 - 8);
			}
			else
			{
				this.spritePixelOffsets[43] = (0, -8);
			}
			if (this.spritePixelOffsets.ContainsKey(44))
			{
				(int, int) tuple2 = this.spritePixelOffsets[44];
				this.spritePixelOffsets[44] = (tuple2.Item1, tuple2.Item2 - 8);
			}
			else
			{
				this.spritePixelOffsets[44] = (0, -8);
			}
		}
		catch
		{
		}
		try
		{
			if (maxFallSpeed.HasValue)
			{
				int value = maxFallSpeed.Value;
				if (value >= 256)
				{
					CUBE_MAX_FALLSPEED = value;
				}
				else
				{
					CUBE_MAX_FALLSPEED = value << 8;
				}
			}
			UpdateEffectiveGravity();
		}
		catch
		{
		}
		this.backgroundTint = backgroundTint;
		this.groundTint = groundTint;
		this.tileTint = tileTint;
		this.playerTint = playerTint;
		this.playerTintEnabled = playerTintEnabled;
		this.gridRenderShiftYPx = gridRenderShiftYPx;
		this.forcePreviewMode = forcePreviewMode;
		this.hideColorTriggers = hideColorTriggers;
		this.previewSpriteMap = previewSpriteMap ?? new Dictionary<int, ImageSource>();
		this.animationFrames = animationFrames ?? new Dictionary<int, ImageSource[]>();
		try
		{
			if (this.animationFrames != null)
			{
				if (!this.animationFrames.ContainsKey(253) && this.animationFrames.ContainsKey(13))
				{
					this.animationFrames[253] = this.animationFrames[13];
				}
				if (!this.animationFrames.ContainsKey(254) && this.animationFrames.ContainsKey(14))
				{
					this.animationFrames[254] = this.animationFrames[14];
				}
			}
		}
		catch
		{
		}
		this.sawFrame1TilesTinted = sawFrame1TilesTinted;
		this.sawFrame2TilesTinted = sawFrame2TilesTinted;
		this.smallSawFrame1TilesTinted = smallSawFrame1TilesTinted;
		this.smallSawFrame2TilesTinted = smallSawFrame2TilesTinted;
		this.largeSawFrame1TilesTinted = largeSawFrame1TilesTinted;
		this.largeSawFrame2TilesTinted = largeSawFrame2TilesTinted;
		this.parallaxBitmap = parallaxBitmap;
		this.parallaxImages = parallaxImages;
		this.parallaxTonedImages = parallaxTonedImages;
		this.parallaxX = parallaxX;
		this.parallaxY = parallaxY;
		this.parallaxRepeatX = parallaxRepeatX;
		this.parallaxRepeatY = parallaxRepeatY;
		this.hasParallaxLayer = hasParallaxLayer;
		this.groundImages = groundImages;
		this.groundTonedImages = groundTonedImages;
		this.groundOffsetY = groundOffsetY;
		this.groundRepeatX = groundRepeatX;
		this.hasGroundLayer = hasGroundLayer;
		this.groundTileRows = groundTileRows;
		_sim_nesCoordOffset = (57 - mapHeight + ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0)) * 16;
		try
		{
			if (startingBackgroundColorCode.HasValue)
			{
				int value2 = startingBackgroundColorCode.Value;
				int trigger = MapStartingCodeToTrigger(value2, isGround: false);
				try
				{
					int firstTrigger = trigger + 1;
					if ((trigger & 0xF) == 12)
					{
						firstTrigger = trigger + 4;
					}
					try
					{
						base.Dispatcher.BeginInvoke((Action)delegate
						{
							try
							{
								pendingBgIdx = 0;
								pendingBgSid = firstTrigger;
								pendingTintChange = true;
								pendingTintChangeIsStartup = false;
								ApplyPendingTints();
							}
							catch
							{
							}
						}, DispatcherPriority.Normal);
					}
					catch
					{
					}
					try
					{
						base.Dispatcher.BeginInvoke((Action)delegate
						{
							try
							{
								pendingBgIdx = 0;
								pendingBgSid = trigger;
								pendingTintChange = true;
								pendingTintChangeIsStartup = false;
								ApplyPendingTints();
							}
							catch
							{
							}
						}, DispatcherPriority.ApplicationIdle);
					}
					catch
					{
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		try
		{
			if (startingGroundColorCode.HasValue)
			{
				int value3 = startingGroundColorCode.Value;
				int num = MapStartingCodeToTrigger(value3, isGround: true);
				try
				{
					pendingGroundIdx = 0;
					pendingGroundSid = num;
					pendingTintChange = true;
					pendingTintChangeIsStartup = true;
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		try
		{
			if (pendingTintChange)
			{
				try
				{
					ApplyPendingTints();
				}
				catch
				{
				}
				try
				{
					EnsureInitialRender();
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		try
		{
			if (!this.hasParallaxLayer || this.parallaxImages == null || this.parallaxImages.Length == 0)
			{
				BitmapImage bitmapImage = LoadCachedResourceImage("Assets.parallax.bmp") ?? LoadCachedResourceImage("parallax.bmp");
				if (bitmapImage != null)
				{
					ImageSource[] array = new ImageSource[1] { bitmapImage };
					this.parallaxImages = array;
					this.hasParallaxLayer = true;
				}
				if (this.groundImages == null || this.groundImages.Length == 0)
				{
					WriteableBitmap writeableBitmap = new WriteableBitmap(16, 16, 96.0, 96.0, PixelFormats.Pbgra32, null);
					try
					{
						writeableBitmap.Lock();
						writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, 16, 16));
					}
					finally
					{
						try
						{
							writeableBitmap.Unlock();
						}
						catch
						{
						}
					}
					this.groundImages = new ImageSource[1] { writeableBitmap };
					this.groundTonedImages = null;
					this.hasGroundLayer = true;
				}
			}
		}
		catch
		{
		}
		try
		{
			if (this.animationFrames != null && this.previewSpriteMap != null)
			{
				foreach (int decorationSpriteId in decorationSpriteIds)
				{
					if (this.animationFrames.ContainsKey(decorationSpriteId))
					{
						continue;
					}
					ImageSource imageSource = null;
					if (this.previewSpriteMap.TryGetValue(decorationSpriteId, out ImageSource value4) && value4 != null)
					{
						imageSource = value4;
					}
					else if (this.spriteImages != null && decorationSpriteId >= 0 && decorationSpriteId < this.spriteImages.Length && this.spriteImages[decorationSpriteId] != null)
					{
						imageSource = this.spriteImages[decorationSpriteId];
					}
					if (imageSource != null)
					{
						ImageSource[] array2 = CreateTwoFramePulse(imageSource);
						if (array2 != null)
						{
							this.animationFrames[decorationSpriteId] = array2;
						}
					}
				}
				int[] array3 = new int[12]
				{
					69, 70, 76, 77, 80, 81, 91, 92, 93, 94,
					89, 90
				};
				int[] array4 = array3;
				foreach (int num3 in array4)
				{
					if (this.animationFrames.ContainsKey(num3))
					{
						continue;
					}
					ImageSource imageSource2 = null;
					if (this.previewSpriteMap.TryGetValue(num3, out ImageSource value5) && value5 != null)
					{
						imageSource2 = value5;
					}
					else if (this.spriteImages != null && num3 >= 0 && num3 < this.spriteImages.Length && this.spriteImages[num3] != null)
					{
						imageSource2 = this.spriteImages[num3];
					}
					if (imageSource2 != null)
					{
						ImageSource[] array5 = CreateTwoFramePulse(imageSource2);
						if (array5 != null)
						{
							this.animationFrames[num3] = array5;
						}
					}
				}
			}
		}
		catch
		{
		}
		int num4 = Math.Max(0, (mapHeight - 15) * 16) << 8;
		if (!hasAppliedStartPos)
		{
			cameraY_fixed = num4;
		}
		if (currentGameMode != 0 && currentGameMode != 4 && currentGameMode != 8 && currentGameMode != 9 && currentGameMode != 11)
		{
			targetCameraY_fixed = cameraY_fixed;
		}
		timer = new DispatcherTimer(DispatcherPriority.Render);
		renderStopwatch.Start();
		uiAnimLastMs = renderStopwatch.Elapsed.TotalMilliseconds;
		CompositionTarget.Rendering += CompositionTarget_Rendering;
		base.PreviewKeyDown += SimulatorWindow_KeyDown;
		base.KeyUp += SimulatorWindow_KeyUp;
		base.MouseMove += SimulatorWindow_MouseMove;
		base.Closing += SimulatorWindow_Closing;
		base.Closed += delegate
		{
			windowClosed = true;
			try
			{
				timer.Stop();
			}
			catch
			{
			}
			try
			{
				if (simTimer != null)
				{
					simTimer.Dispose();
					simTimer = null;
				}
			}
			catch
			{
			}
			try
			{
				Thread.Sleep(20);
			}
			catch
			{
			}
			try
			{
				CompositionTarget.Rendering -= CompositionTarget_Rendering;
			}
			catch
			{
			}
		};
		RenderCanvas.Width = 256.0;
		RenderCanvas.Height = 240.0;
		try
		{
			simulatorScale = Math.Max(1, Math.Min(4, simulatorScale));
			ScaleTransform layoutTransform = new ScaleTransform(simulatorScale, simulatorScale);
			RenderCanvas.LayoutTransform = layoutTransform;
			try
			{
				PauseOverlay.LayoutTransform = layoutTransform;
			}
			catch
			{
			}
			try
			{
				LevelCompleteOverlay.LayoutTransform = layoutTransform;
			}
			catch
			{
			}
			double num5 = 32.0;
			double num6 = 110.0;
			try
			{
				base.Width = (double)(256 * simulatorScale) + num5;
			}
			catch
			{
			}
			try
			{
				base.Height = (double)(240 * simulatorScale) + num6;
			}
			catch
			{
			}
		}
		catch
		{
		}
		try
		{
			RenderOptions.SetBitmapScalingMode(RenderCanvas, BitmapScalingMode.NearestNeighbor);
		}
		catch
		{
		}
		try
		{
			UpdateSimTitle();
		}
		catch
		{
		}
		try
		{
			playerImage = new Image
			{
				Stretch = Stretch.None
			};
			RenderOptions.SetBitmapScalingMode(playerImage, BitmapScalingMode.NearestNeighbor);
			Panel.SetZIndex(playerImage, 1000);
			RenderCanvas.Children.Add(playerImage);
			for (int num7 = 0; num7 < 3; num7++)
			{
				trailGhosts[num7] = new Image
				{
					Stretch = Stretch.None,
					Opacity = 0.5
				};
				RenderOptions.SetBitmapScalingMode(trailGhosts[num7], BitmapScalingMode.NearestNeighbor);
				Panel.SetZIndex(trailGhosts[num7], 997 - num7);
				trailGhosts[num7].Visibility = Visibility.Collapsed;
				RenderCanvas.Children.Add(trailGhosts[num7]);
			}
			try
			{
				bool flag = false;
				string path = AppDomain.CurrentDomain.BaseDirectory ?? ".";
				string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(path, "..\\..\\..\\..\\cube.png"));
				if (File.Exists(fullPath))
				{
					BitmapImage bitmapImage2 = new BitmapImage();
					bitmapImage2.BeginInit();
					bitmapImage2.UriSource = new Uri(fullPath);
					bitmapImage2.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage2.EndInit();
					bitmapImage2.Freeze();
					playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage2) ?? bitmapImage2;
					playerImage.Width = bitmapImage2.PixelWidth;
					playerImage.Height = bitmapImage2.PixelHeight;
					flag = true;
				}
				if (!flag)
				{
					try
					{
						BitmapImage bitmapImage3 = LoadCachedResourceImage("cube.png");
						if (bitmapImage3 != null)
						{
							playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage3) ?? bitmapImage3;
							playerImage.Width = bitmapImage3.PixelWidth;
							playerImage.Height = bitmapImage3.PixelHeight;
							flag = true;
						}
					}
					catch
					{
					}
				}
				if (!flag)
				{
					playerRect = new Rectangle
					{
						Width = 16.0,
						Height = 16.0,
						Fill = new SolidColorBrush(Colors.Magenta)
					};
					Panel.SetZIndex(playerRect, 1000);
					RenderCanvas.Children.Add(playerRect);
					playerRect.Visibility = Visibility.Visible;
					playerImage.Visibility = Visibility.Collapsed;
				}
				else
				{
					playerImage.Visibility = Visibility.Visible;
					if (playerRect != null)
					{
						playerRect.Visibility = Visibility.Collapsed;
					}
				}
			}
			catch
			{
				playerRect = new Rectangle
				{
					Width = 16.0,
					Height = 16.0,
					Fill = new SolidColorBrush(Colors.Magenta)
				};
				Panel.SetZIndex(playerRect, 1000);
				RenderCanvas.Children.Add(playerRect);
				playerRect.Visibility = Visibility.Visible;
				if (playerImage != null)
				{
					playerImage.Visibility = Visibility.Collapsed;
				}
			}
		}
		catch
		{
		}
		try
		{
			player2Image = new Image
			{
				Stretch = Stretch.None
			};
			RenderOptions.SetBitmapScalingMode(player2Image, BitmapScalingMode.NearestNeighbor);
			Panel.SetZIndex(player2Image, 999);
			RenderCanvas.Children.Add(player2Image);
			if (playerImage != null && playerImage.Source != null)
			{
				player2Image.Source = playerImage.Source;
				player2Image.Width = playerImage.Width;
				player2Image.Height = playerImage.Height;
				player2Image.Visibility = Visibility.Collapsed;
			}
			else
			{
				player2Rect = new Rectangle
				{
					Width = 16.0,
					Height = 16.0,
					Fill = new SolidColorBrush(Colors.Cyan)
				};
				Panel.SetZIndex(player2Rect, 999);
				RenderCanvas.Children.Add(player2Rect);
				player2Rect.Visibility = Visibility.Collapsed;
				player2Image.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
		}
		try
		{
			UpdatePlayerImageForMode();
		}
		catch
		{
		}
		try
		{
			UpdatePlayerImageForMode();
		}
		catch
		{
		}
		try
		{
			playerVisualWidth = 16;
			playerVisualHeight = 16;
			if (playerImage != null && playerImage.Source != null && playerImage.Width > 0.0)
			{
				playerVisualWidth = (int)Math.Ceiling(playerImage.Width);
				playerVisualHeight = (int)Math.Ceiling(playerImage.Height);
			}
			else if (playerRect != null)
			{
				playerVisualWidth = (int)Math.Ceiling(playerRect.Width);
				playerVisualHeight = (int)Math.Ceiling(playerRect.Height);
			}
			playerX_fixed = 0;
			interactionScreenOffset_px = -1;
			invincibleCounter = 8;
			try
			{
				int num8 = 0;
				try
				{
					if (hasGroundLayer && groundTileRows > 0)
					{
						num8 = Math.Min(3, groundTileRows);
					}
				}
				catch
				{
					num8 = 0;
				}
				int num9 = (mapHeight - num8) * 16;
				int num10 = 15;
				playerY_fixed = Math.Max(0, num9 - num10) << 8;
				int num11 = Math.Max(0, mapHeight * 16 - playerVisualHeight) << 8;
				if (playerY_fixed > num11)
				{
					playerY_fixed = num11;
				}
			}
			catch
			{
				playerY_fixed = 0;
			}
			try
			{
				ResetOrbSystem();
			}
			catch
			{
			}
			try
			{
				ResetBluePadSystem();
			}
			catch
			{
			}
		}
		catch
		{
		}
		base.Loaded += delegate
		{
			try
			{
				Focus();
				Keyboard.Focus(this);
			}
			catch
			{
			}
			try
			{
				UpdateGameModeDisplay();
			}
			catch
			{
			}
			try
			{
				UpdateSpeedDisplay();
			}
			catch
			{
			}
			try
			{
				bool flag2 = base.Owner is MainWindow { PrecomputedPathfinderInputs: not null } mainWindow && mainWindow.PrecomputedPathfinderInputs.Count > 0;
				bool flag3 = (_pathfinderUserPref.HasValue ? _pathfinderUserPref.Value : flag2);
				if (flag3 && flag2)
				{
					pathfinderEnabled = true;
					PF_LoadPrecomputedInputs();
					try
					{
						PathfinderCheckBox.IsChecked = true;
					}
					catch
					{
					}
					AppendSimDebug($"[PATHFINDER] Auto-enabled with {((MainWindow)base.Owner).PrecomputedPathfinderInputs.Count} inputs");
					return;
				}
				pathfinderEnabled = false;
				try
				{
					PathfinderCheckBox.IsChecked = false;
				}
				catch
				{
				}
			}
			catch
			{
			}
		};
		try
		{
			bgRectPersistent = new Rectangle
			{
				Width = RenderCanvas.Width,
				Height = RenderCanvas.Height,
				Fill = new SolidColorBrush(backgroundTint)
			};
			Canvas.SetLeft(bgRectPersistent, 0.0);
			Canvas.SetTop(bgRectPersistent, 0.0);
			RenderCanvas.Children.Add(bgRectPersistent);
			tileLayerImage = new Image
			{
				Width = 272.0,
				Height = 256.0,
				Stretch = Stretch.None
			};
			RenderOptions.SetBitmapScalingMode(tileLayerImage, BitmapScalingMode.NearestNeighbor);
			tileLayerImage.SnapsToDevicePixels = true;
			RenderOptions.SetEdgeMode(tileLayerImage, EdgeMode.Aliased);
			RenderCanvas.Children.Add(tileLayerImage);
			try
			{
				Panel.SetZIndex(tileLayerImage, 0);
			}
			catch
			{
			}
			spriteLayerImage = new Image
			{
				Width = 272.0,
				Height = 256.0,
				Stretch = Stretch.None,
				IsHitTestVisible = false
			};
			RenderOptions.SetBitmapScalingMode(spriteLayerImage, BitmapScalingMode.NearestNeighbor);
			spriteLayerImage.SnapsToDevicePixels = true;
			RenderOptions.SetEdgeMode(spriteLayerImage, EdgeMode.Aliased);
			RenderCanvas.Children.Add(spriteLayerImage);
			try
			{
				Panel.SetZIndex(spriteLayerImage, 1);
			}
			catch
			{
			}
			groundRectPersistent = new Rectangle
			{
				Width = RenderCanvas.Width,
				Height = 16 * Math.Min(15, 2),
				Fill = new SolidColorBrush(groundTint)
				{
					Opacity = 0.25
				},
				Visibility = Visibility.Collapsed
			};
			Canvas.SetLeft(groundRectPersistent, 0.0);
			Canvas.SetTop(groundRectPersistent, 240 - 16 * Math.Min(15, 2));
			RenderCanvas.Children.Add(groundRectPersistent);
		}
		catch
		{
		}
		try
		{
			try
			{
				pendingTileIdx = 0;
				pendingTileSid = 176;
				pendingTintChange = true;
				pendingTintChangeIsStartup = true;
				try
				{
					ApplyPendingTints();
				}
				catch
				{
				}
				try
				{
					EnsureInitialRender();
				}
				catch
				{
				}
			}
			catch
			{
			}
			try
			{
				UpdateTonedImagesForTileTint(tileTint, startupTintApplied ? new Color?(Color.FromArgb(0, 0, 0, 0)) : ((Color?)null));
			}
			catch
			{
			}
			try
			{
				parallaxTonedImages = ((backgroundTint.A != byte.MaxValue || backgroundTint.R != 0 || backgroundTint.G != 0 || backgroundTint.B != 0) ? CreateHueShiftedImages(parallaxImages, backgroundTint) : CreateBlackMaskedImages(parallaxImages));
			}
			catch
			{
				parallaxTonedImages = parallaxImages;
			}
			try
			{
				if (groundTint.A == byte.MaxValue && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
				{
					try
					{
						groundTonedImages = CreateBlackMaskedExceptColorArray(groundImages, playerPlaceholderGreen, Color.FromArgb(0, 0, 0, 0), recolorOutline: false);
					}
					catch
					{
						groundTonedImages = CreateTwoToneTileImages(groundImages, Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));
					}
				}
				else
				{
					groundTonedImages = CreateHueShiftedImages(groundImages, groundTint, tileTint);
				}
			}
			catch
			{
				groundTonedImages = groundImages;
			}
			try
			{
				if (tileTint.A > 0 && groundTonedImages != null)
				{
					ImageSource[] array6 = CreateOutlineTintedTileImages(groundTonedImages, tileTint);
					if (array6 != null)
					{
						groundTonedImages = array6;
					}
				}
			}
			catch
			{
			}
			try
			{
				if (groundTint.A == byte.MaxValue && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
				{
					try
					{
						tileTonedImages = CreateTwoToneTileImages(tileImages, Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			tileLayerCache = null;
			try
			{
				groundTintedTileCache.Clear();
			}
			catch
			{
			}
			try
			{
				AppendSimDebug($"Constructor: parallaxToned={((parallaxTonedImages != null) ? parallaxTonedImages.Length : 0)} groundToned={((groundTonedImages != null) ? groundTonedImages.Length : 0)} tileToned={((tileTonedImages != null) ? tileTonedImages.Length : 0)}");
			}
			catch
			{
			}
		}
		catch
		{
		}
		try
		{
			ImageSource imageSource3 = null;
			if (parallaxBitmapToned != null)
			{
				imageSource3 = parallaxBitmapToned;
			}
			else if (parallaxBitmap != null)
			{
				imageSource3 = parallaxBitmap;
			}
			else if (parallaxTonedImages != null && parallaxTonedImages.Length != 0 && parallaxTonedImages[0] != null)
			{
				imageSource3 = parallaxTonedImages[0];
			}
			else if (parallaxImages != null && parallaxImages.Length != 0 && parallaxImages[0] != null)
			{
				imageSource3 = parallaxImages[0];
			}
			if (bgRectPersistent != null && imageSource3 is BitmapSource bitmapSource)
			{
				ImageSource image = App.EnsureUnfrozenForRender(imageSource3) ?? imageSource3;
				ImageBrush fill = new ImageBrush(image)
				{
					TileMode = TileMode.Tile,
					ViewportUnits = BrushMappingMode.Absolute,
					Viewport = new Rect(0.0, 0.0, Math.Max(1.0, bitmapSource.PixelWidth), Math.Max(1.0, bitmapSource.PixelHeight)),
					Stretch = Stretch.None
				};
				bgRectPersistent.Fill = fill;
			}
		}
		catch
		{
		}
		try
		{
			AppendSimDebug($"Constructor: hasParallaxLayer={this.hasParallaxLayer}, parallaxBitmap={this.parallaxBitmap != null}, parallaxImagesLen={((this.parallaxImages != null) ? this.parallaxImages.Length : 0)}, parallaxTonedLen={((this.parallaxTonedImages != null) ? this.parallaxTonedImages.Length : 0)}, hasGroundLayer={this.hasGroundLayer}, groundImagesLen={((this.groundImages != null) ? this.groundImages.Length : 0)}, backgroundTint={this.backgroundTint}");
		}
		catch
		{
		}
		try
		{
			EnableCubePhysics_Fresh();
		}
		catch
		{
		}
	}

	private int MapAnimatedTileIndex(int originalIndex)
	{
		if (originalIndex < 0)
		{
			return 0;
		}
		int num = originalIndex;
		switch (originalIndex)
		{
		case 223:
		case 227:
		case 252:
		case 254:
		case 255:
			num = 0;
			break;
		case 253:
			num = 38;
			break;
		}
		if (num >= 8 && num <= 11)
		{
			bool flag = GetEditorAnimationFrameValue() * 9 / 20 % 2 == 1;
			int num2 = num - 8;
			return flag ? (1004 + num2) : (1000 + num2);
		}
		if (num == 4 || num == 125 || num == 127)
		{
			bool flag2 = animationFrame * 9 / 20 % 2 == 1;
			int num3 = num switch
			{
				125 => 1, 
				4 => 0, 
				_ => 2, 
			};
			return flag2 ? (1013 + num3) : (1010 + num3);
		}
		if (num >= 116 && num <= 124)
		{
			bool flag3 = animationFrame * 9 / 20 % 2 == 1;
			int num4 = num - 116;
			return flag3 ? (1029 + num4) : (1020 + num4);
		}
		return originalIndex;
	}

	private async void SimulatorWindow_KeyDown(object sender, KeyEventArgs e)
	{
		if (playbackStartPending)
		{
			try
			{
				e.Handled = true;
				return;
			}
			catch
			{
				return;
			}
		}
		if (e.Key == Key.Up)
		{
			upHeld = true;
		}
		if (e.Key == Key.Down)
		{
			downHeld = true;
		}
		if (e.Key == Key.X || (!MainWindow.Option_CamMode && (e.Key == Key.Up || e.Key == Key.Space)))
		{
			try
			{
				if (!e.IsRepeat)
				{
					if (pathfinderEnabled)
					{
						AppendSimDebug("[KEYDOWN_X] IGNORED (pathfinder active)");
					}
					else
					{
						AppendSimDebug($"[KEYDOWN_X] Key={e.Key} IsRepeat={e.IsRepeat} CamMode={MainWindow.Option_CamMode} currentGameMode={currentGameMode}");
						lock (simLock)
						{
							int newCount = Interlocked.Increment(ref keyXPressedCount);
							AppendSimDebug($"[KEYDOWN_X] Incremented keyXPressedCount to {newCount}");
							if (currentGameMode == 2)
							{
								try
								{
									Interlocked.Exchange(ref ballToggleRequested, 1);
								}
								catch
								{
								}
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		if (e.Key == Key.Q)
		{
			AppendSimDebug("[Q_HANDLER] Q key detected");
			try
			{
				if (!e.IsRepeat)
				{
					AppendSimDebug("[Q_HANDLER] Not a repeat");
					lock (simLock)
					{
						AppendSimDebug("[Q_HANDLER] In lock");
						miniMode = !miniMode;
						currplayer_mini = (byte)(miniMode ? 1u : 0u);
						AppendSimDebug($"[Q_HANDLER] Set miniMode to {miniMode}, currplayer_mini to {currplayer_mini}");
						currplayer_table_idx = ((currplayer_gravity != 0) ? 1 : 0) | ((currplayer_mini != 0) ? 4 : 0);
						try
						{
							UpdatePlayerIconFlip();
						}
						catch
						{
							AppendSimDebug("[Q_HANDLER] UpdatePlayerIconFlip exception");
						}
						try
						{
							UpdatePlayerImageForMode();
						}
						catch
						{
							AppendSimDebug("[Q_HANDLER] UpdatePlayerImageForMode exception");
						}
						try
						{
							UpdatePlayerVisualSizeForMode();
						}
						catch
						{
							AppendSimDebug("[Q_HANDLER] UpdatePlayerVisualSizeForMode exception");
						}
						try
						{
							MiniCheckBox.IsChecked = miniMode;
						}
						catch
						{
							AppendSimDebug("[Q_HANDLER] MiniCheckBox update exception");
						}
						AppendSimDebug("[MINI] Mode now: " + (miniMode ? "MINI" : "NORMAL"));
						e.Handled = true;
					}
				}
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				AppendSimDebug("[Q_HANDLER] Exception: " + ex2.Message);
			}
		}
		if (e.Key == Key.W)
		{
			try
			{
				if (!e.IsRepeat)
				{
					lock (simLock)
					{
						AppendSimDebug("[INPUT] W key pressed - toggling gravity!");
						gravityReversed = !gravityReversed;
						gravityFlipped = gravityReversed;
						effectiveInvertedByW = gravityReversed;
						gravityFlippedThisFrame = true;
						wasZeroedByCollisionLastFrame = false;
						AppendSimDebug("[GRAVITY_FLIP_FLAG] Set gravityFlippedThisFrame=true for modes that need unsticking");
						currplayer_gravity = (byte)(gravityReversed ? 255u : 0u);
						currplayer_table_idx = ((currplayer_gravity != 0) ? 1 : 0) | ((currplayer_mini != 0) ? 4 : 0);
						UpdatePlayerIconFlip();
						try
						{
							UpdateEffectiveGravity();
						}
						catch
						{
						}
						onGround = false;
						groundStabilizeCounter = 0;
						try
						{
							if (currentGameMode == 2)
							{
								ballGoingDown = !gravityReversed;
							}
						}
						catch
						{
						}
						AppendSimDebug("[GRAVITY] Gravity now: " + (gravityReversed ? "INVERTED" : "NORMAL"));
					}
				}
			}
			catch
			{
			}
		}
		if (e.Key == Key.F12 && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.None)
		{
			try
			{
				showYVelocityOverlay = !showYVelocityOverlay;
				if (showYVelocityOverlay)
				{
					try
					{
						if (yVelTextBlock == null)
						{
							yVelTextBlock = new TextBlock();
							yVelTextBlock.Foreground = new SolidColorBrush(Colors.Yellow);
							yVelTextBlock.FontWeight = FontWeights.Bold;
							yVelTextBlock.FontSize = 14.0;
							yVelTextBlock.IsHitTestVisible = false;
							RenderCanvas.Children.Add(yVelTextBlock);
							try
							{
								Panel.SetZIndex(yVelTextBlock, 2000);
							}
							catch
							{
							}
						}
					}
					catch
					{
					}
				}
				else
				{
					try
					{
						if (yVelTextBlock != null)
						{
							yVelTextBlock.Visibility = Visibility.Collapsed;
						}
					}
					catch
					{
					}
				}
				try
				{
					RenderFrame();
				}
				catch
				{
				}
			}
			catch
			{
			}
		}
		if (e.Key == Key.F11 && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.None)
		{
			try
			{
				if (debugWindow == null || !debugWindow.IsLoaded)
				{
					debugWindow = new DebugInfoWindow();
					debugWindow.Owner = this;
					debugWindow.Left = base.Left + base.ActualWidth;
					debugWindow.Top = base.Top;
					debugWindow.Closed += delegate
					{
						debugWindow = null;
					};
					debugWindow.Show();
				}
				else
				{
					debugWindow.Close();
					debugWindow = null;
				}
			}
			catch
			{
			}
		}
		if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
		{
			try
			{
				simTimeScale = Math.Max(0.1, Math.Round((simTimeScale - 0.1) * 10.0) / 10.0);
				try
				{
					UpdateSimTitle();
				}
				catch
				{
				}
				try
				{
					Window owner = base.Owner;
					if (owner is MainWindow mw)
					{
						mw.SetSimulatorPlaybackRate(simTimeScale);
					}
				}
				catch
				{
				}
			}
			catch
			{
			}
		}
		if (e.Key == Key.OemPlus || e.Key == Key.Add)
		{
			try
			{
				simTimeScale = Math.Min(4.0, Math.Round((simTimeScale + 0.1) * 10.0) / 10.0);
				try
				{
					UpdateSimTitle();
				}
				catch
				{
				}
				try
				{
					Window owner = base.Owner;
					if (owner is MainWindow mw2)
					{
						mw2.SetSimulatorPlaybackRate(simTimeScale);
					}
				}
				catch
				{
				}
			}
			catch
			{
			}
		}
		if (e.Key == Key.W)
		{
			try
			{
				if (!e.IsRepeat)
				{
					lock (simLock)
					{
					}
				}
			}
			catch
			{
			}
		}
		if (e.Key == Key.F2)
		{
			try
			{
				if (!e.IsRepeat)
				{
					bool newState = (ShowTileHitboxes = !ShowTileHitboxes);
					try
					{
						MainWindow.Option_ShowTileHitboxes = newState;
						if (Application.Current != null)
						{
							try
							{
								Window owner = Application.Current.MainWindow;
								MainWindow mw3 = owner as MainWindow;
								if (mw3 != null && mw3.MenuOptionTileHitboxes != null)
								{
									mw3.MenuOptionTileHitboxes.IsChecked = newState;
								}
							}
							catch
							{
							}
							foreach (Window w2 in Application.Current.Windows)
							{
								try
								{
									if (w2 is SimulatorWindow sw2)
									{
										sw2.ShowTileHitboxes = newState;
									}
								}
								catch
								{
								}
							}
						}
					}
					catch
					{
					}
					try
					{
						MainWindow.ShowTransientInfo("Show Tile Hitboxes: " + (newState ? "ON" : "OFF"), this);
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
		if (e.Key == Key.F3)
		{
			try
			{
				if (!e.IsRepeat)
				{
					ShowSpriteHitboxes = !ShowSpriteHitboxes;
					try
					{
						MainWindow.Option_ShowSimulatorSpriteHitboxes = ShowSpriteHitboxes;
						if (Application.Current != null)
						{
							try
							{
								Window owner = Application.Current.MainWindow;
								MainWindow mw4 = owner as MainWindow;
								if (mw4 != null && mw4.MenuOptionShowSpriteHitboxes != null)
								{
									mw4.MenuOptionShowSpriteHitboxes.IsChecked = ShowSpriteHitboxes;
								}
							}
							catch
							{
							}
							foreach (Window w3 in Application.Current.Windows)
							{
								try
								{
									if (w3 is SimulatorWindow sw3)
									{
										sw3.ShowSpriteHitboxes = ShowSpriteHitboxes;
									}
								}
								catch
								{
								}
							}
						}
					}
					catch
					{
					}
					try
					{
						MainWindow.ShowTransientInfo("Show Sprite Hitboxes: " + (ShowSpriteHitboxes ? "ON" : "OFF"), this);
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
		if (e.Key == Key.Tab)
		{
			bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
			bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
			if (shift && ctrl)
			{
				tabSpeedMultiplier = 8;
			}
			else if (shift)
			{
				tabSpeedMultiplier = 4;
			}
			else
			{
				tabSpeedMultiplier = 2;
			}
		}
		if (e.Key != Key.Escape || levelCompleteTriggered)
		{
			return;
		}
		bool wasPaused = paused;
		bool willBePaused = !paused;
		if (wasPaused && !willBePaused && !playbackStartPending)
		{
			playbackStartPending = true;
			try
			{
				try
				{
					Window owner = base.Owner;
					if (owner is MainWindow mw5)
					{
						Task t = mw5.StartSimulatorPlaybackAsync();
						if (t != null)
						{
							await t;
						}
					}
				}
				catch
				{
				}
				try
				{
					SimulateNumericStep();
				}
				catch
				{
				}
				try
				{
					RenderFrame();
				}
				catch
				{
				}
				simAccumulatedMs = 0.0;
				simLastMs = simStopwatch.Elapsed.TotalMilliseconds;
			}
			finally
			{
				playbackStartPending = false;
			}
		}
		if (!wasPaused && willBePaused)
		{
			try
			{
				Window owner = base.Owner;
				if (owner is MainWindow mw6)
				{
					mw6.PauseSimulatorPlayback();
				}
			}
			catch
			{
			}
		}
		paused = willBePaused;
		try
		{
			PauseOverlay.Visibility = ((!paused) ? Visibility.Collapsed : Visibility.Visible);
		}
		catch
		{
		}
	}

	private void SimulatorWindow_MouseMove(object sender, MouseEventArgs e)
	{
		try
		{
			if (!deathTriggered || deathTileX < 0 || deathTileY < 0)
			{
				if (deathPlayerDot != null)
				{
					deathPlayerDot.Visibility = Visibility.Collapsed;
				}
				if (deathTileDot != null)
				{
					deathTileDot.Visibility = Visibility.Collapsed;
				}
				return;
			}
			Point position = e.GetPosition(RenderCanvas);
			int num = (playerX_fixed >> 8) - (cameraX_fixed >> 8);
			int num2 = (playerY_fixed >> 8) - (cameraY_fixed >> 8);
			if (position.X >= (double)num && position.X < (double)(num + playerVisualWidth) && position.Y >= (double)num2 && position.Y < (double)(num2 + playerVisualHeight))
			{
				if (deathPlayerDot == null)
				{
					deathPlayerDot = new Ellipse
					{
						Width = 8.0,
						Height = 8.0,
						Fill = new SolidColorBrush(Colors.Red),
						IsHitTestVisible = false
					};
					RenderCanvas.Children.Add(deathPlayerDot);
					Panel.SetZIndex(deathPlayerDot, 3000);
				}
				if (deathTileDot == null)
				{
					deathTileDot = new Ellipse
					{
						Width = 8.0,
						Height = 8.0,
						Fill = new SolidColorBrush(Colors.Red),
						IsHitTestVisible = false
					};
					RenderCanvas.Children.Add(deathTileDot);
					Panel.SetZIndex(deathTileDot, 3000);
				}
				double length = (double)num + (double)playerVisualWidth / 2.0 - 4.0;
				double length2 = (double)num2 + (double)playerVisualHeight / 2.0 - 4.0;
				Canvas.SetLeft(deathPlayerDot, length);
				Canvas.SetTop(deathPlayerDot, length2);
				deathPlayerDot.Visibility = Visibility.Visible;
				int num3 = deathTileX - (cameraX_fixed >> 8);
				int num4 = deathTileY - (cameraY_fixed >> 8);
				Canvas.SetLeft(deathTileDot, num3 - 4);
				Canvas.SetTop(deathTileDot, num4 - 4);
				deathTileDot.Visibility = Visibility.Visible;
			}
			else
			{
				if (deathPlayerDot != null)
				{
					deathPlayerDot.Visibility = Visibility.Collapsed;
				}
				if (deathTileDot != null)
				{
					deathTileDot.Visibility = Visibility.Collapsed;
				}
			}
		}
		catch
		{
		}
	}

	private void SimulatorWindow_Closing(object? sender, CancelEventArgs e)
	{
		try
		{
			StopSimulation();
			base.Dispatcher.Invoke(delegate
			{
				try
				{
					RenderCanvas.Children.Clear();
				}
				catch
				{
				}
			}, DispatcherPriority.Normal);
		}
		catch
		{
		}
	}

	private void SimulatorWindow_KeyUp(object sender, KeyEventArgs e)
	{
		if (playbackStartPending)
		{
			try
			{
				e.Handled = true;
				return;
			}
			catch
			{
				return;
			}
		}
		if (e.Key == Key.Up)
		{
			upHeld = false;
		}
		if (e.Key == Key.Down)
		{
			downHeld = false;
		}
		if (e.Key == Key.X || (!MainWindow.Option_CamMode && (e.Key == Key.Up || e.Key == Key.Space)))
		{
			try
			{
				if (!pathfinderEnabled)
				{
					lock (simLock)
					{
						try
						{
							Interlocked.Exchange(ref keyXPressedCount, 0);
						}
						catch
						{
						}
					}
				}
			}
			catch
			{
			}
			try
			{
				Interlocked.Exchange(ref keyXHeldStartedOnGroundInt, 0);
			}
			catch
			{
			}
			try
			{
				Interlocked.Exchange(ref keyXPressStartedOnGroundInt, 0);
			}
			catch
			{
			}
			try
			{
				orbBufferActive[currplayer] = false;
			}
			catch
			{
			}
			try
			{
				ballInputBufferCountdown[currplayer] = 0;
			}
			catch
			{
			}
			try
			{
				orbHoldConsumed[currplayer] = false;
			}
			catch
			{
			}
			try
			{
				orbHoldConsumedKeyStillDown[currplayer] = false;
			}
			catch
			{
			}
			try
			{
				orbHoldSuppressing[currplayer] = false;
			}
			catch
			{
			}
		}
		if (e.Key == Key.Tab)
		{
			tabSpeedMultiplier = 1;
		}
	}

	private async void PauseOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		try
		{
			if (!paused)
			{
				return;
			}
			if (!playbackStartPending)
			{
				playbackStartPending = true;
				try
				{
					Window owner = base.Owner;
					if (owner is MainWindow mw)
					{
						bool isPlaying = mw.IsMusicPlaying();
						bool isPaused = mw.IsMusicPaused();
						AppendSimDebug($"[UNPAUSE] Music state: playing={isPlaying}, paused={isPaused}, hasAppliedStartPos={hasAppliedStartPos}");
						if (!isPlaying)
						{
							double musicTime = 0.0;
							if (hasAppliedStartPos)
							{
								musicTime = CalculateMusicTimeToPosition(startPosX_forMusicSeek + playerVisualWidth / 2);
								AppendSimDebug($"[UNPAUSE] Starting music with seek to START POS musicTime={musicTime:F3}s");
							}
							else
							{
								AppendSimDebug("[UNPAUSE] Starting music with seek to beginning");
							}
							await mw.StopSimulatorPlaybackAsync();
							Task startTask = mw.ForceStartAndSeekSimulatorPlaybackAsync(musicTime);
							if (startTask != null)
							{
								await startTask;
							}
						}
						else
						{
							AppendSimDebug("[UNPAUSE] Music already playing, no action needed");
						}
					}
					try
					{
						SimulateNumericStep();
					}
					catch
					{
					}
					try
					{
						RenderFrame();
					}
					catch
					{
					}
					simAccumulatedMs = 0.0;
					simLastMs = simStopwatch.Elapsed.TotalMilliseconds;
				}
				finally
				{
					playbackStartPending = false;
				}
			}
			paused = false;
			try
			{
				PauseOverlay.Visibility = Visibility.Collapsed;
			}
			catch
			{
			}
			try
			{
				Focus();
			}
			catch
			{
			}
			e.Handled = true;
		}
		catch
		{
		}
	}

	private void RenderCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		Focus();
		e.Handled = true;
	}

	private void GameModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_suppressDebugBarEvents)
		{
			return;
		}
		try
		{
			if (GameModeComboBox.SelectedIndex >= 0)
			{
				currentGameMode = GameModeComboBox.SelectedIndex;
				if (currentGameMode != 0 && currentGameMode != 4 && currentGameMode != 8 && currentGameMode != 9 && currentGameMode != 11)
				{
					targetCameraY_fixed = cameraY_fixed;
				}
				try
				{
					UpdatePlayerSpeed();
				}
				catch
				{
				}
				try
				{
					UpdateEffectiveGravity();
				}
				catch
				{
				}
				try
				{
					UpdatePlayerImageForMode();
					return;
				}
				catch
				{
					return;
				}
			}
		}
		catch
		{
		}
	}

	private void MiniCheckBox_Changed(object sender, RoutedEventArgs e)
	{
		if (_suppressDebugBarEvents)
		{
			return;
		}
		try
		{
			miniMode = MiniCheckBox.IsChecked == true;
			currplayer_mini = (byte)(miniMode ? 1u : 0u);
			try
			{
				UpdateEffectiveGravity();
			}
			catch
			{
			}
			try
			{
				UpdatePlayerImageForMode();
			}
			catch
			{
			}
			try
			{
				UpdatePlayerVisualSizeForMode();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void InvertedCheckBox_Changed(object sender, RoutedEventArgs e)
	{
		if (_suppressDebugBarEvents)
		{
			return;
		}
		try
		{
			if (physicsEnabled)
			{
				return;
			}
			bool flag = gravityReversed;
			gravityReversed = InvertedCheckBox.IsChecked == true;
			gravityFlipped = gravityReversed;
			currplayer_gravity = (byte)(gravityReversed ? 255u : 0u);
			wasZeroedByCollisionLastFrame = false;
			if (flag == gravityReversed)
			{
				return;
			}
			try
			{
				UpdateEffectiveGravity();
			}
			catch
			{
			}
			try
			{
				UpdatePlayerIconFlip();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void PathfinderCheckBox_Changed(object sender, RoutedEventArgs e)
	{
		lock (simLock)
		{
			pathfinderEnabled = PathfinderCheckBox.IsChecked == true;
			_pathfinderUserPref = pathfinderEnabled;
			if (pathfinderEnabled)
			{
				PF_LoadPrecomputedInputs();
				if (!PF_HasInputData())
				{
					AppendSimDebug("[PATHFINDER] No precomputed path data! Use 'Calculate Path' in editor first.");
				}
			}
			else
			{
				Interlocked.Exchange(ref keyXPressedCount, 0);
				keyXHeld = false;
				pfInputSequence = null;
				pfFrameIndex = 0;
				pfLastAdvancedTick = -1L;
			}
		}
	}

	private void SpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_suppressDebugBarEvents)
		{
			return;
		}
		try
		{
			if (SpeedComboBox.SelectedItem is ComboBoxItem { Tag: not null } comboBoxItem && int.TryParse(comboBoxItem.Tag.ToString(), out var result))
			{
				speed = result;
				int[] array = new int[6] { 571, 708, 881, 1065, 1310, 366 };
				if (result >= 0 && result < array.Length)
				{
					currentSpeed_fixed = array[result];
					playerVelX_fixed = array[result];
				}
				UpdateSpeedDisplay();
			}
		}
		catch
		{
		}
	}

	private void UpdateSpeedDisplay()
	{
		try
		{
			int num = speed;
			if (SpeedComboBox != null && num >= 0 && num < SpeedComboBox.Items.Count && SpeedComboBox.SelectedIndex != num)
			{
				SpeedComboBox.SelectedIndex = num;
			}
		}
		catch
		{
		}
	}

	private void UpdateGameModeDisplay()
	{
		try
		{
			if (GameModeComboBox != null && currentGameMode >= 0 && currentGameMode < GameModeComboBox.Items.Count && GameModeComboBox.SelectedIndex != currentGameMode)
			{
				GameModeComboBox.SelectedIndex = currentGameMode;
			}
		}
		catch
		{
		}
	}

	private async void RestartButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			StopSimulation();
			int startX_px = 0;
			int startY_px = 0;
			bool hasStartPos = false;
			try
			{
				Window owner = base.Owner;
				if (owner is MainWindow { StartPosMarkerX: var startPosMarkerX } mw)
				{
					if (startPosMarkerX.HasValue && mw.StartPosMarkerY.HasValue)
					{
						startX_px = mw.StartPosMarkerX.Value;
						startY_px = mw.StartPosMarkerY.Value;
						hasStartPos = true;
						startPosX_forMusicSeek = startX_px;
						hasAppliedStartPos = true;
					}
					else
					{
						hasAppliedStartPos = false;
					}
				}
			}
			catch
			{
			}
			playerX_fixed = startX_px << 8;
			if (hasStartPos)
			{
				int playerCenter_fixed = playerX_fixed + (playerVisualWidth / 2 << 8);
				if (playerCenter_fixed >= 20480)
				{
					interactionScreenOffset_px = 80;
				}
				else
				{
					interactionScreenOffset_px = -1;
				}
			}
			else
			{
				interactionScreenOffset_px = -1;
			}
			if (hasStartPos)
			{
				playerY_fixed = startY_px << 8;
				int maxPlayerY_fixed = Math.Max(0, mapHeight * 16 - playerVisualHeight) << 8;
				if (playerY_fixed > maxPlayerY_fixed)
				{
					playerY_fixed = maxPlayerY_fixed;
				}
			}
			else
			{
				int? cfgSpawnY = ComputeSpawnYFixed();
				if (cfgSpawnY.HasValue)
				{
					playerY_fixed = cfgSpawnY.Value;
					int maxPlayerY_fixed2 = Math.Max(0, mapHeight * 16 - playerVisualHeight) << 8;
					if (playerY_fixed > maxPlayerY_fixed2)
					{
						playerY_fixed = maxPlayerY_fixed2;
					}
					if (playerY_fixed < 0)
					{
						playerY_fixed = 0;
					}
				}
				else
				{
					try
					{
						int groundRowsToReserve = 0;
						try
						{
							if (hasGroundLayer && groundTileRows > 0)
							{
								groundRowsToReserve = Math.Min(3, groundTileRows);
							}
						}
						catch
						{
							groundRowsToReserve = 0;
						}
						int groundSurface_px = (mapHeight - groundRowsToReserve) * 16;
						int cubeHitboxH = 15;
						playerY_fixed = Math.Max(0, groundSurface_px - cubeHitboxH) << 8;
						int maxPlayerY_fixed3 = Math.Max(0, mapHeight * 16 - playerVisualHeight) << 8;
						if (playerY_fixed > maxPlayerY_fixed3)
						{
							playerY_fixed = maxPlayerY_fixed3;
						}
					}
					catch
					{
						playerY_fixed = 0;
					}
				}
			}
			playerVelY_fixed = 0;
			cubeRotate_fixed = 0;
			cubeRotateMini_fixed = 0;
			_ = currentGameMode;
			_ = miniMode;
			_ = currplayer_gravity;
			_ = gravityReversed;
			int savedSpeed = speed;
			int savedPlayerVelX = playerVelX_fixed;
			if (hasStartPos)
			{
				currentGameMode = 0;
				miniMode = false;
				currplayer_mini = 0;
				currplayer_gravity = 0;
				gravityReversed = false;
				gravityFlipped = false;
				gravityMultiplier = 1.0;
				speed = 1;
				playerVelX_fixed = 708;
				try
				{
					UpdateGameModeDisplay();
				}
				catch
				{
				}
				try
				{
					UpdateSpeedDisplay();
				}
				catch
				{
				}
				try
				{
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						if (MiniCheckBox != null)
						{
							MiniCheckBox.IsChecked = false;
						}
						if (InvertedCheckBox != null)
						{
							InvertedCheckBox.IsChecked = false;
						}
					});
				}
				catch
				{
				}
				try
				{
					UpdatePlayerImageForMode();
				}
				catch
				{
				}
				try
				{
					UpdatePlayerVisualSizeForMode();
				}
				catch
				{
				}
				try
				{
					UpdatePlayerIconFlip();
				}
				catch
				{
				}
				try
				{
					UpdateEffectiveGravity();
				}
				catch
				{
				}
			}
			else
			{
				currentGameMode = _levelStartGameMode;
				miniMode = false;
				currplayer_mini = 0;
				currplayer_gravity = 0;
				gravityReversed = false;
				gravityFlipped = false;
				gravityMultiplier = 1.0;
				speed = savedSpeed;
				playerVelX_fixed = savedPlayerVelX;
				try
				{
					UpdateGameModeDisplay();
				}
				catch
				{
				}
				try
				{
					UpdatePlayerImageForMode();
				}
				catch
				{
				}
				try
				{
					UpdatePlayerVisualSizeForMode();
				}
				catch
				{
				}
				try
				{
					UpdatePlayerIconFlip();
				}
				catch
				{
				}
				try
				{
					UpdateEffectiveGravity();
				}
				catch
				{
				}
				try
				{
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						if (MiniCheckBox != null)
						{
							MiniCheckBox.IsChecked = false;
						}
						if (InvertedCheckBox != null)
						{
							InvertedCheckBox.IsChecked = false;
						}
					});
				}
				catch
				{
				}
			}
			if (hasStartPos)
			{
				int playerCenter_fixed_restart = playerX_fixed + (playerVisualWidth / 2 << 8);
				if (playerCenter_fixed_restart >= 20480 && interactionScreenOffset_px >= 0)
				{
					cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
				}
				else
				{
					cameraX_fixed = 0;
				}
				int maxCameraY_fixed = Math.Max(0, (mapHeight - 15) * 16) << 8;
				int grReserved_sp = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
				int minCameraY_fixed = -(grReserved_sp * 16) << 8;
				cameraY_fixed = Math.Max(minCameraY_fixed, Math.Min(maxCameraY_fixed, playerY_fixed - 30720));
			}
			else
			{
				cameraX_fixed = 0;
				int? cfgScrollY = ComputeScrollYFixed();
				if (cfgScrollY.HasValue)
				{
					cameraY_fixed = cfgScrollY.Value;
				}
				else
				{
					int maxY_fixed = Math.Max(0, (mapHeight - 15) * 16) << 8;
					int grReserved = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
					int minY_fixed = -(grReserved * 16) << 8;
					cameraY_fixed = Math.Max(minY_fixed, Math.Min(maxY_fixed, playerY_fixed - 30720));
				}
			}
			try
			{
				recordedPlayerPath.Clear();
			}
			catch
			{
			}
			try
			{
				recordedPlayer2Path.Clear();
				_prevDualActiveForP2Path = false;
			}
			catch
			{
			}
			try
			{
				recordedPlayer2Path.Clear();
				_prevDualActiveForP2Path = false;
			}
			catch
			{
			}
			try
			{
				processedGravityPortals.Clear();
			}
			catch
			{
			}
			try
			{
				processedGravityModPortals.Clear();
			}
			catch
			{
			}
			try
			{
				processedSpeedPortals.Clear();
			}
			catch
			{
			}
			try
			{
				processedRandomPortals.Clear();
			}
			catch
			{
			}
			try
			{
				processedGameModePortals.Clear();
			}
			catch
			{
			}
			try
			{
				processedMiniPortals.Clear();
			}
			catch
			{
			}
			try
			{
				processedTeleportPortals.Clear();
			}
			catch
			{
			}
			try
			{
				processedCamLockPortals.Clear();
			}
			catch
			{
			}
			try
			{
				processedWrapPortals.Clear();
			}
			catch
			{
			}
			nocamlockforced = false;
			wrapMode = false;
			if (currentGameMode != 0 && currentGameMode != 4 && currentGameMode != 8 && currentGameMode != 9 && currentGameMode != 11)
			{
				targetCameraY_fixed = cameraY_fixed;
			}
			else
			{
				targetCameraY_fixed = 0;
			}
			slowMode = false;
			playerInvis = false;
			forcedTrails = 0;
			simTickCount = 0;
			Array.Clear(playerOldPosY, 0, playerOldPosY.Length);
			try
			{
				processedTimewarpTriggers.Clear();
			}
			catch
			{
			}
			try
			{
				processedPlayerInvisTriggers.Clear();
			}
			catch
			{
			}
			try
			{
				processedTrailTriggers.Clear();
			}
			catch
			{
			}
			try
			{
				for (int tg = 0; tg < 3; tg++)
				{
					if (trailGhosts[tg] != null)
					{
						trailGhosts[tg].Visibility = Visibility.Collapsed;
					}
				}
			}
			catch
			{
			}
			dual = false;
			_prevDualActiveForP2Path = false;
			singlePortalExitPending = false;
			currplayer = 0;
			twoplayer = false;
			if (hasStartPos)
			{
				try
				{
					processedColorTriggers.Clear();
				}
				catch
				{
				}
				try
				{
					ApplyColorTriggersUpToPosition(startX_px);
				}
				catch
				{
				}
			}
			else
			{
				try
				{
					processedColorTriggers.Clear();
				}
				catch
				{
				}
			}
			try
			{
				ResetOrbSystem();
			}
			catch
			{
			}
			try
			{
				ResetBluePadSystem();
			}
			catch
			{
			}
			try
			{
				ResetSlopeState();
			}
			catch
			{
			}
			try
			{
				Interlocked.Exchange(ref keyXPressedCount, 0);
			}
			catch
			{
			}
			keyXHeld = false;
			prevKeyXDown = false;
			upHeld = false;
			downHeld = false;
			wasZeroedByCollisionLastFrame = true;
			onGround = true;
			foreach (var (spriteIndex, spriteId) in collectedCoinInfo)
			{
				if (spriteIndex >= 0 && spriteIndex < sprites.Length)
				{
					sprites[spriteIndex] = spriteId;
				}
			}
			collectedCoins.Clear();
			collectedCoinInfo.Clear();
			pfFrameIndex = 0;
			pfLastAdvancedTick = -1L;
			if (pathfinderEnabled)
			{
				PF_LoadPrecomputedInputs();
			}
			ballSwitched[0] = false;
			ballFlipCooldown = 0;
			p2BallHoldCounter = 0;
			ufoOrbed = false;
			try
			{
				SimulatorWindow simulatorWindow = this;
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(37, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[RESTART] Stopping music (isPlaying=");
				Window owner = base.Owner;
				defaultInterpolatedStringHandler.AppendFormatted(owner is MainWindow mw2 && mw2.IsMusicPlaying());
				defaultInterpolatedStringHandler.AppendLiteral(")");
				simulatorWindow.AppendSimDebug(defaultInterpolatedStringHandler.ToStringAndClear());
				await StopMusicAsync();
				SimulatorWindow simulatorWindow2 = this;
				defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(36, 1);
				defaultInterpolatedStringHandler.AppendLiteral("[RESTART] Music stopped (isPlaying=");
				owner = base.Owner;
				defaultInterpolatedStringHandler.AppendFormatted(owner is MainWindow mw3 && mw3.IsMusicPlaying());
				defaultInterpolatedStringHandler.AppendLiteral(")");
				simulatorWindow2.AppendSimDebug(defaultInterpolatedStringHandler.ToStringAndClear());
			}
			catch
			{
			}
			StopSimulation();
			await Task.Delay(20).ConfigureAwait(continueOnCapturedContext: false);
			StartSimulation();
			paused = true;
			try
			{
				PauseOverlay.Visibility = Visibility.Visible;
			}
			catch
			{
			}
			deathTriggered = false;
			levelCompleteTriggered = false;
			processedEndLevelTriggers.Clear();
			try
			{
				LevelCompleteOverlay.Visibility = Visibility.Collapsed;
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	public async Task StartAndSeekMusicAsync()
	{
		try
		{
			Window owner = base.Owner;
			MainWindow mw = owner as MainWindow;
			if (mw != null && hasAppliedStartPos)
			{
				Task t = mw.StartSimulatorPlaybackAsync();
				if (t != null)
				{
					await t;
					await Task.Delay(200).ConfigureAwait(continueOnCapturedContext: false);
					double musicTimeSeconds = CalculateMusicTimeToPosition(startPosX_forMusicSeek);
					mw.SeekSimulatorPlayback(musicTimeSeconds);
					await Task.Delay(100).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch
		{
		}
	}

	public void ApplySpawnScrollIfNoStartPos()
	{
		if (hasAppliedStartPos)
		{
			return;
		}
		try
		{
			int? num = ComputeSpawnYFixed();
			if (num.HasValue)
			{
				playerY_fixed = num.Value;
				int num2 = Math.Max(0, mapHeight * 16 - playerVisualHeight) << 8;
				if (playerY_fixed > num2)
				{
					playerY_fixed = num2;
				}
				if (playerY_fixed < 0)
				{
					playerY_fixed = 0;
				}
			}
			int? num3 = ComputeScrollYFixed();
			if (num3.HasValue)
			{
				cameraY_fixed = num3.Value;
			}
			else if (num.HasValue)
			{
				int val = Math.Max(0, (mapHeight - 15) * 16) << 8;
				int num4 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
				int val2 = -(num4 * 16) << 8;
				cameraY_fixed = Math.Max(val2, Math.Min(val, playerY_fixed - 30720));
			}
		}
		catch
		{
		}
	}

	public async Task StartRunningAsync()
	{
		try
		{
			if (!paused)
			{
				return;
			}
			if (!playbackStartPending)
			{
				playbackStartPending = true;
				try
				{
					Window owner = base.Owner;
					if (owner is MainWindow mw)
					{
						double musicTime = 0.0;
						if (hasAppliedStartPos)
						{
							musicTime = CalculateMusicTimeToPosition(startPosX_forMusicSeek + playerVisualWidth / 2);
							AppendSimDebug($"[START] Starting and seeking to START POS musicTime={musicTime:F3}s");
						}
						else
						{
							AppendSimDebug("[START] Starting and seeking to beginning");
						}
						Task startTask = mw.ForceStartAndSeekSimulatorPlaybackAsync(musicTime);
						if (startTask != null)
						{
							await startTask;
						}
						paused = false;
					}
					else
					{
						paused = false;
						try
						{
							owner = base.Owner;
							if (owner is MainWindow mw2)
							{
								Task t = mw2.ResumeSimulatorPlaybackAsync();
								if (t != null)
								{
									await t;
								}
							}
						}
						catch
						{
						}
					}
					try
					{
						RenderFrame();
					}
					catch
					{
					}
				}
				finally
				{
					playbackStartPending = false;
				}
			}
			try
			{
				PauseOverlay.Visibility = Visibility.Collapsed;
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	public bool HasStartPosApplied()
	{
		return hasAppliedStartPos;
	}

	public void ApplyStartPosMarker()
	{
		try
		{
			if (!(base.Owner is MainWindow { StartPosMarkerX: not null, StartPosMarkerY: not null, StartPosMarkerX: var startPosMarkerX } mainWindow))
			{
				return;
			}
			int value = startPosMarkerX.Value;
			int value2 = mainWindow.StartPosMarkerY.Value;
			playerX_fixed = value << 8;
			playerY_fixed = value2 << 8;
			int num = Math.Max(0, mapHeight * 16 - playerVisualHeight) << 8;
			if (playerY_fixed > num)
			{
				playerY_fixed = num;
			}
			int num2 = playerX_fixed + (playerVisualWidth / 2 << 8);
			if (num2 >= 20480)
			{
				interactionScreenOffset_px = 80;
				cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
			}
			else
			{
				cameraX_fixed = 0;
				interactionScreenOffset_px = -1;
			}
			int val = Math.Max(0, (mapHeight - 15) * 16) << 8;
			int num3 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			int val2 = -(num3 * 16) << 8;
			cameraY_fixed = Math.Max(val2, Math.Min(val, playerY_fixed - 30720));
			hasAppliedStartPos = true;
			startPosX_forMusicSeek = value;
			try
			{
				int num4 = startingSpeedUiIndex;
				if (1 == 0)
				{
				}
				int num5 = num4 switch
				{
					0 => 571, 
					1 => 708, 
					2 => 881, 
					3 => 1065, 
					4 => 1310, 
					5 => 366, 
					_ => 708, 
				};
				if (1 == 0)
				{
				}
				int num6 = num5;
				playerVelX_fixed = num6;
				speed = startingSpeedUiIndex;
				try
				{
					UpdateSpeedDisplay();
				}
				catch
				{
				}
			}
			catch
			{
			}
			ApplyColorTriggersUpToPosition(value);
			try
			{
				double value3 = CalculateMusicTimeToPosition(value + playerVisualWidth / 2);
				AppendSimDebug($"[START POS] Applied: X={value}, musicTime={value3:F3}s (will seek on unpause)");
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void Timer_Tick(object? sender, EventArgs e)
	{
		if (pfSimulating)
		{
			return;
		}
		try
		{
			bool flag = IsXDownAsync() || Keyboard.IsKeyDown(Key.X) || (!MainWindow.Option_CamMode && (Keyboard.IsKeyDown(Key.Up) || Keyboard.IsKeyDown(Key.Space)));
			bool flag2 = Keyboard.IsKeyDown(Key.Up);
			bool flag3 = Keyboard.IsKeyDown(Key.Down);
			lock (simLock)
			{
				if (pathfinderEnabled)
				{
					upHeld = flag2;
					downHeld = flag3;
				}
				else
				{
					int num = Math.Max(0, mapHeight * 16 - playerVisualHeight) << 8;
					if (!flag || !prevKeyXDown)
					{
					}
					prevKeyXDown = flag;
					keyXHeld = flag;
					upHeld = flag2;
					downHeld = flag3;
				}
			}
		}
		catch
		{
		}
		int num2 = cameraX_fixed + 32768;
		int num3 = tabSpeedMultiplier;
		int num4 = 2048;
		int num5 = playerX_fixed + num4;
		int num6 = (pathfinderEnabled ? playerX_fixed : (playerX_fixed + (int)Math.Round((double)(currentSpeed_fixed * num3) * simTimeScale)));
		int num7 = num6 + num4;
		if (!pathfinderEnabled)
		{
			playerX_fixed = num6;
		}
		if (onGround && !pathfinderEnabled)
		{
			groundStabilizeCounter = 0;
			try
			{
				bool flag4 = false;
				if (gravityReversed)
				{
					flag4 = IsTouchingCeiling();
				}
				else
				{
					int num8 = (playerX_fixed >> 8) + 8;
					int num9 = num8 - 7;
					int num10 = num9 + 14;
					int num11 = num9 + 7;
					int[] array = new int[3] { num9, num11, num10 };
					int num12 = (miniMode ? 7 : 15);
					int num13 = 0;
					if (miniMode)
					{
						num13 = ((currentGameMode == 2) ? 4 : 9);
					}
					int num14 = (playerY_fixed >> 8) + num13 + num12;
					int num15 = num14 / 16;
					int num16 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
					int num17 = num15 + num16;
					if (num17 >= mapHeight)
					{
						flag4 = true;
					}
					else if (num17 >= 0)
					{
						int localY = (num14 % 16 + 16) % 16;
						int[] array2 = array;
						foreach (int num18 in array2)
						{
							int num19 = num18 / 16;
							if (num19 >= 0 && num19 < mapWidth)
							{
								int num20 = tiles[num17 * mapWidth + num19];
								int num21 = MapAnimatedTileIndex(num20);
								int num22 = num21;
								if (num21 >= 1000)
								{
									num22 = ((num21 >= 1000 && num21 <= 1007) ? (8 + (num21 - 1000) % 4) : ((num21 >= 1010 && num21 <= 1015) ? (((num21 - 1010) % 3) switch
									{
										1 => 125, 
										0 => 4, 
										_ => 127, 
									}) : ((num21 < 1020 || num21 > 1037) ? num20 : (116 + (num21 - 1020) % 9))));
								}
								MetatileCollision col = MetatileCollisionTable.GetCollision((byte)num22);
								int num23 = num19 * 16;
								int localX = Math.Max(0, Math.Min(15, num18 - num23));
								if (ProvidesFloorAtColumnStatic(col, localX, out var _) && SharedPhysics.TileOccupiesPixel(col, localX, localY))
								{
									flag4 = true;
									break;
								}
							}
						}
					}
				}
				if (!flag4)
				{
					onGround = false;
					wasZeroedByCollisionLastFrame = false;
				}
			}
			catch
			{
				onGround = false;
			}
		}
		bool flag5 = num5 < 20480 && num7 >= 20480;
		if (flag5)
		{
			interactionScreenOffset_px = 80 - (cameraX_fixed >> 8);
		}
		int num24 = playerX_fixed + num4;
		if (num24 >= 20480)
		{
			if (interactionScreenOffset_px >= 0)
			{
				cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
			}
			else
			{
				cameraX_fixed += num7 - 20480;
			}
			int num25 = Math.Max(0, (mapWidth - 16) * 16) << 8;
			if (cameraX_fixed < 0)
			{
				cameraX_fixed = 0;
			}
			if (cameraX_fixed > num25)
			{
				cameraX_fixed = num25;
			}
		}
		else
		{
			interactionScreenOffset_px = -1;
		}
		int num26 = Math.Max(0, (mapHeight - 15) * 16) << 8;
		int num27 = Math.Max(0, mapHeight * 16 - playerVisualHeight) << 8;
		int num28 = (playerY_fixed >> 8) - (cameraY_fixed >> 8);
		if (upHeld)
		{
			if (camModeActive)
			{
				if (cameraY_fixed > 0)
				{
					cameraY_fixed -= 512;
					if (cameraY_fixed < 0)
					{
						cameraY_fixed = 0;
					}
				}
			}
			else
			{
				int num29 = (playerY_fixed >> 8) + playerVisualHeight / 2 - (cameraY_fixed >> 8);
				int num30 = 80;
				if (!jumpedOnce && camModeActive)
				{
					if (cameraY_fixed > 0)
					{
						cameraY_fixed -= 512;
						if (cameraY_fixed < 0)
						{
							cameraY_fixed = 0;
						}
					}
				}
				else if (!physicsEnabled)
				{
					if (camModeActive)
					{
						playerY_fixed -= 512;
						if (playerY_fixed < 0)
						{
							playerY_fixed = 0;
						}
						num29 = (playerY_fixed >> 8) + playerVisualHeight / 2 - (cameraY_fixed >> 8);
						if (num29 <= num30)
						{
							int val = num30 - num29;
							int num31 = Math.Min(val, cameraY_fixed >> 8);
							cameraY_fixed -= num31 << 8;
							if (cameraY_fixed < 0)
							{
								cameraY_fixed = 0;
							}
						}
					}
				}
				else if (camModeActive && num29 <= num30)
				{
					int val2 = num30 - num29;
					int num32 = Math.Min(val2, cameraY_fixed >> 8);
					cameraY_fixed -= num32 << 8;
					if (cameraY_fixed < 0)
					{
						cameraY_fixed = 0;
					}
				}
				else if (camModeActive && cameraY_fixed > 0)
				{
					cameraY_fixed -= 512;
					if (cameraY_fixed < 0)
					{
						cameraY_fixed = 0;
					}
				}
			}
		}
		if (downHeld)
		{
			if (camModeActive)
			{
				if (cameraY_fixed < num26)
				{
					cameraY_fixed += 512;
					if (cameraY_fixed > num26)
					{
						cameraY_fixed = num26;
					}
				}
			}
			else
			{
				int num33 = 160;
				int num34 = (playerY_fixed >> 8) + playerVisualHeight / 2 - (cameraY_fixed >> 8);
				if (!jumpedOnce && camModeActive)
				{
					if (cameraY_fixed < num26)
					{
						cameraY_fixed += 512;
						if (cameraY_fixed > num26)
						{
							cameraY_fixed = num26;
						}
					}
				}
				else if (num34 < num33)
				{
					if (!physicsEnabled && camModeActive)
					{
						playerY_fixed += 512;
						if (playerY_fixed > num27)
						{
							playerY_fixed = num27;
						}
					}
				}
				else if (camModeActive && cameraY_fixed < num26)
				{
					jumpedOnce = true;
					cameraY_fixed += 512;
					if (cameraY_fixed > num26)
					{
						cameraY_fixed = num26;
					}
					if (!physicsEnabled)
					{
						playerY_fixed += 512;
						if (playerY_fixed > num27)
						{
							playerY_fixed = num27;
						}
					}
				}
				else if (!physicsEnabled)
				{
					playerY_fixed += 512;
					if (playerY_fixed > num27)
					{
						playerY_fixed = num27;
					}
				}
			}
		}
		try
		{
			bool flag6 = currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8 || currentGameMode == 9 || nocamlockforced;
			if (physicsEnabled && jumpedOnce && !paused)
			{
				if ((!dual || twoplayer) && flag6)
				{
					int num35 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
					int num36 = -(num35 * 16) << 8;
					int num37 = playerY_fixed - cameraY_fixed;
					if (num37 < 16384)
					{
						int num38 = 16384 - num37;
						cameraY_fixed -= num38;
						if (cameraY_fixed < num36)
						{
							cameraY_fixed = num36;
						}
					}
					else if (num37 >> 8 >= 160)
					{
						int num39 = num37 - 40960;
						int num40 = Math.Max(0, (mapHeight - 15) * 16) << 8;
						cameraY_fixed += num39;
						if (cameraY_fixed > num40)
						{
							cameraY_fixed = num40;
						}
					}
				}
				else
				{
					int num41 = cameraY_fixed >> 8;
					int num42 = targetCameraY_fixed >> 8;
					if (num42 > num41)
					{
						cameraY_fixed += 512;
					}
					else if (num42 < num41)
					{
						cameraY_fixed -= 768;
						playerY_fixed -= 256;
					}
					int num43 = Math.Max(0, (mapHeight - 15) * 16) << 8;
					int num44 = -(((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0) * 16) << 8;
					if (cameraY_fixed < num44)
					{
						cameraY_fixed = num44;
					}
					if (cameraY_fixed > num43)
					{
						cameraY_fixed = num43;
					}
				}
			}
		}
		catch
		{
		}
		if (physicsEnabled && simTimer == null)
		{
			try
			{
				if (groundStabilizeCounter > 0)
				{
					groundStabilizeCounter--;
				}
				bool flag7 = onGround || groundStabilizeCounter > 0 || invertedCeilingHoldCounter > 0;
				bool flag8 = false;
				int num45 = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
				if (!flag8 && !flag7 && (playerVelY_fixed != 0 || playerY_fixed < num27 - 256))
				{
					if (currentGameMode == 1)
					{
						try
						{
							int num46 = ((!gravityReversed) ? 1 : (-1));
							bool flag9 = ((num46 > 0) ? (playerVelY_fixed < 0) : (playerVelY_fixed > 0));
							bool flag10 = IsXDownAsync() || keyXHeld;
							int num47 = ((!flag9) ? (flag10 ? 73 : 48) : (flag10 ? 76 : 60));
							int num48 = num47 * num46;
							if (flag10)
							{
								num48 = -num48;
							}
							try
							{
								playerVelY_fixed += (int)Math.Round((double)num48 * simTimeScale * simTimeScale);
							}
							catch
							{
								playerVelY_fixed += num48;
							}
							try
							{
								if (num46 < 0)
								{
									if (playerVelY_fixed < -873)
									{
										playerVelY_fixed = -873;
									}
									if (playerVelY_fixed > 1091)
									{
										playerVelY_fixed = 1091;
									}
								}
								else
								{
									if (playerVelY_fixed < -1091)
									{
										playerVelY_fixed = -1091;
									}
									if (playerVelY_fixed > 873)
									{
										playerVelY_fixed = 873;
									}
								}
							}
							catch
							{
							}
						}
						catch
						{
							try
							{
								playerVelY_fixed += (int)Math.Round((double)effectiveGravity_fixed * gravityMultiplier * simTimeScale * simTimeScale);
							}
							catch
							{
								playerVelY_fixed += (int)((double)effectiveGravity_fixed * gravityMultiplier);
							}
						}
					}
					else if (currentGameMode == 2)
					{
						try
						{
							int num49 = (ballGoingDown ? 1 : (-1));
							if (gravityReversed)
							{
								num49 = -num49;
							}
							int num50 = 102;
							int num51 = num50 * num49;
							try
							{
								playerVelY_fixed += (int)Math.Round((double)num51 * simTimeScale * simTimeScale);
							}
							catch
							{
								playerVelY_fixed += num51;
							}
							try
							{
								int num52 = ((num49 >= 0) ? 1843 : (-1843));
								if (num52 >= 0)
								{
									if (playerVelY_fixed > num52)
									{
										playerVelY_fixed = num52;
									}
								}
								else if (playerVelY_fixed < num52)
								{
									playerVelY_fixed = num52;
								}
							}
							catch
							{
							}
						}
						catch
						{
							try
							{
								playerVelY_fixed += (int)Math.Round((double)effectiveGravity_fixed * gravityMultiplier * simTimeScale * simTimeScale);
							}
							catch
							{
								playerVelY_fixed += (int)((double)effectiveGravity_fixed * gravityMultiplier);
							}
						}
					}
					else
					{
						try
						{
							playerVelY_fixed += (int)Math.Round((double)effectiveGravity_fixed * gravityMultiplier * simTimeScale * simTimeScale);
						}
						catch
						{
							playerVelY_fixed += (int)((double)effectiveGravity_fixed * gravityMultiplier);
						}
						try
						{
							if (effectiveMaxFall_fixed >= 0)
							{
								if (playerVelY_fixed > effectiveMaxFall_fixed)
								{
									playerVelY_fixed = effectiveMaxFall_fixed;
								}
							}
							else if (playerVelY_fixed < effectiveMaxFall_fixed)
							{
								playerVelY_fixed = effectiveMaxFall_fixed;
							}
						}
						catch
						{
						}
					}
				}
				playerY_fixed += playerVelY_fixed;
				int num53 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
				int num54 = -(num53 * 16) << 8;
				if (playerY_fixed < num54)
				{
					playerY_fixed = num54;
				}
				try
				{
					if (playerVelY_fixed < 0)
					{
						int num55 = (playerX_fixed >> 8) + playerVisualWidth / 2;
						int num56 = num55 - 7;
						int num57 = num56 + 14;
						int num58 = playerY_fixed >> 8;
						int num59 = num58 / 16;
						int num60 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
						int num61 = num59 + num60;
						if (num61 >= 0 && num61 < mapHeight)
						{
							int num62 = num56 / 16;
							int num63 = num57 / 16;
							bool flag11 = false;
							int num64 = int.MaxValue;
							for (int j = num62; j <= num63; j++)
							{
								if (j < 0 || j >= mapWidth)
								{
									continue;
								}
								int num65 = tiles[num61 * mapWidth + j];
								int num66 = MapAnimatedTileIndex(num65);
								int num67 = num66;
								if (num66 >= 1000)
								{
									num67 = ((num66 >= 1000 && num66 <= 1007) ? (8 + (num66 - 1000) % 4) : ((num66 >= 1010 && num66 <= 1015) ? (((num66 - 1010) % 3) switch
									{
										1 => 125, 
										0 => 4, 
										_ => 127, 
									}) : ((num66 < 1020 || num66 > 1037) ? num65 : (116 + (num66 - 1020) % 9))));
								}
								MetatileCollision col2 = MetatileCollisionTable.GetCollision((byte)num67);
								int num68 = j * 16;
								int num69 = Math.Max(0, num56 - num68);
								int num70 = Math.Min(15, num57 - num68);
								for (int k = num69; k <= num70; k++)
								{
									if (!MainWindow.Option_NoDeath)
									{
										int num71 = num68 + k;
										if (num71 >= num55)
										{
											continue;
										}
									}
									if (BlocksCeilingAtColumn(col2, k))
									{
										flag11 = true;
										int num72 = (num59 + 1) * 16;
										if (num72 < num64)
										{
											num64 = num72;
										}
										break;
									}
								}
								if (flag11)
								{
									break;
								}
							}
							if (flag11 && num64 >= int.MaxValue)
							{
							}
						}
					}
				}
				catch
				{
				}
				try
				{
					int num73 = (miniMode ? 8 : 15);
					int num74 = (miniMode ? 7 : 15);
					int num75 = (playerX_fixed >> 8) + playerVisualWidth / 2;
					int num76 = num75 - num73 / 2;
					int num77 = num76 + (num73 - 1);
					int num78 = playerY_fixed >> 8;
					if (miniMode && !gravityFlipped)
					{
						num78 += 9;
					}
					num78 += num74;
					int num79 = num78 / 16;
					try
					{
						int num80 = 0;
						if (hasGroundLayer && groundTileRows > 0)
						{
							num80 = Math.Min(3, groundTileRows);
						}
						num79 += num80;
					}
					catch
					{
					}
					int num81 = 0;
					int num82 = mapHeight * 16;
					if (num79 >= 0 && num79 < mapHeight)
					{
						int num83 = num76 / 16;
						int num84 = num77 / 16;
						int num85 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
						int num86 = Math.Max(0, num79 - 1);
						int num87 = Math.Min(mapHeight - 1, num79);
						for (int l = num86; l <= num87; l++)
						{
							for (int m = num83; m <= num84; m++)
							{
								if (m < 0 || m >= mapWidth)
								{
									continue;
								}
								int num88 = tiles[l * mapWidth + m];
								int num89 = MapAnimatedTileIndex(num88);
								int num90 = num89;
								if (num89 >= 1000)
								{
									num90 = ((num89 >= 1000 && num89 <= 1007) ? (8 + (num89 - 1000) % 4) : ((num89 >= 1010 && num89 <= 1015) ? (((num89 - 1010) % 3) switch
									{
										1 => 125, 
										0 => 4, 
										_ => 127, 
									}) : ((num89 < 1020 || num89 > 1037) ? num88 : (116 + (num89 - 1020) % 9))));
								}
								MetatileCollision col3 = MetatileCollisionTable.GetCollision((byte)num90);
								int num91 = m * 16;
								int num92 = Math.Max(0, num76 - num91);
								int num93 = Math.Min(15, num77 - num91);
								int num94 = int.MaxValue;
								bool flag12 = false;
								for (int n = num92; n <= num93; n++)
								{
									if (ProvidesFloorAtColumn(col3, n, out var topOffsetPx2))
									{
										int num95 = (l - num85) * 16;
										int num96 = num78 - num95;
										if (num96 >= 0 && num96 <= 15 && TileOccupiesPixel(col3, n, num96))
										{
											flag12 = true;
											if (topOffsetPx2 < num94)
											{
												num94 = topOffsetPx2;
											}
										}
									}
									else if (!MainWindow.Option_NoDeath)
									{
										int num97 = num91 + n;
										if (num97 < num75)
										{
										}
									}
								}
								if (flag12)
								{
									int num98 = (l - num85) * 16 + num94;
									if (num98 < num82)
									{
										num82 = num98;
									}
									num81 = 1;
								}
							}
						}
					}
					if (num81 == 0)
					{
						int num99 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
						if (num99 > 0)
						{
							int num100 = (mapHeight - num99) * 16;
							if (num100 < num82)
							{
								num82 = num100;
							}
							num81 = 1;
						}
					}
					int num101 = ((num81 == 1) ? (num82 - num74 << 8) : num27);
					if (runtimePerFrameLog)
					{
						try
						{
							StringBuilder stringBuilder = new StringBuilder();
							stringBuilder.AppendFormat("SIM_FRAME: playerX_px={0} playerY_px={1} footY_px={2} tileBelowY={3}; ", playerX_fixed >> 8, playerY_fixed >> 8, num78, num79);
							int num102 = num76 / 16;
							int num103 = num77 / 16;
							if (num79 < 0 || num79 >= mapHeight)
							{
								stringBuilder.AppendFormat("tileBelowY={0}:out_of_bounds; ", num79);
							}
							else
							{
								for (int num104 = num102; num104 <= num103; num104++)
								{
									if (num104 < 0 || num104 >= mapWidth)
									{
										stringBuilder.AppendFormat("tx={0}:out_of_bounds; ", num104);
										continue;
									}
									int num105 = tiles[num79 * mapWidth + num104];
									int num106 = MapAnimatedTileIndex(num105);
									int num107 = num106;
									if (num106 >= 1000)
									{
										num107 = ((num106 >= 1000 && num106 <= 1007) ? (8 + (num106 - 1000) % 4) : ((num106 >= 1010 && num106 <= 1015) ? (((num106 - 1010) % 3) switch
										{
											1 => 125, 
											0 => 4, 
											_ => 127, 
										}) : ((num106 < 1020 || num106 > 1037) ? num105 : (116 + (num106 - 1020) % 9))));
									}
									MetatileCollision metatileCollision = MetatileCollisionTable.GetCollision((byte)num107);
									stringBuilder.AppendFormat("tx={0} tid={1} animRemap={2} collTid=0x{3:X2} coll={4}; ", num104, num105, num106, num107, metatileCollision);
								}
							}
							string text = stringBuilder.ToString();
							Console.WriteLine(text);
							try
							{
								File.AppendAllText(simDebugFilePath, DateTime.UtcNow.ToString("o") + " " + text + Environment.NewLine);
							}
							catch
							{
							}
						}
						catch
						{
						}
					}
					else
					{
						int num108 = Interlocked.Exchange(ref jumpBufferCounter, 0);
						int num109 = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
						if (!gravityReversed)
						{
							if (playerY_fixed >= num101 - 256 && playerVelY_fixed >= 0)
							{
								int num110 = num101 - 256;
								if (num110 < 0)
								{
									num110 = 0;
								}
								playerY_fixed = num110;
								playerVelY_fixed = 0;
								onGround = true;
								groundStabilizeCounter = 2;
								if (currentGameMode != 0)
								{
									int num111 = Interlocked.Exchange(ref ballToggleRequested, 0);
									if (currentGameMode == 2 && num111 > 0)
									{
										ballGoingDown = !ballGoingDown;
										try
										{
											playerVelY_fixed = (int)Math.Round((double)(ballGoingDown ? 614 : (-614)) * simTimeScale);
										}
										catch
										{
											playerVelY_fixed = (ballGoingDown ? 614 : (-614));
										}
										onGround = false;
										jumpedOnce = true;
										LogBallEvent($"UI-Landing: consumed queued toggle; ballGoingDown={ballGoingDown}");
									}
								}
							}
							else
							{
								onGround = false;
								groundStabilizeCounter = 0;
							}
						}
						else if (playerY_fixed <= num101 + 256 && playerVelY_fixed <= 0)
						{
							int num112 = num101 + 256;
							playerY_fixed = num112;
							playerVelY_fixed = 0;
							onGround = true;
							if (currentGameMode != 0)
							{
								int num113 = Interlocked.Exchange(ref ballToggleRequested, 0);
								if (currentGameMode == 2 && num113 > 0)
								{
									ballGoingDown = !ballGoingDown;
									try
									{
										playerVelY_fixed = (int)Math.Round((double)(ballGoingDown ? 614 : (-614)) * simTimeScale);
									}
									catch
									{
										playerVelY_fixed = (ballGoingDown ? 614 : (-614));
									}
									onGround = false;
									jumpedOnce = true;
									LogBallEvent($"UI-ReversedLanding: consumed queued toggle; ballGoingDown={ballGoingDown}");
								}
							}
						}
						else
						{
							onGround = false;
						}
					}
				}
				catch
				{
				}
			}
			catch
			{
			}
		}
		if (cameraY_fixed < 0)
		{
			cameraY_fixed = 0;
		}
		if (cameraY_fixed > num26)
		{
			cameraY_fixed = num26;
		}
		if (playerY_fixed > num27)
		{
			playerY_fixed = num27;
			playerVelY_fixed = 0;
		}
		try
		{
			if (currplayer_gravity == 0)
			{
				int num114 = (playerY_fixed >> 8) - (cameraY_fixed >> 8) + gridRenderShiftYPx;
				int num115 = 192;
				int num116 = num114 + playerVisualHeight;
				if (num116 > num115)
				{
					int num117 = num115 - playerVisualHeight;
					int num118 = num117 + (cameraY_fixed >> 8) - gridRenderShiftYPx;
					if (num118 < 0)
					{
						num118 = 0;
					}
					int num119 = num118 << 8;
					if (num119 > num27)
					{
						num119 = num27;
					}
					playerY_fixed = num119;
					if (simTimer == null)
					{
						playerVelY_fixed = 0;
						onGround = true;
					}
				}
			}
		}
		catch
		{
		}
		try
		{
			Color a = backgroundTint;
			Color a2 = tileTint;
			Color a3 = groundTint;
			bool flag13 = backgroundForceSolidBlack;
			int num120 = cameraX_fixed + 32768;
			int num121 = int.MaxValue;
			int? num122 = null;
			if (flag5)
			{
				for (int num123 = 0; num123 < nonEmptySpriteIndices.Length; num123++)
				{
					int num124 = nonEmptySpriteIndices[num123];
					int num125 = sprites[num124];
					if (num125 < 0)
					{
						continue;
					}
					try
					{
						if (num125 == 0 || num125 == 1 || num125 == 2 || num125 == 3 || num125 == 4 || num125 == 23 || num125 == 36 || num125 == 75 || num125 == 88 || num125 == 106 || num125 == 107 || num125 == 108)
						{
							int num126 = (miniMode ? 8 : 15);
							int num127 = (miniMode ? 7 : 15);
							int num128 = (playerX_fixed >> 8) + 1;
							int playerRight_px = num128 + num126 - 1;
							int num129 = playerY_fixed >> 8;
							num129 += GetMiniSpriteOffsetY();
							int playerBottom_px = num129 + num127 - 1;
							if (SpriteIntersectsPlayer(num124, num125, num128, playerRight_px, num129, playerBottom_px))
							{
								try
								{
									int num130 = currentGameMode;
									if (1 == 0)
									{
									}
									int topOffsetPx = num125 switch
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
										_ => currentGameMode, 
									};
									if (1 == 0)
									{
									}
									int num131 = topOffsetPx;
									if (num131 != num130)
									{
										currentGameMode = num131;
										wasZeroedByCollisionLastFrame = false;
										try
										{
											UpdateGameModeDisplay();
										}
										catch
										{
										}
										try
										{
											UpdateEffectiveGravity();
										}
										catch
										{
										}
										try
										{
											playerVelY_fixed >>= 1;
										}
										catch
										{
										}
									}
									try
									{
										UpdatePlayerImageForMode();
									}
									catch
									{
									}
								}
								catch
								{
								}
								break;
							}
						}
					}
					catch
					{
					}
					if (!speedPortalMap.ContainsKey(num125) || physicsEnabled)
					{
						continue;
					}
					int num132;
					if (spriteAnchors != null && spriteAnchors.TryGetValue(num124, out (int, int) value))
					{
						(num132, _) = value;
					}
					else
					{
						num132 = num124 % mapWidth;
					}
					int num133 = num132;
					int num134 = num133 * 16 + 8 << 8;
					if (!camModeActive)
					{
						int num135 = (miniMode ? 8 : 15);
						int num136 = (miniMode ? 7 : 15);
						int num137 = (playerX_fixed >> 8) + 1;
						int playerRight_px2 = num137 + num135 - 1;
						int num138 = playerY_fixed >> 8;
						num138 += GetMiniSpriteOffsetY();
						int playerBottom_px2 = num138 + num136 - 1;
						if (SpriteIntersectsPlayer(num124, num125, num137, playerRight_px2, num138, playerBottom_px2) && !processedSpeedPortals.Contains(num124) && num134 < num121)
						{
							num121 = num134;
							num122 = speedPortalMap[num125];
							processedSpeedPortals.Add(num124);
						}
					}
					else if (flag5)
					{
						if (num134 > num5 && num134 <= 20480 && !processedSpeedPortals.Contains(num124) && num134 < num121)
						{
							num121 = num134;
							num122 = speedPortalMap[num125];
							processedSpeedPortals.Add(num124);
						}
					}
					else if (num134 <= num120 && !processedSpeedPortals.Contains(num124) && num134 < num121)
					{
						num121 = num134;
						num122 = speedPortalMap[num125];
						processedSpeedPortals.Add(num124);
					}
				}
			}
			else if (camModeActive)
			{
				for (int num139 = 0; num139 < nonEmptySpriteIndices.Length; num139++)
				{
					int num140 = nonEmptySpriteIndices[num139];
					int num141 = sprites[num140];
					if (num141 >= 0 && speedPortalMap.ContainsKey(num141))
					{
						int num142;
						if (spriteAnchors != null && spriteAnchors.TryGetValue(num140, out (int, int) value2))
						{
							(num142, _) = value2;
						}
						else
						{
							num142 = num140 % mapWidth;
						}
						int num143 = num142;
						int num144 = num143 * 16 + 8 << 8;
						int num145 = (miniMode ? 8 : 15);
						int num146 = (miniMode ? 7 : 15);
						int num147 = (playerX_fixed >> 8) + 1;
						int playerRight_px3 = num147 + num145 - 1;
						int num148 = playerY_fixed >> 8;
						num148 += GetMiniSpriteOffsetY();
						int playerBottom_px3 = num148 + num146 - 1;
						if (SpriteIntersectsPlayer(num140, num141, num147, playerRight_px3, num148, playerBottom_px3) && num144 < num121)
						{
							num121 = num144;
							num122 = speedPortalMap[num141];
						}
					}
				}
			}
			int num149 = int.MaxValue;
			int? num150 = null;
			int? num151 = null;
			int num152 = int.MaxValue;
			int? num153 = null;
			int? num154 = null;
			int num155 = int.MaxValue;
			int? num156 = null;
			int? num157 = null;
			for (int num158 = 0; num158 < nonEmptySpriteIndices.Length; num158++)
			{
				int num159 = nonEmptySpriteIndices[num158];
				int num160 = sprites[num159];
				if (num160 < 0 || !IsColorTriggerSprite(num160))
				{
					continue;
				}
				int num161;
				if (spriteAnchors != null && spriteAnchors.TryGetValue(num159, out (int, int) value3))
				{
					(num161, _) = value3;
				}
				else
				{
					num161 = num159 % mapWidth;
				}
				int num162 = num161 * 16 + 8 << 8;
				if (flag5)
				{
					if (num162 > num5 && num162 <= 20480)
					{
						if (IsBackgroundTrigger(num160))
						{
							if (num162 < num149)
							{
								num149 = num162;
								num150 = num159;
								num151 = num160;
							}
						}
						else if (IsTileTrigger(num160))
						{
							if (num162 < num152)
							{
								num152 = num162;
								num153 = num159;
								num154 = num160;
							}
						}
						else if (IsGroundTrigger(num160) && num162 < num155)
						{
							num155 = num162;
							num156 = num159;
							num157 = num160;
						}
						continue;
					}
					if (processedColorTriggers.Contains(num159) && num162 > 20480)
					{
						processedColorTriggers.Remove(num159);
					}
					if (processedGravityPortals.Contains(num159) && num162 > 20480)
					{
						processedGravityPortals.Remove(num159);
					}
					if (processedGravityModPortals.Contains(num159) && num162 > 20480)
					{
						processedGravityModPortals.Remove(num159);
					}
					if (processedGameModePortals.Contains(num159) && num162 > 20480)
					{
						processedGameModePortals.Remove(num159);
					}
					if (processedOrbs.Contains(num159) && num162 > 20480)
					{
						processedOrbs.Remove(num159);
					}
					if (processedTeleportPortals.Contains(num159) && num162 > 20480)
					{
						processedTeleportPortals.Remove(num159);
					}
				}
				else if (num162 <= num120)
				{
					if (processedColorTriggers.Contains(num159))
					{
						continue;
					}
					if (IsBackgroundTrigger(num160))
					{
						if (num162 < num149)
						{
							num149 = num162;
							num150 = num159;
							num151 = num160;
						}
					}
					else if (IsTileTrigger(num160))
					{
						if (num162 < num152)
						{
							num152 = num162;
							num153 = num159;
							num154 = num160;
						}
					}
					else if (IsGroundTrigger(num160) && num162 < num155)
					{
						num155 = num162;
						num156 = num159;
						num157 = num160;
					}
				}
				else if (processedColorTriggers.Contains(num159))
				{
					processedColorTriggers.Remove(num159);
				}
			}
			try
			{
				if (num150.HasValue && num151.HasValue)
				{
					Color value4 = (backgroundTint = ColorFromTrigger(num151.Value));
					backgroundForceSolidBlack = num151.Value == 143;
					processedColorTriggers.Add(num150.Value);
					if (enableSimulatorDebugLogging && !triggerLogged.Contains(num150.Value))
					{
						WriteTempLog($"Simulator: Applied background trigger at idx={num150.Value} sid=0x{num151.Value:X} color={value4}");
						triggerLogged.Add(num150.Value);
					}
				}
				if (num153.HasValue && num154.HasValue)
				{
					Color value5 = (tileTint = ColorFromTrigger(num154.Value));
					processedColorTriggers.Add(num153.Value);
					if (enableSimulatorDebugLogging && !triggerLogged.Contains(num153.Value))
					{
						WriteTempLog($"Simulator: Applied tile trigger at idx={num153.Value} sid=0x{num154.Value:X} color={value5}");
						triggerLogged.Add(num153.Value);
					}
				}
				if (num156.HasValue && num157.HasValue)
				{
					Color value6 = ColorFromTrigger(num157.Value);
					if (num157.Value == 207)
					{
						groundTint = Color.FromArgb(byte.MaxValue, 0, 0, 0);
					}
					else
					{
						groundTint = value6;
					}
					processedColorTriggers.Add(num156.Value);
					if (enableSimulatorDebugLogging && !triggerLogged.Contains(num156.Value))
					{
						WriteTempLog($"Simulator: Applied ground trigger at idx={num156.Value} sid=0x{num157.Value:X} color={value6}");
						triggerLogged.Add(num156.Value);
					}
				}
			}
			catch
			{
			}
			try
			{
				Color outlineTint = (startupTintApplied ? Color.FromArgb(0, 0, 0, 0) : tileTint);
				bool flag14 = !AreColorsEqual(a, backgroundTint);
				bool flag15 = !AreColorsEqual(a2, tileTint);
				bool flag16 = !AreColorsEqual(a3, groundTint);
				if (flag14 || flag15 || flag16)
				{
					Color color = (startupTintApplied ? Color.FromArgb(0, 0, 0, 0) : tileTint);
					if (flag15)
					{
						UpdateTonedImagesForTileTint(tileTint, color);
					}
					try
					{
						if (flag14 || flag13 != backgroundForceSolidBlack)
						{
							if (backgroundForceSolidBlack)
							{
								parallaxTonedImages = null;
							}
							else if (backgroundTint.A == byte.MaxValue && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
							{
								parallaxTonedImages = CreateBlackMaskedImages(parallaxImages);
							}
							else
							{
								parallaxTonedImages = CreateHueShiftedImages(parallaxImages, backgroundTint);
							}
						}
					}
					catch
					{
						parallaxTonedImages = parallaxImages;
					}
					try
					{
						if (flag16 || flag15)
						{
							if (groundTint.A == byte.MaxValue && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
							{
								try
								{
									groundTonedImages = CreateBlackMaskedExceptColorArray(groundImages, playerPlaceholderGreen, color, recolorOutline: false);
								}
								catch
								{
									groundTonedImages = CreateTwoToneTileImages(groundImages, Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(byte.MaxValue, 0, 0, 0), color);
								}
							}
							else
							{
								groundTonedImages = CreateHueShiftedImages(groundImages, groundTint, color);
							}
						}
					}
					catch
					{
						groundTonedImages = groundImages;
					}
					try
					{
						if (outlineTint.A > 0 && groundTonedImages != null)
						{
							ImageSource[] array3 = CreateOutlineTintedTileImages(groundTonedImages, outlineTint);
							if (array3 != null)
							{
								groundTonedImages = array3;
							}
						}
					}
					catch
					{
					}
					try
					{
						if (parallaxBitmap != null && (flag14 || flag13 != backgroundForceSolidBlack))
						{
							ImageSource[] array4 = null;
							array4 = ((backgroundTint.A != byte.MaxValue || backgroundTint.R != 0 || backgroundTint.G != 0 || backgroundTint.B != 0) ? CreateHueShiftedImages(new ImageSource[1] { parallaxBitmap }, backgroundTint) : CreateBlackMaskedImages(new ImageSource[1] { parallaxBitmap }));
							if (array4 != null && array4.Length != 0 && array4[0] != null)
							{
								parallaxBitmapToned = array4[0];
							}
							else
							{
								parallaxBitmapToned = parallaxBitmap;
							}
						}
					}
					catch
					{
						parallaxBitmapToned = parallaxBitmap;
					}
					if (flag14)
					{
						try
						{
							if (backgroundTint.A == byte.MaxValue && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
							{
								sawFrame1TilesTinted = CreateSolidBlackImages(sawFrame1TilesOrig);
								sawFrame2TilesTinted = CreateSolidBlackImages(sawFrame2TilesOrig);
								smallSawFrame1TilesTinted = CreateSolidBlackImages(smallSawFrame1TilesOrig);
								smallSawFrame2TilesTinted = CreateSolidBlackImages(smallSawFrame2TilesOrig);
								largeSawFrame1TilesTinted = CreateSolidBlackImages(largeSawFrame1TilesOrig);
								largeSawFrame2TilesTinted = CreateSolidBlackImages(largeSawFrame2TilesOrig);
							}
							else
							{
								sawFrame1TilesTinted = CreateHslShiftedImages(sawFrame1TilesOrig, backgroundTint, color);
								sawFrame2TilesTinted = CreateHslShiftedImages(sawFrame2TilesOrig, backgroundTint, color);
								smallSawFrame1TilesTinted = CreateHslShiftedImages(smallSawFrame1TilesOrig, backgroundTint, color);
								smallSawFrame2TilesTinted = CreateHslShiftedImages(smallSawFrame2TilesOrig, backgroundTint, color);
								largeSawFrame1TilesTinted = CreateHslShiftedImages(largeSawFrame1TilesOrig, backgroundTint, color);
								largeSawFrame2TilesTinted = CreateHslShiftedImages(largeSawFrame2TilesOrig, backgroundTint, color);
							}
						}
						catch
						{
						}
					}
					tileLayerCache = null;
					try
					{
						groundTintedTileCache.Clear();
					}
					catch
					{
					}
					try
					{
						spriteBackgroundCompositeCache.Clear();
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			if (num122.HasValue)
			{
				currentSpeed_fixed = num122.Value;
				playerVelX_fixed = num122.Value;
				if (num122.Value == 571)
				{
					speed = 0;
				}
				else if (num122.Value == 708)
				{
					speed = 1;
				}
				else if (num122.Value == 881)
				{
					speed = 2;
				}
				else if (num122.Value == 1065)
				{
					speed = 3;
				}
				else if (num122.Value == 1310)
				{
					speed = 4;
				}
				try
				{
					base.Dispatcher?.BeginInvoke((Action)delegate
					{
						if (SpeedComboBox != null && speed >= 0 && speed < SpeedComboBox.Items.Count)
						{
							SpeedComboBox.SelectedIndex = speed;
						}
					});
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		RenderFrame();
		static bool ProvidesFloorAtColumn(MetatileCollision col4, int localX2, out int topOffsetPx3)
		{
			return ProvidesFloorAtColumnStatic(col4, localX2, out topOffsetPx3);
		}
	}

	private void RenderFrame()
	{
		if (windowClosed)
		{
			return;
		}
		if (currentGameMode == 6)
		{
			try
			{
				string text = (miniMode ? "wave-mini.png" : "wave.png");
				if (playerVelY_fixed == 0)
				{
					text = (miniMode ? "wave-mini2.png" : "wave2.png");
				}
				string text2 = (playerImage?.Tag as string) ?? "";
				if (!text2.Equals(text, StringComparison.OrdinalIgnoreCase))
				{
					BitmapImage bitmapImage = LoadCachedResourceImage(text);
					if (bitmapImage != null && playerImage != null)
					{
						playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage) ?? bitmapImage;
						playerImage.Tag = text;
					}
				}
				UpdatePlayerIconFlip();
			}
			catch
			{
			}
		}
		if (currentGameMode == 1)
		{
			try
			{
				int shipSpriteFrame = GetShipSpriteFrame();
				int[] array = new int[8] { 0, 0, 1, 2, 3, 4, 5, 6 };
				int num = array[shipSpriteFrame & 7];
				string text4;
				if (miniMode)
				{
					if (1 == 0)
					{
					}
					string text3 = num switch
					{
						0 => "ship-mini.png", 
						1 => "ship-mini1.png", 
						2 => "ship-mini2.png", 
						3 => "ship-mini3.png", 
						4 => "ship-mini4.png", 
						5 => "ship-mini5.png", 
						6 => "ship-mini6.png", 
						_ => "ship-mini.png", 
					};
					if (1 == 0)
					{
					}
					text4 = text3;
				}
				else
				{
					if (1 == 0)
					{
					}
					string text3 = num switch
					{
						0 => "ship.png", 
						1 => "ship2.png", 
						2 => "ship3.png", 
						3 => "ship4.png", 
						4 => "ship5.png", 
						5 => "ship6.png", 
						6 => "ship7.png", 
						_ => "ship.png", 
					};
					if (1 == 0)
					{
					}
					text4 = text3;
				}
				string text5 = text4;
				string text6 = (playerImage?.Tag as string) ?? "";
				if (!text6.Equals(text5, StringComparison.OrdinalIgnoreCase))
				{
					BitmapImage bitmapImage2 = LoadCachedResourceImage(text5);
					if (bitmapImage2 != null && playerImage != null)
					{
						playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage2) ?? bitmapImage2;
						playerImage.Tag = text5;
					}
				}
				UpdatePlayerIconFlip();
			}
			catch
			{
			}
		}
		if (currentGameMode == 3)
		{
			try
			{
				UpdatePlayerIconFlip();
			}
			catch
			{
			}
		}
		if (currentGameMode == 7)
		{
			try
			{
				int swingcopterSpriteFrame = GetSwingcopterSpriteFrame();
				int[] array2 = new int[8] { 0, 0, 1, 2, 2, 3, 4, 4 };
				int num2 = array2[swingcopterSpriteFrame & 7];
				string text7;
				if (miniMode)
				{
					if (1 == 0)
					{
					}
					string text3 = num2 switch
					{
						0 => "swingcopter-mini.png", 
						1 => "swingcopter-mini1.png", 
						2 => "swingcopter-mini2.png", 
						3 => "swingcopter-mini3.png", 
						4 => "swingcopter-mini4.png", 
						_ => "swingcopter-mini.png", 
					};
					if (1 == 0)
					{
					}
					text7 = text3;
				}
				else
				{
					if (1 == 0)
					{
					}
					string text3 = num2 switch
					{
						0 => "swingcopter.png", 
						1 => "swingcopter1.png", 
						2 => "swingcopter2.png", 
						3 => "swingcopter3.png", 
						4 => "swingcopter4.png", 
						_ => "swingcopter.png", 
					};
					if (1 == 0)
					{
					}
					text7 = text3;
				}
				string text8 = text7;
				string text9 = (playerImage?.Tag as string) ?? "";
				if (!text9.Equals(text8, StringComparison.OrdinalIgnoreCase))
				{
					BitmapImage bitmapImage3 = LoadCachedResourceImage(text8);
					if (bitmapImage3 != null && playerImage != null)
					{
						playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage3) ?? bitmapImage3;
						playerImage.Tag = text8;
					}
				}
				UpdatePlayerIconFlip();
			}
			catch
			{
			}
		}
		if (currentGameMode == 10)
		{
			try
			{
				UpdatePlayerIconFlip();
			}
			catch
			{
			}
		}
		if (currentGameMode == 11)
		{
			try
			{
				int footballSpriteFrameAndFlip = GetFootballSpriteFrameAndFlip();
				int num3 = footballSpriteFrameAndFlip & 0xF;
				int num4 = footballSpriteFrameAndFlip & 0xC0;
				string text10 = (miniMode ? ("football-mini" + ((num3 > 0) ? num3.ToString() : "") + ".png") : ("football" + ((num3 > 0) ? num3.ToString() : "") + ".png"));
				string text11 = (playerImage?.Tag as string) ?? "";
				if (!text11.Equals(text10, StringComparison.OrdinalIgnoreCase))
				{
					BitmapImage bitmapImage4 = LoadCachedResourceImage(text10);
					if (bitmapImage4 != null && playerImage != null)
					{
						playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage4) ?? bitmapImage4;
						playerImage.Tag = text10;
					}
				}
				if (playerImage != null)
				{
					bool flag = (num4 & 0x40) != 0;
					bool flag2 = (num4 & 0x80) != 0;
					if (gravityReversed)
					{
						flag2 = !flag2;
					}
					playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
					playerImage.RenderTransform = new ScaleTransform((!flag) ? 1 : (-1), (!flag2) ? 1 : (-1));
				}
			}
			catch
			{
			}
		}
		if (currentGameMode == 9)
		{
			try
			{
				UpdatePlayerIconFlip();
			}
			catch
			{
			}
		}
		if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8)
		{
			try
			{
				if (!miniMode)
				{
					int cubeSpriteFrame = GetCubeSpriteFrame();
					int num5 = cubeSpriteFrame & 7;
					bool flag3 = (cubeSpriteFrame & 0x40) != 0;
					bool flag4 = (cubeSpriteFrame & 0x80) != 0;
					string[] array3 = ((currentGameMode == 8) ? s_ninjaFrameNames : s_cubeFrameNames);
					if (num5 >= array3.Length)
					{
						num5 = 0;
					}
					string text12 = array3[num5];
					string text13 = (playerImage?.Tag as string) ?? "";
					if (!text13.Equals(text12, StringComparison.OrdinalIgnoreCase) && playerImage != null)
					{
						BitmapImage bitmapImage5 = LoadCachedResourceImage(text12);
						if (bitmapImage5 != null)
						{
							playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage5) ?? bitmapImage5;
							playerImage.Tag = text12;
						}
						else
						{
							string path = AppDomain.CurrentDomain.BaseDirectory ?? ".";
							string text14 = System.IO.Path.Combine(path, text12);
							if (File.Exists(text14))
							{
								BitmapImage bitmapImage6 = new BitmapImage();
								bitmapImage6.BeginInit();
								bitmapImage6.UriSource = new Uri(text14);
								bitmapImage6.CacheOption = BitmapCacheOption.OnLoad;
								bitmapImage6.EndInit();
								bitmapImage6.Freeze();
								playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage6) ?? bitmapImage6;
								playerImage.Tag = text12;
							}
							else
							{
								string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(path, "..\\..\\..\\..\\" + text12));
								if (File.Exists(fullPath))
								{
									BitmapImage bitmapImage7 = new BitmapImage();
									bitmapImage7.BeginInit();
									bitmapImage7.UriSource = new Uri(fullPath);
									bitmapImage7.CacheOption = BitmapCacheOption.OnLoad;
									bitmapImage7.EndInit();
									bitmapImage7.Freeze();
									playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage7) ?? bitmapImage7;
									playerImage.Tag = text12;
								}
							}
						}
					}
					if (playerImage != null)
					{
						if (flag3 || flag4)
						{
							playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
							playerImage.RenderTransform = new ScaleTransform((!flag3) ? 1 : (-1), (!flag4) ? 1 : (-1));
						}
						else
						{
							playerImage.RenderTransform = Transform.Identity;
						}
					}
				}
				else
				{
					int cubeSpriteMiniFrame = GetCubeSpriteMiniFrame();
					int num6 = (cubeRotateMini_fixed >> 8) & 0xFF;
					if (num6 < 0 || num6 >= 24)
					{
						num6 = 0;
					}
					int num7 = drawcube_sprite_table[num6];
					bool flag5 = (num7 & 0x40) != 0;
					bool flag6 = (num7 & 0x80) != 0;
					string[] array4 = ((currentGameMode != 8) ? new string[5] { "cube_mini_00_frame_0.png", "cube_mini_01_frame_1.png", "cube_mini_02_frame_2.png", "cube_mini_03_frame_3.png", "cube_mini_04_frame_4.png" } : new string[5] { "ninja_mini_00_frame_0.png", "ninja_mini_01_frame_1.png", "ninja_mini_02_frame_2.png", "ninja_mini_03_frame_3.png", "ninja_mini_04_frame_4.png" });
					string text15 = array4[cubeSpriteMiniFrame];
					string text16 = (playerImage?.Tag as string) ?? "";
					if (!text16.Equals(text15, StringComparison.OrdinalIgnoreCase) && playerImage != null)
					{
						BitmapImage bitmapImage8 = LoadCachedResourceImage(text15);
						if (bitmapImage8 != null)
						{
							playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage8) ?? bitmapImage8;
							playerImage.Tag = text15;
						}
						else
						{
							string path2 = AppDomain.CurrentDomain.BaseDirectory ?? ".";
							string text17 = System.IO.Path.Combine(path2, text15);
							if (File.Exists(text17))
							{
								BitmapImage bitmapImage9 = new BitmapImage();
								bitmapImage9.BeginInit();
								bitmapImage9.UriSource = new Uri(text17);
								bitmapImage9.CacheOption = BitmapCacheOption.OnLoad;
								bitmapImage9.EndInit();
								bitmapImage9.Freeze();
								playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage9) ?? bitmapImage9;
								playerImage.Tag = text15;
							}
							else
							{
								string fullPath2 = System.IO.Path.GetFullPath(System.IO.Path.Combine(path2, "..\\..\\..\\..\\" + text15));
								if (File.Exists(fullPath2))
								{
									BitmapImage bitmapImage10 = new BitmapImage();
									bitmapImage10.BeginInit();
									bitmapImage10.UriSource = new Uri(fullPath2);
									bitmapImage10.CacheOption = BitmapCacheOption.OnLoad;
									bitmapImage10.EndInit();
									bitmapImage10.Freeze();
									playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage10) ?? bitmapImage10;
									playerImage.Tag = text15;
								}
							}
						}
					}
					if (playerImage != null)
					{
						if (flag5 || flag6)
						{
							playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
							playerImage.RenderTransform = new ScaleTransform((!flag5) ? 1 : (-1), (!flag6) ? 1 : (-1));
						}
						else
						{
							playerImage.RenderTransform = Transform.Identity;
						}
					}
				}
			}
			catch
			{
			}
		}
		if (currentGameMode == 9)
		{
			try
			{
				string text18 = (miniMode ? "pogo-mini2.png" : "pogo2.png");
				string text19 = (miniMode ? "pogo-mini.png" : "pogo.png");
				string text20 = ((pogoBounceAnimationCounter > 0) ? text18 : text19);
				AppendSimDebug($"[POGO_ICON] bounceCounter={pogoBounceAnimationCounter}, choice={text20}");
				if (!paused && pogoBounceAnimationCounter > 0)
				{
					pogoBounceAnimationFrameAccum += simTimeScale;
					if (pogoBounceAnimationFrameAccum >= 1.0)
					{
						pogoBounceAnimationCounter--;
						pogoBounceAnimationFrameAccum -= 1.0;
					}
				}
				else if (!paused)
				{
					pogoBounceAnimationFrameAccum = 0.0;
				}
				string text21 = (playerImage?.Tag as string) ?? "";
				if (!text21.Equals(text20, StringComparison.OrdinalIgnoreCase) && playerImage != null)
				{
					BitmapImage bitmapImage11 = LoadCachedResourceImage(text20);
					if (bitmapImage11 != null)
					{
						playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage11) ?? bitmapImage11;
						playerImage.Tag = text20;
					}
					else
					{
						string path3 = AppDomain.CurrentDomain.BaseDirectory ?? ".";
						string text22 = System.IO.Path.Combine(path3, text20);
						if (File.Exists(text22))
						{
							BitmapImage bitmapImage12 = new BitmapImage();
							bitmapImage12.BeginInit();
							bitmapImage12.UriSource = new Uri(text22);
							bitmapImage12.CacheOption = BitmapCacheOption.OnLoad;
							bitmapImage12.EndInit();
							bitmapImage12.Freeze();
							playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage12) ?? bitmapImage12;
							playerImage.Tag = text20;
						}
						else
						{
							string fullPath3 = System.IO.Path.GetFullPath(System.IO.Path.Combine(path3, "..\\..\\..\\..\\" + text20));
							if (File.Exists(fullPath3))
							{
								BitmapImage bitmapImage13 = new BitmapImage();
								bitmapImage13.BeginInit();
								bitmapImage13.UriSource = new Uri(fullPath3);
								bitmapImage13.CacheOption = BitmapCacheOption.OnLoad;
								bitmapImage13.EndInit();
								bitmapImage13.Freeze();
								playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage13) ?? bitmapImage13;
								playerImage.Tag = text20;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				AppendSimDebug("[POGO_ICON] Exception: " + ex.Message);
			}
		}
		if (currentGameMode == 2 && !miniMode)
		{
			try
			{
				if (!paused)
				{
					ballAnimationFrameAccum += simTimeScale;
					if (ballAnimationFrameAccum >= 1.0)
					{
						ballAnimationFrameCounter++;
						if (ballAnimationFrameCounter >= 12)
						{
							ballAnimationFrameCounter = 0;
						}
						ballAnimationFrameAccum -= 1.0;
					}
				}
				string text23 = ((ballAnimationFrameCounter < 6) ? "ball.png" : "ball2.png");
				AppendSimDebug($"[BALL_ANIM] counter={ballAnimationFrameCounter}, accum={ballAnimationFrameAccum:F2}, simTimeScale={simTimeScale}, choice={text23}");
				string text24 = (playerImage?.Tag as string) ?? "";
				if (!text24.Equals(text23, StringComparison.OrdinalIgnoreCase))
				{
					BitmapImage bitmapImage14 = LoadCachedResourceImage("." + text23);
					if (bitmapImage14 != null && playerImage != null)
					{
						playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage14) ?? bitmapImage14;
						playerImage.Tag = text23;
					}
					else
					{
						string path4 = AppDomain.CurrentDomain.BaseDirectory ?? ".";
						string text25 = System.IO.Path.Combine(path4, text23);
						if (File.Exists(text25))
						{
							BitmapImage bitmapImage15 = new BitmapImage();
							bitmapImage15.BeginInit();
							bitmapImage15.UriSource = new Uri(text25);
							bitmapImage15.CacheOption = BitmapCacheOption.OnLoad;
							bitmapImage15.EndInit();
							bitmapImage15.Freeze();
							if (playerImage != null)
							{
								playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage15) ?? bitmapImage15;
							}
							if (playerImage != null)
							{
								playerImage.Tag = text23;
							}
						}
						else
						{
							string fullPath4 = System.IO.Path.GetFullPath(System.IO.Path.Combine(path4, "..\\..\\..\\..\\" + text23));
							if (File.Exists(fullPath4))
							{
								BitmapImage bitmapImage16 = new BitmapImage();
								bitmapImage16.BeginInit();
								bitmapImage16.UriSource = new Uri(fullPath4);
								bitmapImage16.CacheOption = BitmapCacheOption.OnLoad;
								bitmapImage16.EndInit();
								bitmapImage16.Freeze();
								if (playerImage != null)
								{
									playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage16) ?? bitmapImage16;
								}
								if (playerImage != null)
								{
									playerImage.Tag = text23;
								}
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		else
		{
			ballAnimationFrameCounter = 0;
			ballAnimationFrameAccum = 0.0;
		}
		if (currentGameMode == 4)
		{
			try
			{
				bool flag7 = Math.Abs(playerVelY_fixed) > 256;
				bool flag8 = !flag7;
				if (flag8 && !paused)
				{
					robotAnimationFrameAccum += simTimeScale;
					if (robotAnimationFrameAccum >= 1.0)
					{
						robotAnimationFrameCounter++;
						if (robotAnimationFrameCounter >= 80)
						{
							robotAnimationFrameCounter = 0;
						}
						robotAnimationFrameAccum -= 1.0;
					}
				}
				else if (flag7)
				{
					robotAnimationFrameCounter = 0;
					robotAnimationFrameAccum = 0.0;
				}
				string text26;
				if (flag8)
				{
					int num8 = robotAnimationFrameCounter / 20;
					if (1 == 0)
					{
					}
					string text3 = num8 switch
					{
						0 => miniMode ? "robot-mini.png" : "robot.png", 
						1 => miniMode ? "robot-mini2.png" : "robot2.png", 
						2 => miniMode ? "robot-mini3.png" : "robot3.png", 
						3 => miniMode ? "robot-mini4.png" : "robot4.png", 
						_ => miniMode ? "robot-mini.png" : "robot.png", 
					};
					if (1 == 0)
					{
					}
					text26 = text3;
				}
				else
				{
					text26 = (miniMode ? "robot-mini-jump.png" : "robotjump.png");
				}
				string text27 = (playerImage?.Tag as string) ?? "";
				if (!text27.Equals(text26, StringComparison.OrdinalIgnoreCase))
				{
					BitmapImage bitmapImage17 = LoadCachedResourceImage("." + text26);
					if (bitmapImage17 == null)
					{
						string path5 = AppDomain.CurrentDomain.BaseDirectory ?? ".";
						string text28 = System.IO.Path.Combine(path5, text26);
						if (File.Exists(text28))
						{
							bitmapImage17 = new BitmapImage();
							bitmapImage17.BeginInit();
							bitmapImage17.UriSource = new Uri(text28);
							bitmapImage17.CacheOption = BitmapCacheOption.OnLoad;
							bitmapImage17.EndInit();
							bitmapImage17.Freeze();
						}
						else
						{
							string fullPath5 = System.IO.Path.GetFullPath(System.IO.Path.Combine(path5, "..\\..\\..\\..\\" + text26));
							if (File.Exists(fullPath5))
							{
								bitmapImage17 = new BitmapImage();
								bitmapImage17.BeginInit();
								bitmapImage17.UriSource = new Uri(fullPath5);
								bitmapImage17.CacheOption = BitmapCacheOption.OnLoad;
								bitmapImage17.EndInit();
								bitmapImage17.Freeze();
							}
						}
					}
					if (bitmapImage17 != null && playerImage != null)
					{
						playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage17) ?? bitmapImage17;
						playerImage.Tag = text26;
						playerImage.Width = bitmapImage17.PixelWidth;
						playerImage.Height = bitmapImage17.PixelHeight;
						if ((text26 == "robot2.png" || text26 == "robot4.png") && playerImage != null)
						{
							TransformGroup transformGroup = new TransformGroup();
							TranslateTransform value = new TranslateTransform(-8.0, 0.0);
							ScaleTransform value2 = (gravityReversed ? new ScaleTransform(1.0, -1.0) : new ScaleTransform(1.0, 1.0));
							transformGroup.Children.Add(value);
							transformGroup.Children.Add(value2);
							playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
							playerImage.RenderTransform = transformGroup;
						}
						else if (playerImage != null)
						{
							if (gravityReversed)
							{
								playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
								playerImage.RenderTransform = new ScaleTransform(1.0, -1.0);
							}
							else
							{
								playerImage.RenderTransform = Transform.Identity;
							}
						}
					}
				}
			}
			catch
			{
			}
		}
		else
		{
			robotAnimationFrameCounter = 0;
			robotAnimationFrameAccum = 0.0;
		}
		if (currentGameMode == 5)
		{
			try
			{
				bool flag9 = onGround || groundStabilizeCounter > 0;
				if (flag9 && !paused)
				{
					spiderAnimationFrameAccum += simTimeScale;
					if (spiderAnimationFrameAccum >= 1.0)
					{
						spiderAnimationFrameCounter++;
						if (spiderAnimationFrameCounter >= 80)
						{
							spiderAnimationFrameCounter = 0;
						}
						spiderAnimationFrameAccum -= 1.0;
					}
				}
				else if (!flag9)
				{
					spiderAnimationFrameCounter = 0;
					spiderAnimationFrameAccum = 0.0;
				}
				bool flag10 = Math.Abs(playerVelY_fixed) > 256;
				string text29;
				if (flag9 && !flag10)
				{
					int num9 = spiderAnimationFrameCounter / 20;
					if (1 == 0)
					{
					}
					string text3 = num9 switch
					{
						0 => miniMode ? "spider-mini.png" : "spider.png", 
						1 => miniMode ? "spider-mini2.png" : "spider2.png", 
						2 => miniMode ? "spider-mini3.png" : "spider3.png", 
						3 => miniMode ? "spider-mini4.png" : "spider4.png", 
						_ => miniMode ? "spider-mini.png" : "spider.png", 
					};
					if (1 == 0)
					{
					}
					text29 = text3;
				}
				else
				{
					text29 = (miniMode ? "spider-mini-jump.png" : "spiderjump.png");
				}
				try
				{
					if (playerImage != null)
					{
						string text30 = (playerImage.Tag as string) ?? "";
						if (!text30.Equals(text29, StringComparison.OrdinalIgnoreCase))
						{
							BitmapImage bitmapImage18 = LoadCachedResourceImage("." + text29);
							if (bitmapImage18 != null)
							{
								playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage18) ?? bitmapImage18;
								playerImage.Tag = text29;
								playerImage.Width = bitmapImage18.PixelWidth;
								playerImage.Height = bitmapImage18.PixelHeight;
							}
						}
					}
				}
				catch
				{
				}
				if (playerImage != null && (text29 == "spider.png" || text29 == "spider2.png" || text29 == "spider3.png"))
				{
					playerImage.RenderTransformOrigin = new Point(0.0, 0.5);
					TransformGroup transformGroup2 = new TransformGroup();
					transformGroup2.Children.Add(new TranslateTransform(-8.0, 0.0));
					if (gravityFlipped)
					{
						transformGroup2.Children.Add(new ScaleTransform(1.0, -1.0));
					}
					playerImage.RenderTransform = transformGroup2;
				}
				else if (playerImage != null && gravityFlipped)
				{
					playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
					playerImage.RenderTransform = new ScaleTransform(1.0, -1.0);
				}
				else if (playerImage != null)
				{
					playerImage.RenderTransform = Transform.Identity;
				}
			}
			catch
			{
			}
		}
		else
		{
			spiderAnimationFrameCounter = 0;
			spiderAnimationFrameAccum = 0.0;
		}
		try
		{
			renderFrameCounter++;
		}
		catch
		{
			renderFrameCounter = 1;
		}
		try
		{
			if (!simDebugLoggedFirstFrame)
			{
				simDebugLoggedFirstFrame = true;
				string value3 = "none";
				int value4 = 0;
				int value5 = 0;
				try
				{
					ImageSource imageSource = null;
					if (parallaxBitmapToned != null)
					{
						imageSource = parallaxBitmapToned;
					}
					else if (parallaxBitmap != null)
					{
						imageSource = parallaxBitmap;
					}
					else if (parallaxTonedImages != null && parallaxTonedImages.Length == ((parallaxImages != null) ? parallaxImages.Length : 0) && parallaxTonedImages.Length != 0)
					{
						imageSource = parallaxTonedImages[0];
					}
					else if (parallaxImages != null && parallaxImages.Length != 0)
					{
						imageSource = parallaxImages[0];
					}
					if (imageSource is BitmapSource bitmapSource)
					{
						value3 = bitmapSource.GetType().Name;
						value4 = bitmapSource.PixelWidth;
						value5 = bitmapSource.PixelHeight;
					}
					else if (imageSource != null)
					{
						value3 = imageSource.GetType().Name;
					}
				}
				catch
				{
				}
				double value6 = (double)(-(cameraX_fixed >> 8)) * (1.0 - parallaxX);
				double value7 = (double)(-(cameraY_fixed >> 8)) * (1.0 - parallaxY);
				string value8 = ((tileLayerImage?.Source == null) ? "null" : tileLayerImage.Source.GetType().Name);
				AppendSimDebug($"RenderFrame: hasParallaxLayer={hasParallaxLayer}, chosenSrc={value3}, srcW={value4}, srcH={value5}, parallaxImagesLen={((parallaxImages != null) ? parallaxImages.Length : 0)}, parallaxTonedLen={((parallaxTonedImages != null) ? parallaxTonedImages.Length : 0)}, bgRectFill={((bgRectPersistent?.Fill == null) ? "null" : bgRectPersistent.Fill.GetType().Name)}, backgroundTint={backgroundTint}, parallaxOffsetX={value6}, parallaxOffsetY={value7}, tileLayerImageSource={value8}");
				try
				{
					StringBuilder stringBuilder = new StringBuilder();
					stringBuilder.AppendLine("RenderCanvas children:");
					for (int i = 0; i < RenderCanvas.Children.Count; i++)
					{
						UIElement uIElement = RenderCanvas.Children[i];
						int value9 = 0;
						try
						{
							value9 = Panel.GetZIndex(uIElement);
						}
						catch
						{
						}
						string value10 = uIElement?.GetType().Name ?? "null";
						double value11 = 0.0;
						double value12 = 0.0;
						try
						{
							value11 = (uIElement as FrameworkElement)?.Width ?? double.NaN;
							value12 = (uIElement as FrameworkElement)?.Height ?? double.NaN;
						}
						catch
						{
						}
						StringBuilder stringBuilder2 = stringBuilder;
						StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(37, 6, stringBuilder2);
						handler.AppendLiteral("  [");
						handler.AppendFormatted(i);
						handler.AppendLiteral("] Type=");
						handler.AppendFormatted(value10);
						handler.AppendLiteral(" Z=");
						handler.AppendFormatted(value9);
						handler.AppendLiteral(" Width=");
						handler.AppendFormatted(value11);
						handler.AppendLiteral(" Height=");
						handler.AppendFormatted(value12);
						handler.AppendLiteral(" Visible=");
						handler.AppendFormatted(!(uIElement is FrameworkElement frameworkElement) || frameworkElement.Visibility == Visibility.Visible);
						stringBuilder2.AppendLine(ref handler);
					}
					AppendSimDebug(stringBuilder.ToString());
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		int num10;
		int num11;
		int num12;
		int num13;
		int num14;
		bool flag11;
		bool flag12;
		lock (simLock)
		{
			num10 = cameraX_fixed;
			num11 = cameraY_fixed;
			num12 = playerX_fixed;
			num13 = playerY_fixed;
			num14 = playerVelY_fixed;
			flag11 = miniMode;
			flag12 = gravityFlipped;
		}
		int num15 = num10 >> 8;
		int num16 = num10 & 0xFF;
		int num17 = num15 / 16;
		int num18 = num15 % 16;
		double num19 = (double)num16 / 256.0;
		int num20 = num11 >> 8;
		int num21 = num11 & 0xFF;
		double num22 = (double)num21 / 256.0;
		int num23 = 0;
		if (hasGroundLayer && groundTileRows > 0)
		{
			num23 = Math.Min(3, groundTileRows);
		}
		int num24 = num23 * 16;
		int num25 = num20 / 16;
		int num26 = num20 % 16;
		try
		{
			if (bgRectPersistent != null && hasParallaxLayer && backgroundForceSolidBlack)
			{
				try
				{
					bgRectPersistent.Fill = new SolidColorBrush(Color.FromArgb(byte.MaxValue, 0, 0, 0));
				}
				catch
				{
					bgRectPersistent.Fill = Brushes.Black;
				}
			}
			else if (bgRectPersistent != null && hasParallaxLayer && parallaxImages != null && parallaxImages.Length != 0)
			{
				ImageSource imageSource2 = null;
				if (parallaxBitmapToned != null)
				{
					imageSource2 = parallaxBitmapToned;
				}
				else if (parallaxBitmap != null)
				{
					imageSource2 = parallaxBitmap;
				}
				else if (parallaxTonedImages != null && parallaxTonedImages.Length == parallaxImages.Length && parallaxTonedImages[0] != null)
				{
					imageSource2 = parallaxTonedImages[0];
				}
				else if (parallaxImages != null && parallaxImages.Length != 0)
				{
					imageSource2 = parallaxImages[0];
				}
				if (imageSource2 is BitmapSource bitmapSource2)
				{
					double num27 = (double)(-num15) * (1.0 - parallaxX);
					double num28 = (double)(-(num11 >> 8)) * (1.0 - parallaxY);
					if (_cachedParallaxBrush == null || _cachedParallaxSource != imageSource2)
					{
						double width = Math.Max(1.0, bitmapSource2.PixelWidth);
						double height = Math.Max(1.0, bitmapSource2.PixelHeight);
						ImageSource image = App.EnsureUnfrozenForRender(imageSource2) ?? imageSource2;
						_cachedParallaxTransform = new TranslateTransform(num27, num28);
						_cachedParallaxBrush = new ImageBrush(image)
						{
							TileMode = TileMode.Tile,
							ViewportUnits = BrushMappingMode.Absolute,
							Viewport = new Rect(0.0, 0.0, width, height),
							Stretch = Stretch.None,
							Transform = _cachedParallaxTransform
						};
						_cachedParallaxSource = imageSource2;
						bgRectPersistent.Fill = _cachedParallaxBrush;
					}
					else
					{
						_cachedParallaxTransform.X = num27;
						_cachedParallaxTransform.Y = num28;
					}
				}
				else
				{
					try
					{
						BitmapImage bitmapImage19 = LoadCachedResourceImage("Assets.parallax.bmp") ?? LoadCachedResourceImage("parallax Blue.bmp") ?? LoadCachedResourceImage("parallax.bmp");
						if (bitmapImage19 != null)
						{
							parallaxBitmap = bitmapImage19;
							parallaxImages = new ImageSource[1] { bitmapImage19 };
							parallaxTonedImages = null;
							hasParallaxLayer = true;
							BitmapSource image2 = App.EnsureUnfrozenForRender(bitmapImage19) ?? bitmapImage19;
							ImageBrush imageBrush = new ImageBrush(image2);
							imageBrush.TileMode = TileMode.Tile;
							imageBrush.ViewportUnits = BrushMappingMode.Absolute;
							imageBrush.Viewport = new Rect(0.0, 0.0, Math.Max(1.0, bitmapImage19.PixelWidth), Math.Max(1.0, bitmapImage19.PixelHeight));
							imageBrush.Stretch = Stretch.None;
							ImageBrush imageBrush2 = imageBrush;
							double offsetX = (double)(-num15) * (1.0 - parallaxX);
							double offsetY = (double)(-(num11 >> 8)) * (1.0 - parallaxY);
							imageBrush2.Transform = new TranslateTransform(offsetX, offsetY);
							if (bgRectPersistent != null)
							{
								bgRectPersistent.Fill = imageBrush2;
							}
						}
					}
					catch
					{
					}
				}
			}
			else if (bgRectPersistent != null && (_cachedBgTintBrush == null || _cachedBgTintColor != backgroundTint))
			{
				_cachedBgTintColor = backgroundTint;
				_cachedBgTintBrush = new SolidColorBrush(backgroundTint);
				bgRectPersistent.Fill = _cachedBgTintBrush;
			}
		}
		catch
		{
		}
		try
		{
			if (tileLayerImage != null && (tileLayerCache == null || cachedStartTileX != num17 || cachedStartTileY != num25 || (lastCacheHadAnimatedTiles && lastCacheAnimationFrame != animationFrame)))
			{
				int num29 = 17;
				int num30 = 16;
				int num31 = num29 * 16;
				int num32 = num30 * 16;
				DrawingVisual drawingVisual = new DrawingVisual();
				bool value13 = false;
				using (DrawingContext drawingContext = drawingVisual.RenderOpen())
				{
					Brush brush = null;
					try
					{
						if (ShowTileHitboxes)
						{
							brush = new SolidColorBrush(Color.FromArgb(160, byte.MaxValue, 0, 0));
							try
							{
								brush.Freeze();
							}
							catch
							{
							}
						}
					}
					catch
					{
						brush = null;
					}
					int num33 = 16;
					int num34 = num23;
					int num35 = num33 - num34;
					int num36 = 1;
					if (groundImages != null && groundTileRows > 0)
					{
						num36 = Math.Max(1, groundImages.Length / groundTileRows);
					}
					for (int j = 0; j <= 16; j++)
					{
						int num37 = num17 + j;
						for (int k = 0; k <= 15; k++)
						{
							Rect rectangle = new Rect(j * 16, k * 16, 16.0, 16.0);
							int num38 = num25 + num34 + k;
							if (num38 >= mapHeight)
							{
								if (hasGroundLayer && groundImages != null && groundImages.Length != 0)
								{
									int val = num38 - mapHeight;
									int num39 = (num37 % num36 + num36) % num36;
									int num40 = Math.Max(0, Math.Min(groundTileRows - 1, val));
									int num41 = num40 * num36 + num39;
									ImageSource imageSource3 = null;
									if (groundTonedImages != null && num41 >= 0 && num41 < groundTonedImages.Length)
									{
										imageSource3 = groundTonedImages[num41];
									}
									if (imageSource3 == null && groundImages != null && num41 >= 0 && num41 < groundImages.Length)
									{
										imageSource3 = groundImages[num41];
									}
									if (imageSource3 != null)
									{
										if (imageSource3 is BitmapSource bitmapSource3)
										{
											double num42 = Math.Max(1.0, bitmapSource3.PixelWidth);
											double num43 = Math.Max(1.0, bitmapSource3.PixelHeight);
											if (num42 <= 16.0 && num43 <= 16.0)
											{
												double x = rectangle.X + (16.0 - num42) / 2.0;
												double y = rectangle.Y + (16.0 - num43);
												drawingContext.DrawImage(imageSource3, new Rect(x, y, num42, num43));
											}
											else
											{
												drawingContext.DrawImage(imageSource3, rectangle);
											}
										}
										else
										{
											drawingContext.DrawImage(imageSource3, rectangle);
										}
									}
									else
									{
										drawingContext.DrawRectangle(Brushes.Black, null, rectangle);
									}
								}
								else
								{
									drawingContext.DrawRectangle(Brushes.Black, null, rectangle);
								}
								continue;
							}
							if (num37 < 0 || num37 >= mapWidth || num38 < 0 || num38 >= mapHeight)
							{
								drawingContext.DrawRectangle(Brushes.Black, null, rectangle);
								continue;
							}
							int num44 = num38 * mapWidth + num37;
							int num45 = tiles[num44];
							int num46 = MapAnimatedTileIndex(num45);
							int idx = num46;
							idx = ResolveSimulatorTileIndex(idx);
							if (num46 != num45 || idx >= 1000)
							{
								value13 = true;
							}
							ImageSource imageSource4 = null;
							try
							{
								if (idx >= 1000)
								{
									bool flag13 = animationFrame * 9 / 20 % 2 != 0;
									int num47 = idx;
									if (tileTonedImages != null && idx >= 0 && idx < tileTonedImages.Length && tileTonedImages[idx] != null)
									{
										imageSource4 = tileTonedImages[idx];
									}
									else if (tileImages != null && idx >= 0 && idx < tileImages.Length && tileImages[idx] != null)
									{
										imageSource4 = tileImages[idx];
									}
									else if (num47 >= 1000 && num47 <= 1007)
									{
										int num48 = (num47 - 1000) % 4;
										int num49 = ((sawFrame1TilesTinted != null) ? sawFrame1TilesTinted.Length : 0);
										int num50 = ((sawFrame2TilesTinted != null) ? sawFrame2TilesTinted.Length : 0);
										int num51 = 4;
										int num52 = ((num49 < num51 || num49 % num51 != 0) ? 1 : (num49 / num51));
										int num53 = ((num50 < num51 || num50 % num51 != 0) ? 1 : (num50 / num51));
										int num54 = Math.Max(1, Math.Max(num52, num53));
										int num55 = animationFrame * 9 / 20 % num54;
										if (animationFrame * 9 / 20 % 2 == 1 && sawFrame2TilesTinted != null)
										{
											int num56 = ((num53 > 1) ? (num55 % num53 * num51 + num48) : num48);
											if (num56 >= 0 && num56 < num50)
											{
												imageSource4 = sawFrame2TilesTinted[num56];
											}
										}
										if (imageSource4 == null && sawFrame1TilesTinted != null)
										{
											int num57 = ((num52 > 1) ? (num55 % num52 * num51 + num48) : num48);
											if (num57 >= 0 && num57 < num49)
											{
												imageSource4 = sawFrame1TilesTinted[num57];
											}
										}
									}
									else if (num47 >= 1010 && num47 <= 1015)
									{
										int num58 = (num47 - 1010) % 3;
										int num59 = ((smallSawFrame1TilesTinted != null) ? smallSawFrame1TilesTinted.Length : 0);
										int num60 = ((smallSawFrame2TilesTinted != null) ? smallSawFrame2TilesTinted.Length : 0);
										int num61 = 3;
										int num62 = ((num59 < num61 || num59 % num61 != 0) ? 1 : (num59 / num61));
										int num63 = ((num60 < num61 || num60 % num61 != 0) ? 1 : (num60 / num61));
										int num64 = Math.Max(1, Math.Max(num62, num63));
										int num65 = animationFrame * 9 / 20 % num64;
										if (animationFrame * 9 / 20 % 2 == 1 && smallSawFrame2TilesTinted != null)
										{
											int num66 = ((num63 > 1) ? (num65 % num63 * num61 + num58) : num58);
											if (num66 >= 0 && num66 < num60)
											{
												imageSource4 = smallSawFrame2TilesTinted[num66];
											}
										}
										if (imageSource4 == null && smallSawFrame1TilesTinted != null)
										{
											int num67 = ((num62 > 1) ? (num65 % num62 * num61 + num58) : num58);
											if (num67 >= 0 && num67 < num59)
											{
												imageSource4 = smallSawFrame1TilesTinted[num67];
											}
										}
									}
									else if (num47 >= 1020 && num47 <= 1037)
									{
										int num68 = (num47 - 1020) % 9;
										int num69 = ((largeSawFrame1TilesTinted != null) ? largeSawFrame1TilesTinted.Length : 0);
										int num70 = ((largeSawFrame2TilesTinted != null) ? largeSawFrame2TilesTinted.Length : 0);
										int num71 = 9;
										int num72 = ((num69 < num71 || num69 % num71 != 0) ? 1 : (num69 / num71));
										int num73 = ((num70 < num71 || num70 % num71 != 0) ? 1 : (num70 / num71));
										int num74 = Math.Max(1, Math.Max(num72, num73));
										int num75 = animationFrame * 9 / 20 % num74;
										if (animationFrame * 9 / 20 % 2 == 1 && largeSawFrame2TilesTinted != null)
										{
											int num76 = ((num73 > 1) ? (num75 % num73 * num71 + num68) : num68);
											if (num76 >= 0 && num76 < num70)
											{
												imageSource4 = largeSawFrame2TilesTinted[num76];
											}
										}
										if (imageSource4 == null && largeSawFrame1TilesTinted != null)
										{
											int num77 = ((num72 > 1) ? (num75 % num72 * num71 + num68) : num68);
											if (num77 >= 0 && num77 < num69)
											{
												imageSource4 = largeSawFrame1TilesTinted[num77];
											}
										}
									}
								}
							}
							catch
							{
							}
							if (imageSource4 == null && idx >= 0)
							{
								if (idx == 1 || idx == 2 || idx == 5 || idx == 6 || idx == 136 || idx == 137)
								{
									try
									{
										ImageSource value14 = null;
										long key = (long)((ulong)((long)idx << 48) | (((ulong)groundTint.A & 0xFFuL) << 40) | (((ulong)groundTint.R & 0xFFuL) << 32) | (((ulong)groundTint.G & 0xFFuL) << 24) | (((ulong)groundTint.B & 0xFFuL) << 16) | (((ulong)tileTint.A & 0xFFuL) << 8) | ((ulong)tileTint.R & 0xFFuL));
										if (!groundTintedTileCache.TryGetValue(key, out value14))
										{
											if (tileImages != null && idx >= 0 && idx < tileImages.Length && tileImages[idx] != null)
											{
												try
												{
													if (backgroundForceSolidBlack)
													{
														try
														{
															value14 = CreateBlackMaskedExceptColor(tileImages[idx], playerPlaceholderGreen, Color.FromArgb(0, 0, 0, 0), recolorOutline: false);
														}
														catch
														{
														}
													}
													if (value14 == null)
													{
														if (groundTint.A == byte.MaxValue && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
														{
															try
															{
																ImageSource[] array5 = CreateTwoToneTileImages(new ImageSource[1] { tileImages[idx] }, Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));
																if (array5 != null && array5.Length != 0)
																{
																	value14 = array5[0];
																}
															}
															catch
															{
																value14 = CreateBlackMaskedImage(tileImages[idx], tileTint, recolorOutline: false);
															}
														}
														else
														{
															try
															{
																Color bgSecondary = PaletteHelper.RowUpColor(Color.FromArgb(groundTint.A, groundTint.R, groundTint.G, groundTint.B));
																Color color = (startupTintApplied ? Color.FromArgb(0, 0, 0, 0) : tileTint);
																Color outlineTint = ((color.A > 0) ? color : Color.FromArgb(0, 0, 0, 0));
																ImageSource[] array6 = CreateTwoToneTileImages(new ImageSource[1] { tileImages[idx] }, groundTint, bgSecondary, outlineTint);
																if (array6 != null && array6.Length != 0)
																{
																	value14 = array6[0];
																}
															}
															catch
															{
																ImageSource[] array7 = CreateHslShiftedImages(new ImageSource[1] { tileImages[idx] }, groundTint, tileTint);
																if (array7 != null && array7.Length != 0)
																{
																	value14 = array7[0];
																}
															}
														}
													}
												}
												catch
												{
													value14 = tileImages[idx];
												}
											}
											groundTintedTileCache[key] = value14;
										}
										if (value14 != null)
										{
											imageSource4 = value14;
										}
										try
										{
											if (tileImages != null && idx >= 0 && idx < tileImages.Length && tileImages[idx] != null)
											{
												Color bgSecondary2 = PaletteHelper.RowUpColor(Color.FromArgb(groundTint.A, groundTint.R, groundTint.G, groundTint.B));
												if (tileTint.A > 0)
												{
													ImageSource[] array8 = CreateTwoToneTileImages(new ImageSource[1] { tileImages[idx] }, groundTint, bgSecondary2, Color.FromArgb(0, 0, 0, 0));
													ImageSource imageSource5 = ((array8 != null && array8.Length != 0) ? array8[0] : tileImages[idx]);
													ImageSource[] array9 = CreateOutlineTintedTileImages(new ImageSource[1] { imageSource5 }, tileTint);
													if (array9 != null && array9.Length != 0 && array9[0] != null)
													{
														imageSource4 = array9[0];
													}
												}
												else
												{
													ImageSource[] array10 = CreateTwoToneTileImages(new ImageSource[1] { tileImages[idx] }, groundTint, bgSecondary2, Color.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue));
													if (array10 != null && array10.Length != 0 && array10[0] != null)
													{
														imageSource4 = array10[0];
													}
												}
											}
										}
										catch
										{
										}
									}
									catch
									{
									}
								}
								if (imageSource4 == null)
								{
									if (idx >= 1000)
									{
										if (tileTonedImages != null && idx >= 0 && idx < tileTonedImages.Length && tileTonedImages[idx] != null)
										{
											imageSource4 = tileTonedImages[idx];
										}
										else if (tileImages != null && idx >= 0 && idx < tileImages.Length && tileImages[idx] != null)
										{
											imageSource4 = tileImages[idx];
										}
									}
									else if (tileTonedImages != null && idx >= 0 && idx < tileTonedImages.Length && tileTonedImages[idx] != null)
									{
										imageSource4 = tileTonedImages[idx];
									}
									else if (tileImages != null && idx >= 0 && idx < tileImages.Length && tileImages[idx] != null)
									{
										imageSource4 = tileImages[idx];
									}
								}
							}
							try
							{
								if (idx != 1 && idx != 2 && idx != 5 && idx != 6 && idx != 136 && idx != 137 && tileTint.A > 0 && idx >= 0)
								{
									ImageSource imageSource6 = null;
									if (tileTonedImages != null && idx >= 0 && idx < tileTonedImages.Length)
									{
										imageSource6 = tileTonedImages[idx];
									}
									if (imageSource6 == null && tileImages != null && idx >= 0 && idx < tileImages.Length)
									{
										imageSource6 = tileImages[idx];
									}
									if (imageSource6 != null)
									{
										try
										{
											ImageSource[] array11 = CreateOutlineTintedTileImages(new ImageSource[1] { imageSource6 }, tileTint);
											if (array11 != null && array11.Length != 0 && array11[0] != null)
											{
												imageSource4 = array11[0];
											}
										}
										catch
										{
										}
									}
								}
							}
							catch
							{
							}
							if (imageSource4 != null)
							{
								try
								{
									if (tileSelectionLogCount < 64)
									{
										string value15 = "unknown";
										try
										{
											if (tileTonedImages != null && idx >= 0 && idx < tileTonedImages.Length && tileTonedImages[idx] == imageSource4)
											{
												value15 = "tileTonedImages";
											}
											else if (tileImages != null && idx >= 0 && idx < tileImages.Length && tileImages[idx] == imageSource4)
											{
												value15 = "tileImages";
											}
											else
											{
												foreach (KeyValuePair<long, ImageSource> item3 in groundTintedTileCache)
												{
													if (item3.Value == imageSource4)
													{
														value15 = "groundTintedTileCache";
														break;
													}
												}
											}
										}
										catch
										{
										}
										AppendSimDebug($"GetOrRenderCachedTile idx={idx} src={value15} mapIdx={num44} tileVal=0x{num45:X}");
										tileSelectionLogCount++;
									}
								}
								catch
								{
								}
								try
								{
									try
									{
										int[] array12 = new int[12]
										{
											12, 13, 14, 15, 19, 20, 128, 129, 132, 133,
											134, 135
										};
										if (imageSource4 != null && tileImages != null && idx >= 0 && Array.IndexOf(array12, idx) >= 0)
										{
											try
											{
												imageSource4 = ApplyPlayerTintToGreenPixels(imageSource4, tileImages[idx], playerTint);
											}
											catch
											{
											}
										}
									}
									catch
									{
									}
									if (backgroundForceSolidBlack && imageSource4 != null)
									{
										int[] array13 = new int[12]
										{
											12, 13, 14, 15, 19, 20, 128, 129, 132, 133,
											134, 135
										};
										Color excludeColor = ((tileImages == null || idx < 0 || Array.IndexOf(array13, idx) < 0) ? ((tileTint.A > 0) ? Color.FromArgb(byte.MaxValue, tileTint.R, tileTint.G, tileTint.B) : playerPlaceholderGreen) : ((playerTint.A > 0) ? playerTint : Color.FromArgb(byte.MaxValue, 90, 206, 82)));
										long key2 = (long)((((ulong)idx & 0xFFFFuL) << 32) | (((ulong)excludeColor.A & 0xFFuL) << 24) | (((ulong)excludeColor.R & 0xFFuL) << 16) | (((ulong)excludeColor.G & 0xFFuL) << 8) | ((ulong)excludeColor.B & 0xFFuL));
										if (!blackMaskedTileCache.TryGetValue(key2, out ImageSource value16))
										{
											try
											{
												value16 = CreateBlackMaskedExceptColor(imageSource4, excludeColor, Color.FromArgb(0, 0, 0, 0), recolorOutline: false);
											}
											catch
											{
												value16 = imageSource4;
											}
											try
											{
												blackMaskedTileCache[key2] = value16;
											}
											catch
											{
											}
										}
										if (value16 != null)
										{
											imageSource4 = value16;
										}
									}
								}
								catch
								{
								}
								if (imageSource4 is BitmapSource bitmapSource4)
								{
									double num78 = Math.Max(1.0, bitmapSource4.PixelWidth);
									double num79 = Math.Max(1.0, bitmapSource4.PixelHeight);
									if (num78 <= 16.0 && num79 <= 16.0)
									{
										double x2 = rectangle.X + (16.0 - num78) / 2.0;
										double num80 = rectangle.Y + (16.0 - num79);
										try
										{
											if (idx >= 1010 && idx <= 1015 && num79 <= 8.0)
											{
												try
												{
													if (bitmapSource4 != null && IsImageTopHeavy(bitmapSource4))
													{
														num80 -= 8.0;
													}
												}
												catch
												{
												}
											}
										}
										catch
										{
										}
										drawingContext.DrawImage(imageSource4, new Rect(x2, num80, num78, num79));
									}
									else
									{
										drawingContext.DrawImage(imageSource4, rectangle);
									}
								}
								else
								{
									drawingContext.DrawImage(imageSource4, rectangle);
								}
							}
							else
							{
								drawingContext.DrawRectangle(Brushes.Black, null, rectangle);
							}
						}
					}
				}
				RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(num31, num32, 96.0, 96.0, PixelFormats.Pbgra32);
				renderTargetBitmap.Render(drawingVisual);
				try
				{
					renderTargetBitmap.Freeze();
				}
				catch
				{
				}
				tileLayerCache = renderTargetBitmap;
				try
				{
					spriteBackgroundCompositeCache.Clear();
				}
				catch
				{
				}
				try
				{
					AppendSimDebug($"Built tileLayerCache pxW={num31} pxH={num32} hadAnimated={value13} startTileX={num17} startTileY={num25} tilesLen={((tiles != null) ? tiles.Length : 0)} mapWidth={mapWidth} mapHeight={mapHeight}");
				}
				catch
				{
				}
				lastCacheHadAnimatedTiles = value13;
				lastCacheAnimationFrame = animationFrame;
				cachedStartTileX = num17;
				cachedStartTileY = num25;
				tileLayerImage.Source = tileLayerCache;
				try
				{
					AppendSimDebug("Assigned tileLayerImage.Source=" + ((tileLayerImage.Source == null) ? "null" : tileLayerImage.Source.GetType().Name));
				}
				catch
				{
				}
				tileLayerImage.Width = num31;
				tileLayerImage.Height = num32;
			}
		}
		catch
		{
		}
		try
		{
			if (tileLayerImage != null)
			{
				Canvas.SetLeft(tileLayerImage, (double)(-num18) - num19);
				Canvas.SetTop(tileLayerImage, (double)(-num26) - num22);
			}
		}
		catch
		{
		}
		try
		{
			tileHitboxesInUse = 0;
			if (ShowTileHitboxes)
			{
				for (int l = 0; l <= 16; l++)
				{
					int num81 = num17 + l;
					for (int m = 0; m <= 15; m++)
					{
						int num82 = num25 + num23 + m;
						if (num81 < 0 || num81 >= mapWidth || num82 < 0 || num82 >= mapHeight)
						{
							continue;
						}
						MetatileCollision metatileCollision = MetatileCollision.COL_NONE;
						bool flag14 = false;
						try
						{
							if (tiles == null)
							{
								continue;
							}
							int num83 = tiles[num82 * mapWidth + num81];
							int num84 = MapAnimatedTileIndex(num83);
							int num85 = num84;
							if (num84 >= 1000)
							{
								num85 = ((num84 >= 1000 && num84 <= 1007) ? (8 + (num84 - 1000) % 4) : ((num84 >= 1010 && num84 <= 1015) ? (((num84 - 1010) % 3) switch
								{
									1 => 125, 
									0 => 4, 
									_ => 127, 
								}) : ((num84 < 1020 || num84 > 1037) ? num83 : (116 + (num84 - 1020) % 9))));
							}
							metatileCollision = MetatileCollisionTable.GetCollision((byte)num85);
							if (metatileCollision == MetatileCollision.COL_NONE)
							{
								continue;
							}
							for (int n = 0; n < 16; n++)
							{
								if (flag14)
								{
									break;
								}
								for (int num86 = 0; num86 < 16; num86++)
								{
									if (flag14)
									{
										break;
									}
									if (MetatileCollisionTable.TileKillsAtPixel(metatileCollision, num86, n))
									{
										flag14 = true;
									}
								}
							}
							goto IL_497a;
						}
						catch
						{
							goto IL_497a;
						}
						IL_497a:
						bool flag15 = metatileCollision == MetatileCollision.COL_TOP_LEFT_STAIRS || metatileCollision == MetatileCollision.COL_TOP_RIGHT_STAIRS || metatileCollision == MetatileCollision.COL_BOTTOM_LEFT_STAIRS || metatileCollision == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS || metatileCollision == MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT || metatileCollision == MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT || metatileCollision == MetatileCollision.COL_DEATH_TOP_RIGHT || metatileCollision == MetatileCollision.COL_DEATH_TOP_LEFT || metatileCollision == MetatileCollision.COL_DEATH_BOTTOM_RIGHT || metatileCollision == MetatileCollision.COL_DEATH_BOTTOM_LEFT || metatileCollision == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT || metatileCollision == MetatileCollision.COL_DEATH_TOP_BOTTOM || metatileCollision == MetatileCollision.COL_DEATH_LEFT_RIGHT || metatileCollision == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM || metatileCollision == MetatileCollision.COL_TOP_CENTER_SPIKE || metatileCollision == MetatileCollision.COL_BOTTOM_CENTER_SPIKE || metatileCollision == MetatileCollision.COL_LEFT_SPIKE_BLOCK || metatileCollision == MetatileCollision.COL_RIGHT_SPIKE_BLOCK || metatileCollision == MetatileCollision.COL_BOTTOM_LEFT_SPIKE || metatileCollision == MetatileCollision.COL_BOTTOM_RIGHT_SPIKE || metatileCollision == MetatileCollision.COL_BOTTOM_SPIKES;
						bool flag16 = IsSlopeTile(metatileCollision);
						if (flag15 || flag14 || flag16)
						{
							bool flag17 = metatileCollision == MetatileCollision.COL_UP_LEFT_SPIKE || metatileCollision == MetatileCollision.COL_UP_RIGHT_SPIKE || metatileCollision == MetatileCollision.COL_UP_BOTH_SPIKES || metatileCollision == MetatileCollision.COL_DOWN_LEFT_SPIKE || metatileCollision == MetatileCollision.COL_DOWN_RIGHT_SPIKE || metatileCollision == MetatileCollision.COL_DOWN_BOTH_SPIKES || metatileCollision == MetatileCollision.COL_DEATH || metatileCollision == MetatileCollision.COL_DEATH_TOP || metatileCollision == MetatileCollision.COL_DEATH_BOTTOM || metatileCollision == MetatileCollision.COL_DEATH_LEFT || metatileCollision == MetatileCollision.COL_DEATH_RIGHT || metatileCollision == MetatileCollision.COL_DEATH_BOTTOM_LEFT || metatileCollision == MetatileCollision.COL_DEATH_BOTTOM_RIGHT || metatileCollision == MetatileCollision.COL_DEATH_TOP_LEFT || metatileCollision == MetatileCollision.COL_DEATH_TOP_RIGHT || metatileCollision == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT || metatileCollision == MetatileCollision.COL_DEATH_TOP_BOTTOM || metatileCollision == MetatileCollision.COL_DEATH_LEFT_RIGHT || metatileCollision == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM;
							for (int num87 = 0; num87 < 16; num87++)
							{
								for (int num88 = 0; num88 < 16; num88++)
								{
									bool flag18 = TileOccupiesPixel(metatileCollision, num88, num87);
									bool flag19 = MetatileCollisionTable.TileKillsAtPixel(metatileCollision, num88, num87);
									bool flag20 = flag16 && IsSlopeSolidAtPixel(metatileCollision, num88, num87);
									if ((!flag18 && !flag19 && !flag20) || (flag17 && !flag19))
									{
										continue;
									}
									Rectangle rectangle2;
									if (tileHitboxesInUse < tileHitboxPool.Count)
									{
										rectangle2 = tileHitboxPool[tileHitboxesInUse];
										rectangle2.Visibility = Visibility.Visible;
									}
									else
									{
										rectangle2 = new Rectangle();
										rectangle2.IsHitTestVisible = false;
										rectangle2.Stroke = null;
										tileHitboxPool.Add(rectangle2);
										RenderCanvas.Children.Add(rectangle2);
										try
										{
											Panel.SetZIndex(rectangle2, 100);
										}
										catch
										{
										}
									}
									rectangle2.Fill = (flag19 ? new SolidColorBrush(Color.FromArgb(160, byte.MaxValue, 80, 200)) : new SolidColorBrush(Color.FromArgb(160, byte.MaxValue, 0, 0)));
									rectangle2.Width = 1.0;
									rectangle2.Height = 1.0;
									Canvas.SetLeft(rectangle2, l * 16 + num88 - num18);
									Canvas.SetTop(rectangle2, m * 16 + num87 - num26 + gridRenderShiftYPx);
									tileHitboxesInUse++;
								}
							}
							continue;
						}
						var (num89, num90, num91, num92) = GetCollisionBounds(metatileCollision);
						if (num91 <= num89 || num92 <= num90)
						{
							continue;
						}
						int num93 = num91 - num89;
						int num94 = num92 - num90;
						Rectangle rectangle3;
						if (tileHitboxesInUse < tileHitboxPool.Count)
						{
							rectangle3 = tileHitboxPool[tileHitboxesInUse];
							rectangle3.Visibility = Visibility.Visible;
						}
						else
						{
							rectangle3 = new Rectangle();
							rectangle3.IsHitTestVisible = false;
							rectangle3.Stroke = null;
							tileHitboxPool.Add(rectangle3);
							RenderCanvas.Children.Add(rectangle3);
							try
							{
								Panel.SetZIndex(rectangle3, 100);
							}
							catch
							{
							}
						}
						rectangle3.Fill = (flag14 ? new SolidColorBrush(Color.FromArgb(160, 128, 0, 128)) : new SolidColorBrush(Color.FromArgb(160, byte.MaxValue, 0, 0)));
						rectangle3.Width = num93;
						rectangle3.Height = num94;
						Canvas.SetLeft(rectangle3, l * 16 + num89 - num18);
						Canvas.SetTop(rectangle3, m * 16 + num90 - num26 + gridRenderShiftYPx);
						tileHitboxesInUse++;
					}
				}
			}
			for (int num95 = tileHitboxesInUse; num95 < tileHitboxPool.Count; num95++)
			{
				try
				{
					tileHitboxPool[num95].Visibility = Visibility.Collapsed;
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		try
		{
			if (groundRectPersistent != null)
			{
				if (hasGroundLayer && groundImages != null && groundImages.Length != 0)
				{
					int num96 = 16 * Math.Min(15, Math.Max(groundTileRows, 2));
					num96 = 16 * Math.Min(15, Math.Min(groundTileRows, 2));
					groundRectPersistent.Height = num96;
					Canvas.SetTop(groundRectPersistent, 240 - num96);
					groundRectPersistent.Visibility = Visibility.Collapsed;
				}
				else
				{
					int num97 = 16 * Math.Min(15, 2);
					groundRectPersistent.Height = num97;
					Canvas.SetTop(groundRectPersistent, 240 - num97);
					groundRectPersistent.Visibility = Visibility.Collapsed;
				}
			}
		}
		catch
		{
		}
		spritesInUse = 0;
		DrawingVisual drawingVisual2 = new DrawingVisual();
		DrawingContext drawingContext2 = drawingVisual2.RenderOpen();
		for (int num98 = 0; num98 <= 16; num98++)
		{
			int num99 = num17 + num98;
			for (int num100 = 0; num100 <= 15; num100++)
			{
				int num101 = num25 + num23 + num100;
				if (num99 < 0 || num99 >= mapWidth || num101 < 0 || num101 >= mapHeight)
				{
					continue;
				}
				int num102 = num101 * mapWidth + num99;
				int num103 = sprites[num102];
				if (num103 < 0)
				{
					continue;
				}
				double num104 = num98 * 16 - num18;
				double num105 = num100 * 16 - num26 + gridRenderShiftYPx;
				if (num104 + 32.0 < 0.0 || num104 > 256.0 || num105 + 32.0 < 0.0 || num105 > 240.0)
				{
					continue;
				}
				int num106 = num103 switch
				{
					124 => 39, 
					123 => 5, 
					_ => num103, 
				};
				if (num103 == 221 || num103 == 237 || num103 == 142 || num103 == 158 || num103 == 244 || num103 == 245 || num103 == 111 || num103 == 127 || num103 == 242 || num103 == 243 || num103 == 238 || num103 == 239 || num103 == 251 || num103 == 252)
				{
					continue;
				}
				try
				{
					if (hideTriggerSprites && IsHiddenTriggerSprite(num103))
					{
						continue;
					}
				}
				catch
				{
				}
				ImageSource imageSource7 = null;
				if (num103 == 61)
				{
					if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(num103, out ImageSource value17) && value17 != null)
					{
						imageSource7 = value17;
					}
					else
					{
						if (chainUpsideSimImage == null)
						{
							try
							{
								BitmapImage bitmapImage20 = LoadCachedResourceImage("chain-upsidedown.png");
								if (bitmapImage20 == null)
								{
									string text31 = System.IO.Path.Combine(AppContext.BaseDirectory ?? ".", "chain-upsidedown.png");
									if (File.Exists(text31))
									{
										bitmapImage20 = new BitmapImage();
										bitmapImage20.BeginInit();
										bitmapImage20.CacheOption = BitmapCacheOption.OnLoad;
										bitmapImage20.UriSource = new Uri(text31);
										bitmapImage20.EndInit();
										bitmapImage20.Freeze();
									}
								}
								if (bitmapImage20 != null)
								{
									chainUpsideSimImage = new FormatConvertedBitmap(bitmapImage20, PixelFormats.Pbgra32, null, 0.0);
								}
							}
							catch
							{
							}
						}
						if (chainUpsideSimImage != null)
						{
							imageSource7 = chainUpsideSimImage;
						}
					}
				}
				if (animationFrames != null && animationFrames.TryGetValue(num106, out ImageSource[] value18) && value18 != null && value18.Length != 0)
				{
					int num107 = 0;
					if (!decorationSpriteIds.Contains(num103) || value18.Length != 2)
					{
						if (!spriteFrameOffsets.ContainsKey(num102))
						{
							spriteFrameOffsets[num102] = spriteAnimationRandom.Next(0, Math.Max(1, value18.Length));
						}
						num107 = spriteFrameOffsets[num102];
					}
					int num108 = 0;
					if (value18.Length == 2 && (decorationSpriteIds.Contains(num103) || num103 == 84 || num103 == 85 || num103 == 69 || num103 == 70 || num103 == 76 || num103 == 77 || num103 == 80 || num103 == 81 || num103 == 91 || num103 == 92 || num103 == 93 || num103 == 94 || num103 == 89 || num103 == 90))
					{
						num108 = (GetEditorAnimationFrameValue() * 3 / 40 % 2 + 2) % 2;
					}
					else
					{
						bool flag21 = (num103 >= 8 && num103 <= 11) || num103 == 69 || num103 == 70 || num103 == 76 || num103 == 77 || num103 == 80 || num103 == 81 || num103 == 91 || num103 == 92 || num103 == 93 || num103 == 94;
						num108 = ((slowAnimatedSpriteIds.Contains(num103) || decorationSpriteIds.Contains(num103)) ? ((GetEditorAnimationFrameValue() * 9 / 40 + num107) % Math.Max(1, value18.Length)) : ((!flag21) ? ((GetEditorAnimationFrameValue() * 9 / 20 + num107) % Math.Max(1, value18.Length)) : ((GetEditorAnimationFrameValue() * 9 / 60 + num107) % Math.Max(1, value18.Length))));
					}
					imageSource7 = value18[num108];
					try
					{
						if (enableSimulatorDebugLogging && decorationSpriteIds.Contains(num103) && value18.Length == 2)
						{
							int value19 = -1;
							decoLastSelectedFrame.TryGetValue(num102, out value19);
							if (value19 != num108)
							{
								string value20 = ((value18[0] != null) ? RuntimeHelpers.GetHashCode(value18[0]).ToString("X8") : "null");
								string value21 = ((value18[1] != null) ? RuntimeHelpers.GetHashCode(value18[1]).ToString("X8") : "null");
								WriteTempLog($"Simulator: Decoration sprite idx={num102} id=0x{num103:X} frames=[{((value18[0] != null) ? "ok" : "null")},{((value18[1] != null) ? "ok" : "null")}] selectedFrame={num108} hashes=[{value20},{value21}]");
								decoLastSelectedFrame[num102] = num108;
							}
						}
					}
					catch
					{
					}
					if (imageSource7 == null)
					{
						if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(num106, out ImageSource value22) && value22 != null)
						{
							imageSource7 = value22;
						}
						else if (spriteImages != null && num106 < spriteImages.Length && spriteImages[num106] != null)
						{
							imageSource7 = spriteImages[num106];
						}
					}
				}
				else
				{
					if (num103 == 100 || num103 == 126)
					{
						try
						{
							if (previewSpriteMap != null)
							{
								int[] array14 = ((num103 != 100) ? new int[12]
								{
									0, 1, 2, 3, 4, 36, 23, 75, 88, 106,
									107, 108
								} : new int[8] { 0, 1, 2, 3, 4, 36, 23, 75 });
								List<ImageSource> list = new List<ImageSource>();
								int[] array15 = array14;
								foreach (int key3 in array15)
								{
									if (previewSpriteMap.TryGetValue(key3, out ImageSource value23) && value23 != null)
									{
										list.Add(value23);
									}
								}
								if (list.Count > 0)
								{
									int count = list.Count;
									int num110 = 0;
									try
									{
										num110 = animationFrame / 30 % Math.Max(1, count);
									}
									catch
									{
										num110 = 0;
									}
									uint num111 = (uint)num102;
									uint num112 = (uint)((int)num111 * -1640531535) % (uint)count;
									int index = (int)((uint)((int)num112 + num110) % (uint)count);
									imageSource7 = list[index];
								}
							}
						}
						catch
						{
						}
					}
					if (imageSource7 == null)
					{
						if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(num106, out ImageSource value24) && value24 != null)
						{
							imageSource7 = value24;
						}
						else if (spriteImages != null && num106 < spriteImages.Length && spriteImages[num106] != null)
						{
							imageSource7 = spriteImages[num106];
						}
					}
				}
				if (imageSource7 == null || num103 == 143 || num103 == 207 || (num103 >= 112 && num103 <= 116) || (hideColorTriggers && IsColorTriggerSprite(num103)))
				{
					continue;
				}
				double num113 = (num99 - num17) * 16;
				double num114 = num100 * 16 + gridRenderShiftYPx;
				if (spriteAnchors != null && spriteAnchors.TryGetValue(num102, out (int, int) value25))
				{
					int num115 = num102 % mapWidth;
					int num116 = num102 / mapWidth;
					int num117 = num115 - value25.Item1;
					int num118 = num116 - value25.Item2;
					double num119 = (value25.Item1 - num17) * 16;
					double num120 = (value25.Item2 - (num25 + num23)) * 16;
					num113 = num119 + (double)(num117 * 16);
					num114 = num120 + (double)(num118 * 16) + (double)gridRenderShiftYPx;
				}
				int num121 = 16;
				int num122 = 16;
				if (imageSource7 is BitmapSource bitmapSource5)
				{
					num121 = bitmapSource5.PixelWidth;
					num122 = bitmapSource5.PixelHeight;
				}
				if (num113 + (double)num121 < -16.0 || num113 > 288.0 || num114 + (double)num122 < -16.0 || num114 > 272.0)
				{
					continue;
				}
				try
				{
					if (playerTintEnabled && decorationSpriteIds.Contains(num103) && !nonPlayerTintSpriteIds.Contains(num103) && imageSource7 != null)
					{
						imageSource7 = GetPlayerTintedSprite(imageSource7, num106);
					}
				}
				catch
				{
				}
				(int, int) value27;
				if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num102, out (int, int) value26))
				{
					num113 += (double)value26.Item1;
					num114 += (double)value26.Item2;
				}
				else if (spriteAnchors != null && spriteAnchors.TryGetValue(num102, out value27))
				{
					int key4 = value27.Item2 * mapWidth + value27.Item1;
					if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(key4, out (int, int) value28))
					{
						num113 += (double)value28.Item1;
						num114 += (double)value28.Item2;
					}
				}
				try
				{
					if (num103 == 43 || num103 == 44)
					{
						num114 -= 8.0;
					}
					if (num103 == 42 || num103 == 58)
					{
						try
						{
							int num123 = (int)Math.Round(24.0) - 8;
							num114 = Math.Max(0.0, num114 - (double)num123);
						}
						catch
						{
						}
					}
					if (num103 == 45)
					{
						num114 -= 8.0;
					}
					try
					{
						if (decorationSpriteIds.Contains(num103) && num103 != 45 && imageSource7 is BitmapSource bitmapSource6 && (double)bitmapSource6.PixelHeight <= 8.0 && IsImageTopHeavy(bitmapSource6))
						{
							num114 -= 8.0;
						}
					}
					catch
					{
					}
				}
				catch
				{
				}
				ImageSource imageSource8 = imageSource7;
				if (imageSource8 is BitmapSource bitmapSource7)
				{
					drawingContext2.DrawImage(imageSource8, new Rect(num113, num114, bitmapSource7.PixelWidth, bitmapSource7.PixelHeight));
				}
				spritesInUse++;
				try
				{
					int num124 = num103 & 0xFF;
					if (spriteAnchors != null && spriteAnchors.TryGetValue(num102, out (int, int) value29))
					{
						int num125 = value29.Item2 * mapWidth + value29.Item1;
						if (num125 >= 0 && num125 < sprites.Length)
						{
							int num126 = sprites[num125];
							if (num126 >= 0 && num126 < 256)
							{
								num124 = num126 & 0xFF;
							}
						}
					}
					int num127 = 16;
					int num128 = 16;
					int num129 = 0;
					int num130 = 0;
					if (num124 >= 0 && num124 < sprite_widths.Length)
					{
						num127 = sprite_widths[num124];
					}
					if (num124 >= 0 && num124 < sprite_heights.Length)
					{
						num128 = sprite_heights[num124];
					}
					if (num128 >= 252)
					{
						continue;
					}
					if (num124 >= 0 && num124 < sprite_x_offset.Length)
					{
						num129 = sprite_x_offset[num124];
					}
					if (num124 >= 0 && num124 < sprite_y_offset.Length)
					{
						num130 = sprite_y_offset[num124];
					}
					try
					{
						if (num127 == 16 && num128 == 16 && imageSource8 is BitmapSource bitmapSource8)
						{
							num127 = Math.Max(1, bitmapSource8.PixelWidth);
							num128 = Math.Max(1, bitmapSource8.PixelHeight);
						}
					}
					catch
					{
					}
					double a = (int)Math.Round(num113 - (double)num18) + num129;
					double num131 = (int)Math.Round(num114 - (double)num26) + num130;
					if (s_padDownIds.Contains(num124))
					{
						num131 += 8.0;
					}
					int num132 = num10 >> 8;
					int num133 = num11 >> 8;
					int num134 = (int)Math.Round(a) + num132;
					int num135 = (int)Math.Round(num131) + num133 - gridRenderShiftYPx;
					int item = num134 + Math.Max(1, num127) - 1;
					int item2 = num135 + Math.Max(1, num128) - 1;
					try
					{
						hitboxWorldCache[num102] = (num134, num135, item, item2, renderFrameCounter);
					}
					catch
					{
					}
					goto IL_696a;
				}
				catch
				{
					goto IL_696a;
				}
				IL_696a:
				try
				{
					if (!MainWindow.Option_ShowSimulatorSpriteHitboxes || !ShowSpriteHitboxes || IsColorTriggerSprite(num103) || decorationSpriteIds.Contains(num103))
					{
						continue;
					}
					Rectangle rectangle4;
					if (hitboxesInUse < hitboxPool.Count)
					{
						rectangle4 = hitboxPool[hitboxesInUse];
						rectangle4.Visibility = Visibility.Visible;
					}
					else
					{
						rectangle4 = new Rectangle();
						rectangle4.Fill = new SolidColorBrush(Color.FromArgb(96, byte.MaxValue, byte.MaxValue, 0));
						rectangle4.Stroke = new SolidColorBrush(Color.FromArgb(160, byte.MaxValue, 200, 0));
						rectangle4.StrokeThickness = 1.0;
						rectangle4.IsHitTestVisible = false;
						hitboxPool.Add(rectangle4);
						RenderCanvas.Children.Add(rectangle4);
					}
					int num136 = num103 & 0xFF;
					int num137 = (int)Math.Round(num113 - (double)num18);
					int num138 = (int)Math.Round(num114 - (double)num26);
					if (spriteAnchors != null && spriteAnchors.TryGetValue(num102, out (int, int) value30))
					{
						int num139 = value30.Item2 * mapWidth + value30.Item1;
						if (num139 >= 0 && num139 < sprites.Length)
						{
							int num140 = sprites[num139];
							if (num140 >= 0 && num140 < 256)
							{
								num136 = num140 & 0xFF;
							}
						}
					}
					int num141 = 16;
					int num142 = 16;
					int num143 = 0;
					int num144 = 0;
					if (num136 >= 0 && num136 < sprite_widths.Length)
					{
						num141 = sprite_widths[num136];
					}
					if (num136 >= 0 && num136 < sprite_heights.Length)
					{
						num142 = sprite_heights[num136];
					}
					if (num142 >= 252)
					{
						continue;
					}
					if (num136 >= 0 && num136 < sprite_x_offset.Length)
					{
						num143 = sprite_x_offset[num136];
					}
					if (num136 >= 0 && num136 < sprite_y_offset.Length)
					{
						num144 = sprite_y_offset[num136];
					}
					try
					{
						if (num141 == 16 && num142 == 16 && imageSource8 is BitmapSource bitmapSource9)
						{
							num141 = Math.Max(1, bitmapSource9.PixelWidth);
							num142 = Math.Max(1, bitmapSource9.PixelHeight);
						}
					}
					catch
					{
					}
					double length = num137 + num143;
					double length2 = num138 + num144;
					rectangle4.Width = Math.Max(1, num141);
					rectangle4.Height = Math.Max(1, num142);
					Canvas.SetLeft(rectangle4, length);
					Canvas.SetTop(rectangle4, length2);
					hitboxesInUse++;
				}
				catch
				{
				}
			}
		}
		drawingContext2.Close();
		try
		{
			int num145 = 272;
			int num146 = 256;
			RenderTargetBitmap renderTargetBitmap2 = new RenderTargetBitmap(num145, num146, 96.0, 96.0, PixelFormats.Pbgra32);
			renderTargetBitmap2.Render(drawingVisual2);
			try
			{
				renderTargetBitmap2.Freeze();
			}
			catch
			{
			}
			if (spriteLayerImage != null)
			{
				spriteLayerImage.Source = renderTargetBitmap2;
				spriteLayerImage.Width = num145;
				spriteLayerImage.Height = num146;
				Canvas.SetLeft(spriteLayerImage, -num18);
				Canvas.SetTop(spriteLayerImage, -num26);
			}
		}
		catch
		{
		}
		for (int num147 = 0; num147 < spritePool.Count; num147++)
		{
			spritePool[num147].Visibility = Visibility.Collapsed;
		}
		for (int num148 = hitboxesInUse; num148 < hitboxPool.Count; num148++)
		{
			hitboxPool[num148].Visibility = Visibility.Collapsed;
		}
		hitboxesInUse = 0;
		try
		{
			int num149 = (num12 >> 8) - (num10 >> 8);
			int num150 = (num13 >> 8) - (num11 >> 8) + gridRenderShiftYPx;
			int num151 = (num12 >> 8) + playerVisualWidth / 2;
			if (flag11)
			{
				if (flag12)
				{
					int num152 = (num13 >> 8) + 4;
				}
				else
				{
					int num152 = (num13 >> 8) + 13;
				}
			}
			else
			{
				int num152 = (num13 >> 8) + 8;
			}
			if (flag11)
			{
				num150 += 4;
			}
			if (camModeActive || playerInvis)
			{
				if (playerImage != null)
				{
					playerImage.Visibility = Visibility.Collapsed;
				}
				if (playerRect != null)
				{
					playerRect.Visibility = Visibility.Collapsed;
				}
			}
			else if (playerImage != null && playerImage.Source != null)
			{
				Canvas.SetLeft(playerImage, num149);
				Canvas.SetTop(playerImage, num150);
				playerImage.Visibility = Visibility.Visible;
				if (playerRect != null)
				{
					playerRect.Visibility = Visibility.Collapsed;
				}
			}
			else if (playerRect != null)
			{
				Canvas.SetLeft(playerRect, num149);
				Canvas.SetTop(playerRect, num150);
				playerRect.Visibility = Visibility.Visible;
			}
			try
			{
				bool flag22 = forcedTrails > 0 && !camModeActive && !playerInvis && (simTickCount & 1) == 0;
				for (int num153 = 0; num153 < 3; num153++)
				{
					if (trailGhosts[num153] == null)
					{
						continue;
					}
					if (!flag22)
					{
						trailGhosts[num153].Visibility = Visibility.Collapsed;
						continue;
					}
					if (playerImage != null && playerImage.Source != null)
					{
						trailGhosts[num153].Source = playerImage.Source;
						trailGhosts[num153].Width = playerImage.Width;
						trailGhosts[num153].Height = playerImage.Height;
						trailGhosts[num153].RenderTransformOrigin = playerImage.RenderTransformOrigin;
						trailGhosts[num153].RenderTransform = playerImage.RenderTransform;
					}
					int num154 = playerVelX_fixed >> 8;
					int num155 = num149 - num154 * 2 * (num153 + 1);
					int num156 = Math.Min((num153 + 1) * 2, playerOldPosY.Length - 1);
					int num157 = (playerOldPosY[num156] >> 8) - (num11 >> 8) + gridRenderShiftYPx;
					if (flag11)
					{
						num157 += 4;
					}
					Canvas.SetLeft(trailGhosts[num153], num155);
					Canvas.SetTop(trailGhosts[num153], num157);
					trailGhosts[num153].Opacity = 0.4 - (double)num153 * 0.1;
					trailGhosts[num153].Visibility = Visibility.Visible;
				}
			}
			catch
			{
			}
		}
		catch
		{
		}
		try
		{
			if (!dual || camModeActive)
			{
				if (player2Image != null)
				{
					player2Image.Visibility = Visibility.Collapsed;
				}
				if (player2Rect != null)
				{
					player2Rect.Visibility = Visibility.Collapsed;
				}
			}
			else
			{
				int num158 = (player_x_fixed[1] >> 8) - (num10 >> 8);
				int num159 = (player_y_fixed[1] >> 8) - (num11 >> 8);
				if (player_mini[1])
				{
					num159 += 4;
				}
				if (player2Image != null && player2Image.Source != null)
				{
					Canvas.SetLeft(player2Image, num158);
					Canvas.SetTop(player2Image, num159);
					player2Image.Visibility = Visibility.Visible;
					if (player2Rect != null)
					{
						player2Rect.Visibility = Visibility.Collapsed;
					}
				}
				else if (player2Rect != null)
				{
					Canvas.SetLeft(player2Rect, num158);
					Canvas.SetTop(player2Rect, num159);
					player2Rect.Visibility = Visibility.Visible;
				}
			}
		}
		catch
		{
		}
		int num160 = 2048;
		int num161 = num12 + num160;
		bool flag23 = interactionScreenOffset_px >= 0 && num161 >= 20480;
		double num162 = (double)(num10 & 0xFF) / 256.0;
		double num163 = (double)(num11 & 0xFF) / 256.0;
		if (flag23)
		{
			num162 = 0.0;
		}
		Transform renderTransform = RenderCanvas.RenderTransform;
		if (renderTransform is TranslateTransform translateTransform)
		{
			translateTransform.X = 0.0 - num162;
			translateTransform.Y = 0.0 - num163;
		}
		else
		{
			RenderCanvas.RenderTransform = new TranslateTransform(0.0 - num162, 0.0 - num163);
		}
		try
		{
			if (showYVelocityOverlay)
			{
				if (yVelTextBlock == null)
				{
					yVelTextBlock = new TextBlock();
					yVelTextBlock.Foreground = new SolidColorBrush(Colors.Yellow);
					yVelTextBlock.FontWeight = FontWeights.Bold;
					yVelTextBlock.FontSize = 14.0;
					yVelTextBlock.IsHitTestVisible = false;
					RenderCanvas.Children.Add(yVelTextBlock);
					try
					{
						Panel.SetZIndex(yVelTextBlock, 2000);
					}
					catch
					{
					}
				}
				try
				{
					int num164 = num14;
					double value31 = (double)num164 / 256.0;
					string value32 = ((num164 >= 0) ? ("0x" + (num164 & 0xFFFF).ToString("X4")) : ("-0x" + (-num164 & 0xFFFF).ToString("X4")));
					yVelTextBlock.Text = $"Y vel: {value32}  ({value31:F2} px/frame)\nInvertedGravity: {gravityReversed}";
					yVelTextBlock.Visibility = Visibility.Visible;
					Canvas.SetLeft(yVelTextBlock, 4.0);
					Canvas.SetTop(yVelTextBlock, 4.0);
				}
				catch
				{
				}
			}
			else
			{
				try
				{
					if (yVelTextBlock != null)
					{
						yVelTextBlock.Visibility = Visibility.Collapsed;
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		try
		{
			if (debugWindow != null && debugWindow.IsLoaded)
			{
				int num165 = speed;
				if (1 == 0)
				{
				}
				string text3 = num165 switch
				{
					0 => "0.5x", 
					1 => "1x", 
					2 => "2x", 
					3 => "3x", 
					4 => "4x", 
					5 => "0.1x", 
					_ => $"{speed}x", 
				};
				if (1 == 0)
				{
				}
				string xSpeed = text3;
				debugWindow.UpdateDebugInfo(currentGameMode, currplayer_mini != 0, flag12, num12 >> 8, num13 >> 8, xSpeed, ninjajumps[currplayer], dblocked, hblocked, fblocked, jblocked, orbed[currplayer], blackOrbed, dashing[currplayer], robotJumpTime[currplayer], num14, orbBufferActive[currplayer], nocamlockforced);
			}
		}
		catch
		{
		}
	}

	public void EnsureInitialRender()
	{
		try
		{
			if (!base.Dispatcher.CheckAccess())
			{
				base.Dispatcher.Invoke(delegate
				{
					try
					{
						RenderFrame();
					}
					catch
					{
					}
					try
					{
						if (pendingTintChange || pendingTintChangeIsStartup)
						{
							try
							{
								ApplyPendingTints();
							}
							catch
							{
							}
							try
							{
								RenderFrame();
								return;
							}
							catch
							{
								return;
							}
						}
					}
					catch
					{
					}
				});
				return;
			}
			try
			{
				RenderFrame();
			}
			catch
			{
			}
			try
			{
				if (pendingTintChange || pendingTintChangeIsStartup)
				{
					try
					{
						ApplyPendingTints();
					}
					catch
					{
					}
					try
					{
						RenderFrame();
						return;
					}
					catch
					{
						return;
					}
				}
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void CompositionTarget_Rendering(object? sender, EventArgs e)
	{
		if (windowClosed)
		{
			return;
		}
		try
		{
			try
			{
				double totalMilliseconds = renderStopwatch.Elapsed.TotalMilliseconds;
				double num = Math.Max(0.0, totalMilliseconds - uiAnimLastMs);
				uiAnimLastMs = totalMilliseconds;
				uiAnimAccumulatedMs += num * simTimeScale;
				if (!paused)
				{
					while (uiAnimAccumulatedMs >= 16.666666666666668)
					{
						animationFrame++;
						uiAnimAccumulatedMs -= 16.666666666666668;
					}
				}
			}
			catch
			{
			}
			if (paused)
			{
				if (!pfSimulating)
				{
					RenderFrame();
				}
				try
				{
					if (levelCompleteTriggered)
					{
						PauseOverlay.Visibility = Visibility.Collapsed;
						LevelCompleteOverlay.Visibility = Visibility.Visible;
					}
					else if (!deathTriggered)
					{
						PauseOverlay.Visibility = Visibility.Visible;
						LevelCompleteOverlay.Visibility = Visibility.Collapsed;
					}
					else
					{
						PauseOverlay.Visibility = Visibility.Collapsed;
						LevelCompleteOverlay.Visibility = Visibility.Collapsed;
					}
					return;
				}
				catch
				{
					return;
				}
			}
			if (pendingTintChange)
			{
				try
				{
					ApplyPendingTints();
				}
				catch
				{
				}
			}
			try
			{
				_suppressDebugBarEvents = true;
				if (GameModeComboBox != null && GameModeComboBox.SelectedIndex != currentGameMode && currentGameMode >= 0 && currentGameMode < GameModeComboBox.Items.Count)
				{
					GameModeComboBox.SelectedIndex = currentGameMode;
				}
				if (SpeedComboBox != null && SpeedComboBox.SelectedIndex != speed && speed >= 0 && speed < SpeedComboBox.Items.Count)
				{
					SpeedComboBox.SelectedIndex = speed;
				}
				if (MiniCheckBox != null && MiniCheckBox.IsChecked != miniMode)
				{
					MiniCheckBox.IsChecked = miniMode;
				}
				if (InvertedCheckBox != null && InvertedCheckBox.IsChecked != gravityReversed)
				{
					InvertedCheckBox.IsChecked = gravityReversed;
				}
			}
			catch
			{
			}
			finally
			{
				_suppressDebugBarEvents = false;
			}
			try
			{
				PauseOverlay.Visibility = Visibility.Collapsed;
			}
			catch
			{
			}
			try
			{
				LevelCompleteOverlay.Visibility = Visibility.Collapsed;
			}
			catch
			{
			}
			RenderFrame();
		}
		catch
		{
		}
	}

	private void SimulateNumericStep()
	{
		int num = 2048;
		lock (simLock)
		{
			if (restartInProgress || windowClosed || paused)
			{
				return;
			}
			int topOffsetPx = simTickCount++;
			if (slowMode && (simTickCount & 1) != 0)
			{
				return;
			}
			AppendSimDebug($"[STEP_START] step={simTickCount} pfFrame={pfFrameIndex} playerX_fixed=0x{playerX_fixed:X4} ({playerX_fixed >> 8}px), playerY_fixed=0x{playerY_fixed:X4} ({playerY_fixed >> 8}px), playerVelY_fixed=0x{playerVelY_fixed:X4}");
			if (pathfinderEnabled)
			{
				bool flag = (pfInputThisFrame = PF_GetInput());
				if (pfWasPhantomStep)
				{
					AppendSimDebug($"[PF_PHANTOM] Skipping phantom double-step (tick={pfTickGeneration}, frame={pfFrameIndex})");
					return;
				}
				PF_InjectInput(flag);
				if (flag)
				{
					AppendSimDebug($"[PF] Frame {pfFrameIndex - 1}: JUMP INPUT injected (keyXHeld={keyXHeld}, pressCount={keyXPressedCount}, velY=0x{playerVelY_fixed:X4}, wasZeroed={wasZeroedByCollisionLastFrame})");
				}
			}
			int num2 = cameraX_fixed + 32768;
			int num3 = playerX_fixed + num;
			isFullSpeed = simTimeScale == 1.0;
			int num4 = tabSpeedMultiplier;
			int num5 = ((!isFullSpeed) ? (playerX_fixed + (int)Math.Round((double)(currentSpeed_fixed * num4) * simTimeScale)) : (playerX_fixed + currentSpeed_fixed * num4));
			int num6 = num5 + num;
			if (dashing[currplayer] != 0 && !IsXDownAsync() && !keyXHeld)
			{
				velocityY = 0;
				playerVelY_fixed = 0;
				dashing[currplayer] = 0;
			}
			if (orbed[currplayer] && !IsXDownAsync() && !keyXHeld)
			{
				orbed[currplayer] = false;
			}
			bool flag2 = miniMode;
			int num7 = playerY_fixed;
			if (physicsEnabled)
			{
				try
				{
					orbhitonthisframe[currplayer] = false;
					CheckDualPortal();
					CheckSinglePortal();
					CheckGameModePortals();
					CheckBluePadCollision();
					CheckGravityPortals();
					AppendSimDebug($"[GRAV_PRE_MOVEMENT] currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed}");
					CheckTeleportPortals();
					CheckGravityModPortals();
					CheckGravityModTriggers(num3, num6);
					CheckMiniGrowthPortals();
					CheckCamLockPortals(num3, num6);
					CheckMiscTriggers();
					CheckPadCollision();
					CheckSpiderOrbPadCollision();
					CheckDashOrbCollision();
					CheckAlphabetBlocks();
					CheckCoinCollision();
					int num8 = (flag2 ? 8 : 15);
					int num9 = (flag2 ? 7 : 15);
					int num10 = (playerX_fixed >> 8) + 1;
					int num11 = num10 + num8;
					int num12 = (flag2 ? 4 : 0);
					int num13 = (num7 >> 8) + num12;
					int num14 = num13 + num9;
					int num15 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
					int num16 = 0;
					while (num16 < nonEmptySpriteIndices.Length)
					{
						int num17 = nonEmptySpriteIndices[num16];
						int num18 = sprites[num17];
						if (num18 >= 0 && speedPortalMap.ContainsKey(num18) && !processedSpeedPortals.Contains(num17))
						{
							int num19 = num17 % mapWidth;
							int num20 = num17 / mapWidth;
							int num21 = num18 & 0xFF;
							int num22 = num21;
							int num23 = -1;
							bool flag3 = !SharedPhysics.IsSpeedPortal(num21) && !SharedPhysics.IsGameModePortal(num21) && !SharedPhysics.IsGravityPortal(num21) && !SharedPhysics.IsMiniGrowthPortal(num21);
							if (spriteAnchors != null && spriteAnchors.TryGetValue(num17, out (int, int) value))
							{
								num23 = value.Item2 * mapWidth + value.Item1;
								if (flag3 && num23 >= 0 && num23 < sprites.Length)
								{
									int num24 = sprites[num23];
									if (num24 >= 0 && num24 < 256)
									{
										num22 = num24 & 0xFF;
									}
								}
							}
							int val = ((num22 >= 0 && num22 < sprite_widths.Length) ? sprite_widths[num22] : 16);
							int num25 = ((num22 >= 0 && num22 < sprite_heights.Length) ? sprite_heights[num22] : 16);
							if (num25 < 252)
							{
								int num26 = ((num22 >= 0 && num22 < sprite_x_offset.Length) ? sprite_x_offset[num22] : 0);
								int num27 = ((num22 >= 0 && num22 < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[num22] : 0);
								int num28 = 0;
								int num29 = 0;
								(int, int) value3;
								if (num23 >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num23, out (int, int) value2))
								{
									(num28, num29) = value2;
								}
								else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num17, out value3))
								{
									(num28, num29) = value3;
								}
								int num30 = num19 * 16 + num26 + num28;
								int num31 = (num20 - num15) * 16 + num27 + num29;
								int num32 = num30 + Math.Max(1, val);
								int num33 = num31 + Math.Max(1, num25);
								bool flag4 = num11 >= num30 && num32 >= num10;
								bool flag5 = num14 >= num31 && num33 >= num13;
								if (flag4 && flag5)
								{
									int num34 = (playerVelX_fixed = (currentSpeed_fixed = speedPortalMap[num18]));
									switch (num34)
									{
									case 571:
										speed = 0;
										break;
									case 708:
										speed = 1;
										break;
									case 881:
										speed = 2;
										break;
									case 1065:
										speed = 3;
										break;
									case 1310:
										speed = 4;
										break;
									}
									AppendSimDebug($"[SPEED_P1] sid=0x{num18:X2} VelX -> 0x{num34:X4}");
									num5 = ((!isFullSpeed) ? (playerX_fixed + (int)Math.Round((double)(currentSpeed_fixed * num4) * simTimeScale)) : (playerX_fixed + currentSpeed_fixed * num4));
									num6 = num5 + num;
								}
							}
						}
						topOffsetPx = num16++;
					}
				}
				catch
				{
				}
			}
			int num35 = playerX_fixed;
			playerX_fixed = num5;
			if (onGround)
			{
				try
				{
					bool flag6 = false;
					if (gravityReversed)
					{
						flag6 = IsTouchingCeiling();
					}
					else
					{
						int num36 = (playerX_fixed >> 8) + 8;
						int num37 = num36 - 7;
						int num38 = num37 + 14;
						int num39 = num37 + 7;
						int[] array = new int[3] { num37, num39, num38 };
						int num40 = (miniMode ? 7 : 15);
						int num41 = 0;
						if (miniMode)
						{
							num41 = ((currentGameMode == 2) ? 4 : 9);
						}
						int num42 = (playerY_fixed >> 8) + num41 + num40;
						int num43 = num42 / 16;
						int num44 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
						int num45 = num43 + num44;
						if (num45 >= mapHeight)
						{
							flag6 = true;
						}
						else if (num45 >= 0)
						{
							int localY = (num42 % 16 + 16) % 16;
							int[] array2 = array;
							foreach (int num46 in array2)
							{
								int num47 = num46 / 16;
								if (num47 >= 0 && num47 < mapWidth)
								{
									int num48 = tiles[num45 * mapWidth + num47];
									int num49 = MapAnimatedTileIndex(num48);
									int num50 = num49;
									if (num49 >= 1000)
									{
										num50 = ((num49 >= 1000 && num49 <= 1007) ? (8 + (num49 - 1000) % 4) : ((num49 >= 1010 && num49 <= 1015) ? (((num49 - 1010) % 3) switch
										{
											1 => 125, 
											0 => 4, 
											_ => 127, 
										}) : ((num49 < 1020 || num49 > 1037) ? num48 : (116 + (num49 - 1020) % 9))));
									}
									MetatileCollision col = MetatileCollisionTable.GetCollision((byte)num50);
									int num51 = num47 * 16;
									int localX = Math.Max(0, Math.Min(15, num46 - num51));
									if (ProvidesFloorAtColumnStatic(col, localX, out topOffsetPx) && SharedPhysics.TileOccupiesPixel(col, localX, localY))
									{
										flag6 = true;
										break;
									}
								}
							}
						}
					}
					if (!flag6)
					{
						onGround = false;
						wasZeroedByCollisionLastFrame = false;
					}
				}
				catch
				{
					onGround = false;
					wasZeroedByCollisionLastFrame = false;
				}
			}
			int num52 = Interlocked.Exchange(ref jumpBufferCounter, 0);
			int num53 = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
			bool flag7 = num3 < 20480 && num6 >= 20480;
			if (flag7)
			{
				interactionScreenOffset_px = 80 - (cameraX_fixed >> 8);
			}
			int num54 = playerX_fixed + num;
			if (num54 >= 20480)
			{
				if (interactionScreenOffset_px >= 0)
				{
					cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
				}
				else
				{
					cameraX_fixed += num6 - 20480;
				}
				int num55 = Math.Max(0, (mapWidth - 16) * 16) << 8;
				if (cameraX_fixed < 0)
				{
					cameraX_fixed = 0;
				}
				if (cameraX_fixed > num55)
				{
					cameraX_fixed = num55;
				}
			}
			else
			{
				interactionScreenOffset_px = -1;
			}
			int num56 = Math.Max(0, (mapHeight - 15) * 16) << 8;
			int num57 = Math.Max(0, mapHeight * 16 - playerVisualHeight) << 8;
			if (upHeld)
			{
				int num58 = (playerY_fixed >> 8) + playerVisualHeight / 2 - (cameraY_fixed >> 8);
				int num59 = 80;
				if (!jumpedOnce && camModeActive)
				{
					if (cameraY_fixed > 0)
					{
						cameraY_fixed -= 512;
						if (cameraY_fixed < 0)
						{
							cameraY_fixed = 0;
						}
					}
				}
				else if (!physicsEnabled && camModeActive)
				{
					playerY_fixed -= 512;
					if (playerY_fixed < 0)
					{
						playerY_fixed = 0;
					}
					num58 = (playerY_fixed >> 8) + playerVisualHeight / 2 - (cameraY_fixed >> 8);
					if (num58 <= num59)
					{
						int val2 = num59 - num58;
						int num60 = Math.Min(val2, cameraY_fixed >> 8);
						cameraY_fixed -= num60 << 8;
						if (cameraY_fixed < 0)
						{
							cameraY_fixed = 0;
						}
					}
				}
				else if (camModeActive)
				{
					if (num58 <= num59)
					{
						int val3 = num59 - num58;
						int num61 = Math.Min(val3, cameraY_fixed >> 8);
						cameraY_fixed -= num61 << 8;
						if (cameraY_fixed < 0)
						{
							cameraY_fixed = 0;
						}
					}
					else if (cameraY_fixed > 0)
					{
						cameraY_fixed -= 512;
						if (cameraY_fixed < 0)
						{
							cameraY_fixed = 0;
						}
					}
				}
			}
			if (downHeld)
			{
				int num62 = 160;
				int num63 = (playerY_fixed >> 8) - (cameraY_fixed >> 8);
				int num64 = num63 + playerVisualHeight;
				if (!jumpedOnce && camModeActive)
				{
					if (cameraY_fixed < num56)
					{
						cameraY_fixed += 512;
						if (cameraY_fixed > num56)
						{
							cameraY_fixed = num56;
						}
					}
				}
				else if (num64 < num62)
				{
					if (!physicsEnabled && camModeActive)
					{
						playerY_fixed += 512;
						if (playerY_fixed > num57)
						{
							playerY_fixed = num57;
						}
					}
				}
				else if (camModeActive && cameraY_fixed < num56)
				{
					cameraY_fixed += 512;
					if (cameraY_fixed > num56)
					{
						cameraY_fixed = num56;
					}
					if (!physicsEnabled)
					{
						playerY_fixed += 512;
						if (playerY_fixed > num57)
						{
							playerY_fixed = num57;
						}
					}
				}
				else if (camModeActive)
				{
					if (cameraY_fixed < num56)
					{
						cameraY_fixed += 512;
						if (cameraY_fixed > num56)
						{
							cameraY_fixed = num56;
						}
					}
					else if (!physicsEnabled)
					{
						playerY_fixed += 512;
						if (playerY_fixed > num57)
						{
							playerY_fixed = num57;
						}
					}
				}
			}
			playerX_fixed = num35;
			if (physicsEnabled && !camModeActive)
			{
				try
				{
					currplayer_mini = (byte)(miniMode ? 1u : 0u);
					currplayer_gravity = (byte)(gravityFlipped ? 255u : 0u);
					currplayer_table_idx = ((currplayer_gravity != 0) ? 1 : 0) | ((currplayer_mini != 0) ? 4 : 0);
					if (!_levelNameLogged)
					{
						_levelNameLogged = true;
						AppendSimDebug("[LEVEL] " + _levelName);
					}
					if (gravityFlipped)
					{
						AppendSimDebug($"[PHYSICS] FLIPPED! Mode={currentGameMode}, gravity={currplayer_gravity:X2}, mini={currplayer_mini}, table_idx={currplayer_table_idx}, gravityFlipped={gravityFlipped}, gravityReversed={gravityReversed}");
					}
					else
					{
						AppendSimDebug($"[PHYSICS] Mode={currentGameMode}, gravity={currplayer_gravity:X2}, mini={currplayer_mini}, table_idx={currplayer_table_idx}");
					}
					gravityFlipped = currplayer_gravity != 0;
					switch (currentGameMode)
					{
					case 0:
						ProcessCubePhysics_Fresh();
						if (miniMode)
						{
							UpdateCubeRotationMini();
						}
						else
						{
							UpdateCubeRotation();
						}
						break;
					case 1:
						ShipPhysics_Fresh();
						UpdateShipRotation();
						break;
					case 2:
						BallPhysics_Fresh();
						break;
					case 3:
						UfoPhysics_Fresh();
						break;
					case 4:
						RobotPhysics_Fresh();
						if (miniMode)
						{
							UpdateCubeRotationMini();
						}
						else
						{
							UpdateCubeRotation();
						}
						break;
					case 5:
						SpiderPhysics_Fresh();
						break;
					case 6:
						WavePhysics_Fresh();
						break;
					case 7:
						BallPhysics_Fresh();
						UpdateSwingcopterRotation();
						break;
					case 8:
						NinjaPhysics_Fresh();
						if (miniMode)
						{
							UpdateCubeRotationMini();
						}
						else
						{
							UpdateCubeRotation();
						}
						break;
					case 9:
						BallPhysics_Fresh();
						break;
					case 10:
						SnakePhysics_Fresh();
						break;
					case 11:
						FootballPhysics_Fresh();
						UpdateFootballRotation();
						break;
					}
					gravityFlipped = currplayer_gravity != 0;
					AppendSimDebug($"[GRAV_POST_PHYSICS] currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed} mini={miniMode}");
					if (!MainWindow.Option_NoDeath && !deathTriggered && invincibleCounter == 0)
					{
						int num65 = num35 >> 8;
						int num66 = playerY_fixed >> 8;
						if (num65 >= 6830 && num65 <= 6920 && currentGameMode == 4)
						{
							AppendSimDebug($"[SPIKE_DIAG] oldX={num65} Y={num66} velY=0x{playerVelY_fixed:X4} newX={num5 >> 8} mode={currentGameMode} deathTriggered={deathTriggered}");
						}
						if (CheckFloorSpikes(num65, num66, out var fsDeathX, out var fsDeathY))
						{
							AppendSimDebug($"[DEATH] Floor spike 4-corner death at ({fsDeathX},{fsDeathY})");
							deathTriggered = true;
							deathTileX = fsDeathX;
							deathTileY = fsDeathY;
							paused = true;
							StopMusicAsync();
							try
							{
								base.Dispatcher.BeginInvoke((Action)delegate
								{
									try
									{
										PauseOverlay.Visibility = Visibility.Collapsed;
									}
									catch
									{
									}
									if (base.Owner is MainWindow mainWindow)
									{
										try
										{
											mainWindow.PauseSimulatorPlayback();
										}
										catch
										{
										}
										try
										{
											mainWindow.AddDeathMarker(fsDeathX, fsDeathY);
										}
										catch
										{
										}
									}
								});
							}
							catch
							{
							}
						}
					}
					if (!MainWindow.Option_NoDeath && !deathTriggered && !ShouldSkipSideCollisionForSlope() && invincibleCounter == 0 && (currentGameMode == 0 || currentGameMode == 1 || currentGameMode == 2 || currentGameMode == 3 || currentGameMode == 4 || currentGameMode == 5 || currentGameMode == 6 || currentGameMode == 7 || currentGameMode == 8 || currentGameMode == 9 || currentGameMode == 10))
					{
						int num67 = num35 >> 8;
						int num68 = playerY_fixed >> 8;
						int num69;
						int num70;
						int num71;
						if (currentGameMode == 6)
						{
							num69 = 8;
							num70 = 8;
							num71 = 0;
						}
						else
						{
							num69 = ((currplayer_mini != 0) ? 8 : 15);
							num70 = ((currplayer_mini != 0) ? 7 : 15);
							num71 = ((currplayer_mini != 0 && currplayer_gravity == 0) ? 9 : 0);
						}
						int num72 = num67;
						int num73 = num68 + num71;
						int playerRightEdge_fwd = num72 + num69;
						int playerCenterY_fwd;
						if (currplayer_mini != 0)
						{
							int num74 = 16 - num70 >> 1;
							playerCenterY_fwd = num68 + num74 + (num70 >> 1);
							if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8)
							{
								playerCenterY_fwd += ((currplayer_gravity != 0) ? 3 : (-2));
							}
						}
						else
						{
							playerCenterY_fwd = num68 + (num70 >> 1);
						}
						int num75 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
						int num76 = playerRightEdge_fwd / 16;
						int num77 = playerCenterY_fwd / 16;
						int num78 = num77 + num75;
						int num79 = -1;
						string value4 = "OOB";
						if (num76 >= 0 && num76 < mapWidth && num78 >= 0 && num78 < mapHeight)
						{
							int num80 = num78 * mapWidth + num76;
							if (num80 >= 0 && num80 < tiles.Length)
							{
								num79 = tiles[num80];
								value4 = MetatileCollisionTable.GetCollision((byte)num79).ToString();
							}
						}
						AppendSimDebug($"[FWD_CHECK] probe=({playerRightEdge_fwd},{playerCenterY_fwd}) tile=({num76},{num77}) tileIdxY={num78} tid=0x{num79:X2} col={value4} oldX={num67} newX={num5 >> 8}");
						bool flag8 = CheckPixelCollision(playerRightEdge_fwd, playerCenterY_fwd, num75);
						if (flag8)
						{
							int num81 = playerRightEdge_fwd / 16;
							int num82 = playerCenterY_fwd / 16;
							int num83 = num82 + num75;
							if (num81 >= 0 && num81 < mapWidth && num83 >= 0 && num83 < mapHeight)
							{
								int tid = tiles[num83 * mapWidth + num81];
								MetatileCollision metatileCollision = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid));
								if (metatileCollision == MetatileCollision.COL_FLOOR_CEIL || metatileCollision == MetatileCollision.COL_NO_SIDE || SharedPhysics.IsSlopeTile(metatileCollision))
								{
									flag8 = false;
								}
							}
						}
						bool flag9 = PointHitsSpikeFloor(playerRightEdge_fwd, playerCenterY_fwd, num75);
						if (flag8 || flag9)
						{
							string value5 = (flag8 ? "Forward middle pixel collision" : "Forward spike death");
							AppendSimDebug($"[DEATH] {value5} at ({playerRightEdge_fwd},{playerCenterY_fwd}) - post-eject check at OLD X");
							deathTriggered = true;
							deathTileX = playerRightEdge_fwd;
							deathTileY = playerCenterY_fwd;
							paused = true;
							StopMusicAsync();
							try
							{
								base.Dispatcher.BeginInvoke((Action)delegate
								{
									try
									{
										PauseOverlay.Visibility = Visibility.Collapsed;
									}
									catch
									{
									}
									if (base.Owner is MainWindow mainWindow)
									{
										try
										{
											mainWindow.PauseSimulatorPlayback();
										}
										catch
										{
										}
										try
										{
											mainWindow.AddDeathMarker(playerRightEdge_fwd, playerCenterY_fwd);
										}
										catch
										{
										}
									}
								});
							}
							catch
							{
							}
						}
					}
					if (!deathTriggered && !ShouldSkipSideCollisionForSlope() && currentGameMode != 6 && currentGameMode != 10)
					{
						int num84 = num35 >> 8;
						int num85 = playerY_fixed >> 8;
						int num86 = ((currplayer_mini != 0) ? 8 : 15);
						int num87 = ((currplayer_mini != 0) ? 7 : 15);
						int num89;
						if (currplayer_mini != 0)
						{
							int num88 = 16 - num87 >> 1;
							num89 = num85 + num88 + (num87 >> 1);
							if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8)
							{
								num89 += ((currplayer_gravity != 0) ? 3 : (-2));
							}
						}
						else
						{
							num89 = num85 + (num87 >> 1);
						}
						int num90 = num84 + num86;
						int num91 = num90 / 16;
						int num92 = num89 / 16;
						int num93 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
						int num94 = num92 + num93;
						if (num91 >= 0 && num91 < mapWidth && num94 >= 0 && num94 < mapHeight)
						{
							int tid2 = tiles[num94 * mapWidth + num91];
							MetatileCollision metatileCollision2 = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid2));
							if (SharedPhysics.IsSlopeTile(metatileCollision2) && SharedPhysics.SlopeCalc(num90, num89, metatileCollision2).hit)
							{
								int num95 = ((metatileCollision2 == MetatileCollision.COL_SLOPE_RU45 || metatileCollision2 == MetatileCollision.COL_SLOPE_LU45 || (metatileCollision2 >= MetatileCollision.COL_SLOPE_RU22_RIGHT && metatileCollision2 <= MetatileCollision.COL_SLOPE_LU22_LEFT) || (metatileCollision2 >= MetatileCollision.COL_SLOPE_RU66_TOP && metatileCollision2 <= MetatileCollision.COL_SLOPE_LU66_TOP)) ? 2 : (-2));
								playerY_fixed += num95 << 8;
							}
						}
					}
					if (!MainWindow.Option_NoDeath && !deathTriggered && (currentGameMode == 6 || currentGameMode == 10))
					{
						int num96 = num35 >> 8;
						int num97 = playerY_fixed >> 8;
						int num98 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
						bool flag10 = currplayer_mini != 0;
						int num99 = (flag10 ? 4 : 0);
						int num100 = num96 + 4 - 1;
						int num101 = num97 + num99 + 4;
						int num102 = num100 / 16;
						int num103 = num101 / 16;
						int num104 = num103 + num98;
						if (num102 >= 0 && num102 < mapWidth && num104 >= 0 && num104 < mapHeight)
						{
							byte index = (byte)SharedPhysics.MapTileForCollision(tiles[num104 * mapWidth + num102]);
							MetatileCollision metatileCollision3 = MetatileCollisionTable.GetCollision(index);
							if (metatileCollision3 >= MetatileCollision.COL_SLOPE_RD45 && metatileCollision3 <= MetatileCollision.COL_SLOPE_LU66_TOP && (flag10 || metatileCollision3 != MetatileCollision.COL_SLOPE_LU45) && (!flag10 || (metatileCollision3 != MetatileCollision.COL_SLOPE_LU66_TOP && metatileCollision3 != MetatileCollision.COL_SLOPE_LU66_BOT)) && bg_coll_slope(num100, num101, metatileCollision3))
							{
								AppendSimDebug($"[WAVE_DEATH] center slope X={num96} Y={num97} tile=({num102},{num103}) col={metatileCollision3}");
								deathTriggered = true;
								paused = true;
								StopMusicAsync();
								try
								{
									base.Dispatcher.BeginInvoke((Action)delegate
									{
										try
										{
											PauseOverlay.Visibility = Visibility.Collapsed;
										}
										catch
										{
										}
										if (base.Owner is MainWindow mainWindow)
										{
											try
											{
												mainWindow.PauseSimulatorPlayback();
											}
											catch
											{
											}
										}
									});
								}
								catch
								{
								}
							}
						}
						if (!deathTriggered && !ShouldSkipSideCollisionForSlope())
						{
							int num105 = num96 + 8;
							int num106 = num97 + num99 + 4;
							int num107 = num105 / 16;
							int num108 = num106 / 16;
							int num109 = num108 + num98;
							if (num107 >= 0 && num107 < mapWidth && num109 >= 0 && num109 < mapHeight)
							{
								byte index2 = (byte)SharedPhysics.MapTileForCollision(tiles[num109 * mapWidth + num107]);
								MetatileCollision metatileCollision4 = MetatileCollisionTable.GetCollision(index2);
								if (metatileCollision4 >= MetatileCollision.COL_SLOPE_RD45 && metatileCollision4 <= MetatileCollision.COL_SLOPE_LU66_TOP && (flag10 || metatileCollision4 != MetatileCollision.COL_SLOPE_LU45) && (!flag10 || (metatileCollision4 != MetatileCollision.COL_SLOPE_LU66_TOP && metatileCollision4 != MetatileCollision.COL_SLOPE_LU66_BOT)) && bg_coll_slope(num105, num106, metatileCollision4))
								{
									currplayer_slope_frames = 1;
									currplayer_was_on_slope_counter = 3;
									if (!dblocked)
									{
										AppendSimDebug($"[WAVE_DEATH] R-edge slope X={num96} Y={num97} tile=({num107},{num108}) col={metatileCollision4}");
										deathTriggered = true;
										paused = true;
										StopMusicAsync();
										try
										{
											base.Dispatcher.BeginInvoke((Action)delegate
											{
												try
												{
													PauseOverlay.Visibility = Visibility.Collapsed;
												}
												catch
												{
												}
												if (base.Owner is MainWindow mainWindow)
												{
													try
													{
														mainWindow.PauseSimulatorPlayback();
													}
													catch
													{
													}
												}
											});
										}
										catch
										{
										}
									}
									else
									{
										AppendSimDebug($"[WAVE_REDGE_SLOPE_DBLOCKED] X={num96} Y={num97} tile=({num107},{num108}) col={metatileCollision4} sf=1 swoc=3");
									}
								}
							}
						}
					}
					if (!deathTriggered && !camModeActive && CheckDeathCollision(out var deathX_px, out var deathY_px))
					{
						AppendSimDebug($"[DEATH] Death tile collision at ({deathX_px},{deathY_px}) (OLD X)");
						deathTriggered = true;
						deathTileX = deathX_px;
						deathTileY = deathY_px;
						paused = true;
						StopMusicAsync();
						try
						{
							base.Dispatcher.BeginInvoke((Action)delegate
							{
								try
								{
									PauseOverlay.Visibility = Visibility.Collapsed;
								}
								catch
								{
								}
								if (base.Owner is MainWindow mainWindow)
								{
									try
									{
										mainWindow.PauseSimulatorPlayback();
									}
									catch
									{
									}
									try
									{
										mainWindow.AddDeathMarker(deathX_px, deathY_px);
									}
									catch
									{
									}
								}
							});
						}
						catch
						{
						}
					}
					playerX_fixed = num5;
					gravityFlippedThisFrame = false;
					dblocked = false;
					if (dual && !twoplayer)
					{
						AppendSimDebug($"[PLAYER2_START] Processing player 2: X={player_x_fixed[1] >> 8} Y={player_y_fixed[1] >> 8}");
						orbHoldSuppressing[1] = false;
						orbHoldConsumedKeyStillDown[1] = false;
						orbhitonthisframe[1] = false;
						player_x_fixed[0] = playerX_fixed;
						player_y_fixed[0] = playerY_fixed;
						player_vel_y_fixed[0] = playerVelY_fixed;
						player_mini[0] = miniMode;
						player_gravity[0] = currplayer_gravity;
						AppendSimDebug($"[P1_SAVE] player_gravity[0]={player_gravity[0]:X2} currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed}");
						player_wasZeroed[0] = wasZeroedByCollisionLastFrame;
						player_onGround[0] = onGround;
						player_groundStabilize[0] = groundStabilizeCounter;
						player_ballFlipCooldown[0] = ballFlipCooldown;
						player_ballWasGroundedBeforeFlip[0] = ballWasGroundedBeforeFlip;
						player_cubeRotate[0] = cubeRotate_fixed;
						player_cubeRotateMini[0] = cubeRotateMini_fixed;
						player_shipRotate[0] = shipRotate_fixed;
						player_swingRotate[0] = swingcopterRotate_fixed;
						player_footballRotate[0] = footballRotate_fixed;
						SaveSlopeStateForPlayer(0);
						if (pathfinderEnabled)
						{
							bool flag11 = orbhitonthisframe[0];
							if (!flag11 && currentGameMode == 2 && player_ballFlipCooldown[0] > 0)
							{
								flag11 = true;
							}
							bool flag12 = pfRawSequenceInput && !flag11;
							Interlocked.Exchange(ref keyXPressedCount, 0);
							Interlocked.Exchange(ref ballToggleRequested, 0);
							if (currentGameMode == 2)
							{
								if (flag12)
								{
									p2BallHoldCounter = 8;
									Interlocked.Exchange(ref keyXPressedCount, 1);
									keyXHeld = true;
									Interlocked.Exchange(ref ballToggleRequested, 1);
								}
								else if (p2BallHoldCounter > 0)
								{
									keyXHeld = true;
								}
								else
								{
									keyXHeld = false;
								}
								if (p2BallHoldCounter > 0)
								{
									p2BallHoldCounter--;
								}
								if (player_ballFlipCooldown[1] > 0)
								{
									p2BallHoldCounter = 0;
								}
							}
							else if (pfInputThisFrame && !flag11)
							{
								Interlocked.Exchange(ref keyXPressedCount, 1);
								keyXHeld = true;
							}
							else
							{
								keyXHeld = false;
							}
							AppendSimDebug($"[DUAL_INPUT] pfInput={pfInputThisFrame} pfRaw={pfRawSequenceInput} p1Consumed={flag11} p2Hold={p2BallHoldCounter} pressCount={keyXPressedCount} held={keyXHeld}");
						}
						currplayer = 1;
						applyPlayer2Colors = true;
						playerX_fixed = player_x_fixed[1];
						playerY_fixed = player_y_fixed[1];
						playerVelY_fixed = player_vel_y_fixed[1];
						miniMode = player_mini[1];
						currplayer_mini = (byte)(miniMode ? 1u : 0u);
						gravityFlipped = player_gravity[1] != 0;
						currplayer_gravity = player_gravity[1];
						gravityReversed = player_gravity[1] != 0;
						effectiveInvertedByW = gravityReversed;
						currplayer_table_idx = ((currplayer_gravity != 0) ? 1 : 0) | ((currplayer_mini != 0) ? 4 : 0);
						LoadSlopeStateForPlayer(1);
						wasZeroedByCollisionLastFrame = player_wasZeroed[1];
						onGround = player_onGround[1];
						groundStabilizeCounter = player_groundStabilize[1];
						ballFlipCooldown = player_ballFlipCooldown[1];
						ballWasGroundedBeforeFlip = player_ballWasGroundedBeforeFlip[1];
						cubeRotate_fixed = player_cubeRotate[1];
						cubeRotateMini_fixed = player_cubeRotateMini[1];
						shipRotate_fixed = player_shipRotate[1];
						swingcopterRotate_fixed = player_swingRotate[1];
						footballRotate_fixed = player_footballRotate[1];
						try
						{
							orbhitonthisframe[currplayer] = false;
							CheckSinglePortal();
							CheckBluePadCollision();
							CheckGravityPortals();
							CheckGravityModPortals();
							CheckMiniGrowthPortals();
							CheckPadCollision();
							CheckSpiderOrbPadCollision();
							CheckDashOrbCollision();
							CheckAlphabetBlocks();
							CheckCoinCollision();
							int num110 = (miniMode ? 8 : 15);
							int num111 = (miniMode ? 7 : 15);
							int num112 = (playerX_fixed >> 8) + 1;
							int num113 = num112 + num110;
							int num114 = (playerY_fixed >> 8) + GetMiniSpriteOffsetY();
							int num115 = num114 + num111;
							int num116 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
							for (int num117 = 0; num117 < nonEmptySpriteIndices.Length; num117++)
							{
								int num118 = nonEmptySpriteIndices[num117];
								int num119 = sprites[num118];
								if (num119 < 0 || !speedPortalMap.ContainsKey(num119) || processedSpeedPortals.Contains(num118))
								{
									continue;
								}
								int num120 = num118 % mapWidth;
								int num121 = num118 / mapWidth;
								int num122 = num119 & 0xFF;
								int num123 = num122;
								int num124 = -1;
								bool flag13 = !SharedPhysics.IsSpeedPortal(num122) && !SharedPhysics.IsGameModePortal(num122) && !SharedPhysics.IsGravityPortal(num122) && !SharedPhysics.IsMiniGrowthPortal(num122);
								if (spriteAnchors != null && spriteAnchors.TryGetValue(num118, out (int, int) value6))
								{
									num124 = value6.Item2 * mapWidth + value6.Item1;
									if (flag13 && num124 >= 0 && num124 < sprites.Length)
									{
										int num125 = sprites[num124];
										if (num125 >= 0 && num125 < 256)
										{
											num123 = num125 & 0xFF;
										}
									}
								}
								int val4 = ((num123 >= 0 && num123 < sprite_widths.Length) ? sprite_widths[num123] : 16);
								int num126 = ((num123 >= 0 && num123 < sprite_heights.Length) ? sprite_heights[num123] : 16);
								if (num126 >= 252)
								{
									continue;
								}
								int num127 = ((num123 >= 0 && num123 < sprite_x_offset.Length) ? sprite_x_offset[num123] : 0);
								int num128 = ((num123 >= 0 && num123 < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[num123] : 0);
								int num129 = 0;
								int num130 = 0;
								(int, int) value8;
								if (num124 >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num124, out (int, int) value7))
								{
									(num129, num130) = value7;
								}
								else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(num118, out value8))
								{
									(num129, num130) = value8;
								}
								int num131 = num120 * 16 + num127 + num129;
								int num132 = (num121 - num116) * 16 + num128 + num130;
								int num133 = num131 + Math.Max(1, val4);
								int num134 = num132 + Math.Max(1, num126);
								bool flag14 = num113 >= num131 && num133 >= num112;
								bool flag15 = num115 >= num132 && num134 >= num114;
								if (flag14 && flag15)
								{
									int num135 = (playerVelX_fixed = (currentSpeed_fixed = speedPortalMap[num119]));
									switch (num135)
									{
									case 571:
										speed = 0;
										break;
									case 708:
										speed = 1;
										break;
									case 881:
										speed = 2;
										break;
									case 1065:
										speed = 3;
										break;
									case 1310:
										speed = 4;
										break;
									}
									AppendSimDebug($"[SPEED_P2] sid=0x{num119:X2} VelX -> 0x{num135:X4}");
								}
							}
						}
						catch
						{
						}
						try
						{
							gravityFlipped = currplayer_gravity != 0;
							switch (currentGameMode)
							{
							case 0:
								ProcessCubePhysics_Fresh();
								if (miniMode)
								{
									UpdateCubeRotationMini();
								}
								else
								{
									UpdateCubeRotation();
								}
								break;
							case 1:
								ShipPhysics_Fresh();
								UpdateShipRotation();
								break;
							case 2:
								BallPhysics_Fresh();
								break;
							case 3:
								UfoPhysics_Fresh();
								break;
							case 4:
								RobotPhysics_Fresh();
								if (miniMode)
								{
									UpdateCubeRotationMini();
								}
								else
								{
									UpdateCubeRotation();
								}
								break;
							case 5:
								SpiderPhysics_Fresh();
								break;
							case 6:
								WavePhysics_Fresh();
								break;
							case 7:
								BallPhysics_Fresh();
								UpdateSwingcopterRotation();
								break;
							case 8:
								NinjaPhysics_Fresh();
								if (miniMode)
								{
									UpdateCubeRotationMini();
								}
								else
								{
									UpdateCubeRotation();
								}
								break;
							case 9:
								BallPhysics_Fresh();
								break;
							case 10:
								SnakePhysics_Fresh();
								break;
							case 11:
								FootballPhysics_Fresh();
								UpdateFootballRotation();
								break;
							}
							gravityFlipped = currplayer_gravity != 0;
							dblocked = false;
							if (dashing[currplayer] != 0 && !IsXDownAsync() && !keyXHeld)
							{
								velocityY = 0;
								playerVelY_fixed = 0;
								dashing[currplayer] = 0;
							}
							if (orbed[currplayer] && !IsXDownAsync() && !keyXHeld)
							{
								orbed[currplayer] = false;
							}
						}
						catch (Exception ex)
						{
							AppendSimDebug("[PLAYER2] Physics error: " + ex.Message);
						}
						playerX_fixed = player_x_fixed[0];
						player_x_fixed[1] = player_x_fixed[0];
						AppendSimDebug($"[PLAYER2_X_SYNC] Synced X to player 1: {playerX_fixed >> 8}");
						if (!MainWindow.Option_NoDeath && !deathTriggered)
						{
							int num136 = playerX_fixed >> 8;
							int num137 = playerY_fixed >> 8;
							int num138 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
							if (CheckFloorSpikes(num136, num137, out var p2FsDeathX, out var p2FsDeathY))
							{
								AppendSimDebug($"[P2_DEATH] Floor spike at ({p2FsDeathX},{p2FsDeathY})");
								deathTriggered = true;
								deathTileX = p2FsDeathX;
								deathTileY = p2FsDeathY;
								paused = true;
								StopMusicAsync();
								try
								{
									base.Dispatcher.BeginInvoke((Action)delegate
									{
										try
										{
											PauseOverlay.Visibility = Visibility.Collapsed;
										}
										catch
										{
										}
										if (base.Owner is MainWindow mainWindow)
										{
											try
											{
												mainWindow.PauseSimulatorPlayback();
											}
											catch
											{
											}
											try
											{
												mainWindow.AddDeathMarker(p2FsDeathX, p2FsDeathY);
											}
											catch
											{
											}
										}
									});
								}
								catch
								{
								}
							}
							if (!deathTriggered && !ShouldSkipSideCollisionForSlope())
							{
								int num139 = ((currentGameMode == 6) ? 8 : ((currplayer_mini != 0) ? 8 : 15));
								int num140 = ((currentGameMode == 6) ? 8 : ((currplayer_mini != 0) ? 7 : 15));
								int rightEdge_p2 = num136 + num139;
								int centerY_p2;
								if (currentGameMode == 6)
								{
									centerY_p2 = num137 + (num140 >> 1);
								}
								else if (currplayer_mini != 0)
								{
									int num141 = 16 - num140 >> 1;
									centerY_p2 = num137 + num141 + (num140 >> 1);
									if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8)
									{
										centerY_p2 += ((currplayer_gravity != 0) ? 3 : (-2));
									}
								}
								else
								{
									centerY_p2 = num137 + (num140 >> 1);
								}
								bool flag16 = CheckPixelCollision(rightEdge_p2, centerY_p2, num138);
								if (flag16)
								{
									int num142 = rightEdge_p2 / 16;
									int num143 = centerY_p2 / 16;
									int num144 = num143 + num138;
									if (num142 >= 0 && num142 < mapWidth && num144 >= 0 && num144 < mapHeight)
									{
										int tid3 = tiles[num144 * mapWidth + num142];
										MetatileCollision metatileCollision5 = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid3));
										if (metatileCollision5 == MetatileCollision.COL_FLOOR_CEIL || metatileCollision5 == MetatileCollision.COL_NO_SIDE || SharedPhysics.IsSlopeTile(metatileCollision5))
										{
											flag16 = false;
										}
									}
								}
								bool flag17 = PointHitsSpikeFloor(rightEdge_p2, centerY_p2, num138);
								if (flag16 || flag17)
								{
									string value9 = (flag16 ? "Forward collision" : "Forward spike death");
									AppendSimDebug($"[P2_DEATH] {value9} at ({rightEdge_p2},{centerY_p2})");
									deathTriggered = true;
									deathTileX = rightEdge_p2;
									deathTileY = centerY_p2;
									paused = true;
									StopMusicAsync();
									try
									{
										base.Dispatcher.BeginInvoke((Action)delegate
										{
											try
											{
												PauseOverlay.Visibility = Visibility.Collapsed;
											}
											catch
											{
											}
											if (base.Owner is MainWindow mainWindow)
											{
												try
												{
													mainWindow.PauseSimulatorPlayback();
												}
												catch
												{
												}
												try
												{
													mainWindow.AddDeathMarker(rightEdge_p2, centerY_p2);
												}
												catch
												{
												}
											}
										});
									}
									catch
									{
									}
								}
								if (!deathTriggered && currentGameMode != 6 && currentGameMode != 10)
								{
									int num145 = rightEdge_p2 / 16;
									int num146 = centerY_p2 / 16;
									int num147 = num146 + num138;
									if (num145 >= 0 && num145 < mapWidth && num147 >= 0 && num147 < mapHeight)
									{
										int tid4 = tiles[num147 * mapWidth + num145];
										MetatileCollision metatileCollision6 = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid4));
										if (SharedPhysics.IsSlopeTile(metatileCollision6) && SharedPhysics.SlopeCalc(rightEdge_p2, centerY_p2, metatileCollision6).hit)
										{
											int num148;
											switch (metatileCollision6)
											{
											default:
												num148 = ((metatileCollision6 >= MetatileCollision.COL_SLOPE_RU66_TOP && metatileCollision6 <= MetatileCollision.COL_SLOPE_LU66_TOP) ? 1 : 0);
												break;
											case MetatileCollision.COL_SLOPE_RU45:
											case MetatileCollision.COL_SLOPE_LU45:
											case MetatileCollision.COL_SLOPE_RU22_RIGHT:
											case MetatileCollision.COL_SLOPE_RU22_LEFT:
											case MetatileCollision.COL_SLOPE_LU22_RIGHT:
											case MetatileCollision.COL_SLOPE_LU22_LEFT:
												num148 = 1;
												break;
											}
											bool flag18 = (byte)num148 != 0;
											playerY_fixed += (flag18 ? 2 : (-2)) << 8;
										}
									}
								}
							}
							if (!deathTriggered && (currentGameMode == 6 || currentGameMode == 10))
							{
								int num149 = 8;
								int num150 = 8;
								bool flag19 = currplayer_mini != 0;
								int num151 = num136 + (num149 >> 1) - 1;
								int num152 = num137 + (num150 >> 1);
								int num153 = num151 / 16;
								int num154 = num152 / 16;
								int num155 = num154 + num138;
								if (num153 >= 0 && num153 < mapWidth && num155 >= 0 && num155 < mapHeight)
								{
									byte index3 = (byte)SharedPhysics.MapTileForCollision(tiles[num155 * mapWidth + num153]);
									MetatileCollision metatileCollision7 = MetatileCollisionTable.GetCollision(index3);
									if (metatileCollision7 >= MetatileCollision.COL_SLOPE_RD45 && metatileCollision7 <= MetatileCollision.COL_SLOPE_LU66_TOP && (flag19 || metatileCollision7 != MetatileCollision.COL_SLOPE_LU45) && (!flag19 || (metatileCollision7 != MetatileCollision.COL_SLOPE_LU66_TOP && metatileCollision7 != MetatileCollision.COL_SLOPE_LU66_BOT)) && bg_coll_slope(num151, num152, metatileCollision7))
									{
										AppendSimDebug($"[P2_DEATH] Wave center slope at ({num153},{num154})");
										deathTriggered = true;
										paused = true;
										StopMusicAsync();
										try
										{
											base.Dispatcher.BeginInvoke((Action)delegate
											{
												try
												{
													PauseOverlay.Visibility = Visibility.Collapsed;
												}
												catch
												{
												}
												if (base.Owner is MainWindow mainWindow)
												{
													try
													{
														mainWindow.PauseSimulatorPlayback();
													}
													catch
													{
													}
												}
											});
										}
										catch
										{
										}
									}
								}
								if (!deathTriggered && !ShouldSkipSideCollisionForSlope())
								{
									int num156 = num136 + num149;
									int num157 = num137 + (num150 >> 1);
									int num158 = num156 / 16;
									int num159 = num157 / 16;
									int num160 = num159 + num138;
									if (num158 >= 0 && num158 < mapWidth && num160 >= 0 && num160 < mapHeight)
									{
										byte index4 = (byte)SharedPhysics.MapTileForCollision(tiles[num160 * mapWidth + num158]);
										MetatileCollision metatileCollision8 = MetatileCollisionTable.GetCollision(index4);
										if (metatileCollision8 >= MetatileCollision.COL_SLOPE_RD45 && metatileCollision8 <= MetatileCollision.COL_SLOPE_LU66_TOP && (flag19 || metatileCollision8 != MetatileCollision.COL_SLOPE_LU45) && (!flag19 || (metatileCollision8 != MetatileCollision.COL_SLOPE_LU66_TOP && metatileCollision8 != MetatileCollision.COL_SLOPE_LU66_BOT)) && bg_coll_slope(num156, num157, metatileCollision8))
										{
											AppendSimDebug($"[P2_DEATH] Wave R-edge slope at ({num158},{num159})");
											deathTriggered = true;
											paused = true;
											StopMusicAsync();
											try
											{
												base.Dispatcher.BeginInvoke((Action)delegate
												{
													try
													{
														PauseOverlay.Visibility = Visibility.Collapsed;
													}
													catch
													{
													}
													if (base.Owner is MainWindow mainWindow)
													{
														try
														{
															mainWindow.PauseSimulatorPlayback();
														}
														catch
														{
														}
													}
												});
											}
											catch
											{
											}
										}
									}
								}
							}
							if (!deathTriggered)
							{
								if (CheckDeathCollision(out var p2DeathX, out var p2DeathY))
								{
									AppendSimDebug($"[P2_DEATH] Death tile at ({p2DeathX},{p2DeathY})");
									deathTriggered = true;
									deathTileX = p2DeathX;
									deathTileY = p2DeathY;
									paused = true;
									StopMusicAsync();
									try
									{
										base.Dispatcher.BeginInvoke((Action)delegate
										{
											try
											{
												PauseOverlay.Visibility = Visibility.Collapsed;
											}
											catch
											{
											}
											if (base.Owner is MainWindow mainWindow)
											{
												try
												{
													mainWindow.PauseSimulatorPlayback();
												}
												catch
												{
												}
												try
												{
													mainWindow.AddDeathMarker(p2DeathX, p2DeathY);
												}
												catch
												{
												}
											}
										});
									}
									catch
									{
									}
								}
							}
						}
						player_x_fixed[1] = playerX_fixed;
						player_y_fixed[1] = playerY_fixed;
						player_vel_y_fixed[1] = playerVelY_fixed;
						player_mini[1] = miniMode;
						player_gravity[1] = currplayer_gravity;
						player_wasZeroed[1] = wasZeroedByCollisionLastFrame;
						player_onGround[1] = onGround;
						player_groundStabilize[1] = groundStabilizeCounter;
						player_ballFlipCooldown[1] = ballFlipCooldown;
						player_ballWasGroundedBeforeFlip[1] = ballWasGroundedBeforeFlip;
						player_cubeRotate[1] = cubeRotate_fixed;
						player_cubeRotateMini[1] = cubeRotateMini_fixed;
						player_shipRotate[1] = shipRotate_fixed;
						player_swingRotate[1] = swingcopterRotate_fixed;
						player_footballRotate[1] = footballRotate_fixed;
						SaveSlopeStateForPlayer(1);
						AppendSimDebug($"[PLAYER2_END] Player 2 final state: X={player_x_fixed[1] >> 8} Y={player_y_fixed[1] >> 8}");
						if (singlePortalExitPending)
						{
							singlePortalExitPending = false;
							dual = false;
							_prevDualActiveForP2Path = false;
							AppendSimDebug($"[SINGLE_PORTAL_EXIT] dual=false, P1 gets P2's state: Y={player_y_fixed[0] >> 8}, velY={player_vel_y_fixed[0]:X4}, grav={player_gravity[0]:X2}");
						}
						int p2_gameMode = currentGameMode;
						bool p2_mini = miniMode;
						int p2_velY = playerVelY_fixed;
						bool p2_gravReversed = gravityReversed;
						currplayer = 0;
						applyPlayer2Colors = false;
						playerX_fixed = player_x_fixed[0];
						playerY_fixed = player_y_fixed[0];
						playerVelY_fixed = player_vel_y_fixed[0];
						miniMode = player_mini[0];
						currplayer_mini = (byte)(miniMode ? 1u : 0u);
						gravityFlipped = player_gravity[0] != 0;
						currplayer_gravity = player_gravity[0];
						gravityReversed = player_gravity[0] != 0;
						effectiveInvertedByW = gravityReversed;
						currplayer_table_idx = ((currplayer_gravity != 0) ? 1 : 0) | ((currplayer_mini != 0) ? 4 : 0);
						AppendSimDebug($"[P1_RESTORE] player_gravity[0]={player_gravity[0]:X2} currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed}");
						wasZeroedByCollisionLastFrame = player_wasZeroed[0];
						onGround = player_onGround[0];
						groundStabilizeCounter = player_groundStabilize[0];
						ballFlipCooldown = player_ballFlipCooldown[0];
						ballWasGroundedBeforeFlip = player_ballWasGroundedBeforeFlip[0];
						cubeRotate_fixed = player_cubeRotate[0];
						cubeRotateMini_fixed = player_cubeRotateMini[0];
						shipRotate_fixed = player_shipRotate[0];
						swingcopterRotate_fixed = player_swingRotate[0];
						footballRotate_fixed = player_footballRotate[0];
						LoadSlopeStateForPlayer(0);
						try
						{
							base.Dispatcher?.BeginInvoke((Action)delegate
							{
								lock (simLock)
								{
									try
									{
										int num204 = currentGameMode;
										bool flag23 = miniMode;
										int num205 = playerVelY_fixed;
										bool flag24 = gravityReversed;
										bool flag25 = gravityFlipped;
										int num206 = cubeRotate_fixed;
										int num207 = cubeRotateMini_fixed;
										int num208 = shipRotate_fixed;
										int num209 = swingcopterRotate_fixed;
										int num210 = footballRotate_fixed;
										currentGameMode = p2_gameMode;
										miniMode = p2_mini;
										playerVelY_fixed = p2_velY;
										gravityReversed = p2_gravReversed;
										gravityFlipped = p2_gravReversed;
										cubeRotate_fixed = player_cubeRotate[1];
										cubeRotateMini_fixed = player_cubeRotateMini[1];
										shipRotate_fixed = player_shipRotate[1];
										swingcopterRotate_fixed = player_swingRotate[1];
										footballRotate_fixed = player_footballRotate[1];
										applyPlayer2Colors = true;
										UpdatePlayerImageForMode();
										if (p2_gameMode == 0 || p2_gameMode == 4 || p2_gameMode == 8)
										{
											try
											{
												if (!p2_mini)
												{
													int cubeSpriteFrame = GetCubeSpriteFrame();
													int num211 = cubeSpriteFrame & 7;
													bool flag26 = (cubeSpriteFrame & 0x40) != 0;
													bool flag27 = (cubeSpriteFrame & 0x80) != 0;
													string[] array3 = ((p2_gameMode == 8) ? s_ninjaFrameNames : s_cubeFrameNames);
													if (num211 >= array3.Length)
													{
														num211 = 0;
													}
													string text = array3[num211];
													BitmapImage bitmapImage = LoadCachedResourceImage(text);
													if (bitmapImage != null && playerImage != null)
													{
														playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage) ?? bitmapImage;
														playerImage.Tag = text;
														if (flag26 || flag27)
														{
															playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
															playerImage.RenderTransform = new ScaleTransform((!flag26) ? 1 : (-1), (!flag27) ? 1 : (-1));
														}
														else
														{
															playerImage.RenderTransform = Transform.Identity;
														}
													}
												}
												else
												{
													int cubeSpriteMiniFrame = GetCubeSpriteMiniFrame();
													int num212 = (cubeRotateMini_fixed >> 8) & 0xFF;
													if (num212 < 0 || num212 >= 24)
													{
														num212 = 0;
													}
													int num213 = drawcube_sprite_table[num212];
													bool flag28 = (num213 & 0x40) != 0;
													bool flag29 = (num213 & 0x80) != 0;
													string[] array4 = ((p2_gameMode != 8) ? new string[5] { "cube_mini_00_frame_0.png", "cube_mini_01_frame_1.png", "cube_mini_02_frame_2.png", "cube_mini_03_frame_3.png", "cube_mini_04_frame_4.png" } : new string[5] { "ninja_mini_00_frame_0.png", "ninja_mini_01_frame_1.png", "ninja_mini_02_frame_2.png", "ninja_mini_03_frame_3.png", "ninja_mini_04_frame_4.png" });
													string text2 = array4[cubeSpriteMiniFrame];
													BitmapImage bitmapImage2 = LoadCachedResourceImage(text2);
													if (bitmapImage2 != null && playerImage != null)
													{
														playerImage.Source = App.EnsureUnfrozenForRender(bitmapImage2) ?? bitmapImage2;
														playerImage.Tag = text2;
														if (flag28 || flag29)
														{
															playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
															playerImage.RenderTransform = new ScaleTransform((!flag28) ? 1 : (-1), (!flag29) ? 1 : (-1));
														}
														else
														{
															playerImage.RenderTransform = Transform.Identity;
														}
													}
												}
											}
											catch
											{
											}
										}
										if (player2Image != null && playerImage != null && playerImage.Source != null)
										{
											player2Image.Source = playerImage.Source;
											player2Image.Width = playerImage.Width;
											player2Image.Height = playerImage.Height;
											player2Image.RenderTransformOrigin = playerImage.RenderTransformOrigin;
											player2Image.RenderTransform = playerImage.RenderTransform;
										}
										currentGameMode = num204;
										miniMode = flag23;
										playerVelY_fixed = num205;
										gravityReversed = flag24;
										gravityFlipped = flag25;
										cubeRotate_fixed = num206;
										cubeRotateMini_fixed = num207;
										shipRotate_fixed = num208;
										swingcopterRotate_fixed = num209;
										footballRotate_fixed = num210;
										applyPlayer2Colors = false;
										UpdatePlayerImageForMode();
									}
									catch
									{
									}
								}
							});
						}
						catch
						{
						}
					}
				}
				catch (Exception ex2)
				{
					AppendSimDebug($"Game mode physics error (mode {currentGameMode}): {ex2.Message}");
				}
			}
			bool flag20 = false;
			if (cameraY_fixed < 0)
			{
				cameraY_fixed = 0;
			}
			if (cameraY_fixed > num56)
			{
				cameraY_fixed = num56;
			}
			if (playerY_fixed > num57)
			{
				playerY_fixed = num57;
				playerVelY_fixed = 0;
			}
			int num161 = cameraX_fixed + 32768;
			int? num162 = null;
			if (!physicsEnabled)
			{
				for (int num163 = 0; num163 < nonEmptySpriteIndices.Length; num163++)
				{
					int num164 = nonEmptySpriteIndices[num163];
					int num165 = sprites[num164];
					if (num165 < 0 || !speedPortalMap.ContainsKey(num165))
					{
						continue;
					}
					int num166;
					if (spriteAnchors != null && spriteAnchors.TryGetValue(num164, out (int, int) value10))
					{
						(num166, _) = value10;
					}
					else
					{
						num166 = num164 % mapWidth;
					}
					int num167 = num166;
					int num168 = num167 * 16 + 8 << 8;
					if (!camModeActive)
					{
						int num169 = (miniMode ? 8 : 15);
						int num170 = (miniMode ? 7 : 15);
						int num171 = (playerX_fixed >> 8) + 1;
						int playerRight_px = num171 + num169 - 1;
						int num172 = playerY_fixed >> 8;
						num172 += GetMiniSpriteOffsetY();
						int playerBottom_px = num172 + num170 - 1;
						if (SpriteIntersectsPlayer(num164, num165, num171, playerRight_px, num172, playerBottom_px) && !processedSpeedPortals.Contains(num164))
						{
							num162 = speedPortalMap[num165];
							processedSpeedPortals.Add(num164);
							break;
						}
					}
					else if (flag7)
					{
						if (num168 > num3 && num168 <= 20480 && !processedSpeedPortals.Contains(num164))
						{
							num162 = speedPortalMap[num165];
							processedSpeedPortals.Add(num164);
							break;
						}
					}
					else if (num168 <= num161 && !processedSpeedPortals.Contains(num164))
					{
						num162 = speedPortalMap[num165];
						processedSpeedPortals.Add(num164);
						break;
					}
				}
				if (num162.HasValue)
				{
					currentSpeed_fixed = num162.Value;
					playerVelX_fixed = num162.Value;
					if (num162.Value == 571)
					{
						speed = 0;
					}
					else if (num162.Value == 708)
					{
						speed = 1;
					}
					else if (num162.Value == 881)
					{
						speed = 2;
					}
					else if (num162.Value == 1065)
					{
						speed = 3;
					}
					else if (num162.Value == 1310)
					{
						speed = 4;
					}
					else if (num162.Value == 366)
					{
						speed = 5;
					}
					try
					{
						base.Dispatcher?.BeginInvoke((Action)delegate
						{
							if (SpeedComboBox != null && speed >= 0 && speed < SpeedComboBox.Items.Count)
							{
								SpeedComboBox.SelectedIndex = speed;
							}
						});
					}
					catch
					{
					}
				}
			}
			int num173 = int.MaxValue;
			int? num174 = null;
			int? num175 = null;
			int num176 = int.MaxValue;
			int? num177 = null;
			int? num178 = null;
			int num179 = int.MaxValue;
			int? num180 = null;
			int? num181 = null;
			for (int num182 = 0; num182 < nonEmptySpriteIndices.Length; num182++)
			{
				int num183 = nonEmptySpriteIndices[num182];
				int num184 = sprites[num183];
				if (num184 < 0 || !IsColorTriggerSprite(num184))
				{
					continue;
				}
				int num185;
				if (spriteAnchors != null && spriteAnchors.TryGetValue(num183, out (int, int) value11))
				{
					(num185, _) = value11;
				}
				else
				{
					num185 = num183 % mapWidth;
				}
				int num186 = num185;
				int num187 = num186 * 16 + 8 << 8;
				if (flag7)
				{
					if (num187 > num3 && num187 <= 20480)
					{
						if (IsBackgroundTrigger(num184))
						{
							if (num187 < num173)
							{
								num173 = num187;
								num174 = num183;
								num175 = num184;
							}
						}
						else if (IsTileTrigger(num184))
						{
							if (num187 < num176)
							{
								num176 = num187;
								num177 = num183;
								num178 = num184;
							}
						}
						else if (IsGroundTrigger(num184) && num187 < num179)
						{
							num179 = num187;
							num180 = num183;
							num181 = num184;
						}
					}
					else
					{
						if (processedColorTriggers.Contains(num183) && num187 > 20480)
						{
							processedColorTriggers.Remove(num183);
						}
						if (processedGravityPortals.Contains(num183) && num187 > 20480)
						{
							processedGravityPortals.Remove(num183);
						}
						if (processedGravityModPortals.Contains(num183) && num187 > 20480)
						{
							processedGravityModPortals.Remove(num183);
						}
						if (processedTeleportPortals.Contains(num183) && num187 > 20480)
						{
							processedTeleportPortals.Remove(num183);
						}
					}
				}
				else if (num187 <= num161)
				{
					if (processedColorTriggers.Contains(num183))
					{
						continue;
					}
					if (IsBackgroundTrigger(num184))
					{
						if (num187 < num173)
						{
							num173 = num187;
							num174 = num183;
							num175 = num184;
						}
					}
					else if (IsTileTrigger(num184))
					{
						if (num187 < num176)
						{
							num176 = num187;
							num177 = num183;
							num178 = num184;
						}
					}
					else if (IsGroundTrigger(num184) && num187 < num179)
					{
						num179 = num187;
						num180 = num183;
						num181 = num184;
					}
				}
				else
				{
					if (processedColorTriggers.Contains(num183))
					{
						processedColorTriggers.Remove(num183);
					}
					if (processedGravityPortals.Contains(num183))
					{
						processedGravityPortals.Remove(num183);
					}
					if (processedGravityModPortals.Contains(num183))
					{
						processedGravityModPortals.Remove(num183);
					}
					if (processedGameModePortals.Contains(num183))
					{
						processedGameModePortals.Remove(num183);
					}
					if (processedOrbs.Contains(num183))
					{
						processedOrbs.Remove(num183);
					}
					if (processedTeleportPortals.Contains(num183))
					{
						processedTeleportPortals.Remove(num183);
					}
				}
			}
			if (num174.HasValue)
			{
				pendingBgIdx = num174 ?? (-1);
				pendingBgSid = num175 ?? (-1);
				pendingTintChange = true;
			}
			if (num177.HasValue)
			{
				pendingTileIdx = num177 ?? (-1);
				pendingTileSid = num178 ?? (-1);
				pendingTintChange = true;
			}
			if (num180.HasValue)
			{
				pendingGroundIdx = num180 ?? (-1);
				pendingGroundSid = num181 ?? (-1);
				pendingTintChange = true;
			}
			if (!levelCompleteTriggered && !deathTriggered)
			{
				int num188 = cameraY_fixed >> 8;
				int num189 = num188 + 240;
				for (int num190 = 0; num190 < nonEmptySpriteIndices.Length; num190++)
				{
					int num191 = nonEmptySpriteIndices[num190];
					int num192 = sprites[num191];
					if (num192 != 15 || processedEndLevelTriggers.Contains(num191))
					{
						continue;
					}
					int num193;
					int num194;
					if (spriteAnchors != null && spriteAnchors.TryGetValue(num191, out (int, int) value12))
					{
						(num193, num194) = value12;
					}
					else
					{
						num193 = num191 % mapWidth;
						num194 = num191 / mapWidth;
					}
					int num195 = num194 * 16;
					if (num195 + 16 < num188 || num195 >= num189)
					{
						continue;
					}
					int num196 = num193 * 16 + 8 << 8;
					bool flag21 = false;
					if (flag7)
					{
						if (num196 > num3 && num196 <= 20480)
						{
							flag21 = true;
						}
					}
					else if (num196 <= num161)
					{
						flag21 = true;
					}
					if (flag21)
					{
						processedEndLevelTriggers.Add(num191);
						levelCompleteTriggered = true;
						paused = true;
						AppendSimDebug($"[LEVEL_COMPLETE] End-level trigger 0x0F at anchor tile X={num193}");
						try
						{
							List<(int spriteIndex, int spriteId)> coinInfoSnapshot = new List<(int, int)>(collectedCoinInfo);
							base.Dispatcher?.BeginInvoke((Action)delegate
							{
								try
								{
									PauseOverlay.Visibility = Visibility.Collapsed;
								}
								catch
								{
								}
								try
								{
									LevelCompleteOverlay.Visibility = Visibility.Visible;
								}
								catch
								{
								}
								try
								{
									PopulateCoinDisplay(coinInfoSnapshot);
								}
								catch
								{
								}
							});
						}
						catch
						{
						}
						break;
					}
					if (num196 > num161 && processedEndLevelTriggers.Contains(num191))
					{
						processedEndLevelTriggers.Remove(num191);
					}
				}
			}
			for (int num197 = 0; num197 < nonEmptySpriteIndices.Length; num197++)
			{
				int num198 = nonEmptySpriteIndices[num197];
				int num199 = sprites[num198];
				if ((num199 != 111 && num199 != 127) || processedPlayerInvisTriggers.Contains(num198))
				{
					continue;
				}
				int num200;
				if (spriteAnchors != null && spriteAnchors.TryGetValue(num198, out (int, int) value13))
				{
					(num200, _) = value13;
				}
				else
				{
					num200 = num198 % mapWidth;
				}
				int num201 = num200;
				int num202 = num201 * 16 + 8 << 8;
				bool flag22 = false;
				if (flag7)
				{
					if (num202 > num3 && num202 <= 20480)
					{
						flag22 = true;
					}
				}
				else if (num202 <= num161)
				{
					flag22 = true;
				}
				if (flag22)
				{
					playerInvis = num199 == 111;
					processedPlayerInvisTriggers.Add(num198);
				}
				else if (num202 > num161 && processedPlayerInvisTriggers.Contains(num198))
				{
					processedPlayerInvisTriggers.Remove(num198);
				}
			}
		}
		if (forcedTrails > 0)
		{
			for (int num203 = playerOldPosY.Length - 1; num203 > 0; num203--)
			{
				playerOldPosY[num203] = playerOldPosY[num203 - 1];
			}
			playerOldPosY[0] = playerY_fixed;
		}
		if (!pendingTintChange)
		{
			return;
		}
		try
		{
			base.Dispatcher?.BeginInvoke((Action)delegate
			{
				try
				{
					ApplyPendingTints();
				}
				catch
				{
				}
			});
		}
		catch
		{
		}
	}

	private void PopulateCoinDisplay(List<(int spriteIndex, int spriteId)> coinInfo)
	{
		try
		{
			if (CoinDisplayPanel == null)
			{
				return;
			}
			CoinDisplayPanel.Children.Clear();
			if (coinInfo == null || coinInfo.Count == 0)
			{
				return;
			}
			foreach (var (num, num2) in coinInfo)
			{
				try
				{
					if (!SharedPhysics.IsMiniCoinSprite(num2))
					{
						ImageSource imageSource = null;
						if (spriteImages != null && num2 >= 0 && num2 < spriteImages.Length)
						{
							imageSource = spriteImages[num2];
						}
						if (imageSource != null)
						{
							Image image = new Image
							{
								Source = imageSource,
								Width = 16.0,
								Height = 16.0,
								Margin = new Thickness(4.0, 0.0, 4.0, 0.0),
								SnapsToDevicePixels = true
							};
							RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
							CoinDisplayPanel.Children.Add(image);
						}
						else
						{
							TextBlock element = new TextBlock
							{
								Text = $"0x{num2:X2}",
								Foreground = new SolidColorBrush(Colors.Gold),
								FontSize = 10.0,
								FontFamily = new FontFamily("Consolas"),
								Margin = new Thickness(4.0, 0.0, 4.0, 0.0),
								VerticalAlignment = VerticalAlignment.Center
							};
							CoinDisplayPanel.Children.Add(element);
						}
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	private void ApplyPendingTints()
	{
		try
		{
			bool flag = pendingTintChangeIsStartup;
			int num = pendingBgIdx;
			int num2 = pendingBgSid;
			int num3 = pendingTileIdx;
			int num4 = pendingTileSid;
			int num5 = pendingGroundIdx;
			int num6 = pendingGroundSid;
			pendingBgIdx = -1;
			pendingBgSid = -1;
			pendingTileIdx = -1;
			pendingTileSid = -1;
			pendingGroundIdx = -1;
			pendingGroundSid = -1;
			pendingTintChange = false;
			pendingTintChangeIsStartup = false;
			if (flag)
			{
				startupTintApplied = true;
			}
			Color a = backgroundTint;
			Color a2 = tileTint;
			Color a3 = groundTint;
			bool flag2 = backgroundForceSolidBlack;
			Color color = tileTint;
			try
			{
				if (num3 >= 0 && num4 >= 0)
				{
					color = ColorFromTrigger(num4);
				}
			}
			catch
			{
			}
			if (num >= 0 && num2 >= 0)
			{
				Color value = (backgroundTint = ColorFromTrigger(num2));
				processedColorTriggers.Add(num);
				backgroundForceSolidBlack = num2 == 143;
				if (enableSimulatorDebugLogging && !triggerLogged.Contains(num))
				{
					WriteTempLog($"Simulator: Applied background trigger at idx={num} sid=0x{num2:X} color={value}");
					triggerLogged.Add(num);
				}
			}
			if (num3 >= 0 && num4 >= 0)
			{
				Color value2 = (tileTint = ColorFromTrigger(num4));
				processedColorTriggers.Add(num3);
				if (enableSimulatorDebugLogging && !triggerLogged.Contains(num3))
				{
					WriteTempLog($"Simulator: Applied tile trigger at idx={num3} sid=0x{num4:X} color={value2}");
					triggerLogged.Add(num3);
				}
			}
			if (num5 >= 0 && num6 >= 0)
			{
				Color value3 = ColorFromTrigger(num6);
				if (num6 == 207)
				{
					groundTint = Color.FromArgb(byte.MaxValue, 0, 0, 0);
				}
				else
				{
					groundTint = value3;
				}
				processedColorTriggers.Add(num5);
				if (enableSimulatorDebugLogging && !triggerLogged.Contains(num5))
				{
					WriteTempLog($"Simulator: Applied ground trigger at idx={num5} sid=0x{num6:X} color={value3}");
					triggerLogged.Add(num5);
				}
			}
			bool flag3 = !AreColorsEqual(a, backgroundTint);
			bool flag4 = !AreColorsEqual(a2, tileTint);
			bool flag5 = !AreColorsEqual(a3, groundTint);
			bool flag6 = flag;
			Color color2 = Color.FromArgb(0, 0, 0, 0);
			if (!flag)
			{
				if (num3 >= 0 || !AreColorsEqual(a2, color))
				{
					color2 = color;
				}
				else if (flag5)
				{
					color2 = tileTint;
				}
			}
			if (!flag6 && cachedTileTonedImages != null && cachedParallaxTonedImages != null && cachedGroundTonedImages != null && AreColorsEqual(cachedBackgroundTint, backgroundTint) && AreColorsEqual(cachedTileTint, tileTint) && AreColorsEqual(cachedGroundTint, groundTint) && cachedBackgroundForceSolidBlack == backgroundForceSolidBlack)
			{
				tileTonedImages = cachedTileTonedImages;
				parallaxTonedImages = cachedParallaxTonedImages;
				groundTonedImages = cachedGroundTonedImages;
			}
			else
			{
				if (!(flag4 || flag3 || flag5 || flag6))
				{
					return;
				}
				Color? color3 = null;
				Color? color4 = null;
				Color? color5 = null;
				Color? color6 = null;
				try
				{
					Color[] palette = PaletteProvider.GetPalette();
					if (num2 >= 0)
					{
						try
						{
							Color value4 = ColorFromTrigger(num2);
							color3 = value4;
							if (num2 >= 128 && num2 <= 140)
							{
								color4 = Color.FromArgb(byte.MaxValue, 0, 0, 0);
							}
							else
							{
								int spriteIdx = num2 - 16;
								Color value5 = ColorFromTrigger(spriteIdx);
								color4 = value5;
							}
						}
						catch
						{
							color3 = null;
							color4 = null;
						}
					}
					if (num6 >= 0)
					{
						int? paletteIndexForBackgroundTrigger = GetPaletteIndexForBackgroundTrigger(num6);
						if (paletteIndexForBackgroundTrigger.HasValue && paletteIndexForBackgroundTrigger.Value >= 0 && paletteIndexForBackgroundTrigger.Value < palette.Length)
						{
							color5 = palette[paletteIndexForBackgroundTrigger.Value];
							int num7 = paletteIndexForBackgroundTrigger.Value % 14;
							if (num7 > 12)
							{
								num7 = 12;
							}
							int num8 = paletteIndexForBackgroundTrigger.Value / 14;
							if (num6 >= 192 && num6 <= 207)
							{
								color6 = Color.FromArgb(byte.MaxValue, 0, 0, 0);
							}
							else
							{
								try
								{
									RgbToHsl(color5.Value.R, color5.Value.G, color5.Value.B, out var h, out var s, out var l);
									double l2 = Math.Max(0.0, l - 0.12);
									RgbFromHsl(h, s, l2, out var r, out var g, out var b);
									color6 = Color.FromArgb(byte.MaxValue, r, g, b);
								}
								catch
								{
									color6 = Color.FromArgb(byte.MaxValue, 0, 0, 0);
								}
							}
						}
					}
				}
				catch
				{
					color3 = null;
					color4 = null;
					color5 = null;
					color6 = null;
				}
				try
				{
					if (tileImages != null)
					{
						if (color3.HasValue)
						{
							tileTonedImages = CreateTwoToneTileImages(tileImages, color3.Value, color4 ?? Color.FromArgb(byte.MaxValue, 0, 0, 0), color2);
						}
						else if (backgroundForceSolidBlack)
						{
							try
							{
								tileTonedImages = CreateBlackMaskedExceptColorArray(tileImages, playerPlaceholderGreen, Color.FromArgb(0, 0, 0, 0), recolorOutline: false);
							}
							catch
							{
								tileTonedImages = CreateTwoToneTileImages(tileImages, backgroundTint, Color.FromArgb(byte.MaxValue, 0, 0, 0), color2);
							}
						}
						else
						{
							tileTonedImages = CreateTwoToneTileImages(tileImages, backgroundTint, Color.FromArgb(byte.MaxValue, 0, 0, 0), color2);
						}
						try
						{
							if (tileTonedImages != null && tileImages != null)
							{
								for (int i = 144; i <= 163; i++)
								{
									if (i < 0 || i >= tileImages.Length)
									{
										continue;
									}
									ImageSource imageSource = null;
									if (color3.HasValue)
									{
										ImageSource[] array = CreateTwoToneTileImages(new ImageSource[1] { tileImages[i] }, color3.Value, color4 ?? Color.FromArgb(byte.MaxValue, 0, 0, 0), color2);
										if (array != null && array.Length != 0)
										{
											imageSource = array[0];
										}
									}
									else if (backgroundForceSolidBlack)
									{
										try
										{
											imageSource = CreateBlackMaskedExceptColor(tileImages[i], playerPlaceholderGreen, color2, recolorOutline: false);
										}
										catch
										{
											imageSource = tileImages[i];
										}
									}
									else
									{
										ImageSource[] array2 = CreateTwoToneTileImages(new ImageSource[1] { tileImages[i] }, backgroundTint, Color.FromArgb(byte.MaxValue, 0, 0, 0), color2);
										if (array2 != null && array2.Length != 0)
										{
											imageSource = array2[0];
										}
									}
									if (imageSource != null)
									{
										tileTonedImages[i] = imageSource;
									}
								}
								int[] array3 = new int[2] { 215, 216 };
								foreach (int num9 in array3)
								{
									if (num9 < 0 || num9 >= tileImages.Length)
									{
										continue;
									}
									ImageSource imageSource2 = null;
									if (color3.HasValue)
									{
										ImageSource[] array4 = CreateTwoToneTileImages(new ImageSource[1] { tileImages[num9] }, color3.Value, color4 ?? Color.FromArgb(byte.MaxValue, 0, 0, 0), color2);
										if (array4 != null && array4.Length != 0)
										{
											imageSource2 = array4[0];
										}
									}
									else if (backgroundForceSolidBlack)
									{
										try
										{
											imageSource2 = CreateBlackMaskedExceptColor(tileImages[num9], playerPlaceholderGreen, color2, recolorOutline: false);
										}
										catch
										{
											imageSource2 = tileImages[num9];
										}
									}
									else
									{
										ImageSource[] array5 = CreateTwoToneTileImages(new ImageSource[1] { tileImages[num9] }, backgroundTint, Color.FromArgb(byte.MaxValue, 0, 0, 0), color2);
										if (array5 != null && array5.Length != 0)
										{
											imageSource2 = array5[0];
										}
									}
									if (imageSource2 != null)
									{
										tileTonedImages[num9] = imageSource2;
									}
								}
								int[] array6 = new int[6] { 1, 2, 5, 6, 136, 137 };
								int[] array7 = array6;
								foreach (int num10 in array7)
								{
									if (num10 < 0 || num10 >= tileImages.Length)
									{
										continue;
									}
									Color outlineTint = ((color2.A > 0) ? color2 : Color.FromArgb(0, 0, 0, 0));
									if (color5.HasValue)
									{
										ImageSource[] array8 = CreateTwoToneTileImages(new ImageSource[1] { tileImages[num10] }, color6 ?? Color.FromArgb(byte.MaxValue, 0, 0, 0), color5.Value, outlineTint);
										if (array8 != null && array8.Length != 0 && array8[0] != null)
										{
											tileTonedImages[num10] = array8[0];
										}
										continue;
									}
									if (backgroundForceSolidBlack || (groundTint.A == byte.MaxValue && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0))
									{
										try
										{
											tileTonedImages[num10] = CreateBlackMaskedExceptColor(tileImages[num10], playerPlaceholderGreen, outlineTint, outlineTint.A > 0);
										}
										catch
										{
											tileTonedImages[num10] = tileImages[num10];
										}
										continue;
									}
									Color bgSecondary = PaletteHelper.RowUpColor(Color.FromArgb(groundTint.A, groundTint.R, groundTint.G, groundTint.B));
									ImageSource[] array9 = CreateTwoToneTileImages(new ImageSource[1] { tileImages[num10] }, groundTint, bgSecondary, outlineTint);
									if (array9 != null && array9.Length != 0 && array9[0] != null)
									{
										tileTonedImages[num10] = array9[0];
									}
								}
								if (47 < tileTonedImages.Length && 143 < tileTonedImages.Length)
								{
									tileTonedImages[143] = tileTonedImages[47];
								}
								if (tileTonedImages.Length != 0 && 252 < tileTonedImages.Length)
								{
									tileTonedImages[252] = tileTonedImages[0];
								}
							}
						}
						catch
						{
						}
					}
				}
				catch
				{
					tileTonedImages = CreateHslShiftedImages(tileImages, tileTint, tileTint);
				}
				try
				{
					if (!AreColorsEqual(a, backgroundTint) || flag2 != backgroundForceSolidBlack || color3.HasValue || flag6)
					{
						if (parallaxImages != null)
						{
							if (color3.HasValue)
							{
								Color bgSecondary2;
								if (color4.HasValue)
								{
									bgSecondary2 = color4.Value;
								}
								else
								{
									RgbToHsl(color3.Value.R, color3.Value.G, color3.Value.B, out var h2, out var s2, out var l3);
									double l4 = Math.Max(0.0, l3 - 0.12);
									RgbFromHsl(h2, s2, l4, out var r2, out var g2, out var b2);
									bgSecondary2 = Color.FromArgb(byte.MaxValue, r2, g2, b2);
								}
								parallaxTonedImages = CreateTwoToneTileImages(parallaxImages, color3.Value, bgSecondary2, color2);
							}
							else if (backgroundForceSolidBlack)
							{
								parallaxTonedImages = null;
							}
							else if (backgroundTint.A == byte.MaxValue && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
							{
								parallaxTonedImages = CreateTwoToneTileImages(parallaxImages, Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(byte.MaxValue, 0, 0, 0), color2);
							}
							else
							{
								parallaxTonedImages = CreateHueShiftedImages(parallaxImages, backgroundTint, color2);
							}
						}
						else
						{
							parallaxTonedImages = parallaxImages;
						}
					}
				}
				catch
				{
					parallaxTonedImages = parallaxImages;
				}
				try
				{
					if (!AreColorsEqual(a3, groundTint) || color5.HasValue || flag6)
					{
						if (color5.HasValue)
						{
							Color bgPrimary;
							if (color6.HasValue)
							{
								bgPrimary = color6.Value;
							}
							else
							{
								RgbToHsl(color5.Value.R, color5.Value.G, color5.Value.B, out var h3, out var s3, out var l5);
								double l6 = Math.Max(0.0, l5 - 0.12);
								RgbFromHsl(h3, s3, l6, out var r3, out var g3, out var b3);
								bgPrimary = Color.FromArgb(byte.MaxValue, r3, g3, b3);
							}
							groundTonedImages = CreateTwoToneTileImages(groundImages, bgPrimary, color5.Value, color2);
						}
						else if (groundTint.A == byte.MaxValue && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
						{
							groundTonedImages = CreateTwoToneTileImages(groundImages, Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(byte.MaxValue, 0, 0, 0), color2);
						}
						else if (groundTint.A == byte.MaxValue)
						{
							RgbToHsl(groundTint.R, groundTint.G, groundTint.B, out var h4, out var s4, out var l7);
							double l8 = Math.Max(0.0, l7 - 0.12);
							RgbFromHsl(h4, s4, l8, out var r4, out var g4, out var b4);
							Color bgPrimary2 = Color.FromArgb(byte.MaxValue, r4, g4, b4);
							groundTonedImages = CreateTwoToneTileImages(groundImages, bgPrimary2, groundTint, color2);
						}
						else
						{
							groundTonedImages = CreateHueShiftedImages(groundImages, groundTint, color2);
						}
					}
				}
				catch
				{
					groundTonedImages = groundImages;
				}
				cachedTileTonedImages = tileTonedImages;
				cachedParallaxTonedImages = parallaxTonedImages;
				cachedGroundTonedImages = groundTonedImages;
				cachedBackgroundTint = backgroundTint;
				cachedTileTint = tileTint;
				cachedGroundTint = groundTint;
				cachedBackgroundForceSolidBlack = backgroundForceSolidBlack;
				try
				{
					if (color2.A > 0 && groundTonedImages != null)
					{
						ImageSource[] array10 = CreateOutlineTintedTileImages(groundTonedImages, color2);
						if (array10 != null)
						{
							groundTonedImages = array10;
						}
					}
				}
				catch
				{
				}
				try
				{
					if (parallaxBitmap != null && (flag3 || flag2 != backgroundForceSolidBlack || color3.HasValue))
					{
						ImageSource[] array11 = null;
						array11 = (color3.HasValue ? CreateTwoToneTileImages(new ImageSource[1] { parallaxBitmap }, color3.Value, color4 ?? Color.FromArgb(byte.MaxValue, 0, 0, 0), color2) : ((backgroundTint.A != byte.MaxValue || backgroundTint.R != 0 || backgroundTint.G != 0 || backgroundTint.B != 0) ? CreateHueShiftedImages(new ImageSource[1] { parallaxBitmap }, backgroundTint) : CreateBlackMaskedImages(new ImageSource[1] { parallaxBitmap })));
						if (array11 != null && array11.Length != 0 && array11[0] != null)
						{
							parallaxBitmapToned = array11[0];
						}
						else
						{
							parallaxBitmapToned = parallaxBitmap;
						}
					}
				}
				catch
				{
					parallaxBitmapToned = parallaxBitmap;
				}
				if (!AreColorsEqual(a, backgroundTint) || flag6)
				{
					try
					{
						if (backgroundTint.A == byte.MaxValue && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
						{
							sawFrame1TilesTinted = CreateSolidBlackImages(sawFrame1TilesOrig);
							sawFrame2TilesTinted = CreateSolidBlackImages(sawFrame2TilesOrig);
							smallSawFrame1TilesTinted = CreateSolidBlackImages(smallSawFrame1TilesOrig);
							smallSawFrame2TilesTinted = CreateSolidBlackImages(smallSawFrame2TilesOrig);
							largeSawFrame1TilesTinted = CreateSolidBlackImages(largeSawFrame1TilesOrig);
							largeSawFrame2TilesTinted = CreateSolidBlackImages(largeSawFrame2TilesOrig);
						}
						else
						{
							sawFrame1TilesTinted = CreateHslShiftedImages(sawFrame1TilesOrig, backgroundTint, color2);
							sawFrame2TilesTinted = CreateHslShiftedImages(sawFrame2TilesOrig, backgroundTint, color2);
							smallSawFrame1TilesTinted = CreateHslShiftedImages(smallSawFrame1TilesOrig, backgroundTint, color2);
							smallSawFrame2TilesTinted = CreateHslShiftedImages(smallSawFrame2TilesOrig, backgroundTint, color2);
							largeSawFrame1TilesTinted = CreateHslShiftedImages(largeSawFrame1TilesOrig, backgroundTint, color2);
							largeSawFrame2TilesTinted = CreateHslShiftedImages(largeSawFrame2TilesOrig, backgroundTint, color2);
						}
					}
					catch
					{
					}
				}
				tileLayerCache = null;
				try
				{
					groundTintedTileCache.Clear();
				}
				catch
				{
				}
				try
				{
					spriteBackgroundCompositeCache.Clear();
					return;
				}
				catch
				{
					return;
				}
			}
		}
		catch
		{
		}
	}

	private bool IsColorTriggerSprite(int spriteIdx)
	{
		if (spriteIdx < 0)
		{
			return false;
		}
		if (spriteIdx < 128 || spriteIdx > 140)
		{
			switch (spriteIdx)
			{
			default:
				switch (spriteIdx)
				{
				default:
					if ((spriteIdx >= 174 && spriteIdx <= 175) || (spriteIdx >= 176 && spriteIdx <= 191) || (spriteIdx >= 192 && spriteIdx <= 204))
					{
						break;
					}
					switch (spriteIdx)
					{
					default:
						if (spriteIdx < 224 || spriteIdx > 236)
						{
							return false;
						}
						break;
					case 207:
					case 208:
					case 209:
					case 210:
					case 211:
					case 212:
					case 213:
					case 214:
					case 215:
					case 216:
					case 217:
					case 218:
					case 219:
					case 220:
						break;
					}
					break;
				case 159:
				case 160:
				case 161:
				case 162:
				case 163:
				case 164:
				case 165:
				case 166:
				case 167:
				case 168:
				case 169:
				case 170:
				case 171:
				case 172:
					break;
				}
				break;
			case 143:
			case 144:
			case 145:
			case 146:
			case 147:
			case 148:
			case 149:
			case 150:
			case 151:
			case 152:
			case 153:
			case 154:
			case 155:
			case 156:
				break;
			}
		}
		int num = spriteIdx & 0xF;
		int num2 = spriteIdx & 0xF0;
		if (num >= 13 && (num2 == 128 || num2 == 144 || num2 == 160 || num2 == 192 || num2 == 208 || num2 == 224) && spriteIdx != 143 && spriteIdx != 207)
		{
			return false;
		}
		return true;
	}

	private bool IsBackgroundTrigger(int spriteIdx)
	{
		return ((spriteIdx >= 128 && spriteIdx <= 172) || spriteIdx == 143) && IsColorTriggerSprite(spriteIdx);
	}

	private bool IsTileTrigger(int spriteIdx)
	{
		return spriteIdx >= 176 && spriteIdx <= 191 && IsColorTriggerSprite(spriteIdx);
	}

	private bool IsGravityModTrigger(int spriteIdx)
	{
		return spriteIdx >= 112 && spriteIdx <= 116;
	}

	private bool IsGroundTrigger(int spriteIdx)
	{
		return ((spriteIdx >= 192 && spriteIdx <= 236) || spriteIdx == 207) && IsColorTriggerSprite(spriteIdx);
	}

	private Color ColorFromTrigger(int spriteIdx)
	{
		if (spriteIdx == 143 || spriteIdx == 191)
		{
			return Color.FromArgb(byte.MaxValue, 0, 0, 0);
		}
		try
		{
			Color[] palette = PaletteProvider.GetPalette();
			try
			{
				int? paletteIndexForBackgroundTrigger = GetPaletteIndexForBackgroundTrigger(spriteIdx);
				if (paletteIndexForBackgroundTrigger.HasValue && paletteIndexForBackgroundTrigger.Value >= 0 && paletteIndexForBackgroundTrigger.Value < palette.Length)
				{
					Color color = palette[paletteIndexForBackgroundTrigger.Value];
					return Color.FromArgb(byte.MaxValue, color.R, color.G, color.B);
				}
			}
			catch
			{
			}
		}
		catch
		{
		}
		try
		{
			Color? color2 = SampleRepresentativeColorFromSprite(spriteIdx);
			if (color2.HasValue)
			{
				return color2.Value;
			}
		}
		catch
		{
		}
		int num = spriteIdx & 0xF;
		double h = (double)num / 16.0 * 360.0;
		return HslToColor(h, 0.7, 0.45);
	}

	private Color? SampleRepresentativeColorFromSprite(int spriteIdx)
	{
		ImageSource imageSource = null;
		if (previewSpriteMap != null && previewSpriteMap.TryGetValue(spriteIdx, out ImageSource value) && value != null)
		{
			imageSource = value;
		}
		else if (spriteImages != null && spriteIdx >= 0 && spriteIdx < spriteImages.Length)
		{
			imageSource = spriteImages[spriteIdx];
		}
		if (imageSource is BitmapSource source)
		{
			try
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0.0);
				int num = Math.Max(1, formatConvertedBitmap.PixelWidth);
				int num2 = Math.Max(1, formatConvertedBitmap.PixelHeight);
				int num3 = num * 4;
				byte[] array = new byte[num2 * num3];
				formatConvertedBitmap.CopyPixels(array, num3, 0);
				for (int i = 0; i < array.Length; i += 4)
				{
					byte b = array[i];
					byte b2 = array[i + 1];
					byte b3 = array[i + 2];
					byte b4 = array[i + 3];
					if (b4 > 32 && (b3 != 0 || b2 != 0 || b != 0))
					{
						return Color.FromArgb(byte.MaxValue, b3, b2, b);
					}
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private bool IsImageTopHeavy(BitmapSource bs)
	{
		try
		{
			if (bs == null)
			{
				return false;
			}
			int hashCode = RuntimeHelpers.GetHashCode(bs);
			if (imageTopHeavyCache.TryGetValue(hashCode, out var value))
			{
				return value;
			}
			FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0.0);
			int num = Math.Max(1, formatConvertedBitmap.PixelWidth);
			int num2 = Math.Max(1, formatConvertedBitmap.PixelHeight);
			int num3 = num * 4;
			byte[] array = new byte[num2 * num3];
			formatConvertedBitmap.CopyPixels(array, num3, 0);
			long num4 = 0L;
			int num5 = 0;
			int num6 = 4000;
			for (int i = 0; i < num2; i++)
			{
				if (num5 >= num6)
				{
					break;
				}
				int num7 = i * num3;
				for (int j = 0; j < num; j++)
				{
					if (num5 >= num6)
					{
						break;
					}
					int num8 = num7 + j * 4;
					byte b = array[num8 + 3];
					if (b > 32)
					{
						num4 += i;
						num5++;
					}
				}
			}
			bool flag = false;
			if (num5 > 0)
			{
				double num9 = (double)num4 / (double)num5;
				flag = num9 < (double)num2 * 0.45;
			}
			imageTopHeavyCache[hashCode] = flag;
			return flag;
		}
		catch
		{
			return false;
		}
	}

	private Color HslToColor(double h, double s, double l)
	{
		double num = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
		double num2 = h / 60.0;
		double num3 = num * (1.0 - Math.Abs(num2 % 2.0 - 1.0));
		double num4 = 0.0;
		double num5 = 0.0;
		double num6 = 0.0;
		if (0.0 <= num2 && num2 < 1.0)
		{
			num4 = num;
			num5 = num3;
			num6 = 0.0;
		}
		else if (1.0 <= num2 && num2 < 2.0)
		{
			num4 = num3;
			num5 = num;
			num6 = 0.0;
		}
		else if (2.0 <= num2 && num2 < 3.0)
		{
			num4 = 0.0;
			num5 = num;
			num6 = num3;
		}
		else if (3.0 <= num2 && num2 < 4.0)
		{
			num4 = 0.0;
			num5 = num3;
			num6 = num;
		}
		else if (4.0 <= num2 && num2 < 5.0)
		{
			num4 = num3;
			num5 = 0.0;
			num6 = num;
		}
		else
		{
			num4 = num;
			num5 = 0.0;
			num6 = num3;
		}
		double num7 = l - num / 2.0;
		byte r = (byte)Math.Round((num4 + num7) * 255.0);
		byte g = (byte)Math.Round((num5 + num7) * 255.0);
		byte b = (byte)Math.Round((num6 + num7) * 255.0);
		return Color.FromArgb(byte.MaxValue, r, g, b);
	}

	private int MapStartingCodeToTrigger(int code, bool isGround)
	{
		try
		{
			if (code == 15)
			{
				return isGround ? 207 : 143;
			}
			int num = code & 0xF;
			switch (code & 0xF0)
			{
			case 0:
				return (isGround ? 192 : 128) + num;
			case 16:
				return (isGround ? 208 : 144) + num;
			case 32:
				return (isGround ? 224 : 160) + num;
			}
		}
		catch
		{
		}
		return isGround ? 192 : 128;
	}

	private int? GetPaletteIndexForBackgroundTrigger(int spriteIdx)
	{
		try
		{
			int num = -1;
			if (spriteIdx >= 128 && spriteIdx <= 140)
			{
				num = spriteIdx - 128;
			}
			else if (spriteIdx >= 144 && spriteIdx <= 156)
			{
				num = spriteIdx - 144 + 14;
			}
			else if (spriteIdx >= 160 && spriteIdx <= 172)
			{
				num = spriteIdx - 160 + 28;
			}
			else if (spriteIdx >= 192 && spriteIdx <= 204)
			{
				num = spriteIdx - 192;
			}
			else if (spriteIdx >= 208 && spriteIdx <= 220)
			{
				num = spriteIdx - 208 + 14;
			}
			else if (spriteIdx >= 224 && spriteIdx <= 236)
			{
				num = spriteIdx - 224 + 28;
			}
			if (num >= 0)
			{
				return num;
			}
		}
		catch
		{
		}
		return null;
	}

	private ImageSource?[]? CreateTwoToneTileImages(ImageSource?[]? originals, Color bgPrimary, Color bgSecondary, Color outlineTint)
	{
		if (originals == null)
		{
			return null;
		}
		List<ImageSource> list = new List<ImageSource>(originals.Length);
		foreach (ImageSource imageSource in originals)
		{
			if (imageSource == null)
			{
				WriteableBitmap writeableBitmap = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null);
				byte[] pixels = new byte[4];
				try
				{
					writeableBitmap.WritePixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
				}
				catch
				{
				}
				try
				{
					writeableBitmap.Freeze();
				}
				catch
				{
				}
				list.Add(writeableBitmap);
			}
			else if (imageSource is BitmapSource source)
			{
				try
				{
					FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0.0);
					int pixelWidth = formatConvertedBitmap.PixelWidth;
					int pixelHeight = formatConvertedBitmap.PixelHeight;
					int num = pixelWidth * 4;
					byte[] array = new byte[pixelHeight * num];
					formatConvertedBitmap.CopyPixels(array, num, 0);
					double num2 = 1.0;
					double num3 = 0.0;
					int num4 = 0;
					for (int j = 0; j < array.Length; j += 4)
					{
						byte b = array[j];
						byte b2 = array[j + 1];
						byte b3 = array[j + 2];
						if (array[j + 3] == 0)
						{
							continue;
						}
						double num5 = (0.2126 * (double)(int)b3 + 0.7152 * (double)(int)b2 + 0.0722 * (double)(int)b) / 255.0;
						if (!(num5 >= 0.82))
						{
							if (num5 < num2)
							{
								num2 = num5;
							}
							if (num5 > num3)
							{
								num3 = num5;
							}
							num4++;
						}
					}
					double num6 = ((num4 > 0) ? ((num2 + num3) / 2.0) : 0.5);
					double num7 = 0.82;
					if (outlineTint.A == byte.MaxValue && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0)
					{
						num7 = 0.7;
					}
					for (int k = 0; k < array.Length; k += 4)
					{
						byte b4 = array[k];
						byte b5 = array[k + 1];
						byte b6 = array[k + 2];
						if (array[k + 3] == 0)
						{
							continue;
						}
						double num8 = (0.2126 * (double)(int)b6 + 0.7152 * (double)(int)b5 + 0.0722 * (double)(int)b4) / 255.0;
						bool flag = num8 >= num7;
						if (b6 <= 12 && b5 <= 12 && b4 <= 12)
						{
							continue;
						}
						if (flag)
						{
							if (outlineTint.A > 0)
							{
								array[k + 3] = byte.MaxValue;
								array[k + 2] = outlineTint.R;
								array[k + 1] = outlineTint.G;
								array[k] = outlineTint.B;
							}
						}
						else
						{
							Color color = ((num8 >= num6) ? bgPrimary : bgSecondary);
							array[k + 3] = byte.MaxValue;
							array[k + 2] = color.R;
							array[k + 1] = color.G;
							array[k] = color.B;
						}
					}
					WriteableBitmap writeableBitmap2 = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Pbgra32, null);
					writeableBitmap2.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
					list.Add(writeableBitmap2);
				}
				catch
				{
					if (imageSource != null)
					{
						list.Add(imageSource);
					}
					else
					{
						list.Add(new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null));
					}
				}
			}
			else if (imageSource != null)
			{
				list.Add(imageSource);
			}
			else
			{
				list.Add(new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null));
			}
		}
		return list.ToArray();
	}

	private ImageSource?[]? CreateOutlineTintedTileImages(ImageSource?[]? originals, Color outlineTint)
	{
		if (originals == null)
		{
			return null;
		}
		List<ImageSource> list = new List<ImageSource>(originals.Length);
		foreach (ImageSource imageSource in originals)
		{
			if (imageSource == null)
			{
				WriteableBitmap writeableBitmap = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null);
				byte[] pixels = new byte[4];
				try
				{
					writeableBitmap.WritePixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
				}
				catch
				{
				}
				try
				{
					writeableBitmap.Freeze();
				}
				catch
				{
				}
				list.Add(writeableBitmap);
			}
			else if (imageSource is BitmapSource source)
			{
				try
				{
					FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0.0);
					int pixelWidth = formatConvertedBitmap.PixelWidth;
					int pixelHeight = formatConvertedBitmap.PixelHeight;
					int num = pixelWidth * 4;
					byte[] array = new byte[pixelHeight * num];
					formatConvertedBitmap.CopyPixels(array, num, 0);
					double num2 = 0.82;
					if (outlineTint.A == byte.MaxValue && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0)
					{
						num2 = 0.7;
					}
					for (int j = 0; j < array.Length; j += 4)
					{
						byte b = array[j];
						byte b2 = array[j + 1];
						byte b3 = array[j + 2];
						if (array[j + 3] != 0)
						{
							double num3 = (0.2126 * (double)(int)b3 + 0.7152 * (double)(int)b2 + 0.0722 * (double)(int)b) / 255.0;
							if (num3 >= num2)
							{
								array[j + 3] = byte.MaxValue;
								array[j + 2] = outlineTint.R;
								array[j + 1] = outlineTint.G;
								array[j] = outlineTint.B;
							}
						}
					}
					WriteableBitmap writeableBitmap2 = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Pbgra32, null);
					writeableBitmap2.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
					list.Add(writeableBitmap2);
				}
				catch
				{
					list.Add(imageSource);
				}
			}
			else
			{
				list.Add(imageSource);
			}
		}
		return list.ToArray();
	}

	private static bool AreColorsEqual(Color a, Color b)
	{
		return a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
	}

	private ImageSource? GetPlayerTintedSprite(ImageSource src, int spriteId)
	{
		if (!playerTintEnabled)
		{
			return src;
		}
		try
		{
			int num = ((src != null) ? RuntimeHelpers.GetHashCode(src) : 0);
			long key = (long)((ulong)((long)spriteId << 48) | (((ulong)num & 0xFFFFuL) << 32) | ((ulong)playerTint.A << 24) | ((ulong)playerTint.R << 16) | ((ulong)playerTint.G << 8) | playerTint.B);
			if (tintedSpriteCache.TryGetValue(key, out ImageSource value))
			{
				return value;
			}
			if (src is BitmapSource source)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0.0);
				int num2 = Math.Max(1, formatConvertedBitmap.PixelWidth);
				int num3 = Math.Max(1, formatConvertedBitmap.PixelHeight);
				int num4 = num2 * 4;
				byte[] array = new byte[num3 * num4];
				formatConvertedBitmap.CopyPixels(array, num4, 0);
				RgbToHsl(playerTint.R, playerTint.G, playerTint.B, out var h, out var _, out var _);
				for (int i = 0; i < array.Length; i += 4)
				{
					byte b = array[i];
					byte g = array[i + 1];
					byte r = array[i + 2];
					if (array[i + 3] != 0)
					{
						RgbToHsl(r, g, b, out var _, out var s2, out var l2);
						double h3 = h;
						double s3 = s2;
						double l3 = l2;
						RgbFromHsl(h3, s3, l3, out var r2, out var g2, out var b2);
						array[i] = b2;
						array[i + 1] = g2;
						array[i + 2] = r2;
					}
				}
				WriteableBitmap writeableBitmap = new WriteableBitmap(num2, num3, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Pbgra32, null);
				writeableBitmap.WritePixels(new Int32Rect(0, 0, num2, num3), array, num4, 0);
				tintedSpriteCache[key] = writeableBitmap;
				return writeableBitmap;
			}
			tintedSpriteCache[key] = src;
			return src;
		}
		catch
		{
			return src;
		}
	}

	private ImageSource? CompositeSpriteOverBackgroundAt(ImageSource src, Color bg, int destX, int destY)
	{
		if (src == null)
		{
			return null;
		}
		if (!(src is BitmapSource bitmapSource))
		{
			return src;
		}
		try
		{
			int hashCode = RuntimeHelpers.GetHashCode(bitmapSource);
			int num = 0;
			int num2 = 0;
			try
			{
				if (tileLayerImage != null)
				{
					double left = Canvas.GetLeft(tileLayerImage);
					if (!double.IsNaN(left))
					{
						num = (int)Math.Round(left);
					}
					double top = Canvas.GetTop(tileLayerImage);
					if (!double.IsNaN(top))
					{
						num2 = (int)Math.Round(top);
					}
				}
			}
			catch
			{
			}
			string key = $"{hashCode}:{bg.A:X2}{bg.R:X2}{bg.G:X2}{bg.B:X2}:{destX}:{destY}:{animationFrame}:{num}:{num2}";
			if (spriteBackgroundCompositeCache.TryGetValue(key, out ImageSource value))
			{
				return value;
			}
			DrawingVisual drawingVisual = new DrawingVisual();
			using (DrawingContext drawingContext = drawingVisual.RenderOpen())
			{
				BitmapSource bitmapSource2 = tileLayerCache;
				if (bitmapSource2 != null)
				{
					try
					{
						int pixelWidth = bitmapSource2.PixelWidth;
						int pixelHeight = bitmapSource2.PixelHeight;
						int num3 = destX - num;
						int num4 = destY - num2;
						int num5 = Math.Max(0, num3);
						int num6 = Math.Max(0, num4);
						int num7 = Math.Min(pixelWidth, num3 + Math.Max(1, bitmapSource.PixelWidth));
						int num8 = Math.Min(pixelHeight, num4 + Math.Max(1, bitmapSource.PixelHeight));
						int num9 = Math.Max(0, num7 - num5);
						int num10 = Math.Max(0, num8 - num6);
						if (num9 > 0 && num10 > 0)
						{
							try
							{
								CroppedBitmap croppedBitmap = new CroppedBitmap(bitmapSource2, new Int32Rect(num5, num6, num9, num10));
								BitmapSource image = App.EnsureUnfrozenForRender(croppedBitmap) ?? croppedBitmap;
								ImageBrush brush = new ImageBrush(image)
								{
									Stretch = Stretch.None,
									TileMode = TileMode.None
								};
								bool flag = false;
								try
								{
									if (tileLayerImage != null && tileLayerImage.Source is BitmapSource bitmapSource3)
									{
										double offsetX = num - destX;
										double offsetY = num2 - destY;
										drawingContext.PushTransform(new TranslateTransform(offsetX, offsetY));
										BitmapSource imageSource = App.EnsureUnfrozenForRender(bitmapSource3) ?? bitmapSource3;
										drawingContext.DrawImage(imageSource, new Rect(0.0, 0.0, bitmapSource3.PixelWidth, bitmapSource3.PixelHeight));
										drawingContext.Pop();
										flag = true;
									}
								}
								catch
								{
									flag = false;
								}
								if (!flag)
								{
									Brush brush2 = null;
									try
									{
										if (bgRectPersistent != null && bgRectPersistent.Fill != null)
										{
											brush2 = bgRectPersistent.Fill;
										}
									}
									catch
									{
										brush2 = null;
									}
									if (brush2 == null)
									{
										brush2 = new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
										drawingContext.DrawRectangle(brush2 ?? new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0.0, 0.0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
									}
									else
									{
										try
										{
											if (brush2 is ImageBrush { ImageSource: var imageSource2 } imageBrush)
											{
												Color tint = bg;
												try
												{
													Color[] palette = PaletteProvider.GetPalette();
													int? nearestPaletteIndexForColor = GetNearestPaletteIndexForColor(bg, palette);
													if (nearestPaletteIndexForColor.HasValue)
													{
														int num11 = nearestPaletteIndexForColor.Value % 14;
														int num12 = nearestPaletteIndexForColor.Value / 14;
														int num13 = num12 * 14 + ((num11 > 12) ? 12 : num11);
														if (num13 >= 0 && num13 < palette.Length)
														{
															tint = Color.FromArgb(bg.A, palette[num13].R, palette[num13].G, palette[num13].B);
														}
													}
													else
													{
														RgbToHsl(bg.R, bg.G, bg.B, out var h, out var s, out var l);
														l = Math.Max(0.0, l - 0.12);
														RgbFromHsl(h, s, l, out var r, out var g, out var b);
														tint = Color.FromArgb(bg.A, r, g, b);
													}
												}
												catch
												{
												}
												ImageSource imageSource3 = imageSource2 ?? new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null);
												try
												{
													if (imageSource2 != null)
													{
														ImageSource[] array = CreateHslShiftedImages(new ImageSource[1] { imageSource2 }, tint);
														if (array != null && array.Length != 0 && array[0] != null)
														{
															imageSource3 = array[0];
														}
													}
												}
												catch
												{
												}
												ImageSource image2 = App.EnsureUnfrozenForRender(imageSource3) ?? imageSource3;
												ImageBrush imageBrush2 = new ImageBrush(image2)
												{
													Stretch = imageBrush.Stretch,
													TileMode = imageBrush.TileMode,
													Viewport = imageBrush.Viewport,
													ViewportUnits = imageBrush.ViewportUnits
												};
												double num14 = 0.0;
												double num15 = 0.0;
												try
												{
													if (imageBrush.Transform is TranslateTransform translateTransform)
													{
														num14 = translateTransform.X;
														num15 = translateTransform.Y;
													}
													else if (imageBrush.Transform is MatrixTransform { Matrix: var matrix } matrixTransform)
													{
														num14 = matrix.OffsetX;
														num15 = matrixTransform.Matrix.OffsetY;
													}
												}
												catch
												{
												}
												imageBrush2.Transform = new TranslateTransform(num14 - (double)destX, num15 - (double)destY);
												drawingContext.DrawRectangle(imageBrush2, null, new Rect(0.0, 0.0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
											}
											else
											{
												drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0.0, 0.0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
											}
										}
										catch
										{
											drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0.0, 0.0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
										}
									}
								}
								int num16 = num5 - num3;
								int num17 = num6 - num4;
								drawingContext.PushTransform(new TranslateTransform(-num16, -num17));
								drawingContext.DrawRectangle(brush, null, new Rect(num16, num17, num9, num10));
								drawingContext.Pop();
							}
							catch
							{
								Brush brush3 = null;
								try
								{
									if (bgRectPersistent != null && bgRectPersistent.Fill != null)
									{
										brush3 = bgRectPersistent.Fill;
									}
								}
								catch
								{
									brush3 = null;
								}
								if (brush3 == null)
								{
									brush3 = new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
								}
								else
								{
									try
									{
										if (brush3 is ImageBrush { ImageSource: var imageSource4 } imageBrush3)
										{
											Color tint2 = bg;
											try
											{
												Color[] palette2 = PaletteProvider.GetPalette();
												int? nearestPaletteIndexForColor2 = GetNearestPaletteIndexForColor(bg, palette2);
												if (nearestPaletteIndexForColor2.HasValue)
												{
													int num18 = nearestPaletteIndexForColor2.Value % 14;
													int num19 = nearestPaletteIndexForColor2.Value / 14;
													int num20 = num19 * 14 + ((num18 > 12) ? 12 : num18);
													if (num20 >= 0 && num20 < palette2.Length)
													{
														tint2 = Color.FromArgb(bg.A, palette2[num20].R, palette2[num20].G, palette2[num20].B);
													}
												}
												else
												{
													RgbToHsl(bg.R, bg.G, bg.B, out var h2, out var s2, out var l2);
													l2 = Math.Max(0.0, l2 - 0.12);
													RgbFromHsl(h2, s2, l2, out var r2, out var g2, out var b2);
													tint2 = Color.FromArgb(bg.A, r2, g2, b2);
												}
											}
											catch
											{
											}
											ImageSource imageSource5 = imageSource4 ?? new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null);
											try
											{
												if (imageSource4 != null)
												{
													ImageSource[] array2 = CreateHslShiftedImages(new ImageSource[1] { imageSource4 }, tint2);
													if (array2 != null && array2.Length != 0 && array2[0] != null)
													{
														imageSource5 = array2[0];
													}
												}
											}
											catch
											{
											}
											ImageSource image3 = App.EnsureUnfrozenForRender(imageSource5) ?? imageSource5;
											ImageBrush imageBrush4 = new ImageBrush(image3)
											{
												Stretch = imageBrush3.Stretch,
												TileMode = imageBrush3.TileMode,
												Viewport = imageBrush3.Viewport,
												ViewportUnits = imageBrush3.ViewportUnits
											};
											double num21 = 0.0;
											double num22 = 0.0;
											try
											{
												if (imageBrush3.Transform is TranslateTransform translateTransform2)
												{
													num21 = translateTransform2.X;
													num22 = translateTransform2.Y;
												}
												else if (imageBrush3.Transform is MatrixTransform { Matrix: var matrix2 } matrixTransform2)
												{
													num21 = matrix2.OffsetX;
													num22 = matrixTransform2.Matrix.OffsetY;
												}
											}
											catch
											{
											}
											imageBrush4.Transform = new TranslateTransform(num21 - (double)destX, num22 - (double)destY);
											brush3 = imageBrush4;
										}
									}
									catch
									{
									}
								}
								drawingContext.DrawRectangle(brush3 ?? new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0.0, 0.0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
							}
						}
						else
						{
							try
							{
								if (tileLayerImage != null && tileLayerImage.Source is BitmapSource bitmapSource4)
								{
									double offsetX2 = num - destX;
									double offsetY2 = num2 - destY;
									drawingContext.PushTransform(new TranslateTransform(offsetX2, offsetY2));
									BitmapSource imageSource6 = App.EnsureUnfrozenForRender(bitmapSource4) ?? bitmapSource4;
									drawingContext.DrawImage(imageSource6, new Rect(0.0, 0.0, bitmapSource4.PixelWidth, bitmapSource4.PixelHeight));
									drawingContext.Pop();
								}
								else
								{
									drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0.0, 0.0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
								}
							}
							catch
							{
								Brush brush4 = null;
								try
								{
									if (bgRectPersistent != null && bgRectPersistent.Fill != null)
									{
										brush4 = bgRectPersistent.Fill;
									}
								}
								catch
								{
									brush4 = null;
								}
								if (brush4 == null)
								{
									brush4 = new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
								}
								else
								{
									try
									{
										if (brush4 is ImageBrush { ImageSource: var imageSource7 } imageBrush5)
										{
											Color tint3 = bg;
											try
											{
												RgbToHsl(bg.R, bg.G, bg.B, out var h3, out var s3, out var l3);
												l3 = Math.Max(0.0, l3 - 0.12);
												RgbFromHsl(h3, s3, l3, out var r3, out var g3, out var b3);
												tint3 = Color.FromArgb(bg.A, r3, g3, b3);
											}
											catch
											{
											}
											ImageSource imageSource8 = imageSource7 ?? new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null);
											try
											{
												if (imageSource7 != null)
												{
													ImageSource[] array3 = CreateHslShiftedImages(new ImageSource[1] { imageSource7 }, tint3);
													if (array3 != null && array3.Length != 0 && array3[0] != null)
													{
														imageSource8 = array3[0];
													}
												}
											}
											catch
											{
											}
											ImageSource image4 = App.EnsureUnfrozenForRender(imageSource8) ?? imageSource8;
											ImageBrush imageBrush6 = new ImageBrush(image4)
											{
												Stretch = imageBrush5.Stretch,
												TileMode = imageBrush5.TileMode,
												Viewport = imageBrush5.Viewport,
												ViewportUnits = imageBrush5.ViewportUnits
											};
											double num23 = 0.0;
											double num24 = 0.0;
											try
											{
												if (imageBrush5.Transform is TranslateTransform translateTransform3)
												{
													num23 = translateTransform3.X;
													num24 = translateTransform3.Y;
												}
												else if (imageBrush5.Transform is MatrixTransform { Matrix: var matrix3 } matrixTransform3)
												{
													num23 = matrix3.OffsetX;
													num24 = matrixTransform3.Matrix.OffsetY;
												}
											}
											catch
											{
											}
											imageBrush6.Transform = new TranslateTransform(num23 - (double)destX, num24 - (double)destY);
											brush4 = imageBrush6;
										}
									}
									catch
									{
									}
								}
								drawingContext.DrawRectangle(brush4 ?? new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0.0, 0.0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
							}
						}
					}
					catch
					{
						drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0.0, 0.0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
					}
				}
				else
				{
					drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0.0, 0.0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
				}
				BitmapSource imageSource9 = App.EnsureUnfrozenForRender(bitmapSource) ?? bitmapSource;
				drawingContext.DrawImage(imageSource9, new Rect(0.0, 0.0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
			}
			RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(bitmapSource.PixelWidth, bitmapSource.PixelHeight, 96.0, 96.0, PixelFormats.Pbgra32);
			renderTargetBitmap.Render(drawingVisual);
			renderTargetBitmap.Freeze();
			spriteBackgroundCompositeCache[key] = renderTargetBitmap;
			return renderTargetBitmap;
		}
		catch
		{
			return src;
		}
	}

	private void UpdateTonedImagesForTileTint(Color newTileTint, Color? outlineOverride = null)
	{
		try
		{
			Color outlineTint = (outlineOverride.HasValue ? outlineOverride.Value : tileTint);
			if (tileImages != null)
			{
				if (backgroundForceSolidBlack)
				{
					try
					{
						tileTonedImages = CreateBlackMaskedExceptColorArray(tileImages, playerPlaceholderGreen, outlineTint, recolorOutline: false);
					}
					catch
					{
						tileTonedImages = CreateHslShiftedImages(tileImages, newTileTint, outlineTint);
					}
				}
				else
				{
					tileTonedImages = CreateHslShiftedImages(tileImages, newTileTint, outlineTint);
				}
			}
			try
			{
				if (tileTonedImages != null && tileImages != null)
				{
					int[] array = new int[12]
					{
						12, 13, 14, 15, 19, 20, 128, 129, 132, 133,
						134, 135
					};
					int[] array2 = array;
					foreach (int num in array2)
					{
						if (num >= 0 && num < tileTonedImages.Length && num >= 0 && num < tileImages.Length && tileTonedImages[num] != null && tileImages[num] != null)
						{
							try
							{
								tileTonedImages[num] = ApplyPlayerTintToGreenPixels(tileTonedImages[num], tileImages[num], playerTint);
							}
							catch
							{
							}
						}
					}
				}
			}
			catch
			{
			}
			if (sawFrame1TilesTinted != null && sawFrame1TilesTinted.Length == 0)
			{
			}
		}
		catch
		{
		}
	}

	private ImageSource? CreateBlackMaskedImage(ImageSource? src, Color outlineTint = default(Color), bool recolorOutline = true)
	{
		if (src == null)
		{
			return null;
		}
		if (!(src is BitmapSource source))
		{
			return src;
		}
		try
		{
			FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0.0);
			int num = Math.Max(1, formatConvertedBitmap.PixelWidth);
			int num2 = Math.Max(1, formatConvertedBitmap.PixelHeight);
			int num3 = num * 4;
			byte[] array = new byte[num2 * num3];
			formatConvertedBitmap.CopyPixels(array, num3, 0);
			for (int i = 0; i < array.Length; i += 4)
			{
				byte b = array[i];
				byte b2 = array[i + 1];
				byte b3 = array[i + 2];
				if (array[i + 3] == 0)
				{
					continue;
				}
				double num4 = (0.2126 * (double)(int)b3 + 0.7152 * (double)(int)b2 + 0.0722 * (double)(int)b) / 255.0;
				if (num4 >= 0.82)
				{
					if (recolorOutline && outlineTint.A > 0)
					{
						array[i + 3] = byte.MaxValue;
						array[i + 2] = outlineTint.R;
						array[i + 1] = outlineTint.G;
						array[i] = outlineTint.B;
					}
				}
				else
				{
					array[i] = 0;
					array[i + 1] = 0;
					array[i + 2] = 0;
					array[i + 3] = byte.MaxValue;
				}
			}
			WriteableBitmap writeableBitmap = new WriteableBitmap(num, num2, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Pbgra32, null);
			writeableBitmap.WritePixels(new Int32Rect(0, 0, num, num2), array, num3, 0);
			return writeableBitmap;
		}
		catch
		{
			return src;
		}
	}

	private ImageSource? CreateBlackMaskedExceptColor(ImageSource? src, Color excludeColor, Color outlineTint = default(Color), bool recolorOutline = true)
	{
		if (src == null)
		{
			return null;
		}
		if (!(src is BitmapSource source))
		{
			return src;
		}
		try
		{
			FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0.0);
			int num = Math.Max(1, formatConvertedBitmap.PixelWidth);
			int num2 = Math.Max(1, formatConvertedBitmap.PixelHeight);
			int num3 = num * 4;
			byte[] array = new byte[num2 * num3];
			formatConvertedBitmap.CopyPixels(array, num3, 0);
			for (int i = 0; i < array.Length; i += 4)
			{
				byte b = array[i];
				byte b2 = array[i + 1];
				byte b3 = array[i + 2];
				if (array[i + 3] == 0 || (b3 == excludeColor.R && b2 == excludeColor.G && b == excludeColor.B))
				{
					continue;
				}
				double num4 = (0.2126 * (double)(int)b3 + 0.7152 * (double)(int)b2 + 0.0722 * (double)(int)b) / 255.0;
				if (num4 >= 0.82)
				{
					if (recolorOutline && outlineTint.A > 0)
					{
						array[i + 3] = byte.MaxValue;
						array[i + 2] = outlineTint.R;
						array[i + 1] = outlineTint.G;
						array[i] = outlineTint.B;
					}
				}
				else
				{
					array[i] = 0;
					array[i + 1] = 0;
					array[i + 2] = 0;
					array[i + 3] = byte.MaxValue;
				}
			}
			WriteableBitmap writeableBitmap = new WriteableBitmap(num, num2, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Pbgra32, null);
			writeableBitmap.WritePixels(new Int32Rect(0, 0, num, num2), array, num3, 0);
			return writeableBitmap;
		}
		catch
		{
			return src;
		}
	}

	private ImageSource? ApplyPlayerTintToGreenPixels(ImageSource? src, ImageSource? original, Color playerTint)
	{
		if (src == null || original == null)
		{
			return src;
		}
		if (!(src is BitmapSource source) || !(original is BitmapSource source2))
		{
			return src;
		}
		try
		{
			FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0.0);
			FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(source2, PixelFormats.Pbgra32, null, 0.0);
			int num = Math.Max(1, formatConvertedBitmap.PixelWidth);
			int num2 = Math.Max(1, formatConvertedBitmap.PixelHeight);
			if (formatConvertedBitmap2.PixelWidth != num || formatConvertedBitmap2.PixelHeight != num2)
			{
				return src;
			}
			int num3 = num * 4;
			byte[] array = new byte[num2 * num3];
			byte[] array2 = new byte[num2 * num3];
			formatConvertedBitmap.CopyPixels(array, num3, 0);
			formatConvertedBitmap2.CopyPixels(array2, num3, 0);
			byte b = 90;
			byte b2 = 206;
			byte b3 = 82;
			for (int i = 0; i < array.Length; i += 4)
			{
				byte b4 = array2[i];
				byte b5 = array2[i + 1];
				byte b6 = array2[i + 2];
				if (array2[i + 3] != 0 && b6 == b && b5 == b2 && b4 == b3)
				{
					array[i + 3] = playerTint.A;
					array[i + 2] = playerTint.R;
					array[i + 1] = playerTint.G;
					array[i] = playerTint.B;
				}
			}
			WriteableBitmap writeableBitmap = new WriteableBitmap(num, num2, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Pbgra32, null);
			writeableBitmap.WritePixels(new Int32Rect(0, 0, num, num2), array, num3, 0);
			return writeableBitmap;
		}
		catch
		{
			return src;
		}
	}

	private ImageSource?[]? CreateHslShiftedImages(ImageSource?[]? originals, Color tint, Color outlineTint = default(Color))
	{
		if (originals == null)
		{
			return null;
		}
		if (tint.A == 0)
		{
			return originals;
		}
		List<ImageSource> list = new List<ImageSource>(originals.Length);
		if (tint.A == byte.MaxValue)
		{
			foreach (ImageSource imageSource in originals)
			{
				if (imageSource == null)
				{
					WriteableBitmap writeableBitmap = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null);
					byte[] pixels = new byte[4];
					try
					{
						writeableBitmap.WritePixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
					}
					catch
					{
					}
					try
					{
						writeableBitmap.Freeze();
					}
					catch
					{
					}
					list.Add(writeableBitmap);
				}
				else if (imageSource is BitmapSource source)
				{
					try
					{
						FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0.0);
						int pixelWidth = formatConvertedBitmap.PixelWidth;
						int pixelHeight = formatConvertedBitmap.PixelHeight;
						int num = pixelWidth * 4;
						byte[] array = new byte[pixelHeight * num];
						formatConvertedBitmap.CopyPixels(array, num, 0);
						double num2 = 0.82;
						if (outlineTint.A == byte.MaxValue && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0)
						{
							num2 = 0.7;
						}
						for (int j = 0; j < array.Length; j += 4)
						{
							byte b = array[j];
							byte b2 = array[j + 1];
							byte b3 = array[j + 2];
							if (array[j + 3] == 0)
							{
								continue;
							}
							double num3 = (0.2126 * (double)(int)b3 + 0.7152 * (double)(int)b2 + 0.0722 * (double)(int)b) / 255.0;
							bool flag = b3 <= 12 && b2 <= 12 && b <= 12;
							bool flag2 = num3 >= num2;
							if (flag)
							{
								continue;
							}
							if (flag2)
							{
								if (outlineTint.A > 0)
								{
									array[j + 3] = outlineTint.A;
									array[j + 2] = outlineTint.R;
									array[j + 1] = outlineTint.G;
									array[j] = outlineTint.B;
								}
							}
							else
							{
								array[j + 3] = byte.MaxValue;
								array[j + 2] = tint.R;
								array[j + 1] = tint.G;
								array[j] = tint.B;
							}
						}
						WriteableBitmap writeableBitmap2 = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Pbgra32, null);
						writeableBitmap2.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
						list.Add(writeableBitmap2);
					}
					catch
					{
						if (imageSource == null)
						{
							WriteableBitmap writeableBitmap3 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null);
							try
							{
								writeableBitmap3.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0);
							}
							catch
							{
							}
							try
							{
								writeableBitmap3.Freeze();
							}
							catch
							{
							}
							list.Add(writeableBitmap3);
						}
						else
						{
							list.Add(imageSource);
						}
					}
				}
				else if (imageSource == null)
				{
					WriteableBitmap writeableBitmap4 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
					try
					{
						writeableBitmap4.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0);
					}
					catch
					{
					}
					try
					{
						writeableBitmap4.Freeze();
					}
					catch
					{
					}
					list.Add(writeableBitmap4);
				}
				else
				{
					list.Add(imageSource);
				}
			}
			return list.ToArray();
		}
		RgbToHsl(tint.R, tint.G, tint.B, out var h, out var _, out var _);
		foreach (ImageSource imageSource2 in originals)
		{
			if (imageSource2 is BitmapSource source2)
			{
				try
				{
					FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(source2, PixelFormats.Bgra32, null, 0.0);
					int pixelWidth2 = formatConvertedBitmap2.PixelWidth;
					int pixelHeight2 = formatConvertedBitmap2.PixelHeight;
					int num4 = pixelWidth2 * 4;
					byte[] array2 = new byte[pixelHeight2 * num4];
					formatConvertedBitmap2.CopyPixels(array2, num4, 0);
					for (int m = 0; m < array2.Length; m += 4)
					{
						byte b4 = array2[m];
						byte b5 = array2[m + 1];
						byte b6 = array2[m + 2];
						if (array2[m + 3] == 0)
						{
							continue;
						}
						double num5 = (0.2126 * (double)(int)b6 + 0.7152 * (double)(int)b5 + 0.0722 * (double)(int)b4) / 255.0;
						bool flag3 = b6 <= 12 && b5 <= 12 && b4 <= 12;
						double num6 = 0.82;
						if (outlineTint.A == byte.MaxValue && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0)
						{
							num6 = 0.7;
						}
						bool flag4 = num5 >= num6;
						if (flag3)
						{
							continue;
						}
						if (flag4)
						{
							if (outlineTint.A > 0)
							{
								array2[m + 3] = outlineTint.A;
								array2[m + 2] = outlineTint.R;
								array2[m + 1] = outlineTint.G;
								array2[m] = outlineTint.B;
							}
						}
						else
						{
							RgbToHsl(b6, b5, b4, out var _, out var s2, out var l2);
							double h3 = h;
							double s3 = s2;
							double l3 = l2;
							RgbFromHsl(h3, s3, l3, out var r, out var g, out var b7);
							array2[m] = b7;
							array2[m + 1] = g;
							array2[m + 2] = r;
						}
					}
					WriteableBitmap writeableBitmap5 = new WriteableBitmap(pixelWidth2, pixelHeight2, formatConvertedBitmap2.DpiX, formatConvertedBitmap2.DpiY, PixelFormats.Bgra32, null);
					writeableBitmap5.WritePixels(new Int32Rect(0, 0, pixelWidth2, pixelHeight2), array2, num4, 0);
					list.Add(writeableBitmap5);
				}
				catch
				{
					if (imageSource2 == null)
					{
						WriteableBitmap writeableBitmap6 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
						try
						{
							writeableBitmap6.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0);
						}
						catch
						{
						}
						try
						{
							writeableBitmap6.Freeze();
						}
						catch
						{
						}
						list.Add(writeableBitmap6);
					}
					else
					{
						list.Add(imageSource2);
					}
				}
			}
			else if (imageSource2 == null)
			{
				WriteableBitmap writeableBitmap7 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
				try
				{
					writeableBitmap7.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0);
				}
				catch
				{
				}
				try
				{
					writeableBitmap7.Freeze();
				}
				catch
				{
				}
				list.Add(writeableBitmap7);
			}
			else
			{
				list.Add(imageSource2);
			}
		}
		return list.ToArray();
	}

	private ImageSource?[]? CreateTwoFramePulse(ImageSource src)
	{
		try
		{
			if (src is BitmapSource source)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
				int num = Math.Max(1, formatConvertedBitmap.PixelWidth);
				int num2 = Math.Max(1, formatConvertedBitmap.PixelHeight);
				int num3 = num * 4;
				byte[] array = new byte[num2 * num3];
				formatConvertedBitmap.CopyPixels(array, num3, 0);
				byte[] array2 = new byte[num2 * num3];
				for (int i = 0; i < array.Length; i += 4)
				{
					byte b = array[i];
					byte b2 = array[i + 1];
					byte b3 = array[i + 2];
					byte b4 = array[i + 3];
					if (b4 == 0)
					{
						array2[i] = b;
						array2[i + 1] = b2;
						array2[i + 2] = b3;
						array2[i + 3] = b4;
					}
					else
					{
						array2[i] = (byte)Math.Min(255, (int)Math.Round((double)(int)b + (double)(255 - b) * 0.35));
						array2[i + 1] = (byte)Math.Min(255, (int)Math.Round((double)(int)b2 + (double)(255 - b2) * 0.35));
						array2[i + 2] = (byte)Math.Min(255, (int)Math.Round((double)(int)b3 + (double)(255 - b3) * 0.35));
						array2[i + 3] = b4;
					}
				}
				WriteableBitmap writeableBitmap = new WriteableBitmap(formatConvertedBitmap.PixelWidth, formatConvertedBitmap.PixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
				writeableBitmap.WritePixels(new Int32Rect(0, 0, formatConvertedBitmap.PixelWidth, formatConvertedBitmap.PixelHeight), array, num3, 0);
				writeableBitmap.Freeze();
				WriteableBitmap writeableBitmap2 = new WriteableBitmap(formatConvertedBitmap.PixelWidth, formatConvertedBitmap.PixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
				writeableBitmap2.WritePixels(new Int32Rect(0, 0, formatConvertedBitmap.PixelWidth, formatConvertedBitmap.PixelHeight), array2, num3, 0);
				writeableBitmap2.Freeze();
				return new ImageSource[2] { writeableBitmap, writeableBitmap2 };
			}
		}
		catch
		{
		}
		return null;
	}

	private int? GetNearestPaletteIndexForColor(Color c, Color[]? palette)
	{
		try
		{
			if (palette == null || palette.Length == 0)
			{
				return null;
			}
			int num = -1;
			double num2 = double.MaxValue;
			for (int i = 0; i < palette.Length; i++)
			{
				Color color = palette[i];
				double num3 = color.R - c.R;
				double num4 = color.G - c.G;
				double num5 = color.B - c.B;
				double num6 = num3 * num3 + num4 * num4 + num5 * num5;
				if (num6 < num2)
				{
					num2 = num6;
					num = i;
				}
			}
			if (num >= 0)
			{
				return num;
			}
		}
		catch
		{
		}
		return null;
	}

	private ImageSource?[]? CreateHueShiftedImages(ImageSource?[]? originals, Color tint, Color outlineTint = default(Color))
	{
		if (originals == null)
		{
			return null;
		}
		if (tint.A == 0)
		{
			return originals;
		}
		if (tint.A == byte.MaxValue && tint.R == 0 && tint.G == 0 && tint.B == 0)
		{
			return CreateTwoToneTileImages(originals, Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(byte.MaxValue, 0, 0, 0), outlineTint);
		}
		if (tint.A == byte.MaxValue && tint.R == byte.MaxValue && tint.G == byte.MaxValue && tint.B == byte.MaxValue)
		{
			return CreateWhiteMaskedImages(originals, outlineTint);
		}
		if (tint.A == byte.MaxValue)
		{
			List<ImageSource> list = new List<ImageSource>(originals.Length);
			foreach (ImageSource imageSource in originals)
			{
				if (imageSource == null)
				{
					WriteableBitmap writeableBitmap = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
					byte[] pixels = new byte[4];
					try
					{
						writeableBitmap.WritePixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
					}
					catch
					{
					}
					try
					{
						writeableBitmap.Freeze();
					}
					catch
					{
					}
					list.Add(writeableBitmap);
				}
				else if (imageSource is BitmapSource source)
				{
					try
					{
						FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
						int pixelWidth = formatConvertedBitmap.PixelWidth;
						int pixelHeight = formatConvertedBitmap.PixelHeight;
						int num = pixelWidth * 4;
						byte[] array = new byte[pixelHeight * num];
						formatConvertedBitmap.CopyPixels(array, num, 0);
						double num2 = 0.82;
						if (outlineTint.A == byte.MaxValue && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0)
						{
							num2 = 0.7;
						}
						for (int j = 0; j < array.Length; j += 4)
						{
							byte b = array[j];
							byte b2 = array[j + 1];
							byte b3 = array[j + 2];
							if (array[j + 3] == 0)
							{
								continue;
							}
							double num3 = (0.2126 * (double)(int)b3 + 0.7152 * (double)(int)b2 + 0.0722 * (double)(int)b) / 255.0;
							bool flag = b3 <= 12 && b2 <= 12 && b <= 12;
							bool flag2 = num3 >= num2;
							if (flag)
							{
								continue;
							}
							if (flag2)
							{
								if (outlineTint.A > 0)
								{
									array[j + 3] = outlineTint.A;
									array[j + 2] = outlineTint.R;
									array[j + 1] = outlineTint.G;
									array[j] = outlineTint.B;
								}
							}
							else
							{
								array[j + 3] = byte.MaxValue;
								array[j + 2] = tint.R;
								array[j + 1] = tint.G;
								array[j] = tint.B;
							}
						}
						WriteableBitmap writeableBitmap2 = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
						writeableBitmap2.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
						list.Add(writeableBitmap2);
					}
					catch
					{
						if (imageSource == null)
						{
							WriteableBitmap writeableBitmap3 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
							try
							{
								writeableBitmap3.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0);
							}
							catch
							{
							}
							try
							{
								writeableBitmap3.Freeze();
							}
							catch
							{
							}
							list.Add(writeableBitmap3);
						}
						else
						{
							list.Add(imageSource);
						}
					}
				}
				else
				{
					list.Add(imageSource);
				}
			}
			return list.ToArray();
		}
		double num4 = (double)(int)tint.A / 255.0;
		RgbToHsl(tint.R, tint.G, tint.B, out var h, out var s, out var _);
		List<ImageSource> list2 = new List<ImageSource>(originals.Length);
		foreach (ImageSource imageSource2 in originals)
		{
			if (imageSource2 == null)
			{
				WriteableBitmap writeableBitmap4 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
				byte[] pixels2 = new byte[4];
				try
				{
					writeableBitmap4.WritePixels(new Int32Rect(0, 0, 1, 1), pixels2, 4, 0);
				}
				catch
				{
				}
				try
				{
					writeableBitmap4.Freeze();
				}
				catch
				{
				}
				list2.Add(writeableBitmap4);
			}
			else if (imageSource2 is BitmapSource source2)
			{
				try
				{
					FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(source2, PixelFormats.Bgra32, null, 0.0);
					int pixelWidth2 = formatConvertedBitmap2.PixelWidth;
					int pixelHeight2 = formatConvertedBitmap2.PixelHeight;
					int num5 = pixelWidth2 * 4;
					byte[] array2 = new byte[pixelHeight2 * num5];
					formatConvertedBitmap2.CopyPixels(array2, num5, 0);
					for (int m = 0; m < array2.Length; m += 4)
					{
						int num6 = array2[m];
						int num7 = array2[m + 1];
						int num8 = array2[m + 2];
						int num9 = array2[m + 3];
						if (num9 != 0)
						{
							double num10 = (0.2126 * (double)num8 + 0.7152 * (double)num7 + 0.0722 * (double)num6) / 255.0;
							if (num10 >= 0.82 && outlineTint.A > 0)
							{
								array2[m + 3] = outlineTint.A;
								array2[m + 2] = outlineTint.R;
								array2[m + 1] = outlineTint.G;
								array2[m] = outlineTint.B;
								continue;
							}
							RgbToHsl((byte)num8, (byte)num7, (byte)num6, out var h2, out var s2, out var l2);
							double h3 = LerpAngle(h2, h, num4);
							double s3 = s2 * (1.0 - num4) + s * num4;
							double l3 = l2;
							RgbFromHsl(h3, s3, l3, out var r, out var g, out var b4);
							array2[m] = b4;
							array2[m + 1] = g;
							array2[m + 2] = r;
							array2[m + 3] = (byte)num9;
						}
					}
					WriteableBitmap writeableBitmap5 = new WriteableBitmap(pixelWidth2, pixelHeight2, formatConvertedBitmap2.DpiX, formatConvertedBitmap2.DpiY, PixelFormats.Bgra32, null);
					writeableBitmap5.WritePixels(new Int32Rect(0, 0, pixelWidth2, pixelHeight2), array2, num5, 0);
					list2.Add(writeableBitmap5);
				}
				catch
				{
					list2.Add(imageSource2 ?? new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null));
				}
			}
			else
			{
				list2.Add(imageSource2 ?? new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null));
			}
		}
		return list2.ToArray();
	}

	private ImageSource?[]? CreateBlackMaskedImages(ImageSource?[]? originals, Color outlineTint = default(Color), bool recolorOutline = true)
	{
		if (originals == null)
		{
			return null;
		}
		List<ImageSource> list = new List<ImageSource>(originals.Length);
		foreach (ImageSource imageSource in originals)
		{
			if (imageSource == null)
			{
				WriteableBitmap writeableBitmap = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
				byte[] pixels = new byte[4];
				try
				{
					writeableBitmap.WritePixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
				}
				catch
				{
				}
				try
				{
					writeableBitmap.Freeze();
				}
				catch
				{
				}
				list.Add(writeableBitmap);
			}
			else if (imageSource is BitmapSource source)
			{
				try
				{
					FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
					int pixelWidth = formatConvertedBitmap.PixelWidth;
					int pixelHeight = formatConvertedBitmap.PixelHeight;
					int num = pixelWidth * 4;
					byte[] array = new byte[pixelHeight * num];
					formatConvertedBitmap.CopyPixels(array, num, 0);
					for (int j = 0; j < array.Length; j += 4)
					{
						byte b = array[j];
						byte b2 = array[j + 1];
						byte b3 = array[j + 2];
						if (array[j + 3] == 0)
						{
							continue;
						}
						double num2 = (0.2126 * (double)(int)b3 + 0.7152 * (double)(int)b2 + 0.0722 * (double)(int)b) / 255.0;
						if (num2 >= 0.82)
						{
							if (recolorOutline && outlineTint.A > 0)
							{
								array[j + 3] = byte.MaxValue;
								array[j + 2] = outlineTint.R;
								array[j + 1] = outlineTint.G;
								array[j] = outlineTint.B;
							}
						}
						else
						{
							array[j] = 0;
							array[j + 1] = 0;
							array[j + 2] = 0;
							array[j + 3] = byte.MaxValue;
						}
					}
					WriteableBitmap writeableBitmap2 = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
					writeableBitmap2.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
					list.Add(writeableBitmap2);
				}
				catch
				{
					list.Add(imageSource);
				}
			}
			else
			{
				list.Add(imageSource);
			}
		}
		return list.ToArray();
	}

	private ImageSource?[]? CreateBlackMaskedExceptColorArray(ImageSource?[]? originals, Color excludeColor, Color outlineTint = default(Color), bool recolorOutline = true)
	{
		if (originals == null)
		{
			return null;
		}
		List<ImageSource> list = new List<ImageSource>(originals.Length);
		foreach (ImageSource imageSource in originals)
		{
			try
			{
				ImageSource imageSource2 = CreateBlackMaskedExceptColor(imageSource, excludeColor, outlineTint, recolorOutline);
				list.Add(imageSource2 ?? imageSource ?? new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null));
			}
			catch
			{
				list.Add(imageSource ?? new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null));
			}
		}
		return list.ToArray();
	}

	private ImageSource?[]? CreateWhiteMaskedImages(ImageSource?[]? originals, Color outlineTint = default(Color), bool recolorOutline = false)
	{
		if (originals == null)
		{
			return null;
		}
		List<ImageSource> list = new List<ImageSource>(originals.Length);
		foreach (ImageSource imageSource in originals)
		{
			if (imageSource == null)
			{
				WriteableBitmap writeableBitmap = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
				byte[] pixels = new byte[4];
				try
				{
					writeableBitmap.WritePixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
				}
				catch
				{
				}
				try
				{
					writeableBitmap.Freeze();
				}
				catch
				{
				}
				list.Add(writeableBitmap);
			}
			else if (imageSource is BitmapSource source)
			{
				try
				{
					FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
					int pixelWidth = formatConvertedBitmap.PixelWidth;
					int pixelHeight = formatConvertedBitmap.PixelHeight;
					int num = pixelWidth * 4;
					byte[] array = new byte[pixelHeight * num];
					formatConvertedBitmap.CopyPixels(array, num, 0);
					for (int j = 0; j < array.Length; j += 4)
					{
						byte b = array[j];
						byte b2 = array[j + 1];
						byte b3 = array[j + 2];
						if (array[j + 3] == 0)
						{
							continue;
						}
						double num2 = (0.2126 * (double)(int)b3 + 0.7152 * (double)(int)b2 + 0.0722 * (double)(int)b) / 255.0;
						if (num2 >= 0.82)
						{
							if (recolorOutline && outlineTint.A > 0)
							{
								array[j + 3] = byte.MaxValue;
								array[j + 2] = outlineTint.R;
								array[j + 1] = outlineTint.G;
								array[j] = outlineTint.B;
							}
						}
						else
						{
							array[j + 2] = byte.MaxValue;
							array[j + 1] = byte.MaxValue;
							array[j] = byte.MaxValue;
							array[j + 3] = byte.MaxValue;
						}
					}
					WriteableBitmap writeableBitmap2 = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
					writeableBitmap2.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
					list.Add(writeableBitmap2);
				}
				catch
				{
					list.Add(imageSource);
				}
			}
			else
			{
				list.Add(imageSource);
			}
		}
		return list.ToArray();
	}

	private ImageSource?[]? CreateSolidBlackImages(ImageSource?[]? originals)
	{
		if (originals == null)
		{
			return null;
		}
		List<ImageSource> list = new List<ImageSource>(originals.Length);
		foreach (ImageSource imageSource in originals)
		{
			if (imageSource == null)
			{
				WriteableBitmap writeableBitmap = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
				byte[] pixels = new byte[4];
				try
				{
					writeableBitmap.WritePixels(new Int32Rect(0, 0, 1, 1), pixels, 4, 0);
				}
				catch
				{
				}
				try
				{
					writeableBitmap.Freeze();
				}
				catch
				{
				}
				list.Add(writeableBitmap);
			}
			else if (imageSource is BitmapSource source)
			{
				try
				{
					FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
					int num = Math.Max(1, formatConvertedBitmap.PixelWidth);
					int num2 = Math.Max(1, formatConvertedBitmap.PixelHeight);
					int num3 = num * 4;
					byte[] array = new byte[num2 * num3];
					formatConvertedBitmap.CopyPixels(array, num3, 0);
					for (int j = 0; j < array.Length; j += 4)
					{
						if (array[j + 3] != 0)
						{
							array[j] = 0;
							array[j + 1] = 0;
							array[j + 2] = 0;
							array[j + 3] = byte.MaxValue;
						}
					}
					WriteableBitmap writeableBitmap2 = new WriteableBitmap(num, num2, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
					writeableBitmap2.WritePixels(new Int32Rect(0, 0, num, num2), array, num3, 0);
					list.Add(writeableBitmap2);
				}
				catch
				{
					list.Add(imageSource);
				}
			}
			else
			{
				list.Add(imageSource);
			}
		}
		return list.ToArray();
	}

	private static void RgbToHsl(byte r8, byte g8, byte b8, out double h, out double s, out double l)
	{
		double num = (double)(int)r8 / 255.0;
		double num2 = (double)(int)g8 / 255.0;
		double num3 = (double)(int)b8 / 255.0;
		double num4 = Math.Max(num, Math.Max(num2, num3));
		double num5 = Math.Min(num, Math.Min(num2, num3));
		l = (num4 + num5) / 2.0;
		if (num4 == num5)
		{
			h = 0.0;
			s = 0.0;
			return;
		}
		double num6 = num4 - num5;
		s = ((l > 0.5) ? (num6 / (2.0 - num4 - num5)) : (num6 / (num4 + num5)));
		if (num4 == num)
		{
			h = (num2 - num3) / num6 + (double)((num2 < num3) ? 6 : 0);
		}
		else if (num4 == num2)
		{
			h = (num3 - num) / num6 + 2.0;
		}
		else
		{
			h = (num - num2) / num6 + 4.0;
		}
		h *= 60.0;
	}

	private static void RgbFromHsl(double h, double s, double l, out byte r8, out byte g8, out byte b8)
	{
		double num;
		double num2;
		double num3;
		if (s == 0.0)
		{
			num = (num2 = (num3 = l));
		}
		else
		{
			double num4 = ((l < 0.5) ? (l * (1.0 + s)) : (l + s - l * s));
			double num5 = 2.0 * l - num4;
			double num6 = h % 360.0 / 360.0;
			double[] array = new double[3]
			{
				num6 + 1.0 / 3.0,
				num6,
				num6 - 1.0 / 3.0
			};
			double[] array2 = new double[3];
			for (int i = 0; i < 3; i++)
			{
				double num7 = array[i];
				if (num7 < 0.0)
				{
					num7 += 1.0;
				}
				if (num7 > 1.0)
				{
					num7 -= 1.0;
				}
				if (num7 < 1.0 / 6.0)
				{
					array2[i] = num5 + (num4 - num5) * 6.0 * num7;
				}
				else if (num7 < 0.5)
				{
					array2[i] = num4;
				}
				else if (num7 < 2.0 / 3.0)
				{
					array2[i] = num5 + (num4 - num5) * (2.0 / 3.0 - num7) * 6.0;
				}
				else
				{
					array2[i] = num5;
				}
			}
			num = array2[0];
			num2 = array2[1];
			num3 = array2[2];
		}
		r8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(num * 255.0)));
		g8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(num2 * 255.0)));
		b8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(num3 * 255.0)));
	}

	private static double LerpAngle(double a, double b, double t)
	{
		double num = (b - a + 540.0) % 360.0 - 180.0;
		return (a + num * t + 360.0) % 360.0;
	}

	private unsafe BitmapSource ReplaceColorsForPlayer2(BitmapSource original)
	{
		try
		{
			AppendSimDebug($"[COLOR_REPLACE] Starting color replacement, original format: {original.Format}");
			Dictionary<uint, uint> dictionary = new Dictionary<uint, uint>
			{
				{ 4280993616u, 4294929139u },
				{ 4294946125u, 4278238908u }
			};
			FormatConvertedBitmap source = new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0.0);
			WriteableBitmap writeableBitmap = new WriteableBitmap(source);
			writeableBitmap.Lock();
			int num = 0;
			uint* backBuffer = (uint*)writeableBitmap.BackBuffer;
			int num2 = writeableBitmap.PixelWidth * writeableBitmap.PixelHeight;
			for (int i = 0; i < num2; i++)
			{
				uint key = backBuffer[i];
				if (dictionary.TryGetValue(key, out var value))
				{
					backBuffer[i] = value;
					num++;
				}
			}
			AppendSimDebug($"[COLOR_REPLACE] Replaced {num} pixels out of {writeableBitmap.PixelWidth * writeableBitmap.PixelHeight}, size: {writeableBitmap.PixelWidth}x{writeableBitmap.PixelHeight}");
			writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, writeableBitmap.PixelWidth, writeableBitmap.PixelHeight));
			writeableBitmap.Unlock();
			writeableBitmap.Freeze();
			return writeableBitmap;
		}
		catch (Exception ex)
		{
			AppendSimDebug("[COLOR_REPLACE] Error: " + ex.Message);
			return original;
		}
	}

	private void ClearSlopeStuff()
	{
		currplayer_was_on_slope_counter = 0;
		currplayer_slope_frames = 0;
		currplayer_slope_type = 0;
		currplayer_last_slope_type = 0;
	}

	private void ResetSlopeState()
	{
		slope_type_arr[0] = 0;
		slope_type_arr[1] = 0;
		slope_frames_arr[0] = 0;
		slope_frames_arr[1] = 0;
		was_on_slope_counter_arr[0] = 0;
		was_on_slope_counter_arr[1] = 0;
		last_slope_type_arr[0] = 0;
		last_slope_type_arr[1] = 0;
		currplayer_last_slope_type = 0;
		make_cube_jump_higher = false;
		AppendSimDebug("[SLOPE] All slope state reset");
	}

	private void SaveSlopeStateForPlayer(int player)
	{
		slope_frames_arr[player] = currplayer_slope_frames;
		was_on_slope_counter_arr[player] = currplayer_was_on_slope_counter;
		slope_type_arr[player] = currplayer_slope_type;
		last_slope_type_arr[player] = currplayer_last_slope_type;
	}

	private void LoadSlopeStateForPlayer(int player)
	{
		currplayer_slope_frames = slope_frames_arr[player];
		currplayer_was_on_slope_counter = was_on_slope_counter_arr[player];
		currplayer_slope_type = slope_type_arr[player];
		currplayer_last_slope_type = last_slope_type_arr[player];
	}

	private (bool collided, int ejectionAmount) CheckSlopeCollision(int temp_x, int temp_y, MetatileCollision collision)
	{
		if (collision < MetatileCollision.COL_SLOPE_RD45 || collision > MetatileCollision.COL_SLOPE_LU66_TOP)
		{
			return (collided: false, ejectionAmount: 0);
		}
		int num = 0;
		int num2 = 0;
		int num3 = temp_y & 0xF;
		switch (collision)
		{
		case MetatileCollision.COL_SLOPE_LU45:
			num = temp_x & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 9;
			break;
		case MetatileCollision.COL_SLOPE_LD45:
			num = temp_x & 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 1;
			break;
		case MetatileCollision.COL_SLOPE_RU45:
			num = (temp_x & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 13;
			break;
		case MetatileCollision.COL_SLOPE_RD45:
			num = (temp_x & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 5;
			break;
		case MetatileCollision.COL_SLOPE_RU22_RIGHT:
			num = ((temp_x >> 1) & 7) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 14;
			break;
		case MetatileCollision.COL_SLOPE_RU22_LEFT:
			num = (((temp_x >> 1) | 8) & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 14;
			break;
		case MetatileCollision.COL_SLOPE_RD22_RIGHT:
			num = ((temp_x >> 1) & 7) ^ 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 6;
			break;
		case MetatileCollision.COL_SLOPE_RD22_LEFT:
			num = (((temp_x >> 1) | 8) & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 6;
			break;
		case MetatileCollision.COL_SLOPE_LU22_RIGHT:
			num = (temp_x >> 1) & 7;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 10;
			break;
		case MetatileCollision.COL_SLOPE_LU22_LEFT:
			num = ((temp_x >> 1) | 8) & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 10;
			break;
		case MetatileCollision.COL_SLOPE_LD22_RIGHT:
			num = (temp_x >> 1) & 7;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 2;
			break;
		case MetatileCollision.COL_SLOPE_LD22_LEFT:
			num = ((temp_x >> 1) | 8) & 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 2;
			break;
		case MetatileCollision.COL_SLOPE_RD66_TOP:
			if ((byte)(temp_x & 0xF) < 8)
			{
				return (collided: false, ejectionAmount: 0);
			}
			num = (((temp_x & 7) << 1) & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 7;
			break;
		case MetatileCollision.COL_SLOPE_RD66_BOT:
			currplayer_slope_type = 7;
			if ((byte)(temp_x & 0xF) >= 8)
			{
				return (collided: true, ejectionAmount: temp_y & 0xF);
			}
			num = (((temp_x & 0xF) << 1) & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LD66_TOP:
			if ((byte)(temp_x & 0xF) >= 8)
			{
				return (collided: false, ejectionAmount: 0);
			}
			num = ((temp_x & 7) << 1) & 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 3;
			break;
		case MetatileCollision.COL_SLOPE_LD66_BOT:
			currplayer_slope_type = 3;
			if ((byte)(temp_x & 0xF) < 8)
			{
				return (collided: true, ejectionAmount: temp_y & 0xF);
			}
			num = ((temp_x & 0xF) << 1) & 0xF;
			num2 = temp_y & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RU66_TOP:
			if ((byte)(temp_x & 0xF) < 8)
			{
				return (collided: false, ejectionAmount: 0);
			}
			num = (((temp_x & 7) << 1) & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 15;
			break;
		case MetatileCollision.COL_SLOPE_RU66_BOT:
			currplayer_slope_type = 15;
			if ((byte)(temp_x & 0xF) >= 8)
			{
				return (collided: true, ejectionAmount: temp_y & 0xF);
			}
			num = (((temp_x & 0xF) << 1) & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LU66_TOP:
			if ((byte)(temp_x & 0xF) >= 8)
			{
				return (collided: false, ejectionAmount: 0);
			}
			num = ((temp_x & 7) << 1) & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 11;
			break;
		case MetatileCollision.COL_SLOPE_LU66_BOT:
			currplayer_slope_type = 11;
			if ((byte)(temp_x & 0xF) < 8)
			{
				return (collided: true, ejectionAmount: temp_y & 0xF);
			}
			num = ((temp_x & 0xF) << 1) & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			break;
		default:
			return (collided: false, ejectionAmount: 0);
		}
		if ((byte)num2 >= (byte)num)
		{
			num3 = num2 - num;
			currplayer_slope_frames = 1;
			currplayer_was_on_slope_counter = 3;
			if (keyXPressedCount > 0)
			{
				make_cube_jump_higher = true;
			}
			return (collided: true, ejectionAmount: num3);
		}
		if (currplayer_was_on_slope_counter == 0)
		{
			currplayer_slope_type = 0;
			num3 = 0;
		}
		return (collided: false, ejectionAmount: 0);
	}

	private (bool collided, int ejectionAmount, int slopeType) CheckSlopeCollision_WithType(int temp_x, int temp_y, MetatileCollision collision)
	{
		var (item, item2) = CheckSlopeCollision(temp_x, temp_y, collision);
		return (collided: item, ejectionAmount: item2, slopeType: currplayer_slope_type);
	}

	private void SlopeJumpCheck_Fresh()
	{
		bool slopeJumpHigher = make_cube_jump_higher;
		SharedPhysics.SlopeJumpCheck(ref playerVelY_fixed, ref slopeJumpHigher, currplayer_slope_type, currplayer_mini != 0, currplayer_gravity != 0);
		make_cube_jump_higher = slopeJumpHigher;
	}

	private int slope_vel_calc(int slopeType)
	{
		int num = playerVelX_fixed;
		return (slopeType & 3) switch
		{
			2 => num >> 1, 
			1 => num, 
			3 => num << 1, 
			_ => 0, 
		};
	}

	private void apply_slope_vel()
	{
		int num = slope_vel_calc(currplayer_slope_type);
		if ((currplayer_slope_type & 4) != 0)
		{
			if ((currplayer_slope_type & 8) != 0)
			{
				playerVelY_fixed = num;
			}
			else
			{
				playerVelY_fixed = -num;
			}
		}
		else if ((currplayer_slope_type & 8) != 0)
		{
			playerVelY_fixed = -num;
		}
		else
		{
			playerVelY_fixed = num;
		}
		AppendSimDebug($"[SLOPE] apply_slope_vel: slopeType=0x{currplayer_slope_type:X2}, velY={playerVelY_fixed}");
	}

	private void UpdateSlopeCounters_Fresh()
	{
		int slopeFrames = currplayer_slope_frames;
		SharedPhysics.UpdateSlopeCountersFresh(ref slopeFrames, currplayer_slope_type, ref playerVelY_fixed, playerVelX_fixed);
		currplayer_slope_frames = slopeFrames;
	}

	private bool bg_coll_slope(int temp_x, int temp_y, MetatileCollision collision)
	{
		tmp8 = temp_y & 0xF;
		if (collision < MetatileCollision.COL_SLOPE_RD45 || collision > MetatileCollision.COL_SLOPE_LU66_TOP)
		{
			return false;
		}
		int num = 0;
		int num2 = 0;
		switch (collision)
		{
		case MetatileCollision.COL_SLOPE_LU45:
			if (currentGameMode == 6 && currplayer_mini == 0)
			{
				return false;
			}
			num = temp_x & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 9;
			break;
		case MetatileCollision.COL_SLOPE_LD45:
			num = temp_x & 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 1;
			break;
		case MetatileCollision.COL_SLOPE_RU45:
			num = (temp_x & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 13;
			break;
		case MetatileCollision.COL_SLOPE_RD45:
			num = (temp_x & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 5;
			break;
		case MetatileCollision.COL_SLOPE_RU22_RIGHT:
			num = ((temp_x >> 1) & 7) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 14;
			break;
		case MetatileCollision.COL_SLOPE_RU22_LEFT:
			num = (((temp_x >> 1) | 8) & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 14;
			break;
		case MetatileCollision.COL_SLOPE_RD22_RIGHT:
			num = ((temp_x >> 1) & 7) ^ 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 6;
			break;
		case MetatileCollision.COL_SLOPE_RD22_LEFT:
			num = (((temp_x >> 1) | 8) & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 6;
			break;
		case MetatileCollision.COL_SLOPE_LU22_RIGHT:
			num = (temp_x >> 1) & 7;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 10;
			break;
		case MetatileCollision.COL_SLOPE_LU22_LEFT:
			num = ((temp_x >> 1) | 8) & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 10;
			break;
		case MetatileCollision.COL_SLOPE_LD22_RIGHT:
			num = (temp_x >> 1) & 7;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 2;
			break;
		case MetatileCollision.COL_SLOPE_LD22_LEFT:
			num = ((temp_x >> 1) | 8) & 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 2;
			break;
		case MetatileCollision.COL_SLOPE_RD66_TOP:
			if ((temp_x & 0xF) < 8)
			{
				return false;
			}
			num = (((temp_x & 7) << 1) & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 7;
			break;
		case MetatileCollision.COL_SLOPE_RD66_BOT:
			currplayer_slope_type = 7;
			if ((temp_x & 0xF) >= 8)
			{
				return true;
			}
			num = (((temp_x & 0xF) << 1) & 0xF) ^ 0xF;
			num2 = temp_y & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LD66_TOP:
			if ((temp_x & 0xF) >= 8)
			{
				return false;
			}
			num = ((temp_x & 7) << 1) & 0xF;
			num2 = temp_y & 0xF;
			currplayer_slope_type = 3;
			break;
		case MetatileCollision.COL_SLOPE_LD66_BOT:
			currplayer_slope_type = 3;
			if ((temp_x & 0xF) < 8)
			{
				return true;
			}
			num = ((temp_x & 0xF) << 1) & 0xF;
			num2 = temp_y & 0xF;
			break;
		case MetatileCollision.COL_SLOPE_RU66_TOP:
			if ((temp_x & 0xF) < 8)
			{
				return false;
			}
			num = (((temp_x & 7) << 1) & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 15;
			break;
		case MetatileCollision.COL_SLOPE_RU66_BOT:
			currplayer_slope_type = 15;
			if ((temp_x & 0xF) >= 8)
			{
				return true;
			}
			num = (((temp_x & 0xF) << 1) & 0xF) ^ 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			break;
		case MetatileCollision.COL_SLOPE_LU66_TOP:
			if (currentGameMode == 6 && currplayer_mini != 0)
			{
				return false;
			}
			if ((temp_x & 0xF) >= 8)
			{
				return false;
			}
			num = ((temp_x & 7) << 1) & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			currplayer_slope_type = 11;
			break;
		case MetatileCollision.COL_SLOPE_LU66_BOT:
			if (currentGameMode == 6 && currplayer_mini != 0)
			{
				return false;
			}
			currplayer_slope_type = 11;
			if ((temp_x & 0xF) < 8)
			{
				return true;
			}
			num = ((temp_x & 0xF) << 1) & 0xF;
			num2 = (temp_y & 0xF) ^ 0xF;
			break;
		default:
			return false;
		}
		if (num2 >= num)
		{
			tmp8 = num2 - num;
			if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8 || currentGameMode == 11)
			{
				if (IsXDownAsync() || keyXHeld || upHeld)
				{
					make_cube_jump_higher = true;
					AppendSimDebug("[SLOPE] col_end: Cube/Robot/Ninja mode, A held → make_cube_jump_higher");
				}
				else
				{
					currplayer_slope_frames = 1;
					currplayer_was_on_slope_counter = 3;
				}
			}
			else
			{
				int num3 = 0;
				if ((currplayer_slope_type & 4) != 0)
				{
					num3 |= 4;
				}
				if ((currplayer_slope_type & 8) != 0)
				{
					num3 |= 2;
				}
				if (currplayer_gravity != 0)
				{
					num3 |= 1;
				}
				bool flag = num3 == 0 || num3 == 3 || num3 == 4 || num3 == 7;
				bool flag2 = IsXDownAsync() || keyXHeld || upHeld;
				if (flag)
				{
					if (flag2)
					{
						tmp8 = 4;
					}
				}
				else if (!flag2)
				{
					tmp8 = 4;
				}
				currplayer_slope_frames = 1;
				currplayer_was_on_slope_counter = 3;
			}
			AppendSimDebug($"[SLOPE] Collision YES: tmp4={num2}, tmp7={num}, tmp8={tmp8}, slopeType=0x{currplayer_slope_type:X2}, mode={currentGameMode}");
			return true;
		}
		if (currplayer_was_on_slope_counter == 0)
		{
			currplayer_slope_type = 0;
			tmp8 = 0;
		}
		return false;
	}

	private bool bg_coll_return_slope_D(int temp_x, int temp_y, MetatileCollision collision, int tmp2)
	{
		int num = currplayer_slope_frames;
		int num2 = currplayer_was_on_slope_counter;
		bool flag = bg_coll_slope(temp_x, temp_y, collision);
		AppendSimDebug($"[SLOPE] Filter: tmp2={tmp2}, tmp1={flag}, slopeType={currplayer_slope_type:X2}, hasRISING={(currplayer_slope_type & 4) != 0}");
		if (tmp2 == 0)
		{
			if ((currplayer_slope_type & 4) != 0)
			{
				AppendSimDebug("[SLOPE] Filter: LEFT rejects RISING slope");
				currplayer_slope_type = currplayer_last_slope_type;
				if (pathfinderEnabled)
				{
					currplayer_slope_frames = num;
					currplayer_was_on_slope_counter = num2;
				}
				return false;
			}
		}
		else if ((currplayer_slope_type & 4) == 0)
		{
			AppendSimDebug("[SLOPE] Filter: RIGHT rejects non-RISING slope");
			currplayer_slope_type = currplayer_last_slope_type;
			if (pathfinderEnabled)
			{
				currplayer_slope_frames = num;
				currplayer_was_on_slope_counter = num2;
			}
			return false;
		}
		if (flag)
		{
			if ((currplayer_last_slope_type & 4) != 0 && (currplayer_slope_type & 4) == 0 && currplayer_last_slope_type != 0 && currplayer_slope_type != 0)
			{
				currplayer_slope_type = currplayer_last_slope_type;
				tmp8 = playerVelX_fixed >> 8;
			}
			if (currplayer_slope_type != 0)
			{
				currplayer_last_slope_type = currplayer_slope_type;
			}
			eject_D = tmp8;
		}
		return flag;
	}

	private bool bg_coll_D_slopes()
	{
		int num = playerX_fixed >> 8;
		int num2 = playerY_fixed >> 8;
		int num3 = cameraX_fixed >> 8;
		int num4 = num - num3;
		int num8;
		int num9;
		int num10;
		if (currentGameMode == 6 || currentGameMode == 10)
		{
			int num5 = ((currplayer_mini == 0) ? 4 : 0);
			int num6 = num2 + num5;
			int num7 = ((currplayer_mini != 0) ? 4 : 0);
			num8 = num + 4;
			num9 = 8;
			num10 = num6 + 8 - 2 + num7;
			int num11 = 8;
		}
		else
		{
			int num12 = ((currplayer_mini != 0) ? 8 : 15);
			int num11 = ((currplayer_mini != 0) ? 7 : 15);
			int num13 = ((currplayer_mini != 0) ? (16 - num11 >> 1) : 0);
			int num14 = num2 + num13;
			num8 = num;
			num9 = num12;
			num10 = num14 + num11 - 2;
		}
		AppendSimDebug($"[SLOPE] bg_coll_D_slopes: playerX={num}, checkY={num10}, checkX={num8}, checkW={num9}, mini={currplayer_mini != 0}");
		if (num < 16)
		{
			return false;
		}
		int num15 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		int num16 = num10;
		int num17 = 0;
		int num18 = 0;
		do
		{
			int num19 = num8 + num17 * num9;
			int num20 = num19 / 16;
			int num21 = num16 / 16;
			int num22 = num21 + num15;
			if (num20 >= 0 && num20 < mapWidth && num21 >= 0 && num21 < mapHeight && num22 < mapHeight)
			{
				int num23 = num22 * mapWidth + num20;
				if (num23 >= 0 && num23 < tiles.Length)
				{
					byte b = (byte)SharedPhysics.MapTileForCollision(tiles[num23]);
					MetatileCollision metatileCollision = MetatileCollisionTable.GetCollision(b);
					AppendSimDebug($"[SLOPE] Check: tmp2={num17}, tempX={num19}, tempY={num16}, tile=[{num20},{num21}], arrayY={num22}, idx={num23}, tileVal=0x{b:X2}, collision={metatileCollision}");
					if (metatileCollision >= MetatileCollision.COL_SLOPE_RD45 && metatileCollision <= MetatileCollision.COL_SLOPE_LU66_TOP && bg_coll_return_slope_D(num19, num16, metatileCollision, num17))
					{
						num18 = 1;
						AppendSimDebug($"[SLOPE] HIT! eject_D={eject_D}, tmp8={tmp8}, slopeType={currplayer_slope_type:X2}");
					}
				}
			}
			num17++;
		}
		while (num17 < 2);
		return num18 != 0;
	}

	private bool bg_coll_return_slope_U(int temp_x, int temp_y, MetatileCollision collision, int tmp2)
	{
		int num = currplayer_slope_frames;
		int num2 = currplayer_was_on_slope_counter;
		bool flag = bg_coll_slope(temp_x, temp_y, collision);
		AppendSimDebug($"[SLOPE_U] Filter: tmp2={tmp2}, tmp1={flag}, slopeType={currplayer_slope_type:X2}, hasRISING={(currplayer_slope_type & 4) != 0}");
		if (tmp2 == 0)
		{
			if ((currplayer_slope_type & 4) != 0)
			{
				currplayer_slope_type = currplayer_last_slope_type;
				if (pathfinderEnabled)
				{
					currplayer_slope_frames = num;
					currplayer_was_on_slope_counter = num2;
				}
				return false;
			}
		}
		else if ((currplayer_slope_type & 4) == 0)
		{
			currplayer_slope_type = currplayer_last_slope_type;
			if (pathfinderEnabled)
			{
				currplayer_slope_frames = num;
				currplayer_was_on_slope_counter = num2;
			}
			return false;
		}
		if (flag)
		{
			if ((currplayer_last_slope_type & 4) != 0 && (currplayer_slope_type & 4) == 0 && currplayer_last_slope_type != 0 && currplayer_slope_type != 0)
			{
				currplayer_slope_type = currplayer_last_slope_type;
				tmp8 = playerVelX_fixed >> 8;
			}
			if (currplayer_slope_type != 0)
			{
				currplayer_last_slope_type = currplayer_slope_type;
			}
			eject_U = -tmp8;
		}
		return flag;
	}

	private bool bg_coll_U_slopes()
	{
		int num = playerX_fixed >> 8;
		int num2 = playerY_fixed >> 8;
		int num6;
		int num7;
		int num8;
		if (currentGameMode == 6 || currentGameMode == 10)
		{
			int num3 = ((currplayer_mini == 0) ? 4 : 0);
			int num4 = num2 + num3;
			int num5 = 4;
			num6 = num + 4;
			num7 = 8;
			num8 = num4 + num5 + ((currplayer_mini != 0) ? 1 : 2);
		}
		else
		{
			int num9 = ((currplayer_mini != 0) ? 8 : 15);
			int num10 = ((currplayer_mini != 0) ? 7 : 15);
			int num11 = ((currplayer_mini != 0) ? (16 - num10 >> 1) : 0);
			num6 = num;
			num7 = num9;
			num8 = num2 + num11 + ((currplayer_mini != 0) ? 1 : 2) + ((currentGameMode == 1) ? 1 : 0);
		}
		AppendSimDebug($"[SLOPE_U] bg_coll_U_slopes: playerX={num}, checkY={num8}, checkX={num6}, checkW={num7}, mini={currplayer_mini != 0}");
		if (num < 16)
		{
			return false;
		}
		int num12 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		int num13 = num8;
		int num14 = 0;
		int num15 = 0;
		do
		{
			int num16 = num6 + num14 * num7;
			int num17 = num16 / 16;
			int num18 = num13 / 16;
			int num19 = num18 + num12;
			if (num17 >= 0 && num17 < mapWidth && num18 >= 0 && num18 < mapHeight && num19 < mapHeight)
			{
				int num20 = num19 * mapWidth + num17;
				if (num20 >= 0 && num20 < tiles.Length)
				{
					byte b = (byte)SharedPhysics.MapTileForCollision(tiles[num20]);
					MetatileCollision metatileCollision = MetatileCollisionTable.GetCollision(b);
					AppendSimDebug($"[SLOPE_U] Check: tmp2={num14}, tempX={num16}, tempY={num13}, tile=[{num17},{num18}], tileVal=0x{b:X2}, collision={metatileCollision}");
					if (metatileCollision >= MetatileCollision.COL_SLOPE_RD45 && metatileCollision <= MetatileCollision.COL_SLOPE_LU66_TOP && bg_coll_return_slope_U(num16, num13, metatileCollision, num14))
					{
						num15 = 1;
						AppendSimDebug($"[SLOPE_U] HIT! eject_U={eject_U}, tmp8={tmp8}, slopeType={currplayer_slope_type:X2}");
					}
				}
			}
			num14++;
		}
		while (num14 < 2);
		return num15 != 0;
	}

	private void UpdateSlopeCounters()
	{
		int slopeWasOnCounter = currplayer_was_on_slope_counter;
		int slopeType = currplayer_slope_type;
		int lastSlopeType = currplayer_last_slope_type;
		SharedPhysics.UpdateSlopeCounters(ref slopeWasOnCounter, ref slopeType, ref playerVelY_fixed, ref playerY_fixed, currentGameMode, currplayer_gravity != 0, currplayer_mini != 0, ref lastSlopeType);
		currplayer_was_on_slope_counter = slopeWasOnCounter;
		currplayer_slope_type = slopeType;
		currplayer_last_slope_type = lastSlopeType;
	}

	private bool ShouldSkipSideCollisionForSlope()
	{
		return (currplayer_was_on_slope_counter | currplayer_slope_frames) != 0;
	}

	private void SnakePhysics_Fresh()
	{
		int num = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
		if (num > 0)
		{
			currplayer_gravity = (byte)((currplayer_gravity == 0) ? 255u : 0u);
			gravityReversed = currplayer_gravity != 0;
			gravityFlipped = gravityReversed;
			dashing[currplayer] = 1;
			Interlocked.Exchange(ref keyXPressedCount, 0);
			try
			{
				base.Dispatcher?.BeginInvoke((Action)delegate
				{
					UpdatePlayerIconFlip();
					InvertedCheckBox.IsChecked = gravityReversed;
				});
			}
			catch
			{
			}
		}
		switch (dashing[currplayer])
		{
		case 0:
			if (currplayer_mini == 0)
			{
				playerVelY_fixed = ((currplayer_gravity != 0) ? (-playerVelX_fixed) : playerVelX_fixed);
			}
			else
			{
				playerVelY_fixed = ((currplayer_gravity != 0) ? (-(playerVelX_fixed << 1)) : (playerVelX_fixed << 1));
			}
			if (!IsXDownAsync() && !keyXHeld)
			{
				playerVelY_fixed = -playerVelY_fixed;
			}
			break;
		case 1:
			playerVelY_fixed = 1;
			break;
		case 2:
			playerVelY_fixed = -playerVelX_fixed;
			if (isFullSpeed)
			{
				playerY_fixed += playerVelY_fixed;
			}
			else
			{
				playerY_fixed += (int)Math.Round((double)playerVelY_fixed * simTimeScale);
			}
			break;
		case 3:
			playerVelY_fixed = playerVelX_fixed;
			if (isFullSpeed)
			{
				playerY_fixed += playerVelY_fixed;
			}
			else
			{
				playerY_fixed += (int)Math.Round((double)playerVelY_fixed * simTimeScale);
			}
			break;
		case 4:
			playerVelY_fixed = playerVelX_fixed;
			if (isFullSpeed)
			{
				playerY_fixed -= playerVelY_fixed;
			}
			else
			{
				playerY_fixed -= (int)Math.Round((double)playerVelY_fixed * simTimeScale);
			}
			break;
		case 5:
			playerVelY_fixed = playerVelX_fixed;
			if (isFullSpeed)
			{
				playerY_fixed += playerVelY_fixed;
			}
			else
			{
				playerY_fixed += (int)Math.Round((double)playerVelY_fixed * simTimeScale);
			}
			break;
		}
		if (currplayer_slope_frames == 0 && currplayer_was_on_slope_counter == 0)
		{
			if (isFullSpeed)
			{
				playerY_fixed += playerVelY_fixed;
			}
			else
			{
				playerY_fixed += (int)Math.Round((double)playerVelY_fixed * simTimeScale);
			}
		}
		else
		{
			playerVelY_fixed = 0;
		}
		int offsetY = (playerY_fixed >> 8) + ((playerVelY_fixed < 0) ? 2 : (-2));
		WaveEject_Fresh(offsetY);
		if (pfSimulating)
		{
			return;
		}
		try
		{
			int num2 = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num3 = playerY_fixed >> 8;
			if (miniMode)
			{
				num3 += 4;
			}
			int num4 = num3 + playerVisualHeight / 2;
			if (currplayer == 0)
			{
				recordedPlayerPath.Add((num2, num4));
			}
			else if (dual)
			{
				RecordP2PathPoint(num2, num4);
			}
		}
		catch
		{
		}
	}

	private void SpiderPhysics_Fresh()
	{
		bool flag = IsXDownAsync() || keyXHeld;
		int num = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
		bool flag2 = num > 0;
		bool gravityInverted = currplayer_gravity != 0;
		int playerX_px = (playerX_fixed >> 8) + 1;
		int num2 = playerY_fixed >> 8;
		int playerW = ((currplayer_mini != 0) ? 8 : 15);
		int playerH = ((currplayer_mini != 0) ? 7 : 15);
		if (currplayer_mini != 0)
		{
			num2 += 4;
		}
		int scrollX_px = 0;
		int num3 = playerVelY_fixed;
		var (flag3, num4) = UpdateOrbSystem(5, playerX_px, num2, playerW, playerH, scrollX_px, flag2, flag, gravityInverted, currplayer_mini != 0, ref num3);
		if (flag3)
		{
			playerVelY_fixed = num3;
			AppendSimDebug($"[SPIDER] Orb activated! Type=0x{num4:X2}, New velY={playerVelY_fixed}");
			if (num4 == 68)
			{
				blackOrbed = true;
				AppendSimDebug("[SPIDER] BLACK ORB activated - blackOrbed set to true");
			}
			if (flag2)
			{
				Interlocked.Exchange(ref keyXPressedCount, 0);
			}
		}
		if (!flag)
		{
			ClearOrbBuffer();
		}
		int table_idx = (miniMode ? 4 : 0);
		bool flag4 = gravityFlipped;
		int num5 = ((!flag4) ? 1 : (-1));
		tmpfallspeed = GameModePhysics.SPIDER_MAX_FALLSPEED(table_idx) * num5;
		tmpgravity = GameModePhysics.SPIDER_GRAVITY(table_idx) * num5;
		CommonGravityRoutine_Fresh();
		int offsetY = (playerY_fixed >> 8) + ((!flag4) ? 1 : (-2));
		SpiderEject_Fresh(offsetY);
		UpdateSlopeCounters_Fresh();
		onGround = playerVelY_fixed == 0;
		AppendSimDebug($"[SPIDER] After eject: onGround={onGround}, velY={playerVelY_fixed}");
		bool flag5 = IsXDownAsync() || keyXHeld;
		int num6 = Interlocked.Exchange(ref keyXPressedCount, 0);
		bool flag6 = num6 > 0;
		bool flag7 = playerVelY_fixed == 0 && !orbed[currplayer];
		if (!gravityFlipped)
		{
			if ((flag6 || (flag5 && blackOrbed)) && flag7)
			{
				AppendSimDebug($"[SPIDER] TELEPORTING TO CEILING! Start Y={playerY_fixed >> 8}");
				currplayer_gravity = byte.MaxValue;
				gravityFlipped = true;
				gravityReversed = true;
				UpdateCurrplayerTableIdx_Fresh();
				SpiderUpWait_Fresh();
				playerVelY_fixed = 0;
				try
				{
					base.Dispatcher?.BeginInvoke((Action)delegate
					{
						UpdatePlayerIconFlip();
					});
				}
				catch
				{
				}
				AppendSimDebug($"[SPIDER] Teleported to ceiling Y={playerY_fixed >> 8}");
				blackOrbed = false;
			}
			else if (!flag5)
			{
				blackOrbed = false;
				orbed[currplayer] = false;
			}
		}
		else if ((flag6 || (flag5 && blackOrbed)) && flag7)
		{
			AppendSimDebug($"[SPIDER] TELEPORTING TO FLOOR! Start Y={playerY_fixed >> 8}");
			currplayer_gravity = 0;
			gravityFlipped = false;
			gravityReversed = false;
			UpdateCurrplayerTableIdx_Fresh();
			SpiderDownWait_Fresh();
			playerVelY_fixed = 0;
			try
			{
				base.Dispatcher?.BeginInvoke((Action)delegate
				{
					UpdatePlayerIconFlip();
				});
			}
			catch
			{
			}
			AppendSimDebug($"[SPIDER] Teleported to floor Y={playerY_fixed >> 8}");
			blackOrbed = false;
		}
		else if (!flag5)
		{
			blackOrbed = false;
			orbed[currplayer] = false;
		}
		if (pfSimulating)
		{
			return;
		}
		try
		{
			int num7 = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num8 = playerY_fixed >> 8;
			if (currplayer_mini != 0)
			{
				num8 += 4;
			}
			int num9 = num8 + playerVisualHeight / 2;
			if (currplayer == 0)
			{
				recordedPlayerPath.Add((num7, num9));
			}
			else if (dual)
			{
				RecordP2PathPoint(num7, num9);
			}
		}
		catch
		{
		}
	}

	private void SpiderEject_Fresh(int offsetY)
	{
		bool flag = miniMode;
		int width = (flag ? 8 : 15);
		int num = (flag ? 7 : 15);
		int num2 = (flag ? (16 - num >> 1) : 0);
		int playerX_px = playerX_fixed >> 8;
		int num3 = offsetY + num2;
		int groundRowsToReserve = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		UpdateSlopeCounters();
		if (!gravityFlipped)
		{
			if (bg_coll_D_slopes())
			{
				if (eject_D > 0)
				{
					int num4 = playerY_fixed >> 8;
					int num5 = num4 - eject_D;
					playerY_fixed = num5 << 8;
				}
				playerVelY_fixed = 0;
				wasZeroedByCollisionLastFrame = true;
				return;
			}
			var (flag2, num6) = BgCollD_Spider(playerX_px, num3, width, num, groundRowsToReserve, useEjectProbes: true);
			if (flag2)
			{
				int num7 = playerY_fixed >> 8;
				int num8 = num7 - num6;
				playerY_fixed = num8 << 8;
				playerVelY_fixed = 0;
				wasZeroedByCollisionLastFrame = true;
				AppendSimDebug($"[SPIDER_EJECT] Floor collision: eject={num6}, Y {num7} -> {num8}");
			}
			else
			{
				wasZeroedByCollisionLastFrame = false;
			}
		}
		else
		{
			var (flag3, num9) = BgCollU_Spider(playerX_px, num3, width, num, groundRowsToReserve, useEjectProbes: true);
			if (flag3)
			{
				int num10 = num3 + num9;
				playerY_fixed = num10 << 8;
				playerVelY_fixed = 0;
				wasZeroedByCollisionLastFrame = true;
				AppendSimDebug($"[SPIDER_EJECT] Ceiling collision: eject={num9}, Y -> {num10}");
			}
			else
			{
				wasZeroedByCollisionLastFrame = false;
			}
		}
	}

	private void SpiderUpWait_Fresh()
	{
		int width = (miniMode ? 8 : 15);
		int height = (miniMode ? 8 : 15);
		int num = (miniMode ? 0 : 0);
		int playerX_px = playerX_fixed >> 8;
		int num2 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		int num3 = playerY_fixed >> 8;
		int num4 = 200;
		int i;
		for (i = 0; i < num4; i++)
		{
			num3 -= 8;
			playerY_fixed = num3 << 8;
			ProcessCameraScrollDuringSpiderScan();
			if (num3 <= -(num2 * 16))
			{
				AppendSimDebug($"[SPIDER_UP] Hit top boundary at Y={num3}");
				playerY_fixed = 0;
				break;
			}
			var (flag, num5) = BgCollU_Spider(playerX_px, num3 + num, width, height, num2);
			if (flag)
			{
				num3 += num5;
				AppendSimDebug($"[SPIDER_UP] Found ceiling, eject={num5}, final scanY={num3}");
				playerY_fixed = num3 << 8;
				break;
			}
		}
		if (i >= num4)
		{
			AppendSimDebug($"[SPIDER_UP] Max iterations reached, stopping at Y={num3}");
			playerY_fixed = num3 << 8;
		}
	}

	private void SpiderDownWait_Fresh()
	{
		bool flag = miniMode;
		int width = (flag ? 8 : 15);
		int num = (flag ? 7 : 15);
		int num2 = (flag ? (16 - num >> 1) : 0);
		int playerX_px = playerX_fixed >> 8;
		int num3 = ((hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
		int num4 = playerY_fixed >> 8;
		int num5 = (mapHeight - num3) * 16 - num;
		int num6 = 200;
		int i;
		for (i = 0; i < num6; i++)
		{
			num4 += 8;
			playerY_fixed = num4 << 8;
			ProcessCameraScrollDuringSpiderScan();
			if (num4 >= num5)
			{
				AppendSimDebug($"[SPIDER_DOWN] Hit bottom boundary at Y={num4}");
				playerY_fixed = num5 << 8;
				break;
			}
			var (flag2, num7) = BgCollD_Spider(playerX_px, num4 + num2, width, num, num3);
			if (flag2)
			{
				num4 -= num7;
				AppendSimDebug($"[SPIDER_DOWN] Found floor, eject={num7}, final scanY={num4}");
				playerY_fixed = num4 << 8;
				break;
			}
		}
		if (i >= num6)
		{
			AppendSimDebug($"[SPIDER_DOWN] Max iterations reached, stopping at Y={num4}");
			playerY_fixed = num4 << 8;
		}
	}

	private (bool collided, int ejectAmount) BgCollD_Spider(int playerX_px, int playerY_px, int width, int height, int groundRowsToReserve, bool useEjectProbes = false)
	{
		int num = playerY_px + height;
		int num2 = num / 16 + groundRowsToReserve;
		if (num2 >= mapHeight)
		{
			int num3 = (mapHeight - groundRowsToReserve) * 16;
			int item = num - num3;
			return (collided: true, ejectAmount: item);
		}
		if (num2 < 0)
		{
			return (collided: false, ejectAmount: 0);
		}
		int[] array = ((!useEjectProbes) ? new int[2]
		{
			playerX_px + 3,
			playerX_px + width - 3
		} : new int[3]
		{
			playerX_px,
			playerX_px + (width >> 1),
			playerX_px + width
		});
		int[] array2 = array;
		foreach (int num4 in array2)
		{
			int num5 = num4 / 16;
			if (num5 < 0 || num5 >= mapWidth)
			{
				continue;
			}
			int num6 = num2 * mapWidth + num5;
			if (num6 < 0 || num6 >= tiles.Length)
			{
				continue;
			}
			int tid = tiles[num6];
			MetatileCollision col = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid));
			if (IsSolidCollisionForSpider(col))
			{
				(int left, int top, int right, int bottom) collisionBounds = SharedPhysics.GetCollisionBounds(col);
				int item2 = collisionBounds.left;
				int item3 = collisionBounds.top;
				int item4 = collisionBounds.right;
				int item5 = collisionBounds.bottom;
				int num7 = (num2 - groundRowsToReserve) * 16;
				int num8 = num7 + item3;
				if (num >= num8)
				{
					int item6 = num - num8;
					return (collided: true, ejectAmount: item6);
				}
			}
		}
		return (collided: false, ejectAmount: 0);
	}

	private (bool collided, int ejectAmount) BgCollU_Spider(int playerX_px, int playerY_px, int width, int height, int groundRowsToReserve, bool useEjectProbes = false)
	{
		int num = playerY_px / 16 + groundRowsToReserve;
		if (num < 0)
		{
			int item = -playerY_px;
			return (collided: true, ejectAmount: item);
		}
		if (num >= mapHeight)
		{
			return (collided: false, ejectAmount: 0);
		}
		int[] array = ((!useEjectProbes) ? new int[2]
		{
			playerX_px + 3,
			playerX_px + width - 3
		} : new int[3]
		{
			playerX_px,
			playerX_px + (width >> 1),
			playerX_px + width
		});
		int[] array2 = array;
		foreach (int num2 in array2)
		{
			int num3 = num2 / 16;
			if (num3 < 0 || num3 >= mapWidth)
			{
				continue;
			}
			int num4 = num * mapWidth + num3;
			if (num4 >= 0 && num4 < tiles.Length)
			{
				int tid = tiles[num4];
				MetatileCollision col = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tid));
				if (IsSolidCollisionForSpider(col))
				{
					(int left, int top, int right, int bottom) collisionBounds = SharedPhysics.GetCollisionBounds(col);
					int item2 = collisionBounds.left;
					int item3 = collisionBounds.top;
					int item4 = collisionBounds.right;
					int item5 = collisionBounds.bottom;
					int num5 = (num - groundRowsToReserve) * 16;
					int num6 = num5 + item5;
					int item6 = num6 - playerY_px;
					return (collided: true, ejectAmount: item6);
				}
			}
		}
		return (collided: false, ejectAmount: 0);
	}

	private bool IsSolidCollisionForSpider(MetatileCollision collision)
	{
		return collision != MetatileCollision.COL_NONE && !SharedPhysics.IsDeathCollision(collision);
	}

	private void ProcessCameraScrollDuringSpiderScan()
	{
		try
		{
			if (physicsEnabled && jumpedOnce)
			{
				int num = (playerY_fixed >> 8) + playerVisualHeight / 2 - (cameraY_fixed >> 8);
				int num2 = 80;
				int num3 = 160;
				if (num <= num2)
				{
					int val = num2 - num;
					int num4 = Math.Min(val, cameraY_fixed >> 8);
					cameraY_fixed -= num4 << 8;
					if (cameraY_fixed < 0)
					{
						cameraY_fixed = 0;
					}
				}
				else if (num >= num3)
				{
					int val2 = num - num3;
					int num5 = Math.Max(0, (mapHeight - 15) * 16) << 8;
					int val3 = num5 - cameraY_fixed >> 8;
					int num6 = Math.Min(val2, val3);
					cameraY_fixed += num6 << 8;
					if (cameraY_fixed > num5)
					{
						cameraY_fixed = num5;
					}
				}
			}
		}
		catch
		{
		}
		if (pfSimulating)
		{
			return;
		}
		try
		{
			int num7 = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num8 = playerY_fixed >> 8;
			if (currplayer_mini != 0)
			{
				num8 += 4;
			}
			int num9 = num8 + playerVisualHeight / 2;
			if (currplayer == 0)
			{
				recordedPlayerPath.Add((num7, num9));
			}
			else if (dual)
			{
				RecordP2PathPoint(num7, num9);
			}
		}
		catch
		{
		}
	}

	private void CheckTeleportPortals()
	{
		try
		{
			int num = (playerX_fixed >> 8) + 1;
			int num2 = playerY_fixed >> 8;
			int num3 = ((currplayer_mini != 0) ? 8 : 15);
			int num4 = ((currplayer_mini != 0) ? 7 : 15);
			num2 += GetMiniSpriteOffsetY();
			int num5 = num;
			int num6 = num + num3 - 1;
			int num7 = num2;
			int num8 = num2 + num4 - 1;
			AppendSimDebug($"[TELEPORT_PORTAL] Checking collision: player ({num5},{num7})-({num6},{num8}) size={num3}x{num4}");
			for (int i = 0; i < nonEmptySpriteIndices.Length; i++)
			{
				int num9 = nonEmptySpriteIndices[i];
				int num10 = sprites[num9];
				bool flag = num10 == 78;
				bool flag2 = num10 == 102 || num10 == 104 || num10 == 117 || num10 == 119;
				if ((!flag && !flag2) || processedTeleportPortals.Contains(num9) || !SpriteIntersectsPlayer(num9, num10, num5, num6, num7, num8))
				{
					continue;
				}
				AppendSimDebug($"[TELEPORT_PORTAL] Colliding with entrance portal 0x{num10:X2} at idx={num9}");
				bool flag3 = false;
				int num11 = 0;
				int num12 = cameraX_fixed >> 8;
				int num13 = num12 + 256;
				for (int j = 0; j < sprites.Length; j++)
				{
					int num14 = sprites[j];
					bool flag4 = false;
					bool flag5 = false;
					bool flag6 = false;
					if (flag && num14 == 79)
					{
						flag4 = true;
					}
					else if (flag2)
					{
						if (num14 == 103 || num14 == 118)
						{
							flag4 = true;
							flag5 = true;
						}
						else if (num14 == 105 || num14 == 120)
						{
							flag4 = true;
							flag6 = true;
						}
					}
					if (!flag4)
					{
						continue;
					}
					int num15 = j % mapWidth;
					int num16 = j / mapWidth;
					int num17 = num15 * 16;
					int num18 = num16 * 16;
					if (spriteAnchors != null && spriteAnchors.TryGetValue(j, out (int, int) value))
					{
						num17 = value.Item1 * 16;
						num18 = value.Item2 * 16;
					}
					if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(j, out (int, int) value2))
					{
						num18 += value2.Item2;
					}
					if (num17 >= num12 && num17 < num13)
					{
						if (flag)
						{
							num11 = num18 + 16;
						}
						else if (flag5)
						{
							num11 = num18;
						}
						else if (flag6)
						{
							num11 = num18;
						}
						flag3 = true;
						AppendSimDebug($"[TELEPORT_PORTAL] Found visible exit 0x{num14:X2} at ({num17}, {num18}), exit Y={num11}");
					}
				}
				if (flag3)
				{
					num11 -= 48;
					playerY_fixed = (num11 << 8) | (playerY_fixed & 0xFF);
					if (nocamlockforced)
					{
						int val = Math.Max(0, (mapHeight - 15) * 16) << 8;
						targetCameraY_fixed = (cameraY_fixed = Math.Max(0, Math.Min(val, playerY_fixed - 30720)));
					}
					AppendSimDebug($"[TELEPORT_PORTAL] Teleported to Y={num11} (camera Y={cameraY_fixed >> 8}) camLock={!nocamlockforced}");
					processedTeleportPortals.Add(num9);
				}
				else
				{
					AppendSimDebug("[TELEPORT_PORTAL] WARNING: No exit portal visible on screen!");
				}
				break;
			}
		}
		catch (Exception ex)
		{
			AppendSimDebug("[TELEPORT_PORTAL] ERROR: " + ex.Message);
		}
	}

	private void UfoPhysics_Fresh()
	{
		bool flag = IsXDownAsync() || keyXHeld;
		int num = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
		bool flag2 = num > 0;
		bool gravityInverted = currplayer_gravity != 0;
		int playerX_px = (playerX_fixed >> 8) + 1;
		int num2 = playerY_fixed >> 8;
		int playerW = ((currplayer_mini != 0) ? 8 : 15);
		int playerH = ((currplayer_mini != 0) ? 7 : 15);
		if (currplayer_mini != 0)
		{
			num2 += 4;
		}
		int scrollX_px = 0;
		int num3 = playerVelY_fixed;
		if (UpdateOrbSystem(3, playerX_px, num2, playerW, playerH, scrollX_px, flag2, flag, gravityInverted, currplayer_mini != 0, ref num3).activated)
		{
			playerVelY_fixed = num3;
			AppendSimDebug($"[UFO] Orb activated! New velY={playerVelY_fixed}");
			if (flag2)
			{
				Interlocked.Exchange(ref keyXPressedCount, 0);
			}
		}
		if (!flag)
		{
			ClearOrbBuffer();
		}
		int table_idx = (miniMode ? 4 : 0);
		int num4 = ((!gravityFlipped) ? 1 : (-1));
		tmpfallspeed = GameModePhysics.UFO_MAX_FALLSPEED(table_idx) * num4;
		tmpgravity = GameModePhysics.UFO_GRAVITY(table_idx) * num4;
		AppendSimDebug($"[UFO] table_idx={currplayer_table_idx}, gravity={tmpgravity}, fallspeed={tmpfallspeed}");
		CommonGravityRoutine_Fresh();
		UfoShipEject_Fresh();
		int num5 = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
		bool flag3 = num5 > 0;
		AppendSimDebug($"[UFO] Input: pressCount={num5}, pressedJump={flag3}, ufoOrbed={ufoOrbed}");
		if (flag3 && !ufoOrbed)
		{
			Interlocked.Exchange(ref keyXPressedCount, 0);
			int table_idx2 = (miniMode ? 4 : 0);
			int num6 = ((!gravityFlipped) ? 1 : (-1));
			int value = (playerVelY_fixed = GameModePhysics.UFO_JUMP_VEL(table_idx2) * num6);
			AppendSimDebug($"[UFO] JUMP! table_idx={currplayer_table_idx}, jumpVel={value}, velY={playerVelY_fixed}");
		}
		ufoOrbed = false;
		UpdateSlopeCounters_Fresh();
		if (pfSimulating)
		{
			return;
		}
		try
		{
			int num7 = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num8 = playerY_fixed >> 8;
			if (miniMode)
			{
				num8 += 4;
			}
			int num9 = num8 + playerVisualHeight / 2;
			if (currplayer == 0)
			{
				recordedPlayerPath.Add((num7, num9));
			}
			else if (dual)
			{
				RecordP2PathPoint(num7, num9);
			}
		}
		catch
		{
		}
	}

	private void WavePhysics_Fresh()
	{
		if (deathTriggered || paused)
		{
			return;
		}
		AppendSimDebug($"[WAVE_PHYS] START X={playerX_fixed >> 8} Y={playerY_fixed >> 8} velY=0x{playerVelY_fixed:X} wasZeroed={wasZeroedByCollisionLastFrame} dblocked={dblocked} mini={miniMode} grav={gravityFlipped}");
		bool flag = IsXDownAsync() || keyXHeld;
		int num = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
		bool flag2 = num > 0;
		bool gravityInverted = currplayer_gravity != 0;
		int playerX_px = (playerX_fixed >> 8) + 1;
		int num2 = playerY_fixed >> 8;
		int playerW = ((currplayer_mini != 0) ? 8 : 15);
		int playerH = ((currplayer_mini != 0) ? 7 : 15);
		if (currplayer_mini != 0)
		{
			num2 += 4;
		}
		int scrollX_px = 0;
		int num3 = playerVelY_fixed;
		if (UpdateOrbSystem(6, playerX_px, num2, playerW, playerH, scrollX_px, flag2, flag, gravityInverted, currplayer_mini != 0, ref num3).activated)
		{
			playerVelY_fixed = num3;
			AppendSimDebug($"[WAVE] Orb activated! New velY={playerVelY_fixed}");
			if (flag2)
			{
				Interlocked.Exchange(ref keyXPressedCount, 0);
			}
		}
		if (!flag)
		{
			ClearOrbBuffer();
		}
		switch (0)
		{
		case 0:
		{
			if (!miniMode)
			{
				playerVelY_fixed = (gravityFlipped ? (-playerVelX_fixed) : playerVelX_fixed);
			}
			else
			{
				playerVelY_fixed = (gravityFlipped ? (-(playerVelX_fixed << 1)) : (playerVelX_fixed << 1));
			}
			wasZeroedByCollisionLastFrame = false;
			bool flag3 = IsXDownAsync() || keyXHeld;
			if (flag3)
			{
				playerVelY_fixed = -playerVelY_fixed;
			}
			AppendSimDebug($"[WAVE_PHYS] postCalc velY=0x{playerVelY_fixed:X} hold={flag3} slopeF={currplayer_slope_frames} slopeW={currplayer_was_on_slope_counter}");
			if (dblocked || (currplayer_slope_frames == 0 && currplayer_was_on_slope_counter == 0))
			{
				if (isFullSpeed)
				{
					playerY_fixed += playerVelY_fixed;
				}
				else
				{
					playerY_fixed += (int)Math.Round((double)playerVelY_fixed * simTimeScale);
				}
			}
			else
			{
				AppendSimDebug($"[WAVE_PHYS] SLOPE_FREEZE velY zeroed (slopeF={currplayer_slope_frames} slopeW={currplayer_was_on_slope_counter})");
				playerVelY_fixed = 0;
			}
			AppendSimDebug($"[WAVE_PHYS] postMove Y={playerY_fixed >> 8} Y_fixed=0x{playerY_fixed:X}");
			break;
		}
		case 1:
			playerVelY_fixed = 1;
			break;
		case 2:
			playerVelY_fixed = -playerVelX_fixed;
			if (isFullSpeed)
			{
				playerY_fixed += playerVelY_fixed;
			}
			else
			{
				playerY_fixed += (int)Math.Round((double)playerVelY_fixed * simTimeScale);
			}
			break;
		case 3:
			playerVelY_fixed = playerVelX_fixed;
			if (isFullSpeed)
			{
				playerY_fixed += playerVelY_fixed;
			}
			else
			{
				playerY_fixed += (int)Math.Round((double)playerVelY_fixed * simTimeScale);
			}
			break;
		case 4:
			playerVelY_fixed = playerVelX_fixed;
			if (isFullSpeed)
			{
				playerY_fixed -= playerVelY_fixed;
			}
			else
			{
				playerY_fixed -= (int)Math.Round((double)playerVelY_fixed * simTimeScale);
			}
			break;
		case 5:
			playerVelY_fixed = playerVelX_fixed;
			if (isFullSpeed)
			{
				playerY_fixed += playerVelY_fixed;
			}
			else
			{
				playerY_fixed += (int)Math.Round((double)playerVelY_fixed * simTimeScale);
			}
			break;
		}
		int offsetY = (playerY_fixed >> 8) + ((playerVelY_fixed > 0) ? (-2) : 2);
		if (!deathTriggered)
		{
			WaveEject_Fresh(offsetY);
			UpdateSlopeCounters_Fresh();
		}
		try
		{
			int num4 = (playerX_fixed >> 8) + playerVisualWidth / 2;
			int num5 = (playerY_fixed >> 8) + playerVisualHeight / 2;
			if (miniMode)
			{
				num5 += 4;
			}
			List<(int, int)> list = ((currplayer == 0) ? recordedPlayerPath : (dual ? recordedPlayer2Path : null));
			if (list != null && (list.Count == 0 || Math.Abs(num4 - list.Last().Item1) >= 4))
			{
				list.Add((num4, num5));
			}
		}
		catch
		{
		}
	}

	private void WaveEject_Fresh(int offsetY)
	{
		bool flag = miniMode;
		bool flag2 = gravityFlipped;
		UpdateSlopeCounters();
		if (playerVelY_fixed >= 0)
		{
			if (bg_coll_D_slopes())
			{
				AppendSimDebug($"[WAVE_EJECT] slopeHit=true dblocked={dblocked} eject_D={eject_D}");
				if (dblocked)
				{
					if (eject_D > 0)
					{
						int num = playerY_fixed >> 8;
						num -= eject_D;
						playerY_fixed = num << 8;
					}
					playerVelY_fixed = 0;
					wasZeroedByCollisionLastFrame = true;
					AppendSimDebug($"[WAVE_EJECT] slope eject Y={playerY_fixed >> 8}");
					return;
				}
				if (!MainWindow.Option_NoDeath)
				{
					AppendSimDebug($"[WAVE_DEATH] slope death (dblocked=false) X={playerX_fixed >> 8} Y={playerY_fixed >> 8}");
					deathTriggered = true;
					if (pfSimulating)
					{
						return;
					}
					paused = true;
					StopMusicAsync();
					try
					{
						base.Dispatcher.BeginInvoke((Action)delegate
						{
							try
							{
								PauseOverlay.Visibility = Visibility.Collapsed;
							}
							catch
							{
							}
							if (base.Owner is MainWindow mainWindow)
							{
								try
								{
									mainWindow.PauseSimulatorPlayback();
								}
								catch
								{
								}
							}
						});
						return;
					}
					catch
					{
						return;
					}
				}
			}
		}
		else if (bg_coll_U_slopes())
		{
			AppendSimDebug($"[WAVE_EJECT] U_slopeHit=true dblocked={dblocked} eject_U={eject_U}");
			if (dblocked)
			{
				int num2 = playerY_fixed >> 8;
				num2 -= eject_U;
				playerY_fixed = num2 << 8;
				playerVelY_fixed = 0;
				wasZeroedByCollisionLastFrame = true;
				AppendSimDebug($"[WAVE_EJECT] U slope eject Y={playerY_fixed >> 8}");
				return;
			}
			if (!MainWindow.Option_NoDeath)
			{
				AppendSimDebug($"[WAVE_DEATH] U slope death (dblocked=false) X={playerX_fixed >> 8} Y={playerY_fixed >> 8}");
				deathTriggered = true;
				if (pfSimulating)
				{
					return;
				}
				paused = true;
				StopMusicAsync();
				try
				{
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						try
						{
							PauseOverlay.Visibility = Visibility.Collapsed;
						}
						catch
						{
						}
						if (base.Owner is MainWindow mainWindow)
						{
							try
							{
								mainWindow.PauseSimulatorPlayback();
							}
							catch
							{
							}
						}
					});
					return;
				}
				catch
				{
					return;
				}
			}
		}
		Generic_x = (playerX_fixed >> 8) + 4;
		int num3 = ((currplayer_mini == 0) ? 4 : 0);
		Generic_y = (playerY_fixed >> 8) + num3;
		Generic_width = 8;
		Generic_height = 8;
		AppendSimDebug($"[WAVE_EJECT] Generic=({Generic_x},{Generic_y}) {Generic_width}x{Generic_height} velY=0x{playerVelY_fixed:X} miniBaseAdj={num3}");
		if (playerVelY_fixed < 0)
		{
			if (!wave_coll_U())
			{
				return;
			}
			MetatileCollision metatileCollision = (MetatileCollision)collision;
			if (metatileCollision == MetatileCollision.COL_FLOOR_CEIL)
			{
				dblocked = true;
			}
			AppendSimDebug($"[WAVE_EJECT] coll_U hit tile={metatileCollision} dblocked={dblocked} eject_U={eject_U}");
			if (dblocked)
			{
				int num4 = playerY_fixed >> 8;
				num4 -= eject_U;
				playerY_fixed = num4 << 8;
				playerVelY_fixed = 0;
				wasZeroedByCollisionLastFrame = true;
				AppendSimDebug($"[WAVE_EJECT] UP eject Y={playerY_fixed >> 8}");
			}
			else
			{
				if (MainWindow.Option_NoDeath)
				{
					return;
				}
				AppendSimDebug($"[WAVE_DEATH] UP non-walkable tile={metatileCollision} X={playerX_fixed >> 8} Y={playerY_fixed >> 8} probe=({Generic_x},{Generic_y - 1})");
				deathTriggered = true;
				if (pfSimulating)
				{
					return;
				}
				paused = true;
				StopMusicAsync();
				try
				{
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						try
						{
							PauseOverlay.Visibility = Visibility.Collapsed;
						}
						catch
						{
						}
						if (base.Owner is MainWindow mainWindow)
						{
							try
							{
								mainWindow.PauseSimulatorPlayback();
							}
							catch
							{
							}
						}
					});
				}
				catch
				{
				}
			}
		}
		else if (playerVelY_fixed >= 0)
		{
			if (!wave_coll_D())
			{
				return;
			}
			MetatileCollision metatileCollision2 = (MetatileCollision)collision;
			if (metatileCollision2 == MetatileCollision.COL_FLOOR_CEIL)
			{
				dblocked = true;
			}
			AppendSimDebug($"[WAVE_EJECT] coll_D hit tile={metatileCollision2} dblocked={dblocked} eject_D={eject_D}");
			if (dblocked)
			{
				int num5 = playerY_fixed >> 8;
				num5 -= eject_D;
				playerY_fixed = num5 << 8;
				playerVelY_fixed = 0;
				wasZeroedByCollisionLastFrame = true;
				AppendSimDebug($"[WAVE_EJECT] DOWN eject Y={playerY_fixed >> 8}");
			}
			else
			{
				if (MainWindow.Option_NoDeath)
				{
					return;
				}
				AppendSimDebug($"[WAVE_DEATH] DOWN non-walkable tile={metatileCollision2} X={playerX_fixed >> 8} Y={playerY_fixed >> 8} probe=({Generic_x},{Generic_y + Generic_height})");
				deathTriggered = true;
				if (pfSimulating)
				{
					return;
				}
				paused = true;
				StopMusicAsync();
				try
				{
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						try
						{
							PauseOverlay.Visibility = Visibility.Collapsed;
						}
						catch
						{
						}
						if (base.Owner is MainWindow mainWindow)
						{
							try
							{
								mainWindow.PauseSimulatorPlayback();
							}
							catch
							{
							}
						}
					});
				}
				catch
				{
				}
			}
		}
		else
		{
			AppendSimDebug("[WAVE_EJECT] velY==0 skip collision");
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/simulatorwindow.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			SettingsPanel = (StackPanel)target;
			break;
		case 2:
			GameModeComboBox = (ComboBox)target;
			GameModeComboBox.SelectionChanged += GameModeComboBox_SelectionChanged;
			break;
		case 3:
			RestartButton = (Button)target;
			RestartButton.Click += RestartButton_Click;
			break;
		case 4:
			MiniCheckBox = (CheckBox)target;
			MiniCheckBox.Checked += MiniCheckBox_Changed;
			MiniCheckBox.Unchecked += MiniCheckBox_Changed;
			break;
		case 5:
			InvertedCheckBox = (CheckBox)target;
			InvertedCheckBox.Checked += InvertedCheckBox_Changed;
			InvertedCheckBox.Unchecked += InvertedCheckBox_Changed;
			break;
		case 6:
			PathfinderCheckBox = (CheckBox)target;
			PathfinderCheckBox.Checked += PathfinderCheckBox_Changed;
			PathfinderCheckBox.Unchecked += PathfinderCheckBox_Changed;
			break;
		case 7:
			SpeedComboBox = (ComboBox)target;
			SpeedComboBox.SelectionChanged += SpeedComboBox_SelectionChanged;
			break;
		case 8:
			RenderCanvas = (Canvas)target;
			RenderCanvas.MouseLeftButtonDown += RenderCanvas_MouseLeftButtonDown;
			break;
		case 9:
			PauseOverlay = (Grid)target;
			PauseOverlay.MouseLeftButtonUp += PauseOverlay_MouseLeftButtonUp;
			break;
		case 10:
			PauseOverlayCircle = (Ellipse)target;
			break;
		case 11:
			PauseOverlayIcon = (StackPanel)target;
			break;
		case 12:
			PauseOverlayText = (TextBlock)target;
			break;
		case 13:
			LevelCompleteOverlay = (Grid)target;
			break;
		case 14:
			CoinDisplayPanel = (StackPanel)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}

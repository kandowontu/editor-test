using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow : Window
    {
        // Option: show yellow translucent hitboxes for sprites (non-trigger sprites only)
        public bool ShowSpriteHitboxes { get; set; } = false;

        // Sprite geometry tables (from user-provided data). Non-numeric placeholders use sensible defaults.
        private static readonly int[] sprite_heights = new int[] {
            0x34,0x34,0x34,0x34,0x34,0x12,0x12,0x10, // 00-07 (SPBH->0x10)
            0x28,0x28,0x03,0x12,0x03,0x03,0x03,0x10, // 08-0F
            0x0e,0x0e,0x0e,0x0e,0x24,0x24,0x24,0x34, // 10-17
            0x34,0x34,0x10,0x10,0x10,0x10,0x10,0x12, // 18-1F
            0x24,0x24,0x34,0x34,0x34,0x03,0x03,0x12, // 20-27
            0x12,0x12,0x10,0x10,0x10,0x10,0x10,0x10, // 28-2F (DECO->0x10)
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 30-37
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 38-3F
            0x10,0x10,0x10,0x10,0x12,0x12,0x12,0x28, // 40-47
            0x28,0x10,0x10,0x34,0x12,0x12,0x30,0x10, // 48-4F (SPBH->0x10)
            0x12,0x12,0x03,0x03,0x12,0x12,0x03,0x03, // 50-57
            0x34,0x10,0x10,0x12,0x12,0x12,0x12,0x34, // 58-5F (SPBH->0x10)
            0x34,0x34,0x34,0x34,0x34,0x02,0x10,0x10, // 60-67 (SPBH->0x10)
            0x10,0x10,0x34,0x34,0x34,0x20,0x08,0x10, // 68-6F (SPBH->0x10)
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 70-77 (SPBH->0x10)
            0x10,0x12,0x12,0x12,0x12,0x10,0x10,0x10, // 78-7F (SPBH->0x10)
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 80-87 (COLR->0x10)
            0x10,0x10,0x10,0x10,0x10,0x00,0x10,0x10, // 88-8F (SPBH->0x10)
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
            0x10,0x08,0x1B,0x10,0x10,0x0E,0x0E,0x10  // F8-FF
        };

        private static readonly int[] sprite_widths = new int[] {
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

        private static readonly int[] sprite_x_offset = new int[] {
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

        private static readonly int[] sprite_y_offset = new int[] {
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
            -0x01,-0x01,0x05,0x00,-0x01,-0x01,0x05,0x00, // 50-57
            -0x02,0x00,0x00,-0x01,-0x01,-0x01,-0x01,-0x02, // 58-5F
            -0x02,-0x02,-0x02,-0x02,-0x02,0x00,0x00,0x00, // 60-67
            0x00,0x00,-0x02,-0x02,-0x02,0x00,0x04,0x00, // 68-6F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 70-77
            0x00,-0x01,-0x01,-0x01,-0x01,0x00,0x00,0x00, // 78-7F
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

        // Pool for hitbox rectangles
        private System.Collections.Generic.List<System.Windows.Shapes.Rectangle> hitboxPool = new System.Collections.Generic.List<System.Windows.Shapes.Rectangle>();
        private int hitboxesInUse = 0;
        // Interaction line: player's center (fixed-point) where scrolling begins
        private const int INTERACTION_LINE_FIXED = 0x5000;

        // Player world X (fixed-point, 8 fractional bits)
        private int playerX_fixed = 0;

        // Visual player controls used for the player: image preferred, rectangle fallback
        private System.Windows.Controls.Image? playerImage = null;
        private System.Windows.Shapes.Rectangle? playerRect = null;
        
        // When player crosses interaction line, remember the screen pixel offset where the crossing occurred
        // so the camera can follow the player while keeping them at that screen X.
        private int interactionScreenOffset_px = -1;

        private readonly int[] tiles;
        private readonly int[] sprites;
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly ImageSource[]? tileImages;
        private ImageSource[]? tileTonedImages;
        private readonly ImageSource[]? spriteImages;
        private readonly System.Collections.Generic.Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets;
        private readonly System.Collections.Generic.Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors;
        private Color backgroundTint;
        private Color groundTint;
        private Color tileTint;
        private readonly Color playerTint;
        private readonly bool playerTintEnabled;
        private readonly bool forcePreviewMode;
        private readonly bool hideColorTriggers;
        private readonly int gridRenderShiftYPx;
        private readonly System.Collections.Generic.Dictionary<int, ImageSource?>? previewSpriteMap;
        private readonly System.Collections.Generic.Dictionary<int, ImageSource?[]>? animationFrames;
        // Tile-level animated saw frames (tinted versions) passed from MainWindow
        private ImageSource[]? sawFrame1TilesTinted;
        private ImageSource[]? sawFrame2TilesTinted;
        private ImageSource[]? smallSawFrame1TilesTinted;
        private ImageSource[]? smallSawFrame2TilesTinted;
        private ImageSource[]? largeSawFrame1TilesTinted;
        private ImageSource[]? largeSawFrame2TilesTinted;
        // Keep originals so we can re-generate tinted versions when tile tint changes
        private ImageSource[]? sawFrame1TilesOrig;
        private ImageSource[]? sawFrame2TilesOrig;
        private ImageSource[]? smallSawFrame1TilesOrig;
        private ImageSource[]? smallSawFrame2TilesOrig;
        private ImageSource[]? largeSawFrame1TilesOrig;
        private ImageSource[]? largeSawFrame2TilesOrig;

        private const int NES_W = 16; // horizontal tiles (was 15)
        private const int NES_H = 15; // vertical tiles (was 16)
        private const int TILE = 16;

        // Parallax / ground data passed from the editor so simulator can mirror preview-mode
        private ImageSource[]? parallaxImages;
        private ImageSource[]? parallaxTonedImages;
        private double parallaxX = 1.0;
        private double parallaxY = 1.0;
        private bool parallaxRepeatX = true;
        private bool parallaxRepeatY = true;
        private bool hasParallaxLayer = false;

        private ImageSource[]? groundImages;
        private ImageSource[]? groundTonedImages;
        private double groundOffsetY = 0.0;
        private bool groundRepeatX = true;
        private bool hasGroundLayer = false;
        private int groundTileRows = 0;
        // Fixed-point camera X with 8 fractional bits
        private int cameraX_fixed = 0;
        // cameraY in fixed-point (8 fractional bits)
        private int cameraY_fixed = 0; // pixels * 256

        // speed per frame (0x02C4 fixed point with 8 fractional bits)
        private const int SPEED_FIXED = 0x02C4;
        // Dynamic current speed (fixed-point, 8 fractional bits). Starts at 1x.
        private int currentSpeed_fixed = 0x02C4;

        // Speed constants (fixed-point values, 8 fractional bits)
        private const int CUBE_SPEED_X05 = 0x23B;
        private const int CUBE_SPEED_X1  = 0x02C4;
        private const int CUBE_SPEED_X2  = 0x0371;
        private const int CUBE_SPEED_X3  = 0x0429;
        private const int CUBE_SPEED_X4  = 0x051E;

        // Mapping from speed-portal sprite id -> speed value
        private readonly System.Collections.Generic.Dictionary<int, int> speedPortalMap = new System.Collections.Generic.Dictionary<int, int>
        {
            { 0x14, CUBE_SPEED_X05 },
            { 0x15, CUBE_SPEED_X1  },
            { 0x16, CUBE_SPEED_X2  },
            { 0x20, CUBE_SPEED_X3  },
            { 0x21, CUBE_SPEED_X4  }
        };

        private readonly System.Windows.Threading.DispatcherTimer timer;
        private System.Diagnostics.Stopwatch renderStopwatch = new System.Diagnostics.Stopwatch();
        private double accumulatedSeconds = 0.0;

        private int animationFrame = 0;
        private System.Collections.Generic.Dictionary<int, int> spriteFrameOffsets = new System.Collections.Generic.Dictionary<int, int>();
        private Random spriteAnimationRandom = new Random();

        // Tile-layer cache and sprite pooling for performance
        private RenderTargetBitmap? tileLayerCache = null;
        private int cachedStartTileX = int.MinValue;
        private int cachedStartTileY = int.MinValue;
        private System.Windows.Controls.Image? tileLayerImage = null;
        private System.Windows.Shapes.Rectangle? bgRectPersistent = null;
        private System.Windows.Shapes.Rectangle? groundRectPersistent = null;
        private System.Collections.Generic.List<System.Windows.Controls.Image> spritePool = new System.Collections.Generic.List<System.Windows.Controls.Image>();
        private int spritesInUse = 0;
        private bool lastCacheHadAnimatedTiles = false;
        private int lastCacheAnimationFrame = -1;

        private readonly System.Collections.Generic.HashSet<int> decorationSpriteIds = new System.Collections.Generic.HashSet<int> { 0x36, 0x32, 0x33, 0x34, 0x35, 0x37, 0x2C, 0x3C, 0x2D, 0x3D, 0x2E, 0x2F, 0x30, 0x31, 0x38, 0x39, 0x3E, 0x3F, 0x2B, 0x3B, 0x2A, 0x3A, 0x49, 0x4A };

        // Cache tinted decoration sprites keyed by (spriteId<<32)|ARGB
        private readonly System.Collections.Generic.Dictionary<long, ImageSource?> tintedSpriteCache = new System.Collections.Generic.Dictionary<long, ImageSource?>();

        // Cache for ground-tinted tile images keyed by (tileIndex<<32)|ARGB
        private readonly System.Collections.Generic.Dictionary<long, ImageSource?> groundTintedTileCache = new System.Collections.Generic.Dictionary<long, ImageSource?>();

        // Track color-trigger anchors that have already been processed (so we don't resample every frame)
        private System.Collections.Generic.HashSet<int> processedColorTriggers = new System.Collections.Generic.HashSet<int>();

        // Lightweight one-time debug logging sets to avoid spamming output repeatedly
        private System.Collections.Generic.HashSet<int> decoLogged = new System.Collections.Generic.HashSet<int>();
        private System.Collections.Generic.HashSet<int> triggerLogged = new System.Collections.Generic.HashSet<int>();
        // Track last selected decoration frame so we can log when it actually changes
        private System.Collections.Generic.Dictionary<int, int> decoLastSelectedFrame = new System.Collections.Generic.Dictionary<int, int>();
        private bool enableSimulatorDebugLogging = true; // set true to capture helpful messages during diagnosis

        // Append a timestamped simulator debug message to the temp log file.
        private void WriteTempLog(string message)
        {
            if (!enableSimulatorDebugLogging) return;
            try
            {
                string temp = System.IO.Path.GetTempPath();
                string fn = System.IO.Path.Combine(temp, "FamidashSimulator.log");
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
                System.IO.File.AppendAllText(fn, line);
                try { System.Diagnostics.Debug.WriteLine(message); } catch { }
            }
            catch { }
        }

        private bool upHeld = false;
        private bool downHeld = false;
        private bool tabHeld = false;
        // Pause state controlled by ESC
        private bool paused = false;
        // Multiplier applied while Tab (or Shift+Tab / Ctrl+Shift+Tab) is held.
        // Default 1 (no extra multiplier). While Tab is down this becomes 2/4/8 per modifiers.
        private int tabSpeedMultiplier = 1;

        // (debug overlay removed)

        public SimulatorWindow(
            int[] tiles,
            int[] sprites,
            int mapWidth,
            int mapHeight,
            ImageSource[]? tileImages,
            ImageSource[]? tileTonedImages,
            ImageSource[]? spriteImages,
            System.Collections.Generic.Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets,
            System.Collections.Generic.Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors,
            Color backgroundTint,
            Color groundTint,
            Color tileTint,
            Color playerTint,
            bool playerTintEnabled,
            int gridRenderShiftYPx
            , bool forcePreviewMode = true,
            bool hideColorTriggers = false,
            System.Collections.Generic.Dictionary<int, ImageSource?>? previewSpriteMap = null,
            System.Collections.Generic.Dictionary<int, ImageSource?[]>? animationFrames = null,
            ImageSource[]? sawFrame1TilesTinted = null,
            ImageSource[]? sawFrame2TilesTinted = null,
            ImageSource[]? smallSawFrame1TilesTinted = null,
            ImageSource[]? smallSawFrame2TilesTinted = null,
            ImageSource[]? largeSawFrame1TilesTinted = null,
            ImageSource[]? largeSawFrame2TilesTinted = null
            ,
            ImageSource[]? parallaxImages = null,
            ImageSource[]? parallaxTonedImages = null,
            double parallaxX = 1.0,
            double parallaxY = 1.0,
            bool parallaxRepeatX = true,
            bool parallaxRepeatY = true,
            bool hasParallaxLayer = false,
            ImageSource[]? groundImages = null,
            ImageSource[]? groundTonedImages = null,
            double groundOffsetY = 0.0,
            bool groundRepeatX = true,
            bool hasGroundLayer = false,
            int groundTileRows = 0
            )
        {
            InitializeComponent();
            this.tiles = tiles.ToArray();
            this.sprites = sprites.ToArray();
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.tileImages = tileImages;
            this.tileTonedImages = tileTonedImages;
            this.spriteImages = spriteImages;
            this.spritePixelOffsets = new System.Collections.Generic.Dictionary<int, (int, int)>(spritePixelOffsets);
            this.spriteAnchors = new System.Collections.Generic.Dictionary<int, (int, int)>(spriteAnchors);
            // Store saw frame originals and initial tinted copies
            this.sawFrame1TilesOrig = sawFrame1TilesTinted != null ? (ImageSource[])sawFrame1TilesTinted.Clone() : null;
            this.sawFrame2TilesOrig = sawFrame2TilesTinted != null ? (ImageSource[])sawFrame2TilesTinted.Clone() : null;
            this.smallSawFrame1TilesOrig = smallSawFrame1TilesTinted != null ? (ImageSource[])smallSawFrame1TilesTinted.Clone() : null;
            this.smallSawFrame2TilesOrig = smallSawFrame2TilesTinted != null ? (ImageSource[])smallSawFrame2TilesTinted.Clone() : null;
            this.largeSawFrame1TilesOrig = largeSawFrame1TilesTinted != null ? (ImageSource[])largeSawFrame1TilesTinted.Clone() : null;
            this.largeSawFrame2TilesOrig = largeSawFrame2TilesTinted != null ? (ImageSource[])largeSawFrame2TilesTinted.Clone() : null;
            // Initialize tinted copies based on provided tileTint
            this.sawFrame1TilesTinted = CreateHslShiftedImages(this.sawFrame1TilesOrig, tileTint);
            this.sawFrame2TilesTinted = CreateHslShiftedImages(this.sawFrame2TilesOrig, tileTint);
            this.smallSawFrame1TilesTinted = CreateHslShiftedImages(this.smallSawFrame1TilesOrig, tileTint);
            this.smallSawFrame2TilesTinted = CreateHslShiftedImages(this.smallSawFrame2TilesOrig, tileTint);
            this.largeSawFrame1TilesTinted = CreateHslShiftedImages(this.largeSawFrame1TilesOrig, tileTint);
            this.largeSawFrame2TilesTinted = CreateHslShiftedImages(this.largeSawFrame2TilesOrig, tileTint);
            // Simulator-specific tweak: shift sprite 0x2B and 0x2C up 8 pixels to match editor preview
            try
            {
                const int SPRITE_ID_SHIFT_A = 0x2B;
                const int SPRITE_ID_SHIFT_B = 0x2C;
                if (this.spritePixelOffsets.ContainsKey(SPRITE_ID_SHIFT_A))
                {
                    var prev = this.spritePixelOffsets[SPRITE_ID_SHIFT_A];
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_A] = (prev.Item1, prev.Item2 - 8);
                }
                else
                {
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_A] = (0, -8);
                }

                if (this.spritePixelOffsets.ContainsKey(SPRITE_ID_SHIFT_B))
                {
                    var prev = this.spritePixelOffsets[SPRITE_ID_SHIFT_B];
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_B] = (prev.Item1, prev.Item2 - 8);
                }
                else
                {
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_B] = (0, -8);
                }
            }
            catch { }
            this.backgroundTint = backgroundTint;
            this.groundTint = groundTint;
            this.tileTint = tileTint;
            this.playerTint = playerTint;
            this.playerTintEnabled = playerTintEnabled;
            this.gridRenderShiftYPx = gridRenderShiftYPx;
            this.forcePreviewMode = forcePreviewMode;
            this.hideColorTriggers = hideColorTriggers;
            this.previewSpriteMap = previewSpriteMap ?? new System.Collections.Generic.Dictionary<int, ImageSource?>();
            this.animationFrames = animationFrames ?? new System.Collections.Generic.Dictionary<int, ImageSource?[]>();
            this.sawFrame1TilesTinted = sawFrame1TilesTinted;
            this.sawFrame2TilesTinted = sawFrame2TilesTinted;
            this.smallSawFrame1TilesTinted = smallSawFrame1TilesTinted;
            this.smallSawFrame2TilesTinted = smallSawFrame2TilesTinted;
            this.largeSawFrame1TilesTinted = largeSawFrame1TilesTinted;
            this.largeSawFrame2TilesTinted = largeSawFrame2TilesTinted;

            // parallax / ground
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

            // Ensure decoration sprites pulse even when editor didn't provide animation frames.
            try
            {
                // Use the simulator's effective animationFrames / previewSpriteMap so
                // this works even when the caller passed null and we created defaults.
                if (this.animationFrames != null && this.previewSpriteMap != null)
                {
                    foreach (var id in decorationSpriteIds)
                    {
                        if (!this.animationFrames.ContainsKey(id))
                        {
                            if (this.previewSpriteMap.TryGetValue(id, out var pimg) && pimg != null)
                            {
                                var frames = CreateTwoFramePulse(pimg);
                                if (frames != null) this.animationFrames[id] = frames;
                            }
                        }
                    }
                }
            }
            catch { }

            // (debug reporting removed)

            // Clamp initial cameraY so visible region fits (fixed-point)
            int maxY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            // Start the simulator from the bottom of the map by default
            cameraY_fixed = maxY_fixed;

            // Setup a high-precision render loop using CompositionTarget and a stopwatch
            timer = new System.Windows.Threading.DispatcherTimer(DispatcherPriority.Render);
            renderStopwatch.Start();
            System.Windows.Media.CompositionTarget.Rendering += CompositionTarget_Rendering;

            this.KeyDown += SimulatorWindow_KeyDown;
            this.KeyUp += SimulatorWindow_KeyUp;
            this.Closed += (s, e) =>
            {
                try { System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_Rendering; } catch { }
                try { timer.Stop(); } catch { }
            };

            RenderCanvas.Width = NES_W * TILE;
            RenderCanvas.Height = NES_H * TILE;
            // Keep nearest-neighbor sampling for bitmaps, but avoid forcing layout rounding/snapping
            try
            {
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(RenderCanvas, BitmapScalingMode.NearestNeighbor);
            }
            catch { }

            // Ensure the window receives keyboard input for panning

            // Create player visual: try to load `cube.png` from repo root, fall back to magenta rectangle
            try
            {
                // create image control and add to canvas
                playerImage = new System.Windows.Controls.Image { Stretch = Stretch.None };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(playerImage, BitmapScalingMode.NearestNeighbor);
                System.Windows.Controls.Canvas.SetZIndex(playerImage, 1000);
                RenderCanvas.Children.Add(playerImage);

                // Resolve cube.png relative to executable directory (project root is four levels up from bin)
                try
                {
                    bool loaded = false;
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                    string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..\\..\\..\\..\\cube.png"));
                    if (System.IO.File.Exists(candidate))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(candidate);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        playerImage.Source = bi;
                        playerImage.Width = bi.PixelWidth;
                        playerImage.Height = bi.PixelHeight;
                        loaded = true;
                    }

                    // If file path didn't work, attempt to load as an embedded resource from the executing assembly.
                    if (!loaded)
                    {
                        try
                        {
                            var asm = System.Reflection.Assembly.GetExecutingAssembly();
                            var names = asm.GetManifestResourceNames();
                            string? found = names.FirstOrDefault(n => n.EndsWith("cube.png", StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrEmpty(found))
                            {
                                using (var s = asm.GetManifestResourceStream(found))
                                {
                                    if (s != null)
                                    {
                                        var bi = new BitmapImage();
                                        bi.BeginInit();
                                        bi.CacheOption = BitmapCacheOption.OnLoad;
                                        bi.StreamSource = s;
                                        bi.EndInit();
                                        bi.Freeze();
                                        playerImage.Source = bi;
                                        playerImage.Width = bi.PixelWidth;
                                        playerImage.Height = bi.PixelHeight;
                                        loaded = true;
                                    }
                                }
                            }
                        }
                        catch { /* ignore embedded load errors */ }
                    }

                    if (!loaded)
                    {
                        // fallback rectangle if image not found
                        playerRect = new System.Windows.Shapes.Rectangle { Width = TILE, Height = TILE, Fill = new SolidColorBrush(Colors.Magenta) };
                        System.Windows.Controls.Canvas.SetZIndex(playerRect, 1000);
                        RenderCanvas.Children.Add(playerRect);
                        playerRect.Visibility = Visibility.Visible;
                        // hide the image control if it's unused
                        playerImage.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        playerImage.Visibility = Visibility.Visible;
                        if (playerRect != null) playerRect.Visibility = Visibility.Collapsed;
                    }
                }
                catch
                {
                    // image load failed; use rectangle fallback
                    playerRect = new System.Windows.Shapes.Rectangle { Width = TILE, Height = TILE, Fill = new SolidColorBrush(Colors.Magenta) };
                    System.Windows.Controls.Canvas.SetZIndex(playerRect, 1000);
                    RenderCanvas.Children.Add(playerRect);
                    playerRect.Visibility = Visibility.Visible;
                    if (playerImage != null) playerImage.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
            this.Loaded += (s, e) => { try { this.Focus(); Keyboard.Focus(this); } catch { } };
            // Create persistent background / tile-layer / ground children to avoid re-allocating each frame
            try
            {
                bgRectPersistent = new System.Windows.Shapes.Rectangle
                {
                    Width = RenderCanvas.Width,
                    Height = RenderCanvas.Height,
                    Fill = new SolidColorBrush(backgroundTint)
                };
                System.Windows.Controls.Canvas.SetLeft(bgRectPersistent, 0);
                System.Windows.Controls.Canvas.SetTop(bgRectPersistent, 0);
                RenderCanvas.Children.Add(bgRectPersistent);

                // Tile layer image: cache full tile block into a RenderTargetBitmap and blit
                tileLayerImage = new System.Windows.Controls.Image
                {
                    Width = (NES_W + 1) * TILE,
                    Height = (NES_H + 1) * TILE,
                    Stretch = Stretch.None
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(tileLayerImage, BitmapScalingMode.NearestNeighbor);
                // Snap to device pixels and use aliased edge mode to avoid 1-px seams when translating
                tileLayerImage.SnapsToDevicePixels = true;
                System.Windows.Media.RenderOptions.SetEdgeMode(tileLayerImage, EdgeMode.Aliased);
                RenderCanvas.Children.Add(tileLayerImage);

                groundRectPersistent = new System.Windows.Shapes.Rectangle
                {
                    Width = RenderCanvas.Width,
                    Height = TILE * Math.Min(NES_H, 2),
                    Fill = new SolidColorBrush(groundTint) { Opacity = 0.25 },
                    Visibility = Visibility.Collapsed // hide ground overlay temporarily until ground rendering is fixed
                };
                System.Windows.Controls.Canvas.SetLeft(groundRectPersistent, 0);
                System.Windows.Controls.Canvas.SetTop(groundRectPersistent, (NES_H * TILE) - (TILE * Math.Min(NES_H, 2)));
                RenderCanvas.Children.Add(groundRectPersistent);
            }
            catch { }

            // (debug overlay removed)
        }

        // Map a tile index to its animated version based on current animation frame
        // Mirrors MainWindow.GetAnimatedTileIndex for saw tiles so simulator can animate tile-based saws
        private int MapAnimatedTileIndex(int originalIndex)
        {
            // Simulator forces preview-like behavior when requested
            int mapped = originalIndex;

            // Apply same preview remaps as editor (subset relevant to saws)
            switch (originalIndex)
            {
                case 0xFC:
                case 0xDF:
                case 0xE3:
                case 0xFE:
                case 0xFF: mapped = 0x00; break;
                case 0xFD: mapped = 0x26; break;
            }

            if (mapped >= 0x08 && mapped <= 0x0B)
            {
                // Use the same rhythm as sprite animations (frame math below)
                bool showFrame2 = (((animationFrame * 9) / 20) % 2) == 1;
                int tileOffset = mapped - 0x08;
                return showFrame2 ? 1004 + tileOffset : 1000 + tileOffset;
            }

            if (mapped == 0x04 || mapped == 0x7D || mapped == 0x7F)
            {
                bool showFrame2 = (((animationFrame * 9) / 20) % 2) == 1;
                int tileOffset = (mapped == 0x04) ? 0 : (mapped == 0x7D) ? 1 : 2;
                return showFrame2 ? 1013 + tileOffset : 1010 + tileOffset;
            }

            if (mapped >= 0x74 && mapped <= 0x7C)
            {
                bool showFrame2 = (((animationFrame * 9) / 20) % 2) == 1;
                int tileOffset = mapped - 0x74;
                return showFrame2 ? 1029 + tileOffset : 1020 + tileOffset;
            }

            return originalIndex;
        }

        private void SimulatorWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up) upHeld = true;
            if (e.Key == Key.Down) downHeld = true;
            if (e.Key == Key.Tab)
            {
                tabHeld = true;
                // compute tab multiplier based on modifiers: Tab=2x, Shift+Tab=4x, Ctrl+Shift+Tab=8x
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (shift && ctrl) tabSpeedMultiplier = 8;
                else if (shift) tabSpeedMultiplier = 4;
                else tabSpeedMultiplier = 2;
            }
            if (e.Key == Key.Escape)
            {
                // toggle pause
                paused = !paused;
            }
        }

        private void SimulatorWindow_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up) upHeld = false;
            if (e.Key == Key.Down) downHeld = false;
            if (e.Key == Key.Tab)
            {
                tabHeld = false;
                tabSpeedMultiplier = 1;
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Keep previous camera center for later anchor detection
            int prevCameraCenter_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);

            // Advance player X by current dynamic speed (fixed-point)
            // Holding TAB or modifiers change horizontal movement speed via tabSpeedMultiplier.
            int speedMultiplier = tabSpeedMultiplier;
            int centerOffset_fixed = (TILE / 2) << 8;
            int prevPlayerCenter_fixed = playerX_fixed + centerOffset_fixed;
            int attemptedPlayerX_fixed = playerX_fixed + currentSpeed_fixed * speedMultiplier;
            int attemptedPlayerCenter_fixed = attemptedPlayerX_fixed + centerOffset_fixed;

            // Move the player forward in world coordinates first
            playerX_fixed = attemptedPlayerX_fixed;

            // If the player just crossed the interaction line this step, capture the
            // screen X (in pixels) where the interaction line appeared so the camera
            // can keep the player anchored there while the player continues moving.
            bool crossedInteraction = prevPlayerCenter_fixed < INTERACTION_LINE_FIXED && attemptedPlayerCenter_fixed >= INTERACTION_LINE_FIXED;
            if (crossedInteraction)
            {
                interactionScreenOffset_px = (INTERACTION_LINE_FIXED >> 8) - (cameraX_fixed >> 8);
            }

            // If the player's center is at/after the interaction line, make the camera
            // follow the player's world movement such that the player's screen X stays
            // at the recorded interaction offset. If no offset recorded, fall back to
            // keeping the player at the interaction line world X.
            int playerCenter_fixed_now = playerX_fixed + centerOffset_fixed;
            if (playerCenter_fixed_now >= INTERACTION_LINE_FIXED)
            {
                if (interactionScreenOffset_px >= 0)
                {
                    // camera = playerX - interactionScreenOffset
                    cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
                }
                else
                {
                    // No recorded offset (edge case): keep player's center at interaction line
                    cameraX_fixed += attemptedPlayerCenter_fixed - INTERACTION_LINE_FIXED;
                }

                // clamp cameraX to map bounds
                int maxCamera_fixed = Math.Max(0, (mapWidth - NES_W) * TILE) << 8;
                if (cameraX_fixed < 0) cameraX_fixed = 0;
                if (cameraX_fixed > maxCamera_fixed) cameraX_fixed = maxCamera_fixed;
            }
            else
            {
                // player moved left of interaction line: clear recorded offset so crossing will re-capture
                interactionScreenOffset_px = -1;
            }

            // Advance animation frame counter
            animationFrame++;

            // Vertical panning while keys held - use fixed-point for smoothness
            // Use smaller step for smoother motion (2 pixels/frame)
            const int panStep_fixed = 512; // 2 pixels per frame (256 = 1px)
            if (upHeld) cameraY_fixed -= panStep_fixed;
            if (downHeld) cameraY_fixed += panStep_fixed;

            // Clamp cameraY to valid range
            int maxY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            if (cameraY_fixed < 0) cameraY_fixed = 0;
            if (cameraY_fixed > maxY_fixed) cameraY_fixed = maxY_fixed;

            // After moving camera X, check for speed-portal anchor crossings
            try
            {
                // remember previous tints so we can detect changes
                var prevBackgroundTint = backgroundTint;
                var prevTileTint = tileTint;
                var prevGroundTint = groundTint;
                int center_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);
                // We moved right relative to world; detect triggers either from a player crossing
                // the fixed interaction line, or from camera movement when the player is already past it.
                int bestAnchor_fixed = int.MaxValue;
                int? newSpeed_fixed = null;

                if (crossedInteraction)
                {
                    // Player crossed the interaction line this step: consider anchors between the
                    // previous player center and the fixed interaction line.
                    for (int idx = 0; idx < sprites.Length; idx++)
                    {
                        int sid = sprites[idx];
                        if (sid < 0) continue;
                        if (!speedPortalMap.ContainsKey(sid)) continue;

                        int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a)) ? a.anchorTileX : idx % mapWidth;
                        int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                        {
                            if (anchorX_center_fixed < bestAnchor_fixed)
                            {
                                bestAnchor_fixed = anchorX_center_fixed;
                                newSpeed_fixed = speedPortalMap[sid];
                            }
                        }
                    }
                }
                else
                {
                    // Player already past interaction line: use camera-centered detection like before
                    for (int idx = 0; idx < sprites.Length; idx++)
                    {
                        int sid = sprites[idx];
                        if (sid < 0) continue;
                        if (!speedPortalMap.ContainsKey(sid)) continue;

                        int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a)) ? a.anchorTileX : idx % mapWidth;
                        int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                        if (anchorX_center_fixed > prevCameraCenter_fixed && anchorX_center_fixed <= center_fixed)
                        {
                            if (anchorX_center_fixed < bestAnchor_fixed)
                            {
                                bestAnchor_fixed = anchorX_center_fixed;
                                newSpeed_fixed = speedPortalMap[sid];
                            }
                        }
                    }
                }

                // Also detect color-trigger crossings. To reduce sampling cost, only sample a trigger
                // once when it first moves past the interaction/center line. Keep a set of processed anchors so
                // we don't resample every frame while the trigger remains past center.
                int bestBg_fixed = int.MaxValue; int? bgIdx = null; int? bgSid = null;
                int bestTile_fixed = int.MaxValue; int? tileIdx = null; int? tileSid = null;
                int bestGround_fixed = int.MaxValue; int? groundIdx = null; int? groundSid = null;

                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;

                    // Check if this sprite is a color trigger we care about
                    if (!IsColorTriggerSprite(sid)) continue;

                    // Determine anchor tile X for this sprite: prefer explicit anchor, otherwise use tile position
                    int anchorTileX;
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a2))
                        anchorTileX = a2.anchorTileX;
                    else
                        anchorTileX = idx % mapWidth;

                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                    if (crossedInteraction)
                    {
                        // During a player crossing, only consider anchors between previous player center and the fixed interaction line.
                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                        {
                            if (IsBackgroundTrigger(sid))
                            {
                                if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgIdx = idx; bgSid = sid; }
                            }
                            else if (IsTileTrigger(sid))
                            {
                                if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileIdx = idx; tileSid = sid; }
                            }
                            else if (IsGroundTrigger(sid))
                            {
                                if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundIdx = idx; groundSid = sid; }
                            }
                        }
                        else
                        {
                            // Anchor is to the right of the interaction line; clear processed flag so it can trigger again when recrossed
                            if (processedColorTriggers.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedColorTriggers.Remove(idx);
                        }
                    }
                    else
                    {
                        // Camera-centered detection (player already past interaction line)
                        if (anchorX_center_fixed <= center_fixed)
                        {
                            if (processedColorTriggers.Contains(idx))
                            {
                                // already handled previously
                            }
                            else
                            {
                                if (IsBackgroundTrigger(sid))
                                {
                                    if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgIdx = idx; bgSid = sid; }
                                }
                                else if (IsTileTrigger(sid))
                                {
                                    if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileIdx = idx; tileSid = sid; }
                                }
                                else if (IsGroundTrigger(sid))
                                {
                                    if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundIdx = idx; groundSid = sid; }
                                }
                            }
                        }
                        else
                        {
                            // Anchor is left-of-center; clear any processed flag so it can be processed again if recrossed
                            if (processedColorTriggers.Contains(idx)) processedColorTriggers.Remove(idx);
                        }
                    }
                }

                // Apply detected color triggers: compute color and set tints accordingly. Mark anchors processed
                try
                {
                        if (bgIdx.HasValue && bgSid.HasValue)
                    {
                        var c = ColorFromTrigger(bgSid.Value);
                        backgroundTint = c; // update field used for drawing background
                        processedColorTriggers.Add(bgIdx.Value);
                        if (enableSimulatorDebugLogging && !triggerLogged.Contains(bgIdx.Value))
                        {
                            WriteTempLog($"Simulator: Applied background trigger at idx={bgIdx.Value} sid=0x{bgSid.Value:X} color={c}");
                            triggerLogged.Add(bgIdx.Value);
                        }
                    }
                    if (tileIdx.HasValue && tileSid.HasValue)
                    {
                        var c = ColorFromTrigger(tileSid.Value);
                        tileTint = c;
                        processedColorTriggers.Add(tileIdx.Value);
                        if (enableSimulatorDebugLogging && !triggerLogged.Contains(tileIdx.Value))
                        {
                            WriteTempLog($"Simulator: Applied tile trigger at idx={tileIdx.Value} sid=0x{tileSid.Value:X} color={c}");
                            triggerLogged.Add(tileIdx.Value);
                        }
                    }
                    if (groundIdx.HasValue && groundSid.HasValue)
                    {
                        var c = ColorFromTrigger(groundSid.Value);
                        groundTint = c;
                        processedColorTriggers.Add(groundIdx.Value);
                        if (enableSimulatorDebugLogging && !triggerLogged.Contains(groundIdx.Value))
                        {
                            WriteTempLog($"Simulator: Applied ground trigger at idx={groundIdx.Value} sid=0x{groundSid.Value:X} color={c}");
                            triggerLogged.Add(groundIdx.Value);
                        }
                    }
                }
                catch { }

                // If any tint changed, regenerate toned tile images and invalidate tile-layer cache
                try
                {
                    if (!AreColorsEqual(prevBackgroundTint, backgroundTint) || !AreColorsEqual(prevTileTint, tileTint) || !AreColorsEqual(prevGroundTint, groundTint))
                    {
                        // regenerate toned tile sets for the new tile tint so cached tiles draw with new hue
                        UpdateTonedImagesForTileTint(tileTint);

                        // regenerate parallax and ground toned images so parallax/ground respond to their tints
                        try {
                            if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                            {
                                parallaxTonedImages = CreateBlackMaskedImages(parallaxImages);
                            }
                            else
                            {
                                parallaxTonedImages = CreateHueShiftedImages(parallaxImages, backgroundTint);
                            }
                        } catch { parallaxTonedImages = parallaxImages; }
                        try {
                            if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                            {
                                groundTonedImages = CreateBlackMaskedImages(groundImages);
                            }
                            else
                            {
                                groundTonedImages = CreateHueShiftedImages(groundImages, groundTint);
                            }
                        } catch { groundTonedImages = groundImages; }

                        // invalidate cached tile layer so it is rebuilt with new toned images
                        tileLayerCache = null;
                        // Clear ground-tinted tile cache so tiles using ground tint get regenerated
                        try { groundTintedTileCache.Clear(); } catch { }
                    }
                }
                catch { }
                if (newSpeed_fixed.HasValue)
                {
                    currentSpeed_fixed = newSpeed_fixed.Value;
                }
            }
            catch { }

            RenderFrame();
        }

        private void RenderFrame()
        {
            // Compute pixel offset and starting tile index
            int pixelX = cameraX_fixed >> 8; // full pixels
            int subPixel = cameraX_fixed & 0xFF; // fractional
            int startTileX = pixelX / TILE;
            int offsetX = pixelX % TILE;
            int pixelY = cameraY_fixed >> 8;

            // If ground is present in the preview, reserve up to three ground rows at the bottom
            int groundRowsToReserve = 0;
            if (hasGroundLayer && groundTileRows > 0)
            {
                // Reserve exactly 3 rows when a ground layer exists (or fewer if ground bitmap has <3 rows)
                groundRowsToReserve = Math.Min(3, groundTileRows);
            }
            int groundPixels = groundRowsToReserve * TILE;

            // Keep camera-aligned start/offset based on actual camera Y so sub-pixel translation
            // remains consistent with the editor. We'll subtract ground rows when sampling map tiles.
            int startTileY = pixelY / TILE;
            int offsetY = pixelY % TILE;

            // Update persistent background: draw parallax tiled image when available
            try
            {
                if (bgRectPersistent != null && hasParallaxLayer && parallaxImages != null && parallaxImages.Length > 0)
                {
                    ImageSource src = parallaxTonedImages != null && parallaxTonedImages.Length == parallaxImages.Length && parallaxTonedImages[0] != null ? parallaxTonedImages[0] : parallaxImages[0];
                    if (src is BitmapSource pbs)
                    {
                        double tileW = Math.Max(1.0, pbs.PixelWidth);
                        double tileH = Math.Max(1.0, pbs.PixelHeight);
                        var brush = new ImageBrush(src)
                        {
                            TileMode = TileMode.Tile,
                            ViewportUnits = BrushMappingMode.Absolute,
                            Viewport = new Rect(0, 0, tileW, tileH),
                            Stretch = Stretch.Fill
                        };

                        // Parallax translation: background moves slower than camera based on parallaxX/Y.
                        double parallaxOffsetX = -(pixelX) * (1.0 - parallaxX);
                        double parallaxOffsetY = -(cameraY_fixed >> 8) * (1.0 - parallaxY);

                        // Align horizontal scroll to tile pixels to avoid shimmering
                        brush.Transform = new TranslateTransform(parallaxOffsetX, parallaxOffsetY);
                        bgRectPersistent.Fill = brush;
                    }
                    else
                    {
                        bgRectPersistent.Fill = new SolidColorBrush(backgroundTint);
                    }
                }
                else
                {
                    if (bgRectPersistent != null) bgRectPersistent.Fill = new SolidColorBrush(backgroundTint);
                }
            }
            catch { }

            // Rebuild tile-layer cache when integer tile origin changes
            try
            {
                if (tileLayerImage != null && (tileLayerCache == null || cachedStartTileX != startTileX || cachedStartTileY != startTileY || (lastCacheHadAnimatedTiles && lastCacheAnimationFrame != animationFrame)))
                {
                    int cacheTilesX = NES_W + 1;
                    int cacheTilesY = NES_H + 1;
                    int pxW = cacheTilesX * TILE;
                    int pxH = cacheTilesY * TILE;

                    var dv = new DrawingVisual();
                    bool hadAnimated = false;
                    using (var dc = dv.RenderOpen())
                    {
                        int cacheTilesY_local = NES_H + 1;
                        int groundRowsToReserve_local = groundRowsToReserve;
                        int groundStartRow_local = cacheTilesY_local - groundRowsToReserve_local;

                        // Determine ground layout columns if ground images are available
                        int groundCols = 1;
                        if (groundImages != null && groundTileRows > 0)
                        {
                            groundCols = Math.Max(1, groundImages.Length / groundTileRows);
                        }

                        for (int vx = 0; vx <= NES_W; vx++)
                        {
                            int mapX = startTileX + vx;
                            for (int vy = 0; vy <= NES_H; vy++)
                            {
                                Rect dest = new Rect(vx * TILE, vy * TILE, TILE, TILE);

                                // Compute the map Y corresponding to this dest row, where we treat the
                                // visible rows as starting from startTileY - groundRowsToReserve
                                int mapY = startTileY + groundRowsToReserve_local + vy;

                                // If mapY is beyond the bottom of the map, and we have a ground layer,
                                // draw the appropriate ground slice row instead of map tiles.
                                if (mapY >= mapHeight)
                                {
                                    if (hasGroundLayer && groundImages != null && groundImages.Length > 0)
                                    {
                                        int pyGround = mapY - mapHeight; // 0..groundRowsToReserve-1
                                        int pxGround = ((mapX % groundCols) + groundCols) % groundCols; // wrap
                                        int rowIndex = Math.Max(0, Math.Min(groundTileRows - 1, pyGround));
                                        int arrIdx = rowIndex * groundCols + pxGround;
                                        ImageSource? gimg = null;
                                        if (groundTonedImages != null && arrIdx >= 0 && arrIdx < groundTonedImages.Length) gimg = groundTonedImages[arrIdx];
                                        if (gimg == null && groundImages != null && arrIdx >= 0 && arrIdx < groundImages.Length) gimg = groundImages[arrIdx];
                                        if (gimg != null)
                                        {
                                            if (gimg is BitmapSource gbs)
                                            {
                                                double imgW = Math.Max(1.0, gbs.PixelWidth);
                                                double imgH = Math.Max(1.0, gbs.PixelHeight);
                                                if (imgW <= TILE && imgH <= TILE)
                                                {
                                                    double x = dest.X + (TILE - imgW) / 2.0;
                                                    double y = dest.Y + (TILE - imgH);
                                                    dc.DrawImage(gimg, new Rect(x, y, imgW, imgH));
                                                }
                                                else
                                                {
                                                    dc.DrawImage(gimg, dest);
                                                }
                                            }
                                            else
                                            {
                                                dc.DrawImage(gimg, dest);
                                            }
                                        }
                                        else
                                        {
                                            dc.DrawRectangle(Brushes.Black, null, dest);
                                        }
                                        continue;
                                    }
                                    else
                                    {
                                        dc.DrawRectangle(Brushes.Black, null, dest);
                                        continue;
                                    }
                                }

                                // Normal map tile sampling
                                if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight)
                                {
                                    dc.DrawRectangle(Brushes.Black, null, dest);
                                    continue;
                                }
                                int idx = mapY * mapWidth + mapX;
                                int t = tiles[idx];
                                int animatedTileIndex = MapAnimatedTileIndex(t);
                                int useTileIndex = animatedTileIndex;
                                if (animatedTileIndex != t || useTileIndex >= 1000) hadAnimated = true;
                                ImageSource? chosenTile = null;
                                try
                                {
                                    if (useTileIndex >= 1000)
                                    {
                                        // Use the same animation cadence as sprite frames so saws flip in sync
                                        bool frame2 = (((animationFrame * 9) / 20) % 2) != 0;
                                        int ut = useTileIndex;
                                        // Prefer explicit tileImages/toned images for animated tile indices when available
                                        if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] != null)
                                        {
                                            chosenTile = tileTonedImages[useTileIndex];
                                        }
                                        else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                        {
                                            chosenTile = tileImages[useTileIndex];
                                        }
                                        else if (ut >= 1000 && ut <= 1007)
                                        {
                                            int off = (ut - 1000) % 4;
                                            int len1 = sawFrame1TilesTinted != null ? sawFrame1TilesTinted.Length : 0;
                                            int len2 = sawFrame2TilesTinted != null ? sawFrame2TilesTinted.Length : 0;
                                            int tileCount = 4;
                                            int nFrames1 = len1 >= tileCount && len1 % tileCount == 0 ? len1 / tileCount : 1;
                                            int nFrames2 = len2 >= tileCount && len2 % tileCount == 0 ? len2 / tileCount : 1;
                                            int frameCount = Math.Max(1, Math.Max(nFrames1, nFrames2));
                                            int frameIdx = (((animationFrame * 9) / 20)) % frameCount;
                                            bool useFrame2 = (((animationFrame * 9) / 20) % 2) == 1;

                                            if (useFrame2 && sawFrame2TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames2 > 1 ? (frameIdx % nFrames2) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len2) chosenTile = sawFrame2TilesTinted[arrIdx];
                                            }
                                            if (chosenTile == null && sawFrame1TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames1 > 1 ? (frameIdx % nFrames1) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len1) chosenTile = sawFrame1TilesTinted[arrIdx];
                                            }
                                        }
                                        else if (ut >= 1010 && ut <= 1015)
                                        {
                                            int off = (ut - 1010) % 3;
                                            int len1 = smallSawFrame1TilesTinted != null ? smallSawFrame1TilesTinted.Length : 0;
                                            int len2 = smallSawFrame2TilesTinted != null ? smallSawFrame2TilesTinted.Length : 0;
                                            int tileCount = 3;
                                            int nFrames1 = len1 >= tileCount && len1 % tileCount == 0 ? len1 / tileCount : 1;
                                            int nFrames2 = len2 >= tileCount && len2 % tileCount == 0 ? len2 / tileCount : 1;
                                            int frameCount = Math.Max(1, Math.Max(nFrames1, nFrames2));
                                            int frameIdx = (((animationFrame * 9) / 20)) % frameCount;
                                            bool useFrame2 = (((animationFrame * 9) / 20) % 2) == 1;

                                            if (useFrame2 && smallSawFrame2TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames2 > 1 ? (frameIdx % nFrames2) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len2) chosenTile = smallSawFrame2TilesTinted[arrIdx];
                                            }
                                            if (chosenTile == null && smallSawFrame1TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames1 > 1 ? (frameIdx % nFrames1) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len1) chosenTile = smallSawFrame1TilesTinted[arrIdx];
                                            }
                                        }
                                        else if (ut >= 1020 && ut <= 1037)
                                        {
                                            int off = (ut - 1020) % 9;
                                            int len1 = largeSawFrame1TilesTinted != null ? largeSawFrame1TilesTinted.Length : 0;
                                            int len2 = largeSawFrame2TilesTinted != null ? largeSawFrame2TilesTinted.Length : 0;
                                            int tileCount = 9;
                                            int nFrames1 = len1 >= tileCount && len1 % tileCount == 0 ? len1 / tileCount : 1;
                                            int nFrames2 = len2 >= tileCount && len2 % tileCount == 0 ? len2 / tileCount : 1;
                                            int frameCount = Math.Max(1, Math.Max(nFrames1, nFrames2));
                                            int frameIdx = (((animationFrame * 9) / 20)) % frameCount;
                                            bool useFrame2 = (((animationFrame * 9) / 20) % 2) == 1;

                                            if (useFrame2 && largeSawFrame2TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames2 > 1 ? (frameIdx % nFrames2) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len2) chosenTile = largeSawFrame2TilesTinted[arrIdx];
                                            }
                                            if (chosenTile == null && largeSawFrame1TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames1 > 1 ? (frameIdx % nFrames1) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len1) chosenTile = largeSawFrame1TilesTinted[arrIdx];
                                            }
                                        }

                                        // (no diagnostics) intentionally left blank
                                    }
                                }
                                catch { }

                                if (chosenTile == null && useTileIndex >= 0)
                                {
                                    // Use the animated tile index (useTileIndex) consistently when selecting the image.
                                    // For a few ground-related tile indices, prefer applying the ground tint
                                    // (these tiles should respond to ground tint, not tile tint).
                                    var groundAffected = (useTileIndex == 0x01 || useTileIndex == 0x02 || useTileIndex == 0x05 || useTileIndex == 0x06 || useTileIndex == 0x88 || useTileIndex == 0x89);

                                    if (groundAffected)
                                    {
                                        try
                                        {
                                            ImageSource? gt = null;
                                            long key = (((long)useTileIndex) << 32) | ((long)groundTint.A << 24) | ((long)groundTint.R << 16) | ((long)groundTint.G << 8) | groundTint.B;
                                            if (!groundTintedTileCache.TryGetValue(key, out gt))
                                            {
                                                // create a ground-tinted copy from original tileImages when available
                                                if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                {
                                                    try
                                                    {
                                                        if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                                                        {
                                                            // Pure black ground tint: make a black-masked copy (preserve white lines)
                                                            gt = CreateBlackMaskedImage(tileImages[useTileIndex]);
                                                        }
                                                        else
                                                        {
                                                            var arr = CreateHslShiftedImages(new ImageSource[] { tileImages[useTileIndex] }, groundTint);
                                                            if (arr != null && arr.Length > 0) gt = arr[0];
                                                        }
                                                    }
                                                    catch { gt = tileImages[useTileIndex]; }
                                                }
                                                groundTintedTileCache[key] = gt;
                                            }
                                            if (gt != null) chosenTile = gt;
                                        }
                                        catch { }
                                    }

                                    if (chosenTile == null)
                                    {
                                        if (useTileIndex >= 1000)
                                        {
                                            if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] != null)
                                                chosenTile = tileTonedImages[useTileIndex];
                                            else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                chosenTile = tileImages[useTileIndex];
                                        }
                                        else
                                        {
                                            if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] != null)
                                                chosenTile = tileTonedImages[useTileIndex];
                                            else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                chosenTile = tileImages[useTileIndex];
                                        }
                                    }
                                }

                                if (chosenTile != null)
                                {
                                    // If the tile image is smaller than the canonical TILE size (e.g.
                                    // saw halves that are half-height PNGs), draw it at its natural
                                    // pixel size instead of scaling to fill the full tile. Align
                                    // smaller images to the bottom of the tile so transparent
                                    // padding sits above as expected.
                                    if (chosenTile is BitmapSource bs)
                                    {
                                        // Use the bitmap's pixel dimensions to decide if it should be
                                        // drawn at natural size. Device-independent units in this
                                        // renderer correspond to pixels (RTB created at 96 DPI), so
                                        // bs.PixelWidth/Height work directly.
                                        double imgW = Math.Max(1.0, bs.PixelWidth);
                                        double imgH = Math.Max(1.0, bs.PixelHeight);

                                        if (imgW <= TILE && imgH <= TILE)
                                        {
                                            // bottom-align within the tile cell
                                            double x = dest.X + (TILE - imgW) / 2.0;
                                            double y = dest.Y + (TILE - imgH);
                                            dc.DrawImage(chosenTile, new Rect(x, y, imgW, imgH));
                                        }
                                        else
                                        {
                                            // image larger than tile: fall back to scaling to tile
                                            dc.DrawImage(chosenTile, dest);
                                        }
                                    }
                                    else
                                    {
                                        dc.DrawImage(chosenTile, dest);
                                    }
                                }
                                else
                                {
                                    dc.DrawRectangle(Brushes.Black, null, dest);
                                }
                            }
                        }
                    }

                    var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    tileLayerCache = rtb;
                    lastCacheHadAnimatedTiles = hadAnimated;
                    lastCacheAnimationFrame = animationFrame;
                    cachedStartTileX = startTileX;
                    cachedStartTileY = startTileY;
                    tileLayerImage!.Source = tileLayerCache;
                    tileLayerImage!.Width = pxW;
                    tileLayerImage!.Height = pxH;
                }
            }
            catch { }

            // Position the tile layer to account for fractional pixel offset
            try { if (tileLayerImage != null) { System.Windows.Controls.Canvas.SetLeft(tileLayerImage, -offsetX); System.Windows.Controls.Canvas.SetTop(tileLayerImage, -offsetY); } } catch { }

            // Update ground rectangle (render tiled ground image if available)
            try
            {
                if (groundRectPersistent != null)
                {
                    if (hasGroundLayer && groundImages != null && groundImages.Length > 0)
                    {
                        ImageSource src = groundTonedImages != null && groundTonedImages.Length == groundImages.Length && groundTonedImages[0] != null ? groundTonedImages[0] : groundImages[0];
                        if (src is BitmapSource gbs)
                        {
                            double tileW = Math.Max(1.0, gbs.PixelWidth);
                            double tileH = Math.Max(1.0, gbs.PixelHeight);
                            var brush = new ImageBrush(src)
                            {
                                TileMode = TileMode.Tile,
                                ViewportUnits = BrushMappingMode.Absolute,
                                Viewport = new Rect(0, 0, tileW, tileH),
                                Stretch = Stretch.Fill
                            };
                            // Sync horizontal scroll with tiles
                            brush.Transform = new TranslateTransform(-offsetX, 0);
                            groundRectPersistent.Fill = brush;
                        }
                        else
                        {
                            groundRectPersistent.Fill = new SolidColorBrush(groundTint) { Opacity = 0.25 };
                        }
                        int groundHeight = TILE * Math.Min(NES_H, Math.Max(groundTileRows, 2));
                        // Use reserved rows (up to 2) as visual ground height
                        groundHeight = TILE * Math.Min(NES_H, Math.Min(groundTileRows, 2));
                        groundRectPersistent.Height = groundHeight;
                        System.Windows.Controls.Canvas.SetTop(groundRectPersistent, (NES_H * TILE) - groundHeight);
                        // We draw ground cells directly into the tile-layer cache, so keep the persistent
                        // ground rect collapsed to avoid covering the tile layer with a single-tile brush.
                        groundRectPersistent.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        groundRectPersistent.Fill = new SolidColorBrush(groundTint) { Opacity = 0.25 };
                        int groundHeight = TILE * Math.Min(NES_H, 2);
                        groundRectPersistent.Height = groundHeight;
                        System.Windows.Controls.Canvas.SetTop(groundRectPersistent, (NES_H * TILE) - groundHeight);
                        groundRectPersistent.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch { }

            // Render sprites using pooled Image controls
            spritesInUse = 0;
            for (int vx = 0; vx < NES_W; vx++)
            {
                int mapX = startTileX + vx;
                for (int vy = 0; vy < NES_H; vy++)
                {
                    int mapY = startTileY + groundRowsToReserve + vy;
                    if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight) continue;
                    int idx = mapY * mapWidth + mapX;
                    int s = sprites[idx];
                    if (s < 0) continue;

                    ImageSource? chosenSprite = null;
                    if (animationFrames != null && animationFrames.TryGetValue(s, out var frames) && frames != null && frames.Length > 0)
                    {
                        // For 2-frame decoration sprites we want a uniform cadence across all anchors
                        // so do not apply a per-anchor random offset. For other sprites/lengths, preserve
                        // per-anchor randomness so placements don't always animate in lockstep.
                        int offset = 0;
                        if (!(decorationSpriteIds.Contains(s) && frames.Length == 2))
                        {
                            if (!spriteFrameOffsets.ContainsKey(idx)) spriteFrameOffsets[idx] = spriteAnimationRandom.Next(0, Math.Max(1, frames.Length));
                            offset = spriteFrameOffsets[idx];
                        }
                        int frame = 0;
                        if (decorationSpriteIds.Contains(s) && frames.Length == 2)
                        {
                            // Match editor preview two-frame cadence exactly (same math as MainWindow.GetTwoFrameCustomIndex)
                            frame = (((animationFrame * 3) / 40)) % 2;
                            if (frame < 0) frame += 2;
                        }
                        else
                        {
                            frame = (((animationFrame * 9) / 20) + offset) % Math.Max(1, frames.Length);
                        }
                        chosenSprite = frames[frame];
                        // Debug: log decoration frames presence/selection when the selected frame changes
                        try
                        {
                            if (enableSimulatorDebugLogging && decorationSpriteIds.Contains(s) && frames.Length == 2)
                            {
                                int prevFrame = -1;
                                decoLastSelectedFrame.TryGetValue(idx, out prevFrame);
                                if (prevFrame != frame)
                                {
                                    string h0 = frames[0] != null ? frames[0].GetHashCode().ToString("X8") : "null";
                                    string h1 = frames[1] != null ? frames[1].GetHashCode().ToString("X8") : "null";
                                    WriteTempLog($"Simulator: Decoration sprite idx={idx} id=0x{s:X} frames=[{(frames[0]!=null?"ok":"null")},{(frames[1]!=null?"ok":"null")}] selectedFrame={frame} hashes=[{h0},{h1}]");
                                    decoLastSelectedFrame[idx] = frame;
                                }
                            }
                        }
                        catch { }
                        if (chosenSprite == null)
                        {
                            if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(s, out var pimg) && pimg != null)
                                chosenSprite = pimg;
                            else if (spriteImages != null && s < spriteImages.Length && spriteImages[s] != null)
                                chosenSprite = spriteImages[s];
                        }
                    }
                    else
                    {
                        if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(s, out var previewImg) && previewImg != null)
                        {
                            chosenSprite = previewImg;
                        }
                        else if (spriteImages != null && s < spriteImages.Length && spriteImages[s] != null)
                        {
                            chosenSprite = spriteImages[s];
                        }
                    }

                    if (chosenSprite == null) continue;
                    // Always hide black color-trigger sprites (they should activate but not be visible in simulator)
                    if (s == 0x8F || s == 0xCF) continue;
                    if (hideColorTriggers && IsColorTriggerSprite(s)) continue;

                    // Apply player tint to decoration sprites when enabled
                    try
                    {
                        if (playerTintEnabled && decorationSpriteIds.Contains(s) && chosenSprite != null)
                        {
                            chosenSprite = GetPlayerTintedSprite(chosenSprite, s);
                        }
                    }
                    catch { }

                    double px = (mapX - startTileX) * TILE - offsetX;
                    double py = (vy * TILE) - offsetY + gridRenderShiftYPx;
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                    {
                        int storageTileX = idx % mapWidth;
                        int storageTileY = idx / mapWidth;
                        int tileDeltaX = storageTileX - anchor.anchorTileX;
                        int tileDeltaY = storageTileY - anchor.anchorTileY;
                        double anchorDisplayX = (anchor.anchorTileX - startTileX) * TILE - offsetX;
                        double anchorDisplayY = (anchor.anchorTileY - (startTileY + groundRowsToReserve)) * TILE - offsetY;
                        px = anchorDisplayX + tileDeltaX * TILE;
                        py = anchorDisplayY + tileDeltaY * TILE + gridRenderShiftYPx;
                    }
                    if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs))
                    {
                        px += offs.offsetX;
                        py += offs.offsetY; // match editor convention: positive offsetY moves sprite down
                    }
                    else if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anc))
                    {
                        int anchorKey = anc.anchorTileY * mapWidth + anc.anchorTileX;
                        if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var aoffs))
                        {
                            px += aoffs.offsetX;
                            py += aoffs.offsetY; // match editor convention
                        }
                    }

                    // Simulator tweak: medium poles (sprite id 0x2B and 0x2C) render 8px higher in preview
                    try
                    {
                        if (s == 0x2B || s == 0x2C) py -= 8;
                    }
                    catch { }

                    // get pooled image
                    System.Windows.Controls.Image simg;
                    if (spritesInUse < spritePool.Count)
                    {
                        simg = spritePool[spritesInUse];
                        simg.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        simg = new System.Windows.Controls.Image { Stretch = Stretch.None };
                        System.Windows.Media.RenderOptions.SetBitmapScalingMode(simg, BitmapScalingMode.NearestNeighbor);
                        spritePool.Add(simg);
                        RenderCanvas.Children.Add(simg);
                    }
                    // Force-refresh the Image control to ensure WPF updates when the source changes
                    try { simg.Source = null; } catch { }
                    simg.Source = chosenSprite;
                    if (chosenSprite is BitmapSource bs) { simg.Width = bs.PixelWidth; simg.Height = bs.PixelHeight; }
                    System.Windows.Controls.Canvas.SetLeft(simg, px);
                    System.Windows.Controls.Canvas.SetTop(simg, py);
                    spritesInUse++;

                    // Draw hitbox overlay if requested and this is not a color-trigger sprite
                    try
                    {
                        if (ShowSpriteHitboxes && !IsColorTriggerSprite(s) && !decorationSpriteIds.Contains(s))
                        {
                            System.Windows.Shapes.Rectangle hrect;
                            if (hitboxesInUse < hitboxPool.Count)
                            {
                                hrect = hitboxPool[hitboxesInUse];
                                hrect.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                hrect = new System.Windows.Shapes.Rectangle();
                                hrect.Fill = new SolidColorBrush(Color.FromArgb(96, 255, 255, 0)); // translucent yellow
                                hrect.Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 200, 0));
                                hrect.StrokeThickness = 1;
                                hrect.IsHitTestVisible = false;
                                hitboxPool.Add(hrect);
                                RenderCanvas.Children.Add(hrect);
                            }

                            // Determine hitbox from sprite tables; fallback to image bounds
                            int id = s & 0xFF;
                            int hw = 0x10; int hh = 0x10; int hxoff = 0; int hyoff = 0;
                            if (id >= 0 && id < sprite_widths.Length) hw = sprite_widths[id];
                            if (id >= 0 && id < sprite_heights.Length) hh = sprite_heights[id];
                            if (id >= 0 && id < sprite_x_offset.Length) hxoff = sprite_x_offset[id];
                            if (id >= 0 && id < sprite_y_offset.Length) hyoff = sprite_y_offset[id];

                            double hx = px + hxoff; // px already in screen space
                            double hy = py + hyoff;
                            // If this sprite is a non-upside-down pad, shift hitbox down an extra 8 px on top of specified offsets
                            // Known non-upside-down pad IDs include typical down variants; extend set as needed.
                            var padDownIds = new System.Collections.Generic.HashSet<int> { 0x52, 0x0A, 0x0D, 0x25, 0xFD };
                            if (padDownIds.Contains(id)) hy += 8;
                            hrect.Width = Math.Max(1, hw);
                            hrect.Height = Math.Max(1, hh);
                            System.Windows.Controls.Canvas.SetLeft(hrect, hx);
                            System.Windows.Controls.Canvas.SetTop(hrect, hy);
                            hitboxesInUse++;
                        }
                    }
                    catch { }

                    
                }
            }

            // Hide remaining pooled images
            for (int i = spritesInUse; i < spritePool.Count; i++) spritePool[i].Visibility = Visibility.Collapsed;

            // Hide remaining hitboxes
            for (int i = hitboxesInUse; i < hitboxPool.Count; i++) hitboxPool[i].Visibility = Visibility.Collapsed;
            // reset hitbox counter for next frame
            hitboxesInUse = 0;

            // Position the player visual so it appears above the reserved ground rows.
            try
            {
                int playerPixelX = (playerX_fixed >> 8) - (cameraX_fixed >> 8);
                // Place player's bottom so it stands one tile above the reserved ground rows
                int bottomY = NES_H * TILE - groundPixels;
                int playerTop = bottomY - (TILE * 1) + gridRenderShiftYPx;

                if (playerImage != null && playerImage.Source != null)
                {
                    // Position image; center-left semantics preserved from rectangle usage
                    System.Windows.Controls.Canvas.SetLeft(playerImage, playerPixelX);
                    System.Windows.Controls.Canvas.SetTop(playerImage, playerTop);
                    playerImage.Visibility = Visibility.Visible;
                    if (playerRect != null) playerRect.Visibility = Visibility.Collapsed;
                }
                else if (playerRect != null)
                {
                    System.Windows.Controls.Canvas.SetLeft(playerRect, playerPixelX);
                    System.Windows.Controls.Canvas.SetTop(playerRect, playerTop);
                    playerRect.Visibility = Visibility.Visible;
                }
            }
            catch { }

            // Apply sub-pixel smoothing with a translate transform for X and Y
            // Stabilize X fractional translation when the player is anchored at the interaction line
            int centerOffset_fixed_local = (TILE / 2) << 8;
            int playerCenter_fixed_now_local = playerX_fixed + centerOffset_fixed_local;
            bool isAnchoredNow = interactionScreenOffset_px >= 0 && playerCenter_fixed_now_local >= INTERACTION_LINE_FIXED;

            double fracX = (cameraX_fixed & 0xFF) / 256.0;
            double fracY = (cameraY_fixed & 0xFF) / 256.0;

            if (isAnchoredNow)
            {
                // Force fractional X to zero while anchored to avoid 1-px jitter when camera follows
                fracX = 0.0;
            }

            RenderCanvas.RenderTransform = new TranslateTransform(-fracX, -fracY);
        }

        // Use CompositionTarget.Rendering as the main loop to maintain consistent timing. We implement
        // a simple fixed-step simulation so animation and camera advance at 60Hz even if rendering
        // intermittently lags.
        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            try
            {
                var now = renderStopwatch.Elapsed.TotalSeconds;
                // compute delta since last sample
                double delta = now - accumulatedSeconds;
                if (delta < 0) delta = 0;
                accumulatedSeconds = now;

                // accumulate and step in fixed 1/60s increments
                double remaining = delta;
                const double step = 1.0 / 60.0;
                int steps = 0;
                // Safety cap to avoid spiral of death
                int maxSteps = 5;
                // If paused, skip simulation steps but still render so UI stays responsive
                if (paused)
                {
                    RenderFrame();
                    return;
                }

                while (remaining >= step && steps < maxSteps)
                {
                    // perform simulation step: advance camera and animation
                    Timer_Tick(null, EventArgs.Empty);
                    remaining -= step;
                    steps++;
                }

                // Render once per CompositionTarget tick
                RenderFrame();
            }
            catch { }
        }

        // Determine if a sprite id is a color trigger we should consider
        private bool IsColorTriggerSprite(int spriteIdx)
        {
            if (spriteIdx < 0) return false;
            // Accept ranges like the editor, but exclude certain low-nibble values per user request
            // Background triggers: 0x80-0xAC
            // Tile triggers: 0xB0-0xBF
            // Ground triggers: 0xC0-0xEC
            bool inRanges = (spriteIdx >= 0x80 && spriteIdx <= 0x8C) || spriteIdx == 0x8F ||
                            (spriteIdx >= 0x90 && spriteIdx <= 0x9C) || spriteIdx == 0x9F ||
                            (spriteIdx >= 0xA0 && spriteIdx <= 0xAC) || (spriteIdx >= 0xAE && spriteIdx <= 0xAF) ||
                            (spriteIdx >= 0xB0 && spriteIdx <= 0xBF) ||
                            (spriteIdx >= 0xC0 && spriteIdx <= 0xCC) || spriteIdx == 0xCF ||
                            (spriteIdx >= 0xD0 && spriteIdx <= 0xDC) ||
                            (spriteIdx >= 0xE0 && spriteIdx <= 0xEC);
            if (!inRanges) return false;

            // Disregard specific combinations where low nibble is D/E/F for certain high nibbles
            int low = spriteIdx & 0x0F;
            int high = spriteIdx & 0xF0;
            if (low >= 0xD)
            {
                // Most high-nibble groups with low >= 0xD are excluded, but allow explicit
                // single-value exceptions such as 0x8F and 0xCF which should act as triggers.
                if (high == 0x80 || high == 0x90 || high == 0xA0 || high == 0xC0 || high == 0xD0 || high == 0xE0)
                {
                    if (spriteIdx == 0x8F || spriteIdx == 0xCF)
                    {
                        // explicit exceptions: keep as trigger
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool IsBackgroundTrigger(int spriteIdx)
        {
            // Include 0x8F as valid background trigger per request
            return (spriteIdx >= 0x80 && spriteIdx <= 0xAC || spriteIdx == 0x8F) && IsColorTriggerSprite(spriteIdx);
        }

        private bool IsTileTrigger(int spriteIdx)
        {
            return spriteIdx >= 0xB0 && spriteIdx <= 0xBF && IsColorTriggerSprite(spriteIdx);
        }

        private bool IsGroundTrigger(int spriteIdx)
        {
            // Include 0xCF as a valid ground trigger per request
            return ((spriteIdx >= 0xC0 && spriteIdx <= 0xEC) || spriteIdx == 0xCF) && IsColorTriggerSprite(spriteIdx);
        }

        private Color ColorFromTrigger(int spriteIdx)
        {
            // Special-case: certain trigger sprites explicitly mean "black" regardless of sampling.
            if (spriteIdx == 0x8F || spriteIdx == 0xCF)
            {
                return Color.FromArgb(255, 0, 0, 0);
            }

            // Prefer sampling the actual sprite/preview image color so the tint matches icon color
            try
            {
                var c = SampleRepresentativeColorFromSprite(spriteIdx);
                if (c.HasValue) return c.Value;
            }
            catch { }

            // Fallback: map low nibble to HSL hue
            int colorIndex = spriteIdx & 0x0F; // 0-15
            double hue = (colorIndex / 16.0) * 360.0;
            return HslToColor(hue, 0.7, 0.45);
        }

        private Color? SampleRepresentativeColorFromSprite(int spriteIdx)
        {
            ImageSource? src = null;
            if (previewSpriteMap != null && previewSpriteMap.TryGetValue(spriteIdx, out var p) && p != null)
                src = p;
            else if (spriteImages != null && spriteIdx >= 0 && spriteIdx < spriteImages.Length)
                src = spriteImages[spriteIdx];

            if (src is BitmapSource bs)
            {
                try
                {
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                    int w = Math.Max(1, conv.PixelWidth);
                    int h = Math.Max(1, conv.PixelHeight);
                    int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    // Search for a non-transparent, non-black pixel. Prefer most frequent color isn't necessary; choose first non-black.
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte b = pixels[i + 0];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];
                        byte a = pixels[i + 3];
                        if (a > 32)
                        {
                            // ignore fully black pixels (icons often have black outlines)
                            if (!(r == 0 && g == 0 && b == 0))
                            {
                                return Color.FromArgb(255, r, g, b);
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private Color HslToColor(double h, double s, double l)
        {
            // h in [0,360), s,l in [0,1]
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double hh = h / 60.0;
            double x = c * (1 - Math.Abs((hh % 2) - 1));
            double r1 = 0, g1 = 0, b1 = 0;
            if (0 <= hh && hh < 1) { r1 = c; g1 = x; b1 = 0; }
            else if (1 <= hh && hh < 2) { r1 = x; g1 = c; b1 = 0; }
            else if (2 <= hh && hh < 3) { r1 = 0; g1 = c; b1 = x; }
            else if (3 <= hh && hh < 4) { r1 = 0; g1 = x; b1 = c; }
            else if (4 <= hh && hh < 5) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }
            double m = l - c / 2.0;
            byte r = (byte)Math.Round((r1 + m) * 255.0);
            byte g = (byte)Math.Round((g1 + m) * 255.0);
            byte b = (byte)Math.Round((b1 + m) * 255.0);
            return Color.FromArgb(255, r, g, b);
        }

        // Helper: compare colors (treat nullability not applicable here)
        private static bool AreColorsEqual(Color a, Color b)
        {
            return a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
        }

        // Return a player-tinted copy of the given sprite image (cached).
        private ImageSource? GetPlayerTintedSprite(ImageSource src, int spriteId)
        {
            if (!playerTintEnabled) return src;
            try
            {
                // Include the source image identity in the cache key so different frames
                // of the same sprite id don't collapse to the same cached tinted image.
                int srcHash = src?.GetHashCode() ?? 0;
                long key = (((long)spriteId) << 48) | (((long)srcHash & 0xFFFF) << 32) | ((long)playerTint.A << 24) | ((long)playerTint.R << 16) | ((long)playerTint.G << 8) | playerTint.B;
                if (tintedSpriteCache.TryGetValue(key, out var cached)) return cached;

                if (src is BitmapSource bs)
                {
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                    int w = Math.Max(1, conv.PixelWidth);
                    int h = Math.Max(1, conv.PixelHeight);
                    int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    // compute tint hue
                    RgbToHsl(playerTint.R, playerTint.G, playerTint.B, out double tintH, out double tintS, out double tintL);

                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte b = pixels[i + 0];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];
                        byte a = pixels[i + 3];
                        if (a == 0) continue; // preserve fully transparent pixels

                        // Replace hue with tint hue while preserving original saturation/lightness
                        RgbToHsl(r, g, b, out double ph, out double ps, out double pl);
                        double nh = tintH; double ns = ps; double nl = pl;
                        RgbFromHsl(nh, ns, nl, out byte nr, out byte ng, out byte nb);
                        pixels[i + 0] = nb;
                        pixels[i + 1] = ng;
                        pixels[i + 2] = nr;
                        // alpha unchanged
                    }

                    var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                    wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                    wb.Freeze();
                    tintedSpriteCache[key] = wb;
                    return wb;
                }
                else
                {
                    tintedSpriteCache[key] = src;
                    return src;
                }
            }
            catch
            {
                return src;
            }
        }

        // Regenerate toned images for tiles and saw frames using HSL hue shifting.
        // This mirrors MainWindow.UpdateTileTint's behavior so simulator can apply tints locally.
        private void UpdateTonedImagesForTileTint(Color newTileTint)
        {
            try
            {
                // Create HSL-shifted copies for tiles and saw frames
                if (tileImages != null)
                    tileTonedImages = CreateHslShiftedImages(tileImages, newTileTint);

                if (sawFrame1TilesTinted != null && sawFrame1TilesTinted.Length > 0)
                {
                    // If we already had tinted saw frames passed in, re-tint the original saw frames
                    // Fallback: if original arrays are null, do nothing
                }

                // Regenerate saw-frame tinted copies from stored originals so they follow tile tint.
                try
                {
                    sawFrame1TilesTinted = CreateHslShiftedImages(sawFrame1TilesOrig, newTileTint);
                    sawFrame2TilesTinted = CreateHslShiftedImages(sawFrame2TilesOrig, newTileTint);
                    smallSawFrame1TilesTinted = CreateHslShiftedImages(smallSawFrame1TilesOrig, newTileTint);
                    smallSawFrame2TilesTinted = CreateHslShiftedImages(smallSawFrame2TilesOrig, newTileTint);
                    largeSawFrame1TilesTinted = CreateHslShiftedImages(largeSawFrame1TilesOrig, newTileTint);
                    largeSawFrame2TilesTinted = CreateHslShiftedImages(largeSawFrame2TilesOrig, newTileTint);
                }
                catch { }
            }
            catch { }
        }

        // Create a black-masked copy of a single image: non-white pixels become black (alpha preserved).
        private ImageSource? CreateBlackMaskedImage(ImageSource? src)
        {
            if (src == null) return null;
            if (!(src is BitmapSource bs)) return src;
            try
            {
                var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                int w = Math.Max(1, conv.PixelWidth);
                int h = Math.Max(1, conv.PixelHeight);
                int stride = w * 4;
                var pixels = new byte[h * stride];
                conv.CopyPixels(pixels, stride, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i + 0];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];
                    if (a == 0) continue;
                    // Use perceptual luminance to detect near-white (preserve thin white lines
                    // and anti-aliased edge pixels). This is more tolerant than strict RGB
                    // component tests used previously which could drop subtle white pixels.
                    double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                    bool isWhite = lum >= 0.82; // keep pixels that are ~82% luminance or above
                    if (isWhite)
                    {
                        // promote to full white color but keep original alpha to preserve edges
                        pixels[i + 0] = 255;
                        pixels[i + 1] = 255;
                        pixels[i + 2] = 255;
                    }
                    else
                    {
                        // make pixel black; keep alpha as-is to preserve anti-aliased edges
                        pixels[i + 0] = 0; // b
                        pixels[i + 1] = 0; // g
                        pixels[i + 2] = 0; // r
                    }
                }
                var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                wb.Freeze();
                return wb;
            }
            catch { return src; }
        }

        // Create HSL-hue shifted copies of images. For each visible, non-black/non-white pixel
        // we replace the hue with the tint's hue while preserving the original saturation and lightness.
        // Returns originals if tint.A == 0.
        private ImageSource[]? CreateHslShiftedImages(ImageSource[]? originals, Color tint)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // no change requested
            // Precompute tint hue
            RgbToHsl(tint.R, tint.G, tint.B, out double tintH, out double tintS, out double tintL);

            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            byte a = pixels[i + 3];
                            // Skip fully transparent, near-black, and near-white pixels
                            bool isBlack = (r <= 12 && g <= 12 && b <= 12);
                            bool isWhite = (r >= 249 && g >= 249 && b >= 249);
                            if (a == 0 || isBlack || isWhite) continue;

                            // Convert pixel to HSL, replace hue with tint hue, keep S/L
                            RgbToHsl(r, g, b, out double ph, out double ps, out double pl);
                            double nh = tintH; // replace hue
                            double ns = ps;
                            double nl = pl;
                            RgbFromHsl(nh, ns, nl, out byte nr, out byte ng, out byte nb);

                            pixels[i + 0] = nb;
                            pixels[i + 1] = ng;
                            pixels[i + 2] = nr;
                            // alpha unchanged
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        wb.Freeze();
                        outList.Add(wb);
                    }
                    catch
                    {
                        outList.Add(src);
                    }
                }
                else
                {
                    outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        // Create a simple 2-frame pulsing animation from a single sprite image by producing
        // a slightly brightened second frame. Returns null on failure.
        private ImageSource?[]? CreateTwoFramePulse(ImageSource src)
        {
            try
            {
                if (src is BitmapSource bs)
                {
                    // Convert to BGRA32
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                    int w = Math.Max(1, conv.PixelWidth);
                    int h = Math.Max(1, conv.PixelHeight);
                    int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    // Create brightened copy by blending each color toward white (alpha preserved)
                    var bright = new byte[h * stride];
                    const double factor = 0.35; // how strongly to brighten
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte b = pixels[i + 0];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];
                        byte a = pixels[i + 3];
                        if (a == 0) { bright[i + 0] = b; bright[i + 1] = g; bright[i + 2] = r; bright[i + 3] = a; continue; }
                        bright[i + 0] = (byte)Math.Min(255, (int)Math.Round(b + (255 - b) * factor));
                        bright[i + 1] = (byte)Math.Min(255, (int)Math.Round(g + (255 - g) * factor));
                        bright[i + 2] = (byte)Math.Min(255, (int)Math.Round(r + (255 - r) * factor));
                        bright[i + 3] = a;
                    }

                    var wb1 = new WriteableBitmap(conv.PixelWidth, conv.PixelHeight, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                    wb1.WritePixels(new Int32Rect(0, 0, conv.PixelWidth, conv.PixelHeight), pixels, stride, 0);
                    wb1.Freeze();
                    var wb2 = new WriteableBitmap(conv.PixelWidth, conv.PixelHeight, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                    wb2.WritePixels(new Int32Rect(0, 0, conv.PixelWidth, conv.PixelHeight), bright, stride, 0);
                    wb2.Freeze();
                    return new ImageSource?[] { wb1, wb2 };
                }
            }
            catch { }
            return null;
        }

        // Create hue-shifted images with interpolation towards tint hue (used for ground in MainWindow)
        private ImageSource[]? CreateHueShiftedImages(ImageSource[]? originals, Color tint)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // strength 0 => no change
            // If the tint is pure opaque black, produce black-masked images that
            // are solid black except for preserved near-white details (white line).
            if (tint.A == 255 && tint.R == 0 && tint.G == 0 && tint.B == 0)
            {
                return CreateBlackMaskedImages(originals);
            }

            double strength = tint.A / 255.0;
            // convert tint color to HSL once
            RgbToHsl(tint.R, tint.G, tint.B, out double tintH, out double tintS, out double tintL);
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);

            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            int b = pixels[i + 0];
                            int g = pixels[i + 1];
                            int r = pixels[i + 2];
                            int a = pixels[i + 3];

                            RgbToHsl((byte)r, (byte)g, (byte)b, out double h0, out double s0, out double l0);

                            // interpolate hue towards tint hue, and optionally scale/lerp saturation
                            double newH = LerpAngle(h0, tintH, strength);
                            double newS = s0 * (1.0 - strength) + tintS * strength;
                            double newL = l0; // preserve original lightness to keep details

                            RgbFromHsl(newH, newS, newL, out byte r2, out byte g2, out byte b2);

                            pixels[i + 0] = b2;
                            pixels[i + 1] = g2;
                            pixels[i + 2] = r2;
                            pixels[i + 3] = (byte)a; // keep original alpha
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        wb.Freeze();
                        outList.Add(wb);
                    }
                    catch
                    {
                        outList.Add(src);
                    }
                }
                else
                {
                    outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        // Create images where non-white pixels become solid black (alpha preserved for transparent pixels),
        // and near-white pixels are preserved as white. This produces a 'black with white line' effect
        // useful for pure-black triggers.
        private ImageSource[]? CreateBlackMaskedImages(ImageSource[]? originals)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            byte a = pixels[i + 3];
                            if (a == 0) continue; // preserve transparency
                            bool isWhite = (r >= 249 && g >= 249 && b >= 249);
                            if (isWhite)
                            {
                                pixels[i + 0] = 255; pixels[i + 1] = 255; pixels[i + 2] = 255;
                            }
                            else
                            {
                                pixels[i + 0] = 0; pixels[i + 1] = 0; pixels[i + 2] = 0;
                            }
                            pixels[i + 3] = 255; // make opaque
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        wb.Freeze();
                        outList.Add(wb);
                    }
                    catch
                    {
                        outList.Add(src);
                    }
                }
                else
                {
                    outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        // Helper: convert RGB byte values to HSL (H in degrees 0..360, S/L 0..1)
        private static void RgbToHsl(byte r8, byte g8, byte b8, out double h, out double s, out double l)
        {
            double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            if (max == min)
            {
                h = 0.0; s = 0.0; return;
            }
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60.0;
        }

        // Helper: convert HSL to RGB bytes. H in degrees 0..360, S/L 0..1
        private static void RgbFromHsl(double h, double s, double l, out byte r8, out byte g8, out byte b8)
        {
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l; // achromatic
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                double hk = (h % 360.0) / 360.0;
                double[] t = new double[3] { hk + 1.0 / 3.0, hk, hk - 1.0 / 3.0 };
                double[] rgb = new double[3];
                for (int i = 0; i < 3; i++)
                {
                    double tc = t[i];
                    if (tc < 0) tc += 1.0; if (tc > 1) tc -= 1.0;
                    if (tc < 1.0 / 6.0) rgb[i] = p + (q - p) * 6.0 * tc;
                    else if (tc < 1.0 / 2.0) rgb[i] = q;
                    else if (tc < 2.0 / 3.0) rgb[i] = p + (q - p) * (2.0 / 3.0 - tc) * 6.0;
                    else rgb[i] = p;
                }
                r = rgb[0]; g = rgb[1]; b = rgb[2];
            }
            r8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(r * 255.0)));
            g8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(g * 255.0)));
            b8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(b * 255.0)));
        }

        // Linear interpolation for circular hue (degrees). t in 0..1
        private static double LerpAngle(double a, double b, double t)
        {
            // convert to radians for shortest path
            double diff = (b - a + 540.0) % 360.0 - 180.0;
            return (a + diff * t + 360.0) % 360.0;
        }
    }
}

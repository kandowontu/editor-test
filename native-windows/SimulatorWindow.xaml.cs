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
        private BitmapSource? groundBitmap;
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

        // Track color-trigger anchors that have already been processed (so we don't resample every frame)
        private System.Collections.Generic.HashSet<int> processedColorTriggers = new System.Collections.Generic.HashSet<int>();

        private bool upHeld = false;
        private bool downHeld = false;
        private bool tabHeld = false;

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
                if (animationFrames != null && previewSpriteMap != null)
                {
                    foreach (var id in decorationSpriteIds)
                    {
                        if (!animationFrames.ContainsKey(id))
                        {
                            if (previewSpriteMap.TryGetValue(id, out var pimg) && pimg != null)
                            {
                                var frames = CreateTwoFramePulse(pimg);
                                if (frames != null) animationFrames[id] = frames;
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
            if (e.Key == Key.Tab) tabHeld = true;
        }

        private void SimulatorWindow_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up) upHeld = false;
            if (e.Key == Key.Down) downHeld = false;
            if (e.Key == Key.Tab) tabHeld = false;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Keep previous center so we can detect crossings
            int prevCenter_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);

            // Advance camera X by current dynamic speed (fixed-point)
            // Holding TAB doubles horizontal movement speed.
            int speedMultiplier = tabHeld ? 2 : 1;
            cameraX_fixed += currentSpeed_fixed * speedMultiplier;

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
                // We moved right only; find any speed portal anchors whose anchor X lies in (prevCenter, center]
                int bestAnchor_fixed = int.MaxValue;
                int? newSpeed_fixed = null;

                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;
                    if (!speedPortalMap.ContainsKey(sid)) continue;

                    // Determine anchor tile X for this sprite: prefer explicit anchor, otherwise use tile position
                    int anchorTileX;
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a))
                        anchorTileX = a.anchorTileX;
                    else
                        anchorTileX = idx % mapWidth;

                    // Anchor center pixel (fixed)
                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                    if (anchorX_center_fixed > prevCenter_fixed && anchorX_center_fixed <= center_fixed)
                    {
                        if (anchorX_center_fixed < bestAnchor_fixed)
                        {
                            bestAnchor_fixed = anchorX_center_fixed;
                            newSpeed_fixed = speedPortalMap[sid];
                        }
                    }
                }

                // Also detect color-trigger crossings. To reduce sampling cost, only sample a trigger
                // once when it first moves past the center line. Keep a set of processed anchors so
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

                    // If anchor has already moved past center, and we've already processed it, skip.
                    if (anchorX_center_fixed <= center_fixed)
                    {
                        if (processedColorTriggers.Contains(idx))
                        {
                            // already handled previously
                        }
                        else
                        {
                            // Newly past-center trigger; consider for nearest selection per category
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

                // Apply detected color triggers: compute color and set tints accordingly. Mark anchors processed
                try
                {
                        if (bgIdx.HasValue && bgSid.HasValue)
                    {
                        var c = ColorFromTrigger(bgSid.Value);
                        backgroundTint = c; // update field used for drawing background
                        processedColorTriggers.Add(bgIdx.Value);
                    }
                    if (tileIdx.HasValue && tileSid.HasValue)
                    {
                        var c = ColorFromTrigger(tileSid.Value);
                        tileTint = c;
                        processedColorTriggers.Add(tileIdx.Value);
                    }
                    if (groundIdx.HasValue && groundSid.HasValue)
                    {
                        var c = ColorFromTrigger(groundSid.Value);
                        groundTint = c;
                        processedColorTriggers.Add(groundIdx.Value);
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
                        // invalidate cached tile layer so it is rebuilt with new toned images
                        tileLayerCache = null;
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

            // If ground is present in the preview, reserve up to two ground rows at the bottom
            int groundRowsToReserve = 0;
            if (hasGroundLayer && groundTileRows > 0)
            {
                // Reserve exactly 2 rows when a ground layer exists (or fewer if ground bitmap has <2 rows)
                groundRowsToReserve = Math.Min(2, groundTileRows);
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
                        if (!spriteFrameOffsets.ContainsKey(idx)) spriteFrameOffsets[idx] = spriteAnimationRandom.Next(0, Math.Max(1, frames.Length));
                        int offset = spriteFrameOffsets[idx];
                        int frame = (((animationFrame * 9) / 20) + offset) % Math.Max(1, frames.Length);
                        chosenSprite = frames[frame];
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
                    simg.Source = chosenSprite;
                    if (chosenSprite is BitmapSource bs) { simg.Width = bs.PixelWidth; simg.Height = bs.PixelHeight; }
                    System.Windows.Controls.Canvas.SetLeft(simg, px);
                    System.Windows.Controls.Canvas.SetTop(simg, py);
                    spritesInUse++;

                    
                }
            }

            // Hide remaining pooled images
            for (int i = spritesInUse; i < spritePool.Count; i++) spritePool[i].Visibility = Visibility.Collapsed;

            // Apply sub-pixel smoothing with a translate transform for X and Y
            double fracX = (cameraX_fixed & 0xFF) / 256.0;
            double fracY = (cameraY_fixed & 0xFF) / 256.0;
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
                if (high == 0x80 || high == 0x90 || high == 0xA0 || high == 0xC0 || high == 0xD0 || high == 0xE0)
                    return false;
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
            // Include 0xCF as valid ground trigger per request
            return (spriteIdx >= 0xC0 && spriteIdx <= 0xEC || spriteIdx == 0xCF) && IsColorTriggerSprite(spriteIdx);
        }

        private Color ColorFromTrigger(int spriteIdx)
        {
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
                long key = (((long)spriteId) << 32) | ((long)playerTint.A << 24) | ((long)playerTint.R << 16) | ((long)playerTint.G << 8) | playerTint.B;
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

                // For saw frames we don't have originals here; MainWindow may have passed tinted versions already.
                // If those originals are available elsewhere in the simulator, we could re-tint them here.
            }
            catch { }
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

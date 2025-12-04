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
        private readonly ImageSource[]? tileTonedImages;
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
        private readonly ImageSource[]? sawFrame1TilesTinted;
        private readonly ImageSource[]? sawFrame2TilesTinted;
        private readonly ImageSource[]? smallSawFrame1TilesTinted;
        private readonly ImageSource[]? smallSawFrame2TilesTinted;
        private readonly ImageSource[]? largeSawFrame1TilesTinted;
        private readonly ImageSource[]? largeSawFrame2TilesTinted;

        private const int NES_W = 16; // horizontal tiles (was 15)
        private const int NES_H = 15; // vertical tiles (was 16)
        private const int TILE = 16;

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

        // Track color-trigger anchors that have already been processed (so we don't resample every frame)
        private System.Collections.Generic.HashSet<int> processedColorTriggers = new System.Collections.Generic.HashSet<int>();

        private bool upHeld = false;
        private bool downHeld = false;

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
                RenderCanvas.Children.Add(tileLayerImage);

                groundRectPersistent = new System.Windows.Shapes.Rectangle
                {
                    Width = RenderCanvas.Width,
                    Height = TILE * Math.Min(NES_H, 2),
                    Fill = new SolidColorBrush(groundTint) { Opacity = 0.25 }
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
                bool showFrame2 = (((animationFrame * 3) / 4) % 2) == 1;
                int tileOffset = mapped - 0x08;
                return showFrame2 ? 1004 + tileOffset : 1000 + tileOffset;
            }

            if (mapped == 0x04 || mapped == 0x7D || mapped == 0x7F)
            {
                bool showFrame2 = (((animationFrame * 3) / 4) % 2) == 1;
                int tileOffset = (mapped == 0x04) ? 0 : (mapped == 0x7D) ? 1 : 2;
                return showFrame2 ? 1013 + tileOffset : 1010 + tileOffset;
            }

            if (mapped >= 0x74 && mapped <= 0x7C)
            {
                bool showFrame2 = (((animationFrame * 3) / 4) % 2) == 1;
                int tileOffset = mapped - 0x74;
                return showFrame2 ? 1029 + tileOffset : 1020 + tileOffset;
            }

            return originalIndex;
        }

        private void SimulatorWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up) upHeld = true;
            if (e.Key == Key.Down) downHeld = true;
        }

        private void SimulatorWindow_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up) upHeld = false;
            if (e.Key == Key.Down) downHeld = false;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Keep previous center so we can detect crossings
            int prevCenter_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);

            // Advance camera X by current dynamic speed (fixed-point)
            cameraX_fixed += currentSpeed_fixed;

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
            int startTileY = pixelY / TILE;
            int offsetY = pixelY % TILE;

            // Update persistent background tint
            try { if (bgRectPersistent != null) bgRectPersistent.Fill = new SolidColorBrush(backgroundTint); } catch { }

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
                        for (int vx = 0; vx <= NES_W; vx++)
                        {
                            int mapX = startTileX + vx;
                            for (int vy = 0; vy <= NES_H; vy++)
                            {
                                int mapY = startTileY + vy;
                                Rect dest = new Rect(vx * TILE, vy * TILE, TILE, TILE);
                                if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight)
                                {
                                    dc.DrawRectangle(Brushes.Black, null, dest);
                                    continue;
                                }
                                int idx = mapY * mapWidth + mapX;
                                int t = tiles[idx];
                                int animatedTileIndex = MapAnimatedTileIndex(t);
                                if (animatedTileIndex != t || t >= 1000) hadAnimated = true;
                                int useTileIndex = animatedTileIndex;
                                ImageSource? chosenTile = null;
                                try
                                {
                                    if (t >= 1000)
                                    {
                                        bool frame2 = (((animationFrame * 3) / 40) % 2) != 0;
                                        if (t >= 1000 && t <= 1007)
                                        {
                                            int off = (t - 1000) % 4;
                                            chosenTile = frame2 ? (sawFrame2TilesTinted != null && off < sawFrame2TilesTinted.Length ? sawFrame2TilesTinted[off] : null)
                                                                 : (sawFrame1TilesTinted != null && off < sawFrame1TilesTinted.Length ? sawFrame1TilesTinted[off] : null);
                                        }
                                        else if (t >= 1010 && t <= 1015)
                                        {
                                            int off = (t - 1010) % 3;
                                            chosenTile = frame2 ? (smallSawFrame2TilesTinted != null && off < smallSawFrame2TilesTinted.Length ? smallSawFrame2TilesTinted[off] : null)
                                                                 : (smallSawFrame1TilesTinted != null && off < smallSawFrame1TilesTinted.Length ? smallSawFrame1TilesTinted[off] : null);
                                        }
                                        else if (t >= 1020 && t <= 1037)
                                        {
                                            int off = (t - 1020) % 9;
                                            chosenTile = frame2 ? (largeSawFrame2TilesTinted != null && off < largeSawFrame2TilesTinted.Length ? largeSawFrame2TilesTinted[off] : null)
                                                                 : (largeSawFrame1TilesTinted != null && off < largeSawFrame1TilesTinted.Length ? largeSawFrame1TilesTinted[off] : null);
                                        }
                                    }
                                }
                                catch { }

                                if (chosenTile == null && useTileIndex >= 0)
                                {
                                    if (useTileIndex >= 1000)
                                    {
                                        if (tileTonedImages != null && t >= 0 && t < tileTonedImages.Length && tileTonedImages[t] != null)
                                            chosenTile = tileTonedImages[t];
                                        else if (tileImages != null && t >= 0 && t < tileImages.Length && tileImages[t] != null)
                                            chosenTile = tileImages[t];
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
                                    dc.DrawImage(chosenTile, dest);
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

            // Update ground tint rect
            try
            {
                if (groundRectPersistent != null)
                {
                    groundRectPersistent.Fill = new SolidColorBrush(groundTint) { Opacity = 0.25 };
                    int groundHeight = TILE * Math.Min(NES_H, 2);
                    groundRectPersistent.Height = groundHeight;
                    System.Windows.Controls.Canvas.SetTop(groundRectPersistent, (NES_H * TILE) - groundHeight);
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
                    int mapY = startTileY + vy;
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

                    double px = (mapX - startTileX) * TILE - offsetX;
                    double py = (mapY - startTileY) * TILE - offsetY + gridRenderShiftYPx;
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                    {
                        int storageTileX = idx % mapWidth;
                        int storageTileY = idx / mapWidth;
                        int tileDeltaX = storageTileX - anchor.anchorTileX;
                        int tileDeltaY = storageTileY - anchor.anchorTileY;
                        px = (anchor.anchorTileX - startTileX) * TILE - offsetX + tileDeltaX * TILE;
                        py = (anchor.anchorTileY - startTileY) * TILE - offsetY + tileDeltaY * TILE + gridRenderShiftYPx;
                    }
                    if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs))
                    {
                        px += offs.offsetX;
                        py -= offs.offsetY;
                    }
                    else if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anc))
                    {
                        int anchorKey = anc.anchorTileY * mapWidth + anc.anchorTileX;
                        if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var aoffs))
                        {
                            px += aoffs.offsetX;
                            py -= aoffs.offsetY;
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
    }
}

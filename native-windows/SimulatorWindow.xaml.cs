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
        private readonly System.Collections.Generic.Dictionary<int, ImageSource[]?>? animationFrames;

        private const int NES_W = 15;
        private const int NES_H = 16;
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

        private readonly DispatcherTimer timer;

        private int animationFrame = 0;
        private System.Collections.Generic.Dictionary<int, int> spriteFrameOffsets = new System.Collections.Generic.Dictionary<int, int>();
        private Random spriteAnimationRandom = new Random();

        private readonly System.Collections.Generic.HashSet<int> decorationSpriteIds = new System.Collections.Generic.HashSet<int> { 0x36, 0x32, 0x33, 0x34, 0x35, 0x37, 0x2C, 0x3C, 0x2D, 0x3D, 0x2E, 0x2F, 0x30, 0x31, 0x38, 0x39, 0x3E, 0x3F, 0x2B, 0x3B, 0x2A, 0x3A, 0x49, 0x4A };

        private bool upHeld = false;
        private bool downHeld = false;

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
            System.Collections.Generic.Dictionary<int, ImageSource[]?>? animationFrames = null
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
            this.animationFrames = animationFrames ?? new System.Collections.Generic.Dictionary<int, ImageSource[]?>();

            // Debug: report which sprite IDs present in this level have preview replacements
            try
            {
                var ids = new System.Collections.Generic.HashSet<int>(sprites);
                var present = new System.Collections.Generic.List<int>();
                var missing = new System.Collections.Generic.List<int>();
                foreach (var id in ids)
                {
                    if (id < 0) continue;
                    if (this.previewSpriteMap != null && this.previewSpriteMap.TryGetValue(id, out var img) && img != null)
                        present.Add(id);
                    else
                        missing.Add(id);
                }
                System.Diagnostics.Debug.WriteLine($"Simulator preview map: present={present.Count}, missing={missing.Count}");
                if (missing.Count > 0)
                {
                    var sample = string.Join(",", missing.Take(32));
                    System.Diagnostics.Debug.WriteLine($"Missing preview sprite IDs (sample up to 32): {sample}");
                }
            }
            catch { }

            // Clamp initial cameraY so visible region fits (fixed-point)
            int maxY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            // Start the simulator from the bottom of the map by default
            cameraY_fixed = maxY_fixed;

            // Setup a 60FPS timer
            timer = new DispatcherTimer(DispatcherPriority.Render);
            timer.Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0);
            timer.Tick += Timer_Tick;
            timer.Start();

            this.KeyDown += SimulatorWindow_KeyDown;
            this.KeyUp += SimulatorWindow_KeyUp;
            this.Closed += (s, e) => timer.Stop();

            RenderCanvas.Width = NES_W * TILE;
            RenderCanvas.Height = NES_H * TILE;
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
            const int panStep_fixed = 1024; // 4 pixels per frame (256 = 1px)
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

                // Also detect color-trigger crossings (and collect the nearest one crossed per category)
                int bestBg_fixed = int.MaxValue; int? bgSprite = null;
                int bestTile_fixed = int.MaxValue; int? tileSprite = null;
                int bestGround_fixed = int.MaxValue; int? groundSprite = null;

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
                    if (!(anchorX_center_fixed > prevCenter_fixed && anchorX_center_fixed <= center_fixed)) continue;

                    // Classify trigger type
                    if (IsBackgroundTrigger(sid))
                    {
                        if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgSprite = sid; }
                    }
                    else if (IsTileTrigger(sid))
                    {
                        if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileSprite = sid; }
                    }
                    else if (IsGroundTrigger(sid))
                    {
                        if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundSprite = sid; }
                    }
                }

                // Apply detected color triggers: compute color and set tints accordingly
                try
                {
                    if (bgSprite.HasValue)
                    {
                        var c = ColorFromTrigger(bgSprite.Value);
                        backgroundTint = c; // update field used for drawing background
                        System.Diagnostics.Debug.WriteLine($"Simulator: background tint set from trigger 0x{bgSprite.Value:X2}");
                    }
                    if (tileSprite.HasValue)
                    {
                        var c = ColorFromTrigger(tileSprite.Value);
                        tileTint = c;
                        System.Diagnostics.Debug.WriteLine($"Simulator: tile tint set from trigger 0x{tileSprite.Value:X2}");
                    }
                    if (groundSprite.HasValue)
                    {
                        var c = ColorFromTrigger(groundSprite.Value);
                        groundTint = c;
                        System.Diagnostics.Debug.WriteLine($"Simulator: ground tint set from trigger 0x{groundSprite.Value:X2}");
                    }
                }
                catch { }

                if (newSpeed_fixed.HasValue)
                {
                    currentSpeed_fixed = newSpeed_fixed.Value;
                    System.Diagnostics.Debug.WriteLine($"Simulator: speed changed to 0x{currentSpeed_fixed:X} due to speed portal crossing.");
                }
            }
            catch { }

            RenderFrame();
        }

        private void RenderFrame()
        {
            // Clear
            RenderCanvas.Children.Clear();

            // Compute pixel offset and starting tile index
            int pixelX = cameraX_fixed >> 8; // full pixels
            int subPixel = cameraX_fixed & 0xFF; // fractional
            int startTileX = pixelX / TILE;
            int offsetX = pixelX % TILE;
            int pixelY = cameraY_fixed >> 8;
            int startTileY = pixelY / TILE;
            int offsetY = pixelY % TILE;

            // Draw background tint
            var bgRect = new System.Windows.Shapes.Rectangle
            {
                Width = RenderCanvas.Width,
                Height = RenderCanvas.Height,
                Fill = new SolidColorBrush(backgroundTint)
            };
            System.Windows.Controls.Canvas.SetLeft(bgRect, 0);
            System.Windows.Controls.Canvas.SetTop(bgRect, 0);
            RenderCanvas.Children.Add(bgRect);

            // For each visible tile column
            for (int vx = 0; vx < NES_W; vx++)
            {
                int mapX = startTileX + vx;
                for (int vy = 0; vy < NES_H; vy++)
                {
                    int mapY = startTileY + vy;
                    if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight)
                    {
                        // draw a blank rectangle for out-of-bounds
                        var rect = new System.Windows.Shapes.Rectangle
                        {
                            Width = TILE,
                            Height = TILE,
                            Fill = Brushes.Black
                        };
                        System.Windows.Controls.Canvas.SetLeft(rect, vx * TILE - (offsetX));
                        System.Windows.Controls.Canvas.SetTop(rect, vy * TILE);
                        RenderCanvas.Children.Add(rect);
                    }
                    else
                    {
                        int idx = mapY * mapWidth + mapX;
                        int t = tiles[idx];
                        // Prefer toned tile images when available
                        ImageSource? chosenTile = null;
                        if (t >= 0)
                        {
                            if (tileTonedImages != null && t < tileTonedImages.Length && tileTonedImages[t] != null)
                                chosenTile = tileTonedImages[t];
                            else if (tileImages != null && t < tileImages.Length && tileImages[t] != null)
                                chosenTile = tileImages[t];
                        }

                        if (chosenTile != null)
                        {
                            var img = new System.Windows.Controls.Image
                            {
                                Source = chosenTile,
                                Width = TILE,
                                Height = TILE
                            };
                            RenderCanvas.Children.Add(img);
                            System.Windows.Controls.Canvas.SetLeft(img, vx * TILE - (offsetX));
                            System.Windows.Controls.Canvas.SetTop(img, vy * TILE);
                        }
                        else
                        {
                            // empty tile
                            var rect = new System.Windows.Shapes.Rectangle
                            {
                                Width = TILE,
                                Height = TILE,
                                Fill = Brushes.Black
                            };
                            System.Windows.Controls.Canvas.SetLeft(rect, vx * TILE - (offsetX));
                            System.Windows.Controls.Canvas.SetTop(rect, vy * TILE);
                            RenderCanvas.Children.Add(rect);
                        }
                    }
                }
            }

            // Draw ground tint area (a subtle band at bottom). Show up to 2 tile rows of ground.
            var groundHeight = TILE * Math.Min(NES_H, 2); // bottom 2 rows
            var groundRect = new System.Windows.Shapes.Rectangle
            {
                Width = RenderCanvas.Width,
                Height = groundHeight,
                Fill = new SolidColorBrush(groundTint) { Opacity = 0.25 }
            };
            System.Windows.Controls.Canvas.SetLeft(groundRect, 0);
            System.Windows.Controls.Canvas.SetTop(groundRect, (NES_H * TILE) - groundHeight);
            RenderCanvas.Children.Add(groundRect);

            // Render sprites on top (always in preview)
            for (int vx = 0; vx < NES_W; vx++)
            {
                int mapX = startTileX + vx;
                for (int vy = 0; vy < NES_H; vy++)
                {
                    int mapY = startTileY + vy;
                    if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight) continue;
                    int idx = mapY * mapWidth + mapX;
                    int s = sprites[idx];
                    if (s < 0) continue; // -1 means empty

                    // Animated frames: if we have an animationFrames entry for this sprite id, pick a frame
                    ImageSource? chosenSprite = null;
                    if (animationFrames != null && animationFrames.TryGetValue(s, out var frames) && frames != null && frames.Length > 0)
                    {
                        // per-position offset to desync animations
                        if (!spriteFrameOffsets.ContainsKey(idx)) spriteFrameOffsets[idx] = spriteAnimationRandom.Next(0, frames.Length);
                        int offset = spriteFrameOffsets[idx];
                        int frame = (((animationFrame * 9) / 20) + offset) % frames.Length;
                        chosenSprite = frames[frame];
                    }
                    else
                    {
                        // Choose preview replacement when forced and available; otherwise use sliced sprite image
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
                    // If hide color triggers option is enabled, skip rendering those sprites
                    if (hideColorTriggers && IsColorTriggerSprite(s)) continue;

                    // base position in viewport
                    double px = (mapX - startTileX) * TILE - offsetX;
                    double py = (mapY - startTileY) * TILE - offsetY + gridRenderShiftYPx;

                    // If an anchor is present for this sprite position, use it as placement base
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                    {
                        px = (anchor.anchorTileX - startTileX) * TILE - offsetX;
                        py = (anchor.anchorTileY - startTileY) * TILE - offsetY + gridRenderShiftYPx;
                    }

                    // apply pixel offsets if present (offsets keyed by position index)
                    if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs))
                    {
                        px += offs.offsetX;
                        py += offs.offsetY;
                    }

                    var simg = new System.Windows.Controls.Image
                    {
                        Source = chosenSprite,
                        Stretch = Stretch.None
                    };
                    // Use the native pixel size of the sprite image when available
                    if (chosenSprite is BitmapSource bs)
                    {
                        simg.Width = bs.PixelWidth;
                        simg.Height = bs.PixelHeight;
                    }
                    RenderCanvas.Children.Add(simg);
                    System.Windows.Controls.Canvas.SetLeft(simg, px);
                    System.Windows.Controls.Canvas.SetTop(simg, py);

                    // apply player tint overlay to decoration sprites only if enabled
                    if (playerTintEnabled && decorationSpriteIds.Contains(s))
                    {
                        var overlay = new System.Windows.Shapes.Rectangle
                        {
                            Width = simg.Width,
                            Height = simg.Height,
                            Fill = new SolidColorBrush(playerTint) { Opacity = 0.25 }
                        };
                        RenderCanvas.Children.Add(overlay);
                        System.Windows.Controls.Canvas.SetLeft(overlay, px);
                        System.Windows.Controls.Canvas.SetTop(overlay, py);
                    }
                }
            }

            // Apply sub-pixel smoothing with a translate transform for X and Y
            double fracX = (cameraX_fixed & 0xFF) / 256.0;
            double fracY = (cameraY_fixed & 0xFF) / 256.0;
            RenderCanvas.RenderTransform = new TranslateTransform(-fracX, -fracY);
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
            return spriteIdx >= 0x80 && spriteIdx <= 0xAC && IsColorTriggerSprite(spriteIdx);
        }

        private bool IsTileTrigger(int spriteIdx)
        {
            return spriteIdx >= 0xB0 && spriteIdx <= 0xBF && IsColorTriggerSprite(spriteIdx);
        }

        private bool IsGroundTrigger(int spriteIdx)
        {
            return spriteIdx >= 0xC0 && spriteIdx <= 0xEC && IsColorTriggerSprite(spriteIdx);
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

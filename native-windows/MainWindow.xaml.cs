using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamidashEditor
{
    public partial class MainWindow : Window
    {
    private bool initialLeftSizingDone = false;
    private bool suppressManualTileChange = false;
    private bool suppressManualSpriteChange = false;
    private const int TileSize = 16;
    private bool isAdjustingPanels = false;
    private int mapWidth = 200;
    private int mapHeight = 27;
    private int[] tiles = Array.Empty<int>();
    private double gridDarkness = 0.5;
    private Brush mapBackground = new SolidColorBrush(Color.FromRgb(59,59,59));
    private bool manualTileSize = false;
    private bool manualSpriteSize = false;

    // Palette and assets
    private BitmapSource? tilesetBitmap;
    private BitmapSource? spritesBitmap;
    private BitmapSource? parallaxBitmap;
    private BitmapSource? groundBitmap;
    private ImageSource[]? tileImages;
    private ImageSource[]? spriteImages;
    private ImageSource[]? parallaxImages;
    private ImageSource[]? groundImages;
    private int selectedTile = 0;
    private int selectedSprite = -1;
    private int paletteTileSize = 16;
    private int paletteSpriteSize = 16;

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();

            if (ZoomSlider != null) ZoomSlider.ValueChanged += (s, e) => Redraw();
            if (GridDarknessSlider != null) GridDarknessSlider.ValueChanged += GridDarknessSlider_ValueChanged;
            if (HeightSlider != null) HeightSlider.ValueChanged += HeightSlider_ValueChanged;
            if (SaveButton != null) SaveButton.Click += SaveButton_Click;
            if (LoadButton != null) LoadButton.Click += LoadButton_Click;
            if (ResizeButton != null) ResizeButton.Click += ResizeButton_Click;

            if (CanvasHost != null)
            {
                CanvasHost.MouseLeftButtonDown += CanvasHost_MouseLeftButtonDown;
                CanvasHost.MouseMove += CanvasHost_MouseMove;
                CanvasHost.MouseRightButtonDown += CanvasHost_MouseRightButtonDown;
            }

            InitDefaultMap();
                Loaded += (s, e) =>
                {
                    // Ensure initial layout completes before the first redraw so measurements are accurate.
                    LoadAssetsOnStart();
                    // Run left-column sizing and palette sizing after layout has run so ActualWidth/measure are available.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // Run initial sizing once; subsequent calls won't force window min/width to allow shrinking.
                        if (!initialLeftSizingDone)
                        {
                            UpdateLeftColumnWidth(initial: true);
                            initialLeftSizingDone = true;
                        }
                        else
                        {
                            UpdateLeftColumnWidth(initial: false);
                        }
                        AdjustPaletteSizes();
                        UpdateTilesPanelWidth();
                        Redraw();
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                };
            // Redraw when the viewport or scrollviewer size changes so the visible image updates.
            if (MapScrollViewer != null)
            {
                MapScrollViewer.SizeChanged += (_, __) => Redraw();
                MapScrollViewer.ScrollChanged += (_, __) => Redraw();
                MapScrollViewer.Loaded += (_, __) => Redraw();
            }
            // palette size sliders
            if (TileSizeSlider != null) TileSizeSlider.ValueChanged += (s, ev) => { UpdateLeftColumnWidth(); if (!suppressManualTileChange) manualTileSize = true; paletteTileSize = (int)ev.NewValue; PopulateTilesPanel(); };
            if (SpriteSizeSlider != null) SpriteSizeSlider.ValueChanged += (s, ev) => { if (!suppressManualSpriteChange) manualSpriteSize = true; paletteSpriteSize = (int)ev.NewValue; PopulateSpritesPanel(); };
            if (TilesPanel != null) TilesPanel.SizeChanged += (_, __) => AdjustPaletteSizes();
            if (SpritesPanel != null) SpritesPanel.SizeChanged += (_, __) => AdjustPaletteSizes();
            if (RootGrid != null) RootGrid.SizeChanged += (_, __) => UpdateTilesPanelWidth();
            if (BgColorButton != null) BgColorButton.Click += BgColorButton_Click;
            if (HeightSlider != null) HeightSlider.ValueChanged += (s, e) => { /* already wired above */ };
                // tool exclusivity: only one toggled at a time
                if (PlaceTool != null) PlaceTool.Checked += Tool_Checked;
                if (MoveTool != null) MoveTool.Checked += Tool_Checked;
                if (EraseTool != null) EraseTool.Checked += Tool_Checked;
                if (FillTool != null) FillTool.Checked += Tool_Checked;
                if (SelectTool != null) SelectTool.Checked += Tool_Checked;
        }

        private void HeightSlider_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int newH = Math.Max((int)HeightSlider.Minimum, Math.Min((int)HeightSlider.Maximum, (int)Math.Round(e.NewValue)));
            if (newH != mapHeight)
            {
                ResizeMap(mapWidth, newH);
            }
        }

        private void ResizeMap(int newWidth, int newHeight)
        {
            // preserve existing tiles where possible
            var newTiles = Enumerable.Repeat(-1, newWidth * newHeight).ToArray();
            int copyW = Math.Min(mapWidth, newWidth);
            // preserve bottom-aligned: existing content should remain at the bottom
            if (newHeight >= mapHeight)
            {
                int yOffset = newHeight - mapHeight; // add rows at top
                for (int y = 0; y < mapHeight; y++)
                {
                    for (int x = 0; x < copyW; x++)
                    {
                        newTiles[(y + yOffset) * newWidth + x] = tiles[y * mapWidth + x];
                    }
                }
            }
            else // newHeight < mapHeight -> remove rows from top, keep bottom rows
            {
                int startOldY = mapHeight - newHeight;
                for (int y = 0; y < newHeight; y++)
                {
                    for (int x = 0; x < copyW; x++)
                    {
                        newTiles[y * newWidth + x] = tiles[(y + startOldY) * mapWidth + x];
                    }
                }
            }
            mapWidth = newWidth; mapHeight = newHeight; tiles = newTiles;
            if (WidthBox != null) WidthBox.Text = mapWidth.ToString();
            if (HeightBox != null) HeightBox.Text = mapHeight.ToString();
            if (MapHeightLabel != null) MapHeightLabel.Text = mapHeight.ToString();
            if (HeightSlider != null && (int)Math.Round(HeightSlider.Value) != mapHeight) HeightSlider.Value = mapHeight;
            Redraw();
        }

        private void BgColorButton_Click(object? sender, RoutedEventArgs e)
        {
            var brush = (SolidColorBrush?)Resources["AppBackgroundBrush"];
            var initial = brush != null ? brush.Color : Color.FromRgb(40, 40, 40);
            var dlg = new ColorPickerWindow(initial) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var newColor = dlg.SelectedColor;
                // update resource brush (so XAML backgrounds using it update)
                if (Resources.Contains("AppBackgroundBrush") && Resources["AppBackgroundBrush"] is SolidColorBrush sb)
                {
                    sb.Color = newColor;
                }
                mapBackground = new SolidColorBrush(newColor);
                SaveSettings(newColor);
                Redraw();
            }
        }

        private void LoadSettings()
        {
            try
            {
                var dir = AppContext.BaseDirectory;
                var path = System.IO.Path.Combine(dir, "editor-settings.json");
                if (System.IO.File.Exists(path))
                {
                    var txt = System.IO.File.ReadAllText(path);
                    var doc = System.Text.Json.JsonDocument.Parse(txt);
                    if (doc.RootElement.TryGetProperty("background", out var bg))
                    {
                        var r = (byte)bg[0].GetInt32();
                        var g = (byte)bg[1].GetInt32();
                        var b = (byte)bg[2].GetInt32();
                        var col = Color.FromRgb(r, g, b);
                        if (Resources.Contains("AppBackgroundBrush") && Resources["AppBackgroundBrush"] is SolidColorBrush sb)
                        {
                            sb.Color = col;
                        }
                        mapBackground = new SolidColorBrush(col);
                    }
                }
            }
            catch { }
        }

        private void SaveSettings(Color c)
        {
            try
            {
                var obj = new { background = new byte[] { c.R, c.G, c.B } };
                var txt = System.Text.Json.JsonSerializer.Serialize(obj);
                var dir = AppContext.BaseDirectory;
                var path = System.IO.Path.Combine(dir, "editor-settings.json");
                System.IO.File.WriteAllText(path, txt);
            }
            catch { }
        }

        private void InitDefaultMap()
        {
            tiles = Enumerable.Repeat(-1, mapWidth * mapHeight).ToArray();
            if (WidthBox != null) WidthBox.Text = mapWidth.ToString();
            if (HeightBox != null) HeightBox.Text = mapHeight.ToString();
        }

        // When initial=true, allow calling code to expand the window minimum width so the left column
        // can show 16 tiles across on first launch. For subsequent calls, avoid forcing the window
        // min/width so the user can shrink the window naturally.
        private void UpdateLeftColumnWidth(bool initial = false)
        {
            try
            {
                if (RootGrid == null) return;
                var col = RootGrid.ColumnDefinitions[0];
                double ts = (TileSizeSlider != null) ? TileSizeSlider.Value : paletteTileSize;
                // 16 tiles across, plus padding and the system vertical scrollbar width
                double scrollbar = SystemParameters.VerticalScrollBarWidth;
                double padding = 12;
                double desired = Math.Max(160, ts * 16 + scrollbar + padding);
                // set the left column width to desired
                col.Width = new GridLength(desired, GridUnitType.Pixel);

                if (initial)
                {
                    // On first run, expand the window MinWidth so the left column is fully visible.
                    double splitterWidth = (RootGrid.ColumnDefinitions.Count > 1) ? RootGrid.ColumnDefinitions[1].ActualWidth : 5;
                    double mainAreaMin = 300; // reasonable minimum for editor area
                    double required = desired + splitterWidth + mainAreaMin + 40; // extra margins
                    if (this.MinWidth < required) this.MinWidth = required;
                    if (this.Width < required) this.Width = required;
                }
            }
            catch { }
        }

        private void AdjustPaletteSizes()
        {
            try
            {
                // Default behavior: use the tile/sprite size sliders unless user manually adjusted them.
                if (!manualTileSize && TileSizeSlider != null)
                    paletteTileSize = Math.Max(8, (int)Math.Round(TileSizeSlider.Value));
                if (!manualSpriteSize && SpriteSizeSlider != null)
                    paletteSpriteSize = Math.Max(8, (int)Math.Round(SpriteSizeSlider.Value));
                PopulateTilesPanel();
                PopulateSpritesPanel();
                // After repopulating, adjust the ListBox widths to avoid clipping
                UpdateTilesPanelWidth();
            }
            catch { }
        }

        private void UpdateTilesPanelWidth()
        {
            try
            {
                if (RootGrid == null) return;
                if (isAdjustingPanels) return;
                double leftColActual = RootGrid.ColumnDefinitions[0].ActualWidth;
                if (leftColActual <= 0) return;

                // Try to find the internal ScrollViewer so we can read the viewport width which
                // reflects the actual horizontal space available for items (excludes scrollbar).
                double viewportTiles = 0;
                double viewportSprites = 0;
                var svTiles = GetInnerScrollViewer(TilesPanel);
                var svSprites = GetInnerScrollViewer(SpritesPanel);
                if (svTiles != null) viewportTiles = svTiles.ViewportWidth; else viewportTiles = TilesPanel.ActualWidth;
                if (svSprites != null) viewportSprites = svSprites.ViewportWidth; else viewportSprites = SpritesPanel.ActualWidth;

                // Fallback: if viewport is not available yet (0), use column width minus padding
                double fallback = Math.Max(0, leftColActual - 8);
                if (viewportTiles <= 0) viewportTiles = fallback;
                if (viewportSprites <= 0) viewportSprites = fallback;

                // Compute per-tile sizes from the viewport width (16 columns)
                int computedTiles = Math.Max(8, (int)Math.Floor(viewportTiles / 16.0));
                int computedSprites = Math.Max(8, (int)Math.Floor(viewportSprites / 16.0));

                // Update panels while guarding against recursive layout events
                isAdjustingPanels = true;
                try
                {
                    if (!manualTileSize && computedTiles != paletteTileSize)
                    {
                        paletteTileSize = computedTiles;
                        if (TileSizeSlider != null)
                        {
                            suppressManualTileChange = true;
                            TileSizeSlider.Value = paletteTileSize;
                            suppressManualTileChange = false;
                        }
                        PopulateTilesPanel();
                    }

                    if (!manualSpriteSize && computedSprites != paletteSpriteSize)
                    {
                        paletteSpriteSize = computedSprites;
                        if (SpriteSizeSlider != null)
                        {
                            suppressManualSpriteChange = true;
                            SpriteSizeSlider.Value = paletteSpriteSize;
                            suppressManualSpriteChange = false;
                        }
                        PopulateSpritesPanel();
                    }

                    // Let the ListBox stretch to the left column width instead of forcing a smaller width.
                    if (TilesPanel != null) TilesPanel.Width = Double.NaN;
                    if (SpritesPanel != null) SpritesPanel.Width = Double.NaN;
                }
                finally { isAdjustingPanels = false; }
            }
            catch { }
        }

        private ScrollViewer? GetInnerScrollViewer(DependencyObject? root)
        {
            if (root == null) return null;
            var queue = new System.Collections.Generic.Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                int count = VisualTreeHelper.GetChildrenCount(cur);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(cur, i);
                    if (child is ScrollViewer sv) return sv;
                    queue.Enqueue(child);
                }
            }
            return null;
        }

        private void LoadAssetsOnStart()
        {
            var dirs = new List<string>();
            var repo = FindRepoRootFor("famidash.bmp");
            if (!string.IsNullOrEmpty(repo)) { dirs.Add(repo); dirs.Add(Path.Combine(repo, "src", "renderer", "assets")); }
            dirs.Add(AppContext.BaseDirectory); dirs.Add(Path.Combine(AppContext.BaseDirectory, "assets"));

            var tilesCandidates = new[] { "famidash.bmp", "famidash.png", "tileset.bmp", "tileset.png" };
            var spriteCandidates = new[] { "sprites.png", "sprites.bmp" };
            var parallaxCandidates = new[] { "parallax.bmp", "parallax.png" };
            var groundCandidates = new[] { "ground.bmp", "ground.png" };

            foreach (var d in dirs)
            {
                try
                {
                    if (Directory.Exists(d))
                    {
                        foreach (var f in tilesCandidates) { var p = Path.Combine(d, f); if (File.Exists(p)) { LoadTileset(p); break; } }
                        foreach (var f in spriteCandidates) { var p = Path.Combine(d, f); if (File.Exists(p)) { LoadSpriteset(p); break; } }
                        foreach (var f in parallaxCandidates) { var p = Path.Combine(d, f); if (File.Exists(p)) { LoadParallax(p); break; } }
                        foreach (var f in groundCandidates) { var p = Path.Combine(d, f); if (File.Exists(p)) { LoadGround(p); break; } }
                    }
                }
                catch { }
            }
        }

        private string? FindRepoRootFor(string filename)
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                var candidate = Path.Combine(dir, filename);
                if (File.Exists(candidate)) return dir;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }

        private void LoadTileset(string path)
        {
            try
            {
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze();
                tilesetBitmap = bi; SliceTileset(); PopulateTilesPanel(); if (StatusText != null) StatusText.Text = "Loaded tileset: " + Path.GetFileName(path);
            }
            catch (Exception ex) { if (StatusText != null) StatusText.Text = "Tileset load failed: " + ex.Message; }
        }

        private void LoadSpriteset(string path)
        {
            try
            {
                var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze();
                spritesBitmap = bi; SliceSpriteset(); PopulateSpritesPanel(); if (StatusText != null) StatusText.Text = "Loaded sprites: " + Path.GetFileName(path);
            }
            catch (Exception ex) { if (StatusText != null) StatusText.Text = "Sprites load failed: " + ex.Message; }
        }

        private void LoadParallax(string path)
        {
            try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze(); parallaxBitmap = bi; SliceParallax(); if (StatusText != null) StatusText.Text = "Loaded parallax: " + Path.GetFileName(path); }
            catch (Exception ex) { if (StatusText != null) StatusText.Text = "Parallax load failed: " + ex.Message; }
        }

        private void LoadGround(string path)
        {
            try { var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(path); bi.EndInit(); bi.Freeze(); groundBitmap = bi; SliceGround(); if (StatusText != null) StatusText.Text = "Loaded ground: " + Path.GetFileName(path); }
            catch (Exception ex) { if (StatusText != null) StatusText.Text = "Ground load failed: " + ex.Message; }
        }

        private void SliceTileset()
        {
            if (tilesetBitmap == null) { tileImages = null; return; }
            int cols = Math.Max(1, tilesetBitmap.PixelWidth / TileSize);
            int rows = Math.Max(1, tilesetBitmap.PixelHeight / TileSize);
            var list = new List<ImageSource>();
            for (int y = 0; y < rows; y++) for (int x = 0; x < cols; x++) list.Add(new CroppedBitmap(tilesetBitmap, new Int32Rect(x * TileSize, y * TileSize, TileSize, TileSize)));
            tileImages = list.ToArray();
        }

        private void SliceSpriteset()
        {
            if (spritesBitmap == null) { spriteImages = null; return; }
            int cols = Math.Max(1, spritesBitmap.PixelWidth / TileSize);
            int rows = Math.Max(1, spritesBitmap.PixelHeight / TileSize);
            var list = new List<ImageSource>();
            for (int y = 0; y < rows; y++) for (int x = 0; x < cols; x++) list.Add(new CroppedBitmap(spritesBitmap, new Int32Rect(x * TileSize, y * TileSize, TileSize, TileSize)));
            spriteImages = list.ToArray();
        }

        private void SliceParallax()
        {
            if (parallaxBitmap == null) { parallaxImages = null; return; }
            int cols = Math.Max(1, parallaxBitmap.PixelWidth / TileSize);
            int rows = Math.Max(1, parallaxBitmap.PixelHeight / TileSize);
            var list = new List<ImageSource>();
            for (int y = 0; y < rows; y++) for (int x = 0; x < cols; x++) list.Add(new CroppedBitmap(parallaxBitmap, new Int32Rect(x * TileSize, y * TileSize, TileSize, TileSize)));
            parallaxImages = list.ToArray();
        }

        private void SliceGround()
        {
            if (groundBitmap == null) { groundImages = null; return; }
            int cols = Math.Max(1, groundBitmap.PixelWidth / TileSize);
            var list = new List<ImageSource>();
            for (int x = 0; x < cols; x++) list.Add(new CroppedBitmap(groundBitmap, new Int32Rect(x * TileSize, 0, TileSize, TileSize)));
            groundImages = list.ToArray();
        }

        private void PopulateTilesPanel()
        {
            if (TilesPanel == null) return;
            TilesPanel.Items.Clear();
            if (tileImages == null) return;
            int idx = 0;
            foreach (var src in tileImages)
            {
                var img = new Image { Source = src, Width = paletteTileSize, Height = paletteTileSize, Stretch = Stretch.Fill, Tag = idx };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                img.MouseLeftButtonDown += (s, e) => { selectedTile = (int)((Image)s).Tag; UpdateTileHighlight(); if (StatusText != null) StatusText.Text = "Selected tile " + selectedTile; };
                var border = new Border { Child = img, Margin = new Thickness(0), Padding = new Thickness(0), BorderBrush = (idx == selectedTile ? Brushes.Yellow : Brushes.Transparent), BorderThickness = (idx == selectedTile ? new Thickness(2) : new Thickness(0)) };
                TilesPanel.Items.Add(border);
                idx++;
            }
        }

        private void PopulateSpritesPanel()
        {
            if (SpritesPanel == null) return;
            SpritesPanel.Items.Clear();
            if (spriteImages == null) return;
            int idx = 0;
            foreach (var src in spriteImages)
            {
                var img = new Image { Source = src, Width = paletteSpriteSize, Height = paletteSpriteSize, Stretch = Stretch.Fill, Tag = idx };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                img.MouseLeftButtonDown += (s, e) => { selectedSprite = (int)((Image)s).Tag; if (StatusText != null) StatusText.Text = "Selected sprite " + selectedSprite; };
                var border = new Border { Child = img, Margin = new Thickness(0), Padding = new Thickness(0) };
                SpritesPanel.Items.Add(border);
                idx++;
            }
        }

        private void UpdateTileHighlight()
        {
            if (TilesPanel == null) return;
            for (int i = 0; i < TilesPanel.Items.Count; i++)
            {
                if (TilesPanel.Items[i] is Border b)
                {
                    b.BorderBrush = (i == selectedTile) ? Brushes.Yellow : Brushes.Transparent;
                    b.BorderThickness = (i == selectedTile) ? new Thickness(2) : new Thickness(0);
                }
            }
        }

        private void DrawMap()
        {
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            // Render the full map (not just the viewport) so the ScrollViewer content size is correct
            double fullW = mapWidth * TileSize * scale;
            double fullH = mapHeight * TileSize * scale;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Background (customizable)
                dc.DrawRectangle(mapBackground, null, new Rect(0, 0, fullW, fullH));

                // Draw placed tiles (if tileset loaded)
                if (tileImages != null)
                {
                    for (int y = 0; y < mapHeight; y++)
                    {
                        for (int x = 0; x < mapWidth; x++)
                        {
                            int idx = tiles[y * mapWidth + x];
                            if (idx >= 0 && idx < tileImages.Length)
                            {
                                var img = tileImages[idx];
                                double px = x * TileSize * scale;
                                double py = y * TileSize * scale;
                                dc.DrawImage(img, new Rect(px, py, TileSize * scale, TileSize * scale));
                            }
                        }
                    }
                }

                // Grid lines on top (allow darker values by scaling slider)
                // gridDarkness range normally 0..1; allow stronger darkness by multiplying
                int alpha = Math.Min(255, (int)(gridDarkness * 255 * 1.6));
                // use a lighter RGB for better contrast against dark backgrounds
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)alpha, 200, 200, 200)), 1.0);
                pen.Freeze();
                for (int y = 0; y < mapHeight; y++)
                    for (int x = 0; x < mapWidth; x++)
                    {
                        double px = x * TileSize * scale; double py = y * TileSize * scale;
                        dc.DrawRectangle(Brushes.Transparent, pen, new Rect(px, py, TileSize * scale, TileSize * scale));
                    }
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(fullW * dpi.DpiScaleX));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(fullH * dpi.DpiScaleY));
            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(dv);

            if (VisibleImage != null)
            {
                VisibleImage.Source = rtb;
                VisibleImage.Width = fullW; VisibleImage.Height = fullH;
            }
            if (CanvasHost != null)
            {
                CanvasHost.Width = fullW; CanvasHost.Height = fullH;
            }
        }

        private void Redraw() => DrawMap();

        private void CanvasHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CanvasHost == null) return;
            var pos = e.GetPosition(CanvasHost);
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(pos.X / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(pos.Y / (TileSize * scale))));
            // Handle according to active tool
            if (EraseTool != null && EraseTool.IsChecked == true)
            {
                tiles[y * mapWidth + x] = -1;
            }
            else if (FillTool != null && FillTool.IsChecked == true)
            {
                // flood fill
                int target = tiles[y * mapWidth + x];
                if (target != selectedTile)
                {
                    FloodFill(x, y, target, selectedTile);
                }
            }
            else if (MoveTool != null && MoveTool.IsChecked == true)
            {
                // pick tile into current selection
                selectedTile = tiles[y * mapWidth + x];
                UpdateTileHighlight();
            }
            else // Place or Select
            {
                tiles[y * mapWidth + x] = selectedTile;
            }
            Redraw();
        }

        private void CanvasHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (CanvasHost == null) return;
            var pos = e.GetPosition(CanvasHost);
            UpdateCoords(pos);
        }

        private void Tool_Checked(object? sender, RoutedEventArgs e)
        {
            // when one tool is checked, uncheck the others
            if (sender == null) return;
            var tb = sender as ToggleButton;
            if (tb == null) return;
            var all = new[] { PlaceTool, MoveTool, EraseTool, FillTool, SelectTool };
            foreach (var t in all)
            {
                if (t != tb) t.IsChecked = false;
            }
        }

        private void FloodFill(int sx, int sy, int target, int replacement)
        {
            if (target == replacement) return;
            var q = new System.Collections.Generic.Queue<(int x, int y)>();
            q.Enqueue((sx, sy));
            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue();
                if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) continue;
                if (tiles[y * mapWidth + x] != target) continue;
                tiles[y * mapWidth + x] = replacement;
                q.Enqueue((x + 1, y)); q.Enqueue((x - 1, y)); q.Enqueue((x, y + 1)); q.Enqueue((x, y - 1));
            }
        }

        private void CanvasHost_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CanvasHost == null) return;
            var pos = e.GetPosition(CanvasHost);
            UpdateCoords(pos);
        }

        private void UpdateCoords(Point p)
        {
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(p.X / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(p.Y / (TileSize * scale))));
            if (StatusText != null) StatusText.Text = $"Coords: {x}, {y}";
        }

        private void GridDarknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            gridDarkness = e.NewValue; Redraw();
        }

        private void ResizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(WidthBox.Text, out int w) && int.TryParse(HeightBox.Text, out int h) && w > 0 && h > 0)
            {
                ResizeMap(w, h);
            }
            else if (StatusText != null) StatusText.Text = "Invalid width/height";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "JSON level|*.json|All files|*.*" };
            if (dlg.ShowDialog(this) == true)
            {
                var model = new LevelModel { Width = mapWidth, Height = mapHeight, Tiles = tiles };
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(model));
                if (StatusText != null) StatusText.Text = "Saved " + dlg.FileName;
            }
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "JSON level|*.json|All files|*.*" };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    var model = JsonSerializer.Deserialize<LevelModel>(json);
                    if (model != null)
                    {
                        mapWidth = model.Width; mapHeight = model.Height; tiles = model.Tiles ?? Enumerable.Repeat(-1, mapWidth * mapHeight).ToArray();
                        if (WidthBox != null) WidthBox.Text = mapWidth.ToString(); if (HeightBox != null) HeightBox.Text = mapHeight.ToString();
                        Redraw();
                        if (StatusText != null) StatusText.Text = "Loaded " + dlg.FileName;
                    }
                }
                catch (Exception ex) { if (StatusText != null) StatusText.Text = "Load failed: " + ex.Message; }
            }
        }
    }
}

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
    // Painting state for drag-to-draw
    private bool isPainting = false;
    private int lastPaintX = -1;
    private int lastPaintY = -1;

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
                CanvasHost.MouseLeftButtonUp += CanvasHost_MouseLeftButtonUp;
                CanvasHost.MouseLeave += CanvasHost_MouseLeave;
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
                            // Run initial sizing once so 16 tiles fit across with no horizontal scroll.
                            if (!initialLeftSizingDone)
                            {
                                // ensure the left column width is reasonable but don't force window width
                                UpdateLeftColumnWidth(initial: true);
                                Ensure16VisibleOnStartup();
                                initialLeftSizingDone = true;
                            }
                            else
                            {
                                UpdateLeftColumnWidth(initial: false);
                                UpdateTilesPanelWidth();
                            }
                            AdjustPaletteSizes();
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
            if (TileSizeSlider != null) TileSizeSlider.ValueChanged += (s, ev) =>
            {
                // Tile slider adjusts only tile sizes, not the frame width. Enable horizontal scrolling
                // if tiles exceed the available area.
                if (!suppressManualTileChange) manualTileSize = true;
                paletteTileSize = (int)ev.NewValue;
                PopulateTilesPanel();
                // Allow horizontal scrolling when the user enlarges tiles beyond the viewport.
                if (TilesPanel != null)
                {
                    TilesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                    TilesPanel.Width = Double.NaN; // allow measure to determine content
                }
                // By default, sprites follow tile size unless the user manually adjusted sprite size.
                if (!manualSpriteSize)
                {
                    paletteSpriteSize = paletteTileSize;
                    if (SpriteSizeSlider != null)
                    {
                        suppressManualSpriteChange = true;
                        SpriteSizeSlider.Value = paletteSpriteSize;
                        suppressManualSpriteChange = false;
                    }
                    PopulateSpritesPanel();
                    if (SpritesPanel != null)
                    {
                        SpritesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                        SpritesPanel.Width = Double.NaN;
                    }
                }
            };
            // Sprite slider: when user changes sprite slider, mark manual and update sprite sizes.
            if (SpriteSizeSlider != null) SpriteSizeSlider.ValueChanged += (s, ev) =>
            {
                if (!suppressManualSpriteChange) manualSpriteSize = true;
                paletteSpriteSize = (int)ev.NewValue;
                PopulateSpritesPanel();
                if (SpritesPanel != null)
                {
                    SpritesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
                    SpritesPanel.Width = Double.NaN;
                }
            };
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
                // Use the current paletteTileSize (not the slider directly) so changing the slider
                // does not resize the frame. paletteTileSize reflects automatic or manual values.
                double ts = paletteTileSize;
                // 16 tiles across, plus padding and the system vertical scrollbar width
                double scrollbar = SystemParameters.VerticalScrollBarWidth;
                double padding = 12;
                double desired = Math.Max(160, ts * 16 + scrollbar + padding);
                // set the left column width to desired
                col.Width = new GridLength(desired, GridUnitType.Pixel);

                if (initial)
                {
                    // On first run, expand the window MinWidth so the left column is fully visible,
                    // but avoid forcing the actual Window.Width (user should be able to resize freely).
                    double splitterWidth = (RootGrid.ColumnDefinitions.Count > 1) ? RootGrid.ColumnDefinitions[1].ActualWidth : 5;
                    double mainAreaMin = 200; // smaller minimum for the main editor area to keep default window compact
                    double required = desired + splitterWidth + mainAreaMin + 40; // extra margins
                    if (this.MinWidth < required) this.MinWidth = required;
                    // Do not set this.Width here to avoid an oversized initial window.
                }
            }
            catch { }
        }

        private void AdjustPaletteSizes()
        {
            try
            {
                // Default behavior: use the tile size slider unless user manually adjusted it.
                if (!manualTileSize && TileSizeSlider != null)
                    paletteTileSize = Math.Max(8, (int)Math.Round(TileSizeSlider.Value));
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
                // Clamp to a reasonable maximum so the palette doesn't start huge on wide windows
                const int maxPaletteSize = 32;
                if (computedTiles > maxPaletteSize) computedTiles = maxPaletteSize;
                if (computedSprites > maxPaletteSize) computedSprites = maxPaletteSize;

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

                    // Do not auto-adjust sprite palette size here; leave sprites controllable by the user via the slider.

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

        private void Ensure16VisibleOnStartup()
        {
            try
            {
                if (RootGrid == null) return;
                double leftColActual = RootGrid.ColumnDefinitions[0].ActualWidth;
                if (leftColActual <= 0) return;

                // small padding inside the column
                double padding = 8;
                double available = Math.Max(0, leftColActual - padding);

                // account for an internal vertical scrollbar width if present
                double vsw = SystemParameters.VerticalScrollBarWidth;

                int computed = Math.Max(8, (int)Math.Floor((available - vsw) / 16.0));
                const int maxStartupSize = 20; // reasonable cap so startup tiles aren't huge
                if (computed > maxStartupSize) computed = maxStartupSize;

                // Apply computed sizes without marking as manual (so sliders remain untouched by user)
                paletteTileSize = computed;
                paletteSpriteSize = computed;
                if (TileSizeSlider != null)
                {
                    suppressManualTileChange = true;
                    TileSizeSlider.Value = paletteTileSize;
                    suppressManualTileChange = false;
                }
                if (SpriteSizeSlider != null)
                {
                    suppressManualSpriteChange = true;
                    SpriteSizeSlider.Value = paletteSpriteSize;
                    suppressManualSpriteChange = false;
                }

                // Repopulate panels with the computed sizes
                PopulateTilesPanel();
                PopulateSpritesPanel();

                // Disable horizontal scrolling on startup: we sized tiles to fit 16 columns.
                if (TilesPanel != null)
                {
                    TilesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                    TilesPanel.Width = Math.Max(0, available);
                }
                // Make sprites match tiles on startup and disable horizontal scrolling for parity.
                if (SpritesPanel != null)
                {
                    SpritesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                    SpritesPanel.Width = Math.Max(0, available);
                }
            }
            catch { }
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
            // If we have ground tiles, extend the canvas height by one tile so ground can be drawn under the bottom row
            double extraGroundH = (groundImages != null) ? (TileSize * scale) : 0.0;
            double fullH = mapHeight * TileSize * scale + extraGroundH;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Background (customizable)
                dc.DrawRectangle(mapBackground, null, new Rect(0, 0, fullW, fullH));

                // Draw parallax layer tiled across the map (if available). Start at top-left tile.
                if (parallaxImages != null && parallaxBitmap != null)
                {
                    int parallaxCols = Math.Max(1, parallaxBitmap.PixelWidth / TileSize);
                    // Parallax ratio: background moves slower than foreground. 0.9 means background moves at 90% of camera.
                    const double parallaxRatio = 0.9;
                    double camOffsetX = 0.0, camOffsetY = 0.0;
                    if (MapScrollViewer != null)
                    {
                        camOffsetX = MapScrollViewer.HorizontalOffset;
                        camOffsetY = MapScrollViewer.VerticalOffset;
                    }
                    // Amount to shift parallax tiles in world space so final screen shift is parallaxRatio * camera.
                    double parallaxWorldShiftX = camOffsetX * (1.0 - parallaxRatio);
                    double parallaxWorldShiftY = camOffsetY * (1.0 - parallaxRatio);

                    // tile the parallax tiles across the full map area
                    for (int pyTile = 0; pyTile < mapHeight; pyTile++)
                    {
                        for (int pxTile = 0; pxTile < mapWidth; pxTile++)
                        {
                            int idx = (pyTile * parallaxCols + pxTile) % parallaxImages.Length;
                            if (idx >= 0 && idx < parallaxImages.Length)
                            {
                                var img = parallaxImages[idx];
                                double px = pxTile * TileSize * scale + parallaxWorldShiftX;
                                double py = pyTile * TileSize * scale + parallaxWorldShiftY;
                                dc.DrawImage(img, new Rect(px, py, TileSize * scale, TileSize * scale));
                            }
                        }
                    }
                }

                // Draw ground row under the map (if ground images available). Draw before tiles so tiles render on top.
                if (groundImages != null)
                {
                    for (int gx = 0; gx < mapWidth; gx++)
                    {
                        var gimg = groundImages[gx % groundImages.Length];
                        double px = gx * TileSize * scale;
                        double py = mapHeight * TileSize * scale; // just below the last tile row
                        dc.DrawImage(gimg, new Rect(px, py, TileSize * scale, TileSize * scale));
                    }
                }

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

                // Indicate deleted/empty tiles (tiles == -1) with a small dot using the inverted map background color
                if (tiles != null)
                {
                    Color bgCol = (mapBackground as SolidColorBrush)?.Color ?? Color.FromRgb(40, 40, 40);
                    var inv = Color.FromRgb((byte)(255 - bgCol.R), (byte)(255 - bgCol.G), (byte)(255 - bgCol.B));
                    var dotBrush = new SolidColorBrush(Color.FromArgb(200, inv.R, inv.G, inv.B));
                    dotBrush.Freeze();
                    for (int y = 0; y < mapHeight; y++)
                    {
                        for (int x = 0; x < mapWidth; x++)
                        {
                            int idx = tiles[y * mapWidth + x];
                            if (idx == -1)
                            {
                                double px = x * TileSize * scale;
                                double py = y * TileSize * scale;
                                double dotSize = Math.Max(1.0, TileSize * scale * 0.18);
                                var center = new Point(px + (TileSize * scale) / 2.0, py + (TileSize * scale) / 2.0);
                                dc.DrawEllipse(dotBrush, null, center, dotSize / 2.0, dotSize / 2.0);
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
            StartPaintingAt(pos);
        }

        private void CanvasHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            StopPainting();
        }

        private void CanvasHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (CanvasHost == null) return;
            var pos = e.GetPosition(CanvasHost);
            UpdateCoords(pos);
            // If painting (mouse held down for place/erase) then paint the cell under the cursor
            if (isPainting && e.LeftButton == MouseButtonState.Pressed)
            {
                ContinuePaintingAt(pos);
            }
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

        private void StartPaintingAt(Point pos)
        {
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(pos.X / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(pos.Y / (TileSize * scale))));

            // For Fill tool, perform flood-fill on click and do not start drag-painting
            if (FillTool != null && FillTool.IsChecked == true)
            {
                int target = tiles[y * mapWidth + x];
                if (target != selectedTile)
                {
                    FloodFill(x, y, target, selectedTile);
                    Redraw();
                }
                return;
            }

            // For Move tool, pick the tile under cursor into selection (no painting while dragging)
            if (MoveTool != null && MoveTool.IsChecked == true)
            {
                selectedTile = tiles[y * mapWidth + x];
                UpdateTileHighlight();
                return;
            }

            // For Place or Erase tools, start painting and capture mouse so dragging works when cursor leaves the canvas
            if ((PlaceTool != null && PlaceTool.IsChecked == true) || (EraseTool != null && EraseTool.IsChecked == true))
            {
                isPainting = true;
                lastPaintX = -1; lastPaintY = -1;
                CanvasHost.CaptureMouse();
                DoPaintAt(x, y);
            }
        }

        private void ContinuePaintingAt(Point pos)
        {
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(pos.X / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(pos.Y / (TileSize * scale))));
            // avoid repainting the same cell repeatedly
            if (x == lastPaintX && y == lastPaintY) return;
            DoPaintAt(x, y);
        }

        private void StopPainting()
        {
            if (!isPainting) return;
            isPainting = false;
            lastPaintX = -1; lastPaintY = -1;
            if (CanvasHost != null && CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
        }

        private void DoPaintAt(int x, int y)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return;
            // Only paint for Place or Erase
            if (PlaceTool != null && PlaceTool.IsChecked == true)
            {
                tiles[y * mapWidth + x] = selectedTile;
                lastPaintX = x; lastPaintY = y;
                Redraw();
            }
            else if (EraseTool != null && EraseTool.IsChecked == true)
            {
                tiles[y * mapWidth + x] = -1;
                lastPaintX = x; lastPaintY = y;
                Redraw();
            }
        }

        private void UpdateCoords(Point p)
        {
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            // Determine whether pointer is inside the map area
            bool inBounds = p.X >= 0 && p.Y >= 0 && p.X < mapWidth * TileSize * scale && p.Y < mapHeight * TileSize * scale;
            int x = -1, y = -1;
            if (inBounds)
            {
                x = Math.Max(0, Math.Min(mapWidth - 1, (int)(p.X / (TileSize * scale))));
                y = Math.Max(0, Math.Min(mapHeight - 1, (int)(p.Y / (TileSize * scale))));
            }
            if (StatusText != null) StatusText.Text = inBounds ? $"Coords: {x}, {y}" : string.Empty;

            // Position hover rectangle
            try
            {
                if (HoverRect != null)
                {
                    if (inBounds)
                    {
                        double left = x * TileSize * scale;
                        double top = y * TileSize * scale;
                        double size = TileSize * scale;
                        HoverRect.Width = size; HoverRect.Height = size;
                        Canvas.SetLeft(HoverRect, left);
                        Canvas.SetTop(HoverRect, top);
                        HoverRect.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        HoverRect.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch { }
        }

        private void CanvasHost_MouseLeave(object sender, MouseEventArgs e)
        {
            try { if (HoverRect != null) HoverRect.Visibility = Visibility.Collapsed; } catch { }
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

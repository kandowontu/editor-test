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
    // default grid darkness: much lighter so grid lines are subtle over dark backgrounds
    private double gridDarkness = 0.18;
    private Brush mapBackground = new SolidColorBrush(Color.FromRgb(59,59,59));
    // tint overlays (RGBA) applied over the background and ground images
    private Color backgroundTint = Color.FromArgb(0, 0, 0, 0);
    private Color groundTint = Color.FromArgb(0, 0, 0, 0);
    private Color tileTint = Color.FromArgb(0, 0, 0, 0);
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
    // tinted caches (updated when tint changes)
    private ImageSource[]? parallaxTonedImages;
    private ImageSource[]? groundTonedImages;
    private ImageSource[]? tileTonedImages;
    private int selectedTile = 0;
    private int selectedSprite = -1;
    private int paletteTileSize = 16;
    private int paletteSpriteSize = 16;
    // Painting state for drag-to-draw
    private bool isPainting = false;
    private int lastPaintX = -1;
    private int lastPaintY = -1;
    // Selection state
    private bool isSelecting = false;
    private int selectStartX = -1, selectStartY = -1;
    private int selX = -1, selY = -1, selW = 0, selH = 0;
    private int[]? selTiles = null; // row-major selW * selH
    // Allow a small padded margin around the map so users can scroll slightly out-of-bounds
    private double mapViewportPadding = 64.0; // pixels on each side
    // Undo/redo support (unlimited)
    private readonly Stack<IUndoAction> undoStack = new Stack<IUndoAction>();
    private readonly Stack<IUndoAction> redoStack = new Stack<IUndoAction>();
    // when painting (drag), build up a composite action which will be pushed on mouse up
    private TileChangeAction? currentCompositeAction = null;
    private bool suppressUndoRecording = false;
    // Ground/Parallax layout
    // groundTileRows will be set when a ground bitmap is loaded (equals groundBitmap.PixelHeight / TileSize)
    private int groundTileRows = 0; // actual rows available in ground bitmap
    private int parallaxBelowRows = 8; // how many tile-rows of parallax to draw below the ground

    // Rendering caches for performance: background (parallax+ground), tiles (incremental), grid overlay
    private RenderTargetBitmap? backgroundRtb = null;
    private RenderTargetBitmap? parallaxRtb = null;
    private RenderTargetBitmap? groundRtb = null;
    private RenderTargetBitmap? gridRtb = null;
    private WriteableBitmap? tilesWb = null;
    // Pre-scaled tile pixel caches keyed by integer scale key (scale*100)
    private class ScaledTileCache { public byte[][] Pixels; public int TileW; public int TileH; public int Stride; public double Scale; public DpiScale Dpi; public ScaledTileCache(byte[][] pixels, int w, int h, int stride, double scale, DpiScale dpi) { Pixels = pixels; TileW = w; TileH = h; Stride = stride; Scale = scale; Dpi = dpi; } }
    private readonly Dictionary<int, ScaledTileCache> scaledTileCaches = new Dictionary<int, ScaledTileCache>();
    private int cachedPixelWidth = 0;
    private int cachedPixelHeight = 0;
    private double cachedScale = 1.0;
    private bool backgroundDirty = true;
    private bool gridDirty = true;
    // parallax ratio (0..1) where 1.0 = moves with camera, 0 = static world; we want 0.9
    private const double ParallaxRatio = 0.9;
    // cheap transform applied to ParallaxImage so it moves with the scroll without re-rendering pixels
    private TranslateTransform? parallaxTransform = new TranslateTransform(0, 0);

        private interface IUndoAction
        {
            void Undo(MainWindow window);
            void Redo(MainWindow window);
        }

        // Represents a batch of tile changes (can contain many per action like drag or flood-fill)
        private class TileChangeAction : IUndoAction
        {
            public struct Change { public int Index; public int Old; public int New; }
            private readonly List<Change> changes = new List<Change>();
            private readonly Dictionary<int, int> indexMap = new Dictionary<int, int>();

            public void Add(int index, int oldVal, int newVal)
            {
                if (indexMap.TryGetValue(index, out var pos))
                {
                    // update the New value
                    var c = changes[pos]; c = new Change { Index = c.Index, Old = c.Old, New = newVal }; changes[pos] = c;
                }
                else
                {
                    indexMap[index] = changes.Count;
                    changes.Add(new Change { Index = index, Old = oldVal, New = newVal });
                }
            }

            public bool IsEmpty() => changes.Count == 0;

            public void Undo(MainWindow window)
            {
                // apply old values
                var tiles = window.tiles;
                foreach (var c in changes)
                {
                    if (c.Index >= 0 && c.Index < tiles.Length) tiles[c.Index] = c.Old;
                }
            }

            public void Redo(MainWindow window)
            {
                // apply new values
                var tiles = window.tiles;
                foreach (var c in changes)
                {
                    if (c.Index >= 0 && c.Index < tiles.Length) tiles[c.Index] = c.New;
                }
            }
        }

        // Resize action records full tile arrays before/after resize so it can be undone/redone
        private class MapResizeAction : IUndoAction
        {
            private readonly int oldW; private readonly int oldH; private readonly int[] oldTiles;
            private readonly int newW; private readonly int newH; private readonly int[] newTiles;
            public MapResizeAction(int oldW, int oldH, int[] oldTiles, int newW, int newH, int[] newTiles)
            {
                this.oldW = oldW; this.oldH = oldH; this.oldTiles = oldTiles;
                this.newW = newW; this.newH = newH; this.newTiles = newTiles;
            }

            public void Undo(MainWindow window)
            {
                window.tiles = oldTiles;
                window.mapWidth = oldW; window.mapHeight = oldH;
                if (window.WidthBox != null) window.WidthBox.Text = oldW.ToString();
                if (window.HeightBox != null) window.HeightBox.Text = oldH.ToString();
                if (window.MapHeightLabel != null) window.MapHeightLabel.Text = oldH.ToString();
                // avoid triggering the slider change handler
                if (window.HeightSlider != null)
                {
                    window.HeightSlider.ValueChanged -= window.HeightSlider_ValueChanged;
                    window.HeightSlider.Value = oldH;
                    window.HeightSlider.ValueChanged += window.HeightSlider_ValueChanged;
                }
                window.Redraw();
            }

            public void Redo(MainWindow window)
            {
                window.tiles = newTiles;
                window.mapWidth = newW; window.mapHeight = newH;
                if (window.WidthBox != null) window.WidthBox.Text = newW.ToString();
                if (window.HeightBox != null) window.HeightBox.Text = newH.ToString();
                if (window.MapHeightLabel != null) window.MapHeightLabel.Text = newH.ToString();
                if (window.HeightSlider != null)
                {
                    window.HeightSlider.ValueChanged -= window.HeightSlider_ValueChanged;
                    window.HeightSlider.Value = newH;
                    window.HeightSlider.ValueChanged += window.HeightSlider_ValueChanged;
                }
                window.Redraw();
            }
        }

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
                CanvasHost.PreviewMouseWheel += CanvasHost_PreviewMouseWheel;
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
                // On scroll, update only the parallax transform (cheap) instead of re-rendering bitmaps
                MapScrollViewer.ScrollChanged += (s, e) => UpdateParallaxTransform();
                MapScrollViewer.Loaded += (_, __) => { Redraw(); UpdateParallaxTransform(); };
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
            if (BgTintButton != null) BgTintButton.Click += BgTintButton_Click;
            if (GroundTintButton != null) GroundTintButton.Click += GroundTintButton_Click;
            if (TileTintButton != null) TileTintButton.Click += TileTintButton_Click;
            if (UndoButton != null) UndoButton.Click += (s, e) => Undo();
            if (HeightSlider != null) HeightSlider.ValueChanged += (s, e) => { /* already wired above */ };
                // tool exclusivity: only one toggled at a time
                if (PlaceTool != null) PlaceTool.Checked += Tool_Checked;
                if (MoveTool != null) MoveTool.Checked += Tool_Checked;
                if (EraseTool != null) EraseTool.Checked += Tool_Checked;
                if (FillTool != null) FillTool.Checked += Tool_Checked;
                if (SelectTool != null) SelectTool.Checked += Tool_Checked;
            // keyboard shortcuts for undo/redo
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        // Ctrl + Mouse Wheel inside the canvas -> zoom in/out while keeping the point under cursor stable
        private void CanvasHost_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (MapScrollViewer == null || ZoomSlider == null) return;
            // Only act when Ctrl is held
            if (!(Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))) return;
            e.Handled = true;

            double oldScale = ZoomSlider.Value;
            // use a multiplicative zoom per mouse wheel notch (120 delta = one notch)
            double factorPerNotch = 1.1; // 10% per notch
            double factor = Math.Pow(factorPerNotch, e.Delta / 120.0);
            double newScale = oldScale * factor;
            // clamp to slider limits
            newScale = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, newScale));
            if (Math.Abs(newScale - oldScale) < 1e-6) return;

            // Determine mouse position in viewport coordinates
            var mouseVp = e.GetPosition(MapScrollViewer);
            double hp = MapScrollViewer.HorizontalOffset;
            double vp = MapScrollViewer.VerticalOffset;

            // Content coordinate under cursor before zoom
            double contentX = hp + mouseVp.X;
            double contentY = vp + mouseVp.Y;

            // map world coordinate (tile-space) under cursor
            double mapX = (contentX - mapViewportPadding) / (TileSize * oldScale);
            double mapY = (contentY - mapViewportPadding) / (TileSize * oldScale);

            // apply new zoom value
            ZoomSlider.Value = newScale;

            // compute new content coordinate for same world point
            double newContentX = mapViewportPadding + mapX * TileSize * newScale;
            double newContentY = mapViewportPadding + mapY * TileSize * newScale;

            // compute new scroll offsets so the same content point appears under the cursor
            double newH = newContentX - mouseVp.X;
            double newV = newContentY - mouseVp.Y;

            // clamp offsets to valid ranges
            double maxH = Math.Max(0, (CanvasHost.ActualWidth) - MapScrollViewer.ViewportWidth);
            double maxV = Math.Max(0, (CanvasHost.ActualHeight) - MapScrollViewer.ViewportHeight);
            newH = Math.Max(0, Math.Min(maxH, newH));
            newV = Math.Max(0, Math.Min(maxV, newV));

            // apply offsets
            MapScrollViewer.ScrollToHorizontalOffset(newH);
            MapScrollViewer.ScrollToVerticalOffset(newV);

            // rebuild caches if needed and redraw
            try { EnsureLayerBitmaps(newScale, mapViewportPadding, mapWidth * TileSize * newScale, mapHeight * TileSize * newScale, (mapWidth * TileSize * newScale) + mapViewportPadding * 2, (mapHeight * TileSize * newScale) + mapViewportPadding * 2, cachedPixelWidth, cachedPixelHeight); } catch { }
            Redraw();
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

            // record resize action for undo/redo
            if (!suppressUndoRecording)
            {
                var oldTilesCopy = tiles; // keep reference to old array
                var action = new MapResizeAction(mapWidth, mapHeight, oldTilesCopy, newWidth, newHeight, newTiles);
                undoStack.Push(action);
                redoStack.Clear();
            }

            mapWidth = newWidth; mapHeight = newHeight; tiles = newTiles;
            if (WidthBox != null) WidthBox.Text = mapWidth.ToString();
            if (HeightBox != null) HeightBox.Text = mapHeight.ToString();
            if (MapHeightLabel != null) MapHeightLabel.Text = mapHeight.ToString();
            if (HeightSlider != null && (int)Math.Round(HeightSlider.Value) != mapHeight) HeightSlider.Value = mapHeight;
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
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
                    sb.Color = Color.FromRgb(newColor.R, newColor.G, newColor.B);
                }
                // keep mapBackground as solid opaque brush for base; use backgroundTint for alpha overlays
                mapBackground = new SolidColorBrush(Color.FromRgb(newColor.R, newColor.G, newColor.B));
                SaveSettings(newColor);
                Redraw();
            }
        }

        private void BgTintButton_Click(object? sender, RoutedEventArgs e)
        {
            // Open the picker with the stored tint exactly (preserve alpha). Do not auto-promote zero alpha to opaque.
            var initialBgTint = backgroundTint;
            // For parallax/background tint, use full alpha (no slider)
            var dlg = new ColorPickerWindow(initialBgTint, allowAlpha: false) { Owner = this };
            dlg.Title = "Pick Background Tint (RGBA)";
            Action<Color> handler = (c) => { backgroundTint = c; UpdateParallaxTint(); Dispatcher.BeginInvoke(new Action(Redraw)); };
            dlg.ColorChanged += handler;
            var result = dlg.ShowDialog();
            if (result == true)
            {
                backgroundTint = dlg.SelectedColor;
                UpdateParallaxTint();
                Redraw();
                if (StatusText != null) StatusText.Text = $"BgTint set ARGB={backgroundTint.A},{backgroundTint.R},{backgroundTint.G},{backgroundTint.B} parallaxToned={(parallaxTonedImages!=null?parallaxTonedImages.Length:0)}";
            }
            else
            {
                // user cancelled: revert to initial tint
                backgroundTint = initialBgTint;
                UpdateParallaxTint();
                Redraw();
            }
            dlg.ColorChanged -= handler;
        }

        private void GroundTintButton_Click(object? sender, RoutedEventArgs e)
        {
            // Preserve stored alpha when opening the ground tint picker as well.
            var initialGroundTint = groundTint;
            // For ground tint, force full alpha and hide slider
            var dlg = new ColorPickerWindow(initialGroundTint, allowAlpha: false) { Owner = this };
            dlg.Title = "Pick Ground Tint (RGBA)";
            Action<Color> handler = (c) => { groundTint = c; UpdateGroundTint(); Dispatcher.BeginInvoke(new Action(Redraw)); };
            dlg.ColorChanged += handler;
            var result = dlg.ShowDialog();
            if (result == true)
            {
                groundTint = dlg.SelectedColor;
                UpdateGroundTint();
                Redraw();
                if (StatusText != null) StatusText.Text = $"GroundTint set ARGB={groundTint.A},{groundTint.R},{groundTint.G},{groundTint.B} groundToned={(groundTonedImages!=null?groundTonedImages.Length:0)}";
            }
            else
            {
                // revert on cancel
                groundTint = initialGroundTint;
                UpdateGroundTint();
                Redraw();
            }
            dlg.ColorChanged -= handler;
        }

        private void TileTintButton_Click(object? sender, RoutedEventArgs e)
        {
            var initial = tileTint;
            var dlg = new ColorPickerWindow(initial, allowAlpha: false) { Owner = this };
            dlg.Title = "Pick Tile Tint (RGBA)";
            Action<Color> handler = (c) => { tileTint = c; UpdateTileTint(); Dispatcher.BeginInvoke(new Action(Redraw)); };
            dlg.ColorChanged += handler;
            var result = dlg.ShowDialog();
            if (result == true)
            {
                tileTint = dlg.SelectedColor;
                UpdateTileTint();
                Redraw();
                if (StatusText != null) StatusText.Text = $"TileTint set ARGB={tileTint.A},{tileTint.R},{tileTint.G},{tileTint.B} tilesToned={(tileTonedImages!=null?tileTonedImages.Length:0)}";
            }
            else
            {
                tileTint = initial;
                UpdateTileTint();
                Redraw();
            }
            dlg.ColorChanged -= handler;
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
                    // optional background tint (RGBA)
                    if (doc.RootElement.TryGetProperty("backgroundTint", out var bt) && bt.GetArrayLength() >= 4)
                    {
                        var a = (byte)bt[0].GetInt32();
                        var r = (byte)bt[1].GetInt32();
                        var g = (byte)bt[2].GetInt32();
                        var b = (byte)bt[3].GetInt32();
                        backgroundTint = Color.FromArgb(a, r, g, b);
                    }
                    // optional ground tint (RGBA)
                    if (doc.RootElement.TryGetProperty("groundTint", out var gt) && gt.GetArrayLength() >= 4)
                    {
                        var a = (byte)gt[0].GetInt32();
                        var r = (byte)gt[1].GetInt32();
                        var g = (byte)gt[2].GetInt32();
                        var b = (byte)gt[3].GetInt32();
                        groundTint = Color.FromArgb(a, r, g, b);
                    }
                }
            }
            catch { }
        }

        private void SaveSettings(Color c)
        {
            try
            {
                var obj = new {
                    background = new byte[] { c.R, c.G, c.B },
                    backgroundTint = new byte[] { backgroundTint.A, backgroundTint.R, backgroundTint.G, backgroundTint.B },
                    groundTint = new byte[] { groundTint.A, groundTint.R, groundTint.G, groundTint.B }
                };
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
            // Invalidate any existing scaled tile caches when tileset changes
            try { scaledTileCaches.Clear(); } catch { }
            int cols = Math.Max(1, tilesetBitmap.PixelWidth / TileSize);
            int rows = Math.Max(1, tilesetBitmap.PixelHeight / TileSize);
            var list = new List<ImageSource>();
            for (int y = 0; y < rows; y++) for (int x = 0; x < cols; x++) list.Add(new CroppedBitmap(tilesetBitmap, new Int32Rect(x * TileSize, y * TileSize, TileSize, TileSize)));
            tileImages = list.ToArray();
            // update any toned cache when tileset changes
            UpdateTileTint();
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
            // update tinted cache to reflect current tint
            UpdateParallaxTint();
        }

        private void SliceGround()
        {
            if (groundBitmap == null) { groundImages = null; groundTileRows = 0; return; }
            int cols = Math.Max(1, groundBitmap.PixelWidth / TileSize);
            int rows = Math.Max(1, groundBitmap.PixelHeight / TileSize);
            groundTileRows = rows;
            var list = new List<ImageSource>();
            // slice all rows and columns so the ground can be stacked to its full bitmap height
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    list.Add(new CroppedBitmap(groundBitmap, new Int32Rect(x * TileSize, y * TileSize, TileSize, TileSize)));
                }
            }
            groundImages = list.ToArray();
            // update tinted cache to reflect current ground tint
            UpdateGroundTint();
        }

        // Create tinted copies of a set of ImageSources using simple alpha blend with the tint color.
        private ImageSource[]? CreateTintedImages(ImageSource[]? originals, Color tint)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // no tint => return originals so drawing still works
            var outList = new List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
                {
                    // convert to Bgra32 for pixel access
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                    int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    byte ta = tint.A; int tintA = ta;
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        int b = pixels[i + 0];
                        int g = pixels[i + 1];
                        int r = pixels[i + 2];
                        int a = pixels[i + 3];
                        // simple linear blend: out = original*(1 - tA) + tintRGB * tA
                        int outR = (r * (255 - tintA) + tint.R * tintA) / 255;
                        int outG = (g * (255 - tintA) + tint.G * tintA) / 255;
                        int outB = (b * (255 - tintA) + tint.B * tintA) / 255;
                        pixels[i + 0] = (byte)outB;
                        pixels[i + 1] = (byte)outG;
                        pixels[i + 2] = (byte)outR;
                        pixels[i + 3] = (byte)a; // keep original alpha
                    }

                    var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                    wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                    wb.Freeze();
                    outList.Add(wb);
                }
                else
                {
                    outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        // Create hue/saturation-shifted copies of images. The tint's RGB defines the target hue/saturation.
        // The tint.A channel is used as a strength (0..255) controlling interpolation between original H and tint H.
        private ImageSource[]? CreateHueShiftedImages(ImageSource[]? originals, Color tint)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // strength 0 => no change
            double strength = tint.A / 255.0;
            // convert tint color to HSL once
            RgbToHsl(tint.R, tint.G, tint.B, out double tintH, out double tintS, out double tintL);
            var outList = new List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
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

        private void UpdateParallaxTint()
        {
            // Use hue/saturation shifting for parallax so we actually alter hue/saturation instead of overlaying a color.
            parallaxTonedImages = CreateHueShiftedImages(parallaxImages, backgroundTint);
            // mark background/parallax cache dirty so the parallax RTB is rebuilt with the new tint
            backgroundDirty = true;
            if (StatusText != null)
            {
                string info = $"UpdateParallaxTint: tintA={backgroundTint.A} parallaxImages={(parallaxImages!=null?parallaxImages.Length:0)} parallaxToned={(parallaxTonedImages!=null?parallaxTonedImages.Length:0)}";
                try
                {
                    // Sample first pixel from original and toned (if available) for a quick diagnostic
                    if (parallaxImages != null && parallaxImages.Length > 0 && parallaxImages[0] is BitmapSource orig && parallaxTonedImages != null && parallaxTonedImages.Length > 0 && parallaxTonedImages[0] is BitmapSource toned)
                    {
                        var origPixel = new byte[4];
                        var tonedPixel = new byte[4];
                        orig.CopyPixels(new Int32Rect(0, 0, 1, 1), origPixel, 4, 0);
                        toned.CopyPixels(new Int32Rect(0, 0, 1, 1), tonedPixel, 4, 0);
                        info += $" | origARGB={origPixel[3]},{origPixel[2]},{origPixel[1]},{origPixel[0]}";
                        info += $" tonedARGB={tonedPixel[3]},{tonedPixel[2]},{tonedPixel[1]},{tonedPixel[0]}";
                    }
                }
                catch { }
                StatusText.Text = info;
            }
        }

        private void UpdateGroundTint()
        {
            // For ground, also apply hue/saturation shifting so the ground graphics change hue/sat.
            groundTonedImages = CreateHueShiftedImages(groundImages, groundTint);
            // mark background/ground cache dirty so the ground RTB is rebuilt with the new tint
            backgroundDirty = true;
            if (StatusText != null)
            {
                string info = $"UpdateGroundTint: tintA={groundTint.A} groundImages={(groundImages!=null?groundImages.Length:0)} groundToned={(groundTonedImages!=null?groundTonedImages.Length:0)}";
                try
                {
                    if (groundImages != null && groundImages.Length > 0 && groundImages[0] is BitmapSource orig && groundTonedImages != null && groundTonedImages.Length > 0 && groundTonedImages[0] is BitmapSource toned)
                    {
                        var origPixel = new byte[4];
                        var tonedPixel = new byte[4];
                        orig.CopyPixels(new Int32Rect(0, 0, 1, 1), origPixel, 4, 0);
                        toned.CopyPixels(new Int32Rect(0, 0, 1, 1), tonedPixel, 4, 0);
                        info += $" | origARGB={origPixel[3]},{origPixel[2]},{origPixel[1]},{origPixel[0]}";
                        info += $" tonedARGB={tonedPixel[3]},{tonedPixel[2]},{tonedPixel[1]},{tonedPixel[0]}";
                    }
                }
                catch { }
                StatusText.Text = info;
            }
        }

        private void UpdateTileTint()
        {
            // Use hue/saturation shifting for tiles so we can change hue/sat of tile graphics.
            tileTonedImages = CreateHueShiftedImages(tileImages, tileTint);
            // Clear pre-scaled caches so scaled pixels are rebuilt from the toned images
            try { scaledTileCaches.Clear(); } catch { }
            // Rebuild tiles bitmap so tint appears immediately
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
            // Refresh palette so the left frame shows tinted tiles as well
            try { PopulateTilesPanel(); } catch { }
            if (StatusText != null)
            {
                string info = $"UpdateTileTint: tintA={tileTint.A} tileImages={(tileImages!=null?tileImages.Length:0)} tileToned={(tileTonedImages!=null?tileTonedImages.Length:0)}";
                StatusText.Text = info;
            }
        }

        private void PopulateTilesPanel()
        {
            if (TilesPanel == null) return;
            TilesPanel.Items.Clear();
            if (tileImages == null) return;
            int idx = 0;
            foreach (var src in tileImages)
            {
                // prefer tinted tiles in the left palette when available
                var paletteSrc = (tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[idx] : src;
                var img = new Image { Source = paletteSrc, Width = paletteTileSize, Height = paletteTileSize, Stretch = Stretch.Fill, Tag = idx };
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
            // Compute sizes and ensure layer bitmaps exist; actual per-tile updates are incremental elsewhere
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            double fullW = mapWidth * TileSize * scale;
            int groundRowsToDraw = (groundTileRows > 0) ? groundTileRows : 0;
            double extraGroundH = (groundImages != null && groundRowsToDraw > 0) ? (TileSize * scale * groundRowsToDraw) : 0.0;
            double extraParallaxH = (parallaxImages != null) ? (TileSize * scale * parallaxBelowRows) : 0.0;
            double fullH = mapHeight * TileSize * scale + extraGroundH + extraParallaxH;
            double paddedFullW = fullW + pad * 2.0;
            double paddedFullH = fullH + pad * 2.0;

            var dpi = VisualTreeHelper.GetDpi(this);
            int pixelPaddedWidth = Math.Max(1, (int)Math.Ceiling(paddedFullW * dpi.DpiScaleX));
            int pixelPaddedHeight = Math.Max(1, (int)Math.Ceiling(paddedFullH * dpi.DpiScaleY));

            // Ensure background/grid/tiles bitmaps exist and match size/scale
            EnsureLayerBitmaps(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight);

            // Update UI image sources and sizes
            if (BackgroundImage != null && backgroundRtb != null)
            {
                BackgroundImage.Source = backgroundRtb;
                BackgroundImage.Width = paddedFullW; BackgroundImage.Height = paddedFullH;
            }
            if (ParallaxImage != null && parallaxRtb != null)
            {
                ParallaxImage.Source = parallaxRtb;
                ParallaxImage.Width = paddedFullW; ParallaxImage.Height = paddedFullH;
                ParallaxImage.RenderTransform = parallaxTransform;
            }
            if (GroundImage != null && groundRtb != null)
            {
                GroundImage.Source = groundRtb;
                GroundImage.Width = paddedFullW; GroundImage.Height = paddedFullH;
            }
            if (TilesImage != null && tilesWb != null)
            {
                TilesImage.Source = tilesWb;
                TilesImage.Width = paddedFullW; TilesImage.Height = paddedFullH;
            }
            if (GridImage != null && gridRtb != null)
            {
                GridImage.Source = gridRtb;
                GridImage.Width = paddedFullW; GridImage.Height = paddedFullH;
            }

            if (CanvasHost != null)
            {
                CanvasHost.Width = paddedFullW; CanvasHost.Height = paddedFullH;
            }
        }

        private void Redraw() => DrawMap();

        // Ensure the background, tiles and grid bitmaps exist for the current size/scale.
        private void EnsureLayerBitmaps(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            // Recreate background if size changed or marked dirty
            if (backgroundRtb == null || cachedPixelWidth != pixelPaddedWidth || cachedPixelHeight != pixelPaddedHeight || Math.Abs(cachedScale - scale) > 1e-6 || backgroundDirty)
            {
                BuildBackgroundBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi);
                // Also (re)build parallax and ground bitmaps for the current size/scale
                try { BuildParallaxBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi); } catch { }
                try { BuildGroundBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi); } catch { }
                backgroundDirty = false;
            }
            // Recreate grid if needed
            if (gridRtb == null || cachedPixelWidth != pixelPaddedWidth || cachedPixelHeight != pixelPaddedHeight || Math.Abs(cachedScale - scale) > 1e-6 || gridDirty)
            {
                BuildGridBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi);
                gridDirty = false;
            }
            // Create or recreate tiles writeable bitmap if size changed
            if (tilesWb == null || cachedPixelWidth != pixelPaddedWidth || cachedPixelHeight != pixelPaddedHeight || Math.Abs(cachedScale - scale) > 1e-6)
            {
                tilesWb = new WriteableBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
                cachedPixelWidth = pixelPaddedWidth; cachedPixelHeight = pixelPaddedHeight; cachedScale = scale;
                // initialize to transparent
                var empty = new byte[pixelPaddedHeight * tilesWb.BackBufferStride];
                tilesWb.WritePixels(new Int32Rect(0, 0, pixelPaddedWidth, pixelPaddedHeight), empty, tilesWb.BackBufferStride, 0);
                // render all tiles into the tilesWb
                RebuildAllTilesBitmap(scale, pad);
            }
        }

        private void BuildBackgroundBitmap(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Draw only the base map background here; parallax and ground are rendered into
                // their own RenderTargetBitmaps so they can be translated independently.
                dc.DrawRectangle(mapBackground, null, new Rect(pad, pad, fullW, fullH));
            }
            backgroundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            backgroundRtb.Render(dv);
        }

        // Build parallax bitmap (static pixels). We'll translate the image with a cheap transform
        // on scroll instead of re-rendering on every scroll event.
        private void BuildParallaxBitmap(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (parallaxImages != null && parallaxBitmap != null && parallaxImages.Length > 0)
                {
                    int parallaxCols = Math.Max(1, parallaxBitmap.PixelWidth / TileSize);
                    int startRow = 0; int endRow = mapHeight + ((parallaxBelowRows > 0) ? parallaxBelowRows : 0);
                    int groundRowsToDraw = (groundTileRows > 0) ? groundTileRows : 0;
                    // Expand horizontally so parallax covers edges
                    int extraCols = Math.Max(4, (int)Math.Ceiling(paddedFullW / (TileSize * scale)));
                    int startCol = -extraCols;
                    int endCol = mapWidth + extraCols;
                    for (int pyTile = startRow; pyTile < endRow; pyTile++)
                    {
                        if (groundRowsToDraw > 0 && pyTile >= mapHeight && pyTile < mapHeight + groundRowsToDraw) continue;
                        for (int pxTile = startCol; pxTile < endCol; pxTile++)
                        {
                            int idx = (pyTile * parallaxCols + pxTile) % parallaxImages.Length;
                            if (idx < 0) idx += parallaxImages.Length;
                            ImageSource? img = null;
                            try { if (parallaxTonedImages != null && parallaxTonedImages.Length == parallaxImages.Length) img = parallaxTonedImages[idx]; } catch { img = null; }
                            if (img == null) img = parallaxImages[idx];
                            if (img != null)
                            {
                                double px = pxTile * TileSize * scale + pad;
                                double py = pyTile * TileSize * scale + pad;
                                dc.DrawImage(img, new Rect(px, py, TileSize * scale, TileSize * scale));
                            }
                        }
                    }
                }
            }
            parallaxRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            parallaxRtb.Render(dv);
        }

        private void BuildGroundBitmap(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (groundImages != null && groundImages.Length > 0)
                {
                    int cols = Math.Max(1, (groundBitmap?.PixelWidth ?? TileSize) / TileSize);
                    int groundRowsToDraw = (groundTileRows > 0) ? groundTileRows : 0;
                    for (int gy = 0; gy < groundRowsToDraw; gy++)
                    {
                        for (int gx = 0; gx < mapWidth; gx++)
                        {
                            int idx = (gy * cols + (gx % cols)) % groundImages.Length;
                            ImageSource? gimg = null;
                            try { if (groundTonedImages != null && groundTonedImages.Length == groundImages.Length) gimg = groundTonedImages[idx]; } catch { gimg = null; }
                            if (gimg == null) gimg = groundImages[idx];
                            double px = gx * TileSize * scale + pad;
                            double py = (mapHeight + gy) * TileSize * scale + pad;
                            if (gimg != null) dc.DrawImage(gimg, new Rect(px, py, TileSize * scale, TileSize * scale));
                        }
                    }
                }
            }
            groundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            groundRtb.Render(dv);
        }

        // Update the TranslateTransform applied to the ParallaxImage so it moves at the desired
        // parallax ratio relative to the current scroll offsets. This is intentionally cheap
        // and does not re-render any bitmaps.
        private void UpdateParallaxTransform()
        {
            if (MapScrollViewer == null) return;
            if (parallaxRtb == null) return;
            // Camera offsets in content coordinates (pixels)
            double camOffsetX = MapScrollViewer.HorizontalOffset;
            double camOffsetY = MapScrollViewer.VerticalOffset;
            double shiftX = camOffsetX * (1.0 - ParallaxRatio);
            double shiftY = camOffsetY * (1.0 - ParallaxRatio);
            if (parallaxTransform == null) parallaxTransform = new TranslateTransform(shiftX, shiftY);
            else { parallaxTransform.X = shiftX; parallaxTransform.Y = shiftY; }
            if (ParallaxImage != null) ParallaxImage.RenderTransform = parallaxTransform;
        }

        private void BuildGridBitmap(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Draw grid using integer device pixels to keep alignment stable across zoom/DPI changes
                int alpha = Math.Min(255, (int)(gridDarkness * 255 * 1.6));
                // Pen thickness set to one device pixel
                double penThickness = 1.0 / dpi.DpiScaleX;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)alpha, 200, 200, 200)), penThickness);
                pen.Freeze();
                int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                int padPx = (int)Math.Round(pad * dpi.DpiScaleX);
                for (int y = 0; y < mapHeight; y++)
                {
                    for (int x = 0; x < mapWidth; x++)
                    {
                        int pxPix = padPx + x * tilePixelW;
                        int pyPix = padPx + y * tilePixelH;
                        double px = pxPix / dpi.DpiScaleX;
                        double py = pyPix / dpi.DpiScaleY;
                        double w = tilePixelW / dpi.DpiScaleX;
                        double h = tilePixelH / dpi.DpiScaleY;
                        dc.DrawRectangle(Brushes.Transparent, pen, new Rect(px, py, w, h));
                    }
                }
            }
            gridRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            gridRtb.Render(dv);
        }

        // Rebuild the entire tiles writeable bitmap from the tiles[] array
        private void RebuildAllTilesBitmap(double scale, double pad)
        {
            if (tilesWb == null) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
            // clear
            var clear = new byte[cachedPixelHeight * tilesWb.BackBufferStride];
            tilesWb.WritePixels(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight), clear, tilesWb.BackBufferStride, 0);
            // Ensure we have a pre-scaled tile cache for this zoom/dpi to avoid per-tile scaling work
            EnsureScaledTileCache(scale, dpi);
            // write each tile
            for (int y = 0; y < mapHeight; y++) for (int x = 0; x < mapWidth; x++) UpdateTileBitmapAt(x, y, scale, pad, tilePixelW, tilePixelH, dpi);
        }

        // Build or ensure a scaled tile pixel cache for the given zoom and dpi.
        // The cache stores raw BGRA32 pixel arrays for each tileImage scaled to the target tile pixel size.
        private void EnsureScaledTileCache(double scale, DpiScale dpi)
        {
            if (tileImages == null) return;
            int scaleKey = (int)Math.Round(scale * 100.0);
            if (scaledTileCaches.TryGetValue(scaleKey, out var existing))
            {
                // existing cache is fine
                return;
            }
            try
            {
                int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                int stride = tilePixelW * 4;
                int count = tileImages.Length;
                var pixelsArr = new byte[count][];
                for (int i = 0; i < count; i++)
                {
                    var buf = new byte[tilePixelH * stride];
                    try
                    {
                        // prefer tinted tiles when available
                        var src = (tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[i] as ImageSource : tileImages[i] as ImageSource;
                        if (src != null)
                        {
                            var dv = new DrawingVisual();
                            using (var dc = dv.RenderOpen()) dc.DrawImage(src, new Rect(0, 0, tilePixelW, tilePixelH));
                            var rtb = new RenderTargetBitmap(tilePixelW, tilePixelH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                            rtb.Render(dv);
                            rtb.CopyPixels(buf, stride, 0);
                        }
                    }
                    catch { /* leave transparent if copy fails */ }
                    pixelsArr[i] = buf;
                }
                var cache = new ScaledTileCache(pixelsArr, tilePixelW, tilePixelH, stride, scale, dpi);
                scaledTileCaches[scaleKey] = cache;
            }
            catch { /* don't crash on cache build failure */ }
        }

        // Update a single tile's pixels inside the tiles writeable bitmap
        private void UpdateTileBitmapAt(int x, int y, double scale, double pad, int? preTilePxW = null, int? preTilePxH = null, DpiScale? preDpi = null)
        {
            if (tilesWb == null) return;
            var dpi = preDpi ?? VisualTreeHelper.GetDpi(this);
            int tilePixelW = preTilePxW ?? Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int tilePixelH = preTilePxH ?? Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));

            int idx = tiles[y * mapWidth + x];
            int destX = Math.Max(0, (int)Math.Floor((pad + x * TileSize * scale) * dpi.DpiScaleX));
            int destY = Math.Max(0, (int)Math.Floor((pad + y * TileSize * scale) * dpi.DpiScaleY));

            // Use pre-scaled cached pixels when available to avoid rendering per-tile on the UI thread
            int stride = tilePixelW * 4;
            byte[] pixels = new byte[tilePixelH * stride];
            bool usedCache = false;
            try
            {
                var dpiNow = dpi;
                int scaleKey = (int)Math.Round(scale * 100.0);
                if (scaledTileCaches.TryGetValue(scaleKey, out var cache))
                {
                    // Cache must match expected tile size/dpi
                    if (cache.TileW == tilePixelW && cache.TileH == tilePixelH)
                    {
                        if (idx >= 0 && tileImages != null && idx < cache.Pixels.Length)
                        {
                            var src = cache.Pixels[idx];
                            if (src != null && src.Length == pixels.Length)
                            {
                                Buffer.BlockCopy(src, 0, pixels, 0, src.Length);
                                usedCache = true;
                            }
                        }
                    }
                }
            }
            catch { /* fall back to per-tile render below */ }

            if (!usedCache)
            {
                // Render tile image scaled to tilePixelW/tilePixelH into an intermediate bitmap and copy pixels
                if (idx >= 0 && tileImages != null && idx < tileImages.Length)
                {
                    var tileSrc = tileImages[idx] as ImageSource;
                    if (tileSrc != null)
                    {
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen()) dc.DrawImage(tileSrc, new Rect(0, 0, tilePixelW, tilePixelH));
                        var rtb = new RenderTargetBitmap(tilePixelW, tilePixelH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                        rtb.Render(dv);
                        try { rtb.CopyPixels(pixels, stride, 0); }
                        catch { /* swallow */ }
                    }
                }
                // else keep pixels transparent
            }

            try
            {
                tilesWb.WritePixels(new Int32Rect(destX, destY, Math.Min(tilePixelW, cachedPixelWidth - destX), Math.Min(tilePixelH, cachedPixelHeight - destY)), pixels, stride, 0);
            }
            catch { }
            // assign to image source (TilesImage) done in DrawMap/Ensure
        }

        private void CanvasHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CanvasHost == null) return;
            var pos = e.GetPosition(CanvasHost);
            // Branch behavior based on active tool
            // Select tool: start draggable selection rectangle
            if (SelectTool != null && SelectTool.IsChecked == true)
            {
                StartSelectionAt(pos);
                return;
            }
            // Move tool: if we have an existing selection, a click will place the selection's top-left at the clicked tile
            if (MoveTool != null && MoveTool.IsChecked == true)
            {
                double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
                double pad = mapViewportPadding;
                double relX = pos.X - pad;
                double relY = pos.Y - pad;
                int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(relX / (TileSize * scale))));
                int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(relY / (TileSize * scale))));
                if (selTiles != null && selW > 0 && selH > 0)
                {
                    MoveSelectionTo(x, y);
                    return;
                }
                // If no selection, fallback to pick tile under cursor (previous behavior)
                selectedTile = tiles[y * mapWidth + x];
                UpdateTileHighlight();
                return;
            }

            // Erase tool: if there's a selection and user clicks inside it, erase selection
            if (EraseTool != null && EraseTool.IsChecked == true && selTiles != null && selW > 0 && selH > 0)
            {
                double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
                double pad = mapViewportPadding;
                double relX = pos.X - pad;
                double relY = pos.Y - pad;
                int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(relX / (TileSize * scale))));
                int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(relY / (TileSize * scale))));
                if (x >= selX && x < selX + selW && y >= selY && y < selY + selH)
                {
                    EraseSelection();
                    return;
                }
            }

            // Default: place/erase painting behavior
            StartPaintingAt(pos);
        }

        private void CanvasHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isSelecting)
            {
                EndSelection();
            }
            else
            {
                StopPainting();
            }
        }

        private void CanvasHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (CanvasHost == null) return;
            var pos = e.GetPosition(CanvasHost);
            UpdateCoords(pos);
            // If actively selecting, update the selection rectangle
            if (isSelecting && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateSelectionTo(pos);
                return;
            }
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
            var action = new TileChangeAction();
            var q = new System.Collections.Generic.Queue<(int x, int y)>();
            q.Enqueue((sx, sy));
            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue();
                if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) continue;
                int idx = y * mapWidth + x;
                if (tiles[idx] != target) continue;
                // record change
                action.Add(idx, tiles[idx], replacement);
                tiles[idx] = replacement;
                q.Enqueue((x + 1, y)); q.Enqueue((x - 1, y)); q.Enqueue((x, y + 1)); q.Enqueue((x, y - 1));
            }
            if (!action.IsEmpty() && !suppressUndoRecording)
            {
                undoStack.Push(action);
                redoStack.Clear();
            }
            // update tiles bitmap after flood
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
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
            double pad = mapViewportPadding;
            double relX = pos.X - pad;
            double relY = pos.Y - pad;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(relX / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(relY / (TileSize * scale))));

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
                // Prepare scaled tile cache for the current zoom/DPI to avoid per-tile scaling during drag
                try { EnsureScaledTileCache((ZoomSlider!=null?ZoomSlider.Value:1.0), VisualTreeHelper.GetDpi(this)); } catch { }
                // begin composite undo action for this drag/session
                if (!suppressUndoRecording) currentCompositeAction = new TileChangeAction();
                CanvasHost.CaptureMouse();
                DoPaintAt(x, y);
            }
        }

        // Selection helpers
        private void StartSelectionAt(Point pos)
        {
            if (CanvasHost == null) return;
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            double relX = pos.X - pad;
            double relY = pos.Y - pad;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(relX / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(relY / (TileSize * scale))));
            isSelecting = true;
            selectStartX = x; selectStartY = y;
            // show selection rect initially at a single tile
            if (SelectionRect != null)
            {
                double left = x * TileSize * scale + mapViewportPadding;
                double top = y * TileSize * scale + mapViewportPadding;
                double size = TileSize * scale;
                SelectionRect.Width = size; SelectionRect.Height = size;
                Canvas.SetLeft(SelectionRect, left); Canvas.SetTop(SelectionRect, top);
                SelectionRect.Visibility = Visibility.Visible;
            }
            if (CanvasHost != null) CanvasHost.CaptureMouse();
        }

        private void UpdateSelectionTo(Point pos)
        {
            if (!isSelecting || SelectionRect == null) return;
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            double relX = pos.X - pad;
            double relY = pos.Y - pad;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(relX / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(relY / (TileSize * scale))));
            int minX = Math.Min(selectStartX, x), minY = Math.Min(selectStartY, y);
            int maxX = Math.Max(selectStartX, x), maxY = Math.Max(selectStartY, y);
            double left = minX * TileSize * scale + pad; double top = minY * TileSize * scale + pad;
            double w = (maxX - minX + 1) * TileSize * scale; double h = (maxY - minY + 1) * TileSize * scale;
            SelectionRect.Width = w; SelectionRect.Height = h; Canvas.SetLeft(SelectionRect, left); Canvas.SetTop(SelectionRect, top);
        }

        private void EndSelection()
        {
            if (!isSelecting) return;
            isSelecting = false;
            if (CanvasHost != null && CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
            // compute final selection bounds from current SelectionRect position
            if (SelectionRect == null) return;
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            double left = Canvas.GetLeft(SelectionRect) - pad;
            double top = Canvas.GetTop(SelectionRect) - pad;
            int minX = Math.Max(0, Math.Min(mapWidth - 1, (int)(left / (TileSize * scale))));
            int minY = Math.Max(0, Math.Min(mapHeight - 1, (int)(top / (TileSize * scale))));
            int w = Math.Max(1, (int)Math.Round(SelectionRect.Width / (TileSize * scale)));
            int h = Math.Max(1, (int)Math.Round(SelectionRect.Height / (TileSize * scale)));
            // clamp to map
            if (minX + w > mapWidth) w = mapWidth - minX;
            if (minY + h > mapHeight) h = mapHeight - minY;
            selX = minX; selY = minY; selW = w; selH = h;
            // copy tiles
            selTiles = new int[selW * selH];
            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++) selTiles[yy * selW + xx] = tiles[(selY + yy) * mapWidth + (selX + xx)];
            SelectionRect.Visibility = Visibility.Visible;
            if (StatusText != null) StatusText.Text = $"Selected area {selW}x{selH} at {selX},{selY}";
            Redraw();
        }

        private void ClearSelection()
        {
            selTiles = null; selW = 0; selH = 0; selX = selY = -1;
            if (SelectionRect != null) SelectionRect.Visibility = Visibility.Collapsed;
            if (StatusText != null) StatusText.Text = string.Empty;
        }

        private void MoveSelectionTo(int destX, int destY)
        {
            if (selTiles == null || selW <= 0 || selH <= 0) return;
            // clamp destination so selection fits
            if (destX < 0) destX = 0; if (destY < 0) destY = 0;
            if (destX + selW > mapWidth) destX = mapWidth - selW;
            if (destY + selH > mapHeight) destY = mapHeight - selH;

            // Prepare final values map and record changes compared to current tiles
            var final = (int[])tiles.Clone();
            var changed = new TileChangeAction();

            // Apply selection values to final at destination
            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
            {
                int val = selTiles[yy * selW + xx];
                int dIdx = (destY + yy) * mapWidth + (destX + xx);
                final[dIdx] = val;
            }

            // Clear original source cells unless they are also targets for the selection (i.e., overlapping move)
            var targetSet = new HashSet<int>();
            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++) targetSet.Add((destY + yy) * mapWidth + (destX + xx));
            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
            {
                int sIdx = (selY + yy) * mapWidth + (selX + xx);
                if (!targetSet.Contains(sIdx)) final[sIdx] = -1;
            }

            // Build TileChangeAction from differences
            for (int i = 0; i < final.Length; i++)
            {
                if (final[i] != tiles[i]) changed.Add(i, tiles[i], final[i]);
            }

            if (!changed.IsEmpty() && !suppressUndoRecording)
            {
                undoStack.Push(changed);
                redoStack.Clear();
            }

            // Commit final state
            tiles = final;
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
            ClearSelection();
            if (StatusText != null) StatusText.Text = $"Moved selection to {destX},{destY}";
        }

        private void EraseSelection()
        {
            if (selTiles == null || selW <= 0 || selH <= 0) return;
            var action = new TileChangeAction();
            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
            {
                int idx = (selY + yy) * mapWidth + (selX + xx);
                int old = tiles[idx]; if (old != -1) action.Add(idx, old, -1);
                tiles[idx] = -1;
            }
            if (!action.IsEmpty() && !suppressUndoRecording)
            {
                undoStack.Push(action); redoStack.Clear();
            }
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
            ClearSelection();
            if (StatusText != null) StatusText.Text = "Erased selection";
        }

        private void ContinuePaintingAt(Point pos)
        {
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            double relX = pos.X - pad;
            double relY = pos.Y - pad;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(relX / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(relY / (TileSize * scale))));
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
            // push composite action to undo stack
            try
            {
                if (!suppressUndoRecording && currentCompositeAction != null && !currentCompositeAction.IsEmpty())
                {
                    undoStack.Push(currentCompositeAction);
                    redoStack.Clear();
                }
            }
            finally { currentCompositeAction = null; }
        }

        private void DoPaintAt(int x, int y)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return;
            // Only paint for Place or Erase
            if (PlaceTool != null && PlaceTool.IsChecked == true)
            {
                int idx = y * mapWidth + x;
                int old = tiles[idx];
                int neu = selectedTile;
                if (old != neu)
                {
                    if (!suppressUndoRecording)
                    {
                        if (currentCompositeAction == null) currentCompositeAction = new TileChangeAction();
                        currentCompositeAction.Add(idx, old, neu);
                    }
                    tiles[idx] = neu;
                    lastPaintX = x; lastPaintY = y;
                    // update only this tile in the tiles layer
                    try { UpdateTileBitmapAt(x, y, (ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                }
            }
            else if (EraseTool != null && EraseTool.IsChecked == true)
            {
                int idx = y * mapWidth + x;
                int old = tiles[idx];
                int neu = -1;
                if (old != neu)
                {
                    if (!suppressUndoRecording)
                    {
                        if (currentCompositeAction == null) currentCompositeAction = new TileChangeAction();
                        currentCompositeAction.Add(idx, old, neu);
                    }
                    tiles[idx] = neu;
                    lastPaintX = x; lastPaintY = y;
                    try { UpdateTileBitmapAt(x, y, (ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                }
            }
        }

        private void UpdateCoords(Point p)
        {
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            // Determine whether pointer is inside the map area (accounting for padding)
            bool inBounds = p.X >= pad && p.Y >= pad && p.X < pad + mapWidth * TileSize * scale && p.Y < pad + mapHeight * TileSize * scale;
            int x = -1, y = -1;
            if (inBounds)
            {
                double relX = p.X - pad;
                double relY = p.Y - pad;
                x = Math.Max(0, Math.Min(mapWidth - 1, (int)(relX / (TileSize * scale))));
                y = Math.Max(0, Math.Min(mapHeight - 1, (int)(relY / (TileSize * scale))));
            }
            if (StatusText != null) StatusText.Text = inBounds ? $"Coords: {x}, {y}" : string.Empty;

            // Position hover rectangle (offset by padding)
            try
            {
                if (HoverRect != null)
                {
                    if (inBounds)
                    {
                        double left = x * TileSize * scale + pad;
                        double top = y * TileSize * scale + pad;
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
            gridDarkness = e.NewValue; 
            // Mark grid cache dirty so the grid bitmap is rebuilt with the new darkness
            gridDirty = true;
            Redraw();
        }

        private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            // Ctrl+Z = undo, Ctrl+Y = redo
            if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && e.Key == Key.Z)
            {
                Undo(); e.Handled = true; return;
            }
            if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && e.Key == Key.Y)
            {
                Redo(); e.Handled = true; return;
            }
        }

        private void Undo()
        {
            if (undoStack.Count == 0)
            {
                if (StatusText != null) StatusText.Text = "Undo: nothing to undo";
                return;
            }
            var action = undoStack.Pop();
            try
            {
                suppressUndoRecording = true;
                action.Undo(this);
            }
            finally { suppressUndoRecording = false; }
            redoStack.Push(action);
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
            if (StatusText != null) StatusText.Text = "Undid action";
        }

        private void Redo()
        {
            if (redoStack.Count == 0)
            {
                if (StatusText != null) StatusText.Text = "Redo: nothing to redo";
                return;
            }
            var action = redoStack.Pop();
            try
            {
                suppressUndoRecording = true;
                action.Redo(this);
            }
            finally { suppressUndoRecording = false; }
            undoStack.Push(action);
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
            if (StatusText != null) StatusText.Text = "Redid action";
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

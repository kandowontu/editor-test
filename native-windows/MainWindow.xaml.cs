using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;

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
    private int[] sprites = Array.Empty<int>(); // separate layer for sprites
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
    // Multi-tile/sprite selection support
    private List<int> selectedTiles = new List<int> { 0 }; // Multiple selected tiles (for placing in a row/pattern)
    private List<int> selectedSprites = new List<int>(); // Multiple selected sprites
    private int selectionWidth = 1; // Width of the selection grid
    private int selectionHeight = 1; // Height of the selection grid
    private bool isSelectingMultipleTiles = false; // Track if user is dragging in tile palette
    private System.Windows.Point? tileSelectionStart = null; // Starting point for multi-select
    // Layer selection state - can select both layers simultaneously for tools
    private bool tilesLayerActive = true;
    private bool spritesLayerActive = false;
    // Track changes and current file
    private string? currentFilePath = null;
    private bool hasUnsavedChanges = false;
    // Store loaded TMX metadata to preserve when saving
    private string? loadedTilesetSource = null;
    private string? loadedSpritesetSource = null;
    private bool loadedHasEditorSettings = false;
    private int loadedChunkWidth = 16;
    private int loadedChunkHeight = 27;
    private string? loadedExportTarget = null;
    private string loadedExportFormat = "csv";
    private string? loadedParallaxSource = null;
    private double loadedParallaxX = 0.9;
    private double loadedParallaxY = 0.9;
    private bool loadedParallaxRepeatX = true;
    private bool loadedParallaxRepeatY = true;
    private bool loadedHasParallaxLayer = false;
    private string? loadedGroundSource = null;
    private double loadedGroundOffsetY = 432;
    private bool loadedGroundRepeatX = true;
    private bool loadedHasGroundLayer = false;
    private int paletteTileSize = 16;
    private int paletteSpriteSize = 16;
    // Painting state for drag-to-draw
    private bool isPainting = false;
    private int lastPaintX = -1;
    private int lastPaintY = -1;
    // Track if mouse has moved since button down to distinguish click from drag
    private bool hasMouseMoved = false;
    private Point mouseDownPosition;
    // Selection state
    private bool isSelecting = false;
    private int selectStartX = -1, selectStartY = -1;
    private int selX = -1, selY = -1, selW = 0, selH = 0;
    private int[]? selTiles = null; // row-major selW * selH
    private int[]? selSprites = null; // row-major selW * selH
    private System.Collections.Generic.HashSet<int> selectionSet = new System.Collections.Generic.HashSet<int>();
    // Dragging selection state
    private bool isDraggingSelection = false;
    private Point dragStartMouse; // in CanvasHost coords
    private int dragOrigX = 0, dragOrigY = 0; // original selection top-left
    private Point dragOffset; // offset from mouse to selection top-left when dragging
    // Allow a small padded margin around the map so users can scroll slightly out-of-bounds
    private double mapViewportPadding = 64.0; // pixels on each side
    // Undo/redo support (unlimited)
    private readonly Stack<IUndoAction> undoStack = new Stack<IUndoAction>();
    private readonly Stack<IUndoAction> redoStack = new Stack<IUndoAction>();
    // when painting (drag), build up a composite action which will be pushed on mouse up
    private TileChangeAction? currentCompositeAction = null;
    private SpriteChangeAction? currentCompositeSpriteAction = null;
    private bool suppressUndoRecording = false;
    // Ground/Parallax layout
    // groundTileRows will be set when a ground bitmap is loaded (equals groundBitmap.PixelHeight / TileSize)
    private int groundTileRows = 0; // actual rows available in ground bitmap
    private int parallaxBelowRows = 8; // how many tile-rows of parallax to draw below the ground

    // Rendering caches for performance: background (parallax+ground), tiles (incremental), sprites, grid overlay
    private RenderTargetBitmap? backgroundRtb = null;
    private RenderTargetBitmap? parallaxRtb = null;
    private RenderTargetBitmap? groundRtb = null;
    private RenderTargetBitmap? gridRtb = null;
    private WriteableBitmap? tilesWb = null;
    private WriteableBitmap? spritesWb = null;
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
    // Cached brushes for performance
    private static readonly SolidColorBrush SelectionFillBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    private static readonly Brush SelectionStrokeBrush = Brushes.Cyan;
    // Cache hover position to avoid redundant updates
    private int lastHoverX = -1;
    private int lastHoverY = -1;
    // Throttle timers for expensive events
    private System.Windows.Threading.DispatcherTimer? sizeChangedThrottleTimer;
    private System.Windows.Threading.DispatcherTimer? zoomThrottleTimer;
    // Preview mode for animations (saws, etc.)
    private bool previewMode = false;
    private int animationFrame = 0; // Increments each frame, used to determine animation states
    private System.Windows.Threading.DispatcherTimer? previewTimer;
    // Animated saw frames: stored as separate tile images (4 tiles per frame, 2 frames)
    private BitmapSource[]? sawFrame1Tiles; // 4 tiles: top-left, top-right, bottom-left, bottom-right
    private BitmapSource[]? sawFrame2Tiles; // 4 tiles: top-left, top-right, bottom-left, bottom-right
    private ImageSource[]? sawFrame1TilesTinted; // Tinted versions
    private ImageSource[]? sawFrame2TilesTinted; // Tinted versions
    // Small saw frames: 3 single tiles (0x04, 0x7D, 0x7F)
    private BitmapSource[]? smallSawFrame1Tiles; // 3 tiles for frame 1
    private BitmapSource[]? smallSawFrame2Tiles; // 3 tiles for frame 2
    private ImageSource[]? smallSawFrame1TilesTinted; // Tinted versions
    private ImageSource[]? smallSawFrame2TilesTinted; // Tinted versions
    // Large saw frames: 9 tiles (3x3 grid) for 0x74-0x7C
    private BitmapSource[]? largeSawFrame1Tiles; // 9 tiles for frame 1
    private BitmapSource[]? largeSawFrame2Tiles; // 9 tiles for frame 2
    private ImageSource[]? largeSawFrame1TilesTinted; // Tinted versions
    private ImageSource[]? largeSawFrame2TilesTinted; // Tinted versions

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

        // Represents a batch of sprite changes
        private class SpriteChangeAction : IUndoAction
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
                var sprites = window.sprites;
                foreach (var c in changes)
                {
                    if (c.Index >= 0 && c.Index < sprites.Length) sprites[c.Index] = c.Old;
                }
            }

            public void Redo(MainWindow window)
            {
                // apply new values
                var sprites = window.sprites;
                foreach (var c in changes)
                {
                    if (c.Index >= 0 && c.Index < sprites.Length) sprites[c.Index] = c.New;
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
                window.Redraw();
            }

            public void Redo(MainWindow window)
            {
                window.tiles = newTiles;
                window.mapWidth = newW; window.mapHeight = newH;
                if (window.WidthBox != null) window.WidthBox.Text = newW.ToString();
                if (window.HeightBox != null) window.HeightBox.Text = newH.ToString();
                window.Redraw();
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();

            // Throttle zoom changes with longer delay to batch rapid changes
            if (ZoomSlider != null)
            {
                bool isZoomSliderPressed = false;
                
                zoomThrottleTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(150) // Increased from 50ms
                };
                zoomThrottleTimer.Tick += (s, e) =>
                {
                    zoomThrottleTimer?.Stop();
                    // Only redraw if slider is not being dragged
                    if (!isZoomSliderPressed)
                    {
                        Redraw();
                        // Reset hover tracking so it updates at new scale
                        lastHoverX = -1;
                        lastHoverY = -1;
                    }
                };
                
                // Track mouse down/up on slider thumb
                ZoomSlider.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    isZoomSliderPressed = true;
                };
                
                ZoomSlider.PreviewMouseLeftButtonUp += (s, e) =>
                {
                    isZoomSliderPressed = false;
                    // Trigger immediate redraw on mouse release with loading dialog
                    zoomThrottleTimer?.Stop();
                    
                    // Show rendering dialog
                    LoadingWindow? zoomLoadingWindow = null;
                    try
                    {
                        zoomLoadingWindow = new LoadingWindow { Owner = this };
                        zoomLoadingWindow.SetMessage("Rendering zoom...");
                        zoomLoadingWindow.Show();
                        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        
                        Redraw();
                        lastHoverX = -1;
                        lastHoverY = -1;
                    }
                    finally
                    {
                        if (zoomLoadingWindow != null)
                        {
                            zoomLoadingWindow.Close();
                        }
                    }
                };
                
                ZoomSlider.ValueChanged += (s, e) =>
                {
                    // Stop any pending throttled redraw
                    zoomThrottleTimer?.Stop();
                    
                    // Update zoom level label immediately
                    if (ZoomLevelLabel != null)
                        ZoomLevelLabel.Text = $"{(int)e.NewValue}x";
                    
                    // Immediate visual feedback: scale the images temporarily while waiting for redraw
                    UpdateQuickZoomTransform();
                    
                    // Only start throttle timer if not currently dragging
                    // (on release, we'll do immediate redraw instead)
                    if (!isZoomSliderPressed)
                    {
                        zoomThrottleTimer?.Start();
                    }
                };
            }
            if (GridDarknessSlider != null) GridDarknessSlider.ValueChanged += GridDarknessSlider_ValueChanged;
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
                // Throttle SizeChanged to avoid excessive redraws during resize
                sizeChangedThrottleTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(50)
                };
                sizeChangedThrottleTimer.Tick += (s, e) =>
                {
                    sizeChangedThrottleTimer?.Stop();
                    Redraw();
                };
                
                MapScrollViewer.SizeChanged += (_, __) =>
                {
                    sizeChangedThrottleTimer?.Stop();
                    sizeChangedThrottleTimer?.Start();
                };
                // On scroll, update only the parallax transform (cheap) instead of re-rendering bitmaps
                MapScrollViewer.ScrollChanged += (s, e) =>
                {
                    ClampScrollOffsets();
                    UpdateParallaxTransform();
                };
                MapScrollViewer.Loaded += (_, __) => { Redraw(); UpdateParallaxTransform(); ClampScrollOffsets(); };
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
            if (RootGrid != null) RootGrid.SizeChanged += (_, __) => 
            {
                UpdateTilesPanelWidth();
                // Also update column width when in auto mode
                if (!manualTileSize) UpdateLeftColumnWidth(initial: false);
            };
            
            // Wire up menu items
            if (MenuFileSave != null) MenuFileSave.Click += SaveButton_Click;
            if (MenuFileLoad != null) MenuFileLoad.Click += LoadButton_Click;
            if (MenuFileResize != null) MenuFileResize.Click += ResizeButton_Click;
            
            if (MenuToolPlace != null) MenuToolPlace.Click += (s, e) => { if (PlaceTool != null) PlaceTool.IsChecked = true; };
            if (MenuToolMove != null) MenuToolMove.Click += (s, e) => { if (MoveTool != null) MoveTool.IsChecked = true; };
            if (MenuToolErase != null) MenuToolErase.Click += (s, e) => { if (EraseTool != null) EraseTool.IsChecked = true; };
            if (MenuToolFill != null) MenuToolFill.Click += (s, e) => { if (FillTool != null) FillTool.IsChecked = true; };
            if (MenuToolSelect != null) MenuToolSelect.Click += (s, e) => { if (SelectTool != null) SelectTool.IsChecked = true; };
            if (MenuToolWand != null) MenuToolWand.Click += (s, e) => { if (MagicWandTool != null) MagicWandTool.IsChecked = true; };
            if (MenuToolUndo != null) MenuToolUndo.Click += (s, e) => Undo();
            if (MenuToolRedo != null) MenuToolRedo.Click += (s, e) => Redo();
            
            if (MenuColorEditorBackground != null) MenuColorEditorBackground.Click += BgColorButton_Click;
            if (MenuColorBackgroundTint != null) MenuColorBackgroundTint.Click += BgTintButton_Click;
            if (MenuColorGroundTint != null) MenuColorGroundTint.Click += GroundTintButton_Click;
            if (MenuColorTileTint != null) MenuColorTileTint.Click += TileTintButton_Click;
            
            if (UndoButton != null) UndoButton.Click += (s, e) => Undo();
            if (RedoButton != null) RedoButton.Click += (s, e) => Redo();
            
            // Preview mode checkbox and timer
            if (PreviewModeCheckbox != null)
            {
                PreviewModeCheckbox.Checked += (s, e) =>
                {
                    previewMode = true;
                    StartPreviewTimer();
                };
                PreviewModeCheckbox.Unchecked += (s, e) =>
                {
                    previewMode = false;
                    StopPreviewTimer();
                    animationFrame = 0;
                    // Redraw to show non-animated tiles
                    RebuildAllTilesBitmap((ZoomSlider != null ? ZoomSlider.Value : 1.0), mapViewportPadding);
                };
            }
            
            // tool exclusivity: only one toggled at a time
            if (PlaceTool != null) PlaceTool.Checked += Tool_Checked;
                if (MoveTool != null) MoveTool.Checked += Tool_Checked;
                if (EraseTool != null) EraseTool.Checked += Tool_Checked;
                if (FillTool != null) FillTool.Checked += Tool_Checked;
                if (SelectTool != null) SelectTool.Checked += Tool_Checked;
        if (MagicWandTool != null) MagicWandTool.Checked += Tool_Checked;
            // keyboard shortcuts for undo/redo
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            
            // pinch zoom support
            if (MapScrollViewer != null) MapScrollViewer.ManipulationDelta += MapScrollViewer_ManipulationDelta;
        }

        // Ctrl + Mouse Wheel inside the canvas -> zoom in/out while keeping the point under cursor stable
        // Ctrl + Mouse Wheel -> zoom in/out
        // Shift + Mouse Wheel -> horizontal scrolling (also handles touchpad 2-finger swipe)
        // Mouse Wheel without modifiers -> vertical scrolling (handled by ScrollViewer)
        private void CanvasHost_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (MapScrollViewer == null) return;
            
            // Ctrl+Wheel = Zoom
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                if (ZoomSlider == null) return;
                e.Handled = true;

                double oldScale = ZoomSlider.Value;
                // use a multiplicative zoom per mouse wheel notch (120 delta = one notch)
                double factorPerNotch = 1.1; // 10% per notch
                double factor = Math.Pow(factorPerNotch, e.Delta / 120.0);
                double newScale = oldScale * factor;
                // clamp to slider limits
                newScale = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, newScale));
                // Snap to nearest integer
                newScale = Math.Round(newScale);
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

                // Ensure we don't allow scrolling past the bottom of the ground after zoom
                ClampScrollOffsets();

                // rebuild caches if needed and redraw
                try { EnsureLayerBitmaps(newScale, mapViewportPadding, mapWidth * TileSize * newScale, mapHeight * TileSize * newScale, (mapWidth * TileSize * newScale) + mapViewportPadding * 2, (mapHeight * TileSize * newScale) + mapViewportPadding * 2, cachedPixelWidth, cachedPixelHeight); } catch { }
                Redraw();
                return;
            }
            
            // Shift+Wheel = Vertical scrolling
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                // e.Delta: positive = wheel up, negative = wheel down
                // We want positive delta to scroll up (decrease offset)
                double scrollAmount = e.Delta * 0.5; // Scale down for smoother scrolling
                double newOffset = MapScrollViewer.VerticalOffset - scrollAmount;
                MapScrollViewer.ScrollToVerticalOffset(newOffset);
                return;
            }
            
            // No modifiers (Wheel alone) = Horizontal scrolling
            e.Handled = true;
            // e.Delta: positive = wheel up, negative = wheel down
            // We want positive delta to scroll right (increase offset)
            double horizontalScrollAmount = e.Delta * 0.5; // Scale down for smoother scrolling
            double newHorizontalOffset = MapScrollViewer.HorizontalOffset - horizontalScrollAmount;
            MapScrollViewer.ScrollToHorizontalOffset(newHorizontalOffset);
        }

        private void ResizeMap(int newWidth, int newHeight)
        {
            // preserve existing tiles and sprites where possible
            var newTiles = Enumerable.Repeat(-1, newWidth * newHeight).ToArray();
            var newSprites = Enumerable.Repeat(-1, newWidth * newHeight).ToArray();
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
                        newSprites[(y + yOffset) * newWidth + x] = sprites[y * mapWidth + x];
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
                        newSprites[y * newWidth + x] = sprites[(y + startOldY) * mapWidth + x];
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

            mapWidth = newWidth; mapHeight = newHeight; 
            tiles = newTiles;
            sprites = newSprites;
            if (WidthBox != null) WidthBox.Text = mapWidth.ToString();
            if (HeightBox != null) HeightBox.Text = mapHeight.ToString();
            
            // Force rebuild of all layers (background, grid, tiles)
            backgroundDirty = true;
            gridDirty = true;
            // Clear cached dimensions to force size recalculation
            cachedPixelWidth = 0;
            cachedPixelHeight = 0;
            
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
            ClampScrollOffsets();
            Redraw();
        }

        private void BgColorButton_Click(object? sender, RoutedEventArgs e)
        {
            var brush = (SolidColorBrush?)Resources["AppBackgroundBrush"];
            var initial = brush != null ? brush.Color : Color.FromArgb(255, 40, 40, 40);
            // Use color wheel picker for editor background
            var dlg = new ColorWheelPickerWindow(initial) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                var newColor = dlg.SelectedColor;
                // update resource brush (so XAML backgrounds using it update)
                if (Resources.Contains("AppBackgroundBrush") && Resources["AppBackgroundBrush"] is SolidColorBrush sb)
                {
                    sb.Color = newColor;
                }
                // keep mapBackground as brush with full RGBA support
                mapBackground = new SolidColorBrush(newColor);
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

        private void StartPreviewTimer()
        {
            if (previewTimer == null)
            {
                // 60 FPS timer (approximately 16.67ms per frame)
                previewTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0)
                };
                previewTimer.Tick += PreviewTimer_Tick;
            }
            animationFrame = 0;
            previewTimer.Start();
        }

        private void StopPreviewTimer()
        {
            previewTimer?.Stop();
        }

        private void PreviewTimer_Tick(object? sender, EventArgs e)
        {
            animationFrame++;
            
            // Debug: Log frame switching every 60 frames (once per second)
            if (animationFrame % 60 == 0)
            {
                bool frame2 = ((animationFrame / 1) % 2) == 1;
                System.Diagnostics.Debug.WriteLine($"Animation frame {animationFrame}, showing frame {(frame2 ? 2 : 1)}, sawFrame1Tiles={sawFrame1Tiles?.Length}, sawFrame2Tiles={sawFrame2Tiles?.Length}, smallSawFrame1={smallSawFrame1Tiles?.Length}, smallSawFrame2={smallSawFrame2Tiles?.Length}, largeSawFrame1={largeSawFrame1Tiles?.Length}, largeSawFrame2={largeSawFrame2Tiles?.Length}");
            }
            
            // Only rebuild tiles if we have animated saws on screen
            // This is much more efficient than rebuilding everything every frame
            if (tilesWb != null && 
                ((sawFrame1Tiles != null && sawFrame2Tiles != null) || 
                 (smallSawFrame1Tiles != null && smallSawFrame2Tiles != null) ||
                 (largeSawFrame1Tiles != null && largeSawFrame2Tiles != null)))
            {
                // Find and update only the saw tiles (0x08-0x0B, 0x04, 0x7D, 0x7F, 0x74-0x7C)
                bool hasSaws = false;
                for (int i = 0; i < tiles.Length; i++)
                {
                    int tileIdx = tiles[i];
                    if ((tileIdx >= 0x08 && tileIdx <= 0x0B) || tileIdx == 0x04 || tileIdx == 0x7D || tileIdx == 0x7F || (tileIdx >= 0x74 && tileIdx <= 0x7C))
                    {
                        hasSaws = true;
                        break;
                    }
                }
                
                if (hasSaws)
                {
                    // Only update saw tiles, not entire bitmap
                    double scale = (ZoomSlider != null ? ZoomSlider.Value : 1.0);
                    var dpi = VisualTreeHelper.GetDpi(this);
                    int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                    int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                    
                    tilesWb.Lock();
                    try
                    {
                        for (int y = 0; y < mapHeight; y++)
                        {
                            for (int x = 0; x < mapWidth; x++)
                            {
                                int tileIdx = tiles[y * mapWidth + x];
                                if ((tileIdx >= 0x08 && tileIdx <= 0x0B) || tileIdx == 0x04 || tileIdx == 0x7D || tileIdx == 0x7F || (tileIdx >= 0x74 && tileIdx <= 0x7C))
                                {
                                    UpdateTileBitmapAtLocked(x, y, scale, mapViewportPadding, tilePixelW, tilePixelH, dpi);
                                }
                            }
                        }
                        tilesWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
                    }
                    finally
                    {
                        tilesWb.Unlock();
                    }
                }
            }
        }

        // Map a tile index to its animated version based on current animation frame
        // Saw tiles (0x08-0x0B, 0x74-0x7C, 0x04, 0x7D, 0x7F) animate using custom frames
        // Returns special indices (>= 1000) to indicate custom animation tiles
        private int GetAnimatedTileIndex(int originalIndex)
        {
            if (!previewMode) return originalIndex;
            
            // Check if this is one of the saw tiles (0x08-0x0B)
            if (originalIndex >= 0x08 && originalIndex <= 0x0B)
            {
                // 3/4 speed: switch every 4/3 frames (every 80ms at 60 FPS)
                bool showFrame2 = (((animationFrame * 3) / 4) % 2) == 1;
                
                // Map to custom saw frame tile
                // Use special indices: 1000-1003 for frame 1, 1004-1007 for frame 2
                int tileOffset = originalIndex - 0x08; // 0-3
                if (showFrame2)
                    return 1004 + tileOffset; // Frame 2 tiles
                else
                    return 1000 + tileOffset; // Frame 1 tiles
            }
            
            // Check if this is one of the small saw tiles (0x04, 0x7D, 0x7F)
            if (originalIndex == 0x04 || originalIndex == 0x7D || originalIndex == 0x7F)
            {
                // Same speed as large saws
                bool showFrame2 = (((animationFrame * 3) / 4) % 2) == 1;
                
                // Map to custom small saw frame tile
                // Use special indices: 1010-1012 for frame 1, 1013-1015 for frame 2
                int tileOffset = (originalIndex == 0x04) ? 0 : (originalIndex == 0x7D) ? 1 : 2;
                if (showFrame2)
                    return 1013 + tileOffset; // Small saw frame 2 tiles
                else
                    return 1010 + tileOffset; // Small saw frame 1 tiles
            }
            
            // Check if this is one of the large saw tiles (0x74-0x7C) - 9 tiles in 3x3 grid
            if (originalIndex >= 0x74 && originalIndex <= 0x7C)
            {
                // Same speed as other saws
                bool showFrame2 = (((animationFrame * 3) / 4) % 2) == 1;
                
                // Map to custom large saw frame tile
                // Use special indices: 1020-1028 for frame 1, 1029-1037 for frame 2
                int tileOffset = originalIndex - 0x74; // 0-8
                if (showFrame2)
                    return 1029 + tileOffset; // Large saw frame 2 tiles
                else
                    return 1020 + tileOffset; // Large saw frame 1 tiles
            }
            
            return originalIndex; // Not a saw tile
        }
        
        // Get the custom saw animation tile if index is >= 1000
        private BitmapSource? GetCustomAnimationTile(int customIndex)
        {
            if (customIndex >= 1000 && customIndex <= 1003)
            {
                // Frame 1 tiles (1000-1003)
                int offset = customIndex - 1000;
                // Use tinted version if available, otherwise use original
                if (sawFrame1TilesTinted != null && offset < sawFrame1TilesTinted.Length)
                    return sawFrame1TilesTinted[offset] as BitmapSource;
                return sawFrame1Tiles?[offset];
            }
            else if (customIndex >= 1004 && customIndex <= 1007)
            {
                // Frame 2 tiles (1004-1007)
                int offset = customIndex - 1004;
                // Use tinted version if available, otherwise use original
                if (sawFrame2TilesTinted != null && offset < sawFrame2TilesTinted.Length)
                    return sawFrame2TilesTinted[offset] as BitmapSource;
                return sawFrame2Tiles?[offset];
            }
            else if (customIndex >= 1010 && customIndex <= 1012)
            {
                // Small saw frame 1 tiles (1010-1012)
                int offset = customIndex - 1010;
                // Use tinted version if available, otherwise use original
                if (smallSawFrame1TilesTinted != null && offset < smallSawFrame1TilesTinted.Length)
                    return smallSawFrame1TilesTinted[offset] as BitmapSource;
                return smallSawFrame1Tiles?[offset];
            }
            else if (customIndex >= 1013 && customIndex <= 1015)
            {
                // Small saw frame 2 tiles (1013-1015)
                int offset = customIndex - 1013;
                // Use tinted version if available, otherwise use original
                if (smallSawFrame2TilesTinted != null && offset < smallSawFrame2TilesTinted.Length)
                    return smallSawFrame2TilesTinted[offset] as BitmapSource;
                return smallSawFrame2Tiles?[offset];
            }
            else if (customIndex >= 1020 && customIndex <= 1028)
            {
                // Large saw frame 1 tiles (1020-1028) - 9 tiles
                int offset = customIndex - 1020;
                // Use tinted version if available, otherwise use original
                if (largeSawFrame1TilesTinted != null && offset < largeSawFrame1TilesTinted.Length)
                    return largeSawFrame1TilesTinted[offset] as BitmapSource;
                return largeSawFrame1Tiles?[offset];
            }
            else if (customIndex >= 1029 && customIndex <= 1037)
            {
                // Large saw frame 2 tiles (1029-1037) - 9 tiles
                int offset = customIndex - 1029;
                // Use tinted version if available, otherwise use original
                if (largeSawFrame2TilesTinted != null && offset < largeSawFrame2TilesTinted.Length)
                    return largeSawFrame2TilesTinted[offset] as BitmapSource;
                return largeSawFrame2Tiles?[offset];
            }
            return null;
        }
        
        // Check if a custom animation tile index represents a half-height tile (8 pixels tall)
        private bool IsHalfHeightTile(int customIndex)
        {
            // Tiles 1010 and 1012 are half-height (0x04=bottom half, 0x7F=top half)
            // Tiles 1011 is full-height (0x7D=full centered saw)
            // Same for frame 2: 1013 and 1015 are half-height, 1014 is full
            return (customIndex == 1010 || customIndex == 1012 || 
                    customIndex == 1013 || customIndex == 1015);
        }
        
        // Check if a custom animation tile is specifically the top-half tile (0x7F)
        private bool IsTopHalfTile(int customIndex)
        {
            // Tiles 1012 and 1015 are the top-half tiles (0x7F in frames 1 and 2)
            return (customIndex == 1012 || customIndex == 1015);
        }

        // Create saw animation frames from the provided pixel data
        // Each frame is 32x32 pixels (2x2 tiles)
        private void InitializeSawAnimationFrames()
        {
            try
            {
                sawFrame1Tiles = new BitmapSource[4];
                sawFrame2Tiles = new BitmapSource[4];
                
                // Try to load from embedded resources first
                var frame1Full = LoadEmbeddedImage("saw-frame1.png");
                var frame2Full = LoadEmbeddedImage("saw-frame2.png");
                
                // If not found in embedded resources, try file system
                if (frame1Full == null || frame2Full == null)
                {
                    var baseDir = AppContext.BaseDirectory;
                    var frame1Path = System.IO.Path.Combine(baseDir, "saw-frame1.png");
                    var frame2Path = System.IO.Path.Combine(baseDir, "saw-frame2.png");
                    
                    var repo = FindRepoRootFor("famidash.bmp");
                    if (!string.IsNullOrEmpty(repo))
                    {
                        var repoFrame1 = System.IO.Path.Combine(repo, "saw-frame1.png");
                        var repoFrame2 = System.IO.Path.Combine(repo, "saw-frame2.png");
                        if (System.IO.File.Exists(repoFrame1)) frame1Path = repoFrame1;
                        if (System.IO.File.Exists(repoFrame2)) frame2Path = repoFrame2;
                    }
                    
                    if (System.IO.File.Exists(frame1Path) && System.IO.File.Exists(frame2Path))
                    {
                        frame1Full = new BitmapImage();
                        frame1Full.BeginInit();
                        frame1Full.CacheOption = BitmapCacheOption.OnLoad;
                        frame1Full.UriSource = new Uri(frame1Path);
                        frame1Full.EndInit();
                        frame1Full.Freeze();
                        
                        frame2Full = new BitmapImage();
                        frame2Full.BeginInit();
                        frame2Full.CacheOption = BitmapCacheOption.OnLoad;
                        frame2Full.UriSource = new Uri(frame2Path);
                        frame2Full.EndInit();
                        frame2Full.Freeze();
                    }
                }
                
                if (frame1Full != null && frame2Full != null)
                {
                    // Split each 32x32 frame into 4 16x16 tiles
                    sawFrame1Tiles[0] = new CroppedBitmap(frame1Full, new Int32Rect(0, 0, 16, 16));   // Top-left
                    sawFrame1Tiles[1] = new CroppedBitmap(frame1Full, new Int32Rect(16, 0, 16, 16));  // Top-right
                    sawFrame1Tiles[2] = new CroppedBitmap(frame1Full, new Int32Rect(0, 16, 16, 16));  // Bottom-left
                    sawFrame1Tiles[3] = new CroppedBitmap(frame1Full, new Int32Rect(16, 16, 16, 16)); // Bottom-right
                    
                    sawFrame2Tiles[0] = new CroppedBitmap(frame2Full, new Int32Rect(0, 0, 16, 16));
                    sawFrame2Tiles[1] = new CroppedBitmap(frame2Full, new Int32Rect(16, 0, 16, 16));
                    sawFrame2Tiles[2] = new CroppedBitmap(frame2Full, new Int32Rect(0, 16, 16, 16));
                    sawFrame2Tiles[3] = new CroppedBitmap(frame2Full, new Int32Rect(16, 16, 16, 16));
                    
                    System.Diagnostics.Debug.WriteLine($"✓ Loaded saw animation frames");
                    System.Diagnostics.Debug.WriteLine($"  Frame 1: {frame1Full.PixelWidth}x{frame1Full.PixelHeight}");
                    System.Diagnostics.Debug.WriteLine($"  Frame 2: {frame2Full.PixelWidth}x{frame2Full.PixelHeight}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✗ Saw frame files not found");
                }
                
                // Load small saw frames
                var smallFrame1Full = LoadEmbeddedImage("small-saw-frame1.png");
                var smallFrame2Full = LoadEmbeddedImage("small-saw-frame2.png");
                
                // If not found in embedded resources, try file system
                if (smallFrame1Full == null || smallFrame2Full == null)
                {
                    var baseDir = AppContext.BaseDirectory;
                    var smallFrame1Path = System.IO.Path.Combine(baseDir, "small-saw-frame1.png");
                    var smallFrame2Path = System.IO.Path.Combine(baseDir, "small-saw-frame2.png");
                    
                    var repo = FindRepoRootFor("famidash.bmp");
                    if (!string.IsNullOrEmpty(repo))
                    {
                        var repoSmallFrame1 = System.IO.Path.Combine(repo, "small-saw-frame1.png");
                        var repoSmallFrame2 = System.IO.Path.Combine(repo, "small-saw-frame2.png");
                        if (System.IO.File.Exists(repoSmallFrame1)) smallFrame1Path = repoSmallFrame1;
                        if (System.IO.File.Exists(repoSmallFrame2)) smallFrame2Path = repoSmallFrame2;
                    }
                    
                    if (System.IO.File.Exists(smallFrame1Path) && System.IO.File.Exists(smallFrame2Path))
                    {
                        smallFrame1Full = new BitmapImage();
                        smallFrame1Full.BeginInit();
                        smallFrame1Full.CacheOption = BitmapCacheOption.OnLoad;
                        smallFrame1Full.UriSource = new Uri(smallFrame1Path);
                        smallFrame1Full.EndInit();
                        smallFrame1Full.Freeze();
                        
                        smallFrame2Full = new BitmapImage();
                        smallFrame2Full.BeginInit();
                        smallFrame2Full.CacheOption = BitmapCacheOption.OnLoad;
                        smallFrame2Full.UriSource = new Uri(smallFrame2Path);
                        smallFrame2Full.EndInit();
                        smallFrame2Full.Freeze();
                    }
                }
                
                if (smallFrame1Full != null && smallFrame2Full != null)
                {
                    smallSawFrame1Tiles = new BitmapSource[3];
                    smallSawFrame2Tiles = new BitmapSource[3];
                    
                    System.Diagnostics.Debug.WriteLine($"Small saw frames dimensions: Frame1={smallFrame1Full.PixelWidth}x{smallFrame1Full.PixelHeight}, Frame2={smallFrame2Full.PixelWidth}x{smallFrame2Full.PixelHeight}");
                    
                    // Each small saw tile (split HORIZONTALLY - top and bottom halves):
                    // 0x04: Bottom half of saw (16px wide × 8px tall, from y=8)
                    // 0x7F: Top half of saw (16px wide × 8px tall, from y=0)
                    // 0x7D: Full centered small saw (16px wide × 16px tall)
                    smallSawFrame1Tiles[0] = new CroppedBitmap(smallFrame1Full, new Int32Rect(0, 8, 16, 8));    // 0x04 - bottom half
                    smallSawFrame1Tiles[1] = new CroppedBitmap(smallFrame1Full, new Int32Rect(0, 0, 16, 16));   // 0x7D - full saw
                    smallSawFrame1Tiles[2] = new CroppedBitmap(smallFrame1Full, new Int32Rect(0, 0, 16, 8));    // 0x7F - top half
                    
                    smallSawFrame2Tiles[0] = new CroppedBitmap(smallFrame2Full, new Int32Rect(0, 8, 16, 8));    // 0x04 - bottom half
                    smallSawFrame2Tiles[1] = new CroppedBitmap(smallFrame2Full, new Int32Rect(0, 0, 16, 16));   // 0x7D - full saw
                    smallSawFrame2Tiles[2] = new CroppedBitmap(smallFrame2Full, new Int32Rect(0, 0, 16, 8));    // 0x7F - top half
                    
                    System.Diagnostics.Debug.WriteLine($"✓ Loaded small saw animation frames");
                    System.Diagnostics.Debug.WriteLine($"  Small Frame 1: {smallFrame1Full.PixelWidth}x{smallFrame1Full.PixelHeight}");
                    System.Diagnostics.Debug.WriteLine($"  Small Frame 2: {smallFrame2Full.PixelWidth}x{smallFrame2Full.PixelHeight}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✗ Small saw frame files not found");
                }
                
                // Load large saw frames (3x3 grid = 9 tiles for 0x74-0x7C)
                var largeFrame1Full = LoadEmbeddedImage("large-saw-frame1.png");
                var largeFrame2Full = LoadEmbeddedImage("large-saw-frame2.png");
                
                // If not found in embedded resources, try file system
                if (largeFrame1Full == null || largeFrame2Full == null)
                {
                    var baseDir = AppContext.BaseDirectory;
                    var largeFrame1Path = System.IO.Path.Combine(baseDir, "large-saw-frame1.png");
                    var largeFrame2Path = System.IO.Path.Combine(baseDir, "large-saw-frame2.png");
                    
                    var repo = FindRepoRootFor("famidash.bmp");
                    if (!string.IsNullOrEmpty(repo))
                    {
                        var repoLargeFrame1 = System.IO.Path.Combine(repo, "large-saw-frame1.png");
                        var repoLargeFrame2 = System.IO.Path.Combine(repo, "large-saw-frame2.png");
                        if (System.IO.File.Exists(repoLargeFrame1)) largeFrame1Path = repoLargeFrame1;
                        if (System.IO.File.Exists(repoLargeFrame2)) largeFrame2Path = repoLargeFrame2;
                    }
                    
                    if (System.IO.File.Exists(largeFrame1Path) && System.IO.File.Exists(largeFrame2Path))
                    {
                        largeFrame1Full = new BitmapImage();
                        largeFrame1Full.BeginInit();
                        largeFrame1Full.CacheOption = BitmapCacheOption.OnLoad;
                        largeFrame1Full.UriSource = new Uri(largeFrame1Path);
                        largeFrame1Full.EndInit();
                        largeFrame1Full.Freeze();
                        
                        largeFrame2Full = new BitmapImage();
                        largeFrame2Full.BeginInit();
                        largeFrame2Full.CacheOption = BitmapCacheOption.OnLoad;
                        largeFrame2Full.UriSource = new Uri(largeFrame2Path);
                        largeFrame2Full.EndInit();
                        largeFrame2Full.Freeze();
                    }
                }
                
                if (largeFrame1Full != null && largeFrame2Full != null)
                {
                    largeSawFrame1Tiles = new BitmapSource[9];
                    largeSawFrame2Tiles = new BitmapSource[9];
                    
                    System.Diagnostics.Debug.WriteLine($"Large saw frames dimensions: Frame1={largeFrame1Full.PixelWidth}x{largeFrame1Full.PixelHeight}, Frame2={largeFrame2Full.PixelWidth}x{largeFrame2Full.PixelHeight}");
                    
                    // Split 48x48 image into 9 tiles (3x3 grid), 16x16 each
                    // Order: top-left to top-right, then next row, etc. (0x74-0x7C)
                    for (int row = 0; row < 3; row++)
                    {
                        for (int col = 0; col < 3; col++)
                        {
                            int index = row * 3 + col;
                            largeSawFrame1Tiles[index] = new CroppedBitmap(largeFrame1Full, new Int32Rect(col * 16, row * 16, 16, 16));
                            largeSawFrame2Tiles[index] = new CroppedBitmap(largeFrame2Full, new Int32Rect(col * 16, row * 16, 16, 16));
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"✓ Loaded large saw animation frames (9 tiles)");
                    System.Diagnostics.Debug.WriteLine($"  Large Frame 1: {largeFrame1Full.PixelWidth}x{largeFrame1Full.PixelHeight}");
                    System.Diagnostics.Debug.WriteLine($"  Large Frame 2: {largeFrame2Full.PixelWidth}x{largeFrame2Full.PixelHeight}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✗ Large saw frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load saw animation frames: {ex.Message}");
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
                        Color col;
                        if (bg.GetArrayLength() >= 4)
                        {
                            // New format: ARGB
                            var a = (byte)bg[0].GetInt32();
                            var r = (byte)bg[1].GetInt32();
                            var g = (byte)bg[2].GetInt32();
                            var b = (byte)bg[3].GetInt32();
                            col = Color.FromArgb(a, r, g, b);
                        }
                        else
                        {
                            // Old format: RGB (backward compatibility)
                            var r = (byte)bg[0].GetInt32();
                            var g = (byte)bg[1].GetInt32();
                            var b = (byte)bg[2].GetInt32();
                            col = Color.FromRgb(r, g, b);
                        }
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
                    background = new int[] { c.A, c.R, c.G, c.B },
                    backgroundTint = new int[] { backgroundTint.A, backgroundTint.R, backgroundTint.G, backgroundTint.B },
                    groundTint = new int[] { groundTint.A, groundTint.R, groundTint.G, groundTint.B }
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
            sprites = Enumerable.Repeat(-1, mapWidth * mapHeight).ToArray();
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
                
                // When in auto mode (not manual tile size), adjust column width to fit 16 tiles
                // When manual, keep the column fixed and allow scrollbars
                if (!manualTileSize)
                {
                    double ts = paletteTileSize;
                    // 16 tiles across, plus padding and the system vertical scrollbar width
                    double scrollbar = SystemParameters.VerticalScrollBarWidth;
                    double padding = 12;
                    double desired = Math.Max(160, ts * 16 + scrollbar + padding);
                    // set the left column width to desired
                    col.Width = new GridLength(desired, GridUnitType.Pixel);
                }

                if (initial)
                {
                    // On first run, expand the window MinWidth so the left column is fully visible,
                    // but avoid forcing the actual Window.Width (user should be able to resize freely).
                    double colWidth = col.ActualWidth > 0 ? col.ActualWidth : 260;
                    double splitterWidth = (RootGrid.ColumnDefinitions.Count > 1) ? RootGrid.ColumnDefinitions[1].ActualWidth : 5;
                    double mainAreaMin = 200; // smaller minimum for the main editor area to keep default window compact
                    double required = colWidth + splitterWidth + mainAreaMin + 40; // extra margins
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

                    // Disable horizontal scrollbars when not in manual mode (auto-sizing)
                    if (TilesPanel != null)
                    {
                        TilesPanel.Width = Double.NaN;
                        if (!manualTileSize)
                        {
                            TilesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                        }
                    }
                    if (SpritesPanel != null)
                    {
                        SpritesPanel.Width = Double.NaN;
                        if (!manualSpriteSize)
                        {
                            SpritesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
                        }
                    }
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
            try
            {
                // Try to load from embedded resources first
                var embeddedTileset = LoadEmbeddedImage("famidash.bmp");
                if (embeddedTileset != null)
                {
                    tilesetBitmap = embeddedTileset;
                    SliceTileset();
                    PopulateTilesPanel();
                    if (StatusText != null) StatusText.Text = "Loaded tileset from embedded resources";
                }
                
                var embeddedSprites = LoadEmbeddedImage("sprites.png");
                if (embeddedSprites != null)
                {
                    spritesBitmap = embeddedSprites;
                    SliceSpriteset();
                    PopulateSpritesPanel();
                    if (StatusText != null) StatusText.Text = "Loaded sprites from embedded resources";
                }
                
                var embeddedParallax = LoadEmbeddedImage("parallax.bmp");
                if (embeddedParallax != null)
                {
                    parallaxBitmap = embeddedParallax;
                    SliceParallax();
                    if (StatusText != null) StatusText.Text = "Loaded parallax from embedded resources";
                }
                
                var embeddedGround = LoadEmbeddedImage("ground.bmp");
                if (embeddedGround != null)
                {
                    groundBitmap = embeddedGround;
                    SliceGround();
                    if (StatusText != null) StatusText.Text = "Loaded ground from embedded resources";
                }
                
                // If embedded resources didn't load, try file system as fallback
                if (tilesetBitmap == null || spritesBitmap == null || parallaxBitmap == null || groundBitmap == null)
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
                                if (tilesetBitmap == null)
                                    foreach (var f in tilesCandidates) { var p = Path.Combine(d, f); if (File.Exists(p)) { LoadTileset(p); break; } }
                                if (spritesBitmap == null)
                                    foreach (var f in spriteCandidates) { var p = Path.Combine(d, f); if (File.Exists(p)) { LoadSpriteset(p); break; } }
                                if (parallaxBitmap == null)
                                    foreach (var f in parallaxCandidates) { var p = Path.Combine(d, f); if (File.Exists(p)) { LoadParallax(p); break; } }
                                if (groundBitmap == null)
                                    foreach (var f in groundCandidates) { var p = Path.Combine(d, f); if (File.Exists(p)) { LoadGround(p); break; } }
                            }
                        }
                        catch { }
                    }
                }
                
                // Initialize saw animation frames
                InitializeSawAnimationFrames();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LoadAssetsOnStart: {ex.Message}");
                // Don't let asset loading errors crash the app
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

        private BitmapImage? LoadEmbeddedImage(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();
                
                // Find the resource - it might have the full path prefix
                var fullResourceName = resourceNames.FirstOrDefault(r => r.EndsWith(resourceName));
                if (fullResourceName == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Resource not found: {resourceName}");
                    System.Diagnostics.Debug.WriteLine($"Available resources: {string.Join(", ", resourceNames)}");
                    return null;
                }
                
                using (var stream = assembly.GetManifestResourceStream(fullResourceName))
                {
                    if (stream == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load resource stream: {fullResourceName}");
                        return null;
                    }
                    
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = stream;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading embedded resource {resourceName}: {ex.Message}");
                return null;
            }
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
            int tileIdx = 0;
            int errorCount = 0;
            
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    try
                    {
                        var cropped = new CroppedBitmap(tilesetBitmap, new Int32Rect(x * TileSize, y * TileSize, TileSize, TileSize));
                        // Freeze the cropped bitmap to make it thread-safe and prevent issues
                        if (cropped.CanFreeze)
                        {
                            cropped.Freeze();
                        }
                        list.Add(cropped);
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        System.Diagnostics.Debug.WriteLine($"ERROR: Failed to crop tile {tileIdx} (0x{tileIdx:X}) at grid ({x},{y}): {ex.Message}");
                        // Add a transparent placeholder
                        var wb = new WriteableBitmap(TileSize, TileSize, 96, 96, PixelFormats.Bgra32, null);
                        wb.Freeze();
                        list.Add(wb);
                    }
                    tileIdx++;
                }
            }
            
            tileImages = list.ToArray();
            
            if (errorCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: SliceTileset had {errorCount} errors out of {tileImages.Length} tiles");
            }
            
            // update any toned cache when tileset changes
            UpdateTileTint();
        }

        private void SliceSpriteset()
        {
            if (spritesBitmap == null) { spriteImages = null; return; }
            int cols = Math.Max(1, spritesBitmap.PixelWidth / TileSize);
            int rows = Math.Max(1, spritesBitmap.PixelHeight / TileSize);
            var list = new List<ImageSource>();
            int spriteIdx = 0;
            int errorCount = 0;
            
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    try
                    {
                        var cropped = new CroppedBitmap(spritesBitmap, new Int32Rect(x * TileSize, y * TileSize, TileSize, TileSize));
                        // Freeze the cropped bitmap to make it thread-safe and prevent issues
                        if (cropped.CanFreeze)
                        {
                            cropped.Freeze();
                        }
                        list.Add(cropped);
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        System.Diagnostics.Debug.WriteLine($"ERROR: Failed to crop sprite {spriteIdx} (0x{spriteIdx:X}) at grid ({x},{y}): {ex.Message}");
                        // Add a transparent placeholder
                        var wb = new WriteableBitmap(TileSize, TileSize, 96, 96, PixelFormats.Bgra32, null);
                        wb.Freeze();
                        list.Add(wb);
                    }
                    spriteIdx++;
                }
            }
            
            spriteImages = list.ToArray();
            
            if (errorCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: SliceSpriteset had {errorCount} errors out of {spriteImages.Length} sprites");
            }
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
            
            // Also tint the animated saw frames
            sawFrame1TilesTinted = CreateHueShiftedImages(sawFrame1Tiles, tileTint);
            sawFrame2TilesTinted = CreateHueShiftedImages(sawFrame2Tiles, tileTint);
            smallSawFrame1TilesTinted = CreateHueShiftedImages(smallSawFrame1Tiles, tileTint);
            smallSawFrame2TilesTinted = CreateHueShiftedImages(smallSawFrame2Tiles, tileTint);
            largeSawFrame1TilesTinted = CreateHueShiftedImages(largeSawFrame1Tiles, tileTint);
            largeSawFrame2TilesTinted = CreateHueShiftedImages(largeSawFrame2Tiles, tileTint);
            
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
                
                // Debug: check if specific tiles have valid sources
                if ((idx == 34 || idx == 36) && paletteSrc == null)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: Tile {idx} has NULL palette source! Original: {src?.GetType().Name}, Toned: {(tileTonedImages != null && idx < tileTonedImages.Length ? tileTonedImages[idx]?.GetType().Name : "N/A")}");
                }
                
                var img = new Image { Source = paletteSrc, Width = paletteTileSize, Height = paletteTileSize, Stretch = Stretch.Fill, Tag = idx };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                
                // Mouse down starts selection
                img.MouseLeftButtonDown += (s, e) => { 
                    int clickedTile = (int)((Image)s).Tag;
                    
                    // Check if Ctrl is held for multi-layer selection
                    bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                    
                    if (isCtrl && selectedSprite >= 0)
                    {
                        // Ctrl+Click with sprite already selected: enable both layers (single tile only)
                        selectedTile = clickedTile;
                        selectedTiles = new List<int> { clickedTile };
                        selectionWidth = 1;
                        selectionHeight = 1;
                        tilesLayerActive = true;
                        spritesLayerActive = true;
                        if (StatusText != null) StatusText.Text = $"Selected tile {selectedTile} + sprite {selectedSprite} (both layers active)";
                        UpdatePaletteHighlight();
                    }
                    else
                    {
                        // Start multi-tile selection (disables sprite layer)
                        isSelectingMultipleTiles = true;
                        tileSelectionStart = e.GetPosition(TilesPanel);
                        selectedTile = clickedTile;
                        selectedTiles = new List<int> { clickedTile };
                        selectionWidth = 1;
                        selectionHeight = 1;
                        selectedSprite = -1; 
                        tilesLayerActive = true; 
                        spritesLayerActive = false;
                        if (StatusText != null) StatusText.Text = "Selected tile " + selectedTile;
                        UpdatePaletteHighlight();
                        ((Image)s).CaptureMouse();
                    }
                };
                
                // Mouse move updates selection
                img.MouseMove += (s, e) => {
                    if (isSelectingMultipleTiles && tileSelectionStart != null)
                    {
                        var currentPos = e.GetPosition(TilesPanel);
                        UpdateTileSelection(tileSelectionStart.Value, currentPos);
                        UpdatePaletteHighlight();
                    }
                };
                
                // Mouse up finalizes selection
                img.MouseLeftButtonUp += (s, e) => {
                    isSelectingMultipleTiles = false;
                    tileSelectionStart = null;
                    ((Image)s).ReleaseMouseCapture();
                };
                
                var border = new Border { 
                    Child = img, 
                    Margin = new Thickness(0), 
                    Padding = new Thickness(0), 
                    BorderBrush = Brushes.Transparent, 
                    BorderThickness = new Thickness(2) 
                };
                
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
                img.MouseLeftButtonDown += (s, e) => { 
                    int clickedSprite = (int)((Image)s).Tag;
                    
                    // Check if Ctrl is held for multi-layer selection
                    bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                    
                    // Only allow multi-layer if a single tile is selected (not multiple tiles)
                    if (isCtrl && selectedTile >= 0 && selectedTiles.Count == 1)
                    {
                        // Ctrl+Click with single tile selected: enable both layers
                        selectedSprite = clickedSprite;
                        spritesLayerActive = true;
                        tilesLayerActive = true;
                        if (StatusText != null) StatusText.Text = $"Selected tile {selectedTile} + sprite {selectedSprite} (both layers active)";
                    }
                    else
                    {
                        // Normal click: select only sprite, deselect tiles
                        selectedSprite = clickedSprite;
                        selectedTile = -1;
                        selectedTiles = new List<int>();
                        selectionWidth = 1;
                        selectionHeight = 1;
                        spritesLayerActive = true; 
                        tilesLayerActive = false;
                        if (StatusText != null) StatusText.Text = "Selected sprite " + selectedSprite;
                    }
                    
                    UpdatePaletteHighlight();
                };
                var border = new Border { Child = img, Margin = new Thickness(0), Padding = new Thickness(0), BorderBrush = (idx == selectedSprite ? Brushes.Yellow : Brushes.Transparent), BorderThickness = (idx == selectedSprite ? new Thickness(2) : new Thickness(0)) };
                SpritesPanel.Items.Add(border);
                idx++;
            }
        }

        private void UpdateTileSelection(System.Windows.Point start, System.Windows.Point end)
        {
            if (TilesPanel == null || tileImages == null) return;
            
            // TilesPanel uses UniformGrid with 16 columns
            const int tilesPerRow = 16;
            
            // Calculate the size of each cell in the grid
            double cellWidth = TilesPanel.ActualWidth / tilesPerRow;
            
            // Calculate actual number of rows in the grid
            int totalRows = (int)Math.Ceiling((double)tileImages.Length / tilesPerRow);
            double cellHeight = TilesPanel.ActualHeight / totalRows;
            
            // If height isn't ready yet, use estimated height
            if (cellHeight <= 0 || double.IsNaN(cellHeight) || double.IsInfinity(cellHeight))
            {
                cellHeight = paletteTileSize + 2; // Border thickness accounts for spacing
            }
            
            // Convert positions to tile indices
            int startCol = Math.Max(0, Math.Min((int)(start.X / cellWidth), tilesPerRow - 1));
            int startRow = Math.Max(0, (int)(start.Y / cellHeight));
            int endCol = Math.Max(0, Math.Min((int)(end.X / cellWidth), tilesPerRow - 1));
            int endRow = Math.Max(0, (int)(end.Y / cellHeight));
            
            // Get min/max bounds
            int minRow = Math.Min(startRow, endRow);
            int maxRow = Math.Max(startRow, endRow);
            int minCol = Math.Min(startCol, endCol);
            int maxCol = Math.Max(startCol, endCol);
            
            // Build selection list
            selectedTiles.Clear();
            selectionWidth = maxCol - minCol + 1;
            selectionHeight = maxRow - minRow + 1;
            
            for (int row = minRow; row <= maxRow; row++)
            {
                for (int col = minCol; col <= maxCol; col++)
                {
                    int idx = row * tilesPerRow + col;
                    if (idx >= 0 && idx < tileImages.Length)
                    {
                        selectedTiles.Add(idx);
                    }
                }
            }
            
            // Update selectedTile to be the first in selection
            if (selectedTiles.Count > 0)
            {
                selectedTile = selectedTiles[0];
                if (StatusText != null)
                {
                    if (selectedTiles.Count == 1)
                        StatusText.Text = $"Selected tile {selectedTile}";
                    else
                        StatusText.Text = $"Selected {selectedTiles.Count} tiles ({selectionWidth}x{selectionHeight})";
                }
            }
        }

        private void UpdatePaletteHighlight()
        {
            // Update tile highlights - show yellow border around all selected tiles
            if (TilesPanel != null)
            {
                for (int i = 0; i < TilesPanel.Items.Count; i++)
                {
                    if (TilesPanel.Items[i] is Border b)
                    {
                        bool isSelected = selectedTiles.Contains(i);
                        b.BorderBrush = isSelected ? Brushes.Yellow : Brushes.Transparent;
                        b.BorderThickness = isSelected ? new Thickness(2) : new Thickness(0);
                    }
                }
            }
            // Update sprite highlights - show yellow border if sprite is selected
            if (SpritesPanel != null)
            {
                for (int i = 0; i < SpritesPanel.Items.Count; i++)
                {
                    if (SpritesPanel.Items[i] is Border b)
                    {
                        bool isSelected = (i == selectedSprite && selectedSprite >= 0);
                        b.BorderBrush = isSelected ? Brushes.Yellow : Brushes.Transparent;
                        b.BorderThickness = isSelected ? new Thickness(2) : new Thickness(0);
                    }
                }
            }
            
            // Update layer label borders - both can be active simultaneously
            if (TilesLabelBorder != null)
            {
                TilesLabelBorder.BorderBrush = tilesLayerActive ? Brushes.Yellow : Brushes.Transparent;
            }
            if (SpritesLabelBorder != null)
            {
                SpritesLabelBorder.BorderBrush = spritesLayerActive ? Brushes.Yellow : Brushes.Transparent;
            }
        }

        private void TilesLabel_Click(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Ctrl+Click toggles tiles layer on/off
                tilesLayerActive = !tilesLayerActive;
                // Ensure at least one layer is active
                if (!tilesLayerActive && !spritesLayerActive)
                {
                    spritesLayerActive = true;
                }
                UpdatePaletteHighlight();
                string status = tilesLayerActive ? "TILES layer enabled" : "TILES layer disabled";
                if (StatusText != null) StatusText.Text = status;
            }
            else
            {
                // Regular click: activate only tiles layer
                tilesLayerActive = true;
                spritesLayerActive = false;
                // Select a tile if none selected
                if (selectedTile < 0) selectedTile = 0;
                selectedSprite = -1;
                UpdatePaletteHighlight();
                if (StatusText != null) StatusText.Text = "Switched to TILES layer";
            }
        }

        private void SpritesLabel_Click(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                // Ctrl+Click toggles sprites layer on/off
                spritesLayerActive = !spritesLayerActive;
                // Ensure at least one layer is active
                if (!tilesLayerActive && !spritesLayerActive)
                {
                    tilesLayerActive = true;
                }
                UpdatePaletteHighlight();
                string status = spritesLayerActive ? "SPRITES layer enabled" : "SPRITES layer disabled";
                if (StatusText != null) StatusText.Text = status;
            }
            else
            {
                // Regular click: activate only sprites layer
                spritesLayerActive = true;
                tilesLayerActive = false;
                // Select a sprite if none selected
                if (selectedSprite < 0) selectedSprite = 0;
                selectedTile = -1;
                UpdatePaletteHighlight();
                if (StatusText != null) StatusText.Text = "Switched to SPRITES layer";
            }
        }

        private void UpdateTileHighlight()
        {
            // Call the unified highlight method
            UpdatePaletteHighlight();
        }

        private void UpdateTileHighlight2()
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

            // Ensure the rendered layer bitmaps cover the visible editor area. If the ScrollViewer
            // viewport is larger than the padded map size (blank grey around the map), expand the
            // layer sizes to cover the viewport so parallax/ground fill the entire visible region.
            double displayFullW = paddedFullW;
            double displayFullH = paddedFullH;
            if (MapScrollViewer != null)
            {
                // Use ViewportWidth/Height when available; fall back to ActualWidth/Height.
                double vpw = MapScrollViewer.ViewportWidth > 0 ? MapScrollViewer.ViewportWidth : MapScrollViewer.ActualWidth;
                double vph = MapScrollViewer.ViewportHeight > 0 ? MapScrollViewer.ViewportHeight : MapScrollViewer.ActualHeight;
                if (!double.IsNaN(vpw) && vpw > displayFullW) displayFullW = vpw;
                if (!double.IsNaN(vph) && vph > displayFullH) displayFullH = vph;
            }

            var dpi = VisualTreeHelper.GetDpi(this);
            int pixelPaddedWidth = Math.Max(1, (int)Math.Ceiling(paddedFullW * dpi.DpiScaleX));
            int pixelPaddedHeight = Math.Max(1, (int)Math.Ceiling(paddedFullH * dpi.DpiScaleY));
            int pixelDisplayWidth = Math.Max(1, (int)Math.Ceiling(displayFullW * dpi.DpiScaleX));
            int pixelDisplayHeight = Math.Max(1, (int)Math.Ceiling(displayFullH * dpi.DpiScaleY));

            // Ensure background/grid/tiles bitmaps exist and match size/scale
            EnsureLayerBitmaps(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelDisplayWidth, pixelDisplayHeight);

            // Update UI image sources and sizes
            if (BackgroundImage != null && backgroundRtb != null)
            {
                BackgroundImage.Source = backgroundRtb;
                BackgroundImage.Width = displayFullW; BackgroundImage.Height = displayFullH;
                BackgroundImage.LayoutTransform = Transform.Identity; // Clear temporary zoom transform
            }
            if (ParallaxImage != null && parallaxRtb != null)
            {
                ParallaxImage.Source = parallaxRtb;
                ParallaxImage.Width = displayFullW; ParallaxImage.Height = displayFullH;
                ParallaxImage.RenderTransform = parallaxTransform; // Use only parallax transform
            }
            if (GroundImage != null && groundRtb != null)
            {
                GroundImage.Source = groundRtb;
                GroundImage.Width = displayFullW; GroundImage.Height = displayFullH;
                GroundImage.LayoutTransform = Transform.Identity; // Clear temporary zoom transform
            }
            if (TilesImage != null && tilesWb != null)
            {
                TilesImage.Source = tilesWb;
                TilesImage.Width = displayFullW; TilesImage.Height = displayFullH;
                TilesImage.LayoutTransform = Transform.Identity; // Clear temporary zoom transform
            }
            if (SpritesImage != null && spritesWb != null)
            {
                SpritesImage.Source = spritesWb;
                SpritesImage.Width = displayFullW; SpritesImage.Height = displayFullH;
                SpritesImage.LayoutTransform = Transform.Identity; // Clear temporary zoom transform
            }
            if (GridImage != null && gridRtb != null)
            {
                GridImage.Source = gridRtb;
                GridImage.Width = displayFullW; GridImage.Height = displayFullH;
                GridImage.LayoutTransform = Transform.Identity; // Clear temporary zoom transform
            }

            if (CanvasHost != null)
            {
                // CanvasHost should match the display size so overlays (hover/selection) align
                // with the expanded image layers that now cover the full viewport.
                CanvasHost.Width = displayFullW; CanvasHost.Height = displayFullH;
                CanvasHost.LayoutTransform = Transform.Identity; // Clear temporary zoom transform
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
                BuildParallaxBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi);
                BuildGroundBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi);
                backgroundDirty = false;
            }
            // Recreate grid if needed
            if (gridRtb == null || cachedPixelWidth != pixelPaddedWidth || cachedPixelHeight != pixelPaddedHeight || Math.Abs(cachedScale - scale) > 1e-6 || gridDirty)
            {
                BuildGridBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi);
                gridDirty = false;
            }
            // Create or recreate tiles writeable bitmap if size changed
            bool sizeOrScaleChanged = tilesWb == null || cachedPixelWidth != pixelPaddedWidth || cachedPixelHeight != pixelPaddedHeight || Math.Abs(cachedScale - scale) > 1e-6;
            if (sizeOrScaleChanged)
            {
                tilesWb = new WriteableBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
                cachedPixelWidth = pixelPaddedWidth; cachedPixelHeight = pixelPaddedHeight; cachedScale = scale;
                // initialize to transparent
                var empty = new byte[pixelPaddedHeight * tilesWb.BackBufferStride];
                tilesWb.WritePixels(new Int32Rect(0, 0, pixelPaddedWidth, pixelPaddedHeight), empty, tilesWb.BackBufferStride, 0);
                // Render tiles asynchronously to avoid blocking UI
                RebuildAllTilesBitmapAsync(scale, pad);
            }
            // Create or recreate sprites writeable bitmap if size changed (use same flag as tiles!)
            if (sizeOrScaleChanged)
            {
                spritesWb = new WriteableBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
                // initialize to transparent
                var empty = new byte[pixelPaddedHeight * spritesWb.BackBufferStride];
                spritesWb.WritePixels(new Int32Rect(0, 0, pixelPaddedWidth, pixelPaddedHeight), empty, spritesWb.BackBufferStride, 0);
                // Render sprites
                RebuildAllSpritesBitmap(scale, pad);
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
                    int groundRowsToDraw = (groundTileRows > 0) ? groundTileRows : 0;
                    // Determine display size in device-independent units from provided pixel sizes
                    double displayFullW = pixelPaddedWidth / dpi.DpiScaleX;
                    double displayFullH = pixelPaddedHeight / dpi.DpiScaleY;
                    // Only draw tiles that fit in the actual bitmap bounds (no need to cover entire scrollable area)
                    int colsToCover = (int)Math.Ceiling(displayFullW / (TileSize * scale)) + 1;
                    int rowsToCover = (int)Math.Ceiling(displayFullH / (TileSize * scale)) + 1;
                    // Start from origin (0,0), only draw what's visible in viewport
                    int startRow = -(int)Math.Ceiling(pad / (TileSize * scale));
                    int endRow = startRow + rowsToCover;
                    int startCol = -(int)Math.Ceiling(pad / (TileSize * scale));
                    int endCol = startCol + colsToCover;
                    
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
                    // Determine display width so ground extends left/right to fill viewport
                    double displayFullW = pixelPaddedWidth / dpi.DpiScaleX;
                    double displayFullH = pixelPaddedHeight / dpi.DpiScaleY;
                    // Only draw columns that fit in viewport
                    int colsToCover = (int)Math.Ceiling(displayFullW / (TileSize * scale)) + 1;
                    int startCol = -(int)Math.Ceiling(pad / (TileSize * scale));
                    int endCol = startCol + colsToCover;

                    // Determine how many ground tile rows we need to draw
                    double mapAreaH = mapHeight * TileSize * scale;
                    // rows below the map needed to cover the display (include padding)
                    int rowsBelowNeeded = Math.Max(0, (int)Math.Ceiling((displayFullH - mapAreaH - pad) / (TileSize * scale)));
                    // ensure at least the source ground rows are drawn once
                    int rowsToDraw = Math.Max(groundRowsToDraw, rowsBelowNeeded);
                    // add one extra row as a safety margin for rounding errors
                    rowsToDraw += 1;

                    for (int gy = 0; gy < rowsToDraw; gy++)
                    {
                        for (int gx = startCol; gx < endCol; gx++)
                        {
                            int wrappedX = ((gx % cols) + cols) % cols;
                            int srcRow;
                            if (groundRowsToDraw <= 1) srcRow = 0;
                            else if (gy < groundRowsToDraw) srcRow = gy;
                            else
                            {
                                int repeatIndex = (gy - groundRowsToDraw) % (groundRowsToDraw - 1);
                                srcRow = 1 + repeatIndex;
                            }
                            int idx = (srcRow * cols + wrappedX) % groundImages.Length;
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

        // Async versions of parallax and ground rendering to avoid blocking UI during zoom
        // Async wrapper removed - was causing rendering issues
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
        
        // Provide immediate visual feedback during zoom by scaling existing images
        // This avoids the delay of rebuilding all bitmaps
        private void UpdateQuickZoomTransform()
        {
            if (ZoomSlider == null) return;
            double newScale = ZoomSlider.Value;
            
            // Calculate scale factor relative to cached scale
            if (cachedScale > 0 && Math.Abs(cachedScale - newScale) > 1e-6)
            {
                double scaleFactor = newScale / cachedScale;
                var scaleTransform = new ScaleTransform(scaleFactor, scaleFactor);
                
                // Apply temporary scale transform to all image layers
                if (BackgroundImage != null) BackgroundImage.LayoutTransform = scaleTransform;
                if (ParallaxImage != null)
                {
                    // Combine with parallax translate transform
                    var group = new TransformGroup();
                    group.Children.Add(scaleTransform);
                    if (parallaxTransform != null) group.Children.Add(parallaxTransform);
                    ParallaxImage.RenderTransform = group;
                }
                if (GroundImage != null) GroundImage.LayoutTransform = scaleTransform;
                if (TilesImage != null) TilesImage.LayoutTransform = scaleTransform;
                if (GridImage != null) GridImage.LayoutTransform = scaleTransform;
                if (CanvasHost != null) CanvasHost.LayoutTransform = scaleTransform;
            }
        }

        // Prevent the ScrollViewer from scrolling below the last visible ground row.
        // This clamps the vertical offset so the viewport bottom never goes past the bottom
        // of the map+ground content area (including padding). Called from scroll/zoom handlers.
        private void ClampScrollOffsets()
        {
            if (MapScrollViewer == null) return;
            // Compute the padded full height (map + ground + parallax padding) using current zoom
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            double fullH = mapHeight * TileSize * scale;
            int groundRowsToDraw = (groundTileRows > 0) ? groundTileRows : 0;
            double extraGroundH = (groundImages != null && groundRowsToDraw > 0) ? (TileSize * scale * groundRowsToDraw) : 0.0;
            double extraParallaxH = (parallaxImages != null) ? (TileSize * scale * parallaxBelowRows) : 0.0;
            fullH += extraGroundH + extraParallaxH;
            double paddedFullH = fullH + pad * 2.0;

            // compute maximum allowed vertical offset so viewport bottom <= paddedFullH
            double maxAllowedV = Math.Max(0.0, paddedFullH - MapScrollViewer.ViewportHeight);
            // If the current VerticalOffset is larger than allowed, snap it back
            if (MapScrollViewer.VerticalOffset > maxAllowedV + 1e-6)
            {
                MapScrollViewer.ScrollToVerticalOffset(maxAllowedV);
            }
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
            
            // Ensure we have a pre-scaled tile cache for this zoom/dpi to avoid per-tile scaling work
            EnsureScaledTileCache(scale, dpi);
            
            // Lock the bitmap once for all updates to improve performance
            tilesWb.Lock();
            try
            {
                // Clear the bitmap
                unsafe
                {
                    IntPtr pBackBuffer = tilesWb.BackBuffer;
                    int backBufferStride = tilesWb.BackBufferStride;
                    int bytesTotal = backBufferStride * cachedPixelHeight;
                    byte* ptr = (byte*)pBackBuffer.ToPointer();
                    for (int i = 0; i < bytesTotal; i++)
                    {
                        ptr[i] = 0;
                    }
                }
                
                // Write each tile using cached pixels
                for (int y = 0; y < mapHeight; y++)
                {
                    for (int x = 0; x < mapWidth; x++)
                    {
                        UpdateTileBitmapAtLocked(x, y, scale, pad, tilePixelW, tilePixelH, dpi);
                    }
                }
                
                // Mark entire bitmap as dirty
                tilesWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
            }
            finally
            {
                tilesWb.Unlock();
            }
        }
        
        // Async version that renders tiles in small batches to avoid blocking UI
        private async void RebuildAllTilesBitmapAsync(double scale, double pad)
        {
            if (tilesWb == null) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
            
            // Ensure we have a pre-scaled tile cache for this zoom/dpi
            EnsureScaledTileCache(scale, dpi);
            
            // Render tiles in larger batches for better performance
            int batchSize = Math.Max(500, mapWidth); // At least one full row, or 500 tiles
            int totalTiles = mapWidth * mapHeight;
            
            for (int batchStart = 0; batchStart < totalTiles; batchStart += batchSize)
            {
                int batchEnd = Math.Min(batchStart + batchSize, totalTiles);
                
                await System.Threading.Tasks.Task.Run(() =>
                {
                    // Process batch on background thread
                    for (int i = batchStart; i < batchEnd; i++)
                    {
                        int x = i % mapWidth;
                        int y = i / mapWidth;
                        int idx = tiles[y * mapWidth + x];
                        if (idx >= 0)
                        {
                            // Pre-render this tile into cache
                            int scaleKey = (int)Math.Round(scale * 100.0);
                            GetOrRenderCachedTile(idx, scaleKey, tilePixelW, tilePixelH, dpi);
                        }
                    }
                });
                
                // Update UI on main thread
                Dispatcher.Invoke(() =>
                {
                    if (tilesWb == null) return;
                    tilesWb.Lock();
                    try
                    {
                        for (int i = batchStart; i < batchEnd; i++)
                        {
                            int x = i % mapWidth;
                            int y = i / mapWidth;
                            UpdateTileBitmapAtLocked(x, y, scale, pad, tilePixelW, tilePixelH, dpi);
                        }
                        // Mark this batch area as dirty
                        int minY = batchStart / mapWidth;
                        int maxY = (batchEnd - 1) / mapWidth;
                        int dirtyHeight = (maxY - minY + 1) * tilePixelH;
                        tilesWb.AddDirtyRect(new Int32Rect(0, (int)(minY * tilePixelH + pad * dpi.DpiScaleY), cachedPixelWidth, Math.Min(dirtyHeight, cachedPixelHeight)));
                    }
                    finally
                    {
                        tilesWb.Unlock();
                    }
                });
                
                // No delay - process as fast as possible
                // await System.Threading.Tasks.Task.Delay(1);
            }
        }

        // Build or ensure a scaled tile pixel cache for the given zoom and dpi.
        // The cache stores raw BGRA32 pixel arrays for each tileImage scaled to the target tile pixel size.
        // Changed to lazy initialization - creates empty cache, tiles are rendered on-demand
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
                // Allocate cache for 512 tiles: 0-255 for regular tiles, 256-511 for sprites
                int count = 512;
                
                // Create empty cache - tiles will be rendered on-demand
                var pixelsArr = new byte[count][];
                
                var cache = new ScaledTileCache(pixelsArr, tilePixelW, tilePixelH, stride, scale, dpi);
                scaledTileCaches[scaleKey] = cache;
            }
            catch { /* don't crash on cache build failure */ }
        }
        
        // Render and cache a single tile at the given scale (lazy caching)
        private byte[]? GetOrRenderCachedTile(int tileIdx, int scaleKey, int tilePixelW, int tilePixelH, DpiScale dpi)
        {
            if (tileImages == null) return null;
            
            // Check if this is a custom animation tile (>= 1000)
            if (tileIdx >= 1000)
            {
                var customTile = GetCustomAnimationTile(tileIdx);
                if (customTile != null)
                {
                    // Render the custom tile directly (don't cache since it's animated)
                    int customStride = tilePixelW * 4;
                    var customBuf = new byte[tilePixelH * customStride];
                    try
                    {
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            // Check if this is a half-height tile (8 pixels tall source)
                            if (IsHalfHeightTile(tileIdx))
                            {
                                // For half-height tiles (16x8 source), scale proportionally
                                // The source is 16x8, we want it to occupy 8 scaled pixels height
                                // Calculate the scale factor from the tile size
                                double scale = tilePixelH / 16.0; // How much are we scaling from base 16px tile
                                double scaledHeight = 8 * scale;  // 8 pixels scaled
                                
                                // Top-half tiles (0x7F) need to be positioned at the bottom of the tile space
                                if (IsTopHalfTile(tileIdx))
                                {
                                    double yOffset = tilePixelH - scaledHeight; // Position at bottom
                                    dc.DrawImage(customTile, new Rect(0, yOffset, tilePixelW, scaledHeight));
                                }
                                else
                                {
                                    // Bottom-half tiles (0x04) stay at the top
                                    dc.DrawImage(customTile, new Rect(0, 0, tilePixelW, scaledHeight));
                                }
                            }
                            else
                            {
                                // Full-height tiles (16x16 source) use the entire tile space
                                dc.DrawImage(customTile, new Rect(0, 0, tilePixelW, tilePixelH));
                            }
                        }
                        var rtb = new RenderTargetBitmap(tilePixelW, tilePixelH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                        rtb.Render(dv);
                        rtb.CopyPixels(customBuf, customStride, 0);
                    }
                    catch { }
                    return customBuf;
                }
                return null;
            }
            
            if (!scaledTileCaches.TryGetValue(scaleKey, out var cache)) return null;
            if (tileIdx < 0 || tileIdx >= cache.Pixels.Length) return null;
            
            // Check if already cached
            if (cache.Pixels[tileIdx] != null) return cache.Pixels[tileIdx];
            
            // Render and cache it
            int stride = tilePixelW * 4;
            var buf = new byte[tilePixelH * stride];
            try
            {
                ImageSource? src = null;
                
                // Handle sprites (indices 256-511)
                if (tileIdx >= 256 && tileIdx < 512)
                {
                    int spriteIdx = tileIdx - 256;
                    if (spriteImages != null && spriteIdx < spriteImages.Length)
                    {
                        src = spriteImages[spriteIdx] as ImageSource;
                    }
                }
                // Handle regular tiles (indices 0-255)
                else if (tileIdx >= 0 && tileIdx < 256)
                {
                    // Prefer tinted tiles when available
                    src = (tileTonedImages != null && tileIdx < tileTonedImages.Length) 
                        ? tileTonedImages[tileIdx] as ImageSource 
                        : (tileIdx < tileImages.Length ? tileImages[tileIdx] as ImageSource : null);
                }
                
                // Debug: log if specific tiles fail to get source
                if (src == null && (tileIdx == 34 || tileIdx == 36))
                {
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Tile {tileIdx} has NULL source! tileImages.Length={tileImages?.Length}, tileTonedImages.Length={tileTonedImages?.Length}");
                }
                
                if (src != null)
                {
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen()) dc.DrawImage(src, new Rect(0, 0, tilePixelW, tilePixelH));
                    var rtb = new RenderTargetBitmap(tilePixelW, tilePixelH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    rtb.CopyPixels(buf, stride, 0);
                    
                    // Debug: check if pixels are actually non-zero
                    if (tileIdx == 34 || tileIdx == 36)
                    {
                        int nonZero = buf.Count(b => b != 0);
                        System.Diagnostics.Debug.WriteLine($"DEBUG: Rendered tile {tileIdx}, {nonZero}/{buf.Length} non-zero bytes");
                    }
                }
            }
            catch (Exception ex) 
            {
                if (tileIdx == 34 || tileIdx == 36)
                {
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Exception rendering tile {tileIdx}: {ex.Message}");
                }
            }
            
            cache.Pixels[tileIdx] = buf;
            return buf;
        }

        // Update a single tile's pixels inside the tiles writeable bitmap
        private void UpdateTileBitmapAt(int x, int y, double scale, double pad, int? preTilePxW = null, int? preTilePxH = null, DpiScale? preDpi = null)
        {
            if (tilesWb == null) return;
            var dpi = preDpi ?? VisualTreeHelper.GetDpi(this);
            int tilePixelW = preTilePxW ?? Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int tilePixelH = preTilePxH ?? Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));

            int idx = tiles[y * mapWidth + x];
            
            // Apply animation mapping if in preview mode
            int animatedIdx = GetAnimatedTileIndex(idx);
            
            int destX = Math.Max(0, (int)Math.Floor((pad + x * TileSize * scale) * dpi.DpiScaleX));
            int destY = Math.Max(0, (int)Math.Floor((pad + y * TileSize * scale) * dpi.DpiScaleY));

            // Use lazy-cached pixels
            int stride = tilePixelW * 4;
            byte[] pixels;
            int scaleKey = (int)Math.Round(scale * 100.0);
            
            if (animatedIdx >= 0)
            {
                var cached = GetOrRenderCachedTile(animatedIdx, scaleKey, tilePixelW, tilePixelH, dpi);
                if (cached != null && cached.Length > 0)
                {
                    pixels = cached;
                }
                else
                {
                    pixels = new byte[tilePixelH * stride]; // Empty/transparent
                }
            }
            else
            {
                pixels = new byte[tilePixelH * stride]; // Empty tile
            }

            try
            {
                tilesWb.WritePixels(new Int32Rect(destX, destY, Math.Min(tilePixelW, cachedPixelWidth - destX), Math.Min(tilePixelH, cachedPixelHeight - destY)), pixels, stride, 0);
            }
            catch { }
            // assign to image source (TilesImage) done in DrawMap/Ensure
        }

        // Fast version of UpdateTileBitmapAt that works with a locked WriteableBitmap
        // Caller must lock/unlock the tilesWb before/after calling this
        private unsafe void UpdateTileBitmapAtLocked(int x, int y, double scale, double pad, int tilePixelW, int tilePixelH, DpiScale dpi)
        {
            if (tilesWb == null) return;
            
            int idx = tiles[y * mapWidth + x];
            if (idx < 0) return; // Empty tile, already cleared
            
            // Apply animation mapping if in preview mode
            int animatedIdx = GetAnimatedTileIndex(idx);
            
            int destX = Math.Max(0, (int)Math.Floor((pad + x * TileSize * scale) * dpi.DpiScaleX));
            int destY = Math.Max(0, (int)Math.Floor((pad + y * TileSize * scale) * dpi.DpiScaleY));
            
            // Bounds check
            if (destX >= cachedPixelWidth || destY >= cachedPixelHeight) return;
            
            // Get cached pixels (lazy render if not cached) - use animated index
            int scaleKey = (int)Math.Round(scale * 100.0);
            byte[]? srcPixels = GetOrRenderCachedTile(animatedIdx, scaleKey, tilePixelW, tilePixelH, dpi);
            if (srcPixels == null || srcPixels.Length == 0) return;
            
            // Copy pixels directly to locked buffer
            IntPtr pBackBuffer = tilesWb.BackBuffer;
            int backBufferStride = tilesWb.BackBufferStride;
            int copyWidth = Math.Min(tilePixelW, cachedPixelWidth - destX);
            int copyHeight = Math.Min(tilePixelH, cachedPixelHeight - destY);
            int srcStride = tilePixelW * 4;
            
            for (int row = 0; row < copyHeight; row++)
            {
                int srcOffset = row * srcStride;
                long destOffset = (destY + row) * backBufferStride + destX * 4;
                byte* destPtr = (byte*)pBackBuffer.ToPointer() + destOffset;
                
                for (int col = 0; col < copyWidth * 4; col++)
                {
                    destPtr[col] = srcPixels[srcOffset + col];
                }
            }
        }

        private void RebuildAllSpritesBitmap(double scale, double pad)
        {
            if (spritesWb == null || spriteImages == null) return;
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"RebuildAllSpritesBitmap: scale={scale}, pad={pad}, mapWidth={mapWidth}, mapHeight={mapHeight}");
                
                var dpi = VisualTreeHelper.GetDpi(this);
                int spritePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                int spritePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                
                System.Diagnostics.Debug.WriteLine($"  spritePixelW={spritePixelW}, spritePixelH={spritePixelH}, dpi={dpi.DpiScaleX}");
                System.Diagnostics.Debug.WriteLine($"  cachedPixelWidth={cachedPixelWidth}, cachedPixelHeight={cachedPixelHeight}");
                
                // Lock the bitmap once for all updates
                spritesWb.Lock();
                try
                {
                    System.Diagnostics.Debug.WriteLine($"  Bitmap locked, clearing...");
                    // Clear the bitmap
                    unsafe
                    {
                        IntPtr pBackBuffer = spritesWb.BackBuffer;
                        int backBufferStride = spritesWb.BackBufferStride;
                        int bytesTotal = backBufferStride * cachedPixelHeight;
                        byte* ptr = (byte*)pBackBuffer.ToPointer();
                        for (int i = 0; i < bytesTotal; i++)
                        {
                            ptr[i] = 0;
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"  Cleared, now rendering {mapWidth}x{mapHeight} sprites...");
                    // Write each sprite
                    int spriteCount = 0;
                    for (int y = 0; y < mapHeight; y++)
                    {
                        for (int x = 0; x < mapWidth; x++)
                        {
                            int idx = sprites[y * mapWidth + x];
                            if (idx >= 0 && idx < spriteImages.Length)
                            {
                                spriteCount++;
                                try
                                {
                                    UpdateSpriteBitmapAtLocked(x, y, idx, scale, pad, spritePixelW, spritePixelH, dpi);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"    Error at sprite ({x},{y}): {ex.Message}");
                                }
                            }
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"  Rendered {spriteCount} sprites, marking dirty...");
                    // Mark entire bitmap as dirty
                    spritesWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
                    System.Diagnostics.Debug.WriteLine($"  Marked dirty");
                }
                finally
                {
                    System.Diagnostics.Debug.WriteLine($"  Unlocking bitmap...");
                    spritesWb.Unlock();
                    System.Diagnostics.Debug.WriteLine($"  Unlocked");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EXCEPTION in RebuildAllSpritesBitmap: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
                // If rebuild fails, just clear the sprites
                if (spritesWb != null)
                {
                    try
                    {
                        var empty = new byte[cachedPixelHeight * spritesWb.BackBufferStride];
                        spritesWb.WritePixels(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight), empty, spritesWb.BackBufferStride, 0);
                    }
                    catch { }
                }
                throw; // Re-throw so we can see the error
            }
        }

        private void UpdateSpriteBitmapAtLocked(int x, int y, int spriteIdx, double scale, double pad, int spritePixelW, int spritePixelH, DpiScale dpi)
        {
            if (spritesWb == null || spriteImages == null || spriteIdx < 0 || spriteIdx >= spriteImages.Length) return;
            
            try
            {
                // Use same position calculation as tiles for perfect alignment
                int destX = Math.Max(0, (int)Math.Floor((pad + x * TileSize * scale) * dpi.DpiScaleX));
                int destY = Math.Max(0, (int)Math.Floor((pad + y * TileSize * scale) * dpi.DpiScaleY));
                
                // Bounds check
                if (destX >= cachedPixelWidth || destY >= cachedPixelHeight) return;
                
                // Get the source sprite
                var sprite = spriteImages[spriteIdx] as BitmapSource;
                if (sprite == null) return;
                
                // Calculate the actual size we need to render
                int srcWidth = sprite.PixelWidth;
                int srcHeight = sprite.PixelHeight;
                
                // Sanity check dimensions
                if (srcWidth <= 0 || srcHeight <= 0 || spritePixelW <= 0 || spritePixelH <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid dimensions: src={srcWidth}x{srcHeight}, dest={spritePixelW}x{spritePixelH}");
                    return;
                }
                
                // Copy source sprite pixels
                int srcStride = srcWidth * 4;
                byte[] srcPixels = new byte[srcHeight * srcStride];
                sprite.CopyPixels(srcPixels, srcStride, 0);
                
                // If no scaling needed and sprite is already correct size, copy directly
                if (Math.Abs(scale - 1.0) < 0.001 && Math.Abs(dpi.DpiScaleX - 1.0) < 0.001 && srcWidth == TileSize && srcHeight == TileSize)
                {
                    // Direct copy - no scaling
                    IntPtr pBackBuffer = spritesWb.BackBuffer;
                    int backBufferStride = spritesWb.BackBufferStride;
                    int copyWidth = Math.Min(srcWidth, cachedPixelWidth - destX);
                    int copyHeight = Math.Min(srcHeight, cachedPixelHeight - destY);
                    
                    unsafe
                    {
                        for (int row = 0; row < copyHeight; row++)
                        {
                            int srcOffset = row * srcStride;
                            long destOffset = (destY + row) * backBufferStride + destX * 4;
                            byte* destPtr = (byte*)pBackBuffer.ToPointer() + destOffset;
                            
                            for (int col = 0; col < copyWidth * 4; col++)
                            {
                                destPtr[col] = srcPixels[srcOffset + col];
                            }
                        }
                    }
                }
                else
                {
                    // Need to scale - use simple nearest-neighbor scaling to avoid TransformedBitmap issues
                    double scaleX = (double)spritePixelW / srcWidth;
                    double scaleY = (double)spritePixelH / srcHeight;
                    
                    IntPtr pBackBuffer = spritesWb.BackBuffer;
                    int backBufferStride = spritesWb.BackBufferStride;
                    int copyWidth = Math.Min(spritePixelW, cachedPixelWidth - destX);
                    int copyHeight = Math.Min(spritePixelH, cachedPixelHeight - destY);
                    
                    unsafe
                    {
                        for (int row = 0; row < copyHeight; row++)
                        {
                            for (int col = 0; col < copyWidth; col++)
                            {
                                // Map destination pixel back to source pixel (nearest neighbor)
                                int srcX = Math.Min((int)(col / scaleX), srcWidth - 1);
                                int srcY = Math.Min((int)(row / scaleY), srcHeight - 1);
                                int srcOffset = srcY * srcStride + srcX * 4;
                                
                                long destOffset = (destY + row) * backBufferStride + (destX + col) * 4;
                                byte* destPtr = (byte*)pBackBuffer.ToPointer() + destOffset;
                                
                                // Copy BGRA pixel
                                destPtr[0] = srcPixels[srcOffset + 0]; // B
                                destPtr[1] = srcPixels[srcOffset + 1]; // G
                                destPtr[2] = srcPixels[srcOffset + 2]; // R
                                destPtr[3] = srcPixels[srcOffset + 3]; // A
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR rendering sprite at ({x},{y}), idx={spriteIdx}: {ex.Message}");
            }
        }

        private void CanvasHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CanvasHost == null) return;
            var pos = e.GetPosition(CanvasHost);
            
            // Track mouse down position and reset movement flag
            mouseDownPosition = pos;
            hasMouseMoved = false;
            
            // Magic Wand tool: select connected region of same tile
            if (MagicWandTool != null && MagicWandTool.IsChecked == true)
            {
                StartMagicWandAt(pos);
                return;
            }
            // Branch behavior based on active tool
            // Select tool: support Ctrl+click to toggle single-tile selection, or drag to rectangle-select
            if (SelectTool != null && SelectTool.IsChecked == true)
            {
                // Ctrl+click toggles the tile under cursor
                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    ToggleSelectionAt(pos);
                    return;
                }
                // otherwise start rectangle selection
                StartSelectionAt(pos);
                return;
            }
            // Move tool: begin dragging if we have an existing selection, otherwise pick tile/sprite under cursor
            if (MoveTool != null && MoveTool.IsChecked == true)
            {
                double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
                double pad = mapViewportPadding;
                double relX = pos.X - pad;
                double relY = pos.Y - pad;
                int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(relX / (TileSize * scale))));
                int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(relY / (TileSize * scale))));
                if (selectionSet != null && selectionSet.Count > 0)
                {
                    // start drag-move of selection
                    StartDragMove(pos);
                    return;
                }
                // If no selection, check the tile/sprite under cursor based on active layers
                int idx = y * mapWidth + x;
                int tileVal = tilesLayerActive ? tiles[idx] : -1;
                int spriteVal = spritesLayerActive ? sprites[idx] : -1;
                
                if (tileVal != -1 || spriteVal != -1)
                {
                    // create a 1x1 selection at this tile/sprite and begin dragging
                    // Only include the layers that are active (not based on what exists)
                    selX = x; selY = y; selW = 1; selH = 1;
                    selTiles = new int[1] { tileVal };
                    selSprites = new int[1] { spriteVal };
                    selectionSet!.Clear(); selectionSet.Add(idx);
                    // show selection visuals
                    UpdateSelectionVisuals(selX, selY, selW, selH);
                    StartDragMove(pos);
                    return;
                }
                // otherwise nothing to drag
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
            if (CanvasHost == null) return;
            if (isDraggingSelection)
            {
                EndDragMove(e.GetPosition(CanvasHost));
                return;
            }
            if (isSelecting)
            {
                EndSelection();
                return;
            }
            if (isPainting)
            {
                StopPainting();
                return;
            }
            
            // Handle single click - check if we should deselect
            if (selectionSet.Count > 0)
            {
                var pos = e.GetPosition(CanvasHost);
                double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
                double pad = mapViewportPadding;
                double relX = pos.X - pad;
                double relY = pos.Y - pad;
                int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(relX / (TileSize * scale))));
                int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(relY / (TileSize * scale))));
                int idx = y * mapWidth + x;
                
                // If Select or MagicWand tool active and clicked outside selection, clear it
                // (unless Ctrl is held for additive selection)
                // Only deselect on true click (not drag end)
                bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                if (!isCtrl && !selectionSet.Contains(idx) && !hasMouseMoved)
                {
                    if ((SelectTool != null && SelectTool.IsChecked == true) || 
                        (MagicWandTool != null && MagicWandTool.IsChecked == true) ||
                        (MoveTool != null && MoveTool.IsChecked == true))
                    {
                        ClearSelection();
                    }
                }
            }
        }

        private void CanvasHost_MouseMove(object sender, MouseEventArgs e)
        {
            if (CanvasHost == null) return;
            var pos = e.GetPosition(CanvasHost);
            
            // Track if mouse has moved since button down (for click vs drag detection)
            if (e.LeftButton == MouseButtonState.Pressed && !hasMouseMoved)
            {
                const double dragThreshold = 3.0; // pixels
                double dx = pos.X - mouseDownPosition.X;
                double dy = pos.Y - mouseDownPosition.Y;
                if (Math.Sqrt(dx * dx + dy * dy) > dragThreshold)
                {
                    hasMouseMoved = true;
                }
            }
            
            UpdateCoords(pos);
            // If actively selecting, update the selection rectangle
            if (isDraggingSelection && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateDragMoveTo(pos);
                return;
            }
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
            var all = new[] { PlaceTool, MoveTool, EraseTool, FillTool, SelectTool, MagicWandTool };
            foreach (var t in all)
            {
                if (t != tb) t.IsChecked = false;
            }
            
            // Sync menu checkmarks with toolbar
            if (MenuToolPlace != null) MenuToolPlace.IsChecked = (tb == PlaceTool);
            if (MenuToolMove != null) MenuToolMove.IsChecked = (tb == MoveTool);
            if (MenuToolErase != null) MenuToolErase.IsChecked = (tb == EraseTool);
            if (MenuToolFill != null) MenuToolFill.IsChecked = (tb == FillTool);
            if (MenuToolSelect != null) MenuToolSelect.IsChecked = (tb == SelectTool);
            if (MenuToolWand != null) MenuToolWand.IsChecked = (tb == MagicWandTool);
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
                hasUnsavedChanges = true;
            }
            // update tiles bitmap after flood
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
        }

        private void SpriteFloodFill(int sx, int sy, int target, int replacement)
        {
            if (target == replacement) return;
            var action = new SpriteChangeAction();
            var q = new System.Collections.Generic.Queue<(int x, int y)>();
            q.Enqueue((sx, sy));
            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue();
                if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) continue;
                int idx = y * mapWidth + x;
                if (sprites[idx] != target) continue;
                action.Add(idx, target, replacement);
                sprites[idx] = replacement;
                q.Enqueue((x + 1, y)); q.Enqueue((x - 1, y)); q.Enqueue((x, y + 1)); q.Enqueue((x, y - 1));
            }
            if (!action.IsEmpty() && !suppressUndoRecording)
            {
                undoStack.Push(action);
                redoStack.Clear();
                hasUnsavedChanges = true;
            }
            // update sprites bitmap after flood
            try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
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
                bool didFill = false;
                // Fill tiles layer if active
                if (tilesLayerActive && selectedTile >= 0)
                {
                    int target = tiles[y * mapWidth + x];
                    if (target != selectedTile)
                    {
                        FloodFill(x, y, target, selectedTile);
                        didFill = true;
                    }
                }
                // Fill sprites layer if active
                if (spritesLayerActive && selectedSprite >= 0)
                {
                    int target = sprites[y * mapWidth + x];
                    if (target != selectedSprite)
                    {
                        SpriteFloodFill(x, y, target, selectedSprite);
                        didFill = true;
                    }
                }
                if (!didFill && !tilesLayerActive && !spritesLayerActive)
                {
                    // Fallback: if no layers active, just redraw
                    Redraw();
                }
                return;
            }

            // For Move tool, pick the tile/sprite under cursor into selection (respects active layers)
            if (MoveTool != null && MoveTool.IsChecked == true)
            {
                bool pickedTile = false;
                bool pickedSprite = false;
                
                if (tilesLayerActive)
                {
                    int tile = tiles[y * mapWidth + x];
                    if (tile >= 0)
                    {
                        selectedTile = tile;
                        pickedTile = true;
                    }
                }
                
                if (spritesLayerActive)
                {
                    int sprite = sprites[y * mapWidth + x];
                    if (sprite >= 0)
                    {
                        selectedSprite = sprite;
                        pickedSprite = true;
                    }
                }
                
                // Update layer active states based on what was picked
                if (pickedTile || pickedSprite)
                {
                    // If we picked from only one layer, deactivate the other
                    if (pickedTile && !pickedSprite)
                    {
                        tilesLayerActive = true;
                        spritesLayerActive = false;
                        selectedSprite = -1;
                    }
                    else if (pickedSprite && !pickedTile)
                    {
                        spritesLayerActive = true;
                        tilesLayerActive = false;
                        selectedTile = -1;
                    }
                    // If we picked from both layers, keep both active
                    
                    UpdatePaletteHighlight();
                    if (StatusText != null)
                    {
                        if (pickedTile && pickedSprite)
                            StatusText.Text = $"Picked tile {selectedTile} and sprite {selectedSprite}";
                        else if (pickedTile)
                            StatusText.Text = $"Picked tile {selectedTile}";
                        else
                            StatusText.Text = $"Picked sprite {selectedSprite}";
                    }
                }
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
            // Show initial preview selection rectangle
            UpdateSelectionVisuals(x, y, 1, 1, previewMode: true);
            if (CanvasHost != null) CanvasHost.CaptureMouse();
        }

        private void ToggleSelectionAt(Point pos)
        {
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)((pos.X - pad) / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)((pos.Y - pad) / (TileSize * scale))));
            int idx = y * mapWidth + x;
            // Only toggle selection for valid (non-empty) tiles or sprites in active layers
            bool hasTile = tilesLayerActive && tiles[idx] != -1;
            bool hasSprite = spritesLayerActive && sprites[idx] != -1;
            if (!hasTile && !hasSprite) return;
            
            if (selectionSet.Contains(idx)) selectionSet.Remove(idx); else selectionSet.Add(idx);

            if (selectionSet.Count == 0)
            {
                ClearSelection();
                return;
            }

            // compute bounding box of selectionSet
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var i in selectionSet)
            {
                int sx = i % mapWidth; int sy = i / mapWidth;
                if (sx < minX) minX = sx; if (sy < minY) minY = sy;
                if (sx > maxX) maxX = sx; if (sy > maxY) maxY = sy;
            }
            selX = minX; selY = minY; selW = maxX - minX + 1; selH = maxY - minY + 1;
            selTiles = new int[selW * selH];
            selSprites = new int[selW * selH];
            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
            {
                int gx = selX + xx, gy = selY + yy; int gidx = gy * mapWidth + gx;
                if (selectionSet.Contains(gidx))
                {
                    // Only capture active layers
                    selTiles[yy * selW + xx] = tilesLayerActive ? tiles[gidx] : -1;
                    selSprites[yy * selW + xx] = spritesLayerActive ? sprites[gidx] : -1;
                }
                else
                {
                    selTiles[yy * selW + xx] = -1;
                    selSprites[yy * selW + xx] = -1;
                }
            }

            // Update selection visuals
            UpdateSelectionVisuals(selX, selY, selW, selH);
            if (StatusText != null) StatusText.Text = $"Selected items: {selectionSet.Count} (bbox {selW}x{selH} at {selX},{selY})";
        }

        private void StartMagicWandAt(Point pos)
        {
            if (CanvasHost == null) return;
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)((pos.X - pad) / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)((pos.Y - pad) / (TileSize * scale))));
            int startIdx = y * mapWidth + x;
            
            // Use the appropriate layer based on which is active
            // Priority: if only one layer active, use that; if both active, prefer tiles
            int[] layerData = tilesLayerActive ? tiles : sprites;
            int target = layerData[startIdx];
            if (target == -1) return; // nothing to select

            var q = new System.Collections.Generic.Queue<(int x, int y)>();
            var visited = new System.Collections.Generic.HashSet<int>();
            q.Enqueue((x, y)); visited.Add(startIdx);
            while (q.Count > 0)
            {
                var (cx, cy) = q.Dequeue();
                int ci = cy * mapWidth + cx;
                // four neighbors
                var nbrs = new (int nx, int ny)[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) };
                foreach (var n in nbrs)
                {
                    int nx = n.nx, ny = n.ny;
                    if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight) continue;
                    int ni = ny * mapWidth + nx;
                    if (visited.Contains(ni)) continue;
                    if (layerData[ni] == target)
                    {
                        visited.Add(ni);
                        q.Enqueue((nx, ny));
                    }
                }
            }

            // Check if Ctrl is held for additive selection
            bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            
            if (!isCtrl)
            {
                // Replace selection
                selectionSet.Clear();
            }
            
            // Add visited tiles to selection set
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var i in visited)
            {
                selectionSet.Add(i);
            }
            
            // Compute bounding box of entire selectionSet
            foreach (var i in selectionSet)
            {
                int sx = i % mapWidth; int sy = i / mapWidth;
                if (sx < minX) minX = sx; if (sy < minY) minY = sy;
                if (sx > maxX) maxX = sx; if (sy > maxY) maxY = sy;
            }
            
            selX = minX; selY = minY; selW = maxX - minX + 1; selH = maxY - minY + 1;
            selTiles = new int[selW * selH];
            selSprites = new int[selW * selH];
            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
            {
                int gx = selX + xx, gy = selY + yy; int gidx = gy * mapWidth + gx;
                // Only capture active layers
                if (selectionSet.Contains(gidx))
                {
                    selTiles[yy * selW + xx] = tilesLayerActive ? tiles[gidx] : -1;
                    selSprites[yy * selW + xx] = spritesLayerActive ? sprites[gidx] : -1;
                }
                else
                {
                    selTiles[yy * selW + xx] = -1;
                    selSprites[yy * selW + xx] = -1;
                }
            }

            // Update selection visuals
            UpdateSelectionVisuals(selX, selY, selW, selH);
            string mode = isCtrl ? "added" : "selected";
            if (StatusText != null) StatusText.Text = $"Magic wand {mode} {visited.Count} tiles of type {target} (total: {selectionSet.Count})";
        }

        private void StartDragMove(Point pos)
        {
            if (selTiles == null || selW <= 0 || selH <= 0) return;
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            var dpi = VisualTreeHelper.GetDpi(this);
            int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));

            // Build an image for the selection (render scaled tiles and sprites into a RenderTargetBitmap)
            int pixW = selW * tilePixelW;
            int pixH = selH * tilePixelH;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Draw tiles first
                for (int yy = 0; yy < selH; yy++)
                {
                    for (int xx = 0; xx < selW; xx++)
                    {
                        int val = selTiles[yy * selW + xx];
                        if (val >= 0 && tileImages != null && val < tileImages.Length)
                        {
                            ImageSource? src = null;
                            try { if (tileTonedImages != null && tileTonedImages.Length == tileImages.Length) src = tileTonedImages[val]; } catch { src = null; }
                            if (src == null) src = tileImages[val];
                            if (src != null)
                            {
                                double x = xx * TileSize * scale;
                                double y = yy * TileSize * scale;
                                dc.DrawImage(src, new Rect(x, y, TileSize * scale, TileSize * scale));
                            }
                        }
                    }
                }
                // Draw sprites on top
                if (selSprites != null)
                {
                    for (int yy = 0; yy < selH; yy++)
                    {
                        for (int xx = 0; xx < selW; xx++)
                        {
                            int val = selSprites[yy * selW + xx];
                            if (val >= 0 && spriteImages != null && val < spriteImages.Length)
                            {
                                var src = spriteImages[val];
                                if (src != null)
                                {
                                    double x = xx * TileSize * scale;
                                    double y = yy * TileSize * scale;
                                    dc.DrawImage(src, new Rect(x, y, TileSize * scale, TileSize * scale));
                                }
                            }
                        }
                    }
                }
            }
            var rtb = new RenderTargetBitmap(pixW, pixH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();

            // Set ghost image source and initial position
            if (GhostImage != null && CanvasHost != null)
            {
                GhostImage.Source = rtb;
                GhostImage.Width = selW * TileSize * scale;
                GhostImage.Height = selH * TileSize * scale;
                double selLeft = selX * TileSize * scale + mapViewportPadding;
                double selTop = selY * TileSize * scale + mapViewportPadding;
                Canvas.SetLeft(GhostImage, selLeft);
                Canvas.SetTop(GhostImage, selTop);
                GhostImage.Visibility = Visibility.Visible;
            }

            // capture drag state
            isDraggingSelection = true;
            dragStartMouse = pos;
            dragOrigX = selX; dragOrigY = selY;
            // compute offset so the ghost follows the pointer at the same relative point
            double selLeftUnits = selX * TileSize * scale + mapViewportPadding;
            double selTopUnits = selY * TileSize * scale + mapViewportPadding;
            dragOffset = new Point(dragStartMouse.X - selLeftUnits, dragStartMouse.Y - selTopUnits);
            if (CanvasHost != null) CanvasHost.CaptureMouse();
        }

        private void UpdateDragMoveTo(Point pos)
        {
            if (!isDraggingSelection || GhostImage == null) return;
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            // desired top-left in canvas units
            double left = pos.X - dragOffset.X;
            double top = pos.Y - dragOffset.Y;
            // clamp so selection stays within map extents (allow small padding)
            double minLeft = pad; double minTop = pad;
            double maxLeft = pad + Math.Max(0, mapWidth * TileSize * scale - selW * TileSize * scale);
            double maxTop = pad + Math.Max(0, mapHeight * TileSize * scale - selH * TileSize * scale);
            if (left < minLeft) left = minLeft; if (left > maxLeft) left = maxLeft;
            if (top < minTop) top = minTop; if (top > maxTop) top = maxTop;
            Canvas.SetLeft(GhostImage, left);
            Canvas.SetTop(GhostImage, top);
        }

        private void EndDragMove(Point pos)
        {
            if (!isDraggingSelection) return;
            isDraggingSelection = false;
            if (CanvasHost != null && CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
            if (GhostImage == null) return;
            
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            
            // If this was just a click (not a drag) outside the current selection, deselect
            if (!hasMouseMoved && selectionSet.Count > 0)
            {
                int clickX = Math.Max(0, Math.Min(mapWidth - 1, (int)((pos.X - pad) / (TileSize * scale))));
                int clickY = Math.Max(0, Math.Min(mapHeight - 1, (int)((pos.Y - pad) / (TileSize * scale))));
                int clickedIdx = clickY * mapWidth + clickX;
                if (!selectionSet.Contains(clickedIdx))
                {
                    // Hide ghost and clear selection
                    GhostImage.Visibility = Visibility.Collapsed;
                    GhostImage.Source = null;
                    ClearSelection();
                    return;
                }
            }
            
            // compute final destination tile coords from ghost position
            double left = Canvas.GetLeft(GhostImage);
            double top = Canvas.GetTop(GhostImage);
            int destX = (int)Math.Round((left - pad) / (TileSize * scale));
            int destY = (int)Math.Round((top - pad) / (TileSize * scale));
            // clamp
            if (destX < 0) destX = 0; if (destY < 0) destY = 0;
            if (destX + selW > mapWidth) destX = mapWidth - selW;
            if (destY + selH > mapHeight) destY = mapHeight - selH;

            // hide ghost
            GhostImage.Visibility = Visibility.Collapsed;
            GhostImage.Source = null;

            // commit move
            MoveSelectionTo(destX, destY);
        }

        private void UpdateSelectionTo(Point pos)
        {
            if (!isSelecting) return;
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            double relX = pos.X - pad;
            double relY = pos.Y - pad;
            int x = Math.Max(0, Math.Min(mapWidth - 1, (int)(relX / (TileSize * scale))));
            int y = Math.Max(0, Math.Min(mapHeight - 1, (int)(relY / (TileSize * scale))));
            int minX = Math.Min(selectStartX, x), minY = Math.Min(selectStartY, y);
            int maxX = Math.Max(selectStartX, x), maxY = Math.Max(selectStartY, y);
            // During drag-selection, show a temporary preview bounding box
            UpdateSelectionVisuals(minX, minY, maxX - minX + 1, maxY - minY + 1, previewMode: true);
        }

        private void UpdateSelectionVisuals(int x, int y, int w, int h, bool previewMode = false)
        {
            if (SelectionOverlay == null) return;
            SelectionOverlay.Children.Clear();
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            double tileSize = TileSize * scale;

            if (previewMode)
            {
                // During drag-selection, show a simple bounding rectangle preview
                var rect = new Shapes.Rectangle
                {
                    Fill = SelectionFillBrush,
                    Stroke = SelectionStrokeBrush,
                    StrokeThickness = 2,
                    Width = w * tileSize,
                    Height = h * tileSize,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, x * tileSize + pad);
                Canvas.SetTop(rect, y * tileSize + pad);
                SelectionOverlay.Children.Add(rect);
            }
            else
            {
                // Show individual rectangles for each selected tile
                foreach (var idx in selectionSet)
                {
                    int tx = idx % mapWidth;
                    int ty = idx / mapWidth;
                    var rect = new Shapes.Rectangle
                    {
                        Fill = SelectionFillBrush,
                        Stroke = SelectionStrokeBrush,
                        StrokeThickness = 2,
                        Width = tileSize,
                        Height = tileSize,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(rect, tx * tileSize + pad);
                    Canvas.SetTop(rect, ty * tileSize + pad);
                    SelectionOverlay.Children.Add(rect);
                }
            }
        }

        private void EndSelection()
        {
            if (!isSelecting) return;
            isSelecting = false;
            if (CanvasHost != null && CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
            
            // If this was just a click (not a drag) outside the current selection, deselect
            if (!hasMouseMoved && selectionSet.Count > 0)
            {
                int clickedIdx = selectStartY * mapWidth + selectStartX;
                if (!selectionSet.Contains(clickedIdx))
                {
                    ClearSelection();
                    return;
                }
            }
            
            // compute final selection bounds from the drag preview
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            // Use the current preview to determine bounds
            if (SelectionOverlay == null || SelectionOverlay.Children.Count == 0) return;
            var previewRect = SelectionOverlay.Children[0] as Shapes.Rectangle;
            if (previewRect == null) return;
            double pad = mapViewportPadding;
            double left = Canvas.GetLeft(previewRect) - pad;
            double top = Canvas.GetTop(previewRect) - pad;
            int minX = Math.Max(0, Math.Min(mapWidth - 1, (int)(left / (TileSize * scale))));
            int minY = Math.Max(0, Math.Min(mapHeight - 1, (int)(top / (TileSize * scale))));
            int w = Math.Max(1, (int)Math.Round(previewRect.Width / (TileSize * scale)));
            int h = Math.Max(1, (int)Math.Round(previewRect.Height / (TileSize * scale)));
            // clamp to map
            if (minX + w > mapWidth) w = mapWidth - minX;
            if (minY + h > mapHeight) h = mapHeight - minY;
            selX = minX; selY = minY; selW = w; selH = h;
            // copy tiles/sprites and populate selection set
            selTiles = new int[selW * selH];
            selSprites = new int[selW * selH];
            selectionSet.Clear();
            for (int yy = 0; yy < selH; yy++)
            {
                for (int xx = 0; xx < selW; xx++)
                {
                    int idx = (selY + yy) * mapWidth + (selX + xx);
                    // Only capture layers that are currently active
                    selTiles[yy * selW + xx] = tilesLayerActive ? tiles[idx] : -1;
                    selSprites[yy * selW + xx] = spritesLayerActive ? sprites[idx] : -1;
                    // Add to selection set if either active layer is non-empty
                    bool hasTile = tilesLayerActive && tiles[idx] != -1;
                    bool hasSprite = spritesLayerActive && sprites[idx] != -1;
                    if (hasTile || hasSprite) selectionSet.Add(idx);
                }
            }
            // If the rectangular selection contains no valid tiles/sprites, clear selection
            if (selectionSet.Count == 0)
            {
                ClearSelection();
                return;
            }
            UpdateSelectionVisuals(selX, selY, selW, selH);
            if (StatusText != null) StatusText.Text = $"Selected area {selW}x{selH} at {selX},{selY} (items={selectionSet.Count})";
        }

        private void ClearSelection()
        {
            selTiles = null; selSprites = null; selW = 0; selH = 0; selX = selY = -1;
            selectionSet.Clear();
            if (SelectionOverlay != null) SelectionOverlay.Children.Clear();
            if (StatusText != null) StatusText.Text = string.Empty;
        }

        private void MoveSelectionTo(int destX, int destY)
        {
            if (selTiles == null || selW <= 0 || selH <= 0) return;
            // clamp destination so selection fits
            if (destX < 0) destX = 0; if (destY < 0) destY = 0;
            if (destX + selW > mapWidth) destX = mapWidth - selW;
            if (destY + selH > mapHeight) destY = mapHeight - selH;

            // Check if selTiles has any non-empty values
            bool hasAnyTiles = false;
            for (int i = 0; i < selTiles.Length; i++)
            {
                if (selTiles[i] != -1) { hasAnyTiles = true; break; }
            }

            // Prepare final values map and record changes compared to current tiles
            if (hasAnyTiles)
            {
                var finalTiles = (int[])tiles.Clone();
                var changedTiles = new TileChangeAction();

                // Apply selection tile values to final at destination
                for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
                {
                    int val = selTiles[yy * selW + xx];
                    int dIdx = (destY + yy) * mapWidth + (destX + xx);
                    finalTiles[dIdx] = val;
                }

                // Clear original source tile cells unless they are also targets for the selection (i.e., overlapping move)
                var targetSet = new HashSet<int>();
                for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++) targetSet.Add((destY + yy) * mapWidth + (destX + xx));
                for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
                {
                    int sIdx = (selY + yy) * mapWidth + (selX + xx);
                    if (!targetSet.Contains(sIdx)) finalTiles[sIdx] = -1;
                }

                // Build TileChangeAction from differences
                for (int i = 0; i < finalTiles.Length; i++)
                {
                    if (finalTiles[i] != tiles[i]) changedTiles.Add(i, tiles[i], finalTiles[i]);
                }

                if (!changedTiles.IsEmpty() && !suppressUndoRecording)
                {
                    undoStack.Push(changedTiles);
                    redoStack.Clear();
                    hasUnsavedChanges = true;
                }

                // Commit final tile state
                tiles = finalTiles;
                try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
            }
            
            // Check if selSprites has any non-empty values
            bool hasAnySprites = false;
            if (selSprites != null)
            {
                for (int i = 0; i < selSprites.Length; i++)
                {
                    if (selSprites[i] != -1) { hasAnySprites = true; break; }
                }
            }
            
            // Handle sprites if selSprites exists and has content
            if (hasAnySprites && selSprites != null)
            {
                var finalSprites = (int[])sprites.Clone();
                var changedSprites = new SpriteChangeAction();
                
                // Apply selection sprite values to final at destination
                for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
                {
                    int val = selSprites[yy * selW + xx];
                    int dIdx = (destY + yy) * mapWidth + (destX + xx);
                    finalSprites[dIdx] = val;
                }

                // Clear original source sprite cells unless they are also targets
                var targetSet = new HashSet<int>();
                for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++) targetSet.Add((destY + yy) * mapWidth + (destX + xx));
                for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
                {
                    int sIdx = (selY + yy) * mapWidth + (selX + xx);
                    if (!targetSet.Contains(sIdx)) finalSprites[sIdx] = -1;
                }

                // Build SpriteChangeAction from differences
                for (int i = 0; i < finalSprites.Length; i++)
                {
                    if (finalSprites[i] != sprites[i]) changedSprites.Add(i, sprites[i], finalSprites[i]);
                }

                if (!changedSprites.IsEmpty() && !suppressUndoRecording)
                {
                    undoStack.Push(changedSprites);
                    redoStack.Clear();
                    hasUnsavedChanges = true;
                }

                // Commit final sprite state
                sprites = finalSprites;
                try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
            }
            
            // Update selection to new destination: preserve sparse selection membership only for cells that were selected
            var newSelection = new System.Collections.Generic.HashSet<int>();
            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
            {
                int oldIdx = (selY + yy) * mapWidth + (selX + xx);
                int newIdx = (destY + yy) * mapWidth + (destX + xx);
                // if the source cell was part of selectionSet (i.e., non-empty before move), include its destination
                if (/* check source was selected */ selectionSet.Contains(oldIdx)) newSelection.Add(newIdx);
            }
            selectionSet = newSelection;
            // update selX/selY to the new top-left
            selX = destX; selY = destY;
            // Update selection visuals to show the moved selection
            UpdateSelectionVisuals(selX, selY, selW, selH);
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
            // push composite actions to undo stack
            try
            {
                if (!suppressUndoRecording && currentCompositeAction != null && !currentCompositeAction.IsEmpty())
                {
                    undoStack.Push(currentCompositeAction);
                    redoStack.Clear();
                    hasUnsavedChanges = true;
                }
                if (!suppressUndoRecording && currentCompositeSpriteAction != null && !currentCompositeSpriteAction.IsEmpty())
                {
                    undoStack.Push(currentCompositeSpriteAction);
                    redoStack.Clear();
                    hasUnsavedChanges = true;
                }
            }
            finally 
            { 
                currentCompositeAction = null;
                currentCompositeSpriteAction = null;
            }
        }

        private void DoPaintAt(int x, int y)
        {
            if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight) return;
            
            // Place tool: respect active layers
            if (PlaceTool != null && PlaceTool.IsChecked == true)
            {
                bool changed = false;
                
                // Handle multi-tile placement
                if (tilesLayerActive && selectedTiles.Count > 0)
                {
                    for (int dy = 0; dy < selectionHeight; dy++)
                    {
                        for (int dx = 0; dx < selectionWidth; dx++)
                        {
                            int targetX = x + dx;
                            int targetY = y + dy;
                            
                            if (targetX >= 0 && targetX < mapWidth && targetY >= 0 && targetY < mapHeight)
                            {
                                int idx = targetY * mapWidth + targetX;
                                int selIdx = dy * selectionWidth + dx;
                                
                                if (selIdx < selectedTiles.Count)
                                {
                                    int old = tiles[idx];
                                    int neu = selectedTiles[selIdx];
                                    
                                    if (old != neu)
                                    {
                                        if (!suppressUndoRecording)
                                        {
                                            if (currentCompositeAction == null) currentCompositeAction = new TileChangeAction();
                                            currentCompositeAction.Add(idx, old, neu);
                                        }
                                        tiles[idx] = neu;
                                        changed = true;
                                    }
                                }
                            }
                        }
                    }
                }
                
                // Place sprite if sprites layer is active and a sprite is selected
                if (spritesLayerActive && selectedSprite >= 0)
                {
                    int idx = y * mapWidth + x;
                    int old = sprites[idx];
                    int neu = selectedSprite;
                    if (old != neu)
                    {
                        if (!suppressUndoRecording)
                        {
                            if (currentCompositeSpriteAction == null) currentCompositeSpriteAction = new SpriteChangeAction();
                            currentCompositeSpriteAction.Add(idx, old, neu);
                        }
                        sprites[idx] = neu;
                        changed = true;
                    }
                }
                
                if (changed)
                {
                    lastPaintX = x; lastPaintY = y;
                    try 
                    { 
                        if (spritesLayerActive && selectedSprite >= 0)
                        {
                            RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
                        }
                        if (tilesLayerActive && selectedTiles.Count > 0)
                        {
                            // Update all affected tiles
                            for (int dy = 0; dy < selectionHeight; dy++)
                            {
                                for (int dx = 0; dx < selectionWidth; dx++)
                                {
                                    int targetX = x + dx;
                                    int targetY = y + dy;
                                    if (targetX >= 0 && targetX < mapWidth && targetY >= 0 && targetY < mapHeight)
                                    {
                                        UpdateTileBitmapAt(targetX, targetY, (ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
                                    }
                                }
                            }
                        }
                    } 
                    catch { Redraw(); }
                }
                return;
            }
            
            // Erase tool: erase from active layers only
            if (EraseTool != null && EraseTool.IsChecked == true)
            {
                bool changed = false;
                
                if (tilesLayerActive)
                {
                    // Erase multi-tile pattern if multiple tiles are selected
                    for (int dy = 0; dy < selectionHeight; dy++)
                    {
                        for (int dx = 0; dx < selectionWidth; dx++)
                        {
                            int targetX = x + dx;
                            int targetY = y + dy;
                            
                            if (targetX >= 0 && targetX < mapWidth && targetY >= 0 && targetY < mapHeight)
                            {
                                int idx = targetY * mapWidth + targetX;
                                int oldTile = tiles[idx];
                                if (oldTile != -1)
                                {
                                    if (!suppressUndoRecording)
                                    {
                                        if (currentCompositeAction == null) currentCompositeAction = new TileChangeAction();
                                        currentCompositeAction.Add(idx, oldTile, -1);
                                    }
                                    tiles[idx] = -1;
                                    changed = true;
                                }
                            }
                        }
                    }
                }
                
                if (spritesLayerActive)
                {
                    int idx = y * mapWidth + x;
                    int oldSprite = sprites[idx];
                    if (oldSprite != -1)
                    {
                        if (!suppressUndoRecording)
                        {
                            if (currentCompositeSpriteAction == null) currentCompositeSpriteAction = new SpriteChangeAction();
                            currentCompositeSpriteAction.Add(idx, oldSprite, -1);
                        }
                        sprites[idx] = -1;
                        changed = true;
                    }
                }
                
                if (changed)
                {
                    lastPaintX = x; lastPaintY = y;
                    try { 
                        if (tilesLayerActive)
                        {
                            // Update all affected tiles
                            for (int dy = 0; dy < selectionHeight; dy++)
                            {
                                for (int dx = 0; dx < selectionWidth; dx++)
                                {
                                    int targetX = x + dx;
                                    int targetY = y + dy;
                                    if (targetX >= 0 && targetX < mapWidth && targetY >= 0 && targetY < mapHeight)
                                    {
                                        UpdateTileBitmapAt(targetX, targetY, (ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
                                    }
                                }
                            }
                        }
                        if (spritesLayerActive)
                            RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
                    } catch { Redraw(); }
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
            
            // Only update if position changed
            if (x == lastHoverX && y == lastHoverY && inBounds == (lastHoverX != -1)) return;
            lastHoverX = x;
            lastHoverY = y;
            
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

        private void MapScrollViewer_ManipulationDelta(object? sender, ManipulationDeltaEventArgs e)
        {
            // Handle pinch zoom gestures
            if (ZoomSlider == null) return;
            
            // Get the scale change from the pinch gesture
            double scaleChange = e.DeltaManipulation.Scale.X; // or Y, they should be the same for uniform scaling
            
            if (scaleChange != 1.0)
            {
                // Calculate new zoom value
                double currentZoom = ZoomSlider.Value;
                double newZoom = currentZoom * scaleChange;
                
                // Clamp to slider min/max
                if (newZoom < ZoomSlider.Minimum) newZoom = ZoomSlider.Minimum;
                if (newZoom > ZoomSlider.Maximum) newZoom = ZoomSlider.Maximum;
                
                ZoomSlider.Value = newZoom;
                e.Handled = true;
            }
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
            
            // Arrow key scrolling - check all held keys to allow diagonal movement
            if (MapScrollViewer == null) return;
            
            bool isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            double scrollAmount = isShiftPressed ? 4 : 32; // 4px with Shift, 32px normally
            
            bool handled = false;
            
            if (Keyboard.IsKeyDown(Key.Left))
            {
                MapScrollViewer.ScrollToHorizontalOffset(MapScrollViewer.HorizontalOffset - scrollAmount);
                handled = true;
            }
            if (Keyboard.IsKeyDown(Key.Right))
            {
                MapScrollViewer.ScrollToHorizontalOffset(MapScrollViewer.HorizontalOffset + scrollAmount);
                handled = true;
            }
            if (Keyboard.IsKeyDown(Key.Up))
            {
                MapScrollViewer.ScrollToVerticalOffset(MapScrollViewer.VerticalOffset - scrollAmount);
                handled = true;
            }
            if (Keyboard.IsKeyDown(Key.Down))
            {
                MapScrollViewer.ScrollToVerticalOffset(MapScrollViewer.VerticalOffset + scrollAmount);
                handled = true;
            }
            
            if (handled)
                e.Handled = true;
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
            try 
            { 
                RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
                RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
            } 
            catch { Redraw(); }
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
            try 
            { 
                RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
                RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
            } 
            catch { Redraw(); }
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
            var dlg = new SaveFileDialog { Filter = "Tiled Map (TMX)|*.tmx|JSON level|*.json|All files|*.*", DefaultExt = "tmx" };
            
            // Use current file path if we have one
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                dlg.FileName = currentFilePath;
            }
            
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    string ext = Path.GetExtension(dlg.FileName).ToLower();
                    
                    if (ext == ".tmx")
                    {
                        // Determine export target - use loaded value or generate default from filename
                        string? exportTarget = loadedExportTarget;
                        if (string.IsNullOrEmpty(exportTarget))
                        {
                            // Default: use same filename as TMX but with .csv extension
                            exportTarget = Path.ChangeExtension(Path.GetFileName(dlg.FileName), ".csv");
                        }
                        
                        // Determine chunk height - use loaded value or default to map height
                        int chunkHeight = loadedHasEditorSettings ? loadedChunkHeight : mapHeight;
                        
                        // Save as TMX format with separate tiles and sprites
                        var tmxLevel = new TmxLevel
                        {
                            Width = mapWidth,
                            Height = mapHeight,
                            Tiles = tiles,
                            Sprites = sprites,
                            TilesetSource = loadedTilesetSource ?? "../../../GRAPHICS/famidash.bmp",
                            SpritesetSource = loadedSpritesetSource ?? "../../../GRAPHICS/sprites.png",
                            HasEditorSettings = true, // Always include editor settings
                            ChunkWidth = loadedChunkWidth,
                            ChunkHeight = chunkHeight,
                            ExportTarget = exportTarget,
                            ExportFormat = loadedExportFormat,
                            ParallaxSource = loadedParallaxSource,
                            ParallaxX = loadedParallaxX,
                            ParallaxY = loadedParallaxY,
                            ParallaxRepeatX = loadedParallaxRepeatX,
                            ParallaxRepeatY = loadedParallaxRepeatY,
                            HasParallaxLayer = loadedHasParallaxLayer,
                            GroundSource = loadedGroundSource,
                            GroundOffsetY = loadedGroundOffsetY,
                            GroundRepeatX = loadedGroundRepeatX,
                            HasGroundLayer = loadedHasGroundLayer
                        };
                        TmxHandler.SaveTmx(dlg.FileName, tmxLevel);
                    }
                    else
                    {
                        // Save as JSON format
                        var model = new LevelModel { Width = mapWidth, Height = mapHeight, Tiles = tiles };
                        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(model));
                    }
                    
                    currentFilePath = dlg.FileName;
                    hasUnsavedChanges = false;
                    if (StatusText != null) StatusText.Text = "Saved " + dlg.FileName;
                }
                catch (Exception ex)
                {
                    if (StatusText != null) StatusText.Text = "Save failed: " + ex.Message;
                }
            }
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            // Prompt to save if there are unsaved changes
            if (hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before loading?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    SaveButton_Click(sender, e);
                    // If user cancelled the save dialog, abort the load
                    if (hasUnsavedChanges) return;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return; // User cancelled the load operation
                }
                // If No, continue with load without saving
            }
            
            var dlg = new OpenFileDialog { Filter = "Tiled Map (TMX)|*.tmx|JSON level|*.json|All files|*.*" };
            if (dlg.ShowDialog(this) == true)
            {
                LoadingWindow? loadingWindow = null;
                try
                {
                    string ext = Path.GetExtension(dlg.FileName).ToLower();
                    int loadedWidth = 0;
                    int loadedHeight = 0;
                    int[]? loadedTiles = null;
                    int[]? loadedSprites = null;
                    
                    if (ext == ".tmx")
                    {
                        // Show loading dialog
                        loadingWindow = new LoadingWindow { Owner = this };
                        loadingWindow.SetMessage("Loading TMX file...\nThis may take a while on larger maps.");
                        loadingWindow.Show();
                        
                        // Force UI update
                        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        
                        // Load TMX format
                        var tmxLevel = TmxHandler.LoadTmx(dlg.FileName);
                        loadedWidth = tmxLevel.Width;
                        loadedHeight = tmxLevel.Height;
                        loadedTiles = tmxLevel.Tiles;
                        loadedSprites = tmxLevel.Sprites;
                        
                        // Store TMX metadata to preserve when saving
                        loadedTilesetSource = tmxLevel.TilesetSource;
                        loadedSpritesetSource = tmxLevel.SpritesetSource;
                        loadedHasEditorSettings = tmxLevel.HasEditorSettings;
                        loadedChunkWidth = tmxLevel.ChunkWidth;
                        loadedChunkHeight = tmxLevel.ChunkHeight;
                        loadedExportTarget = tmxLevel.ExportTarget;
                        loadedExportFormat = tmxLevel.ExportFormat;
                        loadedParallaxSource = tmxLevel.ParallaxSource;
                        loadedParallaxX = tmxLevel.ParallaxX;
                        loadedParallaxY = tmxLevel.ParallaxY;
                        loadedParallaxRepeatX = tmxLevel.ParallaxRepeatX;
                        loadedParallaxRepeatY = tmxLevel.ParallaxRepeatY;
                        loadedHasParallaxLayer = tmxLevel.HasParallaxLayer;
                        loadedGroundSource = tmxLevel.GroundSource;
                        loadedGroundOffsetY = tmxLevel.GroundOffsetY;
                        loadedGroundRepeatX = tmxLevel.GroundRepeatX;
                        loadedHasGroundLayer = tmxLevel.HasGroundLayer;
                        
                        // Note: Parallax and ground image sources are loaded but not automatically applied
                        // You may want to add logic here to load the actual images if needed
                    }
                    else
                    {
                        // Load JSON format
                        var json = File.ReadAllText(dlg.FileName);
                        var model = JsonSerializer.Deserialize<LevelModel>(json);
                        if (model != null)
                        {
                            loadedWidth = model.Width;
                            loadedHeight = model.Height;
                            loadedTiles = model.Tiles;
                        }
                    }
                    
                    if (loadedWidth > 0 && loadedHeight > 0 && loadedTiles != null)
                    {
                        // Directly set the data without going through ResizeMap to avoid undo recording
                        suppressUndoRecording = true;
                        mapWidth = loadedWidth;
                        mapHeight = loadedHeight;
                        tiles = loadedTiles;
                        sprites = loadedSprites ?? Enumerable.Repeat(-1, loadedWidth * loadedHeight).ToArray();
                        
                        if (WidthBox != null) WidthBox.Text = mapWidth.ToString();
                        if (HeightBox != null) HeightBox.Text = mapHeight.ToString();
                        
                        // Clear selection
                        ClearSelection();
                        
                        // Clear undo/redo stacks when loading a new file
                        undoStack.Clear();
                        redoStack.Clear();
                        
                        suppressUndoRecording = false;
                        
                        // Update current file and clear dirty flag
                        currentFilePath = dlg.FileName;
                        hasUnsavedChanges = false;
                        
                        // Force a full redraw with the new dimensions
                        Redraw();
                        
                        // Render all loaded tiles and sprites to the bitmaps
                        double currentScale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
                        RebuildAllTilesBitmap(currentScale, mapViewportPadding);
                        RebuildAllSpritesBitmap(currentScale, mapViewportPadding);
                        
                        // Snap to ground level (bottom) and left side
                        if (MapScrollViewer != null)
                        {
                            MapScrollViewer.UpdateLayout(); // Ensure layout is updated
                            MapScrollViewer.ScrollToLeftEnd();
                            MapScrollViewer.ScrollToBottom();
                        }
                        
                        if (StatusText != null) StatusText.Text = $"Loaded {Path.GetFileName(dlg.FileName)} ({mapWidth}x{mapHeight})";
                    }
                    else
                    {
                        if (StatusText != null) StatusText.Text = "Load failed: Invalid or empty map data";
                    }
                }
                catch (Exception ex)
                {
                    if (StatusText != null) StatusText.Text = "Load failed: " + ex.Message;
                }
                finally
                {
                    // Close loading window
                    if (loadingWindow != null)
                    {
                        loadingWindow.Close();
                    }
                }
            }
        }
    }
}

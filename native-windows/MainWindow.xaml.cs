using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;
using System.Windows.Interop;

namespace FamidashEditor
{
    public partial class MainWindow : Window
    {
        private FamiStudioIntegration famiIntegration = new FamiStudioIntegration();
        private string? famiStudioPath = null;
        // Set to true when a precomputed name->index mapping is loaded from disk so
        // we prefer that mapping over any in-process remapping at play-time.
        private bool mappingLoadedFromFile = false;
        private string? albumTxtPath = null;
        private enum DrawMode { Tile, Line, Square, Circle, Triangle, Polygon, None }
        private DrawMode currentDrawMode = DrawMode.Tile;
        private bool hollowShape = false;
        private int brushThickness = 1;

        // Deferred draw state
        private bool isDeferredDrawing = false;
        private int drawStartX = -1, drawStartY = -1;
        private int drawCurrentX = -1, drawCurrentY = -1;
        // Polygon construction state
        private System.Collections.Generic.List<(int x, int y)> polygonPoints = new System.Collections.Generic.List<(int x, int y)>();
        private bool isConstructingPolygon = false;
        private int lastKnownSelectedTile = -2;
        private int lastKnownSelectedSprite = -2;
        private System.DateTime lastInputAction = System.DateTime.MinValue;
    // Timer used to perform continuous 1-px fine scrolling while Shift+Left/Right are held
    private System.Windows.Threading.DispatcherTimer? shiftArrowScrollTimer = null;
    private int shiftArrowScrollDir = 0; // -1 = left, +1 = right
    private bool initialLeftSizingDone = false;
    private bool suppressManualTileChange = false;
    private bool suppressManualSpriteChange = false;
    private const int TileSize = 16;
    private bool isAdjustingPanels = false;
    private int mapWidth = 200;
    private int mapHeight = 27;
    private int[] tiles = Array.Empty<int>();
    private int[] sprites = Array.Empty<int>(); // separate layer for sprites
    // Legacy trigger offset option (default off)
    private bool useLegacyTriggerOffset = false;
    // Preview option: hide color triggers in preview mode
    private bool hideColorTriggers = false;
    // Preview option: hide all invisible sprites (user-configurable global setting)
    private bool hideInvisibleSprites = false;
    // Per-level option: replace parallax background with noparallax.bmp when true
    private bool noParallaxBg = false;
    private bool suppressNoParallaxHandler = false;
    private bool swapMouseWheelScroll = false; // when true, swap shift/no-modifier wheel scroll behavior
    private bool invertPinchGesture = true; // if true, invert pinch scale (device-dependent)
    private bool pinchDirectionDetected = false;
    private double lastManipulationCumulativeScale = 1.0;
    private bool manipulationActive = false;
    // NOTE: 'MenuOptionHideInvisibleSprites' is declared in XAML (x:Name) and initialized by InitializeComponent.
    // default grid darkness: much lighter so grid lines are subtle over dark backgrounds
    private double gridDarkness = 0.18;
    private Brush mapBackground = new SolidColorBrush(Color.FromRgb(59,59,59));
    // tint overlays (RGBA) applied over the background and ground images
    private Color backgroundTint = Color.FromArgb(0, 0, 0, 0);
    private Color groundTint = Color.FromArgb(0, 0, 0, 0);
    private Color tileTint = Color.FromArgb(0, 0, 0, 0);
    // Player tint (applied to decoration pixels that are not black/transparent)
    private Color playerTint = Color.FromArgb(0, 0, 0, 0);
    // Whether player tinting is enabled (user-selected). Default: disabled.
    private bool playerTintEnabled = false;
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
    // Copy/Cut/Paste clipboard (rectangular with optional mask for sparse selections)
    private int[]? clipboardTiles = null;
    private int[]? clipboardSprites = null;
    private bool[]? clipboardMask = null; // true == cell was part of original selection
    private int clipboardW = 0;
    private int clipboardH = 0;
    private bool clipboardHasData = false;
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
    private string loadedDecoSet = "DECO1";
    private string loadedBlockSet = "BLOCKSA";
    private string loadedSpikeSet = "SPIKESA";
    private int paletteTileSize = 16;
    private int paletteSpriteSize = 16;
    // Painting state for drag-to-draw
    private bool isPainting = false;
    private int lastPaintX = -1;
    private int lastPaintY = -1;
    // Track if mouse has moved since button down to distinguish click from drag
    private bool hasMouseMoved = false;
    private Point mouseDownPosition;
    // Middle-click panning state
    private bool isMiddlePanning = false;
    private Point middlePanStart;
    private double panStartHOffset = 0.0;
    private double panStartVOffset = 0.0;
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
    
    // Configuration file support for per-TMX settings
    private class TmxConfig
    {
        // Tint components are nullable so they are only written when explicitly set.
        public byte? BackgroundTintR { get; set; }
        public byte? BackgroundTintG { get; set; }
        public byte? BackgroundTintB { get; set; }
        public byte? GroundTintR { get; set; }
        public byte? GroundTintG { get; set; }
        public byte? GroundTintB { get; set; }
        public byte? TileTintR { get; set; }
        public byte? TileTintG { get; set; }
        public byte? TileTintB { get; set; }
        public bool NoParallaxBg { get; set; } = false;
        public string? DecoSet { get; set; } = "DECO1";
        public string? BlockSet { get; set; } = "BLOCKSA";
        public string? SpikeSet { get; set; } = "SPIKESA";
        public bool LockSpritesToSet { get; set; } = false;
    }

    // When locking sprites to a deco set, this hash contains the sprite ids that should be disabled
    private HashSet<int> disabledSprites = new HashSet<int>();
    private bool lockSpritesToSet = false;
    public bool LockSpritesToSet => lockSpritesToSet;
    // When true, prefer external per-set tileset PNGs (if available) and swap famidash.bmp at runtime
    private bool showAccurateTileset = false;
    public bool ShowAccurateTileset => showAccurateTileset;

    // Public setter used by dialogs so behavior applies identically
        public void SetLockSpritesToSet(bool enabled, string? decoOverride = null)
    {
        try
        {
                lockSpritesToSet = enabled;
                // Persist as a global editor setting
                try { SaveSettingsWithTriggerOption(); } catch { }
                ApplyLockSpritesToSet(decoOverride);
        }
        catch { }
    }

        // Public setter used by dialogs to enable/disable the accurate tileset swapping feature.
        // When enabled, this will attempt to locate a matching PNG tileset for the current Block/Spike/NoParallax
        // combination and load it. When disabled, it reverts to the default embedded/fallback tileset.
        public void SetShowAccurateTileset(bool enabled, string? blockOverride = null, string? spikeOverride = null)
        {
            try
            {
                showAccurateTileset = enabled;
                // persist as global editor setting
                try { SaveSettingsWithTriggerOption(); } catch { }

                if (showAccurateTileset)
                {
                    // Determine which block/spike to use
                    var block = string.IsNullOrEmpty(blockOverride) ? loadedBlockSet : blockOverride;
                    var spike = string.IsNullOrEmpty(spikeOverride) ? loadedSpikeSet : spikeOverride;

                    // Helper: convert BLOCKSA -> Blocksa (Pascal-ish)
                    string ToPascal(string s)
                    {
                        if (string.IsNullOrEmpty(s)) return s ?? "";
                        var low = s.ToLowerInvariant();
                        return char.ToUpperInvariant(low[0]) + low.Substring(1);
                    }

                    // Build expected suffix according to pattern you described:
                    // {block}{spike}sawsa{noParallax?"slopesa":"slopesnone"}
                    var suffix = noParallaxBg ? "Slopesa" : "SlopesNone";
                    var pascalBlock = ToPascal(block ?? "");
                    var pascalSpike = ToPascal(spike ?? "");

                    var exactCandidates = new List<string>();
                    // Primary PascalCase form (e.g. BlocksaSpikesaSawsaSlopesa.png)
                    exactCandidates.Add($"{pascalBlock}{pascalSpike}Sawsa{suffix}.png");
                    exactCandidates.Add($"{pascalBlock}{pascalSpike}Sawsa{suffix}.PNG");
                    // Lowercase concatenated form (e.g. blocksaspikesasawsaslopesnone.png)
                    exactCandidates.Add($"{(block??"").ToLowerInvariant()}{(spike??"").ToLowerInvariant()}sawsaslopes{(noParallaxBg?"a":"none")}.png");
                    exactCandidates.Add($"{(block??"").ToLowerInvariant()}{(spike??"").ToLowerInvariant()}sawsaslopes{(noParallaxBg?"a":"none")}.PNG");
                    // Another lowercase variant without repeated 's' where some files may be named blocksaspikesasawsa{suffix}
                    exactCandidates.Add($"{(block??"").ToLowerInvariant()}{(spike??"").ToLowerInvariant()}sawsa{(noParallaxBg?"slopesa":"slopesnone")}.png");

                    // Candidate directories: prioritize explicit user folder, then app, repo
                    var candidates = new List<string>();
                    // explicit path the user mentioned
                    candidates.Add(Path.Combine(AppContext.BaseDirectory, "..")); // keep as general fallback
                    // prioritize a tilesets folder at workspace root (common user location)
                    candidates.Add(Path.Combine("C:\\Editor Test", "tilesets"));
                    candidates.Add(Path.Combine(AppContext.BaseDirectory, "tilesets"));
                    candidates.Add(AppContext.BaseDirectory);
                    candidates.Add(Environment.CurrentDirectory);
                    var repo = FindRepoRootFor("famidash.bmp");
                    if (!string.IsNullOrEmpty(repo))
                    {
                        candidates.Add(Path.Combine(repo, "tilesets"));
                        candidates.Add(repo);
                    }

                    string? found = null;
                    // Try exact candidates first (deterministic)
                    foreach (var dir in candidates)
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(dir)) continue;
                            if (!Directory.Exists(dir)) continue;
                            foreach (var fname in exactCandidates)
                            {
                                var p = Path.Combine(dir, fname);
                                if (File.Exists(p)) { found = p; break; }
                            }
                            if (!string.IsNullOrEmpty(found)) break;
                        }
                        catch { }
                    }

                    // If not found, use previous fuzzy search fallback (scan pngs for substrings)
                    if (string.IsNullOrEmpty(found))
                    {
                        foreach (var dir in candidates)
                        {
                            try
                            {
                                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                                var files = Directory.GetFiles(dir, "*.png");
                                int bestScore = 0;
                                string? bestFile = null;
                                foreach (var f in files)
                                {
                                    var name = Path.GetFileName(f).ToLowerInvariant();
                                    int score = 0;
                                    try { if (!string.IsNullOrEmpty(block) && name.Contains(block.ToLowerInvariant())) score += 2; } catch { }
                                    try { if (!string.IsNullOrEmpty(spike) && name.Contains(spike.ToLowerInvariant())) score += 2; } catch { }
                                    try { if (noParallaxBg && name.Contains("slopesa")) score += 2; } catch { }
                                    try { if (!noParallaxBg && name.Contains("slopesnone")) score += 2; } catch { }
                                    if (score > bestScore)
                                    {
                                        bestScore = score;
                                        bestFile = f;
                                    }
                                }
                                if (bestFile != null && bestScore >= 2)
                                {
                                    found = bestFile;
                                    break;
                                }
                            }
                            catch { }
                        }
                    }

                    if (!string.IsNullOrEmpty(found))
                    {
                        LoadTileset(found);
                        try { SliceTileset(); PopulateTilesPanel(); backgroundDirty = true; RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                        try { Dispatcher.Invoke(() => Redraw()); } catch { }
                        return;
                    }

                    // Nothing found — keep current tileset
                }
                else
                {
                    // Disabled: revert to embedded famidash.bmp or any previously loaded flat tileset source
                    try
                    {
                        // If a TMX provided an explicit tileset source, prefer it
                        if (!string.IsNullOrEmpty(loadedTilesetSource) && File.Exists(loadedTilesetSource))
                        {
                            LoadTileset(loadedTilesetSource);
                        }
                        else
                        {
                            var emb = LoadEmbeddedImage("famidash.bmp");
                            if (emb != null)
                            {
                                tilesetBitmap = emb;
                                SliceTileset();
                                PopulateTilesPanel();
                            }
                            else
                            {
                                // try to find a famidash.bmp/png file on disk
                                var repo = FindRepoRootFor("famidash.bmp");
                                var candidates = new List<string>();
                                if (!string.IsNullOrEmpty(repo)) candidates.Add(Path.Combine(repo, "famidash.bmp"));
                                candidates.Add(Path.Combine(AppContext.BaseDirectory, "famidash.bmp"));
                                candidates.Add(Path.Combine(AppContext.BaseDirectory, "famidash.png"));
                                foreach (var cand in candidates)
                                {
                                    try { if (!string.IsNullOrEmpty(cand) && File.Exists(cand)) { LoadTileset(cand); break; } } catch { }
                                }
                            }
                        }
                    }
                    catch { }

                    try { SliceTileset(); PopulateTilesPanel(); backgroundDirty = true; RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                    try { Dispatcher.Invoke(() => Redraw()); } catch { }
                }
            }
            catch { }
        }

    // Compute disabled sprite list from the current loadedDecoSet and update UI overlays
    private void ApplyLockSpritesToSet(string? decoOverride = null)
        {
        disabledSprites.Clear();
        if (!lockSpritesToSet) {
            // Remove overlays
            try { if (IncompatibleOverlay != null) IncompatibleOverlay.Children.Clear(); } catch { }
            // Rebuild palette to remove disable visuals
            try { PopulateSpritesPanel(); } catch { }
            return;
        }

        // Determine which deco set to use for computing disabled sprites (normalize and accept minor variants)
        string decoToUse = (decoOverride ?? loadedDecoSet ?? "").ToUpperInvariant().Trim();
        // Remove non-alphanumeric characters for robust matching (e.g. "deco 1", "deco_1")
        string decoNorm = new string(decoToUse.Where(c => char.IsLetterOrDigit(c)).ToArray());
        // Based on the selected deco set, compute the disabled sprite IDs (ranges inclusive)
        // Check EXTRAS first to avoid accidental matches with DECO
        if (decoNorm.Contains("EXTRA"))
        {
            // EXTRASPRITES1 -> ranges 0x2A–0x35 and 0x37–0x3F and 0x4A
            for (int i = 0x2A; i <= 0x35; i++) disabledSprites.Add(i);
            for (int i = 0x37; i <= 0x3F; i++) disabledSprites.Add(i);
            disabledSprites.Add(0x4A);
        }
        else if (decoNorm.Contains("DECOCLOUD") || decoNorm.Contains("DECO1") || decoNorm.StartsWith("DECO"))
        {
            // DECO1 / DECOCLOUD share the same disabled list
            // Include 0x64 in the DECO disabled list so the rainbow portal replacement
            // (0x64) is not selectable when locked to DECO1/DECOCLOUD. EXTRAS keeps it enabled.
            int[] list = new int[] { 0x4E, 0x4F, 0x66, 0x67, 0x68, 0x69, 0x4C, 0x4D, 0x50, 0x51, 0x59, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x6E, 0x79, 0x17, 0x4B, 0x58, 0x64 };
            foreach (var v in list) disabledSprites.Add(v);
        }

        // Update palette visuals and deselect if current selection invalid
        try { PopulateSpritesPanel(); } catch { }
        if (selectedSprite >= 0 && disabledSprites.Contains(selectedSprite))
        {
            selectedSprite = -1;
            try { UpdatePaletteHighlight(); } catch { }
        }

        // Update map overlays indicating incompatibilities
        try { UpdateIncompatibleOverlay(); } catch { }
    }

    private void UpdateIncompatibleOverlay()
    {
        if (IncompatibleOverlay == null || CanvasHost == null) return;
        IncompatibleOverlay.Children.Clear();
        if (!lockSpritesToSet) return;
        if (spriteImages == null || sprites == null) return;

        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
            int padPxX = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
            int padPxY = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);

            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    int idx = y * mapWidth + x;
                    int sidx = sprites[idx];
                    if (sidx >= 0 && disabledSprites.Contains(sidx))
                    {
                        var rect = new Shapes.Rectangle
                        {
                            Width = (double)tilePixelW / dpi.DpiScaleX,
                            Height = (double)tilePixelH / dpi.DpiScaleY,
                            Fill = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
                            Stroke = Brushes.Black,
                            StrokeThickness = 1,
                            IsHitTestVisible = false
                        };
                        double left = (padPxX + x * tilePixelW) / dpi.DpiScaleX;
                        double top = (padPxY + y * tilePixelH) / dpi.DpiScaleY + gridRenderShiftY;
                        Canvas.SetLeft(rect, left);
                        Canvas.SetTop(rect, top);
                        IncompatibleOverlay.Children.Add(rect);

                        var txt = new TextBlock
                        {
                            Text = "!",
                            FontWeight = FontWeights.Bold,
                            Foreground = Brushes.Black,
                            FontSize = Math.Max(12, tilePixelH / dpi.DpiScaleY / 2),
                            IsHitTestVisible = false
                        };
                        // Center the text inside rect
                        txt.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        double tx = left + ((double)tilePixelW / dpi.DpiScaleX - txt.DesiredSize.Width) / 2.0;
                        double ty = top + ((double)tilePixelH / dpi.DpiScaleY - txt.DesiredSize.Height) / 2.0;
                        Canvas.SetLeft(txt, tx);
                        Canvas.SetTop(txt, ty);
                        IncompatibleOverlay.Children.Add(txt);
                    }
                }
            }
        }
        catch { }
    }
    
    // Apply the current noParallaxBg setting by selecting the appropriate parallax bitmap
    // and slicing it so subsequent background rebuilds use the desired image.
    private void ApplyParallaxChoice()
    {
        try
        {
            // If per-level override is requested, try to load embedded noparallax first
            if (noParallaxBg)
            {
                var emb = LoadEmbeddedImage("noparallax.bmp");
                if (emb != null)
                {
                    parallaxBitmap = emb;
                    SliceParallax();
                    backgroundDirty = true;
                    try { RebuildAllTilesBitmap((ZoomSlider != null ? ZoomSlider.Value : 1.0), mapViewportPadding); } catch { Redraw(); }
                    try { Dispatcher.Invoke(() => Redraw()); } catch { }
                    return;
                }

                // If embedded resource not found, try to find a noparallax file on disk in common locations
                try
                {
                    var candidates = new List<string>();
                    var repoRoot = FindRepoRootFor("famidash.bmp");
                    if (!string.IsNullOrEmpty(repoRoot))
                    {
                        candidates.Add(Path.Combine(repoRoot, "src", "renderer", "assets", "noparallax.bmp"));
                        candidates.Add(Path.Combine(repoRoot, "src", "render", "assets", "noparallax.bmp"));
                    }
                    candidates.Add(Path.Combine(AppContext.BaseDirectory, "assets", "noparallax.bmp"));
                    candidates.Add(Path.Combine(AppContext.BaseDirectory, "noparallax.bmp"));

                    foreach (var cand in candidates)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(cand) && File.Exists(cand))
                            {
                                LoadParallax(cand);
                                backgroundDirty = true;
                                try { RebuildAllTilesBitmap((ZoomSlider != null ? ZoomSlider.Value : 1.0), mapViewportPadding); } catch { Redraw(); }
                                return;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
            // Otherwise, prefer any loaded parallax source (from TMX); if not available, fall back to embedded parallax
            if (!string.IsNullOrEmpty(loadedParallaxSource) && File.Exists(loadedParallaxSource))
            {
                LoadParallax(loadedParallaxSource);
            }
            else
            {
                var emb2 = LoadEmbeddedImage("parallax.bmp");
                if (emb2 != null)
                {
                    parallaxBitmap = emb2;
                    SliceParallax();
                }
                else
                {
                    parallaxBitmap = null;
                    parallaxImages = null;
                }
            }

            backgroundDirty = true;
            try { RebuildAllTilesBitmap((ZoomSlider != null ? ZoomSlider.Value : 1.0), mapViewportPadding); } catch { Redraw(); }
            try { Dispatcher.Invoke(() => Redraw()); } catch { }
        }
        catch { }
        }

    // Helpers to safely read ScrollViewer viewport size when it may be null
    private double SafeViewportWidth()
    {
        try { return MapScrollViewer?.ViewportWidth ?? MapScrollViewer?.ActualWidth ?? 0.0; } catch { return 0.0; }
    }

    private double SafeViewportHeight()
    {
        try { return MapScrollViewer?.ViewportHeight ?? MapScrollViewer?.ActualHeight ?? 0.0; } catch { return 0.0; }
    }

    private string GetConfigPath(string tmxFilePath)
    {
        // Store configs in Documents/Famidash Editor/
        string docsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string configFolder = Path.Combine(docsFolder, "Famidash Editor");
        
        // Create folder if it doesn't exist
        if (!Directory.Exists(configFolder))
        {
            Directory.CreateDirectory(configFolder);
        }

        

        // Use just the filename (not full path) to allow sharing configs
        string tmxFileName = Path.GetFileName(tmxFilePath);
        string configFileName = tmxFileName + ".cfg";
        
        return Path.Combine(configFolder, configFileName);
    }
    
    private void SaveTmxConfig(string tmxFilePath)
    {
        try
        {
            var config = new TmxConfig();

            // Only write tint components when a tint is actively set (alpha != 0)
            try
            {
                if (backgroundTint.A != 0)
                {
                    config.BackgroundTintR = backgroundTint.R;
                    config.BackgroundTintG = backgroundTint.G;
                    config.BackgroundTintB = backgroundTint.B;
                }
            }
            catch { }
        
        

            try
            {
                if (groundTint.A != 0)
                {
                    config.GroundTintR = groundTint.R;
                    config.GroundTintG = groundTint.G;
                    config.GroundTintB = groundTint.B;
                }
            }
            catch { }

            try
            {
                if (tileTint.A != 0)
                {
                    config.TileTintR = tileTint.R;
                    config.TileTintG = tileTint.G;
                    config.TileTintB = tileTint.B;
                }
            }
            catch { }

            // Always persist these explicit options
            config.NoParallaxBg = noParallaxBg;
            config.DecoSet = loadedDecoSet;
            config.BlockSet = loadedBlockSet;
            config.SpikeSet = loadedSpikeSet;

            string configPath = GetConfigPath(tmxFilePath);
            // Serialize and write the config file, omitting nulls
            var opts = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            string json = JsonSerializer.Serialize(config, opts);
            File.WriteAllText(configPath, json);

            System.Diagnostics.Debug.WriteLine($"Saved config to: {configPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save TMX config: {ex.Message}");
        }
    }
    
    private void LoadTmxConfig(string tmxFilePath)
    {
        try
        {
            string configPath = GetConfigPath(tmxFilePath);
            
            if (File.Exists(configPath))
            {
                string json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<TmxConfig>(json);
                
                if (config != null)
                {
                    // Apply loaded tints only when the components are present in the config
                    try
                    {
                        if (config.BackgroundTintR.HasValue && config.BackgroundTintG.HasValue && config.BackgroundTintB.HasValue)
                        {
                            backgroundTint = Color.FromRgb(config.BackgroundTintR.Value, config.BackgroundTintG.Value, config.BackgroundTintB.Value);
                        }
                    }
                    catch { }

                    try
                    {
                        if (config.GroundTintR.HasValue && config.GroundTintG.HasValue && config.GroundTintB.HasValue)
                        {
                            groundTint = Color.FromRgb(config.GroundTintR.Value, config.GroundTintG.Value, config.GroundTintB.Value);
                        }
                    }
                    catch { }

                    try
                    {
                        if (config.TileTintR.HasValue && config.TileTintG.HasValue && config.TileTintB.HasValue)
                        {
                            tileTint = Color.FromRgb(config.TileTintR.Value, config.TileTintG.Value, config.TileTintB.Value);
                        }
                    }
                    catch { }

                    // Apply loaded no-parallax setting (default false when absent in file)
                    try { noParallaxBg = config.NoParallaxBg; } catch { noParallaxBg = false; }
                    if (MenuOptionNoParallax != null) MenuOptionNoParallax.IsChecked = noParallaxBg;

                    // Apply deco set if present
                    try { loadedDecoSet = string.IsNullOrEmpty(config.DecoSet) ? "DECO1" : config.DecoSet; } catch { loadedDecoSet = "DECO1"; }
                    // Apply block/spike sets if present
                    try { loadedBlockSet = string.IsNullOrEmpty(config.BlockSet) ? "BLOCKSA" : config.BlockSet; } catch { loadedBlockSet = "BLOCKSA"; }
                    try { loadedSpikeSet = string.IsNullOrEmpty(config.SpikeSet) ? "SPIKESA" : config.SpikeSet; } catch { loadedSpikeSet = "SPIKESA"; }
                    // LockSpritesToSet is now a global editor setting; per-TMX configs no longer contain it
                    if (StatusText != null) StatusText.Text = $"Loaded deco set: {loadedDecoSet} block:{loadedBlockSet} spike:{loadedSpikeSet}";

                    // Update tinted images for any tints that were applied
                    UpdateParallaxTint();
                    UpdateGroundTint();
                    UpdateTileTint();

                    System.Diagnostics.Debug.WriteLine($"Loaded config from: {configPath}");
                    if (StatusText != null) StatusText.Text = $"Loaded tint config for {Path.GetFileName(tmxFilePath)}";
                    // Ensure the parallax choice reflects the loaded config
                    try { ApplyParallaxChoice(); } catch { }
                }
            }
            else
            {
                // No config file - reset to defaults (transparent = no tint)
                backgroundTint = Color.FromArgb(0, 0, 0, 0);
                groundTint = Color.FromArgb(0, 0, 0, 0);
                tileTint = Color.FromArgb(0, 0, 0, 0);
                
                // Don't call Update methods - just clear the toned images to use originals
                parallaxTonedImages = null;
                groundTonedImages = null;
                tileTonedImages = null;
                sawFrame1TilesTinted = null;
                sawFrame2TilesTinted = null;
                
                // Mark background dirty to force rebuild of parallax/ground without tints
                backgroundDirty = true;
                // Default: noParallax option absent -> unchecked and render parallax
                noParallaxBg = false;
                if (MenuOptionNoParallax != null) MenuOptionNoParallax.IsChecked = false;

                try { ApplyParallaxChoice(); } catch { }
                
                // Clear tile caches and rebuild tiles without tint
                try { scaledTileCaches.Clear(); } catch { }
                try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
                
                // Refresh tile palette to show untinted tiles
                try { PopulateTilesPanel(); } catch { }
                
                System.Diagnostics.Debug.WriteLine($"No config found at: {configPath}, using defaults");
                // default deco/block/spike sets when no config is present
                loadedDecoSet = "DECO1";
                loadedBlockSet = "BLOCKSA";
                loadedSpikeSet = "SPIKESA";
                // Write out a default config immediately so first-load creates .cfg with deco1
                try { SaveTmxConfig(tmxFilePath); } catch { }
                try { ApplyLockSpritesToSet(); } catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load TMX config: {ex.Message}");
            
            // Reset to defaults on error (transparent = no tint)
            backgroundTint = Color.FromArgb(0, 0, 0, 0);
            groundTint = Color.FromArgb(0, 0, 0, 0);
            tileTint = Color.FromArgb(0, 0, 0, 0);
                loadedDecoSet = "deco1";
            
            // Clear toned images to use originals
            parallaxTonedImages = null;
            groundTonedImages = null;
            tileTonedImages = null;
            sawFrame1TilesTinted = null;
            sawFrame2TilesTinted = null;
            
            // Mark background dirty to force rebuild
            backgroundDirty = true;
            
            // Clear tile caches and rebuild
            try { scaledTileCaches.Clear(); } catch { }
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
            
            // Refresh tile palette
            try { PopulateTilesPanel(); } catch { }
        }
    }
    
    // when painting (drag), build up a composite action which will be pushed on mouse up
    private TileChangeAction? currentCompositeAction = null;
    private SpriteChangeAction? currentCompositeSpriteAction = null;
    private bool suppressUndoRecording = false;
    // Ground/Parallax layout
    // groundTileRows will be set when a ground bitmap is loaded (equals groundBitmap.PixelHeight / TileSize)
    private int groundTileRows = 0; // actual rows available in ground bitmap

    // Rendering caches for performance: background (parallax+ground), tiles (incremental), sprites, grid overlay
    private RenderTargetBitmap? backgroundRtb = null;
    private RenderTargetBitmap? parallaxRtb = null;
    private RenderTargetBitmap? groundRtb = null;
    private RenderTargetBitmap? gridRtb = null;
    private WriteableBitmap? tilesWb = null;
    private WriteableBitmap? portalsWb = null; // new portal background layer (incremental updates)
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
    // Cache last explicit click tile (used as paste anchor)
    private int lastClickX = -1;
    private int lastClickY = -1;
    // Throttle timers for expensive events
    private System.Windows.Threading.DispatcherTimer? sizeChangedThrottleTimer;
    private System.Windows.Threading.DispatcherTimer? zoomThrottleTimer;
    private System.Windows.Threading.DispatcherTimer? zoomCommitTimer;
    private bool deferZoomRebuild = false;
    // Anchor used to preserve the world point under the cursor during zoom commit
    private bool hasZoomAnchor = false;
    private double zoomAnchorMapX = 0.0;
    private double zoomAnchorMapY = 0.0;
    private double zoomAnchorViewportX = 0.0;
    private double zoomAnchorViewportY = 0.0;
    // The Y offset applied to the grid image at commit time; applied to overlays as well
    private double gridRenderShiftY = 0.0;
    private int gridRenderShiftYPx = 0;
    // Preview mode for animations (saws, etc.)
    private bool previewMode = false;
    private int animationFrame = 0; // Increments each frame, used to determine animation states
    private int timerTicks = 0; // Counts all timer ticks for frame skipping logic
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
    // Portal sprites for preview mode (multi-tile replacements)
    private BitmapSource? cubePortalSprite; // 24x48 sprite (1.5x3 tiles) for sprite 0x00 in preview mode
    private BitmapSource? shipPortalSprite; // 24x48 sprite (1.5x3 tiles) for sprite 0x01 in preview mode
    private BitmapSource? ballPortalSprite; // 24x48 sprite (1.5x3 tiles) for sprite 0x02 in preview mode
    private BitmapSource? ufoPortalSprite; // 24x48 sprite (1.5x3 tiles) for sprite 0x03 in preview mode
    private BitmapSource? robotPortalSprite; // 24x48 sprite (1.5x3 tiles) for sprite 0x04 in preview mode
    private BitmapSource? wavePortalSprite; // 24x48 sprite (1.5x3 tiles) for sprite 0x24 in preview mode
    // New portal sprites added by the user
    private BitmapSource? spiderPortalSprite; // for sprite 0x17 (spider-portal.png)
    // Spider pad preview sprites (single-tile, non-animated)
    private BitmapSource? spiderPadSprite; // for sprite 0x56 (spider-pad.png)
    private BitmapSource? spiderPadUpsideDownSprite; // for sprite 0x57 (spider-pad-upsidedown.png)
    private BitmapSource? swingcopterPortalSprite; // for sprite 0x4B (swingcopter-portal.png)
    private BitmapSource? ninjaPortalSprite; // for sprite 0x58 (ninja-portal.png)
    private BitmapSource? teleportPortalEnterSprite; // for sprite 0x4E (teleport-portal-enter.png)
    private BitmapSource? teleportPortalExitSprite;  // for sprite 0x4F (teleport-portal-exit.png)
    // Horizontal teleport portal preview replacements (3x1.5 tiles)
    private BitmapSource? teleportPortalHorizontalEnterDownSprite;   // sprite 0x66
    private BitmapSource? teleportPortalHorizontalExitUpSprite;      // sprite 0x67 (shift up 1 tile)
    private BitmapSource? teleportPortalHorizontalEnterUpSprite;     // sprite 0x68 (shift up 1 tile)
    private BitmapSource? teleportPortalHorizontalExitDownSprite;    // sprite 0x69
    // Speed portal preview sprites (single-frame replacements)
    private BitmapSource? speed05xPortalSprite; // sprite 0x14
    private BitmapSource? speed1xPortalSprite;  // sprite 0x15
    private BitmapSource? speed2xPortalSprite;  // sprite 0x16
    private BitmapSource? speed3xPortalSprite;  // sprite 0x20
    private BitmapSource? speed4xPortalSprite;  // sprite 0x21
    private BitmapSource? speedSpecialPortalSprite; // sprite 0x6D
    // Additional new portals
    private BitmapSource? dualPortalSprite; // for sprite 0x22 (dual-portal.png)
    private BitmapSource? singlePortalSprite; // for sprite 0x23 (single-portal.png)
    // New mini/growth portals
    private BitmapSource? miniPortalSprite; // for sprite 0x18 (mini-portal.png - preview replacement)
    private BitmapSource? growthPortalSprite; // for sprite 0x19 (growth-portal.png - preview replacement)
    // Gravity portals
    private BitmapSource? gravityDownPortalSprite; // for sprite 0x08 (gravity-down-portal.png)
    private BitmapSource? gravityUpPortalSprite; // for sprite 0x09 (gravity-up-portal.png)
    // Horizontal gravity portals
    private BitmapSource? gravityDownDownwardsPortalSprite; // for sprite 0x10 (gravity-down-downwards-portal.png)
    private BitmapSource? gravityDownUpwardsPortalSprite; // for sprite 0x11 (gravity-down-upwards-portal.png)
    private BitmapSource? gravityUpDownwardsPortalSprite; // for sprite 0x12 (gravity-up-downwards-portal.png)
    private BitmapSource? gravityUpUpwardsPortalSprite; // for sprite 0x13 (gravity-up-upwards-portal.png)
    // Additional gravity-strength X-axis portals (preview replacements)
    private BitmapSource? gravity1ThirdXPortalSprite; // for sprite 0x5F (gravity-1-3rd-x-portal.png)
    private BitmapSource? gravity1HalfXPortalSprite; // for sprite 0x60 (gravity-1-half-x-portal.png)
    private BitmapSource? gravity2ThirdXPortalSprite; // for sprite 0x61 (gravity-2-3rd-x-portal.png)
    private BitmapSource? gravity2XPortalSprite; // for sprite 0x62 (gravity-2x-portal.png)
    private BitmapSource? gravity1XPortalSprite; // for sprite 0x63 (gravity-1x-portal.png)
    // Yellow orb animation frames: 4 frames for sprites 0x0B, 0x1F, 0x29
    private BitmapSource[]? yellowOrbFrame1; // Frame 1 for all 3 yellow orb sprites
    private BitmapSource[]? yellowOrbFrame2; // Frame 2 for all 3 yellow orb sprites
    private BitmapSource[]? yellowOrbFrame3; // Frame 3 for all 3 yellow orb sprites
    private BitmapSource[]? yellowOrbFrame4; // Frame 4 for all 3 yellow orb sprites
    // Blue orb animation frames: 4 frames for sprite 0x05
    private BitmapSource[]? blueOrbFrame1;
    private BitmapSource[]? blueOrbFrame2;
    private BitmapSource[]? blueOrbFrame3;
    private BitmapSource[]? blueOrbFrame4;
    // White orb animation frames: 4 frames for sprite 0x7A
    private BitmapSource[]? whiteOrbFrame1;
    private BitmapSource[]? whiteOrbFrame2;
    private BitmapSource[]? whiteOrbFrame3;
    private BitmapSource[]? whiteOrbFrame4;
    // Coin animation frames: 4 frames shared by sprites 0x07, 0x1A, 0x1B
    private BitmapSource[]? coinFrame1;
    private BitmapSource[]? coinFrame2;
    private BitmapSource[]? coinFrame3;
    private BitmapSource[]? coinFrame4;
    // Red pad (preview-only) animation frames: 4 frames for sprite 0x52
    private BitmapSource[]? redPadFrame1;
    private BitmapSource[]? redPadFrame2;
    private BitmapSource[]? redPadFrame3;
    private BitmapSource[]? redPadFrame4;
    // Additional pad animations (preview-only)
    // Sprite 0x53: red-pad-up-frame1..4
    private BitmapSource[]? redPadUpFrame1;
    private BitmapSource[]? redPadUpFrame2;
    private BitmapSource[]? redPadUpFrame3;
    private BitmapSource[]? redPadUpFrame4;
    // Sprite 0x0A: yellow-pad-down-frame1..4
    private BitmapSource[]? yellowPadDownFrame1;
    private BitmapSource[]? yellowPadDownFrame2;
    private BitmapSource[]? yellowPadDownFrame3;
    private BitmapSource[]? yellowPadDownFrame4;
    // Sprite 0x0C: yellow-pad-up-frame1..4
    private BitmapSource[]? yellowPadUpFrame1;
    private BitmapSource[]? yellowPadUpFrame2;
    private BitmapSource[]? yellowPadUpFrame3;
    private BitmapSource[]? yellowPadUpFrame4;
    // Sprite 0x0D: blue-pad-down-frame1..4
    private BitmapSource[]? bluePadDownFrame1;
    private BitmapSource[]? bluePadDownFrame2;
    private BitmapSource[]? bluePadDownFrame3;
    private BitmapSource[]? bluePadDownFrame4;
    // Sprite 0x0E: blue-pad-up-frame1..4
    private BitmapSource[]? bluePadUpFrame1;
    private BitmapSource[]? bluePadUpFrame2;
    private BitmapSource[]? bluePadUpFrame3;
    private BitmapSource[]? bluePadUpFrame4;
    // Sprite 0x25: pink-pad-down-frame1..4
    private BitmapSource[]? pinkPadDownFrame1;
    private BitmapSource[]? pinkPadDownFrame2;
    private BitmapSource[]? pinkPadDownFrame3;
    private BitmapSource[]? pinkPadDownFrame4;
    // Sprite 0x26: pink-pad-up-frame1..4
    private BitmapSource[]? pinkPadUpFrame1;
    private BitmapSource[]? pinkPadUpFrame2;
    private BitmapSource[]? pinkPadUpFrame3;
    private BitmapSource[]? pinkPadUpFrame4;
    // Pink orb animation frames: 4 frames for sprite 0x06
    private BitmapSource[]? pinkOrbFrame1;
    private BitmapSource[]? pinkOrbFrame2;
    private BitmapSource[]? pinkOrbFrame3;
    private BitmapSource[]? pinkOrbFrame4;
    // Green orb animation frames: 4 frames for sprite 0x27
    private BitmapSource[]? greenOrbFrame1;
    private BitmapSource[]? greenOrbFrame2;
    private BitmapSource[]? greenOrbFrame3;
    private BitmapSource[]? greenOrbFrame4;
    // Red orb animation frames: 4 frames for sprite 0x28
    private BitmapSource[]? redOrbFrame1;
    private BitmapSource[]? redOrbFrame2;
    private BitmapSource[]? redOrbFrame3;
    private BitmapSource[]? redOrbFrame4;
    // Black orb animation frames: 4 frames for sprite 0x44
    private BitmapSource[]? blackOrbFrame1;
    private BitmapSource[]? blackOrbFrame2;
    private BitmapSource[]? blackOrbFrame3;
    private BitmapSource[]? blackOrbFrame4;
    // Dash orb 2-frame slow animations (preview-only)
    private BitmapSource[]? dashOrbRightFrame1;
    private BitmapSource[]? dashOrbRightFrame2;
    private BitmapSource[]? dashGravityOrbRightFrame1;
    private BitmapSource[]? dashGravityOrbRightFrame2;
    private BitmapSource[]? dashOrb45UpFrame1;
    private BitmapSource[]? dashOrb45UpFrame2;
    private BitmapSource[]? dashGravityOrb45UpFrame1;
    private BitmapSource[]? dashGravityOrb45UpFrame2;
    private BitmapSource[]? dashOrb45DownFrame1;
    private BitmapSource[]? dashOrb45DownFrame2;
    private BitmapSource[]? dashGravityOrb45DownFrame1;
    private BitmapSource[]? dashGravityOrb45DownFrame2;
    private BitmapSource[]? dashOrbUpFrame1;
    private BitmapSource[]? dashOrbUpFrame2;
    private BitmapSource[]? dashGravityOrbUpFrame1;
    private BitmapSource[]? dashGravityOrbUpFrame2;
    private BitmapSource[]? dashOrbDownFrame1;
    private BitmapSource[]? dashOrbDownFrame2;
    private BitmapSource[]? dashGravityOrbDownFrame1;
    private BitmapSource[]? dashGravityOrbDownFrame2;
    // Teleport orb 2-frame animations
    private BitmapSource[]? teleportOrbEnterFrame1;
    private BitmapSource[]? teleportOrbEnterFrame2;
    private BitmapSource[]? teleportOrbExitFrame1;
    private BitmapSource[]? teleportOrbExitFrame2;
    // Spider orb 2-frame animations (new)
    private BitmapSource[]? spiderOrbDownFrame1;
    private BitmapSource[]? spiderOrbDownFrame2;
    private BitmapSource[]? spiderOrbUpFrame1;
    private BitmapSource[]? spiderOrbUpFrame2;
    // Star decoration 2-frame preview animation (sprite 0x36)
    private BitmapSource[]? starFrame1;
    private BitmapSource[]? starFrame2;
    // New decorations: pulsing ball (0x49) and music note (0x4A)
    private BitmapSource[]? pulsingBallFrame1;
    private BitmapSource[]? pulsingBallFrame2;
    private BitmapSource[]? musicNoteFrame1;
    private BitmapSource[]? musicNoteFrame2;
    // Additional decoration two-frame preview animations
    private BitmapSource[]? diamondFrame1; // sprite 0x32
    private BitmapSource[]? diamondFrame2;
    private BitmapSource[]? diamondHalfFrame1; // sprite 0x33
    private BitmapSource[]? diamondHalfFrame2;
    private BitmapSource[]? questionMarkFrame1; // sprite 0x34
    private BitmapSource[]? questionMarkFrame2;
    private BitmapSource[]? exclamationFrame1; // sprite 0x35
    private BitmapSource[]? exclamationFrame2;
    private BitmapSource[]? xFrame1; // sprite 0x37
    private BitmapSource[]? xFrame2;
    private BitmapSource[]? poleShortFrame1; // sprite 0x2C
    private BitmapSource[]? poleShortFrame2;
    private BitmapSource[]? poleShortUpsideDownFrame1; // sprite 0x3C
    private BitmapSource[]? poleShortUpsideDownFrame2;
    // New short pole decorations (left/right) two-frame previews
    private BitmapSource[]? poleLeftShortFrame1; // sprite 0x38
    private BitmapSource[]? poleLeftShortFrame2;
    private BitmapSource[]? poleRightShortFrame1; // sprite 0x39
    private BitmapSource[]? poleRightShortFrame2;
    // Medium pole replacements (sprite 0x3E/0x3F)
    private BitmapSource[]? poleLeftMediumFrame1; // sprite 0x3E
    private BitmapSource[]? poleLeftMediumFrame2;
    private BitmapSource[]? poleRightMediumFrame1; // sprite 0x3F
    private BitmapSource[]? poleRightMediumFrame2;
    // Pole-medium (1.5 tiles tall) decorative replacements for sprite 0x2C/0x3C
    private BitmapSource[]? poleMediumFrame1; // sprite 0x2C uses custom indices 2122/2123
    private BitmapSource[]? poleMediumFrame2;
    private BitmapSource[]? poleMediumUpsideDownFrame1; // sprite 0x3C uses custom indices 2124/2125
    private BitmapSource[]? poleMediumUpsideDownFrame2;
    // Pole-long (2 tiles tall) decorative replacements for sprite 0x2A/0x3A
    private BitmapSource[]? poleLongFrame1; // sprite 0x2A -> custom 2140/2141
    private BitmapSource[]? poleLongFrame2;
    private BitmapSource[]? poleLongUpsideDownFrame1; // sprite 0x3A -> custom 2142/2143
    private BitmapSource[]? poleLongUpsideDownFrame2;
    // Chain decorations (single-frame preview-only)
    private BitmapSource[]? chainFrame1; // sprite 0x2D
    private BitmapSource[]? chainUpsideDownFrame1; // sprite 0x3D
    // New single-frame decoration previews (spikes)
    private BitmapSource[]? decoSpikesFrame1; // sprite 0x2E
    private BitmapSource[]? decoSpikesUpsideDownFrame1; // sprite 0x2F
    private BitmapSource[]? decoSpikesSmallFrame1; // sprite 0x30
    private BitmapSource[]? decoSpikesSmallUpsideDownFrame1; // sprite 0x31
    // Random frame offsets for each sprite position to desynchronize animations
    private Dictionary<int, int> spriteFrameOffsets = new Dictionary<int, int>();
    private Random spriteAnimationRandom = new Random();
    private int currentSpritePositionKey = 0; // Temp variable for passing position to GetAnimatedSpriteIndex
    // Caches for tinted decoration bitmaps (keyed by combined (id<<32)|ARGB)
    // Simple LRU cache to limit memory usage when storing tinted bitmaps
    private class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int capacity;
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>();
        private readonly LinkedList<KeyValuePair<TKey, TValue>> list = new LinkedList<KeyValuePair<TKey, TValue>>();

        public LruCache(int capacity)
        {
            this.capacity = Math.Max(16, capacity);
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            if (map.TryGetValue(key, out var node))
            {
                // move to front
                list.Remove(node);
                list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default;
            return false;
        }

        public TValue? this[TKey key]
        {
            set { Put(key, value); }
            get
            {
                if (TryGetValue(key, out var v)) return v;
                return default;
            }
        }

                public void Put(TKey key, TValue? value)
        {
            if (map.TryGetValue(key, out var node))
            {
                node.Value = new KeyValuePair<TKey, TValue>(key, value!);
                list.Remove(node);
                list.AddFirst(node);
            }
            else
            {
                var kv = new KeyValuePair<TKey, TValue>(key, value!);
                var n = list.AddFirst(kv);
                map[key] = n;
                if (map.Count > capacity)
                {
                    var last = list.Last;
                    if (last != null)
                    {
                        map.Remove(last.Value.Key);
                        list.RemoveLast();
                    }
                }
            }
        }

        public void Clear()
        {
            map.Clear();
            list.Clear();
        }
    }

    private const int TintCacheCapacity = 512;
    private readonly LruCache<long, BitmapSource?> tintedSpriteCache = new LruCache<long, BitmapSource?>(TintCacheCapacity);
    private readonly LruCache<long, BitmapSource?> tintedCustomCache = new LruCache<long, BitmapSource?>(TintCacheCapacity);
    // Lock for thread-safe access to the tinted caches when precomputing on background threads
    private readonly object tintedCacheLock = new object();
    // Decoration sprite ids that should receive player tinting
    private readonly System.Collections.Generic.HashSet<int> decorationSpriteIds = new System.Collections.Generic.HashSet<int> { 0x36, 0x32, 0x33, 0x34, 0x35, 0x37, 0x2C, 0x3C, 0x2D, 0x3D, 0x2E, 0x2F, 0x30, 0x31, 0x38, 0x39, 0x3E, 0x3F, 0x2B, 0x3B, 0x2A, 0x3A, 0x49, 0x4A };
    // Portal debug log path (initialized at startup)
    private string? portalDebugPath = null;

        // Clear tinted caches (call when player tint changes)
        private void ClearTintedCaches()
        {
            try
            {
                tintedSpriteCache.Clear();
            }
            catch { }
            try
            {
                tintedCustomCache.Clear();
            }
            catch { }
        }

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
            // Attempt to populate the FamiStudio track combo from a pre-parsed JSON or the album TXT
            try { TryLoadFamiAlbumParsedJson(); } catch { }
            // Wire main toolbar Play/Stop buttons (only toolbar controls should drive playback)
            try { if (PlayFamiButton != null) PlayFamiButton.Click += PlayFamiButton_Click; } catch { }
            try { if (StopFamiButton != null) StopFamiButton.Click += StopFamiButton_Click; } catch { }
            // Wire configure FamiStudio menu
            try { if (MenuConfigureFamiStudio != null) MenuConfigureFamiStudio.Click += MenuConfigureFamiStudio_Click; } catch { }

            // Background warm-up: load FamiStudio assemblies and warm the in-process renderer/audio device
            try
            {
                Task.Run(() =>
                {
                    try
                    {
                        // Prefer any explicit configured path
                        if (!string.IsNullOrEmpty(famiStudioPath) && Directory.Exists(famiStudioPath) && !famiIntegration.IsLoaded)
                        {
                            try { famiIntegration.LoadFromFolder(famiStudioPath); } catch { }
                        }

                        // Look for a local .fms next to the repo root or the album txt path
                        string? probeFms = null;
                        var repoFms = Path.Combine(Environment.CurrentDirectory, "the album.fms");
                        if (File.Exists(repoFms)) probeFms = repoFms;
                        if (string.IsNullOrEmpty(probeFms) && !string.IsNullOrEmpty(albumTxtPath) && Path.GetExtension(albumTxtPath).Equals(".fms", StringComparison.OrdinalIgnoreCase)) probeFms = albumTxtPath;
                        if (!string.IsNullOrEmpty(probeFms) && File.Exists(probeFms))
                        {
                            try { famiIntegration.WarmAndPrime(probeFms); } catch { }
                        }
                    }
                    catch { }
                });
            }
            catch { }
            
            // Wire up window closing event to prompt for unsaved changes
            Closing += Window_Closing;

            // Zoom slider handling: preview via quick transform, commit full render on release or after 1s idle
            if (ZoomSlider != null)
            {
                bool isZoomSliderPressed = false;

                zoomThrottleTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(150)
                };
                zoomThrottleTimer.Tick += (s, e) =>
                {
                    zoomThrottleTimer?.Stop();
                    if (!isZoomSliderPressed && !deferZoomRebuild)
                    {
                        Redraw();
                        lastHoverX = -1; lastHoverY = -1;
                    }
                };

                // Commit timer: wait for 1s of inactivity (wheel) or used to delay commit
                zoomCommitTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                zoomCommitTimer.Tick += (s, e) =>
                {
                    zoomCommitTimer?.Stop();
                    deferZoomRebuild = false;
                    LoadingWindow? zoomLoadingWindow = null;
                    try
                    {
                        zoomLoadingWindow = new LoadingWindow { Owner = this };
                        zoomLoadingWindow.SetMessage("Rendering zoom...");
                        zoomLoadingWindow.Show();
                        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        CommitZoom();
                    }
                    finally { if (zoomLoadingWindow != null) zoomLoadingWindow.Close(); }
                };

                // Track mouse down/up on slider thumb
                ZoomSlider.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    isZoomSliderPressed = true;
                    // record current mouse viewport point as anchor
                    try
                    {
                        if (MapScrollViewer != null)
                        {
                            var mp = Mouse.GetPosition(MapScrollViewer);
                            zoomAnchorViewportX = mp.X; zoomAnchorViewportY = mp.Y;
                            double hp = MapScrollViewer?.HorizontalOffset ?? 0; double vp = MapScrollViewer?.VerticalOffset ?? 0;
                            double contentX = hp + mp.X; double contentY = vp + mp.Y;
                            double oldScale = (ZoomSlider!=null?ZoomSlider.Value:1.0);
                            double pad = mapViewportPadding;
                            zoomAnchorMapX = (contentX - pad) / (TileSize * oldScale);
                            zoomAnchorMapY = (contentY - pad) / (TileSize * oldScale);
                            hasZoomAnchor = true;
                        }
                    } catch { hasZoomAnchor = false; }
                };
                ZoomSlider.PreviewMouseLeftButtonUp += (s, e) =>
                {
                    isZoomSliderPressed = false;
                    // Stop any pending commit timer and do an immediate full render with dialog
                    zoomCommitTimer?.Stop();
                    LoadingWindow? zoomLoadingWindow = null;
                    try
                    {
                        zoomLoadingWindow = new LoadingWindow { Owner = this };
                        zoomLoadingWindow.SetMessage("Rendering zoom...");
                        zoomLoadingWindow.Show();
                        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        deferZoomRebuild = false;
                        // CommitZoom will respect the stored zoom anchor if present
                        CommitZoom();
                    }
                    finally { if (zoomLoadingWindow != null) zoomLoadingWindow.Close(); }
                };

                ZoomSlider.ValueChanged += (s, e) =>
                {
                    // Update visible numeric zoom label
                    try { if (ZoomLevelLabel != null) ZoomLevelLabel.Text = $"{(ZoomSlider!=null?ZoomSlider.Value:1.0):0.00}x"; } catch { }
                    // Provide immediate visual feedback by scaling existing images
                    UpdateQuickZoomTransform();

                    // During interaction, defer full rebuild
                    deferZoomRebuild = true;

                    // If the slider is not actively being dragged, start the 1s commit timer
                    if (!isZoomSliderPressed)
                    {
                        // capture current mouse position as an anchor for non-drag wheel/keyboard changes
                        try
                        {
                            if (MapScrollViewer != null)
                            {
                                var mp = Mouse.GetPosition(MapScrollViewer);
                                zoomAnchorViewportX = mp.X; zoomAnchorViewportY = mp.Y;
                                double hp = MapScrollViewer?.HorizontalOffset ?? 0; double vp = MapScrollViewer?.VerticalOffset ?? 0;
                                double contentX = hp + mp.X; double contentY = vp + mp.Y;
                                double oldScale = (ZoomSlider!=null?ZoomSlider.Value:1.0);
                                double pad = mapViewportPadding;
                                zoomAnchorMapX = (contentX - pad) / (TileSize * oldScale);
                                zoomAnchorMapY = (contentY - pad) / (TileSize * oldScale);
                                hasZoomAnchor = true;
                            }
                        } catch { hasZoomAnchor = false; }

                        zoomCommitTimer?.Stop();
                        zoomCommitTimer?.Start();
                    }
                };
            }
            if (SetOptionsButton != null) SetOptionsButton.Click += (s, e) => SetOptionsButton_Click(s, e);
            if (GridDarknessSlider != null) GridDarknessSlider.ValueChanged += GridDarknessSlider_ValueChanged;
            if (SaveButton != null) SaveButton.Click += SaveButton_Click;
            if (LoadButton != null) LoadButton.Click += LoadButton_Click;
            if (ResizeButton != null) ResizeButton.Click += ResizeButton_Click;

            if (CanvasHost != null)
            {
                CanvasHost.MouseLeftButtonDown += CanvasHost_MouseLeftButtonDown;
                CanvasHost.MouseMove += CanvasHost_MouseMove;
                CanvasHost.MouseLeftButtonUp += CanvasHost_MouseLeftButtonUp;
                CanvasHost.MouseDown += CanvasHost_MouseDown;
                CanvasHost.MouseUp += CanvasHost_MouseUp;
                CanvasHost.MouseLeave += CanvasHost_MouseLeave;
                CanvasHost.MouseRightButtonDown += CanvasHost_MouseRightButtonDown;
                CanvasHost.PreviewMouseWheel += CanvasHost_PreviewMouseWheel;
            }
            // Also handle wheel at ScrollViewer level so Ctrl+wheel zoom never falls through to scrolling
            if (MapScrollViewer != null)
            {
                MapScrollViewer.PreviewMouseWheel += (s, e) =>
                {
                    try { CanvasHost_PreviewMouseWheel(MapScrollViewer, e); } catch { }
                };
            }

            // Add MouseUp handler to TilesPanel to catch mouse releases that escape individual tile images
            if (TilesPanel != null)
            {
                TilesPanel.MouseLeftButtonUp += (s, e) =>
                {
                    if (isSelectingMultipleTiles)
                    {
                        isSelectingMultipleTiles = false;
                        tileSelectionStart = null;
                        // Release any captured mouse
                        if (Mouse.Captured != null)
                        {
                            Mouse.Captured.ReleaseMouseCapture();
                        }
                    }
                };
            }

            InitDefaultMap();
                Loaded += (s, e) =>
                {
                    // Ensure initial layout completes before the first redraw so measurements are accurate.
                    LoadAssetsOnStart();
                    // If the user previously enabled accurate tileset switching, attempt to apply it now
                    try { if (showAccurateTileset) SetShowAccurateTileset(true); } catch { }
                    // Ensure palette UI reflects default active layer (tiles) on startup
                    try { UpdatePaletteHighlight(); } catch { }
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
                    // Install a native window hook to capture horizontal mouse wheel (WM_MOUSEHWHEEL)
                    try
                    {
                        var helper = new WindowInteropHelper(this);
                        var src = HwndSource.FromHwnd(helper.Handle);
                        if (src != null)
                        {
                            src.AddHook(NativeWindowProc);
                        }
                    }
                    catch { }
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

            // Wire up draw mode controls
            if (DrawTileButton != null) DrawTileButton.Checked += DrawModeButton_Checked;
            if (DrawLineButton != null) DrawLineButton.Checked += DrawModeButton_Checked;
            if (DrawSquareButton != null) DrawSquareButton.Checked += DrawModeButton_Checked;
            if (DrawCircleButton != null) DrawCircleButton.Checked += DrawModeButton_Checked;
            if (DrawTriangleButton != null) DrawTriangleButton.Checked += DrawModeButton_Checked;
            if (DrawPolygonButton != null) DrawPolygonButton.Checked += DrawModeButton_Checked;
            if (DrawTileButton != null) DrawTileButton.Unchecked += DrawModeButton_Unchecked;
            if (DrawLineButton != null) DrawLineButton.Unchecked += DrawModeButton_Unchecked;
            if (DrawSquareButton != null) DrawSquareButton.Unchecked += DrawModeButton_Unchecked;
            if (DrawCircleButton != null) DrawCircleButton.Unchecked += DrawModeButton_Unchecked;
            if (DrawTriangleButton != null) DrawTriangleButton.Unchecked += DrawModeButton_Unchecked;
            if (DrawPolygonButton != null) DrawPolygonButton.Unchecked += DrawModeButton_Unchecked;
            if (HollowCheckBox != null) HollowCheckBox.Checked += (s,e)=>{ hollowShape = true; if (isDeferredDrawing||isConstructingPolygon) UpdateDeferredPreview(); };
            if (HollowCheckBox != null) HollowCheckBox.Unchecked += (s,e)=>{ hollowShape = false; if (isDeferredDrawing||isConstructingPolygon) UpdateDeferredPreview(); };
            if (BrushThicknessSlider != null) BrushThicknessSlider.ValueChanged += (s,e)=>{ brushThickness = (int)Math.Max(1, Math.Round(e.NewValue)); if (isDeferredDrawing||isConstructingPolygon) UpdateDeferredPreview(); };
            // Initialize last-known selection to avoid immediate cancellation of polygons
            lastKnownSelectedTile = selectedTile; lastKnownSelectedSprite = selectedSprite;
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
            if (MenuFileNew != null) MenuFileNew.Click += NewMenuItem_Click;
            if (MenuFileSave != null) MenuFileSave.Click += SaveButton_Click;
            if (MenuFileSaveAs != null) MenuFileSaveAs.Click += MenuFileSaveAs_Click;
            if (MenuFileLoad != null) MenuFileLoad.Click += LoadButton_Click;
            
            if (MenuToolPlace != null) MenuToolPlace.Click += (s, e) => { if (PlaceTool != null) PlaceTool.IsChecked = true; };
            if (MenuToolMove != null) MenuToolMove.Click += (s, e) => { if (MoveTool != null) MoveTool.IsChecked = true; };
            if (MenuToolErase != null) MenuToolErase.Click += (s, e) => { if (EraseTool != null) EraseTool.IsChecked = true; };
            if (MenuToolFill != null) MenuToolFill.Click += (s, e) => { if (FillTool != null) FillTool.IsChecked = true; };
            if (MenuToolSelect != null) MenuToolSelect.Click += (s, e) => { if (SelectTool != null) SelectTool.IsChecked = true; };
            if (MenuToolWand != null) MenuToolWand.Click += (s, e) => { if (MagicWandTool != null) MagicWandTool.IsChecked = true; };
            if (MenuEditCopy != null) MenuEditCopy.Click += (s, e) => CopySelection();
            if (MenuEditCut != null) MenuEditCut.Click += (s, e) => CutSelection();
            if (MenuEditPaste != null) MenuEditPaste.Click += (s, e) => {
                int dx = (selW > 0 && selH > 0) ? selX : (lastClickX >= 0 ? lastClickX : lastHoverX);
                int dy = (selW > 0 && selH > 0) ? selY : (lastClickY >= 0 ? lastClickY : lastHoverY);
                if (dx >= 0 && dy >= 0) PasteClipboardAt(dx, dy);
            };
            if (MenuToolUndo != null) MenuToolUndo.Click += (s, e) => Undo();
            if (MenuToolRedo != null) MenuToolRedo.Click += (s, e) => Redo();
            
            if (MenuColorEditorBackground != null) MenuColorEditorBackground.Click += BgColorButton_Click;
            if (MenuColorBackgroundTint != null) MenuColorBackgroundTint.Click += BgTintButton_Click;
            if (MenuColorGroundTint != null) MenuColorGroundTint.Click += GroundTintButton_Click;
            if (MenuColorTileTint != null) MenuColorTileTint.Click += TileTintButton_Click;
            if (MenuColorPlayerTint != null) MenuColorPlayerTint.Click += PlayerTintButton_Click;
            
            if (MenuOptionLegacyTriggers != null) 
            {
                MenuOptionLegacyTriggers.Checked += (s, e) => 
                { 
                    useLegacyTriggerOffset = true; 
                    SaveSettingsWithTriggerOption();
                };
                MenuOptionLegacyTriggers.Unchecked += (s, e) => 
                { 
                    useLegacyTriggerOffset = false; 
                    SaveSettingsWithTriggerOption();
                };
            }
            // Global lock sprites option in main Options menu
            if (MenuOptionLockSprites != null)
            {
                // Initialize checked state from loaded settings
                MenuOptionLockSprites.IsChecked = lockSpritesToSet;
                MenuOptionLockSprites.Checked += (s, e) => { try { SetLockSpritesToSet(true); } catch { } };
                MenuOptionLockSprites.Unchecked += (s, e) => { try { SetLockSpritesToSet(false); } catch { } };
            }
            // Swap Mouse Wheel Scroll (global user preference)
            if (MenuOptionSwapMouseWheel != null)
            {
                MenuOptionSwapMouseWheel.Checked += (s, e) =>
                {
                    swapMouseWheelScroll = true;
                    SaveSettingsWithTriggerOption();
                };
                MenuOptionSwapMouseWheel.Unchecked += (s, e) =>
                {
                    swapMouseWheelScroll = false;
                    SaveSettingsWithTriggerOption();
                };
            }
            // Invert pinch gesture option (some devices report inverted scale)
            if (MenuOptionInvertPinch != null)
            {
                MenuOptionInvertPinch.Checked += (s, e) => { invertPinchGesture = true; SaveSettingsWithTriggerOption(); };
                MenuOptionInvertPinch.Unchecked += (s, e) => { invertPinchGesture = false; SaveSettingsWithTriggerOption(); };
            }
            // Hide color triggers preview option
            if (MenuOptionHideColorTriggers != null)
            {
                MenuOptionHideColorTriggers.Checked += (s, e) =>
                {
                    hideColorTriggers = true;
                    SaveSettingsWithTriggerOption();
                    try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                };
                MenuOptionHideColorTriggers.Unchecked += (s, e) =>
                {
                    hideColorTriggers = false;
                    SaveSettingsWithTriggerOption();
                    try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                };
            }
            // Hide all invisible sprites (Preview Mode Options)
            if (MenuOptionHideInvisibleSprites != null)
            {
                MenuOptionHideInvisibleSprites.Checked += (s, e) =>
                {
                    hideInvisibleSprites = true;
                    SaveSettingsWithTriggerOption();
                    // Only rebuild sprites immediately if preview mode is active to avoid flicker
                    if (previewMode)
                    {
                        try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                    }
                };
                MenuOptionHideInvisibleSprites.Unchecked += (s, e) =>
                {
                    hideInvisibleSprites = false;
                    SaveSettingsWithTriggerOption();
                    // Only rebuild sprites immediately if preview mode is active to avoid flicker
                    if (previewMode)
                    {
                        try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                    }
                };
            }
            // No Parallax BG (per-level) option
            if (MenuOptionNoParallax != null)
            {
                MenuOptionNoParallax.Checked += (s, e) =>
                {
                    if (suppressNoParallaxHandler) return;
                    noParallaxBg = true;
                    // save to per-level config immediately if a file is loaded
                    try { if (!string.IsNullOrEmpty(currentFilePath)) SaveTmxConfig(currentFilePath); } catch { }
                    // Apply choice and rebuild background to apply change immediately
                    try { ApplyParallaxChoice(); } catch { backgroundDirty = true; try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); } }
                };
                MenuOptionNoParallax.Unchecked += (s, e) =>
                {
                    if (suppressNoParallaxHandler) return;
                    noParallaxBg = false;
                    try { if (!string.IsNullOrEmpty(currentFilePath)) SaveTmxConfig(currentFilePath); } catch { }
                    try { ApplyParallaxChoice(); } catch { backgroundDirty = true; try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); } }
                };
            }
            
            if (UndoButton != null) UndoButton.Click += (s, e) => Undo();
            if (RedoButton != null) RedoButton.Click += (s, e) => Redo();
            if (CopyButton != null) CopyButton.Click += (s, e) => CopySelection();
            if (CutButton != null) CutButton.Click += (s, e) => CutSelection();
            if (PasteButton != null) PasteButton.Click += (s, e) => {
                int dx = (selW > 0 && selH > 0) ? selX : (lastClickX >= 0 ? lastClickX : lastHoverX);
                int dy = (selW > 0 && selH > 0) ? selY : (lastClickY >= 0 ? lastClickY : lastHoverY);
                if (dx >= 0 && dy >= 0) PasteClipboardAt(dx, dy);
            };
            
            // Preview mode checkbox and timer
            if (PreviewModeCheckbox != null)
            {
                PreviewModeCheckbox.Checked += async (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Preview Mode CHECKED at {DateTime.Now}");
                    previewMode = true;
                    StartPreviewTimer();

                    LoadingWindow? loading = null;
                    try
                    {
                        loading = new LoadingWindow { Owner = this };
                        loading.SetMessage("Enabling preview mode... rendering frames");
                        loading.Show();

                        // Allow the UI to render the dialog and the checkbox change before heavy work
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

                        // Start background precomputation of tinted decoration sprites to reduce per-frame tint work
                        var _ = PrecomputeTintedCachesAsync();

                        // Perform the rebuilds (still on UI thread for safety) after UI had a chance to update
                        RebuildAllTilesBitmap((ZoomSlider != null ? ZoomSlider.Value : 1.0), mapViewportPadding);
                        RebuildAllSpritesBitmap((ZoomSlider != null ? ZoomSlider.Value : 1.0), mapViewportPadding);
                    }
                    finally
                    {
                        if (loading != null)
                        {
                            loading.Close();
                        }
                    }
                };

                PreviewModeCheckbox.Unchecked += async (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Preview Mode UNCHECKED at {DateTime.Now}");
                    previewMode = false;
                    StopPreviewTimer();
                    animationFrame = 0;

                    LoadingWindow? loading = null;
                    try
                    {
                        loading = new LoadingWindow { Owner = this };
                        loading.SetMessage("Disabling preview mode... rebuilding frames");
                        loading.Show();

                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);

                        // Kick off a background refresh of tinted cache (keeps cache warm for next toggle)
                        var __ = PrecomputeTintedCachesAsync();

                        // Clear portal layer and redraw to show non-animated tiles and sprites
                        if (portalsWb != null)
                        {
                            try
                            {
                                portalsWb.Lock();
                                unsafe
                                {
                                    IntPtr pBackBuffer = portalsWb.BackBuffer;
                                    if (pBackBuffer != IntPtr.Zero)
                                    {
                                        int stride = portalsWb.BackBufferStride;
                                        int bytesTotal = stride * cachedPixelHeight;
                                        byte* ptr = (byte*)pBackBuffer.ToPointer();
                                        for (int i = 0; i < bytesTotal; i++) ptr[i] = 0;
                                    }
                                }
                                portalsWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
                            }
                            finally { try { portalsWb.Unlock(); } catch { } }
                        }

                        // Redraw to show non-animated tiles and sprites
                        RebuildAllTilesBitmap((ZoomSlider != null ? ZoomSlider.Value : 1.0), mapViewportPadding);
                        RebuildAllSpritesBitmap((ZoomSlider != null ? ZoomSlider.Value : 1.0), mapViewportPadding);
                    }
                    finally
                    {
                        if (loading != null)
                        {
                            loading.Close();
                        }
                    }
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
            // Handle key up for stopping continuous Shift+arrow scrolling
            this.PreviewKeyUp += MainWindow_PreviewKeyUp;
            
            // pinch zoom support
            if (MapScrollViewer != null)
            {
                MapScrollViewer.ManipulationStarting += MapScrollViewer_ManipulationStarting;
                MapScrollViewer.ManipulationCompleted += MapScrollViewer_ManipulationCompleted;
                MapScrollViewer.ManipulationDelta += MapScrollViewer_ManipulationDelta;
            }
        }

        private void MenuOpenFmsPlayer_Click(object sender, RoutedEventArgs e)
        {
            // FMS Player UI removed. This handler should no longer be reachable.
            try { System.Diagnostics.Debug.WriteLine("MenuOpenFmsPlayer_Click called but the menu item was removed."); } catch { }
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
                // Ensure canvas keeps focus so subsequent wheel events remain routed here
                try { if (CanvasHost != null) { CanvasHost.Focus(); Keyboard.Focus(CanvasHost); } } catch { }

                double oldScale = ZoomSlider.Value;
                // use a multiplicative zoom per mouse wheel notch (120 delta = one notch)
                // Invert sign so wheel-up zooms in and wheel-down zooms out
                double factorPerNotch = 1.1; // 10% per notch
                // Respect user preference: the menu option now means "Invert Wheel/Pinch Zoom".
                // Its checked state should invert the behavior compared to the old default.
                // Choose sign so that when the option is CHECKED the behavior is inverted relative to before.
                double sign = invertPinchGesture ? -1.0 : 1.0;
                double factor = Math.Pow(factorPerNotch, sign * e.Delta / 120.0);
                double newScale = oldScale * factor;
                // clamp to slider limits
                newScale = Math.Max(ZoomSlider.Minimum, Math.Min(ZoomSlider.Maximum, newScale));
                if (Math.Abs(newScale - oldScale) < 1e-6) return;

                // Determine mouse position in viewport coordinates
                var mouseVp = e.GetPosition(MapScrollViewer);
                double hp = (MapScrollViewer?.HorizontalOffset ?? 0);
                double vp = (MapScrollViewer?.VerticalOffset ?? 0);

                // Content coordinate under cursor before zoom
                double contentX = hp + mouseVp.X;
                double contentY = vp + mouseVp.Y;

                // map world coordinate (tile-space) under cursor
                double mapX = (contentX - mapViewportPadding) / (TileSize * oldScale);
                double mapY = (contentY - mapViewportPadding) / (TileSize * oldScale);

                // Record zoom anchor so CommitZoom can preserve the point under the cursor
                try { zoomAnchorViewportX = mouseVp.X; zoomAnchorViewportY = mouseVp.Y; zoomAnchorMapX = mapX; zoomAnchorMapY = mapY; hasZoomAnchor = true; } catch { hasZoomAnchor = false; }

                // apply new zoom value
                if (ZoomSlider != null) ZoomSlider.Value = newScale;

                // compute new content coordinate for same world point
                double newContentX = mapViewportPadding + mapX * TileSize * newScale;
                double newContentY = mapViewportPadding + mapY * TileSize * newScale;

                // compute new scroll offsets so the same content point appears under the cursor
                double newH = newContentX - mouseVp.X;
                double newV = newContentY - mouseVp.Y;

                // clamp offsets to valid ranges
                double maxH = Math.Max(0, (CanvasHost?.ActualWidth ?? 0) - SafeViewportWidth());
                double maxV = Math.Max(0, (CanvasHost?.ActualHeight ?? 0) - SafeViewportHeight());
                newH = Math.Max(0, Math.Min(maxH, newH));
                newV = Math.Max(0, Math.Min(maxV, newV));

                // apply offsets
                if (MapScrollViewer != null)
                {
                    MapScrollViewer?.ScrollToHorizontalOffset(newH);
                    MapScrollViewer?.ScrollToVerticalOffset(newV);
                }

                // Ensure we don't allow scrolling past the bottom of the ground after zoom
                ClampScrollOffsets();

                // Defer heavy rebuild until wheel has been idle for 1s
                try { deferZoomRebuild = true; UpdateQuickZoomTransform(); } catch { }
                try { zoomCommitTimer?.Stop(); zoomCommitTimer?.Start(); } catch { }
                return;
            }
            
            // Shift+Wheel and No-modifier Wheel behavior may be swapped per user preference
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            {
                e.Handled = true;
                double scrollAmount = e.Delta * 0.5; // Scale down for smoother scrolling
                if (!swapMouseWheelScroll)
                {
                    // Default: Shift+Wheel = Vertical scrolling
                    double newOffset = (MapScrollViewer?.VerticalOffset ?? 0) - scrollAmount;
                    MapScrollViewer?.ScrollToVerticalOffset(newOffset);
                }
                else
                {
                    // Swapped: Shift+Wheel = Horizontal scrolling
                    double newHorizontalOffset = (MapScrollViewer?.HorizontalOffset ?? 0) - scrollAmount;
                    MapScrollViewer?.ScrollToHorizontalOffset(newHorizontalOffset);
                }
                return;
            }

            // No modifiers (Wheel alone) behavior
            e.Handled = true;
            double baseScrollAmount = e.Delta * 0.5; // Scale down for smoother scrolling
            if (!swapMouseWheelScroll)
            {
                // Default: Wheel alone = Horizontal scrolling
                double newHorizontalOffset = (MapScrollViewer?.HorizontalOffset ?? 0) - baseScrollAmount;
                MapScrollViewer?.ScrollToHorizontalOffset(newHorizontalOffset);
            }
            else
            {
                // Swapped: Wheel alone = Vertical scrolling
                double newOffset = (MapScrollViewer?.VerticalOffset ?? 0) - baseScrollAmount;
                MapScrollViewer?.ScrollToVerticalOffset(newOffset);
            }
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
            var dlg = new ColorPickerWindow(initialBgTint, allowAlpha: false, forcedIndex: (backgroundTint.A == 0 ? 1 : -1)) { Owner = this };
            dlg.Title = "Pick Background Tint (RGBA)";
            Action<Color> handler = (c) => { backgroundTint = c; UpdateParallaxTint(); Dispatcher.BeginInvoke(new Action(Redraw)); };
            dlg.ColorChanged += handler;
            var result = dlg.ShowDialog();
            if (result == true)
            {
                backgroundTint = dlg.SelectedColor;
                UpdateParallaxTint();
                Redraw();

                // Auto-save config when tint is changed (TMX-specific)
                if (!string.IsNullOrEmpty(currentFilePath))
                {
                    SaveTmxConfig(currentFilePath);
                }

                // Persist as editor default if user checked 'Set as default'
                if (dlg.SetAsDefault)
                {
                    try { SaveSettingsWithTriggerOption(); } catch { }
                }

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
            var dlg = new ColorPickerWindow(initialGroundTint, allowAlpha: false, forcedIndex: (groundTint.A == 0 ? 29 : -1)) { Owner = this };
            dlg.Title = "Pick Ground Tint (RGBA)";
            Action<Color> handler = (c) => { groundTint = c; UpdateGroundTint(); Dispatcher.BeginInvoke(new Action(Redraw)); };
            dlg.ColorChanged += handler;
            var result = dlg.ShowDialog();
            if (result == true)
            {
                groundTint = dlg.SelectedColor;
                UpdateGroundTint();
                Redraw();

                // Auto-save config when tint is changed (TMX-specific)
                if (!string.IsNullOrEmpty(currentFilePath))
                {
                    SaveTmxConfig(currentFilePath);
                }

                // Persist as editor default if user checked 'Set as default'
                if (dlg.SetAsDefault)
                {
                    try { SaveSettingsWithTriggerOption(); } catch { }
                }

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

                // Auto-save config when tint is changed (TMX-specific)
                if (!string.IsNullOrEmpty(currentFilePath))
                {
                    SaveTmxConfig(currentFilePath);
                }

                // Persist as editor default if user checked 'Set as default'
                if (dlg.SetAsDefault)
                {
                    try { SaveSettingsWithTriggerOption(); } catch { }
                }

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

        private void PlayerTintButton_Click(object? sender, RoutedEventArgs e)
        {
            var initial = playerTint;
            // Use the preset 16x4 palette without alpha for player color
            var dlg = new ColorPickerWindow(initial, allowAlpha: false) { Owner = this };
            dlg.Title = "Pick Player Color (tints decorations)";

            // Live preview while dialog open (match bg/ground/tile tint behavior)
            var initialColor = playerTint;
            var initialEnabled = playerTintEnabled;
            Action<Color> handler = (c) => {
                playerTint = c;
                playerTintEnabled = true;
                // Always refresh palette preview and scaled tile caches so the left palette updates
                try { scaledTileCaches.Clear(); } catch { }
                try { PopulateTilesPanel(); } catch { }

                // Only clear tinted sprite caches and rebuild sprites when in preview mode.
                // When not in preview mode, rebuilding sprites on every hover causes visible
                // flicker — avoid that and only apply sprite cache changes when the user
                // confirms the color (or when preview mode is active).
                if (previewMode)
                {
                    try { ClearTintedCaches(); } catch { }
                    try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                    try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
                }
                else
                {
                    // Update tiles only (lighter), avoid touching sprite caches to prevent flicker
                    try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                }
            };
            dlg.ColorChanged += handler;
            var res = dlg.ShowDialog();
            dlg.ColorChanged -= handler;

            if (res == true)
            {
                // Confirm selection: ensure enabled and persist only if requested
                playerTint = dlg.SelectedColor;
                playerTintEnabled = true;

                // Clear tinted caches so new tint takes effect immediately
                ClearTintedCaches();

                if (dlg.SetAsDefault)
                {
                    try { SaveSettingsWithTriggerOption(); } catch { }
                }

                Redraw();
            }
            else
            {
                // Revert to initial values
                playerTint = initialColor;
                playerTintEnabled = initialEnabled;
                // No need to clear caches when cancelling (tint unchanged)
                Redraw();
            }
        }

        private void StartPreviewTimer()
        {
            if (previewTimer == null)
            {
                // Timer at reasonable rate, but we'll throttle actual animation updates
                previewTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0)
                };
                previewTimer.Tick += PreviewTimer_Tick;
            }
            animationFrame = 0;
            timerTicks = 0;
            previewTimer.Start();
        }

        private void StopPreviewTimer()
        {
            previewTimer?.Stop();
        }

        // Compute the visible tile rectangle in map coordinates (inclusive)
        private void GetVisibleTileBounds(out int minX, out int maxX, out int minY, out int maxY)
        {
            minX = 0; maxX = Math.Max(0, mapWidth - 1); minY = 0; maxY = Math.Max(0, mapHeight - 1);
            try
            {
                if (MapScrollViewer == null) return;
                double scale = ZoomSlider?.Value ?? 1.0;
                double pad = mapViewportPadding;
                double viewLeft = (MapScrollViewer?.HorizontalOffset ?? 0);
                double viewTop = (MapScrollViewer?.VerticalOffset ?? 0);
                double viewRight = viewLeft + SafeViewportWidth();
                double viewBottom = viewTop + SafeViewportHeight();

                int lx = (int)Math.Floor((viewLeft - pad) / (TileSize * scale));
                int rx = (int)Math.Floor((viewRight - pad) / (TileSize * scale));
                int ty = (int)Math.Floor((viewTop - pad) / (TileSize * scale));
                int by = (int)Math.Floor((viewBottom - pad) / (TileSize * scale));

                minX = Math.Max(0, Math.Min(mapWidth - 1, lx));
                maxX = Math.Max(0, Math.Min(mapWidth - 1, rx));
                minY = Math.Max(0, Math.Min(mapHeight - 1, ty));
                maxY = Math.Max(0, Math.Min(mapHeight - 1, by));
            }
            catch { }
        }

        private void PreviewTimer_Tick(object? sender, EventArgs e)
        {
            timerTicks++;
            
            // Only update visuals every 2nd tick at 1x zoom to slow down animation
            // At higher zooms, update every tick since rendering is slower
            double currentZoom = (ZoomSlider != null ? ZoomSlider.Value : 1.0);
            int skipFrames = (currentZoom <= 1.0) ? 2 : 1;
            
            // Skip rendering on non-update ticks, but don't increment animation frame
            if (timerTicks % skipFrames != 0)
            {
                return; // Skip rendering this tick
            }
            
            // Only increment animation frame when we actually render
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
                // Helper to compute visible tile bounds
                GetVisibleTileBounds(out _, out _, out _, out _);
                // Helper to compute visible tile bounds
                int visMinX = 0, visMaxX = mapWidth - 1, visMinY = 0, visMaxY = mapHeight - 1;
                GetVisibleTileBounds(out visMinX, out visMaxX, out visMinY, out visMaxY);
                // Find visible tile bounds and update only saw tiles inside the viewport
                GetVisibleTileBounds(out int sMinX, out int sMaxX, out int sMinY, out int sMaxY);
                // Find and update only the saw tiles in the visible region
                bool hasSaws = false;
                for (int yy = sMinY; yy <= sMaxY && !hasSaws; yy++)
                {
                    for (int xx = sMinX; xx <= sMaxX; xx++)
                    {
                        int tileIdx = tiles[yy * mapWidth + xx];
                        if ((tileIdx >= 0x08 && tileIdx <= 0x0B) || tileIdx == 0x04 || tileIdx == 0x7D || tileIdx == 0x7F || (tileIdx >= 0x74 && tileIdx <= 0x7C))
                        {
                            hasSaws = true; break;
                        }
                    }
                }

                if (hasSaws)
                {
                    // Only update saw tiles inside visible bounds
                    double scale = (ZoomSlider != null ? ZoomSlider.Value : 1.0);
                    var dpi = VisualTreeHelper.GetDpi(this);
                    int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                    int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));

                    tilesWb.Lock();
                    try
                    {
                        for (int y = sMinY; y <= sMaxY; y++)
                        {
                            for (int x = sMinX; x <= sMaxX; x++)
                            {
                                int tileIdx = tiles[y * mapWidth + x];
                                if ((tileIdx >= 0x08 && tileIdx <= 0x0B) || tileIdx == 0x04 || tileIdx == 0x7D || tileIdx == 0x7F || (tileIdx >= 0x74 && tileIdx <= 0x7C))
                                {
                                    UpdateTileBitmapAtLocked(x, y, scale, mapViewportPadding, tilePixelW, tilePixelH, dpi);
                                }
                            }
                        }

                        // Compute dirty rect for visible area
                        int padPxX = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
                        int padPxY = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
                        int leftPx = Math.Max(0, padPxX + sMinX * tilePixelW);
                        int topPx = Math.Max(0, padPxY + sMinY * tilePixelH);
                        int wPx = Math.Min(cachedPixelWidth - leftPx, (sMaxX - sMinX + 1) * tilePixelW);
                        int hPx = Math.Min(cachedPixelHeight - topPx, (sMaxY - sMinY + 1) * tilePixelH);
                        tilesWb.AddDirtyRect(new Int32Rect(leftPx, topPx, Math.Max(1, wPx), Math.Max(1, hPx)));
                    }
                    finally
                    {
                        tilesWb.Unlock();
                    }
                }
            }
            
            // Update animated orb sprites (all colors) and preview-only red pad
            bool anyOrbFramesAvailable =
                (yellowOrbFrame1 != null && yellowOrbFrame2 != null && yellowOrbFrame3 != null && yellowOrbFrame4 != null) ||
                (blueOrbFrame1 != null && blueOrbFrame2 != null && blueOrbFrame3 != null && blueOrbFrame4 != null) ||
                (pinkOrbFrame1 != null && pinkOrbFrame2 != null && pinkOrbFrame3 != null && pinkOrbFrame4 != null) ||
                (greenOrbFrame1 != null && greenOrbFrame2 != null && greenOrbFrame3 != null && greenOrbFrame4 != null) ||
                (redOrbFrame1 != null && redOrbFrame2 != null && redOrbFrame3 != null && redOrbFrame4 != null) ||
                (blackOrbFrame1 != null && blackOrbFrame2 != null && blackOrbFrame3 != null && blackOrbFrame4 != null) ||
                (redPadFrame1 != null && redPadFrame2 != null && redPadFrame3 != null && redPadFrame4 != null) ||
                (whiteOrbFrame1 != null && whiteOrbFrame2 != null && whiteOrbFrame3 != null && whiteOrbFrame4 != null) ||
                (coinFrame1 != null && coinFrame2 != null && coinFrame3 != null && coinFrame4 != null) ||
                // Any of the two-frame dash orb sets present
                (dashOrbRightFrame1 != null && dashOrbRightFrame2 != null) ||
                (dashGravityOrbRightFrame1 != null && dashGravityOrbRightFrame2 != null) ||
                (dashOrb45UpFrame1 != null && dashOrb45UpFrame2 != null) ||
                (dashGravityOrb45UpFrame1 != null && dashGravityOrb45UpFrame2 != null) ||
                (dashOrb45DownFrame1 != null && dashOrb45DownFrame2 != null) ||
                (dashGravityOrb45DownFrame1 != null && dashGravityOrb45DownFrame2 != null) ||
                (dashOrbUpFrame1 != null && dashOrbUpFrame2 != null) ||
                (dashGravityOrbUpFrame1 != null && dashGravityOrbUpFrame2 != null) ||
                (dashOrbDownFrame1 != null && dashOrbDownFrame2 != null) ||
                (dashGravityOrbDownFrame1 != null && dashGravityOrbDownFrame2 != null) ||
                // Teleport orb two-frame sets
                (teleportOrbEnterFrame1 != null && teleportOrbEnterFrame2 != null) ||
                (teleportOrbExitFrame1 != null && teleportOrbExitFrame2 != null) ||
                // Spider orb two-frame sets
                (spiderOrbDownFrame1 != null && spiderOrbDownFrame2 != null) ||
                (spiderOrbUpFrame1 != null && spiderOrbUpFrame2 != null);

            // Include star two-frame preview frames and additional decorations
            anyOrbFramesAvailable = anyOrbFramesAvailable || (starFrame1 != null && starFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (pulsingBallFrame1 != null && pulsingBallFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (musicNoteFrame1 != null && musicNoteFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (diamondFrame1 != null && diamondFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (diamondHalfFrame1 != null && diamondHalfFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (questionMarkFrame1 != null && questionMarkFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (exclamationFrame1 != null && exclamationFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (xFrame1 != null && xFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (poleShortFrame1 != null && poleShortFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (poleShortUpsideDownFrame1 != null && poleShortUpsideDownFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (poleMediumFrame1 != null && poleMediumFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (poleMediumUpsideDownFrame1 != null && poleMediumUpsideDownFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (poleLongFrame1 != null && poleLongFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (poleLongUpsideDownFrame1 != null && poleLongUpsideDownFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (poleLeftShortFrame1 != null && poleLeftShortFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (poleLeftMediumFrame1 != null && poleLeftMediumFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (poleRightShortFrame1 != null && poleRightShortFrame2 != null);
            anyOrbFramesAvailable = anyOrbFramesAvailable || (poleRightMediumFrame1 != null && poleRightMediumFrame2 != null);

            if (spritesWb != null && anyOrbFramesAvailable)
            {
                // Check if we have any animated orb sprites (fast scan)
                // Determine visible tile bounds
                GetVisibleTileBounds(out int vMinX, out int vMaxX, out int vMinY, out int vMaxY);

                // Check if we have any animated orb sprites in the visible area (fast scan)
                bool hasAnimatedOrbs = false;
                        for (int yy = vMinY; yy <= vMaxY && !hasAnimatedOrbs; yy++)
                {
                    for (int xx = vMinX; xx <= vMaxX; xx++)
                    {
                        int spriteIdx = sprites[yy * mapWidth + xx];
                        // Animated two-frame sprites and decorations that participate in preview animation
                        if (spriteIdx == 0x0B || spriteIdx == 0x1F || spriteIdx == 0x29 || // Yellow
                            spriteIdx == 0x05 || // Blue
                            spriteIdx == 0x06 || // Pink
                            spriteIdx == 0x27 || // Green
                            spriteIdx == 0x28 || // Red
                            spriteIdx == 0x44 || // Black
                            spriteIdx == 0x7B || spriteIdx == 0x7C || // 0x7B/0x7C mimic blue/green orb behavior
                            spriteIdx == 0x7A || // White
                            spriteIdx == 0x07 || spriteIdx == 0x1A || spriteIdx == 0x1B || // Coins
                            spriteIdx == 0x52 || spriteIdx == 0x53 || // Red pad down/up
                            spriteIdx == 0x0A || spriteIdx == 0x0C || // Yellow pad down/up
                            spriteIdx == 0x0D || spriteIdx == 0x0E || spriteIdx == 0xFD || spriteIdx == 0xFE || // Blue pad down/up (+ aliases)
                            spriteIdx == 0x25 || spriteIdx == 0x26 || // Pink pad down/up
                            // Dash/teleport/spider two-frame sprites
                            spriteIdx == 0x45 || spriteIdx == 0x46 || spriteIdx == 0x4C || spriteIdx == 0x4D || spriteIdx == 0x50 || spriteIdx == 0x51 || spriteIdx == 0x5B || spriteIdx == 0x5C || spriteIdx == 0x5D || spriteIdx == 0x5E || spriteIdx == 0x59 || spriteIdx == 0x5A || spriteIdx == 0x54 || spriteIdx == 0x55 ||
                            // Decorations (including new pulsing/music decorations)
                            spriteIdx == 0x36 || spriteIdx == 0x32 || spriteIdx == 0x33 || spriteIdx == 0x34 || spriteIdx == 0x35 || spriteIdx == 0x37 || spriteIdx == 0x2C || spriteIdx == 0x3C || spriteIdx == 0x38 || spriteIdx == 0x39 || spriteIdx == 0x3E || spriteIdx == 0x3F || spriteIdx == 0x2B || spriteIdx == 0x3B || spriteIdx == 0x2A || spriteIdx == 0x3A || spriteIdx == 0x49 || spriteIdx == 0x4A)
                        {
                            hasAnimatedOrbs = true;
                            if (animationFrame % 60 == 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"Found animated orb sprite 0x{spriteIdx:X2} at ({xx},{yy}), will rebuild sprites");
                            }
                            break;
                        }
                    }
                }

                if (hasAnimatedOrbs)
                {
                    // Only update animated orb sprites in the visible region
                    double scale = (ZoomSlider != null ? ZoomSlider.Value : 1.0);
                    var dpi = VisualTreeHelper.GetDpi(this);
                    int spritePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                    int spritePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));

                    spritesWb.Lock();
                    try
                    {
                        for (int y = vMinY; y <= vMaxY; y++)
                        {
                            for (int x = vMinX; x <= vMaxX; x++)
                            {
                                int spriteIdx = sprites[y * mapWidth + x];
                                if (spriteIdx == 0x0B || spriteIdx == 0x1F || spriteIdx == 0x29 || // Yellow
                                    spriteIdx == 0x05 || // Blue
                                    spriteIdx == 0x06 || // Pink
                                    spriteIdx == 0x27 || // Green
                                    spriteIdx == 0x28 || // Red
                                    spriteIdx == 0x44 || // Black
                                    spriteIdx == 0x7B || spriteIdx == 0x7C || // 0x7B/0x7C mimic blue/green orb behavior
                                        spriteIdx == 0x52 || // Red pad (preview-only)
                                    spriteIdx == 0x53 || // Red pad up
                                    spriteIdx == 0x0A || // Yellow pad down
                                    spriteIdx == 0x0C || // Yellow pad up
                                    spriteIdx == 0x0D || // Blue pad down
                                    spriteIdx == 0x0E || // Blue pad up
                                    spriteIdx == 0xFD || // Blue pad down (alias)
                                    spriteIdx == 0xFE || // Blue pad up   (alias)
                                    spriteIdx == 0x25 || // Pink pad down
                                        spriteIdx == 0x26 || // Pink pad up
                                        spriteIdx == 0x7A || // White orb
                                        spriteIdx == 0x07 || // Coin types
                                        spriteIdx == 0x1A ||
                                        spriteIdx == 0x1B || // Coin types
                                        // New dash orb sprites
                                        spriteIdx == 0x45 || spriteIdx == 0x46 || spriteIdx == 0x4C || spriteIdx == 0x4D || spriteIdx == 0x50 || spriteIdx == 0x51 || spriteIdx == 0x5B || spriteIdx == 0x5C || spriteIdx == 0x5D || spriteIdx == 0x5E || spriteIdx == 0x59 || spriteIdx == 0x5A || spriteIdx == 0x54 || spriteIdx == 0x55 ||
                                        // Decorations (including pulsing ball 0x49 and music note 0x4A)
                                        spriteIdx == 0x36 || spriteIdx == 0x32 || spriteIdx == 0x33 || spriteIdx == 0x34 || spriteIdx == 0x35 || spriteIdx == 0x37 || spriteIdx == 0x2C || spriteIdx == 0x3C || spriteIdx == 0x38 || spriteIdx == 0x39 || spriteIdx == 0x3E || spriteIdx == 0x3F || spriteIdx == 0x2B || spriteIdx == 0x3B || spriteIdx == 0x2A || spriteIdx == 0x3A || spriteIdx == 0x49 || spriteIdx == 0x4A)
                                {
                                    UpdateSpriteBitmapAtLocked(x, y, spriteIdx, scale, mapViewportPadding, spritePixelW, spritePixelH, dpi);
                                }
                            }
                        }

                        // Compute dirty rect for visible area
                        int padPxX = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
                        int padPxY = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
                        int leftPx = Math.Max(0, padPxX + vMinX * spritePixelW);
                        int topPx = Math.Max(0, padPxY + vMinY * spritePixelH);
                        int wPx = Math.Min(cachedPixelWidth - leftPx, (vMaxX - vMinX + 1) * spritePixelW);
                        int hPx = Math.Min(cachedPixelHeight - topPx, (vMaxY - vMinY + 1) * spritePixelH);
                        spritesWb.AddDirtyRect(new Int32Rect(leftPx, topPx, Math.Max(1, wPx), Math.Max(1, hPx)));
                    }
                    finally
                    {
                        spritesWb.Unlock();
                    }
                }
            
            // Also update visible portal regions so animated portals (including 0x64 rainbow)
            // advance each preview tick instead of only when sprites change.
            if (previewMode && portalsWb != null)
            {
                GetVisibleTileBounds(out int pMinX, out int pMaxX, out int pMinY, out int pMaxY);
                bool hasPortals = false;
                for (int yy = pMinY; yy <= pMaxY && !hasPortals; yy++)
                {
                    for (int xx = pMinX; xx <= pMaxX; xx++)
                    {
                        int sidx = sprites[yy * mapWidth + xx];
                        if (IsPortalSprite(sidx)) { hasPortals = true; break; }
                    }
                }

                if (hasPortals)
                {
                    // Rebuild a slightly expanded region to cover multi-tile portals
                    QueueRebuildPortalsRegion(Math.Max(0, pMinX - 1), Math.Max(0, pMinY - 2), Math.Min(mapWidth - 1, pMaxX + 1), Math.Min(mapHeight - 1, pMaxY + 2));
                }
            }
            }
            else
            {
                // Debug: Why aren't we checking orbs?
                    if (animationFrame % 120 == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Not checking orbs: spritesWb={(spritesWb != null)}, yellow={(yellowOrbFrame1 != null)}, blue={(blueOrbFrame1 != null)}, pink={(pinkOrbFrame1 != null)}, green={(greenOrbFrame1 != null)}, red={(redOrbFrame1 != null)}, black={(blackOrbFrame1 != null)}, white={(whiteOrbFrame1 != null)}, coin={(coinFrame1 != null)}");
                }
            }
        }

        // Map a tile index to its animated version based on current animation frame
        // Saw tiles (0x08-0x0B, 0x74-0x7C, 0x04, 0x7D, 0x7F) animate using custom frames
        // Returns special indices (>= 1000) to indicate custom animation tiles
        private int GetAnimatedTileIndex(int originalIndex)
        {
            if (!previewMode) return originalIndex;

            // Preview remaps: map special preview-only tile indices to existing tiles so they render identically
            int mapped = originalIndex;
            switch (originalIndex)
            {
                case 0xE0: mapped = 0x30; break;
                case 0xE1: mapped = 0x24; break;
                case 0xE2: mapped = 0x28; break;
                case 0xE4: mapped = 0x32; break;
                case 0xE5: mapped = 0x25; break;
                case 0xE6:
                case 0xE7: mapped = 0x10; break;
                case 0xDD:
                case 0xDE: mapped = 0x10; break;
                case 0xD9:
                case 0xDA: mapped = 0x11; break;
                case 0xDB:
                case 0xDC: mapped = 0x1B; break;
                case 0xFD: mapped = 0x26; break;
            }
            if (mapped != originalIndex) originalIndex = mapped;
            
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
        
        // Check if a sprite ID is a portal sprite (in preview mode, these become 2x3 multi-tile sprites)
        private bool IsPortalSprite(int spriteIdx)
        {
            // Includes original portals plus newly added portal sprite IDs
                 return spriteIdx == 0x00 || spriteIdx == 0x01 || spriteIdx == 0x02 ||
                     spriteIdx == 0x03 || spriteIdx == 0x04 || spriteIdx == 0x24 ||
                     spriteIdx == 0x17 || spriteIdx == 0x18 || spriteIdx == 0x19 || spriteIdx == 0x4B || spriteIdx == 0x58 ||
                     spriteIdx == 0x08 || spriteIdx == 0x09 ||
                     // Rainbow portal (new): treat 0x64 as a portal for preview rendering
                     spriteIdx == 0x64 ||
                     // Teleport portal preview replacements
                     spriteIdx == 0x4E || spriteIdx == 0x4F ||
                     // Horizontal gravity portals
                     spriteIdx == 0x10 || spriteIdx == 0x11 || spriteIdx == 0x12 || spriteIdx == 0x13 ||
                             spriteIdx == 0x22 || spriteIdx == 0x23 ||
                         // Speed portal preview replacements
                         spriteIdx == 0x14 || spriteIdx == 0x15 || spriteIdx == 0x16 || spriteIdx == 0x20 || spriteIdx == 0x21 || spriteIdx == 0x6D ||
                         // Horizontal teleport portals
                         spriteIdx == 0x66 || spriteIdx == 0x67 || spriteIdx == 0x68 || spriteIdx == 0x69 ||
                     // Additional gravity-X portal sprite IDs (preview replacements)
                     spriteIdx == 0x5F || spriteIdx == 0x60 || spriteIdx == 0x61 || spriteIdx == 0x62 || spriteIdx == 0x63;
        }
        
        // Get the portal sprite bitmap for a given sprite ID
        // Optionally accepts a position key (y*mapWidth + x) so callers can request
        // a per-position deterministic variant (used for the rainbow portal 0x64).
        private BitmapSource? GetPortalSpriteForId(int spriteIdx, int positionKey = -1)
        {
            // Rainbow portal (0x64) cycles through a specific ordered list of portal images.
            if (spriteIdx == 0x64)
            {
                // Ordered list: cube, ship, ball, ufo, robot, wave, spider, swingcopter, ninja
                BitmapSource?[] order = new BitmapSource?[]
                {
                    cubePortalSprite,
                    shipPortalSprite,
                    ballPortalSprite,
                    ufoPortalSprite,
                    robotPortalSprite,
                    wavePortalSprite,
                    spiderPortalSprite,
                    swingcopterPortalSprite,
                    ninjaPortalSprite
                };

                // If positionKey not provided, fall back to cube portal
                if (positionKey < 0)
                {
                    return cubePortalSprite;
                }

                int len = order.Length;
                if (len == 0) return null;

                // Deterministic per-position start offset using Knuth multiplicative hash
                uint seed = (uint)positionKey;
                uint offset = (uint)((seed * 2654435761u) % (uint)len);

                // Advance based on global animationFrame so portals animate over time
                int frameAdvance = 0;
                try { frameAdvance = (animationFrame / 8) % len; } catch { frameAdvance = 0; }

                int idx = (int)((offset + (uint)frameAdvance) % (uint)len);
                return order[idx];
            }

            return spriteIdx switch
            {
                0x00 => cubePortalSprite,
                0x01 => shipPortalSprite,
                0x02 => ballPortalSprite,
                0x03 => ufoPortalSprite,
                0x04 => robotPortalSprite,
                0x24 => wavePortalSprite,
                0x17 => spiderPortalSprite,
                0x18 => miniPortalSprite,
                0x19 => growthPortalSprite,
                0x5F => gravity1ThirdXPortalSprite,
                0x60 => gravity1HalfXPortalSprite,
                0x61 => gravity2ThirdXPortalSprite,
                0x62 => gravity2XPortalSprite,
                0x63 => gravity1XPortalSprite,
                0x4B => swingcopterPortalSprite,
                0x66 => teleportPortalHorizontalEnterDownSprite,
                0x67 => teleportPortalHorizontalExitUpSprite,
                0x68 => teleportPortalHorizontalEnterUpSprite,
                0x69 => teleportPortalHorizontalExitDownSprite,
                0x08 => gravityDownPortalSprite,
                0x09 => gravityUpPortalSprite,
                0x58 => ninjaPortalSprite,
                0x4E => teleportPortalEnterSprite,
                0x4F => teleportPortalExitSprite,
                0x14 => speed05xPortalSprite,
                0x15 => speed1xPortalSprite,
                0x16 => speed2xPortalSprite,
                0x20 => speed3xPortalSprite,
                0x21 => speed4xPortalSprite,
                0x6D => speedSpecialPortalSprite,
                0x10 => gravityDownDownwardsPortalSprite,
                0x11 => gravityDownUpwardsPortalSprite,
                0x12 => gravityUpDownwardsPortalSprite,
                0x13 => gravityUpUpwardsPortalSprite,
                0x22 => dualPortalSprite,
                0x23 => singlePortalSprite,
                _ => null
            };
        }
        
        // Returns special indices (>= 2000) to indicate custom animation sprites
        // Orbs have 4-frame animation: yellow (0x0B, 0x1F, 0x29), blue (0x05), pink (0x06), 
        // green (0x27), red (0x28), black (0x44)
        // Portals: cube (0x00), ship (0x01), ball (0x02), ufo (0x03), robot (0x04), wave (0x24)
        // return 3000-3010 to indicate multi-tile portal sprites (24x48 - 1.5x3 tiles each)
        private int GetAnimatedSpriteIndex(int originalIndex)
        {
            if (!previewMode) return originalIndex;

            // Preview aliasing: treat these non-orb sprites as orbs in preview mode
            if (originalIndex == 0x7B) originalIndex = 0x05; // behave like blue orb
            if (originalIndex == 0x7C) originalIndex = 0x27; // behave like green orb
            
            // Check if this is a portal sprite
            if (originalIndex == 0x00) return 3000; // Cube portal
            // Treat 0x64 as the rainbow portal which should be rendered using the
            // same sizing as standard tall portals (map it to 3000 for sizing).
            if (originalIndex == 0x64) return 3000; // Rainbow portal (cycles through portal images)
            if (originalIndex == 0x01) return 3001; // Ship portal
            if (originalIndex == 0x02) return 3002; // Ball portal
            if (originalIndex == 0x03) return 3003; // UFO portal
            if (originalIndex == 0x04) return 3004; // Robot portal
            if (originalIndex == 0x24) return 3005; // Wave portal
            if (originalIndex == 0x17)
            {
                return 3006; // Spider portal
            }
            if (originalIndex == 0x18)
            {
                return 3017; // Mini portal (new)
            }
            if (originalIndex == 0x19)
            {
                return 3018; // Growth portal (new)
            }
            // Teleport portal preview replacements (non-animated, tall portals)
            if (originalIndex == 0x4E) return 3000; // teleport-portal-enter
            if (originalIndex == 0x4F) return 3000; // teleport-portal-exit
            // Spider pad preview replacements (single-frame, non-animated)
            if (originalIndex == 0x56) return 2152; // spider-pad (0x56)
            if (originalIndex == 0x57) return 2153; // spider-pad-upsidedown (0x57)
            // Horizontal teleport portal preview replacements (3 tiles wide x 1.5 tiles tall)
            if (originalIndex == 0x66) return 3030; // teleport-portal-horizontal-enter-downwards
            if (originalIndex == 0x67) return 3031; // teleport-portal-horizontal-exit-upwards (shift up 1 tile)
            if (originalIndex == 0x68) return 3032; // teleport-portal-horizontal-enter-upwards (shift up 1 tile)
            if (originalIndex == 0x69) return 3033; // teleport-portal-horizontal-exit-downwards
            if (originalIndex == 0x4B)
            {
                return 3007; // Swingcopter portal (0x4B)
            }
            if (originalIndex == 0x58)
            {
                return 3008; // Ninja portal
            }
            // Speed portal preview mappings: treat as tall portals (1.5x3)
            if (originalIndex == 0x14) return 3024; // speed-05x
            if (originalIndex == 0x15) return 3025; // speed-1x
            if (originalIndex == 0x16) return 3026; // speed-2x
            if (originalIndex == 0x20) return 3027; // speed-3x
            if (originalIndex == 0x21) return 3028; // speed-4x
            if (originalIndex == 0x6D) return 3029; // speed-special
            if (originalIndex == 0x08)
            {
                return 3009; // Gravity down portal
            }
            if (originalIndex == 0x09)
            {
                return 3010; // Gravity up portal
            }
            // Horizontal gravity portals (new)
            if (originalIndex == 0x10)
            {
                return 3011; // Gravity down (downwards) horizontal portal
            }
            if (originalIndex == 0x11)
            {
                return 3012; // Gravity down (upwards) horizontal portal
            }
            if (originalIndex == 0x12)
            {
                return 3013; // Gravity up (downwards) horizontal portal
            }
            if (originalIndex == 0x13)
            {
                return 3014; // Gravity up (upwards) horizontal portal
            }
            if (originalIndex == 0x22)
            {
                return 3015; // Dual portal (new)
            }
            if (originalIndex == 0x23)
            {
                return 3016; // Single portal (new)
            }
            // Additional gravity-strength X-axis portals (preview-only)
            if (originalIndex == 0x5F) return 3019; // gravity 1/3
            if (originalIndex == 0x60) return 3020; // gravity 1/2
            if (originalIndex == 0x61) return 3021; // gravity 2/3
            if (originalIndex == 0x62) return 3022; // gravity 2x
            if (originalIndex == 0x63) return 3023; // gravity 1x
            // Teleport orb 2-frame animations (use the same deterministic two-frame timing as dash orbs)
            if (originalIndex == 0x59) return GetTwoFrameCustomIndex(2100); // teleport enter (2100/2101)
            if (originalIndex == 0x5A) return GetTwoFrameCustomIndex(2102); // teleport exit  (2102/2103)
            // Spider orb 2-frame animations (use same two-frame timing)
            if (originalIndex == 0x54) return GetTwoFrameCustomIndex(2106); // spider orb upwards   (2106/2107)
            if (originalIndex == 0x55) return GetTwoFrameCustomIndex(2104); // spider orb downwards (2104/2105)
            // Star decoration (two-frame preview) - sprite 0x36
            // Decorations two-frame preview mapping
            if (originalIndex == 0x32) return GetTwoFrameCustomIndex(2112); // diamond (2112/2113)
            if (originalIndex == 0x33) return GetTwoFrameCustomIndex(2114); // diamond half (2114/2115)
            if (originalIndex == 0x34) return GetTwoFrameCustomIndex(2116); // question mark (2116/2117)
            if (originalIndex == 0x35) return GetTwoFrameCustomIndex(2118); // exclamation (2118/2119)
            if (originalIndex == 0x37) return GetTwoFrameCustomIndex(2120); // x decoration (2120/2121)
            if (originalIndex == 0x2C) return GetTwoFrameCustomIndex(2122); // pole short (2122/2123)
            if (originalIndex == 0x3C) return GetTwoFrameCustomIndex(2124); // pole short upside-down (2124/2125)
            if (originalIndex == 0x38) return GetTwoFrameCustomIndex(2128); // pole-left-short (2128/2129)
            if (originalIndex == 0x39) return GetTwoFrameCustomIndex(2130); // pole-right-short (2130/2131)
            if (originalIndex == 0x3E) return GetTwoFrameCustomIndex(2132); // pole-left-medium (2132/2133)
            if (originalIndex == 0x3F) return GetTwoFrameCustomIndex(2134); // pole-right-medium (2134/2135)
            // New mapping: medium pole replacements for 0x2B/0x3B use custom indices 2136/2138
            if (originalIndex == 0x2B) return GetTwoFrameCustomIndex(2136); // pole-medium (2136/2137)
            if (originalIndex == 0x3B) return GetTwoFrameCustomIndex(2138); // pole-medium upside-down (2138/2139)
            if (originalIndex == 0x2A) return GetTwoFrameCustomIndex(2140); // pole-long (2140/2141)
            if (originalIndex == 0x3A) return GetTwoFrameCustomIndex(2142); // pole-long upside-down (2142/2143)
            if (originalIndex == 0x36) return GetTwoFrameCustomIndex(2110); // star (2110/2111)
            // New decorations mapping: pulsing ball (0x49) and music note (0x4A)
            if (originalIndex == 0x49) return GetTwoFrameCustomIndex(2144); // pulsing ball (2144/2145)
            if (originalIndex == 0x4A) return GetTwoFrameCustomIndex(2146); // music note (2146/2147)
            // Chain decorations (preview-only, single-frame)
            if (originalIndex == 0x2D) return 2126; // chain (2126)
            if (originalIndex == 0x3D) return 2127; // chain-upsidedown (2127)
            // Deco spikes (single-frame preview-only)
            if (originalIndex == 0x2E) return 2148; // deco-spikes (2148)
            if (originalIndex == 0x2F) return 2149; // deco-spikes-upsidedown (2149)
            if (originalIndex == 0x30) return 2150; // deco-spikes-small (2150)
            if (originalIndex == 0x31) return 2151; // deco-spikes-small-upsidedown (2151)
            if (originalIndex == 0x52)
            {
            }
            
            // Check if this is an animated orb sprite
            bool isYellowOrb = (originalIndex == 0x0B || originalIndex == 0x1F || originalIndex == 0x29);
            bool isBlueOrb = (originalIndex == 0x05);
            bool isPinkOrb = (originalIndex == 0x06);
            bool isGreenOrb = (originalIndex == 0x27);
            bool isRedOrb = (originalIndex == 0x28);
            bool isBlackOrb = (originalIndex == 0x44);
            bool isWhiteOrb = (originalIndex == 0x7A);
            // Coins: 0x07, 0x1A, 0x1B
            bool isCoin = (originalIndex == 0x07 || originalIndex == 0x1A || originalIndex == 0x1B);
            // Pads (preview-only): 0x52 red-pad-down, 0x53 red-pad-up, 0x0A yellow-pad-down, 0x0C yellow-pad-up,
            // 0x0D blue-pad-down, 0x0E blue-pad-up, 0x25 pink-pad-down, 0x26 pink-pad-up
            bool isPad = (originalIndex == 0x52 || originalIndex == 0x53 || originalIndex == 0x0A || originalIndex == 0x0C || originalIndex == 0x0D || originalIndex == 0x0E || originalIndex == 0x25 || originalIndex == 0x26 || originalIndex == 0xFD || originalIndex == 0xFE);
            
            if (isYellowOrb || isBlueOrb || isPinkOrb || isGreenOrb || isRedOrb || isBlackOrb || isPad || isWhiteOrb || isCoin)
            {
                // 4-frame animation at 9/20 speed (slower than saws)
                // Each sprite gets a random offset so they don't all sync
                int spritePositionKey = currentSpritePositionKey; // Set by UpdateSpriteBitmapAtLocked
                if (!spriteFrameOffsets.ContainsKey(spritePositionKey))
                {
                    int rnd = spriteAnimationRandom.Next(0, 4);
                    spriteFrameOffsets[spritePositionKey] = rnd;
                }
                int frameOffset = spriteFrameOffsets[spritePositionKey];
                
                // Calculate which frame (0-3) based on animation counter + random offset
                int frame = (((animationFrame * 9) / 20) + frameOffset) % 4;

                // Detailed debug for red pad to trace why frame may be constant
                if (isPad)
                {
                }
                
                // Determine which orb color and map to custom index range
                // Yellow: 2000-2011 (3 sprites × 4 frames)
                // Blue:   2012-2015 (1 sprite × 4 frames)
                // Pink:   2016-2019 (1 sprite × 4 frames)
                // Green:  2020-2023 (1 sprite × 4 frames)
                // Red:    2024-2027 (1 sprite × 4 frames)
                // Black:  2028-2031 (1 sprite × 4 frames)
                
                if (isYellowOrb)
                {
                    int spriteOffset = (originalIndex == 0x0B) ? 0 : (originalIndex == 0x1F) ? 1 : 2;
                    return 2000 + (frame * 3) + spriteOffset;
                }
                else if (isBlueOrb)
                {
                    return 2012 + frame;
                }
                else if (isPinkOrb)
                {
                    return 2016 + frame;
                }
                else if (isGreenOrb)
                {
                    return 2020 + frame;
                }
                else if (isRedOrb)
                {
                    return 2024 + frame;
                }
                else if (isBlackOrb)
                {
                    return 2028 + frame;
                }
                else if (isCoin)
                {
                    // Coins: 2064-2075 (3 sprites × 4 frames)
                    int spriteOffset = (originalIndex == 0x07) ? 0 : (originalIndex == 0x1A) ? 1 : 2;
                    return 2064 + (frame * 3) + spriteOffset;
                }
                else if (isWhiteOrb)
                {
                    // White orb: 2076-2079 (1 sprite × 4 frames)
                    return 2076 + frame;
                }
                else if (isPad)
                {
                    // Map each pad sprite to its own 4-frame custom range
                    int baseIndex = 2032; // default for 0x52
                    switch (originalIndex)
                    {
                        case 0x52: baseIndex = 2032; break; // red-pad-down (existing)
                        case 0x53: baseIndex = 2036; break; // red-pad-up
                        case 0x0A: baseIndex = 2040; break; // yellow-pad-down
                        case 0x0C: baseIndex = 2044; break; // yellow-pad-up
                        case 0x0D: baseIndex = 2048; break; // blue-pad-down
                        case 0x0E: baseIndex = 2052; break; // blue-pad-up
                        case 0xFD: baseIndex = 2048; break; // blue-pad-down (alias 0xFD)
                        case 0xFE: baseIndex = 2052; break; // blue-pad-up   (alias 0xFE)
                        case 0x25: baseIndex = 2056; break; // pink-pad-down
                        case 0x26: baseIndex = 2060; break; // pink-pad-up
                        default: baseIndex = 2032; break;
                    }
                    return baseIndex + frame;
                }
            }

            // New: Dash orb 2-frame slow animations (custom indices 2080-2099)
            // Sprite IDs:
            // 0x45 => 2080/2081
            // 0x46 => 2082/2083
            // 0x4C => 2084/2085
            // 0x4D => 2086/2087
            // 0x50 => 2088/2089
            // 0x51 => 2090/2091
            // 0x5B => 2092/2093
            // 0x5C => 2094/2095
            // 0x5D => 2096/2097
            // 0x5E => 2098/2099
            if (originalIndex == 0x45 || originalIndex == 0x46 || originalIndex == 0x4C || originalIndex == 0x4D || originalIndex == 0x50 || originalIndex == 0x51 || originalIndex == 0x5B || originalIndex == 0x5C || originalIndex == 0x5D || originalIndex == 0x5E)
            {
                int spriteBase = originalIndex switch
                {
                    0x45 => 2080,
                    0x46 => 2082,
                    0x4C => 2084,
                    0x4D => 2086,
                    0x50 => 2088,
                    0x51 => 2090,
                    0x5B => 2092,
                    0x5C => 2094,
                    0x5D => 2096,
                    0x5E => 2098,
                    _ => 2080
                };

                return GetTwoFrameCustomIndex(spriteBase);
            }
            
            return originalIndex; // Not an animated orb
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

        // Helper to compute deterministic two-frame custom animation index
        private int GetTwoFrameCustomIndex(int spriteBase)
        {
            // Slower rate: match the dash-orb timing used elsewhere
            int frame = (((animationFrame * 3) / 40) % 2 + 2) % 2;
            return spriteBase + frame;
        }
        
        // Get the custom orb animation sprite if index is >= 2000
        private BitmapSource? GetCustomAnimationSprite(int customIndex)
        {
            // (debug logging removed)
            // Portal sprites: 3000-3010 (24x48 multi-tile sprites - 1.5x3 tiles each)
            // 3000: Cube portal (0x00)
            // 3001: Ship portal (0x01)
            // 3002: Ball portal (0x02)
            // 3003: UFO portal (0x03)
            // 3004: Robot portal (0x04)
            // 3005: Wave portal (0x24)
            // Yellow orb sprites: 2000-2011 (3 sprites × 4 frames)
            // Blue orb: 2012-2015 (1 sprite × 4 frames)
            // Pink orb: 2016-2019 (1 sprite × 4 frames)
            // Green orb: 2020-2023 (1 sprite × 4 frames)
            // Red orb: 2024-2027 (1 sprite × 4 frames)
            // Black orb: 2028-2031 (1 sprite × 4 frames)
            
            if (customIndex == 3000) return cubePortalSprite;
            if (customIndex == 3001) return shipPortalSprite;
            if (customIndex == 3002) return ballPortalSprite;
            if (customIndex == 3003) return ufoPortalSprite;
            if (customIndex == 3004) return robotPortalSprite;
            if (customIndex == 3005) return wavePortalSprite;
            if (customIndex == 3006) return spiderPortalSprite;
            if (customIndex == 3007) return swingcopterPortalSprite;
            if (customIndex == 3008) return ninjaPortalSprite;
            if (customIndex == 3009) return gravityDownPortalSprite;
            if (customIndex == 3010) return gravityUpPortalSprite;
            // Horizontal teleport portals custom indices (3030-3033)
            if (customIndex == 3030) return teleportPortalHorizontalEnterDownSprite;
            if (customIndex == 3031) return teleportPortalHorizontalExitUpSprite;
            if (customIndex == 3032) return teleportPortalHorizontalEnterUpSprite;
            if (customIndex == 3033) return teleportPortalHorizontalExitDownSprite;
            // Spider pad static previews
            if (customIndex == 2152) return spiderPadSprite;
            if (customIndex == 2153) return spiderPadUpsideDownSprite;
            // Horizontal gravity portal custom indices
            if (customIndex == 3011) return gravityDownDownwardsPortalSprite;
            if (customIndex == 3012) return gravityDownUpwardsPortalSprite;
            if (customIndex == 3013) return gravityUpDownwardsPortalSprite;
            if (customIndex == 3014) return gravityUpUpwardsPortalSprite;
            // New larger portals
            if (customIndex == 3015) return dualPortalSprite;
            if (customIndex == 3016) return singlePortalSprite;
            // New mini/growth portal custom indices
            if (customIndex == 3017) return miniPortalSprite;
            if (customIndex == 3018) return growthPortalSprite;
            // New gravity-strength X-axis portal custom indices
            if (customIndex == 3019) return gravity1ThirdXPortalSprite;
            if (customIndex == 3020) return gravity1HalfXPortalSprite;
            if (customIndex == 3021) return gravity2ThirdXPortalSprite;
            if (customIndex == 3022) return gravity2XPortalSprite;
            if (customIndex == 3023) return gravity1XPortalSprite;
            // Speed portal custom indices
            if (customIndex == 3024) return speed05xPortalSprite;
            if (customIndex == 3025) return speed1xPortalSprite;
            if (customIndex == 3026) return speed2xPortalSprite;
            if (customIndex == 3027) return speed3xPortalSprite;
            if (customIndex == 3028) return speed4xPortalSprite;
            if (customIndex == 3029) return speedSpecialPortalSprite;
            
            if (customIndex >= 2000 && customIndex <= 2011)
            {
                // Yellow orbs
                int frameAndSpriteIndex = customIndex - 2000; // 0-11
                int frame = frameAndSpriteIndex / 3; // 0-3 (which frame)
                int spriteOffset = frameAndSpriteIndex % 3; // 0-2 (which sprite: 0x0B, 0x1F, 0x29)
                
                BitmapSource[]? frameArray = frame switch
                {
                    0 => yellowOrbFrame1,
                    1 => yellowOrbFrame2,
                    2 => yellowOrbFrame3,
                    3 => yellowOrbFrame4,
                    _ => null
                };

                // (debug logging removed)

                if (frameArray != null && spriteOffset < frameArray.Length)
                {
                    return frameArray[spriteOffset];
                }
            }
            else if (customIndex >= 2012 && customIndex <= 2015)
            {
                // Blue orb
                int frame = customIndex - 2012; // 0-3
                return frame switch
                {
                    0 => blueOrbFrame1?[0],
                    1 => blueOrbFrame2?[0],
                    2 => blueOrbFrame3?[0],
                    3 => blueOrbFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2016 && customIndex <= 2019)
            {
                // Pink orb
                int frame = customIndex - 2016; // 0-3
                return frame switch
                {
                    0 => pinkOrbFrame1?[0],
                    1 => pinkOrbFrame2?[0],
                    2 => pinkOrbFrame3?[0],
                    3 => pinkOrbFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2020 && customIndex <= 2023)
            {
                // Green orb
                int frame = customIndex - 2020; // 0-3
                return frame switch
                {
                    0 => greenOrbFrame1?[0],
                    1 => greenOrbFrame2?[0],
                    2 => greenOrbFrame3?[0],
                    3 => greenOrbFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2024 && customIndex <= 2027)
            {
                // Red orb
                int frame = customIndex - 2024; // 0-3
                return frame switch
                {
                    0 => redOrbFrame1?[0],
                    1 => redOrbFrame2?[0],
                    2 => redOrbFrame3?[0],
                    3 => redOrbFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2028 && customIndex <= 2031)
            {
                // Black orb
                int frame = customIndex - 2028; // 0-3
                return frame switch
                {
                    0 => blackOrbFrame1?[0],
                    1 => blackOrbFrame2?[0],
                    2 => blackOrbFrame3?[0],
                    3 => blackOrbFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2032 && customIndex <= 2035)
            {
                // Red pad (preview-only)
                int frame = customIndex - 2032; // 0-3
                return frame switch
                {
                    0 => redPadFrame1?[0],
                    1 => redPadFrame2?[0],
                    2 => redPadFrame3?[0],
                    3 => redPadFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2036 && customIndex <= 2039)
            {
                // Red pad (up)
                int frame = customIndex - 2036;
                return frame switch
                {
                    0 => redPadUpFrame1?[0],
                    1 => redPadUpFrame2?[0],
                    2 => redPadUpFrame3?[0],
                    3 => redPadUpFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2040 && customIndex <= 2043)
            {
                // Yellow pad (down)
                int frame = customIndex - 2040;
                return frame switch
                {
                    0 => yellowPadDownFrame1?[0],
                    1 => yellowPadDownFrame2?[0],
                    2 => yellowPadDownFrame3?[0],
                    3 => yellowPadDownFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2044 && customIndex <= 2047)
            {
                // Yellow pad (up)
                int frame = customIndex - 2044;
                return frame switch
                {
                    0 => yellowPadUpFrame1?[0],
                    1 => yellowPadUpFrame2?[0],
                    2 => yellowPadUpFrame3?[0],
                    3 => yellowPadUpFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2048 && customIndex <= 2051)
            {
                // Blue pad (down)
                int frame = customIndex - 2048;
                return frame switch
                {
                    0 => bluePadDownFrame1?[0],
                    1 => bluePadDownFrame2?[0],
                    2 => bluePadDownFrame3?[0],
                    3 => bluePadDownFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2052 && customIndex <= 2055)
            {
                // Blue pad (up)
                int frame = customIndex - 2052;
                return frame switch
                {
                    0 => bluePadUpFrame1?[0],
                    1 => bluePadUpFrame2?[0],
                    2 => bluePadUpFrame3?[0],
                    3 => bluePadUpFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2056 && customIndex <= 2059)
            {
                // Pink pad (down)
                int frame = customIndex - 2056;
                return frame switch
                {
                    0 => pinkPadDownFrame1?[0],
                    1 => pinkPadDownFrame2?[0],
                    2 => pinkPadDownFrame3?[0],
                    3 => pinkPadDownFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2060 && customIndex <= 2063)
            {
                // Pink pad (up)
                int frame = customIndex - 2060;
                return frame switch
                {
                    0 => pinkPadUpFrame1?[0],
                    1 => pinkPadUpFrame2?[0],
                    2 => pinkPadUpFrame3?[0],
                    3 => pinkPadUpFrame4?[0],
                    _ => null
                };
            }
            else if (customIndex >= 2064 && customIndex <= 2075)
            {
                // Coins: 3 sprites × 4 frames (2064-2075)
                int frameAndSpriteIndex = customIndex - 2064; // 0-11
                int frame = frameAndSpriteIndex / 3; // 0-3
                int spriteOffset = frameAndSpriteIndex % 3; // 0-2 (which coin sprite)

                BitmapSource[]? frameArray = frame switch
                {
                    0 => coinFrame1,
                    1 => coinFrame2,
                    2 => coinFrame3,
                    3 => coinFrame4,
                    _ => null
                };

                if (frameArray != null && spriteOffset < frameArray.Length)
                {
                    return frameArray[spriteOffset];
                }
            }
            else if (customIndex >= 2076 && customIndex <= 2079)
            {
                // White orb (single sprite)
                int frame = customIndex - 2076; // 0-3
                return frame switch
                {
                    0 => whiteOrbFrame1?[0],
                    1 => whiteOrbFrame2?[0],
                    2 => whiteOrbFrame3?[0],
                    3 => whiteOrbFrame4?[0],
                    _ => null
                };
            }

            // Dash orb 2-frame slow animations: 2080-2099 (10 sprites × 2 frames)
            if (customIndex >= 2080 && customIndex <= 2099)
            {
                switch (customIndex)
                {
                    case 2080: return dashOrbRightFrame1?[0];
                    case 2081: return dashOrbRightFrame2?[0];
                    case 2082: return dashGravityOrbRightFrame1?[0];
                    case 2083: return dashGravityOrbRightFrame2?[0];
                    case 2084: return dashOrb45UpFrame1?[0];
                    case 2085: return dashOrb45UpFrame2?[0];
                    case 2086: return dashGravityOrb45UpFrame1?[0];
                    case 2087: return dashGravityOrb45UpFrame2?[0];
                    case 2088: return dashOrb45DownFrame1?[0];
                    case 2089: return dashOrb45DownFrame2?[0];
                    case 2090: return dashGravityOrb45DownFrame1?[0];
                    case 2091: return dashGravityOrb45DownFrame2?[0];
                    case 2092: return dashOrbUpFrame1?[0];
                    case 2093: return dashOrbUpFrame2?[0];
                    case 2094: return dashGravityOrbUpFrame1?[0];
                    case 2095: return dashGravityOrbUpFrame2?[0];
                    case 2096: return dashOrbDownFrame1?[0];
                    case 2097: return dashOrbDownFrame2?[0];
                    case 2098: return dashGravityOrbDownFrame1?[0];
                    case 2099: return dashGravityOrbDownFrame2?[0];
                    default: return null;
                }
            }

            // Teleport orb 2-frame animations: 2100-2103 (2 sprites × 2 frames)
            if (customIndex >= 2100 && customIndex <= 2103)
            {
                switch (customIndex)
                {
                    case 2100: return teleportOrbEnterFrame1?[0];
                    case 2101: return teleportOrbEnterFrame2?[0];
                    case 2102: return teleportOrbExitFrame1?[0];
                    case 2103: return teleportOrbExitFrame2?[0];
                    default: return null;
                }
            }
            // Spider orb 2-frame animations: 2104-2107 (2 sprites × 2 frames)
            if (customIndex >= 2104 && customIndex <= 2107)
            {
                switch (customIndex)
                {
                    case 2104: return spiderOrbDownFrame1?[0];
                    case 2105: return spiderOrbDownFrame2?[0];
                    case 2106: return spiderOrbUpFrame1?[0];
                    case 2107: return spiderOrbUpFrame2?[0];
                    default: return null;
                }
            }
            // Star 2-frame preview: 2110-2111 (single decoration sprite)
            if (customIndex >= 2110 && customIndex <= 2111)
            {
                switch (customIndex)
                {
                    case 2110: return starFrame1?[0];
                    case 2111: return starFrame2?[0];
                    default: return null;
                }
            }
            // Diamond decoration: 2112-2113 (sprite 0x32)
            if (customIndex >= 2112 && customIndex <= 2113)
            {
                switch (customIndex)
                {
                    case 2112: return diamondFrame1?[0];
                    case 2113: return diamondFrame2?[0];
                    default: return null;
                }
            }
            // Diamond half decoration: 2114-2115 (sprite 0x33)
            if (customIndex >= 2114 && customIndex <= 2115)
            {
                switch (customIndex)
                {
                    case 2114: return diamondHalfFrame1?[0];
                    case 2115: return diamondHalfFrame2?[0];
                    default: return null;
                }
            }
            // Question mark decoration: 2116-2117 (sprite 0x34)
            if (customIndex >= 2116 && customIndex <= 2117)
            {
                switch (customIndex)
                {
                    case 2116: return questionMarkFrame1?[0];
                    case 2117: return questionMarkFrame2?[0];
                    default: return null;
                }
            }
            // Exclamation decoration: 2118-2119 (sprite 0x35)
            if (customIndex >= 2118 && customIndex <= 2119)
            {
                switch (customIndex)
                {
                    case 2118: return exclamationFrame1?[0];
                    case 2119: return exclamationFrame2?[0];
                    default: return null;
                }
            }
            // X decoration: 2120-2121 (sprite 0x37)
            if (customIndex >= 2120 && customIndex <= 2121)
            {
                switch (customIndex)
                {
                    case 2120: return xFrame1?[0];
                    case 2121: return xFrame2?[0];
                    default: return null;
                }
            }
            // Pole short: 2122-2123 (sprite 0x2C)
            if (customIndex >= 2122 && customIndex <= 2123)
            {
                switch (customIndex)
                {
                    case 2122: return poleShortFrame1?[0];
                    case 2123: return poleShortFrame2?[0];
                    default: return null;
                }
            }
            // Pole left short: 2128-2129 (sprite 0x38)
            if (customIndex >= 2128 && customIndex <= 2129)
            {
                switch (customIndex)
                {
                    case 2128: return poleLeftShortFrame1?[0];
                    case 2129: return poleLeftShortFrame2?[0];
                    default: return null;
                }
            }
            // Pole left medium: 2132-2133 (sprite 0x3E)
            if (customIndex >= 2132 && customIndex <= 2133)
            {
                switch (customIndex)
                {
                    case 2132: return poleLeftMediumFrame1?[0];
                    case 2133: return poleLeftMediumFrame2?[0];
                    default: return null;
                }
            }
            // Pole right short: 2130-2131 (sprite 0x39)
            if (customIndex >= 2130 && customIndex <= 2131)
            {
                switch (customIndex)
                {
                    case 2130: return poleRightShortFrame1?[0];
                    case 2131: return poleRightShortFrame2?[0];
                    default: return null;
                }
            }
            // Pole right medium: 2134-2135 (sprite 0x3F)
            if (customIndex >= 2134 && customIndex <= 2135)
            {
                switch (customIndex)
                {
                    case 2134: return poleRightMediumFrame1?[0];
                    case 2135: return poleRightMediumFrame2?[0];
                    default: return null;
                }
            }
            // Pole short upside-down: 2124-2125 (sprite 0x3C)
            if (customIndex >= 2124 && customIndex <= 2125)
            {
                switch (customIndex)
                {
                    case 2124: return poleShortUpsideDownFrame1?[0];
                    case 2125: return poleShortUpsideDownFrame2?[0];
                    default: return null;
                }
            }

            // Pole-long custom indices for 0x2A/0x3A: 2140-2143
            if (customIndex >= 2140 && customIndex <= 2141)
            {
                switch (customIndex)
                {
                    case 2140: return poleLongFrame1?[0];
                    case 2141: return poleLongFrame2?[0];
                    default: return null;
                }
            }
            if (customIndex >= 2142 && customIndex <= 2143)
            {
                switch (customIndex)
                {
                    case 2142: return poleLongUpsideDownFrame1?[0];
                    case 2143: return poleLongUpsideDownFrame2?[0];
                    default: return null;
                }
            }

            // Pulsing ball two-frame preview: 2144-2145 (sprite 0x49)
            if (customIndex >= 2144 && customIndex <= 2145)
            {
                switch (customIndex)
                {
                    case 2144: return pulsingBallFrame1?[0];
                    case 2145: return pulsingBallFrame2?[0];
                    default: return null;
                }
            }

            // Music note two-frame preview: 2146-2147 (sprite 0x4A)
            if (customIndex >= 2146 && customIndex <= 2147)
            {
                switch (customIndex)
                {
                    case 2146: return musicNoteFrame1?[0];
                    case 2147: return musicNoteFrame2?[0];
                    default: return null;
                }
            }

            // Pole-medium custom indices for 0x2B/0x3B: 2136-2139
            if (customIndex >= 2136 && customIndex <= 2137)
            {
                switch (customIndex)
                {
                    case 2136: return poleMediumFrame1?[0];
                    case 2137: return poleMediumFrame2?[0];
                    default: return null;
                }
            }
            if (customIndex >= 2138 && customIndex <= 2139)
            {
                switch (customIndex)
                {
                    case 2138: return poleMediumUpsideDownFrame1?[0];
                    case 2139: return poleMediumUpsideDownFrame2?[0];
                    default: return null;
                }
            }
            // Chain single-frame decoration: 2126 (sprite 0x2D)
            if (customIndex == 2126)
            {
                return chainFrame1?[0];
            }
            // Chain upside-down single-frame decoration: 2127 (sprite 0x3D)
            if (customIndex == 2127)
            {
                return chainUpsideDownFrame1?[0];
            }
            // Deco spikes single-frame decorations
            if (customIndex == 2148) { return decoSpikesFrame1?[0]; }
            if (customIndex == 2149) { return decoSpikesUpsideDownFrame1?[0]; }
            if (customIndex == 2150) { return decoSpikesSmallFrame1?[0]; }
            if (customIndex == 2151) { return decoSpikesSmallUpsideDownFrame1?[0]; }
            
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

        // Load portal sprites for preview mode (multi-tile replacements)
        private void InitializePortalSprites()
        {
            try
            {
                // Helper function to load a portal sprite
                BitmapSource? LoadPortalSprite(string filename)
                {
                    string foundPath = "embedded";
                    var portal = LoadEmbeddedImage(filename);
                    
                    if (portal == null)
                    {
                        var baseDir = AppContext.BaseDirectory;
                        var portalPath = System.IO.Path.Combine(baseDir, filename);
                        
                        var repo = FindRepoRootFor("famidash.bmp");
                        if (!string.IsNullOrEmpty(repo))
                        {
                            var repoPortal = System.IO.Path.Combine(repo, filename);
                            if (System.IO.File.Exists(repoPortal)) portalPath = repoPortal;
                        }
                            // If the portal wasn't found yet, also check the user's editor-dev folder (suggested by user)
                            var editorDevPath = System.IO.Path.Combine("C:", "editor-dev", filename);

                            if (!System.IO.File.Exists(portalPath) && System.IO.File.Exists(editorDevPath)) portalPath = editorDevPath;

                            if (System.IO.File.Exists(portalPath))
                        {
                            portal = new BitmapImage();
                            portal.BeginInit();
                            portal.CacheOption = BitmapCacheOption.OnLoad;
                            portal.UriSource = new Uri(portalPath);
                            portal.EndInit();
                            portal.Freeze();
                            foundPath = portalPath;
                        }
                        else
                        {
                        }
                    }
                    
                    if (portal != null)
                    {
                        var converted = new FormatConvertedBitmap(portal, PixelFormats.Pbgra32, null, 0);
                        System.Diagnostics.Debug.WriteLine($"✓ Loaded {filename}: {portal.PixelWidth}x{portal.PixelHeight}");
                        return converted;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"✗ {filename} not found");
                        return null;
                    }
                }
                
                // Load all portal sprites (24x48 - 1.5x3 tiles each)
                cubePortalSprite = LoadPortalSprite("cube-portal.png");
                shipPortalSprite = LoadPortalSprite("ship-portal.png");
                ballPortalSprite = LoadPortalSprite("ball-portal.png");
                ufoPortalSprite = LoadPortalSprite("ufo-portal.png");
                robotPortalSprite = LoadPortalSprite("robot-portal.png");
                wavePortalSprite = LoadPortalSprite("wave-portal.png");
                // New portal sprites
                spiderPortalSprite = LoadPortalSprite("spider-portal.png");
                    // New mini and growth portal preview images (placed in workspace root)
                    miniPortalSprite = LoadPortalSprite("mini-portal.png");
                    growthPortalSprite = LoadPortalSprite("growth-portal.png");
                    // Teleport portal preview replacements (single-frame PNGs)
                    teleportPortalEnterSprite = LoadPortalSprite("teleport-portal-enter.png");
                    teleportPortalExitSprite = LoadPortalSprite("teleport-portal-exit.png");
                        // Spider pad preview sprites (single-tile, non-animated)
                        spiderPadSprite = LoadPortalSprite("spider-pad.png");
                        spiderPadUpsideDownSprite = LoadPortalSprite("spider-pad-upsidedown.png");
                    // Horizontal teleport portal preview assets (3 tiles wide x 1.5 tiles tall)
                    teleportPortalHorizontalEnterDownSprite = LoadPortalSprite("teleport-portal-horizontal-enter-downwards.png");
                    teleportPortalHorizontalExitUpSprite = LoadPortalSprite("teleport-portal-horizontal-exit-upwards.png");
                    teleportPortalHorizontalEnterUpSprite = LoadPortalSprite("teleport-portal-horizontal-enter-upwards.png");
                    teleportPortalHorizontalExitDownSprite = LoadPortalSprite("teleport-portal-horizontal-exit-downwards.png");
                // Speed portal preview images
                speed05xPortalSprite = LoadPortalSprite("speed-05x.png");
                speed1xPortalSprite = LoadPortalSprite("speed-1x.png");
                speed2xPortalSprite = LoadPortalSprite("speed-2x.png");
                speed3xPortalSprite = LoadPortalSprite("speed-3x.png");
                speed4xPortalSprite = LoadPortalSprite("speed-4x.png");
                speedSpecialPortalSprite = LoadPortalSprite("speed-special.png");
                // Additional gravity-strength X-axis portals
                    gravity1ThirdXPortalSprite = LoadPortalSprite("gravity-1-3rd-x-portal.png");
                    gravity1HalfXPortalSprite = LoadPortalSprite("gravity-1-half-x-portal.png");
                    gravity2ThirdXPortalSprite = LoadPortalSprite("gravity-2-3rd-x-portal.png");
                    gravity2XPortalSprite = LoadPortalSprite("gravity-2x-portal.png");
                    gravity1XPortalSprite = LoadPortalSprite("gravity-1x-portal.png");
                swingcopterPortalSprite = LoadPortalSprite("swingcopter-portal.png");
                ninjaPortalSprite = LoadPortalSprite("ninja-portal.png");
                // Gravity portals
                gravityDownPortalSprite = LoadPortalSprite("gravity-down-portal.png");
                gravityUpPortalSprite = LoadPortalSprite("gravity-up-portal.png");
                // Horizontal gravity portals
                gravityDownDownwardsPortalSprite = LoadPortalSprite("gravity-down-downwards-portal.png");
                gravityDownUpwardsPortalSprite = LoadPortalSprite("gravity-down-upwards-portal.png");
                gravityUpDownwardsPortalSprite = LoadPortalSprite("gravity-up-downwards-portal.png");
                gravityUpUpwardsPortalSprite = LoadPortalSprite("gravity-up-upwards-portal.png");
                // New larger portals
                dualPortalSprite = LoadPortalSprite("dual-portal.png");
                singlePortalSprite = LoadPortalSprite("single-portal.png");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load portal sprites: {ex.Message}");
            }
        }

        // Load yellow orb animation frames from PNG files
        // Yellow orbs are sprites 0x0B, 0x1F, 0x29 with 4 animation frames each
        private void InitializeYellowOrbAnimationFrames()
        {
            try
            {
                // Try to load from embedded resources first
                var frame1 = LoadEmbeddedImage("yellow-orb-frame1.png");
                var frame2 = LoadEmbeddedImage("yellow-orb-frame2.png");
                var frame3 = LoadEmbeddedImage("yellow-orb-frame3.png");
                var frame4 = LoadEmbeddedImage("yellow-orb-frame4.png");
                
                // If not found in embedded resources, try file system
                if (frame1 == null || frame2 == null || frame3 == null || frame4 == null)
                {
                    var baseDir = AppContext.BaseDirectory;
                    var frame1Path = System.IO.Path.Combine(baseDir, "yellow-orb-frame1.png");
                    var frame2Path = System.IO.Path.Combine(baseDir, "yellow-orb-frame2.png");
                    var frame3Path = System.IO.Path.Combine(baseDir, "yellow-orb-frame3.png");
                    var frame4Path = System.IO.Path.Combine(baseDir, "yellow-orb-frame4.png");
                    
                    var repo = FindRepoRootFor("famidash.bmp");
                    if (!string.IsNullOrEmpty(repo))
                    {
                        var repoFrame1 = System.IO.Path.Combine(repo, "yellow-orb-frame1.png");
                        var repoFrame2 = System.IO.Path.Combine(repo, "yellow-orb-frame2.png");
                        var repoFrame3 = System.IO.Path.Combine(repo, "yellow-orb-frame3.png");
                        var repoFrame4 = System.IO.Path.Combine(repo, "yellow-orb-frame4.png");
                        
                        if (System.IO.File.Exists(repoFrame1)) frame1Path = repoFrame1;
                        if (System.IO.File.Exists(repoFrame2)) frame2Path = repoFrame2;
                        if (System.IO.File.Exists(repoFrame3)) frame3Path = repoFrame3;
                        if (System.IO.File.Exists(repoFrame4)) frame4Path = repoFrame4;
                    }
                    
                    if (System.IO.File.Exists(frame1Path))
                    {
                        frame1 = new BitmapImage();
                        frame1.BeginInit();
                        frame1.CacheOption = BitmapCacheOption.OnLoad;
                        frame1.UriSource = new Uri(frame1Path);
                        frame1.EndInit();
                        frame1.Freeze();
                    }
                    if (System.IO.File.Exists(frame2Path))
                    {
                        frame2 = new BitmapImage();
                        frame2.BeginInit();
                        frame2.CacheOption = BitmapCacheOption.OnLoad;
                        frame2.UriSource = new Uri(frame2Path);
                        frame2.EndInit();
                        frame2.Freeze();
                    }
                    if (System.IO.File.Exists(frame3Path))
                    {
                        frame3 = new BitmapImage();
                        frame3.BeginInit();
                        frame3.CacheOption = BitmapCacheOption.OnLoad;
                        frame3.UriSource = new Uri(frame3Path);
                        frame3.EndInit();
                        frame3.Freeze();
                    }
                    if (System.IO.File.Exists(frame4Path))
                    {
                        frame4 = new BitmapImage();
                        frame4.BeginInit();
                        frame4.CacheOption = BitmapCacheOption.OnLoad;
                        frame4.UriSource = new Uri(frame4Path);
                        frame4.EndInit();
                        frame4.Freeze();
                    }
                }
                
                if (frame1 != null && frame2 != null && frame3 != null && frame4 != null)
                {
                    // Store each frame (each frame is 16x16 pixels for 3 sprite types)
                    // Convert to Pbgra32 format to match the sprite rendering expectations
                    var convertedFrame1 = new FormatConvertedBitmap(frame1, PixelFormats.Pbgra32, null, 0);
                    var convertedFrame2 = new FormatConvertedBitmap(frame2, PixelFormats.Pbgra32, null, 0);
                    var convertedFrame3 = new FormatConvertedBitmap(frame3, PixelFormats.Pbgra32, null, 0);
                    var convertedFrame4 = new FormatConvertedBitmap(frame4, PixelFormats.Pbgra32, null, 0);
                    
                    // We'll use the same image for all 3 yellow orb sprites (0x0B, 0x1F, 0x29)
                    yellowOrbFrame1 = new BitmapSource[3];
                    yellowOrbFrame2 = new BitmapSource[3];
                    yellowOrbFrame3 = new BitmapSource[3];
                    yellowOrbFrame4 = new BitmapSource[3];
                    
                    // All 3 sprites share the same animation frames
                    for (int i = 0; i < 3; i++)
                    {
                        yellowOrbFrame1[i] = convertedFrame1;
                        yellowOrbFrame2[i] = convertedFrame2;
                        yellowOrbFrame3[i] = convertedFrame3;
                        yellowOrbFrame4[i] = convertedFrame4;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load yellow orb animation frames: {ex.Message}");
            }
        }

        // Helper function to load orb animation frames
        private void LoadOrbFrames(string colorName, ref BitmapSource[]? frame1, ref BitmapSource[]? frame2, 
                                     ref BitmapSource[]? frame3, ref BitmapSource[]? frame4, int arraySize = 1)
        {
            try
            {
                var f1 = LoadEmbeddedImage($"{colorName}-orb-frame1.png");
                var f2 = LoadEmbeddedImage($"{colorName}-orb-frame2.png");
                var f3 = LoadEmbeddedImage($"{colorName}-orb-frame3.png");
                var f4 = LoadEmbeddedImage($"{colorName}-orb-frame4.png");
                
                if (f1 != null && f2 != null && f3 != null && f4 != null)
                {
                    // Convert to Pbgra32 format to match sprite rendering expectations
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);
                    var converted3 = new FormatConvertedBitmap(f3, PixelFormats.Pbgra32, null, 0);
                    var converted4 = new FormatConvertedBitmap(f4, PixelFormats.Pbgra32, null, 0);
                    
                    frame1 = new BitmapSource[arraySize];
                    frame2 = new BitmapSource[arraySize];
                    frame3 = new BitmapSource[arraySize];
                    frame4 = new BitmapSource[arraySize];
                    
                    for (int i = 0; i < arraySize; i++)
                    {
                        frame1[i] = converted1;
                        frame2[i] = converted2;
                        frame3[i] = converted3;
                        frame4[i] = converted4;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"✓ Loaded {colorName} orb animation frames (4 frames)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✗ {colorName} orb frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load {colorName} orb animation frames: {ex.Message}");
            }
        }

        // Load coin frames (non-orb naming: coin-frame1.png..coin-frame4.png)
        private void LoadCoinFrames(ref BitmapSource[]? frame1, ref BitmapSource[]? frame2, ref BitmapSource[]? frame3, ref BitmapSource[]? frame4)
        {
            try
            {
                var f1 = LoadEmbeddedImage("coin-frame1.png");
                var f2 = LoadEmbeddedImage("coin-frame2.png");
                var f3 = LoadEmbeddedImage("coin-frame3.png");
                var f4 = LoadEmbeddedImage("coin-frame4.png");

                if (f1 != null && f2 != null && f3 != null && f4 != null)
                {
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);
                    var converted3 = new FormatConvertedBitmap(f3, PixelFormats.Pbgra32, null, 0);
                    var converted4 = new FormatConvertedBitmap(f4, PixelFormats.Pbgra32, null, 0);

                    // Coin frames are shared by 3 coin sprite IDs
                    frame1 = new BitmapSource[3];
                    frame2 = new BitmapSource[3];
                    frame3 = new BitmapSource[3];
                    frame4 = new BitmapSource[3];

                    for (int i = 0; i < 3; i++)
                    {
                        frame1[i] = converted1;
                        frame2[i] = converted2;
                        frame3[i] = converted3;
                        frame4[i] = converted4;
                    }

                    System.Diagnostics.Debug.WriteLine("✓ Loaded coin animation frames (4 frames)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ coin frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load coin animation frames: {ex.Message}");
            }
        }

        private void InitializeWhiteOrbAnimationFrames()
        {
            LoadOrbFrames("white", ref whiteOrbFrame1, ref whiteOrbFrame2, ref whiteOrbFrame3, ref whiteOrbFrame4, 1);
        }

        private void InitializeCoinAnimationFrames()
        {
            LoadCoinFrames(ref coinFrame1, ref coinFrame2, ref coinFrame3, ref coinFrame4);
        }

        private void InitializeBlueOrbAnimationFrames()
        {
            LoadOrbFrames("blue", ref blueOrbFrame1, ref blueOrbFrame2, ref blueOrbFrame3, ref blueOrbFrame4);
        }

        private void InitializePinkOrbAnimationFrames()
        {
            LoadOrbFrames("pink", ref pinkOrbFrame1, ref pinkOrbFrame2, ref pinkOrbFrame3, ref pinkOrbFrame4);
        }

        private void InitializeGreenOrbAnimationFrames()
        {
            LoadOrbFrames("green", ref greenOrbFrame1, ref greenOrbFrame2, ref greenOrbFrame3, ref greenOrbFrame4);
        }

        private void InitializeRedOrbAnimationFrames()
        {
            LoadOrbFrames("red", ref redOrbFrame1, ref redOrbFrame2, ref redOrbFrame3, ref redOrbFrame4);
        }

        private void InitializeRedPadAnimationFrames()
        {
            try
            {
                var f1 = LoadEmbeddedImage("red-pad-down-frame1.png");
                var f2 = LoadEmbeddedImage("red-pad-down-frame2.png");
                var f3 = LoadEmbeddedImage("red-pad-down-frame3.png");
                var f4 = LoadEmbeddedImage("red-pad-down-frame4.png");

                if (f1 != null && f2 != null && f3 != null && f4 != null)
                {
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);
                    var converted3 = new FormatConvertedBitmap(f3, PixelFormats.Pbgra32, null, 0);
                    var converted4 = new FormatConvertedBitmap(f4, PixelFormats.Pbgra32, null, 0);

                    redPadFrame1 = new BitmapSource[1];
                    redPadFrame2 = new BitmapSource[1];
                    redPadFrame3 = new BitmapSource[1];
                    redPadFrame4 = new BitmapSource[1];

                    redPadFrame1[0] = converted1;
                    redPadFrame2[0] = converted2;
                    redPadFrame3[0] = converted3;
                    redPadFrame4[0] = converted4;

                    System.Diagnostics.Debug.WriteLine("✓ Loaded red pad animation frames (4 frames)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ red pad frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load red pad animation frames: {ex.Message}");
            }
        }

        private void InitializeRedPadUpAnimationFrames()
        {
            try
            {
                var f1 = LoadEmbeddedImage("red-pad-up-frame1.png");
                var f2 = LoadEmbeddedImage("red-pad-up-frame2.png");
                var f3 = LoadEmbeddedImage("red-pad-up-frame3.png");
                var f4 = LoadEmbeddedImage("red-pad-up-frame4.png");

                if (f1 != null && f2 != null && f3 != null && f4 != null)
                {
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);
                    var converted3 = new FormatConvertedBitmap(f3, PixelFormats.Pbgra32, null, 0);
                    var converted4 = new FormatConvertedBitmap(f4, PixelFormats.Pbgra32, null, 0);

                    redPadUpFrame1 = new BitmapSource[1];
                    redPadUpFrame2 = new BitmapSource[1];
                    redPadUpFrame3 = new BitmapSource[1];
                    redPadUpFrame4 = new BitmapSource[1];

                    redPadUpFrame1[0] = converted1;
                    redPadUpFrame2[0] = converted2;
                    redPadUpFrame3[0] = converted3;
                    redPadUpFrame4[0] = converted4;

                    System.Diagnostics.Debug.WriteLine("✓ Loaded red pad (up) animation frames (4 frames)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ red pad (up) frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load red pad (up) animation frames: {ex.Message}");
            }
        }

        private void InitializeYellowPadDownAnimationFrames()
        {
            try
            {
                var f1 = LoadEmbeddedImage("yellow-pad-down-frame1.png");
                var f2 = LoadEmbeddedImage("yellow-pad-down-frame2.png");
                var f3 = LoadEmbeddedImage("yellow-pad-down-frame3.png");
                var f4 = LoadEmbeddedImage("yellow-pad-down-frame4.png");

                if (f1 != null && f2 != null && f3 != null && f4 != null)
                {
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);
                    var converted3 = new FormatConvertedBitmap(f3, PixelFormats.Pbgra32, null, 0);
                    var converted4 = new FormatConvertedBitmap(f4, PixelFormats.Pbgra32, null, 0);

                    yellowPadDownFrame1 = new BitmapSource[1];
                    yellowPadDownFrame2 = new BitmapSource[1];
                    yellowPadDownFrame3 = new BitmapSource[1];
                    yellowPadDownFrame4 = new BitmapSource[1];

                    yellowPadDownFrame1[0] = converted1;
                    yellowPadDownFrame2[0] = converted2;
                    yellowPadDownFrame3[0] = converted3;
                    yellowPadDownFrame4[0] = converted4;

                    System.Diagnostics.Debug.WriteLine("✓ Loaded yellow pad (down) animation frames (4 frames)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ yellow pad (down) frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load yellow pad (down) animation frames: {ex.Message}");
            }
        }

        private void InitializeYellowPadUpAnimationFrames()
        {
            try
            {
                var f1 = LoadEmbeddedImage("yellow-pad-up-frame1.png");
                var f2 = LoadEmbeddedImage("yellow-pad-up-frame2.png");
                var f3 = LoadEmbeddedImage("yellow-pad-up-frame3.png");
                var f4 = LoadEmbeddedImage("yellow-pad-up-frame4.png");

                if (f1 != null && f2 != null && f3 != null && f4 != null)
                {
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);
                    var converted3 = new FormatConvertedBitmap(f3, PixelFormats.Pbgra32, null, 0);
                    var converted4 = new FormatConvertedBitmap(f4, PixelFormats.Pbgra32, null, 0);

                    yellowPadUpFrame1 = new BitmapSource[1];
                    yellowPadUpFrame2 = new BitmapSource[1];
                    yellowPadUpFrame3 = new BitmapSource[1];
                    yellowPadUpFrame4 = new BitmapSource[1];

                    yellowPadUpFrame1[0] = converted1;
                    yellowPadUpFrame2[0] = converted2;
                    yellowPadUpFrame3[0] = converted3;
                    yellowPadUpFrame4[0] = converted4;

                    System.Diagnostics.Debug.WriteLine("✓ Loaded yellow pad (up) animation frames (4 frames)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ yellow pad (up) frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load yellow pad (up) animation frames: {ex.Message}");
            }
        }

        private void InitializeBluePadDownAnimationFrames()
        {
            try
            {
                var f1 = LoadEmbeddedImage("blue-pad-down-frame1.png");
                var f2 = LoadEmbeddedImage("blue-pad-down-frame2.png");
                var f3 = LoadEmbeddedImage("blue-pad-down-frame3.png");
                var f4 = LoadEmbeddedImage("blue-pad-down-frame4.png");

                if (f1 != null && f2 != null && f3 != null && f4 != null)
                {
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);
                    var converted3 = new FormatConvertedBitmap(f3, PixelFormats.Pbgra32, null, 0);
                    var converted4 = new FormatConvertedBitmap(f4, PixelFormats.Pbgra32, null, 0);

                    bluePadDownFrame1 = new BitmapSource[1];
                    bluePadDownFrame2 = new BitmapSource[1];
                    bluePadDownFrame3 = new BitmapSource[1];
                    bluePadDownFrame4 = new BitmapSource[1];

                    bluePadDownFrame1[0] = converted1;
                    bluePadDownFrame2[0] = converted2;
                    bluePadDownFrame3[0] = converted3;
                    bluePadDownFrame4[0] = converted4;

                    System.Diagnostics.Debug.WriteLine("✓ Loaded blue pad (down) animation frames (4 frames)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ blue pad (down) frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load blue pad (down) animation frames: {ex.Message}");
            }
        }

        private void InitializeBluePadUpAnimationFrames()
        {
            try
            {
                var f1 = LoadEmbeddedImage("blue-pad-up-frame1.png");
                var f2 = LoadEmbeddedImage("blue-pad-up-frame2.png");
                var f3 = LoadEmbeddedImage("blue-pad-up-frame3.png");
                var f4 = LoadEmbeddedImage("blue-pad-up-frame4.png");

                if (f1 != null && f2 != null && f3 != null && f4 != null)
                {
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);
                    var converted3 = new FormatConvertedBitmap(f3, PixelFormats.Pbgra32, null, 0);
                    var converted4 = new FormatConvertedBitmap(f4, PixelFormats.Pbgra32, null, 0);

                    bluePadUpFrame1 = new BitmapSource[1];
                    bluePadUpFrame2 = new BitmapSource[1];
                    bluePadUpFrame3 = new BitmapSource[1];
                    bluePadUpFrame4 = new BitmapSource[1];

                    bluePadUpFrame1[0] = converted1;
                    bluePadUpFrame2[0] = converted2;
                    bluePadUpFrame3[0] = converted3;
                    bluePadUpFrame4[0] = converted4;

                    System.Diagnostics.Debug.WriteLine("✓ Loaded blue pad (up) animation frames (4 frames)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ blue pad (up) frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load blue pad (up) animation frames: {ex.Message}");
            }
        }

        private void InitializePinkPadDownAnimationFrames()
        {
            try
            {
                var f1 = LoadEmbeddedImage("pink-pad-down-frame1.png");
                var f2 = LoadEmbeddedImage("pink-pad-down-frame2.png");
                var f3 = LoadEmbeddedImage("pink-pad-down-frame3.png");
                var f4 = LoadEmbeddedImage("pink-pad-down-frame4.png");

                if (f1 != null && f2 != null && f3 != null && f4 != null)
                {
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);
                    var converted3 = new FormatConvertedBitmap(f3, PixelFormats.Pbgra32, null, 0);
                    var converted4 = new FormatConvertedBitmap(f4, PixelFormats.Pbgra32, null, 0);

                    pinkPadDownFrame1 = new BitmapSource[1];
                    pinkPadDownFrame2 = new BitmapSource[1];
                    pinkPadDownFrame3 = new BitmapSource[1];
                    pinkPadDownFrame4 = new BitmapSource[1];

                    pinkPadDownFrame1[0] = converted1;
                    pinkPadDownFrame2[0] = converted2;
                    pinkPadDownFrame3[0] = converted3;
                    pinkPadDownFrame4[0] = converted4;

                    System.Diagnostics.Debug.WriteLine("✓ Loaded pink pad (down) animation frames (4 frames)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ pink pad (down) frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load pink pad (down) animation frames: {ex.Message}");
            }
        }

        private void InitializePinkPadUpAnimationFrames()
        {
            try
            {
                var f1 = LoadEmbeddedImage("pink-pad-up-frame1.png");
                var f2 = LoadEmbeddedImage("pink-pad-up-frame2.png");
                var f3 = LoadEmbeddedImage("pink-pad-up-frame3.png");
                var f4 = LoadEmbeddedImage("pink-pad-up-frame4.png");

                if (f1 != null && f2 != null && f3 != null && f4 != null)
                {
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);
                    var converted3 = new FormatConvertedBitmap(f3, PixelFormats.Pbgra32, null, 0);
                    var converted4 = new FormatConvertedBitmap(f4, PixelFormats.Pbgra32, null, 0);

                    pinkPadUpFrame1 = new BitmapSource[1];
                    pinkPadUpFrame2 = new BitmapSource[1];
                    pinkPadUpFrame3 = new BitmapSource[1];
                    pinkPadUpFrame4 = new BitmapSource[1];

                    pinkPadUpFrame1[0] = converted1;
                    pinkPadUpFrame2[0] = converted2;
                    pinkPadUpFrame3[0] = converted3;
                    pinkPadUpFrame4[0] = converted4;

                    System.Diagnostics.Debug.WriteLine("✓ Loaded pink pad (up) animation frames (4 frames)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("✗ pink pad (up) frame files not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load pink pad (up) animation frames: {ex.Message}");
            }
        }

        private void InitializeBlackOrbAnimationFrames()
        {
            LoadOrbFrames("black", ref blackOrbFrame1, ref blackOrbFrame2, ref blackOrbFrame3, ref blackOrbFrame4);
        }

        // Helper to load two-frame orb variants (slower animations)
        private void LoadTwoFrameOrb(string baseName, ref BitmapSource[]? frame1, ref BitmapSource[]? frame2)
        {
            try
            {
                var f1 = LoadEmbeddedImage($"{baseName}-frame1.png");
                var f2 = LoadEmbeddedImage($"{baseName}-frame2.png");

                if (f1 != null && f2 != null)
                {
                    var converted1 = new FormatConvertedBitmap(f1, PixelFormats.Pbgra32, null, 0);
                    var converted2 = new FormatConvertedBitmap(f2, PixelFormats.Pbgra32, null, 0);

                    frame1 = new BitmapSource[1];
                    frame2 = new BitmapSource[1];
                    frame1[0] = converted1;
                    frame2[0] = converted2;

                    System.Diagnostics.Debug.WriteLine($"✓ Loaded two-frame orb: {baseName} (2 frames)");
                    return;
                }

                // Fallback to file system (repo root or base dir)
                var baseDir = AppContext.BaseDirectory;
                var p1 = Path.Combine(baseDir, $"{baseName}-frame1.png");
                var p2 = Path.Combine(baseDir, $"{baseName}-frame2.png");
                var repo = FindRepoRootFor("famidash.bmp");
                if (!string.IsNullOrEmpty(repo))
                {
                    var r1 = Path.Combine(repo, $"{baseName}-frame1.png");
                    var r2 = Path.Combine(repo, $"{baseName}-frame2.png");
                    if (File.Exists(r1)) p1 = r1;
                    if (File.Exists(r2)) p2 = r2;
                }

                if (File.Exists(p1) && File.Exists(p2))
                {
                    var bi1 = new BitmapImage(); bi1.BeginInit(); bi1.CacheOption = BitmapCacheOption.OnLoad; bi1.UriSource = new Uri(p1); bi1.EndInit(); bi1.Freeze();
                    var bi2 = new BitmapImage(); bi2.BeginInit(); bi2.CacheOption = BitmapCacheOption.OnLoad; bi2.UriSource = new Uri(p2); bi2.EndInit(); bi2.Freeze();

                    frame1 = new BitmapSource[1];
                    frame2 = new BitmapSource[1];
                    frame1[0] = new FormatConvertedBitmap(bi1, PixelFormats.Pbgra32, null, 0);
                    frame2[0] = new FormatConvertedBitmap(bi2, PixelFormats.Pbgra32, null, 0);

                    System.Diagnostics.Debug.WriteLine($"✓ Loaded two-frame orb from files: {baseName}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✗ Two-frame orb files not found: {baseName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load two-frame orb {baseName}: {ex.Message}");
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
                    // per-level: no parallax background
                    if (doc.RootElement.TryGetProperty("noParallaxBg", out var npb))
                    {
                        try { noParallaxBg = npb.GetBoolean(); } catch { noParallaxBg = false; }
                        if (MenuOptionNoParallax != null) MenuOptionNoParallax.IsChecked = noParallaxBg;
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
                    // optional legacy trigger offset
                    if (doc.RootElement.TryGetProperty("useLegacyTriggerOffset", out var lto))
                    {
                        useLegacyTriggerOffset = lto.GetBoolean();
                        if (MenuOptionLegacyTriggers != null)
                        {
                            MenuOptionLegacyTriggers.IsChecked = useLegacyTriggerOffset;
                        }
                    }
                    // optional hide color triggers setting
                    if (doc.RootElement.TryGetProperty("hideColorTriggers", out var hct))
                    {
                        try { hideColorTriggers = hct.GetBoolean(); } catch { hideColorTriggers = false; }
                        if (MenuOptionHideColorTriggers != null)
                        {
                            MenuOptionHideColorTriggers.IsChecked = hideColorTriggers;
                        }
                    }
                    // optional hide invisible sprites setting (global)
                    if (doc.RootElement.TryGetProperty("hideInvisibleSprites", out var his))
                    {
                        try { hideInvisibleSprites = his.GetBoolean(); } catch { hideInvisibleSprites = false; }
                        if (MenuOptionHideInvisibleSprites != null)
                        {
                            MenuOptionHideInvisibleSprites.IsChecked = hideInvisibleSprites;
                        }
                    }
                    // optional lock-sprites global setting
                    if (doc.RootElement.TryGetProperty("lockSpritesToSet", out var ls))
                    {
                        try { lockSpritesToSet = ls.GetBoolean(); } catch { lockSpritesToSet = false; }
                    }
                    // optional show accurate tileset setting (global)
                    if (doc.RootElement.TryGetProperty("showAccurateTileset", out var sat))
                    {
                        try { showAccurateTileset = sat.GetBoolean(); } catch { showAccurateTileset = false; }
                    }
                    // optional grid darkness (double)
                    if (doc.RootElement.TryGetProperty("gridDarkness", out var gd))
                    {
                        try { gridDarkness = gd.GetDouble(); } catch { try { gridDarkness = gd.GetSingle(); } catch { } }
                        if (GridDarknessSlider != null) GridDarknessSlider.Value = gridDarkness;
                        gridDirty = true;
                    }
                    // optional player tint (RGB or ARGB)
                    if (doc.RootElement.TryGetProperty("playerColor", out var pc) && (pc.GetArrayLength() == 3 || pc.GetArrayLength() == 4))
                    {
                        byte a = 255;
                        int idx = 0;
                        if (pc.GetArrayLength() == 4) { a = (byte)pc[0].GetInt32(); idx = 1; }
                        var r = (byte)pc[idx + 0].GetInt32();
                        var g = (byte)pc[idx + 1].GetInt32();
                        var b = (byte)pc[idx + 2].GetInt32();
                        playerTint = Color.FromArgb(a, r, g, b);
                    }
                    // optional player tint enabled flag (defaults to false)
                    if (doc.RootElement.TryGetProperty("playerColorEnabled", out var pce))
                    {
                        try { playerTintEnabled = pce.GetBoolean(); } catch { playerTintEnabled = false; }
                    }
                    // optional swap mouse wheel behavior
                    if (doc.RootElement.TryGetProperty("swapMouseWheelScroll", out var smw))
                    {
                        try { swapMouseWheelScroll = smw.GetBoolean(); } catch { swapMouseWheelScroll = false; }
                        if (MenuOptionSwapMouseWheel != null) MenuOptionSwapMouseWheel.IsChecked = swapMouseWheelScroll;
                    }
                    // optional invert pinch gesture setting
                    if (doc.RootElement.TryGetProperty("invertPinchGesture", out var ipg))
                    {
                        try { invertPinchGesture = ipg.GetBoolean(); } catch { invertPinchGesture = true; }
                        if (MenuOptionInvertPinch != null) MenuOptionInvertPinch.IsChecked = invertPinchGesture;
                    }
                    // optional famistudio path
                    if (doc.RootElement.TryGetProperty("famistudioPath", out var fsPath))
                    {
                        try { famiStudioPath = fsPath.GetString(); } catch { famiStudioPath = null; }
                        if (!string.IsNullOrEmpty(famiStudioPath))
                        {
                            try { famiIntegration.LoadFromFolder(famiStudioPath); } catch { }
                        }
                        else
                        {
                            // If not configured, check the common Program Files location and use it automatically if present
                            try
                            {
                                var defaultPf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FamiStudio");
                                if (Directory.Exists(defaultPf))
                                {
                                    famiStudioPath = defaultPf;
                                    try { famiIntegration.LoadFromFolder(famiStudioPath); } catch { }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                // Ensure any previous tinted caches are cleared so they don't reference stale tints
                ClearTintedCaches();
                // Apply lock state if present in global settings (sprites may not yet be loaded, but ApplyLockSpritesToSet
                // will re-run when sprites are loaded elsewhere)
                try { ApplyLockSpritesToSet(); } catch { }
            }
        }

        private void SaveSettings(Color c)
        {
            try
            {
                var obj = new {
                    background = new int[] { c.A, c.R, c.G, c.B },
                    backgroundTint = new int[] { backgroundTint.A, backgroundTint.R, backgroundTint.G, backgroundTint.B },
                    groundTint = new int[] { groundTint.A, groundTint.R, groundTint.G, groundTint.B },
                    useLegacyTriggerOffset = useLegacyTriggerOffset,
                    swapMouseWheelScroll = swapMouseWheelScroll,
                    invertPinchGesture = invertPinchGesture,
                    hideColorTriggers = hideColorTriggers,
                    hideInvisibleSprites = hideInvisibleSprites,
                    lockSpritesToSet = lockSpritesToSet,
                    showAccurateTileset = showAccurateTileset,
                    playerColor = new int[] { playerTint.A, playerTint.R, playerTint.G, playerTint.B },
                    playerColorEnabled = playerTintEnabled,
                    gridDarkness = gridDarkness,
                    famistudioPath = string.IsNullOrEmpty(famiStudioPath) ? null : famiStudioPath
                };
                var txt = System.Text.Json.JsonSerializer.Serialize(obj);
                var dir = AppContext.BaseDirectory;
                var path = System.IO.Path.Combine(dir, "editor-settings.json");
                System.IO.File.WriteAllText(path, txt);
            }
            catch { }
        }
        
        private void SaveSettingsWithTriggerOption()
        {
            try
            {
                var c = mapBackground is SolidColorBrush sb ? sb.Color : Color.FromRgb(59, 59, 59);
                SaveSettings(c);
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
                // Ensure portal debug log exists and is writable early so subsequent code can append safely
                InitializePortalDebugLog();

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
                    try { ApplyLockSpritesToSet(); } catch { }
                    if (StatusText != null) StatusText.Text = "Loaded sprites from embedded resources";
                }
                
                BitmapSource? embeddedParallax = null;
                if (noParallaxBg)
                {
                    embeddedParallax = LoadEmbeddedImage("noparallax.bmp");
                    if (embeddedParallax != null && StatusText != null) StatusText.Text = "Loaded noparallax from embedded resources";
                }
                if (embeddedParallax == null)
                {
                    embeddedParallax = LoadEmbeddedImage("parallax.bmp");
                    if (embeddedParallax != null && StatusText != null) StatusText.Text = "Loaded parallax from embedded resources";
                }
                if (embeddedParallax != null)
                {
                    parallaxBitmap = embeddedParallax;
                    SliceParallax();
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
                
                // Initialize portal sprites for preview mode
                InitializePortalSprites();
                
                // Initialize yellow orb animation frames
                InitializeYellowOrbAnimationFrames();
                
                // Initialize other orb color animation frames
                InitializeBlueOrbAnimationFrames();
                InitializePinkOrbAnimationFrames();
                InitializeGreenOrbAnimationFrames();
                InitializeRedOrbAnimationFrames();
                InitializeWhiteOrbAnimationFrames();
                InitializeCoinAnimationFrames();
                InitializeBlackOrbAnimationFrames();
                // Initialize pad animation frames (preview-only)
                InitializeRedPadAnimationFrames();
                InitializeRedPadUpAnimationFrames();
                InitializeYellowPadDownAnimationFrames();
                InitializeYellowPadUpAnimationFrames();
                InitializeBluePadDownAnimationFrames();
                InitializeBluePadUpAnimationFrames();
                InitializePinkPadDownAnimationFrames();
                InitializePinkPadUpAnimationFrames();
                // Initialize new dash orb two-frame slow animations
                LoadTwoFrameOrb("dash-orb-right", ref dashOrbRightFrame1, ref dashOrbRightFrame2);
                LoadTwoFrameOrb("dash-gravity-orb-right", ref dashGravityOrbRightFrame1, ref dashGravityOrbRightFrame2);
                LoadTwoFrameOrb("dash-orb-45deg-upwards", ref dashOrb45UpFrame1, ref dashOrb45UpFrame2);
                LoadTwoFrameOrb("dash-gravity-orb-45deg-upwards", ref dashGravityOrb45UpFrame1, ref dashGravityOrb45UpFrame2);
                LoadTwoFrameOrb("dash-orb-45deg-downwards", ref dashOrb45DownFrame1, ref dashOrb45DownFrame2);
                LoadTwoFrameOrb("dash-gravity-orb-45deg-downwards", ref dashGravityOrb45DownFrame1, ref dashGravityOrb45DownFrame2);
                LoadTwoFrameOrb("dash-orb-up", ref dashOrbUpFrame1, ref dashOrbUpFrame2);
                LoadTwoFrameOrb("dash-gravity-orb-up", ref dashGravityOrbUpFrame1, ref dashGravityOrbUpFrame2);
                LoadTwoFrameOrb("dash-orb-down", ref dashOrbDownFrame1, ref dashOrbDownFrame2);
                LoadTwoFrameOrb("dash-gravity-orb-down", ref dashGravityOrbDownFrame1, ref dashGravityOrbDownFrame2);
                // Teleport orb two-frame animations
                LoadTwoFrameOrb("teleport-orb-enter", ref teleportOrbEnterFrame1, ref teleportOrbEnterFrame2);
                LoadTwoFrameOrb("teleport-orb-exit", ref teleportOrbExitFrame1, ref teleportOrbExitFrame2);
                // Spider orb two-frame animations
                LoadTwoFrameOrb("spider-orb-downwards", ref spiderOrbDownFrame1, ref spiderOrbDownFrame2);
                LoadTwoFrameOrb("spider-orb-upwards", ref spiderOrbUpFrame1, ref spiderOrbUpFrame2);
                // Star two-frame preview animation (sprite 0x36)
                LoadTwoFrameOrb("star", ref starFrame1, ref starFrame2);
                // New decorations two-frame previews
                LoadTwoFrameOrb("pulsing-ball", ref pulsingBallFrame1, ref pulsingBallFrame2); // sprite 0x49
                LoadTwoFrameOrb("music-note", ref musicNoteFrame1, ref musicNoteFrame2); // sprite 0x4A
                // Additional decoration two-frame previews
                LoadTwoFrameOrb("diamond", ref diamondFrame1, ref diamondFrame2); // sprite 0x32
                LoadTwoFrameOrb("diamond-half", ref diamondHalfFrame1, ref diamondHalfFrame2); // sprite 0x33
                LoadTwoFrameOrb("question-mark", ref questionMarkFrame1, ref questionMarkFrame2); // sprite 0x34
                LoadTwoFrameOrb("exclamation-mark", ref exclamationFrame1, ref exclamationFrame2); // sprite 0x35
                LoadTwoFrameOrb("x", ref xFrame1, ref xFrame2); // sprite 0x37
                LoadTwoFrameOrb("pole-short", ref poleShortFrame1, ref poleShortFrame2); // sprite 0x2C
                LoadTwoFrameOrb("pole-short-upsidedown", ref poleShortUpsideDownFrame1, ref poleShortUpsideDownFrame2); // sprite 0x3C
                // New short pole left/right decorations
                LoadTwoFrameOrb("pole-left-short", ref poleLeftShortFrame1, ref poleLeftShortFrame2); // sprite 0x38
                LoadTwoFrameOrb("pole-right-short", ref poleRightShortFrame1, ref poleRightShortFrame2); // sprite 0x39
                // Pole-medium replacements for 0x2C/0x3C preview (1.5 tiles tall)
                LoadTwoFrameOrb("pole-medium", ref poleMediumFrame1, ref poleMediumFrame2); // sprite 0x2C -> custom 2122/2123
                LoadTwoFrameOrb("pole-medium-upsidedown", ref poleMediumUpsideDownFrame1, ref poleMediumUpsideDownFrame2); // sprite 0x3C -> custom 2124/2125
                // Pole-long replacements for 0x2A/0x3A preview (2 tiles tall)
                LoadTwoFrameOrb("pole-long", ref poleLongFrame1, ref poleLongFrame2); // sprite 0x2A -> custom 2140/2141
                LoadTwoFrameOrb("pole-long-upsidedown", ref poleLongUpsideDownFrame1, ref poleLongUpsideDownFrame2); // sprite 0x3A -> custom 2142/2143
                    // Medium pole replacements (if provided as embedded assets)
                    LoadTwoFrameOrb("pole-left-medium", ref poleLeftMediumFrame1, ref poleLeftMediumFrame2); // sprite 0x3E
                    LoadTwoFrameOrb("pole-right-medium", ref poleRightMediumFrame1, ref poleRightMediumFrame2); // sprite 0x3F
                // Chain decorations (single-frame preview-only)
                try
                {
                    var ch = LoadEmbeddedImage("chain.png");
                    if (ch != null)
                    {
                        chainFrame1 = new BitmapSource[1];
                        chainFrame1[0] = new FormatConvertedBitmap(ch, PixelFormats.Pbgra32, null, 0);
                    }
                    else
                    {
                        var baseDir = AppContext.BaseDirectory;
                        var p = Path.Combine(baseDir, "chain.png");
                        var repo = FindRepoRootFor("famidash.bmp");
                        if (!string.IsNullOrEmpty(repo)) { var rp = Path.Combine(repo, "chain.png"); if (File.Exists(rp)) p = rp; }
                        if (File.Exists(p))
                        {
                            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(p); bi.EndInit(); bi.Freeze();
                            chainFrame1 = new BitmapSource[1];
                            chainFrame1[0] = new FormatConvertedBitmap(bi, PixelFormats.Pbgra32, null, 0);
                        }
                    }
                }
                catch { }

                try
                {
                    var ch2 = LoadEmbeddedImage("chain-upsidedown.png");
                    if (ch2 != null)
                    {
                        chainUpsideDownFrame1 = new BitmapSource[1];
                        chainUpsideDownFrame1[0] = new FormatConvertedBitmap(ch2, PixelFormats.Pbgra32, null, 0);
                    }
                    else
                    {
                        var baseDir = AppContext.BaseDirectory;
                        var p = Path.Combine(baseDir, "chain-upsidedown.png");
                        var repo = FindRepoRootFor("famidash.bmp");
                        if (!string.IsNullOrEmpty(repo)) { var rp = Path.Combine(repo, "chain-upsidedown.png"); if (File.Exists(rp)) p = rp; }
                        if (File.Exists(p))
                        {
                            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(p); bi.EndInit(); bi.Freeze();
                            chainUpsideDownFrame1 = new BitmapSource[1];
                            chainUpsideDownFrame1[0] = new FormatConvertedBitmap(bi, PixelFormats.Pbgra32, null, 0);
                        }
                    }
                }
                catch { }
                // Load deco spikes (single-frame preview-only)
                try
                {
                    var s = LoadEmbeddedImage("deco-spikes.png");
                    if (s != null)
                    {
                        decoSpikesFrame1 = new BitmapSource[1];
                        decoSpikesFrame1[0] = new FormatConvertedBitmap(s, PixelFormats.Pbgra32, null, 0);
                    }
                    else
                    {
                        var baseDir = AppContext.BaseDirectory;
                        var p = Path.Combine(baseDir, "deco-spikes.png");
                        var repo = FindRepoRootFor("famidash.bmp");
                        if (!string.IsNullOrEmpty(repo)) { var rp = Path.Combine(repo, "deco-spikes.png"); if (File.Exists(rp)) p = rp; }
                        if (File.Exists(p))
                        {
                            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(p); bi.EndInit(); bi.Freeze();
                            decoSpikesFrame1 = new BitmapSource[1];
                            decoSpikesFrame1[0] = new FormatConvertedBitmap(bi, PixelFormats.Pbgra32, null, 0);
                        }
                    }
                }
                catch { }

                try
                {
                    var s2 = LoadEmbeddedImage("deco-spikes-upsidedown.png");
                    if (s2 != null)
                    {
                        decoSpikesUpsideDownFrame1 = new BitmapSource[1];
                        decoSpikesUpsideDownFrame1[0] = new FormatConvertedBitmap(s2, PixelFormats.Pbgra32, null, 0);
                    }
                    else
                    {
                        var baseDir = AppContext.BaseDirectory;
                        var p = Path.Combine(baseDir, "deco-spikes-upsidedown.png");
                        var repo = FindRepoRootFor("famidash.bmp");
                        if (!string.IsNullOrEmpty(repo)) { var rp = Path.Combine(repo, "deco-spikes-upsidedown.png"); if (File.Exists(rp)) p = rp; }
                        if (File.Exists(p))
                        {
                            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(p); bi.EndInit(); bi.Freeze();
                            decoSpikesUpsideDownFrame1 = new BitmapSource[1];
                            decoSpikesUpsideDownFrame1[0] = new FormatConvertedBitmap(bi, PixelFormats.Pbgra32, null, 0);
                        }
                    }
                }
                catch { }

                try
                {
                    var s3 = LoadEmbeddedImage("deco-spikes-small.png");
                    if (s3 != null)
                    {
                        decoSpikesSmallFrame1 = new BitmapSource[1];
                        decoSpikesSmallFrame1[0] = new FormatConvertedBitmap(s3, PixelFormats.Pbgra32, null, 0);
                    }
                    else
                    {
                        var baseDir = AppContext.BaseDirectory;
                        var p = Path.Combine(baseDir, "deco-spikes-small.png");
                        var repo = FindRepoRootFor("famidash.bmp");
                        if (!string.IsNullOrEmpty(repo)) { var rp = Path.Combine(repo, "deco-spikes-small.png"); if (File.Exists(rp)) p = rp; }
                        if (File.Exists(p))
                        {
                            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(p); bi.EndInit(); bi.Freeze();
                            decoSpikesSmallFrame1 = new BitmapSource[1];
                            decoSpikesSmallFrame1[0] = new FormatConvertedBitmap(bi, PixelFormats.Pbgra32, null, 0);
                        }
                    }
                }
                catch { }

                try
                {
                    var s4 = LoadEmbeddedImage("deco-spikes-small-upsidedown.png");
                    if (s4 != null)
                    {
                        decoSpikesSmallUpsideDownFrame1 = new BitmapSource[1];
                        decoSpikesSmallUpsideDownFrame1[0] = new FormatConvertedBitmap(s4, PixelFormats.Pbgra32, null, 0);
                    }
                    else
                    {
                        var baseDir = AppContext.BaseDirectory;
                        var p = Path.Combine(baseDir, "deco-spikes-small-upsidedown.png");
                        var repo = FindRepoRootFor("famidash.bmp");
                        if (!string.IsNullOrEmpty(repo)) { var rp = Path.Combine(repo, "deco-spikes-small-upsidedown.png"); if (File.Exists(rp)) p = rp; }
                        if (File.Exists(p))
                        {
                            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(p); bi.EndInit(); bi.Freeze();
                            decoSpikesSmallUpsideDownFrame1 = new BitmapSource[1];
                            decoSpikesSmallUpsideDownFrame1[0] = new FormatConvertedBitmap(bi, PixelFormats.Pbgra32, null, 0);
                        }
                    }
                }
                catch { }
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
            System.Diagnostics.Debug.WriteLine($"FindRepoRootFor({filename}) starting from: {dir}");
            
            for (int i = 0; i < 6; i++)
            {
                var candidate = Path.Combine(dir, filename);
                bool exists = File.Exists(candidate);
                System.Diagnostics.Debug.WriteLine($"  [{i}] Checking: {candidate} - Exists: {exists}");
                
                if (exists) {
                    System.Diagnostics.Debug.WriteLine($"  FOUND! Returning: {dir}");
                    return dir;
                }
                
                var parent = Directory.GetParent(dir);
                if (parent == null) {
                    System.Diagnostics.Debug.WriteLine($"  No parent directory, breaking");
                    break;
                }
                dir = parent.FullName;
            }
            
            System.Diagnostics.Debug.WriteLine($"  NOT FOUND, returning null");
            return null;
        }

        private BitmapImage? LoadEmbeddedImage(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();
                // (debug logging removed)

                // Find the resource - it might have the full path prefix
                var fullResourceName = resourceNames.FirstOrDefault(r => r.EndsWith(resourceName));
                    if (fullResourceName == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Resource not found: {resourceName}");
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

        private void TryLoadFamiAlbumParsedJson()
        {
            if (FamiTrackCombo == null) return;

            var jsonCandidates = new System.Collections.Generic.List<string>
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "fami-album-parsed.json"),
                System.IO.Path.Combine(Environment.CurrentDirectory, "fami-album-parsed.json")
            };

            var repoRoot = FindRepoRootFor("the album.txt");
            if (!string.IsNullOrEmpty(repoRoot))
            {
                jsonCandidates.Add(System.IO.Path.Combine(repoRoot, "fami-album-parsed.json"));
                jsonCandidates.Add(System.IO.Path.Combine(repoRoot, "native-windows", "fami-album-parsed.json"));
            }

            string? foundJson = null;
            foreach (var c in jsonCandidates)
            {
                try { if (!string.IsNullOrEmpty(c) && File.Exists(c)) { foundJson = c; break; } } catch { }
            }

            System.Collections.Generic.List<string> parsed = new System.Collections.Generic.List<string>();

            if (foundJson != null)
            {
                try
                {
                    var txt = File.ReadAllText(foundJson);
                    using var doc = System.Text.Json.JsonDocument.Parse(txt);
                    if (doc.RootElement.TryGetProperty("parsedNames", out var pn) && pn.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var el in pn.EnumerateArray()) parsed.Add(el.GetString() ?? "");
                    }
                }
                catch { }
            }

            // If no parsed JSON found, try to parse the album TXT directly
            if (parsed.Count == 0)
            {
                string? txtPath = null;
                var txtCandidates = new System.Collections.Generic.List<string>
                {
                    System.IO.Path.Combine(AppContext.BaseDirectory, "the album.txt"),
                    System.IO.Path.Combine(Environment.CurrentDirectory, "the album.txt")
                };
                if (!string.IsNullOrEmpty(repoRoot))
                {
                    txtCandidates.Add(System.IO.Path.Combine(repoRoot, "the album.txt"));
                    txtCandidates.Add(System.IO.Path.Combine(repoRoot, "native-windows", "the album.txt"));
                }

                foreach (var c in txtCandidates)
                {
                    try { if (!string.IsNullOrEmpty(c) && File.Exists(c)) { txtPath = c; break; } } catch { }
                }

                if (txtPath != null)
                {
                    albumTxtPath = txtPath;
                    try { parsed = famiIntegration.ParseFamiStudioTextExport(txtPath); } catch { parsed = new System.Collections.Generic.List<string>(); }
                }
            }

            // Also look for an actual .fms project in the repo root or app base and prefer it for playback
            try
            {
                string? fmsCandidate = null;
                var fmsCandidates = new System.Collections.Generic.List<string>
                {
                    System.IO.Path.Combine(AppContext.BaseDirectory, "the album.fms"),
                    System.IO.Path.Combine(Environment.CurrentDirectory, "the album.fms")
                };
                if (!string.IsNullOrEmpty(repoRoot))
                {
                    fmsCandidates.Add(System.IO.Path.Combine(repoRoot, "the album.fms"));
                    fmsCandidates.Add(System.IO.Path.Combine(repoRoot, "native-windows", "the album.fms"));
                }
                // Also check for any *.fms files at repo root
                if (!string.IsNullOrEmpty(repoRoot))
                {
                    try
                    {
                        foreach (var f in Directory.EnumerateFiles(repoRoot, "*.fms", SearchOption.TopDirectoryOnly)) fmsCandidates.Add(f);
                    }
                    catch { }
                }

                foreach (var c in fmsCandidates)
                {
                    try { if (!string.IsNullOrEmpty(c) && File.Exists(c)) { fmsCandidate = c; break; } } catch { }
                }

                if (!string.IsNullOrEmpty(fmsCandidate))
                {
                    albumTxtPath = fmsCandidate; // reuse variable: it can be .txt or .fms; Play checks extension
                    try { if (StatusText != null) StatusText.Text = $"Found .fms for playback: {Path.GetFileName(fmsCandidate)}"; } catch { }
                }
            }
            catch { }

            // Populate combo
            FamiTrackCombo.Items.Clear();
            // Try to load a precomputed mapping from song name -> playable index
            System.Collections.Generic.Dictionary<string, int>? nameToIndex = null;
            try
            {
                var mapCandidates = new[] {
                    Path.Combine(AppContext.BaseDirectory, "fami-song-index-map.json"),
                    Path.Combine(Environment.CurrentDirectory, "fami-song-index-map.json"),
                    Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory) ?? AppContext.BaseDirectory, "native-windows", "fami-song-index-map.json"),
                    Path.Combine(AppContext.BaseDirectory, "..", "native-windows", "fami-song-index-map.json")
                };
                foreach (var mc in mapCandidates)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(mc) && File.Exists(mc))
                        {
                            var txt = File.ReadAllText(mc);
                            var arr = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>>(txt);
                                if (arr != null)
                            {
                                    nameToIndex = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                                foreach (var d in arr)
                                {
                                    if (d.TryGetValue("name", out var on) && d.TryGetValue("index", out var oi))
                                    {
                                        var n = on?.ToString();
                                        if (int.TryParse(oi?.ToString() ?? "", out var ii) && !string.IsNullOrEmpty(n))
                                        {
                                            if (!nameToIndex.ContainsKey(n)) nameToIndex[n] = ii;
                                        }
                                        mappingLoadedFromFile = true;
                                    }
                                }
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { nameToIndex = null; }

            if (parsed.Count > 0)
            {
                for (int i = 0; i < parsed.Count; i++)
                {
                    int tagIndex = i;
                    try
                    {
                        if (nameToIndex != null && nameToIndex.TryGetValue(parsed[i], out var mapped)) tagIndex = mapped;
                    }
                    catch { }

                    var item = new System.Windows.Controls.ComboBoxItem() { Content = parsed[i], Tag = tagIndex };
                    FamiTrackCombo.Items.Add(item);
                }
                // Combo population complete
                FamiTrackCombo.SelectedIndex = 0;
                try { if (StatusText != null) StatusText.Text = $"Loaded {parsed.Count} names from {(foundJson != null ? Path.GetFileName(foundJson) : (albumTxtPath != null ? Path.GetFileName(albumTxtPath) : "unknown"))}"; } catch { }
            }
            else
            {
                // no names found - leave empty but add placeholders so dropdown shows size
                for (int i = 0; i < 8; i++) FamiTrackCombo.Items.Add(new System.Windows.Controls.ComboBoxItem() { Content = $"Song {i}", Tag = i });
                if (FamiTrackCombo.Items.Count > 0) FamiTrackCombo.SelectedIndex = 0;
                try { if (StatusText != null) StatusText.Text = "No parsed song names found"; } catch { }
            }
        }

        private async void PlayFamiButton_Click(object? sender, RoutedEventArgs e)
        {
            if (albumTxtPath == null)
            {
                System.Windows.MessageBox.Show(this, "No album.txt found to play.", "Play", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // If the path we have is a text export, the FamiStudio CLI cannot export playable WAVs from it.
            // Playback requires a real .fms project file (or an in-process project loaded from FamiStudio assemblies).
            try
            {
                if (Path.GetExtension(albumTxtPath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    System.Windows.MessageBox.Show(this, "The loaded album is a FamiStudio text export (.txt). Playback requires the original .fms project or using FamiStudio itself.\n\nPlease configure the path to a .fms file or open a .fms via the FMS Player.", "Play Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            catch { }

            int idx = -1;
            if (FamiTrackCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem cbi && cbi.Tag is int t)
            {
                idx = t;
            }
            else if (FamiTrackCombo?.SelectedIndex >= 0) idx = FamiTrackCombo.SelectedIndex;

            if (idx < 0)
            {
                System.Windows.MessageBox.Show(this, "No track selected.", "Play", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string fpath = albumTxtPath!;
                    // If we have an actual .fms and the in-process integration is available, map selected name to the real index
                    if (File.Exists(fpath) && Path.GetExtension(fpath).Equals(".fms", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Ensure famiIntegration has loaded the FamiStudio assemblies if a path is configured
                            if (!famiIntegration.IsLoaded && !string.IsNullOrEmpty(famiStudioPath) && Directory.Exists(famiStudioPath))
                            {
                                famiIntegration.LoadFromFolder(famiStudioPath);
                            }

                            // If we have loaded a precomputed mapping from disk, prefer it — do not override via
                            // in-process enumeration at play-time. This avoids mismatches when the runtime FamiStudio
                            // assemblies (on the target machine) differ from the source tool used to build mappings.
                            if (!mappingLoadedFromFile)
                            {
                                var names = famiIntegration.EnumerateTracks(fpath);
                                if (names != null && names.Count > 0)
                                {
                                    // If the combo has a selected item with a string, try to match by name
                                    string? selectedName = null;
                                    if (FamiTrackCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem cb && cb.Content != null) selectedName = cb.Content.ToString();
                                    if (!string.IsNullOrEmpty(selectedName))
                                    {
                                        int mapped = names.FindIndex(n => string.Equals(n, selectedName, StringComparison.OrdinalIgnoreCase));
                                        if (mapped >= 0) idx = mapped;
                                    }
                                }
                            }

                            // No play-time diagnostics in release build
                        }
                        catch { }
                    }

                    famiIntegration.PlayTrack(fpath, idx);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => System.Windows.MessageBox.Show(this, "Play failed: " + ex.Message, "Play Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            });
        }

        private void StopFamiButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                famiIntegration.Stop();
            }
            catch { }
        }

        // Initialize a reliable portal debug log path and create the file with a header.
        // This tries the executable directory first, then the repo root, then the temp folder.
        private void InitializePortalDebugLog()
        {
            if (!string.IsNullOrEmpty(portalDebugPath)) return;

            // Compute repo root once to avoid possible null being passed into Path.Combine
            string? repoRoot = FindRepoRootFor("famidash.bmp");
            string? repoCandidate = (!string.IsNullOrEmpty(repoRoot)) ? Path.Combine(repoRoot, "portal-debug.txt") : null;

            string?[] candidates = new string?[] {
                Path.Combine(AppContext.BaseDirectory, "portal-debug.txt"),
                // Try repo root (if available)
                repoCandidate,
                // Fallback to temp
                Path.Combine(Path.GetTempPath(), "portal-debug.txt")
            };

            foreach (var cand in candidates)
            {
                if (string.IsNullOrEmpty(cand)) continue;
                try
                {
                    // Ensure directory exists
                    var dir = Path.GetDirectoryName(cand);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    // Do not create log files automatically in release - just record candidate path
                    System.Diagnostics.Debug.WriteLine($"Portal debug candidate path: {cand}");
                    portalDebugPath = cand;
                    return;
                }
                catch
                {
                    // try next candidate
                }
            }

            // As a last resort, try to set portalDebugPath to a file under AppContext.BaseDirectory even if writes failed earlier.
            portalDebugPath = Path.Combine(AppContext.BaseDirectory, "portal-debug.txt");
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
                try { ApplyLockSpritesToSet(); } catch { }
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

        private void MenuConfigureFamiStudio_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                using var dlg = new System.Windows.Forms.FolderBrowserDialog();
                dlg.Description = "Select the FamiStudio installation folder (contains FamiStudio.exe / FamiStudio.dll)";
                if (!string.IsNullOrEmpty(famiStudioPath) && Directory.Exists(famiStudioPath)) dlg.SelectedPath = famiStudioPath;
                var res = dlg.ShowDialog();
                if (res == System.Windows.Forms.DialogResult.OK || res == System.Windows.Forms.DialogResult.Yes)
                {
                    var sel = dlg.SelectedPath;
                    if (!string.IsNullOrEmpty(sel) && Directory.Exists(sel))
                    {
                        famiStudioPath = sel;
                        try { famiIntegration.LoadFromFolder(famiStudioPath); } catch (Exception ex) { System.Windows.MessageBox.Show(this, "Failed to load FamiStudio: " + ex.Message, "FamiStudio Load", MessageBoxButton.OK, MessageBoxImage.Error); }
                        // Save new setting
                        try { var c = mapBackground is SolidColorBrush sb ? sb.Color : Color.FromRgb(59, 59, 59); SaveSettings(c); } catch { }
                        try { if (StatusText != null) StatusText.Text = "Configured FamiStudio: " + Path.GetFileName(famiStudioPath); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                try { System.Windows.MessageBox.Show(this, "Failed to configure FamiStudio: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
            }
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
            
            // update toned cache only if tint is active (not transparent)
            if (tileTint.A != 0)
            {
                UpdateTileTint();
            }
            else
            {
                tileTonedImages = null; // clear toned images when no tint
            }
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
            
            System.Diagnostics.Debug.WriteLine($"Loaded {spriteImages.Length} sprite images");
            System.Diagnostics.Debug.WriteLine($"  Sprite 0x17 ({0x17}) in range: {0x17 < spriteImages.Length}");
            System.Diagnostics.Debug.WriteLine($"  Sprite 0x4B ({0x4B}) in range: {0x4B < spriteImages.Length}");
            System.Diagnostics.Debug.WriteLine($"  Sprite 0x58 ({0x58}) in range: {0x58 < spriteImages.Length}");
            System.Diagnostics.Debug.WriteLine($"  Sprite 0x08 ({0x08}) in range: {0x08 < spriteImages.Length}");
            System.Diagnostics.Debug.WriteLine($"  Sprite 0x09 ({0x09}) in range: {0x09 < spriteImages.Length}");
            System.Diagnostics.Debug.WriteLine($"  Sprite 0x0B ({0x0B}) in range: {0x0B < spriteImages.Length}");
            System.Diagnostics.Debug.WriteLine($"  Sprite 0x1F ({0x1F}) in range: {0x1F < spriteImages.Length}");
            System.Diagnostics.Debug.WriteLine($"  Sprite 0x29 ({0x29}) in range: {0x29 < spriteImages.Length}");
            
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
            // update tinted cache only if tint is active (not transparent)
            if (backgroundTint.A != 0)
            {
                UpdateParallaxTint();
            }
            else
            {
                parallaxTonedImages = null; // clear toned images when no tint
            }
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
            // update tinted cache only if tint is active (not transparent)
            if (groundTint.A != 0)
            {
                UpdateGroundTint();
            }
            else
            {
                groundTonedImages = null; // clear toned images when no tint
            }
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

        // Create exact RGB-replaced copies of images. For every non-black, non-transparent pixel
        // we replace the RGB channels with the tint's RGB (preserve original alpha). If tint.A == 0
        // the originals are returned unchanged.
        private ImageSource[]? CreateRgbReplacedImages(ImageSource[]? originals, Color tint)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // no change requested
            var outList = new List<ImageSource>(originals.Length);
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
                            bool isBlack = (r <= 12 && g <= 12 && b <= 12);
                            if (a != 0 && !isBlack)
                            {
                                pixels[i + 0] = tint.B;
                                pixels[i + 1] = tint.G;
                                pixels[i + 2] = tint.R;
                            }
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

        // Create HSL-hue shifted copies of images. For each visible, non-black/non-white pixel
        // we replace the hue with the tint's hue while preserving the original saturation and lightness.
        // Returns originals if tint.A == 0.
        private ImageSource[]? CreateHslShiftedImages(ImageSource[]? originals, Color tint)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // no change requested
            // Precompute tint hue
            RgbToHsl(tint.R, tint.G, tint.B, out double tintH, out double tintS, out double tintL);

            var outList = new List<ImageSource>(originals.Length);
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

        // (No CreateHueShiftedImagesSimple - ground will use the HSL-based CreateHueShiftedImages)

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
            // Use exact RGB replacement for parallax so the displayed color matches the picker swatch.
            parallaxTonedImages = CreateRgbReplacedImages(parallaxImages, backgroundTint);
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
            // For ground, use the HSL hue/saturation shifting as in the previous commit.
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
            // Use HSL hue-shifting for tiles so we preserve shading while changing hue.
            tileTonedImages = CreateHslShiftedImages(tileImages, tileTint);

            // Also tint the animated saw frames using HSL hue shift
            sawFrame1TilesTinted = CreateHslShiftedImages(sawFrame1Tiles, tileTint);
            sawFrame2TilesTinted = CreateHslShiftedImages(sawFrame2Tiles, tileTint);
            smallSawFrame1TilesTinted = CreateHslShiftedImages(smallSawFrame1Tiles, tileTint);
            smallSawFrame2TilesTinted = CreateHslShiftedImages(smallSawFrame2Tiles, tileTint);
            largeSawFrame1TilesTinted = CreateHslShiftedImages(largeSawFrame1Tiles, tileTint);
            largeSawFrame2TilesTinted = CreateHslShiftedImages(largeSawFrame2Tiles, tileTint);
            
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

        // Create an ImageSource that uses the toned image for colors but
        // replaces any pixels where the original (base) image has G >= 180
        // with the provided player tint. Returns toned image if toned is null.
        private ImageSource? CreatePlayerReplacedFromBaseAndToned(ImageSource? baseSrc, ImageSource? tonedSrc, Color tint)
        {
            if (baseSrc == null && tonedSrc == null) return null;
            try
            {
                var baseBs = baseSrc as BitmapSource;
                var tonedBs = tonedSrc as BitmapSource ?? baseBs;
                if (tonedBs == null) return baseSrc;
                var convBase = baseBs != null ? new FormatConvertedBitmap(baseBs, PixelFormats.Bgra32, null, 0) : null;
                var convToned = new FormatConvertedBitmap(tonedBs, PixelFormats.Bgra32, null, 0);
                int w = convToned.PixelWidth, h = convToned.PixelHeight;
                if (w <= 0 || h <= 0) return tonedSrc;
                int stride = w * 4;
                var basePixels = new byte[h * stride];
                var tonedPixels = new byte[h * stride];
                if (convBase != null)
                {
                    convBase.CopyPixels(basePixels, stride, 0);
                }
                convToned.CopyPixels(tonedPixels, stride, 0);

                byte pr = tint.R, pg = tint.G, pb = tint.B;
                for (int i = 0; i + 3 < tonedPixels.Length; i += 4)
                {
                    byte a = (convBase != null) ? basePixels[i + 3] : tonedPixels[i + 3];
                    byte baseG = (convBase != null) ? basePixels[i + 1] : tonedPixels[i + 1];
                    if (a != 0 && baseG >= 180)
                    {
                        tonedPixels[i + 0] = pb;
                        tonedPixels[i + 1] = pg;
                        tonedPixels[i + 2] = pr;
                    }
                }

                var wb = new WriteableBitmap(w, h, convToned.DpiX, convToned.DpiY, PixelFormats.Bgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), tonedPixels, stride, 0);
                wb.Freeze();
                return wb;
            }
            catch { return tonedSrc ?? baseSrc; }
        }

        // Helper: check whether a tile index is in the player-replacement set
        private bool IsPlayerReplacementTile(int tileIdx)
        {
            return (
                (tileIdx >= 0x0C && tileIdx <= 0x0F) ||
                (tileIdx >= 0x13 && tileIdx <= 0x14) ||
                (tileIdx >= 0x80 && tileIdx <= 0x81) ||
                (tileIdx >= 0x84 && tileIdx <= 0x87)
            );
        }

        private void PopulateTilesPanel()
        {
            if (TilesPanel == null) return;
            TilesPanel.Items.Clear();
            if (tileImages == null) return;
            int idx = 0;
            foreach (var src in tileImages)
            {
                // prefer tinted tiles in the left palette when available, except for
                // the special player-replacement tiles which must show the player
                // tinted green pixels while preserving toned colors for non-green.
                ImageSource? paletteSrc = null;
                bool isPlayerTile = IsPlayerReplacementTile(idx);
                var baseSrc = (idx < tileImages.Length) ? tileImages[idx] : src;
                var tonedSrc = (tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[idx] : null;
                if (isPlayerTile)
                {
                    if (playerTintEnabled && baseSrc != null)
                    {
                        paletteSrc = CreatePlayerReplacedFromBaseAndToned(baseSrc, tonedSrc ?? baseSrc, playerTint);
                    }
                    else
                    {
                        paletteSrc = tonedSrc ?? baseSrc;
                    }
                }
                else
                {
                    paletteSrc = tonedSrc ?? src;
                }
                
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

                    // Right-click: make this the active selected tile and activate tile layer
                    img.MouseRightButtonDown += (s, e) => {
                        int clickedTile = (int)((Image)s).Tag;
                        selectedTile = clickedTile;
                        selectedTiles = new List<int> { clickedTile };
                        selectionWidth = 1;
                        selectionHeight = 1;
                        selectedSprite = -1;
                        tilesLayerActive = true;
                        spritesLayerActive = false;
                        // Activate brush/place tool and tile draw mode so it's ready for placement
                        try { if (PlaceTool != null) PlaceTool.IsChecked = true; } catch { }
                        try { if (DrawTileButton != null) DrawTileButton.IsChecked = true; } catch { }
                        UpdatePaletteHighlight();
                        try { if (CanvasHost != null) CanvasHost.Focus(); } catch { }
                        e.Handled = true;
                    };
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
                
                // Update ID indicator on hover
                img.MouseEnter += (s, e) => {
                    int hoveredTile = (int)((Image)s).Tag;
                    if (TileIdIndicator != null)
                        TileIdIndicator.Text = $"0x{hoveredTile:X2}";
                    if (TileSelectedPreviewImage != null && tileImages != null && hoveredTile >= 0 && hoveredTile < tileImages.Length)
                    {
                        TileSelectedPreviewImage.Source = tileImages[hoveredTile];
                        try { System.Windows.Media.RenderOptions.SetBitmapScalingMode(TileSelectedPreviewImage, BitmapScalingMode.NearestNeighbor); } catch { }
                    }
                };
                
                img.MouseLeave += (s, e) => {
                    // Show selected tile when not hovering
                    if (TileIdIndicator != null && selectedTile >= 0)
                        TileIdIndicator.Text = $"0x{selectedTile:X2}";
                    else if (TileIdIndicator != null)
                        TileIdIndicator.Text = "";
                    if (TileSelectedPreviewImage != null)
                    {
                        if (selectedTile >= 0 && tileImages != null && selectedTile < tileImages.Length)
                        {
                            TileSelectedPreviewImage.Source = tileImages[selectedTile];
                        }
                        else
                        {
                            TileSelectedPreviewImage.Source = null;
                        }
                    }
                };
                
                var border = new Border { 
                    Child = img, 
                    Margin = new Thickness(0), 
                    Padding = new Thickness(0), 
                    BorderBrush = Brushes.Transparent, 
                    BorderThickness = new Thickness(0) // Start with 0, will be set to 2 when selected
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
                    // If locking active and this sprite is disabled, ignore clicks
                    if (lockSpritesToSet && disabledSprites.Contains(clickedSprite)) { e.Handled = true; return; }
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

                // Right-click: make this the active selected sprite and activate sprite layer
                img.MouseRightButtonDown += (s, e) => {
                    int clickedSprite = (int)((Image)s).Tag;
                    selectedSprite = clickedSprite;
                    selectedTile = -1;
                    selectedTiles = new List<int>();
                    selectionWidth = 1;
                    selectionHeight = 1;
                    spritesLayerActive = true;
                    tilesLayerActive = false;
                    // Activate brush/place tool so sprite placement is ready
                    try { if (PlaceTool != null) PlaceTool.IsChecked = true; } catch { }
                    // Keep draw mode as Tile but ensure the Place tool is active
                    UpdatePaletteHighlight();
                    try { if (CanvasHost != null) CanvasHost.Focus(); } catch { }
                    e.Handled = true;
                };
                
                // Update ID indicator on hover
                img.MouseEnter += (s, e) => {
                    int hoveredSprite = (int)((Image)s).Tag;
                    if (SpriteIdIndicator != null)
                        SpriteIdIndicator.Text = $"0x{hoveredSprite:X2}";
                    if (SpriteSelectedPreviewImage != null && spriteImages != null && hoveredSprite >= 0 && hoveredSprite < spriteImages.Length)
                    {
                        SpriteSelectedPreviewImage.Source = spriteImages[hoveredSprite];
                        try { System.Windows.Media.RenderOptions.SetBitmapScalingMode(SpriteSelectedPreviewImage, BitmapScalingMode.NearestNeighbor); } catch { }
                    }
                };
                
                img.MouseLeave += (s, e) => {
                    // Show selected sprite when not hovering
                    if (SpriteIdIndicator != null && selectedSprite >= 0)
                        SpriteIdIndicator.Text = $"0x{selectedSprite:X2}";
                    else if (SpriteIdIndicator != null)
                        SpriteIdIndicator.Text = "";
                    if (SpriteSelectedPreviewImage != null)
                    {
                        if (selectedSprite >= 0 && spriteImages != null && selectedSprite < spriteImages.Length)
                        {
                            SpriteSelectedPreviewImage.Source = spriteImages[selectedSprite];
                        }
                        else
                        {
                            SpriteSelectedPreviewImage.Source = null;
                        }
                    }
                };
                
                // If this sprite is disabled by deco-lock, show overlay and prevent selection
                bool isDisabled = lockSpritesToSet && disabledSprites.Contains(idx);
                FrameworkElement childElement = img;
                if (isDisabled)
                {
                    var grid = new Grid();
                    grid.Children.Add(img);
                    var cover = new System.Windows.Shapes.Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), IsHitTestVisible = false };
                    grid.Children.Add(cover);
                    var xlbl = new TextBlock { Text = "X", FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                    grid.Children.Add(xlbl);
                    childElement = grid;
                    // Make image non-interactive
                    img.IsEnabled = false;
                }

                var border = new Border { Child = childElement, Margin = new Thickness(0), Padding = new Thickness(0), BorderBrush = (idx == selectedSprite ? Brushes.Yellow : Brushes.Transparent), BorderThickness = (idx == selectedSprite ? new Thickness(2) : new Thickness(0)), IsEnabled = !isDisabled };
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
                if (TileSelectedPreviewImage != null && tileImages != null && selectedTile >= 0 && selectedTile < tileImages.Length)
                {
                    TileSelectedPreviewImage.Source = tileImages[selectedTile];
                    try { System.Windows.Media.RenderOptions.SetBitmapScalingMode(TileSelectedPreviewImage, BitmapScalingMode.NearestNeighbor); } catch { }
                }
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
            // Cancel polygon if palette selection changed
            if (lastKnownSelectedTile != selectedTile || lastKnownSelectedSprite != selectedSprite)
            {
                CancelPolygon();
            }

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
            
            // Update ID indicators
            UpdateIdIndicators();
            
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

            lastKnownSelectedTile = selectedTile; lastKnownSelectedSprite = selectedSprite;
        }
        
        // Update the ID indicator text labels
        private void UpdateIdIndicators()
        {
            // Update tile ID indicator
            if (TileIdIndicator != null)
            {
                if (selectedTile >= 0)
                    TileIdIndicator.Text = $"0x{selectedTile:X2}";
                else
                    TileIdIndicator.Text = "";
                if (TileSelectedPreviewImage != null)
                {
                    if (selectedTile >= 0 && tileImages != null && selectedTile < tileImages.Length)
                    {
                        TileSelectedPreviewImage.Source = tileImages[selectedTile];
                        try { System.Windows.Media.RenderOptions.SetBitmapScalingMode(TileSelectedPreviewImage, BitmapScalingMode.NearestNeighbor); } catch { }
                    }
                    else
                    {
                        TileSelectedPreviewImage.Source = null;
                    }
                }
            }
            
            // Update sprite ID indicator
            if (SpriteIdIndicator != null)
            {
                if (selectedSprite >= 0)
                    SpriteIdIndicator.Text = $"0x{selectedSprite:X2}";
                else
                    SpriteIdIndicator.Text = "";
                if (SpriteSelectedPreviewImage != null)
                {
                    if (selectedSprite >= 0 && spriteImages != null && selectedSprite < spriteImages.Length)
                    {
                        SpriteSelectedPreviewImage.Source = spriteImages[selectedSprite];
                        try { System.Windows.Media.RenderOptions.SetBitmapScalingMode(SpriteSelectedPreviewImage, BitmapScalingMode.NearestNeighbor); } catch { }
                    }
                    else
                    {
                        SpriteSelectedPreviewImage.Source = null;
                    }
                }
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
            
            // Calculate ground height from the actual ground bitmap
            double groundHeight = 0.0;
            if (groundBitmap != null && groundImages != null && groundImages.Length > 0)
            {
                var currentDpi = VisualTreeHelper.GetDpi(this);
                groundHeight = (groundBitmap.PixelHeight / currentDpi.DpiScaleY) * scale;
            }
            
            // Total height = map + ground - 4 rows (for scroll clamping)
            double fullH = mapHeight * TileSize * scale + groundHeight - (4 * TileSize * scale);
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
                double vpw = SafeViewportWidth();
                double vph = SafeViewportHeight();
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
            if (PortalsImage != null && portalsWb != null)
            {
                PortalsImage.Source = portalsWb;
                PortalsImage.Width = displayFullW; PortalsImage.Height = displayFullH;
                PortalsImage.LayoutTransform = Transform.Identity; // Clear temporary zoom transform
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
            try { UpdateIncompatibleOverlay(); } catch { }
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
                
                // Build parallax and ground - now optimized with GPU tiling for large maps
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
                // create or recreate portals writeable bitmap if size changed
                portalsWb = new WriteableBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
                var emptyPortal = new byte[pixelPaddedHeight * portalsWb.BackBufferStride];
                portalsWb.WritePixels(new Int32Rect(0, 0, pixelPaddedWidth, pixelPaddedHeight), emptyPortal, portalsWb.BackBufferStride, 0);
                // Render portals then sprites if preview mode is enabled, otherwise clear portals bitmap
                if (previewMode)
                {
                    RebuildPortalsRegion(0, 0, mapWidth - 1, mapHeight - 1, scale, pad);
                }
                else
                {
                    // Clear portals bitmap (ensure no stale portal images remain)
                    try
                    {
                        portalsWb.Lock();
                        unsafe
                        {
                            IntPtr pb = portalsWb.BackBuffer;
                            if (pb != IntPtr.Zero)
                            {
                                int stride = portalsWb.BackBufferStride;
                                int bytesTotal = stride * cachedPixelHeight;
                                byte* p = (byte*)pb.ToPointer();
                                for (int i = 0; i < bytesTotal; i++) p[i] = 0;
                            }
                        }
                        portalsWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
                    }
                    finally { try { portalsWb.Unlock(); } catch { } }
                }
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
            if (parallaxBitmap == null || parallaxImages == null || parallaxImages.Length == 0)
            {
                parallaxRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                return;
            }
            
            // Use the full parallax bitmap (not individual tiles)
            BitmapSource sourceImage = parallaxBitmap;

            // Apply tint if needed (check if background tint is active)
            if (backgroundTint.A != 0)
            {
                var tintedImages = CreateRgbReplacedImages(new ImageSource[] { parallaxBitmap }, backgroundTint);
                if (tintedImages != null && tintedImages.Length > 0 && tintedImages[0] is BitmapSource tinted)
                {
                    sourceImage = tinted;
                }
            }

            // Use DrawImage loop like ground - align tiles to device pixels to avoid seams
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Compute tile size in device pixels (integral) to avoid fractional placement
                int tilePixelW = (int)Math.Max(1, Math.Round(sourceImage.PixelWidth * scale));
                int tilePixelH = (int)Math.Max(1, Math.Round(sourceImage.PixelHeight * scale));

                // Full render area in device-independent units
                double renderW = pixelPaddedWidth / dpi.DpiScaleX;
                double renderH = pixelPaddedHeight / dpi.DpiScaleY;

                // Convert pad to device pixels and compute integer-aligned start offset
                int padPix = (int)Math.Round(pad * dpi.DpiScaleX);
                int startXPix = -(padPix % tilePixelW);
                int startYPix = -(padPix % tilePixelH);

                // How many tiles we need (tilePixel based)
                int tilesWide = (int)Math.Ceiling((double)pixelPaddedWidth / tilePixelW) + 2;
                int tilesHigh = (int)Math.Ceiling((double)pixelPaddedHeight / tilePixelH) + 2;

                // Calculate ground area in DIU to avoid overlap
                double groundHeight = 0.0;
                if (groundBitmap != null && groundImages != null && groundImages.Length > 0)
                {
                    groundHeight = (groundBitmap.PixelHeight / dpi.DpiScaleY) * scale;
                }
                double groundStartY = mapHeight * TileSize * scale + pad;
                double groundEndY = groundStartY + groundHeight;

                // Use an ImageBrush with TileMode.Tile to avoid manual tiling seams
                double tileDiuW = (sourceImage.PixelWidth / dpi.DpiScaleX) * scale;
                double tileDiuH = (sourceImage.PixelHeight / dpi.DpiScaleY) * scale;

                var brush = new ImageBrush(sourceImage)
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, tileDiuW, tileDiuH),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Stretch = Stretch.Fill
                };

                // Use nearest-neighbor scaling to avoid blending edges when scaling
                RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);

                // Draw parallax area above ground only (so ground drawn later covers it)
                if (groundHeight > 0)
                {
                    double topH = Math.Max(0.0, groundStartY);
                    dc.DrawRectangle(brush, null, new Rect(0, 0, renderW, topH));
                }
                else
                {
                    dc.DrawRectangle(brush, null, new Rect(0, 0, renderW, renderH));
                }
            }
            
            parallaxRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            parallaxRtb.Render(dv);
        }

        private void BuildGroundBitmap(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
        {
            if (groundBitmap == null || groundImages == null || groundImages.Length == 0)
            {
                groundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                return;
            }
            
            // Use the full ground bitmap (not individual tiles)
            BitmapSource sourceImage = groundBitmap;
            
            // Apply tint if needed (check if ground tint is active - not transparent and not white)
            if (groundTint.A != 0 && (groundTint.R != 255 || groundTint.G != 255 || groundTint.B != 255))
            {
                // Apply hue/saturation shift to the full ground bitmap
                var tintedImages = CreateHueShiftedImages(new ImageSource[] { groundBitmap }, groundTint);
                if (tintedImages != null && tintedImages.Length > 0 && tintedImages[0] is BitmapSource tinted)
                {
                    sourceImage = tinted;
                }
            }
            
            // Ground tiles horizontally but stretches vertically to fit the available space
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Ground starts below the map
                double groundY = mapHeight * TileSize * scale + pad;
                double fillWidth = pixelPaddedWidth / dpi.DpiScaleX;
                double groundHeightDiu = (sourceImage.PixelHeight / dpi.DpiScaleY) * scale;
                
                // Calculate how many times we need to tile the ground horizontally
                double tileWidthDiu = (sourceImage.PixelWidth / dpi.DpiScaleX) * scale;
                int tilesNeeded = (int)Math.Ceiling(fillWidth / tileWidthDiu) + 1;
                
                // Draw the ground tiled horizontally, starting from left edge accounting for padding
                double startX = -(pad % tileWidthDiu); // Align with padding
                for (int i = 0; i < tilesNeeded; i++)
                {
                    double x = startX + (i * tileWidthDiu);
                    dc.DrawImage(sourceImage, new Rect(x, groundY, tileWidthDiu, groundHeightDiu));
                }
            }
            
            groundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            groundRtb.Render(dv);
        }

        // Deferred async rebuild of parallax and ground bitmaps for large maps
        // Renders in horizontal chunks to keep UI responsive
        private async void RebuildParallaxGroundDeferredAsync(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
        {
            // Wait a moment to let tiles/sprites render first
            await System.Threading.Tasks.Task.Delay(300);
            
            System.Diagnostics.Debug.WriteLine("RebuildParallaxGroundDeferredAsync: Starting chunked rebuild");
            
            // Capture necessary data
            var parallaxData = (images: parallaxImages, tonedImages: parallaxTonedImages, bitmap: parallaxBitmap);
            var groundData = (images: groundImages, tonedImages: groundTonedImages, bitmap: groundBitmap, rows: groundTileRows);
            int capturedMapHeight = mapHeight;
            int capturedMapWidth = mapWidth;
            
            // Get viewport size
            double viewportW = pixelPaddedWidth / dpi.DpiScaleX;
            double viewportH = pixelPaddedHeight / dpi.DpiScaleY;
            if (MapScrollViewer != null)
            {
                double vpw = SafeViewportWidth();
                double vph = SafeViewportHeight();
                if (!double.IsNaN(vpw) && vpw > 0) viewportW = vpw;
                if (!double.IsNaN(vph) && vph > 0) viewportH = vph;
            }
            
            // Calculate viewport tiles
            int viewportCols = (int)Math.Ceiling(viewportW / (TileSize * scale)) + 2;
            int viewportRows = (int)Math.Ceiling(viewportH / (TileSize * scale)) + 2;
            
            // Progressive expansion: render viewport, then 2x viewport, then 4x, then full
            int[] expansions = { 1, 2, 4, 0 }; // 0 = full map
            
            foreach (int expansion in expansions)
            {
                int colsToRender = expansion == 0 ? capturedMapWidth : Math.Min(viewportCols * expansion, capturedMapWidth);
                int rowsToRender = expansion == 0 ? capturedMapHeight : Math.Min(viewportRows * expansion, capturedMapHeight);
                
                System.Diagnostics.Debug.WriteLine($"RebuildParallaxGroundDeferredAsync: Rendering expansion {expansion}x (cols={colsToRender}, rows={rowsToRender})");
                
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // Build parallax
                        var parallaxDv = new DrawingVisual();
                        using (var dc = parallaxDv.RenderOpen())
                        {
                            if (parallaxData.images != null && parallaxData.bitmap != null && parallaxData.images.Length > 0)
                            {
                                int parallaxCols = Math.Max(1, parallaxData.bitmap.PixelWidth / TileSize);
                                int groundRowsToDraw = groundData.rows;
                                
                                for (int pyTile = 0; pyTile < rowsToRender; pyTile++)
                                {
                                    if (groundRowsToDraw > 0 && pyTile >= capturedMapHeight && pyTile < capturedMapHeight + groundRowsToDraw) continue;
                                    for (int pxTile = 0; pxTile < colsToRender; pxTile++)
                                    {
                                        int idx = (pyTile * parallaxCols + pxTile) % parallaxData.images.Length;
                                        if (idx < 0) idx += parallaxData.images.Length;
                                        ImageSource? img = null;
                                        try { if (parallaxData.tonedImages != null && parallaxData.tonedImages.Length == parallaxData.images.Length) img = parallaxData.tonedImages[idx]; } catch { img = null; }
                                        if (img == null) img = parallaxData.images[idx];
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
                        var newParallaxRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                        newParallaxRtb.Render(parallaxDv);
                        
                        // Build ground
                        var groundDv = new DrawingVisual();
                        using (var dc = groundDv.RenderOpen())
                        {
                            if (groundData.images != null && groundData.images.Length > 0)
                            {
                                int cols = Math.Max(1, (groundData.bitmap?.PixelWidth ?? TileSize) / TileSize);
                                int groundRowsToDraw = groundData.rows;
                                double mapAreaH = capturedMapHeight * TileSize * scale;
                                int rowsBelowNeeded = Math.Max(0, (int)Math.Ceiling((viewportH - mapAreaH) / (TileSize * scale)));
                                int rowsToDraw = Math.Max(groundRowsToDraw, rowsBelowNeeded);
                                rowsToDraw += 1;

                                for (int gy = 0; gy < rowsToDraw; gy++)
                                {
                                    for (int gx = 0; gx < colsToRender; gx++)
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
                                        int idx = (srcRow * cols + wrappedX) % groundData.images.Length;
                                        ImageSource? gimg = null;
                                        try { if (groundData.tonedImages != null && groundData.tonedImages.Length == groundData.images.Length) gimg = groundData.tonedImages[idx]; } catch { gimg = null; }
                                        if (gimg == null) gimg = groundData.images[idx];
                                        double px = gx * TileSize * scale + pad;
                                        double py = (capturedMapHeight + gy) * TileSize * scale + pad;
                                        if (gimg != null) dc.DrawImage(gimg, new Rect(px, py, TileSize * scale, TileSize * scale));
                                    }
                                }
                            }
                        }
                        var newGroundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                        newGroundRtb.Render(groundDv);
                        
                        // Update the member variables and UI
                        parallaxRtb = newParallaxRtb;
                        groundRtb = newGroundRtb;
                        if (ParallaxImage != null) ParallaxImage.Source = parallaxRtb;
                        if (GroundImage != null) GroundImage.Source = groundRtb;
                        
                        System.Diagnostics.Debug.WriteLine($"RebuildParallaxGroundDeferredAsync: Expansion {expansion}x complete");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"RebuildParallaxGroundDeferredAsync: Error - {ex.Message}");
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
                
                // Yield between expansions to keep UI responsive
                if (expansion != 0) // Don't delay after the last one
                {
                    await System.Threading.Tasks.Task.Delay(200);
                }
            }
            
            System.Diagnostics.Debug.WriteLine("RebuildParallaxGroundDeferredAsync: All expansions complete");
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
            double camOffsetX = (MapScrollViewer?.HorizontalOffset ?? 0);
            double camOffsetY = (MapScrollViewer?.VerticalOffset ?? 0);
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
            }
        }

        private void CommitZoom()
        {
            // Clear any temporary transforms applied during preview
            try
            {
                if (BackgroundImage != null) BackgroundImage.LayoutTransform = Transform.Identity;
                if (ParallaxImage != null)
                {
                    if (parallaxTransform != null) ParallaxImage.RenderTransform = parallaxTransform; else ParallaxImage.RenderTransform = Transform.Identity;
                    ParallaxImage.LayoutTransform = Transform.Identity;
                }
                if (GroundImage != null) GroundImage.LayoutTransform = Transform.Identity;
                if (TilesImage != null) TilesImage.LayoutTransform = Transform.Identity;
                if (GridImage != null) GridImage.LayoutTransform = Transform.Identity;
                if (CanvasHost != null) CanvasHost.LayoutTransform = Transform.Identity;
            }
            catch { }
            // Stop any pending commit timer and clear defer flag
            try { zoomCommitTimer?.Stop(); } catch { }
            deferZoomRebuild = false;

            // Trigger a full redraw which will ensure layer bitmaps are recreated at the current scale
            try { Redraw(); } catch { }

            // Allow layout/measure/render to complete so ActualWidth/Height and Viewport sizes are up-to-date
            try { Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render); } catch { }

            // If we have a zoom anchor, compute new scroll offsets so the same world point remains under the same viewport point
            if (hasZoomAnchor && MapScrollViewer != null)
            {
                try
                {
                    double newScale = ZoomSlider?.Value ?? 1.0;
                    double pad = mapViewportPadding;
                    double newContentX = pad + zoomAnchorMapX * TileSize * newScale;
                    double newContentY = pad + zoomAnchorMapY * TileSize * newScale;
                    double newH = newContentX - zoomAnchorViewportX;
                    double newV = newContentY - zoomAnchorViewportY;
                    double maxH = Math.Max(0, (CanvasHost?.ActualWidth ?? 0) - SafeViewportWidth());
                    double maxV = Math.Max(0, (CanvasHost?.ActualHeight ?? 0) - SafeViewportHeight());
                    newH = Math.Max(0, Math.Min(maxH, newH));
                    newV = Math.Max(0, Math.Min(maxV, newV));
                    MapScrollViewer?.ScrollToHorizontalOffset(newH);
                    MapScrollViewer?.ScrollToVerticalOffset(newV);
                }
                catch { }
            }

            // Clamp offsets and update parallax transform after redraw
            try { ClampScrollOffsets(); } catch { }
            // Snap offsets to device pixels so rendered layers and overlays align exactly
            try { SnapScrollOffsetsToDevicePixels(); } catch { }
            try { UpdateParallaxTransform(); } catch { }

            // clear anchor after commit
            hasZoomAnchor = false;
            // Device-pixel align the grid image so its bottom line matches the ground top
            try
            {
                if (GridImage != null)
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
                    double pad = mapViewportPadding;
                    // Compute tile pixel heights as BuildGridBitmap does
                    int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                    int padPx = (int)Math.Round(pad * dpi.DpiScaleY);
                    // Grid bottom in DIU according to grid's pixel placement
                    double gridBottomDiu = (padPx + mapHeight * tilePixelH) / dpi.DpiScaleY;
                    // Ground top in DIU (ideal continuous)
                    double groundTopDiu = pad + mapHeight * TileSize * scale;
                    // Delta to move grid so its bottom matches ground top
                    double delta = groundTopDiu - gridBottomDiu;
                    // Round the delta to device pixels and apply that integer-pixel shift to the grid.
                    var dpiForGrid = dpi; // already obtained above
                    int shiftPx = (int)Math.Round(delta * dpiForGrid.DpiScaleY);
                    if (Math.Abs(shiftPx) > 0)
                    {
                        double appliedDelta = shiftPx / dpiForGrid.DpiScaleY;
                        var tt = new TranslateTransform(0, appliedDelta);
                        GridImage.RenderTransform = tt;
                        // Also apply same integer-pixel Y shift to image layers so tiles/sprites align with grid
                        try { if (TilesImage != null) TilesImage.RenderTransform = tt; } catch { }
                        try { if (SpritesImage != null) SpritesImage.RenderTransform = tt; } catch { }
                        try { if (PortalsImage != null) PortalsImage.RenderTransform = tt; } catch { }
                        gridRenderShiftY = appliedDelta;
                        gridRenderShiftYPx = shiftPx;
                    }
                    else
                    {
                        GridImage.RenderTransform = Transform.Identity;
                        try { if (TilesImage != null) TilesImage.RenderTransform = Transform.Identity; } catch { }
                        try { if (SpritesImage != null) SpritesImage.RenderTransform = Transform.Identity; } catch { }
                        try { if (PortalsImage != null) PortalsImage.RenderTransform = Transform.Identity; } catch { }
                        gridRenderShiftY = 0.0;
                        gridRenderShiftYPx = 0;
                    }
                }
            }
            catch { }

            lastHoverX = -1; lastHoverY = -1;
            try { this.Activate(); } catch { }
            try { UpdateIncompatibleOverlay(); } catch { }
        }

        // Snap ScrollViewer offsets to integer device pixels to ensure layers align
        private void SnapScrollOffsetsToDevicePixels()
        {
            try
            {
                if (MapScrollViewer == null || CanvasHost == null) return;
                var dpi = VisualTreeHelper.GetDpi(this);
                double h = (MapScrollViewer?.HorizontalOffset ?? 0);
                double v = (MapScrollViewer?.VerticalOffset ?? 0);
                // Convert to device pixels, round, convert back to DIU
                double hPix = Math.Round(h * dpi.DpiScaleX);
                double vPix = Math.Round(v * dpi.DpiScaleY);
                double newH = hPix / dpi.DpiScaleX;
                double newV = vPix / dpi.DpiScaleY;
                // Clamp to valid ranges
                    double maxH = Math.Max(0, (CanvasHost?.ActualWidth ?? 0) - SafeViewportWidth());
                double maxV = Math.Max(0, (CanvasHost?.ActualHeight ?? 0) - SafeViewportHeight());
                newH = Math.Max(0, Math.Min(maxH, newH));
                newV = Math.Max(0, Math.Min(maxV, newV));
                MapScrollViewer?.ScrollToHorizontalOffset(newH);
                MapScrollViewer?.ScrollToVerticalOffset(newV);
            }
            catch { }
        }

        // Prevent the ScrollViewer from scrolling below the last visible ground row.
        // This clamps the vertical offset so the viewport bottom never goes past the bottom
        // of the map+ground content area (including padding). Called from scroll/zoom handlers.
        private void ClampScrollOffsets()
        {
            if (MapScrollViewer == null) return;
            // Compute the padded full height (map + ground + parallax padding) using current zoom
            double scale = ZoomSlider?.Value ?? 1.0;
            double pad = mapViewportPadding;
            double fullH = mapHeight * TileSize * scale;
            
            // Calculate ground height from the actual ground bitmap, not from tile rows
            double groundHeight = 0.0;
            if (groundBitmap != null && groundImages != null && groundImages.Length > 0)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                groundHeight = (groundBitmap.PixelHeight / dpi.DpiScaleY) * scale;
            }
            
            // Add ground height to total height, minus 2 rows to clamp earlier
            fullH += groundHeight - (2 * TileSize * scale);
            double paddedFullH = fullH + pad * 2.0;

            // compute maximum allowed vertical offset so viewport bottom <= paddedFullH
            double maxAllowedV = Math.Max(0.0, paddedFullH - SafeViewportHeight());
            // If the current VerticalOffset is larger than allowed, snap it back
            if ((MapScrollViewer?.VerticalOffset ?? 0) > maxAllowedV + 1e-6)
            {
                MapScrollViewer?.ScrollToVerticalOffset(maxAllowedV);
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
            
            // For very large maps, use bigger batches and skip pre-rendering
            int totalTiles = mapWidth * mapHeight;
            bool isLargeMap = totalTiles > 50000;
            int batchSize = isLargeMap ? Math.Max(2000, mapWidth * 2) : Math.Max(500, mapWidth);
            
            System.Diagnostics.Debug.WriteLine($"RebuildAllTilesBitmapAsync: {totalTiles} tiles, batchSize={batchSize}, isLargeMap={isLargeMap}");
            
            for (int batchStart = 0; batchStart < totalTiles; batchStart += batchSize)
            {
                int batchEnd = Math.Min(batchStart + batchSize, totalTiles);
                
                // For large maps, skip the background pre-render step and just render directly in UI
                if (!isLargeMap)
                {
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
                }
                
                // Update UI on main thread
                await Dispatcher.InvokeAsync(() =>
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
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
            
            System.Diagnostics.Debug.WriteLine($"RebuildAllTilesBitmapAsync: Complete");
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
                        // Convert pixel dimensions to device-independent units for proper rendering
                        double dipWidth = tilePixelW / dpi.DpiScaleX;
                        double dipHeight = tilePixelH / dpi.DpiScaleY;
                        
                        using (var dc = dv.RenderOpen())
                        {
                            // Check if this is a half-height tile (8 pixels tall source)
                            if (IsHalfHeightTile(tileIdx))
                            {
                                // For half-height tiles (16x8 source), scale proportionally
                                // The source is 16x8, we want it to occupy 8 scaled pixels height
                                // Calculate the scale factor from the tile size
                                double scaleFactor = dipHeight / 16.0; // How much are we scaling from base 16 DIP tile
                                double scaledHeight = 8 * scaleFactor;  // 8 DIP scaled
                                
                                // Top-half tiles (0x7F) need to be positioned at the bottom of the tile space
                                if (IsTopHalfTile(tileIdx))
                                {
                                    double yOffset = dipHeight - scaledHeight; // Position at bottom
                                    dc.DrawImage(customTile, new Rect(0, yOffset, dipWidth, scaledHeight));
                                }
                                else
                                {
                                    // Bottom-half tiles (0x04) stay at the top
                                    dc.DrawImage(customTile, new Rect(0, 0, dipWidth, scaledHeight));
                                }
                            }
                            else
                            {
                                // Full-height tiles (16x16 source) use the entire tile space
                                dc.DrawImage(customTile, new Rect(0, 0, dipWidth, dipHeight));
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
                    // For a small set of tiles we must apply player-color replacement
                    // to the green-ish pixels (while preserving the tile-toned colors
                    // for non-green pixels). Use the helper that composes base + toned.
                    if (IsPlayerReplacementTile(tileIdx) && playerTintEnabled)
                    {
                        ImageSource? baseSrc = (tileIdx < tileImages.Length) ? tileImages[tileIdx] as ImageSource : null;
                        ImageSource? tonedSrc = (tileTonedImages != null && tileIdx < tileTonedImages.Length) ? tileTonedImages[tileIdx] as ImageSource : null;
                        src = CreatePlayerReplacedFromBaseAndToned(baseSrc, tonedSrc ?? baseSrc, playerTint);
                    }
                    else
                    {
                        // Prefer tinted tiles when available
                        src = (tileTonedImages != null && tileIdx < tileTonedImages.Length)
                            ? tileTonedImages[tileIdx] as ImageSource
                            : (tileIdx < tileImages.Length ? tileImages[tileIdx] as ImageSource : null);
                    }
                }
                
                // Debug: log if specific tiles fail to get source
                if (src == null && (tileIdx == 34 || tileIdx == 36))
                {
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Tile {tileIdx} has NULL source! tileImages.Length={tileImages?.Length}, tileTonedImages.Length={tileTonedImages?.Length}");
                }
                
                if (src != null)
                {
                    var dv = new DrawingVisual();
                    // Convert pixel dimensions to device-independent units for proper rendering
                    double dipWidth = tilePixelW / dpi.DpiScaleX;
                    double dipHeight = tilePixelH / dpi.DpiScaleY;
                    using (var dc = dv.RenderOpen()) dc.DrawImage(src, new Rect(0, 0, dipWidth, dipHeight));
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
            
            // Calculate pixel position using same method as grid for consistency
            int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
            int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);
            int destX = Math.Max(0, padPxX + x * tilePixelW);
            int destY = Math.Max(0, padPxY + y * tilePixelH);

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

        // Simple wrapper to update a single sprite at position (x, y)
        private void UpdateSpriteBitmapAt(int x, int y, double scale, double pad)
        {
            if (spritesWb == null || sprites == null) return;
            
            var dpi = VisualTreeHelper.GetDpi(this);
            int spritePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int spritePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
            
            int spriteIdx = sprites[y * mapWidth + x];
            
            // Lock the bitmap for direct pixel manipulation
            spritesWb.Lock();
            
            try
            {
                // If sprite is empty (-1), clear that position in the bitmap
                if (spriteIdx < 0)
                {
                    // Calculate pixel position
                    int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
                    int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);
                    int destX = Math.Max(0, padPxX + x * spritePixelW);
                    int destY = Math.Max(0, padPxY + y * spritePixelH);
                    
                    // Clear pixels directly in the back buffer
                    IntPtr pBackBuffer = spritesWb.BackBuffer;
                    if (pBackBuffer == IntPtr.Zero) return;
                    int backBufferStride = spritesWb.BackBufferStride;
                    int clearWidth = Math.Min(spritePixelW, cachedPixelWidth - destX);
                    int clearHeight = Math.Min(spritePixelH, cachedPixelHeight - destY);
                    
                    unsafe
                    {
                        // Clear the area first
                        for (int row = 0; row < clearHeight; row++)
                        {
                            long destOffset = (destY + row) * backBufferStride + destX * 4;
                            byte* destPtr = (byte*)pBackBuffer.ToPointer() + destOffset;
                            
                            for (int col = 0; col < clearWidth * 4; col++)
                            {
                                destPtr[col] = 0; // Set to transparent
                            }
                        }
                        
                        // In preview mode, check if there's a portal nearby that extends into this position
                        // and render that portal section
                        if (previewMode)
                        {
                            // Check tiles around this position to see if any contain a portal sprite
                            // A portal is 2x3 tiles, so we need to check positions that could have portals extending here
                            for (int checkY = Math.Max(0, y - 2); checkY <= Math.Min(mapHeight - 1, y); checkY++)
                            {
                                for (int checkX = Math.Max(0, x - 1); checkX <= Math.Min(mapWidth - 1, x); checkX++)
                                {
                                    int checkIdx = checkY * mapWidth + checkX;
                                    int checkSpriteId = sprites[checkIdx];
                                    
                                    if (IsPortalSprite(checkSpriteId))
                                    {
                                        int portalPosKey = checkY * mapWidth + checkX;
                                        var portalSprite = GetPortalSpriteForId(checkSpriteId, portalPosKey);
                                        if (portalSprite == null) continue;
                                        
                                        // Found a portal! Calculate which part of it overlaps with our current tile
                                        int portalDestX = Math.Max(0, padPxX + checkX * spritePixelW);
                                        int portalDestY = Math.Max(0, padPxY + checkY * spritePixelH);
                                        int portalRenderWidth = spritePixelW;
                                        int portalRenderHeight = spritePixelH;
                                        int portalAnimatedIdx = GetAnimatedSpriteIndex(checkSpriteId);
                                        // Extend portal mapping up through 3029 like elsewhere so speed portals are handled
                                        bool portalIsMulti = (portalAnimatedIdx >= 3000 && portalAnimatedIdx <= 3029);

                                        // Apply preview-mode vertical nudges for selected portal types (skip 0.5x/1x/special)
                                        if (previewMode)
                                        {
                                            if (checkSpriteId == 0x16)
                                            {
                                                int nudgePixels = (int)Math.Round(6.0 * dpi.DpiScaleY);
                                                portalDestY = Math.Max(0, portalDestY - nudgePixels);
                                            }
                                            else if (checkSpriteId == 0x20 || checkSpriteId == 0x21)
                                            {
                                                int nudgePixels = (int)Math.Round(6.0 * dpi.DpiScaleY);
                                                portalDestY = Math.Max(0, portalDestY - nudgePixels);
                                            }
                                        }

                                        if (portalIsMulti)
                                        {
                                            // Standard tall portals (3000-3010) are 1.5 tiles × 3 tiles
                                            if (portalAnimatedIdx >= 3000 && portalAnimatedIdx <= 3010)
                                            {
                                                portalRenderWidth = (spritePixelW * 3) / 2;  // 1.5 tiles wide
                                                portalRenderHeight = spritePixelH * 3;      // 3 tiles tall
                                            }
                                            // Horizontal gravity portals (3011-3014 and 3019-3023) are 3 tiles × 2 tiles
                                            else if ((portalAnimatedIdx >= 3011 && portalAnimatedIdx <= 3014) || (portalAnimatedIdx >= 3019 && portalAnimatedIdx <= 3023))
                                            {
                                                portalRenderWidth = spritePixelW * 3;
                                                portalRenderHeight = spritePixelH * 2;
                                            }
                                            // 3x/4x speed portals (3027/3028): 2 tiles tall, width based on source PNG scaling
                                            else if (portalAnimatedIdx == 3027 || portalAnimatedIdx == 3028)
                                            {
                                                portalRenderWidth = Math.Min(cachedPixelWidth, (int)Math.Round(spritePixelW * (double)portalSprite.PixelWidth / (double)TileSize));
                                                portalRenderHeight = spritePixelH * 2;
                                            }
                                            // 2x preview (3026): 1.5 tiles wide × 2 tiles tall
                                            else if (portalAnimatedIdx == 3026)
                                            {
                                                portalRenderWidth = (spritePixelW * 3) / 2;
                                                portalRenderHeight = spritePixelH * 2;
                                            }
                                            // 0.5x, 1x, special (3024/3025/3029): single tile wide, tall height
                                            else if (portalAnimatedIdx == 3024 || portalAnimatedIdx == 3025 || portalAnimatedIdx == 3029)
                                            {
                                                portalRenderWidth = spritePixelW;
                                                portalRenderHeight = spritePixelH * 3;
                                            }
                                            else
                                            {
                                                // Fallback to standard tall portal sizing
                                                portalRenderWidth = (spritePixelW * 3) / 2;
                                                portalRenderHeight = spritePixelH * 3;
                                            }
                                        }
                                        
                                        // Calculate the intersection between the portal and our current tile
                                        int intersectLeft = Math.Max(destX, portalDestX);
                                        int intersectTop = Math.Max(destY, portalDestY);
                                        int intersectRight = Math.Min(destX + spritePixelW, portalDestX + portalRenderWidth);
                                        int intersectBottom = Math.Min(destY + spritePixelH, portalDestY + portalRenderHeight);
                                        
                                        if (intersectRight > intersectLeft && intersectBottom > intersectTop)
                                        {
                                            // There's an intersection - render this part of the portal
                                            int portalSrcWidth = portalSprite.PixelWidth;
                                            int portalSrcHeight = portalSprite.PixelHeight;
                                            int portalSrcStride = portalSrcWidth * 4;
                                            byte[] portalPixels = new byte[portalSrcHeight * portalSrcStride];
                                            portalSprite.CopyPixels(portalPixels, portalSrcStride, 0);
                                            
                                            double portalScaleX = (double)portalRenderWidth / portalSrcWidth;
                                            double portalScaleY = (double)portalRenderHeight / portalSrcHeight;
                                            
                                            for (int py = intersectTop; py < intersectBottom; py++)
                                            {
                                                for (int px = intersectLeft; px < intersectRight; px++)
                                                {
                                                    // Map back to portal source coordinates
                                                    int offsetX = px - portalDestX;
                                                    int offsetY = py - portalDestY;
                                                    int portalSrcX = Math.Min((int)(offsetX / portalScaleX), portalSrcWidth - 1);
                                                    int portalSrcY = Math.Min((int)(offsetY / portalScaleY), portalSrcHeight - 1);
                                                    int portalSrcOffset = portalSrcY * portalSrcStride + portalSrcX * 4;
                                                    
                                                    long portalDestOffset = py * backBufferStride + px * 4;
                                                    byte* portalDestPtr = (byte*)pBackBuffer.ToPointer() + portalDestOffset;
                                                    
                                                    portalDestPtr[0] = portalPixels[portalSrcOffset + 0]; // B
                                                    portalDestPtr[1] = portalPixels[portalSrcOffset + 1]; // G
                                                    portalDestPtr[2] = portalPixels[portalSrcOffset + 2]; // R
                                                    portalDestPtr[3] = portalPixels[portalSrcOffset + 3]; // A
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    // Mark the dirty region
                    spritesWb.AddDirtyRect(new Int32Rect(destX, destY, clearWidth, clearHeight));
                }
                else
                {
                    // Update the sprite using the locked version
                    UpdateSpriteBitmapAtLocked(x, y, spriteIdx, scale, pad, spritePixelW, spritePixelH, dpi);
                    
                    // Mark the dirty region. Compute the same visual nudges and render size
                    // as the locked renderer so the dirty rect covers the actual pixels written.
                    int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
                    int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);
                    int destX = Math.Max(0, padPxX + x * spritePixelW);
                    int destY = Math.Max(0, padPxY + y * spritePixelH);

                    // Determine animated/custom index to compute multi-tile or chain sizes
                    int animatedIdx = GetAnimatedSpriteIndex(spriteIdx);
                    int renderWidth = spritePixelW;
                    int renderHeight = spritePixelH;
                    bool isMultiTilePortal = (animatedIdx >= 3000 && animatedIdx <= 3029);
                    if (isMultiTilePortal)
                    {
                        if (animatedIdx >= 3000 && animatedIdx <= 3010)
                        {
                            renderWidth = (spritePixelW * 3) / 2;
                            renderHeight = spritePixelH * 3;
                        }
                        else
                        {
                            renderWidth = spritePixelW * 3;
                            renderHeight = spritePixelH * 2;
                        }
                    }
                    // Chains (custom indices 2126/2127) are taller (1.5 tiles)
                    if (!isMultiTilePortal && (animatedIdx == 2126 || animatedIdx == 2127))
                    {
                        renderHeight = (spritePixelH * 3) / 2;
                    }

                    // Apply the same preview-mode nudges used by the locked renderer
                    if (previewMode && spriteIdx == 0x2D)
                    {
                        double oneTileScaled = TileSize * (ZoomSlider != null ? ZoomSlider.Value : 1.0) * dpi.DpiScaleY;
                        int totalShift = (int)Math.Round(oneTileScaled * 1.5);
                        destY = Math.Max(0, destY - totalShift);
                    }

                    int dirtyWidth = Math.Min(renderWidth, cachedPixelWidth - destX);
                    int dirtyHeight = Math.Min(renderHeight, Math.Max(0, cachedPixelHeight - destY));
                    spritesWb.AddDirtyRect(new Int32Rect(destX, destY, dirtyWidth, dirtyHeight));
                }
            }
            finally
            {
                spritesWb.Unlock();
            }
            // Ensure overlay reflects any change to this single sprite cell
            try { UpdateIncompatibleOverlay(); } catch { }
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
            
            // Calculate pixel position using same method as grid for consistency
            int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
            int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);
            int destX = Math.Max(0, padPxX + x * tilePixelW);
            int destY = Math.Max(0, padPxY + y * tilePixelH);
            
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

        private async void RebuildAllSpritesBitmap(double scale, double pad)
        {
            if (spritesWb == null || spriteImages == null) return;
            
            try
            {
                // Ensure portals layer is built for entire map before drawing sprites (only if preview mode is enabled)
                if (previewMode && portalsWb != null)
                {
                    RebuildPortalsRegion(0, 0, mapWidth - 1, mapHeight - 1, scale, pad);
                }
                var dpi = VisualTreeHelper.GetDpi(this);
                int spritePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                int spritePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                
                // Clear the bitmap first
                await Dispatcher.InvokeAsync(() =>
                {
                    if (spritesWb == null) return;
                    spritesWb.Lock();
                    try
                    {
                        unsafe
                        {
                            IntPtr pBackBuffer = spritesWb.BackBuffer;
                            if (pBackBuffer == IntPtr.Zero) return;
                            int backBufferStride = spritesWb.BackBufferStride;
                            int bytesTotal = backBufferStride * cachedPixelHeight;
                            byte* ptr = (byte*)pBackBuffer.ToPointer();
                            for (int i = 0; i < bytesTotal; i++)
                            {
                                ptr[i] = 0;
                            }
                        }
                        spritesWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
                    }
                    finally
                    {
                        spritesWb.Unlock();
                    }
                });
                
                
                // Render sprites in batches
                int totalSprites = mapWidth * mapHeight;
                bool isLargeMap = totalSprites > 50000;
                int batchSize = isLargeMap ? Math.Max(2000, mapWidth * 2) : Math.Max(500, mapWidth);
                
                System.Diagnostics.Debug.WriteLine($"RebuildAllSpritesBitmap: {totalSprites} sprites, batchSize={batchSize}, isLargeMap={isLargeMap}");
                
                for (int batchStart = 0; batchStart < totalSprites; batchStart += batchSize)
                {
                    int batchEnd = Math.Min(batchStart + batchSize, totalSprites);
                    
                    // Update UI on main thread for this batch
                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (spritesWb == null) return;
                        spritesWb.Lock();
                        try
                        {
                            for (int i = batchStart; i < batchEnd; i++)
                            {
                                int x = i % mapWidth;
                                int y = i / mapWidth;
                                int idx = sprites[y * mapWidth + x];
                                if (idx >= 0 && idx < spriteImages.Length)
                                {
                                    try
                                    {
                                        if (idx == 0x17 || idx == 0x4B || idx == 0x58 || idx == 0x08 || idx == 0x09)
                                        {
                                            // (debug logging removed)
                                        }
                                        UpdateSpriteBitmapAtLocked(x, y, idx, scale, pad, spritePixelW, spritePixelH, dpi);
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"    Error at sprite ({x},{y}): {ex.Message}");
                                    }
                                }
                            }
                            
                            // Mark this batch area as dirty. Expand the top by one tile to account
                            // for preview-mode vertical nudges (e.g. chains that hang upward by 1 tile).
                            int minY = batchStart / mapWidth;
                            int maxY = (batchEnd - 1) / mapWidth;
                            // Include one extra tile above the batch to capture sprites shifted upward
                            int dirtyTopTile = Math.Max(0, minY - 1);
                            int dirtyHeight = (maxY - dirtyTopTile + 1) * spritePixelH;
                            int dirtyTopPx = (int)(dirtyTopTile * spritePixelH + pad * dpi.DpiScaleY);
                            spritesWb.AddDirtyRect(new Int32Rect(0, dirtyTopPx, cachedPixelWidth, Math.Min(dirtyHeight, cachedPixelHeight - dirtyTopPx)));
                        }
                        finally
                        {
                            spritesWb.Unlock();
                        }
                    }, System.Windows.Threading.DispatcherPriority.Background);
                }
                
                System.Diagnostics.Debug.WriteLine($"RebuildAllSpritesBitmap: Complete");
                // After rebuilding the full sprites layer, update the incompatibility overlay
                try { Dispatcher.Invoke(() => UpdateIncompatibleOverlay()); } catch { }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EXCEPTION in RebuildAllSpritesBitmap: {ex.Message}");
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
                throw;
            }
        }

        // Rebuild portal region - used to update only the affected portion of portalsWb
        // Fields used to debounce/queue portal region rebuilds so fast painting/dragging
        // operations do not trigger many repeated small RebuildPortalsRegion() calls.
        private int portalDirtyMinX = int.MaxValue;
        private int portalDirtyMinY = int.MaxValue;
        private int portalDirtyMaxX = int.MinValue;
        private int portalDirtyMaxY = int.MinValue;
        private bool portalDirtyScheduled = false;
        private System.Windows.Threading.DispatcherTimer? portalDirtyTimer = null;

        private void EnsurePortalDirtyTimer()
        {
            if (portalDirtyTimer != null) return;
            portalDirtyTimer = new System.Windows.Threading.DispatcherTimer();
            portalDirtyTimer.Interval = TimeSpan.FromMilliseconds(40); // small debounce window
            portalDirtyTimer.Tick += (s, e) =>
            {
                var t = portalDirtyTimer; // capture to local to satisfy nullable analysis
                if (t == null) return;
                t.Stop();
                portalDirtyScheduled = false;
                // Copy and reset
                int minX = portalDirtyMinX;
                int minY = portalDirtyMinY;
                int maxX = portalDirtyMaxX;
                int maxY = portalDirtyMaxY;
                portalDirtyMinX = int.MaxValue;
                portalDirtyMinY = int.MaxValue;
                portalDirtyMaxX = int.MinValue;
                portalDirtyMaxY = int.MinValue;
                if (minX <= maxX && minY <= maxY)
                {
                    try { RebuildPortalsRegion(minX, minY, maxX, maxY, (ZoomSlider != null ? ZoomSlider.Value : 1.0), mapViewportPadding); } catch { }
                }
            };
        }

        private void QueueRebuildPortalsRegion(int minX, int minY, int maxX, int maxY)
        {
            // Merge request
            portalDirtyMinX = Math.Min(portalDirtyMinX, minX);
            portalDirtyMinY = Math.Min(portalDirtyMinY, minY);
            portalDirtyMaxX = Math.Max(portalDirtyMaxX, maxX);
            portalDirtyMaxY = Math.Max(portalDirtyMaxY, maxY);
            EnsurePortalDirtyTimer();
            if (!portalDirtyScheduled && portalDirtyTimer != null)
            {
                portalDirtyScheduled = true;
                portalDirtyTimer.Start();
            }
        }

        private void RebuildPortalsRegion(int minX, int minY, int maxX, int maxY, double scale, double pad)
        {
            if (portalsWb == null) return;
            if (spriteImages == null) return;
            if (spriteImages == null) return;
            if (spriteImages == null) return;
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                int spritePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                int spritePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                // Clamp region
                minX = Math.Max(0, minX);
                minY = Math.Max(0, minY);
                maxX = Math.Min(mapWidth - 1, maxX);
                maxY = Math.Min(mapHeight - 1, maxY);

                // Convert tile coords to pixel rect in padded pixels
                int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
                int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);
                int pxLeft = padPxX + minX * spritePixelW;
                int pxTop = padPxY + minY * spritePixelH;
                int pxRight = padPxX + (maxX + 1) * spritePixelW;
                int pxBottom = padPxY + (maxY + 1) * spritePixelH;

                int widthPx = Math.Max(0, Math.Min(cachedPixelWidth - pxLeft, pxRight - pxLeft));
                int heightPx = Math.Max(0, Math.Min(cachedPixelHeight - pxTop, pxBottom - pxTop));

                if (widthPx <= 0 || heightPx <= 0) return;

                // Clear the region (make it transparent)
                Dispatcher.Invoke(() =>
                {
                    portalsWb.Lock();
                    try
                    {
                        unsafe
                        {
                            IntPtr pBackBuffer = portalsWb.BackBuffer;
                            if (pBackBuffer == IntPtr.Zero) return;
                            int stride = portalsWb.BackBufferStride;
                            for (int row = 0; row < heightPx; row++)
                            {
                                long destOffset = (pxTop + row) * stride + pxLeft * 4;
                                byte* ptr = (byte*)pBackBuffer.ToPointer() + destOffset;
                                for (int col = 0; col < widthPx; col++)
                                {
                                    ptr[col * 4 + 0] = 0;
                                    ptr[col * 4 + 1] = 0;
                                    ptr[col * 4 + 2] = 0;
                                    ptr[col * 4 + 3] = 0;
                                }
                            }
                        }
                        portalsWb.AddDirtyRect(new Int32Rect(pxLeft, pxTop, widthPx, heightPx));
                    }
                    finally { portalsWb.Unlock(); }
                });

                // Now find any portal anchors that may affect this area and redraw them
                // A portal anchor at (ax, ay) occupies tiles ax..ax+1, ay..ay+2
                int checkMinX = Math.Max(0, minX - 1);
                int checkMaxX = Math.Min(mapWidth - 1, maxX);
                int checkMinY = Math.Max(0, minY - 2);
                int checkMaxY = Math.Min(mapHeight - 1, maxY);

                for (int ay = checkMinY; ay <= checkMaxY; ay++)
                {
                    for (int ax = checkMinX; ax <= checkMaxX; ax++)
                    {
                        int idx = sprites[ay * mapWidth + ax];
                        if (IsPortalSprite(idx))
                        {
                            // Determine portal bounds in tiles. Different portal types occupy different tile footprints:
                            // - Standard tall portals: 1.5 tiles wide × 3 tiles tall  (covers ax..ax+1, ay..ay+2)
                            // - Horizontal gravity portals: 3 tiles wide × 2 tiles tall (covers ax..ax+2, ay..ay+1)
                            // - New dual/single portals (3015/3016): treat like standard tall portals (1.5×3)
                            int portalAnimatedIdx = GetAnimatedSpriteIndex(idx);
                            int px1 = ax;
                            int py1 = ay;
                            int px2 = ax + 1; // inclusive right tile (default for tall portals)
                            int py2 = ay + 2; // inclusive bottom tile (default for tall portals)

                            // Horizontal gravity portals are wider and shorter
                            if ((portalAnimatedIdx >= 3011 && portalAnimatedIdx <= 3014) || (portalAnimatedIdx >= 3019 && portalAnimatedIdx <= 3023))
                            {
                                px2 = ax + 2; // 3 tiles wide
                                py2 = ay + 1; // 2 tiles tall
                            }
                            // New dual/single portals (3015/3016) should be treated as tall portals (1.5×3)
                            else if (portalAnimatedIdx == 3015 || portalAnimatedIdx == 3016)
                            {
                                px2 = ax + 1;
                                py2 = ay + 2;
                            }

                            // Intersection test with requested dirty region
                            if (px2 < minX || px1 > maxX || py2 < minY || py1 > maxY) continue; // no intersection

                            // Render this portal anchor into portalsWb
                            Dispatcher.Invoke(() =>
                            {
                                UpdatePortalBitmapAtLocked(ax, ay, idx, scale, pad, spritePixelW, spritePixelH, dpi);
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EXCEPTION in RebuildPortalsRegion: {ex.Message}");
            }
        }

        // Immediately clear a rectangular tile region in the sprites writeable bitmap (pixel-space).
        // Used to avoid ghost pixels when moving selections by zeroing affected pixels before committing changes.
        private void ClearSpritesBitmapTileRect(int minX, int minY, int maxX, int maxY, double scale, double pad)
        {
            if (spritesWb == null) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            int spritePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int spritePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));

            // Clamp region
            minX = Math.Max(0, minX);
            minY = Math.Max(0, minY);
            maxX = Math.Min(mapWidth - 1, maxX);
            maxY = Math.Min(mapHeight - 1, maxY);

            int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
            int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);
            int pxLeft = padPxX + minX * spritePixelW;
            int pxTop = padPxY + minY * spritePixelH;
            int pxRight = padPxX + (maxX + 1) * spritePixelW;
            int pxBottom = padPxY + (maxY + 1) * spritePixelH;

            int widthPx = Math.Max(0, Math.Min(cachedPixelWidth - pxLeft, pxRight - pxLeft));
            int heightPx = Math.Max(0, Math.Min(cachedPixelHeight - pxTop, pxBottom - pxTop));
            if (widthPx <= 0 || heightPx <= 0) return;

            Dispatcher.Invoke(() =>
            {
                spritesWb.Lock();
                try
                {
                    unsafe
                    {
                        IntPtr pBackBuffer = spritesWb.BackBuffer;
                        if (pBackBuffer == IntPtr.Zero) return;
                        int stride = spritesWb.BackBufferStride;
                        for (int row = 0; row < heightPx; row++)
                        {
                            long destOffset = (pxTop + row) * stride + pxLeft * 4;
                            byte* ptr = (byte*)pBackBuffer.ToPointer() + destOffset;
                            for (int col = 0; col < widthPx; col++)
                            {
                                ptr[col * 4 + 0] = 0;
                                ptr[col * 4 + 1] = 0;
                                ptr[col * 4 + 2] = 0;
                                ptr[col * 4 + 3] = 0;
                            }
                        }
                    }
                    spritesWb.AddDirtyRect(new Int32Rect(pxLeft, pxTop, widthPx, heightPx));
                }
                finally { spritesWb.Unlock(); }
            });
        }

        // Immediately clear a rectangular tile region in the portals writeable bitmap (pixel-space).
        private void ClearPortalsBitmapTileRect(int minX, int minY, int maxX, int maxY, double scale, double pad)
        {
            if (portalsWb == null) return;
            var dpi = VisualTreeHelper.GetDpi(this);
            int spritePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int spritePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));

            // Clamp region
            minX = Math.Max(0, minX);
            minY = Math.Max(0, minY);
            maxX = Math.Min(mapWidth - 1, maxX);
            maxY = Math.Min(mapHeight - 1, maxY);

            int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
            int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);
            int pxLeft = padPxX + minX * spritePixelW;
            int pxTop = padPxY + minY * spritePixelH;
            int pxRight = padPxX + (maxX + 1) * spritePixelW;
            int pxBottom = padPxY + (maxY + 1) * spritePixelH;

            int widthPx = Math.Max(0, Math.Min(cachedPixelWidth - pxLeft, pxRight - pxLeft));
            int heightPx = Math.Max(0, Math.Min(cachedPixelHeight - pxTop, pxBottom - pxTop));
            if (widthPx <= 0 || heightPx <= 0) return;

            Dispatcher.Invoke(() =>
            {
                portalsWb.Lock();
                try
                {
                    unsafe
                    {
                        IntPtr pBackBuffer = portalsWb.BackBuffer;
                        if (pBackBuffer == IntPtr.Zero) return;
                        int stride = portalsWb.BackBufferStride;
                        for (int row = 0; row < heightPx; row++)
                        {
                            long destOffset = (pxTop + row) * stride + pxLeft * 4;
                            byte* ptr = (byte*)pBackBuffer.ToPointer() + destOffset;
                            for (int col = 0; col < widthPx; col++)
                            {
                                ptr[col * 4 + 0] = 0;
                                ptr[col * 4 + 1] = 0;
                                ptr[col * 4 + 2] = 0;
                                ptr[col * 4 + 3] = 0;
                            }
                        }
                    }
                    portalsWb.AddDirtyRect(new Int32Rect(pxLeft, pxTop, widthPx, heightPx));
                }
                finally { portalsWb.Unlock(); }
            });
        }

        // Background precompute for tinted decoration sprites to reduce work during rebuild
        private Task PrecomputeTintedCachesAsync()
        {
            if (!playerTintEnabled) return Task.CompletedTask;
            // Capture current tint to avoid races
            var tint = playerTint;
            return Task.Run(() =>
            {
                try
                {
                    uint argb = (uint)((tint.A << 24) | (tint.R << 16) | (tint.G << 8) | (tint.B));
                    foreach (var id in decorationSpriteIds)
                    {
                        try
                        {
                            if (spriteImages == null) continue;
                            if (id < 0 || id >= spriteImages.Length) continue;
                            long key = (((long)id) << 32) | argb;
                            lock (tintedCacheLock)
                            {
                                if (tintedSpriteCache.TryGetValue(key, out var existing) && existing != null) continue;
                            }

                            var src = spriteImages[id] as BitmapSource;
                            if (src == null) continue;

                            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                            int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
                            if (w <= 0 || h <= 0) continue;
                            var pixels = new byte[h * stride];
                            conv.CopyPixels(pixels, stride, 0);

                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                byte b = pixels[i + 0];
                                byte g = pixels[i + 1];
                                byte r = pixels[i + 2];
                                byte a = pixels[i + 3];
                                if (a != 0 && !(r == 0 && g == 0 && b == 0))
                                {
                                    pixels[i + 0] = tint.B;
                                    pixels[i + 1] = tint.G;
                                    pixels[i + 2] = tint.R;
                                }
                            }

                            var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                            wb.Freeze();

                            lock (tintedCacheLock)
                            {
                                tintedSpriteCache.Put(key, wb);
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PrecomputeTintedCachesAsync error: {ex.Message}");
                }
            });
        }

        

        private void UpdateSpriteBitmapAtLocked(int x, int y, int spriteIdx, double scale, double pad, int spritePixelW, int spritePixelH, DpiScale dpi)
        {
            if (spritesWb == null || spriteImages == null) return;
            
            // Bounds check on original index
            if (spriteImages == null) return;
            if (spriteImages == null) return;
            if (spriteImages == null) return;
            if (spriteImages == null) return;
            if (spriteIdx < 0 || spriteIdx >= spriteImages.Length) return;
            
            // Set the position key for random frame offsets (unique per position on map)
            currentSpritePositionKey = y * mapWidth + x;
            
                        // Debug: Log when we're updating an orb or coin (periodic)
                            if ((spriteIdx == 0x0B || spriteIdx == 0x1F || spriteIdx == 0x29 || // Yellow
                  spriteIdx == 0x05 || // Blue
                  spriteIdx == 0x06 || // Pink
                  spriteIdx == 0x27 || // Green
                  spriteIdx == 0x28 || // Red
                  spriteIdx == 0x44 || // Black
                  spriteIdx == 0x7A || // White
                  spriteIdx == 0x07 || spriteIdx == 0x1A || spriteIdx == 0x1B || // Coins
                                    spriteIdx == 0x36 || spriteIdx == 0x49 || spriteIdx == 0x4A) && animationFrame % 60 == 0)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateSpriteBitmapAtLocked: Updating animated sprite 0x{spriteIdx:X2} at ({x},{y}), previewMode={previewMode}");
            }
            
            try
            {
                // Calculate pixel position using same method as grid for consistency
                int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
                int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);
                int destX = Math.Max(0, padPxX + x * spritePixelW);
                int destY = Math.Max(0, padPxY + y * spritePixelH);

                // Shift certain decoration previews up so they visually hang from above.
                // Chains: normal chain (0x2D) continues to hang a bit higher (1.5 tiles shift).
                // The upside-down chain (0x3D) previously nudged up by 1 tile (16px); remove
                // that upward nudge so 0x3D renders at its natural tile origin instead.
                if (previewMode && spriteIdx == 0x2D)
                {
                    double oneTileScaled = TileSize * scale * dpi.DpiScaleY; // e.g. 16*scale*dpi
                    // Use a smaller hang offset so the chain doesn't move up too far.
                    // Previously 1.5 tiles; reduce to 0.5 tiles to correct over-shift.
                    int totalShift = (int)Math.Round(oneTileScaled * 0.5);
                    destY = Math.Max(0, destY - totalShift);
                }
                // Shift pole-medium (sprite 0x2B) upwards by half a tile so it visually aligns
                // with surrounding decorations when previewing.
                if (previewMode && spriteIdx == 0x2B)
                {
                    try
                    {
                        int halfShift = (int)Math.Round(spritePixelH * 0.5);
                        destY = Math.Max(0, destY - halfShift);
                    }
                    catch { }
                }

                // Shift pole-long (sprite 0x2A) upwards by one full tile in preview mode
                if (previewMode && spriteIdx == 0x2A)
                {
                    try
                    {
                        int oneTilePx = spritePixelH; // tile height in pixels at current scale/DPI
                        destY = Math.Max(0, destY - oneTilePx);
                    }
                    catch { }
                }

                // Small nudge for 3x/4x speed preview portals (sprite 0x20/0x21)
                if (previewMode && (spriteIdx == 0x20 || spriteIdx == 0x21))
                {
                    try
                    {
                        int nudgePixels = (int)Math.Round(6.0 * dpi.DpiScaleY);
                        destY = Math.Max(0, destY - nudgePixels);
                    }
                    catch { }
                }

                // Teleport horizontal portals 0x67/0x68 should be shifted up by one tile
                if (previewMode && (spriteIdx == 0x67 || spriteIdx == 0x68))
                {
                    try
                    {
                        int oneTilePx = spritePixelH; // tile height in pixels at current scale/DPI
                        destY = Math.Max(0, destY - oneTilePx);
                    }
                    catch { }
                }
                
                // Bounds check
                if (destX >= cachedPixelWidth || destY >= cachedPixelHeight) return;
                
                // Get the source sprite - check for animation
                int animatedIdx = GetAnimatedSpriteIndex(spriteIdx);
                BitmapSource? sprite = null;
                
                // Debug output for sprite 0x00
                if (spriteIdx == 0x00)
                {
                    System.Diagnostics.Debug.WriteLine($"Sprite 0x00: animatedIdx={animatedIdx}, previewMode={previewMode}");
                }

                // (no logging)
                
                // Check if this is a custom animated sprite
                if (animatedIdx >= 2000)
                {
                    // Special-case: rainbow portal (original sprite 0x64) should be
                    // provided by GetPortalSpriteForId so it can start at a per-position
                    // randomized frame and cycle through the ordered portal images.
                    if (spriteIdx == 0x64)
                    {
                        // currentSpritePositionKey was set earlier in this method
                        sprite = GetPortalSpriteForId(spriteIdx, currentSpritePositionKey);
                    }
                    else
                    {
                        // Request the custom animation sprite
                        sprite = GetCustomAnimationSprite(animatedIdx);
                    }

                    // Lightweight diagnostic: sample first pixel of pad frames to detect per-frame changes
                    if (sprite != null && (spriteIdx == 0x52 || spriteIdx == 0x53 || spriteIdx == 0x0A || spriteIdx == 0x0C || spriteIdx == 0x0D || spriteIdx == 0x0E || spriteIdx == 0x25 || spriteIdx == 0x26 || spriteIdx == 0x7A || spriteIdx == 0x07 || spriteIdx == 0x1A || spriteIdx == 0x1B || spriteIdx == 0x36 || spriteIdx == 0x49 || spriteIdx == 0x4A))
                    {
                        try
                        {
                            // Only log periodically to avoid spamming the debug output
                            if (animationFrame % 30 == 0)
                            {
                                byte[] sample = new byte[4];
                                sprite.CopyPixels(new Int32Rect(0, 0, Math.Max(1, Math.Min(1, sprite.PixelWidth)), Math.Max(1, Math.Min(1, sprite.PixelHeight))), sample, 4, 0);
                                string sampleHex = BitConverter.ToString(sample);
                                System.Diagnostics.Debug.WriteLine($"PAD_SAMPLE ({x},{y}) sprite=0x{spriteIdx:X2} animatedIdx={animatedIdx} sample={sampleHex}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"PAD_SAMPLE error at ({x},{y}) sprite=0x{spriteIdx:X2}: {ex.Message}");
                        }
                    }
                    
                    if (spriteIdx == 0x00)
                    {
                        System.Diagnostics.Debug.WriteLine($"Sprite 0x00: customSprite={(sprite != null ? $"{sprite.PixelWidth}x{sprite.PixelHeight}" : "null")}");
                    }
                }
                
                // Fall back to normal sprite if not animated or animation not loaded
                if (sprite == null)
                {
                    sprite = spriteImages[spriteIdx] as BitmapSource;
                }

                // If player tinting is enabled and this sprite is a decoration, try to obtain a cached tinted bitmap
                BitmapSource? cachedTinted = null;
                if (previewMode && playerTintEnabled && decorationSpriteIds.Contains(spriteIdx) && sprite != null)
                {
                    // Build a 64-bit cache key composed of id and ARGB
                    uint argb = (uint)((playerTint.A << 24) | (playerTint.R << 16) | (playerTint.G << 8) | (playerTint.B));
                    long key = (((long)(animatedIdx >= 2000 ? animatedIdx : spriteIdx)) << 32) | argb;
                    var cache = (animatedIdx >= 2000) ? tintedCustomCache : tintedSpriteCache;
                    if (cache.TryGetValue(key, out var found) && found != null)
                    {
                        cachedTinted = found;
                        sprite = cachedTinted;
                    }
                    else
                    {
                        // create tinted source and insert into cache
                        try
                        {
                            var conv = new FormatConvertedBitmap(sprite, PixelFormats.Bgra32, null, 0);
                            int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
                            var pixels = new byte[h * stride];
                            conv.CopyPixels(pixels, stride, 0);
                            // Apply tint to non-black, non-transparent pixels
                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                byte b = pixels[i + 0];
                                byte g = pixels[i + 1];
                                byte r = pixels[i + 2];
                                byte a = pixels[i + 3];
                                if (a != 0 && !(r == 0 && g == 0 && b == 0))
                                {
                                    pixels[i + 0] = playerTint.B;
                                    pixels[i + 1] = playerTint.G;
                                    pixels[i + 2] = playerTint.R;
                                    // keep alpha
                                }
                            }
                            var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                            wb.Freeze();
                            cache[key] = wb;
                            sprite = wb;
                            
                        }
                        catch { /* ignore tint cache failures, fallback to runtime tint code below */ }
                    }
                }
                
                if (sprite == null) return;
                // If preview-mode hiding of color triggers is enabled and this sprite is such a trigger,
                // skip rendering so it behaves as if disappeared.
                if (previewMode && hideColorTriggers && IsColorTriggerSprite(spriteIdx)) return;
                // If preview-mode hiding of invisible sprites is enabled and this sprite is in that set,
                // skip rendering so it behaves as if disappeared.
                if (previewMode && hideInvisibleSprites && IsInvisibleSprite(spriteIdx)) return;
                
                // Check if this is a multi-tile portal sprite (portal sprites use indices 3000-3039)
                // Extend the range to include the new custom mappings for horizontal teleport portals
                bool isMultiTilePortal = (animatedIdx >= 3000 && animatedIdx <= 3039);
                int renderHeight = spritePixelH;
                int renderWidth = spritePixelW;

                if (isMultiTilePortal)
                {
                    // Different portals have different tile dimensions. Standard portals (3000-3010)
                    // are 1.5 tiles wide x 3 tiles tall. The new horizontal gravity portals
                    // (3011-3014) are wider horizontally and shorter vertically (3 tiles wide x 2 tiles tall).
                    if (animatedIdx >= 3000 && animatedIdx <= 3010)
                    {
                        renderWidth = (spritePixelW * 3) / 2;  // 1.5 tiles wide
                        renderHeight = spritePixelH * 3;      // 3 tiles tall
                    }
                    else
                    {
                        // Horizontal gravity portals: 3 tiles wide, 2 tiles tall
                        // Special-case: horizontal teleport portals (3030-3033) are 3 tiles wide x 1.5 tiles tall
                        if (animatedIdx >= 3030 && animatedIdx <= 3033)
                        {
                            renderWidth = spritePixelW * 3;                 // 3 tiles wide
                            renderHeight = (spritePixelH * 3) / 2;          // 1.5 tiles tall
                        }
                        else
                        {
                            // Horizontal gravity portals: 3 tiles wide, 2 tiles tall
                            renderWidth = spritePixelW * 3;
                            renderHeight = spritePixelH * 2;
                        }
                    }
                }

                // Treat chain decorations (custom indices 2126/2127) as 1.5 tiles tall
                if (!isMultiTilePortal && (animatedIdx == 2126 || animatedIdx == 2127))
                {
                    renderHeight = (spritePixelH * 3) / 2; // 1.5 tiles tall
                }

                // Treat pole-medium decorations for sprites 0x2B/0x3B (custom indices 2136-2139)
                // as 1.5 tiles tall (vertical) and 1 tile wide.
                if (!isMultiTilePortal && (animatedIdx == 2136 || animatedIdx == 2137 || animatedIdx == 2138 || animatedIdx == 2139))
                {
                    renderHeight = (spritePixelH * 3) / 2; // 1.5 tiles tall
                    renderWidth = spritePixelW; // 1 tile wide
                }

                // Treat pole-long decorations for sprites 0x2A/0x3A (custom indices 2140-2143)
                // as 2 tiles tall (vertical) and 1 tile wide.
                if (!isMultiTilePortal && (animatedIdx == 2140 || animatedIdx == 2141 || animatedIdx == 2142 || animatedIdx == 2143))
                {
                    renderHeight = spritePixelH * 2; // 2 tiles tall
                    renderWidth = spritePixelW; // 1 tile wide
                }

                // Treat medium pole decorations (custom indices 2132/2133 and 2134/2135)
                // as 1.5 tiles tall. Additionally, the left-medium pole (original sprite 0x3E)
                // should be anchored so its right side aligns with the anchor tile; compute
                // a desired destination X based on source width so it expands leftwards.
                int desiredDestX = destX; // may be adjusted below for left-medium
                if (!isMultiTilePortal && (animatedIdx == 2132 || animatedIdx == 2133 || animatedIdx == 2134 || animatedIdx == 2135))
                {
                    // Medium poles are 1 tile tall and 1.5 tiles wide
                    renderWidth = (spritePixelW * 3) / 2; // 1.5 tiles wide
                    renderHeight = spritePixelH; // 1 tile tall
                    // For left-medium (sprite 0x3E) align the right side with anchor tile
                    if (spriteIdx == 0x3E)
                    {
                        // Align right side: desiredDestX will be computed after we know the source width
                    }
                    else if (spriteIdx == 0x3F)
                    {
                        // Right-medium keeps normal anchor (left of tile)
                        desiredDestX = destX;
                    }
                }
                
                // Calculate the actual size we need to render
                int srcWidth = sprite.PixelWidth;
                int srcHeight = sprite.PixelHeight;

                // If this is the left-medium pole, we can now compute desiredDestX using the actual source width
                if (!isMultiTilePortal && (animatedIdx == 2132 || animatedIdx == 2133 || animatedIdx == 2134 || animatedIdx == 2135) && spriteIdx == 0x3E)
                {
                    try
                    {
                        // Shift left by half a tile relative to the anchor tile
                        desiredDestX = destX - (int)Math.Round(spritePixelW * 0.5);
                    }
                    catch { desiredDestX = destX; }
                }
                
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
                
                IntPtr pBackBuffer = spritesWb.BackBuffer;
                if (pBackBuffer == IntPtr.Zero) return;
                int backBufferStride = spritesWb.BackBufferStride;

                // To support sprites that may expand left of the anchor (negative desiredDestX),
                // compute a drawing X and a source X start offset. drawX is clamped to >=0
                // and srcXStart is the number of source columns to skip when destX < 0.
                int drawX = Math.Max(0, desiredDestX);
                int srcXStart = drawX - desiredDestX; // zero when desiredDestX >= 0

                // Clear the rendering area
                // For portals: clear their full area
                // For regular sprites: clear only their tile, but also re-render any portal underneath first
                unsafe
                {
                    int clearWidth = Math.Min(renderWidth - srcXStart, Math.Max(0, cachedPixelWidth - drawX));
                    int clearHeight = Math.Min(renderHeight, Math.Max(0, cachedPixelHeight - destY));
                    if (clearWidth <= 0 || clearHeight <= 0) return;

                        // If this is a regular sprite and portals exist underneath, copy portal pixels into spritesWb first
                        // so that animated sprites (orbs) can clear/re-render each frame correctly while preserving portals.
                        if (!isMultiTilePortal && previewMode && portalsWb != null)
                        {
                            try
                            {
                                int portalStride = portalsWb.BackBufferStride;
                                if (portalStride <= 0) throw new Exception("invalid portal stride");
                                byte[] portalBuf = new byte[clearHeight * portalStride];
                                Int32Rect srcRect = new Int32Rect(drawX, destY, clearWidth, clearHeight);
                                portalsWb.CopyPixels(srcRect, portalBuf, portalStride, 0);

                                // Copy portal pixels into spritesWb back buffer
                                IntPtr spritesBackBuffer = spritesWb.BackBuffer;
                                int spritesBackBufferStride = spritesWb.BackBufferStride;
                                if (spritesBackBuffer != IntPtr.Zero && spritesBackBufferStride > 0)
                                {
                                    unsafe
                                    {
                                        int maxRowBytes = Math.Max(0, spritesBackBufferStride - drawX * 4);
                                        for (int row = 0; row < clearHeight; row++)
                                        {
                                            long destOffset = (destY + row) * spritesBackBufferStride + drawX * 4;
                                            byte* destPtr = (byte*)spritesBackBuffer.ToPointer() + destOffset;
                                            int srcRowOffset = row * portalStride;
                                            int copyBytes = Math.Min(clearWidth * 4, portalBuf.Length - srcRowOffset);
                                            copyBytes = Math.Min(copyBytes, maxRowBytes);
                                            if (copyBytes <= 0) continue;
                                            for (int b = 0; b < copyBytes; b++)
                                            {
                                                destPtr[b] = portalBuf[srcRowOffset + b];
                                            }
                                        }
                                    }
                                }
                            }
                            catch { /* swallow portal copy failures to avoid crashing the UI */ }
                        }
                    
                    
                    
                    // Only clear the area if we're rendering a portal OR there's no portal underneath
                    // For regular sprites over portals, we want to preserve the portal and composite on top
                    // When not in preview mode, we should clear tiles fully (so sprites don't ghost)
                    // When in preview mode, preserve portal pixels under sprites to avoid destroying portal visuals
                    bool shouldClear = true;
                    if (isMultiTilePortal)
                    {
                        // Portals own their entire area; always clear and write into portal layer
                        shouldClear = true;
                    }
                    else if (previewMode)
                    {
                        // In preview mode, if there's a portal underneath, don't clear - otherwise, clear
                        bool portalUnderneath = false;
                        for (int checkY = Math.Max(0, y - 2); checkY <= Math.Min(mapHeight - 1, y) && !portalUnderneath; checkY++)
                        {
                            for (int checkX = Math.Max(0, x - 1); checkX <= Math.Min(mapWidth - 1, x) && !portalUnderneath; checkX++)
                            {
                                if (checkX == x && checkY == y) continue;
                                int checkSpriteId = sprites[checkY * mapWidth + checkX];
                                if (IsPortalSprite(checkSpriteId))
                                {
                                    portalUnderneath = true;
                                }
                            }
                        }
                        shouldClear = !portalUnderneath;
                    }
                    
                    // Clear the area only if needed
                    if (shouldClear)
                    {
                        try
                        {
                            int maxRowPixels = Math.Max(0, (backBufferStride / 4) - drawX);
                            int rowsToClear = Math.Min(clearHeight, Math.Max(0, cachedPixelHeight - destY));
                            int colsToClear = Math.Min(clearWidth, Math.Max(0, cachedPixelWidth - drawX));
                            for (int row = 0; row < rowsToClear; row++)
                            {
                                long destOffset = (destY + row) * backBufferStride + drawX * 4;
                                byte* destPtr = (byte*)pBackBuffer.ToPointer() + destOffset;
                                int pixelsThisRow = Math.Min(colsToClear, maxRowPixels);
                                if (pixelsThisRow <= 0) continue;
                                for (int col = 0; col < pixelsThisRow; col++)
                                {
                                    int pixelOffset = col * 4;
                                    destPtr[pixelOffset + 0] = 0; // B
                                    destPtr[pixelOffset + 1] = 0; // G
                                    destPtr[pixelOffset + 2] = 0; // R
                                    destPtr[pixelOffset + 3] = 0; // A (transparent)
                                }
                            }
                        }
                        catch { /* swallow clearing failures */ }
                    }
                    
                    // Portals are now rendered into a dedicated `portalsWb` layer beneath sprites; sprites do not need
                    // to re-render portal pixels underneath. This keeps portal rendering persistent and simpler.
                }
                
                // If this is a portal multi-tile sprite, draw it into the portal layer and return (portals should live on their own layer)
                if (isMultiTilePortal && portalsWb != null)
                {
                    UpdatePortalBitmapAtLocked(x, y, spriteIdx, scale, pad, spritePixelW, spritePixelH, dpi);
                    return;
                }
                // If no scaling needed and sprite is already correct size, copy directly
                // Direct copy fast-path: only use for non-custom, single-tile sprites at 1x scale/DPI.
                // Custom preview sprites (animatedIdx >= 2000) must go through the scaling branch
                // so they can be rendered at 1.5 tiles tall when appropriate (e.g. chains).
                    if (Math.Abs(scale - 1.0) < 0.001 && Math.Abs(dpi.DpiScaleX - 1.0) < 0.001 && srcWidth == TileSize && !isMultiTilePortal && animatedIdx < 2000)
                {
                    // Direct copy - no scaling (only for single-tile sprites)
                    int copyWidth = Math.Min(srcWidth, cachedPixelWidth - destX);
                    int copyHeight = Math.Min(srcHeight, cachedPixelHeight - destY);
                    
                    unsafe
                    {
                        for (int row = 0; row < copyHeight; row++)
                        {
                            int srcOffset = row * srcStride;
                            long destOffset = (destY + row) * backBufferStride + destX * 4;
                            byte* destPtr = (byte*)pBackBuffer.ToPointer() + destOffset;
                            
                            // Use alpha compositing: only overwrite pixels where sprite has opacity
                            // This preserves portal pixels underneath transparent areas of the orb
                            for (int col = 0; col < copyWidth; col++)
                            {
                                int pixelOffset = col * 4;
                                byte srcAlpha = srcPixels[srcOffset + pixelOffset + 3];
                                
                                if (srcAlpha == 255)
                                {
                                    // Fully opaque - direct copy (with optional tint for decoration sprites)
                                    byte srcB = srcPixels[srcOffset + pixelOffset + 0];
                                    byte srcG = srcPixels[srcOffset + pixelOffset + 1];
                                    byte srcR = srcPixels[srcOffset + pixelOffset + 2];

                                    if (previewMode && playerTintEnabled && decorationSpriteIds.Contains(spriteIdx) && !(srcR == 0 && srcG == 0 && srcB == 0))
                                    {
                                        destPtr[pixelOffset + 0] = playerTint.B; // B
                                        destPtr[pixelOffset + 1] = playerTint.G; // G
                                        destPtr[pixelOffset + 2] = playerTint.R; // R
                                        destPtr[pixelOffset + 3] = srcPixels[srcOffset + pixelOffset + 3]; // A
                                    }
                                    else
                                    {
                                        destPtr[pixelOffset + 0] = srcB; // B
                                        destPtr[pixelOffset + 1] = srcG; // G
                                        destPtr[pixelOffset + 2] = srcR; // R
                                        destPtr[pixelOffset + 3] = srcPixels[srcOffset + pixelOffset + 3]; // A
                                    }
                                }
                                else if (srcAlpha > 0)
                                {
                                    // Semi-transparent - alpha blend with existing pixel
                                    byte destAlpha = destPtr[pixelOffset + 3];
                                    
                                    // Read source color and apply tint if needed
                                    byte srcB = srcPixels[srcOffset + pixelOffset + 0];
                                    byte srcG = srcPixels[srcOffset + pixelOffset + 1];
                                    byte srcR = srcPixels[srcOffset + pixelOffset + 2];

                                    bool applyTint = previewMode && playerTintEnabled && decorationSpriteIds.Contains(spriteIdx) && !(srcR == 0 && srcG == 0 && srcB == 0);
                                    if (applyTint)
                                    {
                                        srcB = playerTint.B;
                                        srcG = playerTint.G;
                                        srcR = playerTint.R;
                                    }

                                    if (destAlpha == 0)
                                    {
                                        // Destination is transparent, just copy source
                                        destPtr[pixelOffset + 0] = srcB; // B
                                        destPtr[pixelOffset + 1] = srcG; // G
                                        destPtr[pixelOffset + 2] = srcR; // R
                                        destPtr[pixelOffset + 3] = srcPixels[srcOffset + pixelOffset + 3]; // A
                                    }
                                    else
                                    {
                                        // Both have opacity - proper alpha compositing
                                        float srcA = srcAlpha / 255.0f;
                                        float dstA = destAlpha / 255.0f;
                                        float outA = srcA + dstA * (1 - srcA);
                                        
                                        if (outA > 0)
                                        {
                                            destPtr[pixelOffset + 0] = (byte)((srcB * srcA + destPtr[pixelOffset + 0] * dstA * (1 - srcA)) / outA);
                                            destPtr[pixelOffset + 1] = (byte)((srcG * srcA + destPtr[pixelOffset + 1] * dstA * (1 - srcA)) / outA);
                                            destPtr[pixelOffset + 2] = (byte)((srcR * srcA + destPtr[pixelOffset + 2] * dstA * (1 - srcA)) / outA);
                                            destPtr[pixelOffset + 3] = (byte)(outA * 255);
                                        }
                                    }
                                }
                                // If srcAlpha == 0, don't modify destination (preserve portal underneath)
                            }
                        }
                    }
                }
                else
                {
                    // Need to scale - use simple nearest-neighbor scaling to avoid TransformedBitmap issues
                    double scaleX = (double)renderWidth / srcWidth;
                    double scaleY = (double)renderHeight / srcHeight;
                    
                    int copyWidth = Math.Min(renderWidth, cachedPixelWidth - destX);
                    int copyHeight = Math.Min(renderHeight, cachedPixelHeight - destY);
                    
                    if (isMultiTilePortal)
                    {
                        // (debug logging removed)
                    }
                    
                    unsafe
                    {
                        for (int row = 0; row < copyHeight; row++)
                        {
                            for (int col = 0; col < copyWidth; col++)
                            {
                                // Map destination pixel back to source pixel (nearest neighbor)
                                int srcX = Math.Min((int)((col + srcXStart) / scaleX), srcWidth - 1);
                                int srcY = Math.Min((int)(row / scaleY), srcHeight - 1);
                                int srcOffset = srcY * srcStride + srcX * 4;
                                
                                long destOffset = (destY + row) * backBufferStride + (drawX + col) * 4;
                                byte* destPtr = (byte*)pBackBuffer.ToPointer() + destOffset;
                                
                                byte srcAlpha = srcPixels[srcOffset + 3];
                                
                                if (isMultiTilePortal)
                                {
                                    // Portals: direct copy (they own their entire area)
                                    destPtr[0] = srcPixels[srcOffset + 0]; // B
                                    destPtr[1] = srcPixels[srcOffset + 1]; // G
                                    destPtr[2] = srcPixels[srcOffset + 2]; // R
                                    destPtr[3] = srcPixels[srcOffset + 3]; // A
                                }
                                else if (srcAlpha == 255)
                                {
                                    // Fully opaque - direct copy (with optional tint for decoration sprites)
                                    byte srcB = srcPixels[srcOffset + 0];
                                    byte srcG = srcPixels[srcOffset + 1];
                                    byte srcR = srcPixels[srcOffset + 2];

                                    if (previewMode && playerTintEnabled && decorationSpriteIds.Contains(spriteIdx) && !(srcR == 0 && srcG == 0 && srcB == 0))
                                    {
                                        destPtr[0] = playerTint.B; // B
                                        destPtr[1] = playerTint.G; // G
                                        destPtr[2] = playerTint.R; // R
                                        destPtr[3] = srcPixels[srcOffset + 3]; // A
                                    }
                                    else
                                    {
                                        destPtr[0] = srcB; // B
                                        destPtr[1] = srcG; // G
                                        destPtr[2] = srcR; // R
                                        destPtr[3] = srcPixels[srcOffset + 3]; // A
                                    }
                                }
                                else if (srcAlpha > 0)
                                {
                                    // Semi-transparent - alpha blend with existing pixel
                                    byte destAlpha = destPtr[3];

                                    // Read source color and apply tint if needed
                                    byte srcB = srcPixels[srcOffset + 0];
                                    byte srcG = srcPixels[srcOffset + 1];
                                    byte srcR = srcPixels[srcOffset + 2];

                                    bool applyTint = previewMode && playerTintEnabled && decorationSpriteIds.Contains(spriteIdx) && !(srcR == 0 && srcG == 0 && srcB == 0);
                                    if (applyTint)
                                    {
                                        srcB = playerTint.B;
                                        srcG = playerTint.G;
                                        srcR = playerTint.R;
                                    }

                                    if (destAlpha == 0)
                                    {
                                        // Destination is transparent, just copy source
                                        destPtr[0] = srcB; // B
                                        destPtr[1] = srcG; // G
                                        destPtr[2] = srcR; // R
                                        destPtr[3] = srcPixels[srcOffset + 3]; // A
                                    }
                                    else
                                    {
                                        // Both have opacity - proper alpha compositing
                                        float srcA = srcAlpha / 255.0f;
                                        float dstA = destAlpha / 255.0f;
                                        float outA = srcA + dstA * (1 - srcA);

                                        if (outA > 0)
                                        {
                                            destPtr[0] = (byte)((srcB * srcA + destPtr[0] * dstA * (1 - srcA)) / outA);
                                            destPtr[1] = (byte)((srcG * srcA + destPtr[1] * dstA * (1 - srcA)) / outA);
                                            destPtr[2] = (byte)((srcR * srcA + destPtr[2] * dstA * (1 - srcA)) / outA);
                                            destPtr[3] = (byte)(outA * 255);
                                        }
                                    }
                                }
                                // If srcAlpha == 0, don't modify destination (preserve portal underneath)
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

        // Render a portal anchor sprite into the portal Wb (does not affect spritesWb)
        private void UpdatePortalBitmapAtLocked(int x, int y, int spriteIdx, double scale, double pad, int spritePixelW, int spritePixelH, DpiScale dpi)
        {
            if (spriteImages == null) return;
            if (portalsWb == null) return;
            if (spriteIdx < 0 || spriteIdx >= spriteImages.Length) return;
            if (!IsPortalSprite(spriteIdx)) return;

            int portalPosKey = y * mapWidth + x;
            var portalSprite = GetPortalSpriteForId(spriteIdx, portalPosKey);
            if (portalSprite == null)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Portal sprite null for idx=0x{spriteIdx:X2} at ({x},{y})");
                return;
            }

            // Read source portal image dimensions and pixels early so we can choose proper render sizing
            int srcWidth = portalSprite.PixelWidth;
            int srcHeight = portalSprite.PixelHeight;
            if (srcWidth <= 0 || srcHeight <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Portal sprite invalid size for idx=0x{spriteIdx:X2}: {srcWidth}x{srcHeight}");
                return;
            }
            int srcStride = srcWidth * 4;
            byte[] srcPixels = new byte[srcHeight * srcStride];
            portalSprite.CopyPixels(srcPixels, srcStride, 0);

            // Calculate pixel position
            int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
            int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);
            int destX = Math.Max(0, padPxX + x * spritePixelW);
            int destY = Math.Max(0, padPxY + y * spritePixelH);

            // Special-case: in preview mode, certain horizontal gravity portal previews
            // should be visually shifted up by one tile (16px) to align correctly.
            // Apply for sprite IDs 0x10 and 0x12.
            if (previewMode && (spriteIdx == 0x10 || spriteIdx == 0x12))
            {
                destY = Math.Max(0, destY - spritePixelH);
            }
            // Horizontal teleport portal previews 0x67/0x68 are designed to be shifted
            // up by one tile so they visually align with the surrounding tiles.
            if (previewMode && (spriteIdx == 0x67 || spriteIdx == 0x68))
            {
                destY = Math.Max(0, destY - spritePixelH);
            }
            // Speed portal previews: most should start one tile higher so they visually hang
            // from the tile above similar to other portal previews. However, the 3x/4x
            // speed previews (sprite 0x20 and 0x21) render better when shifted down
            // by one tile instead of up. Keep other speed previews shifted up.
            if (previewMode)
            {
                // Keep nudges for 2x and 3x/4x portals only; skip 0.5x/1x/special (0x14/0x15/0x6D)
                if (spriteIdx == 0x16 || spriteIdx == 0x14 || spriteIdx == 0x15 || spriteIdx == 0x6D)
                {
                    // 2x, and also 0.5x/1x/special speed portals: nudge up 6 logical pixels, scaled by DPI
                    int nudgePixels = (int)Math.Round(6.0 * dpi.DpiScaleY);
                    destY = Math.Max(0, destY - nudgePixels);
                }
                else if (spriteIdx == 0x20 || spriteIdx == 0x21)
                {
                    // 3x/4x speed portals: small nudge up of 6 logical pixels, scaled by DPI
                    int nudgePixels = (int)Math.Round(6.0 * dpi.DpiScaleY);
                    destY = Math.Max(0, destY - nudgePixels);
                }
            }

            int renderWidth = spritePixelW;
            int renderHeight = spritePixelH;
            int portalAnimatedIdx = GetAnimatedSpriteIndex(spriteIdx);
            // Treat custom portal indices 3000+ as multi-tile portals (include newly-added 3030-3033)
            bool portalIsMulti = (portalAnimatedIdx >= 3000 && portalAnimatedIdx <= 3039);
            // Special-case for 3x/4x speed portals (3027/3028): they should be two tiles tall
            // and their horizontal pixel width should follow the source PNG width rather than
            // being forced to a single tile width.
            bool portalIsSpeed34 = (portalAnimatedIdx == 3027 || portalAnimatedIdx == 3028);
            if (portalIsMulti)
            {
                // Standard tall portals (3000-3010) are 1.5 tiles × 3 tiles
                if (portalAnimatedIdx >= 3000 && portalAnimatedIdx <= 3010)
                {
                    renderWidth = (spritePixelW * 3) / 2; // 1.5 tiles wide
                    renderHeight = spritePixelH * 3;
                }
                // Horizontal gravity portals (3011-3014) are 3 tiles × 2 tiles
                else if (portalAnimatedIdx >= 3011 && portalAnimatedIdx <= 3014)
                {
                    renderWidth = spritePixelW * 3;
                    renderHeight = spritePixelH * 2;
                }
                // 3x and 4x speed portals (3027/3028) are only 2 tiles tall; size horizontally to source PNG
                else if (portalIsSpeed34)
                {
                    renderWidth = Math.Min(cachedPixelWidth, (int)Math.Round(spritePixelW * (double)srcWidth / (double)TileSize));
                    renderHeight = spritePixelH * 2;
                }
                // Horizontal teleport portals (3030-3033) are 3 tiles wide × 1.5 tiles tall
                else if (portalAnimatedIdx >= 3030 && portalAnimatedIdx <= 3033)
                {
                    renderWidth = spritePixelW * 3; // 3 tiles wide
                    renderHeight = (spritePixelH * 3) / 2; // 1.5 tiles tall
                }
                // 0.5x and 1x and special speed previews (3024/3025/3029) should only occupy
                // a single tile horizontally but keep the tall height. The 2x preview (3026)
                // is an exception: it should be 1.5 tiles wide and 2 tiles tall.
                else if (portalAnimatedIdx == 3026)
                {
                    renderWidth = (spritePixelW * 3) / 2; // 1.5 tiles wide
                    renderHeight = spritePixelH * 2; // 2 tiles tall
                }
                else if (portalAnimatedIdx == 3024 || portalAnimatedIdx == 3025 || portalAnimatedIdx == 3029)
                {
                    // 0.5x, 1x and special speed portals: render as single tile wide by 2 tiles tall
                    renderWidth = spritePixelW; // single tile wide
                    renderHeight = spritePixelH * 2; // 2 tiles tall
                }
                // New dual/single portals (3015,3016): treat like standard tall portals (1.5×3)
                else
                {
                    renderWidth = (spritePixelW * 3) / 2;
                    renderHeight = spritePixelH * 3;
                }
            }

            

            int copyWidth = Math.Min(renderWidth, cachedPixelWidth - destX);
            int copyHeight = Math.Min(renderHeight, cachedPixelHeight - destY);

            // Lock the portal WB for the duration of this write
            portalsWb.Lock();
            try
            {
                unsafe
                {
                    IntPtr pBackBuffer = portalsWb.BackBuffer;
                    if (pBackBuffer == IntPtr.Zero) return;
                    int backBufferStride = portalsWb.BackBufferStride;
                for (int row = 0; row < copyHeight; row++)
                {
                    for (int col = 0; col < copyWidth; col++)
                    {
                        int srcX = Math.Min((int)(col * srcWidth / (double)renderWidth), srcWidth - 1);
                        int srcY = Math.Min((int)(row * srcHeight / (double)renderHeight), srcHeight - 1);
                        int srcOffset = srcY * srcStride + srcX * 4;

                        long destOffset = (destY + row) * backBufferStride + (destX + col) * 4;
                        byte* destPtr = (byte*)pBackBuffer.ToPointer() + destOffset;

                        byte srcB = srcPixels[srcOffset + 0];
                        byte srcG = srcPixels[srcOffset + 1];
                        byte srcR = srcPixels[srcOffset + 2];
                        byte srcA = srcPixels[srcOffset + 3];

                        byte dstB = destPtr[0];
                        byte dstG = destPtr[1];
                        byte dstR = destPtr[2];
                        byte dstA = destPtr[3];

                        if (srcA == 0)
                        {
                            // nothing to do, preserve existing pixel
                            continue;
                        }
                        else if (srcA == 255 || dstA == 0)
                        {
                            // fully opaque or dest transparent, overwrite
                            destPtr[0] = srcB;
                            destPtr[1] = srcG;
                            destPtr[2] = srcR;
                            destPtr[3] = srcA;
                        }
                        else
                        {
                            float sA = srcA / 255.0f;
                            float dA = dstA / 255.0f;
                            float outA = sA + dA * (1 - sA);
                            if (outA <= 0.0f)
                            {
                                destPtr[0] = 0;
                                destPtr[1] = 0;
                                destPtr[2] = 0;
                                destPtr[3] = 0;
                            }
                            else
                            {
                                destPtr[0] = (byte)((srcB * sA + dstB * dA * (1 - sA)) / outA);
                                destPtr[1] = (byte)((srcG * sA + dstG * dA * (1 - sA)) / outA);
                                destPtr[2] = (byte)((srcR * sA + dstR * dA * (1 - sA)) / outA);
                                destPtr[3] = (byte)(outA * 255);
                            }
                        }
                    }
                }
            }
            }
            finally
            {
                if (portalsWb != null)
                {
                    try { portalsWb.AddDirtyRect(new Int32Rect(destX, destY, copyWidth, copyHeight)); } catch { }
                    try { portalsWb.Unlock(); } catch { }
                }
            }
        }

        // Return true if a sprite id is considered a color-trigger decoration to be hidden when the
        // "Hide Color Triggers" preview option is active.
        private bool IsColorTriggerSprite(int spriteIdx)
        {
            // Ranges: 0x80-0x8C, 0x8F, 0x90-0x9C, 0x9F, 0xA0-0xAC, 0xAE-0xAF, 0xB0-0xBF, 0xC0-0xCC, 0xCF, 0xD0-0xDC, 0xE0-0xEC
            if (spriteIdx >= 0x80 && spriteIdx <= 0x8C) return true;
            if (spriteIdx == 0x8F) return true;
            if (spriteIdx >= 0x90 && spriteIdx <= 0x9C) return true;
            if (spriteIdx == 0x9F) return true;
            if (spriteIdx >= 0xA0 && spriteIdx <= 0xAC) return true;
            if (spriteIdx >= 0xAE && spriteIdx <= 0xAF) return true;
            if (spriteIdx >= 0xB0 && spriteIdx <= 0xBF) return true;
            if (spriteIdx >= 0xC0 && spriteIdx <= 0xCC) return true;
            if (spriteIdx == 0xCF) return true;
            if (spriteIdx >= 0xD0 && spriteIdx <= 0xDC) return true;
            if (spriteIdx >= 0xE0 && spriteIdx <= 0xEC) return true;
            return false;
        }

        // Return true if a sprite id should be considered 'invisible' for the
        // "Hide all invisible sprites" preview option. Includes explicit ids and ranges.
        private bool IsInvisibleSprite(int spriteIdx)
        {
            if (spriteIdx == 0x0F) return true;
            if (spriteIdx == 0x47 || spriteIdx == 0x48) return true;
            if (spriteIdx == 0x6F) return true;
            if (spriteIdx >= 0x70 && spriteIdx <= 0x78) return true;
            if (spriteIdx == 0x7D) return true;
            if (spriteIdx == 0x7F) return true;
            if (spriteIdx == 0x8E) return true;
            if (spriteIdx == 0x9E) return true;
            if (spriteIdx >= 0xDD && spriteIdx <= 0xDF) return true;
            if (spriteIdx >= 0xEE && spriteIdx <= 0xEF) return true;
            if (spriteIdx >= 0xF0 && spriteIdx <= 0xFC) return true;
            return false;
        }

        private void CanvasHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CanvasHost == null) return;
            var pos = e.GetPosition(CanvasHost);

            // Track mouse down position and reset movement flag
            mouseDownPosition = pos;
            // Record explicit click anchor (tile under the click) when inside map
            try
            {
                var ttClick = ViewportPointToTile(pos);
                if (IsPointInsideMap(pos)) { lastClickX = ttClick.x; lastClickY = ttClick.y; } else { lastClickX = -1; lastClickY = -1; }
            }
            catch { lastClickX = -1; lastClickY = -1; }
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
                // If a non-Tile draw mode is active allow deferred draw-based selection (circle/line/polygon/etc.)
                if (currentDrawMode != DrawMode.Tile)
                {
                    // fall through to deferred-draw handling below
                }
                else
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
            }
            // Move tool: begin dragging if we have an existing selection, otherwise pick tile/sprite under cursor
            if (MoveTool != null && MoveTool.IsChecked == true)
            {
                var t = ViewportPointToTile(pos);
                int x = t.x; int y = t.y;
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
                var t = ViewportPointToTile(pos);
                int x = t.x; int y = t.y;
                if (x >= selX && x < selX + selW && y >= selY && y < selY + selH)
                {
                    // Erase on the active layers (tiles/sprites). Use unified handler so both layers
                    // are removed when both are active, or if no layer is marked active treat both as active.
                    EraseSelectedLayers();
                    return;
                }
            }

            // If a draw mode (other than Tile) is active, start deferred drawing or polygon construction
            if (currentDrawMode != DrawMode.Tile)
            {
                // debounce: ignore very quick repeated clicks after a recent commit
                if ((System.DateTime.Now - lastInputAction).TotalMilliseconds < 200) return;
                var tt = ViewportPointToTile(pos);
                // Polygon mode: add vertex on click, finalize on double-click
                if (currentDrawMode == DrawMode.Polygon)
                {
                        // Only add vertex if click is inside the map area
                        if (!IsPointInsideMap(pos)) { return; }
                        polygonPoints.Add((tt.x, tt.y));
                    isConstructingPolygon = true;
                    drawCurrentX = tt.x; drawCurrentY = tt.y;
                    if (CanvasHost != null) CanvasHost.CaptureMouse();
                    UpdateDeferredPreview();
                    if (e.ClickCount >= 2 && polygonPoints.Count >= 3)
                    {
                            CommitPolygon();
                    }
                    return;
                }

                // Other shapes: start drag-based deferred draw
                drawStartX = tt.x; drawStartY = tt.y; drawCurrentX = drawStartX; drawCurrentY = drawStartY;
                isDeferredDrawing = true;
                if (CanvasHost != null) CanvasHost.CaptureMouse();
                UpdateDeferredPreview();
                return;
            }

            // Default: place/erase painting behavior
            StartPaintingAt(pos);
        }

        private bool IsPointInsideMap(Point pos)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            double pad = mapViewportPadding;
            double mapW = mapWidth * TileSize * scale;
            double mapH = mapHeight * TileSize * scale;
            // pos is in CanvasHost DIU coordinates
            if (pos.X < pad || pos.Y < pad) return false;
            if (pos.X > pad + mapW || pos.Y > pad + mapH) return false;
            return true;
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
            
            // If we were doing a deferred draw, finalize it now
            if (isDeferredDrawing)
            {
                var pos = e.GetPosition(CanvasHost);
                var tt = ViewportPointToTile(pos);
                drawCurrentX = tt.x; drawCurrentY = tt.y;
                CommitDeferredDraw();
                return;
            }

            // If constructing polygon, ignore single mouse-up (points are added on mouse-down).
            if (isConstructingPolygon)
            {
                // do not finalize here; polygon finalized on double-click in mouse-down handler
                return;
            }

            // Handle single click - check if we should deselect
            if (selectionSet.Count > 0)
            {
                var pos = e.GetPosition(CanvasHost);
                var tt = ViewportPointToTile(pos);
                int x = tt.x; int y = tt.y;
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

            // If middle-button panning is active, handle scroll offsets and consume the move.
            try
            {
                if (isMiddlePanning && MapScrollViewer != null && e.MiddleButton == MouseButtonState.Pressed)
                {
                    var cur = e.GetPosition(MapScrollViewer);
                    double dx = cur.X - middlePanStart.X;
                    double dy = cur.Y - middlePanStart.Y;
                    double newH = panStartHOffset - dx;
                    double newV = panStartVOffset - dy;
                    // Clamp to valid ranges
                    if (newH < 0) newH = 0;
                    if (newV < 0) newV = 0;
                    try { MapScrollViewer.ScrollToHorizontalOffset(newH); } catch { }
                    try { MapScrollViewer.ScrollToVerticalOffset(newV); } catch { }
                    return;
                }
            }
            catch { }
            
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
                return;
            }

            // If performing a deferred draw, update preview
            if (isDeferredDrawing && e.LeftButton == MouseButtonState.Pressed)
            {
                var tt = ViewportPointToTile(pos);
                drawCurrentX = tt.x; drawCurrentY = tt.y;
                UpdateDeferredPreview();
                return;
            }

            // If constructing a polygon, update skeleton preview to follow mouse
            if (isConstructingPolygon && e.LeftButton == MouseButtonState.Pressed)
            {
                var tt = ViewportPointToTile(pos);
                drawCurrentX = tt.x; drawCurrentY = tt.y;
                UpdateDeferredPreview();
                return;
            }

            // Show ghost preview of selected tile when brush/tile are active
            try
            {
                // Prefer sprite ghost when sprite layer is active and a sprite is selected
                if (PlaceTool != null && PlaceTool.IsChecked == true && spritesLayerActive && selectedSprite >= 0 && spriteImages != null && GhostImage != null)
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
                    int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                    int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                    int padPxX = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
                    int padPxY = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
                    var tt = ViewportPointToTile(pos);
                    int tx = tt.x; int ty = tt.y;
                    if (tx >= 0 && ty >= 0)
                    {
                        int leftPx = padPxX + tx * tilePixelW;
                        int topPx = padPxY + ty * tilePixelH + gridRenderShiftYPx;
                        double left = (double)leftPx / dpi.DpiScaleX;
                        double top = (double)topPx / dpi.DpiScaleY;
                        // If showing sprite ghost, attempt to size according to sprite image natural size in tiles
                        ImageSource spriteSrc = spriteImages[selectedSprite];
                        double diuPerTileX = (double)tilePixelW / dpi.DpiScaleX;
                        double diuPerTileY = (double)tilePixelH / dpi.DpiScaleY;
                        int widthTiles = 1, heightTiles = 1;
                        if (spriteSrc is BitmapSource bs)
                        {
                            widthTiles = Math.Max(1, (int)Math.Round(bs.PixelWidth / (double)TileSize));
                            heightTiles = Math.Max(1, (int)Math.Round(bs.PixelHeight / (double)TileSize));
                        }
                        double widthDiu = widthTiles * diuPerTileX;
                        double heightDiu = heightTiles * diuPerTileY;
                        GhostImage.Source = spriteSrc;
                        try { System.Windows.Media.RenderOptions.SetBitmapScalingMode(GhostImage, BitmapScalingMode.NearestNeighbor); } catch { }
                        GhostImage.Width = widthDiu;
                        GhostImage.Height = heightDiu;
                        Canvas.SetLeft(GhostImage, left);
                        Canvas.SetTop(GhostImage, top);
                        GhostImage.Visibility = Visibility.Visible;
                        GhostImage.Opacity = 0.6;
                    }
                    else
                    {
                        GhostImage.Visibility = Visibility.Collapsed;
                    }
                }
                else if (PlaceTool != null && PlaceTool.IsChecked == true && DrawTileButton != null && DrawTileButton.IsChecked == true && selectedTile >= 0 && tileImages != null && GhostImage != null)
                {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
                    int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                    int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                    int padPxX = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
                    int padPxY = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
                    var tt = ViewportPointToTile(pos);
                    int tx = tt.x; int ty = tt.y;
                    if (tx >= 0 && ty >= 0)
                    {
                        int leftPx = padPxX + tx * tilePixelW;
                        int topPx = padPxY + ty * tilePixelH + gridRenderShiftYPx;
                        double left = (double)leftPx / dpi.DpiScaleX;
                        double top = (double)topPx / dpi.DpiScaleY;
                        double widthDiu = (double)tilePixelW / dpi.DpiScaleX;
                        double heightDiu = (double)tilePixelH / dpi.DpiScaleY;
                        GhostImage.Source = tileImages[selectedTile];
                        try { System.Windows.Media.RenderOptions.SetBitmapScalingMode(GhostImage, BitmapScalingMode.NearestNeighbor); } catch { }
                        GhostImage.Width = widthDiu;
                        GhostImage.Height = heightDiu;
                        Canvas.SetLeft(GhostImage, left);
                        Canvas.SetTop(GhostImage, top);
                        GhostImage.Visibility = Visibility.Visible;
                        GhostImage.Opacity = 0.6;
                    }
                    else
                    {
                        GhostImage.Visibility = Visibility.Collapsed;
                    }
                }
                else if (GhostImage != null)
                {
                    GhostImage.Visibility = Visibility.Collapsed;
                }
            }
            catch { if (GhostImage != null) GhostImage.Visibility = Visibility.Collapsed; }
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

            // When switching to certain tools, reset draw mode back to Tile by default
            // Include FillTool so selecting Fill also activates the Tile draw mode
            if (tb == MoveTool || tb == PlaceTool || tb == EraseTool || tb == MagicWandTool || tb == FillTool)
            {
                if (DrawTileButton != null) DrawTileButton.IsChecked = true;
                currentDrawMode = DrawMode.Tile;
            }

            // When Select, Move or MagicWand tools are active, grey-out (disable)
            // the non-tile drawing shape controls so only Tile remains usable.
            // Also disable shape tools when Fill is active
            bool disableShapes = (tb == SelectTool || tb == MoveTool || tb == MagicWandTool || tb == FillTool);
            if (DrawLineButton != null) DrawLineButton.IsEnabled = !disableShapes;
            if (DrawSquareButton != null) DrawSquareButton.IsEnabled = !disableShapes;
            if (DrawCircleButton != null) DrawCircleButton.IsEnabled = !disableShapes;
            if (DrawTriangleButton != null) DrawTriangleButton.IsEnabled = !disableShapes;
            if (DrawPolygonButton != null) DrawPolygonButton.IsEnabled = !disableShapes;
            if (HollowCheckBox != null) HollowCheckBox.IsEnabled = !disableShapes;
            if (BrushThicknessSlider != null) BrushThicknessSlider.IsEnabled = !disableShapes;
        }

        private void DrawModeButton_Checked(object? sender, RoutedEventArgs e)
        {
            // Keep only one draw mode checked at a time
            var btn = sender as ToggleButton;
            if (btn == null) return;
            var all = new[] { DrawTileButton, DrawLineButton, DrawSquareButton, DrawCircleButton, DrawTriangleButton, DrawPolygonButton };
            foreach (var b in all) if (b != btn) b.IsChecked = false;

            if (btn == DrawTileButton) currentDrawMode = DrawMode.Tile;
            else if (btn == DrawLineButton) currentDrawMode = DrawMode.Line;
            else if (btn == DrawSquareButton) currentDrawMode = DrawMode.Square;
            else if (btn == DrawCircleButton) currentDrawMode = DrawMode.Circle;
            else if (btn == DrawTriangleButton) currentDrawMode = DrawMode.Triangle;
            else if (btn == DrawPolygonButton) currentDrawMode = DrawMode.Polygon;
            else currentDrawMode = DrawMode.None;
        }

        private void DrawModeButton_Unchecked(object? sender, RoutedEventArgs e)
        {
            // If the unchecked button was the currently selected, revert to Tile
            var btn = sender as ToggleButton; if (btn == null) return;
            if (!(DrawTileButton.IsChecked == true || DrawLineButton.IsChecked == true || DrawSquareButton.IsChecked == true || DrawCircleButton.IsChecked == true || DrawTriangleButton.IsChecked == true || DrawPolygonButton.IsChecked == true))
            {
                if (DrawTileButton != null) DrawTileButton.IsChecked = true;
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

            try
            {
                var tt = ViewportPointToTile(pos);
                int x = tt.x, y = tt.y;
                if (x < 0 || y < 0) return;
                int idx = y * mapWidth + x;

                // Prefer picking a sprite if one exists at this location
                int spriteAt = (idx >= 0 && idx < sprites.Length) ? sprites[idx] : -1;
                int tileAt = (idx >= 0 && idx < tiles.Length) ? tiles[idx] : -1;

                bool pickedSomething = false;

                if (spriteAt >= 0)
                {
                    // If lock is enabled and this sprite is in the disabled list, ignore right-click selection
                    if (lockSpritesToSet && disabledSprites.Contains(spriteAt))
                    {
                        // do not pick this sprite
                    }
                    else
                    {
                        // pick sprite under cursor
                        selectedSprite = spriteAt;
                        selectedTile = -1;
                        spritesLayerActive = true;
                        tilesLayerActive = false;
                        // activate place tool for immediate placement
                        try { if (PlaceTool != null) PlaceTool.IsChecked = true; } catch { }
                        pickedSomething = true;
                    }
                }
                else if (tileAt >= 0)
                {
                    selectedTile = tileAt;
                    selectedTiles = new List<int> { tileAt };
                    selectedSprite = -1;
                    tilesLayerActive = true;
                    spritesLayerActive = false;
                    // Activate place tool and ensure tile draw mode
                    try { if (PlaceTool != null) PlaceTool.IsChecked = true; } catch { }
                    try { if (DrawTileButton != null) DrawTileButton.IsChecked = true; } catch { }
                    pickedSomething = true;
                }

                if (pickedSomething)
                {
                    UpdatePaletteHighlight();
                    try { if (CanvasHost != null) CanvasHost.Focus(); } catch { }
                    e.Handled = true;
                }
            }
            catch { }
        }

        private void CanvasHost_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (CanvasHost == null || MapScrollViewer == null) return;
            try
            {
                // Only start panning if middle button was pressed
                if (e.MiddleButton == MouseButtonState.Pressed)
                {
                    isMiddlePanning = true;
                    middlePanStart = e.GetPosition(MapScrollViewer);
                    panStartHOffset = MapScrollViewer.HorizontalOffset;
                    panStartVOffset = MapScrollViewer.VerticalOffset;
                    // Capture mouse so we receive move/up events outside the canvas
                    CanvasHost.CaptureMouse();
                    Mouse.OverrideCursor = Cursors.ScrollAll;
                    e.Handled = true;
                }
            }
            catch { }
        }

        private void CanvasHost_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (CanvasHost == null) return;
            try
            {
                // Stop panning on middle-button release
                if (isMiddlePanning && e.MiddleButton == MouseButtonState.Released)
                {
                    isMiddlePanning = false;
                    try { if (Mouse.Captured == CanvasHost) Mouse.Captured.ReleaseMouseCapture(); } catch { }
                    Mouse.OverrideCursor = null;
                    e.Handled = true;
                }
            }
            catch { }
        }

        private void StartPaintingAt(Point pos)
        {
            var tt = ViewportPointToTile(pos);
            int x = tt.x; int y = tt.y;

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
            var tt = ViewportPointToTile(pos);
            int x = tt.x; int y = tt.y;
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
                var ct = ViewportPointToTile(pos);
                int clickX = ct.x; int clickY = ct.y;
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
            
            // compute final destination tile coords from ghost position using integer-pixel math
            double left = Canvas.GetLeft(GhostImage);
            double top = Canvas.GetTop(GhostImage);
            var dpiGhost = VisualTreeHelper.GetDpi(this);
            int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpiGhost.DpiScaleX));
            int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpiGhost.DpiScaleY));
            int padPxX = (int)Math.Round(pad * dpiGhost.DpiScaleX);
            int padPxY = (int)Math.Round(pad * dpiGhost.DpiScaleY);
            int leftPx = (int)Math.Round((left) * dpiGhost.DpiScaleX);
            int topPx = (int)Math.Round((top) * dpiGhost.DpiScaleY);
            int destX = (leftPx - padPxX + tilePixelW/2) / tilePixelW;
            int destY = (topPx - padPxY + tilePixelH/2) / tilePixelH;
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
            var tt = ViewportPointToTile(pos);
            int x = tt.x; int y = tt.y;
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
            var dpi = VisualTreeHelper.GetDpi(this);
            // Use the same integer-pixel math as BuildGridBitmap so overlays align exactly
            int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
            int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
            int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);

            if (previewMode)
            {
                // During drag-selection, show a simple bounding rectangle preview
                    var rect = new Shapes.Rectangle
                {
                    Fill = SelectionFillBrush,
                        Stroke = SelectionStrokeBrush,
                        StrokeThickness = 1,
                    Width = (double)(w * tilePixelW) / dpi.DpiScaleX,
                    Height = (double)(h * tilePixelH) / dpi.DpiScaleY,
                    IsHitTestVisible = false
                };
                double left = (padPxX + x * tilePixelW) / dpi.DpiScaleX;
                double top = (padPxY + y * tilePixelH) / dpi.DpiScaleY + gridRenderShiftY;
                try { rect.StrokeThickness = 1.0 / dpi.DpiScaleX; rect.SnapsToDevicePixels = true; } catch { }
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
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
                        StrokeThickness = 1,
                        Width = (double)tilePixelW / dpi.DpiScaleX,
                        Height = (double)tilePixelH / dpi.DpiScaleY,
                        IsHitTestVisible = false
                    };
                    double left = (padPxX + tx * tilePixelW) / dpi.DpiScaleX;
                    double top = (padPxY + ty * tilePixelH) / dpi.DpiScaleY + gridRenderShiftY;
                    try { rect.StrokeThickness = 1.0 / dpi.DpiScaleX; rect.SnapsToDevicePixels = true; } catch { }
                    Canvas.SetLeft(rect, left);
                    Canvas.SetTop(rect, top);
                    SelectionOverlay.Children.Add(rect);
                }
            }
        }

        // Polygon helper: fill polygon tiles
        private System.Collections.Generic.List<(int x, int y)> GetFilledPolygonTiles(System.Collections.Generic.List<(int x, int y)> pts)
        {
            var list = new System.Collections.Generic.List<(int x, int y)>();
            if (pts == null || pts.Count < 3) return list;
            int minx = int.MaxValue, miny = int.MaxValue, maxx = int.MinValue, maxy = int.MinValue;
            foreach (var p in pts) { if (p.x < minx) minx = p.x; if (p.x > maxx) maxx = p.x; if (p.y < miny) miny = p.y; if (p.y > maxy) maxy = p.y; }
            minx = Math.Max(0, minx); miny = Math.Max(0, miny); maxx = Math.Min(mapWidth - 1, maxx); maxy = Math.Min(mapHeight - 1, maxy);
            for (int y = miny; y <= maxy; y++)
            {
                for (int x = minx; x <= maxx; x++)
                {
                    double px = x + 0.5; double py = y + 0.5;
                    if (PointInPolygon(px, py, pts)) list.Add((x, y));
                }
            }
            return list;
        }

        private bool PointInPolygon(double px, double py, System.Collections.Generic.List<(int x, int y)> pts)
        {
            bool inside = false;
            int n = pts.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = pts[i].x, yi = pts[i].y;
                double xj = pts[j].x, yj = pts[j].y;
                bool intersect = ((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi + 0.0) + xi);
                if (intersect) inside = !inside;
            }
            return inside;
        }

        private void CommitPolygon()
        {
            #pragma warning disable CS8602
            try
            {
                if (polygonPoints == null || polygonPoints.Count < 3) { polygonPoints.Clear(); isConstructingPolygon = false; return; }
                var list = new System.Collections.Generic.List<(int x, int y)>();
                if (!hollowShape)
                {
                    list = GetFilledPolygonTiles(polygonPoints);
                }
                else
                {
                    var set = new System.Collections.Generic.HashSet<(int x, int y)>();
                    for (int i = 0; i + 1 < polygonPoints.Count; i++)
                    {
                        var a = polygonPoints[i]; var b = polygonPoints[i + 1];
                        var seg = ComputeDrawTileList(a.x, a.y, b.x, b.y, DrawMode.Line, brushThickness);
                        foreach (var t in seg) set.Add(t);
                    }
                    if (polygonPoints.Count >= 3)
                    {
                        var a = polygonPoints[polygonPoints.Count - 1]; var b = polygonPoints[0];
                        var seg = ComputeDrawTileList(a.x, a.y, b.x, b.y, DrawMode.Line, brushThickness);
                        foreach (var t in seg) set.Add(t);
                    }
                    list.AddRange(set);
                }
                if (list.Count == 0) { polygonPoints.Clear(); isConstructingPolygon = false; return; }

                // Handle tool-specific commit behavior: Erase, Select, or Place
                if (EraseTool != null && EraseTool.IsChecked == true)
                {
                    var eraseTileAction = new TileChangeAction();
                    var eraseSpriteAction = new SpriteChangeAction();
                    foreach (var (tx, ty) in list)
                    {
                        int idx = ty * mapWidth + tx;
                        if (tilesLayerActive)
                        {
                            int old = tiles[idx]; if (old != -1) { eraseTileAction.Add(idx, old, -1); tiles[idx] = -1; }
                        }
                        if (spritesLayerActive)
                        {
                            int oldS = sprites[idx]; if (oldS != -1) { eraseSpriteAction.Add(idx, oldS, -1); sprites[idx] = -1; }
                        }
                    }
                    if (!eraseTileAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(eraseTileAction); redoStack.Clear(); hasUnsavedChanges = true; }
                    if (!eraseSpriteAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(eraseSpriteAction); redoStack.Clear(); hasUnsavedChanges = true; }
                    try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
                    try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                }
                else if (SelectTool != null && SelectTool.IsChecked == true)
                {
                    bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                    var newIndices = new System.Collections.Generic.HashSet<int>();
                    foreach (var (tx, ty) in list) newIndices.Add(ty * mapWidth + tx);
                    if (!isCtrl) selectionSet.Clear();
                    foreach (var idx in newIndices) selectionSet.Add(idx);

                    if (selectionSet.Count == 0) { ClearSelection(); }
                    else
                    {
                        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                        foreach (var i in selectionSet) { int sx = i % mapWidth, sy = i / mapWidth; if (sx < minX) minX = sx; if (sy < minY) minY = sy; if (sx > maxX) maxX = sx; if (sy > maxY) maxY = sy; }
                        selX = minX; selY = minY; selW = maxX - minX + 1; selH = maxY - minY + 1;
                        selTiles = new int[selW * selH]; selSprites = new int[selW * selH];
                        for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
                        {
                            int gidx = (selY + yy) * mapWidth + (selX + xx);
                            if (selectionSet.Contains(gidx)) { selTiles[yy * selW + xx] = tilesLayerActive ? tiles[gidx] : -1; selSprites[yy * selW + xx] = spritesLayerActive ? sprites[gidx] : -1; }
                            else { selTiles[yy * selW + xx] = -1; selSprites[yy * selW + xx] = -1; }
                        }
                        UpdateSelectionVisuals(selX, selY, selW, selH);
                    }
                }
                else
                {
                    var tileAction = new TileChangeAction();
                    var spriteAction = new SpriteChangeAction();
                    foreach (var (tx, ty) in list)
                    {
                        int idx = ty * mapWidth + tx;
                        if (tilesLayerActive && selectedTile >= 0)
                        {
                            int old = tiles[idx]; int neu = selectedTile; if (old != neu) { tileAction.Add(idx, old, neu); tiles[idx] = neu; }
                        }
                        if (spritesLayerActive && selectedSprite >= 0)
                        {
                            int oldS = sprites[idx]; int neuS = selectedSprite; if (oldS != neuS) { spriteAction.Add(idx, oldS, neuS); sprites[idx] = neuS; }
                        }
                    }
                    if (!tileAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(tileAction); redoStack.Clear(); hasUnsavedChanges = true; }
                    if (!spriteAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(spriteAction); redoStack.Clear(); hasUnsavedChanges = true; }
                    try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
                    try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                }
            }
            #pragma warning restore CS8602
            catch { }
            finally
            {
                polygonPoints.Clear(); isConstructingPolygon = false; ClearDeferredPreview(); if (CanvasHost != null && CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
                lastInputAction = System.DateTime.Now;
            }
        }

        private void CancelPolygon()
        {
            try
            {
                polygonPoints.Clear();
                isConstructingPolygon = false;
                ClearDeferredPreview();
                if (CanvasHost != null && CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
            }
            catch { }
        }

        private void UpdateDeferredPreview()
        {
            try
            {
                #pragma warning disable CS8602
                if (SelectionOverlay == null) return;
                #pragma warning restore CS8602
                SelectionOverlay.Children.Clear();
                var dpi = VisualTreeHelper.GetDpi(this);
                double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
                int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                int padPxX = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
                int padPxY = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);

                // Only show preview when actively drawing or constructing polygon
                if (!isDeferredDrawing && !isConstructingPolygon) return;

                if (isConstructingPolygon && polygonPoints != null && polygonPoints.Count > 0)
                {
                    // Draw skeleton polyline connecting points and current mouse
                    var pts = new System.Collections.Generic.List<System.Windows.Point>();
                    foreach (var p in polygonPoints)
                    {
                        double cx = (padPxX + p.x * tilePixelW + tilePixelW / 2) / dpi.DpiScaleX;
                        double cy = (padPxY + p.y * tilePixelH + tilePixelH / 2) / dpi.DpiScaleY + gridRenderShiftY;
                        pts.Add(new System.Windows.Point(cx, cy));
                    }
                    // add current mouse position as moving vertex
                    if (drawCurrentX >= 0 && drawCurrentY >= 0)
                    {
                        double cx = (padPxX + drawCurrentX * tilePixelW + tilePixelW / 2) / dpi.DpiScaleX;
                        double cy = (padPxY + drawCurrentY * tilePixelH + tilePixelH / 2) / dpi.DpiScaleY + gridRenderShiftY;
                        pts.Add(new System.Windows.Point(cx, cy));
                    }
                    var poly = new Shapes.Polyline { Stroke = Brushes.Yellow, StrokeThickness = 1, IsHitTestVisible = false, Fill = Brushes.Transparent };
                    try { poly.StrokeThickness = 1.0 / dpi.DpiScaleX; poly.SnapsToDevicePixels = true; } catch { }
                    foreach (var pt in pts) poly.Points.Add(pt);
                    SelectionOverlay.Children.Add(poly);
                    // Also show ghost tiles where the polygon would fill/outline
                    var ghostList = new System.Collections.Generic.List<(int x, int y)>();
                    if (!hollowShape)
                    {
                        // compute filled with current moving vertex appended
                        var tempPts = new System.Collections.Generic.List<(int x, int y)>(polygonPoints);
                        if (drawCurrentX >= 0 && drawCurrentY >= 0) tempPts.Add((drawCurrentX, drawCurrentY));
                        ghostList = GetFilledPolygonTiles(tempPts);
                    }
                    else
                    {
                        // outline: build segments between points and moving vertex
                        var tempPts = new System.Collections.Generic.List<(int x, int y)>(polygonPoints);
                        if (drawCurrentX >= 0 && drawCurrentY >= 0) tempPts.Add((drawCurrentX, drawCurrentY));
                        for (int i = 0; i + 1 < tempPts.Count; i++)
                        {
                            var a = tempPts[i]; var b = tempPts[i + 1];
                            var seg = ComputeDrawTileList(a.x, a.y, b.x, b.y, DrawMode.Line, brushThickness);
                            foreach (var t in seg) ghostList.Add(t);
                        }
                    }
                    if (ghostList.Count > 0)
                    {
                        ImageSource? ghostSrc = null;
                        if (tilesLayerActive && selectedTile >= 0 && tileImages != null)
                        {
                            try { ghostSrc = (tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[selectedTile] : tileImages[selectedTile]; } catch { ghostSrc = null; }
                        }
                        if (spritesLayerActive && selectedSprite >= 0 && spriteImages != null)
                        {
                            ghostSrc = spriteImages[selectedSprite];
                        }
                        if (ghostSrc != null)
                        {
                            foreach (var (gx, gy) in ghostList)
                            {
                                var img = new Image { Source = ghostSrc, Width = (double)tilePixelW / dpi.DpiScaleX, Height = (double)tilePixelH / dpi.DpiScaleY, Opacity = 0.5, IsHitTestVisible = false };
                                Canvas.SetLeft(img, (padPxX + gx * tilePixelW) / dpi.DpiScaleX);
                                Canvas.SetTop(img, (padPxY + gy * tilePixelH) / dpi.DpiScaleY + gridRenderShiftY);
                                SelectionOverlay.Children.Add(img);
                            }
                        }
                        else
                        {
                            // Draw erase/select specific overlay colors when no image available
                            var overlayColor = (EraseTool != null && EraseTool.IsChecked == true) ? Color.FromArgb(128, 220, 64, 64)
                                                : (SelectTool != null && SelectTool.IsChecked == true) ? Color.FromArgb(96, 64, 160, 255)
                                                : Color.FromArgb(96, 255, 255, 0);
                            var brush = new SolidColorBrush(overlayColor);
                            foreach (var (gx, gy) in ghostList)
                            {
                                var rect = new Shapes.Rectangle { Width = (double)tilePixelW / dpi.DpiScaleX, Height = (double)tilePixelH / dpi.DpiScaleY, Fill = brush, Stroke = Brushes.Transparent, IsHitTestVisible = false };
                                Canvas.SetLeft(rect, (padPxX + gx * tilePixelW) / dpi.DpiScaleX);
                                Canvas.SetTop(rect, (padPxY + gy * tilePixelH) / dpi.DpiScaleY + gridRenderShiftY);
                                SelectionOverlay.Children.Add(rect);
                            }
                        }
                    }
                    return;
                }

                // For other shapes, draw thin outline/skeleton rather than per-tile boxes
                if (currentDrawMode == DrawMode.Line)
                {
                    double x1 = (padPxX + drawStartX * tilePixelW + tilePixelW / 2) / dpi.DpiScaleX;
                    double y1 = (padPxY + drawStartY * tilePixelH + tilePixelH / 2) / dpi.DpiScaleY + gridRenderShiftY;
                    double x2 = (padPxX + drawCurrentX * tilePixelW + tilePixelW / 2) / dpi.DpiScaleX;
                    double y2 = (padPxY + drawCurrentY * tilePixelH + tilePixelH / 2) / dpi.DpiScaleY + gridRenderShiftY;
                    var l = new Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brushes.Yellow, StrokeThickness = 1, IsHitTestVisible = false };
                    try { l.StrokeThickness = 1.0 / dpi.DpiScaleX; l.SnapsToDevicePixels = true; } catch { }
                    SelectionOverlay.Children.Add(l);
                    // Also draw ghost tiles for line
                    var lineTiles = ComputeDrawTileList(drawStartX, drawStartY, drawCurrentX, drawCurrentY, DrawMode.Line, brushThickness);
                    DrawGhostTiles(lineTiles, tilePixelW, tilePixelH, padPxX, padPxY, dpi);
                    return;
                }

                if (currentDrawMode == DrawMode.Square)
                {
                    double left = (padPxX + Math.Min(drawStartX, drawCurrentX) * tilePixelW) / dpi.DpiScaleX;
                    double top = (padPxY + Math.Min(drawStartY, drawCurrentY) * tilePixelH) / dpi.DpiScaleY + gridRenderShiftY;
                    double wpx = (Math.Abs(drawCurrentX - drawStartX) + 1) * tilePixelW / dpi.DpiScaleX;
                    double hpx = (Math.Abs(drawCurrentY - drawStartY) + 1) * tilePixelH / dpi.DpiScaleY;
                    var rect = new Shapes.Rectangle { Width = wpx, Height = hpx, Stroke = Brushes.Yellow, StrokeThickness = 1, Fill = Brushes.Transparent, IsHitTestVisible = false };
                    try { rect.StrokeThickness = 1.0 / dpi.DpiScaleX; rect.SnapsToDevicePixels = true; } catch { }
                    Canvas.SetLeft(rect, left);
                    Canvas.SetTop(rect, top);
                    SelectionOverlay.Children.Add(rect);
                    var squareTiles = ComputeDrawTileList(drawStartX, drawStartY, drawCurrentX, drawCurrentY, DrawMode.Square, brushThickness);
                    DrawGhostTiles(squareTiles, tilePixelW, tilePixelH, padPxX, padPxY, dpi);
                    return;
                }

                if (currentDrawMode == DrawMode.Triangle)
                {
                    // isosceles triangle from top center (minY) to base (maxY)
                    int minx = Math.Min(drawStartX, drawCurrentX), maxx = Math.Max(drawStartX, drawCurrentX);
                    int miny = Math.Min(drawStartY, drawCurrentY), maxy = Math.Max(drawStartY, drawCurrentY);
                    double apexX = (padPxX + ((minx + maxx) / 2.0) * tilePixelW + tilePixelW / 2) / dpi.DpiScaleX;
                    double apexY = (padPxY + miny * tilePixelH + tilePixelH / 2) / dpi.DpiScaleY + gridRenderShiftY;
                    double baseLeftX = (padPxX + minx * tilePixelW + tilePixelW / 2) / dpi.DpiScaleX;
                    double baseRightX = (padPxX + maxx * tilePixelW + tilePixelW / 2) / dpi.DpiScaleX;
                    double baseY = (padPxY + maxy * tilePixelH + tilePixelH / 2) / dpi.DpiScaleY + gridRenderShiftY;
                    var poly = new Shapes.Polyline { Stroke = Brushes.Yellow, StrokeThickness = 1, IsHitTestVisible = false, Fill = Brushes.Transparent };
                    poly.Points.Add(new System.Windows.Point(apexX, apexY));
                    poly.Points.Add(new System.Windows.Point(baseRightX, baseY));
                    poly.Points.Add(new System.Windows.Point(baseLeftX, baseY));
                    poly.Points.Add(new System.Windows.Point(apexX, apexY));
                    try { poly.StrokeThickness = 1.0 / dpi.DpiScaleX; poly.SnapsToDevicePixels = true; } catch { }
                    SelectionOverlay.Children.Add(poly);
                    var triTiles = ComputeDrawTileList(Math.Min(drawStartX, drawCurrentX), Math.Min(drawStartY, drawCurrentY), Math.Max(drawStartX, drawCurrentX), Math.Max(drawStartY, drawCurrentY), DrawMode.Triangle, brushThickness);
                    DrawGhostTiles(triTiles, tilePixelW, tilePixelH, padPxX, padPxY, dpi);
                    return;
                }

                if (currentDrawMode == DrawMode.Circle)
                {
                    // Compute the exact tile list for the circle and draw an outline around the tile bounds
                    var tilesToPreview = ComputeDrawTileList(drawStartX, drawStartY, drawCurrentX, drawCurrentY, DrawMode.Circle, brushThickness);
                    if (tilesToPreview.Count == 0) return;
                    int minx = int.MaxValue, miny = int.MaxValue, maxx = int.MinValue, maxy = int.MinValue;
                    foreach (var (tx, ty) in tilesToPreview) { if (tx < minx) minx = tx; if (tx > maxx) maxx = tx; if (ty < miny) miny = ty; if (ty > maxy) maxy = ty; }
                    double left = (padPxX + minx * tilePixelW) / dpi.DpiScaleX;
                    double top = (padPxY + miny * tilePixelH) / dpi.DpiScaleY + gridRenderShiftY;
                    double wpx = (maxx - minx + 1) * tilePixelW / dpi.DpiScaleX;
                    double hpx = (maxy - miny + 1) * tilePixelH / dpi.DpiScaleY;
                    var el = new Shapes.Ellipse { Width = wpx, Height = hpx, Stroke = Brushes.Yellow, StrokeThickness = 1, Fill = Brushes.Transparent, IsHitTestVisible = false };
                    try { el.StrokeThickness = 1.0 / dpi.DpiScaleX; el.SnapsToDevicePixels = true; } catch { }
                    Canvas.SetLeft(el, left); Canvas.SetTop(el, top);
                    SelectionOverlay.Children.Add(el);
                    // Also draw ghost tiles for circle
                    ImageSource? ghostSrc = null;
                    if (tilesLayerActive && selectedTile >= 0 && tileImages != null)
                    {
                        try { ghostSrc = (tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[selectedTile] : tileImages[selectedTile]; } catch { ghostSrc = null; }
                    }
                    if (spritesLayerActive && selectedSprite >= 0 && spriteImages != null) ghostSrc = spriteImages[selectedSprite];
                    if (ghostSrc != null)
                    {
                        foreach (var (tx, ty) in tilesToPreview)
                        {
                            var img = new Image { Source = ghostSrc, Width = (double)tilePixelW / dpi.DpiScaleX, Height = (double)tilePixelH / dpi.DpiScaleY, Opacity = 0.5, IsHitTestVisible = false };
                            Canvas.SetLeft(img, (padPxX + tx * tilePixelW) / dpi.DpiScaleX);
                            Canvas.SetTop(img, (padPxY + ty * tilePixelH) / dpi.DpiScaleY + gridRenderShiftY);
                            SelectionOverlay.Children.Add(img);
                        }
                    }
                    return;
                }
            }
            catch { }
        }

        private System.Collections.Generic.List<(int x, int y)> ComputeDrawTileList(int sx, int sy, int ex, int ey, DrawMode mode, int thickness)
        {
            var list = new System.Collections.Generic.List<(int x, int y)>();
            if (sx < 0 || sy < 0 || ex < 0 || ey < 0) return list;
            if (mode == DrawMode.Tile)
            {
                int half = (thickness - 1) / 2;
                for (int dy = -half; dy <= half; dy++) for (int dx = -half; dx <= half; dx++)
                {
                    int tx = sx + dx; int ty = sy + dy;
                    if (tx >= 0 && tx < mapWidth && ty >= 0 && ty < mapHeight) list.Add((tx, ty));
                }
                return list;
            }
            if (mode == DrawMode.Line)
            {
                int x0 = sx, y0 = sy, x1 = ex, y1 = ey;
                int dx = Math.Abs(x1 - x0), sxSign = x0 < x1 ? 1 : -1;
                int dy = -Math.Abs(y1 - y0), sySign = y0 < y1 ? 1 : -1;
                int err = dx + dy;
                while (true)
                {
                    int half = (thickness - 1) / 2;
                    for (int oy = -half; oy <= half; oy++) for (int ox = -half; ox <= half; ox++)
                    {
                        int tx = x0 + ox; int ty = y0 + oy;
                        if (tx >= 0 && tx < mapWidth && ty >= 0 && ty < mapHeight) list.Add((tx, ty));
                    }
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sxSign; }
                    if (e2 <= dx) { err += dx; y0 += sySign; }
                }
                return list;
            }
            int minx = Math.Min(sx, ex), maxx = Math.Max(sx, ex);
            int miny = Math.Min(sy, ey), maxy = Math.Max(sy, ey);
            if (mode == DrawMode.Square)
            {
                if (hollowShape)
                {
                    int t = Math.Max(1, thickness);
                    for (int y = miny; y <= maxy; y++) for (int x = minx; x <= maxx; x++)
                    {
                        bool onBorder = (x - minx < t) || (maxx - x < t) || (y - miny < t) || (maxy - y < t);
                        if (onBorder && x >= 0 && x < mapWidth && y >= 0 && y < mapHeight) list.Add((x, y));
                    }
                }
                else
                {
                    for (int y = miny; y <= maxy; y++) for (int x = minx; x <= maxx; x++)
                    {
                        if (thickness <= 1) { if (x >= 0 && x < mapWidth && y >= 0 && y < mapHeight) list.Add((x, y)); }
                        else
                        {
                            int half = (thickness - 1) / 2;
                            for (int oy = -half; oy <= half; oy++) for (int ox = -half; ox <= half; ox++)
                            {
                                int tx = x + ox; int ty = y + oy;
                                if (tx >= 0 && tx < mapWidth && ty >= 0 && ty < mapHeight) list.Add((tx, ty));
                            }
                        }
                    }
                }
                return list;
            }
            if (mode == DrawMode.Triangle)
            {
                // Build an isosceles triangle within the rectangle defined by (sx,sy) and (ex,ey).
                int apexX = (minx + maxx) / 2;
                int apexY = miny;
                int baseY = maxy;
                // Filled triangle: rasterize scanlines between apex and base
                if (!hollowShape)
                {
                    int height = Math.Max(1, baseY - apexY);
                    for (int y = apexY; y <= baseY; y++)
                    {
                        double t = (double)(y - apexY) / (double)height;
                        int span = (int)Math.Round((maxx - minx + 1) * t);
                        int cx = apexX;
                        int left = Math.Max(minx, cx - span / 2 - (thickness - 1));
                        int right = Math.Min(maxx, cx + span / 2 + (thickness - 1));
                        for (int x = left; x <= right; x++)
                        {
                            if (x >= 0 && x < mapWidth && y >= 0 && y < mapHeight) list.Add((x, y));
                        }
                    }
                    return list;
                }

                // Hollow triangle: draw three edged segments (apex->left base, apex->right base, base left->base right)
                var edges = new System.Collections.Generic.HashSet<(int x, int y)>();
                int leftBase = minx; int rightBase = maxx;
                // apex -> left base
                var seg1 = ComputeDrawTileList(apexX, apexY, leftBase, baseY, DrawMode.Line, thickness);
                foreach (var s in seg1) edges.Add(s);
                // apex -> right base
                var seg2 = ComputeDrawTileList(apexX, apexY, rightBase, baseY, DrawMode.Line, thickness);
                foreach (var s in seg2) edges.Add(s);
                // base left -> base right
                var seg3 = ComputeDrawTileList(leftBase, baseY, rightBase, baseY, DrawMode.Line, thickness);
                foreach (var s in seg3) edges.Add(s);

                list.AddRange(edges);
                return list;
            }
            if (mode == DrawMode.Circle)
            {
                int rx = Math.Abs(ex - sx), ry = Math.Abs(ey - sy);
                int r = Math.Max(1, Math.Max(rx, ry) + 1);
                int cx = sx, cy = sy;
                // For circles, treat the minimum brush thickness as one tick higher.
                // This ensures the smallest brush setting still produces a complete circle.
                int effThickness = thickness;
                if (effThickness <= 1) effThickness = 2;
                // Use center between sx,ex and sy,ey
                cx = (sx + ex) / 2; cy = (sy + ey) / 2;
                if (hollowShape)
                {
                    int t = Math.Max(1, effThickness);
                    int innerR = Math.Max(0, r - t + 1);
                    int r2 = r * r; int inner2 = innerR * innerR;
                    for (int y = cy - r; y <= cy + r; y++) for (int x = cx - r; x <= cx + r; x++)
                    {
                        int dxp = x - cx, dyp = y - cy; int d2 = dxp * dxp + dyp * dyp;
                        if (d2 <= r2 && d2 >= inner2) if (x >= 0 && x < mapWidth && y >= 0 && y < mapHeight) list.Add((x, y));
                    }
                }
                else
                {
                    for (int y = cy - r; y <= cy + r; y++) for (int x = cx - r; x <= cx + r; x++)
                    {
                        int dxp = x - cx, dyp = y - cy; if (dxp * dxp + dyp * dyp <= r * r) if (x >= 0 && x < mapWidth && y >= 0 && y < mapHeight) list.Add((x, y));
                    }
                }
                return list;
            }
            return list;
        }

        private void CommitDeferredDraw()
        {
            try
            {
                if (!isDeferredDrawing) return;
                var list = ComputeDrawTileList(drawStartX, drawStartY, drawCurrentX, drawCurrentY, currentDrawMode, brushThickness);
                if (list.Count == 0) { isDeferredDrawing = false; ClearDeferredPreview(); if (CanvasHost != null && CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture(); return; }

                // If EraseTool is active, perform erasure on the shape.
                if (EraseTool != null && EraseTool.IsChecked == true)
                {
                    var eraseTileAction = new TileChangeAction();
                    var eraseSpriteAction = new SpriteChangeAction();
                    foreach (var (tx, ty) in list)
                    {
                        int idx = ty * mapWidth + tx;
                        if (tilesLayerActive)
                        {
                            int old = tiles[idx]; if (old != -1) { eraseTileAction.Add(idx, old, -1); tiles[idx] = -1; }
                        }
                        if (spritesLayerActive)
                        {
                            int oldS = sprites[idx]; if (oldS != -1) { eraseSpriteAction.Add(idx, oldS, -1); sprites[idx] = -1; }
                        }
                    }
                    if (!eraseTileAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(eraseTileAction); redoStack.Clear(); hasUnsavedChanges = true; }
                    if (!eraseSpriteAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(eraseSpriteAction); redoStack.Clear(); hasUnsavedChanges = true; }
                    try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
                    try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                    isDeferredDrawing = false; drawStartX = drawStartY = drawCurrentX = drawCurrentY = -1; ClearDeferredPreview(); if (CanvasHost != null && CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
                    return;
                }

                // If SelectTool is active, convert the shape into a selection (additive with Ctrl)
                if (SelectTool != null && SelectTool.IsChecked == true)
                {
                    bool isCtrl = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
                    var newIndices = new System.Collections.Generic.HashSet<int>();
                    foreach (var (tx, ty) in list)
                    {
                        int idx = ty * mapWidth + tx; newIndices.Add(idx);
                    }
                    if (!isCtrl)
                    {
                        selectionSet.Clear();
                    }
                    foreach (var idx in newIndices) selectionSet.Add(idx);

                    // Build selX/selY/selW/selH and selTiles/selSprites representing bounding box of selectionSet
                    if (selectionSet.Count == 0)
                    {
                        ClearSelection();
                    }
                    else
                    {
                        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                        foreach (var i in selectionSet) { int sx = i % mapWidth, sy = i / mapWidth; if (sx < minX) minX = sx; if (sy < minY) minY = sy; if (sx > maxX) maxX = sx; if (sy > maxY) maxY = sy; }
                        selX = minX; selY = minY; selW = maxX - minX + 1; selH = maxY - minY + 1;
                        selTiles = new int[selW * selH]; selSprites = new int[selW * selH];
                        for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
                        {
                            int gidx = (selY + yy) * mapWidth + (selX + xx);
                            if (selectionSet.Contains(gidx)) { selTiles[yy * selW + xx] = tilesLayerActive ? tiles[gidx] : -1; selSprites[yy * selW + xx] = spritesLayerActive ? sprites[gidx] : -1; }
                            else { selTiles[yy * selW + xx] = -1; selSprites[yy * selW + xx] = -1; }
                        }
                        UpdateSelectionVisuals(selX, selY, selW, selH);
                    }

                    isDeferredDrawing = false; drawStartX = drawStartY = drawCurrentX = drawCurrentY = -1; ClearDeferredPreview(); if (CanvasHost != null && CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
                    return;
                }

                // Default: place into active layers using selected tile/sprite
                var tileAction = new TileChangeAction();
                var spriteAction = new SpriteChangeAction();
                foreach (var (tx, ty) in list)
                {
                    int idx = ty * mapWidth + tx;
                    if (tilesLayerActive && selectedTile >= 0)
                    {
                        int old = tiles[idx]; int neu = selectedTile; if (old != neu) { tileAction.Add(idx, old, neu); tiles[idx] = neu; }
                    }
                    if (spritesLayerActive && selectedSprite >= 0)
                    {
                        int oldS = sprites[idx]; int neuS = selectedSprite; if (oldS != neuS) { spriteAction.Add(idx, oldS, neuS); sprites[idx] = neuS; }
                    }
                }
                if (!tileAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(tileAction); redoStack.Clear(); hasUnsavedChanges = true; }
                if (!spriteAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(spriteAction); redoStack.Clear(); hasUnsavedChanges = true; }
                try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
                try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
            }
            catch { }
            finally
            {
                isDeferredDrawing = false; drawStartX = drawStartY = drawCurrentX = drawCurrentY = -1; ClearDeferredPreview(); if (CanvasHost != null && CanvasHost.IsMouseCaptured) CanvasHost.ReleaseMouseCapture();
                lastInputAction = System.DateTime.Now;
            }
        }

        private void ClearDeferredPreview()
        {
            try { if (SelectionOverlay != null) SelectionOverlay.Children.Clear(); } catch { }
        }

        private void DrawGhostTiles(System.Collections.Generic.List<(int x, int y)> tilesPreview, int tilePixelW, int tilePixelH, int padPxX, int padPxY, DpiScale dpi)
        {
            if (tilesPreview == null || tilesPreview.Count == 0) return;
            var dpiScaleX = dpi.DpiScaleX; var dpiScaleY = dpi.DpiScaleY;

            // If erase is active, always draw erase overlay (do not show tile images)
            if (EraseTool != null && EraseTool.IsChecked == true)
            {
                var eraseBrush = new SolidColorBrush(Color.FromArgb(160, 220, 64, 64));
                foreach (var (tx, ty) in tilesPreview)
                {
                    var rect = new Shapes.Rectangle { Width = (double)tilePixelW / dpiScaleX, Height = (double)tilePixelH / dpiScaleY, Fill = eraseBrush, Stroke = Brushes.Transparent, IsHitTestVisible = false };
                    Canvas.SetLeft(rect, (padPxX + tx * tilePixelW) / dpiScaleX);
                    Canvas.SetTop(rect, (padPxY + ty * tilePixelH) / dpiScaleY + gridRenderShiftY);
                    SelectionOverlay.Children.Add(rect);
                }
                return;
            }

            // If select is active, prefer selection overlay
            if (SelectTool != null && SelectTool.IsChecked == true)
            {
                var selBrush = new SolidColorBrush(Color.FromArgb(120, 64, 160, 255));
                foreach (var (tx, ty) in tilesPreview)
                {
                    var rect = new Shapes.Rectangle { Width = (double)tilePixelW / dpiScaleX, Height = (double)tilePixelH / dpiScaleY, Fill = selBrush, Stroke = Brushes.Transparent, IsHitTestVisible = false };
                    Canvas.SetLeft(rect, (padPxX + tx * tilePixelW) / dpiScaleX);
                    Canvas.SetTop(rect, (padPxY + ty * tilePixelH) / dpiScaleY + gridRenderShiftY);
                    SelectionOverlay.Children.Add(rect);
                }
                return;
            }

            // Otherwise attempt to show the actual tile/sprite ghost image
            ImageSource? ghostSrc = null;
            if (tilesLayerActive && selectedTile >= 0 && tileImages != null)
            {
                try { ghostSrc = (tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[selectedTile] : tileImages[selectedTile]; } catch { ghostSrc = null; }
            }
            if (spritesLayerActive && selectedSprite >= 0 && spriteImages != null) ghostSrc = spriteImages[selectedSprite];

            if (ghostSrc == null)
            {
                var fallback = new SolidColorBrush(Color.FromArgb(96, 255, 255, 0));
                foreach (var (tx, ty) in tilesPreview)
                {
                    var rect = new Shapes.Rectangle { Width = (double)tilePixelW / dpiScaleX, Height = (double)tilePixelH / dpiScaleY, Fill = fallback, Stroke = Brushes.Transparent, IsHitTestVisible = false };
                    Canvas.SetLeft(rect, (padPxX + tx * tilePixelW) / dpiScaleX);
                    Canvas.SetTop(rect, (padPxY + ty * tilePixelH) / dpiScaleY + gridRenderShiftY);
                    SelectionOverlay.Children.Add(rect);
                }
                return;
            }

            foreach (var (tx, ty) in tilesPreview)
            {
                var img = new Image { Source = ghostSrc, Width = (double)tilePixelW / dpiScaleX, Height = (double)tilePixelH / dpiScaleY, Opacity = 0.5, IsHitTestVisible = false };
                Canvas.SetLeft(img, (padPxX + tx * tilePixelW) / dpiScaleX);
                Canvas.SetTop(img, (padPxY + ty * tilePixelH) / dpiScaleY + gridRenderShiftY);
                SelectionOverlay.Children.Add(img);
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

            // Build atomic final state for both tiles and sprites, then apply deletions for both layers,
            // then apply placements for both layers. This avoids inter-layer or overlapping-source/dest races
            // where an item could be moved and later accidentally cleared.

            var finalTiles = (int[])tiles.Clone();
            var finalSprites = (int[])sprites.Clone();
            var tileChanges = new TileChangeAction();
            var spriteChanges = new SpriteChangeAction();

            // Helper to determine whether a selection cell should be considered (sparse selection vs full rect)
            bool useSparse = (selectionSet != null && selectionSet.Count > 0);

            // First pass: collect mappings from source->dest for tiles and sprites (respecting sparse selection)
            var tileMappings = new System.Collections.Generic.List<(int src, int dst, int val)>();
            var spriteMappings = new System.Collections.Generic.List<(int src, int dst, int val)>();
            for (int yy = 0; yy < selH; yy++)
            {
                for (int xx = 0; xx < selW; xx++)
                {
                    int srcIdx = (selY + yy) * mapWidth + (selX + xx);
                    if (srcIdx < 0 || srcIdx >= tiles.Length) continue;
                    if (useSparse && (selectionSet == null || !selectionSet.Contains(srcIdx))) continue;

                    if (tilesLayerActive)
                    {
                        int val = selTiles[yy * selW + xx];
                        if (val != -1)
                        {
                            int dstIdx = (destY + yy) * mapWidth + (destX + xx);
                            if (dstIdx >= 0 && dstIdx < finalTiles.Length)
                            {
                                tileMappings.Add((srcIdx, dstIdx, val));
                            }
                        }
                    }

                    if (selSprites != null && spritesLayerActive)
                    {
                        int sval = selSprites[yy * selW + xx];
                        if (sval != -1)
                        {
                            int sDstIdx = (destY + yy) * mapWidth + (destX + xx);
                            if (sDstIdx >= 0 && sDstIdx < finalSprites.Length)
                            {
                                spriteMappings.Add((srcIdx, sDstIdx, sval));
                            }
                        }
                    }
                }
            }

            // Build destination sets so we don't clear a source that is also a destination of some mapping
            var tileDstSet = new System.Collections.Generic.HashSet<int>();
            foreach (var m in tileMappings) tileDstSet.Add(m.dst);
            var spriteDstSet = new System.Collections.Generic.HashSet<int>();
            foreach (var m in spriteMappings) spriteDstSet.Add(m.dst);

            // Apply mappings to finals: set destinations first, then clear sources only if source is not a destination
            foreach (var m in tileMappings)
            {
                finalTiles[m.dst] = m.val;
            }
            foreach (var m in tileMappings)
            {
                if (m.dst != m.src && !tileDstSet.Contains(m.src)) finalTiles[m.src] = -1;
            }

            foreach (var m in spriteMappings)
            {
                finalSprites[m.dst] = m.val;
            }
            foreach (var m in spriteMappings)
            {
                if (m.dst != m.src && !spriteDstSet.Contains(m.src)) finalSprites[m.src] = -1;
            }

            // Build del/put lists for tiles and sprites by comparing final vs current
            var delTiles = new System.Collections.Generic.List<int>();
            var putTiles = new System.Collections.Generic.List<int>();
            for (int i = 0; i < finalTiles.Length; i++)
            {
                if (finalTiles[i] != tiles[i])
                {
                    if (finalTiles[i] == -1) delTiles.Add(i); else putTiles.Add(i);
                    tileChanges.Add(i, tiles[i], finalTiles[i]);
                }
            }

            var delSprites = new System.Collections.Generic.List<int>();
            var putSprites = new System.Collections.Generic.List<int>();
            for (int i = 0; i < finalSprites.Length; i++)
            {
                if (finalSprites[i] != sprites[i])
                {
                    if (finalSprites[i] == -1) delSprites.Add(i); else putSprites.Add(i);
                    spriteChanges.Add(i, sprites[i], finalSprites[i]);
                }
            }

            // Apply deletions for both layers first
            foreach (var idx in delTiles) tiles[idx] = -1;
            foreach (var idx in delSprites)
            {
                // For sprites, clear the pixel footprints for affected indices
                int old = sprites[idx];
                if (old != -1)
                {
                    int animated = GetAnimatedSpriteIndex(old);
                    int sx = idx % mapWidth; int sy = idx / mapWidth;
                    int fminX = sx - 1; int fmaxX = sx + 1; int fminY = sy - 2; int fmaxY = sy + 2;
                    if (animated >= 3000 && animated <= 3029)
                    {
                        if (animated >= 3011 && animated <= 3014)
                        {
                            fminX = sx; fmaxX = sx + 2; fminY = sy; fmaxY = sy + 1;
                        }
                        else
                        {
                            fminX = sx; fmaxX = sx + 1; fminY = sy; fmaxY = sy + 2;
                        }
                    }
                    fminX = Math.Max(0, fminX); fminY = Math.Max(0, fminY); fmaxX = Math.Min(mapWidth - 1, fmaxX); fmaxY = Math.Min(mapHeight - 1, fmaxY);
                    ClearPortalsBitmapTileRect(fminX, fminY, fmaxX, fmaxY, (ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
                    ClearSpritesBitmapTileRect(fminX, fminY, fmaxX, fmaxY, (ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
                }
                sprites[idx] = -1;
            }

            // Rebuild both layers so deletions are visible
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
            try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }

            // Apply placements for both layers
            foreach (var idx in putTiles) tiles[idx] = finalTiles[idx];
            foreach (var idx in putSprites) sprites[idx] = finalSprites[idx];

            if (!tileChanges.IsEmpty() && !suppressUndoRecording) { undoStack.Push(tileChanges); redoStack.Clear(); hasUnsavedChanges = true; }
            if (!spriteChanges.IsEmpty() && !suppressUndoRecording) { undoStack.Push(spriteChanges); redoStack.Clear(); hasUnsavedChanges = true; }

            // Final rebuild
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
            try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
            
            
            
            // Update selection to new destination: preserve sparse selection membership only for cells that were selected
            var newSelection = new System.Collections.Generic.HashSet<int>();
            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
            {
                int oldIdx = (selY + yy) * mapWidth + (selX + xx);
                int newIdx = (destY + yy) * mapWidth + (destX + xx);
                // if the source cell was part of selectionSet (i.e., non-empty before move), include its destination
                if (selectionSet != null && selectionSet.Contains(oldIdx)) newSelection.Add(newIdx);
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

        private void EraseSelectedLayers()
        {
            if (selW <= 0 || selH <= 0) return;
            // If neither layer is active, treat both as active for erase operations
            bool effectiveTilesActive = tilesLayerActive || (!tilesLayerActive && !spritesLayerActive);
            bool effectiveSpritesActive = spritesLayerActive || (!tilesLayerActive && !spritesLayerActive);
            // If the user has a sparse selection (selectionSet), erase only those indices.
            if (selectionSet != null && selectionSet.Count > 0)
            {
                var tileAction = new TileChangeAction();
                var spriteAction = new SpriteChangeAction();

                foreach (var idx in selectionSet)
                {
                    if (idx < 0 || idx >= tiles.Length) continue;
                    if (effectiveTilesActive)
                    {
                        int old = tiles[idx]; if (old != -1) tileAction.Add(idx, old, -1);
                        tiles[idx] = -1;
                    }
                    if (effectiveSpritesActive)
                    {
                        int olds = sprites[idx]; if (olds != -1) spriteAction.Add(idx, olds, -1);
                        sprites[idx] = -1;
                    }
                }

                if (!tileAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(tileAction); redoStack.Clear(); hasUnsavedChanges = true; }
                if (!spriteAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(spriteAction); redoStack.Clear(); hasUnsavedChanges = true; }

                try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
                try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
            }
            else
            {
                // Tile layer (rectangular selection)
                var tileAction = new TileChangeAction();
                if (effectiveTilesActive)
                {
                    for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
                    {
                        int idx = (selY + yy) * mapWidth + (selX + xx);
                        int old = tiles[idx]; if (old != -1) tileAction.Add(idx, old, -1);
                        tiles[idx] = -1;
                    }
                    if (!tileAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(tileAction); redoStack.Clear(); hasUnsavedChanges = true; }
                    try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                }

                // Sprite layer (rectangular selection)
                var spriteAction = new SpriteChangeAction();
                if (effectiveSpritesActive)
                {
                    for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
                    {
                        int idx = (selY + yy) * mapWidth + (selX + xx);
                        int old = sprites[idx]; if (old != -1) spriteAction.Add(idx, old, -1);
                        sprites[idx] = -1;
                    }
                    if (!spriteAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(spriteAction); redoStack.Clear(); hasUnsavedChanges = true; }
                    try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }
                }
            }

            ClearSelection();
            if (StatusText != null) StatusText.Text = "Erased selection on active layers";
        }

        // Copy current selection into the internal clipboard
        private void CopySelection()
        {
            if (selW <= 0 || selH <= 0) return;
            clipboardW = selW; clipboardH = selH;
            clipboardTiles = new int[clipboardW * clipboardH];
            clipboardSprites = new int[clipboardW * clipboardH];
            clipboardMask = new bool[clipboardW * clipboardH];
            clipboardHasData = false;

            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
            {
                int sIdx = (selY + yy) * mapWidth + (selX + xx);
                int ti = selTiles != null ? selTiles[yy * selW + xx] : -1;
                int si = selSprites != null ? selSprites[yy * selW + xx] : -1;
                clipboardTiles[yy * clipboardW + xx] = ti;
                clipboardSprites[yy * clipboardW + xx] = si;
                bool wasSelected = (selectionSet != null && selectionSet.Contains(sIdx)) || ti != -1 || si != -1;
                clipboardMask[yy * clipboardW + xx] = wasSelected;
                if (wasSelected) clipboardHasData = true;
            }
            if (StatusText != null) StatusText.Text = clipboardHasData ? "Copied selection" : "Copied (empty)";
        }

        // Cut = copy then erase selected layers (honor active layers)
        private void CutSelection()
        {
            CopySelection();
            try { EraseSelectedLayers(); } catch { }
            if (StatusText != null) StatusText.Text = "Cut selection";
        }

        // Paste clipboard at destination tile coordinate (top-left)
        private void PasteClipboardAt(int destX, int destY)
        {
            if (!clipboardHasData || clipboardTiles == null || clipboardSprites == null || clipboardMask == null) return;
            if (destX < 0 || destY < 0) return;

            var tileAction = new TileChangeAction();
            var spriteAction = new SpriteChangeAction();

            for (int yy = 0; yy < clipboardH; yy++)
            {
                for (int xx = 0; xx < clipboardW; xx++)
                {
                    if (!clipboardMask[yy * clipboardW + xx]) continue; // only paste cells that were part of original selection
                    int dX = destX + xx; int dY = destY + yy;
                    if (dX < 0 || dX >= mapWidth || dY < 0 || dY >= mapHeight) continue;
                    int dIdx = dY * mapWidth + dX;
                    int tVal = clipboardTiles[yy * clipboardW + xx];
                    int sVal = clipboardSprites[yy * clipboardW + xx];
                    if (tVal != -1)
                    {
                        int old = tiles[dIdx]; if (old != tVal) tileAction.Add(dIdx, old, tVal);
                        tiles[dIdx] = tVal;
                    }
                    if (sVal != -1)
                    {
                        int old = sprites[dIdx]; if (old != sVal) spriteAction.Add(dIdx, old, sVal);
                        sprites[dIdx] = sVal;
                    }
                }
            }

            if (!tileAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(tileAction); redoStack.Clear(); hasUnsavedChanges = true; }
            if (!spriteAction.IsEmpty() && !suppressUndoRecording) { undoStack.Push(spriteAction); redoStack.Clear(); hasUnsavedChanges = true; }

            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
            try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); }

            // Update selection to pasted rectangle
            ClearSelection();
            selW = clipboardW; selH = clipboardH; selX = destX; selY = destY;
            selTiles = (int[])clipboardTiles.Clone();
            selSprites = (int[])clipboardSprites.Clone();
            selectionSet.Clear();
            for (int yy = 0; yy < selH; yy++) for (int xx = 0; xx < selW; xx++)
            {
                if (clipboardMask[yy * clipboardW + xx])
                {
                    int idx = (selY + yy) * mapWidth + (selX + xx);
                    selectionSet.Add(idx);
                }
            }
            UpdateSelectionVisuals(selX, selY, selW, selH);
            if (StatusText != null) StatusText.Text = "Pasted clipboard";
        }

        private void ContinuePaintingAt(Point pos)
        {
            var tt = ViewportPointToTile(pos);
            int x = tt.x; int y = tt.y;
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
                
                // Track if we're replacing a portal sprite (for later cleanup)
                bool replacedPortal = false;
                bool placedPortal = false;
                
                // Place sprite if sprites layer is active and a sprite is selected
                if (spritesLayerActive && selectedSprite >= 0)
                {
                    int idx = y * mapWidth + x;
                    int old = sprites[idx];
                    int neu = selectedSprite;
                    if (old != neu)
                    {
                        // Check if we're replacing a portal in preview mode
                        if (previewMode && IsPortalSprite(old))
                        {
                            replacedPortal = true;
                        }
                        
                        // Check if we're PLACING a portal in preview mode
                        if (previewMode && IsPortalSprite(neu))
                        {
                            placedPortal = true;
                        }
                        
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
                            // Update the newly placed sprite
                            UpdateSpriteBitmapAt(x, y, (ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
                            
                            // If we PLACED a portal in preview mode, rebuild portal pixels for the affected region only
                            if (placedPortal)
                            {
                                QueueRebuildPortalsRegion(x - 1, y - 2, x + 1, y + 2);
                            }
                            
                            // If we replaced a portal in preview mode, rebuild portal region so old portal pixels are removed
                            if (replacedPortal)
                            {
                                QueueRebuildPortalsRegion(x - 1, y - 2, x + 1, y + 2);
                            }
                            
                            // Rebuild portal region around the changed tile to reflect new portal placements
                            if (previewMode)
                            {
                                QueueRebuildPortalsRegion(x - 2, y - 3, x + 2, y + 2);
                            }
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
                    // After making sprite changes, rebuild portal background and then sprites composite so portals are persistent
                    // Only rebuild the entire sprites bitmap when in preview mode (portals/preview-only animations require a full composite).
                    // When preview mode is off, we already updated the specific sprite/tile bitmaps above, so a full rebuild causes unnecessary flicker.
                    try { if (previewMode) RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
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
                
                int erasedOldSprite = -1;
                if (spritesLayerActive)
                {
                    int idx = y * mapWidth + x;
                    erasedOldSprite = sprites[idx];
                    if (erasedOldSprite != -1)
                    {
                        if (!suppressUndoRecording)
                        {
                            if (currentCompositeSpriteAction == null) currentCompositeSpriteAction = new SpriteChangeAction();
                            currentCompositeSpriteAction.Add(idx, erasedOldSprite, -1);
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
                        {
                            // Update only the specific sprite that was erased
                            UpdateSpriteBitmapAt(x, y, (ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding);
                            // If the erased sprite was a portal anchor, rebuild that region
                            if (IsPortalSprite(erasedOldSprite))
                            {
                                QueueRebuildPortalsRegion(x - 1, y - 2, x + 1, y + 2);
                            }
                        }
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
                // Use the same integer-device-pixel mapping used everywhere else
                var tt = ViewportPointToTile(p);
                x = tt.x; y = tt.y;
            }
            
            // Only update if position changed
            if (x == lastHoverX && y == lastHoverY && inBounds == (lastHoverX != -1)) return;
            lastHoverX = x;
            lastHoverY = y;
            
            if (StatusText != null) StatusText.Text = inBounds ? $"Coords: {x}, {y}" : string.Empty;

            // Position hover rectangle (offset by padding) - align to device pixels
            try
            {
                if (HoverRect != null)
                {
                    if (inBounds)
                    {
                        var dpi = VisualTreeHelper.GetDpi(this);
                        // Use same integer-pixel math as grid: compute tile pixel size and pad in pixels
                        int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
                        int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
                        int padPxX = (int)Math.Round(pad * dpi.DpiScaleX);
                        int padPxY = (int)Math.Round(pad * dpi.DpiScaleY);
                        // Compute left/top/size in device pixels and round to integer pixels
                        int leftPx = padPxX + x * tilePixelW;
                        int topPx = padPxY + y * tilePixelH + gridRenderShiftYPx;
                        int sizePx = tilePixelH;
                        // Convert back to DIU after rounding (ensures pixel-aligned edges)
                        double left = (double)leftPx / dpi.DpiScaleX;
                        double top = (double)topPx / dpi.DpiScaleY;
                        double size = (double)sizePx / dpi.DpiScaleY;
                        // Hide the Rectangle hover (legacy) and use the Border hover which draws border inside
                        try { if (HoverRect != null) HoverRect.Visibility = Visibility.Collapsed; } catch { }
                        if (HoverBorder != null)
                        {
                            double strokeDiu = 1.0 / dpi.DpiScaleX;
                            double outerLeft = left - strokeDiu;
                            double outerTop = top - strokeDiu;
                            double outerWidth = size + (2.0 * strokeDiu);
                            double outerHeight = size + (2.0 * strokeDiu);
                            try
                            {
                                HoverBorder.BorderThickness = new Thickness(strokeDiu);
                                HoverBorder.Width = outerWidth;
                                HoverBorder.Height = outerHeight;
                                Canvas.SetLeft(HoverBorder, outerLeft);
                                Canvas.SetTop(HoverBorder, outerTop);
                                HoverBorder.Visibility = Visibility.Visible;
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        HoverRect.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch { }
        }

        // Convert a viewport point (relative to MapScrollViewer) to tile coordinates using
        // the same integer device-pixel math as the grid/tiles rendering.
        private (int x, int y) ViewportPointToTile(Point vp)
        {
            if (MapScrollViewer == null) return (-1, -1);
            var dpi = VisualTreeHelper.GetDpi(this);
            double scale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
            int tilePixelW = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleX));
            int tilePixelH = Math.Max(1, (int)Math.Ceiling(TileSize * scale * dpi.DpiScaleY));
            int padPxX = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
            int padPxY = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);

            // `vp` is expected to be in CanvasHost/content coordinates (device-independent units).
            // Use it directly as content DIU coordinates rather than adding ScrollViewer offsets
            // (callers pass CanvasHost positions via e.GetPosition(CanvasHost)).
            double contentDiuX = vp.X;
            double contentDiuY = vp.Y;
            // Convert to device pixels and round to nearest device pixel so we match the grid rendering which uses integer pixels
            double contentPxX = Math.Round(contentDiuX * dpi.DpiScaleX);
            double contentPxY = Math.Round(contentDiuY * dpi.DpiScaleY);
            // Account for any integer-pixel grid Y shift applied during CommitZoom
            int shiftY = gridRenderShiftYPx;
            // Compute tile indices using post-rounding device-pixel math then floor to integer tile index.
            int tx = (int)Math.Floor((contentPxX - padPxX) / (double)tilePixelW + 1e-9);
            int ty = (int)Math.Floor((contentPxY - padPxY - shiftY) / (double)tilePixelH + 1e-9);
            if (tx < 0) tx = 0; if (tx >= mapWidth) tx = mapWidth - 1;
            if (ty < 0) ty = 0; if (ty >= mapHeight) ty = mapHeight - 1;
            return (tx, ty);
        }

        // Native window hook for horizontal mouse wheel (WM_MOUSEHWHEEL = 0x020E)
        private IntPtr NativeWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_MOUSEHWHEEL = 0x020E;
            if (msg == WM_MOUSEHWHEEL)
            {
                try
                {
                    // HIWORD of wParam contains the wheel delta (signed short)
                    int w = wParam.ToInt32();
                    short delta = (short)(w >> 16);
                    // Scale down for smoother scrolling similar to vertical wheel handling
                    double scrollAmount = delta * 0.5;
                    if (MapScrollViewer != null)
                    {
                        if (!swapMouseWheelScroll)
                        {
                            // Default: horizontal wheel -> vertical scrolling
                            double newOffset = (MapScrollViewer?.VerticalOffset ?? 0) - scrollAmount;
                            MapScrollViewer?.ScrollToVerticalOffset(newOffset);
                        }
                        else
                        {
                            // Swapped: horizontal wheel -> horizontal scrolling
                            double newH = (MapScrollViewer?.HorizontalOffset ?? 0) - scrollAmount;
                            MapScrollViewer?.ScrollToHorizontalOffset(newH);
                        }
                        handled = true;
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
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
            try { SaveSettingsWithTriggerOption(); } catch { }
        }

        private void MapScrollViewer_ManipulationDelta(object? sender, ManipulationDeltaEventArgs e)
        {
            // Handle pinch zoom gestures
            if (ZoomSlider == null) return;
            // Compute per-event scale ratio using cumulative scale so we correctly detect spread vs pinch
            double cumulative = e.CumulativeManipulation.Scale.X;
            double ratio = 1.0;
            if (manipulationActive)
            {
                // ratio > 1 => spread, ratio < 1 => pinch
                if (lastManipulationCumulativeScale <= 0) lastManipulationCumulativeScale = 1.0;
                ratio = cumulative / lastManipulationCumulativeScale;
            }
            else
            {
                ratio = e.DeltaManipulation.Scale.X;
            }
            lastManipulationCumulativeScale = cumulative;
            // Respect user preference: when the option is CHECKED the behavior should be inverted
            // (user requested the meaning to flip). So invert the ratio only when the setting is OFF.
            try
            {
                if (!invertPinchGesture)
                {
                    if (Math.Abs(ratio) > 1e-12) ratio = 1.0 / ratio;
                }
            }
            catch { }
            
            bool handledAny = false;
            // Handle translation (two-finger swipe) as scrolling. Map horizontal translation according
            // to user preference: default (swapMouseWheelScroll==false) maps horizontal swipe -> vertical scroll.
            var translation = e.DeltaManipulation.Translation;
            if (Math.Abs(translation.X) > 0.0)
            {
                double tx = translation.X;
                // Use a scaling factor to match wheel smoothness
                double scrollAmount = tx * 1.0;
                if (!swapMouseWheelScroll)
                {
                    double newV = (MapScrollViewer?.VerticalOffset ?? 0) - scrollAmount;
                    MapScrollViewer?.ScrollToVerticalOffset(newV);
                }
                else
                {
                    double newH = (MapScrollViewer?.HorizontalOffset ?? 0) - scrollAmount;
                    MapScrollViewer?.ScrollToHorizontalOffset(newH);
                }
                handledAny = true;
            }
            if (Math.Abs(translation.Y) > 0.0)
            {
                double ty = translation.Y;
                double scrollAmount = ty * 1.0;
                // Vertical translation maps to vertical scroll always
                double newV = (MapScrollViewer?.VerticalOffset ?? 0) - scrollAmount;
                MapScrollViewer?.ScrollToVerticalOffset(newV);
                handledAny = true;
            }

            if (Math.Abs(ratio - 1.0) > 1e-9)
            {
                // Pinch/spread: use per-event ratio computed from cumulative manipulation so spread increases zoom
                double oldScale = ZoomSlider?.Value ?? 1.0;
                double newZoom = oldScale * ratio;
                // clamp
                double minZoom = ZoomSlider?.Minimum ?? 1.0;
                double maxZoom = ZoomSlider?.Maximum ?? 4.0;
                if (newZoom < minZoom) newZoom = minZoom;
                if (newZoom > maxZoom) newZoom = maxZoom;

                // Preserve anchor under cursor using integer device-pixel math so overlays stay aligned
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[PINCH] pinchDirectionDetected={pinchDirectionDetected} invertPinchGesture={invertPinchGesture} ratio={ratio}");
                    var dpi = VisualTreeHelper.GetDpi(this);
                    var mouseVp = Mouse.GetPosition(MapScrollViewer);
                    double hp = (MapScrollViewer?.HorizontalOffset ?? 0);
                    double vp = (MapScrollViewer?.VerticalOffset ?? 0);

                    // Tile pixel sizes and padding in device pixels (match rendering code)
                    int tilePixelW_old = Math.Max(1, (int)Math.Ceiling(TileSize * oldScale * dpi.DpiScaleX));
                    int tilePixelH_old = Math.Max(1, (int)Math.Ceiling(TileSize * oldScale * dpi.DpiScaleY));
                    int padPxX = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
                    int padPxY = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);

                    // Content position under cursor in device pixels
                    double contentPxX = (hp + mouseVp.X) * dpi.DpiScaleX;
                    double contentPxY = (vp + mouseVp.Y) * dpi.DpiScaleY;

                    // Map coordinates in tile-space (fractional) based on old tile pixels
                    double mapX = (contentPxX - padPxX) / (double)tilePixelW_old;
                    double mapY = (contentPxY - padPxY) / (double)tilePixelH_old;

                    // Record zoom anchor (viewport and map coords) so CommitZoom can preserve the point under cursor
                    try { zoomAnchorViewportX = mouseVp.X; zoomAnchorViewportY = mouseVp.Y; zoomAnchorMapX = mapX; zoomAnchorMapY = mapY; hasZoomAnchor = true; } catch { hasZoomAnchor = false; }

                    // New tile pixel sizes for newZoom
                    int tilePixelW_new = Math.Max(1, (int)Math.Ceiling(TileSize * newZoom * dpi.DpiScaleX));
                    int tilePixelH_new = Math.Max(1, (int)Math.Ceiling(TileSize * newZoom * dpi.DpiScaleY));

                    // Compute new content position in device pixels and round to integer pixels
                    double newContentPxX_d = padPxX + mapX * tilePixelW_new;
                    double newContentPxY_d = padPxY + mapY * tilePixelH_new;
                    int newContentPxX = (int)Math.Round(newContentPxX_d);
                    int newContentPxY = (int)Math.Round(newContentPxY_d);

                    // Emit debug info to Output window and brief UI status to help reproduction
                    try
                    {
                        string dbg = $"pinch sc={ratio:F3} old={oldScale:F3} new={newZoom:F3} pxOld=({(int)contentPxX},{(int)contentPxY}) map=({mapX:F3},{mapY:F3}) pxNew=({newContentPxX},{newContentPxY})";
                        System.Diagnostics.Debug.WriteLine("[PINCH-DETAIL] " + dbg);
                        if (StatusText != null) StatusText.Text = dbg;
                        try
                        {
                            var line = DateTime.UtcNow.ToString("o") + " " + dbg + Environment.NewLine;
                            // Try app base dir first
                            string? logPath = null;
                            try
                            {
                                var dir = AppContext.BaseDirectory;
                                var candidate = System.IO.Path.Combine(dir, "pinch-debug.log");
                                System.IO.File.AppendAllText(candidate, line);
                                logPath = candidate;
                            }
                            catch (Exception ex1)
                            {
                                System.Diagnostics.Debug.WriteLine("[PINCH-LOG] base dir write failed: " + ex1.Message);
                                try
                                {
                                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                                    var folder = System.IO.Path.Combine(appData, "FamidashEditor");
                                    System.IO.Directory.CreateDirectory(folder);
                                    var candidate = System.IO.Path.Combine(folder, "pinch-debug.log");
                                    System.IO.File.AppendAllText(candidate, line);
                                    logPath = candidate;
                                }
                                catch (Exception ex2)
                                {
                                    System.Diagnostics.Debug.WriteLine("[PINCH-LOG] LocalAppData write failed: " + ex2.Message);
                                    try
                                    {
                                        var tmp = System.IO.Path.GetTempPath();
                                        var candidate = System.IO.Path.Combine(tmp, "pinch-debug.log");
                                        System.IO.File.AppendAllText(candidate, line);
                                        logPath = candidate;
                                    }
                                    catch (Exception ex3)
                                    {
                                        System.Diagnostics.Debug.WriteLine("[PINCH-LOG] Temp write failed: " + ex3.Message);
                                        logPath = null;
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(logPath))
                            {
                                try { if (StatusText != null) StatusText.Text = "Wrote pinch log to: " + logPath; } catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("[PINCH-LOG] final write failed: " + ex.Message);
                        }
                    }
                    catch { }

                    // Record zoom anchor so CommitZoom can preserve the point under the cursor
                    try { zoomAnchorViewportX = mouseVp.X; zoomAnchorViewportY = mouseVp.Y; zoomAnchorMapX = mapX; zoomAnchorMapY = mapY; hasZoomAnchor = true; } catch { hasZoomAnchor = false; }
                    // Apply new zoom value
                    if (ZoomSlider != null) ZoomSlider.Value = newZoom;

                    // Convert new content pixel position back to DIU and compute scroll offsets so cursor remains over same world point
                    double newContentX = (double)newContentPxX / dpi.DpiScaleX;
                    double newContentY = (double)newContentPxY / dpi.DpiScaleY;
                    double newH = newContentX - mouseVp.X;
                    double newV = newContentY - mouseVp.Y;

                    double maxH = Math.Max(0, (CanvasHost?.ActualWidth ?? 0) - SafeViewportWidth());
                    double maxV = Math.Max(0, (CanvasHost?.ActualHeight ?? 0) - SafeViewportHeight());
                    newH = Math.Max(0, Math.Min(maxH, newH));
                    newV = Math.Max(0, Math.Min(maxV, newV));

                    MapScrollViewer?.ScrollToHorizontalOffset(newH);
                    MapScrollViewer?.ScrollToVerticalOffset(newV);

                    // Mirror wheel behavior: defer full rebuild and show quick-zoom transform
                    try { deferZoomRebuild = true; UpdateQuickZoomTransform(); } catch { }
                    try { zoomCommitTimer?.Stop(); zoomCommitTimer?.Start(); } catch { }

                    // Snap offsets and update so overlays sync immediately
                    try { SnapScrollOffsetsToDevicePixels(); } catch { }
                    try { UpdateParallaxTransform(); } catch { }
                    try { UpdateCoords(Mouse.GetPosition(CanvasHost)); } catch { }

                    // Additionally, explicitly align the hover overlay to the computed tile
                    try
                    {
                        // Compute tile indices using the post-zoom integer device-pixel content position
                        int tx = (int)Math.Floor((newContentPxX - padPxX) / (double)tilePixelW_new + 1e-9);
                        int ty = (int)Math.Floor((newContentPxY - padPxY) / (double)tilePixelH_new + 1e-9);
                        if (tx < 0) tx = 0; if (tx >= mapWidth) tx = mapWidth - 1;
                        if (ty < 0) ty = 0; if (ty >= mapHeight) ty = mapHeight - 1;
                        lastHoverX = tx; lastHoverY = ty;
                        if (HoverBorder != null)
                        {
                            // compute hover position in device pixels using new tile pixel sizes
                            int leftPx = padPxX + tx * tilePixelW_new;
                            int topPx = padPxY + ty * tilePixelH_new + gridRenderShiftYPx;
                            int sizePx = tilePixelH_new;
                            double left = (double)leftPx / dpi.DpiScaleX;
                            double top = (double)topPx / dpi.DpiScaleY;
                            double size = (double)sizePx / dpi.DpiScaleY;
                            double strokeDiu = 1.0 / dpi.DpiScaleX;
                            double outerLeft = left - strokeDiu;
                            double outerTop = top - strokeDiu;
                            double outerWidth = size + (2.0 * strokeDiu);
                            double outerHeight = size + (2.0 * strokeDiu);
                            try
                            {
                                HoverBorder.BorderThickness = new Thickness(strokeDiu);
                                HoverBorder.Width = outerWidth;
                                HoverBorder.Height = outerHeight;
                                Canvas.SetLeft(HoverBorder, outerLeft);
                                Canvas.SetTop(HoverBorder, outerTop);
                                HoverBorder.Visibility = Visibility.Visible;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
                catch
                {
                    if (ZoomSlider != null) ZoomSlider.Value = newZoom;
                    try { deferZoomRebuild = true; UpdateQuickZoomTransform(); } catch { }
                    try { zoomCommitTimer?.Stop(); zoomCommitTimer?.Start(); } catch { }
                }
                handledAny = true;
            }

            if (handledAny)
            {
                e.Handled = true;
            }
        }

        private void MapScrollViewer_ManipulationStarting(object? sender, ManipulationStartingEventArgs e)
        {
            try
            {
                e.Mode = ManipulationModes.Scale | ManipulationModes.Translate;
                lastManipulationCumulativeScale = 1.0;
                manipulationActive = true;
            }
            catch { }
        }

        private void MapScrollViewer_ManipulationCompleted(object? sender, ManipulationCompletedEventArgs e)
        {
            try
            {
                manipulationActive = false;
                lastManipulationCumulativeScale = 1.0;
                // Ensure UI overlays sync after manipulation
                try { UpdateCoords(Mouse.GetPosition(CanvasHost)); } catch { }
            }
            catch { }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // If we already have a current file path, perform a silent save to that path.
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                try
                {
                    string target = currentFilePath;
                    string ext = Path.GetExtension(target).ToLower();

                    if (ext == ".tmx")
                    {
                        string? exportTarget = loadedExportTarget;
                        if (string.IsNullOrEmpty(exportTarget))
                        {
                            exportTarget = Path.ChangeExtension(Path.GetFileName(target), ".csv");
                        }
                        int chunkHeight = loadedHasEditorSettings ? loadedChunkHeight : mapHeight;
                        var tmxLevel = new TmxLevel
                        {
                            Width = mapWidth,
                            Height = mapHeight,
                            Tiles = tiles,
                            Sprites = sprites,
                            TilesetSource = loadedTilesetSource ?? "../../../GRAPHICS/famidash.bmp",
                            SpritesetSource = loadedSpritesetSource ?? "../../../GRAPHICS/sprites.png",
                            HasEditorSettings = true,
                            ChunkWidth = loadedChunkWidth,
                            ChunkHeight = chunkHeight,
                            ExportTarget = loadedExportTarget,
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
                            , DecoSet = loadedDecoSet
                        };
                        var saveCollisionMessages = TmxHandler.SaveTmx(target, tmxLevel, useLegacyTriggerOffset);
                        if (!string.IsNullOrEmpty(saveCollisionMessages))
                        {
                            MessageBox.Show("Sprite collision adjustments during save:\n\n" + saveCollisionMessages,
                                "Sprite Collision Resolution", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        var model = new LevelModel { Width = mapWidth, Height = mapHeight, Tiles = tiles };
                        File.WriteAllText(target, JsonSerializer.Serialize(model));
                    }

                    hasUnsavedChanges = false;
                    if (StatusText != null) StatusText.Text = "Saved " + target;
                }
                catch (Exception ex)
                {
                    if (StatusText != null) StatusText.Text = "Save failed: " + ex.Message;
                }

                return;
            }

            // No current file path -> fall back to Save As dialog
            var dlg = new SaveFileDialog { Filter = "Tiled Map (TMX)|*.tmx|JSON level|*.json|All files|*.*", DefaultExt = "tmx" };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    string ext = Path.GetExtension(dlg.FileName).ToLower();

                    if (ext == ".tmx")
                    {
                        string? exportTarget = loadedExportTarget;
                        if (string.IsNullOrEmpty(exportTarget))
                        {
                            exportTarget = Path.ChangeExtension(Path.GetFileName(dlg.FileName), ".csv");
                        }
                        int chunkHeight = loadedHasEditorSettings ? loadedChunkHeight : mapHeight;
                        var tmxLevel = new TmxLevel
                        {
                            Width = mapWidth,
                            Height = mapHeight,
                            Tiles = tiles,
                            Sprites = sprites,
                            TilesetSource = loadedTilesetSource ?? "../../../GRAPHICS/famidash.bmp",
                            SpritesetSource = loadedSpritesetSource ?? "../../../GRAPHICS/sprites.png",
                            HasEditorSettings = true,
                            ChunkWidth = loadedChunkWidth,
                            ChunkHeight = chunkHeight,
                            ExportTarget = loadedExportTarget,
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
                            HasGroundLayer = loadedHasGroundLayer,
                            DecoSet = loadedDecoSet
                        };
                        var saveCollisionMessages = TmxHandler.SaveTmx(dlg.FileName, tmxLevel, useLegacyTriggerOffset);
                        if (!string.IsNullOrEmpty(saveCollisionMessages))
                        {
                            MessageBox.Show("Sprite collision adjustments during save:\n\n" + saveCollisionMessages,
                                "Sprite Collision Resolution", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
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
        private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            // Capture modifier state
            var mods = System.Windows.Input.Keyboard.Modifiers;

            // ESC cancels polygon construction
            if (e.Key == Key.Escape && isConstructingPolygon)
            {
                CancelPolygon();
                e.Handled = true; return;
            }

            // Ctrl-based shortcuts
            if ((mods & ModifierKeys.Control) != 0)
            {
                // Ctrl+Z -> Undo, Ctrl+Shift+Z or Ctrl+Y -> Redo
                if (e.Key == Key.Z)
                {
                    if ((mods & ModifierKeys.Shift) != 0)
                    {
                        try { Redo(); } catch { }
                    }
                    else
                    {
                        try { Undo(); } catch { }
                    }
                    e.Handled = true; return;
                }

                if (e.Key == Key.Y)
                {
                    try { Redo(); } catch { }
                    e.Handled = true; return;
                }

                if (e.Key == Key.C)
                {
                    try { CopySelection(); } catch { }
                    e.Handled = true; return;
                }

                if (e.Key == Key.X)
                {
                    try { CutSelection(); } catch { }
                    e.Handled = true; return;
                }

                if (e.Key == Key.V)
                {
                    int dx = (selW > 0 && selH > 0) ? selX : (lastClickX >= 0 ? lastClickX : lastHoverX);
                    int dy = (selW > 0 && selH > 0) ? selY : (lastClickY >= 0 ? lastClickY : lastHoverY);
                        try { if (dx >= 0 && dy >= 0) PasteClipboardAt(dx, dy); } catch { }
                    e.Handled = true; return;
                }

                if (e.Key == Key.Y)
                {
                    try { Redo(); } catch { }
                    e.Handled = true; return;
                }

                if (e.Key == Key.O)
                {
                    try { LoadButton_Click(this, new RoutedEventArgs()); } catch { }
                    e.Handled = true; return;
                }

                if (e.Key == Key.N)
                {
                    try { NewMenuItem_Click(this, new RoutedEventArgs()); } catch { }
                    e.Handled = true; return;
                }
            }

            // Shift + Left/Right -> start continuous fine horizontal scrolling (1 px per tick)
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && (e.Key == Key.Left || e.Key == Key.Right))
            {
                if (MapScrollViewer != null)
                {
                    shiftArrowScrollDir = (e.Key == Key.Right) ? 1 : -1;
                    if (shiftArrowScrollTimer == null)
                    {
                        shiftArrowScrollTimer = new System.Windows.Threading.DispatcherTimer();
                        shiftArrowScrollTimer.Interval = TimeSpan.FromMilliseconds(16); // ~60Hz
                        shiftArrowScrollTimer.Tick += (s, ev) =>
                        {
                            try
                            {
                                // Fine-scroll 2 pixels per tick when Shift+Arrow is held
                                double newH = (MapScrollViewer?.HorizontalOffset ?? 0) + shiftArrowScrollDir * 2.0;
                                if (newH < 0) newH = 0;
                                double maxH = Math.Max(0, (CanvasHost?.ActualWidth ?? 0) - SafeViewportWidth());
                                if (newH > maxH) newH = maxH;
                                MapScrollViewer?.ScrollToHorizontalOffset(newH);
                            }
                            catch { }
                        };
                        shiftArrowScrollTimer.Start();
                    }
                    e.Handled = true; return;
                }
            }

            // Tool shortcuts (single-key, no Ctrl/Alt unless noted)
            if (e.Key == Key.P || e.Key == Key.B)
            {
                if (PlaceTool != null) { PlaceTool.IsChecked = true; e.Handled = true; return; }
            }
            // Left/Right alone -> coarse scroll (when no modifiers pressed)
            if ((mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) == 0 && (e.Key == Key.Left || e.Key == Key.Right))
            {
                if (MapScrollViewer != null)
                {
                    double coarseDelta = 32.0; // coarse scroll amount in pixels
                    double newH = (MapScrollViewer?.HorizontalOffset ?? 0) + (e.Key == Key.Right ? coarseDelta : -coarseDelta);
                    if (newH < 0) newH = 0;
                    double maxH = Math.Max(0, (CanvasHost?.ActualWidth ?? 0) - SafeViewportWidth());
                    if (newH > maxH) newH = maxH;
                    MapScrollViewer?.ScrollToHorizontalOffset(newH);
                    e.Handled = true; return;
                }
            }
            if (e.Key == Key.M)
            {
                if (MoveTool != null) MoveTool.IsChecked = true; e.Handled = true; return;
            }
            if (e.Key == Key.E)
            {
                if (EraseTool != null) EraseTool.IsChecked = true; e.Handled = true; return;
            }
            if (e.Key == Key.F)
            {
                if (FillTool != null) FillTool.IsChecked = true; e.Handled = true; return;
            }

            // Save / Select behavior on S: Ctrl+S = Save, Ctrl+Alt+S = Save As, plain S = Select (only if no modifiers)
            if (e.Key == Key.S)
            {
                // Ctrl+Alt+S -> Save As
                if ((mods & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt))
                {
                    try { MenuFileSaveAs_Click(this, new RoutedEventArgs()); } catch { }
                    e.Handled = true; return;
                }

                // Ctrl+S -> Save
                if ((mods & ModifierKeys.Control) != 0)
                {
                    try { SaveButton_Click(this, new RoutedEventArgs()); } catch { }
                    e.Handled = true; return;
                }

                // Only activate Select when no Ctrl/Alt modifiers are present
                if ((mods & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
                {
                    if (SelectTool != null) { SelectTool.IsChecked = true; e.Handled = true; return; }
                }
            }

            // Delete -> erase selection on active layers
            if (e.Key == Key.Delete)
            {
                try { EraseSelectedLayers(); } catch { }
                e.Handled = true; return;
            }

            if (e.Key == Key.W)
            {
                if (MagicWandTool != null) MagicWandTool.IsChecked = true; e.Handled = true; return;
            }
        }

        private void MainWindow_PreviewKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                // stop continuous shift-arrow scrolling
                if (shiftArrowScrollTimer != null)
                {
                    try { shiftArrowScrollTimer.Stop(); } catch { }
                    shiftArrowScrollTimer = null;
                    shiftArrowScrollDir = 0;
                }
            }
        }

        private void SetOptionsButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SetOptionsWindow(loadedDecoSet, loadedBlockSet, loadedSpikeSet) { Owner = this };
                bool? res = dlg.ShowDialog();
                    if (res == true)
                    {
                        string newDeco = dlg.SelectedDeco ?? "DECO1";
                        string newBlock = dlg.SelectedBlockSet ?? "BLOCKSA";
                        string newSpike = dlg.SelectedSpikeSet ?? "SPIKESA";
                        bool changed = false;
                        if (newDeco != loadedDecoSet)
                        {
                            loadedDecoSet = newDeco; changed = true;
                            if (StatusText != null) StatusText.Text = $"Deco set saved: {loadedDecoSet}";
                            try { RebuildAllSpritesBitmap((ZoomSlider != null ? ZoomSlider.Value : 1.0), mapViewportPadding); } catch { }
                            // If lock is enabled, re-apply disabled sprites immediately for the new deco
                            try { if (lockSpritesToSet) ApplyLockSpritesToSet(); } catch { }
                        }
                        if (newBlock != loadedBlockSet)
                        {
                            loadedBlockSet = newBlock; changed = true;
                            if (StatusText != null) StatusText.Text = $"Block set saved: {loadedBlockSet}";
                        }
                        if (newSpike != loadedSpikeSet)
                        {
                            loadedSpikeSet = newSpike; changed = true;
                            if (StatusText != null) StatusText.Text = $"Spike set saved: {loadedSpikeSet}";
                        }
                        if (changed)
                        {
                            // Save to per-level config without prompting
                            try { if (!string.IsNullOrEmpty(currentFilePath)) SaveTmxConfig(currentFilePath); } catch { }
                            try { Redraw(); } catch { }
                        }
                        // Also persist lock sprites option if dialog changed it (dialog wires owner on toggle but ensure saved)
                        try {
                            try { if (!string.IsNullOrEmpty(currentFilePath)) SaveTmxConfig(currentFilePath); } catch { }
                        } catch { }
                    }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("SetOptions dialog failed: " + ex.Message);
            }
        }

        // Programmatic setter to ensure SetOptionsWindow toggles behave identically
        public void SetNoParallax(bool enabled)
        {
            try
            {
                suppressNoParallaxHandler = true;
                noParallaxBg = enabled;
                if (MenuOptionNoParallax != null) MenuOptionNoParallax.IsChecked = enabled;
                try { if (!string.IsNullOrEmpty(currentFilePath)) SaveTmxConfig(currentFilePath); } catch { }
                try { ApplyParallaxChoice(); } catch { backgroundDirty = true; try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { Redraw(); } }
            }
            finally { suppressNoParallaxHandler = false; }
        }

        // Public wrappers so child dialogs can invoke the tint pickers on the main window
        public void ShowBackgroundTintPicker()
        {
            try { BgTintButton_Click(this, new RoutedEventArgs()); } catch { }
        }

        public void ShowGroundTintPicker()
        {
            try { GroundTintButton_Click(this, new RoutedEventArgs()); } catch { }
        }

        public void ShowTileTintPicker()
        {
            try { TileTintButton_Click(this, new RoutedEventArgs()); } catch { }
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

        private void NewMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Prompt to save if there are unsaved changes
            if (hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before creating a new map?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    SaveButton_Click(sender, e);
                    // If user cancelled the save dialog, abort the new operation
                    if (hasUnsavedChanges) return;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    return; // User cancelled the new operation
                }
                // If No, continue with new without saving
            }
            
            // Create a new 200x27 map
            mapWidth = 200;
            mapHeight = 27;
            // Initialize tiles and sprites to -1 (empty) using shared initializer
            InitDefaultMap();
            // Reset any per-position animation offsets so new empty map starts fresh
            try { spriteFrameOffsets.Clear(); } catch { }
            
            // Reset tints to defaults (transparent = no tint)
            backgroundTint = Color.FromArgb(0, 0, 0, 0);
            groundTint = Color.FromArgb(0, 0, 0, 0);
            tileTint = Color.FromArgb(0, 0, 0, 0);
            
            // Clear toned images to use originals
            parallaxTonedImages = null;
            groundTonedImages = null;
            tileTonedImages = null;
            sawFrame1TilesTinted = null;
            sawFrame2TilesTinted = null;
            
            // Mark background dirty and clear tile caches to force rebuild without tints
            backgroundDirty = true;
            try { scaledTileCaches.Clear(); } catch { }
            try { RebuildAllTilesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
            
            // Refresh tile and sprite palettes
            try { PopulateTilesPanel(); } catch { }
            try { PopulateSpritesPanel(); } catch { }
            
            // Clear undo/redo stacks
            undoStack.Clear();
            redoStack.Clear();
            
            // Reset file path and unsaved changes flag
            currentFilePath = "";
            hasUnsavedChanges = false;
            
            // Update UI
            if (WidthBox != null) WidthBox.Text = mapWidth.ToString();
            if (HeightBox != null) HeightBox.Text = mapHeight.ToString();
            if (StatusText != null) StatusText.Text = "New map created (200x27)";
            
            // Rebuild sprites layer and redraw the map
            try { RebuildAllSpritesBitmap((ZoomSlider!=null?ZoomSlider.Value:1.0), mapViewportPadding); } catch { }
            Redraw();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Prompt to save if there are unsaved changes
            if (hasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Do you want to save before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    SaveButton_Click(this, new RoutedEventArgs());
                    // If user cancelled the save dialog, cancel the close
                    if (hasUnsavedChanges)
                    {
                        e.Cancel = true;
                    }
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true; // User cancelled the close operation
                }
                // If No, continue with close without saving
            }
        }

        

        private void MenuFileSaveAs_Click(object? sender, RoutedEventArgs e)
        {
            // Force a Save As dialog (do not prefill with current file)
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Tiled Map (TMX)|*.tmx|JSON level|*.json|All files|*.*", DefaultExt = "tmx" };
            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    // Reuse Save logic by setting currentFilePath then calling SaveButton_Click
                    var prev = currentFilePath;
                    currentFilePath = dlg.FileName;
                    SaveButton_Click(this, e);
                    currentFilePath = dlg.FileName; // keep new path
                }
                catch (Exception ex)
                {
                    if (StatusText != null) StatusText.Text = "Save failed: " + ex.Message;
                }
            }
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
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
                        var tmxLevel = TmxHandler.LoadTmx(dlg.FileName, useLegacyTriggerOffset);
                        loadedWidth = tmxLevel.Width;
                        loadedHeight = tmxLevel.Height;
                        loadedTiles = tmxLevel.Tiles;
                        loadedSprites = tmxLevel.Sprites;
                        
                        // Show collision messages if any
                        if (!string.IsNullOrEmpty(tmxLevel.LoadCollisionMessages))
                        {
                            MessageBox.Show(this, "Sprite collision adjustments during load:\n\n" + tmxLevel.LoadCollisionMessages, 
                                "Sprite Collision Resolution", MessageBoxButton.OK, MessageBoxImage.Information);
                            
                            // Restore focus to main window after MessageBox
                            this.Activate();
                            this.Focus();
                        }
                        
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
                        // Load deco set from TMX if present; config file may override when LoadTmxConfig runs
                        try { loadedDecoSet = string.IsNullOrEmpty(tmxLevel.DecoSet) ? "deco1" : tmxLevel.DecoSet; } catch { loadedDecoSet = "deco1"; }
                        
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
                        
                        // For large maps, defer the redraw to allow loading window to update
                        bool isLargeMap = (loadedWidth * loadedHeight) > 50000;
                        if (isLargeMap && loadingWindow != null)
                        {
                            loadingWindow.SetMessage($"Rendering map ({loadedWidth}x{loadedHeight})...\nPlease wait, this may take a moment.");
                            // Force UI update and yield to allow loading window to display
                            await System.Threading.Tasks.Task.Delay(100);
                            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
                        }
                        
                        // If preview mode is enabled, disable it for TMX loads so preview-only
                        // decorations and portal previews do not interfere with the loaded map.
                        if (previewMode)
                        {
                            try
                            {
                                if (PreviewModeCheckbox != null)
                                {
                                    PreviewModeCheckbox.IsChecked = false; // triggers Unchecked handler
                                }
                                else
                                {
                                    // Fallback: manually disable preview mode
                                    previewMode = false;
                                    StopPreviewTimer();
                                    animationFrame = 0;
                                    if (portalsWb != null)
                                    {
                                        try
                                        {
                                            portalsWb.Lock();
                                            unsafe
                                            {
                                                IntPtr pBackBuffer = portalsWb.BackBuffer;
                                                if (pBackBuffer != IntPtr.Zero)
                                                {
                                                    int stride = portalsWb.BackBufferStride;
                                                    int bytesTotal = stride * cachedPixelHeight;
                                                    byte* ptr = (byte*)pBackBuffer.ToPointer();
                                                    for (int i = 0; i < bytesTotal; i++) ptr[i] = 0;
                                                }
                                            }
                                            portalsWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
                                        }
                                        finally { try { portalsWb.Unlock(); } catch { } }
                                    }
                                }
                            }
                            catch { }
                        }

                        // Force a full redraw with the new dimensions
                        // This will rebuild all bitmaps via EnsureLayerBitmaps
                        Redraw();
                        
                        // Snap to show ground at bottom (barely visible) and left side
                        if (MapScrollViewer != null)
                        {
                            MapScrollViewer?.UpdateLayout(); // Ensure layout is updated
                            MapScrollViewer?.ScrollToLeftEnd();
                            
                            // Scroll to show the first 3 rows of ground
                            double zoomScale = (ZoomSlider != null) ? ZoomSlider.Value : 1.0;
                            double tilePixelHeight = TileSize * zoomScale;
                            double pad = mapViewportPadding;
                            
                            // Calculate where the ground starts (after the main map)
                            double groundStartY = (mapHeight * tilePixelHeight) + pad;
                            
                            // Position viewport so the top of ground is visible, showing 3 rows of ground
                            // We want the viewport bottom to align with groundStart + 3 tiles
                            double viewportHeight = SafeViewportHeight();
                            double targetOffset = groundStartY + (tilePixelHeight * 3) - viewportHeight;
                            
                            // Clamp to valid scroll range
                            if (targetOffset < 0) targetOffset = 0;
                            double maxScroll = MapScrollViewer?.ScrollableHeight ?? 0.0;
                            if (targetOffset > maxScroll) targetOffset = maxScroll;
                            
                            MapScrollViewer?.ScrollToVerticalOffset(targetOffset);
                        }
                        
                        if (StatusText != null) StatusText.Text = $"Loaded {Path.GetFileName(dlg.FileName)} ({mapWidth}x{mapHeight})";
                        
                        // Load and apply saved tint configuration
                        LoadTmxConfig(dlg.FileName);

                        // Redraw to apply the loaded tints
                        Redraw();

                        // Ensure sprites bitmap is rebuilt after loading TMX so sprites appear
                        // even when the map size/scale did not change (avoid needing a preview toggle).
                        try { RebuildAllSpritesBitmap(ZoomSlider?.Value ?? 1.0, mapViewportPadding); } catch { }
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

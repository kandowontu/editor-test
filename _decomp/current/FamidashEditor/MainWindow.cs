#define DEBUG
using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FamidashEditor.MesenEmbedded;
using Microsoft.Win32;

namespace FamidashEditor;

public class MainWindow : Window, IComponentConnector
{
	private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	private sealed class SimRow
	{
		public int Frame;

		public int X;

		public int Y;

		public int A;
	}

	private sealed class RomRow
	{
		public int Attempt;

		public int RomFrame;

		public int SimCursor;

		public int Px;

		public int Py;

		public int ANext;
	}

	private enum ResizeAnchor
	{
		TopLeft,
		TopRight,
		BottomLeft,
		BottomRight
	}

	private enum DrawMode
	{
		Tile,
		Line,
		Square,
		Circle,
		Ellipse,
		Triangle,
		Polygon,
		None
	}

	private enum SelectMode
	{
		Normal,
		AllSame,
		Lasso,
		Ellipse
	}

	private enum FillMode
	{
		Normal,
		ReplaceSelected
	}

	private class FileTabData
	{
		private int? loadedStartingDifficulty = null;

		private int? loadedStartingStars = null;

		private int? loadedStartingGameMode = null;

		private int? loadedStartingBackgroundColor = null;

		private int? loadedStartingGroundColor = null;

		private string? loadedStartingLowerText = null;

		private string? loadedStartingUpperText = null;

		private int loadedSimulatorScale = 1;

		public string? FilePath { get; set; }

		public int[] Tiles { get; set; } = Array.Empty<int>();

		public int[] Sprites { get; set; } = Array.Empty<int>();

		public Dictionary<int, (int offsetX, int offsetY)> SpritePixelOffsets { get; set; } = new Dictionary<int, (int, int)>();

		public int MapWidth { get; set; } = 200;

		public int MapHeight { get; set; } = 27;

		public bool HasUnsavedChanges { get; set; } = false;

		public string? LoadedTilesetSource { get; set; }

		public string? LoadedSpritesetSource { get; set; }

		public bool LoadedHasEditorSettings { get; set; }

		public int LoadedChunkWidth { get; set; } = 16;

		public int LoadedChunkHeight { get; set; } = 27;

		public string? LoadedExportTarget { get; set; }

		public string LoadedExportFormat { get; set; } = "csv";

		public string? LoadedParallaxSource { get; set; }

		public double LoadedParallaxX { get; set; } = 0.9;

		public double LoadedParallaxY { get; set; } = 0.9;

		public bool LoadedParallaxRepeatX { get; set; } = true;

		public bool LoadedParallaxRepeatY { get; set; } = true;

		public bool LoadedHasParallaxLayer { get; set; }

		public string? LoadedGroundSource { get; set; }

		public double LoadedGroundOffsetY { get; set; } = 432.0;

		public bool LoadedGroundRepeatX { get; set; } = true;

		public bool LoadedHasGroundLayer { get; set; }

		public string LoadedDecoSet { get; set; } = "DECO1";

		public string LoadedBlockSet { get; set; } = "BLOCKSA";

		public string LoadedSpikeSet { get; set; } = "SPIKESA";

		public int? LoadedSpawnYPositionHi { get; set; }

		public int? LoadedSpawnYPositionLow { get; set; }

		public int? LoadedScrollYPositionHi { get; set; }

		public int? LoadedScrollYPositionLow { get; set; }

		public bool? LoadedForcePlatformer { get; set; }

		public int LoadedMaxFallSpeed { get; set; } = 6;

		public bool NoParallaxBg { get; set; }

		public Color BackgroundTint { get; set; } = Color.FromArgb(0, 0, 0, 0);

		public Color GroundTint { get; set; } = Color.FromArgb(0, 0, 0, 0);

		public Color TileTint { get; set; } = Color.FromArgb(0, 0, 0, 0);

		public string? SelectedSong { get; set; } = null;

		public int LoadedStartingSpeedUiIndex { get; set; } = 1;

		public int? LoadedStartingDifficulty
		{
			get
			{
				return loadedStartingDifficulty;
			}
			set
			{
				loadedStartingDifficulty = value;
			}
		}

		public int? LoadedStartingStars
		{
			get
			{
				return loadedStartingStars;
			}
			set
			{
				loadedStartingStars = value;
			}
		}

		public int? LoadedStartingGameMode
		{
			get
			{
				return loadedStartingGameMode;
			}
			set
			{
				loadedStartingGameMode = value;
			}
		}

		public int? LoadedStartingBackgroundColor
		{
			get
			{
				return loadedStartingBackgroundColor;
			}
			set
			{
				loadedStartingBackgroundColor = value;
			}
		}

		public int? LoadedStartingGroundColor
		{
			get
			{
				return loadedStartingGroundColor;
			}
			set
			{
				loadedStartingGroundColor = value;
			}
		}

		public string? LoadedStartingLowerText
		{
			get
			{
				return loadedStartingLowerText;
			}
			set
			{
				loadedStartingLowerText = value;
			}
		}

		public string? LoadedStartingUpperText
		{
			get
			{
				return loadedStartingUpperText;
			}
			set
			{
				loadedStartingUpperText = value;
			}
		}

		public int LoadedSimulatorScale
		{
			get
			{
				return loadedSimulatorScale;
			}
			set
			{
				loadedSimulatorScale = value;
			}
		}

		public bool CreatedAsUntitled { get; set; } = false;

		public int? StartPosX { get; set; } = null;

		public int? StartPosY { get; set; } = null;
	}

	private class TmxConfig
	{
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

		public string? SelectedSong { get; set; } = null;

		public Dictionary<string, int[]>? SpriteOffsets { get; set; } = null;

		public Dictionary<string, int[]>? SpriteAnchors { get; set; } = null;

		public int? StartingSpeed { get; set; } = null;

		public int? StartingBackgroundColor { get; set; } = null;

		public int? StartingGroundColor { get; set; } = null;

		public int? StartingGameMode { get; set; } = null;

		public int? Difficulty { get; set; } = null;

		public int? Stars { get; set; } = null;

		public int? MaxFallSpeed { get; set; } = null;

		public string? UpperText { get; set; } = null;

		public string? LowerText { get; set; } = null;

		public int? SimulatorScale { get; set; } = null;

		public int? SpawnYPositionHi { get; set; } = null;

		public int? SpawnYPositionLow { get; set; } = null;

		public int? ScrollYPositionHi { get; set; } = null;

		public int? ScrollYPositionLow { get; set; } = null;

		public bool? ForcePlatformer { get; set; } = null;
	}

	private class ScaledTileCache
	{
		public byte[][] Pixels;

		public int TileW;

		public int TileH;

		public int Stride;

		public double Scale;

		public DpiScale Dpi;

		public ScaledTileCache(byte[][] pixels, int w, int h, int stride, double scale, DpiScale dpi)
		{
			Pixels = pixels;
			TileW = w;
			TileH = h;
			Stride = stride;
			Scale = scale;
			Dpi = dpi;
		}
	}

	private class IndexInfo
	{
		public int[] Indices = Array.Empty<int>();

		public int MinX = int.MaxValue;

		public int MinY = int.MaxValue;

		public int MaxX = int.MinValue;

		public int MaxY = int.MinValue;

		public int Count
		{
			get
			{
				int[] indices = Indices;
				return (indices != null) ? indices.Length : 0;
			}
		}
	}

	private class LruCache<TKey, TValue> where TKey : notnull
	{
		private readonly int capacity;

		private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>();

		private readonly LinkedList<KeyValuePair<TKey, TValue>> list = new LinkedList<KeyValuePair<TKey, TValue>>();

		public TValue? this[TKey key]
		{
			get
			{
				if (TryGetValue(key, out TValue value))
				{
					return value;
				}
				return default(TValue);
			}
			set
			{
				Put(key, value);
			}
		}

		public LruCache(int capacity)
		{
			this.capacity = Math.Max(16, capacity);
		}

		public bool TryGetValue(TKey key, out TValue? value)
		{
			if (map.TryGetValue(key, out LinkedListNode<KeyValuePair<TKey, TValue>> value2))
			{
				list.Remove(value2);
				list.AddFirst(value2);
				value = value2.Value.Value;
				return true;
			}
			value = default(TValue);
			return false;
		}

		public void Put(TKey key, TValue? value)
		{
			if (map.TryGetValue(key, out LinkedListNode<KeyValuePair<TKey, TValue>> value2))
			{
				value2.Value = new KeyValuePair<TKey, TValue>(key, value);
				list.Remove(value2);
				list.AddFirst(value2);
				return;
			}
			KeyValuePair<TKey, TValue> value3 = new KeyValuePair<TKey, TValue>(key, value);
			LinkedListNode<KeyValuePair<TKey, TValue>> value4 = list.AddFirst(value3);
			map[key] = value4;
			if (map.Count > capacity)
			{
				LinkedListNode<KeyValuePair<TKey, TValue>> last = list.Last;
				if (last != null)
				{
					map.Remove(last.Value.Key);
					list.RemoveLast();
				}
			}
		}

		public void Clear()
		{
			map.Clear();
			list.Clear();
		}
	}

	private interface IUndoAction
	{
		void Undo(MainWindow window);

		void Redo(MainWindow window);
	}

	private class TileChangeAction : IUndoAction
	{
		public struct Change
		{
			public int Index;

			public int Old;

			public int New;
		}

		private readonly List<Change> changes = new List<Change>();

		private readonly Dictionary<int, int> indexMap = new Dictionary<int, int>();

		public void Add(int index, int oldVal, int newVal)
		{
			if (indexMap.TryGetValue(index, out var value))
			{
				Change change = changes[value];
				change = new Change
				{
					Index = change.Index,
					Old = change.Old,
					New = newVal
				};
				changes[value] = change;
			}
			else
			{
				indexMap[index] = changes.Count;
				changes.Add(new Change
				{
					Index = index,
					Old = oldVal,
					New = newVal
				});
			}
		}

		public bool IsEmpty()
		{
			return changes.Count == 0;
		}

		public void Undo(MainWindow window)
		{
			int[] tiles = window.tiles;
			foreach (Change change in changes)
			{
				if (change.Index >= 0 && change.Index < tiles.Length)
				{
					tiles[change.Index] = change.Old;
				}
			}
		}

		public void Redo(MainWindow window)
		{
			int[] tiles = window.tiles;
			foreach (Change change in changes)
			{
				if (change.Index >= 0 && change.Index < tiles.Length)
				{
					tiles[change.Index] = change.New;
				}
			}
		}
	}

	private class SpriteChangeAction : IUndoAction
	{
		public struct Change
		{
			public int Index;

			public int Old;

			public int New;
		}

		private readonly List<Change> changes = new List<Change>();

		private readonly Dictionary<int, int> indexMap = new Dictionary<int, int>();

		private readonly Dictionary<int, (int offsetX, int offsetY)?> oldOffsets = new Dictionary<int, (int, int)?>();

		private readonly Dictionary<int, (int offsetX, int offsetY)?> newOffsets = new Dictionary<int, (int, int)?>();

		public void Add(int index, int oldVal, int newVal)
		{
			if (indexMap.TryGetValue(index, out var value))
			{
				Change change = changes[value];
				change = new Change
				{
					Index = change.Index,
					Old = change.Old,
					New = newVal
				};
				changes[value] = change;
			}
			else
			{
				indexMap[index] = changes.Count;
				changes.Add(new Change
				{
					Index = index,
					Old = oldVal,
					New = newVal
				});
			}
		}

		public void CaptureOffsets(MainWindow window)
		{
			oldOffsets.Clear();
			foreach (Change change in changes)
			{
				if (window.spritePixelOffsets.TryGetValue(change.Index, out (int, int) value))
				{
					oldOffsets[change.Index] = value;
				}
				else
				{
					oldOffsets[change.Index] = null;
				}
			}
		}

		public void CaptureNewOffsets(MainWindow window)
		{
			newOffsets.Clear();
			foreach (Change change in changes)
			{
				if (window.spritePixelOffsets.TryGetValue(change.Index, out (int, int) value))
				{
					newOffsets[change.Index] = value;
				}
				else
				{
					newOffsets[change.Index] = null;
				}
			}
		}

		public bool IsEmpty()
		{
			return changes.Count == 0;
		}

		public void Undo(MainWindow window)
		{
			int[] sprites = window.sprites;
			foreach (Change change in changes)
			{
				if (change.Index >= 0 && change.Index < sprites.Length)
				{
					sprites[change.Index] = change.Old;
				}
			}
			foreach (KeyValuePair<int, (int, int)?> oldOffset in oldOffsets)
			{
				if (oldOffset.Value.HasValue)
				{
					window.spritePixelOffsets[oldOffset.Key] = oldOffset.Value.Value;
				}
				else
				{
					window.spritePixelOffsets.Remove(oldOffset.Key);
				}
			}
			try
			{
				window.RebuildAllSpritesBitmap((window.ZoomSlider != null) ? window.ZoomSlider.Value : 1.0, window.mapViewportPadding);
			}
			catch
			{
				window.Redraw();
			}
			if (window.selectionSet != null && window.selectionSet.Count > 0)
			{
				window.UpdateSelectionVisuals(window.selX, window.selY, window.selW, window.selH);
			}
		}

		public void Redo(MainWindow window)
		{
			int[] sprites = window.sprites;
			foreach (Change change in changes)
			{
				if (change.Index >= 0 && change.Index < sprites.Length)
				{
					sprites[change.Index] = change.New;
				}
			}
			foreach (KeyValuePair<int, (int, int)?> newOffset in newOffsets)
			{
				if (newOffset.Value.HasValue)
				{
					window.spritePixelOffsets[newOffset.Key] = newOffset.Value.Value;
				}
				else
				{
					window.spritePixelOffsets.Remove(newOffset.Key);
				}
			}
			try
			{
				window.RebuildAllSpritesBitmap((window.ZoomSlider != null) ? window.ZoomSlider.Value : 1.0, window.mapViewportPadding);
			}
			catch
			{
				window.Redraw();
			}
			if (window.selectionSet != null && window.selectionSet.Count > 0)
			{
				window.UpdateSelectionVisuals(window.selX, window.selY, window.selW, window.selH);
			}
		}
	}

	private class MapResizeAction : IUndoAction
	{
		private readonly int oldW;

		private readonly int oldH;

		private readonly int[] oldTiles;

		private readonly int newW;

		private readonly int newH;

		private readonly int[] newTiles;

		public MapResizeAction(int oldW, int oldH, int[] oldTiles, int newW, int newH, int[] newTiles)
		{
			this.oldW = oldW;
			this.oldH = oldH;
			this.oldTiles = oldTiles;
			this.newW = newW;
			this.newH = newH;
			this.newTiles = newTiles;
		}

		public void Undo(MainWindow window)
		{
			window.tiles = oldTiles;
			window.mapWidth = oldW;
			window.mapHeight = oldH;
			if (window.WidthBox != null)
			{
				window.WidthBox.Text = oldW.ToString();
			}
			if (window.HeightBox != null)
			{
				window.HeightBox.Text = oldH.ToString();
			}
			window.Redraw();
		}

		public void Redo(MainWindow window)
		{
			window.tiles = newTiles;
			window.mapWidth = newW;
			window.mapHeight = newH;
			if (window.WidthBox != null)
			{
				window.WidthBox.Text = newW.ToString();
			}
			if (window.HeightBox != null)
			{
				window.HeightBox.Text = newH.ToString();
			}
			window.Redraw();
		}
	}

	private class PathfinderSaveData
	{
		public int Version { get; set; } = 1;

		public List<bool> Inputs { get; set; } = new List<bool>();

		public List<int>? CollectedCoins { get; set; }

		public PathfinderValidation Validation { get; set; } = new PathfinderValidation();
	}

	private class PathfinderValidation
	{
		public int MapWidth { get; set; }

		public int MapHeight { get; set; }

		public int StartGameMode { get; set; }

		public int StartSpeedUiIndex { get; set; }

		public int MaxFallSpeed { get; set; }

		public long TileChecksum { get; set; }

		public long SpriteChecksum { get; set; }
	}

	private const string SimFamidashFolder = "C:\\Editor Test\\sim-famidash";

	private const string SimLevelsFolder = "C:\\Editor Test\\sim-famidash\\LEVELS\\level data\\lvlset_D";

	private const string SimLevelMetadata = "C:\\Editor Test\\sim-famidash\\LEVELS\\metadata\\lvlset_D_metadata.json5";

	private const string SimMusicMetadata = "C:\\Editor Test\\sim-famidash\\MUSIC\\metadata\\lvlset_D_metadata.json5";

	private const string SimBuildBat = "C:\\Editor Test\\sim-famidash\\1BigSimExport.bat";

	private const string SimOutputRom = "C:\\Editor Test\\sim-famidash\\Famidash.nes";

	private string? mesenPath = null;

	private string? famidashRomPath = null;

	private string mesenRamAddresses = "0000,0001,0002,0003,0010,0011,0012,0013";

	private int mesenCaptureFrames = 1200;

	private int mesenCaptureEveryNFrames = 1;

	private int mesenCaptureTimeoutSeconds = 180;

	private MesenHwndHost? _mesenEmbeddedHwndHost;

	private bool _mesenEmbeddedShown;

	private const double MesenEmbeddedDefaultWidth = 532.0;

	private bool _overlayAndFollow = true;

	private bool _camFollow = true;

	private Process? _mesenOverlayProcess;

	private IntPtr _mesenHwnd;

	private int _latestNesScrollX;

	private int _latestNesScrollY;

	private int _rawNesScrollX = int.MinValue;

	private CancellationTokenSource? _overlayReadCts;

	private Task? _overlayReadTask;

	private int _lastMesenScreenX = int.MinValue;

	private int _lastMesenScreenY = int.MinValue;

	private int _lastMesenWinW = int.MinValue;

	private int _lastMesenWinH = int.MinValue;

	private double _lastScrollOffsetX = double.NaN;

	private double _lastScrollOffsetY = double.NaN;

	private double _fixedAnchorViewportX = double.NaN;

	private double _fixedAnchorViewportY = double.NaN;

	private double _initialCameraCanvasY = double.NaN;

	private double _smoothScrollX = double.NaN;

	private double _smoothScrollTargetX = double.NaN;

	private int _smoothScrollCoarseXPx = int.MinValue;

	private int _smoothScrollShiftXPx = int.MinValue;

	private TranslateTransform? _smoothScrollTransform;

	private bool _overlayRenderHooked = false;

	private string? _mesenLogStamp;

	private const uint SWP_NOACTIVATE = 16u;

	private const uint SWP_NOZORDER = 4u;

	private const uint SWP_SHOWWINDOW = 64u;

	private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

	public static bool Option_ShowSimulatorSpriteHitboxes = false;

	public static bool Option_ShowTileHitboxes = false;

	public static bool Option_NoDeath = false;

	private bool isResizingSelection = false;

	private Point resizeStartMouse = new Point(0.0, 0.0);

	private int resizeOrigW = 0;

	private int resizeOrigH = 0;

	private int[,] resizeOrigTiles = null;

	private int[,] resizeOrigSprites = null;

	private ResizeAnchor resizeAnchor = ResizeAnchor.TopLeft;

	private bool isResizeModeActive = false;

	public static bool Option_CamMode = false;

	public static bool Option_EmbeddedMesen = false;

	private int structureSetOffset = 0;

	private int structureSetBaseTile = 32;

	private FamiStudioIntegration famiIntegration = new FamiStudioIntegration();

	private string? famiStudioPath = null;

	private bool mappingLoadedFromFile = false;

	private string? albumTxtPath = null;

	private DrawMode currentDrawMode = DrawMode.Tile;

	private bool hollowShape = false;

	private int brushThickness = 1;

	private bool isDeferredDrawing = false;

	private int drawStartX = -1;

	private int drawStartY = -1;

	private int drawCurrentX = -1;

	private int drawCurrentY = -1;

	private bool deferredDrawFromModifier = false;

	private List<(int x, int y)> polygonPoints = new List<(int, int)>();

	private bool isConstructingPolygon = false;

	private int lastKnownSelectedTile = -2;

	private int lastKnownSelectedSprite = -2;

	private DateTime lastInputAction = DateTime.MinValue;

	private bool isRightMouseDown = false;

	private Point rightMouseDownPosition;

	private bool rightDragStarted = false;

	private const double RightDragThreshold = 4.0;

	private bool pendingDrag = false;

	private bool pendingSelection = false;

	private DispatcherTimer? shiftArrowScrollTimer = null;

	private int shiftArrowScrollDir = 0;

	private bool initialLeftSizingDone = false;

	private bool suppressManualTileChange = false;

	private bool suppressManualSpriteChange = false;

	private const int TileSize = 16;

	private bool isAdjustingPanels = false;

	private int mapWidth = 200;

	private int mapHeight = 27;

	private int[] tiles = Array.Empty<int>();

	private int[] sprites = Array.Empty<int>();

	private Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets = new Dictionary<int, (int, int)>();

	private bool useLegacyTriggerOffset = false;

	private bool hideColorTriggers = false;

	private bool hideInvisibleSprites = false;

	private bool suppressCollisionMessages = true;

	private bool noParallaxBg = false;

	private bool suppressNoParallaxHandler = false;

	private bool swapMouseWheelScroll = false;

	private bool invertPinchGesture = true;

	private bool pinchDirectionDetected = false;

	private bool hideTriggerSprites = true;

	private bool hideBackground = false;

	private bool hideGround = false;

	private bool useFastZoom = false;

	private SelectMode currentSelectMode = SelectMode.Normal;

	private FillMode currentFillMode = FillMode.Normal;

	private bool suppressSettingsSave = true;

	private string tileboardPosition = "LEFT";

	private double lastManipulationCumulativeScale = 1.0;

	private bool manipulationActive = false;

	private double gridDarkness = 0.18;

	private Brush mapBackground = new SolidColorBrush(Color.FromRgb(59, 59, 59));

	private Color backgroundTint = Color.FromArgb(0, 0, 0, 0);

	private Color groundTint = Color.FromArgb(0, 0, 0, 0);

	private Color tileTint = Color.FromArgb(0, 0, 0, 0);

	private Color defaultBackgroundTint = Color.FromArgb(byte.MaxValue, 0, 23, 116);

	private Color defaultGroundTint = Color.FromArgb(byte.MaxValue, 0, 23, 116);

	private Color defaultTileTint = Color.FromArgb(byte.MaxValue, 0, 23, 116);

	private Color playerTint = Color.FromArgb(0, 0, 0, 0);

	private bool playerTintEnabled = false;

	private bool manualTileSize = false;

	private bool manualSpriteSize = false;

	private BitmapSource? tilesetBitmap;

	private BitmapSource? spritesBitmap;

	private BitmapSource? parallaxBitmap;

	private BitmapSource? groundBitmap;

	private ImageSource?[]? tileImages;

	private ImageSource?[]? spriteImages;

	private ImageSource?[]? parallaxImages;

	private ImageSource?[]? groundImages;

	private ImageSource?[]? parallaxTonedImages;

	private ImageSource?[]? groundTonedImages;

	private ImageSource?[]? tileTonedImages;

	private int selectedTile = 0;

	private int selectedSprite = -1;

	private List<int> selectedTiles = new List<int> { 0 };

	private List<int> selectedSprites = new List<int>();

	private int selectionWidth = 1;

	private int selectionHeight = 1;

	private bool isSelectingMultipleTiles = false;

	private Point? tileSelectionStart = null;

	private int[]? clipboardTiles = null;

	private int[]? clipboardSprites = null;

	private bool[]? clipboardMask = null;

	private int clipboardW = 0;

	private int clipboardH = 0;

	private bool clipboardHasData = false;

	private bool tilesLayerActive = true;

	private bool spritesLayerActive = false;

	private string? currentFilePath = null;

	private bool hasUnsavedChanges = false;

	private int? loadedStartingGameMode = null;

	private int? loadedSpawnYPositionHi = null;

	private int? loadedSpawnYPositionLow = null;

	private int? loadedScrollYPositionHi = null;

	private int? loadedScrollYPositionLow = null;

	private bool? loadedForcePlatformer = null;

	private List<FileTabData> openFiles = new List<FileTabData>();

	private int currentFileIndex = -1;

	private List<string> recentFiles = new List<string>();

	private const int MaxRecentFiles = 10;

	private bool isHandlingNewTab = false;

	private TabItem? lastSelectedTab = null;

	private TabItem? lastProgrammaticSelectedTab = null;

	private bool isSwitchingTab = false;

	private string? loadedTilesetSource = null;

	private string? loadedSpritesetSource = null;

	private bool loadedHasEditorSettings = false;

	private int loadedChunkWidth = 16;

	private int loadedChunkHeight = 27;

	private string? loadedExportTarget = null;

	private string loadedExportFormat = "csv";

	private string? loadedParallaxSource = null;

	private string? originalParallaxSource = null;

	private double loadedParallaxX = 0.9;

	private double loadedParallaxY = 0.9;

	private bool loadedParallaxRepeatX = true;

	private bool loadedParallaxRepeatY = true;

	private bool loadedHasParallaxLayer = false;

	private string? loadedGroundSource = null;

	private double loadedGroundOffsetY = 432.0;

	private bool loadedGroundRepeatX = true;

	private bool loadedHasGroundLayer = false;

	private bool isTileboardHidden = false;

	private string loadedDecoSet = "DECO1";

	private string loadedBlockSet = "BLOCKSA";

	private string loadedSpikeSet = "SPIKESA";

	private int loadedStartingSpeedUiIndex = 1;

	private int? loadedStartingDifficulty = null;

	private int? loadedStartingStars = null;

	private int loadedSimulatorScale = 1;

	private bool loadedOpenSimulatorPaused = true;

	private int? loadedStartingBackgroundColor = null;

	private int? loadedStartingGroundColor = null;

	private string? loadedStartingLowerText = null;

	private string? loadedStartingUpperText = null;

	private int paletteTileSize = 16;

	private int paletteSpriteSize = 16;

	private int loadedMaxFallSpeed = 6;

	private bool isPainting = false;

	private int lastPaintX = -1;

	private int lastPaintY = -1;

	private bool hasMouseMoved = false;

	private Point mouseDownPosition;

	private bool isMiddlePanning = false;

	private Point middlePanStart;

	private double panStartHOffset = 0.0;

	private double panStartVOffset = 0.0;

	private bool isSelecting = false;

	private int selectStartX = -1;

	private int selectStartY = -1;

	private int selX = -1;

	private int selY = -1;

	private int selW = 0;

	private int selH = 0;

	private int[]? selTiles = null;

	private int[]? selSprites = null;

	private Dictionary<int, (int offsetX, int offsetY)> selSpriteOffsets = new Dictionary<int, (int, int)>();

	private Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors = new Dictionary<int, (int, int)>();

	private HashSet<int> selectionSet = new HashSet<int>();

	private bool isDraggingSelection = false;

	private bool dragMovedOffMap = false;

	private Point dragStartMouse;

	private int dragOrigX = 0;

	private int dragOrigY = 0;

	private Point dragOffset;

	private int ghostLogicalWidth = 0;

	private int ghostLogicalHeight = 0;

	private double lastDragScale = 1.0;

	private int dragFinalTileX = 0;

	private int dragFinalTileY = 0;

	private int dragFinalOffsetX = 0;

	private int dragFinalOffsetY = 0;

	private double mapViewportPadding = 64.0;

	private readonly Stack<IUndoAction> undoStack = new Stack<IUndoAction>();

	private readonly Stack<IUndoAction> redoStack = new Stack<IUndoAction>();

	private HashSet<int> disabledSprites = new HashSet<int>();

	private bool lockSpritesToSet = true;

	private bool showAccurateTileset = true;

	private TileChangeAction? currentCompositeAction = null;

	private SpriteChangeAction? currentCompositeSpriteAction = null;

	private bool suppressUndoRecording = false;

	private int groundTileRows = 0;

	private RenderTargetBitmap? backgroundRtb = null;

	private RenderTargetBitmap? parallaxRtb = null;

	private RenderTargetBitmap? groundRtb = null;

	private RenderTargetBitmap? gridRtb = null;

	private WriteableBitmap? tilesWb = null;

	private WriteableBitmap? portalsWb = null;

	private WriteableBitmap? spritesWb = null;

	private readonly Dictionary<int, ScaledTileCache> scaledTileCaches = new Dictionary<int, ScaledTileCache>();

	private readonly Dictionary<long, BitmapSource> parallaxTintCache = new Dictionary<long, BitmapSource>();

	private readonly Dictionary<long, BitmapSource> groundTintCache = new Dictionary<long, BitmapSource>();

	private int cachedPixelWidth = 0;

	private int cachedPixelHeight = 0;

	private double cachedScale = 1.0;

	private bool backgroundDirty = true;

	private bool gridDirty = true;

	private const double ParallaxRatio = 0.9;

	private TranslateTransform? parallaxTransform = new TranslateTransform(0.0, 0.0);

	private static readonly SolidColorBrush SelectionFillBrush = new SolidColorBrush(Color.FromArgb(64, byte.MaxValue, byte.MaxValue, byte.MaxValue));

	private static readonly Brush SelectionStrokeBrush = Brushes.Cyan;

	private readonly Dictionary<int, IndexInfo> tileIndexMap = new Dictionary<int, IndexInfo>();

	private readonly Dictionary<int, IndexInfo> spriteIndexMap = new Dictionary<int, IndexInfo>();

	private int lastHoveredSelectSameTileId = int.MinValue;

	private int lastHoveredSelectSameSpriteId = int.MinValue;

	private static readonly SolidColorBrush SelectSameFillBrush = new SolidColorBrush(Color.FromArgb(64, 150, 200, byte.MaxValue));

	private static readonly Brush SelectSameStrokeBrush = Brushes.LightSkyBlue;

	private bool selectionIsLarge = false;

	private const int LargeSelectionThreshold = 5000;

	private const int MaskRenderMaxPixels = 2000000;

	private const int MaskRenderIndexThreshold = 2000;

	private DispatcherTimer? selectSameHoverTimer = null;

	private int pendingHoverTileId = int.MinValue;

	private bool pendingHoverUseTiles = true;

	private int[]? pendingHoverIndicesRef = null;

	private int lastHoverX = -1;

	private int lastHoverY = -1;

	private int lastClickX = -1;

	private int lastClickY = -1;

	private DispatcherTimer? sizeChangedThrottleTimer;

	private DispatcherTimer? zoomThrottleTimer;

	private DispatcherTimer? zoomCommitTimer;

	private bool deferZoomRebuild = false;

	private bool isSnappingZoom = false;

	private bool hasZoomAnchor = false;

	private double zoomAnchorMapX = 0.0;

	private double zoomAnchorMapY = 0.0;

	private double zoomAnchorViewportX = 0.0;

	private double zoomAnchorViewportY = 0.0;

	private double gridRenderShiftY = 0.0;

	private int gridRenderShiftYPx = 0;

	private List<(int x, int y)> playerPathPoints = new List<(int, int)>();

	private List<(int x, int y)> playerPath2Points = new List<(int, int)>();

	private Polyline? playerPathPolyline = null;

	private Polyline? playerPath2Polyline = null;

	private List<(List<(int x, int y)> points, Color color)> pathfinderPaths = new List<(List<(int, int)>, Color)>();

	private List<(List<(int x, int y)> points, Color color)> pathfinderPath2s = new List<(List<(int, int)>, Color)>();

	private List<UIElement> pathfinderPathPolylines = new List<UIElement>();

	private List<List<(int x, int y)>> attemptedPaths = new List<List<(int, int)>>();

	private List<UIElement> attemptedPathPolylines = new List<UIElement>();

	private List<List<(int x, int y)>> mesenPaths = new List<List<(int, int)>>();

	private List<UIElement> mesenPathPolylines = new List<UIElement>();

	private bool _attemptedPathsCleared = false;

	private List<UIElement> _speculativePolylines = new List<UIElement>();

	private List<List<(int x, int y)>> _speculativePathData = new List<List<(int, int)>>();

	private PathfinderEngine? _activePathfinderEngine = null;

	private Line? playerDeathMarkerA = null;

	private Line? playerDeathMarkerB = null;

	private UIElement? startPosMarker = null;

	private int? startPosMarkerX = null;

	private int? startPosMarkerY = null;

	private UIElement? spawnYOverlayMarker = null;

	private UIElement? cameraYOverlayMarker = null;

	private List<bool>? precomputedPathfinderInputs = null;

	private HashSet<int>? precomputedCollectedCoins = null;

	private HashSet<int>? precomputedSkippedPads = null;

	private bool previewMode = false;

	private int animationFrame = 0;

	private int timerTicks = 0;

	private DispatcherTimer? previewTimer;

	private BitmapSource[]? sawFrame1Tiles;

	private BitmapSource[]? sawFrame2Tiles;

	private ImageSource?[]? sawFrame1TilesTinted;

	private ImageSource?[]? sawFrame2TilesTinted;

	private BitmapSource[]? smallSawFrame1Tiles;

	private BitmapSource[]? smallSawFrame2Tiles;

	private ImageSource?[]? smallSawFrame1TilesTinted;

	private ImageSource?[]? smallSawFrame2TilesTinted;

	private BitmapSource[]? largeSawFrame1Tiles;

	private BitmapSource[]? largeSawFrame2Tiles;

	private ImageSource?[]? largeSawFrame1TilesTinted;

	private ImageSource?[]? largeSawFrame2TilesTinted;

	private BitmapSource? cubePortalSprite;

	private BitmapSource? shipPortalSprite;

	private BitmapSource? ballPortalSprite;

	private BitmapSource? ufoPortalSprite;

	private BitmapSource? robotPortalSprite;

	private BitmapSource? wavePortalSprite;

	private BitmapSource? spiderPortalSprite;

	private BitmapSource? spiderPadSprite;

	private BitmapSource? spiderPadUpsideDownSprite;

	private BitmapSource? swingcopterPortalSprite;

	private BitmapSource? ninjaPortalSprite;

	private BitmapSource? pogoPortalSprite;

	private BitmapSource? snakePortalSprite;

	private BitmapSource? footballPortalSprite;

	private BitmapSource? teleportPortalEnterSprite;

	private BitmapSource? teleportPortalExitSprite;

	private BitmapSource? teleportPortalHorizontalEnterDownSprite;

	private BitmapSource? teleportPortalHorizontalExitUpSprite;

	private BitmapSource? teleportPortalHorizontalEnterUpSprite;

	private BitmapSource? teleportPortalHorizontalExitDownSprite;

	private BitmapSource? speed05xPortalSprite;

	private BitmapSource? speed1xPortalSprite;

	private BitmapSource? speed2xPortalSprite;

	private BitmapSource? speed3xPortalSprite;

	private BitmapSource? speed4xPortalSprite;

	private BitmapSource? speedSpecialPortalSprite;

	private BitmapSource? dualPortalSprite;

	private BitmapSource? singlePortalSprite;

	private BitmapSource? miniPortalSprite;

	private BitmapSource? growthPortalSprite;

	private BitmapSource? gravityDownPortalSprite;

	private BitmapSource? gravityUpPortalSprite;

	private BitmapSource? gravityDownDownwardsPortalSprite;

	private BitmapSource? gravityDownUpwardsPortalSprite;

	private BitmapSource? gravityUpDownwardsPortalSprite;

	private BitmapSource? gravityUpUpwardsPortalSprite;

	private BitmapSource? gravity1ThirdXPortalSprite;

	private BitmapSource? gravity1HalfXPortalSprite;

	private BitmapSource? gravity2ThirdXPortalSprite;

	private BitmapSource? gravity2XPortalSprite;

	private BitmapSource? gravity1XPortalSprite;

	private BitmapSource[]? yellowOrbFrame1;

	private BitmapSource[]? yellowOrbFrame2;

	private BitmapSource[]? yellowOrbFrame3;

	private BitmapSource[]? yellowOrbFrame4;

	private BitmapSource[]? blueOrbFrame1;

	private BitmapSource[]? blueOrbFrame2;

	private BitmapSource[]? blueOrbFrame3;

	private BitmapSource[]? blueOrbFrame4;

	private BitmapSource[]? whiteOrbFrame1;

	private BitmapSource[]? whiteOrbFrame2;

	private BitmapSource[]? whiteOrbFrame3;

	private BitmapSource[]? whiteOrbFrame4;

	private BitmapSource[]? coinFrame1;

	private BitmapSource[]? coinFrame2;

	private BitmapSource[]? coinFrame3;

	private BitmapSource[]? coinFrame4;

	private BitmapSource[]? miniCoinFrame1;

	private BitmapSource[]? miniCoinFrame2;

	private BitmapSource[]? miniCoinFrame3;

	private BitmapSource[]? miniCoinFrame4;

	private BitmapSource[]? redPadFrame1;

	private BitmapSource[]? redPadFrame2;

	private BitmapSource[]? redPadFrame3;

	private BitmapSource[]? redPadFrame4;

	private BitmapSource[]? redPadUpFrame1;

	private BitmapSource[]? redPadUpFrame2;

	private BitmapSource[]? redPadUpFrame3;

	private BitmapSource[]? redPadUpFrame4;

	private BitmapSource[]? yellowPadDownFrame1;

	private BitmapSource[]? yellowPadDownFrame2;

	private BitmapSource[]? yellowPadDownFrame3;

	private BitmapSource[]? yellowPadDownFrame4;

	private BitmapSource[]? yellowPadUpFrame1;

	private BitmapSource[]? yellowPadUpFrame2;

	private BitmapSource[]? yellowPadUpFrame3;

	private BitmapSource[]? yellowPadUpFrame4;

	private BitmapSource[]? bluePadDownFrame1;

	private BitmapSource[]? bluePadDownFrame2;

	private BitmapSource[]? bluePadDownFrame3;

	private BitmapSource[]? bluePadDownFrame4;

	private BitmapSource[]? bluePadUpFrame1;

	private BitmapSource[]? bluePadUpFrame2;

	private BitmapSource[]? bluePadUpFrame3;

	private BitmapSource[]? bluePadUpFrame4;

	private BitmapSource[]? pinkPadDownFrame1;

	private BitmapSource[]? pinkPadDownFrame2;

	private BitmapSource[]? pinkPadDownFrame3;

	private BitmapSource[]? pinkPadDownFrame4;

	private BitmapSource[]? pinkPadUpFrame1;

	private BitmapSource[]? pinkPadUpFrame2;

	private BitmapSource[]? pinkPadUpFrame3;

	private BitmapSource[]? pinkPadUpFrame4;

	private BitmapSource[]? greenPadExpandedFrame1;

	private BitmapSource[]? greenPadExpandedFrame2;

	private BitmapSource[]? greenPadExpandedFrame3;

	private BitmapSource[]? greenPadExpandedFrame4;

	private BitmapSource[]? pinkOrbFrame1;

	private BitmapSource[]? pinkOrbFrame2;

	private BitmapSource[]? pinkOrbFrame3;

	private BitmapSource[]? pinkOrbFrame4;

	private BitmapSource[]? greenOrbFrame1;

	private BitmapSource[]? greenOrbFrame2;

	private BitmapSource[]? greenOrbFrame3;

	private BitmapSource[]? greenOrbFrame4;

	private BitmapSource[]? redOrbFrame1;

	private BitmapSource[]? redOrbFrame2;

	private BitmapSource[]? redOrbFrame3;

	private BitmapSource[]? redOrbFrame4;

	private BitmapSource[]? blackOrbFrame1;

	private BitmapSource[]? blackOrbFrame2;

	private BitmapSource[]? blackOrbFrame3;

	private BitmapSource[]? blackOrbFrame4;

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

	private BitmapSource[]? teleportOrbEnterFrame1;

	private BitmapSource[]? teleportOrbEnterFrame2;

	private BitmapSource[]? teleportOrbExitFrame1;

	private BitmapSource[]? teleportOrbExitFrame2;

	private BitmapSource[]? spiderOrbDownFrame1;

	private BitmapSource[]? spiderOrbDownFrame2;

	private BitmapSource[]? spiderOrbUpFrame1;

	private BitmapSource[]? spiderOrbUpFrame2;

	private BitmapSource[]? starFrame1;

	private BitmapSource[]? starFrame2;

	private BitmapSource[]? pulsingBallFrame1;

	private BitmapSource[]? pulsingBallFrame2;

	private BitmapSource[]? musicNoteFrame1;

	private BitmapSource[]? musicNoteFrame2;

	private BitmapSource[]? diamondFrame1;

	private BitmapSource[]? diamondFrame2;

	private BitmapSource[]? diamondHalfFrame1;

	private BitmapSource[]? diamondHalfFrame2;

	private BitmapSource[]? questionMarkFrame1;

	private BitmapSource[]? questionMarkFrame2;

	private BitmapSource[]? exclamationFrame1;

	private BitmapSource[]? exclamationFrame2;

	private BitmapSource[]? xFrame1;

	private BitmapSource[]? xFrame2;

	private BitmapSource[]? poleShortFrame1;

	private BitmapSource[]? poleShortFrame2;

	private BitmapSource[]? poleShortUpsideDownFrame1;

	private BitmapSource[]? poleShortUpsideDownFrame2;

	private BitmapSource[]? poleLeftShortFrame1;

	private BitmapSource[]? poleLeftShortFrame2;

	private BitmapSource[]? poleRightShortFrame1;

	private BitmapSource[]? poleRightShortFrame2;

	private BitmapSource[]? poleLeftMediumFrame1;

	private BitmapSource[]? poleLeftMediumFrame2;

	private BitmapSource[]? poleRightMediumFrame1;

	private BitmapSource[]? poleRightMediumFrame2;

	private BitmapSource[]? poleMediumFrame1;

	private BitmapSource[]? poleMediumFrame2;

	private BitmapSource[]? poleMediumUpsideDownFrame1;

	private BitmapSource[]? poleMediumUpsideDownFrame2;

	private BitmapSource[]? poleLongFrame1;

	private BitmapSource[]? poleLongFrame2;

	private BitmapSource[]? poleLongUpsideDownFrame1;

	private BitmapSource[]? poleLongUpsideDownFrame2;

	private BitmapSource[]? chainFrame1;

	private BitmapSource[]? chainUpsideFrame1;

	private BitmapSource[]? decoSpikesFrame1;

	private BitmapSource[]? decoSpikesUpsideDownFrame1;

	private BitmapSource[]? decoSpikesSmallFrame1;

	private BitmapSource[]? decoSpikesSmallUpsideDownFrame1;

	private Dictionary<int, int> spriteFrameOffsets = new Dictionary<int, int>();

	private Random spriteAnimationRandom = new Random();

	private int currentSpritePositionKey = 0;

	private const int TintCacheCapacity = 512;

	private readonly LruCache<long, BitmapSource?> tintedSpriteCache = new LruCache<long, BitmapSource>(512);

	private readonly LruCache<long, BitmapSource?> tintedCustomCache = new LruCache<long, BitmapSource>(512);

	private readonly object tintedCacheLock = new object();

	private readonly HashSet<int> decorationSpriteIds = new HashSet<int>
	{
		54, 50, 51, 52, 53, 55, 44, 60, 45, 61,
		46, 47, 48, 49, 56, 57, 62, 63, 43, 59,
		42, 58, 73, 74
	};

	private readonly HashSet<int> nonPlayerTintSpriteIds = new HashSet<int> { 7, 26, 27, 110 };

	private string? portalDebugPath = null;

	private int portalDirtyMinX = int.MaxValue;

	private int portalDirtyMinY = int.MaxValue;

	private int portalDirtyMaxX = int.MinValue;

	private int portalDirtyMaxY = int.MinValue;

	private bool portalDirtyScheduled = false;

	private DispatcherTimer? portalDirtyTimer = null;

	private int incompatibleDirtyMinX = int.MaxValue;

	private int incompatibleDirtyMinY = int.MaxValue;

	private int incompatibleDirtyMaxX = int.MinValue;

	private int incompatibleDirtyMaxY = int.MinValue;

	private bool incompatibleDirtyScheduled = false;

	private DispatcherTimer? incompatibleDirtyTimer = null;

	private bool isLassoActive = false;

	private List<Point> lassoPoints = new List<Point>();

	private DispatcherTimer? _pathfinderFollowTimer;

	private bool _pathfinderFollowing = false;

	internal Grid RootGrid;

	internal ColumnDefinition MesenSplitterCol;

	internal ColumnDefinition MesenPanelCol;

	internal Grid TileboardPanel;

	internal Border TilesLabelBorder;

	internal TextBlock TilesLabel;

	internal ToggleButton TileEyeButton;

	internal Slider TileSizeSlider;

	internal TextBlock TileIdIndicator;

	internal Image TileSelectedPreviewImage;

	internal System.Windows.Controls.ListBox TilesPanel;

	internal Border SpritesLabelBorder;

	internal TextBlock SpritesLabel;

	internal ToggleButton SpriteEyeButton;

	internal Slider SpriteSizeSlider;

	internal TextBlock SpriteIdIndicator;

	internal Image SpriteSelectedPreviewImage;

	internal System.Windows.Controls.ListBox SpritesPanel;

	internal DockPanel MainEditorPanel;

	internal MenuItem MenuFileNew;

	internal MenuItem MenuFileSave;

	internal MenuItem MenuFileSaveAs;

	internal MenuItem MenuFileLoad;

	internal MenuItem MenuFileRecent;

	internal MenuItem MenuFileSavePF;

	internal MenuItem MenuFileLoadPF;

	internal MenuItem MenuFileClose;

	internal MenuItem MenuFileExit;

	internal MenuItem MenuToolPlace;

	internal MenuItem MenuToolMove;

	internal MenuItem MenuToolErase;

	internal MenuItem MenuToolFill;

	internal MenuItem MenuToolSelect;

	internal MenuItem MenuToolLasso;

	internal MenuItem MenuToolEllipse;

	internal MenuItem Menu_Manipulate_Rotate_Tools;

	internal MenuItem Menu_Manipulate_RotateCCW_Tools;

	internal MenuItem Menu_Manipulate_Resize_Tools;

	internal MenuItem Menu_Manipulate_FlipH_Tools;

	internal MenuItem Menu_Manipulate_FlipV_Tools;

	internal MenuItem MenuToolSelectAllSame;

	internal MenuItem MenuToolReplaceSelected;

	internal MenuItem MenuToolWand;

	internal MenuItem MenuToolStructure;

	internal MenuItem MenuToolOffsetMap;

	internal MenuItem MenuEditCut;

	internal MenuItem MenuEditCopy;

	internal MenuItem MenuEditPaste;

	internal MenuItem MenuToolUndo;

	internal MenuItem MenuToolRedo;

	internal MenuItem MenuToolStartPos;

	internal MenuItem MenuOpenSimulator;

	internal MenuItem MenuConfigureFamidashRom;

	internal MenuItem MenuRunFamidashMesen;

	internal MenuItem MenuCaptureRamMesen;

	internal MenuItem MenuBuildAndTest;

	internal MenuItem MenuOverlayAndFollow;

	internal MenuItem MenuCamFollow;

	internal MenuItem MenuOptionLegacyTriggers;

	internal MenuItem MenuOptionSuppressCollisionMessages;

	internal MenuItem MenuOptionSwapMouseWheel;

	internal MenuItem MenuOptionSwapPinch;

	internal MenuItem MenuOptionHideBackground;

	internal MenuItem MenuOptionHideGround;

	internal MenuItem MenuOptionFastZoom;

	internal MenuItem MenuColorEditorBackground;

	internal MenuItem MenuTileboardLeft;

	internal MenuItem MenuTileboardRight;

	internal MenuItem MenuTileboardTop;

	internal MenuItem MenuTileboardBottom;

	internal MenuItem MenuTileboardHidden;

	internal MenuItem MenuColorPlayerTint;

	internal MenuItem MenuConfigureFamiStudio;

	internal MenuItem MenuScanFamiStudioTracks;

	internal MenuItem MenuColorBackgroundTint;

	internal MenuItem MenuColorGroundTint;

	internal MenuItem MenuColorTileTint;

	internal MenuItem MenuOptionShowAccurateTileset;

	internal MenuItem MenuOptionLockSprites;

	internal MenuItem MenuOptionHideColorTriggers;

	internal MenuItem MenuOptionHideInvisibleSprites;

	internal MenuItem MenuSimulatorSize1x;

	internal MenuItem MenuSimulatorSize2x;

	internal MenuItem MenuSimulatorSize3x;

	internal MenuItem MenuSimulatorSize4x;

	internal MenuItem MenuOptionShowSpriteHitboxes;

	internal MenuItem MenuOptionTileHitboxes;

	internal MenuItem MenuOptionNoDeath;

	internal MenuItem MenuOptionCamMode;

	internal MenuItem MenuOptionHideTriggerSprites;

	internal MenuItem MenuOptionOpenSimulatorPaused;

	internal MenuItem MenuOptionClearPlayerPath;

	internal System.Windows.Controls.TabControl FileTabControl;

	internal ToggleButton PlaceTool;

	internal ToggleButton MoveTool;

	internal ToggleButton EraseTool;

	internal ToggleButton FillTool;

	internal System.Windows.Controls.Button FillToolMore;

	internal MenuItem Menu_Fill_Normal;

	internal MenuItem Menu_Fill_ReplaceSelected;

	internal ToggleButton SelectTool;

	internal System.Windows.Controls.Button SelectToolMore;

	internal MenuItem Menu_Select_Normal;

	internal MenuItem Menu_Select_AllSame;

	internal MenuItem Menu_Select_Lasso;

	internal MenuItem Menu_Select_Ellipse;

	internal ToggleButton MagicWandTool;

	internal ToggleButton StructureTool;

	internal Image StructureToolIcon;

	internal ToggleButton StartPosTool;

	internal Popup StructurePopup;

	internal System.Windows.Controls.Button StructureSetAButton;

	internal Image StructureSetAIcon;

	internal System.Windows.Controls.Button StructureSetBButton;

	internal Image StructureSetBIcon;

	internal System.Windows.Controls.Button StructureSetCButton;

	internal Image StructureSetCIcon;

	internal System.Windows.Controls.CheckBox PreviewModeCheckbox;

	internal System.Windows.Controls.ComboBox FamiTrackCombo;

	internal System.Windows.Controls.Button PlayFamiButton;

	internal System.Windows.Controls.Button StopFamiButton;

	internal ToggleButton DrawTileButton;

	internal ToggleButton DrawLineButton;

	internal ToggleButton DrawSquareButton;

	internal ToggleButton DrawCircleButton;

	internal ToggleButton DrawEllipseButton;

	internal ToggleButton DrawTriangleButton;

	internal ToggleButton DrawPolygonButton;

	internal System.Windows.Controls.CheckBox HollowCheckBox;

	internal Slider BrushThicknessSlider;

	internal System.Windows.Controls.Button ManipulateButton;

	internal MenuItem Menu_Manipulate_Rotate;

	internal MenuItem Menu_Manipulate_RotateCCW;

	internal MenuItem Menu_Manipulate_Resize;

	internal MenuItem Menu_Manipulate_FlipH;

	internal MenuItem Menu_Manipulate_FlipV;

	internal System.Windows.Controls.Button UndoButton;

	internal System.Windows.Controls.Button RedoButton;

	internal System.Windows.Controls.Button CutButton;

	internal System.Windows.Controls.Button CopyButton;

	internal System.Windows.Controls.Button PasteButton;

	internal System.Windows.Controls.Button SaveButton;

	internal System.Windows.Controls.Button LoadButton;

	internal System.Windows.Controls.Button SetOptionsButton;

	internal System.Windows.Controls.Button CalculatePathButton;

	internal System.Windows.Controls.Button StopPathfinderButton;

	internal System.Windows.Controls.ProgressBar PathfinderProgressBar;

	internal System.Windows.Controls.Button PathfinderJumpToButton;

	internal System.Windows.Controls.Button PathfinderReplayButton;

	internal System.Windows.Controls.TextBox WidthBox;

	internal System.Windows.Controls.TextBox HeightBox;

	internal System.Windows.Controls.Button ResizeButton;

	internal TextBlock StatusText;

	internal Slider GridDarknessSlider;

	internal Slider ZoomSlider;

	internal TextBlock ZoomLevelLabel;

	internal ScrollViewer MapScrollViewer;

	internal Grid MapContentRoot;

	internal Image BackgroundImage;

	internal Image ParallaxImage;

	internal Image GroundImage;

	internal Image TilesImage;

	internal Image PortalsImage;

	internal Image SpritesImage;

	internal Image GridImage;

	internal Canvas CanvasHost;

	internal Rectangle HoverRect;

	internal Border HoverBorder;

	internal Canvas SelectionOverlay;

	internal Canvas IncompatibleOverlay;

	internal Canvas SelectSameOverlay;

	internal Image GhostImage;

	internal Rectangle OffsetGhostTile;

	internal Canvas OffsetTooltipContainer;

	internal Canvas OffsetGhostContainer;

	internal GridSplitter MesenSplitter;

	internal Border MesenEmbeddedHost;

	private bool _contentLoaded;

	internal static string ReplayRootDir => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Famidash Editor", "Replays");

	internal string CurrentReplayDir => GetLevelReplayDir(currentFilePath);

	private string ScrollTempFile => System.IO.Path.Combine(CurrentReplayDir, "famidash_overlay_scroll.txt");

	internal string OverlayLuaPath => System.IO.Path.Combine(CurrentReplayDir, "famidash_overlay.lua");

	internal string OverlayLuaNoPathlinesPath => System.IO.Path.Combine(CurrentReplayDir, "famidash_overlay (nopathlines).lua");

	internal string ReplayTempFile => System.IO.Path.Combine(CurrentReplayDir, "famidash_replay.csv");

	internal string CurrentLevelTag => SanitizeLevelName(currentFilePath);

	private string MesenLogStamp => _mesenLogStamp ?? (_mesenLogStamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"));

	internal string MesenTraceFile => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"famidash_mesen_trace_{CurrentLevelTag}_{MesenLogStamp}.csv");

	internal string MesenOrbDebugFile => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"famidash_mesen_orb_debug_{CurrentLevelTag}_{MesenLogStamp}.log");

	internal string MesenPhysicsDebugFile => System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"famidash_mesen_physics_debug_{CurrentLevelTag}_{MesenLogStamp}.log");

	public int? LoadedStartingGameMode
	{
		get
		{
			return loadedStartingGameMode;
		}
		set
		{
			loadedStartingGameMode = value;
		}
	}

	public int? LoadedSpawnYPositionHi
	{
		get
		{
			return loadedSpawnYPositionHi;
		}
		set
		{
			loadedSpawnYPositionHi = value;
		}
	}

	public int? LoadedSpawnYPositionLow
	{
		get
		{
			return loadedSpawnYPositionLow;
		}
		set
		{
			loadedSpawnYPositionLow = value;
		}
	}

	public int? LoadedScrollYPositionHi
	{
		get
		{
			return loadedScrollYPositionHi;
		}
		set
		{
			loadedScrollYPositionHi = value;
		}
	}

	public int? LoadedScrollYPositionLow
	{
		get
		{
			return loadedScrollYPositionLow;
		}
		set
		{
			loadedScrollYPositionLow = value;
		}
	}

	public bool? LoadedForcePlatformer
	{
		get
		{
			return loadedForcePlatformer;
		}
		set
		{
			loadedForcePlatformer = value;
		}
	}

	public int LoadedStartingSpeedUiIndex
	{
		get
		{
			return loadedStartingSpeedUiIndex;
		}
		set
		{
			loadedStartingSpeedUiIndex = value;
		}
	}

	public int? LoadedStartingDifficulty
	{
		get
		{
			return loadedStartingDifficulty;
		}
		set
		{
			loadedStartingDifficulty = value;
		}
	}

	public int? LoadedStartingStars
	{
		get
		{
			return loadedStartingStars;
		}
		set
		{
			loadedStartingStars = value;
		}
	}

	public int LoadedSimulatorScale
	{
		get
		{
			return loadedSimulatorScale;
		}
		set
		{
			loadedSimulatorScale = value;
		}
	}

	public bool LoadedOpenSimulatorPaused
	{
		get
		{
			return loadedOpenSimulatorPaused;
		}
		set
		{
			loadedOpenSimulatorPaused = value;
		}
	}

	public int? LoadedStartingBackgroundColor
	{
		get
		{
			return loadedStartingBackgroundColor;
		}
		set
		{
			loadedStartingBackgroundColor = value;
		}
	}

	public int? LoadedStartingGroundColor
	{
		get
		{
			return loadedStartingGroundColor;
		}
		set
		{
			loadedStartingGroundColor = value;
		}
	}

	public string? LoadedStartingLowerText
	{
		get
		{
			return loadedStartingLowerText;
		}
		set
		{
			loadedStartingLowerText = value;
		}
	}

	public string? LoadedStartingUpperText
	{
		get
		{
			return loadedStartingUpperText;
		}
		set
		{
			loadedStartingUpperText = value;
		}
	}

	public int LoadedMaxFallSpeed
	{
		get
		{
			return loadedMaxFallSpeed;
		}
		set
		{
			loadedMaxFallSpeed = value;
		}
	}

	public bool LockSpritesToSet => lockSpritesToSet;

	public bool ShowAccurateTileset => showAccurateTileset;

	public bool NoParallaxBg => noParallaxBg;

	public int MapWidth => mapWidth;

	public int MapHeight => mapHeight;

	public int? StartPosMarkerX => startPosMarkerX;

	public int? StartPosMarkerY => startPosMarkerY;

	public List<bool>? PrecomputedPathfinderInputs => precomputedPathfinderInputs;

	public HashSet<int>? PrecomputedCollectedCoins => precomputedCollectedCoins;

	public HashSet<int>? PrecomputedSkippedPads => precomputedSkippedPads;

	public int EditorAnimationFrame => animationFrame;

	public bool EditorPreviewMode => previewMode;

	private void MenuBuildAndTest_Click(object? sender, RoutedEventArgs e)
	{
		BuildAndTestAsync(useReplay: false, skipConfirm: false);
	}

	internal Task BuildAndTestForReplayAsync()
	{
		return BuildAndTestAsync(useReplay: true, skipConfirm: true);
	}

	private async Task BuildAndTestAsync(bool useReplay, bool skipConfirm)
	{
		string tmxPath = GetCurrentTmxPath();
		if (string.IsNullOrEmpty(tmxPath) || !File.Exists(tmxPath))
		{
			System.Windows.MessageBox.Show(this, "No level is currently open.", "Build and Test", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		string levelName = System.IO.Path.GetFileNameWithoutExtension(tmxPath).ToLowerInvariant();
		string rawSong = "";
		try
		{
			object obj = FamiTrackCombo?.SelectedItem;
			if (obj is ComboBoxItem { Content: var content })
			{
				rawSong = content?.ToString() ?? "";
			}
			if (string.IsNullOrEmpty(rawSong) && FamiTrackCombo?.SelectedItem != null)
			{
				rawSong = FamiTrackCombo.SelectedItem.ToString() ?? "";
			}
		}
		catch
		{
		}
		if (string.IsNullOrEmpty(rawSong))
		{
			try
			{
				if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
				{
					rawSong = openFiles[currentFileIndex].SelectedSong ?? "";
				}
			}
			catch
			{
			}
		}
		if (string.IsNullOrEmpty(rawSong))
		{
			System.Windows.MessageBox.Show(this, "No music track is selected for this level.\nPlease select a song before building.", "Build and Test", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		string jsonSegment;
		try
		{
			jsonSegment = BuildLevelJsonSegment(levelName, rawSong);
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show(this, "Failed to generate level JSON:\n" + ex.Message, "Build and Test", MessageBoxButton.OK, MessageBoxImage.Hand);
			return;
		}
		if (!skipConfirm)
		{
			MessageBoxResult confirm = System.Windows.MessageBox.Show(this, $"Build and test level: {levelName}\nSong: {rawSong}\n\nThis will:\n" + "  A) Copy the TMX into sim-famidash\n  B) Update the level metadata\n  C) Update the music metadata\n  D) Run 1BigSimExport.bat\n  E) Open the ROM in Mesen\n\nContinue?", "Build and Test", MessageBoxButton.YesNo, MessageBoxImage.Question);
			if (confirm != MessageBoxResult.Yes)
			{
				return;
			}
		}
		if (StatusText != null)
		{
			StatusText.Text = "Build and Test: preparing...";
		}
		try
		{
			Directory.CreateDirectory("C:\\Editor Test\\sim-famidash\\LEVELS\\level data\\lvlset_D");
			string destTmx = System.IO.Path.Combine("C:\\Editor Test\\sim-famidash\\LEVELS\\level data\\lvlset_D", System.IO.Path.GetFileName(tmxPath));
			File.Copy(tmxPath, destTmx, overwrite: true);
			UpdateLevelMetadata(jsonSegment);
			UpdateMusicMetadata(rawSong);
			if (StatusText != null)
			{
				StatusText.Text = "Build and Test: building ROM...";
			}
			if (!(await RunBuildBatAsync()))
			{
				System.Windows.MessageBox.Show(this, "1BigSimExport.bat failed or timed out. Check the console output.", "Build and Test", MessageBoxButton.OK, MessageBoxImage.Hand);
				if (StatusText != null)
				{
					StatusText.Text = "Build and Test: build failed.";
				}
			}
			else if (!File.Exists("C:\\Editor Test\\sim-famidash\\Famidash.nes"))
			{
				System.Windows.MessageBox.Show(this, "Build completed but ROM not found at:\nC:\\Editor Test\\sim-famidash\\Famidash.nes", "Build and Test", MessageBoxButton.OK, MessageBoxImage.Hand);
				if (StatusText != null)
				{
					StatusText.Text = "Build and Test: ROM not found.";
				}
			}
			else
			{
				OpenRomInMesen("C:\\Editor Test\\sim-famidash\\Famidash.nes", useReplay);
				if (StatusText != null)
				{
					StatusText.Text = (useReplay ? ("Replay: launched " + levelName + " in Mesen with injected pathfinder inputs.") : ("Build and Test: launched " + levelName + " in Mesen."));
				}
			}
		}
		catch (Exception ex2)
		{
			System.Windows.MessageBox.Show(this, "Build and Test failed:\n" + ex2.Message, "Build and Test", MessageBoxButton.OK, MessageBoxImage.Hand);
			if (StatusText != null)
			{
				StatusText.Text = "Build and Test: error.";
			}
		}
	}

	private string BuildLevelJsonSegment(string levelName, string rawSong)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string value = "";
		if (!string.IsNullOrEmpty(rawSong))
		{
			string input = rawSong.ToLowerInvariant();
			input = Regex.Replace(input, "\\s+", "_");
			input = Regex.Replace(input, "[^a-z0-9_]", "");
			value = "song_" + input;
		}
		string value2 = GetString("LoadedStartingUpperText").ToUpperInvariant();
		string value3 = GetString("LoadedStartingLowerText").ToUpperInvariant();
		string value4 = GetString("loadedDecoSet", "DECO1");
		string text = GetString("loadedBlockSet", "BLOCKSA");
		string text2 = GetString("loadedSpikeSet", "SPIKESA");
		string text3 = (text.StartsWith("BLOCKS", StringComparison.OrdinalIgnoreCase) ? text.Substring(6) : text);
		string text4 = (text2.StartsWith("SPIKES", StringComparison.OrdinalIgnoreCase) ? text2.Substring(6) : text2);
		text3 = text3.ToUpperInvariant();
		text4 = text4.ToUpperInvariant();
		string value5 = "AUTO";
		int? num = GetNullableInt("LoadedStartingDifficulty");
		if (num.HasValue)
		{
			int num2 = Math.Clamp(num.Value, 0, 13);
			string[] array = new string[7] { "EASY", "NORMAL", "HARD", "HARDER", "INSANE", "DEMON", "AUTO" };
			string[] array2 = new string[7] { "EASYDEMON", "MEDIUMDEMON", "HARDDEMON", "INSANEDEMON", "EXTREMEDEMON", "IMPOSSIBLEDEMON", "GRANDPADEMON" };
			value5 = ((num2 < 7) ? array[num2] : array2[num2 - 7]);
		}
		int value6 = GetNullableInt("LoadedStartingStars") ?? 3;
		int valueOrDefault = GetNullableInt("LoadedStartingGameMode").GetValueOrDefault();
		int value7 = 0;
		try
		{
			int num3 = GetNullableInt("LoadedStartingSpeedUiIndex") ?? 1;
			value7 = num3 switch
			{
				1 => 0, 
				0 => 1, 
				_ => num3, 
			};
		}
		catch
		{
		}
		int num4 = GetNullableInt("LoadedMaxFallSpeed") ?? 6;
		if (num4 == 0)
		{
			num4 = 6;
		}
		int? num5 = GetNullableInt("LoadedStartingBackgroundColor");
		int? num6 = GetNullableInt("LoadedStartingGroundColor");
		int? num7 = GetNullableInt("LoadedSpawnYPositionHi");
		int? num8 = GetNullableInt("LoadedSpawnYPositionLow");
		int? num9 = GetNullableInt("LoadedScrollYPositionHi");
		int? num10 = GetNullableInt("LoadedScrollYPositionLow");
		bool flag = GetBool("NoParallaxBg");
		bool flag2 = false;
		try
		{
			if (GetType().GetProperty("LoadedForcePlatformer", BindingFlags.Instance | BindingFlags.Public)?.GetValue(this) is bool flag3)
			{
				flag2 = flag3;
			}
		}
		catch
		{
		}
		Dictionary<(int, int), List<(int, int)>> dictionary = null;
		try
		{
			Dictionary<int, (int, int)> spriteOffsets = GetSpriteOffsets();
			if (spriteOffsets != null && spriteOffsets.Count > 0)
			{
				dictionary = new Dictionary<(int, int), List<(int, int)>>();
				foreach (KeyValuePair<int, (int, int)> item3 in spriteOffsets)
				{
					int key = item3.Key;
					int item = key % MapWidth;
					int item2 = key / MapWidth;
					(int, int) key2 = (item3.Value.Item1, item3.Value.Item2);
					if (!dictionary.ContainsKey(key2))
					{
						dictionary[key2] = new List<(int, int)>();
					}
					dictionary[key2].Add((item, item2));
				}
			}
		}
		catch
		{
		}
		stringBuilder.AppendLine("\t\t{");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
		handler.AppendLiteral("\t\t\tlevel: \"");
		handler.AppendFormatted(levelName);
		handler.AppendLiteral("\",");
		stringBuilder3.AppendLine(ref handler);
		if (!string.IsNullOrEmpty(value2))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tupperText: \"");
			handler.AppendFormatted(value2);
			handler.AppendLiteral("\",");
			stringBuilder4.AppendLine(ref handler);
		}
		if (!string.IsNullOrEmpty(value3))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tlowerText: \"");
			handler.AppendFormatted(value3);
			handler.AppendLiteral("\",");
			stringBuilder5.AppendLine(ref handler);
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("\t\t\tdecoType: \"");
		handler.AppendFormatted(value4);
		handler.AppendLiteral("\",");
		stringBuilder6.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder7 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("\t\t\tspikeSet: \"");
		handler.AppendFormatted(text4);
		handler.AppendLiteral("\",");
		stringBuilder7.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder8 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("\t\t\tblockSet: \"");
		handler.AppendFormatted(text3);
		handler.AppendLiteral("\",");
		stringBuilder8.AppendLine(ref handler);
		stringBuilder.AppendLine("\t\t\tsawSet: \"A\",");
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
		handler.AppendLiteral("\t\t\tdifficulty: \"");
		handler.AppendFormatted(value5);
		handler.AppendLiteral("\",");
		stringBuilder9.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
		handler.AppendLiteral("\t\t\tstars: ");
		handler.AppendFormatted(value6);
		handler.AppendLiteral(",");
		stringBuilder10.AppendLine(ref handler);
		if (!string.IsNullOrEmpty(value))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder11 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tsongID: \"");
			handler.AppendFormatted(value);
			handler.AppendLiteral("\",");
			stringBuilder11.AppendLine(ref handler);
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder12 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder2);
		handler.AppendLiteral("\t\t\tstartingGameMode: ");
		handler.AppendFormatted(valueOrDefault);
		handler.AppendLiteral(",");
		stringBuilder12.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder13 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
		handler.AppendLiteral("\t\t\tstartingSpeed: ");
		handler.AppendFormatted(value7);
		handler.AppendLiteral(",");
		stringBuilder13.AppendLine(ref handler);
		if (num4 != 6)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder14 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tmaxFallSpeed: 0x");
			handler.AppendFormatted(num4, "X2");
			handler.AppendLiteral(",");
			stringBuilder14.AppendLine(ref handler);
		}
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder15 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(31, 1, stringBuilder2);
		handler.AppendLiteral("\t\t\tstartingBackgroundColor: 0x");
		handler.AppendFormatted(num5 ?? 18, "X2");
		handler.AppendLiteral(",");
		stringBuilder15.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder16 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(27, 1, stringBuilder2);
		handler.AppendLiteral("\t\t\tstartingGroundColor: 0x");
		handler.AppendFormatted(num6 ?? 2, "X2");
		handler.AppendLiteral(",");
		stringBuilder16.AppendLine(ref handler);
		if (num7.HasValue)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder17 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tspawnYPositionHi: 0x");
			handler.AppendFormatted(num7.Value, "X2");
			handler.AppendLiteral(",");
			stringBuilder17.AppendLine(ref handler);
		}
		if (num8.HasValue)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder18 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(25, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tspawnYPositionLow: 0x");
			handler.AppendFormatted(num8.Value, "X2");
			handler.AppendLiteral(",");
			stringBuilder18.AppendLine(ref handler);
		}
		if (num9.HasValue)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder19 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(25, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tscrollYPositionHi: 0x");
			handler.AppendFormatted(num9.Value, "X2");
			handler.AppendLiteral(",");
			stringBuilder19.AppendLine(ref handler);
		}
		if (num10.HasValue)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder20 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(26, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tscrollYPositionLow: 0x");
			handler.AppendFormatted(num10.Value, "X2");
			handler.AppendLiteral(",");
			stringBuilder20.AppendLine(ref handler);
		}
		if (flag2)
		{
			stringBuilder.AppendLine("\t\t\tforcePlatformer: true,");
		}
		if (flag)
		{
			stringBuilder.AppendLine("\t\t\tparallaxDisable: true,");
		}
		if (dictionary != null && dictionary.Count > 0)
		{
			stringBuilder.AppendLine("\t\t\tobjectOffsets: [");
			bool flag4 = true;
			foreach (KeyValuePair<(int, int), List<(int, int)>> item4 in dictionary)
			{
				if (!flag4)
				{
					stringBuilder.AppendLine(",");
				}
				flag4 = false;
				stringBuilder.AppendLine("\t\t\t\t{");
				List<(int, int)> value8 = item4.Value;
				if (value8.Count == 1)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder21 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(23, 2, stringBuilder2);
					handler.AppendLiteral("\t\t\t\t\tcoordinates: [");
					handler.AppendFormatted(value8[0].Item1);
					handler.AppendLiteral(", ");
					handler.AppendFormatted(value8[0].Item2);
					handler.AppendLiteral("],");
					stringBuilder21.AppendLine(ref handler);
				}
				else
				{
					stringBuilder.AppendLine("\t\t\t\t\tcoordinates: [");
					for (int i = 0; i < value8.Count; i++)
					{
						stringBuilder2 = stringBuilder;
						StringBuilder stringBuilder22 = stringBuilder2;
						handler = new StringBuilder.AppendInterpolatedStringHandler(10, 3, stringBuilder2);
						handler.AppendLiteral("\t\t\t\t\t\t[");
						handler.AppendFormatted(value8[i].Item1);
						handler.AppendLiteral(", ");
						handler.AppendFormatted(value8[i].Item2);
						handler.AppendLiteral("]");
						handler.AppendFormatted((i < value8.Count - 1) ? "," : "");
						stringBuilder22.AppendLine(ref handler);
					}
					stringBuilder.AppendLine("\t\t\t\t\t],");
				}
				if (item4.Key.Item2 != 0)
				{
					string value9 = ((item4.Key.Item2 >= 0) ? $"+{item4.Key.Item2}" : item4.Key.Item2.ToString());
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder23 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(14, 2, stringBuilder2);
					handler.AppendLiteral("\t\t\t\t\toffsetY: ");
					handler.AppendFormatted(value9);
					handler.AppendFormatted((item4.Key.Item1 != 0) ? "," : "");
					stringBuilder23.AppendLine(ref handler);
				}
				if (item4.Key.Item1 != 0)
				{
					string value10 = ((item4.Key.Item1 >= 0) ? $"+{item4.Key.Item1}" : item4.Key.Item1.ToString());
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder24 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
					handler.AppendLiteral("\t\t\t\t\toffsetX: ");
					handler.AppendFormatted(value10);
					stringBuilder24.AppendLine(ref handler);
				}
				stringBuilder.Append("\t\t\t\t}");
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("\t\t\t],");
		}
		stringBuilder.AppendLine("\t\t}");
		return stringBuilder.ToString();
		bool GetBool(string name)
		{
			object obj4 = GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(this);
			if (obj4 != null)
			{
				try
				{
					return Convert.ToBoolean(obj4);
				}
				catch
				{
					return false;
				}
			}
			return false;
		}
		int? GetNullableInt(string name)
		{
			object obj4 = GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(this);
			if (obj4 != null)
			{
				try
				{
					return Convert.ToInt32(obj4);
				}
				catch
				{
					return null;
				}
			}
			return null;
		}
		string GetString(string name, string def = "")
		{
			FieldInfo field = GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
			if (field != null)
			{
				return (field.GetValue(this) as string) ?? def;
			}
			return (GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(this) as string) ?? def;
		}
	}

	private static void UpdateLevelMetadata(string jsonSegment)
	{
		string text = File.ReadAllText("C:\\Editor Test\\sim-famidash\\LEVELS\\metadata\\lvlset_D_metadata.json5");
		int num = text.IndexOf("*/", StringComparison.Ordinal);
		if (num < 0)
		{
			throw new InvalidOperationException("Could not find template comment block in level metadata.");
		}
		num += 2;
		int num2 = text.IndexOf("globalObjectOffsets", num, StringComparison.Ordinal);
		if (num2 < 0)
		{
			throw new InvalidOperationException("Could not find globalObjectOffsets in level metadata.");
		}
		int num3 = text.LastIndexOf("],", num2, StringComparison.Ordinal);
		if (num3 < 0)
		{
			throw new InvalidOperationException("Could not find community_levels closing bracket in level metadata.");
		}
		while (num3 > num && text[num3 - 1] != '\n')
		{
			num3--;
		}
		string contents = text.Substring(0, num) + "\n\n" + jsonSegment + "\n" + text.Substring(num3);
		File.WriteAllText("C:\\Editor Test\\sim-famidash\\LEVELS\\metadata\\lvlset_D_metadata.json5", contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}

	private static void UpdateMusicMetadata(string rawSong)
	{
		string text = File.ReadAllText("C:\\Editor Test\\sim-famidash\\MUSIC\\metadata\\lvlset_D_metadata.json5");
		Match match = Regex.Match(text, "lowerText\\s*:\\s*\"TEXT\"", RegexOptions.IgnoreCase);
		if (match.Success)
		{
			string input = text.Substring(0, match.Index);
			MatchCollection matchCollection = Regex.Matches(input, "fmsSongName\\s*:\\s*\"[^\"]*\"");
			if (matchCollection.Count > 0)
			{
				Match match2 = matchCollection[matchCollection.Count - 1];
				string contents = text.Substring(0, match2.Index) + "fmsSongName: \"" + EscapeJson5(rawSong) + "\"" + text.Substring(match2.Index + match2.Length);
				File.WriteAllText("C:\\Editor Test\\sim-famidash\\MUSIC\\metadata\\lvlset_D_metadata.json5", contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				return;
			}
		}
		string contents2 = Regex.Replace(text, "fmsSongName:\\s*\"Jumper\"", "fmsSongName: \"" + EscapeJson5(rawSong) + "\"", RegexOptions.IgnoreCase);
		File.WriteAllText("C:\\Editor Test\\sim-famidash\\MUSIC\\metadata\\lvlset_D_metadata.json5", contents2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
	}

	private static string EscapeJson5(string s)
	{
		return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
	}

	private static Task<bool> RunBuildBatAsync()
	{
		return Task.Run(delegate
		{
			try
			{
				ProcessStartInfo startInfo = new ProcessStartInfo("cmd.exe", "/c \"C:\\Editor Test\\sim-famidash\\1BigSimExport.bat\"")
				{
					WorkingDirectory = "C:\\Editor Test\\sim-famidash",
					UseShellExecute = true,
					CreateNoWindow = false
				};
				using Process process = Process.Start(startInfo);
				if (process == null)
				{
					return false;
				}
				return process.WaitForExit(300000) && process.ExitCode == 0;
			}
			catch
			{
				return false;
			}
		});
	}

	private void OpenRomInMesen(string romPath)
	{
		OpenRomInMesen(romPath, useReplay: false);
	}

	private void OpenRomInMesen(string romPath, bool useReplay)
	{
		string text = OverlayLuaPath;
		try
		{
			File.WriteAllText(text, BuildOverlayLuaScript(useReplay, drawPathlines: true));
			File.WriteAllText(OverlayLuaNoPathlinesPath, BuildOverlayLuaScript(useReplay, drawPathlines: false));
		}
		catch
		{
			text = "";
		}
		if (Option_EmbeddedMesen && OpenRomInEmbeddedMesen(romPath, text))
		{
			return;
		}
		string text2 = System.IO.Path.Combine(AppContext.BaseDirectory, "mesen", "Mesen.exe");
		if (!File.Exists(text2))
		{
			System.Windows.MessageBox.Show(this, "Bundled Mesen not found at:\n" + text2 + "\n\nRebuild the editor to copy Mesen to the output folder.", "Build and Test", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		string directoryName = System.IO.Path.GetDirectoryName(text2);
		string text3 = "\"" + romPath + "\"";
		if (!string.IsNullOrEmpty(text) && File.Exists(text))
		{
			text3 = text3 + " \"" + text + "\"";
		}
		ProcessStartInfo startInfo = new ProcessStartInfo(text2, text3)
		{
			UseShellExecute = false,
			CreateNoWindow = false,
			WorkingDirectory = directoryName
		};
		Process process = Process.Start(startInfo);
		if (process != null)
		{
			StartMesenOverlay(process);
		}
	}

	private void MenuConfigureMesen_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
			{
				Title = "Select Mesen2 executable",
				Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
				CheckFileExists = true
			};
			try
			{
				if (!string.IsNullOrWhiteSpace(mesenPath))
				{
					openFileDialog.InitialDirectory = System.IO.Path.GetDirectoryName(mesenPath);
				}
				else
				{
					string text = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mesen2");
					if (Directory.Exists(text))
					{
						openFileDialog.InitialDirectory = text;
					}
				}
			}
			catch
			{
			}
			if (openFileDialog.ShowDialog(this) == true)
			{
				mesenPath = openFileDialog.FileName;
				SaveEditorSettings();
				if (StatusText != null)
				{
					StatusText.Text = "Mesen configured: " + System.IO.Path.GetFileName(mesenPath);
				}
			}
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show(this, "Failed to configure Mesen: " + ex.Message, "Mesen", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void MenuConfigureFamidashRom_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
			{
				Title = "Select Famidash NES ROM",
				Filter = "NES ROM (*.nes)|*.nes|All files (*.*)|*.*",
				CheckFileExists = true
			};
			try
			{
				if (!string.IsNullOrWhiteSpace(famidashRomPath))
				{
					openFileDialog.InitialDirectory = System.IO.Path.GetDirectoryName(famidashRomPath);
				}
			}
			catch
			{
			}
			if (openFileDialog.ShowDialog(this) == true)
			{
				famidashRomPath = openFileDialog.FileName;
				SaveEditorSettings();
				if (StatusText != null)
				{
					StatusText.Text = "Famidash ROM configured: " + System.IO.Path.GetFileName(famidashRomPath);
				}
			}
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show(this, "Failed to configure ROM: " + ex.Message, "Mesen", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void MenuRunFamidashMesen_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			string text = System.IO.Path.Combine(AppContext.BaseDirectory, "mesen", "Mesen.exe");
			if (!File.Exists(text))
			{
				if (string.IsNullOrWhiteSpace(mesenPath) || !File.Exists(mesenPath))
				{
					throw new InvalidOperationException("Bundled Mesen not found at " + text + ". Rebuild the editor to bundle Mesen.");
				}
				text = mesenPath;
			}
			if (string.IsNullOrWhiteSpace(famidashRomPath) || !File.Exists(famidashRomPath))
			{
				throw new InvalidOperationException("Famidash ROM is not configured. Use Tools → Mesen (NES) → Configure Famidash ROM.");
			}
			string directoryName = System.IO.Path.GetDirectoryName(text);
			string text2 = OverlayLuaPath;
			try
			{
				File.WriteAllText(text2, BuildOverlayLuaScript(includeReplay: true, drawPathlines: true));
				File.WriteAllText(OverlayLuaNoPathlinesPath, BuildOverlayLuaScript(includeReplay: true, drawPathlines: false));
			}
			catch
			{
				text2 = "";
			}
			string text3 = "\"" + famidashRomPath + "\"";
			if (!string.IsNullOrEmpty(text2) && File.Exists(text2))
			{
				text3 = text3 + " \"" + text2 + "\"";
			}
			ProcessStartInfo startInfo = new ProcessStartInfo(text, text3)
			{
				UseShellExecute = false,
				CreateNoWindow = false,
				WorkingDirectory = directoryName
			};
			Process process = Process.Start(startInfo);
			if (process != null)
			{
				StartMesenOverlay(process);
			}
			if (StatusText != null)
			{
				StatusText.Text = "Launched Famidash in Mesen.";
			}
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show(this, ex.Message, "Mesen", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async void MenuCaptureRamMesen_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			EnsureMesenConfigured();
			List<ushort> addrs = ParseAddressList(mesenRamAddresses);
			if (addrs.Count == 0)
			{
				throw new InvalidOperationException("No valid RAM addresses configured. Set mesenRamAddresses in editor-settings.json (hex list, e.g. 00A0,00A1,00F0). ");
			}
			string outputPath = GetDefaultRamCapturePath();
			Microsoft.Win32.SaveFileDialog saveDlg = new Microsoft.Win32.SaveFileDialog
			{
				Title = "Save RAM capture output",
				Filter = "JSON Lines (*.jsonl)|*.jsonl|All files (*.*)|*.*",
				FileName = System.IO.Path.GetFileName(outputPath),
				InitialDirectory = System.IO.Path.GetDirectoryName(outputPath)
			};
			if (saveDlg.ShowDialog(this) == true)
			{
				outputPath = saveDlg.FileName;
				MesenRamCaptureOptions options = new MesenRamCaptureOptions
				{
					MesenExePath = mesenPath,
					RomPath = famidashRomPath,
					OutputPath = outputPath,
					TimeoutSeconds = Math.Max(10, mesenCaptureTimeoutSeconds),
					MaxFrames = Math.Max(1, mesenCaptureFrames),
					SampleEveryNFrames = Math.Max(1, mesenCaptureEveryNFrames),
					Addresses = addrs
				};
				if (StatusText != null)
				{
					StatusText.Text = "Capturing RAM via Mesen test runner...";
				}
				MesenRamCaptureResult result = await Task.Run(() => MesenNesIntegration.CaptureRam(options));
				string msg = "RAM capture complete.\n\nSamples: " + result.Samples.ToString(CultureInfo.InvariantCulture) + "\nOutput: " + outputPath + "\nExitCode: " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + "\nStopCode: " + result.StopCode.ToString(CultureInfo.InvariantCulture);
				if (!string.IsNullOrWhiteSpace(result.StdErr))
				{
					msg = msg + "\n\nStderr:\n" + result.StdErr;
				}
				System.Windows.MessageBox.Show(this, msg, "Mesen RAM Capture", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				if (StatusText != null)
				{
					StatusText.Text = "RAM capture written to: " + System.IO.Path.GetFileName(outputPath);
				}
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			System.Windows.MessageBox.Show(this, "RAM capture failed: " + ex2.Message, "Mesen RAM Capture", MessageBoxButton.OK, MessageBoxImage.Hand);
			if (StatusText != null)
			{
				StatusText.Text = "RAM capture failed.";
			}
		}
	}

	private void EnsureMesenConfigured()
	{
		string text = System.IO.Path.Combine(AppContext.BaseDirectory, "mesen", "Mesen.exe");
		if (!File.Exists(text) && (string.IsNullOrWhiteSpace(mesenPath) || !File.Exists(mesenPath)))
		{
			throw new InvalidOperationException("Mesen not found. Expected bundled copy at:\n" + text + "\nRebuild the editor to bundle Mesen, or configure a path manually.");
		}
		if (string.IsNullOrWhiteSpace(famidashRomPath) || !File.Exists(famidashRomPath))
		{
			throw new InvalidOperationException("Famidash ROM is not configured. Use Tools -> Mesen (NES) -> Configure Famidash ROM.");
		}
	}

	private List<ushort> ParseAddressList(string? csv)
	{
		List<ushort> list = new List<ushort>();
		if (string.IsNullOrWhiteSpace(csv))
		{
			return list;
		}
		string[] array = csv.Split(new char[6] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		string[] array2 = array;
		foreach (string text in array2)
		{
			string text2 = text.Trim();
			if (text2.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			{
				text2 = text2.Substring(2);
			}
			if (ushort.TryParse(text2, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result) && !list.Contains(result))
			{
				list.Add(result);
			}
		}
		return list;
	}

	private string GetDefaultRamCapturePath()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		string text = System.IO.Path.Combine(folderPath, "Famidash Editor", "mesen-ram-captures");
		Directory.CreateDirectory(text);
		string text2 = "famidash";
		try
		{
			if (!string.IsNullOrWhiteSpace(currentFilePath))
			{
				text2 = System.IO.Path.GetFileNameWithoutExtension(currentFilePath) ?? text2;
			}
		}
		catch
		{
		}
		string text3 = string.Concat(text2.Where((char ch) => !System.IO.Path.GetInvalidFileNameChars().Contains(ch)));
		if (string.IsNullOrWhiteSpace(text3))
		{
			text3 = "famidash";
		}
		string text4 = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
		return System.IO.Path.Combine(text, text3 + "_ram_" + text4 + ".jsonl");
	}

	internal bool OpenRomInEmbeddedMesen(string romPath, string luaPath)
	{
		try
		{
			EnsureEmbeddedPanelVisible();
			if (_mesenEmbeddedHwndHost == null || _mesenEmbeddedHwndHost.Hwnd == IntPtr.Zero)
			{
				System.Windows.MessageBox.Show(this, "Embedded Mesen panel failed to create its host HWND.", "Embedded Mesen", MessageBoxButton.OK, MessageBoxImage.Hand);
				return false;
			}
			if (!MesenEmbeddedController.Initialized)
			{
				IntPtr handle = new WindowInteropHelper(this).Handle;
				string bundledMesenDir = MesenInterop.BundledMesenDir;
				MesenEmbeddedController.Initialize(handle, _mesenEmbeddedHwndHost.Hwnd, bundledMesenDir, noInput: true);
			}
			if (!MesenEmbeddedController.LoadRom(romPath))
			{
				System.Windows.MessageBox.Show(this, "Embedded Mesen: LoadRom failed for " + romPath, "Embedded Mesen", MessageBoxButton.OK, MessageBoxImage.Hand);
				return false;
			}
			UpdateEmbeddedMesenRendererSize();
			if (!string.IsNullOrEmpty(luaPath) && File.Exists(luaPath))
			{
				MesenEmbeddedController.LoadOverlayLua(luaPath);
			}
			return true;
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show(this, "Embedded Mesen launch failed:\n" + ex.Message, "Embedded Mesen", MessageBoxButton.OK, MessageBoxImage.Hand);
			return false;
		}
	}

	internal void CloseEmbeddedMesen()
	{
		try
		{
			MesenEmbeddedController.UnloadOverlayLua();
		}
		catch
		{
		}
		try
		{
			MesenEmbeddedController.Stop();
		}
		catch
		{
		}
		HideEmbeddedPanel();
	}

	private void EnsureEmbeddedPanelVisible()
	{
		if (MesenEmbeddedHost == null)
		{
			return;
		}
		if (_mesenEmbeddedHwndHost == null)
		{
			_mesenEmbeddedHwndHost = new MesenHwndHost();
			MesenEmbeddedHost.Child = _mesenEmbeddedHwndHost;
			MesenEmbeddedHost.SizeChanged += MesenEmbeddedHost_SizeChanged;
		}
		if (!_mesenEmbeddedShown)
		{
			if (MesenSplitterCol != null)
			{
				MesenSplitterCol.Width = new GridLength(5.0);
			}
			if (MesenPanelCol != null)
			{
				MesenPanelCol.Width = new GridLength(532.0);
			}
			if (MesenSplitter != null)
			{
				MesenSplitter.Visibility = Visibility.Visible;
			}
			MesenEmbeddedHost.Visibility = Visibility.Visible;
			_mesenEmbeddedShown = true;
		}
	}

	private void HideEmbeddedPanel()
	{
		if (MesenEmbeddedHost != null)
		{
			if (MesenPanelCol != null)
			{
				MesenPanelCol.Width = new GridLength(0.0);
			}
			if (MesenSplitterCol != null)
			{
				MesenSplitterCol.Width = new GridLength(0.0);
			}
			if (MesenSplitter != null)
			{
				MesenSplitter.Visibility = Visibility.Collapsed;
			}
			MesenEmbeddedHost.Visibility = Visibility.Collapsed;
			_mesenEmbeddedShown = false;
		}
	}

	private void MesenEmbeddedHost_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateEmbeddedMesenRendererSize();
	}

	private void UpdateEmbeddedMesenRendererSize()
	{
		if (!MesenEmbeddedController.Initialized || MesenEmbeddedHost == null)
		{
			return;
		}
		double actualWidth = MesenEmbeddedHost.ActualWidth;
		double actualHeight = MesenEmbeddedHost.ActualHeight;
		if (!(actualWidth < 1.0) && !(actualHeight < 1.0))
		{
			PresentationSource presentationSource = PresentationSource.FromVisual(this);
			double num = 1.0;
			double num2 = 1.0;
			if (presentationSource?.CompositionTarget != null)
			{
				num = presentationSource.CompositionTarget.TransformToDevice.M11;
				num2 = presentationSource.CompositionTarget.TransformToDevice.M22;
			}
			MesenEmbeddedController.SetRendererSize((int)Math.Round(actualWidth * num), (int)Math.Round(actualHeight * num2));
		}
	}

	private static string SanitizeLevelName(string? raw)
	{
		string text = ((!string.IsNullOrWhiteSpace(raw)) ? System.IO.Path.GetFileNameWithoutExtension(raw) : "untitled");
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "untitled";
		}
		char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			text = text.Replace(oldChar, '_');
		}
		return text;
	}

	internal static string GetLevelReplayDir(string? levelFilePath)
	{
		string text = System.IO.Path.Combine(ReplayRootDir, SanitizeLevelName(levelFilePath));
		try
		{
			Directory.CreateDirectory(text);
		}
		catch
		{
		}
		return text;
	}

	private void RefreshMesenLogStamp()
	{
		_mesenLogStamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
	}

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	internal void StartMesenOverlay(Process proc)
	{
		RefreshMesenLogStamp();
		string[] array = new string[3] { MesenTraceFile, MesenOrbDebugFile, MesenPhysicsDebugFile };
		foreach (string text in array)
		{
			try
			{
				if (!string.IsNullOrEmpty(text))
				{
					File.WriteAllText(text, string.Empty);
				}
			}
			catch
			{
			}
		}
		_mesenOverlayProcess = proc;
		_mesenHwnd = (IntPtr)0;
		_latestNesScrollX = 0;
		_latestNesScrollY = 0;
		_rawNesScrollX = int.MinValue;
		_lastMesenWinW = int.MinValue;
		_lastMesenWinH = int.MinValue;
		_fixedAnchorViewportX = double.NaN;
		_fixedAnchorViewportY = double.NaN;
		_initialCameraCanvasY = double.NaN;
		_smoothScrollX = double.NaN;
		_smoothScrollTargetX = double.NaN;
		_smoothScrollCoarseXPx = int.MinValue;
		_smoothScrollShiftXPx = int.MinValue;
		ClearSmoothEditorHorizontalShift();
		_overlayReadCts = new CancellationTokenSource();
		_overlayReadTask = OverlayReadLoop(_overlayReadCts.Token);
		if (!_overlayRenderHooked)
		{
			CompositionTarget.Rendering += OverlayRendering_Tick;
			_overlayRenderHooked = true;
		}
		Task.Run(delegate
		{
			try
			{
				proc.WaitForExit();
			}
			catch
			{
			}
			base.Dispatcher.BeginInvoke(new Action(StopMesenOverlay));
		});
	}

	internal void StopMesenOverlay()
	{
		string scrollTempFile = ScrollTempFile;
		try
		{
			_overlayReadCts?.Cancel();
		}
		catch
		{
		}
		_overlayReadCts = null;
		_overlayReadTask = null;
		if (_overlayRenderHooked)
		{
			try
			{
				CompositionTarget.Rendering -= OverlayRendering_Tick;
			}
			catch
			{
			}
			_overlayRenderHooked = false;
		}
		Process mesenOverlayProcess = _mesenOverlayProcess;
		_mesenHwnd = (IntPtr)0;
		_mesenOverlayProcess = null;
		_lastMesenScreenX = int.MinValue;
		_lastMesenScreenY = int.MinValue;
		_lastMesenWinW = int.MinValue;
		_lastMesenWinH = int.MinValue;
		_rawNesScrollX = int.MinValue;
		_lastScrollOffsetX = double.NaN;
		_lastScrollOffsetY = double.NaN;
		_fixedAnchorViewportX = double.NaN;
		_fixedAnchorViewportY = double.NaN;
		_initialCameraCanvasY = double.NaN;
		_smoothScrollX = double.NaN;
		_smoothScrollTargetX = double.NaN;
		_smoothScrollCoarseXPx = int.MinValue;
		_smoothScrollShiftXPx = int.MinValue;
		ClearSmoothEditorHorizontalShift();
		try
		{
			if (mesenOverlayProcess != null && !mesenOverlayProcess.HasExited)
			{
				mesenOverlayProcess.Kill(entireProcessTree: true);
			}
		}
		catch
		{
		}
		try
		{
			if (File.Exists(scrollTempFile))
			{
				File.Delete(scrollTempFile);
			}
		}
		catch
		{
		}
		try
		{
			LoadAndShowMesenTracePath(MesenTraceFile);
		}
		catch
		{
		}
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		try
		{
			StopMesenOverlay();
		}
		catch
		{
		}
		try
		{
			MesenEmbeddedController.Release();
		}
		catch
		{
		}
		base.OnClosing(e);
	}

	private async Task OverlayReadLoop(CancellationToken token)
	{
		while (!token.IsCancellationRequested && _mesenOverlayProcess != null && !_mesenOverlayProcess.HasExited)
		{
			try
			{
				if (File.Exists(ScrollTempFile))
				{
					string txt;
					using (FileStream fs = new FileStream(ScrollTempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
					{
						using StreamReader sr = new StreamReader(fs);
						txt = sr.ReadToEnd().Trim();
					}
					string[] parts = txt.Split(',');
					if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out var vx) && int.TryParse(parts[1].Trim(), out var vy))
					{
						int candidateX = Math.Max(0, vx);
						if (_rawNesScrollX == int.MinValue)
						{
							_rawNesScrollX = candidateX;
							_latestNesScrollX = candidateX;
						}
						else if (candidateX + 64 < _rawNesScrollX)
						{
							_rawNesScrollX = candidateX;
							_latestNesScrollX = candidateX;
						}
						else
						{
							_rawNesScrollX = Math.Max(_rawNesScrollX, candidateX);
							_latestNesScrollX = _rawNesScrollX;
						}
						_latestNesScrollY = vy;
					}
					txt = null;
				}
			}
			catch
			{
			}
			try
			{
				await Task.Delay(1, token);
			}
			catch (TaskCanceledException)
			{
				break;
			}
		}
	}

	private void OverlayRendering_Tick(object? sender, EventArgs e)
	{
		if (!_overlayAndFollow || _mesenOverlayProcess == null || _mesenOverlayProcess.HasExited)
		{
			return;
		}
		if (_mesenHwnd == (IntPtr)0)
		{
			_mesenHwnd = FindWindowForProcess(_mesenOverlayProcess.Id);
			if (_mesenHwnd == (IntPtr)0)
			{
				return;
			}
		}
		RepositionMesenWindow();
	}

	private void RepositionMesenWindow()
	{
		if (_mesenHwnd == (IntPtr)0 || CanvasHost == null)
		{
			return;
		}
		try
		{
			DpiScale dpi = VisualTreeHelper.GetDpi(this);
			double num = ZoomSlider?.Value ?? 1.0;
			double num2 = _latestNesScrollX;
			double num3 = _latestNesScrollY;
			double num4 = mapViewportPadding + num2 * num;
			double num5 = mapViewportPadding + (num3 + 48.0) * num + gridRenderShiftY;
			double num6 = 256.0 * num;
			double num7 = 240.0 * num;
			int num8 = (int)(num6 * dpi.DpiScaleX);
			int num9 = (int)(num7 * dpi.DpiScaleY);
			double num10 = SafeViewportWidth();
			double num11 = SafeViewportHeight();
			double val = Math.Max(0.0, (num10 - num6) / 2.0);
			double val2 = Math.Max(0.0, (num11 - num7) / 2.0);
			if (double.IsNaN(_fixedAnchorViewportX) || double.IsNaN(_fixedAnchorViewportY))
			{
				_fixedAnchorViewportX = Math.Min(val, mapViewportPadding);
				_fixedAnchorViewportY = Math.Min(val2, mapViewportPadding + 48.0 * num + gridRenderShiftY);
				_initialCameraCanvasY = num5;
			}
			double fixedAnchorViewportX = _fixedAnchorViewportX;
			double num12 = -528.0 * num;
			double fixedAnchorViewportY = _fixedAnchorViewportY;
			double num13 = fixedAnchorViewportY;
			if (_camFollow && MapScrollViewer != null)
			{
				double num14 = 32.0 * num;
				double num15 = Math.Max(0.0, num4 - fixedAnchorViewportX + num14);
				UpdateSmoothEditorHorizontalScroll(num15);
				_lastScrollOffsetX = num15;
				_lastScrollOffsetY = MapScrollViewer.VerticalOffset;
				if (double.IsNaN(_initialCameraCanvasY))
				{
					_initialCameraCanvasY = num5;
				}
				double num16 = num5 - _initialCameraCanvasY;
				num13 = fixedAnchorViewportY + num16;
			}
			else
			{
				ClearSmoothEditorHorizontalShift();
			}
			num13 += num12;
			double x = Math.Round(fixedAnchorViewportX * dpi.DpiScaleX) / dpi.DpiScaleX;
			double y = Math.Round(num13 * dpi.DpiScaleY) / dpi.DpiScaleY;
			Point point = ((MapScrollViewer != null) ? MapScrollViewer.PointToScreen(new Point(x, y)) : CanvasHost.PointToScreen(new Point(x, y)));
			int num17 = (int)Math.Round(point.X);
			int num18 = (int)Math.Round(point.Y);
			SetWindowPos(_mesenHwnd, HWND_TOPMOST, num17, num18, num8, num9, 80u);
			_lastMesenScreenX = num17;
			_lastMesenScreenY = num18;
			_lastMesenWinW = num8;
			_lastMesenWinH = num9;
		}
		catch
		{
		}
	}

	private void UpdateSmoothEditorHorizontalScroll(double targetOffsetX)
	{
		ScrollViewer mapScrollViewer = MapScrollViewer;
		Canvas canvasHost = CanvasHost;
		Grid mapContentRoot = MapContentRoot;
		if (mapScrollViewer != null && canvasHost != null && mapContentRoot != null)
		{
			DpiScale dpi = VisualTreeHelper.GetDpi(this);
			double num = Math.Max(0.0, canvasHost.ActualWidth - SafeViewportWidth());
			double num2 = Math.Max(0.0, Math.Min(num, targetOffsetX));
			int val = (int)Math.Round(num2 * dpi.DpiScaleX);
			int val2 = (int)Math.Round(num * dpi.DpiScaleX);
			val = Math.Max(0, Math.Min(val2, val));
			int num3 = val / 512 * 512;
			int num4 = val - num3;
			if (_smoothScrollCoarseXPx != num3)
			{
				mapScrollViewer.ScrollToHorizontalOffset((double)num3 / dpi.DpiScaleX);
				_smoothScrollCoarseXPx = num3;
			}
			if (_smoothScrollShiftXPx != num4)
			{
				TranslateTransform translateTransform = _smoothScrollTransform ?? (_smoothScrollTransform = new TranslateTransform());
				translateTransform.X = 0.0 - (double)num4 / dpi.DpiScaleX;
				translateTransform.Y = 0.0;
				mapContentRoot.RenderTransform = translateTransform;
				_smoothScrollShiftXPx = num4;
			}
			_smoothScrollX = (double)val / dpi.DpiScaleX;
			_smoothScrollTargetX = _smoothScrollX;
		}
	}

	private void ClearSmoothEditorHorizontalShift()
	{
		try
		{
			if (MapContentRoot != null)
			{
				MapContentRoot.RenderTransform = Transform.Identity;
			}
		}
		catch
		{
		}
		_smoothScrollCoarseXPx = int.MinValue;
		_smoothScrollShiftXPx = int.MinValue;
		_smoothScrollTransform = null;
	}

	private static IntPtr FindWindowForProcess(int processId)
	{
		nint found = 0;
		EnumWindows(delegate(IntPtr hWnd, IntPtr _)
		{
			GetWindowThreadProcessId(hWnd, out var lpdwProcessId);
			if (lpdwProcessId == (uint)processId && IsWindowVisible(hWnd))
			{
				found = hWnd;
				return false;
			}
			return true;
		}, (IntPtr)0);
		return found;
	}

	internal string BuildOverlayLuaScript()
	{
		return BuildOverlayLuaScript(includeReplay: true);
	}

	internal string BuildOverlayLuaScript(bool includeReplay)
	{
		return BuildOverlayLuaScript(includeReplay, drawPathlines: true);
	}

	internal string BuildOverlayLuaScript(bool includeReplay, bool drawPathlines)
	{
		string fileName = System.IO.Path.GetFileName(MesenTraceFile);
		string fileName2 = System.IO.Path.GetFileName(MesenOrbDebugFile);
		string fileName3 = System.IO.Path.GetFileName(MesenPhysicsDebugFile);
		string value = "{}";
		string value2 = "{}";
		int value3 = 0;
		if (includeReplay)
		{
			try
			{
				if (File.Exists(ReplayTempFile))
				{
					StringBuilder stringBuilder = new StringBuilder();
					StringBuilder stringBuilder2 = new StringBuilder();
					stringBuilder.Append('{');
					stringBuilder2.Append('{');
					bool flag = true;
					bool flag2 = true;
					string[] array = File.ReadAllLines(ReplayTempFile);
					foreach (string text in array)
					{
						if (string.IsNullOrEmpty(text))
						{
							continue;
						}
						if (text.StartsWith("nes_y_offset", StringComparison.Ordinal))
						{
							int num = text.IndexOf(',');
							if (num > 0 && int.TryParse(text.AsSpan(num + 1), out var result))
							{
								value3 = result;
							}
						}
						else
						{
							if (text.StartsWith("frame", StringComparison.Ordinal) || text.StartsWith("p2_path", StringComparison.Ordinal))
							{
								continue;
							}
							if (text.StartsWith("p2,", StringComparison.Ordinal))
							{
								string[] array2 = text.Split(',');
								if (array2.Length >= 3 && int.TryParse(array2[1], out var result2) && int.TryParse(array2[2], out var result3))
								{
									if (!flag2)
									{
										stringBuilder2.Append(',');
									}
									flag2 = false;
									stringBuilder2.Append("{x=").Append(result2).Append(",y=")
										.Append(result3)
										.Append('}');
								}
								continue;
							}
							string[] array3 = text.Split(',');
							if (array3.Length >= 4 && int.TryParse(array3[1], out var result4) && int.TryParse(array3[2], out var result5) && int.TryParse(array3[3], out var result6))
							{
								if (!flag)
								{
									stringBuilder.Append(',');
								}
								flag = false;
								stringBuilder.Append("{x=").Append(result4).Append(",y=")
									.Append(result5)
									.Append(",a=")
									.Append(result6)
									.Append('}');
							}
						}
					}
					stringBuilder.Append('}');
					stringBuilder2.Append('}');
					value = stringBuilder.ToString();
					value2 = stringBuilder2.ToString();
				}
			}
			catch
			{
			}
		}
		string text2 = "-- FamidashEditor overlay script (auto-generated, do not edit)\r\nlocal scrollFile = \"famidash_overlay_scroll.txt\"\r\n\r\nemu.addEventCallback(function()\r\n    local scrollX = emu.read32(0x04A6, emu.memType.nesMemory) or 0\r\n    local sy_raw  = emu.read16(0x04AA, emu.memType.nesMemory) or 0\r\n    -- calculate_linear_scroll_y: linear = lo + hi * 240 (matches nesdash.s implementation)\r\n    local sy_lo   = sy_raw & 0xFF\r\n    local sy_hi   = (sy_raw >> 8) & 0xFF\r\n    local scrollY = sy_lo + sy_hi * 240\r\n    local tempFile = scrollFile .. \".tmp\"\r\n    local f = io.open(tempFile, \"w\")\r\n    if f then\r\n        f:write(tostring(scrollX) .. \",\" .. tostring(scrollY))\r\n        f:close()\r\n        pcall(function() os.remove(scrollFile) end)\r\n        pcall(function() os.rename(tempFile, scrollFile) end)\r\n    end\r\nend, emu.eventType.endFrame)\r\n";
		string text3 = $"\r\n-- ── Replay injector ────────────────────────────────────────────────────────\r\n-- Output paths (written, never read).  Resolved at runtime from environment\r\n-- so the script contains no machine-specific paths.  Falls back to the\r\n-- current working directory if no temp env var is set.\r\nlocal function _tempDir()\r\n    local d = os.getenv(\"TEMP\") or os.getenv(\"TMP\") or os.getenv(\"TMPDIR\") or \"\"\r\n    if d == \"\" then return \".\" end\r\n    -- Strip trailing slash/backslash for consistent joining.\r\n    local last = d:sub(-1)\r\n    if last == \"/\" or last == \"\\\\\" then d = d:sub(1, -2) end\r\n    return d\r\nend\r\nlocal function _tempPath(name)\r\n    local d = _tempDir()\r\n    -- Prefer backslash on Windows (TEMP env), forward slash elsewhere.\r\n    local sep = (os.getenv(\"TEMP\") or os.getenv(\"TMP\")) and \"\\\\\" or \"/\"\r\n    return d .. sep .. name\r\nend\r\nlocal traceFile  = _tempPath(\"{fileName}\")\r\nlocal orbDbgFile = _tempPath(\"{fileName2}\")\r\nlocal physDbgFile= _tempPath(\"{fileName3}\")\r\n-- Replay table is embedded directly so this script has NO inbound file deps.\r\n-- Regenerated by FamidashEditor every time a path is exported.\r\nlocal replay     = {value}\r\nlocal replay2    = {value2}\r\nlocal nesYOffset = {value3}\r\nlocal SHOW_PATHLINES = {(drawPathlines ? "true" : "false")}\r\nlocal STATE_GAME = 0x02\r\nlocal ADDR_GAMESTATE = 0x049C\r\nlocal ADDR_JOYPAD1_HOLD = 0x0022\r\nlocal PAD_A  = 0x80\r\nlocal PAD_UP = 0x08\r\nlocal cursor = 1\r\nlocal armed = false\r\nlocal prevPx = -1\r\nlocal lastA = false\r\nlocal frameIdx = 0\r\nlocal traceFp = nil\r\nlocal orbDbgFp = nil\r\nlocal physDbgFp = nil\r\n-- Per-frame ring buffer of physics-routine events (cleared each NMI / endFrame).\r\n-- Format: each entry is a preformatted string \"TAG pc=XXXX cpx=... cpy=... ...\"\r\nlocal physEvents = {{}}\r\nlocal physEventCount = 0\r\nlocal PHYS_EVENT_MAX = 64\r\n-- Frame range filter for orb debug dump.  Set both to large values to log\r\n-- the entire run, narrow them once you know the divergence frame band.\r\nlocal ORB_DBG_LO = 0\r\nlocal ORB_DBG_HI = 1000000\r\n-- Ring buffer of recent ACTUAL NES (px,py) positions, for drawing a real\r\n-- player trail to visualise divergence from the PF-projected path.\r\n-- Each entry is {{ x = pf-coord X, y = pf-coord Y }}.  Drawn yellow.\r\nlocal nesTrail = {{}}\r\nlocal nesTrailMax = 600\r\nlocal nesTrailHead = 1\r\nlocal nesTrailCount = 0\r\nlocal function clearPathOverlay()\r\n    armed = false\r\n    lastA = false\r\n    prevPx = -1\r\n    nesTrail = {{}}\r\n    nesTrailHead = 1\r\n    nesTrailCount = 0\r\n    physEventCount = 0\r\nend\r\n-- Linear-Y offset between NES world coords and pathfinder world coords:\r\n--   PF_y = NES_y - nesYOffset    (set per-level by ExportReplayCsv)\r\n-- Already baked in above from the C# generator.\r\n\r\n-- ── cube_data[0] write tracker ─────────────────────────────────────────────\r\n-- Capture the PC of every write to $007B (cube_data[0]).  When the byte\r\n-- transitions 0->1 (death flag set), the most recent PC is the routine that\r\n-- killed the player.  Stored in `lastCubeDataWritePC` and flushed into the\r\n-- per-frame trace row's `cube_data` cell as \"val@PC\" once per kill.\r\nlocal lastCubeDataWritePC = 0\r\nlocal cubeDataDeathPC = nil      -- PC at the 0->1 transition (sticky until reset)\r\nlocal cubeDataDeathCtx = nil     -- probe context snapshot at the 0->1 transition\r\nlocal cubeDataPrev = 0\r\nemu.addMemoryCallback(function(addr, value)\r\n    local st = emu.getState()\r\n    -- Mesen2 state is a flat table keyed by dotted-path strings, e.g.\r\n    -- state[\"cpu.pc\"], state[\"ppu.scanline\"].  Nested-table access\r\n    -- (st.cpu.pc) returns nil and gave PC=0000 in the previous trace.\r\n    local pc = 0\r\n    if st then\r\n        pc = st[\"cpu.pc\"] or st[\"cpu.PC\"] or 0\r\n    end\r\n    lastCubeDataWritePC = pc\r\n    -- Detect 0->1 transition (death).  Bit 0 is the kill flag.\r\n    local prev = cubeDataPrev\r\n    cubeDataPrev = value\r\n    if (prev & 1) == 0 and (value & 1) == 1 then\r\n        cubeDataDeathPC = pc\r\n        -- Snapshot the probe context that bg_collision_sub left in zeropage\r\n        -- when the kill routine fired.  Addresses come from\r\n        -- sim-famidash/BUILD/main/famidash.dbg:\r\n        --   _temp_x=$93 _temp_y=$94 _temp_room=$95 _collision=$81\r\n        --   _Generic=$5C8 (x,y,width,height bytes)\r\n        --   _currplayer_x=$68 (16-bit) _currplayer_y=$6A (16-bit)\r\n        --   _currplayer_mini=$67 _scroll_x=$4A6 (32-bit)\r\n        local M = emu.memType.nesMemory\r\n        local tx  = emu.read(0x93, M) or 0\r\n        local ty  = emu.read(0x94, M) or 0\r\n        local tr  = emu.read(0x95, M) or 0\r\n        local col = emu.read(0x81, M) or 0\r\n        local gx  = emu.read(0x5C8, M) or 0\r\n        local gy  = emu.read(0x5C9, M) or 0\r\n        local gw  = emu.read(0x5CA, M) or 0\r\n        local gh  = emu.read(0x5CB, M) or 0\r\n        local cpx = emu.read16(0x68, M) or 0\r\n        local cpy = emu.read16(0x6A, M) or 0\r\n        local mini= emu.read(0x67, M) or 0\r\n        local sx  = emu.read32(0x4A6, M) or 0\r\n        cubeDataDeathCtx = string.format(\r\n            \"tx=%02X ty=%02X tr=%02X col=%02X G=(%02X,%02X,%dx%d) cp=(%04X,%04X) mini=%d sx=%05X\",\r\n            tx, ty, tr, col, gx, gy, gw, gh, cpx, cpy, mini, sx)\r\n    end\r\nend, emu.callbackType.write, 0x007B)\r\n\r\n-- ── Physics-routine entry hooks ────────────────────────────────────────────\r\n-- For PF↔NES divergence diagnosis, capture full pre-call state at every\r\n-- entry to the major movement / eject / collision routines.  PCs come from\r\n-- BUILD/huge/famidash.dbg (rebuild ROM with shifted addrs ⇒ regenerate).\r\n-- Each event is appended to physEvents[]; the per-frame trace callback\r\n-- flushes them to physDbgFile and clears the buffer.\r\n--\r\n-- NOTE: cc65 banked PRG.  Multiple banks may map the same CPU address; the\r\n-- callback fires on ANY exec at the PC regardless of which bank is paged in.\r\n-- We tag each event with the current bank (read via emu.getState() PRG\r\n-- register) so post-hoc filtering can distinguish wrong-bank false hits.\r\n--\r\n-- Routines (huge build):\r\n--   $A183 _bg_coll_U   (size 313 -> last $A2BB)\r\n--   $A2BC _bg_coll_D   (size 306 -> last $A3ED)\r\n--   $ADDB _bg_coll_R   (size  35 -> last $ADFD)\r\n--   $ADFE _bg_coll_L   (size  43 -> last $AE28)\r\n--   $B03C _bg_coll_death (size 148 -> last $B0CF)\r\n--   $B0D0 _ufo_ship_eject (size  42 -> last $B0F9)\r\n--   $B0FA _common_gravity_routine (size 357 -> last $B25E)\r\n--   $B25F _ufo_movement (size  97 -> last $B2BF)\r\n--   $B2C0 _ball_eject (size 286 -> last $B3DD)\r\n--   $B3DE _ball_movement (size 319 -> last $B51C ish)\r\n--   $B542 _cube_eject (size 164 -> last $B5E5)\r\n--   $B5E6 _cube_movement (size 1233 -> last $BAB6)\r\n--   $BAD5 _ship_movement (size 222 -> last $BBB2)\r\n--   $BBB3 _spider_eject (size  40 -> last $BBDA)\r\n--   $BBDB _spider_movement (size 300 -> last $BD06)\r\n--   $BD65 _wave_movement (size 291 -> last $BE87)\r\n--   $87DA _x_movement_coll (size  77 -> last $8826)\r\n--   $8827 _x_movement (size 441 -> last $89DF)\r\nlocal function _physBank()\r\n    local s = emu.getState()\r\n    if not s then return -1 end\r\n    -- Mesen2 NES state exposes 'mapper.X' and 'cartridge.X' fields.  The\r\n    -- selected PRG bank for the $A000-$BFFF window is mapper-specific;\r\n    -- best-effort dump of common keys.\r\n    return s[\"mapper.prgPageSize\"] or s[\"mapper.bank0\"] or -1\r\nend\r\n\r\nlocal function _physSnap(tag, pc)\r\n    if physEventCount >= PHYS_EVENT_MAX then return end\r\n    local M = emu.memType.nesMemory\r\n    local cpx  = emu.read16(0x68, M) or 0\r\n    local cpy  = emu.read16(0x6A, M) or 0\r\n    local cpvy = emu.read16(0x6E, M) or 0\r\n    if cpvy >= 0x8000 then cpvy = cpvy - 0x10000 end\r\n    local gx  = emu.read(0x5C8, M) or 0\r\n    local gy  = emu.read(0x5C9, M) or 0\r\n    local gw  = emu.read(0x5CA, M) or 0\r\n    local gh  = emu.read(0x5CB, M) or 0\r\n    local ej_U = emu.read(0x8D, M) or 0\r\n    local ej_D = emu.read(0x8C, M) or 0\r\n    local col  = emu.read(0x81, M) or 0\r\n    local tx   = emu.read(0x93, M) or 0\r\n    local ty   = emu.read(0x94, M) or 0\r\n    local tr   = emu.read(0x95, M) or 0\r\n    local mini = emu.read(0x67, M) or 0\r\n    local grav = emu.read(0x70, M) or 0\r\n    local tidx = emu.read(0x79, M) or 0\r\n    local cd   = emu.read(0x7B, M) or 0\r\n    local sx   = emu.read32(0x4A6, M) or 0\r\n    local sy_r = emu.read16(0x4AA, M) or 0\r\n    -- ── SLOPE STATE (zero page currplayer + zp-cached + globals) ─────────\r\n    -- 0x74=currplayer_slope_frames, 0x75=currplayer_was_on_slope_counter,\r\n    -- 0x76=currplayer_slope_type, 0x77=currplayer_last_slope_type.\r\n    -- 0x97=slope_frames(zp), 0x99=slope_type(zp), 0x9B=was_on_slope_counter(zp).\r\n    -- 0x4B5=_make_cube_jump_higher, 0x49A=_last_slope_type (player array).\r\n    local cpsF   = emu.read(0x74, M) or 0\r\n    local cpswOn = emu.read(0x75, M) or 0\r\n    local cpsT   = emu.read(0x76, M) or 0\r\n    local cplst  = emu.read(0x77, M) or 0\r\n    local zsF    = emu.read(0x97, M) or 0\r\n    local zsT    = emu.read(0x99, M) or 0\r\n    local zswOn  = emu.read(0x9B, M) or 0\r\n    local mcjh   = emu.read(0x4B5, M) or 0\r\n    local glst   = emu.read(0x49A, M) or 0\r\n    local cpvx = emu.read16(0x6C, M) or 0\r\n    if cpvx >= 0x8000 then cpvx = cpvx - 0x10000 end\r\n    -- A/X/Y registers at entry — useful to read return value when hooking\r\n    -- the byte AFTER an RTS-bearing call site.\r\n    local st = emu.getState()\r\n    local A = (st and (st[\"cpu.a\"] or st[\"cpu.A\"])) or 0\r\n    local X = (st and (st[\"cpu.x\"] or st[\"cpu.X\"])) or 0\r\n    local Y = (st and (st[\"cpu.y\"] or st[\"cpu.Y\"])) or 0\r\n    physEventCount = physEventCount + 1\r\n    physEvents[physEventCount] = string.format(\r\n        \"%s pc=%04X A=%02X X=%02X Y=%02X cpx=%04X cpy=%04X cpvx=%d cpvy=%d \" ..\r\n        \"G=(%02X,%02X,%dx%d) ej_U=%02X ej_D=%02X col=%02X probe=(%02X,%02X,%02X) \" ..\r\n        \"mini=%d grav=%02X tidx=%02X cd=%02X sx=%05X sy=%04X \" ..\r\n        \"SLOPE[cpsF=%d cpswOn=%d cpsT=%02X cplst=%02X zsF=%d zsT=%02X zswOn=%d mcjh=%d glst=%02X]\",\r\n        tag, pc, A, X, Y, cpx, cpy, cpvx, cpvy,\r\n        gx, gy, gw, gh, ej_U, ej_D, col, tx, ty, tr,\r\n        mini, grav, tidx, cd, sx, sy_r,\r\n        cpsF, cpswOn, cpsT, cplst, zsF, zsT, zswOn, mcjh, glst)\r\nend\r\n\r\nlocal function _hookPhys(tag, pc)\r\n    emu.addMemoryCallback(function() _physSnap(tag, pc) end,\r\n        emu.callbackType.exec, pc)\r\nend\r\n\r\n-- Entry hooks\r\n_hookPhys(\"bg_coll_U.in\",      0xA183)\r\n_hookPhys(\"bg_coll_D.in\",      0xA2BC)\r\n_hookPhys(\"bg_coll_R.in\",      0xAE0C)\r\n_hookPhys(\"bg_coll_L.in\",      0xAE2F)\r\n_hookPhys(\"bg_coll_death.in\",  0xB07E)\r\n_hookPhys(\"ufo_ship_eject.in\", 0xB148)\r\n_hookPhys(\"common_gravity.in\", 0xB172)\r\n_hookPhys(\"common_gravity.out\", 0xB2D6) -- last byte of _common_gravity_routine ($B172+357-1)\r\n_hookPhys(\"ufo_movement.in\",   0xB2D7)\r\n_hookPhys(\"ball_eject.in\",     0xB338)\r\n_hookPhys(\"ball_movement.in\",  0xB456)\r\n_hookPhys(\"cube_eject.in\",     0xB5CA)\r\n_hookPhys(\"cube_movement.in\",  0xB66E)\r\n_hookPhys(\"ship_movement.in\",  0xBB5D)\r\n_hookPhys(\"spider_eject.in\",   0xBC3B)\r\n_hookPhys(\"spider_movement.in\",0xBC63)\r\n_hookPhys(\"wave_movement.in\",  0xBDED)\r\n_hookPhys(\"x_movement_coll.in\",0x882D)\r\n_hookPhys(\"x_movement.in\",     0x887A)\r\n\r\n-- ── SLOPE routine hooks ─────────────────────────────────────────────────\r\n-- Addresses from famidash/BUILD/huge/famidash.dbg.  Each in+out pair shows\r\n-- the pre/post zp slope state so PF↔NES slope timing/state can be diffed.\r\n--   _slope_vel          : $87C1 + 50 -> last $87F2\r\n--   _apply_slope_vel    : $87F3 + 58 -> last $882C\r\n--   _bg_coll_slope      : $AA0A + 823 -> last $AD40\r\n--   _bg_coll_return_slope_D : $AEF1 + 82 -> last $AF42\r\n--   _bg_coll_return_slope_U : $AF43 + 87 -> last $AF99\r\n--   _slope_jump_check   : $B5A5 + 37 -> last $B5C9\r\n_hookPhys(\"slope_vel.in\",          0x87C1)\r\n_hookPhys(\"slope_vel.out\",         0x87F2)\r\n_hookPhys(\"apply_slope_vel.in\",    0x87F3)\r\n_hookPhys(\"apply_slope_vel.out\",   0x882C)\r\n_hookPhys(\"bg_coll_slope.in\",      0xAA0A)\r\n_hookPhys(\"bg_coll_slope.out\",     0xAD40)\r\n_hookPhys(\"bg_coll_ret_slp_D.in\",  0xAEF1)\r\n_hookPhys(\"bg_coll_ret_slp_D.out\", 0xAF42)\r\n_hookPhys(\"bg_coll_ret_slp_U.in\",  0xAF43)\r\n_hookPhys(\"bg_coll_ret_slp_U.out\", 0xAF99)\r\n_hookPhys(\"slope_jump_check.in\",   0xB5A5)\r\n_hookPhys(\"slope_jump_check.out\",  0xB5C9)\r\n\r\n-- Exit hooks (last byte of each scope = presumed RTS).  Captures POST-call\r\n-- state of currplayer_y / vel_y / eject_U/D / collision so a U/D eject's\r\n-- effect on Y_high is directly visible in the log.  A register holds C\r\n-- return value at function exit (if scope ends with RTS).\r\n_hookPhys(\"bg_coll_U.out\",      0xA2BB)\r\n_hookPhys(\"bg_coll_D.out\",      0xA3ED)\r\n_hookPhys(\"bg_coll_R.out\",      0xAE2E)\r\n_hookPhys(\"bg_coll_L.out\",      0xAE59)\r\n_hookPhys(\"bg_coll_death.out\",  0xB147)\r\n_hookPhys(\"ufo_ship_eject.out\", 0xB171)\r\n_hookPhys(\"ufo_movement.out\",   0xB337)\r\n_hookPhys(\"ball_eject.out\",     0xB455)\r\n_hookPhys(\"cube_eject.out\",     0xB66D)\r\n_hookPhys(\"ship_movement.out\",  0xBC3A)\r\n_hookPhys(\"spider_eject.out\",   0xBC62)\r\n_hookPhys(\"spider_movement.out\",0xBD8E)\r\n_hookPhys(\"x_movement_coll.out\",0x8879)\r\n_hookPhys(\"x_movement.out\",     0x8A32)\r\n\r\n-- ── currplayer_y write watchers ($6A=low, $6B=high) ────────────────────────\r\n-- Memory-write hooks log EVERY write to cpy so we can pinpoint which\r\n-- instruction modifies it.  Diagnoses the sim=2211 +0x200 mystery where\r\n-- ej_D=00 yet cpy_high increased by 2 between slope_vel.out and bg_coll_D.out.\r\nlocal function _cpyWriteSnap(addr, value)\r\n    if physEventCount >= PHYS_EVENT_MAX then return end\r\n    local M = emu.memType.nesMemory\r\n    local st = emu.getState()\r\n    local pc = (st and (st[\"cpu.pc\"] or st[\"cpu.PC\"])) or 0\r\n    local A  = (st and (st[\"cpu.a\"]  or st[\"cpu.A\"]))  or 0\r\n    local X  = (st and (st[\"cpu.x\"]  or st[\"cpu.X\"]))  or 0\r\n    local Y  = (st and (st[\"cpu.y\"]  or st[\"cpu.Y\"]))  or 0\r\n    -- cpy is 16-bit at $6A; we get notified PER-byte so read both to show\r\n    -- the COMPOSED post-write value (Mesen fires callback BEFORE the actual\r\n    -- write completes — we synthesize the post-write value).\r\n    local cpy_lo = emu.read(0x6A, M) or 0\r\n    local cpy_hi = emu.read(0x6B, M) or 0\r\n    if addr == 0x6A then cpy_lo = value end\r\n    if addr == 0x6B then cpy_hi = value end\r\n    local cpy_post = cpy_lo | (cpy_hi << 8)\r\n    local ej_U = emu.read(0x8D, M) or 0\r\n    local ej_D = emu.read(0x8C, M) or 0\r\n    -- ROM file offset of the currently-paged code at PC: disambiguates which\r\n    -- bank's $BDxx code is actually executing.  For MMC3 8KB swap at\r\n    -- $A000-$BFFF, bank = offset / 0x2000 (when normalized to PRG ROM start).\r\n    local romOff = -1\r\n    if emu.getPrgRomOffset then romOff = emu.getPrgRomOffset(pc) or -1 end\r\n    physEventCount = physEventCount + 1\r\n    physEvents[physEventCount] = string.format(\r\n        \"CPY_WRITE addr=%04X val=%02X -> cpy_post=%04X pc=%04X romOff=%X A=%02X X=%02X Y=%02X ej_U=%02X ej_D=%02X\",\r\n        addr, value, cpy_post, pc, romOff, A, X, Y, ej_U, ej_D)\r\nend\r\nemu.addMemoryCallback(_cpyWriteSnap, emu.callbackType.write, 0x006A)\r\nemu.addMemoryCallback(_cpyWriteSnap, emu.callbackType.write, 0x006B)\r\n\r\n-- Per-frame: track player position, arm replay on level start (0 -> non-zero),\r\n-- advance cursor monotonically, capture the desired A state for the next poll.\r\nemu.addEventCallback(function()\r\n    if #replay == 0 then\r\n        emu.drawString(8, 8, \"REPLAY: file empty or missing\", 0xFF6666, 0x000000)\r\n        return\r\n    end\r\n\r\n    -- Only draw/advance overlay while actively in gameplay; clear paths\r\n    -- immediately when the game transitions to level-complete/menu states.\r\n    local gameState = emu.read(ADDR_GAMESTATE, emu.memType.nesMemory) or 0\r\n    if gameState ~= STATE_GAME then\r\n        if armed or nesTrailCount > 0 then\r\n            clearPathOverlay()\r\n        end\r\n        return\r\n    end\r\n\r\n    -- _player_x / _player_y are screen-relative; add scroll to get NES world\r\n    -- coords, then subtract nesYOffset to convert into pathfinder world coords\r\n    -- (matches PathfinderEngine PathPoints which use the editor's row 0 as\r\n    -- the origin, while the NES build skips top map rows).\r\n    local scrollX = emu.read32(0x04A6, emu.memType.nesMemory) or 0\r\n    local sy_raw  = emu.read16(0x04AA, emu.memType.nesMemory) or 0\r\n    local scrollY = (sy_raw & 0xFF) + ((sy_raw >> 8) & 0xFF) * 240\r\n    local rawX = emu.read16(0x043D, emu.memType.nesMemory) or 0\r\n    local rawY = emu.read16(0x0441, emu.memType.nesMemory) or 0\r\n    local px = scrollX + (rawX >> 8) + 8\r\n    local py = scrollY + (rawY >> 8) + 8 - nesYOffset\r\n\r\n    -- Detect level start / respawn: prev was 0/uninitialized, now in-game.\r\n    if (prevPx <= 0 or px < prevPx - 16) and px > 0 then\r\n        cursor = 1\r\n        armed = true\r\n        frameIdx = 0\r\n        -- Reset the actual-NES trail on respawn so old trails don't linger.\r\n        nesTrail = {{}}\r\n        nesTrailHead = 1\r\n        nesTrailCount = 0\r\n        -- Trace file: APPEND on respawn so death-frames are preserved across\r\n        -- attempts.  Open in \"a\" the first time, write a separator on each\r\n        -- arm so attempts are visually grouped.  This way comparing the trace\r\n        -- against PF can see the frames immediately preceding NES-death.\r\n        if not traceFp then\r\n            traceFp = io.open(traceFile, \"w\")\r\n            if traceFp then\r\n                traceFp:write(\"nes_y_offset,\" .. tostring(nesYOffset) .. \"\\n\")\r\n                traceFp:write(\"rom_frame,sim_cursor,px,py,a_next,a_cur,raw_x,raw_y,scrollx,scrolly,vel_y,table_idx,gravity_mod,dashing,gamemode,scroll_y_subpx,framerate,tgt_scroll_y,cp_y,scroll_y_raw,cube_data,death_pc,death_ctx,collmap_r8,mini,cp_gravity,nocamlock,nocamlockforced,min_scroll_y,dual\\n\")\r\n            end\r\n        else\r\n            traceFp:write(\"# --- respawn ---\\n\")\r\n            traceFp:flush()\r\n        end\r\n        if not orbDbgFp then\r\n            orbDbgFp = io.open(orbDbgFile, \"w\")\r\n            if orbDbgFp then\r\n                orbDbgFp:write(\"# NES sprite/orb decision dump.  Per-frame: ENTER + per-slot SLOT lines.\\n\")\r\n                orbDbgFp:write(\"# Symbol RAM addrs: Generic=0x5C8 (x,y,w,h), Generic2=0x5CC (x,y,w,h)\\n\")\r\n                orbDbgFp:write(\"#   activesprites_x_lo=0x4DB[16], _x_hi=0x4EB[16] (world X 16-bit)\\n\")\r\n                orbDbgFp:write(\"#   activesprites_y_lo=0x4FB[16], _y_hi=0x50B[16] (world Y 16-bit)\\n\")\r\n                orbDbgFp:write(\"#   activesprites_type=0x51B[16]\\n\")\r\n                orbDbgFp:write(\"#   activesprites_realx=0x54B[16] (1 byte, screen-local draw x)\\n\")\r\n                orbDbgFp:write(\"#   activesprites_realy=0x55B[16] (1 byte, screen-local draw y)\\n\")\r\n                orbDbgFp:write(\"#   activesprites_active=0x56B[16], _activated=0x57B[16]\\n\")\r\n                orbDbgFp:write(\"#   currplayer_x=0x68 (16-bit), currplayer_y=0x6A (16-bit)\\n\")\r\n                orbDbgFp:write(\"#   currplayer_vel_y=0x6E (16-bit signed), currplayer_table_idx=0x79\\n\")\r\n                orbDbgFp:write(\"#   currplayer_gravity=0x70, currplayer_mini=0x67, cube_data=0x7B\\n\")\r\n                orbDbgFp:write(\"#   dashing=0x4D4, orbactive=0x4C6, orbhitonthisframe=0x451\\n\")\r\n                orbDbgFp:write(\"#   index=0x92 (last sprite slot tested), max_loaded_sprites=16\\n\")\r\n                orbDbgFp:write(\"#   sprite tables: widths=0xA811, heights=0xA711, x_offset=0xA911, y_offset=0xAA11\\n\")\r\n            end\r\n        else\r\n            orbDbgFp:write(\"# --- respawn ---\\n\")\r\n            orbDbgFp:flush()\r\n        end\r\n        if not physDbgFp then\r\n            physDbgFp = io.open(physDbgFile, \"w\")\r\n            if physDbgFp then\r\n                physDbgFp:write(\"# NES physics-routine entry/exit log.  Per-frame: F=<frame> sim=<cursor>\\n\")\r\n                physDbgFp:write(\"# followed by 0..N TAG lines (entry: <name>.in / exit: <name>.out).\\n\")\r\n                physDbgFp:write(\"# Fields: tag pc=PC A/X/Y cpx cpy cpvx cpvy G=(x,y,wxh) ej_U ej_D col probe=(tx,ty,tr) mini grav tidx cd sx sy SLOPE[cpsF cpswOn cpsT cplst zsF zsT zswOn mcjh glst]\\n\")\r\n                physDbgFp:write(\"# Probes: $93=temp_x $94=temp_y $95=temp_room $81=collision\\n\")\r\n                physDbgFp:write(\"# Eject:  $8D=eject_U $8C=eject_D (currplayer_y_high -= eject_U+1 / += eject_D+1)\\n\")\r\n            end\r\n        else\r\n            physDbgFp:write(\"# --- respawn ---\\n\")\r\n            physDbgFp:flush()\r\n        end\r\n    end\r\n    prevPx = px\r\n\r\n    -- Append current NES position to the actual-trail ring buffer.\r\n    if armed then\r\n        local holdBits = emu.read(ADDR_JOYPAD1_HOLD, emu.memType.nesMemory) or 0\r\n        local heldNow = ((holdBits & (PAD_A | PAD_UP)) ~= 0)\r\n        nesTrail[nesTrailHead] = {{ x = px, y = py, a = heldNow and 1 or 0, ra = lastA and 1 or 0 }}\r\n        nesTrailHead = nesTrailHead + 1\r\n        if nesTrailHead > nesTrailMax then nesTrailHead = 1 end\r\n        if nesTrailCount < nesTrailMax then nesTrailCount = nesTrailCount + 1 end\r\n    end\r\n\r\n    if armed then\r\n        -- Guard: at game-start scrollX is garbage (~0xFFFFFF82), making px ~4\r\n        -- billion and blowing the cursor to #replay in one step.  Only advance\r\n        -- when px is in a plausible in-level range (NES level width << 500000px).\r\n        if px >= 0 and px < 524288 then\r\n            while cursor < #replay and replay[cursor + 1].x <= px do\r\n                cursor = cursor + 1\r\n            end\r\n        end\r\n        -- Frame alignment: PathPoints[cursor] is the post-physics position for\r\n        -- sim frame `cursor`, produced by Inputs[cursor]. We've just observed\r\n        -- that position in endFrame N. The next emulator inputPolled will be\r\n        -- for frame N+1, so we must feed Inputs[cursor+1] -- otherwise the\r\n        -- press lands one frame late (jumps fire after the spike).\r\n        local curEntry = replay[cursor]\r\n        local nextEntry = replay[cursor + 1] or replay[cursor]\r\n        local curA = curEntry and (curEntry.a == 1) or false\r\n        lastA = nextEntry and (nextEntry.a == 1) or false\r\n\r\n        -- Append per-frame trace row.\r\n        if traceFp then\r\n            -- Per-frame dump of NES physics state for divergence analysis.\r\n            -- Symbol locations from BUILD/main/famidash.dbg:\r\n            --   _gamemode             abs (varies; read via debugger pref)\r\n            --   _scroll_y_subpx       abs (8-bit)\r\n            local vel_y_raw = emu.read16(0x006E, emu.memType.nesMemory) or 0\r\n            local vel_y = vel_y_raw\r\n            if vel_y >= 0x8000 then vel_y = vel_y - 0x10000 end\r\n            local table_idx = emu.read(0x0079, emu.memType.nesMemory) or 0\r\n            local gravity_mod = emu.read(0x05C2, emu.memType.nesMemory) or 0\r\n            local dashing = emu.read(0x04D4, emu.memType.nesMemory) or 0\r\n            local gamemode = emu.read(0x007A, emu.memType.nesMemory) or 0\r\n            local scroll_y_subpx = emu.read(0x04AC, emu.memType.nesMemory) or 0\r\n            local framerate_v = emu.read(0x002D, emu.memType.nesMemory) or 0\r\n            local tgt_scroll_y = emu.read16(0x04AF, emu.memType.nesMemory) or 0\r\n            local cp_y_raw = emu.read16(0x006A, emu.memType.nesMemory) or 0\r\n            local cp_y = cp_y_raw\r\n            if cp_y >= 0x8000 then cp_y = cp_y - 0x10000 end\r\n            local scroll_y_raw = sy_raw\r\n            local cube_data0 = emu.read(0x007B, emu.memType.nesMemory) or 0\r\n            local mini_flag = emu.read(0x0067, emu.memType.nesMemory) or 0\r\n            local cp_gravity = emu.read(0x0070, emu.memType.nesMemory) or 0\r\n            local nocamlock = emu.read(0x0495, emu.memType.nesMemory) or 0\r\n            local nocamlockforced = emu.read(0x0496, emu.memType.nesMemory) or 0\r\n            local min_scroll_y = emu.read16(0x0363, emu.memType.nesMemory) or 0\r\n            local dual_v = emu.read(0x0096, emu.memType.nesMemory) or 0\r\n            -- Dump collMap row 8 of all 4 rooms (entries $80..$8F) where the\r\n            -- ship-section spike-collision probes typically land at this scroll\r\n            -- depth.  Format: \"r0:[hex16]|r1:[hex16]|r2:[hex16]|r3:[hex16]\".\r\n            local cm_addrs = {{0x6080, 0x6180, 0x6280, 0x6380}}\r\n            local cm_str = \"\"\r\n            for ri, base in ipairs(cm_addrs) do\r\n                if ri > 1 then cm_str = cm_str .. \"|\" end\r\n                cm_str = cm_str .. \"r\" .. tostring(ri-1) .. \":\"\r\n                for k = 0, 15 do\r\n                    cm_str = cm_str .. string.format(\"%02X\", emu.read(base + k, emu.memType.nesMemory) or 0)\r\n                end\r\n            end\r\n            traceFp:write(string.format(\"%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%s,%s,%s,%d,%d,%d,%d,%d,%d\\n\",\r\n                frameIdx, cursor, px, py, lastA and 1 or 0, curA and 1 or 0,\r\n                rawX, rawY, scrollX, scrollY,\r\n                vel_y, table_idx, gravity_mod, dashing, gamemode, scroll_y_subpx,\r\n                framerate_v, tgt_scroll_y, cp_y, scroll_y_raw, cube_data0,\r\n                cubeDataDeathPC and string.format(\"%04X\", cubeDataDeathPC) or \"\",\r\n                cubeDataDeathCtx or \"\",\r\n                cm_str, mini_flag, cp_gravity,\r\n                nocamlock, nocamlockforced, min_scroll_y, dual_v))\r\n            traceFp:flush()\r\n            -- Reset sticky death PC after we've logged it once with cube_data=1.\r\n            if cube_data0 == 0 then\r\n                cubeDataDeathPC = nil\r\n                cubeDataDeathCtx = nil\r\n            end\r\n        end\r\n\r\n        -- ── Physics-routine event flush ─────────────────────────────────\r\n        -- Drain the per-frame ring of physics-routine entry/exit events\r\n        -- captured by the addMemoryCallback hooks above into physDbgFile.\r\n        -- Header line ties events to (rom_frame, sim_cursor) so the log can\r\n        -- be cross-referenced 1:1 with the per-frame trace above.\r\n        if physDbgFp and physEventCount > 0 then\r\n            physDbgFp:write(string.format(\"F=%d sim=%d events=%d\\n\",\r\n                frameIdx, cursor, physEventCount))\r\n            for i = 1, physEventCount do\r\n                physDbgFp:write(\"  \")\r\n                physDbgFp:write(physEvents[i])\r\n                physDbgFp:write(\"\\n\")\r\n            end\r\n            physDbgFp:flush()\r\n        end\r\n        physEventCount = 0\r\n\r\n        -- ── NES orb-debug dump ─────────────────────────────────────────\r\n        -- Mirror of PF's famidash_pf_orb_debug.log.  For each frame within\r\n        -- ORB_DBG_LO..ORB_DBG_HI, dump the full sprite ring buffer and the\r\n        -- player/sprite hitboxes.  Match by \"f={{frame}}\" against PF.\r\n        -- frame in this log == sim_cursor (NES emulator frame counts the\r\n        -- replay-driven sim ticks).  Use \"sim={{cursor}}\" tag for explicit\r\n        -- alignment with PF's f={{frameCounter}} (which IS sim_cursor).\r\n        if orbDbgFp and cursor >= ORB_DBG_LO and cursor <= ORB_DBG_HI then\r\n            local M = emu.memType.nesMemory\r\n            local function rd8(a)  return emu.read(a, M)   or 0 end\r\n            local function rd16(a) return emu.read16(a, M) or 0 end\r\n            local function s16(v)  if v >= 0x8000 then return v - 0x10000 else return v end end\r\n\r\n            local g_x  = rd8(0x5C8); local g_y  = rd8(0x5C9)\r\n            local g_w  = rd8(0x5CA); local g_h  = rd8(0x5CB)\r\n            local g2_x = rd8(0x5CC); local g2_y = rd8(0x5CD)\r\n            local g2_w = rd8(0x5CE); local g2_h = rd8(0x5CF)\r\n            local cpx = rd16(0x68); local cpy = rd16(0x6A)\r\n            local cpvy = s16(rd16(0x6E))\r\n            local cp_grav = rd8(0x70); local cp_mini = rd8(0x67)\r\n            local cp_tidx = rd8(0x79); local cp_cube = rd8(0x7B)\r\n            local dash = rd8(0x4D4); local orbAct = rd8(0x4C6)\r\n            local orbHit = rd8(0x451); local lastIdx = rd8(0x92)\r\n            local gm = rd8(0x7A)\r\n\r\n            orbDbgFp:write(string.format(\r\n                \"f=%d sim=%d ENTER mode=%d mini=%d grav=%d dash=%d orbAct=%d orbHit=%d lastSprIdx=%d \" ..\r\n                \"cpx=0x%04X cpy=0x%04X cpvy=%d cp_tidx=%d cube_data=0x%02X \" ..\r\n                \"Generic=(%d,%d,%dx%d) Generic2=(%d,%d,%dx%d) press=%d held=%d\\n\",\r\n                frameIdx, cursor, gm, cp_mini, cp_grav, dash, orbAct, orbHit, lastIdx,\r\n                cpx, cpy, cpvy, cp_tidx, cp_cube,\r\n                g_x, g_y, g_w, g_h, g2_x, g2_y, g2_w, g2_h,\r\n                lastA and 1 or 0, curA and 1 or 0))\r\n\r\n            -- Walk all 16 active-sprite slots.  Layout (from famidash.dbg,\r\n            -- expanded by lohi_arr16_decl in arr_macros.h):\r\n            --   _activesprites_x_lo = 0x4DB[16]   _activesprites_x_hi = 0x4EB[16]\r\n            --   _activesprites_y_lo = 0x4FB[16]   _activesprites_y_hi = 0x50B[16]\r\n            --   _activesprites_type = 0x51B[16]\r\n            --   _activesprites_realx= 0x54B[16]   (1 byte, screen-local draw x)\r\n            --   _activesprites_realy= 0x55B[16]   (1 byte, screen-local draw y)\r\n            --   _activesprites_active   = 0x56B[16]\r\n            --   _activesprites_activated= 0x57B[16]\r\n            for sl = 0, 15 do\r\n                local stype = rd8(0x51B + sl)\r\n                local active = rd8(0x56B + sl)\r\n                if active ~= 0 and stype ~= 0xFF then\r\n                    local x_lo = rd8(0x4DB + sl); local x_hi = rd8(0x4EB + sl)\r\n                    local y_lo = rd8(0x4FB + sl); local y_hi = rd8(0x50B + sl)\r\n                    local worldX = x_hi * 256 + x_lo\r\n                    local worldY = y_hi * 256 + y_lo\r\n                    local rx = rd8(0x54B + sl)\r\n                    local ry = rd8(0x55B + sl)\r\n                    local act_flag = rd8(0x57B + sl)\r\n                    -- Sprite-table geometry by type id.  These tables live\r\n                    -- in a swappable bank, but during normal play the bank\r\n                    -- is paged in for sprite_collide, so a best-effort read\r\n                    -- via emu.read often works.  If values look bogus\r\n                    -- (height==0) ignore them.\r\n                    local sw  = rd8(0xA811 + stype)\r\n                    local sh  = rd8(0xA711 + stype)\r\n                    local sxo = rd8(0xA911 + stype)\r\n                    local syo = rd8(0xAA11 + stype)\r\n                    if sxo >= 0x80 then sxo = sxo - 0x100 end\r\n                    if syo >= 0x80 then syo = syo - 0x100 end\r\n                    orbDbgFp:write(string.format(\r\n                        \"f=%d sim=%d SLOT %02d type=0x%02X worldX=%d worldY=%d \" ..\r\n                        \"realx=%d realy=%d active=%d activated=%d \" ..\r\n                        \"sw=%d sh=%d sxo=%d syo=%d\\n\",\r\n                        frameIdx, cursor, sl, stype, worldX, worldY,\r\n                        rx, ry, active, act_flag,\r\n                        sw, sh, sxo, syo))\r\n                end\r\n            end\r\n            orbDbgFp:flush()\r\n        end\r\n        frameIdx = frameIdx + 1\r\n    else\r\n        lastA = false\r\n    end\r\n\r\n    if SHOW_PATHLINES then\r\n    -- Live debug overlay (text removed; paths only)\r\n    local color = armed and 0x00FF66 or 0xFFAA00\r\n\r\n    -- Draw the predicted pathfinder path as a polyline directly on the NES\r\n    -- framebuffer. PF coords -> NES screen coords:\r\n    --   screenX = pf_x - scrollX\r\n    --   screenY = pf_y - scrollY + nesYOffset\r\n    -- (Both lines centered on the hitbox center, so no extra +/- 8 needed.)\r\n    -- We draw a window of points around the current cursor to keep things fast.\r\n    local function pf2sx(x) return x - scrollX end\r\n    local function pf2sy(y) return y - scrollY + nesYOffset end\r\n    local gamemodeOverlay = emu.read(0x007A, emu.memType.nesMemory) or 0\r\n    local isWaveMode = (gamemodeOverlay == 6)\r\n    local function drawPfSegment(ax, ay, bx, by, color, thickness)\r\n        emu.drawLine(ax, ay, bx, by, color)\r\n        local radius = math.floor((thickness - 1) / 2)\r\n        if radius <= 0 then return end\r\n        local dx = bx - ax\r\n        local dy = by - ay\r\n        for i = 1, radius do\r\n            if math.abs(dx) >= math.abs(dy) then\r\n                emu.drawLine(ax, ay - i, bx, by - i, color)\r\n                emu.drawLine(ax, ay + i, bx, by + i, color)\r\n            else\r\n                emu.drawLine(ax - i, ay, bx - i, by, color)\r\n                emu.drawLine(ax + i, ay, bx + i, by, color)\r\n            end\r\n        end\r\n    end\r\n    local function drawFilledCircle(cx, cy, radius, color)\r\n        for dy = -radius, radius do\r\n            local span = math.floor(math.sqrt(radius * radius - dy * dy) + 0.5)\r\n            emu.drawLine(cx - span, cy + dy, cx + span, cy + dy, color)\r\n        end\r\n    end\r\n    local first = math.max(1, cursor - 60)\r\n    local last  = math.min(#replay, cursor + 240)\r\n    local prev = replay[first]\r\n    if prev then\r\n        local pxA, pyA = pf2sx(prev.x), pf2sy(prev.y)\r\n        for i = first + 1, last do\r\n            local cur = replay[i]\r\n            local pxB, pyB = pf2sx(cur.x), pf2sy(cur.y)\r\n            -- Only draw segments where at least one endpoint is on screen.\r\n            if (pxA >= -8 and pxA <= 264 and pyA >= -8 and pyA <= 248)\r\n               or (pxB >= -8 and pxB <= 264 and pyB >= -8 and pyB <= 248) then\r\n                local segColor = (i <= cursor) and 0xFF44CC or 0x66CCFF\r\n                local held = ((prev.a or 0) ~= 0) or ((cur.a or 0) ~= 0)\r\n                local thickness = (held and (not isWaveMode)) and 3 or 1\r\n                drawPfSegment(pxA, pyA, pxB, pyB, segColor, thickness)\r\n                local pressedFresh = ((prev.a or 0) == 0) and ((cur.a or 0) ~= 0)\r\n                if (not isWaveMode) and pressedFresh then\r\n                    drawFilledCircle(pxB, pyB, 4, 0xFFA500) -- orange click marker\r\n                end\r\n            end\r\n            pxA, pyA = pxB, pyB\r\n            prev = cur\r\n        end\r\n        -- Highlight the current cursor entry.\r\n        local c = replay[cursor]\r\n        if c then\r\n            local cx, cy = pf2sx(c.x), pf2sy(c.y)\r\n            emu.drawRectangle(cx - 2, cy - 2, 5, 5, 0xFFFF00, true)\r\n        end\r\n    end\r\n\r\n    -- Draw projected P2 only while the ROM dual flag is live.  The exported\r\n    -- P2 table contains only dual-active points, with (-1,-1) sentinels\r\n    -- splitting segments after single portals.\r\n    local dualOverlay = emu.read(0x0096, emu.memType.nesMemory) or 0\r\n    if dualOverlay ~= 0 and #replay2 >= 2 then\r\n        local currentX = replay[cursor] and replay[cursor].x or px\r\n        local prev2 = nil\r\n        for i = 1, #replay2 do\r\n            local cur2 = replay2[i]\r\n            if not cur2 or cur2.x < 0 or cur2.y < 0 then\r\n                prev2 = nil\r\n            else\r\n                if prev2 then\r\n                    local nearWindow = (cur2.x >= currentX - 256 and cur2.x <= currentX + 768)\r\n                                    or (prev2.x >= currentX - 256 and prev2.x <= currentX + 768)\r\n                    if nearWindow then\r\n                        local ax2, ay2 = pf2sx(prev2.x), pf2sy(prev2.y)\r\n                        local bx2, by2 = pf2sx(cur2.x), pf2sy(cur2.y)\r\n                        if (ax2 >= -8 and ax2 <= 264 and ay2 >= -8 and ay2 <= 248)\r\n                           or (bx2 >= -8 and bx2 <= 264 and by2 >= -8 and by2 <= 248) then\r\n                            drawPfSegment(ax2, ay2, bx2, by2, 0x00FF99, 2)\r\n                        end\r\n                    end\r\n                end\r\n                prev2 = cur2\r\n            end\r\n        end\r\n    end\r\n\r\n    -- Draw the ACTUAL NES position trail in YELLOW (0xFFFF00) so divergence\r\n    -- from the PF projection (purple/cyan) is visible.  Walk the ring buffer\r\n    -- in chronological order from oldest to newest.\r\n    if nesTrailCount >= 2 then\r\n        local startIdx\r\n        if nesTrailCount < nesTrailMax then\r\n            startIdx = 1\r\n        else\r\n            startIdx = nesTrailHead  -- oldest entry slot (next to be overwritten)\r\n        end\r\n        local prevEntry = nesTrail[startIdx]\r\n        local idx = startIdx\r\n        for step = 2, nesTrailCount do\r\n            idx = idx + 1\r\n            if idx > nesTrailMax then idx = 1 end\r\n            local curEntry = nesTrail[idx]\r\n            if prevEntry and curEntry then\r\n                local axS, ayS = pf2sx(prevEntry.x), pf2sy(prevEntry.y)\r\n                local bxS, byS = pf2sx(curEntry.x), pf2sy(curEntry.y)\r\n                if (axS >= -8 and axS <= 264 and ayS >= -8 and ayS <= 248)\r\n                   or (bxS >= -8 and bxS <= 264 and byS >= -8 and byS <= 248) then\r\n                    local thickHeld = ((prevEntry.a or 0) ~= 0) or ((curEntry.a or 0) ~= 0)\r\n                    local thickReplay = ((prevEntry.ra or 0) ~= 0) or ((curEntry.ra or 0) ~= 0)\r\n                    local thickness = ((thickHeld or thickReplay) and (not isWaveMode)) and 3 or 1\r\n                    drawPfSegment(axS, ayS, bxS, byS, 0xFFFF00, thickness)\r\n                    local pressedHeldFresh = ((prevEntry.a or 0) == 0) and ((curEntry.a or 0) ~= 0)\r\n                    local pressedReplayFresh = ((prevEntry.ra or 0) == 0) and ((curEntry.ra or 0) ~= 0)\r\n                    if (not isWaveMode) and (pressedHeldFresh or pressedReplayFresh) then\r\n                        drawFilledCircle(bxS, byS, 4, 0xFFFF00)\r\n                    end\r\n                end\r\n            end\r\n            prevEntry = curEntry\r\n        end\r\n    end\r\n    if armed and replay[cursor] then\r\n        -- (debug text overlay removed)\r\n    end\r\n    end\r\nend, emu.eventType.endFrame)\r\n\r\n-- Counter so we can prove inputPolled is firing and our setInput was called.\r\nlocal pollCount = 0\r\nlocal pressCount = 0\r\nemu.addEventCallback(function()\r\n    pollCount = pollCount + 1\r\n    if not armed then return end\r\n    if lastA then pressCount = pressCount + 1 end\r\n    -- Mesen2 setInput signature: setInput(inputTable, port, subport)\r\n    -- (the public wiki incorrectly lists port first; the C++ source reads\r\n    -- the table from stack slot 1 with port/subport popped from the top.)\r\n    emu.setInput({{\r\n        a = lastA, b = false,\r\n        select = false, start = false,\r\n        up = false, down = false, left = false, right = false\r\n    }}, 0)\r\nend, emu.eventType.inputPolled)\r\n\r\nemu.addEventCallback(function()\r\n    -- (debug text overlay removed)\r\nend, emu.eventType.endFrame)\r\n";
		return text2 + text3;
	}

	private static List<SimRow> LoadSimRows(string path, out int yOffset)
	{
		yOffset = 0;
		List<SimRow> list = new List<SimRow>();
		string[] array = File.ReadAllLines(path);
		foreach (string text in array)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			if (text.StartsWith("nes_y_offset", StringComparison.Ordinal))
			{
				string[] array2 = text.Split(',');
				if (array2.Length >= 2 && int.TryParse(array2[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
				{
					yOffset = result;
				}
			}
			else if (!text.StartsWith("frame", StringComparison.Ordinal))
			{
				string[] array3 = text.Split(',');
				if (array3.Length >= 4 && int.TryParse(array3[0], out var result2) && int.TryParse(array3[1], out var result3) && int.TryParse(array3[2], out var result4) && int.TryParse(array3[3], out var result5))
				{
					list.Add(new SimRow
					{
						Frame = result2,
						X = result3,
						Y = result4,
						A = result5
					});
				}
			}
		}
		return list;
	}

	private static List<RomRow> LoadRomRows(string path)
	{
		List<RomRow> list = new List<RomRow>();
		int num = 0;
		string[] array = File.ReadAllLines(path);
		foreach (string text in array)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			if (text.StartsWith("#", StringComparison.Ordinal))
			{
				num++;
			}
			else
			{
				if (text.StartsWith("nes_y_offset", StringComparison.Ordinal) || text.StartsWith("rom_frame", StringComparison.Ordinal))
				{
					continue;
				}
				string[] array2 = text.Split(',');
				if (array2.Length >= 5 && int.TryParse(array2[0], out var result) && int.TryParse(array2[1], out var result2) && long.TryParse(array2[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result3))
				{
					int px = (int)result3;
					if (int.TryParse(array2[3], out var result4) && int.TryParse(array2[4], out var result5))
					{
						list.Add(new RomRow
						{
							Attempt = num,
							RomFrame = result,
							SimCursor = result2,
							Px = px,
							Py = result4,
							ANext = result5
						});
					}
				}
			}
		}
		return list;
	}

	private void PathfinderCompareButton_Click(object sender, RoutedEventArgs e)
	{
		RunTraceCompare();
	}

	private void RefreshReplayButtonVisibility()
	{
		try
		{
			PathfinderReplayButton.Visibility = ((!File.Exists(ReplayTempFile)) ? Visibility.Collapsed : Visibility.Visible);
		}
		catch
		{
		}
	}

	private void RunTraceCompare(bool silent = false)
	{
		try
		{
			if (!File.Exists(ReplayTempFile))
			{
				if (!silent)
				{
					StatusText.Text = "Compare: no sim replay CSV on disk.";
				}
				string value = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
				string tempPath = System.IO.Path.GetTempPath();
				string currentLevelTag = CurrentLevelTag;
				string text = System.IO.Path.Combine(tempPath, $"famidash_mesen_trace_{currentLevelTag}_{value}.csv");
				string text2 = System.IO.Path.Combine(tempPath, $"famidash_replay_{currentLevelTag}_{value}.csv");
				if (!silent)
				{
					StatusText.Text = "Compare: no Mesen trace on disk. Run \"Replay in Mesen\" first.";
				}
				return;
			}
			try
			{
				string value2 = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
				string tempPath2 = System.IO.Path.GetTempPath();
				string currentLevelTag2 = CurrentLevelTag;
				string destFileName = System.IO.Path.Combine(tempPath2, $"famidash_mesen_trace_{currentLevelTag2}_{value2}.csv");
				string destFileName2 = System.IO.Path.Combine(tempPath2, $"famidash_replay_{currentLevelTag2}_{value2}.csv");
				File.Copy(MesenTraceFile, destFileName, overwrite: true);
				File.Copy(ReplayTempFile, destFileName2, overwrite: true);
			}
			catch
			{
			}
			int yOffset;
			List<SimRow> sim = LoadSimRows(ReplayTempFile, out yOffset);
			List<RomRow> list = LoadRomRows(MesenTraceFile);
			if (sim.Count == 0 || list.Count == 0)
			{
				StatusText.Text = $"Compare: empty trace (sim={sim.Count}, rom={list.Count}).";
				return;
			}
			var list2 = (from r in list
				group r by r.Attempt into g
				select new
				{
					Attempt = g.Key,
					Rows = g.OrderBy((RomRow r) => r.RomFrame).ToList(),
					MaxCursor = g.Max((RomRow r) => r.SimCursor),
					ValidCount = g.Count((RomRow r) => r.SimCursor - 1 >= 0 && r.SimCursor - 1 < sim.Count)
				} into a
				where a.ValidCount > 0
				orderby a.MaxCursor descending, a.Attempt descending
				select a).ToList();
			if (list2.Count == 0)
			{
				StatusText.Text = "Compare: no valid ROM rows matched replay range.";
				return;
			}
			var anon = list2[0];
			List<RomRow> rows = anon.Rows;
			int num = -1;
			int value3 = -1;
			int value4 = 0;
			int value5 = 0;
			int value6 = 0;
			int value7 = 0;
			int value8 = -1;
			int num2 = -1;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			List<(RomRow, SimRow, int, int)> list3 = new List<(RomRow, SimRow, int, int)>();
			int groundRowsToReserve = ((groundBitmap != null && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("rom_f, sim_f, rom_xy, sim_xy, dx, dy, a_next, sim_a");
			StringBuilder stringBuilder2 = new StringBuilder();
			stringBuilder2.AppendLine("rom_f, sim_f, rom_xy, sim_xy, dx, dy, a_next, sim_a");
			StringBuilder stringBuilder3;
			StringBuilder.AppendInterpolatedStringHandler handler;
			foreach (RomRow item in rows)
			{
				if (item.SimCursor < 1)
				{
					continue;
				}
				int num7 = item.SimCursor - 1;
				if (num7 >= 0 && num7 < sim.Count)
				{
					SimRow simRow = sim[num7];
					int num8 = item.Px - simRow.X;
					int num9 = item.Py - simRow.Y;
					num3++;
					list3.Add((item, simRow, num8, num9));
					num4++;
					if (item.ANext == simRow.A)
					{
						num5++;
					}
					if (num7 + 1 < sim.Count && item.ANext == sim[num7 + 1].A)
					{
						num6++;
					}
					if (Math.Abs(num8) > Math.Abs(value6))
					{
						value6 = num8;
						value8 = item.RomFrame;
					}
					if (Math.Abs(num9) > Math.Abs(value7))
					{
						value7 = num9;
						num2 = item.RomFrame;
					}
					if (num < 0 && (Math.Abs(num8) > 1 || Math.Abs(num9) > 1))
					{
						num = item.RomFrame;
						value3 = simRow.Frame;
						value4 = num8;
						value5 = num9;
					}
					if (stringBuilder.Length < 8000)
					{
						stringBuilder3 = stringBuilder;
						StringBuilder stringBuilder4 = stringBuilder3;
						handler = new StringBuilder.AppendInterpolatedStringHandler(13, 10, stringBuilder3);
						handler.AppendFormatted(item.RomFrame);
						handler.AppendLiteral(",");
						handler.AppendFormatted(simRow.Frame);
						handler.AppendLiteral(",(");
						handler.AppendFormatted(item.Px);
						handler.AppendLiteral(",");
						handler.AppendFormatted(item.Py);
						handler.AppendLiteral("),(");
						handler.AppendFormatted(simRow.X);
						handler.AppendLiteral(",");
						handler.AppendFormatted(simRow.Y);
						handler.AppendLiteral("),");
						handler.AppendFormatted(num8);
						handler.AppendLiteral(",");
						handler.AppendFormatted(num9);
						handler.AppendLiteral(",");
						handler.AppendFormatted(item.ANext);
						handler.AppendLiteral(",");
						handler.AppendFormatted(simRow.A);
						stringBuilder4.AppendLine(ref handler);
					}
					stringBuilder3 = stringBuilder2;
					StringBuilder stringBuilder5 = stringBuilder3;
					handler = new StringBuilder.AppendInterpolatedStringHandler(13, 10, stringBuilder3);
					handler.AppendFormatted(item.RomFrame);
					handler.AppendLiteral(",");
					handler.AppendFormatted(simRow.Frame);
					handler.AppendLiteral(",(");
					handler.AppendFormatted(item.Px);
					handler.AppendLiteral(",");
					handler.AppendFormatted(item.Py);
					handler.AppendLiteral("),(");
					handler.AppendFormatted(simRow.X);
					handler.AppendLiteral(",");
					handler.AppendFormatted(simRow.Y);
					handler.AppendLiteral("),");
					handler.AppendFormatted(num8);
					handler.AppendLiteral(",");
					handler.AppendFormatted(num9);
					handler.AppendLiteral(",");
					handler.AppendFormatted(item.ANext);
					handler.AppendLiteral(",");
					handler.AppendFormatted(simRow.A);
					stringBuilder5.AppendLine(ref handler);
				}
			}
			int romFrame = rows[rows.Count - 1].RomFrame;
			int simCursor = rows[rows.Count - 1].SimCursor;
			bool flag = simCursor < sim.Count - 4;
			(RomRow, SimRow, int, int) tuple = (null, null, 0, 0);
			if (flag)
			{
				foreach (var item2 in list3)
				{
					if (item2.Item1.SimCursor >= simCursor - 1 && (Math.Abs(item2.Item3) > 1 || Math.Abs(item2.Item4) > 1))
					{
						tuple = (item2.Item1, item2.Item2, item2.Item3, item2.Item4);
						break;
					}
				}
			}
			(RomRow, SimRow, int, int) tuple2 = (null, null, 0, 0);
			foreach (var item3 in list3)
			{
				if (item3.Item1.RomFrame == num2)
				{
					tuple2 = (item3.Item1, item3.Item2, item3.Item3, item3.Item4);
					break;
				}
			}
			StringBuilder stringBuilder6 = new StringBuilder();
			stringBuilder6.AppendLine("=== Pathfinder vs Mesen trace comparison ===");
			stringBuilder3 = stringBuilder6;
			StringBuilder stringBuilder7 = stringBuilder3;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder3);
			handler.AppendLiteral("sim rows:   ");
			handler.AppendFormatted(sim.Count);
			stringBuilder7.AppendLine(ref handler);
			stringBuilder3 = stringBuilder6;
			StringBuilder stringBuilder8 = stringBuilder3;
			handler = new StringBuilder.AppendInterpolatedStringHandler(44, 3, stringBuilder3);
			handler.AppendLiteral("rom rows:   ");
			handler.AppendFormatted(rows.Count);
			handler.AppendLiteral("  (last rom_frame=");
			handler.AppendFormatted(romFrame);
			handler.AppendLiteral(", sim_cursor=");
			handler.AppendFormatted(simCursor);
			handler.AppendLiteral(")");
			stringBuilder8.AppendLine(ref handler);
			stringBuilder3 = stringBuilder6;
			StringBuilder stringBuilder9 = stringBuilder3;
			handler = new StringBuilder.AppendInterpolatedStringHandler(67, 4, stringBuilder3);
			handler.AppendLiteral("attempts:   ");
			handler.AppendFormatted(list2.Count);
			handler.AppendLiteral(" parsed; using attempt #");
			handler.AppendFormatted(anon.Attempt);
			handler.AppendLiteral(" (max sim_cursor=");
			handler.AppendFormatted(anon.MaxCursor);
			handler.AppendLiteral(", valid rows=");
			handler.AppendFormatted(anon.ValidCount);
			handler.AppendLiteral(")");
			stringBuilder9.AppendLine(ref handler);
			stringBuilder3 = stringBuilder6;
			StringBuilder stringBuilder10 = stringBuilder3;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder3);
			handler.AppendLiteral("compared:   ");
			handler.AppendFormatted(num3);
			stringBuilder10.AppendLine(ref handler);
			stringBuilder3 = stringBuilder6;
			StringBuilder stringBuilder11 = stringBuilder3;
			handler = new StringBuilder.AppendInterpolatedStringHandler(12, 1, stringBuilder3);
			handler.AppendLiteral("nes_y_off:  ");
			handler.AppendFormatted(yOffset);
			stringBuilder11.AppendLine(ref handler);
			if (num4 > 0)
			{
				double value9 = 100.0 * (double)num5 / (double)num4;
				double value10 = 100.0 * (double)num6 / (double)num4;
				stringBuilder3 = stringBuilder6;
				StringBuilder stringBuilder12 = stringBuilder3;
				handler = new StringBuilder.AppendInterpolatedStringHandler(34, 6, stringBuilder3);
				handler.AppendLiteral("A phase:    same=");
				handler.AppendFormatted(num5);
				handler.AppendLiteral("/");
				handler.AppendFormatted(num4);
				handler.AppendLiteral(" (");
				handler.AppendFormatted(value9, "F1");
				handler.AppendLiteral("%), next=");
				handler.AppendFormatted(num6);
				handler.AppendLiteral("/");
				handler.AppendFormatted(num4);
				handler.AppendLiteral(" (");
				handler.AppendFormatted(value10, "F1");
				handler.AppendLiteral("%)");
				stringBuilder12.AppendLine(ref handler);
			}
			stringBuilder6.AppendLine();
			if (num < 0)
			{
				stringBuilder6.AppendLine("No position divergence detected (within ±1 px tolerance).");
			}
			else
			{
				stringBuilder3 = stringBuilder6;
				StringBuilder stringBuilder13 = stringBuilder3;
				handler = new StringBuilder.AppendInterpolatedStringHandler(47, 4, stringBuilder3);
				handler.AppendLiteral("FIRST DIVERGE: rom_frame=");
				handler.AppendFormatted(num);
				handler.AppendLiteral(", sim_frame=");
				handler.AppendFormatted(value3);
				handler.AppendLiteral(", dx=");
				handler.AppendFormatted(value4);
				handler.AppendLiteral(", dy=");
				handler.AppendFormatted(value5);
				stringBuilder13.AppendLine(ref handler);
			}
			stringBuilder3 = stringBuilder6;
			StringBuilder stringBuilder14 = stringBuilder3;
			handler = new StringBuilder.AppendInterpolatedStringHandler(23, 2, stringBuilder3);
			handler.AppendLiteral("MAX |dx|=");
			handler.AppendFormatted(Math.Abs(value6));
			handler.AppendLiteral(" at rom_frame=");
			handler.AppendFormatted(value8);
			stringBuilder14.AppendLine(ref handler);
			stringBuilder3 = stringBuilder6;
			StringBuilder stringBuilder15 = stringBuilder3;
			handler = new StringBuilder.AppendInterpolatedStringHandler(23, 2, stringBuilder3);
			handler.AppendLiteral("MAX |dy|=");
			handler.AppendFormatted(Math.Abs(value7));
			handler.AppendLiteral(" at rom_frame=");
			handler.AppendFormatted(num2);
			stringBuilder15.AppendLine(ref handler);
			if (flag)
			{
				stringBuilder3 = stringBuilder6;
				StringBuilder stringBuilder16 = stringBuilder3;
				handler = new StringBuilder.AppendInterpolatedStringHandler(59, 2, stringBuilder3);
				handler.AppendLiteral("ROM ended early: stopped at sim_cursor ");
				handler.AppendFormatted(simCursor);
				handler.AppendLiteral(" of ");
				handler.AppendFormatted(sim.Count);
				handler.AppendLiteral(" (likely death).");
				stringBuilder16.AppendLine(ref handler);
			}
			if (tuple.Item1 != null && tuple.Item2 != null)
			{
				stringBuilder3 = stringBuilder6;
				StringBuilder stringBuilder17 = stringBuilder3;
				handler = new StringBuilder.AppendInterpolatedStringHandler(53, 4, stringBuilder3);
				handler.AppendLiteral("FIRST FATAL DIVERGE: rom_frame=");
				handler.AppendFormatted(tuple.Item1.RomFrame);
				handler.AppendLiteral(", sim_frame=");
				handler.AppendFormatted(tuple.Item2.Frame);
				handler.AppendLiteral(", dx=");
				handler.AppendFormatted(tuple.Item3);
				handler.AppendLiteral(", dy=");
				handler.AppendFormatted(tuple.Item4);
				stringBuilder17.AppendLine(ref handler);
				stringBuilder6.AppendLine("Fatal divergence collision samples:");
				stringBuilder6.AppendLine(DescribeCollisionAt("ROM", tuple.Item1.Px, tuple.Item1.Py));
				stringBuilder6.AppendLine(DescribeCollisionAt("SIM", tuple.Item2.X, tuple.Item2.Y));
				stringBuilder6.AppendLine();
			}
			if (tuple2.Item1 != null && tuple2.Item2 != null)
			{
				stringBuilder3 = stringBuilder6;
				StringBuilder stringBuilder18 = stringBuilder3;
				handler = new StringBuilder.AppendInterpolatedStringHandler(39, 1, stringBuilder3);
				handler.AppendLiteral("MAX-DY collision samples at rom_frame=");
				handler.AppendFormatted(tuple2.Item1.RomFrame);
				handler.AppendLiteral(":");
				stringBuilder18.AppendLine(ref handler);
				stringBuilder6.AppendLine(DescribeCollisionAt("ROM", tuple2.Item1.Px, tuple2.Item1.Py));
				stringBuilder6.AppendLine(DescribeCollisionAt("SIM", tuple2.Item2.X, tuple2.Item2.Y));
				stringBuilder6.AppendLine();
			}
			if (flag && list3.Count > 0)
			{
				stringBuilder6.AppendLine("ROM death scene (last 6 frames before ROM trace ended):");
				int num10 = Math.Max(0, list3.Count - 6);
				for (int num11 = num10; num11 < list3.Count; num11++)
				{
					(RomRow, SimRow, int, int) tuple3 = list3[num11];
					int px = tuple3.Item1.Px;
					int py = tuple3.Item1.Py;
					stringBuilder3 = stringBuilder6;
					StringBuilder stringBuilder19 = stringBuilder3;
					handler = new StringBuilder.AppendInterpolatedStringHandler(23, 4, stringBuilder3);
					handler.AppendLiteral("  rom_f=");
					handler.AppendFormatted(tuple3.Item1.RomFrame);
					handler.AppendLiteral(" sim_f=");
					handler.AppendFormatted(tuple3.Item2.Frame);
					handler.AppendLiteral(" pos=(");
					handler.AppendFormatted(px);
					handler.AppendLiteral(",");
					handler.AppendFormatted(py);
					handler.AppendLiteral(")");
					stringBuilder19.AppendLine(ref handler);
					stringBuilder6.AppendLine(DescribeCollisionAt("    TL ", px, py));
					stringBuilder6.AppendLine(DescribeCollisionAt("    TR ", px + 15, py));
					stringBuilder6.AppendLine(DescribeCollisionAt("    CTR", px + 7, py + 7));
					stringBuilder6.AppendLine(DescribeCollisionAt("    BL ", px, py + 15));
					stringBuilder6.AppendLine(DescribeCollisionAt("    BR ", px + 15, py + 15));
				}
				stringBuilder6.AppendLine();
				(RomRow, SimRow, int, int) tuple4 = list3[list3.Count - 1];
				int num12 = (tuple4.Item1.Px + 7) / 16;
				int value11 = (tuple4.Item1.Py + 7) / 16;
				stringBuilder3 = stringBuilder6;
				StringBuilder stringBuilder20 = stringBuilder3;
				handler = new StringBuilder.AppendInterpolatedStringHandler(85, 7, stringBuilder3);
				handler.AppendLiteral("Tile region around ROM death pos=(");
				handler.AppendFormatted(tuple4.Item1.Px);
				handler.AppendLiteral(",");
				handler.AppendFormatted(tuple4.Item1.Py);
				handler.AppendLiteral(") center_tile=(");
				handler.AppendFormatted(num12);
				handler.AppendLiteral(",");
				handler.AppendFormatted(value11);
				handler.AppendLiteral(") groundRowsToReserve=");
				handler.AppendFormatted(groundRowsToReserve);
				handler.AppendLiteral(" mapW=");
				handler.AppendFormatted(mapWidth);
				handler.AppendLiteral(" mapH=");
				handler.AppendFormatted(mapHeight);
				stringBuilder20.AppendLine(ref handler);
				stringBuilder6.Append("    tileX:");
				for (int num13 = -7; num13 <= 7; num13++)
				{
					stringBuilder3 = stringBuilder6;
					StringBuilder stringBuilder21 = stringBuilder3;
					handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, stringBuilder3);
					handler.AppendLiteral("  ");
					handler.AppendFormatted(num12 + num13, 4);
					stringBuilder21.Append(ref handler);
				}
				stringBuilder6.AppendLine();
				for (int num14 = 0; num14 < mapHeight; num14++)
				{
					int value12 = num14 - groundRowsToReserve;
					string value13 = $"y={value12,3}(aY={num14,2})";
					stringBuilder3 = stringBuilder6;
					StringBuilder stringBuilder22 = stringBuilder3;
					handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder3);
					handler.AppendLiteral("    ");
					handler.AppendFormatted(value13);
					handler.AppendLiteral(":");
					stringBuilder22.Append(ref handler);
					bool flag2 = false;
					for (int num15 = -7; num15 <= 7; num15++)
					{
						int num16 = num12 + num15;
						if (num16 < 0 || num16 >= mapWidth)
						{
							stringBuilder6.Append("    --");
							continue;
						}
						int num17 = num14 * mapWidth + num16;
						if (num17 < 0 || num17 >= tiles.Length)
						{
							stringBuilder6.Append("    !!");
							continue;
						}
						int num18 = tiles[num17];
						if (num18 < 0)
						{
							stringBuilder6.Append("    ..");
							continue;
						}
						stringBuilder3 = stringBuilder6;
						StringBuilder stringBuilder23 = stringBuilder3;
						handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder3);
						handler.AppendLiteral("  0x");
						handler.AppendFormatted(num18, "X2");
						stringBuilder23.Append(ref handler);
						flag2 = true;
					}
					stringBuilder6.AppendLine();
				}
				stringBuilder6.AppendLine();
				stringBuilder3 = stringBuilder6;
				StringBuilder stringBuilder24 = stringBuilder3;
				handler = new StringBuilder.AppendInterpolatedStringHandler(30, 2, stringBuilder3);
				handler.AppendLiteral("Non-empty tiles in columns ");
				handler.AppendFormatted(num12 - 2);
				handler.AppendLiteral("..");
				handler.AppendFormatted(num12 + 2);
				handler.AppendLiteral(":");
				stringBuilder24.AppendLine(ref handler);
				for (int num19 = num12 - 2; num19 <= num12 + 2; num19++)
				{
					if (num19 < 0 || num19 >= mapWidth)
					{
						continue;
					}
					for (int num20 = 0; num20 < mapHeight; num20++)
					{
						int num21 = num20 * mapWidth + num19;
						if (num21 >= 0 && num21 < tiles.Length)
						{
							int num22 = tiles[num21];
							if (num22 >= 0)
							{
								int num23 = SharedPhysics.MapTileForCollision(num22);
								MetatileCollision collision = MetatileCollisionTable.GetCollision((byte)num23);
								int value14 = num20 - groundRowsToReserve;
								stringBuilder3 = stringBuilder6;
								StringBuilder stringBuilder25 = stringBuilder3;
								handler = new StringBuilder.AppendInterpolatedStringHandler(38, 6, stringBuilder3);
								handler.AppendLiteral("    tile=(");
								handler.AppendFormatted(num19);
								handler.AppendLiteral(",");
								handler.AppendFormatted(value14);
								handler.AppendLiteral(") aY=");
								handler.AppendFormatted(num20);
								handler.AppendLiteral(" tid=0x");
								handler.AppendFormatted(num22, "X2");
								handler.AppendLiteral(" mapped=0x");
								handler.AppendFormatted(num23, "X2");
								handler.AppendLiteral(" col=");
								handler.AppendFormatted(collision);
								stringBuilder25.AppendLine(ref handler);
							}
						}
					}
				}
				stringBuilder6.AppendLine();
			}
			stringBuilder6.AppendLine();
			stringBuilder6.AppendLine("First rows of per-frame comparison (rom_f,sim_f,rom_xy,sim_xy,dx,dy,a_next,sim_a):");
			stringBuilder6.Append(stringBuilder);
			StringBuilder stringBuilder26 = new StringBuilder();
			stringBuilder26.Append(stringBuilder6);
			stringBuilder26.AppendLine();
			stringBuilder26.AppendLine("Full per-frame comparison (rom_f,sim_f,rom_xy,sim_xy,dx,dy,a_next,sim_a):");
			stringBuilder26.Append(stringBuilder2);
			string text3 = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string text4 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "famidash_trace_compare_" + text3 + ".txt");
			try
			{
				File.WriteAllText(text4, stringBuilder26.ToString());
			}
			catch
			{
			}
			string text5 = ((num >= 0) ? $"Compare: DIVERGE at rom_frame {num} (sim {value3}) dx={value4} dy={value5}. Report -> {text4}" : $"Compare: no divergence ({num3} frames). Report -> {text4}");
			StatusText.Text = text5;
			if (!silent)
			{
				System.Windows.MessageBox.Show(stringBuilder6.ToString(), "Trace comparison", MessageBoxButton.OK, (num < 0) ? MessageBoxImage.Asterisk : MessageBoxImage.Exclamation);
			}
			string DescribeCollisionAt(string who, int num25, int num27)
			{
				int num24 = num25 / 16;
				int num26 = num27 / 16;
				int num28 = num26 + groundRowsToReserve;
				int num29 = (num25 % 16 + 16) % 16;
				int num30 = (num27 % 16 + 16) % 16;
				if (num24 >= 0 && num24 < mapWidth)
				{
					if (num28 >= 0)
					{
						if (num28 < mapHeight)
						{
							int num31 = num28 * mapWidth + num24;
							if (num31 >= 0 && num31 < tiles.Length)
							{
								int num32 = tiles[num31];
								int num33 = SharedPhysics.MapTileForCollision(num32);
								MetatileCollision collision2 = MetatileCollisionTable.GetCollision((byte)num33);
								bool value15 = MetatileCollisionTable.TileKillsAtPixel(collision2, num29, num30);
								return $"  {who}: p=({num25},{num27}) tile=({num24},{num26}) arrayY={num28} tid=0x{num32:X2} mapped=0x{num33:X2} col={collision2} local=({num29},{num30}) kills={value15}";
							}
							return $"  {who}: p=({num25},{num27}) tile=({num24},{num26}) arrayY={num28} bad_index={num31}";
						}
						return $"  {who}: p=({num25},{num27}) tile=({num24},{num26}) arrayY={num28} implicit_ground col={1} local=({num29},{num30})";
					}
					return $"  {who}: p=({num25},{num27}) tile=({num24},{num26}) arrayY={num28} above_map";
				}
				return $"  {who}: p=({num25},{num27}) tile=({num24},{num26}) out_of_bounds_x";
			}
		}
		catch (Exception ex)
		{
			StatusText.Text = "Compare error: " + ex.Message;
		}
	}

	private void TileEyeButton_Checked(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (TilesImage != null)
			{
				TilesImage.Visibility = Visibility.Visible;
			}
			if (PortalsImage != null)
			{
				PortalsImage.Visibility = Visibility.Visible;
			}
			if (TileEyeButton != null)
			{
				TileEyeButton.Content = "??";
			}
		}
		catch
		{
		}
	}

	private void FillToolMore_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (sender is System.Windows.Controls.Button { ContextMenu: not null } button)
			{
				button.ContextMenu.PlacementTarget = button;
				button.ContextMenu.Placement = PlacementMode.Bottom;
				button.ContextMenu.IsOpen = true;
			}
		}
		catch
		{
		}
	}

	private void SelectToolMore_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (sender is System.Windows.Controls.Button { ContextMenu: not null } button)
			{
				button.ContextMenu.PlacementTarget = button;
				button.ContextMenu.Placement = PlacementMode.Bottom;
				button.ContextMenu.IsOpen = true;
			}
		}
		catch
		{
		}
	}

	private async void RenderIndicesToOverlayAsync(int[] indices, double scale, DpiScale dpi)
	{
		if (SelectSameOverlay == null)
		{
			return;
		}
		int tilePixelW = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
		int tilePixelH = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
		int padPxX = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
		int padPxY = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
		int gShiftYpx = gridRenderShiftYPx;
		long totalPixels = (long)mapWidth * (long)mapHeight;
		if (indices.Length >= 2000 && totalPixels <= 2000000)
		{
			try
			{
				await RenderMaskToCanvasAsync(SelectSameOverlay, indices, scale, dpi);
				return;
			}
			catch
			{
			}
		}
		Rect[] rects = await Task.Run(delegate
		{
			List<Rect> list = new List<Rect>(indices.Length);
			foreach (int num in indices)
			{
				int num2 = num % mapWidth;
				int num3 = num / mapWidth;
				double x = (double)(padPxX + num2 * tilePixelW) / dpi.DpiScaleX;
				double y = (double)(padPxY + num3 * tilePixelH) / dpi.DpiScaleY + (double)gShiftYpx / dpi.DpiScaleY;
				double width = (double)tilePixelW / dpi.DpiScaleX;
				double height = (double)tilePixelH / dpi.DpiScaleY;
				list.Add(new Rect(x, y, width, height));
			}
			return list.ToArray();
		});
		try
		{
			await base.Dispatcher.InvokeAsync(delegate
			{
				try
				{
					SelectSameOverlay.Children.Clear();
					DrawingVisual drawingVisual = new DrawingVisual();
					using (DrawingContext drawingContext = drawingVisual.RenderOpen())
					{
						SolidColorBrush selectSameFillBrush = SelectSameFillBrush;
						Brush selectSameStrokeBrush = SelectSameStrokeBrush;
						Pen pen = new Pen(selectSameStrokeBrush, 1.0 / dpi.DpiScaleX);
						Rect[] array = rects;
						foreach (Rect rectangle in array)
						{
							drawingContext.DrawRectangle(selectSameFillBrush, pen, rectangle);
						}
					}
					int pixelWidth = Math.Max(1, (int)Math.Round(((double)(mapWidth * 16) * scale + mapViewportPadding * 2.0) * dpi.DpiScaleX));
					int pixelHeight = Math.Max(1, (int)Math.Round(((double)(mapHeight * 16) * scale + mapViewportPadding * 2.0) * dpi.DpiScaleY));
					RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
					renderTargetBitmap.Render(drawingVisual);
					Image element = new Image
					{
						Source = renderTargetBitmap,
						IsHitTestVisible = false
					};
					SelectSameOverlay.Children.Add(element);
					Canvas.SetLeft(element, 0.0);
					Canvas.SetTop(element, 0.0);
				}
				catch
				{
				}
			}, DispatcherPriority.Background);
		}
		catch
		{
		}
	}

	private async Task RenderMaskToCanvasAsync(Canvas targetCanvas, int[] indices, double scale, DpiScale dpi)
	{
		if (targetCanvas == null)
		{
			return;
		}
		int w = mapWidth;
		int h = mapHeight;
		int stride = w * 4;
		byte[] pixels = new byte[stride * h];
		Color fillColor = Colors.LimeGreen;
		try
		{
			SolidColorBrush scb = SelectSameFillBrush;
			if (scb != null)
			{
				fillColor = scb.Color;
			}
		}
		catch
		{
		}
		byte lightenFactor = 32;
		byte a = fillColor.A;
		byte r = (byte)Math.Min(255, fillColor.R + lightenFactor);
		byte g = (byte)Math.Min(255, fillColor.G + lightenFactor);
		byte b = (byte)Math.Min(255, fillColor.B + lightenFactor);
		byte pr = (byte)(r * a / 255);
		byte pg = (byte)(g * a / 255);
		byte pb = (byte)(b * a / 255);
		foreach (int idx in indices)
		{
			if (idx >= 0 && idx < w * h)
			{
				int px = idx % w;
				int py = idx / w;
				int off = py * stride + px * 4;
				pixels[off] = pb;
				pixels[off + 1] = pg;
				pixels[off + 2] = pr;
				pixels[off + 3] = a;
			}
		}
		await base.Dispatcher.InvokeAsync(delegate
		{
			try
			{
				targetCanvas.Children.Clear();
				WriteableBitmap writeableBitmap = new WriteableBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
				writeableBitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
				Image image = new Image
				{
					Source = writeableBitmap,
					IsHitTestVisible = false
				};
				double width = (double)w * 16.0 * scale;
				double height = (double)h * 16.0 * scale;
				image.Width = width;
				image.Height = height;
				Canvas.SetLeft(image, mapViewportPadding);
				Canvas.SetTop(image, mapViewportPadding + (double)gridRenderShiftYPx / dpi.DpiScaleY);
				RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
				targetCanvas.Children.Add(image);
			}
			catch
			{
			}
		}, DispatcherPriority.Background);
	}

	private void ClearSelectSameOverlay()
	{
		try
		{
			if (SelectSameOverlay != null)
			{
				SelectSameOverlay.Children.Clear();
			}
		}
		catch
		{
		}
	}

	private void UpdateSelectSameOverlay(bool useTiles, int id)
	{
		try
		{
			ClearSelectSameOverlay();
			if (id >= 0)
			{
				IndexInfo value = null;
				if (useTiles)
				{
					tileIndexMap.TryGetValue(id, out value);
				}
				else
				{
					spriteIndexMap.TryGetValue(id, out value);
				}
				if (value != null && value.Indices != null && value.Indices.Length != 0)
				{
					DpiScale dpi = VisualTreeHelper.GetDpi(this);
					double scale = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
					RenderIndicesToOverlayAsync(value.Indices, scale, dpi);
				}
			}
		}
		catch
		{
		}
	}

	private async void RenderIndicesToSelectionOverlayAsync(int[]? indices, double scale, DpiScale dpi)
	{
		if (indices == null || indices.Length == 0)
		{
			return;
		}
		try
		{
			try
			{
				await RenderMaskToCanvasAsync(SelectionOverlay, indices, scale, dpi);
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void TileEyeButton_Unchecked(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (TilesImage != null)
			{
				TilesImage.Visibility = Visibility.Collapsed;
			}
			if (PortalsImage != null)
			{
				PortalsImage.Visibility = Visibility.Collapsed;
			}
			if (TileEyeButton != null)
			{
				TileEyeButton.Content = "??";
			}
		}
		catch
		{
		}
	}

	private void PerformSelectAllSameAtCursor()
	{
		try
		{
			int num = ((lastHoverX >= 0) ? lastHoverX : ((lastClickX >= 0) ? lastClickX : (-1)));
			int num2 = ((lastHoverY >= 0) ? lastHoverY : ((lastClickY >= 0) ? lastClickY : (-1)));
			if (num < 0 || num2 < 0)
			{
				return;
			}
			int num3 = num2 * mapWidth + num;
			bool flag = tilesLayerActive || (!tilesLayerActive && !spritesLayerActive);
			bool flag2 = spritesLayerActive || (!tilesLayerActive && !spritesLayerActive);
			bool flag3 = flag && tiles[num3] != -1;
			bool flag4 = !flag3 && flag2 && sprites[num3] != -1;
			int num4 = -1;
			if (flag3)
			{
				num4 = tiles[num3];
			}
			else if (flag4)
			{
				num4 = sprites[num3];
			}
			if (num4 == -1)
			{
				return;
			}
			IndexInfo value = null;
			if (flag3)
			{
				tileIndexMap.TryGetValue(num4, out value);
			}
			else
			{
				spriteIndexMap.TryGetValue(num4, out value);
			}
			if (value == null || value.Count == 0)
			{
				return;
			}
			selectionSet = new HashSet<int>(value.Indices);
			selectionIsLarge = selectionSet.Count > 5000;
			int num5 = int.MaxValue;
			int num6 = int.MaxValue;
			int num7 = int.MinValue;
			int num8 = int.MinValue;
			foreach (int item in selectionSet)
			{
				int num9 = item % mapWidth;
				int num10 = item / mapWidth;
				if (num9 < num5)
				{
					num5 = num9;
				}
				if (num10 < num6)
				{
					num6 = num10;
				}
				if (num9 > num7)
				{
					num7 = num9;
				}
				if (num10 > num8)
				{
					num8 = num10;
				}
			}
			selX = num5;
			selY = num6;
			selW = num7 - num5 + 1;
			selH = num8 - num6 + 1;
			PopulateSelectionArraysFromSet();
			UpdateSelectionVisuals(selX, selY, selW, selH);
			try
			{
				DpiScale dpi = VisualTreeHelper.GetDpi(this);
				double scale = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
				RenderMaskToCanvasAsync(SelectionOverlay, value.Indices, scale, dpi);
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void PerformReplaceSelected()
	{
		try
		{
			if (selectionSet == null || selectionSet.Count == 0)
			{
				return;
			}
			TileChangeAction tileChangeAction = new TileChangeAction();
			SpriteChangeAction spriteChangeAction = new SpriteChangeAction();
			foreach (int item in selectionSet)
			{
				if (tilesLayerActive && selectedTile >= 0)
				{
					int num = tiles[item];
					if (num != selectedTile)
					{
						tileChangeAction.Add(item, num, selectedTile);
						tiles[item] = selectedTile;
					}
				}
				if (spritesLayerActive && selectedSprite >= 0)
				{
					int num2 = sprites[item];
					if (num2 != selectedSprite)
					{
						spriteChangeAction.Add(item, num2, selectedSprite);
						sprites[item] = selectedSprite;
					}
				}
			}
			if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
			{
				undoStack.Push(tileChangeAction);
				redoStack.Clear();
				SetHasUnsavedChanges(unsaved: true);
			}
			bool flag = false;
			bool flag2 = false;
			if (!spriteChangeAction.IsEmpty() && !suppressUndoRecording)
			{
				undoStack.Push(spriteChangeAction);
				redoStack.Clear();
				SetHasUnsavedChanges(unsaved: true);
				flag2 = true;
			}
			if (!tileChangeAction.IsEmpty())
			{
				flag = true;
			}
			try
			{
				if (flag)
				{
					RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
			}
			catch
			{
			}
			try
			{
				if (flag2)
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
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

	private void SpriteEyeButton_Checked(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (SpritesImage != null)
			{
				SpritesImage.Visibility = Visibility.Visible;
			}
			if (SpriteEyeButton != null)
			{
				SpriteEyeButton.Content = "??";
			}
		}
		catch
		{
		}
	}

	private void SpriteEyeButton_Unchecked(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (SpritesImage != null)
			{
				SpritesImage.Visibility = Visibility.Collapsed;
			}
			if (SpriteEyeButton != null)
			{
				SpriteEyeButton.Content = "??";
			}
		}
		catch
		{
		}
	}

	private void MenuOptionHideTriggerSprites_Checked(object? sender, RoutedEventArgs e)
	{
		try
		{
			SetHideTriggerSprites(enabled: true);
		}
		catch
		{
		}
	}

	private void MenuOptionHideTriggerSprites_Unchecked(object? sender, RoutedEventArgs e)
	{
		try
		{
			SetHideTriggerSprites(enabled: false);
		}
		catch
		{
		}
	}

	private void MenuOptionShowSpriteHitboxes_Checked(object? sender, RoutedEventArgs e)
	{
		try
		{
			Option_ShowSimulatorSpriteHitboxes = true;
			foreach (Window window in System.Windows.Application.Current.Windows)
			{
				try
				{
					if (window is SimulatorWindow simulatorWindow)
					{
						simulatorWindow.ShowSpriteHitboxes = true;
					}
				}
				catch
				{
				}
			}
			try
			{
				SaveEditorSettings();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void MenuOptionShowSpriteHitboxes_Unchecked(object? sender, RoutedEventArgs e)
	{
		try
		{
			Option_ShowSimulatorSpriteHitboxes = false;
			foreach (Window window in System.Windows.Application.Current.Windows)
			{
				try
				{
					if (window is SimulatorWindow simulatorWindow)
					{
						simulatorWindow.ShowSpriteHitboxes = false;
					}
				}
				catch
				{
				}
			}
			try
			{
				SaveEditorSettings();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void MenuOptionTileHitboxes_Checked(object? sender, RoutedEventArgs e)
	{
		try
		{
			Option_ShowTileHitboxes = true;
			foreach (Window window in System.Windows.Application.Current.Windows)
			{
				try
				{
					if (window is SimulatorWindow simulatorWindow)
					{
						simulatorWindow.ShowTileHitboxes = true;
					}
				}
				catch
				{
				}
			}
			try
			{
				SaveEditorSettings();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void MenuOptionTileHitboxes_Unchecked(object? sender, RoutedEventArgs e)
	{
		try
		{
			Option_ShowTileHitboxes = false;
			foreach (Window window in System.Windows.Application.Current.Windows)
			{
				try
				{
					if (window is SimulatorWindow simulatorWindow)
					{
						simulatorWindow.ShowTileHitboxes = false;
					}
				}
				catch
				{
				}
			}
			try
			{
				SaveEditorSettings();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void MenuOptionClearPlayerPath_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			ClearPlayerPathOverlay();
			ShowTransientInfo("Player path cleared", this, 900);
		}
		catch
		{
		}
	}

	private void MenuOptionShowAccurateTileset_Checked(object? sender, RoutedEventArgs e)
	{
		try
		{
			SetShowAccurateTileset(enabled: true);
		}
		catch
		{
		}
	}

	private void MenuOptionShowAccurateTileset_Unchecked(object? sender, RoutedEventArgs e)
	{
		try
		{
			SetShowAccurateTileset(enabled: false);
		}
		catch
		{
		}
	}

	protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
	{
		try
		{
			if (e.Key == Key.F2 && !e.IsRepeat)
			{
				try
				{
					bool flag = (Option_ShowTileHitboxes = !Option_ShowTileHitboxes);
					try
					{
						if (MenuOptionTileHitboxes != null)
						{
							MenuOptionTileHitboxes.IsChecked = flag;
						}
					}
					catch
					{
					}
					foreach (Window window3 in System.Windows.Application.Current.Windows)
					{
						try
						{
							if (window3 is SimulatorWindow simulatorWindow)
							{
								simulatorWindow.ShowTileHitboxes = flag;
							}
						}
						catch
						{
						}
					}
					try
					{
						SaveEditorSettings();
					}
					catch
					{
					}
					ShowTransientInfo("Show Tile Hitboxes: " + (flag ? "ON" : "OFF"), this);
					e.Handled = true;
					return;
				}
				catch
				{
				}
			}
			if (e.Key == Key.F3 && !e.IsRepeat)
			{
				try
				{
					bool flag2 = (Option_ShowSimulatorSpriteHitboxes = !Option_ShowSimulatorSpriteHitboxes);
					try
					{
						if (MenuOptionShowSpriteHitboxes != null)
						{
							MenuOptionShowSpriteHitboxes.IsChecked = flag2;
						}
					}
					catch
					{
					}
					foreach (Window window4 in System.Windows.Application.Current.Windows)
					{
						try
						{
							if (window4 is SimulatorWindow simulatorWindow2)
							{
								simulatorWindow2.ShowSpriteHitboxes = flag2;
							}
						}
						catch
						{
						}
					}
					try
					{
						SaveEditorSettings();
					}
					catch
					{
					}
					ShowTransientInfo("Show Sprite Hitboxes: " + (flag2 ? "ON" : "OFF"), this);
					e.Handled = true;
					return;
				}
				catch
				{
				}
			}
			if (e.Key == Key.F6 && !e.IsRepeat)
			{
				try
				{
					CalculatePathButton_Click(CalculatePathButton, new RoutedEventArgs());
					e.Handled = true;
					return;
				}
				catch
				{
				}
			}
			if (e.Key == Key.F9 && !e.IsRepeat)
			{
				try
				{
					if (PathfinderReplayButton != null && PathfinderReplayButton.IsEnabled)
					{
						PathfinderReplayButton_Click(PathfinderReplayButton, new RoutedEventArgs());
						e.Handled = true;
						return;
					}
				}
				catch
				{
				}
			}
			if (e.Key == Key.F10)
			{
				try
				{
					bool flag3 = (Option_CamMode = !Option_CamMode);
					try
					{
						if (MenuOptionCamMode != null)
						{
							MenuOptionCamMode.IsChecked = flag3;
						}
					}
					catch
					{
					}
					try
					{
						SaveEditorSettings();
					}
					catch
					{
					}
					ShowTransientInfo("Cam Mode: " + (flag3 ? "ON" : "OFF"), this, 1200);
					e.Handled = true;
					return;
				}
				catch
				{
				}
			}
			if (e.Key == Key.F11 && !e.IsRepeat)
			{
				try
				{
					bool flag4 = (Option_NoDeath = !Option_NoDeath);
					try
					{
						if (MenuOptionNoDeath != null)
						{
							MenuOptionNoDeath.IsChecked = flag4;
						}
					}
					catch
					{
					}
					try
					{
						SaveEditorSettings();
					}
					catch
					{
					}
					ShowTransientInfo("No Death: " + (flag4 ? "ON" : "OFF"), this, 1200);
					e.Handled = true;
					return;
				}
				catch
				{
				}
			}
			if (e.Key == Key.F12 && !e.IsRepeat)
			{
				try
				{
					if (!_attemptedPathsCleared && attemptedPaths.Count > 0)
					{
						ClearAttemptedPathOverlay();
						_attemptedPathsCleared = true;
						ShowTransientInfo("Attempted paths cleared (press F12 again for final path)", this, 1200);
					}
					else
					{
						MenuOptionClearPlayerPath_Click(this, e);
					}
					e.Handled = true;
					return;
				}
				catch
				{
				}
			}
			try
			{
				if (!e.IsRepeat && e.Key == Key.P)
				{
					e.Handled = true;
					try
					{
						if (FindName("StartPosTool") is ToggleButton toggleButton)
						{
							toggleButton.IsChecked = true;
							try
							{
								Tool_Checked(toggleButton, new RoutedEventArgs());
							}
							catch
							{
							}
							ShowTransientInfo("START POS tool active - click on map to place marker", this, 2000);
						}
						return;
					}
					catch
					{
						return;
					}
				}
				bool flag5 = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
				if (!e.IsRepeat && flag5 && e.Key == Key.P)
				{
					e.Handled = true;
					SetStartPosMarker(null, null);
					ShowTransientInfo("START POS cleared", this);
					return;
				}
			}
			catch
			{
			}
			try
			{
				if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && e.Key == Key.A)
				{
					e.Handled = true;
					bool flag6 = tilesLayerActive || (!tilesLayerActive && !spritesLayerActive);
					bool flag7 = spritesLayerActive || (!tilesLayerActive && !spritesLayerActive);
					List<int> list = new List<int>();
					int num = mapWidth * mapHeight;
					if (flag6)
					{
						for (int i = 0; i < num; i++)
						{
							if (tiles[i] != -1)
							{
								list.Add(i);
							}
						}
					}
					if (flag7)
					{
						for (int j = 0; j < num; j++)
						{
							if (sprites[j] != -1)
							{
								list.Add(j);
							}
						}
					}
					int[] array = list.Distinct().ToArray();
					if (array.Length == 0)
					{
						ClearSelection();
						return;
					}
					selectionSet = new HashSet<int>(array);
					selectionIsLarge = selectionSet.Count > 5000;
					int num2 = int.MaxValue;
					int num3 = int.MaxValue;
					int num4 = int.MinValue;
					int num5 = int.MinValue;
					foreach (int item in selectionSet)
					{
						int num6 = item % mapWidth;
						int num7 = item / mapWidth;
						if (num6 < num2)
						{
							num2 = num6;
						}
						if (num7 < num3)
						{
							num3 = num7;
						}
						if (num6 > num4)
						{
							num4 = num6;
						}
						if (num7 > num5)
						{
							num5 = num7;
						}
					}
					selX = num2;
					selY = num3;
					selW = num4 - num2 + 1;
					selH = num5 - num3 + 1;
					try
					{
						PopulateSelectionArraysFromSet();
					}
					catch
					{
					}
					UpdateSelectionVisuals(selX, selY, selW, selH);
					try
					{
						DpiScale dpi = VisualTreeHelper.GetDpi(this);
						double scale = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
						RenderMaskToCanvasAsync(SelectionOverlay, array, scale, dpi);
					}
					catch
					{
					}
					try
					{
						PopulateSelectionArraysFromSet();
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
		try
		{
			bool flag8 = WidthBox != null && WidthBox.IsFocused;
			bool flag9 = HeightBox != null && HeightBox.IsFocused;
			bool flag10 = flag8 || flag9;
			if (!flag10 && !e.IsRepeat && (e.Key == Key.D1 || e.Key == Key.NumPad1))
			{
				e.Handled = true;
				tilesLayerActive = true;
				spritesLayerActive = false;
				if (selectedTile < 0)
				{
					selectedTile = 0;
				}
				selectedSprite = -1;
				UpdatePaletteHighlight();
				if (StatusText != null)
				{
					StatusText.Text = "Switched to TILES layer";
				}
				return;
			}
			if (!flag10 && !e.IsRepeat && (e.Key == Key.D2 || e.Key == Key.NumPad2))
			{
				e.Handled = true;
				tilesLayerActive = false;
				spritesLayerActive = true;
				if (selectedSprite < 0)
				{
					selectedSprite = 0;
				}
				selectedTile = -1;
				UpdatePaletteHighlight();
				if (StatusText != null)
				{
					StatusText.Text = "Switched to SPRITES layer";
				}
				return;
			}
			if (!flag10 && !e.IsRepeat && (e.Key == Key.D3 || e.Key == Key.NumPad3))
			{
				e.Handled = true;
				tilesLayerActive = true;
				spritesLayerActive = true;
				UpdatePaletteHighlight();
				if (StatusText != null)
				{
					StatusText.Text = "Switched to BOTH layers";
				}
				return;
			}
		}
		catch
		{
		}
		base.OnKeyDown(e);
		try
		{
			if (e.Key == Key.L && !e.IsRepeat)
			{
				e.Handled = true;
				currentSelectMode = SelectMode.Lasso;
				if (FindName("LassoTool") is ToggleButton toggleButton2)
				{
					toggleButton2.IsChecked = true;
					try
					{
						Tool_Checked(toggleButton2, new RoutedEventArgs());
					}
					catch
					{
					}
				}
				else if (SelectTool != null)
				{
					SelectTool.IsChecked = true;
					try
					{
						Tool_Checked(SelectTool, new RoutedEventArgs());
					}
					catch
					{
					}
				}
				try
				{
					if (MenuToolLasso != null)
					{
						MenuToolLasso.IsChecked = true;
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
			PopulateSelectionArraysFromSet();
		}
		catch
		{
		}
	}

	public bool GetHideTriggerSprites()
	{
		try
		{
			return hideTriggerSprites;
		}
		catch
		{
			return true;
		}
	}

	public void SetHideTriggerSprites(bool enabled)
	{
		try
		{
			hideTriggerSprites = enabled;
			try
			{
				SaveEditorSettings();
			}
			catch
			{
			}
			try
			{
				foreach (Window window in System.Windows.Application.Current.Windows)
				{
					try
					{
						if (window is SimulatorWindow simulatorWindow)
						{
							simulatorWindow.SetHideTriggerSprites(enabled);
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
		catch
		{
		}
	}

	public static void ShowTransientInfo(string text, Window? owner = null, int ms = 1500)
	{
		try
		{
			System.Windows.Application.Current?.Dispatcher?.BeginInvoke((Action)delegate
			{
				try
				{
					Window popup = new Window
					{
						WindowStyle = WindowStyle.None,
						AllowsTransparency = true,
						Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)),
						Width = 260.0,
						Height = 56.0,
						ShowInTaskbar = false,
						Topmost = true,
						ResizeMode = ResizeMode.NoResize,
						Owner = owner,
						Content = new TextBlock
						{
							Text = text,
							Foreground = Brushes.White,
							FontSize = 14.0,
							HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
							VerticalAlignment = VerticalAlignment.Center,
							TextAlignment = TextAlignment.Center,
							Margin = new Thickness(8.0)
						}
					};
					if (owner != null)
					{
						popup.WindowStartupLocation = WindowStartupLocation.CenterOwner;
					}
					else
					{
						popup.WindowStartupLocation = WindowStartupLocation.CenterScreen;
					}
					popup.Show();
					DispatcherTimer t = new DispatcherTimer
					{
						Interval = TimeSpan.FromMilliseconds(ms)
					};
					t.Tick += delegate
					{
						try
						{
							t.Stop();
							popup.Close();
						}
						catch
						{
						}
					};
					t.Start();
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

	private void SetSimulatorSizeFromMenu(int size)
	{
		try
		{
			if (size < 1)
			{
				size = 1;
			}
			if (size > 4)
			{
				size = 4;
			}
			loadedSimulatorScale = size;
			try
			{
				if (MenuSimulatorSize1x != null)
				{
					MenuSimulatorSize1x.IsChecked = size == 1;
				}
			}
			catch
			{
			}
			try
			{
				if (MenuSimulatorSize2x != null)
				{
					MenuSimulatorSize2x.IsChecked = size == 2;
				}
			}
			catch
			{
			}
			try
			{
				if (MenuSimulatorSize3x != null)
				{
					MenuSimulatorSize3x.IsChecked = size == 3;
				}
			}
			catch
			{
			}
			try
			{
				if (MenuSimulatorSize4x != null)
				{
					MenuSimulatorSize4x.IsChecked = size == 4;
				}
			}
			catch
			{
			}
			try
			{
				SaveSettingsWithTriggerOption();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	public (int? startingBackground, int? startingGround, int? startingGameMode, int startingSpeedUiIndex, int? startingDifficulty, int? startingStars) GetLoadedStartingValues()
	{
		try
		{
			if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
			{
				FileTabData fileTabData = openFiles[currentFileIndex];
				return (startingBackground: fileTabData.LoadedStartingBackgroundColor, startingGround: fileTabData.LoadedStartingGroundColor, startingGameMode: fileTabData.LoadedStartingGameMode, startingSpeedUiIndex: fileTabData.LoadedStartingSpeedUiIndex, startingDifficulty: fileTabData.LoadedStartingDifficulty, startingStars: fileTabData.LoadedStartingStars);
			}
		}
		catch
		{
		}
		return (startingBackground: loadedStartingBackgroundColor, startingGround: loadedStartingGroundColor, startingGameMode: loadedStartingGameMode, startingSpeedUiIndex: loadedStartingSpeedUiIndex, startingDifficulty: loadedStartingDifficulty, startingStars: loadedStartingStars);
	}

	private string NormalizeSongName(string songName)
	{
		if (string.IsNullOrEmpty(songName))
		{
			return "";
		}
		return songName.ToLowerInvariant().Replace(' ', '_').Replace("-", "")
			.Replace("'", "")
			.Replace("!", "")
			.Replace("?", "")
			.Replace(".", "")
			.Replace(",", "")
			.Replace(":", "")
			.Replace(";", "")
			.Replace("(", "")
			.Replace(")", "")
			.Replace("[", "")
			.Replace("]", "")
			.Replace("{", "")
			.Replace("}", "")
			.Replace("&", "")
			.Replace("#", "")
			.Replace("@", "")
			.Replace("$", "")
			.Replace("%", "")
			.Replace("^", "")
			.Replace("*", "")
			.Replace("+", "")
			.Replace("=", "")
			.Replace("/", "")
			.Replace("\\", "")
			.Replace("|", "")
			.Replace("<", "")
			.Replace(">", "");
	}

	public void SetLockSpritesToSet(bool enabled, string? decoOverride = null)
	{
		try
		{
			lockSpritesToSet = enabled;
			try
			{
				SaveSettingsWithTriggerOption();
			}
			catch
			{
			}
			ApplyLockSpritesToSet(decoOverride);
		}
		catch
		{
		}
	}

	public void SetShowAccurateTileset(bool enabled, string? blockOverride = null, string? spikeOverride = null, bool persistSetting = true)
	{
		try
		{
			showAccurateTileset = enabled;
			if (persistSetting)
			{
				try
				{
					SaveSettingsWithTriggerOption();
				}
				catch
				{
				}
			}
			if (showAccurateTileset)
			{
				string text = (string.IsNullOrEmpty(blockOverride) ? loadedBlockSet : blockOverride);
				string text2 = (string.IsNullOrEmpty(spikeOverride) ? loadedSpikeSet : spikeOverride);
				string text3 = "Slopesa";
				string text4 = "SlopesA";
				string text5 = "SlopesNone";
				string value = ToPascal(text ?? "");
				string value2 = ToPascal(text2 ?? "");
				List<string> list = new List<string>();
				string value3 = (noParallaxBg ? text3 : text5);
				string value4 = (noParallaxBg ? text4 : text3);
				list.Add($"{value}{value2}Sawsa{value3}.png");
				list.Add($"{value}{value2}Sawsa{value3}.PNG");
				list.Add($"{value}{value2}Sawsa{value4}.png");
				list.Add($"{value}{value2}Sawsa{text5}.png");
				string text6 = (text ?? "").ToLowerInvariant() + (text2 ?? "").ToLowerInvariant();
				list.Add(text6 + "sawsa" + (noParallaxBg ? "slopesa" : "slopesnone") + ".png");
				list.Add(text6 + "sawsaslopesa.png");
				list.Add(text6 + "sawsaslopesnone.png");
				List<string> list2 = new List<string>();
				list2.Add(System.IO.Path.Combine(AppContext.BaseDirectory, ".."));
				list2.Add(System.IO.Path.Combine("C:\\Editor Test", "tilesets"));
				list2.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "tilesets"));
				list2.Add(AppContext.BaseDirectory);
				list2.Add(Environment.CurrentDirectory);
				string text7 = FindRepoRootFor("famidash.bmp");
				if (!string.IsNullOrEmpty(text7))
				{
					list2.Add(System.IO.Path.Combine(text7, "tilesets"));
					list2.Add(text7);
				}
				string text8 = null;
				foreach (string item in list2)
				{
					try
					{
						if (string.IsNullOrEmpty(item) || !Directory.Exists(item))
						{
							continue;
						}
						foreach (string item2 in list)
						{
							string text9 = System.IO.Path.Combine(item, item2);
							if (File.Exists(text9))
							{
								text8 = text9;
								break;
							}
						}
						if (string.IsNullOrEmpty(text8))
						{
							continue;
						}
						break;
					}
					catch
					{
					}
				}
				if (string.IsNullOrEmpty(text8))
				{
					foreach (string item3 in list2)
					{
						try
						{
							if (string.IsNullOrEmpty(item3) || !Directory.Exists(item3))
							{
								continue;
							}
							string[] files = Directory.GetFiles(item3, "*.png");
							int num = 0;
							string text10 = null;
							string[] array = files;
							foreach (string text11 in array)
							{
								string text12 = System.IO.Path.GetFileName(text11).ToLowerInvariant();
								int num2 = 0;
								try
								{
									if (!string.IsNullOrEmpty(text) && text12.Contains(text.ToLowerInvariant()))
									{
										num2 += 2;
									}
								}
								catch
								{
								}
								try
								{
									if (!string.IsNullOrEmpty(text2) && text12.Contains(text2.ToLowerInvariant()))
									{
										num2 += 2;
									}
								}
								catch
								{
								}
								try
								{
									if (noParallaxBg && text12.Contains("slopesa"))
									{
										num2 += 2;
									}
								}
								catch
								{
								}
								try
								{
									if (!noParallaxBg && text12.Contains("slopesnone"))
									{
										num2 += 2;
									}
								}
								catch
								{
								}
								if (num2 > num)
								{
									num = num2;
									text10 = text11;
								}
							}
							if (text10 == null || num < 2)
							{
								continue;
							}
							text8 = text10;
							break;
						}
						catch
						{
						}
					}
				}
				if (string.IsNullOrEmpty(text8))
				{
					return;
				}
				LoadTileset(text8);
				try
				{
					SliceTileset();
					PopulateTilesPanel();
					backgroundDirty = true;
					RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
					Redraw();
				}
				try
				{
					base.Dispatcher.Invoke(delegate
					{
						Redraw();
					});
					return;
				}
				catch
				{
					return;
				}
			}
			try
			{
				if (!string.IsNullOrEmpty(loadedTilesetSource) && File.Exists(loadedTilesetSource))
				{
					LoadTileset(loadedTilesetSource);
				}
				else
				{
					BitmapImage bitmapImage = LoadEmbeddedImage("famidash.bmp");
					if (bitmapImage != null)
					{
						tilesetBitmap = bitmapImage;
						SliceTileset();
						PopulateTilesPanel();
					}
					else
					{
						string text13 = FindRepoRootFor("famidash.bmp");
						List<string> list3 = new List<string>();
						if (!string.IsNullOrEmpty(text13))
						{
							list3.Add(System.IO.Path.Combine(text13, "famidash.bmp"));
						}
						list3.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "famidash.bmp"));
						list3.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "famidash.png"));
						foreach (string item4 in list3)
						{
							try
							{
								if (!string.IsNullOrEmpty(item4) && File.Exists(item4))
								{
									LoadTileset(item4);
									break;
								}
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
			try
			{
				SliceTileset();
				PopulateTilesPanel();
				backgroundDirty = true;
				RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
			}
			catch
			{
				Redraw();
			}
			try
			{
				base.Dispatcher.Invoke(delegate
				{
					Redraw();
				});
			}
			catch
			{
			}
		}
		catch
		{
		}
		static string ToPascal(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return s ?? "";
			}
			string text14 = s.ToLowerInvariant();
			return char.ToUpperInvariant(text14[0]) + text14.Substring(1);
		}
	}

	private void ApplyLockSpritesToSet(string? decoOverride = null)
	{
		disabledSprites.Clear();
		if (!lockSpritesToSet)
		{
			try
			{
				if (IncompatibleOverlay != null)
				{
					IncompatibleOverlay.Children.Clear();
				}
			}
			catch
			{
			}
			try
			{
				PopulateSpritesPanel();
				return;
			}
			catch
			{
				return;
			}
		}
		if (!noParallaxBg)
		{
			disabledSprites.Add(23);
			disabledSprites.Add(75);
			disabledSprites.Add(88);
			disabledSprites.Add(100);
			disabledSprites.Add(106);
			disabledSprites.Add(107);
			disabledSprites.Add(108);
			disabledSprites.Add(126);
		}
		string source = (decoOverride ?? loadedDecoSet ?? "").ToUpperInvariant().Trim();
		string text = new string(source.Where((char c) => char.IsLetterOrDigit(c)).ToArray());
		if (text.Contains("EXTRA"))
		{
			for (int num = 42; num <= 53; num++)
			{
				disabledSprites.Add(num);
			}
			for (int num2 = 55; num2 <= 63; num2++)
			{
				disabledSprites.Add(num2);
			}
			disabledSprites.Add(74);
		}
		else if (text.Contains("DECOCLOUD") || text.Contains("DECO1") || text.StartsWith("DECO"))
		{
			int[] array = new int[18]
			{
				78, 79, 102, 103, 104, 105, 76, 77, 80, 81,
				89, 90, 91, 92, 93, 94, 110, 121
			};
			int[] array2 = array;
			foreach (int item in array2)
			{
				disabledSprites.Add(item);
			}
		}
		try
		{
			PopulateSpritesPanel();
		}
		catch
		{
		}
		if (selectedSprite >= 0 && disabledSprites.Contains(selectedSprite))
		{
			selectedSprite = -1;
			try
			{
				UpdatePaletteHighlight();
			}
			catch
			{
			}
		}
		try
		{
			UpdateIncompatibleOverlay();
		}
		catch
		{
		}
	}

	private void UpdateIncompatibleOverlay()
	{
		if (IncompatibleOverlay == null || CanvasHost == null)
		{
			return;
		}
		IncompatibleOverlay.Children.Clear();
		if (!lockSpritesToSet || spriteImages == null || sprites == null)
		{
			return;
		}
		try
		{
			DpiScale dpi = VisualTreeHelper.GetDpi(this);
			double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
			int num2 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleX));
			int num3 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleY));
			int num4 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
			int num5 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
			for (int i = 0; i < mapHeight; i++)
			{
				for (int j = 0; j < mapWidth; j++)
				{
					int num6 = i * mapWidth + j;
					int num7 = sprites[num6];
					if (num7 >= 0 && disabledSprites.Contains(num7))
					{
						Rectangle element = new Rectangle
						{
							Width = (double)num2 / dpi.DpiScaleX,
							Height = (double)num3 / dpi.DpiScaleY,
							Fill = new SolidColorBrush(Color.FromArgb(192, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
							Stroke = Brushes.Black,
							StrokeThickness = 1.0,
							IsHitTestVisible = false
						};
						double num8 = (double)(num4 + j * num2) / dpi.DpiScaleX;
						double num9 = (double)(num5 + i * num3) / dpi.DpiScaleY + gridRenderShiftY;
						Canvas.SetLeft(element, num8);
						Canvas.SetTop(element, num9);
						IncompatibleOverlay.Children.Add(element);
						TextBlock textBlock = new TextBlock
						{
							Text = "!",
							FontWeight = FontWeights.Bold,
							Foreground = Brushes.Black,
							FontSize = Math.Max(12.0, (double)num3 / dpi.DpiScaleY / 2.0),
							IsHitTestVisible = false
						};
						textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
						double length = num8 + ((double)num2 / dpi.DpiScaleX - textBlock.DesiredSize.Width) / 2.0;
						double length2 = num9 + ((double)num3 / dpi.DpiScaleY - textBlock.DesiredSize.Height) / 2.0;
						Canvas.SetLeft(textBlock, length);
						Canvas.SetTop(textBlock, length2);
						IncompatibleOverlay.Children.Add(textBlock);
					}
				}
			}
		}
		catch
		{
		}
	}

	private void ApplyParallaxChoice()
	{
		try
		{
			try
			{
				string text = FindRepoRootFor("famidash.bmp");
				string text2 = null;
				string text3 = null;
				if (!string.IsNullOrEmpty(text))
				{
					text2 = System.IO.Path.Combine(text, "src", "renderer", "assets", "parallax.bmp");
					text3 = System.IO.Path.Combine(text, "src", "renderer", "assets", "noparallax.bmp");
				}
				if (noParallaxBg)
				{
					if (!string.IsNullOrEmpty(text3) && File.Exists(text3))
					{
						Debug.WriteLine("ApplyParallaxChoice: using repo noparallax -> " + text3);
						LoadParallax(text3);
					}
					else
					{
						BitmapImage bitmapImage = LoadEmbeddedImage("noparallax.bmp");
						if (bitmapImage != null)
						{
							Debug.WriteLine("ApplyParallaxChoice: using embedded noparallax.bmp");
							parallaxBitmap = bitmapImage;
							SliceParallax();
						}
						else
						{
							List<string> list = new List<string>();
							if (!string.IsNullOrEmpty(text))
							{
								list.Add(System.IO.Path.Combine(text, "src", "renderer", "assets", "noparallax.bmp"));
								list.Add(System.IO.Path.Combine(text, "src", "render", "assets", "noparallax.bmp"));
							}
							list.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "noparallax.bmp"));
							list.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "noparallax.bmp"));
							foreach (string item in list)
							{
								try
								{
									if (!string.IsNullOrEmpty(item) && File.Exists(item))
									{
										Debug.WriteLine("ApplyParallaxChoice: using disk candidate -> " + item);
										LoadParallax(item);
										break;
									}
								}
								catch
								{
								}
							}
						}
					}
				}
				else if (!string.IsNullOrEmpty(text2) && File.Exists(text2))
				{
					Debug.WriteLine("ApplyParallaxChoice: using repo parallax -> " + text2);
					LoadParallax(text2);
				}
				else if (!string.IsNullOrEmpty(originalParallaxSource) && File.Exists(originalParallaxSource))
				{
					LoadParallax(originalParallaxSource);
				}
				else if (!string.IsNullOrEmpty(loadedParallaxSource) && File.Exists(loadedParallaxSource))
				{
					LoadParallax(loadedParallaxSource);
				}
				else
				{
					BitmapImage bitmapImage2 = LoadEmbeddedImage("parallax.bmp");
					if (bitmapImage2 != null)
					{
						Debug.WriteLine("ApplyParallaxChoice: using embedded parallax.bmp");
						parallaxBitmap = bitmapImage2;
						SliceParallax();
					}
					else
					{
						parallaxBitmap = null;
						parallaxImages = null;
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
		parallaxTintCache.Clear();
		backgroundDirty = true;
		try
		{
			base.Dispatcher.Invoke(delegate
			{
				Redraw();
			}, DispatcherPriority.Render);
		}
		catch
		{
		}
	}

	private double SafeViewportWidth()
	{
		try
		{
			return MapScrollViewer?.ViewportWidth ?? MapScrollViewer?.ActualWidth ?? 0.0;
		}
		catch
		{
			return 0.0;
		}
	}

	private double SafeViewportHeight()
	{
		try
		{
			return MapScrollViewer?.ViewportHeight ?? MapScrollViewer?.ActualHeight ?? 0.0;
		}
		catch
		{
			return 0.0;
		}
	}

	private string GetConfigPath(string tmxFilePath)
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		string text = System.IO.Path.Combine(folderPath, "Famidash Editor");
		if (!Directory.Exists(text))
		{
			Directory.CreateDirectory(text);
		}
		string fileName = System.IO.Path.GetFileName(tmxFilePath);
		string path = fileName + ".cfg";
		return System.IO.Path.Combine(text, path);
	}

	private void SaveEditorSettings()
	{
		try
		{
			SaveSettingsWithTriggerOption();
		}
		catch
		{
		}
	}

	private void SaveTmxConfig(string tmxFilePath)
	{
		try
		{
			TmxConfig tmxConfig = new TmxConfig();
			FileTabData fileTabData = null;
			try
			{
				fileTabData = openFiles?.Find((FileTabData f) => !string.IsNullOrEmpty(f.FilePath) && System.IO.Path.GetFullPath(f.FilePath).Equals(System.IO.Path.GetFullPath(tmxFilePath), StringComparison.OrdinalIgnoreCase));
			}
			catch
			{
				fileTabData = null;
			}
			if (fileTabData == null)
			{
				Debug.WriteLine("SaveTmxConfig: File not found in openFiles, skipping save: " + tmxFilePath);
				return;
			}
			Color color = fileTabData.BackgroundTint;
			Color color2 = fileTabData.GroundTint;
			Color color3 = fileTabData.TileTint;
			try
			{
				if (color.A != 0)
				{
					tmxConfig.BackgroundTintR = color.R;
					tmxConfig.BackgroundTintG = color.G;
					tmxConfig.BackgroundTintB = color.B;
				}
			}
			catch
			{
			}
			try
			{
				if (color2.A != 0)
				{
					tmxConfig.GroundTintR = color2.R;
					tmxConfig.GroundTintG = color2.G;
					tmxConfig.GroundTintB = color2.B;
				}
			}
			catch
			{
			}
			try
			{
				if (color3.A != 0)
				{
					tmxConfig.TileTintR = color3.R;
					tmxConfig.TileTintG = color3.G;
					tmxConfig.TileTintB = color3.B;
				}
			}
			catch
			{
			}
			tmxConfig.NoParallaxBg = fileTabData.NoParallaxBg;
			tmxConfig.DecoSet = fileTabData.LoadedDecoSet ?? "";
			tmxConfig.BlockSet = fileTabData.LoadedBlockSet ?? "";
			tmxConfig.SpikeSet = fileTabData.LoadedSpikeSet ?? "";
			try
			{
				if (!string.IsNullOrEmpty(fileTabData.SelectedSong))
				{
					tmxConfig.SelectedSong = fileTabData.SelectedSong;
				}
			}
			catch
			{
			}
			try
			{
				int num = fileTabData.LoadedStartingSpeedUiIndex;
				tmxConfig.StartingSpeed = num switch
				{
					1 => 0, 
					0 => 1, 
					_ => num, 
				};
			}
			catch
			{
			}
			try
			{
				tmxConfig.MaxFallSpeed = fileTabData.LoadedMaxFallSpeed;
			}
			catch
			{
			}
			try
			{
				int? num2 = fileTabData.LoadedStartingBackgroundColor;
				if (num2.HasValue)
				{
					tmxConfig.StartingBackgroundColor = num2.Value;
				}
			}
			catch
			{
			}
			try
			{
				int? num3 = fileTabData.LoadedStartingGameMode;
				if (num3.HasValue)
				{
					tmxConfig.StartingGameMode = num3.Value;
				}
			}
			catch
			{
			}
			try
			{
				int? num4 = fileTabData.LoadedStartingGroundColor;
				if (num4.HasValue)
				{
					tmxConfig.StartingGroundColor = num4.Value;
				}
			}
			catch
			{
			}
			try
			{
				int? num5 = fileTabData.LoadedSpawnYPositionHi;
				if (num5.HasValue)
				{
					tmxConfig.SpawnYPositionHi = num5.Value;
				}
			}
			catch
			{
			}
			try
			{
				int? num6 = fileTabData.LoadedSpawnYPositionLow;
				if (num6.HasValue)
				{
					tmxConfig.SpawnYPositionLow = num6.Value;
				}
			}
			catch
			{
			}
			try
			{
				int? num7 = fileTabData.LoadedScrollYPositionHi;
				if (num7.HasValue)
				{
					tmxConfig.ScrollYPositionHi = num7.Value;
				}
			}
			catch
			{
			}
			try
			{
				int? num8 = fileTabData.LoadedScrollYPositionLow;
				if (num8.HasValue)
				{
					tmxConfig.ScrollYPositionLow = num8.Value;
				}
			}
			catch
			{
			}
			try
			{
				int? num9 = fileTabData.LoadedStartingDifficulty;
				if (num9.HasValue)
				{
					tmxConfig.Difficulty = num9.Value;
				}
			}
			catch
			{
			}
			try
			{
				int? num10 = fileTabData.LoadedStartingStars;
				if (num10.HasValue)
				{
					tmxConfig.Stars = num10.Value;
				}
			}
			catch
			{
			}
			try
			{
				string text = fileTabData.LoadedStartingLowerText;
				if (!string.IsNullOrEmpty(text))
				{
					tmxConfig.LowerText = text;
				}
			}
			catch
			{
			}
			try
			{
				string text2 = fileTabData.LoadedStartingUpperText;
				if (!string.IsNullOrEmpty(text2))
				{
					tmxConfig.UpperText = text2;
				}
			}
			catch
			{
			}
			try
			{
				Dictionary<int, (int, int)> dictionary = fileTabData.SpritePixelOffsets;
				if (dictionary != null && dictionary.Count > 0)
				{
					tmxConfig.SpriteOffsets = new Dictionary<string, int[]>();
					foreach (KeyValuePair<int, (int, int)> item in dictionary)
					{
						int value = item.Key % mapWidth;
						int value2 = item.Key / mapWidth;
						string key = $"{value},{value2}";
						tmxConfig.SpriteOffsets[key] = new int[2]
						{
							item.Value.Item1,
							item.Value.Item2
						};
					}
				}
			}
			catch
			{
			}
			try
			{
				if (openFiles != null && currentFileIndex >= 0 && currentFileIndex < openFiles.Count && openFiles[currentFileIndex] == fileTabData)
				{
					Dictionary<int, (int anchorTileX, int anchorTileY)> dictionary2 = spriteAnchors;
					if (dictionary2 != null && dictionary2.Count > 0)
					{
						tmxConfig.SpriteAnchors = new Dictionary<string, int[]>();
						foreach (KeyValuePair<int, (int, int)> spriteAnchor in spriteAnchors)
						{
							int value3 = spriteAnchor.Key % mapWidth;
							int value4 = spriteAnchor.Key / mapWidth;
							string key2 = $"{value3},{value4}";
							tmxConfig.SpriteAnchors[key2] = new int[2]
							{
								spriteAnchor.Value.Item1,
								spriteAnchor.Value.Item2
							};
						}
					}
				}
			}
			catch
			{
			}
			string configPath = GetConfigPath(tmxFilePath);
			try
			{
				TmxConfig tmxConfig2 = null;
				if (File.Exists(configPath))
				{
					try
					{
						string json = File.ReadAllText(configPath);
						tmxConfig2 = JsonSerializer.Deserialize<TmxConfig>(json);
					}
					catch
					{
						tmxConfig2 = null;
					}
				}
				TmxConfig tmxConfig3 = tmxConfig2 ?? new TmxConfig();
				try
				{
					if (tmxConfig.BackgroundTintR.HasValue)
					{
						tmxConfig3.BackgroundTintR = tmxConfig.BackgroundTintR;
						tmxConfig3.BackgroundTintG = tmxConfig.BackgroundTintG;
						tmxConfig3.BackgroundTintB = tmxConfig.BackgroundTintB;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.GroundTintR.HasValue)
					{
						tmxConfig3.GroundTintR = tmxConfig.GroundTintR;
						tmxConfig3.GroundTintG = tmxConfig.GroundTintG;
						tmxConfig3.GroundTintB = tmxConfig.GroundTintB;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.TileTintR.HasValue)
					{
						tmxConfig3.TileTintR = tmxConfig.TileTintR;
						tmxConfig3.TileTintG = tmxConfig.TileTintG;
						tmxConfig3.TileTintB = tmxConfig.TileTintB;
					}
				}
				catch
				{
				}
				try
				{
					tmxConfig3.NoParallaxBg = tmxConfig.NoParallaxBg;
				}
				catch
				{
				}
				try
				{
					if (!string.IsNullOrEmpty(tmxConfig.DecoSet))
					{
						tmxConfig3.DecoSet = tmxConfig.DecoSet;
					}
				}
				catch
				{
				}
				try
				{
					if (!string.IsNullOrEmpty(tmxConfig.BlockSet))
					{
						tmxConfig3.BlockSet = tmxConfig.BlockSet;
					}
				}
				catch
				{
				}
				try
				{
					if (!string.IsNullOrEmpty(tmxConfig.SpikeSet))
					{
						tmxConfig3.SpikeSet = tmxConfig.SpikeSet;
					}
				}
				catch
				{
				}
				try
				{
					if (!string.IsNullOrEmpty(tmxConfig.SelectedSong))
					{
						tmxConfig3.SelectedSong = tmxConfig.SelectedSong;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.MaxFallSpeed.HasValue)
					{
						tmxConfig3.MaxFallSpeed = tmxConfig.MaxFallSpeed;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.StartingSpeed.HasValue)
					{
						tmxConfig3.StartingSpeed = tmxConfig.StartingSpeed;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.StartingBackgroundColor.HasValue)
					{
						tmxConfig3.StartingBackgroundColor = tmxConfig.StartingBackgroundColor;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.StartingGameMode.HasValue)
					{
						tmxConfig3.StartingGameMode = tmxConfig.StartingGameMode;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.StartingGroundColor.HasValue)
					{
						tmxConfig3.StartingGroundColor = tmxConfig.StartingGroundColor;
					}
				}
				catch
				{
				}
				try
				{
					tmxConfig3.SpawnYPositionHi = tmxConfig.SpawnYPositionHi;
				}
				catch
				{
				}
				try
				{
					tmxConfig3.SpawnYPositionLow = tmxConfig.SpawnYPositionLow;
				}
				catch
				{
				}
				try
				{
					tmxConfig3.ScrollYPositionHi = tmxConfig.ScrollYPositionHi;
				}
				catch
				{
				}
				try
				{
					tmxConfig3.ScrollYPositionLow = tmxConfig.ScrollYPositionLow;
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.Difficulty.HasValue)
					{
						tmxConfig3.Difficulty = tmxConfig.Difficulty;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.Stars.HasValue)
					{
						tmxConfig3.Stars = tmxConfig.Stars;
					}
				}
				catch
				{
				}
				try
				{
					if (!string.IsNullOrEmpty(tmxConfig.LowerText))
					{
						tmxConfig3.LowerText = tmxConfig.LowerText;
					}
				}
				catch
				{
				}
				try
				{
					if (!string.IsNullOrEmpty(tmxConfig.UpperText))
					{
						tmxConfig3.UpperText = tmxConfig.UpperText;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.SpriteOffsets != null && tmxConfig.SpriteOffsets.Count > 0)
					{
						tmxConfig3.SpriteOffsets = tmxConfig.SpriteOffsets;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.SpriteAnchors != null && tmxConfig.SpriteAnchors.Count > 0)
					{
						tmxConfig3.SpriteAnchors = tmxConfig.SpriteAnchors;
					}
				}
				catch
				{
				}
				JsonSerializerOptions options = new JsonSerializerOptions
				{
					WriteIndented = true,
					DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
				};
				string contents = JsonSerializer.Serialize(tmxConfig3, options);
				File.WriteAllText(configPath, contents);
				Debug.WriteLine("Saved config to: " + configPath);
			}
			catch (Exception ex)
			{
				Debug.WriteLine("Failed to save (merge) TMX config: " + ex.Message);
			}
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("Failed to save TMX config: " + ex2.Message);
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
				TmxConfig tmxConfig = JsonSerializer.Deserialize<TmxConfig>(json);
				if (tmxConfig == null)
				{
					return;
				}
				bool flag = false;
				bool flag2 = false;
				bool flag3 = false;
				try
				{
					if (tmxConfig.BackgroundTintR.HasValue && tmxConfig.BackgroundTintG.HasValue && tmxConfig.BackgroundTintB.HasValue)
					{
						backgroundTint = Color.FromRgb(tmxConfig.BackgroundTintR.Value, tmxConfig.BackgroundTintG.Value, tmxConfig.BackgroundTintB.Value);
						flag = true;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.GroundTintR.HasValue && tmxConfig.GroundTintG.HasValue && tmxConfig.GroundTintB.HasValue)
					{
						groundTint = Color.FromRgb(tmxConfig.GroundTintR.Value, tmxConfig.GroundTintG.Value, tmxConfig.GroundTintB.Value);
						flag2 = true;
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.TileTintR.HasValue && tmxConfig.TileTintG.HasValue && tmxConfig.TileTintB.HasValue)
					{
						tileTint = Color.FromRgb(tmxConfig.TileTintR.Value, tmxConfig.TileTintG.Value, tmxConfig.TileTintB.Value);
						flag3 = true;
					}
				}
				catch
				{
				}
				if (!flag || !flag2 || !flag3)
				{
					try
					{
						string baseDirectory = AppContext.BaseDirectory;
						string text = System.IO.Path.Combine(baseDirectory, "editor-settings.json");
						Debug.WriteLine($"Reloading missing tints from: {text} (bg:{!flag}, gnd:{!flag2}, tile:{!flag3})");
						if (File.Exists(text))
						{
							string json2 = File.ReadAllText(text);
							JsonDocument jsonDocument = JsonDocument.Parse(json2);
							if (!flag && jsonDocument.RootElement.TryGetProperty("backgroundTint", out var value) && value.GetArrayLength() >= 4)
							{
								backgroundTint = Color.FromArgb((byte)value[0].GetInt32(), (byte)value[1].GetInt32(), (byte)value[2].GetInt32(), (byte)value[3].GetInt32());
								Debug.WriteLine($"Reloaded backgroundTint: {backgroundTint}");
							}
							if (!flag2 && jsonDocument.RootElement.TryGetProperty("groundTint", out var value2) && value2.GetArrayLength() >= 4)
							{
								groundTint = Color.FromArgb((byte)value2[0].GetInt32(), (byte)value2[1].GetInt32(), (byte)value2[2].GetInt32(), (byte)value2[3].GetInt32());
								Debug.WriteLine($"Reloaded groundTint: {groundTint}");
							}
							if (!flag3 && jsonDocument.RootElement.TryGetProperty("tileTint", out var value3) && value3.GetArrayLength() >= 4)
							{
								tileTint = Color.FromArgb((byte)value3[0].GetInt32(), (byte)value3[1].GetInt32(), (byte)value3[2].GetInt32(), (byte)value3[3].GetInt32());
								Debug.WriteLine($"Reloaded tileTint: {tileTint}");
							}
						}
						else
						{
							Debug.WriteLine("Settings file not found at: " + text);
						}
					}
					catch (Exception ex)
					{
						Debug.WriteLine("Error reloading missing tints: " + ex.Message);
					}
				}
				try
				{
					noParallaxBg = tmxConfig.NoParallaxBg;
				}
				catch
				{
					noParallaxBg = false;
				}
				try
				{
					loadedDecoSet = (string.IsNullOrEmpty(tmxConfig.DecoSet) ? "DECO1" : tmxConfig.DecoSet);
				}
				catch
				{
					loadedDecoSet = "DECO1";
				}
				try
				{
					loadedBlockSet = (string.IsNullOrEmpty(tmxConfig.BlockSet) ? "BLOCKSA" : tmxConfig.BlockSet);
				}
				catch
				{
					loadedBlockSet = "BLOCKSA";
				}
				try
				{
					loadedSpikeSet = (string.IsNullOrEmpty(tmxConfig.SpikeSet) ? "SPIKESA" : tmxConfig.SpikeSet);
				}
				catch
				{
					loadedSpikeSet = "SPIKESA";
				}
				try
				{
					if (!string.IsNullOrEmpty(tmxConfig.SelectedSong) && FamiTrackCombo != null)
					{
						for (int i = 0; i < FamiTrackCombo.Items.Count; i++)
						{
							if (FamiTrackCombo.Items[i] is ComboBoxItem { Content: var content } && content?.ToString() == tmxConfig.SelectedSong)
							{
								FamiTrackCombo.SelectedIndex = i;
								break;
							}
						}
					}
				}
				catch
				{
				}
				try
				{
					if (tmxConfig.StartingSpeed.HasValue)
					{
						int value4 = tmxConfig.StartingSpeed.Value;
						loadedStartingSpeedUiIndex = value4 switch
						{
							0 => 1, 
							1 => 0, 
							_ => value4, 
						};
					}
					else
					{
						loadedStartingSpeedUiIndex = 1;
					}
				}
				catch
				{
					loadedStartingSpeedUiIndex = 1;
				}
				try
				{
					loadedStartingBackgroundColor = (tmxConfig.StartingBackgroundColor.HasValue ? new int?(tmxConfig.StartingBackgroundColor.Value) : ((int?)null));
				}
				catch
				{
					loadedStartingBackgroundColor = null;
				}
				try
				{
					loadedStartingGameMode = (tmxConfig.StartingGameMode.HasValue ? new int?(tmxConfig.StartingGameMode.Value) : ((int?)null));
				}
				catch
				{
					loadedStartingGameMode = null;
				}
				try
				{
					loadedStartingGroundColor = (tmxConfig.StartingGroundColor.HasValue ? new int?(tmxConfig.StartingGroundColor.Value) : ((int?)null));
				}
				catch
				{
					loadedStartingGroundColor = null;
				}
				try
				{
					loadedStartingDifficulty = (tmxConfig.Difficulty.HasValue ? new int?(tmxConfig.Difficulty.Value) : ((int?)null));
				}
				catch
				{
					loadedStartingDifficulty = null;
				}
				try
				{
					loadedStartingStars = (tmxConfig.Stars.HasValue ? new int?(tmxConfig.Stars.Value) : ((int?)null));
				}
				catch
				{
					loadedStartingStars = null;
				}
				try
				{
					loadedStartingLowerText = ((!string.IsNullOrEmpty(tmxConfig.LowerText)) ? tmxConfig.LowerText : null);
				}
				catch
				{
					loadedStartingLowerText = null;
				}
				try
				{
					loadedStartingUpperText = ((!string.IsNullOrEmpty(tmxConfig.UpperText)) ? tmxConfig.UpperText : null);
				}
				catch
				{
					loadedStartingUpperText = null;
				}
				try
				{
					loadedSpawnYPositionHi = (tmxConfig.SpawnYPositionHi.HasValue ? new int?(tmxConfig.SpawnYPositionHi.Value) : ((int?)null));
				}
				catch
				{
					loadedSpawnYPositionHi = null;
				}
				try
				{
					loadedSpawnYPositionLow = (tmxConfig.SpawnYPositionLow.HasValue ? new int?(tmxConfig.SpawnYPositionLow.Value) : ((int?)null));
				}
				catch
				{
					loadedSpawnYPositionLow = null;
				}
				try
				{
					loadedScrollYPositionHi = (tmxConfig.ScrollYPositionHi.HasValue ? new int?(tmxConfig.ScrollYPositionHi.Value) : ((int?)null));
				}
				catch
				{
					loadedScrollYPositionHi = null;
				}
				try
				{
					loadedScrollYPositionLow = (tmxConfig.ScrollYPositionLow.HasValue ? new int?(tmxConfig.ScrollYPositionLow.Value) : ((int?)null));
				}
				catch
				{
					loadedScrollYPositionLow = null;
				}
				try
				{
					loadedMaxFallSpeed = (tmxConfig.MaxFallSpeed.HasValue ? tmxConfig.MaxFallSpeed.Value : 6);
				}
				catch
				{
					loadedMaxFallSpeed = 6;
				}
				try
				{
					if (tmxConfig.SpriteOffsets != null && tmxConfig.SpriteOffsets.Count > 0)
					{
						spritePixelOffsets.Clear();
						foreach (KeyValuePair<string, int[]> spriteOffset in tmxConfig.SpriteOffsets)
						{
							string[] array = spriteOffset.Key.Split(',');
							if (array.Length == 2 && int.TryParse(array[0], out var result) && int.TryParse(array[1], out var result2))
							{
								int key = result2 * mapWidth + result;
								if (spriteOffset.Value.Length >= 2)
								{
									spritePixelOffsets[key] = (spriteOffset.Value[0], spriteOffset.Value[1]);
								}
							}
						}
						Debug.WriteLine($"Loaded {spritePixelOffsets.Count} sprite offsets from config");
					}
				}
				catch (Exception ex2)
				{
					Debug.WriteLine("Error loading sprite offsets: " + ex2.Message);
				}
				try
				{
					if (tmxConfig.SpriteAnchors != null && tmxConfig.SpriteAnchors.Count > 0)
					{
						spriteAnchors.Clear();
						foreach (KeyValuePair<string, int[]> spriteAnchor in tmxConfig.SpriteAnchors)
						{
							string[] array2 = spriteAnchor.Key.Split(',');
							if (array2.Length == 2 && int.TryParse(array2[0], out var result3) && int.TryParse(array2[1], out var result4))
							{
								int key2 = result4 * mapWidth + result3;
								if (spriteAnchor.Value.Length >= 2)
								{
									spriteAnchors[key2] = (spriteAnchor.Value[0], spriteAnchor.Value[1]);
								}
							}
						}
						Debug.WriteLine($"Loaded {spriteAnchors.Count} sprite anchors from config");
					}
				}
				catch (Exception ex3)
				{
					Debug.WriteLine("Error loading sprite anchors: " + ex3.Message);
				}
				try
				{
					if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
					{
						FileTabData fileTabData = openFiles[currentFileIndex];
						fileTabData.LoadedDecoSet = loadedDecoSet;
						fileTabData.LoadedBlockSet = loadedBlockSet;
						fileTabData.LoadedSpikeSet = loadedSpikeSet;
						fileTabData.LoadedStartingSpeedUiIndex = loadedStartingSpeedUiIndex;
						fileTabData.LoadedMaxFallSpeed = loadedMaxFallSpeed;
						fileTabData.LoadedStartingBackgroundColor = loadedStartingBackgroundColor;
						fileTabData.LoadedStartingGameMode = loadedStartingGameMode;
						fileTabData.LoadedStartingGroundColor = loadedStartingGroundColor;
						fileTabData.LoadedStartingDifficulty = loadedStartingDifficulty;
						fileTabData.LoadedStartingStars = loadedStartingStars;
						fileTabData.LoadedStartingLowerText = loadedStartingLowerText;
						fileTabData.LoadedStartingUpperText = loadedStartingUpperText;
						fileTabData.LoadedSpawnYPositionHi = loadedSpawnYPositionHi;
						fileTabData.LoadedSpawnYPositionLow = loadedSpawnYPositionLow;
						fileTabData.LoadedScrollYPositionHi = loadedScrollYPositionHi;
						fileTabData.LoadedScrollYPositionLow = loadedScrollYPositionLow;
						fileTabData.NoParallaxBg = noParallaxBg;
						fileTabData.SpritePixelOffsets = new Dictionary<int, (int, int)>(spritePixelOffsets);
						if (!string.IsNullOrEmpty(tmxConfig.SelectedSong))
						{
							fileTabData.SelectedSong = tmxConfig.SelectedSong;
						}
					}
				}
				catch
				{
				}
				if (StatusText != null)
				{
					StatusText.Text = $"Loaded deco set: {loadedDecoSet} block:{loadedBlockSet} spike:{loadedSpikeSet}";
				}
				UpdateParallaxTint();
				UpdateGroundTint();
				UpdateTileTint();
				backgroundDirty = true;
				try
				{
					scaledTileCaches.Clear();
				}
				catch
				{
				}
				try
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
				}
				try
				{
					if (showAccurateTileset)
					{
						SetShowAccurateTileset(enabled: true, loadedBlockSet, loadedSpikeSet);
					}
				}
				catch
				{
				}
				try
				{
					Redraw();
				}
				catch
				{
				}
				Debug.WriteLine("Loaded config from: " + configPath);
				if (StatusText != null)
				{
					StatusText.Text = "Loaded tint config for " + System.IO.Path.GetFileName(tmxFilePath);
				}
				try
				{
					ApplyParallaxChoice();
				}
				catch
				{
				}
				try
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					ApplyLockSpritesToSet();
					return;
				}
				catch
				{
					return;
				}
			}
			try
			{
				string baseDirectory2 = AppContext.BaseDirectory;
				string path = System.IO.Path.Combine(baseDirectory2, "editor-settings.json");
				if (File.Exists(path))
				{
					string json3 = File.ReadAllText(path);
					JsonDocument jsonDocument2 = JsonDocument.Parse(json3);
					if (jsonDocument2.RootElement.TryGetProperty("backgroundTint", out var value5) && value5.GetArrayLength() >= 4)
					{
						byte a = (byte)value5[0].GetInt32();
						byte r = (byte)value5[1].GetInt32();
						byte g = (byte)value5[2].GetInt32();
						byte b = (byte)value5[3].GetInt32();
						backgroundTint = Color.FromArgb(a, r, g, b);
					}
					if (jsonDocument2.RootElement.TryGetProperty("groundTint", out var value6) && value6.GetArrayLength() >= 4)
					{
						byte a2 = (byte)value6[0].GetInt32();
						byte r2 = (byte)value6[1].GetInt32();
						byte g2 = (byte)value6[2].GetInt32();
						byte b2 = (byte)value6[3].GetInt32();
						groundTint = Color.FromArgb(a2, r2, g2, b2);
					}
					if (jsonDocument2.RootElement.TryGetProperty("tileTint", out var value7) && value7.GetArrayLength() >= 4)
					{
						byte a3 = (byte)value7[0].GetInt32();
						byte r3 = (byte)value7[1].GetInt32();
						byte g3 = (byte)value7[2].GetInt32();
						byte b3 = (byte)value7[3].GetInt32();
						tileTint = Color.FromArgb(a3, r3, g3, b3);
					}
					try
					{
						if (jsonDocument2.RootElement.TryGetProperty("hideTriggerSprites", out var value8))
						{
							try
							{
								hideTriggerSprites = value8.GetBoolean();
							}
							catch
							{
								hideTriggerSprites = true;
							}
							try
							{
								foreach (Window window in System.Windows.Application.Current.Windows)
								{
									try
									{
										if (window is SimulatorWindow simulatorWindow)
										{
											simulatorWindow.SetHideTriggerSprites(hideTriggerSprites);
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
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			UpdateParallaxTint();
			UpdateGroundTint();
			UpdateTileTint();
			noParallaxBg = false;
			try
			{
				ApplyParallaxChoice();
			}
			catch
			{
			}
			backgroundDirty = true;
			try
			{
				scaledTileCaches.Clear();
			}
			catch
			{
			}
			try
			{
				Redraw();
			}
			catch
			{
			}
			try
			{
				PopulateTilesPanel();
			}
			catch
			{
			}
			Debug.WriteLine("No config found at: " + configPath + ", using defaults");
			loadedDecoSet = "DECO1";
			loadedBlockSet = "BLOCKSA";
			loadedSpikeSet = "SPIKESA";
			loadedSpawnYPositionHi = null;
			loadedSpawnYPositionLow = null;
			loadedScrollYPositionHi = null;
			loadedScrollYPositionLow = null;
			try
			{
				RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				ApplyLockSpritesToSet();
			}
			catch
			{
			}
		}
		catch (Exception ex4)
		{
			Debug.WriteLine("Failed to load TMX config: " + ex4.Message);
			try
			{
				string baseDirectory3 = AppContext.BaseDirectory;
				string path2 = System.IO.Path.Combine(baseDirectory3, "editor-settings.json");
				if (File.Exists(path2))
				{
					string json4 = File.ReadAllText(path2);
					JsonDocument jsonDocument3 = JsonDocument.Parse(json4);
					if (jsonDocument3.RootElement.TryGetProperty("backgroundTint", out var value9) && value9.GetArrayLength() >= 4)
					{
						backgroundTint = Color.FromArgb((byte)value9[0].GetInt32(), (byte)value9[1].GetInt32(), (byte)value9[2].GetInt32(), (byte)value9[3].GetInt32());
					}
					if (jsonDocument3.RootElement.TryGetProperty("groundTint", out var value10) && value10.GetArrayLength() >= 4)
					{
						groundTint = Color.FromArgb((byte)value10[0].GetInt32(), (byte)value10[1].GetInt32(), (byte)value10[2].GetInt32(), (byte)value10[3].GetInt32());
					}
					if (jsonDocument3.RootElement.TryGetProperty("tileTint", out var value11) && value11.GetArrayLength() >= 4)
					{
						tileTint = Color.FromArgb((byte)value11[0].GetInt32(), (byte)value11[1].GetInt32(), (byte)value11[2].GetInt32(), (byte)value11[3].GetInt32());
					}
				}
			}
			catch
			{
			}
			loadedDecoSet = "DECO1";
			UpdateParallaxTint();
			UpdateGroundTint();
			UpdateTileTint();
			backgroundDirty = true;
			try
			{
				scaledTileCaches.Clear();
			}
			catch
			{
			}
			try
			{
				RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
			}
			catch
			{
			}
			try
			{
				PopulateTilesPanel();
			}
			catch
			{
			}
		}
	}

	private void ClearTintedCaches()
	{
		try
		{
			tintedSpriteCache.Clear();
		}
		catch
		{
		}
		try
		{
			tintedCustomCache.Clear();
		}
		catch
		{
		}
	}

	public unsafe MainWindow()
	{
		InitializeComponent();
		LoadSettings();
		try
		{
			suppressSettingsSave = false;
		}
		catch
		{
		}
		LoadRecentFiles();
		try
		{
			if (MenuOptionHideTriggerSprites != null)
			{
				MenuOptionHideTriggerSprites.IsChecked = GetHideTriggerSprites();
			}
		}
		catch
		{
		}
		try
		{
			if (MenuOptionShowAccurateTileset != null)
			{
				MenuOptionShowAccurateTileset.IsChecked = ShowAccurateTileset;
			}
		}
		catch
		{
		}
		try
		{
			if (MenuOptionOpenSimulatorPaused != null)
			{
				MenuOptionOpenSimulatorPaused.IsChecked = loadedOpenSimulatorPaused;
			}
		}
		catch
		{
		}
		InitDefaultMap();
		CreateNewTab();
		try
		{
			TryLoadFamiAlbumParsedJson();
		}
		catch
		{
		}
		try
		{
			if (PlayFamiButton != null)
			{
				PlayFamiButton.Click += PlayFamiButton_Click;
			}
		}
		catch
		{
		}
		try
		{
			if (StopFamiButton != null)
			{
				StopFamiButton.Click += StopFamiButton_Click;
			}
		}
		catch
		{
		}
		try
		{
			if (MenuConfigureFamiStudio != null)
			{
				MenuConfigureFamiStudio.Click += MenuConfigureFamiStudio_Click;
			}
		}
		catch
		{
		}
		try
		{
			if (MenuScanFamiStudioTracks != null)
			{
				MenuScanFamiStudioTracks.Click += MenuScanFamiStudioTracks_Click;
			}
		}
		catch
		{
		}
		try
		{
			InitializeStructurePopupIcons();
		}
		catch
		{
		}
		try
		{
			Task.Run(delegate
			{
				try
				{
					if (!string.IsNullOrEmpty(famiStudioPath) && Directory.Exists(famiStudioPath) && !famiIntegration.IsLoaded)
					{
						try
						{
							famiIntegration.LoadFromFolder(famiStudioPath);
						}
						catch
						{
						}
					}
					string text = null;
					string text2 = System.IO.Path.Combine(Environment.CurrentDirectory, "the album.fms");
					if (File.Exists(text2))
					{
						text = text2;
					}
					if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(albumTxtPath) && System.IO.Path.GetExtension(albumTxtPath).Equals(".fms", StringComparison.OrdinalIgnoreCase))
					{
						text = albumTxtPath;
					}
					if (!string.IsNullOrEmpty(text) && File.Exists(text))
					{
						try
						{
							famiIntegration.WarmAndPrime(text);
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
		}
		catch
		{
		}
		base.Closing += Window_Closing;
		try
		{
			if (FindName("ManipulateButton") is System.Windows.Controls.Button button)
			{
				button.IsEnabled = false;
			}
		}
		catch
		{
		}
		try
		{
			if (FindName("Menu_Manipulate_Rotate") is MenuItem menuItem)
			{
				menuItem.IsEnabled = false;
			}
			if (FindName("Menu_Manipulate_RotateCCW") is MenuItem menuItem2)
			{
				menuItem2.IsEnabled = false;
			}
			if (FindName("Menu_Manipulate_Resize") is MenuItem menuItem3)
			{
				menuItem3.IsEnabled = false;
			}
			if (FindName("Menu_Manipulate_FlipH") is MenuItem menuItem4)
			{
				menuItem4.IsEnabled = false;
			}
			if (FindName("Menu_Manipulate_FlipV") is MenuItem menuItem5)
			{
				menuItem5.IsEnabled = false;
			}
			if (FindName("Menu_Manipulate_Rotate_Tools") is MenuItem menuItem6)
			{
				menuItem6.IsEnabled = false;
			}
			if (FindName("Menu_Manipulate_RotateCCW_Tools") is MenuItem menuItem7)
			{
				menuItem7.IsEnabled = false;
			}
			if (FindName("Menu_Manipulate_Resize_Tools") is MenuItem menuItem8)
			{
				menuItem8.IsEnabled = false;
			}
			if (FindName("Menu_Manipulate_FlipH_Tools") is MenuItem menuItem9)
			{
				menuItem9.IsEnabled = false;
			}
			if (FindName("Menu_Manipulate_FlipV_Tools") is MenuItem menuItem10)
			{
				menuItem10.IsEnabled = false;
			}
		}
		catch
		{
		}
		if (ZoomSlider != null)
		{
			bool isZoomSliderPressed = false;
			zoomThrottleTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(150.0)
			};
			zoomThrottleTimer.Tick += delegate
			{
				zoomThrottleTimer?.Stop();
				if (!isZoomSliderPressed && !deferZoomRebuild)
				{
					Redraw();
					lastHoverX = -1;
					lastHoverY = -1;
				}
			};
			zoomCommitTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(1.0)
			};
			zoomCommitTimer.Tick += delegate
			{
				zoomCommitTimer?.Stop();
				deferZoomRebuild = false;
				LoadingWindow loadingWindow = null;
				try
				{
					loadingWindow = new LoadingWindow
					{
						Owner = this
					};
					loadingWindow.SetMessage("Rendering zoom...");
					loadingWindow.Show();
					base.Dispatcher.Invoke(delegate
					{
					}, DispatcherPriority.Background);
					CommitZoom();
				}
				finally
				{
					loadingWindow?.Close();
				}
			};
			ZoomSlider.PreviewMouseLeftButtonDown += delegate
			{
				isZoomSliderPressed = true;
				try
				{
					if (MapScrollViewer != null)
					{
						Point position = Mouse.GetPosition(MapScrollViewer);
						zoomAnchorViewportX = position.X;
						zoomAnchorViewportY = position.Y;
						double num = MapScrollViewer?.HorizontalOffset ?? 0.0;
						double num2 = MapScrollViewer?.VerticalOffset ?? 0.0;
						double num3 = num + position.X;
						double num4 = num2 + position.Y;
						double num5 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
						double num6 = mapViewportPadding;
						zoomAnchorMapX = (num3 - num6) / (16.0 * num5);
						zoomAnchorMapY = (num4 - num6) / (16.0 * num5);
						hasZoomAnchor = true;
					}
				}
				catch
				{
					hasZoomAnchor = false;
				}
			};
			ZoomSlider.PreviewMouseLeftButtonUp += delegate
			{
				isZoomSliderPressed = false;
				zoomCommitTimer?.Stop();
				LoadingWindow loadingWindow = null;
				try
				{
					loadingWindow = new LoadingWindow
					{
						Owner = this
					};
					loadingWindow.SetMessage("Rendering zoom...");
					loadingWindow.Show();
					base.Dispatcher.Invoke(delegate
					{
					}, DispatcherPriority.Background);
					deferZoomRebuild = false;
					CommitZoom();
				}
				finally
				{
					loadingWindow?.Close();
				}
			};
			ZoomSlider.ValueChanged += delegate
			{
				if (ZoomSlider != null && !isSnappingZoom)
				{
					double value = ZoomSlider.Value;
					double num = Math.Round(value * 4.0) / 4.0;
					if (num < 0.25)
					{
						num = 0.25;
					}
					if (num > 4.0)
					{
						num = 4.0;
					}
					if (Math.Abs(value - num) > 0.001)
					{
						isSnappingZoom = true;
						ZoomSlider.Value = num;
						isSnappingZoom = false;
						return;
					}
				}
				try
				{
					if (ZoomLevelLabel != null)
					{
						ZoomLevelLabel.Text = $"{((ZoomSlider != null) ? ZoomSlider.Value : 1.0):0.00}x";
					}
				}
				catch
				{
				}
				if (isDraggingSelection && GhostImage != null && GhostImage.Source != null)
				{
					try
					{
						double num2 = lastDragScale;
						double num3 = ZoomSlider?.Value ?? 1.0;
						DpiScale dpi = VisualTreeHelper.GetDpi(this);
						Point position = Mouse.GetPosition(CanvasHost);
						int num4 = Math.Max(1, (int)Math.Ceiling(16.0 * num2 * dpi.DpiScaleX));
						int num5 = Math.Max(1, (int)Math.Ceiling(16.0 * num2 * dpi.DpiScaleY));
						int num6 = Math.Max(1, (int)Math.Ceiling(16.0 * num3 * dpi.DpiScaleX));
						int num7 = Math.Max(1, (int)Math.Ceiling(16.0 * num3 * dpi.DpiScaleY));
						int num8 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
						int num9 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
						double left = Canvas.GetLeft(GhostImage);
						double top = Canvas.GetTop(GhostImage);
						GhostImage.Width = (double)ghostLogicalWidth * num3 * dpi.DpiScaleX / dpi.DpiScaleX;
						GhostImage.Height = (double)ghostLogicalHeight * num3 * dpi.DpiScaleY / dpi.DpiScaleY;
						int num10 = (int)Math.Round(left * dpi.DpiScaleX);
						int num11 = (int)Math.Round(top * dpi.DpiScaleY);
						double num12 = (double)(num10 - num8) / (double)num4;
						double num13 = (double)(num11 - num9) / (double)num5;
						int num14 = num8 + (int)Math.Round(num12 * (double)num6);
						int num15 = num9 + (int)Math.Round(num13 * (double)num7);
						double num16 = (double)num14 / dpi.DpiScaleX;
						double num17 = (double)num15 / dpi.DpiScaleY;
						Canvas.SetLeft(GhostImage, num16);
						Canvas.SetTop(GhostImage, num17);
						lastDragScale = num3;
						dragOffset = new Point(position.X - num16, position.Y - num17);
					}
					catch
					{
					}
				}
				UpdateQuickZoomTransform();
				deferZoomRebuild = true;
				if (!isZoomSliderPressed)
				{
					try
					{
						if (MapScrollViewer != null)
						{
							Point position2 = Mouse.GetPosition(MapScrollViewer);
							zoomAnchorViewportX = position2.X;
							zoomAnchorViewportY = position2.Y;
							double num18 = MapScrollViewer?.HorizontalOffset ?? 0.0;
							double num19 = MapScrollViewer?.VerticalOffset ?? 0.0;
							double num20 = num18 + position2.X;
							double num21 = num19 + position2.Y;
							double num22 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
							double num23 = mapViewportPadding;
							zoomAnchorMapX = (num20 - num23) / (16.0 * num22);
							zoomAnchorMapY = (num21 - num23) / (16.0 * num22);
							hasZoomAnchor = true;
						}
					}
					catch
					{
						hasZoomAnchor = false;
					}
					zoomCommitTimer?.Stop();
					zoomCommitTimer?.Start();
				}
			};
		}
		if (SetOptionsButton != null)
		{
			SetOptionsButton.Click += delegate(object s, RoutedEventArgs e)
			{
				SetOptionsButton_Click(s, e);
			};
		}
		if (GridDarknessSlider != null)
		{
			GridDarknessSlider.ValueChanged += GridDarknessSlider_ValueChanged;
		}
		if (SaveButton != null)
		{
			SaveButton.Click += SaveButton_Click;
		}
		if (LoadButton != null)
		{
			LoadButton.Click += LoadButton_Click;
		}
		if (ResizeButton != null)
		{
			ResizeButton.Click += ResizeButton_Click;
		}
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
		if (MapScrollViewer != null)
		{
			MapScrollViewer.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
			{
				try
				{
					CanvasHost_PreviewMouseWheel(MapScrollViewer, e);
				}
				catch
				{
				}
			};
		}
		if (TilesPanel != null)
		{
			TilesPanel.MouseLeftButtonUp += delegate
			{
				if (isSelectingMultipleTiles)
				{
					isSelectingMultipleTiles = false;
					tileSelectionStart = null;
					if (Mouse.Captured != null)
					{
						Mouse.Captured.ReleaseMouseCapture();
					}
				}
			};
		}
		InitDefaultMap();
		base.Loaded += delegate
		{
			ApplyTileboardPosition();
			LoadAssetsOnStart();
			try
			{
				if (showAccurateTileset)
				{
					SetShowAccurateTileset(enabled: true);
				}
			}
			catch
			{
			}
			try
			{
				UpdatePaletteHighlight();
			}
			catch
			{
			}
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (!initialLeftSizingDone)
				{
					UpdateLeftColumnWidth(initial: true);
					Ensure16VisibleOnStartup();
					initialLeftSizingDone = true;
				}
				else
				{
					UpdateLeftColumnWidth();
					UpdateTilesPanelWidth();
				}
				AdjustPaletteSizes();
				UpdateParallaxTint();
				UpdateGroundTint();
				UpdateTileTint();
				Redraw();
			}, DispatcherPriority.Loaded);
			try
			{
				WindowInteropHelper windowInteropHelper = new WindowInteropHelper(this);
				HwndSource.FromHwnd(windowInteropHelper.Handle)?.AddHook(NativeWindowProc);
			}
			catch
			{
			}
		};
		if (MapScrollViewer != null)
		{
			sizeChangedThrottleTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(50.0)
			};
			sizeChangedThrottleTimer.Tick += delegate
			{
				sizeChangedThrottleTimer?.Stop();
				Redraw();
			};
			MapScrollViewer.SizeChanged += delegate
			{
				sizeChangedThrottleTimer?.Stop();
				sizeChangedThrottleTimer?.Start();
			};
			MapScrollViewer.ScrollChanged += delegate
			{
				ClampScrollOffsets();
				UpdateParallaxTransform();
			};
			MapScrollViewer.Loaded += delegate
			{
				Redraw();
				UpdateParallaxTransform();
				ClampScrollOffsets();
			};
		}
		if (TileSizeSlider != null)
		{
			TileSizeSlider.ValueChanged += delegate(object s, RoutedPropertyChangedEventArgs<double> ev)
			{
				if (!suppressManualTileChange)
				{
					manualTileSize = true;
				}
				try
				{
					Slider slider = s as Slider;
					int num = (int)Math.Round(ev.NewValue / 4.0) * 4;
					if (slider != null)
					{
						num = Math.Max((int)slider.Minimum, Math.Min((int)slider.Maximum, num));
						if (Math.Abs(slider.Value - (double)num) > 0.0001)
						{
							slider.Value = num;
							return;
						}
					}
					paletteTileSize = num;
				}
				catch
				{
					paletteTileSize = (int)Math.Round(ev.NewValue);
				}
				PopulateTilesPanel();
				if (TilesPanel != null)
				{
					TilesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
					TilesPanel.Width = double.NaN;
				}
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
						SpritesPanel.Width = double.NaN;
					}
				}
			};
		}
		if (DrawTileButton != null)
		{
			DrawTileButton.Checked += DrawModeButton_Checked;
		}
		if (DrawLineButton != null)
		{
			DrawLineButton.Checked += DrawModeButton_Checked;
		}
		if (DrawSquareButton != null)
		{
			DrawSquareButton.Checked += DrawModeButton_Checked;
		}
		if (DrawCircleButton != null)
		{
			DrawCircleButton.Checked += DrawModeButton_Checked;
		}
		if (DrawEllipseButton != null)
		{
			DrawEllipseButton.Checked += DrawModeButton_Checked;
		}
		if (DrawTriangleButton != null)
		{
			DrawTriangleButton.Checked += DrawModeButton_Checked;
		}
		if (DrawPolygonButton != null)
		{
			DrawPolygonButton.Checked += DrawModeButton_Checked;
		}
		if (DrawTileButton != null)
		{
			DrawTileButton.Unchecked += DrawModeButton_Unchecked;
		}
		if (DrawLineButton != null)
		{
			DrawLineButton.Unchecked += DrawModeButton_Unchecked;
		}
		if (DrawSquareButton != null)
		{
			DrawSquareButton.Unchecked += DrawModeButton_Unchecked;
		}
		if (DrawCircleButton != null)
		{
			DrawCircleButton.Unchecked += DrawModeButton_Unchecked;
		}
		if (DrawEllipseButton != null)
		{
			DrawEllipseButton.Unchecked += DrawModeButton_Unchecked;
		}
		if (DrawTriangleButton != null)
		{
			DrawTriangleButton.Unchecked += DrawModeButton_Unchecked;
		}
		if (DrawPolygonButton != null)
		{
			DrawPolygonButton.Unchecked += DrawModeButton_Unchecked;
		}
		if (HollowCheckBox != null)
		{
			HollowCheckBox.Checked += delegate
			{
				hollowShape = true;
				if (isDeferredDrawing || isConstructingPolygon)
				{
					UpdateDeferredPreview();
				}
			};
		}
		if (HollowCheckBox != null)
		{
			HollowCheckBox.Unchecked += delegate
			{
				hollowShape = false;
				if (isDeferredDrawing || isConstructingPolygon)
				{
					UpdateDeferredPreview();
				}
			};
		}
		if (BrushThicknessSlider != null)
		{
			BrushThicknessSlider.ValueChanged += delegate(object s, RoutedPropertyChangedEventArgs<double> e)
			{
				brushThickness = (int)Math.Max(1.0, Math.Round(e.NewValue));
				if (isDeferredDrawing || isConstructingPolygon)
				{
					UpdateDeferredPreview();
				}
			};
		}
		lastKnownSelectedTile = selectedTile;
		lastKnownSelectedSprite = selectedSprite;
		if (SpriteSizeSlider != null)
		{
			SpriteSizeSlider.ValueChanged += delegate(object s, RoutedPropertyChangedEventArgs<double> ev)
			{
				if (!suppressManualSpriteChange)
				{
					manualSpriteSize = true;
				}
				try
				{
					if (s is Slider slider)
					{
						int val = (int)Math.Round(ev.NewValue / 4.0) * 4;
						val = Math.Max((int)slider.Minimum, Math.Min((int)slider.Maximum, val));
						if (Math.Abs(slider.Value - (double)val) > 0.0001)
						{
							slider.Value = val;
							return;
						}
						paletteSpriteSize = val;
					}
					else
					{
						paletteSpriteSize = (int)Math.Round(ev.NewValue);
					}
				}
				catch
				{
					paletteSpriteSize = (int)Math.Round(ev.NewValue);
				}
				PopulateSpritesPanel();
				if (SpritesPanel != null)
				{
					SpritesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
					SpritesPanel.Width = double.NaN;
				}
			};
		}
		if (TilesPanel != null)
		{
			TilesPanel.SizeChanged += delegate
			{
				AdjustPaletteSizes();
			};
		}
		if (SpritesPanel != null)
		{
			SpritesPanel.SizeChanged += delegate
			{
				AdjustPaletteSizes();
			};
		}
		if (TilesPanel != null)
		{
			TilesPanel.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
			{
				if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
				{
					ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(TilesPanel);
					if (scrollViewer != null && scrollViewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible)
					{
						double num = (invertPinchGesture ? e.Delta : (-e.Delta));
						scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - num / 3.0);
						e.Handled = true;
					}
				}
			};
		}
		if (SpritesPanel != null)
		{
			SpritesPanel.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
			{
				if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
				{
					ScrollViewer scrollViewer = FindVisualChild<ScrollViewer>(SpritesPanel);
					if (scrollViewer != null && scrollViewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible)
					{
						double num = (invertPinchGesture ? e.Delta : (-e.Delta));
						scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - num / 3.0);
						e.Handled = true;
					}
				}
			};
		}
		if (RootGrid != null)
		{
			RootGrid.SizeChanged += delegate
			{
				UpdateTilesPanelWidth();
				if (!manualTileSize)
				{
					UpdateLeftColumnWidth();
				}
			};
		}
		if (MenuFileNew != null)
		{
			MenuFileNew.Click += NewMenuItem_Click;
		}
		if (MenuFileSave != null)
		{
			MenuFileSave.Click += SaveButton_Click;
		}
		if (MenuFileSaveAs != null)
		{
			MenuFileSaveAs.Click += MenuFileSaveAs_Click;
		}
		if (MenuOpenSimulator != null)
		{
			MenuOpenSimulator.Click += MenuOpenSimulator_Click;
		}
		if (MenuConfigureFamidashRom != null)
		{
			MenuConfigureFamidashRom.Click += MenuConfigureFamidashRom_Click;
		}
		if (MenuRunFamidashMesen != null)
		{
			MenuRunFamidashMesen.Click += MenuRunFamidashMesen_Click;
		}
		if (MenuCaptureRamMesen != null)
		{
			MenuCaptureRamMesen.Click += MenuCaptureRamMesen_Click;
		}
		if (MenuBuildAndTest != null)
		{
			MenuBuildAndTest.Click += MenuBuildAndTest_Click;
		}
		if (MenuOverlayAndFollow != null)
		{
			MenuOverlayAndFollow.IsChecked = _overlayAndFollow;
			MenuOverlayAndFollow.Click += delegate
			{
				_overlayAndFollow = MenuOverlayAndFollow.IsChecked;
				try
				{
					SaveEditorSettings();
				}
				catch
				{
				}
			};
		}
		if (MenuCamFollow != null)
		{
			MenuCamFollow.IsChecked = _camFollow;
			MenuCamFollow.Click += delegate
			{
				_camFollow = MenuCamFollow.IsChecked;
				try
				{
					SaveEditorSettings();
				}
				catch
				{
				}
			};
		}
		if (MenuFileLoad != null)
		{
			MenuFileLoad.Click += LoadButton_Click;
		}
		if (MenuFileClose != null)
		{
			MenuFileClose.Click += MenuFileClose_Click;
		}
		if (MenuFileExit != null)
		{
			MenuFileExit.Click += delegate
			{
				Close();
			};
		}
		if (MenuFileSavePF != null)
		{
			MenuFileSavePF.Click += MenuFileSavePF_Click;
		}
		if (MenuFileLoadPF != null)
		{
			MenuFileLoadPF.Click += MenuFileLoadPF_Click;
		}
		base.PreviewKeyDown += MainWindow_KeyDown;
		if (MenuToolPlace != null)
		{
			MenuToolPlace.Click += delegate
			{
				if (PlaceTool != null)
				{
					PlaceTool.IsChecked = true;
				}
			};
		}
		if (MenuToolMove != null)
		{
			MenuToolMove.Click += delegate
			{
				if (MoveTool != null)
				{
					MoveTool.IsChecked = true;
				}
			};
		}
		if (MenuToolErase != null)
		{
			MenuToolErase.Click += delegate
			{
				if (EraseTool != null)
				{
					EraseTool.IsChecked = true;
				}
			};
		}
		if (MenuToolFill != null)
		{
			MenuToolFill.Click += delegate
			{
				if (FillTool != null)
				{
					FillTool.IsChecked = true;
				}
			};
		}
		if (MenuToolSelect != null)
		{
			MenuToolSelect.Click += delegate
			{
				if (SelectTool != null)
				{
					SelectTool.IsChecked = true;
				}
			};
		}
		if (MenuToolLasso != null)
		{
			MenuToolLasso.Click += delegate
			{
				try
				{
					currentSelectMode = SelectMode.Lasso;
					if (FindName("LassoTool") is ToggleButton toggleButton4)
					{
						toggleButton4.IsChecked = true;
						try
						{
							Tool_Checked(toggleButton4, new RoutedEventArgs());
							return;
						}
						catch
						{
							return;
						}
					}
					if (SelectTool != null)
					{
						SelectTool.IsChecked = true;
						try
						{
							Tool_Checked(SelectTool, new RoutedEventArgs());
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
			};
		}
		if (MenuToolEllipse != null)
		{
			MenuToolEllipse.Click += delegate
			{
				try
				{
					currentSelectMode = SelectMode.Ellipse;
					if (SelectTool != null)
					{
						SelectTool.IsChecked = true;
						try
						{
							Tool_Checked(SelectTool, new RoutedEventArgs());
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
			};
		}
		if (MenuToolWand != null)
		{
			MenuToolWand.Click += delegate
			{
				if (MagicWandTool != null)
				{
					MagicWandTool.IsChecked = true;
				}
			};
		}
		if (MenuToolSelectAllSame != null)
		{
			MenuToolSelectAllSame.Click += delegate
			{
				try
				{
					currentSelectMode = SelectMode.AllSame;
					if (SelectTool != null)
					{
						SelectTool.IsChecked = true;
						try
						{
							Tool_Checked(SelectTool, new RoutedEventArgs());
						}
						catch
						{
						}
					}
					PerformSelectAllSameAtCursor();
				}
				catch
				{
				}
			};
		}
		if (MenuToolStructure != null)
		{
			MenuToolStructure.Click += delegate
			{
				try
				{
					if (FindName("StructureTool") is ToggleButton toggleButton4)
					{
						toggleButton4.IsChecked = true;
						try
						{
							Tool_Checked(toggleButton4, new RoutedEventArgs());
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
			};
		}
		if (MenuToolStartPos != null)
		{
			MenuToolStartPos.Click += delegate
			{
				try
				{
					if (FindName("StartPosTool") is ToggleButton toggleButton4)
					{
						toggleButton4.IsChecked = true;
						try
						{
							Tool_Checked(toggleButton4, new RoutedEventArgs());
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
			};
		}
		if (MenuToolReplaceSelected != null)
		{
			MenuToolReplaceSelected.Click += delegate
			{
				try
				{
					currentFillMode = FillMode.ReplaceSelected;
					if (FillTool != null)
					{
						FillTool.IsChecked = true;
						try
						{
							Tool_Checked(FillTool, new RoutedEventArgs());
						}
						catch
						{
						}
					}
					if (StatusText != null)
					{
						StatusText.Text = "Replace Selected: pick a tile then click a selected cell to apply";
					}
				}
				catch
				{
				}
			};
		}
		if (MenuToolOffsetMap != null)
		{
			MenuToolOffsetMap.Click += delegate
			{
				try
				{
					OffsetMapWindow offsetMapWindow = new OffsetMapWindow
					{
						Owner = this
					};
					offsetMapWindow.ShowDialog();
				}
				catch
				{
				}
			};
		}
		try
		{
			if (Menu_Select_Normal != null)
			{
				Menu_Select_Normal.Click += delegate
				{
					currentSelectMode = SelectMode.Normal;
					if (SelectTool != null)
					{
						SelectTool.IsChecked = true;
						try
						{
							Tool_Checked(SelectTool, new RoutedEventArgs());
						}
						catch
						{
						}
					}
				};
			}
			if (Menu_Select_AllSame != null)
			{
				Menu_Select_AllSame.Click += delegate
				{
					currentSelectMode = SelectMode.AllSame;
					if (SelectTool != null)
					{
						SelectTool.IsChecked = true;
						try
						{
							Tool_Checked(SelectTool, new RoutedEventArgs());
						}
						catch
						{
						}
					}
				};
			}
			if (Menu_Select_Lasso != null)
			{
				Menu_Select_Lasso.Click += delegate
				{
					currentSelectMode = SelectMode.Lasso;
					if (FindName("LassoTool") is ToggleButton toggleButton4)
					{
						toggleButton4.IsChecked = true;
						try
						{
							Tool_Checked(toggleButton4, new RoutedEventArgs());
							return;
						}
						catch
						{
							return;
						}
					}
					if (SelectTool != null)
					{
						SelectTool.IsChecked = true;
						try
						{
							Tool_Checked(SelectTool, new RoutedEventArgs());
						}
						catch
						{
						}
					}
				};
			}
			if (Menu_Select_Ellipse != null)
			{
				Menu_Select_Ellipse.Click += delegate
				{
					currentSelectMode = SelectMode.Ellipse;
					if (SelectTool != null)
					{
						SelectTool.IsChecked = true;
						try
						{
							Tool_Checked(SelectTool, new RoutedEventArgs());
						}
						catch
						{
						}
					}
				};
			}
			if (Menu_Fill_Normal != null)
			{
				Menu_Fill_Normal.Click += delegate
				{
					currentFillMode = FillMode.Normal;
					if (FillTool != null)
					{
						FillTool.IsChecked = true;
						try
						{
							Tool_Checked(FillTool, new RoutedEventArgs());
						}
						catch
						{
						}
					}
				};
			}
			if (Menu_Fill_ReplaceSelected != null)
			{
				Menu_Fill_ReplaceSelected.Click += delegate
				{
					try
					{
						if (FillTool != null)
						{
							FillTool.IsChecked = true;
							try
							{
								Tool_Checked(FillTool, new RoutedEventArgs());
							}
							catch
							{
							}
						}
						currentFillMode = FillMode.ReplaceSelected;
						if (StatusText != null)
						{
							StatusText.Text = "Replace Selected: pick a tile then click a selected cell to apply";
						}
					}
					catch
					{
					}
				};
			}
		}
		catch
		{
		}
		if (MenuEditCopy != null)
		{
			MenuEditCopy.Click += delegate
			{
				CopySelection();
			};
		}
		if (MenuEditCut != null)
		{
			MenuEditCut.Click += delegate
			{
				CutSelection();
			};
		}
		if (MenuEditPaste != null)
		{
			MenuEditPaste.Click += delegate
			{
				int num = ((selW > 0 && selH > 0) ? selX : ((lastClickX >= 0) ? lastClickX : lastHoverX));
				int num2 = ((selW > 0 && selH > 0) ? selY : ((lastClickY >= 0) ? lastClickY : lastHoverY));
				if (num >= 0 && num2 >= 0)
				{
					PasteClipboardAt(num, num2);
				}
			};
		}
		if (MenuToolUndo != null)
		{
			MenuToolUndo.Click += delegate
			{
				Undo();
			};
		}
		if (MenuToolRedo != null)
		{
			MenuToolRedo.Click += delegate
			{
				Redo();
			};
		}
		if (MenuColorEditorBackground != null)
		{
			MenuColorEditorBackground.Click += BgColorButton_Click;
		}
		if (MenuColorBackgroundTint != null)
		{
			MenuColorBackgroundTint.Click += BgTintButton_Click;
		}
		if (MenuColorGroundTint != null)
		{
			MenuColorGroundTint.Click += GroundTintButton_Click;
		}
		if (MenuColorTileTint != null)
		{
			MenuColorTileTint.Click += TileTintButton_Click;
		}
		if (MenuColorPlayerTint != null)
		{
			MenuColorPlayerTint.Click += PlayerTintButton_Click;
		}
		if (MenuOptionLegacyTriggers != null)
		{
			MenuOptionLegacyTriggers.Checked += delegate
			{
				useLegacyTriggerOffset = true;
				SaveSettingsWithTriggerOption();
			};
			MenuOptionLegacyTriggers.Unchecked += delegate
			{
				useLegacyTriggerOffset = false;
				SaveSettingsWithTriggerOption();
			};
		}
		if (MenuOptionLockSprites != null)
		{
			MenuOptionLockSprites.IsChecked = lockSpritesToSet;
			MenuOptionLockSprites.Checked += delegate
			{
				try
				{
					SetLockSpritesToSet(enabled: true);
				}
				catch
				{
				}
			};
			MenuOptionLockSprites.Unchecked += delegate
			{
				try
				{
					SetLockSpritesToSet(enabled: false);
				}
				catch
				{
				}
			};
		}
		if (MenuOptionSwapMouseWheel != null)
		{
			MenuOptionSwapMouseWheel.Checked += delegate
			{
				swapMouseWheelScroll = true;
				SaveSettingsWithTriggerOption();
			};
			MenuOptionSwapMouseWheel.Unchecked += delegate
			{
				swapMouseWheelScroll = false;
				SaveSettingsWithTriggerOption();
			};
		}
		if (MenuOptionOpenSimulatorPaused != null)
		{
			MenuOptionOpenSimulatorPaused.Checked += delegate
			{
				loadedOpenSimulatorPaused = true;
				SaveSettingsWithTriggerOption();
			};
			MenuOptionOpenSimulatorPaused.Unchecked += delegate
			{
				loadedOpenSimulatorPaused = false;
				SaveSettingsWithTriggerOption();
			};
		}
		if (MenuOptionNoDeath != null)
		{
			MenuOptionNoDeath.Checked += delegate
			{
				Option_NoDeath = true;
				SaveSettingsWithTriggerOption();
			};
			MenuOptionNoDeath.Unchecked += delegate
			{
				Option_NoDeath = false;
				SaveSettingsWithTriggerOption();
			};
		}
		if (MenuOptionCamMode != null)
		{
			MenuOptionCamMode.Checked += delegate
			{
				Option_CamMode = true;
				SaveSettingsWithTriggerOption();
			};
			MenuOptionCamMode.Unchecked += delegate
			{
				Option_CamMode = false;
				SaveSettingsWithTriggerOption();
			};
		}
		if (MenuOptionSwapPinch != null)
		{
			MenuOptionSwapPinch.Checked += delegate
			{
				invertPinchGesture = true;
				SaveSettingsWithTriggerOption();
			};
			MenuOptionSwapPinch.Unchecked += delegate
			{
				invertPinchGesture = false;
				SaveSettingsWithTriggerOption();
			};
		}
		if (MenuOptionHideBackground != null)
		{
			MenuOptionHideBackground.Checked += delegate
			{
				hideBackground = true;
				SaveSettingsWithTriggerOption();
				Redraw();
			};
			MenuOptionHideBackground.Unchecked += delegate
			{
				hideBackground = false;
				SaveSettingsWithTriggerOption();
				Redraw();
			};
		}
		if (MenuOptionHideGround != null)
		{
			MenuOptionHideGround.Checked += delegate
			{
				hideGround = true;
				SaveSettingsWithTriggerOption();
				Redraw();
			};
			MenuOptionHideGround.Unchecked += delegate
			{
				hideGround = false;
				SaveSettingsWithTriggerOption();
				Redraw();
			};
		}
		if (MenuOptionFastZoom != null)
		{
			MenuOptionFastZoom.Checked += delegate
			{
				useFastZoom = true;
				SaveSettingsWithTriggerOption();
				if (ParallaxImage != null)
				{
					ParallaxImage.Visibility = Visibility.Collapsed;
				}
				if (GroundImage != null)
				{
					GroundImage.Visibility = Visibility.Collapsed;
				}
			};
			MenuOptionFastZoom.Unchecked += delegate
			{
				useFastZoom = false;
				SaveSettingsWithTriggerOption();
				if (ParallaxImage != null)
				{
					ParallaxImage.Visibility = (hideBackground ? Visibility.Collapsed : Visibility.Visible);
				}
				if (GroundImage != null)
				{
					GroundImage.Visibility = (hideGround ? Visibility.Collapsed : Visibility.Visible);
				}
			};
		}
		if (MenuTileboardLeft != null)
		{
			MenuTileboardLeft.Checked += delegate
			{
				if (MenuTileboardRight != null)
				{
					MenuTileboardRight.IsChecked = false;
				}
				if (MenuTileboardTop != null)
				{
					MenuTileboardTop.IsChecked = false;
				}
				if (MenuTileboardBottom != null)
				{
					MenuTileboardBottom.IsChecked = false;
				}
				SetTileboardPosition("LEFT");
			};
		}
		if (MenuTileboardRight != null)
		{
			MenuTileboardRight.Checked += delegate
			{
				if (MenuTileboardLeft != null)
				{
					MenuTileboardLeft.IsChecked = false;
				}
				if (MenuTileboardTop != null)
				{
					MenuTileboardTop.IsChecked = false;
				}
				if (MenuTileboardBottom != null)
				{
					MenuTileboardBottom.IsChecked = false;
				}
				SetTileboardPosition("RIGHT");
			};
		}
		if (MenuTileboardTop != null)
		{
			MenuTileboardTop.Checked += delegate
			{
				if (MenuTileboardLeft != null)
				{
					MenuTileboardLeft.IsChecked = false;
				}
				if (MenuTileboardRight != null)
				{
					MenuTileboardRight.IsChecked = false;
				}
				if (MenuTileboardBottom != null)
				{
					MenuTileboardBottom.IsChecked = false;
				}
				SetTileboardPosition("TOP");
			};
		}
		if (MenuTileboardBottom != null)
		{
			MenuTileboardBottom.Checked += delegate
			{
				if (MenuTileboardLeft != null)
				{
					MenuTileboardLeft.IsChecked = false;
				}
				if (MenuTileboardRight != null)
				{
					MenuTileboardRight.IsChecked = false;
				}
				if (MenuTileboardTop != null)
				{
					MenuTileboardTop.IsChecked = false;
				}
				SetTileboardPosition("BOTTOM");
			};
		}
		if (MenuTileboardHidden != null)
		{
			MenuTileboardHidden.Checked += delegate
			{
				isTileboardHidden = true;
				if (TileboardPanel != null)
				{
					TileboardPanel.Visibility = Visibility.Collapsed;
					GridSplitter gridSplitter = RootGrid?.Children.OfType<GridSplitter>().FirstOrDefault();
					if (gridSplitter != null)
					{
						gridSplitter.Visibility = Visibility.Collapsed;
					}
					if (RootGrid != null && RootGrid.ColumnDefinitions.Count >= 3)
					{
						if (tileboardPosition == "LEFT")
						{
							RootGrid.ColumnDefinitions[0].Width = new GridLength(0.0);
							RootGrid.ColumnDefinitions[1].Width = new GridLength(0.0);
						}
						else if (tileboardPosition == "RIGHT")
						{
							RootGrid.ColumnDefinitions[1].Width = new GridLength(0.0);
							RootGrid.ColumnDefinitions[2].Width = new GridLength(0.0);
						}
						else if (RootGrid.RowDefinitions.Count >= 3)
						{
							if (tileboardPosition == "TOP")
							{
								RootGrid.RowDefinitions[0].Height = new GridLength(0.0);
								RootGrid.RowDefinitions[1].Height = new GridLength(0.0);
							}
							else
							{
								RootGrid.RowDefinitions[1].Height = new GridLength(0.0);
								RootGrid.RowDefinitions[2].Height = new GridLength(0.0);
							}
						}
					}
				}
			};
			MenuTileboardHidden.Unchecked += delegate
			{
				isTileboardHidden = false;
				if (TileboardPanel != null)
				{
					TileboardPanel.Visibility = Visibility.Visible;
					ApplyTileboardPosition();
				}
			};
		}
		if (MenuOptionHideColorTriggers != null)
		{
			MenuOptionHideColorTriggers.Checked += delegate
			{
				hideColorTriggers = true;
				SaveSettingsWithTriggerOption();
				try
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
					Redraw();
				}
			};
			MenuOptionHideColorTriggers.Unchecked += delegate
			{
				hideColorTriggers = false;
				SaveSettingsWithTriggerOption();
				try
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
					Redraw();
				}
			};
		}
		if (MenuSimulatorSize1x != null && MenuSimulatorSize2x != null && MenuSimulatorSize3x != null && MenuSimulatorSize4x != null)
		{
			try
			{
				MenuSimulatorSize1x.IsChecked = loadedSimulatorScale == 1;
				MenuSimulatorSize2x.IsChecked = loadedSimulatorScale == 2;
				MenuSimulatorSize3x.IsChecked = loadedSimulatorScale == 3;
				MenuSimulatorSize4x.IsChecked = loadedSimulatorScale == 4;
			}
			catch
			{
			}
			MenuSimulatorSize1x.Checked += delegate
			{
				try
				{
					SetSimulatorSizeFromMenu(1);
				}
				catch
				{
				}
			};
			MenuSimulatorSize2x.Checked += delegate
			{
				try
				{
					SetSimulatorSizeFromMenu(2);
				}
				catch
				{
				}
			};
			MenuSimulatorSize3x.Checked += delegate
			{
				try
				{
					SetSimulatorSizeFromMenu(3);
				}
				catch
				{
				}
			};
			MenuSimulatorSize4x.Checked += delegate
			{
				try
				{
					SetSimulatorSizeFromMenu(4);
				}
				catch
				{
				}
			};
		}
		if (MenuOptionHideInvisibleSprites != null)
		{
			MenuOptionHideInvisibleSprites.Checked += delegate
			{
				hideInvisibleSprites = true;
				SaveSettingsWithTriggerOption();
				if (previewMode)
				{
					try
					{
						RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					}
					catch
					{
						Redraw();
					}
				}
			};
			MenuOptionHideInvisibleSprites.Unchecked += delegate
			{
				hideInvisibleSprites = false;
				SaveSettingsWithTriggerOption();
				if (previewMode)
				{
					try
					{
						RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					}
					catch
					{
						Redraw();
					}
				}
			};
		}
		if (MenuOptionSuppressCollisionMessages != null)
		{
			MenuOptionSuppressCollisionMessages.Checked += delegate
			{
				suppressCollisionMessages = true;
				SaveSettingsWithTriggerOption();
			};
			MenuOptionSuppressCollisionMessages.Unchecked += delegate
			{
				suppressCollisionMessages = false;
				SaveSettingsWithTriggerOption();
			};
		}
		if (UndoButton != null)
		{
			UndoButton.Click += delegate
			{
				Undo();
			};
		}
		if (RedoButton != null)
		{
			RedoButton.Click += delegate
			{
				Redo();
			};
		}
		if (CopyButton != null)
		{
			CopyButton.Click += delegate
			{
				CopySelection();
			};
		}
		if (CutButton != null)
		{
			CutButton.Click += delegate
			{
				CutSelection();
			};
		}
		if (PasteButton != null)
		{
			PasteButton.Click += delegate
			{
				int num = ((selW > 0 && selH > 0) ? selX : ((lastClickX >= 0) ? lastClickX : lastHoverX));
				int num2 = ((selW > 0 && selH > 0) ? selY : ((lastClickY >= 0) ? lastClickY : lastHoverY));
				if (num >= 0 && num2 >= 0)
				{
					PasteClipboardAt(num, num2);
				}
			};
		}
		if (PreviewModeCheckbox != null)
		{
			PreviewModeCheckbox.Checked += async delegate
			{
				Debug.WriteLine($"Preview Mode CHECKED at {DateTime.Now}");
				previewMode = true;
				StartPreviewTimer();
				LoadingWindow loading = null;
				try
				{
					loading = new LoadingWindow
					{
						Owner = this
					};
					loading.SetMessage("Enabling preview mode... rendering frames");
					loading.Show();
					await Dispatcher.Yield(DispatcherPriority.Background);
					PrecomputeTintedCachesAsync();
					RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				finally
				{
					loading?.Close();
				}
			};
			PreviewModeCheckbox.Unchecked += async delegate
			{
				Debug.WriteLine($"Preview Mode UNCHECKED at {DateTime.Now}");
				previewMode = false;
				StopPreviewTimer();
				animationFrame = 0;
				LoadingWindow loading = null;
				try
				{
					loading = new LoadingWindow
					{
						Owner = this
					};
					loading.SetMessage("Disabling preview mode... rebuilding frames");
					loading.Show();
					await Dispatcher.Yield(DispatcherPriority.Background);
					PrecomputeTintedCachesAsync();
					if (portalsWb != null)
					{
						try
						{
							portalsWb.Lock();
							IntPtr pBackBuffer = portalsWb.BackBuffer;
							if (pBackBuffer != IntPtr.Zero)
							{
								int stride = portalsWb.BackBufferStride;
								int bytesTotal = stride * cachedPixelHeight;
								byte* ptr = (byte*)pBackBuffer.ToPointer();
								for (int i = 0; i < bytesTotal; i++)
								{
									ptr[i] = 0;
								}
							}
							portalsWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
						}
						finally
						{
							try
							{
								portalsWb.Unlock();
							}
							catch
							{
							}
						}
					}
					RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				finally
				{
					loading?.Close();
				}
			};
		}
		if (PlaceTool != null)
		{
			PlaceTool.Checked += Tool_Checked;
		}
		if (MoveTool != null)
		{
			MoveTool.Checked += Tool_Checked;
		}
		if (EraseTool != null)
		{
			EraseTool.Checked += Tool_Checked;
		}
		if (FillTool != null)
		{
			FillTool.Checked += Tool_Checked;
		}
		if (SelectTool != null)
		{
			SelectTool.Checked += Tool_Checked;
		}
		if (FindName("LassoTool") is ToggleButton toggleButton)
		{
			toggleButton.Checked += Tool_Checked;
		}
		if (MagicWandTool != null)
		{
			MagicWandTool.Checked += Tool_Checked;
		}
		try
		{
			if (FindName("StructureTool") is ToggleButton toggleButton2)
			{
				toggleButton2.Checked += Tool_Checked;
			}
		}
		catch
		{
		}
		try
		{
			if (FindName("StartPosTool") is ToggleButton toggleButton3)
			{
				toggleButton3.Checked += Tool_Checked;
			}
		}
		catch
		{
		}
		base.PreviewKeyDown += MainWindow_PreviewKeyDown;
		base.PreviewKeyUp += MainWindow_PreviewKeyUp;
		if (MapScrollViewer != null)
		{
			MapScrollViewer.ManipulationStarting += MapScrollViewer_ManipulationStarting;
			MapScrollViewer.ManipulationCompleted += MapScrollViewer_ManipulationCompleted;
			MapScrollViewer.ManipulationDelta += MapScrollViewer_ManipulationDelta;
			if (FindName("CanvasHost") is Canvas canvas)
			{
				canvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown_Lasso;
				canvas.MouseMove += Canvas_MouseMove_Lasso;
				canvas.MouseLeftButtonUp += Canvas_MouseLeftButtonUp_Lasso;
			}
		}
	}

	private void MenuOpenSimulator_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			OpenSimulatorWindow();
		}
		catch
		{
		}
	}

	private void Canvas_MouseLeftButtonDown_Lasso(object? sender, MouseButtonEventArgs e)
	{
		try
		{
			if (((FindName("LassoTool") is ToggleButton { IsChecked: var isChecked } && isChecked == true) || (SelectTool != null && SelectTool.IsChecked == true && currentSelectMode == SelectMode.Lasso)) && sender is Canvas canvas)
			{
				isLassoActive = true;
				lassoPoints.Clear();
				Point position = e.GetPosition(canvas);
				lassoPoints.Add(position);
				canvas.CaptureMouse();
				RenderLassoPath();
				e.Handled = true;
			}
		}
		catch
		{
		}
	}

	private void Canvas_MouseMove_Lasso(object? sender, System.Windows.Input.MouseEventArgs e)
	{
		try
		{
			if (isLassoActive && sender is Canvas relativeTo)
			{
				Point position = e.GetPosition(relativeTo);
				if (lassoPoints.Count == 0 || Distance(lassoPoints[lassoPoints.Count - 1], position) > 2.0)
				{
					lassoPoints.Add(position);
					RenderLassoPath();
				}
				e.Handled = true;
			}
		}
		catch
		{
		}
	}

	private void Canvas_MouseLeftButtonUp_Lasso(object? sender, MouseButtonEventArgs e)
	{
		try
		{
			if (isLassoActive && sender is Canvas canvas)
			{
				Point position = e.GetPosition(canvas);
				lassoPoints.Add(position);
				isLassoActive = false;
				try
				{
					canvas.ReleaseMouseCapture();
				}
				catch
				{
				}
				RenderLassoPath();
				ApplyLassoSelection();
				e.Handled = true;
			}
		}
		catch
		{
		}
	}

	private double Distance(Point a, Point b)
	{
		double num = a.X - b.X;
		double num2 = a.Y - b.Y;
		return Math.Sqrt(num * num + num2 * num2);
	}

	private void ApplyLassoSelection()
	{
		try
		{
			if (lassoPoints == null || lassoPoints.Count < 3)
			{
				return;
			}
			PathGeometry pathGeometry = new PathGeometry();
			PathFigure pathFigure = new PathFigure();
			pathFigure.StartPoint = lassoPoints[0];
			pathFigure.IsClosed = true;
			pathFigure.IsFilled = true;
			PolyLineSegment value = new PolyLineSegment(lassoPoints.ToArray(), isStroked: true);
			pathFigure.Segments.Add(value);
			pathGeometry.Figures.Add(pathFigure);
			HashSet<int> hashSet = new HashSet<int>();
			double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
			bool flag = tilesLayerActive || (!tilesLayerActive && !spritesLayerActive);
			bool flag2 = spritesLayerActive || (!tilesLayerActive && !spritesLayerActive);
			for (int i = 0; i < mapHeight; i++)
			{
				for (int j = 0; j < mapWidth; j++)
				{
					double x = (double)(j * 16) + 8.0 + mapViewportPadding;
					double y = (double)(i * 16) + 8.0 + mapViewportPadding;
					if (pathGeometry.FillContains(new Point(x, y)))
					{
						int num2 = i * mapWidth + j;
						bool flag3 = flag && tiles[num2] != -1;
						bool flag4 = flag2 && sprites[num2] != -1;
						if (flag3 || flag4)
						{
							hashSet.Add(num2);
						}
					}
				}
			}
			if (hashSet.Count == 0)
			{
				ClearSelection();
				return;
			}
			if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
			{
				selectionSet = hashSet;
			}
			else
			{
				foreach (int item in hashSet)
				{
					selectionSet.Add(item);
				}
			}
			selectionIsLarge = selectionSet.Count > 5000;
			int num3 = int.MaxValue;
			int num4 = int.MaxValue;
			int num5 = int.MinValue;
			int num6 = int.MinValue;
			foreach (int item2 in selectionSet)
			{
				int num7 = item2 % mapWidth;
				int num8 = item2 / mapWidth;
				if (num7 < num3)
				{
					num3 = num7;
				}
				if (num8 < num4)
				{
					num4 = num8;
				}
				if (num7 > num5)
				{
					num5 = num7;
				}
				if (num8 > num6)
				{
					num6 = num8;
				}
			}
			selX = num3;
			selY = num4;
			selW = num5 - num3 + 1;
			selH = num6 - num4 + 1;
			PopulateSelectionArraysFromSet();
			UpdateSelectionVisuals(selX, selY, selW, selH);
			try
			{
				RenderMaskToCanvasAsync(SelectionOverlay, selectionSet.ToArray(), (ZoomSlider != null) ? ZoomSlider.Value : 1.0, VisualTreeHelper.GetDpi(this));
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void MenuOpenFmsPlayer_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Debug.WriteLine("MenuOpenFmsPlayer_Click called but the menu item was removed.");
		}
		catch
		{
		}
	}

	private void CanvasHost_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
	{
		if (MapScrollViewer == null)
		{
			return;
		}
		if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
		{
			e.Handled = true;
			if (ZoomSlider == null)
			{
				return;
			}
			try
			{
				if (CanvasHost != null)
				{
					CanvasHost.Focus();
					Keyboard.Focus(CanvasHost);
				}
			}
			catch
			{
			}
			double value = ZoomSlider.Value;
			double num = (invertPinchGesture ? 1.0 : (-1.0));
			int num2 = e.Delta / 120;
			double num3 = value + num * (double)num2 * 0.25;
			if (num3 < 0.25)
			{
				num3 = 0.25;
			}
			if (num3 > 4.0)
			{
				num3 = 4.0;
			}
			num3 = Math.Round(num3 * 4.0) / 4.0;
			if (!(Math.Abs(num3 - value) < 0.001))
			{
				Point position = e.GetPosition(MapScrollViewer);
				double num4 = MapScrollViewer?.HorizontalOffset ?? 0.0;
				double num5 = MapScrollViewer?.VerticalOffset ?? 0.0;
				double num6 = num4 + position.X;
				double num7 = num5 + position.Y;
				double num8 = (num6 - mapViewportPadding) / (16.0 * value);
				double num9 = (num7 - mapViewportPadding) / (16.0 * value);
				try
				{
					zoomAnchorViewportX = position.X;
					zoomAnchorViewportY = position.Y;
					zoomAnchorMapX = num8;
					zoomAnchorMapY = num9;
					hasZoomAnchor = true;
				}
				catch
				{
					hasZoomAnchor = false;
				}
				isSnappingZoom = true;
				if (ZoomSlider != null)
				{
					ZoomSlider.Value = num3;
				}
				isSnappingZoom = false;
				double num10 = mapViewportPadding + num8 * 16.0 * num3;
				double num11 = mapViewportPadding + num9 * 16.0 * num3;
				double val = num10 - position.X;
				double val2 = num11 - position.Y;
				double val3 = Math.Max(0.0, (CanvasHost?.ActualWidth ?? 0.0) - SafeViewportWidth());
				double val4 = Math.Max(0.0, (CanvasHost?.ActualHeight ?? 0.0) - SafeViewportHeight());
				val = Math.Max(0.0, Math.Min(val3, val));
				val2 = Math.Max(0.0, Math.Min(val4, val2));
				if (MapScrollViewer != null)
				{
					MapScrollViewer?.ScrollToHorizontalOffset(val);
					MapScrollViewer?.ScrollToVerticalOffset(val2);
				}
				ClampScrollOffsets();
				try
				{
					deferZoomRebuild = true;
					UpdateQuickZoomTransform();
				}
				catch
				{
				}
				try
				{
					zoomCommitTimer?.Stop();
					zoomCommitTimer?.Start();
				}
				catch
				{
				}
			}
		}
		else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
		{
			e.Handled = true;
			double num12 = (double)e.Delta * 0.5;
			if (!swapMouseWheelScroll)
			{
				double offset = (MapScrollViewer?.VerticalOffset ?? 0.0) - num12;
				MapScrollViewer?.ScrollToVerticalOffset(offset);
			}
			else
			{
				double offset2 = (MapScrollViewer?.HorizontalOffset ?? 0.0) - num12;
				MapScrollViewer?.ScrollToHorizontalOffset(offset2);
			}
		}
		else
		{
			e.Handled = true;
			double num13 = (double)e.Delta * 0.5;
			if (!swapMouseWheelScroll)
			{
				double offset3 = (MapScrollViewer?.HorizontalOffset ?? 0.0) - num13;
				MapScrollViewer?.ScrollToHorizontalOffset(offset3);
			}
			else
			{
				double offset4 = (MapScrollViewer?.VerticalOffset ?? 0.0) - num13;
				MapScrollViewer?.ScrollToVerticalOffset(offset4);
			}
		}
	}

	private async void ResizeMap(int newWidth, int newHeight)
	{
		LoadingWindow loadingWindow = null;
		bool isLargeResize = newWidth * newHeight > 50000;
		if (isLargeResize)
		{
			loadingWindow = new LoadingWindow
			{
				Owner = this,
				WindowStartupLocation = WindowStartupLocation.CenterOwner
			};
			loadingWindow.SetMessage($"Resizing map to {newWidth}x{newHeight}...\nPlease wait.");
			loadingWindow.Show();
			await Task.Delay(50);
			await Dispatcher.Yield(DispatcherPriority.Render);
		}
		try
		{
			int[] newTiles = Enumerable.Repeat(-1, newWidth * newHeight).ToArray();
			int[] newSprites = Enumerable.Repeat(-1, newWidth * newHeight).ToArray();
			int copyW = Math.Min(mapWidth, newWidth);
			if (newHeight >= mapHeight)
			{
				int yOffset = newHeight - mapHeight;
				for (int y = 0; y < mapHeight; y++)
				{
					for (int x = 0; x < copyW; x++)
					{
						newTiles[(y + yOffset) * newWidth + x] = tiles[y * mapWidth + x];
						newSprites[(y + yOffset) * newWidth + x] = sprites[y * mapWidth + x];
					}
				}
			}
			else
			{
				int startOldY = mapHeight - newHeight;
				for (int i = 0; i < newHeight; i++)
				{
					for (int j = 0; j < copyW; j++)
					{
						newTiles[i * newWidth + j] = tiles[(i + startOldY) * mapWidth + j];
						newSprites[i * newWidth + j] = sprites[(i + startOldY) * mapWidth + j];
					}
				}
			}
			if (!suppressUndoRecording)
			{
				MapResizeAction action = new MapResizeAction(oldTiles: tiles, oldW: mapWidth, oldH: mapHeight, newW: newWidth, newH: newHeight, newTiles: newTiles);
				undoStack.Push(action);
				redoStack.Clear();
			}
			mapWidth = newWidth;
			mapHeight = newHeight;
			tiles = newTiles;
			sprites = newSprites;
			spritePixelOffsets.Clear();
			if (WidthBox != null)
			{
				WidthBox.Text = mapWidth.ToString();
			}
			if (HeightBox != null)
			{
				HeightBox.Text = mapHeight.ToString();
			}
			if (isLargeResize && loadingWindow != null)
			{
				loadingWindow.SetMessage($"Rebuilding map layers ({newWidth}x{newHeight})...\nPlease wait.");
				await Task.Delay(50);
				await Dispatcher.Yield(DispatcherPriority.Render);
			}
			backgroundDirty = true;
			gridDirty = true;
			cachedPixelWidth = 0;
			cachedPixelHeight = 0;
			try
			{
				RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
			}
			catch
			{
			}
			ClampScrollOffsets();
			Redraw();
		}
		finally
		{
			loadingWindow?.Close();
		}
	}

	private void BgColorButton_Click(object? sender, RoutedEventArgs e)
	{
		Color initialColor = ((SolidColorBrush)base.Resources["AppBackgroundBrush"])?.Color ?? Color.FromArgb(byte.MaxValue, 40, 40, 40);
		ColorWheelPickerWindow colorWheelPickerWindow = new ColorWheelPickerWindow(initialColor)
		{
			Owner = this
		};
		if (colorWheelPickerWindow.ShowDialog() == true)
		{
			Color selectedColor = colorWheelPickerWindow.SelectedColor;
			if (base.Resources.Contains("AppBackgroundBrush") && base.Resources["AppBackgroundBrush"] is SolidColorBrush solidColorBrush)
			{
				solidColorBrush.Color = selectedColor;
			}
			mapBackground = new SolidColorBrush(selectedColor);
			SaveSettings(selectedColor);
			Redraw();
		}
	}

	private void BgTintButton_Click(object? sender, RoutedEventArgs e)
	{
		Color initial = backgroundTint;
		ColorPickerWindow colorPickerWindow = new ColorPickerWindow(initial, allowAlpha: false, (backgroundTint.A == 0) ? 1 : (-1))
		{
			Owner = this
		};
		colorPickerWindow.Title = "Pick Background Tint (RGBA)";
		Action<Color> value = delegate(Color c)
		{
			backgroundTint = c;
			UpdateParallaxTint();
			try
			{
				base.Dispatcher.Invoke(RedrawViewportOnly, DispatcherPriority.Render);
			}
			catch
			{
			}
		};
		colorPickerWindow.ColorChanged += value;
		if (colorPickerWindow.ShowDialog() == true)
		{
			backgroundTint = colorPickerWindow.SelectedColor;
			UpdateParallaxTint();
			Redraw();
			if (colorPickerWindow.SetAsDefault)
			{
				defaultBackgroundTint = backgroundTint;
				try
				{
					SaveSettingsWithTriggerOption();
				}
				catch
				{
				}
			}
			if (StatusText != null)
			{
				StatusText.Text = $"BgTint set ARGB={backgroundTint.A},{backgroundTint.R},{backgroundTint.G},{backgroundTint.B} parallaxToned={((parallaxTonedImages != null) ? parallaxTonedImages.Length : 0)}";
			}
		}
		else
		{
			backgroundTint = initial;
			UpdateParallaxTint();
			Redraw();
		}
		colorPickerWindow.ColorChanged -= value;
	}

	private void GroundTintButton_Click(object? sender, RoutedEventArgs e)
	{
		Color initial = groundTint;
		ColorPickerWindow colorPickerWindow = new ColorPickerWindow(initial, allowAlpha: false, (groundTint.A == 0) ? 29 : (-1))
		{
			Owner = this
		};
		colorPickerWindow.Title = "Pick Ground Tint (RGBA)";
		Action<Color> value = delegate(Color c)
		{
			groundTint = c;
			UpdateGroundTint();
			try
			{
				base.Dispatcher.Invoke(RedrawViewportOnly, DispatcherPriority.Render);
			}
			catch
			{
			}
		};
		colorPickerWindow.ColorChanged += value;
		if (colorPickerWindow.ShowDialog() == true)
		{
			groundTint = colorPickerWindow.SelectedColor;
			UpdateGroundTint();
			Redraw();
			if (colorPickerWindow.SetAsDefault)
			{
				defaultGroundTint = groundTint;
				try
				{
					SaveSettingsWithTriggerOption();
				}
				catch
				{
				}
			}
			if (StatusText != null)
			{
				StatusText.Text = $"GroundTint set ARGB={groundTint.A},{groundTint.R},{groundTint.G},{groundTint.B} groundToned={((groundTonedImages != null) ? groundTonedImages.Length : 0)}";
			}
		}
		else
		{
			groundTint = initial;
			UpdateGroundTint();
			Redraw();
		}
		colorPickerWindow.ColorChanged -= value;
	}

	private void TileTintButton_Click(object? sender, RoutedEventArgs e)
	{
		Color initial = tileTint;
		ColorPickerWindow colorPickerWindow = new ColorPickerWindow(initial, allowAlpha: false)
		{
			Owner = this
		};
		colorPickerWindow.Title = "Pick Tile Tint (RGBA)";
		Action<Color> value = delegate(Color c)
		{
			tileTint = c;
			UpdateTileTint();
			try
			{
				base.Dispatcher.Invoke(RedrawViewportOnly, DispatcherPriority.Render);
			}
			catch
			{
			}
		};
		colorPickerWindow.ColorChanged += value;
		if (colorPickerWindow.ShowDialog() == true)
		{
			tileTint = colorPickerWindow.SelectedColor;
			UpdateTileTint();
			Redraw();
			if (colorPickerWindow.SetAsDefault)
			{
				defaultTileTint = tileTint;
				try
				{
					SaveSettingsWithTriggerOption();
				}
				catch
				{
				}
			}
			if (StatusText != null)
			{
				StatusText.Text = $"TileTint set ARGB={tileTint.A},{tileTint.R},{tileTint.G},{tileTint.B} tilesToned={((tileTonedImages != null) ? tileTonedImages.Length : 0)}";
			}
		}
		else
		{
			tileTint = initial;
			UpdateTileTint();
			Redraw();
		}
		colorPickerWindow.ColorChanged -= value;
	}

	private void PlayerTintButton_Click(object? sender, RoutedEventArgs e)
	{
		Color initial = playerTint;
		ColorPickerWindow colorPickerWindow = new ColorPickerWindow(initial, allowAlpha: false)
		{
			Owner = this
		};
		colorPickerWindow.Title = "Pick Player Color (tints decorations)";
		Color color = playerTint;
		bool flag = playerTintEnabled;
		Action<Color> value = delegate(Color c)
		{
			playerTint = c;
			playerTintEnabled = true;
			try
			{
				scaledTileCaches.Clear();
			}
			catch
			{
			}
			try
			{
				PopulateTilesPanel();
			}
			catch
			{
			}
			if (previewMode)
			{
				try
				{
					ClearTintedCaches();
				}
				catch
				{
				}
				try
				{
					RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
					Redraw();
				}
				try
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					return;
				}
				catch
				{
					return;
				}
			}
			try
			{
				RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
			}
			catch
			{
				Redraw();
			}
		};
		colorPickerWindow.ColorChanged += value;
		bool? flag2 = colorPickerWindow.ShowDialog();
		colorPickerWindow.ColorChanged -= value;
		if (flag2 == true)
		{
			playerTint = colorPickerWindow.SelectedColor;
			playerTintEnabled = true;
			ClearTintedCaches();
			if (colorPickerWindow.SetAsDefault)
			{
				try
				{
					SaveSettingsWithTriggerOption();
				}
				catch
				{
				}
			}
			Redraw();
		}
		else
		{
			playerTint = color;
			playerTintEnabled = flag;
			Redraw();
		}
	}

	private void StartPreviewTimer()
	{
		if (previewTimer == null)
		{
			previewTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(16.666666666666668)
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

	private void GetVisibleTileBounds(out int minX, out int maxX, out int minY, out int maxY)
	{
		minX = 0;
		maxX = Math.Max(0, mapWidth - 1);
		minY = 0;
		maxY = Math.Max(0, mapHeight - 1);
		try
		{
			if (MapScrollViewer != null)
			{
				double num = ZoomSlider?.Value ?? 1.0;
				double num2 = mapViewportPadding;
				double num3 = MapScrollViewer?.HorizontalOffset ?? 0.0;
				double num4 = MapScrollViewer?.VerticalOffset ?? 0.0;
				double num5 = num3 + SafeViewportWidth();
				double num6 = num4 + SafeViewportHeight();
				int val = (int)Math.Floor((num3 - num2) / (16.0 * num));
				int val2 = (int)Math.Floor((num5 - num2) / (16.0 * num));
				int val3 = (int)Math.Floor((num4 - num2) / (16.0 * num));
				int val4 = (int)Math.Floor((num6 - num2) / (16.0 * num));
				minX = Math.Max(0, Math.Min(mapWidth - 1, val));
				maxX = Math.Max(0, Math.Min(mapWidth - 1, val2));
				minY = Math.Max(0, Math.Min(mapHeight - 1, val3));
				maxY = Math.Max(0, Math.Min(mapHeight - 1, val4));
			}
		}
		catch
		{
		}
	}

	private void PreviewTimer_Tick(object? sender, EventArgs e)
	{
		timerTicks++;
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		int num2 = ((!(num <= 1.0)) ? 1 : 2);
		if (timerTicks % num2 != 0)
		{
			return;
		}
		animationFrame++;
		if (animationFrame % 60 == 0)
		{
			bool flag = animationFrame / 1 % 2 == 1;
			Debug.WriteLine($"Animation frame {animationFrame}, showing frame {((!flag) ? 1 : 2)}, sawFrame1Tiles={sawFrame1Tiles?.Length}, sawFrame2Tiles={sawFrame2Tiles?.Length}, smallSawFrame1={smallSawFrame1Tiles?.Length}, smallSawFrame2={smallSawFrame2Tiles?.Length}, largeSawFrame1={largeSawFrame1Tiles?.Length}, largeSawFrame2={largeSawFrame2Tiles?.Length}");
		}
		if (tilesWb != null && ((sawFrame1Tiles != null && sawFrame2Tiles != null) || (smallSawFrame1Tiles != null && smallSawFrame2Tiles != null) || (largeSawFrame1Tiles != null && largeSawFrame2Tiles != null)))
		{
			GetVisibleTileBounds(out var _, out var _, out var _, out var _);
			int minX2 = 0;
			int maxX2 = mapWidth - 1;
			int minY2 = 0;
			int maxY2 = mapHeight - 1;
			GetVisibleTileBounds(out minX2, out maxX2, out minY2, out maxY2);
			GetVisibleTileBounds(out var minX3, out var maxX3, out var minY3, out var maxY3);
			bool flag2 = false;
			for (int i = minY3; i <= maxY3; i++)
			{
				if (flag2)
				{
					break;
				}
				for (int j = minX3; j <= maxX3; j++)
				{
					int num3 = tiles[i * mapWidth + j];
					if ((num3 >= 8 && num3 <= 11) || num3 == 4 || num3 == 125 || num3 == 127 || (num3 >= 116 && num3 <= 124))
					{
						flag2 = true;
						break;
					}
				}
			}
			if (flag2)
			{
				double num4 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
				DpiScale dpi = VisualTreeHelper.GetDpi(this);
				int num5 = Math.Max(1, (int)Math.Ceiling(16.0 * num4 * dpi.DpiScaleX));
				int num6 = Math.Max(1, (int)Math.Ceiling(16.0 * num4 * dpi.DpiScaleY));
				tilesWb.Lock();
				try
				{
					for (int k = minY3; k <= maxY3; k++)
					{
						for (int l = minX3; l <= maxX3; l++)
						{
							int num7 = tiles[k * mapWidth + l];
							if ((num7 >= 8 && num7 <= 11) || num7 == 4 || num7 == 125 || num7 == 127 || (num7 >= 116 && num7 <= 124))
							{
								UpdateTileBitmapAtLocked(l, k, num4, mapViewportPadding, num5, num6, dpi);
							}
						}
					}
					int num8 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
					int num9 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
					int num10 = Math.Max(0, num8 + minX3 * num5);
					int num11 = Math.Max(0, num9 + minY3 * num6);
					int val = Math.Min(cachedPixelWidth - num10, (maxX3 - minX3 + 1) * num5);
					int val2 = Math.Min(cachedPixelHeight - num11, (maxY3 - minY3 + 1) * num6);
					tilesWb.AddDirtyRect(new Int32Rect(num10, num11, Math.Max(1, val), Math.Max(1, val2)));
				}
				finally
				{
					tilesWb.Unlock();
				}
			}
		}
		bool flag3 = (yellowOrbFrame1 != null && yellowOrbFrame2 != null && yellowOrbFrame3 != null && yellowOrbFrame4 != null) || (blueOrbFrame1 != null && blueOrbFrame2 != null && blueOrbFrame3 != null && blueOrbFrame4 != null) || (pinkOrbFrame1 != null && pinkOrbFrame2 != null && pinkOrbFrame3 != null && pinkOrbFrame4 != null) || (greenOrbFrame1 != null && greenOrbFrame2 != null && greenOrbFrame3 != null && greenOrbFrame4 != null) || (redOrbFrame1 != null && redOrbFrame2 != null && redOrbFrame3 != null && redOrbFrame4 != null) || (blackOrbFrame1 != null && blackOrbFrame2 != null && blackOrbFrame3 != null && blackOrbFrame4 != null) || (redPadFrame1 != null && redPadFrame2 != null && redPadFrame3 != null && redPadFrame4 != null) || (whiteOrbFrame1 != null && whiteOrbFrame2 != null && whiteOrbFrame3 != null && whiteOrbFrame4 != null) || (coinFrame1 != null && coinFrame2 != null && coinFrame3 != null && coinFrame4 != null) || (dashOrbRightFrame1 != null && dashOrbRightFrame2 != null) || (dashGravityOrbRightFrame1 != null && dashGravityOrbRightFrame2 != null) || (dashOrb45UpFrame1 != null && dashOrb45UpFrame2 != null) || (dashGravityOrb45UpFrame1 != null && dashGravityOrb45UpFrame2 != null) || (dashOrb45DownFrame1 != null && dashOrb45DownFrame2 != null) || (dashGravityOrb45DownFrame1 != null && dashGravityOrb45DownFrame2 != null) || (dashOrbUpFrame1 != null && dashOrbUpFrame2 != null) || (dashGravityOrbUpFrame1 != null && dashGravityOrbUpFrame2 != null) || (dashOrbDownFrame1 != null && dashOrbDownFrame2 != null) || (dashGravityOrbDownFrame1 != null && dashGravityOrbDownFrame2 != null) || (teleportOrbEnterFrame1 != null && teleportOrbEnterFrame2 != null) || (teleportOrbExitFrame1 != null && teleportOrbExitFrame2 != null) || (spiderOrbDownFrame1 != null && spiderOrbDownFrame2 != null) || (spiderOrbUpFrame1 != null && spiderOrbUpFrame2 != null) || (starFrame1 != null && starFrame2 != null) || (pulsingBallFrame1 != null && pulsingBallFrame2 != null) || (musicNoteFrame1 != null && musicNoteFrame2 != null) || (diamondFrame1 != null && diamondFrame2 != null) || (diamondHalfFrame1 != null && diamondHalfFrame2 != null) || (questionMarkFrame1 != null && questionMarkFrame2 != null) || (exclamationFrame1 != null && exclamationFrame2 != null) || (xFrame1 != null && xFrame2 != null) || (poleShortFrame1 != null && poleShortFrame2 != null) || (poleShortUpsideDownFrame1 != null && poleShortUpsideDownFrame2 != null) || (poleMediumFrame1 != null && poleMediumFrame2 != null) || (poleMediumUpsideDownFrame1 != null && poleMediumUpsideDownFrame2 != null) || (poleLongFrame1 != null && poleLongFrame2 != null) || (poleLongUpsideDownFrame1 != null && poleLongUpsideDownFrame2 != null) || (poleLeftShortFrame1 != null && poleLeftShortFrame2 != null) || (poleLeftMediumFrame1 != null && poleLeftMediumFrame2 != null) || (poleRightShortFrame1 != null && poleRightShortFrame2 != null) || (poleRightMediumFrame1 != null && poleRightMediumFrame2 != null);
		if (spritesWb != null && flag3)
		{
			GetVisibleTileBounds(out var minX4, out var maxX4, out var minY4, out var maxY4);
			bool flag4 = false;
			for (int m = minY4; m <= maxY4; m++)
			{
				if (flag4)
				{
					break;
				}
				for (int n = minX4; n <= maxX4; n++)
				{
					int num12 = sprites[m * mapWidth + n];
					if (num12 == 11 || num12 == 31 || num12 == 41 || num12 == 5 || num12 == 6 || num12 == 39 || num12 == 40 || num12 == 68 || num12 == 123 || num12 == 124 || num12 == 122 || num12 == 7 || num12 == 26 || num12 == 27 || num12 == 110 || num12 == 82 || num12 == 83 || num12 == 10 || num12 == 12 || num12 == 13 || num12 == 14 || num12 == 253 || num12 == 254 || num12 == 37 || num12 == 38 || num12 == 69 || num12 == 70 || num12 == 76 || num12 == 77 || num12 == 80 || num12 == 81 || num12 == 91 || num12 == 92 || num12 == 93 || num12 == 94 || num12 == 89 || num12 == 90 || num12 == 84 || num12 == 85 || num12 == 54 || num12 == 50 || num12 == 51 || num12 == 52 || num12 == 53 || num12 == 55 || num12 == 44 || num12 == 60 || num12 == 56 || num12 == 57 || num12 == 62 || num12 == 63 || num12 == 43 || num12 == 59 || num12 == 42 || num12 == 58 || num12 == 73 || num12 == 74)
					{
						flag4 = true;
						if (animationFrame % 60 == 0)
						{
							Debug.WriteLine($"Found animated orb sprite 0x{num12:X2} at ({n},{m}), will rebuild sprites");
						}
						break;
					}
				}
			}
			if (flag4)
			{
				double num13 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
				DpiScale dpi2 = VisualTreeHelper.GetDpi(this);
				int num14 = Math.Max(1, (int)Math.Ceiling(16.0 * num13 * dpi2.DpiScaleX));
				int num15 = Math.Max(1, (int)Math.Ceiling(16.0 * num13 * dpi2.DpiScaleY));
				spritesWb.Lock();
				try
				{
					for (int num16 = minY4; num16 <= maxY4; num16++)
					{
						for (int num17 = minX4; num17 <= maxX4; num17++)
						{
							int num18 = sprites[num16 * mapWidth + num17];
							if (num18 == 11 || num18 == 31 || num18 == 41 || num18 == 5 || num18 == 6 || num18 == 39 || num18 == 40 || num18 == 68 || num18 == 123 || num18 == 124 || num18 == 82 || num18 == 83 || num18 == 10 || num18 == 12 || num18 == 13 || num18 == 14 || num18 == 253 || num18 == 254 || num18 == 37 || num18 == 38 || num18 == 101 || num18 == 122 || num18 == 7 || num18 == 26 || num18 == 27 || num18 == 110 || num18 == 69 || num18 == 70 || num18 == 76 || num18 == 77 || num18 == 80 || num18 == 81 || num18 == 91 || num18 == 92 || num18 == 93 || num18 == 94 || num18 == 89 || num18 == 90 || num18 == 84 || num18 == 85 || num18 == 54 || num18 == 50 || num18 == 51 || num18 == 52 || num18 == 53 || num18 == 55 || num18 == 44 || num18 == 60 || num18 == 56 || num18 == 57 || num18 == 62 || num18 == 63 || num18 == 43 || num18 == 59 || num18 == 42 || num18 == 58 || num18 == 73 || num18 == 74)
							{
								UpdateSpriteBitmapAtLocked(num17, num16, num18, num13, mapViewportPadding, num14, num15, dpi2);
							}
						}
					}
					int num19 = (int)Math.Round(mapViewportPadding * dpi2.DpiScaleX);
					int num20 = (int)Math.Round(mapViewportPadding * dpi2.DpiScaleY);
					int num21 = Math.Max(0, num19 + minX4 * num14);
					int num22 = Math.Max(0, num20 + minY4 * num15);
					int val3 = Math.Min(cachedPixelWidth - num21, (maxX4 - minX4 + 1) * num14);
					int val4 = Math.Min(cachedPixelHeight - num22, (maxY4 - minY4 + 1) * num15);
					spritesWb.AddDirtyRect(new Int32Rect(num21, num22, Math.Max(1, val3), Math.Max(1, val4)));
				}
				finally
				{
					spritesWb.Unlock();
				}
			}
			if (!previewMode || portalsWb == null)
			{
				return;
			}
			GetVisibleTileBounds(out var minX5, out var maxX5, out var minY5, out var maxY5);
			bool flag5 = false;
			for (int num23 = minY5; num23 <= maxY5; num23++)
			{
				if (flag5)
				{
					break;
				}
				for (int num24 = minX5; num24 <= maxX5; num24++)
				{
					int spriteIdx = sprites[num23 * mapWidth + num24];
					if (IsPortalSprite(spriteIdx))
					{
						flag5 = true;
						break;
					}
				}
			}
			if (flag5)
			{
				QueueRebuildPortalsRegion(Math.Max(0, minX5 - 1), Math.Max(0, minY5 - 2), Math.Min(mapWidth - 1, maxX5 + 1), Math.Min(mapHeight - 1, maxY5 + 2));
			}
		}
		else if (animationFrame % 120 == 0)
		{
			Debug.WriteLine($"Not checking orbs: spritesWb={spritesWb != null}, yellow={yellowOrbFrame1 != null}, blue={blueOrbFrame1 != null}, pink={pinkOrbFrame1 != null}, green={greenOrbFrame1 != null}, red={redOrbFrame1 != null}, black={blackOrbFrame1 != null}, white={whiteOrbFrame1 != null}, coin={coinFrame1 != null}");
		}
	}

	private int GetAnimatedTileIndex(int originalIndex)
	{
		if (!previewMode)
		{
			return originalIndex;
		}
		int num = originalIndex;
		switch (originalIndex)
		{
		case 224:
			num = 48;
			break;
		case 225:
			num = 36;
			break;
		case 226:
			num = 40;
			break;
		case 228:
			num = 50;
			break;
		case 229:
			num = 37;
			break;
		case 230:
		case 231:
			num = 16;
			break;
		case 143:
			num = 47;
			break;
		case 221:
		case 222:
			num = 16;
			break;
		case 217:
		case 218:
			num = 17;
			break;
		case 219:
		case 220:
			num = 27;
			break;
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
		if (num != originalIndex)
		{
			originalIndex = num;
		}
		if (originalIndex >= 8 && originalIndex <= 11)
		{
			bool flag = animationFrame * 3 / 4 % 2 == 1;
			int num2 = originalIndex - 8;
			if (flag)
			{
				return 1004 + num2;
			}
			return 1000 + num2;
		}
		if (originalIndex == 4 || originalIndex == 125 || originalIndex == 127)
		{
			bool flag2 = animationFrame * 3 / 4 % 2 == 1;
			int num3 = originalIndex switch
			{
				125 => 1, 
				4 => 0, 
				_ => 2, 
			};
			if (flag2)
			{
				return 1013 + num3;
			}
			return 1010 + num3;
		}
		if (originalIndex >= 116 && originalIndex <= 124)
		{
			bool flag3 = animationFrame * 3 / 4 % 2 == 1;
			int num4 = originalIndex - 116;
			if (flag3)
			{
				return 1029 + num4;
			}
			return 1020 + num4;
		}
		return originalIndex;
	}

	private bool IsPortalSprite(int spriteIdx)
	{
		return spriteIdx == 0 || spriteIdx == 1 || spriteIdx == 2 || spriteIdx == 3 || spriteIdx == 4 || spriteIdx == 36 || spriteIdx == 23 || spriteIdx == 24 || spriteIdx == 25 || spriteIdx == 75 || spriteIdx == 88 || spriteIdx == 106 || spriteIdx == 107 || spriteIdx == 108 || spriteIdx == 8 || spriteIdx == 9 || spriteIdx == 100 || spriteIdx == 126 || spriteIdx == 78 || spriteIdx == 79 || spriteIdx == 16 || spriteIdx == 17 || spriteIdx == 18 || spriteIdx == 19 || spriteIdx == 34 || spriteIdx == 35 || spriteIdx == 20 || spriteIdx == 21 || spriteIdx == 22 || spriteIdx == 32 || spriteIdx == 33 || spriteIdx == 109 || spriteIdx == 102 || spriteIdx == 103 || spriteIdx == 104 || spriteIdx == 105 || spriteIdx == 95 || spriteIdx == 96 || spriteIdx == 97 || spriteIdx == 98 || spriteIdx == 99;
	}

	private BitmapSource? GetPortalSpriteForId(int spriteIdx, int positionKey = -1)
	{
		if (spriteIdx == 100 || spriteIdx == 126)
		{
			BitmapSource[] array = ((spriteIdx != 100) ? new BitmapSource[12]
			{
				cubePortalSprite, shipPortalSprite, ballPortalSprite, ufoPortalSprite, robotPortalSprite, wavePortalSprite, spiderPortalSprite, swingcopterPortalSprite, ninjaPortalSprite, pogoPortalSprite,
				snakePortalSprite, footballPortalSprite
			} : new BitmapSource[8] { cubePortalSprite, shipPortalSprite, ballPortalSprite, ufoPortalSprite, robotPortalSprite, wavePortalSprite, spiderPortalSprite, swingcopterPortalSprite });
			if (positionKey < 0)
			{
				return cubePortalSprite;
			}
			int num = array.Length;
			if (num == 0)
			{
				return null;
			}
			uint num2 = (uint)(positionKey * -1640531535) % (uint)num;
			int num3 = 0;
			try
			{
				num3 = animationFrame / 10 % num;
			}
			catch
			{
				num3 = 0;
			}
			int num4 = (int)((uint)((int)num2 + num3) % (uint)num);
			return array[num4];
		}
		if (1 == 0)
		{
		}
		BitmapSource result = spriteIdx switch
		{
			0 => cubePortalSprite, 
			1 => shipPortalSprite, 
			2 => ballPortalSprite, 
			3 => ufoPortalSprite, 
			4 => robotPortalSprite, 
			36 => wavePortalSprite, 
			23 => spiderPortalSprite, 
			24 => miniPortalSprite, 
			25 => growthPortalSprite, 
			95 => gravity1ThirdXPortalSprite, 
			96 => gravity1HalfXPortalSprite, 
			97 => gravity2ThirdXPortalSprite, 
			98 => gravity2XPortalSprite, 
			99 => gravity1XPortalSprite, 
			75 => swingcopterPortalSprite, 
			102 => teleportPortalHorizontalEnterDownSprite, 
			103 => teleportPortalHorizontalExitUpSprite, 
			104 => teleportPortalHorizontalEnterUpSprite, 
			105 => teleportPortalHorizontalExitDownSprite, 
			8 => gravityDownPortalSprite, 
			9 => gravityUpPortalSprite, 
			88 => ninjaPortalSprite, 
			106 => pogoPortalSprite, 
			107 => snakePortalSprite, 
			108 => footballPortalSprite, 
			78 => teleportPortalEnterSprite, 
			79 => teleportPortalExitSprite, 
			20 => speed05xPortalSprite, 
			21 => speed1xPortalSprite, 
			22 => speed2xPortalSprite, 
			32 => speed3xPortalSprite, 
			33 => speed4xPortalSprite, 
			109 => speedSpecialPortalSprite, 
			16 => gravityDownDownwardsPortalSprite, 
			17 => gravityDownUpwardsPortalSprite, 
			18 => gravityUpDownwardsPortalSprite, 
			19 => gravityUpUpwardsPortalSprite, 
			34 => dualPortalSprite, 
			35 => singlePortalSprite, 
			_ => null, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private int GetAnimatedSpriteIndex(int originalIndex)
	{
		if (!previewMode)
		{
			return originalIndex;
		}
		if (originalIndex == 123)
		{
			originalIndex = 5;
		}
		if (originalIndex == 124)
		{
			originalIndex = 39;
		}
		switch (originalIndex)
		{
		case 0:
			return 3000;
		case 100:
			return 3000;
		case 126:
			return 3000;
		case 1:
			return 3001;
		case 2:
			return 3002;
		case 3:
			return 3003;
		case 4:
			return 3004;
		case 36:
			return 3005;
		case 23:
			return 3006;
		case 106:
			return 3019;
		case 107:
			return 3020;
		case 108:
			return 3021;
		case 24:
			return 3017;
		case 25:
			return 3018;
		case 78:
			return 3000;
		case 79:
			return 3000;
		case 86:
			return 2152;
		case 87:
			return 2153;
		case 102:
			return 3030;
		case 103:
			return 3031;
		case 104:
			return 3032;
		case 105:
			return 3033;
		case 75:
			return 3007;
		case 88:
			return 3008;
		case 20:
			return 3024;
		case 21:
			return 3025;
		case 22:
			return 3026;
		case 32:
			return 3027;
		case 33:
			return 3028;
		case 109:
			return 3029;
		case 8:
			return 3009;
		case 9:
			return 3010;
		case 16:
			return 3011;
		case 17:
			return 3012;
		case 18:
			return 3013;
		case 19:
			return 3014;
		case 34:
			return 3015;
		case 35:
			return 3016;
		case 95:
			return 3019;
		case 96:
			return 3020;
		case 97:
			return 3021;
		case 98:
			return 3022;
		case 99:
			return 3023;
		case 89:
			return GetTwoFrameCustomIndex(2100);
		case 90:
			return GetTwoFrameCustomIndex(2102);
		case 84:
			return GetTwoFrameCustomIndex(2106);
		case 85:
			return GetTwoFrameCustomIndex(2104);
		case 50:
			return GetTwoFrameCustomIndex(2112);
		case 51:
			return GetTwoFrameCustomIndex(2114);
		case 52:
			return GetTwoFrameCustomIndex(2116);
		case 53:
			return GetTwoFrameCustomIndex(2118);
		case 55:
			return GetTwoFrameCustomIndex(2120);
		case 44:
			return GetTwoFrameCustomIndex(2122);
		case 60:
			return GetTwoFrameCustomIndex(2124);
		case 56:
			return GetTwoFrameCustomIndex(2128);
		case 57:
			return GetTwoFrameCustomIndex(2130);
		case 62:
			return GetTwoFrameCustomIndex(2132);
		case 63:
			return GetTwoFrameCustomIndex(2134);
		case 43:
			return GetTwoFrameCustomIndex(2136);
		case 59:
			return GetTwoFrameCustomIndex(2138);
		case 42:
			return GetTwoFrameCustomIndex(2140);
		case 58:
			return GetTwoFrameCustomIndex(2142);
		case 54:
			return GetTwoFrameCustomIndex(2110);
		case 73:
			return GetTwoFrameCustomIndex(2144);
		case 74:
			return GetTwoFrameCustomIndex(2146);
		case 45:
			return 2126;
		case 61:
			return 2127;
		case 46:
			return 2148;
		case 47:
			return 2149;
		case 48:
			return 2150;
		case 49:
			return 2151;
		default:
		{
			bool flag = originalIndex == 11 || originalIndex == 31 || originalIndex == 41;
			bool flag2 = originalIndex == 5;
			bool flag3 = originalIndex == 6;
			bool flag4 = originalIndex == 39;
			bool flag5 = originalIndex == 40;
			bool flag6 = originalIndex == 68;
			bool flag7 = originalIndex == 122;
			bool flag8 = originalIndex == 7 || originalIndex == 26 || originalIndex == 27;
			bool flag9 = originalIndex == 110;
			bool flag10 = originalIndex == 82 || originalIndex == 83 || originalIndex == 10 || originalIndex == 12 || originalIndex == 13 || originalIndex == 14 || originalIndex == 37 || originalIndex == 38 || originalIndex == 101 || originalIndex == 253 || originalIndex == 254;
			if (flag || flag2 || flag3 || flag4 || flag5 || flag6 || flag10 || flag7 || flag8 || flag9)
			{
				int key = currentSpritePositionKey;
				if (!spriteFrameOffsets.ContainsKey(key))
				{
					int value = spriteAnimationRandom.Next(0, 4);
					spriteFrameOffsets[key] = value;
				}
				int num = spriteFrameOffsets[key];
				int num2 = (animationFrame * 9 / 20 + num) % 4;
				if (flag10)
				{
				}
				if (flag9)
				{
					return 2400 + num2;
				}
				if (flag)
				{
					return 2000 + num2 * 3 + originalIndex switch
					{
						31 => 1, 
						11 => 0, 
						_ => 2, 
					};
				}
				if (flag2)
				{
					return 2012 + num2;
				}
				if (flag3)
				{
					return 2016 + num2;
				}
				if (flag4)
				{
					return 2020 + num2;
				}
				if (flag5)
				{
					return 2024 + num2;
				}
				if (flag6)
				{
					return 2028 + num2;
				}
				if (flag8)
				{
					return 2064 + num2 * 3 + originalIndex switch
					{
						26 => 1, 
						7 => 0, 
						_ => 2, 
					};
				}
				if (flag7)
				{
					return 2076 + num2;
				}
				if (flag10)
				{
					int num3 = 2032;
					return originalIndex switch
					{
						82 => 2032, 
						83 => 2036, 
						10 => 2040, 
						12 => 2044, 
						13 => 2048, 
						14 => 2052, 
						253 => 2048, 
						254 => 2052, 
						37 => 2056, 
						38 => 2060, 
						101 => 2154, 
						_ => 2032, 
					} + num2;
				}
			}
			if (originalIndex == 69 || originalIndex == 70 || originalIndex == 76 || originalIndex == 77 || originalIndex == 80 || originalIndex == 81 || originalIndex == 91 || originalIndex == 92 || originalIndex == 93 || originalIndex == 94)
			{
				if (1 == 0)
				{
				}
				int num4 = originalIndex switch
				{
					69 => 2080, 
					70 => 2082, 
					76 => 2084, 
					77 => 2086, 
					80 => 2088, 
					81 => 2090, 
					91 => 2092, 
					92 => 2094, 
					93 => 2096, 
					94 => 2098, 
					_ => 2080, 
				};
				if (1 == 0)
				{
				}
				int spriteBase = num4;
				return GetTwoFrameCustomIndex(spriteBase);
			}
			return originalIndex;
		}
		}
	}

	private BitmapSource? GetCustomAnimationTile(int customIndex)
	{
		if (customIndex >= 1000 && customIndex <= 1003)
		{
			int num = customIndex - 1000;
			if (sawFrame1TilesTinted != null && num < sawFrame1TilesTinted.Length)
			{
				return sawFrame1TilesTinted[num] as BitmapSource;
			}
			BitmapSource[]? array = sawFrame1Tiles;
			return (array != null) ? array[num] : null;
		}
		if (customIndex >= 1004 && customIndex <= 1007)
		{
			int num2 = customIndex - 1004;
			if (sawFrame2TilesTinted != null && num2 < sawFrame2TilesTinted.Length)
			{
				return sawFrame2TilesTinted[num2] as BitmapSource;
			}
			BitmapSource[]? array2 = sawFrame2Tiles;
			return (array2 != null) ? array2[num2] : null;
		}
		if (customIndex >= 1010 && customIndex <= 1012)
		{
			int num3 = customIndex - 1010;
			if (smallSawFrame1TilesTinted != null && num3 < smallSawFrame1TilesTinted.Length)
			{
				return smallSawFrame1TilesTinted[num3] as BitmapSource;
			}
			BitmapSource[]? array3 = smallSawFrame1Tiles;
			return (array3 != null) ? array3[num3] : null;
		}
		if (customIndex >= 1013 && customIndex <= 1015)
		{
			int num4 = customIndex - 1013;
			if (smallSawFrame2TilesTinted != null && num4 < smallSawFrame2TilesTinted.Length)
			{
				return smallSawFrame2TilesTinted[num4] as BitmapSource;
			}
			BitmapSource[]? array4 = smallSawFrame2Tiles;
			return (array4 != null) ? array4[num4] : null;
		}
		if (customIndex >= 1020 && customIndex <= 1028)
		{
			int num5 = customIndex - 1020;
			if (largeSawFrame1TilesTinted != null && num5 < largeSawFrame1TilesTinted.Length)
			{
				return largeSawFrame1TilesTinted[num5] as BitmapSource;
			}
			BitmapSource[]? array5 = largeSawFrame1Tiles;
			return (array5 != null) ? array5[num5] : null;
		}
		if (customIndex >= 1029 && customIndex <= 1037)
		{
			int num6 = customIndex - 1029;
			if (largeSawFrame2TilesTinted != null && num6 < largeSawFrame2TilesTinted.Length)
			{
				return largeSawFrame2TilesTinted[num6] as BitmapSource;
			}
			BitmapSource[]? array6 = largeSawFrame2Tiles;
			return (array6 != null) ? array6[num6] : null;
		}
		return null;
	}

	private int GetTwoFrameCustomIndex(int spriteBase)
	{
		int num = (animationFrame * 3 / 40 % 2 + 2) % 2;
		return spriteBase + num;
	}

	private BitmapSource? GetCustomAnimationSprite(int customIndex)
	{
		if (customIndex == 3000)
		{
			return cubePortalSprite;
		}
		if (customIndex == 3001)
		{
			return shipPortalSprite;
		}
		if (customIndex == 3002)
		{
			return ballPortalSprite;
		}
		if (customIndex == 3003)
		{
			return ufoPortalSprite;
		}
		if (customIndex == 3004)
		{
			return robotPortalSprite;
		}
		if (customIndex == 3005)
		{
			return wavePortalSprite;
		}
		if (customIndex == 3006)
		{
			return spiderPortalSprite;
		}
		if (customIndex == 3007)
		{
			return swingcopterPortalSprite;
		}
		if (customIndex == 3008)
		{
			return ninjaPortalSprite;
		}
		if (customIndex == 3009)
		{
			return gravityDownPortalSprite;
		}
		if (customIndex == 3010)
		{
			return gravityUpPortalSprite;
		}
		if (customIndex == 3030)
		{
			return teleportPortalHorizontalEnterDownSprite;
		}
		if (customIndex == 3031)
		{
			return teleportPortalHorizontalExitUpSprite;
		}
		if (customIndex == 3032)
		{
			return teleportPortalHorizontalEnterUpSprite;
		}
		if (customIndex == 3033)
		{
			return teleportPortalHorizontalExitDownSprite;
		}
		if (customIndex == 2152)
		{
			return spiderPadSprite;
		}
		if (customIndex == 2153)
		{
			return spiderPadUpsideDownSprite;
		}
		if (customIndex == 3011)
		{
			return gravityDownDownwardsPortalSprite;
		}
		if (customIndex == 3012)
		{
			return gravityDownUpwardsPortalSprite;
		}
		if (customIndex == 3013)
		{
			return gravityUpDownwardsPortalSprite;
		}
		if (customIndex == 3014)
		{
			return gravityUpUpwardsPortalSprite;
		}
		if (customIndex == 3015)
		{
			return dualPortalSprite;
		}
		if (customIndex == 3016)
		{
			return singlePortalSprite;
		}
		if (customIndex == 3017)
		{
			return miniPortalSprite;
		}
		if (customIndex == 3018)
		{
			return growthPortalSprite;
		}
		if (customIndex == 3019)
		{
			return gravity1ThirdXPortalSprite;
		}
		if (customIndex == 3020)
		{
			return gravity1HalfXPortalSprite;
		}
		if (customIndex == 3021)
		{
			return gravity2ThirdXPortalSprite;
		}
		if (customIndex == 3022)
		{
			return gravity2XPortalSprite;
		}
		if (customIndex == 3023)
		{
			return gravity1XPortalSprite;
		}
		if (customIndex == 3024)
		{
			return speed05xPortalSprite;
		}
		if (customIndex == 3025)
		{
			return speed1xPortalSprite;
		}
		if (customIndex == 3026)
		{
			return speed2xPortalSprite;
		}
		if (customIndex == 3027)
		{
			return speed3xPortalSprite;
		}
		if (customIndex == 3028)
		{
			return speed4xPortalSprite;
		}
		if (customIndex == 3029)
		{
			return speedSpecialPortalSprite;
		}
		if (customIndex >= 2000 && customIndex <= 2011)
		{
			int num = customIndex - 2000;
			int num2 = num / 3;
			int num3 = num % 3;
			if (1 == 0)
			{
			}
			BitmapSource[] array = num2 switch
			{
				0 => yellowOrbFrame1, 
				1 => yellowOrbFrame2, 
				2 => yellowOrbFrame3, 
				3 => yellowOrbFrame4, 
				_ => null, 
			};
			if (1 == 0)
			{
			}
			BitmapSource[] array2 = array;
			if (array2 != null && num3 < array2.Length)
			{
				return array2[num3];
			}
		}
		else
		{
			if (customIndex >= 2012 && customIndex <= 2015)
			{
				int num4 = customIndex - 2012;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num4)
				{
				case 0:
				{
					BitmapSource[]? array5 = blueOrbFrame1;
					result = ((array5 != null) ? array5[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array4 = blueOrbFrame2;
					result = ((array4 != null) ? array4[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array6 = blueOrbFrame3;
					result = ((array6 != null) ? array6[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array3 = blueOrbFrame4;
					result = ((array3 != null) ? array3[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2016 && customIndex <= 2019)
			{
				int num5 = customIndex - 2016;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num5)
				{
				case 0:
				{
					BitmapSource[]? array9 = pinkOrbFrame1;
					result = ((array9 != null) ? array9[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array8 = pinkOrbFrame2;
					result = ((array8 != null) ? array8[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array10 = pinkOrbFrame3;
					result = ((array10 != null) ? array10[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array7 = pinkOrbFrame4;
					result = ((array7 != null) ? array7[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2020 && customIndex <= 2023)
			{
				int num6 = customIndex - 2020;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num6)
				{
				case 0:
				{
					BitmapSource[]? array13 = greenOrbFrame1;
					result = ((array13 != null) ? array13[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array12 = greenOrbFrame2;
					result = ((array12 != null) ? array12[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array14 = greenOrbFrame3;
					result = ((array14 != null) ? array14[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array11 = greenOrbFrame4;
					result = ((array11 != null) ? array11[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2024 && customIndex <= 2027)
			{
				int num7 = customIndex - 2024;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num7)
				{
				case 0:
				{
					BitmapSource[]? array17 = redOrbFrame1;
					result = ((array17 != null) ? array17[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array16 = redOrbFrame2;
					result = ((array16 != null) ? array16[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array18 = redOrbFrame3;
					result = ((array18 != null) ? array18[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array15 = redOrbFrame4;
					result = ((array15 != null) ? array15[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2028 && customIndex <= 2031)
			{
				int num8 = customIndex - 2028;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num8)
				{
				case 0:
				{
					BitmapSource[]? array21 = blackOrbFrame1;
					result = ((array21 != null) ? array21[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array20 = blackOrbFrame2;
					result = ((array20 != null) ? array20[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array22 = blackOrbFrame3;
					result = ((array22 != null) ? array22[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array19 = blackOrbFrame4;
					result = ((array19 != null) ? array19[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2032 && customIndex <= 2035)
			{
				int num9 = customIndex - 2032;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num9)
				{
				case 0:
				{
					BitmapSource[]? array25 = redPadFrame1;
					result = ((array25 != null) ? array25[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array24 = redPadFrame2;
					result = ((array24 != null) ? array24[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array26 = redPadFrame3;
					result = ((array26 != null) ? array26[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array23 = redPadFrame4;
					result = ((array23 != null) ? array23[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2036 && customIndex <= 2039)
			{
				int num10 = customIndex - 2036;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num10)
				{
				case 0:
				{
					BitmapSource[]? array29 = redPadUpFrame1;
					result = ((array29 != null) ? array29[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array28 = redPadUpFrame2;
					result = ((array28 != null) ? array28[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array30 = redPadUpFrame3;
					result = ((array30 != null) ? array30[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array27 = redPadUpFrame4;
					result = ((array27 != null) ? array27[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2040 && customIndex <= 2043)
			{
				int num11 = customIndex - 2040;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num11)
				{
				case 0:
				{
					BitmapSource[]? array33 = yellowPadDownFrame1;
					result = ((array33 != null) ? array33[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array32 = yellowPadDownFrame2;
					result = ((array32 != null) ? array32[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array34 = yellowPadDownFrame3;
					result = ((array34 != null) ? array34[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array31 = yellowPadDownFrame4;
					result = ((array31 != null) ? array31[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2044 && customIndex <= 2047)
			{
				int num12 = customIndex - 2044;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num12)
				{
				case 0:
				{
					BitmapSource[]? array37 = yellowPadUpFrame1;
					result = ((array37 != null) ? array37[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array36 = yellowPadUpFrame2;
					result = ((array36 != null) ? array36[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array38 = yellowPadUpFrame3;
					result = ((array38 != null) ? array38[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array35 = yellowPadUpFrame4;
					result = ((array35 != null) ? array35[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2048 && customIndex <= 2051)
			{
				int num13 = customIndex - 2048;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num13)
				{
				case 0:
				{
					BitmapSource[]? array41 = bluePadDownFrame1;
					result = ((array41 != null) ? array41[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array40 = bluePadDownFrame2;
					result = ((array40 != null) ? array40[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array42 = bluePadDownFrame3;
					result = ((array42 != null) ? array42[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array39 = bluePadDownFrame4;
					result = ((array39 != null) ? array39[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2052 && customIndex <= 2055)
			{
				int num14 = customIndex - 2052;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num14)
				{
				case 0:
				{
					BitmapSource[]? array45 = bluePadUpFrame1;
					result = ((array45 != null) ? array45[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array44 = bluePadUpFrame2;
					result = ((array44 != null) ? array44[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array46 = bluePadUpFrame3;
					result = ((array46 != null) ? array46[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array43 = bluePadUpFrame4;
					result = ((array43 != null) ? array43[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2056 && customIndex <= 2059)
			{
				int num15 = customIndex - 2056;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num15)
				{
				case 0:
				{
					BitmapSource[]? array49 = pinkPadDownFrame1;
					result = ((array49 != null) ? array49[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array48 = pinkPadDownFrame2;
					result = ((array48 != null) ? array48[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array50 = pinkPadDownFrame3;
					result = ((array50 != null) ? array50[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array47 = pinkPadDownFrame4;
					result = ((array47 != null) ? array47[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2060 && customIndex <= 2063)
			{
				int num16 = customIndex - 2060;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num16)
				{
				case 0:
				{
					BitmapSource[]? array53 = pinkPadUpFrame1;
					result = ((array53 != null) ? array53[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array52 = pinkPadUpFrame2;
					result = ((array52 != null) ? array52[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array54 = pinkPadUpFrame3;
					result = ((array54 != null) ? array54[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array51 = pinkPadUpFrame4;
					result = ((array51 != null) ? array51[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2154 && customIndex <= 2157)
			{
				int num17 = customIndex - 2154;
				if (1 == 0)
				{
				}
				BitmapSource result;
				switch (num17)
				{
				case 0:
				{
					BitmapSource[]? array57 = greenPadExpandedFrame1;
					result = ((array57 != null) ? array57[0] : null);
					break;
				}
				case 1:
				{
					BitmapSource[]? array56 = greenPadExpandedFrame2;
					result = ((array56 != null) ? array56[0] : null);
					break;
				}
				case 2:
				{
					BitmapSource[]? array58 = greenPadExpandedFrame3;
					result = ((array58 != null) ? array58[0] : null);
					break;
				}
				case 3:
				{
					BitmapSource[]? array55 = greenPadExpandedFrame4;
					result = ((array55 != null) ? array55[0] : null);
					break;
				}
				default:
					result = null;
					break;
				}
				if (1 == 0)
				{
				}
				return result;
			}
			if (customIndex >= 2064 && customIndex <= 2075)
			{
				int num18 = customIndex - 2064;
				int num19 = num18 / 3;
				int num20 = num18 % 3;
				if (1 == 0)
				{
				}
				BitmapSource[] array = num19 switch
				{
					0 => coinFrame1, 
					1 => coinFrame2, 
					2 => coinFrame3, 
					3 => coinFrame4, 
					_ => null, 
				};
				if (1 == 0)
				{
				}
				BitmapSource[] array59 = array;
				if (array59 != null && num20 < array59.Length)
				{
					return array59[num20];
				}
			}
			else
			{
				if (customIndex >= 2400 && customIndex <= 2403)
				{
					int num21 = customIndex - 2400;
					if (1 == 0)
					{
					}
					BitmapSource result;
					switch (num21)
					{
					case 0:
					{
						BitmapSource[]? array62 = miniCoinFrame1;
						result = ((array62 != null) ? array62[0] : null);
						break;
					}
					case 1:
					{
						BitmapSource[]? array61 = miniCoinFrame2;
						result = ((array61 != null) ? array61[0] : null);
						break;
					}
					case 2:
					{
						BitmapSource[]? array63 = miniCoinFrame3;
						result = ((array63 != null) ? array63[0] : null);
						break;
					}
					case 3:
					{
						BitmapSource[]? array60 = miniCoinFrame4;
						result = ((array60 != null) ? array60[0] : null);
						break;
					}
					default:
						result = null;
						break;
					}
					if (1 == 0)
					{
					}
					return result;
				}
				if (customIndex >= 2076 && customIndex <= 2079)
				{
					int num22 = customIndex - 2076;
					if (1 == 0)
					{
					}
					BitmapSource result;
					switch (num22)
					{
					case 0:
					{
						BitmapSource[]? array66 = whiteOrbFrame1;
						result = ((array66 != null) ? array66[0] : null);
						break;
					}
					case 1:
					{
						BitmapSource[]? array65 = whiteOrbFrame2;
						result = ((array65 != null) ? array65[0] : null);
						break;
					}
					case 2:
					{
						BitmapSource[]? array67 = whiteOrbFrame3;
						result = ((array67 != null) ? array67[0] : null);
						break;
					}
					case 3:
					{
						BitmapSource[]? array64 = whiteOrbFrame4;
						result = ((array64 != null) ? array64[0] : null);
						break;
					}
					default:
						result = null;
						break;
					}
					if (1 == 0)
					{
					}
					return result;
				}
			}
		}
		if (customIndex >= 2080 && customIndex <= 2099)
		{
			switch (customIndex)
			{
			case 2080:
			{
				BitmapSource[]? array85 = dashOrbRightFrame1;
				return (array85 != null) ? array85[0] : null;
			}
			case 2081:
			{
				BitmapSource[]? array77 = dashOrbRightFrame2;
				return (array77 != null) ? array77[0] : null;
			}
			case 2082:
			{
				BitmapSource[]? array86 = dashGravityOrbRightFrame1;
				return (array86 != null) ? array86[0] : null;
			}
			case 2083:
			{
				BitmapSource[]? array81 = dashGravityOrbRightFrame2;
				return (array81 != null) ? array81[0] : null;
			}
			case 2084:
			{
				BitmapSource[]? array74 = dashOrb45UpFrame1;
				return (array74 != null) ? array74[0] : null;
			}
			case 2085:
			{
				BitmapSource[]? array82 = dashOrb45UpFrame2;
				return (array82 != null) ? array82[0] : null;
			}
			case 2086:
			{
				BitmapSource[]? array83 = dashGravityOrb45UpFrame1;
				return (array83 != null) ? array83[0] : null;
			}
			case 2087:
			{
				BitmapSource[]? array70 = dashGravityOrb45UpFrame2;
				return (array70 != null) ? array70[0] : null;
			}
			case 2088:
			{
				BitmapSource[]? array75 = dashOrb45DownFrame1;
				return (array75 != null) ? array75[0] : null;
			}
			case 2089:
			{
				BitmapSource[]? array79 = dashOrb45DownFrame2;
				return (array79 != null) ? array79[0] : null;
			}
			case 2090:
			{
				BitmapSource[]? array69 = dashGravityOrb45DownFrame1;
				return (array69 != null) ? array69[0] : null;
			}
			case 2091:
			{
				BitmapSource[]? array87 = dashGravityOrb45DownFrame2;
				return (array87 != null) ? array87[0] : null;
			}
			case 2092:
			{
				BitmapSource[]? array78 = dashOrbUpFrame1;
				return (array78 != null) ? array78[0] : null;
			}
			case 2093:
			{
				BitmapSource[]? array73 = dashOrbUpFrame2;
				return (array73 != null) ? array73[0] : null;
			}
			case 2094:
			{
				BitmapSource[]? array71 = dashGravityOrbUpFrame1;
				return (array71 != null) ? array71[0] : null;
			}
			case 2095:
			{
				BitmapSource[]? array84 = dashGravityOrbUpFrame2;
				return (array84 != null) ? array84[0] : null;
			}
			case 2096:
			{
				BitmapSource[]? array80 = dashOrbDownFrame1;
				return (array80 != null) ? array80[0] : null;
			}
			case 2097:
			{
				BitmapSource[]? array76 = dashOrbDownFrame2;
				return (array76 != null) ? array76[0] : null;
			}
			case 2098:
			{
				BitmapSource[]? array72 = dashGravityOrbDownFrame1;
				return (array72 != null) ? array72[0] : null;
			}
			case 2099:
			{
				BitmapSource[]? array68 = dashGravityOrbDownFrame2;
				return (array68 != null) ? array68[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2100 && customIndex <= 2103)
		{
			switch (customIndex)
			{
			case 2100:
			{
				BitmapSource[]? array90 = teleportOrbEnterFrame1;
				return (array90 != null) ? array90[0] : null;
			}
			case 2101:
			{
				BitmapSource[]? array89 = teleportOrbEnterFrame2;
				return (array89 != null) ? array89[0] : null;
			}
			case 2102:
			{
				BitmapSource[]? array91 = teleportOrbExitFrame1;
				return (array91 != null) ? array91[0] : null;
			}
			case 2103:
			{
				BitmapSource[]? array88 = teleportOrbExitFrame2;
				return (array88 != null) ? array88[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2104 && customIndex <= 2107)
		{
			switch (customIndex)
			{
			case 2104:
			{
				BitmapSource[]? array94 = spiderOrbDownFrame1;
				return (array94 != null) ? array94[0] : null;
			}
			case 2105:
			{
				BitmapSource[]? array93 = spiderOrbDownFrame2;
				return (array93 != null) ? array93[0] : null;
			}
			case 2106:
			{
				BitmapSource[]? array95 = spiderOrbUpFrame1;
				return (array95 != null) ? array95[0] : null;
			}
			case 2107:
			{
				BitmapSource[]? array92 = spiderOrbUpFrame2;
				return (array92 != null) ? array92[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2110 && customIndex <= 2111)
		{
			switch (customIndex)
			{
			case 2110:
			{
				BitmapSource[]? array97 = starFrame1;
				return (array97 != null) ? array97[0] : null;
			}
			case 2111:
			{
				BitmapSource[]? array96 = starFrame2;
				return (array96 != null) ? array96[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2112 && customIndex <= 2113)
		{
			switch (customIndex)
			{
			case 2112:
			{
				BitmapSource[]? array99 = diamondFrame1;
				return (array99 != null) ? array99[0] : null;
			}
			case 2113:
			{
				BitmapSource[]? array98 = diamondFrame2;
				return (array98 != null) ? array98[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2114 && customIndex <= 2115)
		{
			switch (customIndex)
			{
			case 2114:
			{
				BitmapSource[]? array101 = diamondHalfFrame1;
				return (array101 != null) ? array101[0] : null;
			}
			case 2115:
			{
				BitmapSource[]? array100 = diamondHalfFrame2;
				return (array100 != null) ? array100[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2116 && customIndex <= 2117)
		{
			switch (customIndex)
			{
			case 2116:
			{
				BitmapSource[]? array103 = questionMarkFrame1;
				return (array103 != null) ? array103[0] : null;
			}
			case 2117:
			{
				BitmapSource[]? array102 = questionMarkFrame2;
				return (array102 != null) ? array102[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2118 && customIndex <= 2119)
		{
			switch (customIndex)
			{
			case 2118:
			{
				BitmapSource[]? array105 = exclamationFrame1;
				return (array105 != null) ? array105[0] : null;
			}
			case 2119:
			{
				BitmapSource[]? array104 = exclamationFrame2;
				return (array104 != null) ? array104[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2120 && customIndex <= 2121)
		{
			switch (customIndex)
			{
			case 2120:
			{
				BitmapSource[]? array107 = xFrame1;
				return (array107 != null) ? array107[0] : null;
			}
			case 2121:
			{
				BitmapSource[]? array106 = xFrame2;
				return (array106 != null) ? array106[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2122 && customIndex <= 2123)
		{
			switch (customIndex)
			{
			case 2122:
			{
				BitmapSource[]? array109 = poleShortFrame1;
				return (array109 != null) ? array109[0] : null;
			}
			case 2123:
			{
				BitmapSource[]? array108 = poleShortFrame2;
				return (array108 != null) ? array108[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2128 && customIndex <= 2129)
		{
			switch (customIndex)
			{
			case 2128:
			{
				BitmapSource[]? array111 = poleLeftShortFrame1;
				return (array111 != null) ? array111[0] : null;
			}
			case 2129:
			{
				BitmapSource[]? array110 = poleLeftShortFrame2;
				return (array110 != null) ? array110[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2132 && customIndex <= 2133)
		{
			switch (customIndex)
			{
			case 2132:
			{
				BitmapSource[]? array113 = poleLeftMediumFrame1;
				return (array113 != null) ? array113[0] : null;
			}
			case 2133:
			{
				BitmapSource[]? array112 = poleLeftMediumFrame2;
				return (array112 != null) ? array112[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2130 && customIndex <= 2131)
		{
			switch (customIndex)
			{
			case 2130:
			{
				BitmapSource[]? array115 = poleRightShortFrame1;
				return (array115 != null) ? array115[0] : null;
			}
			case 2131:
			{
				BitmapSource[]? array114 = poleRightShortFrame2;
				return (array114 != null) ? array114[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2134 && customIndex <= 2135)
		{
			switch (customIndex)
			{
			case 2134:
			{
				BitmapSource[]? array117 = poleRightMediumFrame1;
				return (array117 != null) ? array117[0] : null;
			}
			case 2135:
			{
				BitmapSource[]? array116 = poleRightMediumFrame2;
				return (array116 != null) ? array116[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2124 && customIndex <= 2125)
		{
			switch (customIndex)
			{
			case 2124:
			{
				BitmapSource[]? array119 = poleShortUpsideDownFrame1;
				return (array119 != null) ? array119[0] : null;
			}
			case 2125:
			{
				BitmapSource[]? array118 = poleShortUpsideDownFrame2;
				return (array118 != null) ? array118[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2140 && customIndex <= 2141)
		{
			switch (customIndex)
			{
			case 2140:
			{
				BitmapSource[]? array121 = poleLongFrame1;
				return (array121 != null) ? array121[0] : null;
			}
			case 2141:
			{
				BitmapSource[]? array120 = poleLongFrame2;
				return (array120 != null) ? array120[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2142 && customIndex <= 2143)
		{
			switch (customIndex)
			{
			case 2142:
			{
				BitmapSource[]? array123 = poleLongUpsideDownFrame1;
				return (array123 != null) ? array123[0] : null;
			}
			case 2143:
			{
				BitmapSource[]? array122 = poleLongUpsideDownFrame2;
				return (array122 != null) ? array122[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2144 && customIndex <= 2145)
		{
			switch (customIndex)
			{
			case 2144:
			{
				BitmapSource[]? array125 = pulsingBallFrame1;
				return (array125 != null) ? array125[0] : null;
			}
			case 2145:
			{
				BitmapSource[]? array124 = pulsingBallFrame2;
				return (array124 != null) ? array124[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2146 && customIndex <= 2147)
		{
			switch (customIndex)
			{
			case 2146:
			{
				BitmapSource[]? array127 = musicNoteFrame1;
				return (array127 != null) ? array127[0] : null;
			}
			case 2147:
			{
				BitmapSource[]? array126 = musicNoteFrame2;
				return (array126 != null) ? array126[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2136 && customIndex <= 2137)
		{
			switch (customIndex)
			{
			case 2136:
			{
				BitmapSource[]? array129 = poleMediumFrame1;
				return (array129 != null) ? array129[0] : null;
			}
			case 2137:
			{
				BitmapSource[]? array128 = poleMediumFrame2;
				return (array128 != null) ? array128[0] : null;
			}
			default:
				return null;
			}
		}
		if (customIndex >= 2138 && customIndex <= 2139)
		{
			switch (customIndex)
			{
			case 2138:
			{
				BitmapSource[]? array131 = poleMediumUpsideDownFrame1;
				return (array131 != null) ? array131[0] : null;
			}
			case 2139:
			{
				BitmapSource[]? array130 = poleMediumUpsideDownFrame2;
				return (array130 != null) ? array130[0] : null;
			}
			default:
				return null;
			}
		}
		switch (customIndex)
		{
		case 2126:
		{
			BitmapSource[]? array137 = chainFrame1;
			return (array137 != null) ? array137[0] : null;
		}
		case 2127:
		{
			BitmapSource[]? array133 = chainUpsideFrame1;
			return (array133 != null) ? array133[0] : null;
		}
		case 2148:
		{
			BitmapSource[]? array135 = decoSpikesFrame1;
			return (array135 != null) ? array135[0] : null;
		}
		case 2149:
		{
			BitmapSource[]? array134 = decoSpikesUpsideDownFrame1;
			return (array134 != null) ? array134[0] : null;
		}
		case 2150:
		{
			BitmapSource[]? array136 = decoSpikesSmallFrame1;
			return (array136 != null) ? array136[0] : null;
		}
		case 2151:
		{
			BitmapSource[]? array132 = decoSpikesSmallUpsideDownFrame1;
			return (array132 != null) ? array132[0] : null;
		}
		default:
			return null;
		}
	}

	private bool IsHalfHeightTile(int customIndex)
	{
		return customIndex == 1010 || customIndex == 1012 || customIndex == 1013 || customIndex == 1015;
	}

	private bool IsTopHalfTile(int customIndex)
	{
		return customIndex == 1012 || customIndex == 1015;
	}

	private void InitializeSawAnimationFrames()
	{
		try
		{
			sawFrame1Tiles = new BitmapSource[4];
			sawFrame2Tiles = new BitmapSource[4];
			BitmapImage bitmapImage = LoadEmbeddedImage("saw-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("saw-frame2.png");
			if (bitmapImage == null || bitmapImage2 == null)
			{
				string baseDirectory = AppContext.BaseDirectory;
				string text = System.IO.Path.Combine(baseDirectory, "saw-frame1.png");
				string text2 = System.IO.Path.Combine(baseDirectory, "saw-frame2.png");
				string text3 = FindRepoRootFor("famidash.bmp");
				if (!string.IsNullOrEmpty(text3))
				{
					string text4 = System.IO.Path.Combine(text3, "saw-frame1.png");
					string text5 = System.IO.Path.Combine(text3, "saw-frame2.png");
					if (File.Exists(text4))
					{
						text = text4;
					}
					if (File.Exists(text5))
					{
						text2 = text5;
					}
				}
				if (File.Exists(text) && File.Exists(text2))
				{
					bitmapImage = new BitmapImage();
					bitmapImage.BeginInit();
					bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage.UriSource = new Uri(text);
					bitmapImage.EndInit();
					bitmapImage.Freeze();
					bitmapImage2 = new BitmapImage();
					bitmapImage2.BeginInit();
					bitmapImage2.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage2.UriSource = new Uri(text2);
					bitmapImage2.EndInit();
					bitmapImage2.Freeze();
				}
			}
			if (bitmapImage != null && bitmapImage2 != null)
			{
				sawFrame1Tiles[0] = new CroppedBitmap(bitmapImage, new Int32Rect(0, 0, 16, 16));
				sawFrame1Tiles[1] = new CroppedBitmap(bitmapImage, new Int32Rect(16, 0, 16, 16));
				sawFrame1Tiles[2] = new CroppedBitmap(bitmapImage, new Int32Rect(0, 16, 16, 16));
				sawFrame1Tiles[3] = new CroppedBitmap(bitmapImage, new Int32Rect(16, 16, 16, 16));
				sawFrame2Tiles[0] = new CroppedBitmap(bitmapImage2, new Int32Rect(0, 0, 16, 16));
				sawFrame2Tiles[1] = new CroppedBitmap(bitmapImage2, new Int32Rect(16, 0, 16, 16));
				sawFrame2Tiles[2] = new CroppedBitmap(bitmapImage2, new Int32Rect(0, 16, 16, 16));
				sawFrame2Tiles[3] = new CroppedBitmap(bitmapImage2, new Int32Rect(16, 16, 16, 16));
				Debug.WriteLine("? Loaded saw animation frames");
				Debug.WriteLine($"  Frame 1: {bitmapImage.PixelWidth}x{bitmapImage.PixelHeight}");
				Debug.WriteLine($"  Frame 2: {bitmapImage2.PixelWidth}x{bitmapImage2.PixelHeight}");
			}
			else
			{
				Debug.WriteLine("? Saw frame files not found");
			}
			BitmapImage bitmapImage3 = LoadEmbeddedImage("small-saw-frame1.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("small-saw-frame2.png");
			if (bitmapImage3 == null || bitmapImage4 == null)
			{
				string baseDirectory2 = AppContext.BaseDirectory;
				string text6 = System.IO.Path.Combine(baseDirectory2, "small-saw-frame1.png");
				string text7 = System.IO.Path.Combine(baseDirectory2, "small-saw-frame2.png");
				string text8 = FindRepoRootFor("famidash.bmp");
				if (!string.IsNullOrEmpty(text8))
				{
					string text9 = System.IO.Path.Combine(text8, "small-saw-frame1.png");
					string text10 = System.IO.Path.Combine(text8, "small-saw-frame2.png");
					if (File.Exists(text9))
					{
						text6 = text9;
					}
					if (File.Exists(text10))
					{
						text7 = text10;
					}
				}
				if (File.Exists(text6) && File.Exists(text7))
				{
					bitmapImage3 = new BitmapImage();
					bitmapImage3.BeginInit();
					bitmapImage3.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage3.UriSource = new Uri(text6);
					bitmapImage3.EndInit();
					bitmapImage3.Freeze();
					bitmapImage4 = new BitmapImage();
					bitmapImage4.BeginInit();
					bitmapImage4.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage4.UriSource = new Uri(text7);
					bitmapImage4.EndInit();
					bitmapImage4.Freeze();
				}
			}
			if (bitmapImage3 != null && bitmapImage4 != null)
			{
				smallSawFrame1Tiles = new BitmapSource[3];
				smallSawFrame2Tiles = new BitmapSource[3];
				Debug.WriteLine($"Small saw frames dimensions: Frame1={bitmapImage3.PixelWidth}x{bitmapImage3.PixelHeight}, Frame2={bitmapImage4.PixelWidth}x{bitmapImage4.PixelHeight}");
				smallSawFrame1Tiles[0] = new CroppedBitmap(bitmapImage3, new Int32Rect(0, 8, 16, 8));
				smallSawFrame1Tiles[1] = new CroppedBitmap(bitmapImage3, new Int32Rect(0, 0, 16, 16));
				smallSawFrame1Tiles[2] = new CroppedBitmap(bitmapImage3, new Int32Rect(0, 0, 16, 8));
				smallSawFrame2Tiles[0] = new CroppedBitmap(bitmapImage4, new Int32Rect(0, 8, 16, 8));
				smallSawFrame2Tiles[1] = new CroppedBitmap(bitmapImage4, new Int32Rect(0, 0, 16, 16));
				smallSawFrame2Tiles[2] = new CroppedBitmap(bitmapImage4, new Int32Rect(0, 0, 16, 8));
				Debug.WriteLine("? Loaded small saw animation frames");
				Debug.WriteLine($"  Small Frame 1: {bitmapImage3.PixelWidth}x{bitmapImage3.PixelHeight}");
				Debug.WriteLine($"  Small Frame 2: {bitmapImage4.PixelWidth}x{bitmapImage4.PixelHeight}");
			}
			else
			{
				Debug.WriteLine("? Small saw frame files not found");
			}
			BitmapImage bitmapImage5 = LoadEmbeddedImage("large-saw-frame1.png");
			BitmapImage bitmapImage6 = LoadEmbeddedImage("large-saw-frame2.png");
			if (bitmapImage5 == null || bitmapImage6 == null)
			{
				string baseDirectory3 = AppContext.BaseDirectory;
				string text11 = System.IO.Path.Combine(baseDirectory3, "large-saw-frame1.png");
				string text12 = System.IO.Path.Combine(baseDirectory3, "large-saw-frame2.png");
				string text13 = FindRepoRootFor("famidash.bmp");
				if (!string.IsNullOrEmpty(text13))
				{
					string text14 = System.IO.Path.Combine(text13, "large-saw-frame1.png");
					string text15 = System.IO.Path.Combine(text13, "large-saw-frame2.png");
					if (File.Exists(text14))
					{
						text11 = text14;
					}
					if (File.Exists(text15))
					{
						text12 = text15;
					}
				}
				if (File.Exists(text11) && File.Exists(text12))
				{
					bitmapImage5 = new BitmapImage();
					bitmapImage5.BeginInit();
					bitmapImage5.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage5.UriSource = new Uri(text11);
					bitmapImage5.EndInit();
					bitmapImage5.Freeze();
					bitmapImage6 = new BitmapImage();
					bitmapImage6.BeginInit();
					bitmapImage6.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage6.UriSource = new Uri(text12);
					bitmapImage6.EndInit();
					bitmapImage6.Freeze();
				}
			}
			if (bitmapImage5 != null && bitmapImage6 != null)
			{
				largeSawFrame1Tiles = new BitmapSource[9];
				largeSawFrame2Tiles = new BitmapSource[9];
				Debug.WriteLine($"Large saw frames dimensions: Frame1={bitmapImage5.PixelWidth}x{bitmapImage5.PixelHeight}, Frame2={bitmapImage6.PixelWidth}x{bitmapImage6.PixelHeight}");
				for (int i = 0; i < 3; i++)
				{
					for (int j = 0; j < 3; j++)
					{
						int num = i * 3 + j;
						largeSawFrame1Tiles[num] = new CroppedBitmap(bitmapImage5, new Int32Rect(j * 16, i * 16, 16, 16));
						largeSawFrame2Tiles[num] = new CroppedBitmap(bitmapImage6, new Int32Rect(j * 16, i * 16, 16, 16));
					}
				}
				Debug.WriteLine("? Loaded large saw animation frames (9 tiles)");
				Debug.WriteLine($"  Large Frame 1: {bitmapImage5.PixelWidth}x{bitmapImage5.PixelHeight}");
				Debug.WriteLine($"  Large Frame 2: {bitmapImage6.PixelWidth}x{bitmapImage6.PixelHeight}");
			}
			else
			{
				Debug.WriteLine("? Large saw frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load saw animation frames: " + ex.Message);
		}
	}

	private void InitializePortalSprites()
	{
		try
		{
			cubePortalSprite = LoadPortalSprite("cube-portal.png");
			shipPortalSprite = LoadPortalSprite("ship-portal.png");
			ballPortalSprite = LoadPortalSprite("ball-portal.png");
			ufoPortalSprite = LoadPortalSprite("ufo-portal.png");
			robotPortalSprite = LoadPortalSprite("robot-portal.png");
			wavePortalSprite = LoadPortalSprite("wave-portal.png");
			spiderPortalSprite = LoadPortalSprite("spider-portal.png");
			miniPortalSprite = LoadPortalSprite("mini-portal.png");
			growthPortalSprite = LoadPortalSprite("growth-portal.png");
			teleportPortalEnterSprite = LoadPortalSprite("teleport-portal-enter.png");
			teleportPortalExitSprite = LoadPortalSprite("teleport-portal-exit.png");
			spiderPadSprite = LoadPortalSprite("spider-pad.png");
			spiderPadUpsideDownSprite = LoadPortalSprite("spider-pad-upsidedown.png");
			teleportPortalHorizontalEnterDownSprite = LoadPortalSprite("teleport-portal-horizontal-enter-downwards.png");
			teleportPortalHorizontalExitUpSprite = LoadPortalSprite("teleport-portal-horizontal-exit-upwards.png");
			teleportPortalHorizontalEnterUpSprite = LoadPortalSprite("teleport-portal-horizontal-enter-upwards.png");
			teleportPortalHorizontalExitDownSprite = LoadPortalSprite("teleport-portal-horizontal-exit-downwards.png");
			speed05xPortalSprite = LoadPortalSprite("speed-05x.png");
			speed1xPortalSprite = LoadPortalSprite("speed-1x.png");
			speed2xPortalSprite = LoadPortalSprite("speed-2x.png");
			speed3xPortalSprite = LoadPortalSprite("speed-3x.png");
			speed4xPortalSprite = LoadPortalSprite("speed-4x.png");
			speedSpecialPortalSprite = LoadPortalSprite("speed-special.png");
			pogoPortalSprite = LoadPortalSprite("pogo-portal.png");
			snakePortalSprite = LoadPortalSprite("snake-portal.png");
			footballPortalSprite = LoadPortalSprite("football-portal.png");
			gravity1ThirdXPortalSprite = LoadPortalSprite("gravity-1-3rd-x-portal.png");
			gravity1HalfXPortalSprite = LoadPortalSprite("gravity-1-half-x-portal.png");
			gravity2ThirdXPortalSprite = LoadPortalSprite("gravity-2-3rd-x-portal.png");
			gravity2XPortalSprite = LoadPortalSprite("gravity-2x-portal.png");
			gravity1XPortalSprite = LoadPortalSprite("gravity-1x-portal.png");
			swingcopterPortalSprite = LoadPortalSprite("swingcopter-portal.png");
			ninjaPortalSprite = LoadPortalSprite("ninja-portal.png");
			gravityDownPortalSprite = LoadPortalSprite("gravity-down-portal.png");
			gravityUpPortalSprite = LoadPortalSprite("gravity-up-portal.png");
			gravityDownDownwardsPortalSprite = LoadPortalSprite("gravity-down-downwards-portal.png");
			gravityDownUpwardsPortalSprite = LoadPortalSprite("gravity-down-upwards-portal.png");
			gravityUpDownwardsPortalSprite = LoadPortalSprite("gravity-up-downwards-portal.png");
			gravityUpUpwardsPortalSprite = LoadPortalSprite("gravity-up-upwards-portal.png");
			dualPortalSprite = LoadPortalSprite("dual-portal.png");
			singlePortalSprite = LoadPortalSprite("single-portal.png");
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load portal sprites: " + ex.Message);
		}
		BitmapSource? LoadPortalSprite(string filename)
		{
			string text = "embedded";
			BitmapImage bitmapImage = LoadEmbeddedImage(filename);
			if (bitmapImage == null)
			{
				string baseDirectory = AppContext.BaseDirectory;
				string text2 = System.IO.Path.Combine(baseDirectory, filename);
				string text3 = FindRepoRootFor("famidash.bmp");
				if (!string.IsNullOrEmpty(text3))
				{
					string text4 = System.IO.Path.Combine(text3, filename);
					if (File.Exists(text4))
					{
						text2 = text4;
					}
				}
				string text5 = System.IO.Path.Combine("C:", "editor-dev", filename);
				if (!File.Exists(text2) && File.Exists(text5))
				{
					text2 = text5;
				}
				if (File.Exists(text2))
				{
					bitmapImage = new BitmapImage();
					bitmapImage.BeginInit();
					bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage.UriSource = new Uri(text2);
					bitmapImage.EndInit();
					bitmapImage.Freeze();
					text = text2;
				}
			}
			if (bitmapImage != null)
			{
				FormatConvertedBitmap result = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				Debug.WriteLine($"? Loaded {filename}: {bitmapImage.PixelWidth}x{bitmapImage.PixelHeight}");
				return result;
			}
			Debug.WriteLine("? " + filename + " not found");
			return null;
		}
	}

	private void InitializeYellowOrbAnimationFrames()
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("yellow-orb-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("yellow-orb-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("yellow-orb-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("yellow-orb-frame4.png");
			if (bitmapImage == null || bitmapImage2 == null || bitmapImage3 == null || bitmapImage4 == null)
			{
				string baseDirectory = AppContext.BaseDirectory;
				string text = System.IO.Path.Combine(baseDirectory, "yellow-orb-frame1.png");
				string text2 = System.IO.Path.Combine(baseDirectory, "yellow-orb-frame2.png");
				string text3 = System.IO.Path.Combine(baseDirectory, "yellow-orb-frame3.png");
				string text4 = System.IO.Path.Combine(baseDirectory, "yellow-orb-frame4.png");
				string text5 = FindRepoRootFor("famidash.bmp");
				if (!string.IsNullOrEmpty(text5))
				{
					string text6 = System.IO.Path.Combine(text5, "yellow-orb-frame1.png");
					string text7 = System.IO.Path.Combine(text5, "yellow-orb-frame2.png");
					string text8 = System.IO.Path.Combine(text5, "yellow-orb-frame3.png");
					string text9 = System.IO.Path.Combine(text5, "yellow-orb-frame4.png");
					if (File.Exists(text6))
					{
						text = text6;
					}
					if (File.Exists(text7))
					{
						text2 = text7;
					}
					if (File.Exists(text8))
					{
						text3 = text8;
					}
					if (File.Exists(text9))
					{
						text4 = text9;
					}
				}
				if (File.Exists(text))
				{
					bitmapImage = new BitmapImage();
					bitmapImage.BeginInit();
					bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage.UriSource = new Uri(text);
					bitmapImage.EndInit();
					bitmapImage.Freeze();
				}
				if (File.Exists(text2))
				{
					bitmapImage2 = new BitmapImage();
					bitmapImage2.BeginInit();
					bitmapImage2.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage2.UriSource = new Uri(text2);
					bitmapImage2.EndInit();
					bitmapImage2.Freeze();
				}
				if (File.Exists(text3))
				{
					bitmapImage3 = new BitmapImage();
					bitmapImage3.BeginInit();
					bitmapImage3.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage3.UriSource = new Uri(text3);
					bitmapImage3.EndInit();
					bitmapImage3.Freeze();
				}
				if (File.Exists(text4))
				{
					bitmapImage4 = new BitmapImage();
					bitmapImage4.BeginInit();
					bitmapImage4.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage4.UriSource = new Uri(text4);
					bitmapImage4.EndInit();
					bitmapImage4.Freeze();
				}
			}
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				yellowOrbFrame1 = new BitmapSource[3];
				yellowOrbFrame2 = new BitmapSource[3];
				yellowOrbFrame3 = new BitmapSource[3];
				yellowOrbFrame4 = new BitmapSource[3];
				for (int i = 0; i < 3; i++)
				{
					yellowOrbFrame1[i] = formatConvertedBitmap;
					yellowOrbFrame2[i] = formatConvertedBitmap2;
					yellowOrbFrame3[i] = formatConvertedBitmap3;
					yellowOrbFrame4[i] = formatConvertedBitmap4;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load yellow orb animation frames: " + ex.Message);
		}
	}

	private void LoadOrbFrames(string colorName, ref BitmapSource[]? frame1, ref BitmapSource[]? frame2, ref BitmapSource[]? frame3, ref BitmapSource[]? frame4, int arraySize = 1)
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage(colorName + "-orb-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage(colorName + "-orb-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage(colorName + "-orb-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage(colorName + "-orb-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				frame1 = new BitmapSource[arraySize];
				frame2 = new BitmapSource[arraySize];
				frame3 = new BitmapSource[arraySize];
				frame4 = new BitmapSource[arraySize];
				for (int i = 0; i < arraySize; i++)
				{
					frame1[i] = formatConvertedBitmap;
					frame2[i] = formatConvertedBitmap2;
					frame3[i] = formatConvertedBitmap3;
					frame4[i] = formatConvertedBitmap4;
				}
				Debug.WriteLine("? Loaded " + colorName + " orb animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? " + colorName + " orb frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load " + colorName + " orb animation frames: " + ex.Message);
		}
	}

	private void LoadCoinFrames(ref BitmapSource[]? frame1, ref BitmapSource[]? frame2, ref BitmapSource[]? frame3, ref BitmapSource[]? frame4)
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("coin-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("coin-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("coin-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("coin-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				frame1 = new BitmapSource[3];
				frame2 = new BitmapSource[3];
				frame3 = new BitmapSource[3];
				frame4 = new BitmapSource[3];
				for (int i = 0; i < 3; i++)
				{
					frame1[i] = formatConvertedBitmap;
					frame2[i] = formatConvertedBitmap2;
					frame3[i] = formatConvertedBitmap3;
					frame4[i] = formatConvertedBitmap4;
				}
				Debug.WriteLine("? Loaded coin animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? coin frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load coin animation frames: " + ex.Message);
		}
	}

	private void InitializeWhiteOrbAnimationFrames()
	{
		LoadOrbFrames("white", ref whiteOrbFrame1, ref whiteOrbFrame2, ref whiteOrbFrame3, ref whiteOrbFrame4);
	}

	private void InitializeCoinAnimationFrames()
	{
		LoadCoinFrames(ref coinFrame1, ref coinFrame2, ref coinFrame3, ref coinFrame4);
	}

	private void InitializeMiniCoinAnimationFrames()
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("mini-coin-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("mini-coin-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("mini-coin-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("mini-coin-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				miniCoinFrame1 = new BitmapSource[1];
				miniCoinFrame2 = new BitmapSource[1];
				miniCoinFrame3 = new BitmapSource[1];
				miniCoinFrame4 = new BitmapSource[1];
				miniCoinFrame1[0] = formatConvertedBitmap;
				miniCoinFrame2[0] = formatConvertedBitmap2;
				miniCoinFrame3[0] = formatConvertedBitmap3;
				miniCoinFrame4[0] = formatConvertedBitmap4;
				Debug.WriteLine("? Loaded mini-coin animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? mini-coin frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load mini-coin animation frames: " + ex.Message);
		}
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
			BitmapImage bitmapImage = LoadEmbeddedImage("red-pad-down-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("red-pad-down-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("red-pad-down-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("red-pad-down-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				redPadFrame1 = new BitmapSource[1];
				redPadFrame2 = new BitmapSource[1];
				redPadFrame3 = new BitmapSource[1];
				redPadFrame4 = new BitmapSource[1];
				redPadFrame1[0] = formatConvertedBitmap;
				redPadFrame2[0] = formatConvertedBitmap2;
				redPadFrame3[0] = formatConvertedBitmap3;
				redPadFrame4[0] = formatConvertedBitmap4;
				Debug.WriteLine("? Loaded red pad animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? red pad frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load red pad animation frames: " + ex.Message);
		}
	}

	private void InitializeRedPadUpAnimationFrames()
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("red-pad-up-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("red-pad-up-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("red-pad-up-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("red-pad-up-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				redPadUpFrame1 = new BitmapSource[1];
				redPadUpFrame2 = new BitmapSource[1];
				redPadUpFrame3 = new BitmapSource[1];
				redPadUpFrame4 = new BitmapSource[1];
				redPadUpFrame1[0] = formatConvertedBitmap;
				redPadUpFrame2[0] = formatConvertedBitmap2;
				redPadUpFrame3[0] = formatConvertedBitmap3;
				redPadUpFrame4[0] = formatConvertedBitmap4;
				Debug.WriteLine("? Loaded red pad (up) animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? red pad (up) frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load red pad (up) animation frames: " + ex.Message);
		}
	}

	private void InitializeYellowPadDownAnimationFrames()
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("yellow-pad-down-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("yellow-pad-down-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("yellow-pad-down-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("yellow-pad-down-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				yellowPadDownFrame1 = new BitmapSource[1];
				yellowPadDownFrame2 = new BitmapSource[1];
				yellowPadDownFrame3 = new BitmapSource[1];
				yellowPadDownFrame4 = new BitmapSource[1];
				yellowPadDownFrame1[0] = formatConvertedBitmap;
				yellowPadDownFrame2[0] = formatConvertedBitmap2;
				yellowPadDownFrame3[0] = formatConvertedBitmap3;
				yellowPadDownFrame4[0] = formatConvertedBitmap4;
				Debug.WriteLine("? Loaded yellow pad (down) animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? yellow pad (down) frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load yellow pad (down) animation frames: " + ex.Message);
		}
	}

	private void InitializeYellowPadUpAnimationFrames()
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("yellow-pad-up-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("yellow-pad-up-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("yellow-pad-up-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("yellow-pad-up-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				yellowPadUpFrame1 = new BitmapSource[1];
				yellowPadUpFrame2 = new BitmapSource[1];
				yellowPadUpFrame3 = new BitmapSource[1];
				yellowPadUpFrame4 = new BitmapSource[1];
				yellowPadUpFrame1[0] = formatConvertedBitmap;
				yellowPadUpFrame2[0] = formatConvertedBitmap2;
				yellowPadUpFrame3[0] = formatConvertedBitmap3;
				yellowPadUpFrame4[0] = formatConvertedBitmap4;
				Debug.WriteLine("? Loaded yellow pad (up) animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? yellow pad (up) frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load yellow pad (up) animation frames: " + ex.Message);
		}
	}

	private void InitializeBluePadDownAnimationFrames()
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("blue-pad-down-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("blue-pad-down-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("blue-pad-down-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("blue-pad-down-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				bluePadDownFrame1 = new BitmapSource[1];
				bluePadDownFrame2 = new BitmapSource[1];
				bluePadDownFrame3 = new BitmapSource[1];
				bluePadDownFrame4 = new BitmapSource[1];
				bluePadDownFrame1[0] = formatConvertedBitmap;
				bluePadDownFrame2[0] = formatConvertedBitmap2;
				bluePadDownFrame3[0] = formatConvertedBitmap3;
				bluePadDownFrame4[0] = formatConvertedBitmap4;
				Debug.WriteLine("? Loaded blue pad (down) animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? blue pad (down) frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load blue pad (down) animation frames: " + ex.Message);
		}
	}

	private void InitializeBluePadUpAnimationFrames()
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("blue-pad-up-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("blue-pad-up-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("blue-pad-up-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("blue-pad-up-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				bluePadUpFrame1 = new BitmapSource[1];
				bluePadUpFrame2 = new BitmapSource[1];
				bluePadUpFrame3 = new BitmapSource[1];
				bluePadUpFrame4 = new BitmapSource[1];
				bluePadUpFrame1[0] = formatConvertedBitmap;
				bluePadUpFrame2[0] = formatConvertedBitmap2;
				bluePadUpFrame3[0] = formatConvertedBitmap3;
				bluePadUpFrame4[0] = formatConvertedBitmap4;
				Debug.WriteLine("? Loaded blue pad (up) animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? blue pad (up) frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load blue pad (up) animation frames: " + ex.Message);
		}
	}

	private void InitializePinkPadDownAnimationFrames()
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("pink-pad-down-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("pink-pad-down-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("pink-pad-down-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("pink-pad-down-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				pinkPadDownFrame1 = new BitmapSource[1];
				pinkPadDownFrame2 = new BitmapSource[1];
				pinkPadDownFrame3 = new BitmapSource[1];
				pinkPadDownFrame4 = new BitmapSource[1];
				pinkPadDownFrame1[0] = formatConvertedBitmap;
				pinkPadDownFrame2[0] = formatConvertedBitmap2;
				pinkPadDownFrame3[0] = formatConvertedBitmap3;
				pinkPadDownFrame4[0] = formatConvertedBitmap4;
				Debug.WriteLine("? Loaded pink pad (down) animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? pink pad (down) frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load pink pad (down) animation frames: " + ex.Message);
		}
	}

	private void InitializePinkPadUpAnimationFrames()
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("pink-pad-up-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("pink-pad-up-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("pink-pad-up-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("pink-pad-up-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				pinkPadUpFrame1 = new BitmapSource[1];
				pinkPadUpFrame2 = new BitmapSource[1];
				pinkPadUpFrame3 = new BitmapSource[1];
				pinkPadUpFrame4 = new BitmapSource[1];
				pinkPadUpFrame1[0] = formatConvertedBitmap;
				pinkPadUpFrame2[0] = formatConvertedBitmap2;
				pinkPadUpFrame3[0] = formatConvertedBitmap3;
				pinkPadUpFrame4[0] = formatConvertedBitmap4;
				Debug.WriteLine("? Loaded pink pad (up) animation frames (4 frames)");
			}
			else
			{
				Debug.WriteLine("? pink pad (up) frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load pink pad (up) animation frames: " + ex.Message);
		}
	}

	private void InitializeGreenPadExpandedAnimationFrames()
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage("green-pad-expanded-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage("green-pad-expanded-frame2.png");
			BitmapImage bitmapImage3 = LoadEmbeddedImage("green-pad-expanded-frame3.png");
			BitmapImage bitmapImage4 = LoadEmbeddedImage("green-pad-expanded-frame4.png");
			if (bitmapImage != null && bitmapImage2 != null && bitmapImage3 != null && bitmapImage4 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap3 = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap4 = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				greenPadExpandedFrame1 = new BitmapSource[1];
				greenPadExpandedFrame2 = new BitmapSource[1];
				greenPadExpandedFrame3 = new BitmapSource[1];
				greenPadExpandedFrame4 = new BitmapSource[1];
				greenPadExpandedFrame1[0] = formatConvertedBitmap;
				greenPadExpandedFrame2[0] = formatConvertedBitmap2;
				greenPadExpandedFrame3[0] = formatConvertedBitmap3;
				greenPadExpandedFrame4[0] = formatConvertedBitmap4;
				Debug.WriteLine("? Loaded green pad expanded animation frames (4 frames, 32px tall)");
			}
			else
			{
				Debug.WriteLine("? green pad expanded frame files not found");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load green pad expanded animation frames: " + ex.Message);
		}
	}

	private void InitializeBlackOrbAnimationFrames()
	{
		LoadOrbFrames("black", ref blackOrbFrame1, ref blackOrbFrame2, ref blackOrbFrame3, ref blackOrbFrame4);
	}

	private void LoadTwoFrameOrb(string baseName, ref BitmapSource[]? frame1, ref BitmapSource[]? frame2)
	{
		try
		{
			BitmapImage bitmapImage = LoadEmbeddedImage(baseName + "-frame1.png");
			BitmapImage bitmapImage2 = LoadEmbeddedImage(baseName + "-frame2.png");
			if (bitmapImage != null && bitmapImage2 != null)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapImage, PixelFormats.Pbgra32, null, 0.0);
				FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapImage2, PixelFormats.Pbgra32, null, 0.0);
				frame1 = new BitmapSource[1];
				frame2 = new BitmapSource[1];
				frame1[0] = formatConvertedBitmap;
				frame2[0] = formatConvertedBitmap2;
				Debug.WriteLine("? Loaded two-frame orb: " + baseName + " (2 frames)");
				return;
			}
			string baseDirectory = AppContext.BaseDirectory;
			string text = System.IO.Path.Combine(baseDirectory, baseName + "-frame1.png");
			string text2 = System.IO.Path.Combine(baseDirectory, baseName + "-frame2.png");
			string text3 = FindRepoRootFor("famidash.bmp");
			if (!string.IsNullOrEmpty(text3))
			{
				string text4 = System.IO.Path.Combine(text3, baseName + "-frame1.png");
				string text5 = System.IO.Path.Combine(text3, baseName + "-frame2.png");
				if (File.Exists(text4))
				{
					text = text4;
				}
				if (File.Exists(text5))
				{
					text2 = text5;
				}
			}
			if (File.Exists(text) && File.Exists(text2))
			{
				BitmapImage bitmapImage3 = new BitmapImage();
				bitmapImage3.BeginInit();
				bitmapImage3.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage3.UriSource = new Uri(text);
				bitmapImage3.EndInit();
				bitmapImage3.Freeze();
				BitmapImage bitmapImage4 = new BitmapImage();
				bitmapImage4.BeginInit();
				bitmapImage4.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage4.UriSource = new Uri(text2);
				bitmapImage4.EndInit();
				bitmapImage4.Freeze();
				frame1 = new BitmapSource[1];
				frame2 = new BitmapSource[1];
				frame1[0] = new FormatConvertedBitmap(bitmapImage3, PixelFormats.Pbgra32, null, 0.0);
				frame2[0] = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				Debug.WriteLine("? Loaded two-frame orb from files: " + baseName);
			}
			else
			{
				Debug.WriteLine("? Two-frame orb files not found: " + baseName);
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load two-frame orb " + baseName + ": " + ex.Message);
		}
	}

	private void LoadSettings()
	{
		try
		{
			string baseDirectory = AppContext.BaseDirectory;
			string path = System.IO.Path.Combine(baseDirectory, "editor-settings.json");
			if (!File.Exists(path))
			{
				string contents = "{\"version\":2,\"background\":[255,59,59,59],\"backgroundTint\":[255,0,23,116],\"groundTint\":[255,0,23,116],\"tileTint\":[255,0,23,116],\"useLegacyTriggerOffset\":false,\"swapMouseWheelScroll\":false,\"invertPinchGesture\":true,\"hideColorTriggers\":false,\"hideInvisibleSprites\":false,\"hideBackground\":false,\"hideGround\":false,\"useFastZoom\":false,\"lockSpritesToSet\":true,\"showAccurateTileset\":true,\"playerColor\":[255,100,229,60],\"playerColorEnabled\":true,\"gridDarkness\":0.18,\"famistudioPath\":\"C:\\\\Program Files\\\\FamiStudio\"}";
				File.WriteAllText(path, contents);
			}
			if (!File.Exists(path))
			{
				return;
			}
			string json = File.ReadAllText(path);
			JsonDocument jsonDocument = JsonDocument.Parse(json);
			int num = 0;
			if (jsonDocument.RootElement.TryGetProperty("version", out var value))
			{
				try
				{
					num = value.GetInt32();
				}
				catch
				{
					num = 0;
				}
			}
			if (num != 2)
			{
				try
				{
					File.Delete(path);
				}
				catch
				{
				}
				string text = "{\"version\":2,\"background\":[255,59,59,59],\"backgroundTint\":[255,0,23,116],\"groundTint\":[255,0,23,116],\"tileTint\":[255,0,23,116],\"useLegacyTriggerOffset\":false,\"swapMouseWheelScroll\":false,\"invertPinchGesture\":true,\"hideColorTriggers\":false,\"hideInvisibleSprites\":false,\"hideBackground\":false,\"hideGround\":false,\"useFastZoom\":false,\"lockSpritesToSet\":true,\"showAccurateTileset\":true,\"playerColor\":[255,100,229,60],\"playerColorEnabled\":true,\"gridDarkness\":0.18,\"famistudioPath\":\"C:\\\\Program Files\\\\FamiStudio\"}";
				File.WriteAllText(path, text);
				json = text;
				jsonDocument = JsonDocument.Parse(json);
			}
			if (jsonDocument.RootElement.TryGetProperty("background", out var value2))
			{
				Color color;
				if (value2.GetArrayLength() >= 4)
				{
					byte a = (byte)value2[0].GetInt32();
					byte r = (byte)value2[1].GetInt32();
					byte g = (byte)value2[2].GetInt32();
					byte b = (byte)value2[3].GetInt32();
					color = Color.FromArgb(a, r, g, b);
				}
				else
				{
					byte r2 = (byte)value2[0].GetInt32();
					byte g2 = (byte)value2[1].GetInt32();
					byte b2 = (byte)value2[2].GetInt32();
					color = Color.FromRgb(r2, g2, b2);
				}
				if (base.Resources.Contains("AppBackgroundBrush") && base.Resources["AppBackgroundBrush"] is SolidColorBrush solidColorBrush)
				{
					solidColorBrush.Color = color;
				}
				mapBackground = new SolidColorBrush(color);
			}
			if (jsonDocument.RootElement.TryGetProperty("backgroundTint", out var value3) && value3.GetArrayLength() >= 4)
			{
				byte a2 = (byte)value3[0].GetInt32();
				byte r3 = (byte)value3[1].GetInt32();
				byte g3 = (byte)value3[2].GetInt32();
				byte b3 = (byte)value3[3].GetInt32();
				backgroundTint = Color.FromArgb(a2, r3, g3, b3);
				defaultBackgroundTint = backgroundTint;
			}
			if (jsonDocument.RootElement.TryGetProperty("noParallaxBg", out var value4))
			{
				try
				{
					noParallaxBg = value4.GetBoolean();
				}
				catch
				{
					noParallaxBg = false;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("groundTint", out var value5) && value5.GetArrayLength() >= 4)
			{
				byte a3 = (byte)value5[0].GetInt32();
				byte r4 = (byte)value5[1].GetInt32();
				byte g4 = (byte)value5[2].GetInt32();
				byte b4 = (byte)value5[3].GetInt32();
				groundTint = Color.FromArgb(a3, r4, g4, b4);
				defaultGroundTint = groundTint;
			}
			if (jsonDocument.RootElement.TryGetProperty("tileTint", out var value6) && value6.GetArrayLength() >= 4)
			{
				byte a4 = (byte)value6[0].GetInt32();
				byte r5 = (byte)value6[1].GetInt32();
				byte g5 = (byte)value6[2].GetInt32();
				byte b5 = (byte)value6[3].GetInt32();
				tileTint = Color.FromArgb(a4, r5, g5, b5);
				defaultTileTint = tileTint;
			}
			if (jsonDocument.RootElement.TryGetProperty("useLegacyTriggerOffset", out var value7))
			{
				useLegacyTriggerOffset = value7.GetBoolean();
				if (MenuOptionLegacyTriggers != null)
				{
					MenuOptionLegacyTriggers.IsChecked = useLegacyTriggerOffset;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("hideColorTriggers", out var value8))
			{
				try
				{
					hideColorTriggers = value8.GetBoolean();
				}
				catch
				{
					hideColorTriggers = false;
				}
				if (MenuOptionHideColorTriggers != null)
				{
					MenuOptionHideColorTriggers.IsChecked = hideColorTriggers;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("hideInvisibleSprites", out var value9))
			{
				try
				{
					hideInvisibleSprites = value9.GetBoolean();
				}
				catch
				{
					hideInvisibleSprites = false;
				}
				if (MenuOptionHideInvisibleSprites != null)
				{
					MenuOptionHideInvisibleSprites.IsChecked = hideInvisibleSprites;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("lockSpritesToSet", out var value10))
			{
				try
				{
					lockSpritesToSet = value10.GetBoolean();
				}
				catch
				{
					lockSpritesToSet = false;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("showAccurateTileset", out var value11))
			{
				try
				{
					showAccurateTileset = value11.GetBoolean();
				}
				catch
				{
					showAccurateTileset = false;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("suppressCollisionMessages", out var value12))
			{
				try
				{
					suppressCollisionMessages = value12.GetBoolean();
				}
				catch
				{
					suppressCollisionMessages = true;
				}
			}
			else
			{
				suppressCollisionMessages = true;
			}
			if (MenuOptionSuppressCollisionMessages != null)
			{
				MenuOptionSuppressCollisionMessages.IsChecked = suppressCollisionMessages;
			}
			if (jsonDocument.RootElement.TryGetProperty("gridDarkness", out var value13))
			{
				try
				{
					gridDarkness = value13.GetDouble();
				}
				catch
				{
					try
					{
						gridDarkness = value13.GetSingle();
					}
					catch
					{
					}
				}
				if (GridDarknessSlider != null)
				{
					GridDarknessSlider.Value = gridDarkness;
				}
				gridDirty = true;
			}
			if (jsonDocument.RootElement.TryGetProperty("playerColor", out var value14) && (value14.GetArrayLength() == 3 || value14.GetArrayLength() == 4))
			{
				byte a5 = byte.MaxValue;
				int num2 = 0;
				if (value14.GetArrayLength() == 4)
				{
					a5 = (byte)value14[0].GetInt32();
					num2 = 1;
				}
				byte r6 = (byte)value14[num2].GetInt32();
				byte g6 = (byte)value14[num2 + 1].GetInt32();
				byte b6 = (byte)value14[num2 + 2].GetInt32();
				playerTint = Color.FromArgb(a5, r6, g6, b6);
			}
			if (jsonDocument.RootElement.TryGetProperty("playerColorEnabled", out var value15))
			{
				try
				{
					playerTintEnabled = value15.GetBoolean();
				}
				catch
				{
					playerTintEnabled = false;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("swapMouseWheelScroll", out var value16))
			{
				try
				{
					swapMouseWheelScroll = value16.GetBoolean();
				}
				catch
				{
					swapMouseWheelScroll = false;
				}
				if (MenuOptionSwapMouseWheel != null)
				{
					MenuOptionSwapMouseWheel.IsChecked = swapMouseWheelScroll;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("invertPinchGesture", out var value17))
			{
				try
				{
					invertPinchGesture = value17.GetBoolean();
				}
				catch
				{
					invertPinchGesture = true;
				}
				if (MenuOptionSwapPinch != null)
				{
					MenuOptionSwapPinch.IsChecked = invertPinchGesture;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("hideBackground", out var value18))
			{
				try
				{
					hideBackground = value18.GetBoolean();
				}
				catch
				{
					hideBackground = false;
				}
				if (MenuOptionHideBackground != null)
				{
					MenuOptionHideBackground.IsChecked = hideBackground;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("hideGround", out var value19))
			{
				try
				{
					hideGround = value19.GetBoolean();
				}
				catch
				{
					hideGround = false;
				}
				if (MenuOptionHideGround != null)
				{
					MenuOptionHideGround.IsChecked = hideGround;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("useFastZoom", out var value20))
			{
				try
				{
					useFastZoom = value20.GetBoolean();
				}
				catch
				{
					useFastZoom = false;
				}
				if (MenuOptionFastZoom != null)
				{
					MenuOptionFastZoom.IsChecked = useFastZoom;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("simulatorScale", out var value21))
			{
				try
				{
					int num3 = value21.GetInt32();
					if (num3 < 1)
					{
						num3 = 1;
					}
					if (num3 > 4)
					{
						num3 = 4;
					}
					loadedSimulatorScale = num3;
				}
				catch
				{
					loadedSimulatorScale = 1;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("openSimulatorPaused", out var value22))
			{
				try
				{
					loadedOpenSimulatorPaused = value22.GetBoolean();
				}
				catch
				{
					loadedOpenSimulatorPaused = false;
				}
				if (MenuOptionOpenSimulatorPaused != null)
				{
					MenuOptionOpenSimulatorPaused.IsChecked = loadedOpenSimulatorPaused;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("noDeath", out var value23))
			{
				try
				{
					Option_NoDeath = value23.GetBoolean();
				}
				catch
				{
					Option_NoDeath = false;
				}
				if (MenuOptionNoDeath != null)
				{
					MenuOptionNoDeath.IsChecked = Option_NoDeath;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("showTileHitboxes", out var value24))
			{
				try
				{
					Option_ShowTileHitboxes = value24.GetBoolean();
				}
				catch
				{
					Option_ShowTileHitboxes = false;
				}
				if (MenuOptionTileHitboxes != null)
				{
					MenuOptionTileHitboxes.IsChecked = Option_ShowTileHitboxes;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("camMode", out var value25))
			{
				try
				{
					Option_CamMode = value25.GetBoolean();
				}
				catch
				{
					Option_CamMode = false;
				}
				if (MenuOptionCamMode != null)
				{
					MenuOptionCamMode.IsChecked = Option_CamMode;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("tileboardPosition", out var value26))
			{
				tileboardPosition = value26.GetString() ?? "LEFT";
			}
			else
			{
				tileboardPosition = "LEFT";
			}
			if (MenuTileboardLeft != null)
			{
				MenuTileboardLeft.IsChecked = tileboardPosition == "LEFT";
			}
			if (MenuTileboardRight != null)
			{
				MenuTileboardRight.IsChecked = tileboardPosition == "RIGHT";
			}
			if (MenuTileboardTop != null)
			{
				MenuTileboardTop.IsChecked = tileboardPosition == "TOP";
			}
			if (MenuTileboardBottom != null)
			{
				MenuTileboardBottom.IsChecked = tileboardPosition == "BOTTOM";
			}
			if (jsonDocument.RootElement.TryGetProperty("famistudioPath", out var value27))
			{
				try
				{
					famiStudioPath = value27.GetString();
				}
				catch
				{
					famiStudioPath = null;
				}
				if (!string.IsNullOrEmpty(famiStudioPath))
				{
					try
					{
						famiIntegration.LoadFromFolder(famiStudioPath);
					}
					catch
					{
					}
				}
				else
				{
					try
					{
						string path2 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FamiStudio");
						if (Directory.Exists(path2))
						{
							famiStudioPath = path2;
							try
							{
								famiIntegration.LoadFromFolder(famiStudioPath);
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
			}
			if (jsonDocument.RootElement.TryGetProperty("mesenPath", out var value28))
			{
				try
				{
					mesenPath = value28.GetString();
				}
				catch
				{
					mesenPath = null;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("famidashRomPath", out var value29))
			{
				try
				{
					famidashRomPath = value29.GetString();
				}
				catch
				{
					famidashRomPath = null;
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("mesenRamAddresses", out var value30))
			{
				try
				{
					mesenRamAddresses = value30.GetString() ?? mesenRamAddresses;
				}
				catch
				{
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("mesenCaptureFrames", out var value31))
			{
				try
				{
					mesenCaptureFrames = Math.Max(1, value31.GetInt32());
				}
				catch
				{
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("mesenCaptureEveryNFrames", out var value32))
			{
				try
				{
					mesenCaptureEveryNFrames = Math.Max(1, value32.GetInt32());
				}
				catch
				{
				}
			}
			if (!jsonDocument.RootElement.TryGetProperty("mesenCaptureTimeoutSeconds", out var value33))
			{
				return;
			}
			try
			{
				mesenCaptureTimeoutSeconds = Math.Max(10, value33.GetInt32());
			}
			catch
			{
			}
			if (jsonDocument.RootElement.TryGetProperty("overlayAndFollow", out var value34))
			{
				try
				{
					_overlayAndFollow = value34.GetBoolean();
				}
				catch
				{
				}
			}
			if (jsonDocument.RootElement.TryGetProperty("camFollow", out var value35))
			{
				try
				{
					_camFollow = value35.GetBoolean();
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
		finally
		{
			ClearTintedCaches();
			try
			{
				ApplyLockSpritesToSet();
			}
			catch
			{
			}
		}
	}

	private void SaveSettings(Color c)
	{
		try
		{
			var value = new
			{
				version = 2,
				background = new int[4] { c.A, c.R, c.G, c.B },
				backgroundTint = new int[4] { defaultBackgroundTint.A, defaultBackgroundTint.R, defaultBackgroundTint.G, defaultBackgroundTint.B },
				groundTint = new int[4] { defaultGroundTint.A, defaultGroundTint.R, defaultGroundTint.G, defaultGroundTint.B },
				tileTint = new int[4] { defaultTileTint.A, defaultTileTint.R, defaultTileTint.G, defaultTileTint.B },
				useLegacyTriggerOffset = useLegacyTriggerOffset,
				swapMouseWheelScroll = swapMouseWheelScroll,
				invertPinchGesture = invertPinchGesture,
				hideColorTriggers = hideColorTriggers,
				hideInvisibleSprites = hideInvisibleSprites,
				hideBackground = hideBackground,
				hideGround = hideGround,
				useFastZoom = useFastZoom,
				simulatorScale = loadedSimulatorScale,
				lockSpritesToSet = lockSpritesToSet,
				showAccurateTileset = showAccurateTileset,
				suppressCollisionMessages = suppressCollisionMessages,
				playerColor = new int[4] { playerTint.A, playerTint.R, playerTint.G, playerTint.B },
				playerColorEnabled = playerTintEnabled,
				gridDarkness = gridDarkness,
				famistudioPath = (string.IsNullOrEmpty(famiStudioPath) ? null : famiStudioPath),
				mesenPath = (string.IsNullOrEmpty(mesenPath) ? null : mesenPath),
				famidashRomPath = (string.IsNullOrEmpty(famidashRomPath) ? null : famidashRomPath),
				mesenRamAddresses = mesenRamAddresses,
				mesenCaptureFrames = mesenCaptureFrames,
				mesenCaptureEveryNFrames = mesenCaptureEveryNFrames,
				mesenCaptureTimeoutSeconds = mesenCaptureTimeoutSeconds,
				tileboardPosition = tileboardPosition,
				noDeath = Option_NoDeath,
				showTileHitboxes = Option_ShowTileHitboxes,
				camMode = Option_CamMode,
				openSimulatorPaused = loadedOpenSimulatorPaused,
				overlayAndFollow = _overlayAndFollow,
				camFollow = _camFollow
			};
			string contents = JsonSerializer.Serialize(value);
			string baseDirectory = AppContext.BaseDirectory;
			string path = System.IO.Path.Combine(baseDirectory, "editor-settings.json");
			File.WriteAllText(path, contents);
		}
		catch
		{
		}
	}

	private void SaveSettingsWithTriggerOption()
	{
		try
		{
			if (!suppressSettingsSave)
			{
				Color c = ((mapBackground is SolidColorBrush solidColorBrush) ? solidColorBrush.Color : Color.FromRgb(59, 59, 59));
				SaveSettings(c);
			}
		}
		catch
		{
		}
	}

	private void InitDefaultMap()
	{
		tiles = Enumerable.Repeat(-1, mapWidth * mapHeight).ToArray();
		sprites = Enumerable.Repeat(-1, mapWidth * mapHeight).ToArray();
		if (WidthBox != null)
		{
			WidthBox.Text = mapWidth.ToString();
		}
		if (HeightBox != null)
		{
			HeightBox.Text = mapHeight.ToString();
		}
	}

	private void CreateNewTab(string? filePath = null)
	{
		if (filePath == null)
		{
			int num = 200;
			int num2 = 27;
			int[] array = Enumerable.Repeat(-1, num * num2).ToArray();
			int[] array2 = Enumerable.Repeat(-1, num * num2).ToArray();
			FileTabData fileTabData = new FileTabData
			{
				FilePath = null,
				Tiles = array,
				Sprites = array2,
				SpritePixelOffsets = new Dictionary<int, (int, int)>(),
				MapWidth = num,
				MapHeight = num2,
				HasUnsavedChanges = false,
				LoadedTilesetSource = loadedTilesetSource,
				LoadedSpritesetSource = loadedSpritesetSource,
				LoadedHasEditorSettings = loadedHasEditorSettings,
				LoadedChunkWidth = loadedChunkWidth,
				LoadedChunkHeight = loadedChunkHeight,
				LoadedExportTarget = loadedExportTarget,
				LoadedExportFormat = loadedExportFormat,
				LoadedParallaxSource = loadedParallaxSource,
				LoadedParallaxX = loadedParallaxX,
				LoadedParallaxY = loadedParallaxY,
				LoadedParallaxRepeatX = loadedParallaxRepeatX,
				LoadedParallaxRepeatY = loadedParallaxRepeatY,
				LoadedHasParallaxLayer = loadedHasParallaxLayer,
				LoadedGroundSource = loadedGroundSource,
				LoadedGroundOffsetY = loadedGroundOffsetY,
				LoadedGroundRepeatX = loadedGroundRepeatX,
				LoadedHasGroundLayer = loadedHasGroundLayer,
				LoadedDecoSet = "DECO1",
				LoadedBlockSet = "BLOCKSA",
				LoadedSpikeSet = "SPIKESA",
				LoadedStartingSpeedUiIndex = 1,
				LoadedStartingBackgroundColor = 18,
				LoadedStartingGameMode = 0,
				LoadedStartingGroundColor = 2,
				LoadedStartingLowerText = null,
				LoadedStartingUpperText = null,
				LoadedStartingDifficulty = 0,
				LoadedStartingStars = 3,
				LoadedMaxFallSpeed = 6,
				CreatedAsUntitled = true,
				NoParallaxBg = false,
				BackgroundTint = backgroundTint,
				GroundTint = groundTint,
				TileTint = tileTint,
				StartPosX = null,
				StartPosY = null
			};
			openFiles.Add(fileTabData);
			currentFileIndex = openFiles.Count - 1;
			startPosMarkerX = null;
			startPosMarkerY = null;
			try
			{
				if (startPosMarker != null)
				{
					CanvasHost?.Children.Remove(startPosMarker);
				}
			}
			catch
			{
			}
			startPosMarker = null;
			StackPanel stackPanel = new StackPanel
			{
				Orientation = System.Windows.Controls.Orientation.Horizontal
			};
			TextBlock element = new TextBlock
			{
				Text = "Untitled",
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			};
			System.Windows.Controls.Button button = new System.Windows.Controls.Button
			{
				Content = "\ufffd",
				Width = 16.0,
				Height = 16.0,
				Padding = new Thickness(0.0),
				Margin = new Thickness(0.0),
				VerticalAlignment = VerticalAlignment.Center,
				Background = Brushes.Transparent,
				BorderThickness = new Thickness(0.0),
				FontSize = 14.0,
				FontWeight = FontWeights.Bold,
				Cursor = System.Windows.Input.Cursors.Hand,
				Visibility = Visibility.Visible,
				Tag = fileTabData
			};
			button.Click += CloseTab_Click;
			stackPanel.Children.Add(element);
			stackPanel.Children.Add(button);
			TabItem tabItem = new TabItem
			{
				Header = stackPanel,
				Tag = fileTabData
			};
			int num3 = FileTabControl.Items.Count;
			if (num3 > 0 && FileTabControl.Items[num3 - 1] is TabItem { Tag: var tag } && tag?.ToString() == "NEW")
			{
				num3--;
			}
			isHandlingNewTab = true;
			try
			{
				FileTabControl.Items.Insert(num3, tabItem);
				lastProgrammaticSelectedTab = tabItem;
				FileTabControl.SelectedItem = tabItem;
				currentFileIndex = openFiles.Count - 1;
				try
				{
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						try
						{
							Activate();
							Focus();
						}
						catch
						{
						}
					}, DispatcherPriority.Background);
				}
				catch
				{
				}
				try
				{
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						lastProgrammaticSelectedTab = null;
					}, DispatcherPriority.Background);
				}
				catch
				{
				}
			}
			finally
			{
				isHandlingNewTab = false;
			}
			try
			{
				SwitchToTab(openFiles.Count - 1);
			}
			catch
			{
			}
			EnsureNewTabButton();
			return;
		}
		FileTabData fileTabData2 = new FileTabData
		{
			FilePath = filePath,
			Tiles = ((tiles != null && tiles.Length != 0) ? ((int[])tiles.Clone()) : Array.Empty<int>()),
			Sprites = ((sprites != null && sprites.Length != 0) ? ((int[])sprites.Clone()) : Array.Empty<int>()),
			SpritePixelOffsets = new Dictionary<int, (int, int)>(spritePixelOffsets),
			MapWidth = mapWidth,
			MapHeight = mapHeight,
			HasUnsavedChanges = hasUnsavedChanges,
			LoadedTilesetSource = loadedTilesetSource,
			LoadedSpritesetSource = loadedSpritesetSource,
			LoadedHasEditorSettings = loadedHasEditorSettings,
			LoadedChunkWidth = loadedChunkWidth,
			LoadedChunkHeight = loadedChunkHeight,
			LoadedExportTarget = loadedExportTarget,
			LoadedExportFormat = loadedExportFormat,
			LoadedParallaxSource = loadedParallaxSource,
			LoadedParallaxX = loadedParallaxX,
			LoadedParallaxY = loadedParallaxY,
			LoadedParallaxRepeatX = loadedParallaxRepeatX,
			LoadedParallaxRepeatY = loadedParallaxRepeatY,
			LoadedHasParallaxLayer = loadedHasParallaxLayer,
			LoadedGroundSource = loadedGroundSource,
			LoadedGroundOffsetY = loadedGroundOffsetY,
			LoadedGroundRepeatX = loadedGroundRepeatX,
			LoadedHasGroundLayer = loadedHasGroundLayer,
			LoadedDecoSet = loadedDecoSet,
			LoadedBlockSet = loadedBlockSet,
			LoadedSpikeSet = loadedSpikeSet,
			LoadedStartingSpeedUiIndex = loadedStartingSpeedUiIndex,
			LoadedStartingBackgroundColor = loadedStartingBackgroundColor,
			LoadedStartingGameMode = loadedStartingGameMode,
			LoadedStartingGroundColor = loadedStartingGroundColor,
			LoadedStartingLowerText = loadedStartingLowerText,
			LoadedStartingUpperText = loadedStartingUpperText,
			LoadedStartingDifficulty = loadedStartingDifficulty,
			LoadedStartingStars = loadedStartingStars,
			NoParallaxBg = noParallaxBg,
			BackgroundTint = backgroundTint,
			GroundTint = groundTint,
			TileTint = tileTint,
			StartPosX = null,
			StartPosY = null
		};
		openFiles.Add(fileTabData2);
		currentFileIndex = openFiles.Count - 1;
		startPosMarkerX = null;
		startPosMarkerY = null;
		try
		{
			if (startPosMarker != null)
			{
				CanvasHost?.Children.Remove(startPosMarker);
			}
		}
		catch
		{
		}
		startPosMarker = null;
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = System.Windows.Controls.Orientation.Horizontal
		};
		TextBlock element2 = new TextBlock
		{
			Text = ((filePath != null) ? System.IO.Path.GetFileName(filePath) : "Untitled"),
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		System.Windows.Controls.Button button2 = new System.Windows.Controls.Button
		{
			Content = "\ufffd",
			Width = 16.0,
			Height = 16.0,
			Padding = new Thickness(0.0),
			Margin = new Thickness(0.0),
			VerticalAlignment = VerticalAlignment.Center,
			Background = Brushes.Transparent,
			BorderThickness = new Thickness(0.0),
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Cursor = System.Windows.Input.Cursors.Hand,
			Visibility = Visibility.Visible,
			Tag = fileTabData2
		};
		button2.Click += CloseTab_Click;
		stackPanel2.Children.Add(element2);
		stackPanel2.Children.Add(button2);
		TabItem tabItem3 = new TabItem
		{
			Header = stackPanel2,
			Tag = fileTabData2
		};
		int num4 = FileTabControl.Items.Count;
		if (num4 > 0 && FileTabControl.Items[num4 - 1] is TabItem { Tag: var tag2 } && tag2?.ToString() == "NEW")
		{
			num4--;
		}
		isHandlingNewTab = true;
		try
		{
			FileTabControl.Items.Insert(num4, tabItem3);
			lastProgrammaticSelectedTab = tabItem3;
			FileTabControl.SelectedItem = tabItem3;
			try
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					try
					{
						Activate();
						Focus();
					}
					catch
					{
					}
				}, DispatcherPriority.Background);
			}
			catch
			{
			}
			try
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					lastProgrammaticSelectedTab = null;
				}, DispatcherPriority.Background);
			}
			catch
			{
			}
		}
		finally
		{
			isHandlingNewTab = false;
		}
		EnsureNewTabButton();
	}

	public unsafe void OpenSimulatorWindow()
	{
		try
		{
			Dictionary<int, ImageSource> dictionary = new Dictionary<int, ImageSource>();
			try
			{
				if (cubePortalSprite != null)
				{
					dictionary[0] = cubePortalSprite;
				}
				if (shipPortalSprite != null)
				{
					dictionary[1] = shipPortalSprite;
				}
				if (ballPortalSprite != null)
				{
					dictionary[2] = ballPortalSprite;
				}
				if (ufoPortalSprite != null)
				{
					dictionary[3] = ufoPortalSprite;
				}
				if (robotPortalSprite != null)
				{
					dictionary[4] = robotPortalSprite;
				}
				if (wavePortalSprite != null)
				{
					dictionary[36] = wavePortalSprite;
				}
				if (spiderPortalSprite != null)
				{
					dictionary[23] = spiderPortalSprite;
				}
				if (spiderPadSprite != null)
				{
					dictionary[86] = spiderPadSprite;
				}
				if (spiderPadUpsideDownSprite != null)
				{
					dictionary[87] = spiderPadUpsideDownSprite;
				}
				if (swingcopterPortalSprite != null)
				{
					dictionary[75] = swingcopterPortalSprite;
				}
				if (ninjaPortalSprite != null)
				{
					dictionary[88] = ninjaPortalSprite;
				}
				if (pogoPortalSprite != null)
				{
					dictionary[106] = pogoPortalSprite;
				}
				if (snakePortalSprite != null)
				{
					dictionary[107] = snakePortalSprite;
				}
				if (footballPortalSprite != null)
				{
					dictionary[108] = footballPortalSprite;
				}
				if (teleportPortalEnterSprite != null)
				{
					dictionary[78] = teleportPortalEnterSprite;
				}
				if (teleportPortalExitSprite != null)
				{
					dictionary[79] = teleportPortalExitSprite;
				}
				if (teleportPortalHorizontalEnterDownSprite != null)
				{
					dictionary[102] = teleportPortalHorizontalEnterDownSprite;
				}
				if (teleportPortalHorizontalExitUpSprite != null)
				{
					dictionary[103] = teleportPortalHorizontalExitUpSprite;
				}
				if (teleportPortalHorizontalEnterUpSprite != null)
				{
					dictionary[104] = teleportPortalHorizontalEnterUpSprite;
				}
				if (teleportPortalHorizontalExitDownSprite != null)
				{
					dictionary[105] = teleportPortalHorizontalExitDownSprite;
				}
				if (speed05xPortalSprite != null)
				{
					dictionary[20] = speed05xPortalSprite;
				}
				if (speed1xPortalSprite != null)
				{
					dictionary[21] = speed1xPortalSprite;
				}
				if (speed2xPortalSprite != null)
				{
					dictionary[22] = speed2xPortalSprite;
				}
				if (speed3xPortalSprite != null)
				{
					dictionary[32] = speed3xPortalSprite;
				}
				if (speed4xPortalSprite != null)
				{
					dictionary[33] = speed4xPortalSprite;
				}
				if (speedSpecialPortalSprite != null)
				{
					dictionary[109] = speedSpecialPortalSprite;
				}
				if (dualPortalSprite != null)
				{
					dictionary[34] = dualPortalSprite;
				}
				if (singlePortalSprite != null)
				{
					dictionary[35] = singlePortalSprite;
				}
				if (miniPortalSprite != null)
				{
					dictionary[24] = miniPortalSprite;
				}
				if (growthPortalSprite != null)
				{
					dictionary[25] = growthPortalSprite;
				}
				if (gravityDownPortalSprite != null)
				{
					dictionary[8] = gravityDownPortalSprite;
				}
				if (gravityUpPortalSprite != null)
				{
					dictionary[9] = gravityUpPortalSprite;
				}
				if (gravityDownDownwardsPortalSprite != null)
				{
					dictionary[16] = gravityDownDownwardsPortalSprite;
				}
				if (gravityDownUpwardsPortalSprite != null)
				{
					dictionary[17] = gravityDownUpwardsPortalSprite;
				}
				if (gravityUpDownwardsPortalSprite != null)
				{
					dictionary[18] = gravityUpDownwardsPortalSprite;
				}
				if (gravityUpUpwardsPortalSprite != null)
				{
					dictionary[19] = gravityUpUpwardsPortalSprite;
				}
				if (gravity1ThirdXPortalSprite != null)
				{
					dictionary[95] = gravity1ThirdXPortalSprite;
				}
				if (gravity1HalfXPortalSprite != null)
				{
					dictionary[96] = gravity1HalfXPortalSprite;
				}
				if (gravity2ThirdXPortalSprite != null)
				{
					dictionary[97] = gravity2ThirdXPortalSprite;
				}
				if (gravity2XPortalSprite != null)
				{
					dictionary[98] = gravity2XPortalSprite;
				}
				if (gravity1XPortalSprite != null)
				{
					dictionary[99] = gravity1XPortalSprite;
				}
				if (yellowOrbFrame1 != null && yellowOrbFrame1.Length != 0)
				{
					dictionary[11] = yellowOrbFrame1[0];
					dictionary[31] = yellowOrbFrame1[0];
					dictionary[41] = yellowOrbFrame1[0];
				}
				if (blueOrbFrame1 != null && blueOrbFrame1.Length != 0)
				{
					dictionary[5] = blueOrbFrame1[0];
				}
				if (whiteOrbFrame1 != null && whiteOrbFrame1.Length != 0)
				{
					dictionary[122] = whiteOrbFrame1[0];
				}
				if (coinFrame1 != null && coinFrame1.Length != 0)
				{
					dictionary[7] = coinFrame1[0];
					dictionary[26] = coinFrame1[0];
					dictionary[27] = coinFrame1[0];
				}
				if (miniCoinFrame1 != null && miniCoinFrame1.Length != 0)
				{
					dictionary[110] = miniCoinFrame1[0];
				}
				if (redPadFrame1 != null && redPadFrame1.Length != 0)
				{
					dictionary[82] = redPadFrame1[0];
				}
				if (redPadUpFrame1 != null && redPadUpFrame1.Length != 0)
				{
					dictionary[83] = redPadUpFrame1[0];
				}
				if (starFrame1 != null && starFrame1.Length != 0)
				{
					dictionary[54] = starFrame1[0];
				}
				if (pulsingBallFrame1 != null && pulsingBallFrame1.Length != 0)
				{
					dictionary[73] = pulsingBallFrame1[0];
				}
				if (musicNoteFrame1 != null && musicNoteFrame1.Length != 0)
				{
					dictionary[74] = musicNoteFrame1[0];
				}
				if (diamondFrame1 != null && diamondFrame1.Length != 0)
				{
					dictionary[50] = diamondFrame1[0];
				}
				if (diamondHalfFrame1 != null && diamondHalfFrame1.Length != 0)
				{
					dictionary[51] = diamondHalfFrame1[0];
				}
				if (questionMarkFrame1 != null && questionMarkFrame1.Length != 0)
				{
					dictionary[52] = questionMarkFrame1[0];
				}
				if (exclamationFrame1 != null && exclamationFrame1.Length != 0)
				{
					dictionary[53] = exclamationFrame1[0];
				}
				if (xFrame1 != null && xFrame1.Length != 0)
				{
					dictionary[55] = xFrame1[0];
				}
				if (poleShortFrame1 != null && poleShortFrame1.Length != 0)
				{
					dictionary[44] = poleShortFrame1[0];
				}
				if (poleShortUpsideDownFrame1 != null && poleShortUpsideDownFrame1.Length != 0)
				{
					dictionary[60] = poleShortUpsideDownFrame1[0];
				}
				if (chainFrame1 != null && chainFrame1.Length != 0)
				{
					dictionary[45] = chainFrame1[0];
					if (chainUpsideFrame1 != null && chainUpsideFrame1.Length != 0)
					{
						dictionary[61] = chainUpsideFrame1[0];
					}
				}
				if (decoSpikesFrame1 != null && decoSpikesFrame1.Length != 0)
				{
					dictionary[46] = decoSpikesFrame1[0];
				}
				if (decoSpikesUpsideDownFrame1 != null && decoSpikesUpsideDownFrame1.Length != 0)
				{
					dictionary[47] = decoSpikesUpsideDownFrame1[0];
				}
				if (decoSpikesSmallFrame1 != null && decoSpikesSmallFrame1.Length != 0)
				{
					dictionary[48] = decoSpikesSmallFrame1[0];
				}
				if (decoSpikesSmallUpsideDownFrame1 != null && decoSpikesSmallUpsideDownFrame1.Length != 0)
				{
					dictionary[49] = decoSpikesSmallUpsideDownFrame1[0];
				}
				try
				{
					if (bluePadDownFrame1 != null && bluePadDownFrame1.Length != 0)
					{
						dictionary[253] = bluePadDownFrame1[0];
					}
					if (bluePadUpFrame1 != null && bluePadUpFrame1.Length != 0)
					{
						dictionary[254] = bluePadUpFrame1[0];
					}
				}
				catch
				{
				}
				try
				{
					if (yellowPadDownFrame1 != null && yellowPadDownFrame1.Length != 0)
					{
						dictionary[10] = yellowPadDownFrame1[0];
					}
					if (yellowPadUpFrame1 != null && yellowPadUpFrame1.Length != 0)
					{
						dictionary[12] = yellowPadUpFrame1[0];
					}
				}
				catch
				{
				}
				try
				{
					WriteableBitmap writeableBitmap = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
					writeableBitmap.Lock();
					byte* backBuffer = (byte*)writeableBitmap.BackBuffer;
					for (int i = 0; i < 4; i++)
					{
						backBuffer[i] = 0;
					}
					writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, 1, 1));
					writeableBitmap.Unlock();
					writeableBitmap.Freeze();
					dictionary[246] = writeableBitmap;
					dictionary[247] = writeableBitmap;
					dictionary[248] = writeableBitmap;
					dictionary[249] = writeableBitmap;
					dictionary[250] = writeableBitmap;
				}
				catch
				{
				}
				try
				{
					if (bluePadDownFrame1 != null && bluePadDownFrame1.Length != 0)
					{
						dictionary[13] = bluePadDownFrame1[0];
					}
					if (bluePadUpFrame1 != null && bluePadUpFrame1.Length != 0)
					{
						dictionary[14] = bluePadUpFrame1[0];
					}
					if (pinkPadDownFrame1 != null && pinkPadDownFrame1.Length != 0)
					{
						dictionary[37] = pinkPadDownFrame1[0];
					}
					if (pinkPadUpFrame1 != null && pinkPadUpFrame1.Length != 0)
					{
						dictionary[38] = pinkPadUpFrame1[0];
					}
					if (greenPadExpandedFrame1 != null && greenPadExpandedFrame1.Length != 0)
					{
						dictionary[101] = greenPadExpandedFrame1[0];
					}
				}
				catch
				{
				}
				try
				{
					if (poleShortFrame1 != null && poleShortFrame1.Length != 0)
					{
						dictionary[44] = poleShortFrame1[0];
					}
					if (poleShortUpsideDownFrame1 != null && poleShortUpsideDownFrame1.Length != 0)
					{
						dictionary[60] = poleShortUpsideDownFrame1[0];
					}
					if (poleMediumFrame1 != null && poleMediumFrame1.Length != 0)
					{
						dictionary[44] = poleMediumFrame1[0];
					}
					if (poleMediumUpsideDownFrame1 != null && poleMediumUpsideDownFrame1.Length != 0)
					{
						dictionary[60] = poleMediumUpsideDownFrame1[0];
					}
					if (poleMediumFrame1 != null && poleMediumFrame1.Length != 0)
					{
						dictionary[43] = poleMediumFrame1[0];
					}
					if (poleMediumUpsideDownFrame1 != null && poleMediumUpsideDownFrame1.Length != 0)
					{
						dictionary[59] = poleMediumUpsideDownFrame1[0];
					}
					if (poleLongFrame1 != null && poleLongFrame1.Length != 0)
					{
						dictionary[42] = poleLongFrame1[0];
					}
					if (poleLongUpsideDownFrame1 != null && poleLongUpsideDownFrame1.Length != 0)
					{
						dictionary[58] = poleLongUpsideDownFrame1[0];
					}
					if (poleLeftShortFrame1 != null && poleLeftShortFrame1.Length != 0)
					{
						dictionary[56] = poleLeftShortFrame1[0];
					}
					if (poleRightShortFrame1 != null && poleRightShortFrame1.Length != 0)
					{
						dictionary[57] = poleRightShortFrame1[0];
					}
					if (poleLeftMediumFrame1 != null && poleLeftMediumFrame1.Length != 0)
					{
						dictionary[62] = poleLeftMediumFrame1[0];
					}
					if (poleRightMediumFrame1 != null && poleRightMediumFrame1.Length != 0)
					{
						dictionary[63] = poleRightMediumFrame1[0];
					}
				}
				catch
				{
				}
			}
			catch
			{
			}
			Dictionary<int, ImageSource[]> dictionary2 = new Dictionary<int, ImageSource[]>();
			try
			{
				if (yellowOrbFrame1 != null && yellowOrbFrame2 != null && yellowOrbFrame3 != null && yellowOrbFrame4 != null)
				{
					for (int j = 0; j < 3; j++)
					{
						ImageSource[] value = new ImageSource[4]
						{
							(yellowOrbFrame1.Length > j) ? yellowOrbFrame1[j] : null,
							(yellowOrbFrame2.Length > j) ? yellowOrbFrame2[j] : null,
							(yellowOrbFrame3.Length > j) ? yellowOrbFrame3[j] : null,
							(yellowOrbFrame4.Length > j) ? yellowOrbFrame4[j] : null
						};
						dictionary2[j switch
						{
							1 => 31, 
							0 => 11, 
							_ => 41, 
						}] = value;
					}
				}
				if (blueOrbFrame1 != null && blueOrbFrame2 != null && blueOrbFrame3 != null && blueOrbFrame4 != null)
				{
					dictionary2[5] = new ImageSource[4]
					{
						blueOrbFrame1[0],
						blueOrbFrame2[0],
						blueOrbFrame3[0],
						blueOrbFrame4[0]
					};
				}
				if (whiteOrbFrame1 != null && whiteOrbFrame2 != null && whiteOrbFrame3 != null && whiteOrbFrame4 != null)
				{
					dictionary2[122] = new ImageSource[4]
					{
						whiteOrbFrame1[0],
						whiteOrbFrame2[0],
						whiteOrbFrame3[0],
						whiteOrbFrame4[0]
					};
				}
				if (coinFrame1 != null && coinFrame2 != null && coinFrame3 != null && coinFrame4 != null)
				{
					for (int k = 0; k < 3; k++)
					{
						dictionary2[k switch
						{
							1 => 26, 
							0 => 7, 
							_ => 27, 
						}] = new ImageSource[4]
						{
							(coinFrame1.Length > k) ? coinFrame1[k] : null,
							(coinFrame2.Length > k) ? coinFrame2[k] : null,
							(coinFrame3.Length > k) ? coinFrame3[k] : null,
							(coinFrame4.Length > k) ? coinFrame4[k] : null
						};
					}
				}
				if (miniCoinFrame1 != null && miniCoinFrame2 != null && miniCoinFrame3 != null && miniCoinFrame4 != null)
				{
					dictionary2[110] = new ImageSource[4]
					{
						miniCoinFrame1[0],
						miniCoinFrame2[0],
						miniCoinFrame3[0],
						miniCoinFrame4[0]
					};
				}
				if (pinkOrbFrame1 != null && pinkOrbFrame2 != null && pinkOrbFrame3 != null && pinkOrbFrame4 != null)
				{
					dictionary2[6] = new ImageSource[4]
					{
						pinkOrbFrame1[0],
						pinkOrbFrame2[0],
						pinkOrbFrame3[0],
						pinkOrbFrame4[0]
					};
				}
				if (greenOrbFrame1 != null && greenOrbFrame2 != null && greenOrbFrame3 != null && greenOrbFrame4 != null)
				{
					dictionary2[39] = new ImageSource[4]
					{
						greenOrbFrame1[0],
						greenOrbFrame2[0],
						greenOrbFrame3[0],
						greenOrbFrame4[0]
					};
				}
				if (redOrbFrame1 != null && redOrbFrame2 != null && redOrbFrame3 != null && redOrbFrame4 != null)
				{
					dictionary2[40] = new ImageSource[4]
					{
						redOrbFrame1[0],
						redOrbFrame2[0],
						redOrbFrame3[0],
						redOrbFrame4[0]
					};
				}
				if (blackOrbFrame1 != null && blackOrbFrame2 != null && blackOrbFrame3 != null && blackOrbFrame4 != null)
				{
					dictionary2[68] = new ImageSource[4]
					{
						blackOrbFrame1[0],
						blackOrbFrame2[0],
						blackOrbFrame3[0],
						blackOrbFrame4[0]
					};
				}
				if (redPadFrame1 != null && redPadFrame2 != null && redPadFrame3 != null && redPadFrame4 != null)
				{
					dictionary2[82] = new ImageSource[4]
					{
						redPadFrame1[0],
						redPadFrame2[0],
						redPadFrame3[0],
						redPadFrame4[0]
					};
				}
				if (redPadUpFrame1 != null && redPadUpFrame2 != null && redPadUpFrame3 != null && redPadUpFrame4 != null)
				{
					dictionary2[83] = new ImageSource[4]
					{
						redPadUpFrame1[0],
						redPadUpFrame2[0],
						redPadUpFrame3[0],
						redPadUpFrame4[0]
					};
				}
				if (yellowPadDownFrame1 != null && yellowPadDownFrame2 != null && yellowPadDownFrame3 != null && yellowPadDownFrame4 != null)
				{
					dictionary2[10] = new ImageSource[4]
					{
						yellowPadDownFrame1[0],
						yellowPadDownFrame2[0],
						yellowPadDownFrame3[0],
						yellowPadDownFrame4[0]
					};
				}
				if (yellowPadUpFrame1 != null && yellowPadUpFrame2 != null && yellowPadUpFrame3 != null && yellowPadUpFrame4 != null)
				{
					dictionary2[12] = new ImageSource[4]
					{
						yellowPadUpFrame1[0],
						yellowPadUpFrame2[0],
						yellowPadUpFrame3[0],
						yellowPadUpFrame4[0]
					};
				}
				if (bluePadDownFrame1 != null && bluePadDownFrame2 != null && bluePadDownFrame3 != null && bluePadDownFrame4 != null)
				{
					dictionary2[13] = new ImageSource[4]
					{
						bluePadDownFrame1[0],
						bluePadDownFrame2[0],
						bluePadDownFrame3[0],
						bluePadDownFrame4[0]
					};
				}
				if (bluePadUpFrame1 != null && bluePadUpFrame2 != null && bluePadUpFrame3 != null && bluePadUpFrame4 != null)
				{
					dictionary2[14] = new ImageSource[4]
					{
						bluePadUpFrame1[0],
						bluePadUpFrame2[0],
						bluePadUpFrame3[0],
						bluePadUpFrame4[0]
					};
				}
				if (pinkPadDownFrame1 != null && pinkPadDownFrame2 != null && pinkPadDownFrame3 != null && pinkPadDownFrame4 != null)
				{
					dictionary2[37] = new ImageSource[4]
					{
						pinkPadDownFrame1[0],
						pinkPadDownFrame2[0],
						pinkPadDownFrame3[0],
						pinkPadDownFrame4[0]
					};
				}
				if (pinkPadUpFrame1 != null && pinkPadUpFrame2 != null && pinkPadUpFrame3 != null && pinkPadUpFrame4 != null)
				{
					dictionary2[38] = new ImageSource[4]
					{
						pinkPadUpFrame1[0],
						pinkPadUpFrame2[0],
						pinkPadUpFrame3[0],
						pinkPadUpFrame4[0]
					};
				}
				if (greenPadExpandedFrame1 != null && greenPadExpandedFrame2 != null && greenPadExpandedFrame3 != null && greenPadExpandedFrame4 != null)
				{
					dictionary2[101] = new ImageSource[4]
					{
						greenPadExpandedFrame1[0],
						greenPadExpandedFrame2[0],
						greenPadExpandedFrame3[0],
						greenPadExpandedFrame4[0]
					};
				}
				if (poleShortFrame1 != null && poleShortFrame2 != null)
				{
					dictionary2[44] = new ImageSource[2]
					{
						poleShortFrame1[0],
						poleShortFrame2[0]
					};
				}
				if (poleShortUpsideDownFrame1 != null && poleShortUpsideDownFrame2 != null)
				{
					dictionary2[60] = new ImageSource[2]
					{
						poleShortUpsideDownFrame1[0],
						poleShortUpsideDownFrame2[0]
					};
				}
				if (poleMediumFrame1 != null && poleMediumFrame2 != null)
				{
					dictionary2[44] = new ImageSource[2]
					{
						poleMediumFrame1[0],
						poleMediumFrame2[0]
					};
				}
				if (poleMediumUpsideDownFrame1 != null && poleMediumUpsideDownFrame2 != null)
				{
					dictionary2[60] = new ImageSource[2]
					{
						poleMediumUpsideDownFrame1[0],
						poleMediumUpsideDownFrame2[0]
					};
				}
				if (poleMediumFrame1 != null && poleMediumFrame2 != null)
				{
					dictionary2[43] = new ImageSource[2]
					{
						poleMediumFrame1[0],
						poleMediumFrame2[0]
					};
				}
				if (poleMediumUpsideDownFrame1 != null && poleMediumUpsideDownFrame2 != null)
				{
					dictionary2[59] = new ImageSource[2]
					{
						poleMediumUpsideDownFrame1[0],
						poleMediumUpsideDownFrame2[0]
					};
				}
				if (poleLongFrame1 != null && poleLongFrame2 != null)
				{
					dictionary2[42] = new ImageSource[2]
					{
						poleLongFrame1[0],
						poleLongFrame2[0]
					};
				}
				if (poleLongUpsideDownFrame1 != null && poleLongUpsideDownFrame2 != null)
				{
					dictionary2[58] = new ImageSource[2]
					{
						poleLongUpsideDownFrame1[0],
						poleLongUpsideDownFrame2[0]
					};
				}
				if (poleLeftShortFrame1 != null && poleLeftShortFrame2 != null)
				{
					dictionary2[56] = new ImageSource[2]
					{
						poleLeftShortFrame1[0],
						poleLeftShortFrame2[0]
					};
				}
				if (poleRightShortFrame1 != null && poleRightShortFrame2 != null)
				{
					dictionary2[57] = new ImageSource[2]
					{
						poleRightShortFrame1[0],
						poleRightShortFrame2[0]
					};
				}
				if (poleLeftMediumFrame1 != null && poleLeftMediumFrame2 != null)
				{
					dictionary2[62] = new ImageSource[2]
					{
						poleLeftMediumFrame1[0],
						poleLeftMediumFrame2[0]
					};
				}
				if (poleRightMediumFrame1 != null && poleRightMediumFrame2 != null)
				{
					dictionary2[63] = new ImageSource[2]
					{
						poleRightMediumFrame1[0],
						poleRightMediumFrame2[0]
					};
				}
				if (starFrame1 != null && starFrame2 != null)
				{
					dictionary2[54] = new ImageSource[2]
					{
						starFrame1[0],
						starFrame2[0]
					};
				}
				if (pulsingBallFrame1 != null && pulsingBallFrame2 != null)
				{
					dictionary2[73] = new ImageSource[2]
					{
						pulsingBallFrame1[0],
						pulsingBallFrame2[0]
					};
				}
				if (musicNoteFrame1 != null && musicNoteFrame2 != null)
				{
					dictionary2[74] = new ImageSource[2]
					{
						musicNoteFrame1[0],
						musicNoteFrame2[0]
					};
				}
				if (diamondFrame1 != null && diamondFrame2 != null)
				{
					dictionary2[50] = new ImageSource[2]
					{
						diamondFrame1[0],
						diamondFrame2[0]
					};
				}
				if (diamondHalfFrame1 != null && diamondHalfFrame2 != null)
				{
					dictionary2[51] = new ImageSource[2]
					{
						diamondHalfFrame1[0],
						diamondHalfFrame2[0]
					};
				}
				if (questionMarkFrame1 != null && questionMarkFrame2 != null)
				{
					dictionary2[52] = new ImageSource[2]
					{
						questionMarkFrame1[0],
						questionMarkFrame2[0]
					};
				}
				if (exclamationFrame1 != null && exclamationFrame2 != null)
				{
					dictionary2[53] = new ImageSource[2]
					{
						exclamationFrame1[0],
						exclamationFrame2[0]
					};
				}
				if (xFrame1 != null && xFrame2 != null)
				{
					dictionary2[55] = new ImageSource[2]
					{
						xFrame1[0],
						xFrame2[0]
					};
				}
				if (dashOrbRightFrame1 != null && dashOrbRightFrame2 != null)
				{
					dictionary2[69] = new ImageSource[2]
					{
						dashOrbRightFrame1[0],
						dashOrbRightFrame2[0]
					};
				}
				if (dashGravityOrbRightFrame1 != null && dashGravityOrbRightFrame2 != null)
				{
					dictionary2[70] = new ImageSource[2]
					{
						dashGravityOrbRightFrame1[0],
						dashGravityOrbRightFrame2[0]
					};
				}
				if (dashOrb45UpFrame1 != null && dashOrb45UpFrame2 != null)
				{
					dictionary2[76] = new ImageSource[2]
					{
						dashOrb45UpFrame1[0],
						dashOrb45UpFrame2[0]
					};
				}
				if (dashGravityOrb45UpFrame1 != null && dashGravityOrb45UpFrame2 != null)
				{
					dictionary2[77] = new ImageSource[2]
					{
						dashGravityOrb45UpFrame1[0],
						dashGravityOrb45UpFrame2[0]
					};
				}
				if (dashOrb45DownFrame1 != null && dashOrb45DownFrame2 != null)
				{
					dictionary2[80] = new ImageSource[2]
					{
						dashOrb45DownFrame1[0],
						dashOrb45DownFrame2[0]
					};
				}
				if (dashGravityOrb45DownFrame1 != null && dashGravityOrb45DownFrame2 != null)
				{
					dictionary2[81] = new ImageSource[2]
					{
						dashGravityOrb45DownFrame1[0],
						dashGravityOrb45DownFrame2[0]
					};
				}
				if (dashOrbUpFrame1 != null && dashOrbUpFrame2 != null)
				{
					dictionary2[91] = new ImageSource[2]
					{
						dashOrbUpFrame1[0],
						dashOrbUpFrame2[0]
					};
				}
				if (dashGravityOrbUpFrame1 != null && dashGravityOrbUpFrame2 != null)
				{
					dictionary2[92] = new ImageSource[2]
					{
						dashGravityOrbUpFrame1[0],
						dashGravityOrbUpFrame2[0]
					};
				}
				if (dashOrbDownFrame1 != null && dashOrbDownFrame2 != null)
				{
					dictionary2[93] = new ImageSource[2]
					{
						dashOrbDownFrame1[0],
						dashOrbDownFrame2[0]
					};
				}
				if (dashGravityOrbDownFrame1 != null && dashGravityOrbDownFrame2 != null)
				{
					dictionary2[94] = new ImageSource[2]
					{
						dashGravityOrbDownFrame1[0],
						dashGravityOrbDownFrame2[0]
					};
				}
				if (spiderOrbUpFrame1 != null && spiderOrbUpFrame2 != null)
				{
					dictionary2[84] = new ImageSource[2]
					{
						spiderOrbUpFrame1[0],
						spiderOrbUpFrame2[0]
					};
				}
				if (spiderOrbDownFrame1 != null && spiderOrbDownFrame2 != null)
				{
					dictionary2[85] = new ImageSource[2]
					{
						spiderOrbDownFrame1[0],
						spiderOrbDownFrame2[0]
					};
				}
				if (teleportOrbEnterFrame1 != null && teleportOrbEnterFrame2 != null)
				{
					dictionary2[89] = new ImageSource[2]
					{
						teleportOrbEnterFrame1[0],
						teleportOrbEnterFrame2[0]
					};
				}
				if (teleportOrbExitFrame1 != null && teleportOrbExitFrame2 != null)
				{
					dictionary2[90] = new ImageSource[2]
					{
						teleportOrbExitFrame1[0],
						teleportOrbExitFrame2[0]
					};
				}
			}
			catch
			{
			}
			bool flag = showAccurateTileset;
			try
			{
				if (!flag)
				{
					try
					{
						SetShowAccurateTileset(enabled: true, loadedBlockSet, loadedSpikeSet, persistSetting: false);
					}
					catch
					{
					}
				}
				try
				{
					string[] array = ((!noParallaxBg) ? new string[4] { "Assets.parallax.bmp", "parallax Blue.bmp", "parallax.bmp", "noparallax.bmp" } : new string[3] { "noparallax.bmp", "Assets.parallax.bmp", "parallax.bmp" });
					bool flag2 = false;
					string[] array2 = array;
					foreach (string resourceName in array2)
					{
						try
						{
							BitmapSource bitmapSource = LoadEmbeddedImage(resourceName);
							if (bitmapSource != null)
							{
								parallaxBitmap = bitmapSource;
								SliceParallax();
								flag2 = true;
								break;
							}
						}
						catch
						{
						}
					}
					if (!flag2 && parallaxBitmap == null)
					{
						BitmapImage bitmapImage = LoadEmbeddedImage("parallax Blue.bmp") ?? LoadEmbeddedImage("parallax.bmp");
						if (bitmapImage != null)
						{
							parallaxBitmap = bitmapImage;
							SliceParallax();
						}
					}
				}
				catch
				{
				}
				try
				{
					string[] array3 = new string[3] { "Assets.ground.bmp", "native_ground.bmp", "ground.bmp" };
					bool flag3 = false;
					string[] array4 = array3;
					foreach (string resourceName2 in array4)
					{
						try
						{
							BitmapImage bitmapImage2 = LoadEmbeddedImage(resourceName2);
							if (bitmapImage2 != null)
							{
								groundBitmap = bitmapImage2;
								SliceGround();
								flag3 = true;
								break;
							}
						}
						catch
						{
						}
					}
					if (!flag3 && groundBitmap == null)
					{
						BitmapImage bitmapImage3 = LoadEmbeddedImage("native_ground.bmp") ?? LoadEmbeddedImage("ground.bmp");
						if (bitmapImage3 != null)
						{
							groundBitmap = bitmapImage3;
							SliceGround();
						}
					}
				}
				catch
				{
				}
				bool hasParallaxLayer = true;
				bool hasGroundLayer = true;
				try
				{
					loadedParallaxX = 0.9;
					loadedParallaxY = 0.9;
					loadedParallaxRepeatX = true;
					loadedParallaxRepeatY = true;
				}
				catch
				{
				}
				try
				{
					ImageSource imageSource = null;
					imageSource = ((!noParallaxBg) ? (LoadEmbeddedImage("Assets.parallax.bmp") ?? LoadEmbeddedImage("parallax Blue.bmp") ?? LoadEmbeddedImage("parallax.bmp")) : (LoadEmbeddedImage("noparallax.bmp") ?? LoadEmbeddedImage("Assets.parallax.bmp") ?? LoadEmbeddedImage("parallax.bmp")));
					if (imageSource != null)
					{
						parallaxBitmap = imageSource as BitmapSource;
						try
						{
							SliceParallax();
						}
						catch
						{
						}
						loadedHasParallaxLayer = true;
						loadedParallaxX = 0.9;
						loadedParallaxY = 0.9;
						loadedParallaxRepeatX = true;
						loadedParallaxRepeatY = true;
					}
					BitmapImage bitmapImage4 = LoadEmbeddedImage("Assets.ground.bmp") ?? LoadEmbeddedImage("ground.bmp") ?? LoadEmbeddedImage("native_ground.bmp");
					if (bitmapImage4 != null)
					{
						groundBitmap = bitmapImage4;
						try
						{
							SliceGround();
						}
						catch
						{
						}
						loadedHasGroundLayer = true;
						loadedGroundRepeatX = true;
					}
				}
				catch
				{
				}
				try
				{
					if (parallaxImages == null || parallaxImages.Length == 0)
					{
						BitmapImage bitmapImage5 = LoadEmbeddedImage("Assets.parallax.bmp") ?? LoadEmbeddedImage("parallax Blue.bmp") ?? LoadEmbeddedImage("parallax.bmp") ?? LoadEmbeddedImage("noparallax.bmp");
						if (bitmapImage5 != null)
						{
							parallaxBitmap = bitmapImage5;
							SliceParallax();
						}
					}
				}
				catch
				{
				}
				try
				{
					if (parallaxImages == null || parallaxImages.Length == 0)
					{
						WriteableBitmap writeableBitmap2 = new WriteableBitmap(16, 16, 96.0, 96.0, PixelFormats.Pbgra32, null);
						writeableBitmap2.Lock();
						try
						{
							int num = 1024;
							byte[] source = new byte[num];
							Marshal.Copy(source, 0, writeableBitmap2.BackBuffer, num);
							writeableBitmap2.AddDirtyRect(new Int32Rect(0, 0, 16, 16));
						}
						finally
						{
							writeableBitmap2.Unlock();
						}
						writeableBitmap2.Freeze();
						parallaxBitmap = writeableBitmap2;
						SliceParallax();
					}
				}
				catch
				{
				}
				ImageSource[] array5 = parallaxImages;
				ImageSource[] array6 = parallaxTonedImages;
				ImageSource imageSource2 = parallaxBitmap;
				if ((array5 == null || array5.Length == 0) && imageSource2 != null)
				{
					try
					{
						SliceParallax();
						array5 = parallaxImages;
						imageSource2 = parallaxBitmap;
					}
					catch
					{
					}
				}
				if (array5 == null || array5.Length == 0)
				{
					WriteableBitmap writeableBitmap3 = new WriteableBitmap(16, 16, 96.0, 96.0, PixelFormats.Pbgra32, null);
					try
					{
						writeableBitmap3.Lock();
						writeableBitmap3.AddDirtyRect(new Int32Rect(0, 0, 16, 16));
					}
					finally
					{
						try
						{
							writeableBitmap3.Unlock();
						}
						catch
						{
						}
					}
					writeableBitmap3.Freeze();
					imageSource2 = writeableBitmap3;
					array5 = new ImageSource[1] { writeableBitmap3 };
					array6 = null;
				}
				int[] array7 = (tiles ?? Array.Empty<int>()).Select((int t) => (t >= 0) ? t : 0).ToArray();
				int[] array8 = (sprites ?? Array.Empty<int>()).ToArray();
				SimulatorWindow sim = new SimulatorWindow(array7, array8, mapWidth, mapHeight, tileImages, tileTonedImages, spriteImages, spritePixelOffsets, spriteAnchors, backgroundTint, groundTint, tileTint, playerTint, playerTintEnabled, gridRenderShiftYPx, forcePreviewMode: true, hideColorTriggers: true, dictionary, dictionary2, sawFrame1TilesTinted, sawFrame2TilesTinted, smallSawFrame1TilesTinted, smallSawFrame2TilesTinted, largeSawFrame1TilesTinted, largeSawFrame2TilesTinted, imageSource2, array5, array6, loadedParallaxX, loadedParallaxY, loadedParallaxRepeatX, loadedParallaxRepeatY, hasParallaxLayer, groundImages, groundTonedImages, loadedGroundOffsetY, loadedGroundRepeatX, hasGroundLayer, groundTileRows, loadedStartingBackgroundColor, loadedStartingGroundColor, loadedSimulatorScale, loadedMaxFallSpeed, loadedStartingGameMode.HasValue ? loadedStartingGameMode.Value : 0);
				try
				{
					if (famiIntegration != null)
					{
						famiIntegration.SetPlaybackRate(1.0);
					}
				}
				catch
				{
				}
				try
				{
					sim.ShowSpriteHitboxes = MenuOptionShowSpriteHitboxes.IsChecked;
				}
				catch
				{
				}
				try
				{
					sim.LevelName = currentFilePath ?? "";
				}
				catch
				{
				}
				sim.Owner = this;
				try
				{
					sim.SetStartingSpeedUiIndex(loadedStartingSpeedUiIndex);
				}
				catch
				{
				}
				try
				{
					sim.SetSpawnScrollConfig(loadedSpawnYPositionHi, loadedSpawnYPositionLow, loadedScrollYPositionHi, loadedScrollYPositionLow);
				}
				catch
				{
				}
				try
				{
					sim.ApplyStartPosMarker();
				}
				catch
				{
				}
				try
				{
					sim.ApplySpawnScrollIfNoStartPos();
				}
				catch
				{
				}
				try
				{
					sim.EnsureInitialRender();
				}
				catch
				{
				}
				try
				{
					sim.Closed += delegate
					{
						try
						{
							Task.Run(delegate
							{
								try
								{
									sim.StopSimulation();
								}
								catch
								{
								}
								try
								{
									famiIntegration?.Stop();
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
							base.Dispatcher.BeginInvoke((Action)delegate
							{
								try
								{
									Activate();
								}
								catch
								{
								}
							});
						}
						catch
						{
						}
					};
				}
				catch
				{
				}
				try
				{
					if (!string.IsNullOrEmpty(albumTxtPath) && famiIntegration != null)
					{
						famiIntegration.WarmAndPrime(albumTxtPath);
					}
				}
				catch
				{
				}
				sim.Show();
				try
				{
					sim.StartSimulation();
				}
				catch
				{
				}
				try
				{
					if (!loadedOpenSimulatorPaused)
					{
						sim.StartRunningAsync();
					}
				}
				catch
				{
				}
			}
			finally
			{
				try
				{
					if (!flag)
					{
						SetShowAccurateTileset(enabled: false, loadedBlockSet, loadedSpikeSet, persistSetting: false);
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

	public void StartSimulatorPlayback()
	{
		try
		{
			if (famiIntegration == null || famiIntegration.IsPlaying)
			{
				return;
			}
			int playIdx = -1;
			if (FamiTrackCombo?.SelectedItem is ComboBoxItem { Tag: var tag } && tag is int num)
			{
				playIdx = num;
			}
			else
			{
				System.Windows.Controls.ComboBox famiTrackCombo = FamiTrackCombo;
				if (famiTrackCombo != null && famiTrackCombo.SelectedIndex >= 0)
				{
					playIdx = FamiTrackCombo.SelectedIndex;
				}
			}
			if (string.IsNullOrEmpty(albumTxtPath) || playIdx < 0)
			{
				return;
			}
			try
			{
				Task.Run(delegate
				{
					famiIntegration.PlayTrack(albumTxtPath, playIdx);
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

	public void PauseSimulatorPlayback()
	{
		try
		{
			if (famiIntegration == null)
			{
				return;
			}
			try
			{
				Task.Run(delegate
				{
					famiIntegration.Pause();
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

	public bool IsMusicPaused()
	{
		try
		{
			return famiIntegration != null && famiIntegration.IsPaused;
		}
		catch
		{
			return false;
		}
	}

	public bool IsMusicPlaying()
	{
		try
		{
			return famiIntegration != null && famiIntegration.IsPlaying;
		}
		catch
		{
			return false;
		}
	}

	public async Task ForceStartSimulatorPlaybackAsync()
	{
		try
		{
			if (famiIntegration == null)
			{
				return;
			}
			int playIdx = -1;
			object obj = FamiTrackCombo?.SelectedItem;
			int t = default(int);
			int num;
			if (obj is ComboBoxItem { Tag: var tag })
			{
				if (tag is int)
				{
					t = (int)tag;
					num = 1;
				}
				else
				{
					num = 0;
				}
			}
			else
			{
				num = 0;
			}
			if (num != 0)
			{
				playIdx = t;
			}
			else
			{
				System.Windows.Controls.ComboBox famiTrackCombo = FamiTrackCombo;
				if (famiTrackCombo != null && famiTrackCombo.SelectedIndex >= 0)
				{
					playIdx = FamiTrackCombo.SelectedIndex;
				}
			}
			if (!string.IsNullOrEmpty(albumTxtPath) && playIdx >= 0)
			{
				Task.Run(delegate
				{
					famiIntegration.PlayTrack(albumTxtPath, playIdx);
				});
				Stopwatch sw = Stopwatch.StartNew();
				while (!famiIntegration.IsPlaying && sw.ElapsedMilliseconds < 1500)
				{
					await Task.Delay(8).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch
		{
		}
	}

	public async Task ForceStartAndSeekSimulatorPlaybackAsync(double seekSeconds)
	{
		try
		{
			if (famiIntegration == null)
			{
				return;
			}
			int playIdx = -1;
			object obj = FamiTrackCombo?.SelectedItem;
			int t = default(int);
			int num;
			if (obj is ComboBoxItem { Tag: var tag })
			{
				if (tag is int)
				{
					t = (int)tag;
					num = 1;
				}
				else
				{
					num = 0;
				}
			}
			else
			{
				num = 0;
			}
			if (num != 0)
			{
				playIdx = t;
			}
			else
			{
				System.Windows.Controls.ComboBox famiTrackCombo = FamiTrackCombo;
				if (famiTrackCombo != null && famiTrackCombo.SelectedIndex >= 0)
				{
					playIdx = FamiTrackCombo.SelectedIndex;
				}
			}
			if (!string.IsNullOrEmpty(albumTxtPath) && playIdx >= 0)
			{
				Task.Run(delegate
				{
					famiIntegration.PlayTrack(albumTxtPath, playIdx);
				});
				await Task.Delay(8).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					famiIntegration.Pause();
				}
				catch
				{
				}
				Stopwatch sw = Stopwatch.StartNew();
				while (famiIntegration.IsPlaying && !famiIntegration.IsPaused && sw.ElapsedMilliseconds < 200)
				{
					await Task.Delay(8).ConfigureAwait(continueOnCapturedContext: false);
				}
				try
				{
					famiIntegration.SeekToPosition(seekSeconds);
				}
				catch
				{
				}
				await Task.Delay(8).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					famiIntegration.Resume();
				}
				catch
				{
				}
				sw.Restart();
				while (!famiIntegration.IsPlaying && sw.ElapsedMilliseconds < 1500)
				{
					await Task.Delay(8).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch
		{
		}
	}

	public void StopSimulatorPlayback()
	{
		try
		{
			if (famiIntegration == null)
			{
				return;
			}
			try
			{
				famiIntegration.Stop();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	public async Task StopSimulatorPlaybackAsync()
	{
		try
		{
			if (famiIntegration == null)
			{
				return;
			}
			try
			{
				famiIntegration.Stop();
			}
			catch
			{
			}
			await Task.Delay(150).ConfigureAwait(continueOnCapturedContext: false);
			Stopwatch sw = Stopwatch.StartNew();
			while ((famiIntegration.IsPlaying || famiIntegration.IsPaused) && sw.ElapsedMilliseconds < 350)
			{
				try
				{
					famiIntegration.Stop();
				}
				catch
				{
				}
				await Task.Delay(50).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch
		{
		}
	}

	public void SeekSimulatorPlayback(double seconds)
	{
		try
		{
			if (famiIntegration == null)
			{
				return;
			}
			try
			{
				Task.Run(delegate
				{
					famiIntegration.SeekToPosition(seconds);
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

	public void SeekSimulatorPlaybackSync(double seconds)
	{
		try
		{
			if (famiIntegration != null)
			{
				try
				{
					famiIntegration.SeekToPosition(seconds);
				}
				catch
				{
				}
				Thread.Sleep(150);
			}
		}
		catch
		{
		}
	}

	public async Task ResumeSimulatorPlaybackAsync()
	{
		try
		{
			if (famiIntegration == null || famiIntegration.IsPlaying || !famiIntegration.IsPaused)
			{
				return;
			}
			try
			{
				Task.Run(delegate
				{
					famiIntegration.Resume();
				});
			}
			catch
			{
			}
			Stopwatch sw = Stopwatch.StartNew();
			while (!famiIntegration.IsPlaying && sw.ElapsedMilliseconds < 1500)
			{
				await Task.Delay(8).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch
		{
		}
	}

	public void SetSimulatorPlaybackRate(double rate)
	{
		try
		{
			if (famiIntegration == null)
			{
				return;
			}
			try
			{
				famiIntegration.SetPlaybackRate(rate);
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	public async Task StartSimulatorPlaybackAsync()
	{
		try
		{
			if (famiIntegration == null || famiIntegration.IsPlaying || famiIntegration.IsPaused)
			{
				return;
			}
			int playIdx = -1;
			object obj = FamiTrackCombo?.SelectedItem;
			int t = default(int);
			int num;
			if (obj is ComboBoxItem { Tag: var tag })
			{
				if (tag is int)
				{
					t = (int)tag;
					num = 1;
				}
				else
				{
					num = 0;
				}
			}
			else
			{
				num = 0;
			}
			if (num != 0)
			{
				playIdx = t;
			}
			else
			{
				System.Windows.Controls.ComboBox famiTrackCombo = FamiTrackCombo;
				if (famiTrackCombo != null && famiTrackCombo.SelectedIndex >= 0)
				{
					playIdx = FamiTrackCombo.SelectedIndex;
				}
			}
			if (!string.IsNullOrEmpty(albumTxtPath) && playIdx >= 0)
			{
				Task.Run(delegate
				{
					famiIntegration.PlayTrack(albumTxtPath, playIdx);
				});
				Stopwatch sw = Stopwatch.StartNew();
				while (!famiIntegration.IsPlaying && sw.ElapsedMilliseconds < 1500)
				{
					await Task.Delay(8).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch
		{
		}
	}

	private void EnsureNewTabButton()
	{
		bool flag = false;
		foreach (TabItem item in (IEnumerable)FileTabControl.Items)
		{
			if (item.Tag?.ToString() == "NEW")
			{
				flag = true;
				break;
			}
		}
		bool flag2 = openFiles.Exists((FileTabData f) => string.IsNullOrEmpty(f.FilePath));
		if (!flag)
		{
			TabItem newItem = new TabItem
			{
				Header = "+",
				Tag = "NEW",
				Width = 30.0
			};
			FileTabControl.Items.Add(newItem);
		}
	}

	private async void CloseTab_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if (!(sender is System.Windows.Controls.Button { Tag: var tag } button))
		{
			return;
		}
		if (tag is FileTabData td)
		{
			int idx = openFiles.IndexOf(td);
			if (idx >= 0)
			{
				await CloseTabAtIndex(idx);
			}
			return;
		}
		object tag2 = button.Tag;
		int index = default(int);
		int num;
		if (tag2 is int)
		{
			index = (int)tag2;
			num = 1;
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			await CloseTabAtIndex(index);
		}
	}

	private async Task CloseTabAtIndex(int index)
	{
		if (index < 0 || index >= openFiles.Count)
		{
			return;
		}
		try
		{
			famiIntegration.Stop();
		}
		catch
		{
		}
		FileTabData tabData = openFiles[index];
		if (tabData.HasUnsavedChanges)
		{
			string fileName = ((tabData.FilePath != null) ? System.IO.Path.GetFileName(tabData.FilePath) : "Untitled");
			switch (System.Windows.MessageBox.Show("Save changes to " + fileName + "?", "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
			{
			case MessageBoxResult.Cancel:
				return;
			case MessageBoxResult.Yes:
				await SwitchToTab(index);
				SaveButton_Click(this, new RoutedEventArgs());
				if (hasUnsavedChanges)
				{
					return;
				}
				break;
			}
		}
		openFiles.RemoveAt(index);
		for (int i = 0; i < FileTabControl.Items.Count; i++)
		{
			object obj2 = FileTabControl.Items[i];
			if (obj2 is TabItem tab && tab.Tag == tabData)
			{
				try
				{
					isHandlingNewTab = true;
				}
				catch
				{
				}
				try
				{
					FileTabControl.Items.RemoveAt(i);
				}
				catch
				{
				}
				try
				{
					isHandlingNewTab = false;
				}
				catch
				{
				}
				break;
			}
		}
		if (currentFileIndex == index)
		{
			if (openFiles.Count > 0)
			{
				int newIndex = Math.Min(index, openFiles.Count - 1);
				currentFileIndex = -1;
				await SwitchToTab(newIndex);
				try
				{
					await base.Dispatcher.InvokeAsync(delegate
					{
						try
						{
							Activate();
							Focus();
						}
						catch
						{
						}
					}, DispatcherPriority.Background);
					return;
				}
				catch
				{
					return;
				}
			}
			NewMenuItem_Click(this, new RoutedEventArgs());
			try
			{
				await base.Dispatcher.InvokeAsync(delegate
				{
					try
					{
						Activate();
						Focus();
					}
					catch
					{
					}
				}, DispatcherPriority.Background);
				return;
			}
			catch
			{
				return;
			}
		}
		if (currentFileIndex > index)
		{
			currentFileIndex--;
		}
	}

	private async void MenuFileClose_Click(object sender, RoutedEventArgs e)
	{
		if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
		{
			await CloseTabAtIndex(currentFileIndex);
		}
	}

	private async Task SwitchToTab(int index)
	{
		if (isSwitchingTab)
		{
			return;
		}
		isSwitchingTab = true;
		if (index < 0 || index >= openFiles.Count)
		{
			return;
		}
		LoadingWindow loadingWindow = null;
		try
		{
			loadingWindow = new LoadingWindow
			{
				Owner = this
			};
			loadingWindow.SetMessage("Tab rendering, please wait...");
			loadingWindow.Show();
			await base.Dispatcher.InvokeAsync(delegate
			{
			}, DispatcherPriority.Render);
		}
		catch
		{
		}
		try
		{
			if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
			{
				SaveCurrentTabState();
			}
			currentFileIndex = index;
			FileTabData tabData = openFiles[index];
			if (tabData.Tiles != null && tabData.Tiles.Length != 0)
			{
				tiles = new int[tabData.Tiles.Length];
				Array.Copy(tabData.Tiles, tiles, tabData.Tiles.Length);
			}
			else
			{
				tiles = Array.Empty<int>();
			}
			if (tabData.Sprites != null && tabData.Sprites.Length != 0)
			{
				sprites = new int[tabData.Sprites.Length];
				Array.Copy(tabData.Sprites, sprites, tabData.Sprites.Length);
			}
			else
			{
				sprites = Array.Empty<int>();
			}
			spritePixelOffsets = new Dictionary<int, (int, int)>(tabData.SpritePixelOffsets);
			mapWidth = tabData.MapWidth;
			mapHeight = tabData.MapHeight;
			hasUnsavedChanges = tabData.HasUnsavedChanges;
			currentFilePath = tabData.FilePath;
			RefreshReplayButtonVisibility();
			if (WidthBox != null)
			{
				WidthBox.Text = mapWidth.ToString();
			}
			if (HeightBox != null)
			{
				HeightBox.Text = mapHeight.ToString();
			}
			if (!string.IsNullOrEmpty(tabData.FilePath) && File.Exists(tabData.FilePath))
			{
				try
				{
					LoadTmxConfig(tabData.FilePath);
					if (showAccurateTileset)
					{
						try
						{
							SetShowAccurateTileset(enabled: true);
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
				loadedTilesetSource = tabData.LoadedTilesetSource;
				loadedSpritesetSource = tabData.LoadedSpritesetSource;
				loadedHasEditorSettings = tabData.LoadedHasEditorSettings;
				loadedChunkWidth = tabData.LoadedChunkWidth;
				loadedChunkHeight = tabData.LoadedChunkHeight;
				loadedExportTarget = tabData.LoadedExportTarget;
				loadedExportFormat = tabData.LoadedExportFormat;
				loadedParallaxSource = tabData.LoadedParallaxSource;
				loadedParallaxX = tabData.LoadedParallaxX;
				loadedParallaxY = tabData.LoadedParallaxY;
				loadedParallaxRepeatX = tabData.LoadedParallaxRepeatX;
				loadedParallaxRepeatY = tabData.LoadedParallaxRepeatY;
				loadedHasParallaxLayer = tabData.LoadedHasParallaxLayer;
				loadedGroundSource = tabData.LoadedGroundSource;
				loadedGroundOffsetY = tabData.LoadedGroundOffsetY;
				loadedGroundRepeatX = tabData.LoadedGroundRepeatX;
				loadedHasGroundLayer = tabData.LoadedHasGroundLayer;
				loadedDecoSet = tabData.LoadedDecoSet;
				loadedBlockSet = tabData.LoadedBlockSet;
				loadedSpikeSet = tabData.LoadedSpikeSet;
				try
				{
					loadedMaxFallSpeed = tabData.LoadedMaxFallSpeed;
				}
				catch
				{
					loadedMaxFallSpeed = 6;
				}
				try
				{
					loadedStartingSpeedUiIndex = tabData.LoadedStartingSpeedUiIndex;
				}
				catch
				{
					loadedStartingSpeedUiIndex = 1;
				}
				try
				{
					loadedStartingBackgroundColor = tabData.LoadedStartingBackgroundColor;
				}
				catch
				{
					loadedStartingBackgroundColor = null;
				}
				try
				{
					loadedStartingGameMode = tabData.LoadedStartingGameMode;
				}
				catch
				{
					loadedStartingGameMode = null;
				}
				try
				{
					loadedStartingGroundColor = tabData.LoadedStartingGroundColor;
				}
				catch
				{
					loadedStartingGroundColor = null;
				}
				try
				{
					loadedSpawnYPositionHi = tabData.LoadedSpawnYPositionHi;
				}
				catch
				{
					loadedSpawnYPositionHi = null;
				}
				try
				{
					loadedSpawnYPositionLow = tabData.LoadedSpawnYPositionLow;
				}
				catch
				{
					loadedSpawnYPositionLow = null;
				}
				try
				{
					loadedScrollYPositionHi = tabData.LoadedScrollYPositionHi;
				}
				catch
				{
					loadedScrollYPositionHi = null;
				}
				try
				{
					loadedScrollYPositionLow = tabData.LoadedScrollYPositionLow;
				}
				catch
				{
					loadedScrollYPositionLow = null;
				}
				try
				{
					loadedForcePlatformer = tabData.LoadedForcePlatformer;
				}
				catch
				{
					loadedForcePlatformer = null;
				}
				noParallaxBg = tabData.NoParallaxBg;
				backgroundTint = tabData.BackgroundTint;
				groundTint = tabData.GroundTint;
				tileTint = tabData.TileTint;
				try
				{
					if (!string.IsNullOrEmpty(tabData.SelectedSong) && FamiTrackCombo != null)
					{
						for (int i = 0; i < FamiTrackCombo.Items.Count; i++)
						{
							object obj14 = FamiTrackCombo.Items[i];
							if (obj14 is ComboBoxItem { Content: var content } && content?.ToString() == tabData.SelectedSong)
							{
								FamiTrackCombo.SelectedIndex = i;
								break;
							}
						}
					}
				}
				catch
				{
				}
				UpdateParallaxTint();
				UpdateGroundTint();
				UpdateTileTint();
			}
			if (lockSpritesToSet && MenuOptionLockSprites != null)
			{
				MenuOptionLockSprites.IsChecked = lockSpritesToSet;
			}
			backgroundDirty = true;
			try
			{
				scaledTileCaches.Clear();
			}
			catch
			{
			}
			try
			{
				RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
			}
			catch
			{
			}
			Redraw();
			SetStartPosMarker(tabData.StartPosX, tabData.StartPosY);
		}
		finally
		{
			try
			{
				loadingWindow?.Close();
			}
			catch
			{
			}
			isSwitchingTab = false;
		}
		try
		{
			if (currentFileIndex < 0 || currentFileIndex >= openFiles.Count)
			{
				return;
			}
			for (int i2 = 0; i2 < FileTabControl.Items.Count; i2++)
			{
				object obj14 = FileTabControl.Items[i2];
				TabItem ti = obj14 as TabItem;
				int num;
				if (ti != null)
				{
					obj14 = ti.Tag;
					if (obj14 is FileTabData td)
					{
						num = ((openFiles.IndexOf(td) == currentFileIndex) ? 1 : 0);
						goto IL_0b1b;
					}
				}
				num = 0;
				goto IL_0b1b;
				IL_0b1b:
				if (num != 0)
				{
					try
					{
						isHandlingNewTab = true;
						lastProgrammaticSelectedTab = ti;
						FileTabControl.SelectedItem = ti;
						try
						{
							await base.Dispatcher.InvokeAsync(delegate
							{
								lastProgrammaticSelectedTab = null;
							}, DispatcherPriority.Background);
						}
						catch
						{
						}
						try
						{
							await base.Dispatcher.InvokeAsync(delegate
							{
								try
								{
									Activate();
									Focus();
								}
								catch
								{
								}
							}, DispatcherPriority.Background);
							break;
						}
						catch
						{
							break;
						}
					}
					finally
					{
						isHandlingNewTab = false;
					}
				}
			}
		}
		catch
		{
		}
	}

	private void SaveCurrentTabState()
	{
		if (currentFileIndex < 0 || currentFileIndex >= openFiles.Count)
		{
			return;
		}
		FileTabData fileTabData = openFiles[currentFileIndex];
		if (tiles != null)
		{
			fileTabData.Tiles = new int[tiles.Length];
			Array.Copy(tiles, fileTabData.Tiles, tiles.Length);
		}
		else
		{
			fileTabData.Tiles = Array.Empty<int>();
		}
		if (sprites != null)
		{
			fileTabData.Sprites = new int[sprites.Length];
			Array.Copy(sprites, fileTabData.Sprites, sprites.Length);
		}
		else
		{
			fileTabData.Sprites = Array.Empty<int>();
		}
		fileTabData.SpritePixelOffsets = new Dictionary<int, (int, int)>(spritePixelOffsets);
		fileTabData.MapWidth = mapWidth;
		fileTabData.MapHeight = mapHeight;
		fileTabData.HasUnsavedChanges = hasUnsavedChanges;
		fileTabData.LoadedTilesetSource = loadedTilesetSource;
		fileTabData.LoadedSpritesetSource = loadedSpritesetSource;
		fileTabData.LoadedHasEditorSettings = loadedHasEditorSettings;
		fileTabData.LoadedChunkWidth = loadedChunkWidth;
		fileTabData.LoadedChunkHeight = loadedChunkHeight;
		fileTabData.LoadedExportTarget = loadedExportTarget;
		fileTabData.LoadedExportFormat = loadedExportFormat;
		fileTabData.LoadedParallaxSource = loadedParallaxSource;
		fileTabData.LoadedParallaxX = loadedParallaxX;
		fileTabData.LoadedParallaxY = loadedParallaxY;
		fileTabData.LoadedParallaxRepeatX = loadedParallaxRepeatX;
		fileTabData.LoadedParallaxRepeatY = loadedParallaxRepeatY;
		fileTabData.LoadedHasParallaxLayer = loadedHasParallaxLayer;
		fileTabData.LoadedGroundSource = loadedGroundSource;
		fileTabData.LoadedGroundOffsetY = loadedGroundOffsetY;
		fileTabData.LoadedGroundRepeatX = loadedGroundRepeatX;
		fileTabData.LoadedHasGroundLayer = loadedHasGroundLayer;
		fileTabData.LoadedDecoSet = loadedDecoSet;
		fileTabData.LoadedBlockSet = loadedBlockSet;
		fileTabData.LoadedSpikeSet = loadedSpikeSet;
		try
		{
			fileTabData.LoadedSpawnYPositionHi = loadedSpawnYPositionHi;
		}
		catch
		{
			fileTabData.LoadedSpawnYPositionHi = null;
		}
		try
		{
			fileTabData.LoadedSpawnYPositionLow = loadedSpawnYPositionLow;
		}
		catch
		{
			fileTabData.LoadedSpawnYPositionLow = null;
		}
		try
		{
			fileTabData.LoadedScrollYPositionHi = loadedScrollYPositionHi;
		}
		catch
		{
			fileTabData.LoadedScrollYPositionHi = null;
		}
		try
		{
			fileTabData.LoadedScrollYPositionLow = loadedScrollYPositionLow;
		}
		catch
		{
			fileTabData.LoadedScrollYPositionLow = null;
		}
		try
		{
			fileTabData.LoadedForcePlatformer = loadedForcePlatformer;
		}
		catch
		{
			fileTabData.LoadedForcePlatformer = null;
		}
		fileTabData.LoadedMaxFallSpeed = loadedMaxFallSpeed;
		fileTabData.LoadedStartingSpeedUiIndex = loadedStartingSpeedUiIndex;
		fileTabData.LoadedStartingGameMode = loadedStartingGameMode;
		fileTabData.LoadedStartingBackgroundColor = loadedStartingBackgroundColor;
		fileTabData.LoadedStartingGroundColor = loadedStartingGroundColor;
		try
		{
			if (!fileTabData.CreatedAsUntitled || !string.IsNullOrEmpty(fileTabData.FilePath))
			{
				fileTabData.LoadedStartingLowerText = loadedStartingLowerText;
				fileTabData.LoadedStartingUpperText = loadedStartingUpperText;
			}
		}
		catch
		{
			fileTabData.LoadedStartingLowerText = loadedStartingLowerText;
			fileTabData.LoadedStartingUpperText = loadedStartingUpperText;
		}
		fileTabData.LoadedStartingDifficulty = loadedStartingDifficulty;
		fileTabData.LoadedStartingStars = loadedStartingStars;
		fileTabData.NoParallaxBg = noParallaxBg;
		fileTabData.BackgroundTint = backgroundTint;
		fileTabData.GroundTint = groundTint;
		fileTabData.TileTint = tileTint;
		fileTabData.StartPosX = startPosMarkerX;
		fileTabData.StartPosY = startPosMarkerY;
		try
		{
			if (FamiTrackCombo?.SelectedItem is ComboBoxItem { Content: not null } comboBoxItem)
			{
				fileTabData.SelectedSong = comboBoxItem.Content.ToString();
			}
		}
		catch
		{
		}
	}

	private void UpdateTabHeaderForIndex(int index)
	{
		if (index < 0 || index >= openFiles.Count || FileTabControl == null)
		{
			return;
		}
		FileTabData fileTabData = openFiles[index];
		for (int i = 0; i < FileTabControl.Items.Count; i++)
		{
			if (!(FileTabControl.Items[i] is TabItem tabItem) || tabItem.Tag != fileTabData)
			{
				continue;
			}
			try
			{
				if (tabItem.Header is StackPanel stackPanel && stackPanel.Children.Count > 0 && stackPanel.Children[0] is TextBlock textBlock)
				{
					string text = ((fileTabData.FilePath != null) ? System.IO.Path.GetFileName(fileTabData.FilePath) : "Untitled");
					textBlock.Text = (fileTabData.HasUnsavedChanges ? (text + " *") : text);
				}
				break;
			}
			catch
			{
				break;
			}
		}
	}

	private void SetHasUnsavedChanges(bool unsaved)
	{
		hasUnsavedChanges = unsaved;
		if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
		{
			openFiles[currentFileIndex].HasUnsavedChanges = unsaved;
			try
			{
				UpdateTabHeaderForIndex(currentFileIndex);
			}
			catch
			{
			}
		}
	}

	private async void FileTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (isHandlingNewTab || isSwitchingTab)
		{
			return;
		}
		object selectedItem;
		try
		{
			if (lastProgrammaticSelectedTab != null && lastProgrammaticSelectedTab == FileTabControl.SelectedItem)
			{
				lastProgrammaticSelectedTab = null;
				selectedItem = FileTabControl.SelectedItem;
				if (selectedItem is TabItem t)
				{
					lastSelectedTab = t;
				}
				return;
			}
		}
		catch
		{
		}
		selectedItem = FileTabControl.SelectedItem;
		if (!(selectedItem is TabItem tab) || (lastSelectedTab != null && lastSelectedTab == tab))
		{
			return;
		}
		if (tab.Tag?.ToString() == "NEW" && FileTabControl.Items[FileTabControl.Items.Count - 1] == tab)
		{
			int existingUntitled = openFiles.FindIndex((FileTabData f) => string.IsNullOrEmpty(f.FilePath));
			if (existingUntitled >= 0)
			{
				for (int i = 0; i < FileTabControl.Items.Count; i++)
				{
					selectedItem = FileTabControl.Items[i];
					TabItem ti = selectedItem as TabItem;
					int num;
					if (ti != null)
					{
						selectedItem = ti.Tag;
						if (selectedItem is FileTabData td)
						{
							num = ((openFiles.IndexOf(td) == existingUntitled) ? 1 : 0);
							goto IL_0263;
						}
					}
					num = 0;
					goto IL_0263;
					IL_0263:
					if (num != 0)
					{
						try
						{
							isHandlingNewTab = true;
							lastProgrammaticSelectedTab = ti;
							FileTabControl.SelectedItem = ti;
							try
							{
								base.Dispatcher.BeginInvoke((Action)delegate
								{
									lastProgrammaticSelectedTab = null;
								}, DispatcherPriority.Background);
							}
							catch
							{
							}
						}
						finally
						{
							isHandlingNewTab = false;
						}
						try
						{
							await SwitchToTab(existingUntitled);
						}
						catch
						{
						}
						if (StatusText != null)
						{
							StatusText.Text = "Switched to existing Untitled tab.";
						}
						break;
					}
				}
			}
			else
			{
				isHandlingNewTab = true;
				try
				{
					NewMenuItem_Click(this, new RoutedEventArgs());
				}
				finally
				{
					isHandlingNewTab = false;
				}
			}
		}
		else
		{
			selectedItem = tab.Tag;
			if (selectedItem is FileTabData td2)
			{
				int index = openFiles.IndexOf(td2);
				if (index >= 0 && currentFileIndex != index && index < openFiles.Count)
				{
					try
					{
						await SwitchToTab(index);
					}
					catch
					{
					}
				}
			}
		}
		lastSelectedTab = tab;
	}

	private void AddToRecentFiles(string filePath)
	{
		recentFiles.Remove(filePath);
		recentFiles.Insert(0, filePath);
		if (recentFiles.Count > 10)
		{
			recentFiles.RemoveAt(10);
		}
		SaveRecentFiles();
		UpdateRecentFilesMenu();
	}

	private void UpdateRecentFilesMenu()
	{
		if (MenuFileRecent == null)
		{
			return;
		}
		MenuFileRecent.Items.Clear();
		if (recentFiles.Count == 0)
		{
			MenuItem newItem = new MenuItem
			{
				Header = "(No recent files)",
				IsEnabled = false
			};
			MenuFileRecent.Items.Add(newItem);
			return;
		}
		for (int i = 0; i < recentFiles.Count; i++)
		{
			string text = recentFiles[i];
			MenuItem menuItem = new MenuItem
			{
				Header = $"_{i + 1}  {System.IO.Path.GetFileName(text)}",
				Tag = text,
				ToolTip = text
			};
			menuItem.Click += RecentFile_Click;
			MenuFileRecent.Items.Add(menuItem);
		}
	}

	private void RecentFile_Click(object sender, RoutedEventArgs e)
	{
		if (sender is MenuItem { Tag: string tag })
		{
			if (File.Exists(tag))
			{
				LoadTMXFile(tag);
				return;
			}
			System.Windows.MessageBox.Show("File not found: " + tag, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
			recentFiles.Remove(tag);
			SaveRecentFiles();
			UpdateRecentFilesMenu();
		}
	}

	private void LoadTMXFile(string filePath)
	{
		try
		{
			if (playerPathPolyline != null && CanvasHost != null)
			{
				CanvasHost.Children.Remove(playerPathPolyline);
			}
			playerPathPolyline = null;
			if (playerDeathMarkerA != null && CanvasHost != null)
			{
				CanvasHost.Children.Remove(playerDeathMarkerA);
			}
			if (playerDeathMarkerB != null && CanvasHost != null)
			{
				CanvasHost.Children.Remove(playerDeathMarkerB);
			}
			playerDeathMarkerA = null;
			playerDeathMarkerB = null;
			if (startPosMarker != null && CanvasHost != null)
			{
				CanvasHost.Children.Remove(startPosMarker);
			}
			startPosMarker = null;
			startPosMarkerX = null;
			startPosMarkerY = null;
		}
		catch
		{
		}
		bool flag = false;
		try
		{
			if (hasUnsavedChanges && openFiles != null && openFiles.Count == 1)
			{
				FileTabData fileTabData = openFiles[0];
				if (fileTabData != null && string.IsNullOrEmpty(fileTabData.FilePath))
				{
					flag = true;
				}
			}
		}
		catch
		{
		}
		if (flag)
		{
			switch (System.Windows.MessageBox.Show("You have unsaved changes in the untitled tab. Do you want to save before loading?", "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
			{
			case MessageBoxResult.Yes:
				SaveButton_Click(this, new RoutedEventArgs());
				if (hasUnsavedChanges)
				{
					return;
				}
				try
				{
					if (currentFileIndex < 0 || currentFileIndex >= openFiles.Count)
					{
						break;
					}
					openFiles[currentFileIndex].FilePath = currentFilePath;
					openFiles[currentFileIndex].HasUnsavedChanges = false;
					for (int i = 0; i < FileTabControl.Items.Count; i++)
					{
						if (FileTabControl.Items[i] is TabItem { Tag: FileTabData tag } tabItem && openFiles.IndexOf(tag) == currentFileIndex)
						{
							StackPanel stackPanel = new StackPanel
							{
								Orientation = System.Windows.Controls.Orientation.Horizontal
							};
							TextBlock element = new TextBlock
							{
								Text = System.IO.Path.GetFileName(currentFilePath ?? ""),
								Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
								VerticalAlignment = VerticalAlignment.Center
							};
							System.Windows.Controls.Button button = new System.Windows.Controls.Button
							{
								Content = "\ufffd",
								Width = 16.0,
								Height = 16.0,
								Padding = new Thickness(0.0),
								Margin = new Thickness(0.0),
								VerticalAlignment = VerticalAlignment.Center,
								Background = Brushes.Transparent,
								BorderThickness = new Thickness(0.0),
								FontSize = 14.0,
								FontWeight = FontWeights.Bold,
								Cursor = System.Windows.Input.Cursors.Hand,
								Visibility = Visibility.Visible,
								Tag = openFiles[currentFileIndex]
							};
							button.Click += CloseTab_Click;
							stackPanel.Children.Add(element);
							stackPanel.Children.Add(button);
							tabItem.Header = stackPanel;
							break;
						}
					}
				}
				catch
				{
				}
				break;
			case MessageBoxResult.Cancel:
				return;
			}
		}
		LoadingWindow loadingWindow = null;
		try
		{
			string text = System.IO.Path.GetExtension(filePath).ToLower();
			int num = 0;
			int num2 = 0;
			int[] array = null;
			int[] array2 = null;
			if (text == ".tmx")
			{
				loadingWindow = new LoadingWindow
				{
					Owner = this
				};
				loadingWindow.SetMessage("Loading TMX file...\nThis may take a while on larger maps.");
				loadingWindow.Show();
				base.Dispatcher.Invoke(delegate
				{
				}, DispatcherPriority.Background);
				TmxLevel tmxLevel = TmxHandler.LoadTmx(filePath, useLegacyTriggerOffset);
				num = tmxLevel.Width;
				num2 = tmxLevel.Height;
				array = tmxLevel.Tiles?.ToArray();
				array2 = tmxLevel.Sprites?.ToArray();
				if (!suppressCollisionMessages && !string.IsNullOrEmpty(tmxLevel.LoadCollisionMessages))
				{
					System.Windows.MessageBox.Show(this, "Sprite collision adjustments during load:\n\n" + tmxLevel.LoadCollisionMessages, "Sprite Collision Resolution", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					Activate();
					Focus();
				}
				loadedTilesetSource = tmxLevel.TilesetSource;
				loadedSpritesetSource = tmxLevel.SpritesetSource;
				loadedHasEditorSettings = tmxLevel.HasEditorSettings;
				loadedChunkWidth = tmxLevel.ChunkWidth;
				loadedChunkHeight = tmxLevel.ChunkHeight;
				loadedExportTarget = tmxLevel.ExportTarget;
				loadedExportFormat = tmxLevel.ExportFormat;
				loadedParallaxSource = tmxLevel.ParallaxSource;
				originalParallaxSource = tmxLevel.ParallaxSource;
				loadedParallaxX = tmxLevel.ParallaxX;
				loadedParallaxY = tmxLevel.ParallaxY;
				loadedParallaxRepeatX = tmxLevel.ParallaxRepeatX;
				loadedParallaxRepeatY = tmxLevel.ParallaxRepeatY;
				loadedHasParallaxLayer = true;
				loadedGroundSource = tmxLevel.GroundSource;
				loadedGroundOffsetY = tmxLevel.GroundOffsetY;
				loadedGroundRepeatX = tmxLevel.GroundRepeatX;
				loadedHasGroundLayer = true;
				try
				{
					loadedDecoSet = (string.IsNullOrEmpty(tmxLevel.DecoSet) ? "deco1" : tmxLevel.DecoSet);
				}
				catch
				{
					loadedDecoSet = "deco1";
				}
			}
			if (num <= 0 || num2 <= 0 || array == null)
			{
				return;
			}
			if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
			{
				SaveCurrentTabState();
			}
			suppressUndoRecording = true;
			mapWidth = num;
			mapHeight = num2;
			if (array != null && array.Length != 0)
			{
				tiles = new int[array.Length];
				Array.Copy(array, tiles, array.Length);
			}
			else
			{
				tiles = Array.Empty<int>();
			}
			if (array2 != null && array2.Length != 0)
			{
				sprites = new int[array2.Length];
				Array.Copy(array2, sprites, array2.Length);
			}
			else
			{
				sprites = Enumerable.Repeat(-1, num * num2).ToArray();
			}
			if (WidthBox != null)
			{
				WidthBox.Text = mapWidth.ToString();
			}
			if (HeightBox != null)
			{
				HeightBox.Text = mapHeight.ToString();
			}
			ClearSelection();
			undoStack.Clear();
			redoStack.Clear();
			suppressUndoRecording = false;
			currentFilePath = filePath;
			SetHasUnsavedChanges(unsaved: false);
			AddToRecentFiles(filePath);
			try
			{
				LoadTmxConfig(filePath);
			}
			catch
			{
			}
			bool flag2 = false;
			if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
			{
				FileTabData fileTabData2 = openFiles[currentFileIndex];
				if (string.IsNullOrEmpty(fileTabData2.FilePath) && !fileTabData2.HasUnsavedChanges)
				{
					flag2 = true;
				}
			}
			if (flag2)
			{
				openFiles[currentFileIndex].FilePath = filePath;
				for (int num3 = 0; num3 < FileTabControl.Items.Count; num3++)
				{
					if (FileTabControl.Items[num3] is TabItem { Tag: FileTabData tag2 } tabItem2 && openFiles.IndexOf(tag2) == currentFileIndex)
					{
						StackPanel stackPanel2 = new StackPanel
						{
							Orientation = System.Windows.Controls.Orientation.Horizontal
						};
						TextBlock element2 = new TextBlock
						{
							Text = System.IO.Path.GetFileName(filePath),
							Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
							VerticalAlignment = VerticalAlignment.Center
						};
						System.Windows.Controls.Button button2 = new System.Windows.Controls.Button
						{
							Content = "\ufffd",
							Width = 16.0,
							Height = 16.0,
							Padding = new Thickness(0.0),
							Margin = new Thickness(0.0),
							VerticalAlignment = VerticalAlignment.Center,
							Background = Brushes.Transparent,
							BorderThickness = new Thickness(0.0),
							FontSize = 14.0,
							FontWeight = FontWeights.Bold,
							Cursor = System.Windows.Input.Cursors.Hand,
							Visibility = Visibility.Visible,
							Tag = openFiles[currentFileIndex]
						};
						button2.Click += CloseTab_Click;
						stackPanel2.Children.Add(element2);
						stackPanel2.Children.Add(button2);
						tabItem2.Header = stackPanel2;
						break;
					}
				}
			}
			else
			{
				CreateNewTab(filePath);
				try
				{
					SwitchToTab(currentFileIndex);
				}
				catch
				{
				}
			}
			Redraw();
			try
			{
				if (MapScrollViewer != null)
				{
					double num4 = ZoomSlider?.Value ?? 1.0;
					double num5 = mapViewportPadding;
					double num6 = (double)(mapHeight * 16) * num4;
					double num7 = 0.0;
					if (groundBitmap != null && groundImages != null && groundImages.Length != 0)
					{
						DpiScale dpi = VisualTreeHelper.GetDpi(this);
						num7 = (double)groundBitmap.PixelHeight / dpi.DpiScaleY * num4;
					}
					num6 += num7 - 32.0 * num4;
					double num8 = num6 + num5 * 2.0;
					double num9 = Math.Max(0.0, num8 - SafeViewportHeight());
					double offset = Math.Round(num9 * 2.0 / 3.0);
					MapScrollViewer?.ScrollToVerticalOffset(offset);
				}
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show("Error loading file: " + ex.Message, "Load Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			loadingWindow?.Close();
		}
	}

	private void SaveRecentFiles()
	{
		try
		{
			string baseDirectory = AppContext.BaseDirectory;
			string path = System.IO.Path.Combine(baseDirectory, "recent-files.json");
			string contents = JsonSerializer.Serialize(recentFiles);
			File.WriteAllText(path, contents);
		}
		catch
		{
		}
	}

	private void LoadRecentFiles()
	{
		try
		{
			string baseDirectory = AppContext.BaseDirectory;
			string path = System.IO.Path.Combine(baseDirectory, "recent-files.json");
			if (File.Exists(path))
			{
				string json = File.ReadAllText(path);
				List<string> list = JsonSerializer.Deserialize<List<string>>(json);
				if (list != null)
				{
					recentFiles = list.Where((string f) => File.Exists(f)).Take(10).ToList();
				}
			}
		}
		catch
		{
		}
		UpdateRecentFilesMenu();
	}

	private void SetTileboardPosition(string position)
	{
		string text = tileboardPosition;
		tileboardPosition = position;
		if ((position == "LEFT" || position == "RIGHT") && (text == "TOP" || text == "BOTTOM"))
		{
			manualTileSize = false;
			manualSpriteSize = false;
		}
		ApplyTileboardPosition();
		UpdateLeftColumnWidth();
		PopulateTilesPanel();
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (tileboardPosition == "LEFT" || tileboardPosition == "RIGHT")
			{
				Ensure16VisibleOnStartup();
			}
		}, DispatcherPriority.Loaded);
		SaveSettingsWithTriggerOption();
	}

	private void ApplyTileboardPosition()
	{
		if (RootGrid == null || RootGrid.ColumnDefinitions.Count < 3)
		{
			return;
		}
		if (isTileboardHidden)
		{
			if (TileboardPanel != null)
			{
				TileboardPanel.Visibility = Visibility.Collapsed;
			}
			GridSplitter gridSplitter = RootGrid.Children.OfType<GridSplitter>().FirstOrDefault();
			if (gridSplitter != null)
			{
				gridSplitter.Visibility = Visibility.Collapsed;
			}
			if (tileboardPosition == "LEFT")
			{
				RootGrid.ColumnDefinitions[0].Width = new GridLength(0.0);
				RootGrid.ColumnDefinitions[1].Width = new GridLength(0.0);
			}
			else if (tileboardPosition == "RIGHT")
			{
				RootGrid.ColumnDefinitions[1].Width = new GridLength(0.0);
				RootGrid.ColumnDefinitions[2].Width = new GridLength(0.0);
			}
			else if (RootGrid.RowDefinitions.Count >= 3)
			{
				if (tileboardPosition == "TOP")
				{
					RootGrid.RowDefinitions[0].Height = new GridLength(0.0);
					RootGrid.RowDefinitions[1].Height = new GridLength(0.0);
				}
				else
				{
					RootGrid.RowDefinitions[1].Height = new GridLength(0.0);
					RootGrid.RowDefinitions[2].Height = new GridLength(0.0);
				}
			}
		}
		else if (tileboardPosition == "TOP" || tileboardPosition == "BOTTOM")
		{
			if (RootGrid.RowDefinitions.Count == 0)
			{
				RootGrid.RowDefinitions.Clear();
				if (tileboardPosition == "TOP")
				{
					RootGrid.RowDefinitions.Add(new RowDefinition
					{
						Height = new GridLength(260.0, GridUnitType.Pixel)
					});
					RootGrid.RowDefinitions.Add(new RowDefinition
					{
						Height = new GridLength(5.0, GridUnitType.Pixel)
					});
					RootGrid.RowDefinitions.Add(new RowDefinition
					{
						Height = new GridLength(1.0, GridUnitType.Star)
					});
				}
				else
				{
					RootGrid.RowDefinitions.Add(new RowDefinition
					{
						Height = new GridLength(1.0, GridUnitType.Star)
					});
					RootGrid.RowDefinitions.Add(new RowDefinition
					{
						Height = new GridLength(5.0, GridUnitType.Pixel)
					});
					RootGrid.RowDefinitions.Add(new RowDefinition
					{
						Height = new GridLength(260.0, GridUnitType.Pixel)
					});
				}
			}
			Grid.SetColumn(TileboardPanel, 0);
			Grid.SetColumn(MainEditorPanel, 0);
			Grid.SetColumnSpan(TileboardPanel, 3);
			Grid.SetColumnSpan(MainEditorPanel, 3);
			if (tileboardPosition == "TOP")
			{
				Grid.SetRow(TileboardPanel, 0);
				Grid.SetRow(MainEditorPanel, 2);
			}
			else
			{
				Grid.SetRow(MainEditorPanel, 0);
				Grid.SetRow(TileboardPanel, 2);
			}
			if (RootGrid.Children.Count > 2)
			{
				GridSplitter gridSplitter2 = RootGrid.Children.OfType<GridSplitter>().FirstOrDefault();
				if (gridSplitter2 != null)
				{
					gridSplitter2.Visibility = Visibility.Collapsed;
				}
			}
		}
		else if (tileboardPosition == "RIGHT")
		{
			if (RootGrid.RowDefinitions.Count > 0)
			{
				RootGrid.RowDefinitions.Clear();
			}
			Grid.SetRowSpan(TileboardPanel, 1);
			Grid.SetRowSpan(MainEditorPanel, 1);
			Grid.SetRow(TileboardPanel, 0);
			Grid.SetRow(MainEditorPanel, 0);
			Grid.SetColumn(TileboardPanel, 2);
			Grid.SetColumn(MainEditorPanel, 0);
			Grid.SetColumnSpan(TileboardPanel, 1);
			Grid.SetColumnSpan(MainEditorPanel, 1);
			RootGrid.ColumnDefinitions[0].Width = new GridLength(1.0, GridUnitType.Star);
			RootGrid.ColumnDefinitions[2].Width = new GridLength(260.0, GridUnitType.Pixel);
			GridSplitter gridSplitter3 = RootGrid.Children.OfType<GridSplitter>().FirstOrDefault();
			if (gridSplitter3 != null)
			{
				gridSplitter3.Visibility = Visibility.Visible;
			}
		}
		else
		{
			if (RootGrid.RowDefinitions.Count > 0)
			{
				RootGrid.RowDefinitions.Clear();
			}
			Grid.SetRowSpan(TileboardPanel, 1);
			Grid.SetRowSpan(MainEditorPanel, 1);
			Grid.SetRow(TileboardPanel, 0);
			Grid.SetRow(MainEditorPanel, 0);
			Grid.SetColumn(TileboardPanel, 0);
			Grid.SetColumn(MainEditorPanel, 2);
			Grid.SetColumnSpan(TileboardPanel, 1);
			Grid.SetColumnSpan(MainEditorPanel, 1);
			RootGrid.ColumnDefinitions[0].Width = new GridLength(260.0, GridUnitType.Pixel);
			RootGrid.ColumnDefinitions[2].Width = new GridLength(1.0, GridUnitType.Star);
			GridSplitter gridSplitter4 = RootGrid.Children.OfType<GridSplitter>().FirstOrDefault();
			if (gridSplitter4 != null)
			{
				gridSplitter4.Visibility = Visibility.Visible;
			}
		}
	}

	private void UpdateLeftColumnWidth(bool initial = false)
	{
		try
		{
			if (RootGrid == null || isTileboardHidden)
			{
				return;
			}
			if (tileboardPosition == "TOP" || tileboardPosition == "BOTTOM")
			{
				int num = ((!(tileboardPosition == "TOP")) ? 2 : 0);
				if (RootGrid.RowDefinitions.Count > num)
				{
					RowDefinition rowDefinition = RootGrid.RowDefinitions[num];
					if (!manualTileSize)
					{
						double num2 = paletteTileSize;
						double verticalScrollBarWidth = SystemParameters.VerticalScrollBarWidth;
						double num3 = 12.0;
						double value = Math.Max(160.0, num2 * 8.0 + verticalScrollBarWidth + num3 + 100.0);
						rowDefinition.Height = new GridLength(value, GridUnitType.Pixel);
					}
				}
				return;
			}
			int index = ((tileboardPosition == "RIGHT") ? 2 : 0);
			ColumnDefinition columnDefinition = RootGrid.ColumnDefinitions[index];
			if (!manualTileSize)
			{
				double num4 = paletteTileSize;
				double verticalScrollBarWidth2 = SystemParameters.VerticalScrollBarWidth;
				double num5 = 12.0;
				double value2 = Math.Max(160.0, num4 * 16.0 + verticalScrollBarWidth2 + num5);
				columnDefinition.Width = new GridLength(value2, GridUnitType.Pixel);
			}
			if (initial)
			{
				double num6 = ((columnDefinition.ActualWidth > 0.0) ? columnDefinition.ActualWidth : 260.0);
				double num7 = ((RootGrid.ColumnDefinitions.Count > 1) ? RootGrid.ColumnDefinitions[1].ActualWidth : 5.0);
				double num8 = 200.0;
				double num9 = num6 + num7 + num8 + 40.0;
				if (base.MinWidth < num9)
				{
					base.MinWidth = num9;
				}
			}
		}
		catch
		{
		}
	}

	private void AdjustPaletteSizes()
	{
		try
		{
			if (!manualTileSize && TileSizeSlider != null)
			{
				paletteTileSize = Math.Max(8, (int)Math.Round(TileSizeSlider.Value));
			}
			PopulateTilesPanel();
			PopulateSpritesPanel();
			UpdateTilesPanelWidth();
		}
		catch
		{
		}
	}

	private void UpdateTilesPanelWidth()
	{
		try
		{
			if (RootGrid == null || isAdjustingPanels)
			{
				return;
			}
			double actualWidth = RootGrid.ColumnDefinitions[0].ActualWidth;
			if (actualWidth <= 0.0)
			{
				return;
			}
			double num = 0.0;
			double num2 = 0.0;
			ScrollViewer innerScrollViewer = GetInnerScrollViewer(TilesPanel);
			ScrollViewer innerScrollViewer2 = GetInnerScrollViewer(SpritesPanel);
			num = innerScrollViewer?.ViewportWidth ?? TilesPanel.ActualWidth;
			num2 = innerScrollViewer2?.ViewportWidth ?? SpritesPanel.ActualWidth;
			double num3 = Math.Max(0.0, actualWidth - 8.0);
			if (num <= 0.0)
			{
				num = num3;
			}
			if (num2 <= 0.0)
			{
				num2 = num3;
			}
			double num4 = Math.Min(num, num2);
			int num5 = Math.Max(8, (int)Math.Floor(num4 / 16.0));
			int num6 = num5;
			if (num5 > 32)
			{
				num5 = 32;
			}
			if (num6 > 32)
			{
				num6 = 32;
			}
			isAdjustingPanels = true;
			try
			{
				if (!manualTileSize && num5 != paletteTileSize)
				{
					paletteTileSize = num5;
					if (TileSizeSlider != null)
					{
						suppressManualTileChange = true;
						TileSizeSlider.Value = paletteTileSize;
						suppressManualTileChange = false;
					}
					PopulateTilesPanel();
				}
				if (num5 > 32)
				{
					num5 = 32;
				}
				if (!manualTileSize && num5 != paletteTileSize)
				{
					paletteTileSize = num5;
					if (TileSizeSlider != null)
					{
						suppressManualTileChange = true;
						TileSizeSlider.Value = paletteTileSize;
						suppressManualTileChange = false;
					}
					PopulateTilesPanel();
				}
				double width = (double)paletteTileSize * 16.0;
				if (TilesPanel != null)
				{
					if (!manualTileSize)
					{
						TilesPanel.Width = width;
						TilesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
					}
					else
					{
						TilesPanel.Width = double.NaN;
						TilesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
					}
				}
				if (SpritesPanel != null)
				{
					if (!manualSpriteSize)
					{
						SpritesPanel.Width = width;
						SpritesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
					}
					else
					{
						SpritesPanel.Width = double.NaN;
						SpritesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
					}
				}
			}
			finally
			{
				isAdjustingPanels = false;
			}
		}
		catch
		{
		}
	}

	private ScrollViewer? GetInnerScrollViewer(DependencyObject? root)
	{
		if (root == null)
		{
			return null;
		}
		Queue<DependencyObject> queue = new Queue<DependencyObject>();
		queue.Enqueue(root);
		while (queue.Count > 0)
		{
			DependencyObject reference = queue.Dequeue();
			int childrenCount = VisualTreeHelper.GetChildrenCount(reference);
			for (int i = 0; i < childrenCount; i++)
			{
				DependencyObject child = VisualTreeHelper.GetChild(reference, i);
				if (child is ScrollViewer result)
				{
					return result;
				}
				queue.Enqueue(child);
			}
		}
		return null;
	}

	private void Ensure16VisibleOnStartup()
	{
		try
		{
			if (RootGrid == null)
			{
				return;
			}
			double actualWidth = RootGrid.ColumnDefinitions[0].ActualWidth;
			if (!(actualWidth <= 0.0))
			{
				double num = 8.0;
				double num2 = Math.Max(0.0, actualWidth - num);
				double verticalScrollBarWidth = SystemParameters.VerticalScrollBarWidth;
				int num3 = Math.Max(8, (int)Math.Floor((num2 - verticalScrollBarWidth) / 16.0));
				if (num3 > 20)
				{
					num3 = 20;
				}
				paletteTileSize = num3;
				paletteSpriteSize = num3;
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
				PopulateTilesPanel();
				PopulateSpritesPanel();
				if (TilesPanel != null)
				{
					TilesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
					TilesPanel.Width = Math.Max(0.0, num2);
				}
				if (SpritesPanel != null)
				{
					SpritesPanel.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
					SpritesPanel.Width = Math.Max(0.0, num2);
				}
			}
		}
		catch
		{
		}
	}

	private void LoadAssetsOnStart()
	{
		try
		{
			InitializePortalDebugLog();
			BitmapImage bitmapImage = LoadEmbeddedImage("famidash.bmp");
			if (bitmapImage != null)
			{
				tilesetBitmap = bitmapImage;
				SliceTileset();
				PopulateTilesPanel();
				if (StatusText != null)
				{
					StatusText.Text = "Loaded tileset from embedded resources";
				}
			}
			BitmapImage bitmapImage2 = LoadEmbeddedImage("sprites.png");
			if (bitmapImage2 != null)
			{
				spritesBitmap = bitmapImage2;
				SliceSpriteset();
				PopulateSpritesPanel();
				try
				{
					ApplyLockSpritesToSet();
				}
				catch
				{
				}
				if (StatusText != null)
				{
					StatusText.Text = "Loaded sprites from embedded resources";
				}
			}
			BitmapSource bitmapSource = null;
			if (noParallaxBg)
			{
				bitmapSource = LoadEmbeddedImage("noparallax.bmp");
				if (bitmapSource != null && StatusText != null)
				{
					StatusText.Text = "Loaded noparallax from embedded resources";
				}
			}
			if (bitmapSource == null)
			{
				bitmapSource = LoadEmbeddedImage("parallax Blue.bmp");
				if (bitmapSource == null)
				{
					bitmapSource = LoadEmbeddedImage("parallax.bmp");
				}
				if (bitmapSource != null && StatusText != null)
				{
					StatusText.Text = "Loaded parallax from embedded resources";
				}
			}
			if (bitmapSource != null)
			{
				parallaxBitmap = bitmapSource;
				SliceParallax();
			}
			BitmapImage bitmapImage3 = LoadEmbeddedImage("native_ground.bmp") ?? LoadEmbeddedImage("ground.bmp");
			if (bitmapImage3 != null)
			{
				groundBitmap = bitmapImage3;
				SliceGround();
				if (StatusText != null)
				{
					StatusText.Text = "Loaded ground from embedded resources";
				}
			}
			if (tilesetBitmap == null || spritesBitmap == null || parallaxBitmap == null || groundBitmap == null)
			{
				List<string> list = new List<string>();
				string text = FindRepoRootFor("famidash.bmp");
				if (!string.IsNullOrEmpty(text))
				{
					list.Add(text);
					list.Add(System.IO.Path.Combine(text, "src", "renderer", "assets"));
				}
				list.Add(AppContext.BaseDirectory);
				list.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "assets"));
				string[] array = new string[4] { "famidash.bmp", "famidash.png", "tileset.bmp", "tileset.png" };
				string[] array2 = new string[2] { "sprites.png", "sprites.bmp" };
				string[] array3 = new string[2] { "parallax.bmp", "parallax.png" };
				string[] array4 = new string[2] { "ground.bmp", "ground.png" };
				foreach (string item in list)
				{
					try
					{
						if (!Directory.Exists(item))
						{
							continue;
						}
						if (tilesetBitmap == null)
						{
							string[] array5 = array;
							foreach (string path in array5)
							{
								string path2 = System.IO.Path.Combine(item, path);
								if (File.Exists(path2))
								{
									LoadTileset(path2);
									break;
								}
							}
						}
						if (spritesBitmap == null)
						{
							string[] array6 = array2;
							foreach (string path3 in array6)
							{
								string path4 = System.IO.Path.Combine(item, path3);
								if (File.Exists(path4))
								{
									LoadSpriteset(path4);
									break;
								}
							}
						}
						if (parallaxBitmap == null)
						{
							string[] array7 = array3;
							foreach (string path5 in array7)
							{
								string path6 = System.IO.Path.Combine(item, path5);
								if (File.Exists(path6))
								{
									LoadParallax(path6);
									break;
								}
							}
						}
						if (groundBitmap != null)
						{
							continue;
						}
						string[] array8 = array4;
						foreach (string path7 in array8)
						{
							string path8 = System.IO.Path.Combine(item, path7);
							if (File.Exists(path8))
							{
								LoadGround(path8);
								break;
							}
						}
					}
					catch
					{
					}
				}
			}
			InitializeSawAnimationFrames();
			InitializePortalSprites();
			InitializeYellowOrbAnimationFrames();
			InitializeBlueOrbAnimationFrames();
			InitializePinkOrbAnimationFrames();
			InitializeGreenOrbAnimationFrames();
			InitializeRedOrbAnimationFrames();
			InitializeWhiteOrbAnimationFrames();
			InitializeCoinAnimationFrames();
			InitializeMiniCoinAnimationFrames();
			InitializeBlackOrbAnimationFrames();
			InitializeRedPadAnimationFrames();
			InitializeRedPadUpAnimationFrames();
			InitializeYellowPadDownAnimationFrames();
			InitializeYellowPadUpAnimationFrames();
			InitializeBluePadDownAnimationFrames();
			InitializeBluePadUpAnimationFrames();
			InitializePinkPadDownAnimationFrames();
			InitializePinkPadUpAnimationFrames();
			InitializeGreenPadExpandedAnimationFrames();
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
			LoadTwoFrameOrb("teleport-orb-enter", ref teleportOrbEnterFrame1, ref teleportOrbEnterFrame2);
			LoadTwoFrameOrb("teleport-orb-exit", ref teleportOrbExitFrame1, ref teleportOrbExitFrame2);
			LoadTwoFrameOrb("spider-orb-downwards", ref spiderOrbDownFrame1, ref spiderOrbDownFrame2);
			LoadTwoFrameOrb("spider-orb-upwards", ref spiderOrbUpFrame1, ref spiderOrbUpFrame2);
			LoadTwoFrameOrb("star", ref starFrame1, ref starFrame2);
			LoadTwoFrameOrb("pulsing-ball", ref pulsingBallFrame1, ref pulsingBallFrame2);
			LoadTwoFrameOrb("music-note", ref musicNoteFrame1, ref musicNoteFrame2);
			LoadTwoFrameOrb("diamond", ref diamondFrame1, ref diamondFrame2);
			LoadTwoFrameOrb("diamond-half", ref diamondHalfFrame1, ref diamondHalfFrame2);
			LoadTwoFrameOrb("question-mark", ref questionMarkFrame1, ref questionMarkFrame2);
			LoadTwoFrameOrb("exclamation-mark", ref exclamationFrame1, ref exclamationFrame2);
			LoadTwoFrameOrb("x", ref xFrame1, ref xFrame2);
			LoadTwoFrameOrb("pole-short", ref poleShortFrame1, ref poleShortFrame2);
			LoadTwoFrameOrb("pole-short-upsidedown", ref poleShortUpsideDownFrame1, ref poleShortUpsideDownFrame2);
			LoadTwoFrameOrb("pole-left-short", ref poleLeftShortFrame1, ref poleLeftShortFrame2);
			LoadTwoFrameOrb("pole-right-short", ref poleRightShortFrame1, ref poleRightShortFrame2);
			LoadTwoFrameOrb("pole-medium", ref poleMediumFrame1, ref poleMediumFrame2);
			LoadTwoFrameOrb("pole-medium-upsidedown", ref poleMediumUpsideDownFrame1, ref poleMediumUpsideDownFrame2);
			LoadTwoFrameOrb("pole-long", ref poleLongFrame1, ref poleLongFrame2);
			LoadTwoFrameOrb("pole-long-upsidedown", ref poleLongUpsideDownFrame1, ref poleLongUpsideDownFrame2);
			LoadTwoFrameOrb("pole-left-medium", ref poleLeftMediumFrame1, ref poleLeftMediumFrame2);
			LoadTwoFrameOrb("pole-right-medium", ref poleRightMediumFrame1, ref poleRightMediumFrame2);
			try
			{
				BitmapImage bitmapImage4 = LoadEmbeddedImage("chain.png");
				if (bitmapImage4 != null)
				{
					chainFrame1 = new BitmapSource[1];
					chainFrame1[0] = new FormatConvertedBitmap(bitmapImage4, PixelFormats.Pbgra32, null, 0.0);
				}
				try
				{
					BitmapImage bitmapImage5 = LoadEmbeddedImage("chain-upsidedown.png");
					if (bitmapImage5 != null)
					{
						chainUpsideFrame1 = new BitmapSource[1];
						chainUpsideFrame1[0] = new FormatConvertedBitmap(bitmapImage5, PixelFormats.Pbgra32, null, 0.0);
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
				BitmapImage bitmapImage6 = LoadEmbeddedImage("deco-spikes.png");
				if (bitmapImage6 != null)
				{
					decoSpikesFrame1 = new BitmapSource[1];
					decoSpikesFrame1[0] = new FormatConvertedBitmap(bitmapImage6, PixelFormats.Pbgra32, null, 0.0);
				}
				else
				{
					string baseDirectory = AppContext.BaseDirectory;
					string text2 = System.IO.Path.Combine(baseDirectory, "deco-spikes.png");
					string text3 = FindRepoRootFor("famidash.bmp");
					if (!string.IsNullOrEmpty(text3))
					{
						string text4 = System.IO.Path.Combine(text3, "deco-spikes.png");
						if (File.Exists(text4))
						{
							text2 = text4;
						}
					}
					if (File.Exists(text2))
					{
						BitmapImage bitmapImage7 = new BitmapImage();
						bitmapImage7.BeginInit();
						bitmapImage7.CacheOption = BitmapCacheOption.OnLoad;
						bitmapImage7.UriSource = new Uri(text2);
						bitmapImage7.EndInit();
						bitmapImage7.Freeze();
						decoSpikesFrame1 = new BitmapSource[1];
						decoSpikesFrame1[0] = new FormatConvertedBitmap(bitmapImage7, PixelFormats.Pbgra32, null, 0.0);
					}
				}
			}
			catch
			{
			}
			try
			{
				BitmapImage bitmapImage8 = LoadEmbeddedImage("deco-spikes-upsidedown.png");
				if (bitmapImage8 != null)
				{
					decoSpikesUpsideDownFrame1 = new BitmapSource[1];
					decoSpikesUpsideDownFrame1[0] = new FormatConvertedBitmap(bitmapImage8, PixelFormats.Pbgra32, null, 0.0);
				}
				else
				{
					string baseDirectory2 = AppContext.BaseDirectory;
					string text5 = System.IO.Path.Combine(baseDirectory2, "deco-spikes-upsidedown.png");
					string text6 = FindRepoRootFor("famidash.bmp");
					if (!string.IsNullOrEmpty(text6))
					{
						string text7 = System.IO.Path.Combine(text6, "deco-spikes-upsidedown.png");
						if (File.Exists(text7))
						{
							text5 = text7;
						}
					}
					if (File.Exists(text5))
					{
						BitmapImage bitmapImage9 = new BitmapImage();
						bitmapImage9.BeginInit();
						bitmapImage9.CacheOption = BitmapCacheOption.OnLoad;
						bitmapImage9.UriSource = new Uri(text5);
						bitmapImage9.EndInit();
						bitmapImage9.Freeze();
						decoSpikesUpsideDownFrame1 = new BitmapSource[1];
						decoSpikesUpsideDownFrame1[0] = new FormatConvertedBitmap(bitmapImage9, PixelFormats.Pbgra32, null, 0.0);
					}
				}
			}
			catch
			{
			}
			try
			{
				BitmapImage bitmapImage10 = LoadEmbeddedImage("deco-spikes-small.png");
				if (bitmapImage10 != null)
				{
					decoSpikesSmallFrame1 = new BitmapSource[1];
					decoSpikesSmallFrame1[0] = new FormatConvertedBitmap(bitmapImage10, PixelFormats.Pbgra32, null, 0.0);
				}
				else
				{
					string baseDirectory3 = AppContext.BaseDirectory;
					string text8 = System.IO.Path.Combine(baseDirectory3, "deco-spikes-small.png");
					string text9 = FindRepoRootFor("famidash.bmp");
					if (!string.IsNullOrEmpty(text9))
					{
						string text10 = System.IO.Path.Combine(text9, "deco-spikes-small.png");
						if (File.Exists(text10))
						{
							text8 = text10;
						}
					}
					if (File.Exists(text8))
					{
						BitmapImage bitmapImage11 = new BitmapImage();
						bitmapImage11.BeginInit();
						bitmapImage11.CacheOption = BitmapCacheOption.OnLoad;
						bitmapImage11.UriSource = new Uri(text8);
						bitmapImage11.EndInit();
						bitmapImage11.Freeze();
						decoSpikesSmallFrame1 = new BitmapSource[1];
						decoSpikesSmallFrame1[0] = new FormatConvertedBitmap(bitmapImage11, PixelFormats.Pbgra32, null, 0.0);
					}
				}
			}
			catch
			{
			}
			try
			{
				BitmapImage bitmapImage12 = LoadEmbeddedImage("deco-spikes-small-upsidedown.png");
				if (bitmapImage12 != null)
				{
					decoSpikesSmallUpsideDownFrame1 = new BitmapSource[1];
					decoSpikesSmallUpsideDownFrame1[0] = new FormatConvertedBitmap(bitmapImage12, PixelFormats.Pbgra32, null, 0.0);
					return;
				}
				string baseDirectory4 = AppContext.BaseDirectory;
				string text11 = System.IO.Path.Combine(baseDirectory4, "deco-spikes-small-upsidedown.png");
				string text12 = FindRepoRootFor("famidash.bmp");
				if (!string.IsNullOrEmpty(text12))
				{
					string text13 = System.IO.Path.Combine(text12, "deco-spikes-small-upsidedown.png");
					if (File.Exists(text13))
					{
						text11 = text13;
					}
				}
				if (File.Exists(text11))
				{
					BitmapImage bitmapImage13 = new BitmapImage();
					bitmapImage13.BeginInit();
					bitmapImage13.CacheOption = BitmapCacheOption.OnLoad;
					bitmapImage13.UriSource = new Uri(text11);
					bitmapImage13.EndInit();
					bitmapImage13.Freeze();
					decoSpikesSmallUpsideDownFrame1 = new BitmapSource[1];
					decoSpikesSmallUpsideDownFrame1[0] = new FormatConvertedBitmap(bitmapImage13, PixelFormats.Pbgra32, null, 0.0);
				}
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Error in LoadAssetsOnStart: " + ex.Message);
		}
	}

	private string? FindRepoRootFor(string filename)
	{
		string text = AppContext.BaseDirectory;
		Debug.WriteLine("FindRepoRootFor(" + filename + ") starting from: " + text);
		for (int i = 0; i < 6; i++)
		{
			string text2 = System.IO.Path.Combine(text, filename);
			bool flag = File.Exists(text2);
			Debug.WriteLine($"  [{i}] Checking: {text2} - Exists: {flag}");
			if (flag)
			{
				Debug.WriteLine("  FOUND! Returning: " + text);
				return text;
			}
			DirectoryInfo parent = Directory.GetParent(text);
			if (parent == null)
			{
				Debug.WriteLine("  No parent directory, breaking");
				break;
			}
			text = parent.FullName;
		}
		Debug.WriteLine("  NOT FOUND, returning null");
		return null;
	}

	private BitmapImage? LoadEmbeddedImage(string resourceName)
	{
		try
		{
			Assembly executingAssembly = Assembly.GetExecutingAssembly();
			string[] manifestResourceNames = executingAssembly.GetManifestResourceNames();
			string text = manifestResourceNames.FirstOrDefault((string r) => r.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
			if (text == null)
			{
				text = manifestResourceNames.FirstOrDefault((string r) => r.IndexOf(resourceName, StringComparison.OrdinalIgnoreCase) >= 0);
			}
			if (text == null)
			{
				if (resourceName.IndexOf("parallax", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					text = manifestResourceNames.FirstOrDefault((string r) => r.IndexOf("parallax", StringComparison.OrdinalIgnoreCase) >= 0);
				}
				else if (resourceName.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					text = manifestResourceNames.FirstOrDefault((string r) => r.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0);
				}
			}
			if (text == null)
			{
				Debug.WriteLine("Resource not found: " + resourceName);
				Debug.WriteLine("Available resources: " + string.Join(", ", manifestResourceNames));
				return null;
			}
			using Stream stream = executingAssembly.GetManifestResourceStream(text);
			if (stream == null)
			{
				Debug.WriteLine("Failed to load resource stream: " + text);
				return null;
			}
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.StreamSource = stream;
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Error loading embedded resource " + resourceName + ": " + ex.Message);
			return null;
		}
	}

	private void TryLoadFamiAlbumParsedJson()
	{
		if (FamiTrackCombo == null)
		{
			return;
		}
		List<string> list = new List<string>
		{
			System.IO.Path.Combine(AppContext.BaseDirectory, "fami-album-parsed.json"),
			System.IO.Path.Combine(Environment.CurrentDirectory, "fami-album-parsed.json")
		};
		string text = FindRepoRootFor("the album.txt");
		if (!string.IsNullOrEmpty(text))
		{
			list.Add(System.IO.Path.Combine(text, "fami-album-parsed.json"));
			list.Add(System.IO.Path.Combine(text, "native-windows", "fami-album-parsed.json"));
		}
		string text2 = null;
		foreach (string item in list)
		{
			try
			{
				if (!string.IsNullOrEmpty(item) && File.Exists(item))
				{
					text2 = item;
					break;
				}
			}
			catch
			{
			}
		}
		List<string> list2 = new List<string>();
		if (text2 != null)
		{
			try
			{
				string json = File.ReadAllText(text2);
				using JsonDocument jsonDocument = JsonDocument.Parse(json);
				if (jsonDocument.RootElement.TryGetProperty("parsedNames", out var value) && value.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item2 in value.EnumerateArray())
					{
						list2.Add(item2.GetString() ?? "");
					}
				}
			}
			catch
			{
			}
		}
		if (list2.Count == 0)
		{
			string text3 = null;
			List<string> list3 = new List<string>
			{
				System.IO.Path.Combine(AppContext.BaseDirectory, "the album.txt"),
				System.IO.Path.Combine(Environment.CurrentDirectory, "the album.txt")
			};
			if (!string.IsNullOrEmpty(text))
			{
				list3.Add(System.IO.Path.Combine(text, "the album.txt"));
				list3.Add(System.IO.Path.Combine(text, "native-windows", "the album.txt"));
			}
			foreach (string item3 in list3)
			{
				try
				{
					if (!string.IsNullOrEmpty(item3) && File.Exists(item3))
					{
						text3 = item3;
						break;
					}
				}
				catch
				{
				}
			}
			if (text3 != null)
			{
				albumTxtPath = text3;
				try
				{
					list2 = famiIntegration.ParseFamiStudioTextExport(text3);
				}
				catch
				{
					list2 = new List<string>();
				}
			}
		}
		try
		{
			string text4 = null;
			List<string> list4 = new List<string>
			{
				System.IO.Path.Combine(AppContext.BaseDirectory, "the album.fms"),
				System.IO.Path.Combine(Environment.CurrentDirectory, "the album.fms")
			};
			if (!string.IsNullOrEmpty(text))
			{
				list4.Add(System.IO.Path.Combine(text, "the album.fms"));
				list4.Add(System.IO.Path.Combine(text, "native-windows", "the album.fms"));
			}
			if (!string.IsNullOrEmpty(text))
			{
				try
				{
					foreach (string item4 in Directory.EnumerateFiles(text, "*.fms", SearchOption.TopDirectoryOnly))
					{
						list4.Add(item4);
					}
				}
				catch
				{
				}
			}
			foreach (string item5 in list4)
			{
				try
				{
					if (!string.IsNullOrEmpty(item5) && File.Exists(item5))
					{
						text4 = item5;
						break;
					}
				}
				catch
				{
				}
			}
			if (!string.IsNullOrEmpty(text4))
			{
				albumTxtPath = text4;
				try
				{
					if (StatusText != null)
					{
						StatusText.Text = "Found .fms for playback: " + System.IO.Path.GetFileName(text4);
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
		FamiTrackCombo.Items.Clear();
		Dictionary<string, int> dictionary = null;
		try
		{
			string[] array = new string[4]
			{
				System.IO.Path.Combine(AppContext.BaseDirectory, "fami-song-index-map.json"),
				System.IO.Path.Combine(Environment.CurrentDirectory, "fami-song-index-map.json"),
				System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AppContext.BaseDirectory) ?? AppContext.BaseDirectory, "native-windows", "fami-song-index-map.json"),
				System.IO.Path.Combine(AppContext.BaseDirectory, "..", "native-windows", "fami-song-index-map.json")
			};
			string[] array2 = array;
			foreach (string text5 in array2)
			{
				try
				{
					if (string.IsNullOrEmpty(text5) || !File.Exists(text5))
					{
						continue;
					}
					string json2 = File.ReadAllText(text5);
					List<Dictionary<string, object>> list5 = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json2);
					if (list5 == null)
					{
						continue;
					}
					dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
					foreach (Dictionary<string, object> item6 in list5)
					{
						if (item6.TryGetValue("name", out var value2) && item6.TryGetValue("index", out var value3))
						{
							string text6 = value2?.ToString();
							if (int.TryParse(value3?.ToString() ?? "", out var result) && !string.IsNullOrEmpty(text6) && !dictionary.ContainsKey(text6))
							{
								dictionary[text6] = result;
							}
							mappingLoadedFromFile = true;
						}
					}
					break;
				}
				catch
				{
				}
			}
		}
		catch
		{
			dictionary = null;
		}
		if (list2.Count > 0)
		{
			for (int j = 0; j < list2.Count; j++)
			{
				int num = j;
				try
				{
					if (dictionary != null && dictionary.TryGetValue(list2[j], out var value4))
					{
						num = value4;
					}
				}
				catch
				{
				}
				ComboBoxItem newItem = new ComboBoxItem
				{
					Content = list2[j],
					Tag = num
				};
				FamiTrackCombo.Items.Add(newItem);
			}
			int selectedIndex = 0;
			for (int k = 0; k < FamiTrackCombo.Items.Count; k++)
			{
				if (FamiTrackCombo.Items[k] is ComboBoxItem { Content: { } content } && content.ToString()?.Equals("Stereo Madness", StringComparison.OrdinalIgnoreCase) == true)
				{
					selectedIndex = k;
					break;
				}
			}
			FamiTrackCombo.SelectedIndex = selectedIndex;
			try
			{
				if (StatusText != null)
				{
					StatusText.Text = $"Loaded {list2.Count} names from {((text2 != null) ? System.IO.Path.GetFileName(text2) : ((albumTxtPath != null) ? System.IO.Path.GetFileName(albumTxtPath) : "unknown"))}";
				}
				return;
			}
			catch
			{
				return;
			}
		}
		for (int l = 0; l < 8; l++)
		{
			FamiTrackCombo.Items.Add(new ComboBoxItem
			{
				Content = $"Song {l}",
				Tag = l
			});
		}
		if (FamiTrackCombo.Items.Count > 0)
		{
			FamiTrackCombo.SelectedIndex = 0;
		}
		try
		{
			if (StatusText != null)
			{
				StatusText.Text = "No parsed song names found";
			}
		}
		catch
		{
		}
	}

	private async void PlayFamiButton_Click(object? sender, RoutedEventArgs e)
	{
		if (albumTxtPath == null)
		{
			System.Windows.MessageBox.Show(this, "No album.txt found to play.", "Play", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		int idx = -1;
		object obj = FamiTrackCombo?.SelectedItem;
		int t = default(int);
		int num;
		if (obj is ComboBoxItem { Tag: var tag })
		{
			if (tag is int)
			{
				t = (int)tag;
				num = 1;
			}
			else
			{
				num = 0;
			}
		}
		else
		{
			num = 0;
		}
		if (num != 0)
		{
			idx = t;
		}
		else
		{
			System.Windows.Controls.ComboBox famiTrackCombo = FamiTrackCombo;
			if (famiTrackCombo != null && famiTrackCombo.SelectedIndex >= 0)
			{
				idx = FamiTrackCombo.SelectedIndex;
			}
		}
		if (idx < 0)
		{
			System.Windows.MessageBox.Show(this, "No track selected.", "Play", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		await Task.Run(delegate
		{
			try
			{
				string text = albumTxtPath;
				if (File.Exists(text) && System.IO.Path.GetExtension(text).Equals(".fms", StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						if (!famiIntegration.IsLoaded && !string.IsNullOrEmpty(famiStudioPath) && Directory.Exists(famiStudioPath))
						{
							famiIntegration.LoadFromFolder(famiStudioPath);
						}
						if (!mappingLoadedFromFile)
						{
							List<string> list = famiIntegration.EnumerateTracks(text);
							if (list != null && list.Count > 0)
							{
								string selectedName = null;
								if (FamiTrackCombo?.SelectedItem is ComboBoxItem { Content: not null } comboBoxItem)
								{
									selectedName = comboBoxItem.Content.ToString();
								}
								if (!string.IsNullOrEmpty(selectedName))
								{
									int num2 = list.FindIndex((string n) => string.Equals(n, selectedName, StringComparison.OrdinalIgnoreCase));
									if (num2 >= 0)
									{
										idx = num2;
									}
								}
							}
						}
					}
					catch
					{
					}
				}
				famiIntegration.PlayTrack(text, idx);
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				Exception ex3 = ex2;
				base.Dispatcher.Invoke(() => System.Windows.MessageBox.Show(this, "Play failed: " + ex3.Message, "Play Error", MessageBoxButton.OK, MessageBoxImage.Hand));
			}
		});
	}

	private void StopFamiButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			famiIntegration.Stop();
		}
		catch
		{
		}
	}

	private void MenuScanFamiStudioTracks_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			using System.Windows.Forms.OpenFileDialog openFileDialog = new System.Windows.Forms.OpenFileDialog();
			openFileDialog.Title = "Select a FamiStudio project (.fms) or text export (.txt)";
			openFileDialog.Filter = "FamiStudio project (*.fms)|*.fms|Text export (*.txt)|*.txt|All files (*.*)|*.*";
			openFileDialog.Multiselect = false;
			DialogResult dialogResult = openFileDialog.ShowDialog();
			if (dialogResult != System.Windows.Forms.DialogResult.OK)
			{
				return;
			}
			string fileName = openFileDialog.FileName;
			if (string.IsNullOrEmpty(fileName) || !File.Exists(fileName))
			{
				return;
			}
			List<string> list = new List<string>();
			string text = null;
			if (System.IO.Path.GetExtension(fileName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
			{
				list = famiIntegration.ParseFamiStudioTextExport(fileName);
				text = fileName;
			}
			else if (System.IO.Path.GetExtension(fileName).Equals(".fms", StringComparison.OrdinalIgnoreCase))
			{
				bool flag = false;
				string text2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"famistudio_txt_{Guid.NewGuid()}.txt");
				if (!string.IsNullOrEmpty(famiStudioPath) && Directory.Exists(famiStudioPath))
				{
					string text3 = System.IO.Path.Combine(famiStudioPath, "FamiStudio.exe");
					if (File.Exists(text3))
					{
						string[] array = new string[5] { "text-export", "textexport", "txt-export", "export-text", "famistudio-txt-export" };
						string[] array2 = array;
						foreach (string value in array2)
						{
							try
							{
								if (File.Exists(text2))
								{
									try
									{
										File.Delete(text2);
									}
									catch
									{
									}
								}
								string arguments = $"\"{fileName}\" {value} \"{text2}\"";
								ProcessStartInfo startInfo = new ProcessStartInfo(text3, arguments)
								{
									CreateNoWindow = true,
									UseShellExecute = false,
									RedirectStandardOutput = true,
									RedirectStandardError = true
								};
								using Process process = Process.Start(startInfo);
								if (process == null)
								{
									continue;
								}
								process.WaitForExit(5000);
								if (!File.Exists(text2) || new FileInfo(text2).Length <= 0)
								{
									continue;
								}
								try
								{
									list = famiIntegration.ParseFamiStudioTextExport(text2);
									text = text2;
									flag = true;
								}
								catch
								{
									goto end_IL_0245;
								}
								break;
								end_IL_0245:;
							}
							catch
							{
							}
						}
					}
				}
				if (!flag)
				{
					try
					{
						list = famiIntegration.TryParseFmsSongNames(fileName);
						text = fileName;
					}
					catch
					{
						list = new List<string>();
					}
					if (list.Count == 0 && famiIntegration.IsLoaded)
					{
						try
						{
							list = famiIntegration.EnumerateTracks(fileName);
							text = fileName;
						}
						catch
						{
						}
					}
				}
			}
			FamiTrackCombo.Items.Clear();
			albumTxtPath = text;
			if (list != null && list.Count > 0)
			{
				for (int j = 0; j < list.Count; j++)
				{
					ComboBoxItem newItem = new ComboBoxItem
					{
						Content = list[j],
						Tag = j
					};
					FamiTrackCombo.Items.Add(newItem);
				}
				int selectedIndex = 0;
				for (int k = 0; k < FamiTrackCombo.Items.Count; k++)
				{
					if (FamiTrackCombo.Items[k] is ComboBoxItem { Content: { } content } && content.ToString()?.Equals("Stereo Madness", StringComparison.OrdinalIgnoreCase) == true)
					{
						selectedIndex = k;
						break;
					}
				}
				FamiTrackCombo.SelectedIndex = selectedIndex;
				if (StatusText != null)
				{
					StatusText.Text = $"Loaded {list.Count} tracks from {System.IO.Path.GetFileName(text ?? fileName)} (transient)";
				}
			}
			else
			{
				for (int l = 0; l < 8; l++)
				{
					FamiTrackCombo.Items.Add(new ComboBoxItem
					{
						Content = $"Song {l}",
						Tag = l
					});
				}
				if (FamiTrackCombo.Items.Count > 0)
				{
					FamiTrackCombo.SelectedIndex = 0;
				}
				if (StatusText != null)
				{
					StatusText.Text = "No parsed song names found in selected file";
				}
			}
		}
		catch (Exception ex)
		{
			try
			{
				System.Windows.MessageBox.Show(this, "Scan failed: " + ex.Message, "FamiStudio Scan", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
			catch
			{
			}
		}
	}

	private void InitializePortalDebugLog()
	{
		if (!string.IsNullOrEmpty(portalDebugPath))
		{
			return;
		}
		string text = FindRepoRootFor("famidash.bmp");
		string text2 = ((!string.IsNullOrEmpty(text)) ? System.IO.Path.Combine(text, "portal-debug.txt") : null);
		string[] array = new string[3]
		{
			System.IO.Path.Combine(AppContext.BaseDirectory, "portal-debug.txt"),
			text2,
			System.IO.Path.Combine(System.IO.Path.GetTempPath(), "portal-debug.txt")
		};
		string[] array2 = array;
		foreach (string text3 in array2)
		{
			if (string.IsNullOrEmpty(text3))
			{
				continue;
			}
			try
			{
				string directoryName = System.IO.Path.GetDirectoryName(text3);
				if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				Debug.WriteLine("Portal debug candidate path: " + text3);
				portalDebugPath = text3;
				return;
			}
			catch
			{
			}
		}
		portalDebugPath = System.IO.Path.Combine(AppContext.BaseDirectory, "portal-debug.txt");
	}

	private void LoadTileset(string path)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			tilesetBitmap = bitmapImage;
			SliceTileset();
			PopulateTilesPanel();
			if (StatusText != null)
			{
				StatusText.Text = "Loaded tileset: " + System.IO.Path.GetFileName(path);
			}
		}
		catch (Exception ex)
		{
			if (StatusText != null)
			{
				StatusText.Text = "Tileset load failed: " + ex.Message;
			}
		}
	}

	private void LoadSpriteset(string path)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			spritesBitmap = bitmapImage;
			SliceSpriteset();
			PopulateSpritesPanel();
			if (StatusText != null)
			{
				StatusText.Text = "Loaded sprites: " + System.IO.Path.GetFileName(path);
			}
			try
			{
				ApplyLockSpritesToSet();
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			if (StatusText != null)
			{
				StatusText.Text = "Sprites load failed: " + ex.Message;
			}
		}
	}

	private void LoadParallax(string path)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			parallaxBitmap = bitmapImage;
			SliceParallax();
			if (StatusText != null)
			{
				StatusText.Text = "Loaded parallax: " + System.IO.Path.GetFileName(path);
			}
		}
		catch (Exception ex)
		{
			if (StatusText != null)
			{
				StatusText.Text = "Parallax load failed: " + ex.Message;
			}
		}
	}

	private void LoadGround(string path)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			groundBitmap = bitmapImage;
			SliceGround();
			if (StatusText != null)
			{
				StatusText.Text = "Loaded ground: " + System.IO.Path.GetFileName(path);
			}
		}
		catch (Exception ex)
		{
			if (StatusText != null)
			{
				StatusText.Text = "Ground load failed: " + ex.Message;
			}
		}
	}

	private void MenuConfigureFamiStudio_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
			folderBrowserDialog.Description = "Select the FamiStudio installation folder (contains FamiStudio.exe / FamiStudio.dll)";
			if (!string.IsNullOrEmpty(famiStudioPath) && Directory.Exists(famiStudioPath))
			{
				folderBrowserDialog.SelectedPath = famiStudioPath;
			}
			DialogResult dialogResult = folderBrowserDialog.ShowDialog();
			if (dialogResult != System.Windows.Forms.DialogResult.OK && dialogResult != System.Windows.Forms.DialogResult.Yes)
			{
				return;
			}
			string selectedPath = folderBrowserDialog.SelectedPath;
			if (string.IsNullOrEmpty(selectedPath) || !Directory.Exists(selectedPath))
			{
				return;
			}
			famiStudioPath = selectedPath;
			try
			{
				famiIntegration.LoadFromFolder(famiStudioPath);
			}
			catch (Exception ex)
			{
				System.Windows.MessageBox.Show(this, "Failed to load FamiStudio: " + ex.Message, "FamiStudio Load", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
			try
			{
				Color c = ((mapBackground is SolidColorBrush solidColorBrush) ? solidColorBrush.Color : Color.FromRgb(59, 59, 59));
				SaveSettings(c);
			}
			catch
			{
			}
			try
			{
				if (StatusText != null)
				{
					StatusText.Text = "Configured FamiStudio: " + System.IO.Path.GetFileName(famiStudioPath);
				}
			}
			catch
			{
			}
		}
		catch (Exception ex2)
		{
			try
			{
				System.Windows.MessageBox.Show(this, "Failed to configure FamiStudio: " + ex2.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
			catch
			{
			}
		}
	}

	private void SliceTileset()
	{
		if (tilesetBitmap == null)
		{
			tileImages = null;
			return;
		}
		try
		{
			scaledTileCaches.Clear();
		}
		catch
		{
		}
		int num = Math.Max(1, tilesetBitmap.PixelWidth / 16);
		int num2 = Math.Max(1, tilesetBitmap.PixelHeight / 16);
		List<ImageSource> list = new List<ImageSource>();
		int num3 = 0;
		int num4 = 0;
		for (int i = 0; i < num2; i++)
		{
			for (int j = 0; j < num; j++)
			{
				try
				{
					CroppedBitmap croppedBitmap = new CroppedBitmap(tilesetBitmap, new Int32Rect(j * 16, i * 16, 16, 16));
					if (croppedBitmap.CanFreeze)
					{
						croppedBitmap.Freeze();
					}
					list.Add(croppedBitmap);
				}
				catch (Exception ex)
				{
					num4++;
					Debug.WriteLine($"ERROR: Failed to crop tile {num3} (0x{num3:X}) at grid ({j},{i}): {ex.Message}");
					WriteableBitmap writeableBitmap = new WriteableBitmap(16, 16, 96.0, 96.0, PixelFormats.Bgra32, null);
					writeableBitmap.Freeze();
					list.Add(writeableBitmap);
				}
				num3++;
			}
		}
		tileImages = list.ToArray();
		if (num4 > 0)
		{
			Debug.WriteLine($"WARNING: SliceTileset had {num4} errors out of {tileImages.Length} tiles");
		}
		if (tileTint.A != 0)
		{
			UpdateTileTint();
		}
		else
		{
			tileTonedImages = null;
		}
	}

	private void SliceSpriteset()
	{
		if (spritesBitmap == null)
		{
			spriteImages = null;
			return;
		}
		int num = Math.Max(1, spritesBitmap.PixelWidth / 16);
		int num2 = Math.Max(1, spritesBitmap.PixelHeight / 16);
		List<ImageSource> list = new List<ImageSource>();
		int num3 = 0;
		int num4 = 0;
		for (int i = 0; i < num2; i++)
		{
			for (int j = 0; j < num; j++)
			{
				try
				{
					CroppedBitmap croppedBitmap = new CroppedBitmap(spritesBitmap, new Int32Rect(j * 16, i * 16, 16, 16));
					if (croppedBitmap.CanFreeze)
					{
						croppedBitmap.Freeze();
					}
					list.Add(croppedBitmap);
				}
				catch (Exception ex)
				{
					num4++;
					Debug.WriteLine($"ERROR: Failed to crop sprite {num3} (0x{num3:X}) at grid ({j},{i}): {ex.Message}");
					WriteableBitmap writeableBitmap = new WriteableBitmap(16, 16, 96.0, 96.0, PixelFormats.Bgra32, null);
					writeableBitmap.Freeze();
					list.Add(writeableBitmap);
				}
				num3++;
			}
		}
		spriteImages = list.ToArray();
		Debug.WriteLine($"Loaded {spriteImages.Length} sprite images");
		Debug.WriteLine($"  Sprite 0x17 ({23}) in range: {23 < spriteImages.Length}");
		Debug.WriteLine($"  Sprite 0x4B ({75}) in range: {75 < spriteImages.Length}");
		Debug.WriteLine($"  Sprite 0x58 ({88}) in range: {88 < spriteImages.Length}");
		Debug.WriteLine($"  Sprite 0x08 ({8}) in range: {8 < spriteImages.Length}");
		Debug.WriteLine($"  Sprite 0x09 ({9}) in range: {9 < spriteImages.Length}");
		Debug.WriteLine($"  Sprite 0x0B ({11}) in range: {11 < spriteImages.Length}");
		Debug.WriteLine($"  Sprite 0x1F ({31}) in range: {31 < spriteImages.Length}");
		Debug.WriteLine($"  Sprite 0x29 ({41}) in range: {41 < spriteImages.Length}");
		if (num4 > 0)
		{
			Debug.WriteLine($"WARNING: SliceSpriteset had {num4} errors out of {spriteImages.Length} sprites");
		}
	}

	private void SliceParallax()
	{
		if (parallaxBitmap == null)
		{
			parallaxImages = null;
			return;
		}
		BitmapSource bitmapSource = parallaxBitmap;
		if (bitmapSource == null)
		{
			parallaxImages = null;
			return;
		}
		if (Math.Max(bitmapSource.PixelWidth, bitmapSource.PixelHeight) > 2048)
		{
			bitmapSource = DownsampleBitmap(bitmapSource, 2048) ?? bitmapSource;
		}
		int num = Math.Max(1, bitmapSource.PixelWidth / 16);
		int num2 = Math.Max(1, bitmapSource.PixelHeight / 16);
		List<ImageSource> list = new List<ImageSource>();
		for (int i = 0; i < num2; i += 8)
		{
			int num3 = Math.Min(num2, i + 8);
			for (int j = i; j < num3; j++)
			{
				for (int k = 0; k < num; k++)
				{
					try
					{
						CroppedBitmap croppedBitmap = new CroppedBitmap(bitmapSource, new Int32Rect(k * 16, j * 16, 16, 16));
						if (croppedBitmap.CanFreeze)
						{
							try
							{
								croppedBitmap.Freeze();
							}
							catch
							{
							}
						}
						list.Add(croppedBitmap);
					}
					catch
					{
						WriteableBitmap writeableBitmap = new WriteableBitmap(16, 16, 96.0, 96.0, PixelFormats.Bgra32, null);
						try
						{
							writeableBitmap.WritePixels(new Int32Rect(0, 0, 16, 16), new byte[1024], 64, 0);
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
				}
			}
			try
			{
				base.Dispatcher.Invoke(delegate
				{
				}, DispatcherPriority.Background);
			}
			catch
			{
			}
			try
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
			catch
			{
			}
		}
		parallaxImages = list.ToArray();
		if (backgroundTint.A != 0)
		{
			UpdateParallaxTint();
		}
		else
		{
			parallaxTonedImages = null;
		}
	}

	private void SliceGround()
	{
		if (groundBitmap == null)
		{
			groundImages = null;
			groundTileRows = 0;
			return;
		}
		BitmapSource bitmapSource = groundBitmap;
		if (bitmapSource == null)
		{
			groundImages = null;
			groundTileRows = 0;
			return;
		}
		if (Math.Max(bitmapSource.PixelWidth, bitmapSource.PixelHeight) > 2048)
		{
			bitmapSource = DownsampleBitmap(bitmapSource, 2048) ?? bitmapSource;
		}
		int num = Math.Max(1, bitmapSource.PixelWidth / 16);
		int num2 = (groundTileRows = Math.Max(1, bitmapSource.PixelHeight / 16));
		List<ImageSource> list = new List<ImageSource>();
		for (int i = 0; i < num2; i += 8)
		{
			int num3 = Math.Min(num2, i + 8);
			for (int j = i; j < num3; j++)
			{
				for (int k = 0; k < num; k++)
				{
					try
					{
						CroppedBitmap croppedBitmap = new CroppedBitmap(bitmapSource, new Int32Rect(k * 16, j * 16, 16, 16));
						if (croppedBitmap.CanFreeze)
						{
							try
							{
								croppedBitmap.Freeze();
							}
							catch
							{
							}
						}
						list.Add(croppedBitmap);
					}
					catch
					{
						WriteableBitmap writeableBitmap = new WriteableBitmap(16, 16, 96.0, 96.0, PixelFormats.Bgra32, null);
						try
						{
							writeableBitmap.WritePixels(new Int32Rect(0, 0, 16, 16), new byte[1024], 64, 0);
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
				}
			}
			try
			{
				base.Dispatcher.Invoke(delegate
				{
				}, DispatcherPriority.Background);
			}
			catch
			{
			}
			try
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
			catch
			{
			}
		}
		groundImages = list.ToArray();
		if (groundTint.A != 0)
		{
			UpdateGroundTint();
		}
		else
		{
			groundTonedImages = null;
		}
	}

	private BitmapSource? DownsampleBitmap(BitmapSource? src, int maxDim)
	{
		if (src == null)
		{
			return src;
		}
		int pixelWidth = src.PixelWidth;
		int pixelHeight = src.PixelHeight;
		int num = Math.Max(pixelWidth, pixelHeight);
		if (num <= maxDim)
		{
			return src;
		}
		double num2 = (double)maxDim / (double)num;
		try
		{
			TransformedBitmap transformedBitmap = new TransformedBitmap(src, new ScaleTransform(num2, num2));
			try
			{
				transformedBitmap.Freeze();
			}
			catch
			{
			}
			return transformedBitmap;
		}
		catch
		{
			return src;
		}
	}

	private ImageSource?[]? CreateTintedImages(ImageSource?[]? originals, Color tint)
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
		foreach (ImageSource imageSource in originals)
		{
			if (imageSource is BitmapSource source)
			{
				FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
				int pixelWidth = formatConvertedBitmap.PixelWidth;
				int pixelHeight = formatConvertedBitmap.PixelHeight;
				int num = pixelWidth * 4;
				byte[] array = new byte[pixelHeight * num];
				formatConvertedBitmap.CopyPixels(array, num, 0);
				byte a = tint.A;
				int num2 = a;
				for (int j = 0; j < array.Length; j += 4)
				{
					int num3 = array[j];
					int num4 = array[j + 1];
					int num5 = array[j + 2];
					int num6 = array[j + 3];
					int num7 = (num5 * (255 - num2) + tint.R * num2) / 255;
					int num8 = (num4 * (255 - num2) + tint.G * num2) / 255;
					int num9 = (num3 * (255 - num2) + tint.B * num2) / 255;
					array[j] = (byte)num9;
					array[j + 1] = (byte)num8;
					array[j + 2] = (byte)num7;
					array[j + 3] = (byte)num6;
				}
				WriteableBitmap writeableBitmap = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
				writeableBitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
				writeableBitmap.Freeze();
				list.Add(writeableBitmap);
			}
			else if (imageSource == null)
			{
				WriteableBitmap writeableBitmap2 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
				try
				{
					writeableBitmap2.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0);
				}
				catch
				{
				}
				try
				{
					writeableBitmap2.Freeze();
				}
				catch
				{
				}
				list.Add(writeableBitmap2);
			}
			else
			{
				list.Add(imageSource);
			}
		}
		return list.ToArray();
	}

	private ImageSource?[]? CreateExactRgbReplacedImages(ImageSource?[]? originals, Color tint)
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
		foreach (ImageSource imageSource in originals)
		{
			if (imageSource is BitmapSource source)
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
						if (array[j + 3] != 0)
						{
							array[j] = tint.B;
							array[j + 1] = tint.G;
							array[j + 2] = tint.R;
						}
					}
					WriteableBitmap writeableBitmap = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
					writeableBitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
					writeableBitmap.Freeze();
					list.Add(writeableBitmap);
				}
				catch
				{
					if (imageSource == null)
					{
						WriteableBitmap writeableBitmap2 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
						try
						{
							writeableBitmap2.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0);
						}
						catch
						{
						}
						try
						{
							writeableBitmap2.Freeze();
						}
						catch
						{
						}
						list.Add(writeableBitmap2);
					}
					else
					{
						list.Add(imageSource);
					}
				}
			}
			else if (imageSource == null)
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
		return list.ToArray();
	}

	private ImageSource?[]? CreateRgbReplacedImages(ImageSource?[]? originals, Color tint)
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
		foreach (ImageSource imageSource in originals)
		{
			if (imageSource is BitmapSource source)
			{
				try
				{
					FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
					int pixelWidth = formatConvertedBitmap.PixelWidth;
					int pixelHeight = formatConvertedBitmap.PixelHeight;
					int num = pixelWidth * 4;
					byte[] array = new byte[pixelHeight * num];
					formatConvertedBitmap.CopyPixels(array, num, 0);
					double num2 = double.MaxValue;
					double num3 = double.MinValue;
					int num4 = 0;
					for (int j = 0; j < array.Length; j += 4)
					{
						byte b = array[j];
						byte b2 = array[j + 1];
						byte b3 = array[j + 2];
						byte b4 = array[j + 3];
						bool flag = b3 <= 12 && b2 <= 12 && b <= 12;
						if (b4 != 0 && !flag)
						{
							double num5 = 0.299 * (double)(int)b3 + 0.587 * (double)(int)b2 + 0.114 * (double)(int)b;
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
					Color color = Color.FromRgb(tint.R, tint.G, tint.B);
					Color color2 = PaletteHelper.RowUpColor(color);
					double num6 = ((num2 == double.MaxValue) ? 0.0 : ((num2 + num3) * 0.5));
					for (int k = 0; k < array.Length; k += 4)
					{
						byte b5 = array[k];
						byte b6 = array[k + 1];
						byte b7 = array[k + 2];
						byte b8 = array[k + 3];
						bool flag2 = b7 <= 12 && b6 <= 12 && b5 <= 12;
						if (b8 != 0 && !flag2)
						{
							if (num4 <= 1 || num3 == num2)
							{
								array[k] = color.B;
								array[k + 1] = color.G;
								array[k + 2] = color.R;
							}
							else
							{
								double num7 = 0.299 * (double)(int)b7 + 0.587 * (double)(int)b6 + 0.114 * (double)(int)b5;
								Color color3 = ((num7 >= num6) ? color : color2);
								array[k] = color3.B;
								array[k + 1] = color3.G;
								array[k + 2] = color3.R;
							}
						}
					}
					WriteableBitmap writeableBitmap = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
					writeableBitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
					writeableBitmap.Freeze();
					list.Add(writeableBitmap);
				}
				catch
				{
					if (imageSource == null)
					{
						WriteableBitmap writeableBitmap2 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
						try
						{
							writeableBitmap2.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0);
						}
						catch
						{
						}
						try
						{
							writeableBitmap2.Freeze();
						}
						catch
						{
						}
						list.Add(writeableBitmap2);
					}
					else
					{
						list.Add(imageSource);
					}
				}
			}
			else if (imageSource == null)
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
		return list.ToArray();
	}

	private ImageSource?[]? CreateHslShiftedImages(ImageSource?[]? originals, Color tint)
	{
		if (originals == null)
		{
			return null;
		}
		if (tint.A == 0)
		{
			return originals;
		}
		RgbToHsl(tint.R, tint.G, tint.B, out var h, out var _, out var _);
		List<ImageSource> list = new List<ImageSource>(originals.Length);
		foreach (ImageSource imageSource in originals)
		{
			if (imageSource is BitmapSource source)
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
						byte b4 = array[j + 3];
						bool flag = b3 <= 12 && b2 <= 12 && b <= 12;
						bool flag2 = b3 >= 249 && b2 >= 249 && b >= 249;
						if (!(b4 == 0 || flag || flag2))
						{
							RgbToHsl(b3, b2, b, out var _, out var s2, out var l2);
							double h3 = h;
							double s3 = s2;
							double l3 = l2;
							RgbFromHsl(h3, s3, l3, out var r, out var g, out var b5);
							array[j] = b5;
							array[j + 1] = g;
							array[j + 2] = r;
						}
					}
					WriteableBitmap writeableBitmap = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
					writeableBitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
					writeableBitmap.Freeze();
					list.Add(writeableBitmap);
				}
				catch
				{
					if (imageSource == null)
					{
						WriteableBitmap writeableBitmap2 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
						try
						{
							writeableBitmap2.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0);
						}
						catch
						{
						}
						try
						{
							writeableBitmap2.Freeze();
						}
						catch
						{
						}
						list.Add(writeableBitmap2);
					}
					else
					{
						list.Add(imageSource);
					}
				}
			}
			else if (imageSource == null)
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
		return list.ToArray();
	}

	private ImageSource?[]? CreateHueShiftedImages(ImageSource?[]? originals, Color tint)
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
			return CreateTwoToneTileImages(originals, Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(byte.MaxValue, 0, 0, 0), tileTint);
		}
		double num = (double)(int)tint.A / 255.0;
		RgbToHsl(tint.R, tint.G, tint.B, out var h, out var s, out var _);
		List<ImageSource> list = new List<ImageSource>(originals.Length);
		foreach (ImageSource imageSource in originals)
		{
			if (imageSource is BitmapSource source)
			{
				try
				{
					FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
					int pixelWidth = formatConvertedBitmap.PixelWidth;
					int pixelHeight = formatConvertedBitmap.PixelHeight;
					int num2 = pixelWidth * 4;
					byte[] array = new byte[pixelHeight * num2];
					formatConvertedBitmap.CopyPixels(array, num2, 0);
					for (int j = 0; j < array.Length; j += 4)
					{
						int num3 = array[j];
						int num4 = array[j + 1];
						int num5 = array[j + 2];
						int num6 = array[j + 3];
						RgbToHsl((byte)num5, (byte)num4, (byte)num3, out var h2, out var s2, out var l2);
						double h3 = LerpAngle(h2, h, num);
						double s3 = s2 * (1.0 - num) + s * num;
						double l3 = l2;
						RgbFromHsl(h3, s3, l3, out var r, out var g, out var b);
						array[j] = b;
						array[j + 1] = g;
						array[j + 2] = r;
						array[j + 3] = (byte)num6;
					}
					WriteableBitmap writeableBitmap = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
					writeableBitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num2, 0);
					writeableBitmap.Freeze();
					list.Add(writeableBitmap);
				}
				catch
				{
					list.Add(imageSource);
				}
			}
			else if (imageSource == null)
			{
				WriteableBitmap writeableBitmap2 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Bgra32, null);
				try
				{
					writeableBitmap2.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0);
				}
				catch
				{
				}
				try
				{
					writeableBitmap2.Freeze();
				}
				catch
				{
				}
				list.Add(writeableBitmap2);
			}
			else
			{
				list.Add(imageSource);
			}
		}
		return list.ToArray();
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
			if (imageSource is BitmapSource source)
			{
				try
				{
					FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
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
					WriteableBitmap writeableBitmap = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
					writeableBitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num, 0);
					writeableBitmap.Freeze();
					list.Add(writeableBitmap);
				}
				catch
				{
					if (imageSource != null)
					{
						list.Add(imageSource);
						continue;
					}
					WriteableBitmap writeableBitmap2 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null);
					try
					{
						writeableBitmap2.Lock();
						writeableBitmap2.AddDirtyRect(new Int32Rect(0, 0, 1, 1));
					}
					finally
					{
						try
						{
							writeableBitmap2.Unlock();
						}
						catch
						{
						}
					}
					writeableBitmap2.Freeze();
					list.Add(writeableBitmap2);
				}
				continue;
			}
			if (imageSource != null)
			{
				list.Add(imageSource);
				continue;
			}
			WriteableBitmap writeableBitmap3 = new WriteableBitmap(1, 1, 96.0, 96.0, PixelFormats.Pbgra32, null);
			try
			{
				writeableBitmap3.Lock();
				writeableBitmap3.AddDirtyRect(new Int32Rect(0, 0, 1, 1));
			}
			finally
			{
				try
				{
					writeableBitmap3.Unlock();
				}
				catch
				{
				}
			}
			writeableBitmap3.Freeze();
			list.Add(writeableBitmap3);
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

	private void UpdateParallaxTint()
	{
		parallaxTonedImages = CreateRgbReplacedImages(parallaxImages, backgroundTint);
		backgroundDirty = true;
	}

	private void UpdateGroundTint()
	{
		groundTonedImages = CreateExactRgbReplacedImages(groundImages, groundTint);
		backgroundDirty = true;
	}

	private void UpdateTileTint()
	{
		if (tileTint.A == byte.MaxValue && tileTint.R == 0 && tileTint.G == 0 && tileTint.B == 0)
		{
			tileTonedImages = CreateTwoToneTileImages(tileImages, Color.FromArgb(byte.MaxValue, 0, 0, 0), Color.FromArgb(byte.MaxValue, 0, 0, 0), tileTint);
		}
		else
		{
			RgbToHsl(tileTint.R, tileTint.G, tileTint.B, out var h, out var s, out var l);
			if (s < 0.06)
			{
				double l2 = Math.Max(0.0, l * 0.45);
				RgbFromHsl(h, s, l2, out var r, out var g, out var b);
				Color bgSecondary = Color.FromArgb(byte.MaxValue, r, g, b);
				Color outlineTint = Color.FromArgb(byte.MaxValue, 0, 0, 0);
				tileTonedImages = CreateTwoToneTileImages(tileImages, tileTint, bgSecondary, outlineTint);
			}
			else
			{
				tileTonedImages = CreateHslShiftedImages(tileImages, tileTint);
			}
		}
		ImageSource[] originals = sawFrame1Tiles;
		sawFrame1TilesTinted = CreateHslShiftedImages(originals, tileTint);
		originals = sawFrame2Tiles;
		sawFrame2TilesTinted = CreateHslShiftedImages(originals, tileTint);
		originals = smallSawFrame1Tiles;
		smallSawFrame1TilesTinted = CreateHslShiftedImages(originals, tileTint);
		originals = smallSawFrame2Tiles;
		smallSawFrame2TilesTinted = CreateHslShiftedImages(originals, tileTint);
		originals = largeSawFrame1Tiles;
		largeSawFrame1TilesTinted = CreateHslShiftedImages(originals, tileTint);
		originals = largeSawFrame2Tiles;
		largeSawFrame2TilesTinted = CreateHslShiftedImages(originals, tileTint);
		try
		{
			scaledTileCaches.Clear();
		}
		catch
		{
		}
		try
		{
			RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
			Redraw();
		}
		try
		{
			PopulateTilesPanel();
		}
		catch
		{
		}
	}

	private ImageSource? CreatePlayerReplacedFromBaseAndToned(ImageSource? baseSrc, ImageSource? tonedSrc, Color tint)
	{
		if (baseSrc == null && tonedSrc == null)
		{
			return null;
		}
		try
		{
			BitmapSource bitmapSource = baseSrc as BitmapSource;
			BitmapSource bitmapSource2 = (tonedSrc as BitmapSource) ?? bitmapSource;
			if (bitmapSource2 == null)
			{
				return baseSrc;
			}
			FormatConvertedBitmap formatConvertedBitmap = ((bitmapSource != null) ? new FormatConvertedBitmap(bitmapSource, PixelFormats.Bgra32, null, 0.0) : null);
			FormatConvertedBitmap formatConvertedBitmap2 = new FormatConvertedBitmap(bitmapSource2, PixelFormats.Bgra32, null, 0.0);
			int pixelWidth = formatConvertedBitmap2.PixelWidth;
			int pixelHeight = formatConvertedBitmap2.PixelHeight;
			if (pixelWidth <= 0 || pixelHeight <= 0)
			{
				return tonedSrc;
			}
			int num = pixelWidth * 4;
			byte[] array = new byte[pixelHeight * num];
			byte[] array2 = new byte[pixelHeight * num];
			formatConvertedBitmap?.CopyPixels(array, num, 0);
			formatConvertedBitmap2.CopyPixels(array2, num, 0);
			byte r = tint.R;
			byte g = tint.G;
			byte b = tint.B;
			for (int i = 0; i + 3 < array2.Length; i += 4)
			{
				byte b2 = ((formatConvertedBitmap != null) ? array[i + 3] : array2[i + 3]);
				byte b3 = ((formatConvertedBitmap != null) ? array[i] : array2[i]);
				byte b4 = ((formatConvertedBitmap != null) ? array[i + 1] : array2[i + 1]);
				byte b5 = ((formatConvertedBitmap != null) ? array[i + 2] : array2[i + 2]);
				if (b2 != 0)
				{
					bool flag = b5 == byte.MaxValue && b4 == 50 && b3 == 43;
					bool flag2 = b5 == 90 && b4 == 206 && b3 == 82;
					if (flag || flag2)
					{
						array2[i] = b;
						array2[i + 1] = g;
						array2[i + 2] = r;
					}
				}
			}
			WriteableBitmap writeableBitmap = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap2.DpiX, formatConvertedBitmap2.DpiY, PixelFormats.Bgra32, null);
			writeableBitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array2, num, 0);
			writeableBitmap.Freeze();
			return writeableBitmap;
		}
		catch
		{
			return tonedSrc ?? baseSrc;
		}
	}

	private bool IsPlayerReplacementTile(int tileIdx)
	{
		return (tileIdx >= 12 && tileIdx <= 15) || (tileIdx >= 19 && tileIdx <= 20) || (tileIdx >= 128 && tileIdx <= 129) || (tileIdx >= 132 && tileIdx <= 135);
	}

	private void PopulateTilesPanel()
	{
		if (TilesPanel == null)
		{
			return;
		}
		TilesPanel.Items.Clear();
		if (tileImages == null)
		{
			return;
		}
		int[] array;
		if (tileboardPosition == "TOP" || tileboardPosition == "BOTTOM")
		{
			array = new int[256];
			for (int i = 0; i < 256; i++)
			{
				int num = i / 16;
				int num2 = i % 16;
				array[num2 * 16 + num] = i;
			}
		}
		else
		{
			array = Enumerable.Range(0, 256).ToArray();
		}
		int[] array2 = array;
		foreach (int num3 in array2)
		{
			if (num3 >= tileImages.Length)
			{
				continue;
			}
			ImageSource imageSource = tileImages[num3];
			ImageSource imageSource2 = null;
			bool flag = IsPlayerReplacementTile(num3);
			ImageSource imageSource3 = ((num3 < tileImages.Length) ? tileImages[num3] : imageSource);
			ImageSource imageSource4 = ((tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[num3] : null);
			imageSource2 = ((!flag) ? (imageSource4 ?? imageSource) : ((!playerTintEnabled || imageSource3 == null) ? (imageSource4 ?? imageSource3) : CreatePlayerReplacedFromBaseAndToned(imageSource3, imageSource4 ?? imageSource3, playerTint)));
			if ((num3 == 34 || num3 == 36) && imageSource2 == null)
			{
				Debug.WriteLine($"ERROR: Tile {num3} has NULL palette source! Original: {imageSource?.GetType().Name}, Toned: {((tileTonedImages == null || num3 >= tileTonedImages.Length) ? "N/A" : tileTonedImages[num3]?.GetType().Name)}");
			}
			Image img = new Image
			{
				Source = imageSource2,
				Width = paletteTileSize,
				Height = paletteTileSize,
				Stretch = Stretch.Fill,
				Tag = num3
			};
			RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
			img.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				int item = (int)((Image)s).Tag;
				HashSet<int> hashSet = null;
				try
				{
					if (selectionSet != null && selectionSet.Count > 0)
					{
						hashSet = new HashSet<int>(selectionSet);
					}
				}
				catch
				{
					hashSet = null;
				}
				if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && selectedSprite >= 0)
				{
					selectedTile = item;
					selectedTiles = new List<int> { item };
					selectionWidth = 1;
					selectionHeight = 1;
					tilesLayerActive = true;
					spritesLayerActive = true;
					if (StatusText != null)
					{
						StatusText.Text = $"Selected tile {selectedTile} + sprite {selectedSprite} (both layers active)";
					}
					UpdatePaletteHighlight();
				}
				else
				{
					isSelectingMultipleTiles = true;
					tileSelectionStart = e.GetPosition(TilesPanel);
					selectedTile = item;
					selectedTiles = new List<int> { item };
					selectionWidth = 1;
					selectionHeight = 1;
					selectedSprite = -1;
					tilesLayerActive = true;
					spritesLayerActive = false;
					if (StatusText != null)
					{
						StatusText.Text = "Selected tile " + selectedTile;
					}
					UpdatePaletteHighlight();
					((Image)s).CaptureMouse();
				}
				img.MouseRightButtonDown += delegate(object obj4, MouseButtonEventArgs e2)
				{
					int item2 = (selectedTile = (int)((Image)obj4).Tag);
					selectedTiles = new List<int> { item2 };
					selectionWidth = 1;
					selectionHeight = 1;
					selectedSprite = -1;
					tilesLayerActive = true;
					spritesLayerActive = false;
					try
					{
						if (PlaceTool != null)
						{
							PlaceTool.IsChecked = true;
						}
					}
					catch
					{
					}
					try
					{
						if (DrawTileButton != null)
						{
							DrawTileButton.IsChecked = true;
						}
					}
					catch
					{
					}
					UpdatePaletteHighlight();
					try
					{
						if (CanvasHost != null)
						{
							CanvasHost.Focus();
						}
					}
					catch
					{
					}
					e2.Handled = true;
				};
				try
				{
					if (hashSet != null)
					{
						selectionSet = hashSet;
						try
						{
							UpdateSelectionVisuals(selX, selY, selW, selH);
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
			};
			img.MouseMove += delegate(object s, System.Windows.Input.MouseEventArgs e)
			{
				if (isSelectingMultipleTiles && tileSelectionStart.HasValue)
				{
					Point position = e.GetPosition(TilesPanel);
					UpdateTileSelection(tileSelectionStart.Value, position);
					UpdatePaletteHighlight();
				}
			};
			img.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
			{
				isSelectingMultipleTiles = false;
				tileSelectionStart = null;
				((Image)s).ReleaseMouseCapture();
			};
			img.MouseEnter += delegate(object s, System.Windows.Input.MouseEventArgs e)
			{
				int num4 = (int)((Image)s).Tag;
				if (TileIdIndicator != null)
				{
					TileIdIndicator.Text = $"0x{num4:X2}";
				}
				if (TileSelectedPreviewImage != null && tileImages != null && num4 >= 0 && num4 < tileImages.Length)
				{
					TileSelectedPreviewImage.Source = tileImages[num4];
					try
					{
						RenderOptions.SetBitmapScalingMode(TileSelectedPreviewImage, BitmapScalingMode.NearestNeighbor);
					}
					catch
					{
					}
				}
			};
			img.MouseLeave += delegate
			{
				if (TileIdIndicator != null && selectedTile >= 0)
				{
					TileIdIndicator.Text = $"0x{selectedTile:X2}";
				}
				else if (TileIdIndicator != null)
				{
					TileIdIndicator.Text = "";
				}
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
			Border newItem = new Border
			{
				Child = img,
				Margin = new Thickness(0.0),
				Padding = new Thickness(0.0),
				BorderBrush = Brushes.Transparent,
				BorderThickness = new Thickness(0.0)
			};
			TilesPanel.Items.Add(newItem);
		}
	}

	private void PopulateSpritesPanel()
	{
		if (SpritesPanel == null)
		{
			return;
		}
		SpritesPanel.Items.Clear();
		if (spriteImages == null)
		{
			return;
		}
		int num = 0;
		ImageSource[] array = spriteImages;
		foreach (ImageSource source in array)
		{
			Image image = new Image
			{
				Source = source,
				Width = paletteSpriteSize,
				Height = paletteSpriteSize,
				Stretch = Stretch.Fill,
				Tag = num
			};
			RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
			image.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				int item = (int)((Image)s).Tag;
				if (lockSpritesToSet && disabledSprites.Contains(item))
				{
					e.Handled = true;
				}
				else
				{
					if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && selectedTile >= 0 && selectedTiles.Count == 1)
					{
						selectedSprite = item;
						spritesLayerActive = true;
						tilesLayerActive = true;
						if (StatusText != null)
						{
							StatusText.Text = $"Selected tile {selectedTile} + sprite {selectedSprite} (both layers active)";
						}
					}
					else
					{
						selectedSprite = item;
						selectedTile = -1;
						selectedTiles = new List<int>();
						selectionWidth = 1;
						selectionHeight = 1;
						spritesLayerActive = true;
						tilesLayerActive = false;
						if (StatusText != null)
						{
							StatusText.Text = "Selected sprite " + selectedSprite;
						}
					}
					UpdatePaletteHighlight();
				}
			};
			image.MouseRightButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				int num2 = (int)((Image)s).Tag;
				selectedSprite = num2;
				selectedTile = -1;
				selectedTiles = new List<int>();
				selectionWidth = 1;
				selectionHeight = 1;
				spritesLayerActive = true;
				tilesLayerActive = false;
				try
				{
					if (PlaceTool != null)
					{
						PlaceTool.IsChecked = true;
					}
				}
				catch
				{
				}
				UpdatePaletteHighlight();
				try
				{
					if (CanvasHost != null)
					{
						CanvasHost.Focus();
					}
				}
				catch
				{
				}
				e.Handled = true;
			};
			image.MouseEnter += delegate(object s, System.Windows.Input.MouseEventArgs e)
			{
				int num2 = (int)((Image)s).Tag;
				if (SpriteIdIndicator != null)
				{
					SpriteIdIndicator.Text = $"0x{num2:X2}";
				}
				if (SpriteSelectedPreviewImage != null && spriteImages != null && num2 >= 0 && num2 < spriteImages.Length)
				{
					SpriteSelectedPreviewImage.Source = spriteImages[num2];
					try
					{
						RenderOptions.SetBitmapScalingMode(SpriteSelectedPreviewImage, BitmapScalingMode.NearestNeighbor);
					}
					catch
					{
					}
				}
			};
			image.MouseLeave += delegate
			{
				if (SpriteIdIndicator != null && selectedSprite >= 0)
				{
					SpriteIdIndicator.Text = $"0x{selectedSprite:X2}";
				}
				else if (SpriteIdIndicator != null)
				{
					SpriteIdIndicator.Text = "";
				}
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
			bool flag = lockSpritesToSet && disabledSprites.Contains(num);
			FrameworkElement child = image;
			if (flag)
			{
				Grid grid = new Grid();
				grid.Children.Add(image);
				Rectangle element = new Rectangle
				{
					Fill = new SolidColorBrush(Color.FromArgb(224, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
					IsHitTestVisible = false
				};
				grid.Children.Add(element);
				TextBlock element2 = new TextBlock
				{
					Text = "X",
					FontWeight = FontWeights.Bold,
					Foreground = Brushes.Black,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center,
					IsHitTestVisible = false
				};
				grid.Children.Add(element2);
				child = grid;
				image.IsEnabled = false;
			}
			Border newItem = new Border
			{
				Child = child,
				Margin = new Thickness(0.0),
				Padding = new Thickness(0.0),
				BorderBrush = ((num == selectedSprite) ? Brushes.Yellow : Brushes.Transparent),
				BorderThickness = ((num == selectedSprite) ? new Thickness(2.0) : new Thickness(0.0)),
				IsEnabled = !flag
			};
			SpritesPanel.Items.Add(newItem);
			num++;
		}
	}

	private void UpdateTileSelection(Point start, Point end)
	{
		if (TilesPanel == null || tileImages == null)
		{
			return;
		}
		double num = TilesPanel.ActualWidth / 16.0;
		int num2 = (int)Math.Ceiling((double)tileImages.Length / 16.0);
		double num3 = TilesPanel.ActualHeight / (double)num2;
		if (num3 <= 0.0 || double.IsNaN(num3) || double.IsInfinity(num3))
		{
			num3 = paletteTileSize + 2;
		}
		int val = Math.Max(0, Math.Min((int)(start.X / num), 15));
		int val2 = Math.Max(0, (int)(start.Y / num3));
		int val3 = Math.Max(0, Math.Min((int)(end.X / num), 15));
		int val4 = Math.Max(0, (int)(end.Y / num3));
		int num4 = Math.Min(val2, val4);
		int num5 = Math.Max(val2, val4);
		int num6 = Math.Min(val, val3);
		int num7 = Math.Max(val, val3);
		selectedTiles.Clear();
		selectionWidth = num7 - num6 + 1;
		selectionHeight = num5 - num4 + 1;
		for (int i = num4; i <= num5; i++)
		{
			for (int j = num6; j <= num7; j++)
			{
				int num8 = i * 16 + j;
				if (num8 >= 0 && num8 < tileImages.Length)
				{
					selectedTiles.Add(num8);
				}
			}
		}
		if (selectedTiles.Count <= 0)
		{
			return;
		}
		selectedTile = selectedTiles[0];
		if (TileSelectedPreviewImage != null && tileImages != null && selectedTile >= 0 && selectedTile < tileImages.Length)
		{
			TileSelectedPreviewImage.Source = tileImages[selectedTile];
			try
			{
				RenderOptions.SetBitmapScalingMode(TileSelectedPreviewImage, BitmapScalingMode.NearestNeighbor);
			}
			catch
			{
			}
		}
		if (StatusText != null)
		{
			if (selectedTiles.Count == 1)
			{
				StatusText.Text = $"Selected tile {selectedTile}";
				return;
			}
			StatusText.Text = $"Selected {selectedTiles.Count} tiles ({selectionWidth}x{selectionHeight})";
		}
	}

	private void UpdatePaletteHighlight()
	{
		if (lastKnownSelectedTile != selectedTile || lastKnownSelectedSprite != selectedSprite)
		{
			CancelPolygon();
		}
		if (TilesPanel != null)
		{
			for (int i = 0; i < TilesPanel.Items.Count; i++)
			{
				if (!(TilesPanel.Items[i] is Border border))
				{
					continue;
				}
				int num = -1;
				try
				{
					if (border.Child is Image { Tag: var tag } && tag is int num2)
					{
						num = num2;
					}
				}
				catch
				{
				}
				bool flag = num >= 0 && selectedTiles.Contains(num);
				border.BorderBrush = (flag ? Brushes.Yellow : Brushes.Transparent);
				border.BorderThickness = (flag ? new Thickness(2.0) : new Thickness(0.0));
			}
		}
		UpdateIdIndicators();
		if (SpritesPanel != null)
		{
			for (int j = 0; j < SpritesPanel.Items.Count; j++)
			{
				if (SpritesPanel.Items[j] is Border border2)
				{
					bool flag2 = j == selectedSprite && selectedSprite >= 0;
					border2.BorderBrush = (flag2 ? Brushes.Yellow : Brushes.Transparent);
					border2.BorderThickness = (flag2 ? new Thickness(2.0) : new Thickness(0.0));
				}
			}
		}
		if (TilesLabelBorder != null)
		{
			TilesLabelBorder.BorderBrush = (tilesLayerActive ? Brushes.Yellow : Brushes.Transparent);
		}
		if (SpritesLabelBorder != null)
		{
			SpritesLabelBorder.BorderBrush = (spritesLayerActive ? Brushes.Yellow : Brushes.Transparent);
		}
		lastKnownSelectedTile = selectedTile;
		lastKnownSelectedSprite = selectedSprite;
	}

	private void UpdateIdIndicators()
	{
		if (TileIdIndicator != null)
		{
			if (selectedTile >= 0)
			{
				TileIdIndicator.Text = $"0x{selectedTile:X2}";
			}
			else
			{
				TileIdIndicator.Text = "";
			}
			if (TileSelectedPreviewImage != null)
			{
				if (selectedTile >= 0 && tileImages != null && selectedTile < tileImages.Length)
				{
					TileSelectedPreviewImage.Source = tileImages[selectedTile];
					try
					{
						RenderOptions.SetBitmapScalingMode(TileSelectedPreviewImage, BitmapScalingMode.NearestNeighbor);
					}
					catch
					{
					}
				}
				else
				{
					TileSelectedPreviewImage.Source = null;
				}
			}
		}
		if (SpriteIdIndicator == null)
		{
			return;
		}
		if (selectedSprite >= 0)
		{
			SpriteIdIndicator.Text = $"0x{selectedSprite:X2}";
		}
		else
		{
			SpriteIdIndicator.Text = "";
		}
		if (SpriteSelectedPreviewImage == null)
		{
			return;
		}
		if (selectedSprite >= 0 && spriteImages != null && selectedSprite < spriteImages.Length)
		{
			SpriteSelectedPreviewImage.Source = spriteImages[selectedSprite];
			try
			{
				RenderOptions.SetBitmapScalingMode(SpriteSelectedPreviewImage, BitmapScalingMode.NearestNeighbor);
				return;
			}
			catch
			{
				return;
			}
		}
		SpriteSelectedPreviewImage.Source = null;
	}

	private void TilesLabel_Click(object sender, MouseButtonEventArgs e)
	{
		if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
		{
			tilesLayerActive = !tilesLayerActive;
			if (!tilesLayerActive && !spritesLayerActive)
			{
				spritesLayerActive = true;
			}
			UpdatePaletteHighlight();
			string text = (tilesLayerActive ? "TILES layer enabled" : "TILES layer disabled");
			if (StatusText != null)
			{
				StatusText.Text = text;
			}
			return;
		}
		tilesLayerActive = true;
		spritesLayerActive = false;
		if (selectedTile < 0)
		{
			selectedTile = 0;
		}
		selectedSprite = -1;
		UpdatePaletteHighlight();
		if (StatusText != null)
		{
			StatusText.Text = "Switched to TILES layer";
		}
	}

	private void SpritesLabel_Click(object sender, MouseButtonEventArgs e)
	{
		if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
		{
			spritesLayerActive = !spritesLayerActive;
			if (!tilesLayerActive && !spritesLayerActive)
			{
				tilesLayerActive = true;
			}
			UpdatePaletteHighlight();
			string text = (spritesLayerActive ? "SPRITES layer enabled" : "SPRITES layer disabled");
			if (StatusText != null)
			{
				StatusText.Text = text;
			}
			return;
		}
		spritesLayerActive = true;
		tilesLayerActive = false;
		if (selectedSprite < 0)
		{
			selectedSprite = 0;
		}
		selectedTile = -1;
		UpdatePaletteHighlight();
		if (StatusText != null)
		{
			StatusText.Text = "Switched to SPRITES layer";
		}
	}

	private void UpdateTileHighlight()
	{
		UpdatePaletteHighlight();
	}

	private void UpdateTileHighlight2()
	{
		if (TilesPanel == null)
		{
			return;
		}
		for (int i = 0; i < TilesPanel.Items.Count; i++)
		{
			if (TilesPanel.Items[i] is Border border)
			{
				border.BorderBrush = ((i == selectedTile) ? Brushes.Yellow : Brushes.Transparent);
				border.BorderThickness = ((i == selectedTile) ? new Thickness(2.0) : new Thickness(0.0));
			}
		}
	}

	private void DrawMap()
	{
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		double num2 = mapViewportPadding;
		double num3 = (double)(mapWidth * 16) * num;
		double num4 = 0.0;
		if (groundBitmap != null && groundImages != null && groundImages.Length != 0)
		{
			DpiScale dpi = VisualTreeHelper.GetDpi(this);
			num4 = (double)groundBitmap.PixelHeight / dpi.DpiScaleY * num;
		}
		double num5 = (double)(mapHeight * 16) * num + num4 - 64.0 * num;
		double num6 = num3 + num2 * 2.0;
		double num7 = num5 + num2 * 2.0;
		double num8 = num6;
		double num9 = num7;
		if (MapScrollViewer != null)
		{
			double num10 = SafeViewportWidth();
			double num11 = SafeViewportHeight();
			if (!double.IsNaN(num10) && num10 > num8)
			{
				num8 = num10;
			}
			if (!double.IsNaN(num11) && num11 > num9)
			{
				num9 = num11;
			}
		}
		DpiScale dpi2 = VisualTreeHelper.GetDpi(this);
		int num12 = Math.Max(1, (int)Math.Ceiling(num6 * dpi2.DpiScaleX));
		int num13 = Math.Max(1, (int)Math.Ceiling(num7 * dpi2.DpiScaleY));
		int pixelPaddedWidth = Math.Max(1, (int)Math.Ceiling(num8 * dpi2.DpiScaleX));
		int pixelPaddedHeight = Math.Max(1, (int)Math.Ceiling(num9 * dpi2.DpiScaleY));
		EnsureLayerBitmaps(num, num2, num3, num5, num6, num7, pixelPaddedWidth, pixelPaddedHeight);
		if (BackgroundImage != null && backgroundRtb != null)
		{
			BackgroundImage.Source = backgroundRtb;
			BackgroundImage.Width = num8;
			BackgroundImage.Height = num9;
			BackgroundImage.LayoutTransform = Transform.Identity;
		}
		if (ParallaxImage != null && parallaxRtb != null)
		{
			ParallaxImage.Source = parallaxRtb;
			ParallaxImage.Width = num8;
			ParallaxImage.Height = num9;
			ParallaxImage.RenderTransform = parallaxTransform;
			ParallaxImage.Visibility = ((hideBackground || useFastZoom) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (GroundImage != null && groundRtb != null)
		{
			GroundImage.Source = groundRtb;
			GroundImage.Width = num8;
			GroundImage.Height = num9;
			GroundImage.LayoutTransform = Transform.Identity;
			GroundImage.Visibility = ((hideGround || useFastZoom) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (TilesImage != null && tilesWb != null)
		{
			TilesImage.Source = tilesWb;
			TilesImage.Width = num8;
			TilesImage.Height = num9;
			TilesImage.LayoutTransform = Transform.Identity;
		}
		if (SpritesImage != null && spritesWb != null)
		{
			SpritesImage.Source = spritesWb;
			SpritesImage.Width = num8;
			SpritesImage.Height = num9;
			SpritesImage.LayoutTransform = Transform.Identity;
		}
		if (PortalsImage != null && portalsWb != null)
		{
			PortalsImage.Source = portalsWb;
			PortalsImage.Width = num8;
			PortalsImage.Height = num9;
			PortalsImage.LayoutTransform = Transform.Identity;
		}
		if (GridImage != null && gridRtb != null)
		{
			GridImage.Source = gridRtb;
			GridImage.Width = num8;
			GridImage.Height = num9;
			GridImage.LayoutTransform = Transform.Identity;
		}
		if (CanvasHost != null)
		{
			CanvasHost.Width = num8;
			CanvasHost.Height = num9;
			CanvasHost.LayoutTransform = Transform.Identity;
		}
		try
		{
			UpdateIncompatibleOverlay();
		}
		catch
		{
		}
		try
		{
			UpdatePlayerPathOverlay();
		}
		catch
		{
		}
		try
		{
			UpdateSpawnScrollOverlay();
		}
		catch
		{
		}
	}

	private void Redraw()
	{
		DrawMap();
	}

	private void RedrawViewportOnly()
	{
		DrawMap();
	}

	private void UpdateSpawnScrollOverlay()
	{
		try
		{
			if (CanvasHost == null)
			{
				return;
			}
			if (spawnYOverlayMarker != null)
			{
				try
				{
					CanvasHost.Children.Remove(spawnYOverlayMarker);
				}
				catch
				{
				}
				spawnYOverlayMarker = null;
			}
			if (cameraYOverlayMarker != null)
			{
				try
				{
					CanvasHost.Children.Remove(cameraYOverlayMarker);
				}
				catch
				{
				}
				cameraYOverlayMarker = null;
			}
			double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
			double num2 = mapViewportPadding;
			if (loadedSpawnYPositionHi.HasValue)
			{
				int num3 = loadedSpawnYPositionHi.Value & 0xFF;
				int num4 = (loadedSpawnYPositionLow.HasValue ? loadedSpawnYPositionLow.Value : 0) & 0xFF;
				int num5 = (num3 << 8) | num4;
				int num6 = (mapHeight - 15) * 16;
				int num7 = (num5 >> 8) + num6;
				double length = num2 + 0.0 * num;
				double length2 = num2 + (double)(num7 + 48) * num + gridRenderShiftY;
				double width = 16.0 * num;
				double height = 16.0 * num;
				Rectangle rectangle = new Rectangle
				{
					Width = width,
					Height = height,
					Fill = new SolidColorBrush(Color.FromArgb(96, 0, byte.MaxValue, 0)),
					Stroke = new SolidColorBrush(Color.FromArgb(192, 0, byte.MaxValue, 0)),
					StrokeThickness = Math.Max(1.0, 1.5 * num),
					IsHitTestVisible = true
				};
				rectangle.ToolTip = $"Spawn Y: Hi=0x{num3:X2} Lo=0x{num4:X2}\nTMX pixel Y={num7}";
				Canvas.SetLeft(rectangle, length);
				Canvas.SetTop(rectangle, length2);
				System.Windows.Controls.Panel.SetZIndex(rectangle, 2200);
				CanvasHost.Children.Add(rectangle);
				spawnYOverlayMarker = rectangle;
			}
			if (loadedScrollYPositionHi.HasValue)
			{
				int num8 = loadedScrollYPositionHi.Value & 0xFF;
				int num9 = (loadedScrollYPositionLow.HasValue ? loadedScrollYPositionLow.Value : 0) & 0xFF;
				int num10 = num8 * 240 + num9;
				int num11 = 719;
				int num12 = num11 - num10;
				int num13 = (mapHeight - 15) * 16;
				int num14 = Math.Max(0, num13 - num12);
				double length3 = num2 + 0.0 * num;
				double length4 = num2 + (double)(num14 + 48) * num + gridRenderShiftY;
				double width2 = 256.0 * num;
				double height2 = 240.0 * num;
				Rectangle rectangle2 = new Rectangle
				{
					Width = width2,
					Height = height2,
					Fill = Brushes.Transparent,
					Stroke = new SolidColorBrush(Color.FromArgb(160, byte.MaxValue, byte.MaxValue, 0)),
					StrokeThickness = Math.Max(1.0, 2.0 * num),
					StrokeDashArray = new DoubleCollection { 4.0, 2.0 },
					IsHitTestVisible = true
				};
				rectangle2.ToolTip = $"Camera Scroll Y: Hi=0x{num8:X2} Lo=0x{num9:X2}\nTMX camera top={num14}px";
				Canvas.SetLeft(rectangle2, length3);
				Canvas.SetTop(rectangle2, length4);
				System.Windows.Controls.Panel.SetZIndex(rectangle2, 2190);
				CanvasHost.Children.Add(rectangle2);
				cameraYOverlayMarker = rectangle2;
			}
		}
		catch
		{
		}
	}

	private static Color GetPathfinderBiasColor(double bias)
	{
		if (bias < 0.125)
		{
			return Color.FromArgb(224, byte.MaxValue, 68, 68);
		}
		if (bias < 0.375)
		{
			return Color.FromArgb(224, byte.MaxValue, 170, 0);
		}
		if (bias < 0.625)
		{
			return Color.FromArgb(224, byte.MaxValue, byte.MaxValue, 0);
		}
		if (bias < 0.875)
		{
			return Color.FromArgb(224, 68, 153, byte.MaxValue);
		}
		return Color.FromArgb(224, byte.MaxValue, 68, byte.MaxValue);
	}

	private void UpdatePlayerPathOverlay()
	{
		try
		{
			if (CanvasHost == null)
			{
				return;
			}
			if (playerPathPolyline != null)
			{
				try
				{
					CanvasHost.Children.Remove(playerPathPolyline);
				}
				catch
				{
				}
				playerPathPolyline = null;
			}
			if (playerPath2Polyline != null)
			{
				try
				{
					CanvasHost.Children.Remove(playerPath2Polyline);
				}
				catch
				{
				}
				playerPath2Polyline = null;
			}
			foreach (UIElement pathfinderPathPolyline in pathfinderPathPolylines)
			{
				try
				{
					CanvasHost.Children.Remove(pathfinderPathPolyline);
				}
				catch
				{
				}
			}
			pathfinderPathPolylines.Clear();
			foreach (UIElement attemptedPathPolyline in attemptedPathPolylines)
			{
				try
				{
					CanvasHost.Children.Remove(attemptedPathPolyline);
				}
				catch
				{
				}
			}
			attemptedPathPolylines.Clear();
			foreach (UIElement mesenPathPolyline in mesenPathPolylines)
			{
				try
				{
					CanvasHost.Children.Remove(mesenPathPolyline);
				}
				catch
				{
				}
			}
			mesenPathPolylines.Clear();
			double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
			double num2 = mapViewportPadding;
			if (attemptedPaths.Count > 0)
			{
				StreamGeometry streamGeometry = new StreamGeometry();
				using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
				{
					foreach (List<(int, int)> attemptedPath in attemptedPaths)
					{
						if (attemptedPath != null && attemptedPath.Count >= 2)
						{
							(int, int) tuple = attemptedPath[0];
							streamGeometryContext.BeginFigure(new Point(num2 + (double)tuple.Item1 * num, num2 + (double)(tuple.Item2 + 48) * num + gridRenderShiftY), isFilled: false, isClosed: false);
							for (int i = 1; i < attemptedPath.Count; i++)
							{
								(int, int) tuple2 = attemptedPath[i];
								streamGeometryContext.LineTo(new Point(num2 + (double)tuple2.Item1 * num, num2 + (double)(tuple2.Item2 + 48) * num + gridRenderShiftY), isStroked: true, isSmoothJoin: false);
							}
						}
					}
				}
				streamGeometry.Freeze();
				System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
				{
					Data = streamGeometry,
					Stroke = new SolidColorBrush(Color.FromArgb(160, byte.MaxValue, 102, 51)),
					StrokeThickness = Math.Max(1.0, 1.5 * num),
					IsHitTestVisible = false
				};
				System.Windows.Controls.Panel.SetZIndex(path, 2050);
				CanvasHost.Children.Add(path);
				attemptedPathPolylines.Add(path);
			}
			if (mesenPaths.Count > 0)
			{
				StreamGeometry streamGeometry2 = new StreamGeometry();
				using (StreamGeometryContext streamGeometryContext2 = streamGeometry2.Open())
				{
					double num3 = num * num;
					foreach (List<(int, int)> mesenPath in mesenPaths)
					{
						if (mesenPath == null || mesenPath.Count < 2)
						{
							continue;
						}
						(int, int) tuple3 = mesenPath[0];
						double num4 = num2 + (double)tuple3.Item1 * num;
						double num5 = num2 + (double)(tuple3.Item2 + 48) * num + gridRenderShiftY;
						streamGeometryContext2.BeginFigure(new Point(num4, num5), isFilled: false, isClosed: false);
						for (int j = 1; j < mesenPath.Count; j++)
						{
							(int, int) tuple4 = mesenPath[j];
							double num6 = num2 + (double)tuple4.Item1 * num;
							double num7 = num2 + (double)(tuple4.Item2 + 48) * num + gridRenderShiftY;
							double num8 = num6 - num4;
							double num9 = num7 - num5;
							if (num8 * num8 + num9 * num9 >= num3)
							{
								streamGeometryContext2.LineTo(new Point(num6, num7), isStroked: true, isSmoothJoin: false);
								num4 = num6;
								num5 = num7;
							}
						}
						(int, int) tuple5 = mesenPath[mesenPath.Count - 1];
						double x = num2 + (double)tuple5.Item1 * num;
						double y = num2 + (double)(tuple5.Item2 + 48) * num + gridRenderShiftY;
						streamGeometryContext2.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: false);
					}
				}
				streamGeometry2.Freeze();
				System.Windows.Shapes.Path path2 = new System.Windows.Shapes.Path
				{
					Data = streamGeometry2,
					Stroke = new SolidColorBrush(Color.FromArgb(224, 0, byte.MaxValue, 102)),
					StrokeThickness = Math.Max(1.0, 2.0 * num),
					IsHitTestVisible = false
				};
				System.Windows.Controls.Panel.SetZIndex(path2, 2100);
				CanvasHost.Children.Add(path2);
				mesenPathPolylines.Add(path2);
			}
			foreach (var pathfinderPath in pathfinderPaths)
			{
				if (pathfinderPath.points == null || pathfinderPath.points.Count == 0)
				{
					continue;
				}
				StreamGeometry streamGeometry3 = new StreamGeometry();
				using (StreamGeometryContext streamGeometryContext3 = streamGeometry3.Open())
				{
					double num10 = num * num;
					double num11 = 0.0;
					double num12 = 0.0;
					bool flag = false;
					foreach (var item in pathfinderPath.points)
					{
						double num13 = num2 + (double)item.x * num;
						double num14 = num2 + (double)(item.y + 48) * num + gridRenderShiftY;
						if (!flag)
						{
							streamGeometryContext3.BeginFigure(new Point(num13, num14), isFilled: false, isClosed: false);
							num11 = num13;
							num12 = num14;
							flag = true;
							continue;
						}
						double num15 = num13 - num11;
						double num16 = num14 - num12;
						if (num15 * num15 + num16 * num16 >= num10)
						{
							streamGeometryContext3.LineTo(new Point(num13, num14), isStroked: true, isSmoothJoin: false);
							num11 = num13;
							num12 = num14;
						}
					}
					if (flag && pathfinderPath.points.Count > 1)
					{
						(int, int) tuple6 = pathfinderPath.points[pathfinderPath.points.Count - 1];
						double x2 = num2 + (double)tuple6.Item1 * num;
						double y2 = num2 + (double)(tuple6.Item2 + 48) * num + gridRenderShiftY;
						streamGeometryContext3.LineTo(new Point(x2, y2), isStroked: true, isSmoothJoin: false);
					}
				}
				streamGeometry3.Freeze();
				System.Windows.Shapes.Path path3 = new System.Windows.Shapes.Path
				{
					Data = streamGeometry3,
					Stroke = new SolidColorBrush(pathfinderPath.color),
					StrokeThickness = Math.Max(1.0, 2.0 * num),
					IsHitTestVisible = false
				};
				System.Windows.Controls.Panel.SetZIndex(path3, 2000);
				CanvasHost.Children.Add(path3);
				pathfinderPathPolylines.Add(path3);
			}
			foreach (var pathfinderPath2 in pathfinderPath2s)
			{
				if (pathfinderPath2.points == null || pathfinderPath2.points.Count == 0)
				{
					continue;
				}
				StreamGeometry streamGeometry4 = new StreamGeometry();
				using (StreamGeometryContext streamGeometryContext4 = streamGeometry4.Open())
				{
					double num17 = num * num;
					double num18 = 0.0;
					double num19 = 0.0;
					bool flag2 = false;
					foreach (var item2 in pathfinderPath2.points)
					{
						if (item2.x == -1 && item2.y == -1)
						{
							flag2 = false;
							continue;
						}
						double num20 = num2 + (double)item2.x * num;
						double num21 = num2 + (double)(item2.y + 48) * num + gridRenderShiftY;
						if (!flag2)
						{
							streamGeometryContext4.BeginFigure(new Point(num20, num21), isFilled: false, isClosed: false);
							num18 = num20;
							num19 = num21;
							flag2 = true;
							continue;
						}
						double num22 = num20 - num18;
						double num23 = num21 - num19;
						if (num22 * num22 + num23 * num23 >= num17)
						{
							streamGeometryContext4.LineTo(new Point(num20, num21), isStroked: true, isSmoothJoin: false);
							num18 = num20;
							num19 = num21;
						}
					}
				}
				streamGeometry4.Freeze();
				System.Windows.Shapes.Path path4 = new System.Windows.Shapes.Path
				{
					Data = streamGeometry4,
					Stroke = new SolidColorBrush(pathfinderPath2.color),
					StrokeThickness = Math.Max(1.0, 2.0 * num),
					IsHitTestVisible = false
				};
				System.Windows.Controls.Panel.SetZIndex(path4, 2000);
				CanvasHost.Children.Add(path4);
				pathfinderPathPolylines.Add(path4);
			}
			if (playerPathPoints == null || playerPathPoints.Count == 0)
			{
				return;
			}
			Polyline polyline = new Polyline
			{
				Stroke = new SolidColorBrush(Color.FromArgb(224, 144, 238, 144)),
				StrokeThickness = Math.Max(1.0, 2.0 * num),
				IsHitTestVisible = false
			};
			foreach (var playerPathPoint in playerPathPoints)
			{
				double x3 = num2 + (double)playerPathPoint.x * num;
				double y3 = num2 + (double)(playerPathPoint.y + 48) * num + gridRenderShiftY;
				polyline.Points.Add(new Point(x3, y3));
			}
			playerPathPolyline = polyline;
			System.Windows.Controls.Panel.SetZIndex(polyline, 2000);
			CanvasHost.Children.Add(polyline);
			if (playerPath2Points == null || playerPath2Points.Count <= 0)
			{
				return;
			}
			Polyline polyline2 = new Polyline
			{
				Stroke = new SolidColorBrush(Color.FromArgb(224, 135, 206, 235)),
				StrokeThickness = Math.Max(1.0, 2.0 * num),
				IsHitTestVisible = false
			};
			StreamGeometry streamGeometry5 = new StreamGeometry();
			using (StreamGeometryContext streamGeometryContext5 = streamGeometry5.Open())
			{
				bool flag3 = false;
				foreach (var playerPath2Point in playerPath2Points)
				{
					if (playerPath2Point.x == -1 && playerPath2Point.y == -1)
					{
						flag3 = false;
						continue;
					}
					double x4 = num2 + (double)playerPath2Point.x * num;
					double y4 = num2 + (double)(playerPath2Point.y + 48) * num + gridRenderShiftY;
					if (!flag3)
					{
						streamGeometryContext5.BeginFigure(new Point(x4, y4), isFilled: false, isClosed: false);
						flag3 = true;
					}
					else
					{
						streamGeometryContext5.LineTo(new Point(x4, y4), isStroked: true, isSmoothJoin: false);
					}
				}
			}
			streamGeometry5.Freeze();
			System.Windows.Shapes.Path element = new System.Windows.Shapes.Path
			{
				Data = streamGeometry5,
				Stroke = polyline2.Stroke,
				StrokeThickness = polyline2.StrokeThickness,
				IsHitTestVisible = false
			};
			System.Windows.Controls.Panel.SetZIndex(element, 2000);
			CanvasHost.Children.Add(element);
			playerPath2Polyline = polyline2;
		}
		catch
		{
		}
	}

	public void ShowPlayerPathFromSimulator(IEnumerable<(int x, int y)> pts)
	{
		try
		{
			base.Dispatcher?.BeginInvoke((Action)delegate
			{
				try
				{
					playerPathPoints = pts?.ToList() ?? new List<(int, int)>();
					playerPath2Points = new List<(int, int)>();
					UpdatePlayerPathOverlay();
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

	public void ShowPlayerPathsFromSimulator(IEnumerable<(int x, int y)> path1, IEnumerable<(int x, int y)> path2, bool dualMode)
	{
		try
		{
			base.Dispatcher?.BeginInvoke((Action)delegate
			{
				try
				{
					playerPathPoints = path1?.ToList() ?? new List<(int, int)>();
					playerPath2Points = path2?.ToList() ?? new List<(int, int)>();
					UpdatePlayerPathOverlay();
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

	public void ClearSimulatorPathOnly()
	{
		try
		{
			playerPathPoints.Clear();
			playerPath2Points.Clear();
			if (playerPathPolyline != null && CanvasHost != null)
			{
				try
				{
					CanvasHost.Children.Remove(playerPathPolyline);
				}
				catch
				{
				}
				playerPathPolyline = null;
			}
			if (playerPath2Polyline != null && CanvasHost != null)
			{
				try
				{
					CanvasHost.Children.Remove(playerPath2Polyline);
				}
				catch
				{
				}
				playerPath2Polyline = null;
			}
			try
			{
				if (playerDeathMarkerA != null && CanvasHost != null)
				{
					CanvasHost.Children.Remove(playerDeathMarkerA);
				}
			}
			catch
			{
			}
			try
			{
				if (playerDeathMarkerB != null && CanvasHost != null)
				{
					CanvasHost.Children.Remove(playerDeathMarkerB);
				}
			}
			catch
			{
			}
			playerDeathMarkerA = null;
			playerDeathMarkerB = null;
		}
		catch
		{
		}
	}

	public void ClearAttemptedPathOverlay()
	{
		try
		{
			attemptedPaths.Clear();
			foreach (UIElement attemptedPathPolyline in attemptedPathPolylines)
			{
				try
				{
					if (CanvasHost != null)
					{
						CanvasHost.Children.Remove(attemptedPathPolyline);
					}
				}
				catch
				{
				}
			}
			attemptedPathPolylines.Clear();
		}
		catch
		{
		}
	}

	public void LoadAndShowMesenTracePath(string traceCsvPath)
	{
		try
		{
			if (string.IsNullOrEmpty(traceCsvPath) || !File.Exists(traceCsvPath))
			{
				return;
			}
			List<List<(int x, int y)>> attempts = new List<List<(int, int)>>();
			List<(int, int)> list = new List<(int, int)>();
			string[] array = File.ReadAllLines(traceCsvPath);
			foreach (string text in array)
			{
				if (string.IsNullOrWhiteSpace(text))
				{
					continue;
				}
				if (text.StartsWith("#", StringComparison.Ordinal))
				{
					if (list.Count > 0)
					{
						attempts.Add(list);
						list = new List<(int, int)>();
					}
				}
				else
				{
					if (text.StartsWith("nes_y_offset", StringComparison.Ordinal) || text.StartsWith("rom_frame", StringComparison.Ordinal))
					{
						continue;
					}
					string[] array2 = text.Split(',');
					if (array2.Length >= 4 && long.TryParse(array2[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
					{
						int num = (int)result;
						if (int.TryParse(array2[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2) && num >= 0 && num <= 524288)
						{
							list.Add((num, result2));
						}
					}
				}
			}
			if (list.Count > 0)
			{
				attempts.Add(list);
			}
			base.Dispatcher?.BeginInvoke((Action)delegate
			{
				try
				{
					mesenPaths = attempts;
					UpdatePlayerPathOverlay();
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

	public void ClearPlayerPathOverlay()
	{
		try
		{
			ClearSimulatorPathOnly();
			ClearAttemptedPathOverlay();
			_attemptedPathsCleared = false;
			pathfinderPaths.Clear();
			pathfinderPath2s.Clear();
			foreach (UIElement pathfinderPathPolyline in pathfinderPathPolylines)
			{
				try
				{
					if (CanvasHost != null)
					{
						CanvasHost.Children.Remove(pathfinderPathPolyline);
					}
				}
				catch
				{
				}
			}
			pathfinderPathPolylines.Clear();
			mesenPaths.Clear();
			foreach (UIElement mesenPathPolyline in mesenPathPolylines)
			{
				try
				{
					if (CanvasHost != null)
					{
						CanvasHost.Children.Remove(mesenPathPolyline);
					}
				}
				catch
				{
				}
			}
			mesenPathPolylines.Clear();
		}
		catch
		{
		}
	}

	public void AddDeathMarker(int worldX_px, int worldY_px)
	{
		try
		{
			base.Dispatcher?.BeginInvoke((Action)delegate
			{
				try
				{
					if (CanvasHost != null)
					{
						try
						{
							if (playerDeathMarkerA != null)
							{
								CanvasHost.Children.Remove(playerDeathMarkerA);
							}
						}
						catch
						{
						}
						try
						{
							if (playerDeathMarkerB != null)
							{
								CanvasHost.Children.Remove(playerDeathMarkerB);
							}
						}
						catch
						{
						}
						playerDeathMarkerA = null;
						playerDeathMarkerB = null;
						double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
						double num2 = mapViewportPadding;
						double num3 = num2 + (double)worldX_px * num;
						double num4 = num2 + (double)(worldY_px + 48) * num + gridRenderShiftY;
						double num5 = Math.Max(4.0, 8.0 * num);
						Line element = new Line
						{
							X1 = num3 - num5,
							Y1 = num4 - num5,
							X2 = num3 + num5,
							Y2 = num4 + num5,
							Stroke = Brushes.Red,
							StrokeThickness = Math.Max(1.0, 2.0 * num),
							IsHitTestVisible = false
						};
						Line element2 = new Line
						{
							X1 = num3 - num5,
							Y1 = num4 + num5,
							X2 = num3 + num5,
							Y2 = num4 - num5,
							Stroke = Brushes.Red,
							StrokeThickness = Math.Max(1.0, 2.0 * num),
							IsHitTestVisible = false
						};
						playerDeathMarkerA = element;
						playerDeathMarkerB = element2;
						System.Windows.Controls.Panel.SetZIndex(element, 2001);
						System.Windows.Controls.Panel.SetZIndex(element2, 2001);
						CanvasHost.Children.Add(element);
						CanvasHost.Children.Add(element2);
					}
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

	public void SetStartPosMarker(int? worldX_px, int? worldY_px)
	{
		try
		{
			base.Dispatcher?.BeginInvoke((Action)delegate
			{
				try
				{
					if (CanvasHost != null)
					{
						try
						{
							if (startPosMarker != null)
							{
								CanvasHost.Children.Remove(startPosMarker);
							}
						}
						catch
						{
						}
						startPosMarker = null;
						startPosMarkerX = null;
						startPosMarkerY = null;
						if (worldX_px.HasValue && worldY_px.HasValue)
						{
							double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
							double num2 = mapViewportPadding;
							startPosMarkerX = worldX_px.Value;
							startPosMarkerY = worldY_px.Value;
							if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
							{
								openFiles[currentFileIndex].StartPosX = worldX_px.Value;
								openFiles[currentFileIndex].StartPosY = worldY_px.Value;
							}
							double length = num2 + (double)worldX_px.Value * num;
							double length2 = num2 + (double)(worldY_px.Value + 48) * num + gridRenderShiftY;
							double num3 = 16.0 * num;
							Canvas canvas = new Canvas();
							Rectangle element = new Rectangle
							{
								Width = num3,
								Height = num3,
								Stroke = Brushes.LimeGreen,
								Fill = new SolidColorBrush(Color.FromArgb(80, 0, byte.MaxValue, 0)),
								StrokeThickness = Math.Max(2.0, 2.5 * num),
								IsHitTestVisible = false
							};
							TextBlock textBlock = new TextBlock
							{
								Text = "START POS",
								Foreground = Brushes.White,
								FontSize = Math.Max(9.0, 11.0 * num),
								FontWeight = FontWeights.Bold,
								IsHitTestVisible = false
							};
							textBlock.Effect = new DropShadowEffect
							{
								Color = Colors.Black,
								BlurRadius = 3.0,
								ShadowDepth = 0.0,
								Opacity = 1.0
							};
							Canvas.SetLeft(element, 0.0);
							Canvas.SetTop(element, 0.0);
							Canvas.SetLeft(textBlock, -5.0);
							Canvas.SetTop(textBlock, num3 + 2.0);
							canvas.Children.Add(element);
							canvas.Children.Add(textBlock);
							Canvas.SetLeft(canvas, length);
							Canvas.SetTop(canvas, length2);
							startPosMarker = canvas;
							System.Windows.Controls.Panel.SetZIndex(canvas, 2002);
							CanvasHost.Children.Add(canvas);
						}
					}
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

	private void StatusText_Copy_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (StatusText != null)
			{
				string text = StatusText.Text ?? string.Empty;
				if (!string.IsNullOrEmpty(text))
				{
					System.Windows.Clipboard.SetText(text);
				}
			}
		}
		catch
		{
		}
	}

	private unsafe void EnsureLayerBitmaps(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight)
	{
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		bool flag = cachedPixelWidth == pixelPaddedWidth && cachedPixelHeight == pixelPaddedHeight && Math.Abs(cachedScale - scale) > 1E-06;
		if (backgroundRtb == null || cachedPixelWidth != pixelPaddedWidth || cachedPixelHeight != pixelPaddedHeight || Math.Abs(cachedScale - scale) > 1E-06 || backgroundDirty)
		{
			BuildBackgroundBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi);
			if (!(useFastZoom && flag))
			{
				BuildParallaxBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi);
				BuildGroundBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi);
			}
			backgroundDirty = false;
		}
		if (gridRtb == null || cachedPixelWidth != pixelPaddedWidth || cachedPixelHeight != pixelPaddedHeight || Math.Abs(cachedScale - scale) > 1E-06 || gridDirty)
		{
			BuildGridBitmap(scale, pad, fullW, fullH, paddedFullW, paddedFullH, pixelPaddedWidth, pixelPaddedHeight, dpi);
			gridDirty = false;
		}
		bool flag2 = tilesWb == null || cachedPixelWidth != pixelPaddedWidth || cachedPixelHeight != pixelPaddedHeight || Math.Abs(cachedScale - scale) > 1E-06;
		if (flag2)
		{
			tilesWb = new WriteableBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
			cachedPixelWidth = pixelPaddedWidth;
			cachedPixelHeight = pixelPaddedHeight;
			cachedScale = scale;
			byte[] pixels = new byte[pixelPaddedHeight * tilesWb.BackBufferStride];
			tilesWb.WritePixels(new Int32Rect(0, 0, pixelPaddedWidth, pixelPaddedHeight), pixels, tilesWb.BackBufferStride, 0);
			RebuildAllTilesBitmapAsync(scale, pad);
		}
		if (!flag2)
		{
			return;
		}
		spritesWb = new WriteableBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
		byte[] pixels2 = new byte[pixelPaddedHeight * spritesWb.BackBufferStride];
		spritesWb.WritePixels(new Int32Rect(0, 0, pixelPaddedWidth, pixelPaddedHeight), pixels2, spritesWb.BackBufferStride, 0);
		portalsWb = new WriteableBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
		byte[] pixels3 = new byte[pixelPaddedHeight * portalsWb.BackBufferStride];
		portalsWb.WritePixels(new Int32Rect(0, 0, pixelPaddedWidth, pixelPaddedHeight), pixels3, portalsWb.BackBufferStride, 0);
		if (previewMode)
		{
			RebuildPortalsRegion(0, 0, mapWidth - 1, mapHeight - 1, scale, pad);
		}
		else
		{
			try
			{
				portalsWb.Lock();
				IntPtr backBuffer = portalsWb.BackBuffer;
				if (backBuffer != IntPtr.Zero)
				{
					int backBufferStride = portalsWb.BackBufferStride;
					int num = backBufferStride * cachedPixelHeight;
					byte* ptr = (byte*)backBuffer.ToPointer();
					for (int i = 0; i < num; i++)
					{
						ptr[i] = 0;
					}
				}
				portalsWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
			}
			finally
			{
				try
				{
					portalsWb.Unlock();
				}
				catch
				{
				}
			}
		}
		RebuildAllSpritesBitmap(scale, pad);
	}

	private void BuildBackgroundBitmap(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
	{
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			drawingContext.DrawRectangle(mapBackground, null, new Rect(pad, pad, fullW, fullH));
		}
		try
		{
			backgroundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
			backgroundRtb.Render(drawingVisual);
		}
		catch (COMException)
		{
			App.EnableSoftwareRendering();
			try
			{
				backgroundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
				backgroundRtb.Render(drawingVisual);
			}
			catch
			{
				backgroundRtb = null;
			}
		}
	}

	private BitmapSource? GetOrCreateSingleTinted(BitmapSource src, Color tint, bool useRgbReplace, int scaleKey)
	{
		try
		{
			uint num = (uint)((tint.A << 24) | (tint.R << 16) | (tint.G << 8) | tint.B);
			long key = ((long)scaleKey << 32) | num;
			Dictionary<long, BitmapSource> dictionary = (useRgbReplace ? parallaxTintCache : groundTintCache);
			if (dictionary.TryGetValue(key, out var value))
			{
				return value;
			}
			ImageSource[] array = null;
			array = ((!useRgbReplace) ? CreateHueShiftedImages(new ImageSource[1] { src }, tint) : CreateRgbReplacedImages(new ImageSource[1] { src }, tint));
			if (array != null && array.Length != 0 && array[0] is BitmapSource bitmapSource)
			{
				dictionary[key] = bitmapSource;
				return bitmapSource;
			}
		}
		catch
		{
		}
		return null;
	}

	private void BuildParallaxBitmap(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
	{
		if (hideBackground)
		{
			parallaxRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
			return;
		}
		if (parallaxBitmap == null)
		{
			parallaxRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
			return;
		}
		BitmapSource bitmapSource = parallaxBitmap;
		if (backgroundTint.A != 0)
		{
			BitmapSource orCreateSingleTinted = GetOrCreateSingleTinted(parallaxBitmap, backgroundTint, useRgbReplace: true, (int)Math.Round(scale * 100.0));
			if (orCreateSingleTinted != null)
			{
				bitmapSource = orCreateSingleTinted;
			}
		}
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			double width = (double)pixelPaddedWidth / dpi.DpiScaleX;
			double height = (double)pixelPaddedHeight / dpi.DpiScaleY;
			double num = (double)bitmapSource.PixelWidth / dpi.DpiScaleX * scale;
			double num2 = (double)bitmapSource.PixelHeight / dpi.DpiScaleY * scale;
			double offsetX = 0.0 - pad % num;
			double offsetY = 0.0 - pad % num2;
			BitmapSource image = App.EnsureUnfrozenForRender(bitmapSource) ?? bitmapSource;
			ImageBrush imageBrush = new ImageBrush(image)
			{
				TileMode = TileMode.Tile,
				ViewportUnits = BrushMappingMode.Absolute,
				Viewport = new Rect(0.0, 0.0, num, num2),
				Stretch = Stretch.Fill
			};
			imageBrush.Transform = new TranslateTransform(offsetX, offsetY);
			RenderOptions.SetBitmapScalingMode(drawingVisual, BitmapScalingMode.NearestNeighbor);
			double num3 = 0.0;
			if (groundBitmap != null && groundImages != null && groundImages.Length != 0)
			{
				num3 = (double)groundBitmap.PixelHeight / dpi.DpiScaleY * scale;
			}
			double val = (double)(mapHeight * 16) * scale + pad;
			if (num3 > 0.0)
			{
				double height2 = Math.Max(0.0, val);
				drawingContext.DrawRectangle(imageBrush, null, new Rect(0.0, 0.0, width, height2));
			}
			else
			{
				drawingContext.DrawRectangle(imageBrush, null, new Rect(0.0, 0.0, width, height));
			}
		}
		try
		{
			parallaxRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
			parallaxRtb.Render(drawingVisual);
		}
		catch (COMException)
		{
			App.EnableSoftwareRendering();
			try
			{
				parallaxRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
				parallaxRtb.Render(drawingVisual);
			}
			catch
			{
				parallaxRtb = null;
			}
		}
		double width2 = (double)pixelPaddedWidth / dpi.DpiScaleX;
		double num4 = (double)pixelPaddedHeight / dpi.DpiScaleY;
		double num5 = 0.0;
		try
		{
			if (groundBitmap != null && groundImages != null && groundImages.Length != 0)
			{
				num5 = (double)groundBitmap.PixelHeight / dpi.DpiScaleY * scale;
			}
		}
		catch
		{
		}
		double num6 = (double)(mapHeight * 16) * scale + pad;
		try
		{
			if (parallaxRtb == null)
			{
				return;
			}
			int pixelWidth = parallaxRtb.PixelWidth;
			int pixelHeight = parallaxRtb.PixelHeight;
			DpiScale dpiScale = dpi;
			int num7 = Math.Max(1, Math.Min(pixelHeight, (int)Math.Round(((num5 > 0.0) ? num6 : num4) * dpiScale.DpiScaleY)));
			int x = Math.Max(0, Math.Min(pixelWidth - 1, pixelWidth / 2));
			byte[] array = new byte[4];
			bool flag = true;
			for (int i = 1; i <= 3; i++)
			{
				int y = Math.Max(0, Math.Min(pixelHeight - 1, num7 * i / 4));
				try
				{
					parallaxRtb.CopyPixels(new Int32Rect(x, y, 1, 1), array, 4, 0);
				}
				catch
				{
					array[3] = 0;
				}
				if (array[3] != 0)
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				DrawingVisual drawingVisual2 = new DrawingVisual();
				using (DrawingContext drawingContext2 = drawingVisual2.RenderOpen())
				{
					double height3 = ((num5 > 0.0) ? Math.Max(0.0, num6) : num4);
					drawingContext2.DrawImage(bitmapSource, new Rect(0.0, 0.0, width2, height3));
				}
				RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpiScale.PixelsPerInchX, dpiScale.PixelsPerInchY, PixelFormats.Pbgra32);
				renderTargetBitmap.Render(drawingVisual2);
				parallaxRtb = renderTargetBitmap;
			}
		}
		catch
		{
		}
	}

	private void BuildGroundBitmap(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
	{
		if (hideGround)
		{
			groundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
			return;
		}
		if (groundBitmap == null || groundImages == null || groundImages.Length == 0)
		{
			groundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
			return;
		}
		BitmapSource bitmapSource = groundBitmap;
		if (groundTint.A != 0 && (groundTint.R != byte.MaxValue || groundTint.G != byte.MaxValue || groundTint.B != byte.MaxValue))
		{
			BitmapSource orCreateSingleTinted = GetOrCreateSingleTinted(groundBitmap, groundTint, useRgbReplace: false, (int)Math.Round(scale * 100.0));
			if (orCreateSingleTinted != null)
			{
				bitmapSource = orCreateSingleTinted;
			}
		}
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			double num = (double)(mapHeight * 16) * scale + pad;
			double width = (double)pixelPaddedWidth / dpi.DpiScaleX;
			double num2 = (double)bitmapSource.PixelWidth / dpi.DpiScaleX * scale;
			double height = (double)bitmapSource.PixelHeight / dpi.DpiScaleY * scale;
			BitmapSource image = App.EnsureUnfrozenForRender(bitmapSource) ?? bitmapSource;
			ImageBrush imageBrush = new ImageBrush(image)
			{
				TileMode = TileMode.Tile,
				ViewportUnits = BrushMappingMode.Absolute,
				Viewport = new Rect(0.0, 0.0, num2, height),
				Stretch = Stretch.Fill
			};
			double offsetX = 0.0 - pad % num2;
			imageBrush.Transform = new TranslateTransform(offsetX, num);
			drawingContext.DrawRectangle(imageBrush, null, new Rect(0.0, num, width, height));
		}
		try
		{
			groundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
			groundRtb.Render(drawingVisual);
		}
		catch (COMException)
		{
			App.EnableSoftwareRendering();
			try
			{
				groundRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
				groundRtb.Render(drawingVisual);
			}
			catch
			{
				groundRtb = null;
			}
		}
	}

	private async void RebuildParallaxGroundDeferredAsync(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
	{
		if (hideBackground && hideGround)
		{
			return;
		}
		await Task.Delay(300);
		Debug.WriteLine("RebuildParallaxGroundDeferredAsync: Starting chunked rebuild");
		(ImageSource?[] images, ImageSource?[] tonedImages, BitmapSource bitmap) parallaxData = (images: parallaxImages, tonedImages: parallaxTonedImages, bitmap: parallaxBitmap);
		(ImageSource?[] images, ImageSource?[] tonedImages, BitmapSource bitmap, int rows) groundData = (images: groundImages, tonedImages: groundTonedImages, bitmap: groundBitmap, rows: groundTileRows);
		int capturedMapHeight = mapHeight;
		int capturedMapWidth = mapWidth;
		double viewportW = (double)pixelPaddedWidth / dpi.DpiScaleX;
		double viewportH = (double)pixelPaddedHeight / dpi.DpiScaleY;
		if (MapScrollViewer != null)
		{
			double vpw = SafeViewportWidth();
			double vph = SafeViewportHeight();
			if (!double.IsNaN(vpw) && vpw > 0.0)
			{
				viewportW = vpw;
			}
			if (!double.IsNaN(vph) && vph > 0.0)
			{
				viewportH = vph;
			}
		}
		int viewportCols = (int)Math.Ceiling(viewportW / (16.0 * scale)) + 2;
		int viewportRows = (int)Math.Ceiling(viewportH / (16.0 * scale)) + 2;
		int[] expansions = new int[4] { 1, 2, 4, 0 };
		int[] array = expansions;
		foreach (int expansion in array)
		{
			int colsToRender = ((expansion == 0) ? capturedMapWidth : Math.Min(viewportCols * expansion, capturedMapWidth));
			int rowsToRender = ((expansion == 0) ? capturedMapHeight : Math.Min(viewportRows * expansion, capturedMapHeight));
			Debug.WriteLine($"RebuildParallaxGroundDeferredAsync: Rendering expansion {expansion}x (cols={colsToRender}, rows={rowsToRender})");
			await base.Dispatcher.InvokeAsync(delegate
			{
				try
				{
					DrawingVisual drawingVisual = new DrawingVisual();
					using (DrawingContext drawingContext = drawingVisual.RenderOpen())
					{
						if (!hideBackground && parallaxData.images != null && parallaxData.bitmap != null && parallaxData.images.Length != 0)
						{
							int num = Math.Max(1, parallaxData.bitmap.PixelWidth / 16);
							int item = groundData.rows;
							for (int j = 0; j < rowsToRender; j++)
							{
								if (item <= 0 || j < capturedMapHeight || j >= capturedMapHeight + item)
								{
									for (int k = 0; k < colsToRender; k++)
									{
										int num2 = (j * num + k) % parallaxData.images.Length;
										if (num2 < 0)
										{
											num2 += parallaxData.images.Length;
										}
										ImageSource imageSource = null;
										try
										{
											if (parallaxData.tonedImages != null && parallaxData.tonedImages.Length == parallaxData.images.Length)
											{
												imageSource = parallaxData.tonedImages[num2];
											}
										}
										catch
										{
											imageSource = null;
										}
										if (imageSource == null)
										{
											imageSource = parallaxData.images[num2];
										}
										if (imageSource != null)
										{
											double x = (double)(k * 16) * scale + pad;
											double y = (double)(j * 16) * scale + pad;
											drawingContext.DrawImage(imageSource, new Rect(x, y, 16.0 * scale, 16.0 * scale));
										}
									}
								}
							}
						}
					}
					RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
					renderTargetBitmap.Render(drawingVisual);
					DrawingVisual drawingVisual2 = new DrawingVisual();
					using (DrawingContext drawingContext2 = drawingVisual2.RenderOpen())
					{
						if (!hideGround && groundData.images != null && groundData.images.Length != 0)
						{
							int num3 = Math.Max(1, (groundData.bitmap?.PixelWidth ?? 16) / 16);
							int item2 = groundData.rows;
							double num4 = (double)(capturedMapHeight * 16) * scale;
							int val = Math.Max(0, (int)Math.Ceiling((viewportH - num4) / (16.0 * scale)));
							int num5 = Math.Max(item2, val);
							num5++;
							for (int l = 0; l < num5; l++)
							{
								for (int m = 0; m < colsToRender; m++)
								{
									int num6 = (m % num3 + num3) % num3;
									int num7;
									if (item2 <= 1)
									{
										num7 = 0;
									}
									else if (l < item2)
									{
										num7 = l;
									}
									else
									{
										int num8 = (l - item2) % (item2 - 1);
										num7 = 1 + num8;
									}
									int num9 = (num7 * num3 + num6) % groundData.images.Length;
									ImageSource imageSource2 = null;
									try
									{
										if (groundData.tonedImages != null && groundData.tonedImages.Length == groundData.images.Length)
										{
											imageSource2 = groundData.tonedImages[num9];
										}
									}
									catch
									{
										imageSource2 = null;
									}
									if (imageSource2 == null)
									{
										imageSource2 = groundData.images[num9];
									}
									double x2 = (double)(m * 16) * scale + pad;
									double y2 = (double)((capturedMapHeight + l) * 16) * scale + pad;
									if (imageSource2 != null)
									{
										drawingContext2.DrawImage(imageSource2, new Rect(x2, y2, 16.0 * scale, 16.0 * scale));
									}
								}
							}
						}
					}
					RenderTargetBitmap renderTargetBitmap2 = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
					renderTargetBitmap2.Render(drawingVisual2);
					parallaxRtb = renderTargetBitmap;
					groundRtb = renderTargetBitmap2;
					if (ParallaxImage != null)
					{
						ParallaxImage.Source = parallaxRtb;
					}
					if (GroundImage != null)
					{
						GroundImage.Source = groundRtb;
					}
					Debug.WriteLine($"RebuildParallaxGroundDeferredAsync: Expansion {expansion}x complete");
				}
				catch (Exception ex)
				{
					Debug.WriteLine("RebuildParallaxGroundDeferredAsync: Error - " + ex.Message);
				}
			}, DispatcherPriority.Background);
			if (expansion != 0)
			{
				await Task.Delay(200);
			}
		}
		Debug.WriteLine("RebuildParallaxGroundDeferredAsync: All expansions complete");
	}

	private void UpdateParallaxTransform()
	{
		if (MapScrollViewer != null && parallaxRtb != null)
		{
			double num = MapScrollViewer?.HorizontalOffset ?? 0.0;
			double num2 = MapScrollViewer?.VerticalOffset ?? 0.0;
			double num3 = num * 0.09999999999999998;
			double num4 = num2 * 0.09999999999999998;
			if (parallaxTransform == null)
			{
				parallaxTransform = new TranslateTransform(num3, num4);
			}
			else
			{
				parallaxTransform.X = num3;
				parallaxTransform.Y = num4;
			}
			if (ParallaxImage != null)
			{
				ParallaxImage.RenderTransform = parallaxTransform;
			}
		}
	}

	private void UpdateQuickZoomTransform()
	{
		if (ZoomSlider == null)
		{
			return;
		}
		double value = ZoomSlider.Value;
		if (!(cachedScale > 0.0) || !(Math.Abs(cachedScale - value) > 1E-06))
		{
			return;
		}
		double num = value / cachedScale;
		ScaleTransform scaleTransform = new ScaleTransform(num, num);
		if (BackgroundImage != null)
		{
			BackgroundImage.LayoutTransform = scaleTransform;
		}
		if (ParallaxImage != null)
		{
			TransformGroup transformGroup = new TransformGroup();
			transformGroup.Children.Add(scaleTransform);
			if (parallaxTransform != null)
			{
				transformGroup.Children.Add(parallaxTransform);
			}
			ParallaxImage.RenderTransform = transformGroup;
		}
		if (GroundImage != null)
		{
			GroundImage.LayoutTransform = scaleTransform;
		}
		if (TilesImage != null)
		{
			TilesImage.LayoutTransform = scaleTransform;
		}
		if (SpritesImage != null)
		{
			SpritesImage.LayoutTransform = scaleTransform;
		}
		if (PortalsImage != null)
		{
			PortalsImage.LayoutTransform = scaleTransform;
		}
		if (GridImage != null)
		{
			GridImage.LayoutTransform = scaleTransform;
		}
		if (CanvasHost != null)
		{
			CanvasHost.LayoutTransform = scaleTransform;
		}
	}

	private void CommitZoom()
	{
		try
		{
			zoomCommitTimer?.Stop();
		}
		catch
		{
		}
		deferZoomRebuild = false;
		if (useFastZoom)
		{
			hasZoomAnchor = false;
			return;
		}
		try
		{
			if (BackgroundImage != null)
			{
				BackgroundImage.LayoutTransform = Transform.Identity;
			}
			if (ParallaxImage != null)
			{
				if (parallaxTransform != null)
				{
					ParallaxImage.RenderTransform = parallaxTransform;
				}
				else
				{
					ParallaxImage.RenderTransform = Transform.Identity;
				}
				ParallaxImage.LayoutTransform = Transform.Identity;
			}
			if (GroundImage != null)
			{
				GroundImage.LayoutTransform = Transform.Identity;
			}
			if (TilesImage != null)
			{
				TilesImage.LayoutTransform = Transform.Identity;
			}
			if (GridImage != null)
			{
				GridImage.LayoutTransform = Transform.Identity;
			}
			if (CanvasHost != null)
			{
				CanvasHost.LayoutTransform = Transform.Identity;
			}
		}
		catch
		{
		}
		try
		{
			Redraw();
		}
		catch
		{
		}
		try
		{
			base.Dispatcher.Invoke(delegate
			{
			}, DispatcherPriority.Render);
		}
		catch
		{
		}
		if (hasZoomAnchor && MapScrollViewer != null)
		{
			try
			{
				double num = ZoomSlider?.Value ?? 1.0;
				double num2 = mapViewportPadding;
				double num3 = num2 + zoomAnchorMapX * 16.0 * num;
				double num4 = num2 + zoomAnchorMapY * 16.0 * num;
				double val = num3 - zoomAnchorViewportX;
				double val2 = num4 - zoomAnchorViewportY;
				double val3 = Math.Max(0.0, (CanvasHost?.ActualWidth ?? 0.0) - SafeViewportWidth());
				double val4 = Math.Max(0.0, (CanvasHost?.ActualHeight ?? 0.0) - SafeViewportHeight());
				val = Math.Max(0.0, Math.Min(val3, val));
				val2 = Math.Max(0.0, Math.Min(val4, val2));
				MapScrollViewer?.ScrollToHorizontalOffset(val);
				MapScrollViewer?.ScrollToVerticalOffset(val2);
			}
			catch
			{
			}
		}
		try
		{
			ClampScrollOffsets();
		}
		catch
		{
		}
		try
		{
			SnapScrollOffsetsToDevicePixels();
		}
		catch
		{
		}
		try
		{
			UpdateParallaxTransform();
		}
		catch
		{
		}
		hasZoomAnchor = false;
		try
		{
			if (GridImage != null)
			{
				DpiScale dpi = VisualTreeHelper.GetDpi(this);
				double num5 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
				double num6 = mapViewportPadding;
				int num7 = Math.Max(1, (int)Math.Ceiling(16.0 * num5 * dpi.DpiScaleY));
				int num8 = (int)Math.Round(num6 * dpi.DpiScaleY);
				double num9 = (double)(num8 + mapHeight * num7) / dpi.DpiScaleY;
				double num10 = num6 + (double)(mapHeight * 16) * num5;
				double num11 = num10 - num9;
				DpiScale dpiScale = dpi;
				int num12 = (int)Math.Round(num11 * dpiScale.DpiScaleY);
				if (Math.Abs(num12) > 0)
				{
					double offsetY = (double)num12 / dpiScale.DpiScaleY;
					TranslateTransform renderTransform = new TranslateTransform(0.0, offsetY);
					GridImage.RenderTransform = renderTransform;
					try
					{
						if (TilesImage != null)
						{
							TilesImage.RenderTransform = renderTransform;
						}
					}
					catch
					{
					}
					try
					{
						if (SpritesImage != null)
						{
							SpritesImage.RenderTransform = renderTransform;
						}
					}
					catch
					{
					}
					try
					{
						if (PortalsImage != null)
						{
							PortalsImage.RenderTransform = renderTransform;
						}
					}
					catch
					{
					}
					gridRenderShiftY = offsetY;
					gridRenderShiftYPx = num12;
				}
				else
				{
					GridImage.RenderTransform = Transform.Identity;
					try
					{
						if (TilesImage != null)
						{
							TilesImage.RenderTransform = Transform.Identity;
						}
					}
					catch
					{
					}
					try
					{
						if (SpritesImage != null)
						{
							SpritesImage.RenderTransform = Transform.Identity;
						}
					}
					catch
					{
					}
					try
					{
						if (PortalsImage != null)
						{
							PortalsImage.RenderTransform = Transform.Identity;
						}
					}
					catch
					{
					}
					gridRenderShiftY = 0.0;
					gridRenderShiftYPx = 0;
				}
			}
		}
		catch
		{
		}
		lastHoverX = -1;
		lastHoverY = -1;
		try
		{
			Activate();
		}
		catch
		{
		}
		try
		{
			UpdateIncompatibleOverlay();
		}
		catch
		{
		}
	}

	private void SnapScrollOffsetsToDevicePixels()
	{
		try
		{
			if (MapScrollViewer != null && CanvasHost != null)
			{
				DpiScale dpi = VisualTreeHelper.GetDpi(this);
				double num = MapScrollViewer?.HorizontalOffset ?? 0.0;
				double num2 = MapScrollViewer?.VerticalOffset ?? 0.0;
				double num3 = Math.Round(num * dpi.DpiScaleX);
				double num4 = Math.Round(num2 * dpi.DpiScaleY);
				double val = num3 / dpi.DpiScaleX;
				double val2 = num4 / dpi.DpiScaleY;
				double val3 = Math.Max(0.0, (CanvasHost?.ActualWidth ?? 0.0) - SafeViewportWidth());
				double val4 = Math.Max(0.0, (CanvasHost?.ActualHeight ?? 0.0) - SafeViewportHeight());
				val = Math.Max(0.0, Math.Min(val3, val));
				val2 = Math.Max(0.0, Math.Min(val4, val2));
				MapScrollViewer?.ScrollToHorizontalOffset(val);
				MapScrollViewer?.ScrollToVerticalOffset(val2);
			}
		}
		catch
		{
		}
	}

	private void ClampScrollOffsets()
	{
		if (MapScrollViewer != null)
		{
			double num = ZoomSlider?.Value ?? 1.0;
			double num2 = mapViewportPadding;
			double num3 = (double)(mapHeight * 16) * num;
			double num4 = 0.0;
			if (groundBitmap != null && groundImages != null && groundImages.Length != 0)
			{
				DpiScale dpi = VisualTreeHelper.GetDpi(this);
				num4 = (double)groundBitmap.PixelHeight / dpi.DpiScaleY * num;
			}
			num3 += num4 - 32.0 * num;
			double num5 = num3 + num2 * 2.0;
			double num6 = Math.Max(0.0, num5 - SafeViewportHeight());
			if ((MapScrollViewer?.VerticalOffset ?? 0.0) > num6 + 1E-06)
			{
				MapScrollViewer?.ScrollToVerticalOffset(num6);
			}
		}
	}

	private void BuildGridBitmap(double scale, double pad, double fullW, double fullH, double paddedFullW, double paddedFullH, int pixelPaddedWidth, int pixelPaddedHeight, DpiScale dpi)
	{
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			int num = Math.Min(255, (int)(gridDarkness * 255.0 * 1.6));
			double thickness = 1.0 / dpi.DpiScaleX;
			Pen pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)num, 200, 200, 200)), thickness);
			pen.Freeze();
			int num2 = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
			int num3 = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
			int num4 = (int)Math.Round(pad * dpi.DpiScaleX);
			for (int i = 0; i < mapHeight; i++)
			{
				for (int j = 0; j < mapWidth; j++)
				{
					int num5 = num4 + j * num2;
					int num6 = num4 + i * num3;
					double x = (double)num5 / dpi.DpiScaleX;
					double y = (double)num6 / dpi.DpiScaleY;
					double width = (double)num2 / dpi.DpiScaleX;
					double height = (double)num3 / dpi.DpiScaleY;
					drawingContext.DrawRectangle(Brushes.Transparent, pen, new Rect(x, y, width, height));
				}
			}
		}
		gridRtb = new RenderTargetBitmap(pixelPaddedWidth, pixelPaddedHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
		gridRtb.Render(drawingVisual);
	}

	private unsafe void RebuildAllTilesBitmap(double scale, double pad)
	{
		if (tilesWb == null)
		{
			return;
		}
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		int tilePixelW = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
		int tilePixelH = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
		EnsureScaledTileCache(scale, dpi);
		tilesWb.Lock();
		try
		{
			IntPtr backBuffer = tilesWb.BackBuffer;
			int backBufferStride = tilesWb.BackBufferStride;
			int num = backBufferStride * cachedPixelHeight;
			byte* ptr = (byte*)backBuffer.ToPointer();
			for (int i = 0; i < num; i++)
			{
				ptr[i] = 0;
			}
			for (int j = 0; j < mapHeight; j++)
			{
				for (int k = 0; k < mapWidth; k++)
				{
					UpdateTileBitmapAtLocked(k, j, scale, pad, tilePixelW, tilePixelH, dpi);
				}
			}
			tilesWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
		}
		finally
		{
			tilesWb.Unlock();
		}
		try
		{
			BuildSelectSameIndexMaps();
		}
		catch
		{
		}
	}

	private async void RebuildAllTilesBitmapAsync(double scale, double pad)
	{
		if (tilesWb == null)
		{
			return;
		}
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		int tilePixelW = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
		int tilePixelH = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
		EnsureScaledTileCache(scale, dpi);
		int totalTiles = mapWidth * mapHeight;
		bool isLargeMap = totalTiles > 50000;
		int batchSize = (isLargeMap ? Math.Max(2000, mapWidth * 2) : Math.Max(500, mapWidth));
		Debug.WriteLine($"RebuildAllTilesBitmapAsync: {totalTiles} tiles, batchSize={batchSize}, isLargeMap={isLargeMap}");
		for (int batchStart = 0; batchStart < totalTiles; batchStart += batchSize)
		{
			int batchEnd = Math.Min(batchStart + batchSize, totalTiles);
			if (!isLargeMap)
			{
				await Task.Run(delegate
				{
					for (int i = batchStart; i < batchEnd; i++)
					{
						int num = i % mapWidth;
						int num2 = i / mapWidth;
						if (tiles != null && num >= 0 && num2 >= 0 && num2 < mapHeight && num < mapWidth)
						{
							int num3 = num2 * mapWidth + num;
							if (num3 >= 0 && num3 < tiles.Length)
							{
								int num4 = tiles[num3];
								if (num4 >= 0)
								{
									int scaleKey = (int)Math.Round(scale * 100.0);
									try
									{
										GetOrRenderCachedTile(num4, scaleKey, tilePixelW, tilePixelH, dpi);
									}
									catch
									{
									}
								}
							}
						}
					}
				});
			}
			await base.Dispatcher.InvokeAsync(delegate
			{
				if (tilesWb == null)
				{
					return;
				}
				tilesWb.Lock();
				try
				{
					for (int i = batchStart; i < batchEnd; i++)
					{
						int num = i % mapWidth;
						int num2 = i / mapWidth;
						if (tilesWb != null && tiles != null && num >= 0 && num2 >= 0 && num2 < mapHeight && num < mapWidth)
						{
							int num3 = num2 * mapWidth + num;
							if (num3 >= 0 && num3 < tiles.Length)
							{
								try
								{
									UpdateTileBitmapAtLocked(num, num2, scale, pad, tilePixelW, tilePixelH, dpi);
								}
								catch
								{
								}
							}
						}
					}
					int num4 = batchStart / mapWidth;
					int num5 = (batchEnd - 1) / mapWidth;
					int val = (num5 - num4 + 1) * tilePixelH;
					int num6 = (int)((double)(num4 * tilePixelH) + pad * dpi.DpiScaleY);
					int num7 = Math.Max(0, Math.Min(cachedPixelWidth, tilesWb.PixelWidth));
					int num8 = Math.Max(0, Math.Min(val, Math.Min(cachedPixelHeight, tilesWb.PixelHeight - num6)));
					if (num6 >= 0 && num6 < tilesWb.PixelHeight && num7 > 0 && num8 > 0)
					{
						tilesWb.AddDirtyRect(new Int32Rect(0, num6, num7, num8));
					}
				}
				finally
				{
					tilesWb.Unlock();
				}
			}, DispatcherPriority.Background);
		}
		Debug.WriteLine("RebuildAllTilesBitmapAsync: Complete");
		try
		{
			BuildSelectSameIndexMaps();
		}
		catch
		{
		}
	}

	private void EnsureScaledTileCache(double scale, DpiScale dpi)
	{
		if (tileImages == null)
		{
			return;
		}
		int key = (int)Math.Round(scale * 100.0);
		if (scaledTileCaches.TryGetValue(key, out ScaledTileCache _))
		{
			return;
		}
		try
		{
			int num = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
			int h = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
			int stride = num * 4;
			int num2 = 512;
			byte[][] pixels = new byte[num2][];
			ScaledTileCache value2 = new ScaledTileCache(pixels, num, h, stride, scale, dpi);
			scaledTileCaches[key] = value2;
		}
		catch
		{
		}
	}

	private byte[]? GetOrRenderCachedTile(int tileIdx, int scaleKey, int tilePixelW, int tilePixelH, DpiScale dpi)
	{
		if (tileImages == null)
		{
			return null;
		}
		if (tileIdx >= 1000)
		{
			BitmapSource customAnimationTile = GetCustomAnimationTile(tileIdx);
			if (customAnimationTile != null)
			{
				int num = tilePixelW * 4;
				byte[] array = new byte[tilePixelH * num];
				try
				{
					DrawingVisual drawingVisual = new DrawingVisual();
					double width = (double)tilePixelW / dpi.DpiScaleX;
					double num2 = (double)tilePixelH / dpi.DpiScaleY;
					using (DrawingContext drawingContext = drawingVisual.RenderOpen())
					{
						if (IsHalfHeightTile(tileIdx))
						{
							double num3 = num2 / 16.0;
							double num4 = 8.0 * num3;
							if (IsTopHalfTile(tileIdx))
							{
								double y = num2 - num4;
								drawingContext.DrawImage(customAnimationTile, new Rect(0.0, y, width, num4));
							}
							else
							{
								drawingContext.DrawImage(customAnimationTile, new Rect(0.0, 0.0, width, num4));
							}
						}
						else
						{
							drawingContext.DrawImage(customAnimationTile, new Rect(0.0, 0.0, width, num2));
						}
					}
					RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(tilePixelW, tilePixelH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
					renderTargetBitmap.Render(drawingVisual);
					renderTargetBitmap.CopyPixels(array, num, 0);
				}
				catch
				{
				}
				return array;
			}
			return null;
		}
		if (!scaledTileCaches.TryGetValue(scaleKey, out ScaledTileCache value))
		{
			return null;
		}
		if (tileIdx < 0 || tileIdx >= value.Pixels.Length)
		{
			return null;
		}
		if (value.Pixels[tileIdx] != null)
		{
			return value.Pixels[tileIdx];
		}
		int num5 = tilePixelW * 4;
		byte[] array2 = new byte[tilePixelH * num5];
		try
		{
			ImageSource imageSource = null;
			if (tileIdx >= 256 && tileIdx < 512)
			{
				int num6 = tileIdx - 256;
				if (spriteImages != null && num6 < spriteImages.Length)
				{
					imageSource = spriteImages[num6];
				}
			}
			else if (tileIdx >= 0 && tileIdx < 256)
			{
				if (IsPlayerReplacementTile(tileIdx) && playerTintEnabled)
				{
					ImageSource imageSource2 = ((tileIdx < tileImages.Length) ? tileImages[tileIdx] : null);
					ImageSource imageSource3 = ((tileTonedImages != null && tileIdx < tileTonedImages.Length) ? tileTonedImages[tileIdx] : null);
					imageSource = CreatePlayerReplacedFromBaseAndToned(imageSource2, imageSource3 ?? imageSource2, playerTint);
				}
				else
				{
					imageSource = ((tileTonedImages != null && tileIdx < tileTonedImages.Length) ? tileTonedImages[tileIdx] : ((tileIdx < tileImages.Length) ? tileImages[tileIdx] : null));
				}
			}
			if (imageSource == null && (tileIdx == 34 || tileIdx == 36))
			{
				Debug.WriteLine($"DEBUG: Tile {tileIdx} has NULL source! tileImages.Length={tileImages?.Length}, tileTonedImages.Length={tileTonedImages?.Length}");
			}
			if (imageSource != null)
			{
				DrawingVisual drawingVisual2 = new DrawingVisual();
				double width2 = (double)tilePixelW / dpi.DpiScaleX;
				double height = (double)tilePixelH / dpi.DpiScaleY;
				using (DrawingContext drawingContext2 = drawingVisual2.RenderOpen())
				{
					drawingContext2.DrawImage(imageSource, new Rect(0.0, 0.0, width2, height));
				}
				RenderTargetBitmap renderTargetBitmap2 = new RenderTargetBitmap(tilePixelW, tilePixelH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
				renderTargetBitmap2.Render(drawingVisual2);
				renderTargetBitmap2.CopyPixels(array2, num5, 0);
				if (tileIdx == 34 || tileIdx == 36)
				{
					int value2 = array2.Count((byte b) => b != 0);
					Debug.WriteLine($"DEBUG: Rendered tile {tileIdx}, {value2}/{array2.Length} non-zero bytes");
				}
			}
		}
		catch (Exception ex)
		{
			if (tileIdx == 34 || tileIdx == 36)
			{
				Debug.WriteLine($"DEBUG: Exception rendering tile {tileIdx}: {ex.Message}");
			}
		}
		value.Pixels[tileIdx] = array2;
		return array2;
	}

	private void UpdateTileBitmapAt(int x, int y, double scale, double pad, int? preTilePxW = null, int? preTilePxH = null, DpiScale? preDpi = null)
	{
		if (tilesWb == null)
		{
			return;
		}
		DpiScale dpi = preDpi ?? VisualTreeHelper.GetDpi(this);
		int num = preTilePxW ?? Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
		int num2 = preTilePxH ?? Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
		int originalIndex = tiles[y * mapWidth + x];
		int animatedTileIndex = GetAnimatedTileIndex(originalIndex);
		int num3 = (int)Math.Round(pad * dpi.DpiScaleX);
		int num4 = (int)Math.Round(pad * dpi.DpiScaleY);
		int num5 = Math.Max(0, num3 + x * num);
		int num6 = Math.Max(0, num4 + y * num2);
		int num7 = num * 4;
		int scaleKey = (int)Math.Round(scale * 100.0);
		byte[] pixels;
		if (animatedTileIndex >= 0)
		{
			byte[] orRenderCachedTile = GetOrRenderCachedTile(animatedTileIndex, scaleKey, num, num2, dpi);
			pixels = ((orRenderCachedTile == null || orRenderCachedTile.Length == 0) ? new byte[num2 * num7] : orRenderCachedTile);
		}
		else
		{
			pixels = new byte[num2 * num7];
		}
		try
		{
			tilesWb.WritePixels(new Int32Rect(num5, num6, Math.Min(num, cachedPixelWidth - num5), Math.Min(num2, cachedPixelHeight - num6)), pixels, num7, 0);
		}
		catch
		{
		}
	}

	private unsafe void UpdateSpriteBitmapAt(int x, int y, double scale, double pad)
	{
		if (spritesWb == null || sprites == null)
		{
			return;
		}
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		int num = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
		int num2 = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
		int num3 = sprites[y * mapWidth + x];
		spritesWb.Lock();
		try
		{
			if (num3 < 0)
			{
				int num4 = (int)Math.Round(pad * dpi.DpiScaleX);
				int num5 = (int)Math.Round(pad * dpi.DpiScaleY);
				int num6 = Math.Max(0, num4 + x * num);
				int num7 = Math.Max(0, num5 + y * num2);
				IntPtr backBuffer = spritesWb.BackBuffer;
				if (backBuffer == IntPtr.Zero)
				{
					return;
				}
				int backBufferStride = spritesWb.BackBufferStride;
				int num8 = Math.Min(num, cachedPixelWidth - num6);
				int num9 = Math.Min(num2, cachedPixelHeight - num7);
				for (int i = 0; i < num9; i++)
				{
					long num10 = (num7 + i) * backBufferStride + num6 * 4;
					byte* ptr = (byte*)backBuffer.ToPointer() + num10;
					for (int j = 0; j < num8 * 4; j++)
					{
						ptr[j] = 0;
					}
				}
				if (previewMode)
				{
					for (int k = Math.Max(0, y - 2); k <= Math.Min(mapHeight - 1, y); k++)
					{
						for (int l = Math.Max(0, x - 1); l <= Math.Min(mapWidth - 1, x); l++)
						{
							int num11 = k * mapWidth + l;
							int num12 = sprites[num11];
							if (!IsPortalSprite(num12))
							{
								continue;
							}
							int num13 = k * mapWidth + l;
							BitmapSource portalSpriteForId = GetPortalSpriteForId(num12, num13);
							if (portalSpriteForId == null)
							{
								continue;
							}
							int num14 = Math.Max(0, num4 + l * num);
							int num15 = Math.Max(0, num5 + k * num2);
							int num16 = num;
							int num17 = num2;
							int animatedSpriteIndex = GetAnimatedSpriteIndex(num12);
							bool flag = animatedSpriteIndex >= 3000 && animatedSpriteIndex <= 3029;
							if (spritePixelOffsets.TryGetValue(num13, out (int, int) value))
							{
								int num18 = (int)Math.Round((double)value.Item1 * scale * dpi.DpiScaleX);
								int num19 = (int)Math.Round((double)value.Item2 * scale * dpi.DpiScaleY);
								num14 += num18;
								num15 += num19;
							}
							if (flag)
							{
								if (animatedSpriteIndex >= 3000 && animatedSpriteIndex <= 3010)
								{
									num16 = num * 3 / 2;
									num17 = num2 * 3;
								}
								else if ((animatedSpriteIndex >= 3011 && animatedSpriteIndex <= 3014) || (animatedSpriteIndex >= 3019 && animatedSpriteIndex <= 3023))
								{
									num16 = num * 3;
									num17 = num2 * 2;
								}
								else if (animatedSpriteIndex == 3027 || animatedSpriteIndex == 3028)
								{
									num16 = Math.Min(cachedPixelWidth, (int)Math.Round((double)num * (double)portalSpriteForId.PixelWidth / 16.0));
									num17 = num2 * 2;
								}
								else if (animatedSpriteIndex == 3026)
								{
									num16 = num * 3 / 2;
									num17 = num2 * 2;
								}
								else if (animatedSpriteIndex == 3024 || animatedSpriteIndex == 3025 || animatedSpriteIndex == 3029)
								{
									num16 = num;
									num17 = num2 * 2;
								}
								else
								{
									num16 = num * 3 / 2;
									num17 = num2 * 3;
								}
							}
							int num20 = Math.Max(num6, num14);
							int num21 = Math.Max(num7, num15);
							int num22 = Math.Min(num6 + num, num14 + num16);
							int num23 = Math.Min(num7 + num2, num15 + num17);
							if (num22 <= num20 || num23 <= num21)
							{
								continue;
							}
							int pixelWidth = portalSpriteForId.PixelWidth;
							int pixelHeight = portalSpriteForId.PixelHeight;
							int num24 = pixelWidth * 4;
							byte[] array = new byte[pixelHeight * num24];
							portalSpriteForId.CopyPixels(array, num24, 0);
							double num25 = (double)num16 / (double)pixelWidth;
							double num26 = (double)num17 / (double)pixelHeight;
							for (int m = num21; m < num23; m++)
							{
								for (int n = num20; n < num22; n++)
								{
									int num27 = n - num14;
									int num28 = m - num15;
									int num29 = Math.Min((int)((double)num27 / num25), pixelWidth - 1);
									int num30 = Math.Min((int)((double)num28 / num26), pixelHeight - 1);
									int num31 = num30 * num24 + num29 * 4;
									long num32 = m * backBufferStride + n * 4;
									byte* ptr2 = (byte*)backBuffer.ToPointer() + num32;
									*ptr2 = array[num31];
									ptr2[1] = array[num31 + 1];
									ptr2[2] = array[num31 + 2];
									ptr2[3] = array[num31 + 3];
								}
							}
						}
					}
				}
				spritesWb.AddDirtyRect(new Int32Rect(num6, num7, num8, num9));
			}
			else
			{
				UpdateSpriteBitmapAtLocked(x, y, num3, scale, pad, num, num2, dpi);
				int num33 = (int)Math.Round(pad * dpi.DpiScaleX);
				int num34 = (int)Math.Round(pad * dpi.DpiScaleY);
				int num35 = Math.Max(0, num33 + x * num);
				int num36 = Math.Max(0, num34 + y * num2);
				int animatedSpriteIndex2 = GetAnimatedSpriteIndex(num3);
				int val = num;
				int val2 = num2;
				bool flag2 = animatedSpriteIndex2 >= 3000 && animatedSpriteIndex2 <= 3029;
				if (flag2)
				{
					if (animatedSpriteIndex2 >= 3000 && animatedSpriteIndex2 <= 3010)
					{
						val = num * 3 / 2;
						val2 = num2 * 3;
					}
					else
					{
						val = num * 3;
						val2 = num2 * 2;
					}
				}
				if (!flag2 && (animatedSpriteIndex2 == 2126 || animatedSpriteIndex2 == 2127))
				{
					val2 = num2 * 3 / 2;
				}
				if (previewMode && num3 == 45)
				{
					double num37 = 16.0 * ((ZoomSlider != null) ? ZoomSlider.Value : 1.0) * dpi.DpiScaleY;
					int num38 = (int)Math.Round(num37 * 1.5);
					num36 = Math.Max(0, num36 - num38);
				}
				int width = Math.Min(val, cachedPixelWidth - num35);
				int height = Math.Min(val2, Math.Max(0, cachedPixelHeight - num36));
				spritesWb.AddDirtyRect(new Int32Rect(num35, num36, width, height));
			}
		}
		finally
		{
			spritesWb.Unlock();
		}
		try
		{
			QueueUpdateIncompatibleOverlay(x, y, x, y);
		}
		catch
		{
		}
	}

	private unsafe void UpdateTileBitmapAtLocked(int x, int y, double scale, double pad, int tilePixelW, int tilePixelH, DpiScale dpi)
	{
		if (tilesWb == null)
		{
			return;
		}
		int num = tiles[y * mapWidth + x];
		if (num < 0)
		{
			return;
		}
		int animatedTileIndex = GetAnimatedTileIndex(num);
		int num2 = (int)Math.Round(pad * dpi.DpiScaleX);
		int num3 = (int)Math.Round(pad * dpi.DpiScaleY);
		int num4 = Math.Max(0, num2 + x * tilePixelW);
		int num5 = Math.Max(0, num3 + y * tilePixelH);
		if (num4 >= cachedPixelWidth || num5 >= cachedPixelHeight)
		{
			return;
		}
		int scaleKey = (int)Math.Round(scale * 100.0);
		byte[] orRenderCachedTile = GetOrRenderCachedTile(animatedTileIndex, scaleKey, tilePixelW, tilePixelH, dpi);
		if (orRenderCachedTile == null || orRenderCachedTile.Length == 0)
		{
			return;
		}
		IntPtr backBuffer = tilesWb.BackBuffer;
		int backBufferStride = tilesWb.BackBufferStride;
		int num6 = Math.Min(tilePixelW, cachedPixelWidth - num4);
		int num7 = Math.Min(tilePixelH, cachedPixelHeight - num5);
		int num8 = tilePixelW * 4;
		int num9 = tilePixelH * num8;
		if (orRenderCachedTile.Length < num9)
		{
			num7 = Math.Min(num7, orRenderCachedTile.Length / num8);
		}
		for (int i = 0; i < num7; i++)
		{
			int num10 = i * num8;
			long num11 = (num5 + i) * backBufferStride + num4 * 4;
			byte* ptr = (byte*)backBuffer.ToPointer() + num11;
			int num12 = Math.Min(num6 * 4, orRenderCachedTile.Length - num10);
			for (int j = 0; j < num12; j++)
			{
				ptr[j] = orRenderCachedTile[num10 + j];
			}
		}
	}

	private unsafe async void RebuildAllSpritesBitmap(double scale, double pad)
	{
		if (spritesWb == null || spriteImages == null)
		{
			return;
		}
		try
		{
			if (previewMode && portalsWb != null)
			{
				RebuildPortalsRegion(0, 0, mapWidth - 1, mapHeight - 1, scale, pad);
			}
			DpiScale dpi = VisualTreeHelper.GetDpi(this);
			int spritePixelW = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
			int spritePixelH = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
			await base.Dispatcher.InvokeAsync(delegate
			{
				if (spritesWb == null)
				{
					return;
				}
				spritesWb.Lock();
				try
				{
					IntPtr backBuffer = spritesWb.BackBuffer;
					if (backBuffer != IntPtr.Zero)
					{
						int backBufferStride = spritesWb.BackBufferStride;
						int num = backBufferStride * cachedPixelHeight;
						byte* ptr = (byte*)backBuffer.ToPointer();
						for (int i = 0; i < num; i++)
						{
							ptr[i] = 0;
						}
						spritesWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
					}
				}
				finally
				{
					spritesWb.Unlock();
				}
			});
			int totalSprites = mapWidth * mapHeight;
			bool isLargeMap = totalSprites > 50000;
			int batchSize = (isLargeMap ? Math.Max(2000, mapWidth * 2) : Math.Max(500, mapWidth));
			Debug.WriteLine($"RebuildAllSpritesBitmap: {totalSprites} sprites, batchSize={batchSize}, isLargeMap={isLargeMap}");
			for (int batchStart = 0; batchStart < totalSprites; batchStart += batchSize)
			{
				int batchEnd = Math.Min(batchStart + batchSize, totalSprites);
				await base.Dispatcher.InvokeAsync(delegate
				{
					if (spritesWb == null)
					{
						return;
					}
					spritesWb.Lock();
					try
					{
						for (int i = batchStart; i < batchEnd; i++)
						{
							int num = i % mapWidth;
							int num2 = i / mapWidth;
							int num3 = sprites[num2 * mapWidth + num];
							if (num3 >= 0 && num3 < spriteImages.Length)
							{
								try
								{
									if (num3 == 23 || num3 == 75 || num3 == 88 || num3 == 100 || num3 == 8 || num3 == 9)
									{
									}
									UpdateSpriteBitmapAtLocked(num, num2, num3, scale, pad, spritePixelW, spritePixelH, dpi);
								}
								catch (Exception ex2)
								{
									Debug.WriteLine($"    Error at sprite ({num},{num2}): {ex2.Message}");
								}
							}
						}
						int num4 = batchStart / mapWidth;
						int num5 = (batchEnd - 1) / mapWidth;
						int num6 = spritePixelH;
						int num7 = 0;
						for (int j = batchStart; j < batchEnd; j++)
						{
							if (spritePixelOffsets.TryGetValue(j, out (int, int) value))
							{
								int val = (int)Math.Round((double)Math.Abs(value.Item2) * scale * dpi.DpiScaleY);
								if (value.Item2 < 0)
								{
									num6 = Math.Max(num6, val);
								}
								else
								{
									num7 = Math.Max(num7, val);
								}
							}
						}
						int num8 = Math.Max(0, num4 - (int)Math.Ceiling((double)num6 / (double)spritePixelH));
						int num9 = Math.Min(mapHeight - 1, num5 + (int)Math.Ceiling((double)num7 / (double)spritePixelH));
						int val2 = (num9 - num8 + 1) * spritePixelH + num6 + num7;
						int num10 = Math.Max(0, (int)((double)(num8 * spritePixelH) + pad * dpi.DpiScaleY - (double)num6));
						int num11 = Math.Min(val2, cachedPixelHeight - num10);
						if (num11 > 0 && num10 < cachedPixelHeight)
						{
							spritesWb.AddDirtyRect(new Int32Rect(0, num10, cachedPixelWidth, num11));
						}
					}
					finally
					{
						spritesWb.Unlock();
					}
				}, DispatcherPriority.Background);
			}
			Debug.WriteLine("RebuildAllSpritesBitmap: Complete");
			try
			{
				base.Dispatcher.Invoke(delegate
				{
					UpdateIncompatibleOverlay();
				});
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("EXCEPTION in RebuildAllSpritesBitmap: " + ex.Message);
			if (spritesWb != null)
			{
				try
				{
					byte[] empty = new byte[cachedPixelHeight * spritesWb.BackBufferStride];
					spritesWb.WritePixels(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight), empty, spritesWb.BackBufferStride, 0);
				}
				catch
				{
				}
			}
			throw;
		}
	}

	private void EnsurePortalDirtyTimer()
	{
		if (portalDirtyTimer != null)
		{
			return;
		}
		portalDirtyTimer = new DispatcherTimer();
		portalDirtyTimer.Interval = TimeSpan.FromMilliseconds(40.0);
		portalDirtyTimer.Tick += delegate
		{
			DispatcherTimer dispatcherTimer = portalDirtyTimer;
			if (dispatcherTimer != null)
			{
				dispatcherTimer.Stop();
				portalDirtyScheduled = false;
				int num = portalDirtyMinX;
				int num2 = portalDirtyMinY;
				int num3 = portalDirtyMaxX;
				int num4 = portalDirtyMaxY;
				portalDirtyMinX = int.MaxValue;
				portalDirtyMinY = int.MaxValue;
				portalDirtyMaxX = int.MinValue;
				portalDirtyMaxY = int.MinValue;
				if (num <= num3 && num2 <= num4)
				{
					try
					{
						RebuildPortalsRegion(num, num2, num3, num4, (ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					}
					catch
					{
					}
				}
			}
		};
	}

	private void EnsureIncompatibleDirtyTimer()
	{
		if (incompatibleDirtyTimer != null)
		{
			return;
		}
		incompatibleDirtyTimer = new DispatcherTimer();
		incompatibleDirtyTimer.Interval = TimeSpan.FromMilliseconds(40.0);
		incompatibleDirtyTimer.Tick += delegate
		{
			DispatcherTimer dispatcherTimer = incompatibleDirtyTimer;
			if (dispatcherTimer == null)
			{
				return;
			}
			dispatcherTimer.Stop();
			incompatibleDirtyScheduled = false;
			int num = incompatibleDirtyMinX;
			int num2 = incompatibleDirtyMinY;
			int num3 = incompatibleDirtyMaxX;
			int num4 = incompatibleDirtyMaxY;
			incompatibleDirtyMinX = int.MaxValue;
			incompatibleDirtyMinY = int.MaxValue;
			incompatibleDirtyMaxX = int.MinValue;
			incompatibleDirtyMaxY = int.MinValue;
			try
			{
				UpdateIncompatibleOverlay();
			}
			catch
			{
			}
		};
	}

	private void QueueRebuildPortalsRegion(int minX, int minY, int maxX, int maxY)
	{
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

	private void QueueUpdateIncompatibleOverlay(int minX, int minY, int maxX, int maxY)
	{
		incompatibleDirtyMinX = Math.Min(incompatibleDirtyMinX, minX);
		incompatibleDirtyMinY = Math.Min(incompatibleDirtyMinY, minY);
		incompatibleDirtyMaxX = Math.Max(incompatibleDirtyMaxX, maxX);
		incompatibleDirtyMaxY = Math.Max(incompatibleDirtyMaxY, maxY);
		EnsureIncompatibleDirtyTimer();
		if (!incompatibleDirtyScheduled && incompatibleDirtyTimer != null)
		{
			incompatibleDirtyScheduled = true;
			incompatibleDirtyTimer.Start();
		}
	}

	private unsafe void RebuildPortalsRegion(int minX, int minY, int maxX, int maxY, double scale, double pad)
	{
		if (portalsWb == null || spriteImages == null || spriteImages == null || spriteImages == null)
		{
			return;
		}
		try
		{
			DpiScale dpi = VisualTreeHelper.GetDpi(this);
			int spritePixelW = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
			int spritePixelH = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
			minX = Math.Max(0, minX);
			minY = Math.Max(0, minY);
			maxX = Math.Min(mapWidth - 1, maxX);
			maxY = Math.Min(mapHeight - 1, maxY);
			int num = (int)Math.Round(pad * dpi.DpiScaleX);
			int num2 = (int)Math.Round(pad * dpi.DpiScaleY);
			int pxLeft = num + minX * spritePixelW;
			int pxTop = num2 + minY * spritePixelH;
			int num3 = num + (maxX + 1) * spritePixelW;
			int num4 = num2 + (maxY + 1) * spritePixelH;
			int widthPx = Math.Max(0, Math.Min(cachedPixelWidth - pxLeft, num3 - pxLeft));
			int heightPx = Math.Max(0, Math.Min(cachedPixelHeight - pxTop, num4 - pxTop));
			if (widthPx <= 0 || heightPx <= 0)
			{
				return;
			}
			base.Dispatcher.Invoke(delegate
			{
				portalsWb.Lock();
				try
				{
					IntPtr backBuffer = portalsWb.BackBuffer;
					if (backBuffer != IntPtr.Zero)
					{
						int backBufferStride = portalsWb.BackBufferStride;
						for (int i = 0; i < heightPx; i++)
						{
							long num13 = (pxTop + i) * backBufferStride + pxLeft * 4;
							byte* ptr = (byte*)backBuffer.ToPointer() + num13;
							for (int j = 0; j < widthPx; j++)
							{
								ptr[j * 4] = 0;
								ptr[j * 4 + 1] = 0;
								ptr[j * 4 + 2] = 0;
								ptr[j * 4 + 3] = 0;
							}
						}
						portalsWb.AddDirtyRect(new Int32Rect(pxLeft, pxTop, widthPx, heightPx));
					}
				}
				finally
				{
					portalsWb.Unlock();
				}
			});
			int num5 = Math.Max(0, minX - 1);
			int num6 = Math.Min(mapWidth - 1, maxX);
			int num7 = Math.Max(0, minY - 2);
			int num8 = Math.Min(mapHeight - 1, maxY);
			for (int ay = num7; ay <= num8; ay++)
			{
				for (int ax = num5; ax <= num6; ax++)
				{
					int idx = sprites[ay * mapWidth + ax];
					if (!IsPortalSprite(idx))
					{
						continue;
					}
					int animatedSpriteIndex = GetAnimatedSpriteIndex(idx);
					int num9 = ax;
					int num10 = ay;
					int num11 = ax + 1;
					int num12 = ay + 2;
					if ((animatedSpriteIndex >= 3011 && animatedSpriteIndex <= 3014) || (animatedSpriteIndex >= 3019 && animatedSpriteIndex <= 3023))
					{
						num11 = ax + 2;
						num12 = ay + 1;
					}
					else if (animatedSpriteIndex == 3015 || animatedSpriteIndex == 3016)
					{
						num11 = ax + 1;
						num12 = ay + 2;
					}
					if (num11 >= minX && num9 <= maxX && num12 >= minY && num10 <= maxY)
					{
						base.Dispatcher.Invoke(delegate
						{
							UpdatePortalBitmapAtLocked(ax, ay, idx, scale, pad, spritePixelW, spritePixelH, dpi);
						});
					}
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("EXCEPTION in RebuildPortalsRegion: " + ex.Message);
		}
	}

	private unsafe void ClearSpritesBitmapTileRect(int minX, int minY, int maxX, int maxY, double scale, double pad)
	{
		if (spritesWb == null)
		{
			return;
		}
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		int num = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
		int num2 = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
		minX = Math.Max(0, minX);
		minY = Math.Max(0, minY);
		maxX = Math.Min(mapWidth - 1, maxX);
		maxY = Math.Min(mapHeight - 1, maxY);
		int num3 = (int)Math.Round(pad * dpi.DpiScaleX);
		int num4 = (int)Math.Round(pad * dpi.DpiScaleY);
		int pxLeft = num3 + minX * num;
		int pxTop = num4 + minY * num2;
		int num5 = num3 + (maxX + 1) * num;
		int num6 = num4 + (maxY + 1) * num2;
		int widthPx = Math.Max(0, Math.Min(cachedPixelWidth - pxLeft, num5 - pxLeft));
		int heightPx = Math.Max(0, Math.Min(cachedPixelHeight - pxTop, num6 - pxTop));
		if (widthPx <= 0 || heightPx <= 0)
		{
			return;
		}
		base.Dispatcher.Invoke(delegate
		{
			spritesWb.Lock();
			try
			{
				IntPtr backBuffer = spritesWb.BackBuffer;
				if (backBuffer != IntPtr.Zero)
				{
					int backBufferStride = spritesWb.BackBufferStride;
					for (int i = 0; i < heightPx; i++)
					{
						long num7 = (pxTop + i) * backBufferStride + pxLeft * 4;
						byte* ptr = (byte*)backBuffer.ToPointer() + num7;
						for (int j = 0; j < widthPx; j++)
						{
							ptr[j * 4] = 0;
							ptr[j * 4 + 1] = 0;
							ptr[j * 4 + 2] = 0;
							ptr[j * 4 + 3] = 0;
						}
					}
					spritesWb.AddDirtyRect(new Int32Rect(pxLeft, pxTop, widthPx, heightPx));
				}
			}
			finally
			{
				spritesWb.Unlock();
			}
		});
	}

	private unsafe void ClearPortalsBitmapTileRect(int minX, int minY, int maxX, int maxY, double scale, double pad)
	{
		if (portalsWb == null)
		{
			return;
		}
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		int num = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleX));
		int num2 = Math.Max(1, (int)Math.Ceiling(16.0 * scale * dpi.DpiScaleY));
		minX = Math.Max(0, minX);
		minY = Math.Max(0, minY);
		maxX = Math.Min(mapWidth - 1, maxX);
		maxY = Math.Min(mapHeight - 1, maxY);
		int num3 = (int)Math.Round(pad * dpi.DpiScaleX);
		int num4 = (int)Math.Round(pad * dpi.DpiScaleY);
		int pxLeft = num3 + minX * num;
		int pxTop = num4 + minY * num2;
		int num5 = num3 + (maxX + 1) * num;
		int num6 = num4 + (maxY + 1) * num2;
		int widthPx = Math.Max(0, Math.Min(cachedPixelWidth - pxLeft, num5 - pxLeft));
		int heightPx = Math.Max(0, Math.Min(cachedPixelHeight - pxTop, num6 - pxTop));
		if (widthPx <= 0 || heightPx <= 0)
		{
			return;
		}
		base.Dispatcher.Invoke(delegate
		{
			portalsWb.Lock();
			try
			{
				IntPtr backBuffer = portalsWb.BackBuffer;
				if (backBuffer != IntPtr.Zero)
				{
					int backBufferStride = portalsWb.BackBufferStride;
					for (int i = 0; i < heightPx; i++)
					{
						long num7 = (pxTop + i) * backBufferStride + pxLeft * 4;
						byte* ptr = (byte*)backBuffer.ToPointer() + num7;
						for (int j = 0; j < widthPx; j++)
						{
							ptr[j * 4] = 0;
							ptr[j * 4 + 1] = 0;
							ptr[j * 4 + 2] = 0;
							ptr[j * 4 + 3] = 0;
						}
					}
					portalsWb.AddDirtyRect(new Int32Rect(pxLeft, pxTop, widthPx, heightPx));
				}
			}
			finally
			{
				portalsWb.Unlock();
			}
		});
	}

	private Task PrecomputeTintedCachesAsync()
	{
		if (!playerTintEnabled)
		{
			return Task.CompletedTask;
		}
		Color tint = playerTint;
		return Task.Run(delegate
		{
			try
			{
				uint num = (uint)((tint.A << 24) | (tint.R << 16) | (tint.G << 8) | tint.B);
				foreach (int decorationSpriteId in decorationSpriteIds)
				{
					try
					{
						if (spriteImages != null && decorationSpriteId >= 0 && decorationSpriteId < spriteImages.Length)
						{
							long key = ((long)decorationSpriteId << 32) | num;
							lock (tintedCacheLock)
							{
								if (!tintedSpriteCache.TryGetValue(key, out BitmapSource value) || value == null)
								{
									goto end_IL_00b2;
								}
								goto end_IL_005a;
								end_IL_00b2:;
							}
							if (spriteImages[decorationSpriteId] is BitmapSource source)
							{
								FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0.0);
								int pixelWidth = formatConvertedBitmap.PixelWidth;
								int pixelHeight = formatConvertedBitmap.PixelHeight;
								int num2 = pixelWidth * 4;
								if (pixelWidth > 0 && pixelHeight > 0)
								{
									byte[] array = new byte[pixelHeight * num2];
									formatConvertedBitmap.CopyPixels(array, num2, 0);
									for (int i = 0; i < array.Length; i += 4)
									{
										byte b = array[i];
										byte b2 = array[i + 1];
										byte b3 = array[i + 2];
										if (array[i + 3] != 0 && (b3 != 0 || b2 != 0 || b != 0))
										{
											array[i] = tint.B;
											array[i + 1] = tint.G;
											array[i + 2] = tint.R;
										}
									}
									WriteableBitmap writeableBitmap = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
									writeableBitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array, num2, 0);
									writeableBitmap.Freeze();
									lock (tintedCacheLock)
									{
										tintedSpriteCache.Put(key, writeableBitmap);
									}
								}
							}
						}
						end_IL_005a:;
					}
					catch
					{
					}
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("PrecomputeTintedCachesAsync error: " + ex.Message);
			}
		});
	}

	private unsafe void UpdateSpriteBitmapAtLocked(int x, int y, int spriteIdx, double scale, double pad, int spritePixelW, int spritePixelH, DpiScale dpi)
	{
		if (spritesWb == null || spriteImages == null || spriteImages == null || spriteImages == null || spriteImages == null || spriteImages == null || spriteIdx < 0 || spriteIdx >= spriteImages.Length)
		{
			return;
		}
		currentSpritePositionKey = y * mapWidth + x;
		if ((spriteIdx == 11 || spriteIdx == 31 || spriteIdx == 41 || spriteIdx == 5 || spriteIdx == 6 || spriteIdx == 39 || spriteIdx == 40 || spriteIdx == 68 || spriteIdx == 122 || spriteIdx == 7 || spriteIdx == 26 || spriteIdx == 27 || spriteIdx == 54 || spriteIdx == 73 || spriteIdx == 74) && animationFrame % 60 == 0)
		{
			Debug.WriteLine($"UpdateSpriteBitmapAtLocked: Updating animated sprite 0x{spriteIdx:X2} at ({x},{y}), previewMode={previewMode}");
		}
		try
		{
			int num = (int)Math.Round(pad * dpi.DpiScaleX);
			int num2 = (int)Math.Round(pad * dpi.DpiScaleY);
			int num3 = Math.Max(0, num + x * spritePixelW);
			int num4 = Math.Max(0, num2 + y * spritePixelH);
			int key = y * mapWidth + x;
			bool flag = false;
			(int, int) value2;
			if (spritePixelOffsets.TryGetValue(key, out (int, int) value))
			{
				int num5 = (int)Math.Round((double)value.Item1 * scale * dpi.DpiScaleX);
				int num6 = (int)Math.Round((double)value.Item2 * scale * dpi.DpiScaleY);
				num3 += num5;
				num4 += num6;
				flag = true;
			}
			else if (spriteAnchors != null && spriteAnchors.TryGetValue(key, out value2))
			{
				int key2 = value2.Item2 * mapWidth + value2.Item1;
				if (spritePixelOffsets.TryGetValue(key2, out (int, int) value3))
				{
					int num7 = (int)Math.Round((double)value3.Item1 * scale * dpi.DpiScaleX);
					int num8 = (int)Math.Round((double)value3.Item2 * scale * dpi.DpiScaleY);
					num3 += num7;
					num4 += num8;
					flag = true;
				}
			}
			if (previewMode && !flag && spriteIdx == 45)
			{
				double num9 = 16.0 * scale * dpi.DpiScaleY;
				int num10 = (int)Math.Round(num9 * 0.5);
				num4 = Math.Max(0, num4 - num10);
			}
			if (previewMode && !flag && spriteIdx == 43)
			{
				try
				{
					int num11 = (int)Math.Round((double)spritePixelH * 0.5);
					num4 = Math.Max(0, num4 - num11);
				}
				catch
				{
				}
			}
			if (previewMode && !flag && spriteIdx == 42)
			{
				try
				{
					num4 = Math.Max(0, num4 - spritePixelH);
				}
				catch
				{
				}
			}
			if (previewMode && !flag && (spriteIdx == 32 || spriteIdx == 33))
			{
				try
				{
					int num12 = (int)Math.Round(6.0 * dpi.DpiScaleY);
					num4 = Math.Max(0, num4 - num12);
				}
				catch
				{
				}
			}
			if (previewMode && (spriteIdx == 103 || spriteIdx == 104))
			{
				try
				{
					num4 = Math.Max(0, num4 - spritePixelH);
				}
				catch
				{
				}
			}
			if (num3 >= cachedPixelWidth || num4 >= cachedPixelHeight)
			{
				return;
			}
			int animatedSpriteIndex = GetAnimatedSpriteIndex(spriteIdx);
			BitmapSource bitmapSource = null;
			if (spriteIdx == 0)
			{
				Debug.WriteLine($"Sprite 0x00: animatedIdx={animatedSpriteIndex}, previewMode={previewMode}");
			}
			if (animatedSpriteIndex >= 2000)
			{
				bitmapSource = ((spriteIdx != 100 && spriteIdx != 126) ? GetCustomAnimationSprite(animatedSpriteIndex) : GetPortalSpriteForId(spriteIdx, currentSpritePositionKey));
				if (bitmapSource != null && (spriteIdx == 82 || spriteIdx == 83 || spriteIdx == 10 || spriteIdx == 12 || spriteIdx == 13 || spriteIdx == 14 || spriteIdx == 37 || spriteIdx == 38 || spriteIdx == 122 || spriteIdx == 7 || spriteIdx == 26 || spriteIdx == 27 || spriteIdx == 54 || spriteIdx == 73 || spriteIdx == 74))
				{
					try
					{
						if (animationFrame % 30 == 0)
						{
							byte[] array = new byte[4];
							bitmapSource.CopyPixels(new Int32Rect(0, 0, Math.Max(1, Math.Min(1, bitmapSource.PixelWidth)), Math.Max(1, Math.Min(1, bitmapSource.PixelHeight))), array, 4, 0);
							string value4 = BitConverter.ToString(array);
							Debug.WriteLine($"PAD_SAMPLE ({x},{y}) sprite=0x{spriteIdx:X2} animatedIdx={animatedSpriteIndex} sample={value4}");
						}
					}
					catch (Exception ex)
					{
						Debug.WriteLine($"PAD_SAMPLE error at ({x},{y}) sprite=0x{spriteIdx:X2}: {ex.Message}");
					}
				}
				if (spriteIdx == 0)
				{
					Debug.WriteLine("Sprite 0x00: customSprite=" + ((bitmapSource != null) ? $"{bitmapSource.PixelWidth}x{bitmapSource.PixelHeight}" : "null"));
				}
			}
			if (bitmapSource == null)
			{
				bitmapSource = spriteImages[spriteIdx] as BitmapSource;
			}
			BitmapSource bitmapSource2 = null;
			if (previewMode && playerTintEnabled && decorationSpriteIds.Contains(spriteIdx) && bitmapSource != null)
			{
				uint num13 = (uint)((playerTint.A << 24) | (playerTint.R << 16) | (playerTint.G << 8) | playerTint.B);
				long key3 = ((long)((animatedSpriteIndex >= 2000) ? animatedSpriteIndex : spriteIdx) << 32) | num13;
				LruCache<long, BitmapSource> lruCache = ((animatedSpriteIndex >= 2000) ? tintedCustomCache : tintedSpriteCache);
				if (lruCache.TryGetValue(key3, out BitmapSource value5) && value5 != null)
				{
					bitmapSource2 = value5;
					bitmapSource = bitmapSource2;
				}
				else
				{
					try
					{
						FormatConvertedBitmap formatConvertedBitmap = new FormatConvertedBitmap(bitmapSource, PixelFormats.Bgra32, null, 0.0);
						int pixelWidth = formatConvertedBitmap.PixelWidth;
						int pixelHeight = formatConvertedBitmap.PixelHeight;
						int num14 = pixelWidth * 4;
						byte[] array2 = new byte[pixelHeight * num14];
						formatConvertedBitmap.CopyPixels(array2, num14, 0);
						for (int i = 0; i < array2.Length; i += 4)
						{
							byte b = array2[i];
							byte b2 = array2[i + 1];
							byte b3 = array2[i + 2];
							if (array2[i + 3] != 0 && (b3 != 0 || b2 != 0 || b != 0))
							{
								array2[i] = playerTint.B;
								array2[i + 1] = playerTint.G;
								array2[i + 2] = playerTint.R;
							}
						}
						WriteableBitmap writeableBitmap = new WriteableBitmap(pixelWidth, pixelHeight, formatConvertedBitmap.DpiX, formatConvertedBitmap.DpiY, PixelFormats.Bgra32, null);
						writeableBitmap.WritePixels(new Int32Rect(0, 0, pixelWidth, pixelHeight), array2, num14, 0);
						writeableBitmap.Freeze();
						lruCache[key3] = writeableBitmap;
						bitmapSource = writeableBitmap;
					}
					catch
					{
					}
				}
			}
			if (bitmapSource == null || (previewMode && hideColorTriggers && IsColorTriggerSprite(spriteIdx)) || (previewMode && hideInvisibleSprites && IsInvisibleSprite(spriteIdx)))
			{
				return;
			}
			bool flag2 = animatedSpriteIndex >= 3000 && animatedSpriteIndex <= 3039;
			int num15 = spritePixelH;
			int num16 = spritePixelW;
			if (flag2)
			{
				if (animatedSpriteIndex >= 3000 && animatedSpriteIndex <= 3010)
				{
					num16 = spritePixelW * 3 / 2;
					num15 = spritePixelH * 3;
				}
				else if (animatedSpriteIndex >= 3030 && animatedSpriteIndex <= 3033)
				{
					num16 = spritePixelW * 3;
					num15 = spritePixelH * 3 / 2;
				}
				else
				{
					num16 = spritePixelW * 3;
					num15 = spritePixelH * 2;
				}
			}
			if (!flag2 && (animatedSpriteIndex == 2126 || animatedSpriteIndex == 2127))
			{
				num15 = spritePixelH * 3 / 2;
			}
			if (!flag2 && (animatedSpriteIndex == 2136 || animatedSpriteIndex == 2137 || animatedSpriteIndex == 2138 || animatedSpriteIndex == 2139))
			{
				num15 = spritePixelH * 3 / 2;
				num16 = spritePixelW;
			}
			if (!flag2 && (animatedSpriteIndex == 2140 || animatedSpriteIndex == 2141 || animatedSpriteIndex == 2142 || animatedSpriteIndex == 2143))
			{
				num15 = spritePixelH * 2;
				num16 = spritePixelW;
			}
			if (!flag2 && spriteIdx == 101)
			{
				num15 = spritePixelH * 2;
				num16 = spritePixelW;
			}
			int num17 = num3;
			if (!flag2 && (animatedSpriteIndex == 2132 || animatedSpriteIndex == 2133 || animatedSpriteIndex == 2134 || animatedSpriteIndex == 2135))
			{
				num16 = spritePixelW * 3 / 2;
				num15 = spritePixelH;
				if (spriteIdx != 62 && spriteIdx == 63)
				{
					num17 = num3;
				}
			}
			BitmapSource bitmapSource3 = bitmapSource;
			try
			{
				if (bitmapSource.Format != PixelFormats.Bgra32)
				{
					bitmapSource3 = new FormatConvertedBitmap(bitmapSource, PixelFormats.Bgra32, null, 0.0);
				}
			}
			catch
			{
				bitmapSource3 = bitmapSource;
			}
			int pixelWidth2 = bitmapSource3.PixelWidth;
			int pixelHeight2 = bitmapSource3.PixelHeight;
			if (!flag2 && (animatedSpriteIndex == 2132 || animatedSpriteIndex == 2133 || animatedSpriteIndex == 2134 || animatedSpriteIndex == 2135) && spriteIdx == 62)
			{
				try
				{
					num17 = num3 - (int)Math.Round((double)spritePixelW * 0.5);
				}
				catch
				{
					num17 = num3;
				}
			}
			if (pixelWidth2 <= 0 || pixelHeight2 <= 0 || spritePixelW <= 0 || spritePixelH <= 0)
			{
				Debug.WriteLine($"Invalid dimensions: src={pixelWidth2}x{pixelHeight2}, dest={spritePixelW}x{spritePixelH}");
				return;
			}
			int num18 = pixelWidth2 * 4;
			byte[] array3 = new byte[pixelHeight2 * num18];
			bitmapSource3.CopyPixels(array3, num18, 0);
			IntPtr backBuffer = spritesWb.BackBuffer;
			if (backBuffer == IntPtr.Zero)
			{
				return;
			}
			int backBufferStride = spritesWb.BackBufferStride;
			int num19 = Math.Max(0, num17);
			int num20 = num19 - num17;
			int num21 = Math.Min(num16 - num20, Math.Max(0, cachedPixelWidth - num19));
			int num22 = Math.Min(num15, Math.Max(0, cachedPixelHeight - num4));
			if (num21 <= 0 || num22 <= 0)
			{
				return;
			}
			if (!flag2 && previewMode && portalsWb != null)
			{
				try
				{
					int backBufferStride2 = portalsWb.BackBufferStride;
					if (backBufferStride2 <= 0)
					{
						throw new Exception("invalid portal stride");
					}
					byte[] array4 = new byte[num22 * backBufferStride2];
					Int32Rect sourceRect = new Int32Rect(num19, num4, num21, num22);
					portalsWb.CopyPixels(sourceRect, array4, backBufferStride2, 0);
					IntPtr backBuffer2 = spritesWb.BackBuffer;
					int backBufferStride3 = spritesWb.BackBufferStride;
					if (backBuffer2 != IntPtr.Zero && backBufferStride3 > 0)
					{
						int val = Math.Max(0, backBufferStride3 - num19 * 4);
						for (int j = 0; j < num22; j++)
						{
							long num23 = (num4 + j) * backBufferStride3 + num19 * 4;
							byte* ptr = (byte*)backBuffer2.ToPointer() + num23;
							int num24 = j * backBufferStride2;
							int val2 = Math.Min(num21 * 4, array4.Length - num24);
							val2 = Math.Min(val2, val);
							if (val2 > 0)
							{
								for (int k = 0; k < val2; k++)
								{
									ptr[k] = array4[num24 + k];
								}
							}
						}
					}
				}
				catch
				{
				}
			}
			bool flag3 = true;
			if (flag2)
			{
				flag3 = true;
			}
			else if (previewMode)
			{
				bool flag4 = false;
				for (int l = Math.Max(0, y - 2); l <= Math.Min(mapHeight - 1, y); l++)
				{
					if (flag4)
					{
						break;
					}
					for (int m = Math.Max(0, x - 1); m <= Math.Min(mapWidth - 1, x); m++)
					{
						if (flag4)
						{
							break;
						}
						if (m != x || l != y)
						{
							int spriteIdx2 = sprites[l * mapWidth + m];
							if (IsPortalSprite(spriteIdx2))
							{
								flag4 = true;
							}
						}
					}
				}
				flag3 = !flag4;
			}
			if (flag3)
			{
				try
				{
					int val3 = Math.Max(0, backBufferStride / 4 - num19);
					int num25 = Math.Min(num22, Math.Max(0, cachedPixelHeight - num4));
					int val4 = Math.Min(num21, Math.Max(0, cachedPixelWidth - num19));
					for (int n = 0; n < num25; n++)
					{
						long num26 = (num4 + n) * backBufferStride + num19 * 4;
						byte* ptr2 = (byte*)backBuffer.ToPointer() + num26;
						int num27 = Math.Min(val4, val3);
						if (num27 > 0)
						{
							for (int num28 = 0; num28 < num27; num28++)
							{
								int num29 = num28 * 4;
								ptr2[num29] = 0;
								ptr2[num29 + 1] = 0;
								ptr2[num29 + 2] = 0;
								ptr2[num29 + 3] = 0;
							}
						}
					}
				}
				catch
				{
				}
			}
			if (flag2 && portalsWb != null)
			{
				UpdatePortalBitmapAtLocked(x, y, spriteIdx, scale, pad, spritePixelW, spritePixelH, dpi);
				return;
			}
			if (Math.Abs(scale - 1.0) < 0.001 && Math.Abs(dpi.DpiScaleX - 1.0) < 0.001 && pixelWidth2 == 16 && !flag2 && animatedSpriteIndex < 2000)
			{
				int num30 = Math.Min(pixelWidth2, cachedPixelWidth - num3);
				int num31 = Math.Min(pixelHeight2, cachedPixelHeight - num4);
				for (int num32 = 0; num32 < num31; num32++)
				{
					int num33 = num32 * num18;
					long num34 = (num4 + num32) * backBufferStride + num3 * 4;
					byte* ptr3 = (byte*)backBuffer.ToPointer() + num34;
					for (int num35 = 0; num35 < num30; num35++)
					{
						int num36 = num35 * 4;
						byte b4 = array3[num33 + num36 + 3];
						if (b4 == byte.MaxValue)
						{
							byte b5 = array3[num33 + num36];
							byte b6 = array3[num33 + num36 + 1];
							byte b7 = array3[num33 + num36 + 2];
							if (previewMode && playerTintEnabled && decorationSpriteIds.Contains(spriteIdx) && !nonPlayerTintSpriteIds.Contains(spriteIdx) && (b7 != 0 || b6 != 0 || b5 != 0))
							{
								ptr3[num36] = playerTint.B;
								ptr3[num36 + 1] = playerTint.G;
								ptr3[num36 + 2] = playerTint.R;
								ptr3[num36 + 3] = array3[num33 + num36 + 3];
							}
							else
							{
								ptr3[num36] = b5;
								ptr3[num36 + 1] = b6;
								ptr3[num36 + 2] = b7;
								ptr3[num36 + 3] = array3[num33 + num36 + 3];
							}
						}
						else
						{
							if (b4 <= 0)
							{
								continue;
							}
							byte b8 = ptr3[num36 + 3];
							byte b9 = array3[num33 + num36];
							byte b10 = array3[num33 + num36 + 1];
							byte b11 = array3[num33 + num36 + 2];
							if (previewMode && playerTintEnabled && decorationSpriteIds.Contains(spriteIdx) && !nonPlayerTintSpriteIds.Contains(spriteIdx) && (b11 != 0 || b10 != 0 || b9 != 0))
							{
								b9 = playerTint.B;
								b10 = playerTint.G;
								b11 = playerTint.R;
							}
							if (b8 == 0)
							{
								ptr3[num36] = b9;
								ptr3[num36 + 1] = b10;
								ptr3[num36 + 2] = b11;
								ptr3[num36 + 3] = array3[num33 + num36 + 3];
								continue;
							}
							float num37 = (float)(int)b4 / 255f;
							float num38 = (float)(int)b8 / 255f;
							float num39 = num37 + num38 * (1f - num37);
							if (num39 > 0f)
							{
								ptr3[num36] = (byte)(((float)(int)b9 * num37 + (float)(int)ptr3[num36] * num38 * (1f - num37)) / num39);
								ptr3[num36 + 1] = (byte)(((float)(int)b10 * num37 + (float)(int)ptr3[num36 + 1] * num38 * (1f - num37)) / num39);
								ptr3[num36 + 2] = (byte)(((float)(int)b11 * num37 + (float)(int)ptr3[num36 + 2] * num38 * (1f - num37)) / num39);
								ptr3[num36 + 3] = (byte)(num39 * 255f);
							}
						}
					}
				}
			}
			else
			{
				double num40 = (double)num16 / (double)pixelWidth2;
				double num41 = (double)num15 / (double)pixelHeight2;
				int num42 = Math.Min(num16, cachedPixelWidth - num3);
				int num43 = Math.Min(num15, cachedPixelHeight - num4);
				if (flag2)
				{
				}
				for (int num44 = 0; num44 < num43; num44++)
				{
					for (int num45 = 0; num45 < num42; num45++)
					{
						int num46 = Math.Min((int)((double)(num45 + num20) / num40), pixelWidth2 - 1);
						int num47 = Math.Min((int)((double)num44 / num41), pixelHeight2 - 1);
						int num48 = num47 * num18 + num46 * 4;
						long num49 = (num4 + num44) * backBufferStride + (num19 + num45) * 4;
						byte* ptr4 = (byte*)backBuffer.ToPointer() + num49;
						byte b12 = array3[num48 + 3];
						if (flag2)
						{
							*ptr4 = array3[num48];
							ptr4[1] = array3[num48 + 1];
							ptr4[2] = array3[num48 + 2];
							ptr4[3] = array3[num48 + 3];
						}
						else if (b12 == byte.MaxValue)
						{
							byte b13 = array3[num48];
							byte b14 = array3[num48 + 1];
							byte b15 = array3[num48 + 2];
							if (previewMode && playerTintEnabled && decorationSpriteIds.Contains(spriteIdx) && !nonPlayerTintSpriteIds.Contains(spriteIdx) && (b15 != 0 || b14 != 0 || b13 != 0))
							{
								*ptr4 = playerTint.B;
								ptr4[1] = playerTint.G;
								ptr4[2] = playerTint.R;
								ptr4[3] = array3[num48 + 3];
							}
							else
							{
								*ptr4 = b13;
								ptr4[1] = b14;
								ptr4[2] = b15;
								ptr4[3] = array3[num48 + 3];
							}
						}
						else
						{
							if (b12 <= 0)
							{
								continue;
							}
							byte b16 = ptr4[3];
							byte b17 = array3[num48];
							byte b18 = array3[num48 + 1];
							byte b19 = array3[num48 + 2];
							if (previewMode && playerTintEnabled && decorationSpriteIds.Contains(spriteIdx) && !nonPlayerTintSpriteIds.Contains(spriteIdx) && (b19 != 0 || b18 != 0 || b17 != 0))
							{
								b17 = playerTint.B;
								b18 = playerTint.G;
								b19 = playerTint.R;
							}
							if (b16 == 0)
							{
								*ptr4 = b17;
								ptr4[1] = b18;
								ptr4[2] = b19;
								ptr4[3] = array3[num48 + 3];
								continue;
							}
							float num50 = (float)(int)b12 / 255f;
							float num51 = (float)(int)b16 / 255f;
							float num52 = num50 + num51 * (1f - num50);
							if (num52 > 0f)
							{
								*ptr4 = (byte)(((float)(int)b17 * num50 + (float)(int)(*ptr4) * num51 * (1f - num50)) / num52);
								ptr4[1] = (byte)(((float)(int)b18 * num50 + (float)(int)ptr4[1] * num51 * (1f - num50)) / num52);
								ptr4[2] = (byte)(((float)(int)b19 * num50 + (float)(int)ptr4[2] * num51 * (1f - num50)) / num52);
								ptr4[3] = (byte)(num52 * 255f);
							}
						}
					}
				}
			}
			if (previewMode || !spritePixelOffsets.ContainsKey(key))
			{
				return;
			}
			try
			{
				int num53 = Math.Max(4, (int)Math.Round(6.0 * scale * dpi.DpiScaleX));
				int num54 = num3 + spritePixelW - num53 - 2;
				int num55 = num4 + spritePixelH - num53 - 2;
				for (int num56 = 0; num56 < num53; num56++)
				{
					int num57 = num55 + num56;
					if (num57 < 0 || num57 >= cachedPixelHeight)
					{
						continue;
					}
					for (int num58 = 0; num58 < num53; num58++)
					{
						int num59 = num54 + num58;
						if (num59 >= 0 && num59 < cachedPixelWidth)
						{
							long num60 = num57 * backBufferStride + num59 * 4;
							byte* ptr5 = (byte*)backBuffer.ToPointer() + num60;
							*ptr5 = byte.MaxValue;
							ptr5[1] = byte.MaxValue;
							ptr5[2] = byte.MaxValue;
							ptr5[3] = byte.MaxValue;
						}
					}
				}
			}
			catch
			{
			}
		}
		catch (Exception ex2)
		{
			Debug.WriteLine($"ERROR rendering sprite at ({x},{y}), idx={spriteIdx}: {ex2.Message}");
		}
	}

	private unsafe void UpdatePortalBitmapAtLocked(int x, int y, int spriteIdx, double scale, double pad, int spritePixelW, int spritePixelH, DpiScale dpi)
	{
		if (spriteImages == null || portalsWb == null || spriteIdx < 0 || spriteIdx >= spriteImages.Length || !IsPortalSprite(spriteIdx))
		{
			return;
		}
		int num = y * mapWidth + x;
		BitmapSource portalSpriteForId = GetPortalSpriteForId(spriteIdx, num);
		if (portalSpriteForId == null)
		{
			Debug.WriteLine($"WARNING: Portal sprite null for idx=0x{spriteIdx:X2} at ({x},{y})");
			return;
		}
		int pixelWidth = portalSpriteForId.PixelWidth;
		int pixelHeight = portalSpriteForId.PixelHeight;
		if (pixelWidth <= 0 || pixelHeight <= 0)
		{
			Debug.WriteLine($"WARNING: Portal sprite invalid size for idx=0x{spriteIdx:X2}: {pixelWidth}x{pixelHeight}");
			return;
		}
		int num2 = pixelWidth * 4;
		byte[] array = new byte[pixelHeight * num2];
		portalSpriteForId.CopyPixels(array, num2, 0);
		int num3 = (int)Math.Round(pad * dpi.DpiScaleX);
		int num4 = (int)Math.Round(pad * dpi.DpiScaleY);
		int num5 = Math.Max(0, num3 + x * spritePixelW);
		int num6 = Math.Max(0, num4 + y * spritePixelH);
		(int, int) value2;
		if (spritePixelOffsets.TryGetValue(num, out (int, int) value))
		{
			int num7 = (int)Math.Round((double)value.Item1 * scale * dpi.DpiScaleX);
			int num8 = (int)Math.Round((double)value.Item2 * scale * dpi.DpiScaleY);
			num5 += num7;
			num6 += num8;
		}
		else if (spriteAnchors != null && spriteAnchors.TryGetValue(num, out value2))
		{
			int key = value2.Item2 * mapWidth + value2.Item1;
			if (spritePixelOffsets.TryGetValue(key, out (int, int) value3))
			{
				int num9 = (int)Math.Round((double)value3.Item1 * scale * dpi.DpiScaleX);
				int num10 = (int)Math.Round((double)value3.Item2 * scale * dpi.DpiScaleY);
				num5 += num9;
				num6 += num10;
			}
		}
		if (previewMode && (spriteIdx == 16 || spriteIdx == 18))
		{
			num6 = Math.Max(0, num6 - spritePixelH);
		}
		if (previewMode && (spriteIdx == 103 || spriteIdx == 104))
		{
			num6 = Math.Max(0, num6 - spritePixelH);
		}
		int num11 = spritePixelW;
		int num12 = spritePixelH;
		int animatedSpriteIndex = GetAnimatedSpriteIndex(spriteIdx);
		bool flag = animatedSpriteIndex >= 3000 && animatedSpriteIndex <= 3039;
		bool flag2 = animatedSpriteIndex == 3027 || animatedSpriteIndex == 3028;
		if (flag)
		{
			if (animatedSpriteIndex >= 3000 && animatedSpriteIndex <= 3010)
			{
				num11 = spritePixelW * 3 / 2;
				num12 = spritePixelH * 3;
			}
			else if (animatedSpriteIndex >= 3011 && animatedSpriteIndex <= 3014)
			{
				num11 = spritePixelW * 3;
				num12 = spritePixelH * 2;
			}
			else if (flag2)
			{
				num11 = Math.Min(cachedPixelWidth, (int)Math.Round((double)spritePixelW * (double)pixelWidth / 16.0));
				num12 = spritePixelH * 2;
			}
			else if (animatedSpriteIndex >= 3030 && animatedSpriteIndex <= 3033)
			{
				num11 = spritePixelW * 3;
				num12 = spritePixelH * 3 / 2;
			}
			else if (animatedSpriteIndex == 3026)
			{
				num11 = spritePixelW * 3 / 2;
				num12 = spritePixelH * 2;
			}
			else if (animatedSpriteIndex == 3024 || animatedSpriteIndex == 3025 || animatedSpriteIndex == 3029)
			{
				num11 = spritePixelW;
				num12 = spritePixelH * 2;
			}
			else
			{
				num11 = spritePixelW * 3 / 2;
				num12 = spritePixelH * 3;
			}
		}
		int num13 = Math.Min(num11, cachedPixelWidth - num5);
		int num14 = Math.Min(num12, cachedPixelHeight - num6);
		portalsWb.Lock();
		try
		{
			IntPtr backBuffer = portalsWb.BackBuffer;
			if (backBuffer == IntPtr.Zero)
			{
				return;
			}
			int backBufferStride = portalsWb.BackBufferStride;
			for (int i = 0; i < num14; i++)
			{
				for (int j = 0; j < num13; j++)
				{
					int num15 = Math.Min((int)((double)(j * pixelWidth) / (double)num11), pixelWidth - 1);
					int num16 = Math.Min((int)((double)(i * pixelHeight) / (double)num12), pixelHeight - 1);
					int num17 = num16 * num2 + num15 * 4;
					long num18 = (num6 + i) * backBufferStride + (num5 + j) * 4;
					byte* ptr = (byte*)backBuffer.ToPointer() + num18;
					byte b = array[num17];
					byte b2 = array[num17 + 1];
					byte b3 = array[num17 + 2];
					byte b4 = array[num17 + 3];
					byte b5 = *ptr;
					byte b6 = ptr[1];
					byte b7 = ptr[2];
					byte b8 = ptr[3];
					if (b4 == 0)
					{
						continue;
					}
					if (b4 == byte.MaxValue || b8 == 0)
					{
						*ptr = b;
						ptr[1] = b2;
						ptr[2] = b3;
						ptr[3] = b4;
						continue;
					}
					float num19 = (float)(int)b4 / 255f;
					float num20 = (float)(int)b8 / 255f;
					float num21 = num19 + num20 * (1f - num19);
					if (num21 <= 0f)
					{
						*ptr = 0;
						ptr[1] = 0;
						ptr[2] = 0;
						ptr[3] = 0;
					}
					else
					{
						*ptr = (byte)(((float)(int)b * num19 + (float)(int)b5 * num20 * (1f - num19)) / num21);
						ptr[1] = (byte)(((float)(int)b2 * num19 + (float)(int)b6 * num20 * (1f - num19)) / num21);
						ptr[2] = (byte)(((float)(int)b3 * num19 + (float)(int)b7 * num20 * (1f - num19)) / num21);
						ptr[3] = (byte)(num21 * 255f);
					}
				}
			}
		}
		finally
		{
			if (portalsWb != null)
			{
				try
				{
					portalsWb.AddDirtyRect(new Int32Rect(num5, num6, num13, num14));
				}
				catch
				{
				}
				try
				{
					portalsWb.Unlock();
				}
				catch
				{
				}
			}
		}
	}

	private bool IsColorTriggerSprite(int spriteIdx)
	{
		if (spriteIdx >= 128 && spriteIdx <= 140)
		{
			return true;
		}
		if (spriteIdx == 143)
		{
			return true;
		}
		if (spriteIdx >= 144 && spriteIdx <= 156)
		{
			return true;
		}
		if (spriteIdx == 159)
		{
			return true;
		}
		if (spriteIdx >= 160 && spriteIdx <= 172)
		{
			return true;
		}
		if (spriteIdx >= 174 && spriteIdx <= 175)
		{
			return true;
		}
		if (spriteIdx >= 176 && spriteIdx <= 191)
		{
			return true;
		}
		if (spriteIdx >= 192 && spriteIdx <= 204)
		{
			return true;
		}
		if (spriteIdx == 207)
		{
			return true;
		}
		if (spriteIdx >= 208 && spriteIdx <= 220)
		{
			return true;
		}
		if (spriteIdx >= 224 && spriteIdx <= 236)
		{
			return true;
		}
		return false;
	}

	private bool IsInvisibleSprite(int spriteIdx)
	{
		switch (spriteIdx)
		{
		case 15:
			return true;
		default:
			if (spriteIdx != 72)
			{
				if (spriteIdx == 111)
				{
					return true;
				}
				if (spriteIdx >= 112 && spriteIdx <= 120)
				{
					return true;
				}
				if (spriteIdx == 125)
				{
					return true;
				}
				if (spriteIdx == 127)
				{
					return true;
				}
				if (spriteIdx == 142)
				{
					return true;
				}
				if (spriteIdx == 158)
				{
					return true;
				}
				if (spriteIdx >= 221 && spriteIdx <= 223)
				{
					return true;
				}
				if (spriteIdx == 237)
				{
					return true;
				}
				if (spriteIdx >= 238 && spriteIdx <= 239)
				{
					return true;
				}
				if (spriteIdx >= 240 && spriteIdx <= 252)
				{
					return true;
				}
				return false;
			}
			goto case 71;
		case 71:
			return true;
		}
	}

	private void CanvasHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (CanvasHost == null)
		{
			return;
		}
		Point position = e.GetPosition(CanvasHost);
		try
		{
			if (isResizingSelection)
			{
				UpdateResizePreview(position);
				return;
			}
		}
		catch
		{
		}
		mouseDownPosition = position;
		try
		{
			(int, int) tuple = ViewportPointToTile(position);
			if (IsPointInsideMap(position))
			{
				(lastClickX, lastClickY) = tuple;
			}
			else
			{
				lastClickX = -1;
				lastClickY = -1;
			}
		}
		catch
		{
			lastClickX = -1;
			lastClickY = -1;
		}
		hasMouseMoved = false;
		if (FindName("StartPosTool") is ToggleButton { IsChecked: var isChecked } && isChecked == true)
		{
			try
			{
				double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
				double num2 = mapViewportPadding;
				int num3 = (int)((position.X - num2) / num) - 8;
				int num4 = (int)((position.Y - num2 - gridRenderShiftY) / num) - 48 - 8;
				if (num3 < 0)
				{
					num3 = 0;
				}
				int num5 = mapWidth * 16;
				if (num3 >= num5 - 16)
				{
					num3 = num5 - 16;
				}
				int num6 = ((groundBitmap != null && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
				int num7 = (mapHeight - num6) * 16;
				if (num4 >= num7)
				{
					num4 = num7 - 16;
				}
				SetStartPosMarker(num3, num4);
				return;
			}
			catch
			{
				return;
			}
		}
		if (MagicWandTool != null && MagicWandTool.IsChecked == true)
		{
			StartMagicWandAt(position);
			return;
		}
		if (SelectTool != null && SelectTool.IsChecked == true && currentSelectMode == SelectMode.AllSame)
		{
			var (num8, num9) = ViewportPointToTile(position);
			if (num8 < 0 || num9 < 0)
			{
				return;
			}
			int num10 = num9 * mapWidth + num8;
			bool flag = tilesLayerActive && tiles[num10] != -1;
			int num11 = (flag ? tiles[num10] : ((spritesLayerActive && sprites[num10] != -1) ? sprites[num10] : (-1)));
			if (num11 == -1)
			{
				return;
			}
			IndexInfo value = null;
			if (flag)
			{
				tileIndexMap.TryGetValue(num11, out value);
			}
			else
			{
				spriteIndexMap.TryGetValue(num11, out value);
			}
			if (value == null || value.Count == 0)
			{
				return;
			}
			bool flag2 = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
			if (!flag2)
			{
				selectionSet.Clear();
			}
			if (value.Count > 5000 && !flag2)
			{
				selectionSet.Clear();
				selX = value.MinX;
				selY = value.MinY;
				selW = value.MaxX - value.MinX + 1;
				selH = value.MaxY - value.MinY + 1;
				selectionIsLarge = true;
				pendingHoverIndicesRef = value.Indices;
				UpdateSelectionVisuals(selX, selY, selW, selH, previewMode: true);
				try
				{
					RenderMaskToCanvasAsync(SelectionOverlay, pendingHoverIndicesRef, (ZoomSlider != null) ? ZoomSlider.Value : 1.0, VisualTreeHelper.GetDpi(this));
				}
				catch
				{
				}
			}
			else
			{
				if (!flag2 && selectionSet.Count == 0)
				{
					selectionSet = new HashSet<int>(value.Indices);
				}
				else
				{
					int[] indices = value.Indices;
					foreach (int item in indices)
					{
						selectionSet.Add(item);
					}
				}
				selectionIsLarge = selectionSet.Count > 5000;
			}
			if (selectionSet.Count == 0)
			{
				ClearSelection();
				return;
			}
			int num12 = int.MaxValue;
			int num13 = int.MaxValue;
			int num14 = int.MinValue;
			int num15 = int.MinValue;
			foreach (int item5 in selectionSet)
			{
				int num16 = item5 % mapWidth;
				int num17 = item5 / mapWidth;
				if (num16 < num12)
				{
					num12 = num16;
				}
				if (num17 < num13)
				{
					num13 = num17;
				}
				if (num16 > num14)
				{
					num14 = num16;
				}
				if (num17 > num15)
				{
					num15 = num17;
				}
			}
			selX = num12;
			selY = num13;
			selW = num14 - num12 + 1;
			selH = num15 - num13 + 1;
			if (selectionSet.Count > 5000)
			{
				selTiles = null;
				selSprites = null;
				selectionIsLarge = true;
			}
			else
			{
				selectionIsLarge = false;
				selTiles = new int[selW * selH];
				selSprites = new int[selW * selH];
				for (int j = 0; j < selH; j++)
				{
					for (int k = 0; k < selW; k++)
					{
						int num18 = (selY + j) * mapWidth + (selX + k);
						if (selectionSet.Contains(num18))
						{
							selTiles[j * selW + k] = (tilesLayerActive ? tiles[num18] : (-1));
							selSprites[j * selW + k] = (spritesLayerActive ? sprites[num18] : (-1));
						}
						else
						{
							selTiles[j * selW + k] = -1;
							selSprites[j * selW + k] = -1;
						}
					}
				}
			}
			UpdateSelectionVisuals(selX, selY, selW, selH);
			if (StatusText != null)
			{
				StatusText.Text = $"Selected items: {selectionSet.Count} (bbox {selW}x{selH} at {selX},{selY})";
			}
			return;
		}
		if (SelectTool != null && SelectTool.IsChecked == true && (currentSelectMode == SelectMode.Normal || currentSelectMode == SelectMode.Ellipse))
		{
			if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
			{
				ToggleSelectionAt(position);
				return;
			}
			int val;
			int val2;
			(val, val2) = ViewportPointToTile(position);
			if (!IsPointInsideMap(position))
			{
				ClearSelection();
				return;
			}
			val = Math.Max(0, Math.Min(mapWidth - 1, val));
			val2 = Math.Max(0, Math.Min(mapHeight - 1, val2));
			int num19 = val2 * mapWidth + val;
			bool flag3 = tilesLayerActive && tiles[num19] != -1;
			bool flag4 = spritesLayerActive && sprites[num19] != -1;
			if (currentSelectMode == SelectMode.Ellipse)
			{
				selectionSet.Clear();
				selectionSet.Add(num19);
				selX = val;
				selY = val2;
				selW = 1;
				selH = 1;
				selTiles = new int[1] { tilesLayerActive ? tiles[num19] : (-1) };
				selSprites = new int[1] { spritesLayerActive ? sprites[num19] : (-1) };
				UpdateSelectionVisuals(selX, selY, selW, selH);
				pendingSelection = true;
			}
			else if (!flag3 && !flag4)
			{
				ClearSelection();
			}
			else
			{
				selectionSet.Clear();
				selectionSet.Add(num19);
				selX = val;
				selY = val2;
				selW = 1;
				selH = 1;
				selTiles = new int[1] { tilesLayerActive ? tiles[num19] : (-1) };
				selSprites = new int[1] { spritesLayerActive ? sprites[num19] : (-1) };
				UpdateSelectionVisuals(selX, selY, selW, selH);
				pendingSelection = true;
			}
			return;
		}
		if (MoveTool != null && MoveTool.IsChecked == true)
		{
			var (num20, num21) = ViewportPointToTile(position);
			if (selectionSet != null && selectionSet.Count > 0)
			{
				int item2 = num21 * mapWidth + num20;
				if (!selectionSet.Contains(item2) && spritesLayerActive)
				{
					double num22 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
					int num23 = (int)Math.Round((position.X - mapViewportPadding) / num22);
					int num24 = (int)Math.Round((position.Y - mapViewportPadding) / num22);
					bool flag5 = false;
					for (int l = 0; l < selH; l++)
					{
						if (flag5)
						{
							break;
						}
						for (int m = 0; m < selW; m++)
						{
							if (flag5)
							{
								break;
							}
							int num25 = selX + m;
							int num26 = selY + l;
							int num27 = num26 * mapWidth + num25;
							if (selectionSet.Contains(num27) && sprites[num27] != -1)
							{
								int num28 = 0;
								int num29 = 0;
								if (spritePixelOffsets.TryGetValue(num27, out (int, int) value2))
								{
									(num28, num29) = value2;
								}
								int num30 = num25 * 16 + num28;
								int num31 = num26 * 16 + num29;
								int num32 = num30 + 16;
								int num33 = num31 + 16;
								if (num23 >= num30 && num23 < num32 && num24 >= num31 && num24 < num33)
								{
									flag5 = true;
								}
							}
						}
					}
					if (!flag5)
					{
						ClearSelection();
						return;
					}
				}
				pendingDrag = true;
				return;
			}
			int num34 = num20;
			int num35 = num21;
			int num36 = num21 * mapWidth + num20;
			int num37 = (tilesLayerActive ? tiles[num36] : (-1));
			int num38 = (spritesLayerActive ? sprites[num36] : (-1));
			if (spritesLayerActive && num38 == -1)
			{
				double num39 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
				int num40 = (int)Math.Round((position.X - mapViewportPadding) / num39);
				int num41 = (int)Math.Round((position.Y - mapViewportPadding) / num39);
				for (int n = -1; n <= 1; n++)
				{
					for (int num42 = -1; num42 <= 1; num42++)
					{
						int num43 = num20 + num42;
						int num44 = num21 + n;
						if (num43 < 0 || num43 >= mapWidth || num44 < 0 || num44 >= mapHeight)
						{
							continue;
						}
						int num45 = num44 * mapWidth + num43;
						if (sprites[num45] != -1 && spritePixelOffsets.TryGetValue(num45, out (int, int) value3))
						{
							int num46 = num43 * 16 + value3.Item1;
							int num47 = num44 * 16 + value3.Item2;
							int num48 = num46 + 16;
							int num49 = num47 + 16;
							if (num40 >= num46 && num40 < num48 && num41 >= num47 && num41 < num49)
							{
								num34 = num43;
								num35 = num44;
								num38 = sprites[num45];
								num36 = num45;
								break;
							}
						}
					}
					if (num38 != -1)
					{
						break;
					}
				}
			}
			if (num37 == -1 && num38 == -1)
			{
				return;
			}
			if (e.ClickCount >= 2 && num38 != -1 && spritePixelOffsets.ContainsKey(num36))
			{
				spritePixelOffsets.Remove(num36);
				spriteAnchors.Remove(num36);
				try
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
					Redraw();
				}
				SaveCurrentTmxConfig();
			}
			else
			{
				selX = num34;
				selY = num35;
				selW = 1;
				selH = 1;
				selTiles = new int[1] { num37 };
				selSprites = new int[1] { num38 };
				selectionSet.Clear();
				selectionSet.Add(num36);
				UpdateSelectionVisuals(selX, selY, selW, selH);
				pendingDrag = true;
			}
			return;
		}
		if (EraseTool != null && EraseTool.IsChecked == true && selTiles != null && selW > 0 && selH > 0)
		{
			var (num50, num51) = ViewportPointToTile(position);
			if (num50 >= selX && num50 < selX + selW && num51 >= selY && num51 < selY + selH)
			{
				EraseSelectedLayers();
				return;
			}
		}
		if (currentDrawMode != DrawMode.Tile)
		{
			if ((DateTime.Now - lastInputAction).TotalMilliseconds < 200.0)
			{
				return;
			}
			(int, int) tuple8 = ViewportPointToTile(position);
			if (currentDrawMode == DrawMode.Polygon)
			{
				if (IsPointInsideMap(position))
				{
					polygonPoints.Add((tuple8.Item1, tuple8.Item2));
					isConstructingPolygon = true;
					(drawCurrentX, drawCurrentY) = tuple8;
					if (CanvasHost != null)
					{
						CanvasHost.CaptureMouse();
					}
					UpdateDeferredPreview();
					if (e.ClickCount >= 2 && polygonPoints.Count >= 3)
					{
						CommitPolygon();
					}
				}
			}
			else if (IsPointInsideMap(position))
			{
				drawStartX = tuple8.Item1;
				drawStartY = tuple8.Item2;
				drawCurrentX = drawStartX;
				drawCurrentY = drawStartY;
				isDeferredDrawing = true;
				if (CanvasHost != null)
				{
					CanvasHost.CaptureMouse();
				}
				UpdateDeferredPreview();
			}
			return;
		}
		try
		{
			bool flag6 = PlaceTool != null && PlaceTool.IsChecked == true;
			bool flag7 = DrawTileButton != null && DrawTileButton.IsChecked == true;
			if (flag6 && flag7)
			{
				(int, int) tuple10 = ViewportPointToTile(position);
				int item3 = tuple10.Item1;
				int item4 = tuple10.Item2;
				bool flag8 = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
				bool flag9 = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);
				bool flag10 = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
				if (flag8 && flag9)
				{
					currentDrawMode = DrawMode.Circle;
					isDeferredDrawing = true;
					deferredDrawFromModifier = true;
					drawStartX = item3;
					drawStartY = item4;
					drawCurrentX = item3;
					drawCurrentY = item4;
					if (CanvasHost != null)
					{
						CanvasHost.CaptureMouse();
					}
					UpdateDeferredPreview();
					return;
				}
				if (flag8 && flag10)
				{
					currentDrawMode = DrawMode.Square;
					isDeferredDrawing = true;
					deferredDrawFromModifier = true;
					drawStartX = item3;
					drawStartY = item4;
					drawCurrentX = item3;
					drawCurrentY = item4;
					if (CanvasHost != null)
					{
						CanvasHost.CaptureMouse();
					}
					UpdateDeferredPreview();
					return;
				}
				if (flag8 && !flag9 && !flag10)
				{
					currentDrawMode = DrawMode.Line;
					isDeferredDrawing = true;
					deferredDrawFromModifier = true;
					drawStartX = item3;
					drawStartY = item4;
					drawCurrentX = item3;
					drawCurrentY = item4;
					if (CanvasHost != null)
					{
						CanvasHost.CaptureMouse();
					}
					UpdateDeferredPreview();
					return;
				}
				if (flag9 && flag10 && !flag8)
				{
					if (tilesLayerActive && selectedTile >= 0)
					{
						int num52 = tiles[item4 * mapWidth + item3];
						if (num52 != selectedTile)
						{
							FloodFill(item3, item4, num52, selectedTile);
						}
					}
					else if (spritesLayerActive && selectedSprite >= 0)
					{
						int num53 = sprites[item4 * mapWidth + item3];
						if (num53 != selectedSprite)
						{
							SpriteFloodFill(item3, item4, num53, selectedSprite);
						}
					}
					return;
				}
			}
		}
		catch
		{
		}
		StartPaintingAt(position);
	}

	private bool IsPointInsideMap(Point pos)
	{
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		double num2 = mapViewportPadding;
		double num3 = (double)(mapWidth * 16) * num;
		double num4 = (double)(mapHeight * 16) * num;
		if (pos.X < num2 || pos.Y < num2)
		{
			return false;
		}
		if (pos.X > num2 + num3 || pos.Y > num2 + num4)
		{
			return false;
		}
		return true;
	}

	private void CanvasHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (CanvasHost == null)
		{
			return;
		}
		if (isResizingSelection)
		{
			Point position = e.GetPosition(CanvasHost);
			try
			{
				CommitResize(position);
			}
			catch
			{
			}
			isResizeModeActive = false;
			Mouse.OverrideCursor = null;
		}
		else if (isDraggingSelection)
		{
			EndDragMove(e.GetPosition(CanvasHost));
		}
		else if (isSelecting)
		{
			EndSelection();
		}
		else if (isPainting)
		{
			StopPainting();
		}
		else if (isDeferredDrawing)
		{
			Point position2 = e.GetPosition(CanvasHost);
			(drawCurrentX, drawCurrentY) = ViewportPointToTile(position2);
			CommitDeferredDraw();
		}
		else
		{
			if (isConstructingPolygon)
			{
				return;
			}
			if (selectionSet.Count > 0)
			{
				Point position3 = e.GetPosition(CanvasHost);
				(int, int) tuple2 = ViewportPointToTile(position3);
				int item = tuple2.Item1;
				int item2 = tuple2.Item2;
				int item3 = item2 * mapWidth + item;
				if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl) && !selectionSet.Contains(item3) && !hasMouseMoved && ((SelectTool != null && SelectTool.IsChecked == true) || (MagicWandTool != null && MagicWandTool.IsChecked == true) || (MoveTool != null && MoveTool.IsChecked == true)))
				{
					ClearSelection();
				}
			}
			pendingDrag = false;
			pendingSelection = false;
		}
	}

	private void CanvasHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (CanvasHost == null)
		{
			return;
		}
		Point position = e.GetPosition(CanvasHost);
		try
		{
			if (isResizeModeActive)
			{
				if (selW > 0 && selH > 0)
				{
					var (num, num2) = ViewportPointToTile(position);
					if (num >= selX && num < selX + selW && num2 >= selY && num2 < selY + selH)
					{
						Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeNWSE;
					}
					else
					{
						Mouse.OverrideCursor = null;
					}
				}
				else
				{
					Mouse.OverrideCursor = null;
				}
			}
		}
		catch
		{
		}
		try
		{
			if (isMiddlePanning && MapScrollViewer != null && e.MiddleButton == MouseButtonState.Pressed)
			{
				Point position2 = e.GetPosition(MapScrollViewer);
				double num3 = position2.X - middlePanStart.X;
				double num4 = position2.Y - middlePanStart.Y;
				double num5 = panStartHOffset - num3;
				double num6 = panStartVOffset - num4;
				if (num5 < 0.0)
				{
					num5 = 0.0;
				}
				if (num6 < 0.0)
				{
					num6 = 0.0;
				}
				try
				{
					MapScrollViewer.ScrollToHorizontalOffset(num5);
				}
				catch
				{
				}
				try
				{
					MapScrollViewer.ScrollToVerticalOffset(num6);
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
		if (e.LeftButton == MouseButtonState.Pressed && !hasMouseMoved)
		{
			double num7 = position.X - mouseDownPosition.X;
			double num8 = position.Y - mouseDownPosition.Y;
			if (Math.Sqrt(num7 * num7 + num8 * num8) > 3.0)
			{
				hasMouseMoved = true;
				try
				{
					if (pendingSelection)
					{
						pendingSelection = false;
						try
						{
							StartSelectionAt(mouseDownPosition);
						}
						catch
						{
						}
					}
				}
				catch
				{
					pendingSelection = false;
				}
				try
				{
					if (pendingDrag)
					{
						pendingDrag = false;
						if (MoveTool != null && MoveTool.IsChecked == true && selectionSet != null && selectionSet.Count > 0)
						{
							StartDragMove(position);
						}
					}
				}
				catch
				{
					pendingDrag = false;
				}
			}
		}
		if (e.RightButton == MouseButtonState.Pressed && isRightMouseDown && !rightDragStarted)
		{
			double num9 = position.X - rightMouseDownPosition.X;
			double num10 = position.Y - rightMouseDownPosition.Y;
			if (Math.Sqrt(num9 * num9 + num10 * num10) > 4.0)
			{
				rightDragStarted = true;
				try
				{
					StartSelectionAt(rightMouseDownPosition);
				}
				catch
				{
				}
			}
		}
		UpdateCoords(position);
		try
		{
			if (isResizingSelection)
			{
				UpdateResizePreview(position);
				return;
			}
		}
		catch
		{
		}
		try
		{
			if (!previewMode && OffsetGhostContainer != null && OffsetTooltipContainer != null)
			{
				DpiScale dpi = VisualTreeHelper.GetDpi(this);
				double num11 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
				int num12 = Math.Max(1, (int)Math.Ceiling(16.0 * num11 * dpi.DpiScaleX));
				int num13 = Math.Max(1, (int)Math.Ceiling(16.0 * num11 * dpi.DpiScaleY));
				int num14 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
				int num15 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
				int num16 = (int)Math.Round(position.X * dpi.DpiScaleX);
				int num17 = (int)Math.Round(position.Y * dpi.DpiScaleY);
				List<(int, int, (int, int), bool, int, int, int, int)> list = new List<(int, int, (int, int), bool, int, int, int, int)>();
				for (int i = 0; i < sprites.Length; i++)
				{
					int num18 = sprites[i];
					if (num18 != -1)
					{
						int num19 = i % mapWidth;
						int num20 = i / mapWidth;
						int num21 = num19;
						int num22 = num20;
						if (spriteAnchors.TryGetValue(i, out (int, int) value))
						{
							(num21, num22) = value;
						}
						int num23 = num14 + num19 * num12;
						int num24 = num15 + num20 * num13 + gridRenderShiftYPx;
						(int, int) value2;
						bool flag = spritePixelOffsets.TryGetValue(i, out value2);
						(int, int) item = (flag ? value2 : (0, 0));
						int num25 = (int)Math.Round((double)item.Item1 * num11 * dpi.DpiScaleX);
						int num26 = (int)Math.Round((double)item.Item2 * num11 * dpi.DpiScaleY);
						int num27 = num23 + num25;
						int num28 = num24 + num26;
						int num29 = num27 + num12;
						int num30 = num28 + num13;
						int num31 = num14 + num21 * num12;
						int num32 = num15 + num22 * num13 + gridRenderShiftYPx;
						if (num16 >= num27 && num16 < num29 && num17 >= num28 && num17 < num30)
						{
							list.Add((i, num18, item, flag, num23, num24, num27, num28));
						}
					}
				}
				if (list.Count > 0)
				{
					if (!list.Any<(int, int, (int, int), bool, int, int, int, int)>(((int posKey, int spriteId, (int offsetX, int offsetY) offset, bool hasOffset, int origLeftPx, int origTopPx, int shiftedLeftPx, int shiftedTopPx) s) => s.hasOffset))
					{
						if (OffsetGhostContainer != null)
						{
							OffsetGhostContainer.Visibility = Visibility.Collapsed;
						}
						if (OffsetTooltipContainer != null)
						{
							OffsetTooltipContainer.Visibility = Visibility.Collapsed;
						}
						return;
					}
					OffsetGhostContainer.Children.Clear();
					OffsetTooltipContainer.Children.Clear();
					double num33 = (double)list[0].Rest.Item1 / dpi.DpiScaleY;
					double num34 = (double)num12 / dpi.DpiScaleX;
					double height = (double)num13 / dpi.DpiScaleY;
					foreach (var item2 in list)
					{
						double length = (double)item2.Item5 / dpi.DpiScaleX;
						double length2 = (double)item2.Item6 / dpi.DpiScaleY;
						Rectangle element = new Rectangle
						{
							Fill = Brushes.Transparent,
							Stroke = new SolidColorBrush(Color.FromArgb(136, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
							StrokeThickness = 1.0,
							StrokeDashArray = new DoubleCollection { 2.0, 2.0 },
							Width = num34,
							Height = height
						};
						Canvas.SetLeft(element, length);
						Canvas.SetTop(element, length2);
						OffsetGhostContainer.Children.Add(element);
						StackPanel stackPanel = new StackPanel
						{
							Orientation = System.Windows.Controls.Orientation.Horizontal,
							Background = new SolidColorBrush(Color.FromArgb(221, 0, 0, 0))
						};
						if (spriteImages != null && item2.Item2 >= 0 && item2.Item2 < spriteImages.Length && spriteImages[item2.Item2] != null)
						{
							Image element2 = new Image
							{
								Source = spriteImages[item2.Item2],
								Width = 16.0,
								Height = 16.0,
								Margin = new Thickness(2.0)
							};
							stackPanel.Children.Add(element2);
						}
						if (item2.Item4)
						{
							int value3 = item2.Item3.Item1;
							int value4 = item2.Item3.Item2;
							if (spriteAnchors.TryGetValue(item2.Item1, out (int, int) value5))
							{
								int num35 = item2.Item1 % mapWidth;
								int num36 = item2.Item1 / mapWidth;
								int num37 = num35 - value5.Item1;
								int num38 = num36 - value5.Item2;
								value3 = num37 * 16 + item2.Item3.Item1;
								value4 = num38 * 16 + item2.Item3.Item2;
							}
							string text = $"ID:{item2.Item2:X2} Offset: X={value3:+#;-#;0} Y={value4:+#;-#;0}";
							TextBlock element3 = new TextBlock
							{
								Text = text,
								Foreground = Brushes.White,
								Padding = new Thickness(4.0, 2.0, 4.0, 2.0),
								FontSize = 10.0,
								VerticalAlignment = VerticalAlignment.Center
							};
							stackPanel.Children.Add(element3);
						}
						else
						{
							TextBlock element4 = new TextBlock
							{
								Text = "",
								Foreground = Brushes.White,
								Padding = new Thickness(4.0, 2.0, 4.0, 2.0),
								FontSize = 10.0,
								VerticalAlignment = VerticalAlignment.Center,
								Width = 140.0
							};
							stackPanel.Children.Add(element4);
						}
						double num39 = (double)item2.Item7 / dpi.DpiScaleX;
						Canvas.SetLeft(stackPanel, num39 + num34 + 5.0);
						Canvas.SetTop(stackPanel, num33);
						OffsetTooltipContainer.Children.Add(stackPanel);
						num33 += 20.0;
					}
					OffsetGhostContainer.Visibility = Visibility.Visible;
					OffsetTooltipContainer.Visibility = Visibility.Visible;
				}
				else
				{
					OffsetGhostContainer.Visibility = Visibility.Collapsed;
					OffsetTooltipContainer.Visibility = Visibility.Collapsed;
				}
			}
		}
		catch
		{
			if (OffsetGhostContainer != null)
			{
				OffsetGhostContainer.Visibility = Visibility.Collapsed;
			}
			if (OffsetTooltipContainer != null)
			{
				OffsetTooltipContainer.Visibility = Visibility.Collapsed;
			}
		}
		if (isDraggingSelection && (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed))
		{
			UpdateDragMoveTo(position);
			return;
		}
		if (isSelecting && (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed))
		{
			UpdateSelectionTo(position);
			return;
		}
		if (isPainting && e.LeftButton == MouseButtonState.Pressed)
		{
			ContinuePaintingAt(position);
			return;
		}
		if (isDeferredDrawing && e.LeftButton == MouseButtonState.Pressed)
		{
			(drawCurrentX, drawCurrentY) = ViewportPointToTile(position);
			UpdateDeferredPreview();
			return;
		}
		if (!isConstructingPolygon || e.LeftButton != MouseButtonState.Pressed)
		{
			try
			{
				if (PlaceTool != null && PlaceTool.IsChecked == true && spritesLayerActive && selectedSprite >= 0 && spriteImages != null && GhostImage != null)
				{
					DpiScale dpi2 = VisualTreeHelper.GetDpi(this);
					double num40 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
					int num41 = Math.Max(1, (int)Math.Ceiling(16.0 * num40 * dpi2.DpiScaleX));
					int num42 = Math.Max(1, (int)Math.Ceiling(16.0 * num40 * dpi2.DpiScaleY));
					int num43 = (int)Math.Round(mapViewportPadding * dpi2.DpiScaleX);
					int num44 = (int)Math.Round(mapViewportPadding * dpi2.DpiScaleY);
					var (num45, num46) = ViewportPointToTile(position);
					if (num45 >= 0 && num46 >= 0)
					{
						int num47 = num43 + num45 * num41;
						int num48 = num44 + num46 * num42 + gridRenderShiftYPx;
						double length3 = (double)num47 / dpi2.DpiScaleX;
						double length4 = (double)num48 / dpi2.DpiScaleY;
						ImageSource imageSource = ((spriteImages != null && selectedSprite >= 0 && selectedSprite < spriteImages.Length) ? spriteImages[selectedSprite] : null);
						double num49 = (double)num41 / dpi2.DpiScaleX;
						double num50 = (double)num42 / dpi2.DpiScaleY;
						int num51 = 1;
						int num52 = 1;
						if (imageSource is BitmapSource bitmapSource)
						{
							num51 = Math.Max(1, (int)Math.Round((double)bitmapSource.PixelWidth / 16.0));
							num52 = Math.Max(1, (int)Math.Round((double)bitmapSource.PixelHeight / 16.0));
						}
						double width = (double)num51 * num49;
						double height2 = (double)num52 * num50;
						GhostImage.Source = imageSource;
						try
						{
							RenderOptions.SetBitmapScalingMode(GhostImage, BitmapScalingMode.NearestNeighbor);
						}
						catch
						{
						}
						GhostImage.Width = width;
						GhostImage.Height = height2;
						Canvas.SetLeft(GhostImage, length3);
						Canvas.SetTop(GhostImage, length4);
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
					DpiScale dpi3 = VisualTreeHelper.GetDpi(this);
					double num53 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
					int num54 = Math.Max(1, (int)Math.Ceiling(16.0 * num53 * dpi3.DpiScaleX));
					int num55 = Math.Max(1, (int)Math.Ceiling(16.0 * num53 * dpi3.DpiScaleY));
					int num56 = (int)Math.Round(mapViewportPadding * dpi3.DpiScaleX);
					int num57 = (int)Math.Round(mapViewportPadding * dpi3.DpiScaleY);
					var (num58, num59) = ViewportPointToTile(position);
					if (num58 >= 0 && num59 >= 0)
					{
						int num60 = num56 + num58 * num54;
						int num61 = num57 + num59 * num55 + gridRenderShiftYPx;
						double length5 = (double)num60 / dpi3.DpiScaleX;
						double length6 = (double)num61 / dpi3.DpiScaleY;
						double width2 = (double)num54 / dpi3.DpiScaleX;
						double height3 = (double)num55 / dpi3.DpiScaleY;
						GhostImage.Source = tileImages[selectedTile];
						try
						{
							RenderOptions.SetBitmapScalingMode(GhostImage, BitmapScalingMode.NearestNeighbor);
						}
						catch
						{
						}
						GhostImage.Width = width2;
						GhostImage.Height = height3;
						Canvas.SetLeft(GhostImage, length5);
						Canvas.SetTop(GhostImage, length6);
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
				return;
			}
			catch
			{
				if (GhostImage != null)
				{
					GhostImage.Visibility = Visibility.Collapsed;
				}
				return;
			}
		}
		(drawCurrentX, drawCurrentY) = ViewportPointToTile(position);
		UpdateDeferredPreview();
	}

	private void Tool_Checked(object? sender, RoutedEventArgs e)
	{
		if (sender == null || !(sender is ToggleButton toggleButton))
		{
			return;
		}
		ToggleButton toggleButton2 = FindName("LassoTool") as ToggleButton;
		ToggleButton toggleButton3 = FindName("StartPosTool") as ToggleButton;
		List<ToggleButton> list = new List<ToggleButton> { PlaceTool, MoveTool, EraseTool, FillTool, SelectTool, MagicWandTool };
		if (toggleButton2 != null)
		{
			list.Add(toggleButton2);
		}
		ToggleButton toggleButton4 = FindName("StructureTool") as ToggleButton;
		bool flag = toggleButton4 != null && toggleButton == toggleButton4;
		if (toggleButton4 != null)
		{
			list.Add(toggleButton4);
		}
		if (toggleButton3 != null)
		{
			list.Add(toggleButton3);
		}
		foreach (ToggleButton item in list)
		{
			if (item != null && item != toggleButton && (toggleButton == null || toggleButton != toggleButton2 || item != SelectTool))
			{
				item.IsChecked = false;
			}
		}
		try
		{
			if (toggleButton != null && toggleButton == toggleButton2 && SelectTool != null)
			{
				SelectTool.IsChecked = true;
			}
		}
		catch
		{
		}
		if (MenuToolPlace != null)
		{
			MenuToolPlace.IsChecked = toggleButton == PlaceTool;
		}
		if (MenuToolMove != null)
		{
			MenuToolMove.IsChecked = toggleButton == MoveTool;
		}
		if (MenuToolErase != null)
		{
			MenuToolErase.IsChecked = toggleButton == EraseTool;
		}
		if (MenuToolFill != null)
		{
			MenuToolFill.IsChecked = toggleButton == FillTool;
		}
		if (MenuToolSelect != null)
		{
			MenuToolSelect.IsChecked = toggleButton == SelectTool || toggleButton == toggleButton2;
		}
		if (MenuToolWand != null)
		{
			MenuToolWand.IsChecked = toggleButton == MagicWandTool;
		}
		if (MenuToolLasso != null)
		{
			MenuToolLasso.IsChecked = toggleButton == toggleButton2 || (toggleButton == SelectTool && currentSelectMode == SelectMode.Lasso);
		}
		if (FindName("MenuToolStructure") is MenuItem menuItem)
		{
			menuItem.IsChecked = flag;
		}
		if (FindName("MenuToolStartPos") is MenuItem menuItem2)
		{
			menuItem2.IsChecked = toggleButton == toggleButton3;
		}
		MenuItem menuItem3 = FindName("Menu_Select_Normal") as MenuItem;
		MenuItem menuItem4 = FindName("Menu_Select_AllSame") as MenuItem;
		MenuItem menuItem5 = FindName("Menu_Select_Lasso") as MenuItem;
		if (menuItem3 != null)
		{
			menuItem3.IsChecked = toggleButton == SelectTool && currentSelectMode == SelectMode.Normal;
		}
		if (menuItem4 != null)
		{
			menuItem4.IsChecked = toggleButton == SelectTool && currentSelectMode == SelectMode.AllSame;
		}
		if (menuItem5 != null)
		{
			menuItem5.IsChecked = (toggleButton == SelectTool && currentSelectMode == SelectMode.Lasso) || toggleButton == toggleButton2;
		}
		if (toggleButton == PlaceTool || toggleButton == MoveTool || toggleButton == EraseTool || toggleButton == FillTool || toggleButton == SelectTool || toggleButton == MagicWandTool || flag)
		{
			if (DrawTileButton != null)
			{
				DrawTileButton.IsChecked = true;
			}
			currentDrawMode = DrawMode.Tile;
		}
		bool flag2 = toggleButton != PlaceTool && toggleButton != EraseTool;
		if (DrawLineButton != null)
		{
			DrawLineButton.IsEnabled = !flag2;
		}
		if (DrawSquareButton != null)
		{
			DrawSquareButton.IsEnabled = !flag2;
		}
		if (DrawCircleButton != null)
		{
			DrawCircleButton.IsEnabled = !flag2;
		}
		if (DrawEllipseButton != null)
		{
			DrawEllipseButton.IsEnabled = !flag2;
		}
		if (DrawTriangleButton != null)
		{
			DrawTriangleButton.IsEnabled = !flag2;
		}
		if (DrawPolygonButton != null)
		{
			DrawPolygonButton.IsEnabled = !flag2;
		}
		if (HollowCheckBox != null)
		{
			HollowCheckBox.IsEnabled = !flag2;
		}
		if (BrushThicknessSlider != null)
		{
			BrushThicknessSlider.IsEnabled = !flag2;
		}
		try
		{
			ClearSelectSameOverlay();
		}
		catch
		{
		}
		try
		{
			if ((toggleButton2 == null || toggleButton2.IsChecked != true) && (toggleButton != SelectTool || currentSelectMode != SelectMode.Lasso))
			{
				ClearLassoOverlay();
			}
		}
		catch
		{
		}
	}

	private void DrawModeButton_Checked(object? sender, RoutedEventArgs e)
	{
		if (!(sender is ToggleButton toggleButton))
		{
			return;
		}
		List<ToggleButton> list = new List<ToggleButton> { DrawTileButton, DrawLineButton, DrawSquareButton, DrawCircleButton, DrawEllipseButton, DrawTriangleButton, DrawPolygonButton };
		foreach (ToggleButton item in list)
		{
			if (item != null && item != toggleButton)
			{
				item.IsChecked = false;
			}
		}
		if (toggleButton == DrawTileButton)
		{
			currentDrawMode = DrawMode.Tile;
		}
		else if (toggleButton == DrawLineButton)
		{
			currentDrawMode = DrawMode.Line;
		}
		else if (toggleButton == DrawSquareButton)
		{
			currentDrawMode = DrawMode.Square;
		}
		else if (toggleButton == DrawCircleButton)
		{
			currentDrawMode = DrawMode.Circle;
		}
		else if (toggleButton == DrawEllipseButton)
		{
			currentDrawMode = DrawMode.Ellipse;
		}
		else if (toggleButton == DrawTriangleButton)
		{
			currentDrawMode = DrawMode.Triangle;
		}
		else if (toggleButton == DrawPolygonButton)
		{
			currentDrawMode = DrawMode.Polygon;
		}
	}

	private void InitializeStructurePopupIcons()
	{
		try
		{
			if (tileImages != null)
			{
				if (StructureSetAIcon != null && 32 < tileImages.Length)
				{
					StructureSetAIcon.Source = tileImages[32];
				}
				if (StructureSetBIcon != null && 64 < tileImages.Length)
				{
					StructureSetBIcon.Source = tileImages[64];
				}
				if (StructureSetCIcon != null && 96 < tileImages.Length)
				{
					StructureSetCIcon.Source = tileImages[96];
				}
			}
		}
		catch
		{
		}
	}

	private void UpdateStructureToolIcon()
	{
		try
		{
			if (StructureToolIcon == null)
			{
				return;
			}
			if (tileImages == null)
			{
				StructureToolIcon.Visibility = Visibility.Collapsed;
				return;
			}
			int num = structureSetBaseTile;
			if (num >= 0 && num < tileImages.Length && tileImages[num] != null)
			{
				StructureToolIcon.Source = tileImages[num];
				StructureToolIcon.Visibility = Visibility.Visible;
			}
			else
			{
				StructureToolIcon.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
		}
	}

	private void SelectStructureSet(int baseTile)
	{
		structureSetBaseTile = baseTile;
		structureSetOffset = (baseTile - 32) / 32;
		UpdateStructureToolIcon();
	}

	private void ClearLassoOverlay()
	{
		try
		{
			isLassoActive = false;
			lassoPoints.Clear();
			if (!(FindName("SelectionOverlay") is Canvas canvas))
			{
				return;
			}
			for (int num = canvas.Children.Count - 1; num >= 0; num--)
			{
				if (canvas.Children[num] is FrameworkElement { Name: "LassoPath" })
				{
					canvas.Children.RemoveAt(num);
				}
			}
		}
		catch
		{
		}
	}

	private void RenderLassoPath()
	{
		try
		{
			if (!(FindName("SelectionOverlay") is Canvas canvas))
			{
				return;
			}
			for (int num = canvas.Children.Count - 1; num >= 0; num--)
			{
				if (canvas.Children[num] is FrameworkElement { Name: "LassoPath" })
				{
					canvas.Children.RemoveAt(num);
				}
			}
			if (lassoPoints.Count >= 2)
			{
				System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
				{
					Name = "LassoPath",
					Stroke = Brushes.Lime,
					StrokeThickness = 1.5,
					Fill = new SolidColorBrush(Color.FromArgb(40, 0, byte.MaxValue, 0)),
					IsHitTestVisible = false
				};
				StreamGeometry streamGeometry = new StreamGeometry();
				using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
				{
					streamGeometryContext.BeginFigure(lassoPoints[0], isFilled: true, isClosed: true);
					streamGeometryContext.PolyLineTo(lassoPoints.ToArray(), isStroked: true, isSmoothJoin: true);
				}
				path.Data = streamGeometry;
				canvas.Children.Add(path);
			}
		}
		catch
		{
		}
	}

	private void StructureTool_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			Popup popup = FindName("StructurePopup") as Popup;
			ToggleButton toggleButton = FindName("StructureTool") as ToggleButton;
			if (popup == null || toggleButton == null)
			{
				return;
			}
			if (popup.IsOpen)
			{
				popup.IsOpen = false;
			}
			else
			{
				InitializeStructurePopupIcons();
				popup.IsOpen = true;
			}
			toggleButton.IsChecked = true;
			try
			{
				Tool_Checked(toggleButton, new RoutedEventArgs());
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void StructureSetAButton_Click(object? sender, RoutedEventArgs e)
	{
		SelectStructureSet(32);
		if (FindName("StructurePopup") is Popup popup)
		{
			popup.IsOpen = false;
		}
	}

	private void StructureSetBButton_Click(object? sender, RoutedEventArgs e)
	{
		SelectStructureSet(64);
		if (FindName("StructurePopup") is Popup popup)
		{
			popup.IsOpen = false;
		}
	}

	private void StructureSetCButton_Click(object? sender, RoutedEventArgs e)
	{
		SelectStructureSet(96);
		if (FindName("StructurePopup") is Popup popup)
		{
			popup.IsOpen = false;
		}
	}

	private void DrawModeButton_Unchecked(object? sender, RoutedEventArgs e)
	{
		if (sender is ToggleButton && DrawTileButton.IsChecked != true && DrawLineButton.IsChecked != true && DrawSquareButton.IsChecked != true && DrawCircleButton.IsChecked != true && DrawEllipseButton.IsChecked != true && DrawTriangleButton.IsChecked != true && DrawPolygonButton.IsChecked != true && DrawTileButton != null)
		{
			DrawTileButton.IsChecked = true;
		}
	}

	private void FloodFill(int sx, int sy, int target, int replacement)
	{
		if (target == replacement)
		{
			return;
		}
		TileChangeAction tileChangeAction = new TileChangeAction();
		Queue<(int, int)> queue = new Queue<(int, int)>();
		queue.Enqueue((sx, sy));
		while (queue.Count > 0)
		{
			var (num, num2) = queue.Dequeue();
			if (num >= 0 && num < mapWidth && num2 >= 0 && num2 < mapHeight)
			{
				int num3 = num2 * mapWidth + num;
				if (tiles[num3] == target)
				{
					tileChangeAction.Add(num3, tiles[num3], replacement);
					tiles[num3] = replacement;
					queue.Enqueue((num + 1, num2));
					queue.Enqueue((num - 1, num2));
					queue.Enqueue((num, num2 + 1));
					queue.Enqueue((num, num2 - 1));
				}
			}
		}
		if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(tileChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		try
		{
			RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
		}
	}

	private void SpriteFloodFill(int sx, int sy, int target, int replacement)
	{
		if (target == replacement)
		{
			return;
		}
		SpriteChangeAction spriteChangeAction = new SpriteChangeAction();
		Queue<(int, int)> queue = new Queue<(int, int)>();
		queue.Enqueue((sx, sy));
		while (queue.Count > 0)
		{
			var (num, num2) = queue.Dequeue();
			if (num >= 0 && num < mapWidth && num2 >= 0 && num2 < mapHeight)
			{
				int num3 = num2 * mapWidth + num;
				if (sprites[num3] == target)
				{
					spriteChangeAction.Add(num3, target, replacement);
					sprites[num3] = replacement;
					queue.Enqueue((num + 1, num2));
					queue.Enqueue((num - 1, num2));
					queue.Enqueue((num, num2 + 1));
					queue.Enqueue((num, num2 - 1));
				}
			}
		}
		if (!spriteChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(spriteChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		try
		{
			RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
		}
	}

	private void CanvasHost_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (CanvasHost == null)
		{
			return;
		}
		Point position = e.GetPosition(CanvasHost);
		UpdateCoords(position);
		try
		{
			isRightMouseDown = true;
			rightMouseDownPosition = position;
			rightDragStarted = false;
			e.Handled = true;
		}
		catch
		{
		}
	}

	private void CanvasHost_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (CanvasHost == null || MapScrollViewer == null)
		{
			return;
		}
		try
		{
			if (isResizeModeActive && e.LeftButton == MouseButtonState.Pressed && selW > 0 && selH > 0)
			{
				Point position = e.GetPosition(CanvasHost);
				var (num, num2) = ViewportPointToTile(position);
				if (num >= selX && num < selX + selW && num2 >= selY && num2 < selY + selH)
				{
					resizeOrigW = selW;
					resizeOrigH = selH;
					resizeOrigTiles = new int[resizeOrigW, resizeOrigH];
					resizeOrigSprites = new int[resizeOrigW, resizeOrigH];
					for (int i = 0; i < resizeOrigH; i++)
					{
						for (int j = 0; j < resizeOrigW; j++)
						{
							int num3 = (selY + i) * mapWidth + (selX + j);
							resizeOrigTiles[j, i] = tiles[num3];
							resizeOrigSprites[j, i] = sprites[num3];
						}
					}
					isResizingSelection = true;
					int num4 = num - selX;
					int num5 = num2 - selY;
					bool flag = num4 * 2 < resizeOrigW;
					bool flag2 = num5 * 2 < resizeOrigH;
					if (flag)
					{
						if (flag2)
						{
							resizeAnchor = ResizeAnchor.TopLeft;
						}
						else
						{
							resizeAnchor = ResizeAnchor.BottomLeft;
						}
					}
					else if (flag2)
					{
						resizeAnchor = ResizeAnchor.TopRight;
					}
					else
					{
						resizeAnchor = ResizeAnchor.BottomRight;
					}
					DpiScale dpi = VisualTreeHelper.GetDpi(this);
					double num6 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
					int num7 = Math.Max(1, (int)Math.Ceiling(16.0 * num6 * dpi.DpiScaleX));
					int num8 = Math.Max(1, (int)Math.Ceiling(16.0 * num6 * dpi.DpiScaleY));
					int num9 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
					int num10 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
					double num11 = (double)(num9 + selX * num7) / dpi.DpiScaleX;
					double num12 = (double)(num10 + selY * num8) / dpi.DpiScaleY;
					double num13 = (double)(resizeOrigW * 16) * num6;
					double num14 = (double)(resizeOrigH * 16) * num6;
					double x = num11;
					double y = num12;
					double x2 = num11 + num13;
					double y2 = num12;
					double x3 = num11;
					double y3 = num12 + num14;
					double x4 = num11 + num13;
					double y4 = num12 + num14;
					switch (resizeAnchor)
					{
					case ResizeAnchor.TopLeft:
						resizeStartMouse = new Point(x, y);
						Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeNWSE;
						break;
					case ResizeAnchor.TopRight:
						resizeStartMouse = new Point(x2, y2);
						Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeNESW;
						break;
					case ResizeAnchor.BottomLeft:
						resizeStartMouse = new Point(x3, y3);
						Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeNESW;
						break;
					case ResizeAnchor.BottomRight:
						resizeStartMouse = new Point(x4, y4);
						Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeNWSE;
						break;
					}
					try
					{
						CanvasHost.CaptureMouse();
					}
					catch
					{
					}
					e.Handled = true;
					return;
				}
			}
		}
		catch
		{
		}
		try
		{
			if (e.MiddleButton == MouseButtonState.Pressed)
			{
				isMiddlePanning = true;
				middlePanStart = e.GetPosition(MapScrollViewer);
				panStartHOffset = MapScrollViewer.HorizontalOffset;
				panStartVOffset = MapScrollViewer.VerticalOffset;
				CanvasHost.CaptureMouse();
				Mouse.OverrideCursor = System.Windows.Input.Cursors.ScrollAll;
				e.Handled = true;
			}
		}
		catch
		{
		}
	}

	private void CanvasHost_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (CanvasHost == null)
		{
			return;
		}
		try
		{
			if (isMiddlePanning && e.MiddleButton == MouseButtonState.Released)
			{
				isMiddlePanning = false;
				try
				{
					if (Mouse.Captured == CanvasHost)
					{
						Mouse.Captured.ReleaseMouseCapture();
					}
				}
				catch
				{
				}
				Mouse.OverrideCursor = null;
				e.Handled = true;
			}
			if (isRightMouseDown && !rightDragStarted)
			{
				Point position = e.GetPosition(CanvasHost);
				try
				{
					double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
					double num2 = mapViewportPadding;
					int num3 = Math.Max(0, Math.Min(mapWidth - 1, (int)((position.X - num2) / (16.0 * num))));
					int num4 = Math.Max(0, Math.Min(mapHeight - 1, (int)((position.Y - num2) / (16.0 * num))));
					int num5 = num4 * mapWidth + num3;
					bool flag = false;
					bool flag2 = false;
					if (tilesLayerActive)
					{
						int num6 = tiles[num5];
						if (num6 >= 0)
						{
							selectedTile = num6;
							selectedTiles = new List<int> { selectedTile };
							selectionWidth = 1;
							selectionHeight = 1;
							flag = true;
						}
					}
					if (spritesLayerActive)
					{
						int num7 = sprites[num5];
						if (num7 >= 0)
						{
							selectedSprite = num7;
							flag2 = true;
						}
					}
					if (flag || flag2)
					{
						if (flag && !flag2)
						{
							tilesLayerActive = true;
							spritesLayerActive = false;
							selectedSprite = -1;
						}
						else if (flag2 && !flag)
						{
							spritesLayerActive = true;
							tilesLayerActive = false;
							selectedTile = -1;
						}
						UpdatePaletteHighlight();
						if (StatusText != null)
						{
							if (flag && flag2)
							{
								StatusText.Text = $"Picked tile {selectedTile} and sprite {selectedSprite}";
							}
							else if (flag)
							{
								StatusText.Text = $"Picked tile {selectedTile}";
							}
							else
							{
								StatusText.Text = $"Picked sprite {selectedSprite}";
							}
						}
					}
				}
				catch
				{
				}
				isRightMouseDown = false;
				rightDragStarted = false;
				pendingDrag = false;
				e.Handled = true;
			}
			else if (isSelecting && e.RightButton == MouseButtonState.Released)
			{
				EndSelection();
				isRightMouseDown = false;
				rightDragStarted = false;
				pendingDrag = false;
				e.Handled = true;
			}
		}
		catch
		{
		}
	}

	private void StartPaintingAt(Point pos)
	{
		if (!IsPointInsideMap(pos))
		{
			return;
		}
		var (num, num2) = ViewportPointToTile(pos);
		if (FillTool != null && FillTool.IsChecked == true)
		{
			try
			{
				if (currentFillMode == FillMode.ReplaceSelected)
				{
					int item = num2 * mapWidth + num;
					if (selectionSet != null && selectionSet.Contains(item))
					{
						PerformReplaceSelected();
					}
					else if (StatusText != null)
					{
						StatusText.Text = "Replace Selected: click inside current selection to apply";
					}
				}
				else
				{
					bool flag = false;
					if (tilesLayerActive && selectedTile >= 0)
					{
						int num3 = tiles[num2 * mapWidth + num];
						if (num3 != selectedTile)
						{
							FloodFill(num, num2, num3, selectedTile);
							flag = true;
						}
					}
					if (spritesLayerActive && selectedSprite >= 0)
					{
						int num4 = sprites[num2 * mapWidth + num];
						if (num4 != selectedSprite)
						{
							SpriteFloodFill(num, num2, num4, selectedSprite);
							flag = true;
						}
					}
					if (!flag && !tilesLayerActive && !spritesLayerActive)
					{
						Redraw();
					}
				}
				return;
			}
			catch
			{
				return;
			}
		}
		if (MoveTool != null && MoveTool.IsChecked == true)
		{
			bool flag2 = false;
			bool flag3 = false;
			if (tilesLayerActive)
			{
				int num5 = tiles[num2 * mapWidth + num];
				if (num5 >= 0)
				{
					selectedTile = num5;
					selectedTiles = new List<int> { selectedTile };
					selectionWidth = 1;
					selectionHeight = 1;
					flag2 = true;
				}
			}
			if (spritesLayerActive)
			{
				int num6 = sprites[num2 * mapWidth + num];
				if (num6 >= 0)
				{
					selectedSprite = num6;
					flag3 = true;
				}
			}
			if (!(flag2 || flag3))
			{
				return;
			}
			if (flag2 && !flag3)
			{
				tilesLayerActive = true;
				spritesLayerActive = false;
				selectedSprite = -1;
			}
			else if (flag3 && !flag2)
			{
				spritesLayerActive = true;
				tilesLayerActive = false;
				selectedTile = -1;
			}
			UpdatePaletteHighlight();
			if (StatusText != null)
			{
				if (flag2 && flag3)
				{
					StatusText.Text = $"Picked tile {selectedTile} and sprite {selectedSprite}";
				}
				else if (flag2)
				{
					StatusText.Text = $"Picked tile {selectedTile}";
				}
				else
				{
					StatusText.Text = $"Picked sprite {selectedSprite}";
				}
			}
			return;
		}
		try
		{
			if (FindName("StructureTool") is ToggleButton { IsChecked: var isChecked } && isChecked == true)
			{
				(int, int) tuple2 = ViewportPointToTile(pos);
				ConvertRegionToStructure(tuple2.Item1, tuple2.Item2);
				return;
			}
		}
		catch
		{
		}
		if ((PlaceTool != null && PlaceTool.IsChecked == true) || (EraseTool != null && EraseTool.IsChecked == true))
		{
			isPainting = true;
			lastPaintX = -1;
			lastPaintY = -1;
			try
			{
				EnsureScaledTileCache((ZoomSlider != null) ? ZoomSlider.Value : 1.0, VisualTreeHelper.GetDpi(this));
			}
			catch
			{
			}
			if (!suppressUndoRecording)
			{
				currentCompositeAction = new TileChangeAction();
			}
			CanvasHost.CaptureMouse();
			DoPaintAt(num, num2);
		}
	}

	private void ConvertRegionToStructure(int sx, int sy)
	{
		if (sx < 0 || sx >= mapWidth || sy < 0 || sy >= mapHeight)
		{
			return;
		}
		int num = sy * mapWidth + sx;
		if (tiles[num] == -1 || tiles[num] == 0)
		{
			return;
		}
		Queue<(int, int)> queue = new Queue<(int, int)>();
		HashSet<int> hashSet = new HashSet<int>();
		queue.Enqueue((sx, sy));
		hashSet.Add(num);
		while (queue.Count > 0)
		{
			(int, int) tuple = queue.Dequeue();
			int item = tuple.Item1;
			int item2 = tuple.Item2;
			(int, int)[] array = new(int, int)[4]
			{
				(item + 1, item2),
				(item - 1, item2),
				(item, item2 + 1),
				(item, item2 - 1)
			};
			(int, int)[] array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				var (num2, num3) = array2[i];
				if (num2 >= 0 && num2 < mapWidth && num3 >= 0 && num3 < mapHeight)
				{
					int num4 = num3 * mapWidth + num2;
					if (!hashSet.Contains(num4) && tiles[num4] != -1 && tiles[num4] != 0)
					{
						hashSet.Add(num4);
						queue.Enqueue((num2, num3));
					}
				}
			}
		}
		if (hashSet.Count == 0)
		{
			return;
		}
		TileChangeAction tileChangeAction = new TileChangeAction();
		foreach (int item3 in hashSet)
		{
			int num5 = item3 % mapWidth;
			int num6 = item3 / mapWidth;
			bool flag = num6 - 1 >= 0 && hashSet.Contains((num6 - 1) * mapWidth + num5);
			bool flag2 = num6 + 1 < mapHeight && hashSet.Contains((num6 + 1) * mapWidth + num5);
			bool flag3 = num5 - 1 >= 0 && hashSet.Contains(num6 * mapWidth + (num5 - 1));
			bool flag4 = num5 + 1 < mapWidth && hashSet.Contains(num6 * mapWidth + (num5 + 1));
			bool flag5 = false;
			try
			{
				if (num6 + 1 >= mapHeight)
				{
					flag5 = true;
				}
				else if (loadedHasGroundLayer)
				{
					int num7 = (int)Math.Floor(loadedGroundOffsetY / 16.0);
					if (num6 + 1 >= num7)
					{
						flag5 = true;
					}
				}
			}
			catch
			{
				flag5 = false;
			}
			bool flag6 = flag2 || flag5;
			int num8 = (flag ? 1 : 0) + (flag6 ? 1 : 0) + (flag3 ? 1 : 0) + (flag4 ? 1 : 0);
			int num9 = -1;
			switch (num8)
			{
			case 4:
				num9 = 47;
				break;
			case 3:
				if (!flag)
				{
					num9 = 33;
				}
				else if (!flag4)
				{
					num9 = 34;
				}
				else if (!flag6)
				{
					num9 = 35;
				}
				else if (!flag3)
				{
					num9 = 36;
				}
				break;
			case 2:
				if (flag && flag6)
				{
					num9 = 45;
				}
				else if (flag3 && flag4)
				{
					num9 = 46;
				}
				else if (flag4 && flag6)
				{
					num9 = 37;
				}
				else if (flag3 && flag6)
				{
					num9 = 38;
				}
				else if (flag3 && flag)
				{
					num9 = 39;
				}
				else if (flag4 && flag)
				{
					num9 = 40;
				}
				break;
			case 1:
				if (flag)
				{
					num9 = 50;
				}
				else if (flag6)
				{
					num9 = ((!flag2) ? 45 : 48);
				}
				else if (flag3)
				{
					num9 = 49;
				}
				else if (flag4)
				{
					num9 = 51;
				}
				break;
			default:
				num9 = 45;
				break;
			}
			try
			{
				num9 += structureSetOffset * 32;
			}
			catch
			{
			}
			int num10 = tiles[item3];
			int num11 = num9;
			if (num10 != num11)
			{
				tileChangeAction.Add(item3, num10, num11);
				tiles[item3] = num11;
			}
		}
		if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(tileChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		try
		{
			double scale = (useFastZoom ? cachedScale : ((ZoomSlider != null) ? ZoomSlider.Value : 1.0));
			foreach (int item4 in hashSet)
			{
				int x = item4 % mapWidth;
				int y = item4 / mapWidth;
				UpdateTileBitmapAt(x, y, scale, mapViewportPadding);
			}
		}
		catch
		{
			Redraw();
		}
	}

	private void StartSelectionAt(Point pos)
	{
		if (CanvasHost != null)
		{
			(int, int) tuple = ViewportPointToTile(pos);
			int item = tuple.Item1;
			int item2 = tuple.Item2;
			isSelecting = true;
			selectStartX = item;
			selectStartY = item2;
			UpdateSelectionVisuals(item, item2, 1, 1, previewMode: true);
			if (CanvasHost != null)
			{
				CanvasHost.CaptureMouse();
			}
		}
	}

	private void ToggleSelectionAt(Point pos)
	{
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		double num2 = mapViewportPadding;
		int num3 = Math.Max(0, Math.Min(mapWidth - 1, (int)((pos.X - num2) / (16.0 * num))));
		int num4 = Math.Max(0, Math.Min(mapHeight - 1, (int)((pos.Y - num2) / (16.0 * num))));
		int num5 = num4 * mapWidth + num3;
		bool flag = tilesLayerActive && tiles[num5] != -1;
		bool flag2 = spritesLayerActive && sprites[num5] != -1;
		if (!flag && !flag2)
		{
			return;
		}
		if (selectionSet.Contains(num5))
		{
			selectionSet.Remove(num5);
		}
		else
		{
			selectionSet.Add(num5);
		}
		if (selectionSet.Count == 0)
		{
			ClearSelection();
			return;
		}
		int num6 = int.MaxValue;
		int num7 = int.MaxValue;
		int num8 = int.MinValue;
		int num9 = int.MinValue;
		foreach (int item in selectionSet)
		{
			int num10 = item % mapWidth;
			int num11 = item / mapWidth;
			if (num10 < num6)
			{
				num6 = num10;
			}
			if (num11 < num7)
			{
				num7 = num11;
			}
			if (num10 > num8)
			{
				num8 = num10;
			}
			if (num11 > num9)
			{
				num9 = num11;
			}
		}
		selX = num6;
		selY = num7;
		selW = num8 - num6 + 1;
		selH = num9 - num7 + 1;
		selTiles = new int[selW * selH];
		selSprites = new int[selW * selH];
		for (int i = 0; i < selH; i++)
		{
			for (int j = 0; j < selW; j++)
			{
				int num12 = selX + j;
				int num13 = selY + i;
				int num14 = num13 * mapWidth + num12;
				if (selectionSet.Contains(num14))
				{
					selTiles[i * selW + j] = (tilesLayerActive ? tiles[num14] : (-1));
					selSprites[i * selW + j] = (spritesLayerActive ? sprites[num14] : (-1));
				}
				else
				{
					selTiles[i * selW + j] = -1;
					selSprites[i * selW + j] = -1;
				}
			}
		}
		UpdateSelectionVisuals(selX, selY, selW, selH);
		if (StatusText != null)
		{
			StatusText.Text = $"Selected items: {selectionSet.Count} (bbox {selW}x{selH} at {selX},{selY})";
		}
	}

	private void StartMagicWandAt(Point pos)
	{
		if (CanvasHost == null)
		{
			return;
		}
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		double num2 = mapViewportPadding;
		int num3 = Math.Max(0, Math.Min(mapWidth - 1, (int)((pos.X - num2) / (16.0 * num))));
		int num4 = Math.Max(0, Math.Min(mapHeight - 1, (int)((pos.Y - num2) / (16.0 * num))));
		int num5 = num4 * mapWidth + num3;
		int[] array = (tilesLayerActive ? tiles : sprites);
		int num6 = array[num5];
		if (num6 == -1)
		{
			return;
		}
		Queue<(int, int)> queue = new Queue<(int, int)>();
		HashSet<int> hashSet = new HashSet<int>();
		queue.Enqueue((num3, num4));
		hashSet.Add(num5);
		while (queue.Count > 0)
		{
			(int, int) tuple = queue.Dequeue();
			int item = tuple.Item1;
			int item2 = tuple.Item2;
			int num7 = item2 * mapWidth + item;
			(int, int)[] array2 = new(int, int)[4]
			{
				(item + 1, item2),
				(item - 1, item2),
				(item, item2 + 1),
				(item, item2 - 1)
			};
			(int, int)[] array3 = array2;
			for (int i = 0; i < array3.Length; i++)
			{
				var (num8, num9) = array3[i];
				if (num8 >= 0 && num8 < mapWidth && num9 >= 0 && num9 < mapHeight)
				{
					int num10 = num9 * mapWidth + num8;
					if (!hashSet.Contains(num10) && array[num10] == num6)
					{
						hashSet.Add(num10);
						queue.Enqueue((num8, num9));
					}
				}
			}
		}
		bool flag = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
		if (!flag)
		{
			selectionSet.Clear();
		}
		int num11 = int.MaxValue;
		int num12 = int.MaxValue;
		int num13 = int.MinValue;
		int num14 = int.MinValue;
		foreach (int item3 in hashSet)
		{
			selectionSet.Add(item3);
		}
		foreach (int item4 in selectionSet)
		{
			int num15 = item4 % mapWidth;
			int num16 = item4 / mapWidth;
			if (num15 < num11)
			{
				num11 = num15;
			}
			if (num16 < num12)
			{
				num12 = num16;
			}
			if (num15 > num13)
			{
				num13 = num15;
			}
			if (num16 > num14)
			{
				num14 = num16;
			}
		}
		selX = num11;
		selY = num12;
		selW = num13 - num11 + 1;
		selH = num14 - num12 + 1;
		selTiles = new int[selW * selH];
		selSprites = new int[selW * selH];
		for (int j = 0; j < selH; j++)
		{
			for (int k = 0; k < selW; k++)
			{
				int num17 = selX + k;
				int num18 = selY + j;
				int num19 = num18 * mapWidth + num17;
				if (selectionSet.Contains(num19))
				{
					selTiles[j * selW + k] = (tilesLayerActive ? tiles[num19] : (-1));
					selSprites[j * selW + k] = (spritesLayerActive ? sprites[num19] : (-1));
				}
				else
				{
					selTiles[j * selW + k] = -1;
					selSprites[j * selW + k] = -1;
				}
			}
		}
		UpdateSelectionVisuals(selX, selY, selW, selH);
		string value = (flag ? "added" : "selected");
		if (StatusText != null)
		{
			StatusText.Text = $"Magic wand {value} {hashSet.Count} tiles of type {num6} (total: {selectionSet.Count})";
		}
	}

	private void StartDragMove(Point pos)
	{
		if (selTiles == null || selW <= 0 || selH <= 0)
		{
			return;
		}
		selSpriteOffsets.Clear();
		for (int i = 0; i < selH; i++)
		{
			for (int j = 0; j < selW; j++)
			{
				int num = (selY + i) * mapWidth + (selX + j);
				if (selectionSet.Contains(num) && !spriteAnchors.ContainsKey(num))
				{
					spriteAnchors[num] = (selX + j, selY + i);
				}
			}
		}
		double num2 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		int num3 = Math.Max(1, (int)Math.Ceiling(16.0 * num2 * dpi.DpiScaleX));
		int num4 = Math.Max(1, (int)Math.Ceiling(16.0 * num2 * dpi.DpiScaleY));
		int num5 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
		int num6 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
		int num7 = selW * 16;
		int num8 = selH * 16;
		ghostLogicalWidth = num7;
		ghostLogicalHeight = num8;
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			for (int k = 0; k < selH; k++)
			{
				for (int l = 0; l < selW; l++)
				{
					int num9 = selTiles[k * selW + l];
					if (num9 < 0 || tileImages == null || num9 >= tileImages.Length)
					{
						continue;
					}
					ImageSource imageSource = null;
					try
					{
						if (tileTonedImages != null && tileTonedImages.Length == tileImages.Length)
						{
							imageSource = tileTonedImages[num9];
						}
					}
					catch
					{
						imageSource = null;
					}
					if (imageSource == null)
					{
						imageSource = tileImages[num9];
					}
					if (imageSource != null)
					{
						drawingContext.DrawImage(imageSource, new Rect(l * 16, k * 16, 16.0, 16.0));
					}
				}
			}
			if (selSprites != null)
			{
				for (int m = 0; m < selH; m++)
				{
					for (int n = 0; n < selW; n++)
					{
						int num10 = selSprites[m * selW + n];
						if (num10 >= 0 && spriteImages != null && num10 < spriteImages.Length)
						{
							ImageSource imageSource2 = spriteImages[num10];
							if (imageSource2 != null)
							{
								drawingContext.DrawImage(imageSource2, new Rect(n * 16, m * 16, 16.0, 16.0));
							}
						}
					}
				}
			}
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(num7, num8, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		renderTargetBitmap.Freeze();
		if (HoverRect != null)
		{
			HoverRect.Visibility = Visibility.Collapsed;
		}
		if (HoverBorder != null)
		{
			HoverBorder.Visibility = Visibility.Collapsed;
		}
		if (OffsetGhostContainer != null)
		{
			OffsetGhostContainer.Visibility = Visibility.Collapsed;
		}
		if (OffsetTooltipContainer != null)
		{
			OffsetTooltipContainer.Visibility = Visibility.Collapsed;
		}
		if (GhostImage != null && CanvasHost != null)
		{
			GhostImage.Source = renderTargetBitmap;
			GhostImage.Width = (double)num7 * num2 * dpi.DpiScaleX / dpi.DpiScaleX;
			GhostImage.Height = (double)num8 * num2 * dpi.DpiScaleY / dpi.DpiScaleY;
			int num11 = num5 + selX * num3;
			int num12 = num6 + selY * num4;
			double length = (double)num11 / dpi.DpiScaleX;
			double length2 = (double)num12 / dpi.DpiScaleY;
			Canvas.SetLeft(GhostImage, length);
			Canvas.SetTop(GhostImage, length2);
			GhostImage.Visibility = Visibility.Visible;
		}
		isDraggingSelection = true;
		dragMovedOffMap = false;
		dragStartMouse = pos;
		dragOrigX = selX;
		dragOrigY = selY;
		lastDragScale = num2;
		double left = Canvas.GetLeft(GhostImage);
		double top = Canvas.GetTop(GhostImage);
		dragOffset = new Point(dragStartMouse.X - left, dragStartMouse.Y - top);
		if (CanvasHost != null)
		{
			CanvasHost.CaptureMouse();
		}
	}

	private void UpdateDragMoveTo(Point pos)
	{
		if (!isDraggingSelection || GhostImage == null)
		{
			return;
		}
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		int num2 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleX));
		int num3 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleY));
		double num4 = (double)num2 / dpi.DpiScaleX;
		double num5 = (double)num3 / dpi.DpiScaleY;
		int num6 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
		double num7 = (double)num6 / dpi.DpiScaleX;
		bool flag = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
		bool flag2 = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
		bool flag3 = spritesLayerActive && !tilesLayerActive;
		double num8 = pos.X - dragOffset.X;
		double num9 = pos.Y - dragOffset.Y;
		double num12;
		double num13;
		if (flag && flag2 && flag3)
		{
			double a = (num8 - num7) / num;
			double a2 = (num9 - num7) / num;
			int num10 = (int)Math.Round(a);
			int num11 = (int)Math.Round(a2);
			num12 = num7 + (double)num10 * num;
			num13 = num7 + (double)num11 * num;
		}
		else if (flag && flag3)
		{
			double num14 = (num8 - num7) / num;
			double num15 = (num9 - num7) / num;
			int num16 = (int)Math.Floor(num14 / 16.0);
			int num17 = (int)Math.Floor(num15 / 16.0);
			double num18 = num14 - (double)(num16 * 16);
			double num19 = num15 - (double)(num17 * 16);
			int num20 = (int)Math.Round(num18 / 8.0) * 8;
			int num21 = (int)Math.Round(num19 / 8.0) * 8;
			dragFinalTileX = num16;
			dragFinalTileY = num17;
			dragFinalOffsetX = num20;
			dragFinalOffsetY = num21;
			num12 = num7 + (double)(num16 * 16 + num20) * num;
			num13 = num7 + (double)(num17 * 16 + num21) * num;
		}
		else
		{
			int num22 = (int)Math.Round((num8 - num7) / num4);
			int num23 = (int)Math.Round((num9 - num7) / num5);
			num12 = num7 + (double)num22 * num4;
			num13 = num7 + (double)num23 * num5;
		}
		double num24 = num7;
		double num25 = num7;
		double num26 = num7 + Math.Max(0.0, (double)mapWidth * num4 - (double)selW * num4);
		double num27 = num7 + Math.Max(0.0, (double)mapHeight * num5 - (double)selH * num5);
		if (num12 < num24 || num12 > num26 || num13 < num25 || num13 > num27)
		{
			dragMovedOffMap = true;
			if (num12 < num24)
			{
				num12 = num24;
			}
			if (num12 > num26)
			{
				num12 = num26;
			}
			if (num13 < num25)
			{
				num13 = num25;
			}
			if (num13 > num27)
			{
				num13 = num27;
			}
		}
		else
		{
			dragMovedOffMap = false;
		}
		Canvas.SetLeft(GhostImage, num12);
		Canvas.SetTop(GhostImage, num13);
		double num28 = (num12 - num7) / num;
		double num29 = (num13 - num7) / num;
		if (flag && flag2 && flag3)
		{
			dragFinalTileX = (int)Math.Floor(num28 / 16.0);
			dragFinalTileY = (int)Math.Floor(num29 / 16.0);
			dragFinalOffsetX = (int)Math.Round(num28 - (double)(dragFinalTileX * 16));
			dragFinalOffsetY = (int)Math.Round(num29 - (double)(dragFinalTileY * 16));
		}
		else if (flag && flag3)
		{
			dragFinalTileX = (int)Math.Floor(num28 / 16.0);
			dragFinalTileY = (int)Math.Floor(num29 / 16.0);
			double num30 = num28 - (double)(dragFinalTileX * 16);
			double num31 = num29 - (double)(dragFinalTileY * 16);
			dragFinalOffsetX = (int)Math.Round(num30 / 8.0) * 8;
			dragFinalOffsetY = (int)Math.Round(num31 / 8.0) * 8;
		}
		else
		{
			dragFinalTileX = (int)Math.Round(num28 / 16.0);
			dragFinalTileY = (int)Math.Round(num29 / 16.0);
			dragFinalOffsetX = 0;
			dragFinalOffsetY = 0;
		}
		try
		{
			if (flag && flag3)
			{
				List<int> list = new List<int>();
				double num32 = num28;
				double num33 = num29;
				double num34 = num32 + (double)(selW * 16);
				double num35 = num33 + (double)(selH * 16);
				for (int i = 0; i < sprites.Length; i++)
				{
					int num36 = sprites[i];
					if (num36 != -1)
					{
						int num37 = i % mapWidth;
						int num38 = i / mapWidth;
						int num39 = num37 * 16;
						int num40 = num38 * 16;
						int num41 = 0;
						int num42 = 0;
						if (spritePixelOffsets.TryGetValue(i, out (int, int) value))
						{
							(num41, num42) = value;
						}
						double num43 = num39 + num41;
						double num44 = num40 + num42;
						double num45 = num43 + 16.0;
						double num46 = num44 + 16.0;
						if (!(num45 <= num32) && !(num43 >= num34) && !(num46 <= num33) && !(num44 >= num35))
						{
							list.Add(i);
						}
					}
				}
				string text = $"DragDebug: finalNative=({num28:0.##},{num29:0.##}) tile=({dragFinalTileX},{dragFinalTileY}) off=({dragFinalOffsetX},{dragFinalOffsetY}) overlaps={string.Join(";", list)}";
				Debug.WriteLine(text);
				try
				{
					string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "drag_debug.log");
					File.AppendAllText(path, text + Environment.NewLine);
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
			try
			{
				if (flag && flag3 && GhostImage != null)
				{
					GhostImage.Visibility = Visibility.Visible;
				}
			}
			catch
			{
			}
		}
		catch
		{
		}
		if (SelectionOverlay != null)
		{
			SelectionOverlay.Children.Clear();
			double width = (double)(selW * 16) * num;
			double height = (double)(selH * 16) * num;
			Rectangle element = new Rectangle
			{
				Width = width,
				Height = height,
				Stroke = Brushes.Yellow,
				StrokeThickness = 2.0 / num,
				Fill = Brushes.Transparent,
				IsHitTestVisible = false
			};
			Canvas.SetLeft(element, num12);
			Canvas.SetTop(element, num13);
			SelectionOverlay.Children.Add(element);
		}
	}

	private void EndDragMove(Point pos)
	{
		if (!isDraggingSelection)
		{
			return;
		}
		isDraggingSelection = false;
		if (CanvasHost != null && CanvasHost.IsMouseCaptured)
		{
			CanvasHost.ReleaseMouseCapture();
		}
		if (GhostImage == null)
		{
			return;
		}
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		double num2 = mapViewportPadding;
		if (!hasMouseMoved && selectionSet.Count > 0)
		{
			(int, int) tuple = ViewportPointToTile(pos);
			int item = tuple.Item1;
			int item2 = tuple.Item2;
			int item3 = item2 * mapWidth + item;
			if (!selectionSet.Contains(item3))
			{
				GhostImage.Visibility = Visibility.Collapsed;
				GhostImage.Source = null;
				ClearSelection();
				return;
			}
		}
		bool flag = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
		bool flag2 = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
		bool flag3 = spritesLayerActive && !tilesLayerActive;
		int num3 = dragFinalTileX;
		int num4 = dragFinalTileY;
		int pixelOffsetX = dragFinalOffsetX;
		int pixelOffsetY = dragFinalOffsetY;
		if (num3 < 0)
		{
			num3 = 0;
		}
		if (num4 < 0)
		{
			num4 = 0;
		}
		if (num3 + selW > mapWidth)
		{
			num3 = mapWidth - selW;
		}
		if (num4 + selH > mapHeight)
		{
			num4 = mapHeight - selH;
		}
		GhostImage.Visibility = Visibility.Collapsed;
		GhostImage.Source = null;
		if (SelectionOverlay != null)
		{
			SelectionOverlay.Children.Clear();
		}
		if (dragMovedOffMap)
		{
			dragMovedOffMap = false;
			return;
		}
		bool preserveOffsets = flag && flag3;
		MoveSelectionTo(num3, num4, pixelOffsetX, pixelOffsetY, preserveOffsets);
	}

	private void UpdateSelectionTo(Point pos)
	{
		if (isSelecting)
		{
			(int, int) tuple = ViewportPointToTile(pos);
			int item = tuple.Item1;
			int item2 = tuple.Item2;
			int num = Math.Min(selectStartX, item);
			int num2 = Math.Min(selectStartY, item2);
			int num3 = Math.Max(selectStartX, item);
			int num4 = Math.Max(selectStartY, item2);
			UpdateSelectionVisuals(num, num2, num3 - num + 1, num4 - num2 + 1, previewMode: true);
		}
	}

	private void UpdateSelectionVisuals(int x, int y, int w, int h, bool previewMode = false)
	{
		if (SelectionOverlay == null)
		{
			return;
		}
		SelectionOverlay.Children.Clear();
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		double num2 = mapViewportPadding;
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		int num3 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleX));
		int num4 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleY));
		int num5 = (int)Math.Round(num2 * dpi.DpiScaleX);
		int num6 = (int)Math.Round(num2 * dpi.DpiScaleY);
		if (previewMode)
		{
			double length = (double)(num5 + x * num3) / dpi.DpiScaleX;
			double length2 = (double)(num6 + y * num4) / dpi.DpiScaleY + gridRenderShiftY;
			double width = (double)(w * num3) / dpi.DpiScaleX;
			double height = (double)(h * num4) / dpi.DpiScaleY;
			if (currentSelectMode == SelectMode.Ellipse)
			{
				Ellipse ellipse = new Ellipse
				{
					Fill = SelectionFillBrush,
					Stroke = SelectionStrokeBrush,
					StrokeThickness = 1.0,
					Width = width,
					Height = height,
					IsHitTestVisible = false
				};
				try
				{
					ellipse.StrokeThickness = 1.0 / dpi.DpiScaleX;
					ellipse.SnapsToDevicePixels = true;
				}
				catch
				{
				}
				Canvas.SetLeft(ellipse, length);
				Canvas.SetTop(ellipse, length2);
				SelectionOverlay.Children.Add(ellipse);
			}
			else
			{
				Rectangle rectangle = new Rectangle
				{
					Fill = SelectionFillBrush,
					Stroke = SelectionStrokeBrush,
					StrokeThickness = 1.0,
					Width = width,
					Height = height,
					IsHitTestVisible = false
				};
				try
				{
					rectangle.StrokeThickness = 1.0 / dpi.DpiScaleX;
					rectangle.SnapsToDevicePixels = true;
				}
				catch
				{
				}
				Canvas.SetLeft(rectangle, length);
				Canvas.SetTop(rectangle, length2);
				SelectionOverlay.Children.Add(rectangle);
			}
		}
		else
		{
			try
			{
				if (selectionSet != null && selectionSet.Count > 0)
				{
					DpiScale dpi2 = dpi;
					double scale = num;
					RenderMaskToCanvasAsync(SelectionOverlay, selectionSet.ToArray(), scale, dpi2);
				}
			}
			catch
			{
			}
		}
		try
		{
			int num7 = 0;
			if (selectionSet != null && selectionSet.Count > 0)
			{
				foreach (int item in selectionSet)
				{
					if (item < 0 || item >= tiles.Length)
					{
						continue;
					}
					bool flag = tiles[item] != -1;
					bool flag2 = sprites[item] != -1;
					if (flag || flag2)
					{
						num7++;
						if (num7 >= 2)
						{
							break;
						}
					}
				}
			}
			else
			{
				for (int i = 0; i < h; i++)
				{
					if (num7 >= 2)
					{
						break;
					}
					for (int j = 0; j < w; j++)
					{
						if (num7 >= 2)
						{
							break;
						}
						int num8 = (y + i) * mapWidth + (x + j);
						if (num8 >= 0 && num8 < tiles.Length)
						{
							bool flag3 = tiles[num8] != -1;
							bool flag4 = sprites[num8] != -1;
							if (flag3 || flag4)
							{
								num7++;
							}
						}
					}
				}
			}
			if (FindName("ManipulateButton") is System.Windows.Controls.Button button)
			{
				button.IsEnabled = num7 >= 2;
			}
			try
			{
				MenuItem menuItem = FindName("Menu_Manipulate_Rotate") as MenuItem;
				MenuItem menuItem2 = FindName("Menu_Manipulate_RotateCCW") as MenuItem;
				MenuItem menuItem3 = FindName("Menu_Manipulate_Resize") as MenuItem;
				MenuItem menuItem4 = FindName("Menu_Manipulate_FlipH") as MenuItem;
				MenuItem menuItem5 = FindName("Menu_Manipulate_FlipV") as MenuItem;
				MenuItem menuItem6 = FindName("Menu_Manipulate_Rotate_Tools") as MenuItem;
				MenuItem menuItem7 = FindName("Menu_Manipulate_RotateCCW_Tools") as MenuItem;
				MenuItem menuItem8 = FindName("Menu_Manipulate_Resize_Tools") as MenuItem;
				MenuItem menuItem9 = FindName("Menu_Manipulate_FlipH_Tools") as MenuItem;
				MenuItem menuItem10 = FindName("Menu_Manipulate_FlipV_Tools") as MenuItem;
				bool isEnabled = num7 >= 2;
				if (menuItem != null)
				{
					menuItem.IsEnabled = isEnabled;
				}
				if (menuItem2 != null)
				{
					menuItem2.IsEnabled = isEnabled;
				}
				if (menuItem3 != null)
				{
					menuItem3.IsEnabled = isEnabled;
				}
				if (menuItem4 != null)
				{
					menuItem4.IsEnabled = isEnabled;
				}
				if (menuItem5 != null)
				{
					menuItem5.IsEnabled = isEnabled;
				}
				if (menuItem6 != null)
				{
					menuItem6.IsEnabled = isEnabled;
				}
				if (menuItem7 != null)
				{
					menuItem7.IsEnabled = isEnabled;
				}
				if (menuItem8 != null)
				{
					menuItem8.IsEnabled = isEnabled;
				}
				if (menuItem9 != null)
				{
					menuItem9.IsEnabled = isEnabled;
				}
				if (menuItem10 != null)
				{
					menuItem10.IsEnabled = isEnabled;
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

	private void UpdateResizePreview(Point pos)
	{
		if (!isResizingSelection || resizeOrigTiles == null)
		{
			return;
		}
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		int num2 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleX));
		int num3 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleY));
		int num4 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
		double num5 = (double)(num4 + selX * num2) / dpi.DpiScaleX;
		double num6 = (double)(num4 + selY * num3) / dpi.DpiScaleY;
		double num7 = (double)(resizeOrigW * 16) * num;
		double num8 = (double)(resizeOrigH * 16) * num;
		double num9 = ((resizeAnchor != ResizeAnchor.TopLeft && resizeAnchor != ResizeAnchor.BottomLeft) ? (resizeStartMouse.X - pos.X) : (pos.X - resizeStartMouse.X));
		double num10 = ((resizeAnchor != ResizeAnchor.TopLeft && resizeAnchor != ResizeAnchor.TopRight) ? (resizeStartMouse.Y - pos.Y) : (pos.Y - resizeStartMouse.Y));
		double num11 = Math.Max(0.1, (num7 + num9) / num7);
		double num12 = Math.Max(0.1, (num8 + num10) / num8);
		int num13 = Math.Max(1, (int)Math.Round((double)resizeOrigW * num11));
		int num14 = Math.Max(1, (int)Math.Round((double)resizeOrigH * num12));
		int num15 = num13 * 16;
		int num16 = num14 * 16;
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			for (int i = 0; i < num14; i++)
			{
				for (int j = 0; j < num13; j++)
				{
					int num17 = (int)Math.Floor((double)j * (double)resizeOrigW / (double)num13);
					int num18 = (int)Math.Floor((double)i * (double)resizeOrigH / (double)num14);
					if (num17 < 0)
					{
						num17 = 0;
					}
					if (num17 >= resizeOrigW)
					{
						num17 = resizeOrigW - 1;
					}
					if (num18 < 0)
					{
						num18 = 0;
					}
					if (num18 >= resizeOrigH)
					{
						num18 = resizeOrigH - 1;
					}
					int num19 = resizeOrigTiles[num17, num18];
					if (num19 >= 0 && tileImages != null && num19 < tileImages.Length)
					{
						ImageSource imageSource = tileImages[num19];
						if (imageSource != null)
						{
							drawingContext.DrawImage(imageSource, new Rect(j * 16, i * 16, 16.0, 16.0));
						}
					}
					int num20 = resizeOrigSprites[num17, num18];
					if (num20 >= 0 && spriteImages != null && num20 < spriteImages.Length)
					{
						ImageSource imageSource2 = spriteImages[num20];
						if (imageSource2 != null)
						{
							drawingContext.DrawImage(imageSource2, new Rect(j * 16, i * 16, 16.0, 16.0));
						}
					}
				}
			}
		}
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(num15, num16, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(drawingVisual);
		renderTargetBitmap.Freeze();
		if (GhostImage != null && CanvasHost != null)
		{
			GhostImage.Source = renderTargetBitmap;
			double num21 = (double)num15 * num * dpi.DpiScaleX / dpi.DpiScaleX;
			double num22 = (double)num16 * num * dpi.DpiScaleY / dpi.DpiScaleY;
			GhostImage.Width = num21;
			GhostImage.Height = num22;
			double length = num5 + ((resizeAnchor == ResizeAnchor.TopRight || resizeAnchor == ResizeAnchor.BottomRight) ? (num7 - num21) : 0.0);
			double length2 = num6 + ((resizeAnchor == ResizeAnchor.BottomLeft || resizeAnchor == ResizeAnchor.BottomRight) ? (num8 - num22) : 0.0);
			Canvas.SetLeft(GhostImage, length);
			Canvas.SetTop(GhostImage, length2);
			GhostImage.Visibility = Visibility.Visible;
			GhostImage.Opacity = 0.6;
		}
		int x = selX + ((resizeAnchor == ResizeAnchor.TopRight || resizeAnchor == ResizeAnchor.BottomRight) ? (resizeOrigW - num13) : 0);
		int y = selY + ((resizeAnchor == ResizeAnchor.BottomLeft || resizeAnchor == ResizeAnchor.BottomRight) ? (resizeOrigH - num14) : 0);
		UpdateSelectionVisuals(x, y, num13, num14, previewMode: true);
	}

	private void CommitResize(Point pos)
	{
		if (!isResizingSelection)
		{
			return;
		}
		isResizingSelection = false;
		try
		{
			if (CanvasHost != null && CanvasHost.IsMouseCaptured)
			{
				CanvasHost.ReleaseMouseCapture();
			}
		}
		catch
		{
		}
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		double num2 = (double)(resizeOrigW * 16) * num;
		double num3 = (double)(resizeOrigH * 16) * num;
		double num4 = ((resizeAnchor != ResizeAnchor.TopLeft && resizeAnchor != ResizeAnchor.BottomLeft) ? (resizeStartMouse.X - pos.X) : (pos.X - resizeStartMouse.X));
		double num5 = ((resizeAnchor != ResizeAnchor.TopLeft && resizeAnchor != ResizeAnchor.TopRight) ? (resizeStartMouse.Y - pos.Y) : (pos.Y - resizeStartMouse.Y));
		double num6 = num2 + num4;
		double num7 = num3 + num5;
		bool flag = num6 < 0.0;
		bool flag2 = num7 < 0.0;
		double value = num6 / num2;
		double value2 = num7 / num3;
		double num8 = Math.Max(0.05, Math.Abs(value));
		double num9 = Math.Max(0.05, Math.Abs(value2));
		int num10 = Math.Max(1, (int)Math.Round((double)resizeOrigW * num8));
		int num11 = Math.Max(1, (int)Math.Round((double)resizeOrigH * num9));
		HashSet<int> hashSet = new HashSet<int>();
		int num12 = selX;
		int num13 = selY;
		int num14 = num12 + ((resizeAnchor == ResizeAnchor.TopRight || resizeAnchor == ResizeAnchor.BottomRight) ? (resizeOrigW - num10) : 0);
		int num15 = num13 + ((resizeAnchor == ResizeAnchor.BottomLeft || resizeAnchor == ResizeAnchor.BottomRight) ? (resizeOrigH - num11) : 0);
		for (int i = 0; i < resizeOrigH; i++)
		{
			for (int j = 0; j < resizeOrigW; j++)
			{
				hashSet.Add(num12 + j + (num13 + i) * mapWidth);
			}
		}
		for (int k = 0; k < num11; k++)
		{
			for (int l = 0; l < num10; l++)
			{
				hashSet.Add(num14 + l + (num15 + k) * mapWidth);
			}
		}
		TileChangeAction tileChangeAction = new TileChangeAction();
		SpriteChangeAction spriteChangeAction = new SpriteChangeAction();
		int[,] array = new int[num10, num11];
		int[,] array2 = new int[num10, num11];
		for (int m = 0; m < num11; m++)
		{
			for (int n = 0; n < num10; n++)
			{
				int num16 = (flag ? ((int)Math.Floor((double)(num10 - 1 - n) * (double)resizeOrigW / (double)num10)) : ((int)Math.Floor((double)n * (double)resizeOrigW / (double)num10)));
				int num17 = (flag2 ? ((int)Math.Floor((double)(num11 - 1 - m) * (double)resizeOrigH / (double)num11)) : ((int)Math.Floor((double)m * (double)resizeOrigH / (double)num11)));
				if (num16 < 0)
				{
					num16 = 0;
				}
				if (num16 >= resizeOrigW)
				{
					num16 = resizeOrigW - 1;
				}
				if (num17 < 0)
				{
					num17 = 0;
				}
				if (num17 >= resizeOrigH)
				{
					num17 = resizeOrigH - 1;
				}
				array[n, m] = resizeOrigTiles[num16, num17];
				array2[n, m] = resizeOrigSprites[num16, num17];
			}
		}
		foreach (int item in hashSet)
		{
			int oldVal = tiles[item];
			int oldVal2 = sprites[item];
			int newVal = -1;
			int newVal2 = -1;
			int num18 = item % mapWidth - num14;
			int num19 = item / mapWidth - num15;
			if (num18 >= 0 && num19 >= 0 && num18 < num10 && num19 < num11)
			{
				newVal = array[num18, num19];
				newVal2 = array2[num18, num19];
			}
			tileChangeAction.Add(item, oldVal, newVal);
			spriteChangeAction.Add(item, oldVal2, newVal2);
		}
		if (!spriteChangeAction.IsEmpty())
		{
			spriteChangeAction.CaptureOffsets(this);
		}
		foreach (int item2 in hashSet)
		{
			int num20 = item2 % mapWidth - num14;
			int num21 = item2 / mapWidth - num15;
			int num22 = -1;
			int num23 = -1;
			if (num20 >= 0 && num21 >= 0 && num20 < num10 && num21 < num11)
			{
				num22 = array[num20, num21];
				num23 = array2[num20, num21];
			}
			tiles[item2] = num22;
			sprites[item2] = num23;
			spritePixelOffsets.Remove(item2);
		}
		for (int num24 = 0; num24 < resizeOrigH; num24++)
		{
			for (int num25 = 0; num25 < resizeOrigW; num25++)
			{
				int key = (num13 + num24) * mapWidth + (num12 + num25);
				if (spritePixelOffsets.TryGetValue(key, out (int, int) value3))
				{
					int num26 = (int)Math.Floor((double)num25 * (double)num10 / (double)resizeOrigW);
					int num27 = (int)Math.Floor((double)num24 * (double)num11 / (double)resizeOrigH);
					if (flag)
					{
						num26 = num10 - 1 - num26;
					}
					if (flag2)
					{
						num27 = num11 - 1 - num27;
					}
					int num28 = (num15 + num27) * mapWidth + (num14 + num26);
					if (num28 >= 0 && num28 < sprites.Length)
					{
						spritePixelOffsets[num28] = value3;
					}
				}
			}
		}
		if (!spriteChangeAction.IsEmpty())
		{
			spriteChangeAction.CaptureNewOffsets(this);
		}
		if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(tileChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		if (!spriteChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(spriteChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		try
		{
			RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
		}
		try
		{
			RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
			Redraw();
		}
		HashSet<int> hashSet2 = new HashSet<int>();
		for (int num29 = 0; num29 < num11; num29++)
		{
			for (int num30 = 0; num30 < num10; num30++)
			{
				hashSet2.Add((num15 + num29) * mapWidth + (num14 + num30));
			}
		}
		selectionSet = hashSet2;
		selW = num10;
		selH = num11;
		selX = num14;
		selY = num15;
		UpdateSelectionVisuals(selX, selY, selW, selH);
		if (GhostImage != null)
		{
			GhostImage.Visibility = Visibility.Collapsed;
			GhostImage.Source = null;
		}
	}

	private void BuildSelectSameIndexMaps()
	{
		try
		{
			tileIndexMap.Clear();
			spriteIndexMap.Clear();
			if (tiles != null && tiles.Length != 0)
			{
				Dictionary<int, List<int>> dictionary = new Dictionary<int, List<int>>();
				for (int i = 0; i < tiles.Length; i++)
				{
					int key = tiles[i];
					if (!dictionary.TryGetValue(key, out var value))
					{
						value = (dictionary[key] = new List<int>());
					}
					value.Add(i);
				}
				foreach (KeyValuePair<int, List<int>> item in dictionary)
				{
					List<int> value2 = item.Value;
					value2.Sort();
					IndexInfo indexInfo = new IndexInfo
					{
						Indices = value2.ToArray()
					};
					int num = int.MaxValue;
					int num2 = int.MaxValue;
					int num3 = int.MinValue;
					int num4 = int.MinValue;
					int[] indices = indexInfo.Indices;
					foreach (int num5 in indices)
					{
						int num6 = num5 % mapWidth;
						int num7 = num5 / mapWidth;
						if (num6 < num)
						{
							num = num6;
						}
						if (num7 < num2)
						{
							num2 = num7;
						}
						if (num6 > num3)
						{
							num3 = num6;
						}
						if (num7 > num4)
						{
							num4 = num7;
						}
					}
					if (num == int.MaxValue)
					{
						num = 0;
						num2 = 0;
						num3 = 0;
						num4 = 0;
					}
					indexInfo.MinX = num;
					indexInfo.MinY = num2;
					indexInfo.MaxX = num3;
					indexInfo.MaxY = num4;
					tileIndexMap[item.Key] = indexInfo;
				}
			}
			if (sprites == null || sprites.Length == 0)
			{
				return;
			}
			Dictionary<int, List<int>> dictionary2 = new Dictionary<int, List<int>>();
			for (int k = 0; k < sprites.Length; k++)
			{
				int key2 = sprites[k];
				if (!dictionary2.TryGetValue(key2, out var value3))
				{
					value3 = (dictionary2[key2] = new List<int>());
				}
				value3.Add(k);
			}
			foreach (KeyValuePair<int, List<int>> item2 in dictionary2)
			{
				List<int> value4 = item2.Value;
				value4.Sort();
				IndexInfo indexInfo2 = new IndexInfo
				{
					Indices = value4.ToArray()
				};
				int num8 = int.MaxValue;
				int num9 = int.MaxValue;
				int num10 = int.MinValue;
				int num11 = int.MinValue;
				int[] indices2 = indexInfo2.Indices;
				foreach (int num12 in indices2)
				{
					int num13 = num12 % mapWidth;
					int num14 = num12 / mapWidth;
					if (num13 < num8)
					{
						num8 = num13;
					}
					if (num14 < num9)
					{
						num9 = num14;
					}
					if (num13 > num10)
					{
						num10 = num13;
					}
					if (num14 > num11)
					{
						num11 = num14;
					}
				}
				if (num8 == int.MaxValue)
				{
					num8 = 0;
					num9 = 0;
					num10 = 0;
					num11 = 0;
				}
				indexInfo2.MinX = num8;
				indexInfo2.MinY = num9;
				indexInfo2.MaxX = num10;
				indexInfo2.MaxY = num11;
				spriteIndexMap[item2.Key] = indexInfo2;
			}
		}
		catch
		{
		}
	}

	private List<(int x, int y)> GetFilledPolygonTiles(List<(int x, int y)> pts)
	{
		List<(int, int)> list = new List<(int, int)>();
		if (pts == null || pts.Count < 3)
		{
			return list;
		}
		int num = int.MaxValue;
		int num2 = int.MaxValue;
		int num3 = int.MinValue;
		int num4 = int.MinValue;
		foreach (var pt in pts)
		{
			if (pt.x < num)
			{
				(num, _) = pt;
			}
			if (pt.x > num3)
			{
				(num3, _) = pt;
			}
			if (pt.y < num2)
			{
				num2 = pt.y;
			}
			if (pt.y > num4)
			{
				num4 = pt.y;
			}
		}
		num = Math.Max(0, num);
		num2 = Math.Max(0, num2);
		num3 = Math.Min(mapWidth - 1, num3);
		num4 = Math.Min(mapHeight - 1, num4);
		for (int i = num2; i <= num4; i++)
		{
			for (int j = num; j <= num3; j++)
			{
				double px = (double)j + 0.5;
				double py = (double)i + 0.5;
				if (PointInPolygon(px, py, pts))
				{
					list.Add((j, i));
				}
			}
		}
		return list;
	}

	private bool PointInPolygon(double px, double py, List<(int x, int y)> pts)
	{
		bool flag = false;
		int count = pts.Count;
		int num = 0;
		int index = count - 1;
		while (num < count)
		{
			double num2 = pts[num].x;
			double num3 = pts[num].y;
			double num4 = pts[index].x;
			double num5 = pts[index].y;
			if (num3 > py != num5 > py && px < (num4 - num2) * (py - num3) / (num5 - num3 + 0.0) + num2)
			{
				flag = !flag;
			}
			index = num++;
		}
		return flag;
	}

	private void CommitPolygon()
	{
		try
		{
			if (polygonPoints == null || polygonPoints.Count < 3)
			{
				polygonPoints.Clear();
				isConstructingPolygon = false;
				return;
			}
			List<(int, int)> list = new List<(int, int)>();
			if (!hollowShape)
			{
				list = GetFilledPolygonTiles(polygonPoints);
			}
			else
			{
				HashSet<(int, int)> hashSet = new HashSet<(int, int)>();
				for (int i = 0; i + 1 < polygonPoints.Count; i++)
				{
					(int, int) tuple = polygonPoints[i];
					(int, int) tuple2 = polygonPoints[i + 1];
					List<(int, int)> list2 = ComputeDrawTileList(tuple.Item1, tuple.Item2, tuple2.Item1, tuple2.Item2, DrawMode.Line, brushThickness);
					foreach (var item5 in list2)
					{
						hashSet.Add(item5);
					}
				}
				if (polygonPoints.Count >= 3)
				{
					(int, int) tuple3 = polygonPoints[polygonPoints.Count - 1];
					(int, int) tuple4 = polygonPoints[0];
					List<(int, int)> list3 = ComputeDrawTileList(tuple3.Item1, tuple3.Item2, tuple4.Item1, tuple4.Item2, DrawMode.Line, brushThickness);
					foreach (var item6 in list3)
					{
						hashSet.Add(item6);
					}
				}
				list.AddRange(hashSet);
			}
			if (list.Count == 0)
			{
				polygonPoints.Clear();
				isConstructingPolygon = false;
				return;
			}
			if (EraseTool != null && EraseTool.IsChecked == true)
			{
				TileChangeAction tileChangeAction = new TileChangeAction();
				SpriteChangeAction spriteChangeAction = new SpriteChangeAction();
				foreach (var item7 in list)
				{
					int item = item7.Item1;
					int item2 = item7.Item2;
					int num = item2 * mapWidth + item;
					if (tilesLayerActive)
					{
						int num2 = tiles[num];
						if (num2 != -1)
						{
							tileChangeAction.Add(num, num2, -1);
							tiles[num] = -1;
						}
					}
					if (spritesLayerActive)
					{
						int num3 = sprites[num];
						if (num3 != -1)
						{
							spriteChangeAction.Add(num, num3, -1);
							sprites[num] = -1;
						}
					}
				}
				if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
				{
					undoStack.Push(tileChangeAction);
					redoStack.Clear();
					SetHasUnsavedChanges(unsaved: true);
				}
				if (!spriteChangeAction.IsEmpty() && !suppressUndoRecording)
				{
					undoStack.Push(spriteChangeAction);
					redoStack.Clear();
					SetHasUnsavedChanges(unsaved: true);
				}
				try
				{
					RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
				}
				try
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					return;
				}
				catch
				{
					Redraw();
					return;
				}
			}
			if (SelectTool != null && SelectTool.IsChecked == true)
			{
				bool flag = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
				HashSet<int> hashSet2 = new HashSet<int>();
				foreach (var (num4, num5) in list)
				{
					hashSet2.Add(num5 * mapWidth + num4);
				}
				if (!flag)
				{
					selectionSet.Clear();
				}
				foreach (int item8 in hashSet2)
				{
					selectionSet.Add(item8);
				}
				if (selectionSet.Count == 0)
				{
					ClearSelection();
					return;
				}
				int num6 = int.MaxValue;
				int num7 = int.MaxValue;
				int num8 = int.MinValue;
				int num9 = int.MinValue;
				foreach (int item9 in selectionSet)
				{
					int num10 = item9 % mapWidth;
					int num11 = item9 / mapWidth;
					if (num10 < num6)
					{
						num6 = num10;
					}
					if (num11 < num7)
					{
						num7 = num11;
					}
					if (num10 > num8)
					{
						num8 = num10;
					}
					if (num11 > num9)
					{
						num9 = num11;
					}
				}
				selX = num6;
				selY = num7;
				selW = num8 - num6 + 1;
				selH = num9 - num7 + 1;
				selTiles = new int[selW * selH];
				selSprites = new int[selW * selH];
				for (int j = 0; j < selH; j++)
				{
					for (int k = 0; k < selW; k++)
					{
						int num12 = (selY + j) * mapWidth + (selX + k);
						if (selectionSet.Contains(num12))
						{
							selTiles[j * selW + k] = (tilesLayerActive ? tiles[num12] : (-1));
							selSprites[j * selW + k] = (spritesLayerActive ? sprites[num12] : (-1));
						}
						else
						{
							selTiles[j * selW + k] = -1;
							selSprites[j * selW + k] = -1;
						}
					}
				}
				UpdateSelectionVisuals(selX, selY, selW, selH);
				return;
			}
			TileChangeAction tileChangeAction2 = new TileChangeAction();
			SpriteChangeAction spriteChangeAction2 = new SpriteChangeAction();
			foreach (var item10 in list)
			{
				int item3 = item10.Item1;
				int item4 = item10.Item2;
				int num13 = item4 * mapWidth + item3;
				if (tilesLayerActive && selectedTile >= 0)
				{
					int num14 = tiles[num13];
					int num15 = selectedTile;
					if (num14 != num15)
					{
						tileChangeAction2.Add(num13, num14, num15);
						tiles[num13] = num15;
					}
				}
				if (spritesLayerActive && selectedSprite >= 0)
				{
					int num16 = sprites[num13];
					int num17 = selectedSprite;
					if (num16 != num17)
					{
						spriteChangeAction2.Add(num13, num16, num17);
						sprites[num13] = num17;
					}
				}
			}
			if (!tileChangeAction2.IsEmpty() && !suppressUndoRecording)
			{
				undoStack.Push(tileChangeAction2);
				redoStack.Clear();
				SetHasUnsavedChanges(unsaved: true);
			}
			if (!spriteChangeAction2.IsEmpty() && !suppressUndoRecording)
			{
				undoStack.Push(spriteChangeAction2);
				redoStack.Clear();
				SetHasUnsavedChanges(unsaved: true);
			}
			try
			{
				RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
			}
			catch
			{
			}
			try
			{
				RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
			}
			catch
			{
				Redraw();
			}
		}
		catch
		{
		}
		finally
		{
			polygonPoints.Clear();
			isConstructingPolygon = false;
			ClearDeferredPreview();
			if (CanvasHost != null && CanvasHost.IsMouseCaptured)
			{
				CanvasHost.ReleaseMouseCapture();
			}
			lastInputAction = DateTime.Now;
		}
	}

	private void ManipulateButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (sender is FrameworkElement { ContextMenu: not null } frameworkElement)
			{
				frameworkElement.ContextMenu.PlacementTarget = frameworkElement;
				frameworkElement.ContextMenu.IsOpen = true;
			}
		}
		catch
		{
		}
	}

	private void Menu_Manipulate_Rotate_Click(object sender, RoutedEventArgs e)
	{
		ApplyManipulation("rotate");
	}

	private void Menu_Manipulate_RotateCCW_Click(object sender, RoutedEventArgs e)
	{
		ApplyManipulation("rotateccw");
	}

	private void Menu_Manipulate_Resize_Click(object sender, RoutedEventArgs e)
	{
		if (selW <= 0 || selH <= 0)
		{
			return;
		}
		int num = ((selectionSet != null && selectionSet.Count > 0) ? selectionSet.Count : (selW * selH));
		if (num <= 1)
		{
			return;
		}
		isResizeModeActive = true;
		try
		{
			if (StatusText != null)
			{
				StatusText.Text = "Resize mode: hover selection and drag to resize";
			}
		}
		catch
		{
		}
	}

	private void Menu_Manipulate_FlipH_Click(object sender, RoutedEventArgs e)
	{
		ApplyManipulation("fliph");
	}

	private void Menu_Manipulate_FlipV_Click(object sender, RoutedEventArgs e)
	{
		ApplyManipulation("flipv");
	}

	private void ApplyManipulation(string op)
	{
		if (selW <= 0 || selH <= 0)
		{
			return;
		}
		int num = ((selectionSet != null && selectionSet.Count > 0) ? selectionSet.Count : (selW * selH));
		if (num <= 1)
		{
			return;
		}
		int num2 = selW;
		int num3 = selH;
		int num4 = selX;
		int num5 = selY;
		int[,] array = new int[num2, num3];
		int[,] array2 = new int[num2, num3];
		Dictionary<(int, int), (int, int)> dictionary = new Dictionary<(int, int), (int, int)>();
		for (int i = 0; i < num3; i++)
		{
			for (int j = 0; j < num2; j++)
			{
				int num6 = (num5 + i) * mapWidth + (num4 + j);
				array[j, i] = tiles[num6];
				array2[j, i] = sprites[num6];
				if (spritePixelOffsets.TryGetValue(num6, out (int, int) value))
				{
					dictionary[(j, i)] = (value.Item1, value.Item2);
				}
			}
		}
		int num7 = num2;
		int num8 = num3;
		int[,] array3;
		int[,] array4;
		switch (op)
		{
		default:
			return;
		case "rotate":
		{
			num7 = num3;
			num8 = num2;
			array3 = new int[num7, num8];
			array4 = new int[num7, num8];
			for (int num27 = 0; num27 < num8; num27++)
			{
				for (int num28 = 0; num28 < num7; num28++)
				{
					array3[num28, num27] = -1;
					array4[num28, num27] = -1;
				}
			}
			for (int num29 = 0; num29 < num3; num29++)
			{
				for (int num30 = 0; num30 < num2; num30++)
				{
					int item3 = (num5 + num29) * mapWidth + (num4 + num30);
					if (selectionSet == null || selectionSet.Count == 0 || selectionSet.Contains(item3))
					{
						int num31 = num3 - 1 - num29;
						int num32 = num30;
						if (num31 >= 0 && num31 < num7 && num32 >= 0 && num32 < num8)
						{
							array3[num31, num32] = array[num30, num29];
							array4[num31, num32] = array2[num30, num29];
						}
					}
				}
			}
			break;
		}
		case "rotateccw":
		{
			num7 = num3;
			num8 = num2;
			array3 = new int[num7, num8];
			array4 = new int[num7, num8];
			for (int num33 = 0; num33 < num8; num33++)
			{
				for (int num34 = 0; num34 < num7; num34++)
				{
					array3[num34, num33] = -1;
					array4[num34, num33] = -1;
				}
			}
			for (int num35 = 0; num35 < num3; num35++)
			{
				for (int num36 = 0; num36 < num2; num36++)
				{
					int item4 = (num5 + num35) * mapWidth + (num4 + num36);
					if (selectionSet == null || selectionSet.Count == 0 || selectionSet.Contains(item4))
					{
						int num37 = num35;
						int num38 = num2 - 1 - num36;
						if (num37 >= 0 && num37 < num7 && num38 >= 0 && num38 < num8)
						{
							array3[num37, num38] = array[num36, num35];
							array4[num37, num38] = array2[num36, num35];
						}
					}
				}
			}
			break;
		}
		case "fliph":
		{
			num7 = num2;
			num8 = num3;
			array3 = new int[num7, num8];
			array4 = new int[num7, num8];
			for (int num21 = 0; num21 < num8; num21++)
			{
				for (int num22 = 0; num22 < num7; num22++)
				{
					array3[num22, num21] = -1;
					array4[num22, num21] = -1;
				}
			}
			for (int num23 = 0; num23 < num3; num23++)
			{
				for (int num24 = 0; num24 < num2; num24++)
				{
					int item2 = (num5 + num23) * mapWidth + (num4 + num24);
					if (selectionSet == null || selectionSet.Count == 0 || selectionSet.Contains(item2))
					{
						int num25 = num2 - 1 - num24;
						int num26 = num23;
						array3[num25, num26] = array[num24, num23];
						array4[num25, num26] = array2[num24, num23];
					}
				}
			}
			break;
		}
		case "flipv":
		{
			num7 = num2;
			num8 = num3;
			array3 = new int[num7, num8];
			array4 = new int[num7, num8];
			for (int num15 = 0; num15 < num8; num15++)
			{
				for (int num16 = 0; num16 < num7; num16++)
				{
					array3[num16, num15] = -1;
					array4[num16, num15] = -1;
				}
			}
			for (int num17 = 0; num17 < num3; num17++)
			{
				for (int num18 = 0; num18 < num2; num18++)
				{
					int item = (num5 + num17) * mapWidth + (num4 + num18);
					if (selectionSet == null || selectionSet.Count == 0 || selectionSet.Contains(item))
					{
						int num19 = num18;
						int num20 = num3 - 1 - num17;
						array3[num19, num20] = array[num18, num17];
						array4[num19, num20] = array2[num18, num17];
					}
				}
			}
			break;
		}
		case "resize":
		{
			int inputW = num2;
			int inputH = num3;
			try
			{
				Window dlg = new Window
				{
					Title = "Resize Selection",
					Width = 260.0,
					Height = 140.0,
					WindowStartupLocation = WindowStartupLocation.CenterOwner,
					Owner = this
				};
				StackPanel stackPanel = new StackPanel
				{
					Margin = new Thickness(8.0)
				};
				stackPanel.Children.Add(new TextBlock
				{
					Text = "New Width:"
				});
				System.Windows.Controls.TextBox tbW = new System.Windows.Controls.TextBox
				{
					Text = num2.ToString()
				};
				stackPanel.Children.Add(tbW);
				stackPanel.Children.Add(new TextBlock
				{
					Text = "New Height:"
				});
				System.Windows.Controls.TextBox tbH = new System.Windows.Controls.TextBox
				{
					Text = num3.ToString()
				};
				stackPanel.Children.Add(tbH);
				StackPanel stackPanel2 = new StackPanel
				{
					Orientation = System.Windows.Controls.Orientation.Horizontal,
					HorizontalAlignment = System.Windows.HorizontalAlignment.Right
				};
				System.Windows.Controls.Button button = new System.Windows.Controls.Button
				{
					Content = "OK",
					Width = 64.0,
					Margin = new Thickness(4.0)
				};
				System.Windows.Controls.Button button2 = new System.Windows.Controls.Button
				{
					Content = "Cancel",
					Width = 64.0,
					Margin = new Thickness(4.0)
				};
				stackPanel2.Children.Add(button);
				stackPanel2.Children.Add(button2);
				stackPanel.Children.Add(stackPanel2);
				dlg.Content = stackPanel;
				button.Click += delegate
				{
					if (int.TryParse(tbW.Text, out var result) && int.TryParse(tbH.Text, out var result2))
					{
						inputW = Math.Max(1, result);
						inputH = Math.Max(1, result2);
						dlg.Tag = "ok";
						dlg.Close();
					}
					else
					{
						System.Windows.MessageBox.Show(this, "Invalid dimensions", "Resize", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					}
				};
				button2.Click += delegate
				{
					dlg.Tag = null;
					dlg.Close();
				};
				dlg.ShowDialog();
				if (dlg.Tag == null)
				{
					return;
				}
				num7 = inputW;
				num8 = inputH;
			}
			catch
			{
				return;
			}
			array3 = new int[num7, num8];
			array4 = new int[num7, num8];
			for (int num9 = 0; num9 < num8; num9++)
			{
				for (int num10 = 0; num10 < num7; num10++)
				{
					array3[num10, num9] = -1;
					array4[num10, num9] = -1;
				}
			}
			for (int num11 = 0; num11 < num8; num11++)
			{
				for (int num12 = 0; num12 < num7; num12++)
				{
					int num13 = (int)Math.Floor((double)num12 * (double)num2 / (double)num7);
					int num14 = (int)Math.Floor((double)num11 * (double)num3 / (double)num8);
					if (num13 < 0)
					{
						num13 = 0;
					}
					if (num13 >= num2)
					{
						num13 = num2 - 1;
					}
					if (num14 < 0)
					{
						num14 = 0;
					}
					if (num14 >= num3)
					{
						num14 = num3 - 1;
					}
					array3[num12, num11] = array[num13, num14];
					array4[num12, num11] = array2[num13, num14];
				}
			}
			break;
		}
		}
		int[] array5 = (int[])tiles.Clone();
		int[] array6 = (int[])sprites.Clone();
		for (int num39 = 0; num39 < num3; num39++)
		{
			for (int num40 = 0; num40 < num2; num40++)
			{
				int num41 = (num5 + num39) * mapWidth + (num4 + num40);
				if (num41 >= 0 && num41 < array5.Length)
				{
					array5[num41] = -1;
					array6[num41] = -1;
				}
			}
		}
		for (int num42 = 0; num42 < num8; num42++)
		{
			for (int num43 = 0; num43 < num7; num43++)
			{
				int num44 = (num5 + num42) * mapWidth + (num4 + num43);
				if (num44 >= 0 && num44 < array5.Length)
				{
					array5[num44] = array3[num43, num42];
					array6[num44] = array4[num43, num42];
				}
			}
		}
		TileChangeAction tileChangeAction = new TileChangeAction();
		SpriteChangeAction spriteChangeAction = new SpriteChangeAction();
		HashSet<int> hashSet = new HashSet<int>();
		for (int num45 = 0; num45 < num3; num45++)
		{
			for (int num46 = 0; num46 < num2; num46++)
			{
				hashSet.Add((num5 + num45) * mapWidth + (num4 + num46));
			}
		}
		for (int num47 = 0; num47 < num8; num47++)
		{
			for (int num48 = 0; num48 < num7; num48++)
			{
				hashSet.Add((num5 + num47) * mapWidth + (num4 + num48));
			}
		}
		foreach (int item6 in hashSet)
		{
			if (item6 >= 0 && item6 < tiles.Length)
			{
				int num49 = tiles[item6];
				int num50 = sprites[item6];
				int num51 = array5[item6];
				int num52 = array6[item6];
				if (num49 != num51)
				{
					tileChangeAction.Add(item6, num49, num51);
				}
				if (num50 != num52)
				{
					spriteChangeAction.Add(item6, num50, num52);
				}
			}
		}
		if (!spriteChangeAction.IsEmpty())
		{
			spriteChangeAction.CaptureOffsets(this);
		}
		for (int num53 = 0; num53 < tiles.Length; num53++)
		{
			tiles[num53] = array5[num53];
			sprites[num53] = array6[num53];
		}
		Dictionary<int, (int, int)> dictionary2 = new Dictionary<int, (int, int)>();
		for (int num54 = 0; num54 < num3; num54++)
		{
			for (int num55 = 0; num55 < num2; num55++)
			{
				int item5 = (num5 + num54) * mapWidth + (num4 + num55);
				if ((selectionSet == null || selectionSet.Count == 0 || selectionSet.Contains(item5)) && dictionary.TryGetValue((num55, num54), out var value2))
				{
					int num56 = 0;
					int num57 = 0;
					switch (op)
					{
					case "rotate":
						num56 = num3 - 1 - num54;
						num57 = num55;
						break;
					case "rotateccw":
						num56 = num54;
						num57 = num2 - 1 - num55;
						break;
					case "fliph":
						num56 = num2 - 1 - num55;
						num57 = num54;
						break;
					case "flipv":
						num56 = num55;
						num57 = num3 - 1 - num54;
						break;
					case "resize":
						num56 = (int)Math.Floor((double)num55 * (double)num7 / (double)num2);
						num57 = (int)Math.Floor((double)num54 * (double)num8 / (double)num3);
						break;
					default:
						num56 = num55;
						num57 = num54;
						break;
					}
					int num58 = (num5 + num57) * mapWidth + (num4 + num56);
					if (num58 >= 0 && num58 < tiles.Length)
					{
						dictionary2[num58] = value2;
					}
				}
			}
		}
		for (int num59 = 0; num59 < tiles.Length; num59++)
		{
			if (spritePixelOffsets.ContainsKey(num59) && hashSet.Contains(num59))
			{
				spritePixelOffsets.Remove(num59);
			}
		}
		foreach (KeyValuePair<int, (int, int)> item7 in dictionary2)
		{
			spritePixelOffsets[item7.Key] = item7.Value;
		}
		if (!spriteChangeAction.IsEmpty())
		{
			spriteChangeAction.CaptureNewOffsets(this);
		}
		if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(tileChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		if (!spriteChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(spriteChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		try
		{
			RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
		}
		try
		{
			RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
			Redraw();
		}
		HashSet<int> hashSet2 = new HashSet<int>();
		for (int num60 = 0; num60 < num8; num60++)
		{
			for (int num61 = 0; num61 < num7; num61++)
			{
				hashSet2.Add((num5 + num60) * mapWidth + (num4 + num61));
			}
		}
		selectionSet = hashSet2;
		selW = num7;
		selH = num8;
		UpdateSelectionVisuals(selX, selY, selW, selH);
	}

	private void CancelPolygon()
	{
		try
		{
			polygonPoints.Clear();
			isConstructingPolygon = false;
			ClearDeferredPreview();
			if (CanvasHost != null && CanvasHost.IsMouseCaptured)
			{
				CanvasHost.ReleaseMouseCapture();
			}
		}
		catch
		{
		}
	}

	private void UpdateDeferredPreview()
	{
		try
		{
			if (SelectionOverlay == null)
			{
				return;
			}
			SelectionOverlay.Children.Clear();
			DpiScale dpi = VisualTreeHelper.GetDpi(this);
			double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
			int num2 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleX));
			int num3 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleY));
			int num4 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
			int num5 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
			if (!isDeferredDrawing && !isConstructingPolygon)
			{
				return;
			}
			if (isConstructingPolygon && polygonPoints != null && polygonPoints.Count > 0)
			{
				List<Point> list = new List<Point>();
				foreach (var polygonPoint in polygonPoints)
				{
					double x = (double)(num4 + polygonPoint.x * num2 + num2 / 2) / dpi.DpiScaleX;
					double y = (double)(num5 + polygonPoint.y * num3 + num3 / 2) / dpi.DpiScaleY + gridRenderShiftY;
					list.Add(new Point(x, y));
				}
				if (drawCurrentX >= 0 && drawCurrentY >= 0)
				{
					double x2 = (double)(num4 + drawCurrentX * num2 + num2 / 2) / dpi.DpiScaleX;
					double y2 = (double)(num5 + drawCurrentY * num3 + num3 / 2) / dpi.DpiScaleY + gridRenderShiftY;
					list.Add(new Point(x2, y2));
				}
				Polyline polyline = new Polyline
				{
					Stroke = Brushes.Yellow,
					StrokeThickness = 1.0,
					IsHitTestVisible = false,
					Fill = Brushes.Transparent
				};
				try
				{
					polyline.StrokeThickness = 1.0 / dpi.DpiScaleX;
					polyline.SnapsToDevicePixels = true;
				}
				catch
				{
				}
				foreach (Point item9 in list)
				{
					polyline.Points.Add(item9);
				}
				SelectionOverlay.Children.Add(polyline);
				List<(int, int)> list2 = new List<(int, int)>();
				if (!hollowShape)
				{
					List<(int, int)> list3 = new List<(int, int)>(polygonPoints);
					if (drawCurrentX >= 0 && drawCurrentY >= 0)
					{
						list3.Add((drawCurrentX, drawCurrentY));
					}
					list2 = GetFilledPolygonTiles(list3);
				}
				else
				{
					List<(int, int)> list4 = new List<(int, int)>(polygonPoints);
					if (drawCurrentX >= 0 && drawCurrentY >= 0)
					{
						list4.Add((drawCurrentX, drawCurrentY));
					}
					for (int i = 0; i + 1 < list4.Count; i++)
					{
						(int, int) tuple = list4[i];
						(int, int) tuple2 = list4[i + 1];
						List<(int, int)> list5 = ComputeDrawTileList(tuple.Item1, tuple.Item2, tuple2.Item1, tuple2.Item2, DrawMode.Line, brushThickness);
						foreach (var item10 in list5)
						{
							list2.Add(item10);
						}
					}
				}
				if (list2.Count <= 0)
				{
					return;
				}
				ImageSource imageSource = null;
				if (tilesLayerActive && selectedTile >= 0 && tileImages != null)
				{
					try
					{
						imageSource = ((tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[selectedTile] : tileImages[selectedTile]);
					}
					catch
					{
						imageSource = null;
					}
				}
				if (spritesLayerActive && selectedSprite >= 0 && spriteImages != null)
				{
					imageSource = spriteImages[selectedSprite];
				}
				if (imageSource != null)
				{
					foreach (var item11 in list2)
					{
						int item = item11.Item1;
						int item2 = item11.Item2;
						Image element = new Image
						{
							Source = imageSource,
							Width = (double)num2 / dpi.DpiScaleX,
							Height = (double)num3 / dpi.DpiScaleY,
							Opacity = 0.5,
							IsHitTestVisible = false
						};
						Canvas.SetLeft(element, (double)(num4 + item * num2) / dpi.DpiScaleX);
						Canvas.SetTop(element, (double)(num5 + item2 * num3) / dpi.DpiScaleY + gridRenderShiftY);
						SelectionOverlay.Children.Add(element);
					}
					return;
				}
				Color color = ((EraseTool != null && EraseTool.IsChecked == true) ? Color.FromArgb(128, 220, 64, 64) : ((SelectTool != null && SelectTool.IsChecked == true) ? Color.FromArgb(96, 64, 160, byte.MaxValue) : Color.FromArgb(96, byte.MaxValue, byte.MaxValue, 0)));
				SolidColorBrush fill = new SolidColorBrush(color);
				{
					foreach (var item12 in list2)
					{
						int item3 = item12.Item1;
						int item4 = item12.Item2;
						Rectangle element2 = new Rectangle
						{
							Width = (double)num2 / dpi.DpiScaleX,
							Height = (double)num3 / dpi.DpiScaleY,
							Fill = fill,
							Stroke = Brushes.Transparent,
							IsHitTestVisible = false
						};
						Canvas.SetLeft(element2, (double)(num4 + item3 * num2) / dpi.DpiScaleX);
						Canvas.SetTop(element2, (double)(num5 + item4 * num3) / dpi.DpiScaleY + gridRenderShiftY);
						SelectionOverlay.Children.Add(element2);
					}
					return;
				}
			}
			if (currentDrawMode == DrawMode.Line)
			{
				double x3 = (double)(num4 + drawStartX * num2 + num2 / 2) / dpi.DpiScaleX;
				double y3 = (double)(num5 + drawStartY * num3 + num3 / 2) / dpi.DpiScaleY + gridRenderShiftY;
				double x4 = (double)(num4 + drawCurrentX * num2 + num2 / 2) / dpi.DpiScaleX;
				double y4 = (double)(num5 + drawCurrentY * num3 + num3 / 2) / dpi.DpiScaleY + gridRenderShiftY;
				Line line = new Line
				{
					X1 = x3,
					Y1 = y3,
					X2 = x4,
					Y2 = y4,
					Stroke = Brushes.Yellow,
					StrokeThickness = 1.0,
					IsHitTestVisible = false
				};
				try
				{
					line.StrokeThickness = 1.0 / dpi.DpiScaleX;
					line.SnapsToDevicePixels = true;
				}
				catch
				{
				}
				SelectionOverlay.Children.Add(line);
				List<(int, int)> tilesPreview = ComputeDrawTileList(drawStartX, drawStartY, drawCurrentX, drawCurrentY, DrawMode.Line, brushThickness);
				DrawGhostTiles(tilesPreview, num2, num3, num4, num5, dpi);
			}
			else if (currentDrawMode == DrawMode.Square)
			{
				double length = (double)(num4 + Math.Min(drawStartX, drawCurrentX) * num2) / dpi.DpiScaleX;
				double length2 = (double)(num5 + Math.Min(drawStartY, drawCurrentY) * num3) / dpi.DpiScaleY + gridRenderShiftY;
				double width = (double)((Math.Abs(drawCurrentX - drawStartX) + 1) * num2) / dpi.DpiScaleX;
				double height = (double)((Math.Abs(drawCurrentY - drawStartY) + 1) * num3) / dpi.DpiScaleY;
				Rectangle rectangle = new Rectangle
				{
					Width = width,
					Height = height,
					Stroke = Brushes.Yellow,
					StrokeThickness = 1.0,
					Fill = Brushes.Transparent,
					IsHitTestVisible = false
				};
				try
				{
					rectangle.StrokeThickness = 1.0 / dpi.DpiScaleX;
					rectangle.SnapsToDevicePixels = true;
				}
				catch
				{
				}
				Canvas.SetLeft(rectangle, length);
				Canvas.SetTop(rectangle, length2);
				SelectionOverlay.Children.Add(rectangle);
				List<(int, int)> tilesPreview2 = ComputeDrawTileList(drawStartX, drawStartY, drawCurrentX, drawCurrentY, DrawMode.Square, brushThickness);
				DrawGhostTiles(tilesPreview2, num2, num3, num4, num5, dpi);
			}
			else if (currentDrawMode == DrawMode.Triangle)
			{
				int num6 = Math.Min(drawStartX, drawCurrentX);
				int num7 = Math.Max(drawStartX, drawCurrentX);
				int num8 = Math.Min(drawStartY, drawCurrentY);
				int num9 = Math.Max(drawStartY, drawCurrentY);
				double x5 = ((double)num4 + (double)(num6 + num7) / 2.0 * (double)num2 + (double)(num2 / 2)) / dpi.DpiScaleX;
				double y5 = (double)(num5 + num8 * num3 + num3 / 2) / dpi.DpiScaleY + gridRenderShiftY;
				double x6 = (double)(num4 + num6 * num2 + num2 / 2) / dpi.DpiScaleX;
				double x7 = (double)(num4 + num7 * num2 + num2 / 2) / dpi.DpiScaleX;
				double y6 = (double)(num5 + num9 * num3 + num3 / 2) / dpi.DpiScaleY + gridRenderShiftY;
				Polyline polyline2 = new Polyline
				{
					Stroke = Brushes.Yellow,
					StrokeThickness = 1.0,
					IsHitTestVisible = false,
					Fill = Brushes.Transparent
				};
				polyline2.Points.Add(new Point(x5, y5));
				polyline2.Points.Add(new Point(x7, y6));
				polyline2.Points.Add(new Point(x6, y6));
				polyline2.Points.Add(new Point(x5, y5));
				try
				{
					polyline2.StrokeThickness = 1.0 / dpi.DpiScaleX;
					polyline2.SnapsToDevicePixels = true;
				}
				catch
				{
				}
				SelectionOverlay.Children.Add(polyline2);
				List<(int, int)> tilesPreview3 = ComputeDrawTileList(Math.Min(drawStartX, drawCurrentX), Math.Min(drawStartY, drawCurrentY), Math.Max(drawStartX, drawCurrentX), Math.Max(drawStartY, drawCurrentY), DrawMode.Triangle, brushThickness);
				DrawGhostTiles(tilesPreview3, num2, num3, num4, num5, dpi);
			}
			else
			{
				if (currentDrawMode == DrawMode.Circle)
				{
					List<(int, int)> list6 = ComputeDrawTileList(drawStartX, drawStartY, drawCurrentX, drawCurrentY, DrawMode.Circle, brushThickness);
					if (list6.Count == 0)
					{
						return;
					}
					int num10 = int.MaxValue;
					int num11 = int.MaxValue;
					int num12 = int.MinValue;
					int num13 = int.MinValue;
					foreach (var (num14, num15) in list6)
					{
						if (num14 < num10)
						{
							num10 = num14;
						}
						if (num14 > num12)
						{
							num12 = num14;
						}
						if (num15 < num11)
						{
							num11 = num15;
						}
						if (num15 > num13)
						{
							num13 = num15;
						}
					}
					double length3 = (double)(num4 + num10 * num2) / dpi.DpiScaleX;
					double length4 = (double)(num5 + num11 * num3) / dpi.DpiScaleY + gridRenderShiftY;
					double width2 = (double)((num12 - num10 + 1) * num2) / dpi.DpiScaleX;
					double height2 = (double)((num13 - num11 + 1) * num3) / dpi.DpiScaleY;
					Ellipse ellipse = new Ellipse
					{
						Width = width2,
						Height = height2,
						Stroke = Brushes.Yellow,
						StrokeThickness = 1.0,
						Fill = Brushes.Transparent,
						IsHitTestVisible = false
					};
					try
					{
						ellipse.StrokeThickness = 1.0 / dpi.DpiScaleX;
						ellipse.SnapsToDevicePixels = true;
					}
					catch
					{
					}
					Canvas.SetLeft(ellipse, length3);
					Canvas.SetTop(ellipse, length4);
					SelectionOverlay.Children.Add(ellipse);
					ImageSource imageSource2 = null;
					if (tilesLayerActive && selectedTile >= 0 && tileImages != null)
					{
						try
						{
							imageSource2 = ((tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[selectedTile] : tileImages[selectedTile]);
						}
						catch
						{
							imageSource2 = null;
						}
					}
					if (spritesLayerActive && selectedSprite >= 0 && spriteImages != null)
					{
						imageSource2 = spriteImages[selectedSprite];
					}
					if (imageSource2 == null)
					{
						return;
					}
					{
						foreach (var item13 in list6)
						{
							int item5 = item13.Item1;
							int item6 = item13.Item2;
							Image element3 = new Image
							{
								Source = imageSource2,
								Width = (double)num2 / dpi.DpiScaleX,
								Height = (double)num3 / dpi.DpiScaleY,
								Opacity = 0.5,
								IsHitTestVisible = false
							};
							Canvas.SetLeft(element3, (double)(num4 + item5 * num2) / dpi.DpiScaleX);
							Canvas.SetTop(element3, (double)(num5 + item6 * num3) / dpi.DpiScaleY + gridRenderShiftY);
							SelectionOverlay.Children.Add(element3);
						}
						return;
					}
				}
				if (currentDrawMode != DrawMode.Ellipse)
				{
					return;
				}
				List<(int, int)> list7 = ComputeDrawTileList(drawStartX, drawStartY, drawCurrentX, drawCurrentY, DrawMode.Ellipse, brushThickness);
				if (list7.Count == 0)
				{
					return;
				}
				int num16 = int.MaxValue;
				int num17 = int.MaxValue;
				int num18 = int.MinValue;
				int num19 = int.MinValue;
				foreach (var (num20, num21) in list7)
				{
					if (num20 < num16)
					{
						num16 = num20;
					}
					if (num20 > num18)
					{
						num18 = num20;
					}
					if (num21 < num17)
					{
						num17 = num21;
					}
					if (num21 > num19)
					{
						num19 = num21;
					}
				}
				double length5 = (double)(num4 + num16 * num2) / dpi.DpiScaleX;
				double length6 = (double)(num5 + num17 * num3) / dpi.DpiScaleY + gridRenderShiftY;
				double width3 = (double)((num18 - num16 + 1) * num2) / dpi.DpiScaleX;
				double height3 = (double)((num19 - num17 + 1) * num3) / dpi.DpiScaleY;
				Ellipse ellipse2 = new Ellipse
				{
					Width = width3,
					Height = height3,
					Stroke = Brushes.Yellow,
					StrokeThickness = 1.0,
					Fill = Brushes.Transparent,
					IsHitTestVisible = false
				};
				try
				{
					ellipse2.StrokeThickness = 1.0 / dpi.DpiScaleX;
					ellipse2.SnapsToDevicePixels = true;
				}
				catch
				{
				}
				Canvas.SetLeft(ellipse2, length5);
				Canvas.SetTop(ellipse2, length6);
				SelectionOverlay.Children.Add(ellipse2);
				ImageSource imageSource3 = null;
				if (tilesLayerActive && selectedTile >= 0 && tileImages != null)
				{
					try
					{
						imageSource3 = ((tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[selectedTile] : tileImages[selectedTile]);
					}
					catch
					{
						imageSource3 = null;
					}
				}
				if (spritesLayerActive && selectedSprite >= 0 && spriteImages != null)
				{
					imageSource3 = spriteImages[selectedSprite];
				}
				if (imageSource3 == null)
				{
					return;
				}
				{
					foreach (var item14 in list7)
					{
						int item7 = item14.Item1;
						int item8 = item14.Item2;
						Image element4 = new Image
						{
							Source = imageSource3,
							Width = (double)num2 / dpi.DpiScaleX,
							Height = (double)num3 / dpi.DpiScaleY,
							Opacity = 0.5,
							IsHitTestVisible = false
						};
						Canvas.SetLeft(element4, (double)(num4 + item7 * num2) / dpi.DpiScaleX);
						Canvas.SetTop(element4, (double)(num5 + item8 * num3) / dpi.DpiScaleY + gridRenderShiftY);
						SelectionOverlay.Children.Add(element4);
					}
					return;
				}
			}
		}
		catch
		{
		}
	}

	private List<(int x, int y)> ComputeDrawTileList(int sx, int sy, int ex, int ey, DrawMode mode, int thickness)
	{
		List<(int, int)> list = new List<(int, int)>();
		if (sx < 0 || sy < 0 || ex < 0 || ey < 0)
		{
			return list;
		}
		switch (mode)
		{
		case DrawMode.Tile:
		{
			int num70 = (thickness - 1) / 2;
			for (int num71 = -num70; num71 <= num70; num71++)
			{
				for (int num72 = -num70; num72 <= num70; num72++)
				{
					int num73 = sx + num72;
					int num74 = sy + num71;
					if (num73 >= 0 && num73 < mapWidth && num74 >= 0 && num74 < mapHeight)
					{
						list.Add((num73, num74));
					}
				}
			}
			return list;
		}
		case DrawMode.Line:
		{
			int num57 = sx;
			int num58 = sy;
			int num59 = Math.Abs(ex - num57);
			int num60 = ((num57 < ex) ? 1 : (-1));
			int num61 = -Math.Abs(ey - num58);
			int num62 = ((num58 < ey) ? 1 : (-1));
			int num63 = num59 + num61;
			while (true)
			{
				int num64 = (thickness - 1) / 2;
				for (int num65 = -num64; num65 <= num64; num65++)
				{
					for (int num66 = -num64; num66 <= num64; num66++)
					{
						int num67 = num57 + num66;
						int num68 = num58 + num65;
						if (num67 >= 0 && num67 < mapWidth && num68 >= 0 && num68 < mapHeight)
						{
							list.Add((num67, num68));
						}
					}
				}
				if (num57 == ex && num58 == ey)
				{
					break;
				}
				int num69 = 2 * num63;
				if (num69 >= num61)
				{
					num63 += num61;
					num57 += num60;
				}
				if (num69 <= num59)
				{
					num63 += num59;
					num58 += num62;
				}
			}
			return list;
		}
		default:
		{
			int num = Math.Min(sx, ex);
			int num2 = Math.Max(sx, ex);
			int num3 = Math.Min(sy, ey);
			int num4 = Math.Max(sy, ey);
			switch (mode)
			{
			case DrawMode.Square:
				if (hollowShape)
				{
					int num20 = Math.Max(1, thickness);
					for (int m = num3; m <= num4; m++)
					{
						for (int n = num; n <= num2; n++)
						{
							if ((n - num < num20 || num2 - n < num20 || m - num3 < num20 || num4 - m < num20) && n >= 0 && n < mapWidth && m >= 0 && m < mapHeight)
							{
								list.Add((n, m));
							}
						}
					}
				}
				else
				{
					for (int num21 = num3; num21 <= num4; num21++)
					{
						for (int num22 = num; num22 <= num2; num22++)
						{
							if (thickness <= 1)
							{
								if (num22 >= 0 && num22 < mapWidth && num21 >= 0 && num21 < mapHeight)
								{
									list.Add((num22, num21));
								}
								continue;
							}
							int num23 = (thickness - 1) / 2;
							for (int num24 = -num23; num24 <= num23; num24++)
							{
								for (int num25 = -num23; num25 <= num23; num25++)
								{
									int num26 = num22 + num25;
									int num27 = num21 + num24;
									if (num26 >= 0 && num26 < mapWidth && num27 >= 0 && num27 < mapHeight)
									{
										list.Add((num26, num27));
									}
								}
							}
						}
					}
				}
				return list;
			case DrawMode.Triangle:
			{
				int num28 = (num + num2) / 2;
				int num29 = num3;
				int num30 = num4;
				if (!hollowShape)
				{
					int num31 = Math.Max(1, num30 - num29);
					for (int num32 = num29; num32 <= num30; num32++)
					{
						double num33 = (double)(num32 - num29) / (double)num31;
						int num34 = (int)Math.Round((double)(num2 - num + 1) * num33);
						int num35 = num28;
						int num36 = Math.Max(num, num35 - num34 / 2 - (thickness - 1));
						int num37 = Math.Min(num2, num35 + num34 / 2 + (thickness - 1));
						for (int num38 = num36; num38 <= num37; num38++)
						{
							if (num38 >= 0 && num38 < mapWidth && num32 >= 0 && num32 < mapHeight)
							{
								list.Add((num38, num32));
							}
						}
					}
					return list;
				}
				HashSet<(int, int)> hashSet = new HashSet<(int, int)>();
				int num39 = num;
				int ex2 = num2;
				List<(int, int)> list2 = ComputeDrawTileList(num28, num29, num39, num30, DrawMode.Line, thickness);
				foreach (var item in list2)
				{
					hashSet.Add(item);
				}
				List<(int, int)> list3 = ComputeDrawTileList(num28, num29, ex2, num30, DrawMode.Line, thickness);
				foreach (var item2 in list3)
				{
					hashSet.Add(item2);
				}
				List<(int, int)> list4 = ComputeDrawTileList(num39, num30, ex2, num30, DrawMode.Line, thickness);
				foreach (var item3 in list4)
				{
					hashSet.Add(item3);
				}
				list.AddRange(hashSet);
				return list;
			}
			case DrawMode.Circle:
			{
				int val = Math.Abs(ex - sx);
				int val2 = Math.Abs(ey - sy);
				int num40 = Math.Max(1, Math.Max(val, val2) + 1);
				int num41 = sx;
				int num42 = sy;
				int num43 = thickness;
				if (num43 <= 1)
				{
					num43 = 2;
				}
				num41 = (sx + ex) / 2;
				num42 = (sy + ey) / 2;
				if (hollowShape)
				{
					int num44 = Math.Max(1, num43);
					int num45 = Math.Max(0, num40 - num44 + 1);
					int num46 = num40 * num40;
					int num47 = num45 * num45;
					for (int num48 = num42 - num40; num48 <= num42 + num40; num48++)
					{
						for (int num49 = num41 - num40; num49 <= num41 + num40; num49++)
						{
							int num50 = num49 - num41;
							int num51 = num48 - num42;
							int num52 = num50 * num50 + num51 * num51;
							if (num52 <= num46 && num52 >= num47 && num49 >= 0 && num49 < mapWidth && num48 >= 0 && num48 < mapHeight)
							{
								list.Add((num49, num48));
							}
						}
					}
				}
				else
				{
					for (int num53 = num42 - num40; num53 <= num42 + num40; num53++)
					{
						for (int num54 = num41 - num40; num54 <= num41 + num40; num54++)
						{
							int num55 = num54 - num41;
							int num56 = num53 - num42;
							if (num55 * num55 + num56 * num56 <= num40 * num40 && num54 >= 0 && num54 < mapWidth && num53 >= 0 && num53 < mapHeight)
							{
								list.Add((num54, num53));
							}
						}
					}
				}
				return list;
			}
			case DrawMode.Ellipse:
			{
				num = Math.Min(sx, ex);
				num2 = Math.Max(sx, ex);
				num3 = Math.Min(sy, ey);
				num4 = Math.Max(sy, ey);
				double num5 = Math.Max(1.0, (double)(num2 - num + 1) / 2.0);
				double num6 = Math.Max(1.0, (double)(num4 - num3 + 1) / 2.0);
				double num7 = (double)(num + num2) / 2.0;
				double num8 = (double)(num3 + num4) / 2.0;
				if (hollowShape)
				{
					int num9 = Math.Max(1, thickness);
					double num10 = Math.Max(0.0001, num5 - (double)num9 + 1.0);
					double num11 = Math.Max(0.0001, num6 - (double)num9 + 1.0);
					for (int i = (int)Math.Floor(num8 - num6); i <= (int)Math.Ceiling(num8 + num6); i++)
					{
						for (int j = (int)Math.Floor(num7 - num5); j <= (int)Math.Ceiling(num7 + num5); j++)
						{
							double num12 = ((double)j - num7) / num5;
							double num13 = ((double)i - num8) / num6;
							double num14 = num12 * num12 + num13 * num13;
							double num15 = ((double)j - num7) / num10;
							double num16 = ((double)i - num8) / num11;
							double num17 = num15 * num15 + num16 * num16;
							if (num14 <= 1.0 && num17 >= 1.0 && j >= 0 && j < mapWidth && i >= 0 && i < mapHeight)
							{
								list.Add((j, i));
							}
						}
					}
				}
				else
				{
					for (int k = (int)Math.Floor(num8 - num6); k <= (int)Math.Ceiling(num8 + num6); k++)
					{
						for (int l = (int)Math.Floor(num7 - num5); l <= (int)Math.Ceiling(num7 + num5); l++)
						{
							double num18 = ((double)l - num7) / num5;
							double num19 = ((double)k - num8) / num6;
							if (num18 * num18 + num19 * num19 <= 1.0 && l >= 0 && l < mapWidth && k >= 0 && k < mapHeight)
							{
								list.Add((l, k));
							}
						}
					}
				}
				return list;
			}
			default:
				return list;
			}
		}
		}
	}

	private void CommitDeferredDraw()
	{
		try
		{
			if (!isDeferredDrawing)
			{
				return;
			}
			List<(int, int)> list = ComputeDrawTileList(drawStartX, drawStartY, drawCurrentX, drawCurrentY, currentDrawMode, brushThickness);
			if (list.Count == 0)
			{
				isDeferredDrawing = false;
				ClearDeferredPreview();
				if (CanvasHost != null && CanvasHost.IsMouseCaptured)
				{
					CanvasHost.ReleaseMouseCapture();
				}
				return;
			}
			if (EraseTool != null && EraseTool.IsChecked == true)
			{
				TileChangeAction tileChangeAction = new TileChangeAction();
				SpriteChangeAction spriteChangeAction = new SpriteChangeAction();
				foreach (var item8 in list)
				{
					int item = item8.Item1;
					int item2 = item8.Item2;
					int num = item2 * mapWidth + item;
					if (tilesLayerActive)
					{
						int num2 = tiles[num];
						if (num2 != -1)
						{
							tileChangeAction.Add(num, num2, -1);
							tiles[num] = -1;
						}
					}
					if (spritesLayerActive)
					{
						int num3 = sprites[num];
						if (num3 != -1)
						{
							spriteChangeAction.Add(num, num3, -1);
							sprites[num] = -1;
						}
					}
				}
				if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
				{
					undoStack.Push(tileChangeAction);
					redoStack.Clear();
					SetHasUnsavedChanges(unsaved: true);
				}
				if (!spriteChangeAction.IsEmpty() && !suppressUndoRecording)
				{
					undoStack.Push(spriteChangeAction);
					redoStack.Clear();
					SetHasUnsavedChanges(unsaved: true);
				}
				try
				{
					RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
				}
				try
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
					Redraw();
				}
				isDeferredDrawing = false;
				drawStartX = (drawStartY = (drawCurrentX = (drawCurrentY = -1)));
				ClearDeferredPreview();
				if (CanvasHost != null && CanvasHost.IsMouseCaptured)
				{
					CanvasHost.ReleaseMouseCapture();
				}
				return;
			}
			if (SelectTool != null && SelectTool.IsChecked == true)
			{
				bool flag = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
				HashSet<int> hashSet = new HashSet<int>();
				foreach (var item9 in list)
				{
					int item3 = item9.Item1;
					int item4 = item9.Item2;
					int item5 = item4 * mapWidth + item3;
					hashSet.Add(item5);
				}
				if (!flag)
				{
					selectionSet.Clear();
				}
				foreach (int item10 in hashSet)
				{
					selectionSet.Add(item10);
				}
				if (selectionSet.Count == 0)
				{
					ClearSelection();
				}
				else
				{
					int num4 = int.MaxValue;
					int num5 = int.MaxValue;
					int num6 = int.MinValue;
					int num7 = int.MinValue;
					foreach (int item11 in selectionSet)
					{
						int num8 = item11 % mapWidth;
						int num9 = item11 / mapWidth;
						if (num8 < num4)
						{
							num4 = num8;
						}
						if (num9 < num5)
						{
							num5 = num9;
						}
						if (num8 > num6)
						{
							num6 = num8;
						}
						if (num9 > num7)
						{
							num7 = num9;
						}
					}
					selX = num4;
					selY = num5;
					selW = num6 - num4 + 1;
					selH = num7 - num5 + 1;
					selTiles = new int[selW * selH];
					selSprites = new int[selW * selH];
					for (int i = 0; i < selH; i++)
					{
						for (int j = 0; j < selW; j++)
						{
							int num10 = (selY + i) * mapWidth + (selX + j);
							if (selectionSet.Contains(num10))
							{
								selTiles[i * selW + j] = (tilesLayerActive ? tiles[num10] : (-1));
								selSprites[i * selW + j] = (spritesLayerActive ? sprites[num10] : (-1));
							}
							else
							{
								selTiles[i * selW + j] = -1;
								selSprites[i * selW + j] = -1;
							}
						}
					}
					UpdateSelectionVisuals(selX, selY, selW, selH);
				}
				isDeferredDrawing = false;
				drawStartX = (drawStartY = (drawCurrentX = (drawCurrentY = -1)));
				ClearDeferredPreview();
				if (CanvasHost != null && CanvasHost.IsMouseCaptured)
				{
					CanvasHost.ReleaseMouseCapture();
				}
				return;
			}
			TileChangeAction tileChangeAction2 = new TileChangeAction();
			SpriteChangeAction spriteChangeAction2 = new SpriteChangeAction();
			foreach (var item12 in list)
			{
				int item6 = item12.Item1;
				int item7 = item12.Item2;
				int num11 = item7 * mapWidth + item6;
				if (tilesLayerActive && selectedTile >= 0)
				{
					int num12 = tiles[num11];
					int num13 = selectedTile;
					if (num12 != num13)
					{
						tileChangeAction2.Add(num11, num12, num13);
						tiles[num11] = num13;
					}
				}
				if (spritesLayerActive && selectedSprite >= 0)
				{
					int num14 = sprites[num11];
					int num15 = selectedSprite;
					if (num14 != num15)
					{
						spriteChangeAction2.Add(num11, num14, num15);
						sprites[num11] = num15;
					}
				}
			}
			bool flag2 = false;
			bool flag3 = false;
			if (!tileChangeAction2.IsEmpty() && !suppressUndoRecording)
			{
				undoStack.Push(tileChangeAction2);
				redoStack.Clear();
				SetHasUnsavedChanges(unsaved: true);
				flag2 = true;
			}
			if (!spriteChangeAction2.IsEmpty() && !suppressUndoRecording)
			{
				undoStack.Push(spriteChangeAction2);
				redoStack.Clear();
				SetHasUnsavedChanges(unsaved: true);
				flag3 = true;
			}
			try
			{
				if (flag2)
				{
					RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
			}
			catch
			{
			}
			try
			{
				if (flag3)
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				else if (flag2)
				{
					Redraw();
				}
			}
			catch
			{
				Redraw();
			}
		}
		catch
		{
		}
		finally
		{
			isDeferredDrawing = false;
			drawStartX = (drawStartY = (drawCurrentX = (drawCurrentY = -1)));
			ClearDeferredPreview();
			if (CanvasHost != null && CanvasHost.IsMouseCaptured)
			{
				CanvasHost.ReleaseMouseCapture();
			}
			if (deferredDrawFromModifier)
			{
				deferredDrawFromModifier = false;
				try
				{
					currentDrawMode = DrawMode.Tile;
				}
				catch
				{
				}
				try
				{
					if (DrawTileButton != null)
					{
						DrawTileButton.IsChecked = true;
					}
				}
				catch
				{
				}
			}
			lastInputAction = DateTime.Now;
		}
	}

	private void ClearDeferredPreview()
	{
		try
		{
			if (SelectionOverlay != null)
			{
				SelectionOverlay.Children.Clear();
			}
		}
		catch
		{
		}
	}

	private void DrawGhostTiles(List<(int x, int y)> tilesPreview, int tilePixelW, int tilePixelH, int padPxX, int padPxY, DpiScale dpi)
	{
		if (tilesPreview == null || tilesPreview.Count == 0)
		{
			return;
		}
		double dpiScaleX = dpi.DpiScaleX;
		double dpiScaleY = dpi.DpiScaleY;
		if (EraseTool != null && EraseTool.IsChecked == true)
		{
			SolidColorBrush fill = new SolidColorBrush(Color.FromArgb(160, 220, 64, 64));
			{
				foreach (var item9 in tilesPreview)
				{
					int item = item9.x;
					int item2 = item9.y;
					Rectangle element = new Rectangle
					{
						Width = (double)tilePixelW / dpiScaleX,
						Height = (double)tilePixelH / dpiScaleY,
						Fill = fill,
						Stroke = Brushes.Transparent,
						IsHitTestVisible = false
					};
					Canvas.SetLeft(element, (double)(padPxX + item * tilePixelW) / dpiScaleX);
					Canvas.SetTop(element, (double)(padPxY + item2 * tilePixelH) / dpiScaleY + gridRenderShiftY);
					SelectionOverlay.Children.Add(element);
				}
				return;
			}
		}
		if (SelectTool != null && SelectTool.IsChecked == true)
		{
			SolidColorBrush fill2 = new SolidColorBrush(Color.FromArgb(120, 64, 160, byte.MaxValue));
			{
				foreach (var item10 in tilesPreview)
				{
					int item3 = item10.x;
					int item4 = item10.y;
					Rectangle element2 = new Rectangle
					{
						Width = (double)tilePixelW / dpiScaleX,
						Height = (double)tilePixelH / dpiScaleY,
						Fill = fill2,
						Stroke = Brushes.Transparent,
						IsHitTestVisible = false
					};
					Canvas.SetLeft(element2, (double)(padPxX + item3 * tilePixelW) / dpiScaleX);
					Canvas.SetTop(element2, (double)(padPxY + item4 * tilePixelH) / dpiScaleY + gridRenderShiftY);
					SelectionOverlay.Children.Add(element2);
				}
				return;
			}
		}
		ImageSource imageSource = null;
		if (tilesLayerActive && selectedTile >= 0 && tileImages != null)
		{
			try
			{
				imageSource = ((tileTonedImages != null && tileTonedImages.Length == tileImages.Length) ? tileTonedImages[selectedTile] : tileImages[selectedTile]);
			}
			catch
			{
				imageSource = null;
			}
		}
		if (spritesLayerActive && selectedSprite >= 0 && spriteImages != null)
		{
			imageSource = spriteImages[selectedSprite];
		}
		if (imageSource == null)
		{
			SolidColorBrush fill3 = new SolidColorBrush(Color.FromArgb(96, byte.MaxValue, byte.MaxValue, 0));
			{
				foreach (var item11 in tilesPreview)
				{
					int item5 = item11.x;
					int item6 = item11.y;
					Rectangle element3 = new Rectangle
					{
						Width = (double)tilePixelW / dpiScaleX,
						Height = (double)tilePixelH / dpiScaleY,
						Fill = fill3,
						Stroke = Brushes.Transparent,
						IsHitTestVisible = false
					};
					Canvas.SetLeft(element3, (double)(padPxX + item5 * tilePixelW) / dpiScaleX);
					Canvas.SetTop(element3, (double)(padPxY + item6 * tilePixelH) / dpiScaleY + gridRenderShiftY);
					SelectionOverlay.Children.Add(element3);
				}
				return;
			}
		}
		foreach (var item12 in tilesPreview)
		{
			int item7 = item12.x;
			int item8 = item12.y;
			Image element4 = new Image
			{
				Source = imageSource,
				Width = (double)tilePixelW / dpiScaleX,
				Height = (double)tilePixelH / dpiScaleY,
				Opacity = 0.5,
				IsHitTestVisible = false
			};
			Canvas.SetLeft(element4, (double)(padPxX + item7 * tilePixelW) / dpiScaleX);
			Canvas.SetTop(element4, (double)(padPxY + item8 * tilePixelH) / dpiScaleY + gridRenderShiftY);
			SelectionOverlay.Children.Add(element4);
		}
	}

	private void EndSelection()
	{
		if (!isSelecting)
		{
			return;
		}
		isSelecting = false;
		if (CanvasHost != null && CanvasHost.IsMouseCaptured)
		{
			CanvasHost.ReleaseMouseCapture();
		}
		if (!hasMouseMoved && selectionSet.Count > 0)
		{
			int item = selectStartY * mapWidth + selectStartX;
			if (!selectionSet.Contains(item))
			{
				ClearSelection();
				return;
			}
		}
		double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
		if (SelectionOverlay == null || SelectionOverlay.Children.Count == 0 || !(SelectionOverlay.Children[0] is FrameworkElement frameworkElement))
		{
			return;
		}
		double num2 = mapViewportPadding;
		double num3 = Canvas.GetLeft(frameworkElement) - num2;
		double num4 = Canvas.GetTop(frameworkElement) - num2;
		int num5 = Math.Max(0, Math.Min(mapWidth - 1, (int)(num3 / (16.0 * num))));
		int num6 = Math.Max(0, Math.Min(mapHeight - 1, (int)(num4 / (16.0 * num))));
		int num7 = Math.Max(1, (int)Math.Round(frameworkElement.Width / (16.0 * num)));
		int num8 = Math.Max(1, (int)Math.Round(frameworkElement.Height / (16.0 * num)));
		if (num5 + num7 > mapWidth)
		{
			num7 = mapWidth - num5;
		}
		if (num6 + num8 > mapHeight)
		{
			num8 = mapHeight - num6;
		}
		selX = num5;
		selY = num6;
		selW = num7;
		selH = num8;
		HashSet<int> hashSet = new HashSet<int>();
		if (currentSelectMode == SelectMode.Ellipse)
		{
			double num9 = (double)selW / 2.0;
			double num10 = (double)selH / 2.0;
			for (int i = 0; i < selH; i++)
			{
				for (int j = 0; j < selW; j++)
				{
					int num11 = (selY + i) * mapWidth + (selX + j);
					bool flag = tilesLayerActive && tiles[num11] != -1;
					bool flag2 = spritesLayerActive && sprites[num11] != -1;
					if (!flag && !flag2)
					{
						continue;
					}
					double num12 = (double)j + 0.5;
					double num13 = (double)i + 0.5;
					double num14 = num12 - num9;
					double num15 = num13 - num10;
					if (num9 <= 0.0 || num10 <= 0.0)
					{
						hashSet.Add(num11);
						continue;
					}
					double num16 = num14 * num14 / (num9 * num9) + num15 * num15 / (num10 * num10);
					if (num16 <= 1.0)
					{
						hashSet.Add(num11);
					}
				}
			}
		}
		else
		{
			for (int k = 0; k < selH; k++)
			{
				for (int l = 0; l < selW; l++)
				{
					int num17 = (selY + k) * mapWidth + (selX + l);
					bool flag3 = tilesLayerActive && tiles[num17] != -1;
					bool flag4 = spritesLayerActive && sprites[num17] != -1;
					if (flag3 || flag4)
					{
						hashSet.Add(num17);
					}
				}
			}
		}
		if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
		{
			selectionSet = hashSet;
		}
		else
		{
			foreach (int item2 in hashSet)
			{
				selectionSet.Add(item2);
			}
		}
		if (selectionSet.Count == 0)
		{
			ClearSelection();
			return;
		}
		num5 = int.MaxValue;
		num6 = int.MaxValue;
		int num18 = int.MinValue;
		int num19 = int.MinValue;
		foreach (int item3 in selectionSet)
		{
			int num20 = item3 % mapWidth;
			int num21 = item3 / mapWidth;
			if (num20 < num5)
			{
				num5 = num20;
			}
			if (num21 < num6)
			{
				num6 = num21;
			}
			if (num20 > num18)
			{
				num18 = num20;
			}
			if (num21 > num19)
			{
				num19 = num21;
			}
		}
		selX = num5;
		selY = num6;
		selW = num18 - num5 + 1;
		selH = num19 - num6 + 1;
		selTiles = new int[selW * selH];
		selSprites = new int[selW * selH];
		for (int m = 0; m < selH; m++)
		{
			for (int n = 0; n < selW; n++)
			{
				int num22 = (selY + m) * mapWidth + (selX + n);
				if (selectionSet.Contains(num22))
				{
					selTiles[m * selW + n] = (tilesLayerActive ? tiles[num22] : (-1));
					selSprites[m * selW + n] = (spritesLayerActive ? sprites[num22] : (-1));
				}
				else
				{
					selTiles[m * selW + n] = -1;
					selSprites[m * selW + n] = -1;
				}
			}
		}
		if (selectionSet.Count == 0)
		{
			ClearSelection();
			return;
		}
		UpdateSelectionVisuals(selX, selY, selW, selH);
		if (StatusText != null)
		{
			StatusText.Text = $"Selected area {selW}x{selH} at {selX},{selY} (items={selectionSet.Count})";
		}
	}

	private void ClearSelection()
	{
		selTiles = null;
		selSprites = null;
		selW = 0;
		selH = 0;
		selX = (selY = -1);
		selectionSet.Clear();
		spriteAnchors.Clear();
		if (SelectionOverlay != null)
		{
			SelectionOverlay.Children.Clear();
		}
		if (StatusText != null)
		{
			StatusText.Text = string.Empty;
		}
		try
		{
			if (FindName("ManipulateButton") is System.Windows.Controls.Button button)
			{
				button.IsEnabled = false;
			}
		}
		catch
		{
		}
	}

	private void PopulateSelectionArraysFromSet()
	{
		try
		{
			if (selW <= 0 || selH <= 0 || selX < 0 || selY < 0)
			{
				return;
			}
			selTiles = new int[selW * selH];
			selSprites = new int[selW * selH];
			for (int i = 0; i < selH; i++)
			{
				for (int j = 0; j < selW; j++)
				{
					int num = (selY + i) * mapWidth + (selX + j);
					selTiles[i * selW + j] = (tilesLayerActive ? tiles[num] : (-1));
					selSprites[i * selW + j] = (spritesLayerActive ? sprites[num] : (-1));
				}
			}
		}
		catch
		{
		}
	}

	private void MoveSelectionTo(int destX, int destY, int pixelOffsetX = 0, int pixelOffsetY = 0, bool preserveOffsets = false)
	{
		if (selTiles == null || selW <= 0 || selH <= 0)
		{
			return;
		}
		if (destX < 0)
		{
			destX = 0;
		}
		if (destY < 0)
		{
			destY = 0;
		}
		if (destX + selW > mapWidth)
		{
			destX = mapWidth - selW;
		}
		if (destY + selH > mapHeight)
		{
			destY = mapHeight - selH;
		}
		int[] array = (int[])tiles.Clone();
		int[] array2 = (int[])sprites.Clone();
		TileChangeAction tileChangeAction = new TileChangeAction();
		SpriteChangeAction spriteChangeAction = new SpriteChangeAction();
		bool flag = selectionSet != null && selectionSet.Count > 0;
		List<(int, int, int)> list = new List<(int, int, int)>();
		List<(int, int, int)> list2 = new List<(int, int, int)>();
		Dictionary<int, (int, int)> dictionary = new Dictionary<int, (int, int)>();
		HashSet<int> hashSet = new HashSet<int>();
		for (int i = 0; i < selH; i++)
		{
			for (int j = 0; j < selW; j++)
			{
				int num = (selY + i) * mapWidth + (selX + j);
				if (num < 0 || num >= tiles.Length || (flag && (selectionSet == null || !selectionSet.Contains(num))))
				{
					continue;
				}
				if (tilesLayerActive)
				{
					int num2 = selTiles[i * selW + j];
					if (num2 != -1)
					{
						int num3 = (destY + i) * mapWidth + (destX + j);
						if (num3 >= 0 && num3 < array.Length)
						{
							list.Add((num, num3, num2));
						}
					}
				}
				if (selSprites == null || !spritesLayerActive)
				{
					continue;
				}
				int num4 = selSprites[i * selW + j];
				if (num4 == -1)
				{
					continue;
				}
				int num5 = (destY + i) * mapWidth + (destX + j);
				if (num5 < 0 || num5 >= array2.Length)
				{
					continue;
				}
				if (preserveOffsets)
				{
					int num6 = selX + j;
					int num7 = selY + i;
					int num8 = destX + j;
					int num9 = destY + i;
					int num14;
					int num15;
					if (spriteAnchors.TryGetValue(num, out (int, int) value))
					{
						int num10 = num8 - value.Item1;
						int num11 = num9 - value.Item2;
						int num12 = num10 * 16 + pixelOffsetX;
						int num13 = num11 * 16 + pixelOffsetY;
						num14 = value.Item1 * 16 + num12;
						num15 = value.Item2 * 16 + num13;
					}
					else
					{
						num14 = num8 * 16 + pixelOffsetX;
						num15 = num9 * 16 + pixelOffsetY;
					}
					int num16 = num6 * 16;
					int num17 = num7 * 16;
					int item = num14 - num16;
					int item2 = num15 - num17;
					dictionary[num] = (item, item2);
					hashSet.Add(num);
					list2.Add((num, num, num4));
					spriteChangeAction.Add(num, sprites[num], sprites[num]);
				}
				else
				{
					list2.Add((num, num5, num4));
				}
			}
		}
		HashSet<int> hashSet2 = new HashSet<int>();
		foreach (var item7 in list)
		{
			hashSet2.Add(item7.Item2);
		}
		HashSet<int> hashSet3 = new HashSet<int>();
		foreach (var item8 in list2)
		{
			hashSet3.Add(item8.Item2);
		}
		foreach (var item9 in list)
		{
			array[item9.Item2] = item9.Item3;
		}
		foreach (var item10 in list)
		{
			if (item10.Item2 != item10.Item1 && !hashSet2.Contains(item10.Item1))
			{
				array[item10.Item1] = -1;
			}
		}
		foreach (var item11 in list2)
		{
			array2[item11.Item2] = item11.Item3;
		}
		foreach (var item12 in list2)
		{
			if (item12.Item2 != item12.Item1 && !hashSet3.Contains(item12.Item1))
			{
				array2[item12.Item1] = -1;
			}
		}
		List<int> list3 = new List<int>();
		List<int> list4 = new List<int>();
		for (int k = 0; k < array.Length; k++)
		{
			if (array[k] != tiles[k])
			{
				if (array[k] == -1)
				{
					list3.Add(k);
				}
				else
				{
					list4.Add(k);
				}
				tileChangeAction.Add(k, tiles[k], array[k]);
			}
		}
		List<int> list5 = new List<int>();
		List<int> list6 = new List<int>();
		for (int l = 0; l < array2.Length; l++)
		{
			if (array2[l] != sprites[l])
			{
				if (array2[l] == -1)
				{
					list5.Add(l);
				}
				else
				{
					list6.Add(l);
				}
				spriteChangeAction.Add(l, sprites[l], array2[l]);
			}
		}
		if (!spriteChangeAction.IsEmpty())
		{
			spriteChangeAction.CaptureOffsets(this);
		}
		foreach (int item13 in list3)
		{
			tiles[item13] = -1;
		}
		foreach (int item14 in list5)
		{
			int num18 = sprites[item14];
			if (num18 != -1)
			{
				int animatedSpriteIndex = GetAnimatedSpriteIndex(num18);
				int num19 = item14 % mapWidth;
				int num20 = item14 / mapWidth;
				int val = num19 - 1;
				int val2 = num19 + 1;
				int val3 = num20 - 2;
				int val4 = num20 + 2;
				if (animatedSpriteIndex >= 3000 && animatedSpriteIndex <= 3029)
				{
					if (animatedSpriteIndex >= 3011 && animatedSpriteIndex <= 3014)
					{
						val = num19;
						val2 = num19 + 2;
						val3 = num20;
						val4 = num20 + 1;
					}
					else
					{
						val = num19;
						val2 = num19 + 1;
						val3 = num20;
						val4 = num20 + 2;
					}
				}
				val = Math.Max(0, val);
				val3 = Math.Max(0, val3);
				val2 = Math.Min(mapWidth - 1, val2);
				val4 = Math.Min(mapHeight - 1, val4);
				ClearPortalsBitmapTileRect(val, val3, val2, val4, (ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				ClearSpritesBitmapTileRect(val, val3, val2, val4, (ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
			}
			sprites[item14] = -1;
			if (spritePixelOffsets.ContainsKey(item14))
			{
				spritePixelOffsets.Remove(item14);
			}
		}
		try
		{
			RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
		}
		try
		{
			RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
			Redraw();
		}
		foreach (int item15 in list4)
		{
			tiles[item15] = array[item15];
		}
		foreach (int item16 in list6)
		{
			sprites[item16] = array2[item16];
		}
		if (preserveOffsets && selSprites != null && spritesLayerActive)
		{
			for (int m = 0; m < selH; m++)
			{
				for (int n = 0; n < selW; n++)
				{
					int num21 = (selY + m) * mapWidth + (selX + n);
					if (num21 < 0 || num21 >= sprites.Length || (flag && (selectionSet == null || !selectionSet.Contains(num21))))
					{
						continue;
					}
					int num22 = selSprites[m * selW + n];
					if (num22 == -1)
					{
						continue;
					}
					int num23 = (destY + m) * mapWidth + (destX + n);
					if (num23 < 0 || num23 >= sprites.Length)
					{
						continue;
					}
					(int, int) value3;
					if (dictionary.TryGetValue(num21, out var value2))
					{
						spritePixelOffsets[num21] = (value2.Item1, value2.Item2);
					}
					else if (spriteAnchors.TryGetValue(num21, out value3))
					{
						int num24 = destX + n;
						int num25 = destY + m;
						int num26 = num24 - value3.Item1;
						int num27 = num25 - value3.Item2;
						int num28 = num26 * 16 + pixelOffsetX;
						int num29 = num27 * 16 + pixelOffsetY;
						int num30 = value3.Item1 * 16 + num28;
						int num31 = value3.Item2 * 16 + num29;
						int num32 = num24 * 16;
						int num33 = num25 * 16;
						int item3 = num30 - num32;
						int item4 = num31 - num33;
						spritePixelOffsets[num23] = (item3, item4);
						if (num21 != num23)
						{
							spriteAnchors.Remove(num21);
							Debug.WriteLine($"Transferring anchor from idx {num21} to {num23}, keeping coords ({value3.Item1}, {value3.Item2})");
							spriteAnchors[num23] = value3;
						}
					}
					else if (pixelOffsetX != 0 || pixelOffsetY != 0)
					{
						spritePixelOffsets[num23] = (pixelOffsetX, pixelOffsetY);
					}
					else if (spritePixelOffsets.ContainsKey(num23))
					{
						spritePixelOffsets.Remove(num23);
					}
				}
			}
		}
		else if (!preserveOffsets && selSprites != null && spritesLayerActive)
		{
			for (int num34 = 0; num34 < selH; num34++)
			{
				for (int num35 = 0; num35 < selW; num35++)
				{
					int num36 = (destY + num34) * mapWidth + (destX + num35);
					if (num36 >= 0 && num36 < sprites.Length && spritePixelOffsets.ContainsKey(num36))
					{
						spritePixelOffsets.Remove(num36);
					}
				}
			}
		}
		if (!spriteChangeAction.IsEmpty())
		{
			spriteChangeAction.CaptureNewOffsets(this);
		}
		if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(tileChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		if (!spriteChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(spriteChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		try
		{
			RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
		}
		try
		{
			RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
			Redraw();
		}
		HashSet<int> hashSet4 = new HashSet<int>();
		for (int num37 = 0; num37 < selH; num37++)
		{
			for (int num38 = 0; num38 < selW; num38++)
			{
				int item5 = (selY + num37) * mapWidth + (selX + num38);
				int item6 = (destY + num37) * mapWidth + (destX + num38);
				if (selectionSet != null && selectionSet.Contains(item5))
				{
					hashSet4.Add(item6);
				}
			}
		}
		selectionSet = hashSet4;
		selX = destX;
		selY = destY;
		UpdateSelectionVisuals(selX, selY, selW, selH);
		if (StatusText != null)
		{
			StatusText.Text = $"Moved selection to {destX},{destY}";
		}
	}

	private void EraseSelection()
	{
		if (selTiles == null || selW <= 0 || selH <= 0)
		{
			return;
		}
		TileChangeAction tileChangeAction = new TileChangeAction();
		for (int i = 0; i < selH; i++)
		{
			for (int j = 0; j < selW; j++)
			{
				int num = (selY + i) * mapWidth + (selX + j);
				int num2 = tiles[num];
				if (num2 != -1)
				{
					tileChangeAction.Add(num, num2, -1);
				}
				tiles[num] = -1;
			}
		}
		if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(tileChangeAction);
			redoStack.Clear();
		}
		try
		{
			RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
			Redraw();
		}
		ClearSelection();
		if (StatusText != null)
		{
			StatusText.Text = "Erased selection";
		}
	}

	private void EraseSelectedLayers()
	{
		if (selW <= 0 || selH <= 0)
		{
			return;
		}
		bool flag = tilesLayerActive || (!tilesLayerActive && !spritesLayerActive);
		bool flag2 = spritesLayerActive || (!tilesLayerActive && !spritesLayerActive);
		if (selectionSet != null && selectionSet.Count > 0)
		{
			TileChangeAction tileChangeAction = new TileChangeAction();
			SpriteChangeAction spriteChangeAction = new SpriteChangeAction();
			foreach (int item in selectionSet)
			{
				if (item < 0 || item >= tiles.Length)
				{
					continue;
				}
				if (flag)
				{
					int num = tiles[item];
					if (num != -1)
					{
						tileChangeAction.Add(item, num, -1);
					}
					tiles[item] = -1;
				}
				if (flag2)
				{
					int num2 = sprites[item];
					if (num2 != -1)
					{
						spriteChangeAction.Add(item, num2, -1);
					}
					sprites[item] = -1;
				}
			}
			if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
			{
				undoStack.Push(tileChangeAction);
				redoStack.Clear();
				SetHasUnsavedChanges(unsaved: true);
			}
			if (!spriteChangeAction.IsEmpty() && !suppressUndoRecording)
			{
				undoStack.Push(spriteChangeAction);
				redoStack.Clear();
				SetHasUnsavedChanges(unsaved: true);
			}
			try
			{
				if (!tileChangeAction.IsEmpty())
				{
					RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
			}
			catch
			{
			}
			try
			{
				if (!spriteChangeAction.IsEmpty())
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
			}
			catch
			{
				Redraw();
			}
		}
		else
		{
			TileChangeAction tileChangeAction2 = new TileChangeAction();
			if (flag)
			{
				for (int i = 0; i < selH; i++)
				{
					for (int j = 0; j < selW; j++)
					{
						int num3 = (selY + i) * mapWidth + (selX + j);
						int num4 = tiles[num3];
						if (num4 != -1)
						{
							tileChangeAction2.Add(num3, num4, -1);
						}
						tiles[num3] = -1;
					}
				}
				if (!tileChangeAction2.IsEmpty() && !suppressUndoRecording)
				{
					undoStack.Push(tileChangeAction2);
					redoStack.Clear();
					SetHasUnsavedChanges(unsaved: true);
				}
				try
				{
					RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
					Redraw();
				}
			}
			SpriteChangeAction spriteChangeAction2 = new SpriteChangeAction();
			if (flag2)
			{
				for (int k = 0; k < selH; k++)
				{
					for (int l = 0; l < selW; l++)
					{
						int num5 = (selY + k) * mapWidth + (selX + l);
						int num6 = sprites[num5];
						if (num6 != -1)
						{
							spriteChangeAction2.Add(num5, num6, -1);
						}
						sprites[num5] = -1;
					}
				}
				if (!spriteChangeAction2.IsEmpty() && !suppressUndoRecording)
				{
					undoStack.Push(spriteChangeAction2);
					redoStack.Clear();
					SetHasUnsavedChanges(unsaved: true);
				}
				try
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
					Redraw();
				}
			}
		}
		ClearSelection();
		if (StatusText != null)
		{
			StatusText.Text = "Erased selection on active layers";
		}
	}

	public void ApplyMapOffset(int dx, int dy)
	{
		try
		{
			int num = mapWidth;
			int num2 = mapHeight;
			int[] array = Enumerable.Repeat(-1, num * num2).ToArray();
			int[] array2 = Enumerable.Repeat(-1, num * num2).ToArray();
			Dictionary<int, (int, int)> dictionary = new Dictionary<int, (int, int)>();
			Dictionary<int, (int, int)> dictionary2 = new Dictionary<int, (int, int)>();
			for (int i = 0; i < num2; i++)
			{
				for (int j = 0; j < num; j++)
				{
					int num3 = i * num + j;
					int num4 = j + dx;
					int num5 = i + dy;
					if (num4 >= 0 && num4 < num && num5 >= 0 && num5 < num2)
					{
						int num6 = num5 * num + num4;
						array[num6] = tiles[num3];
						array2[num6] = sprites[num3];
						if (spritePixelOffsets.TryGetValue(num3, out (int, int) value))
						{
							dictionary[num6] = value;
						}
						if (spriteAnchors.TryGetValue(num3, out (int, int) value2))
						{
							dictionary2[num6] = value2;
						}
					}
				}
			}
			TileChangeAction tileChangeAction = new TileChangeAction();
			SpriteChangeAction spriteChangeAction = new SpriteChangeAction();
			for (int k = 0; k < num * num2; k++)
			{
				if (array[k] != tiles[k])
				{
					tileChangeAction.Add(k, tiles[k], array[k]);
				}
				if (array2[k] != sprites[k])
				{
					spriteChangeAction.Add(k, sprites[k], array2[k]);
				}
			}
			if (!tileChangeAction.IsEmpty() || !spriteChangeAction.IsEmpty())
			{
				bool flag = suppressUndoRecording;
				try
				{
					tiles = array;
					sprites = array2;
					spritePixelOffsets = dictionary;
					spriteAnchors = dictionary2;
					if (!tileChangeAction.IsEmpty())
					{
						undoStack.Push(tileChangeAction);
						redoStack.Clear();
						SetHasUnsavedChanges(unsaved: true);
					}
					if (!spriteChangeAction.IsEmpty())
					{
						undoStack.Push(spriteChangeAction);
						redoStack.Clear();
						SetHasUnsavedChanges(unsaved: true);
					}
					try
					{
						RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					}
					catch
					{
					}
					try
					{
						RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					}
					catch
					{
						Redraw();
					}
				}
				finally
				{
					suppressUndoRecording = flag;
				}
			}
			try
			{
				ClearSelection();
			}
			catch
			{
			}
			if (StatusText != null)
			{
				StatusText.Text = $"Offset map by ({dx},{dy})";
			}
		}
		catch
		{
		}
	}

	private void CopySelection()
	{
		if (selW <= 0 || selH <= 0)
		{
			return;
		}
		clipboardW = selW;
		clipboardH = selH;
		clipboardTiles = new int[clipboardW * clipboardH];
		clipboardSprites = new int[clipboardW * clipboardH];
		clipboardMask = new bool[clipboardW * clipboardH];
		clipboardHasData = false;
		for (int i = 0; i < selH; i++)
		{
			for (int j = 0; j < selW; j++)
			{
				int item = (selY + i) * mapWidth + (selX + j);
				int num = ((selTiles != null) ? selTiles[i * selW + j] : (-1));
				int num2 = ((selSprites != null) ? selSprites[i * selW + j] : (-1));
				clipboardTiles[i * clipboardW + j] = num;
				clipboardSprites[i * clipboardW + j] = num2;
				bool flag = (selectionSet != null && selectionSet.Contains(item)) || num != -1 || num2 != -1;
				clipboardMask[i * clipboardW + j] = flag;
				if (flag)
				{
					clipboardHasData = true;
				}
			}
		}
		if (StatusText != null)
		{
			StatusText.Text = (clipboardHasData ? "Copied selection" : "Copied (empty)");
		}
	}

	private void CutSelection()
	{
		CopySelection();
		try
		{
			EraseSelectedLayers();
		}
		catch
		{
		}
		if (StatusText != null)
		{
			StatusText.Text = "Cut selection";
		}
	}

	private void PasteClipboardAt(int destX, int destY)
	{
		if (!clipboardHasData || clipboardTiles == null || clipboardSprites == null || clipboardMask == null || destX < 0 || destY < 0)
		{
			return;
		}
		TileChangeAction tileChangeAction = new TileChangeAction();
		SpriteChangeAction spriteChangeAction = new SpriteChangeAction();
		for (int i = 0; i < clipboardH; i++)
		{
			for (int j = 0; j < clipboardW; j++)
			{
				if (!clipboardMask[i * clipboardW + j])
				{
					continue;
				}
				int num = destX + j;
				int num2 = destY + i;
				if (num < 0 || num >= mapWidth || num2 < 0 || num2 >= mapHeight)
				{
					continue;
				}
				int num3 = num2 * mapWidth + num;
				int num4 = clipboardTiles[i * clipboardW + j];
				int num5 = clipboardSprites[i * clipboardW + j];
				if (num4 != -1)
				{
					int num6 = tiles[num3];
					if (num6 != num4)
					{
						tileChangeAction.Add(num3, num6, num4);
					}
					tiles[num3] = num4;
				}
				if (num5 != -1)
				{
					int num7 = sprites[num3];
					if (num7 != num5)
					{
						spriteChangeAction.Add(num3, num7, num5);
					}
					sprites[num3] = num5;
				}
			}
		}
		if (!tileChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(tileChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		if (!spriteChangeAction.IsEmpty() && !suppressUndoRecording)
		{
			undoStack.Push(spriteChangeAction);
			redoStack.Clear();
			SetHasUnsavedChanges(unsaved: true);
		}
		try
		{
			RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
		}
		try
		{
			RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
			Redraw();
		}
		ClearSelection();
		selW = clipboardW;
		selH = clipboardH;
		selX = destX;
		selY = destY;
		selTiles = (int[])clipboardTiles.Clone();
		selSprites = (int[])clipboardSprites.Clone();
		selectionSet.Clear();
		for (int k = 0; k < selH; k++)
		{
			for (int l = 0; l < selW; l++)
			{
				if (clipboardMask[k * clipboardW + l])
				{
					int item = (selY + k) * mapWidth + (selX + l);
					selectionSet.Add(item);
				}
			}
		}
		UpdateSelectionVisuals(selX, selY, selW, selH);
		if (StatusText != null)
		{
			StatusText.Text = "Pasted clipboard";
		}
	}

	private void ContinuePaintingAt(Point pos)
	{
		var (num, num2) = ViewportPointToTile(pos);
		if (num != lastPaintX || num2 != lastPaintY)
		{
			DoPaintAt(num, num2);
		}
	}

	private void StopPainting()
	{
		if (!isPainting)
		{
			return;
		}
		isPainting = false;
		lastPaintX = -1;
		lastPaintY = -1;
		if (CanvasHost != null && CanvasHost.IsMouseCaptured)
		{
			CanvasHost.ReleaseMouseCapture();
		}
		try
		{
			if (!suppressUndoRecording && currentCompositeAction != null && !currentCompositeAction.IsEmpty())
			{
				undoStack.Push(currentCompositeAction);
				redoStack.Clear();
				SetHasUnsavedChanges(unsaved: true);
			}
			if (!suppressUndoRecording && currentCompositeSpriteAction != null && !currentCompositeSpriteAction.IsEmpty())
			{
				undoStack.Push(currentCompositeSpriteAction);
				redoStack.Clear();
				SetHasUnsavedChanges(unsaved: true);
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
		if (x < 0 || x >= mapWidth || y < 0 || y >= mapHeight)
		{
			return;
		}
		if (PlaceTool != null && PlaceTool.IsChecked == true)
		{
			bool flag = false;
			if (tilesLayerActive && selectedTiles.Count > 0)
			{
				for (int i = 0; i < selectionHeight; i++)
				{
					for (int j = 0; j < selectionWidth; j++)
					{
						int num = x + j;
						int num2 = y + i;
						if (num < 0 || num >= mapWidth || num2 < 0 || num2 >= mapHeight)
						{
							continue;
						}
						int num3 = num2 * mapWidth + num;
						int num4 = i * selectionWidth + j;
						if (num4 >= selectedTiles.Count)
						{
							continue;
						}
						int num5 = tiles[num3];
						int num6 = selectedTiles[num4];
						try
						{
							if (FindName("StructureTool") is ToggleButton { IsChecked: var isChecked } && isChecked == true)
							{
								num6 += structureSetOffset * 32;
							}
						}
						catch
						{
						}
						if (num5 == num6)
						{
							continue;
						}
						if (!suppressUndoRecording)
						{
							if (currentCompositeAction == null)
							{
								currentCompositeAction = new TileChangeAction();
							}
							currentCompositeAction.Add(num3, num5, num6);
						}
						tiles[num3] = num6;
						flag = true;
					}
				}
			}
			bool flag2 = false;
			bool flag3 = false;
			if (spritesLayerActive && selectedSprite >= 0)
			{
				int num7 = y * mapWidth + x;
				int num8 = sprites[num7];
				int num9 = selectedSprite;
				if (num8 != num9)
				{
					if (previewMode && IsPortalSprite(num8))
					{
						flag2 = true;
					}
					if (previewMode && IsPortalSprite(num9))
					{
						flag3 = true;
					}
					if (spritePixelOffsets.ContainsKey(num7))
					{
						spritePixelOffsets.Remove(num7);
					}
					if (!suppressUndoRecording)
					{
						if (currentCompositeSpriteAction == null)
						{
							currentCompositeSpriteAction = new SpriteChangeAction();
						}
						currentCompositeSpriteAction.Add(num7, num8, num9);
					}
					sprites[num7] = num9;
					flag = true;
				}
			}
			if (!flag)
			{
				return;
			}
			lastPaintX = x;
			lastPaintY = y;
			try
			{
				if (spritesLayerActive && selectedSprite >= 0)
				{
					double scale = (useFastZoom ? cachedScale : ((ZoomSlider != null) ? ZoomSlider.Value : 1.0));
					UpdateSpriteBitmapAt(x, y, scale, mapViewportPadding);
					if (flag3)
					{
						QueueRebuildPortalsRegion(x - 1, y - 2, x + 1, y + 2);
					}
					if (flag2)
					{
						QueueRebuildPortalsRegion(x - 1, y - 2, x + 1, y + 2);
					}
					if (previewMode)
					{
						QueueRebuildPortalsRegion(x - 2, y - 3, x + 2, y + 2);
					}
				}
				if (tilesLayerActive && selectedTiles.Count > 0)
				{
					double scale2 = (useFastZoom ? cachedScale : ((ZoomSlider != null) ? ZoomSlider.Value : 1.0));
					for (int k = 0; k < selectionHeight; k++)
					{
						for (int l = 0; l < selectionWidth; l++)
						{
							int num10 = x + l;
							int num11 = y + k;
							if (num10 >= 0 && num10 < mapWidth && num11 >= 0 && num11 < mapHeight)
							{
								UpdateTileBitmapAt(num10, num11, scale2, mapViewportPadding);
							}
						}
					}
				}
			}
			catch
			{
				Redraw();
			}
			try
			{
				if (previewMode)
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				return;
			}
			catch
			{
				return;
			}
		}
		if (EraseTool == null || EraseTool.IsChecked != true)
		{
			return;
		}
		bool flag4 = false;
		if (tilesLayerActive)
		{
			for (int m = 0; m < selectionHeight; m++)
			{
				for (int n = 0; n < selectionWidth; n++)
				{
					int num12 = x + n;
					int num13 = y + m;
					if (num12 < 0 || num12 >= mapWidth || num13 < 0 || num13 >= mapHeight)
					{
						continue;
					}
					int num14 = num13 * mapWidth + num12;
					int num15 = tiles[num14];
					if (num15 == -1)
					{
						continue;
					}
					if (!suppressUndoRecording)
					{
						if (currentCompositeAction == null)
						{
							currentCompositeAction = new TileChangeAction();
						}
						currentCompositeAction.Add(num14, num15, -1);
					}
					tiles[num14] = -1;
					flag4 = true;
				}
			}
		}
		int num16 = -1;
		if (spritesLayerActive)
		{
			int num17 = y * mapWidth + x;
			num16 = sprites[num17];
			if (num16 != -1)
			{
				if (!suppressUndoRecording)
				{
					if (currentCompositeSpriteAction == null)
					{
						currentCompositeSpriteAction = new SpriteChangeAction();
					}
					currentCompositeSpriteAction.Add(num17, num16, -1);
				}
				sprites[num17] = -1;
				flag4 = true;
			}
		}
		if (!flag4)
		{
			return;
		}
		lastPaintX = x;
		lastPaintY = y;
		try
		{
			double scale3 = (useFastZoom ? cachedScale : ((ZoomSlider != null) ? ZoomSlider.Value : 1.0));
			if (tilesLayerActive)
			{
				for (int num18 = 0; num18 < selectionHeight; num18++)
				{
					for (int num19 = 0; num19 < selectionWidth; num19++)
					{
						int num20 = x + num19;
						int num21 = y + num18;
						if (num20 >= 0 && num20 < mapWidth && num21 >= 0 && num21 < mapHeight)
						{
							UpdateTileBitmapAt(num20, num21, scale3, mapViewportPadding);
						}
					}
				}
			}
			if (spritesLayerActive)
			{
				UpdateSpriteBitmapAt(x, y, scale3, mapViewportPadding);
				if (IsPortalSprite(num16))
				{
					QueueRebuildPortalsRegion(x - 1, y - 2, x + 1, y + 2);
				}
			}
		}
		catch
		{
			Redraw();
		}
	}

	private void UpdateCoords(Point p)
	{
		double num = (useFastZoom ? cachedScale : ((ZoomSlider != null) ? ZoomSlider.Value : 1.0));
		double num2 = mapViewportPadding;
		bool flag = p.X >= num2 && p.Y >= num2 && p.X < num2 + (double)(mapWidth * 16) * num && p.Y < num2 + (double)(mapHeight * 16) * num;
		int num3 = -1;
		int num4 = -1;
		if (flag)
		{
			(num3, num4) = ViewportPointToTile(p);
		}
		if (num3 == lastHoverX && num4 == lastHoverY && flag == (lastHoverX != -1))
		{
			return;
		}
		lastHoverX = num3;
		lastHoverY = num4;
		if (StatusText != null)
		{
			StatusText.Text = (flag ? $"Coords: {num3}, {num4}" : string.Empty);
		}
		try
		{
			if (HoverRect == null)
			{
				return;
			}
			bool flag2 = FindName("StartPosTool") is ToggleButton { IsChecked: var isChecked } && isChecked == true;
			if (flag && !isDraggingSelection && !flag2)
			{
				DpiScale dpi = VisualTreeHelper.GetDpi(this);
				int num5 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleX));
				int num6 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleY));
				int num7 = (int)Math.Round(num2 * dpi.DpiScaleX);
				int num8 = (int)Math.Round(num2 * dpi.DpiScaleY);
				int num9 = num7 + num3 * num5;
				int num10 = num8 + num4 * num6 + gridRenderShiftYPx;
				int num11 = num6;
				double num12 = (double)num9 / dpi.DpiScaleX;
				double num13 = (double)num10 / dpi.DpiScaleY;
				double num14 = (double)num11 / dpi.DpiScaleY;
				try
				{
					if (HoverRect != null)
					{
						HoverRect.Visibility = Visibility.Collapsed;
					}
				}
				catch
				{
				}
				if (HoverBorder == null)
				{
					return;
				}
				double num15 = 1.0 / dpi.DpiScaleX;
				double length = num12 - num15;
				double length2 = num13 - num15;
				double width = num14 + 2.0 * num15;
				double height = num14 + 2.0 * num15;
				try
				{
					HoverBorder.BorderThickness = new Thickness(num15);
					HoverBorder.Width = width;
					HoverBorder.Height = height;
					Canvas.SetLeft(HoverBorder, length);
					Canvas.SetTop(HoverBorder, length2);
					HoverBorder.Visibility = Visibility.Visible;
				}
				catch
				{
				}
				try
				{
					if (SelectTool != null && SelectTool.IsChecked == true && currentSelectMode == SelectMode.AllSame)
					{
						(int, int) tuple2 = ViewportPointToTile(p);
						int num16 = tuple2.Item2 * mapWidth + tuple2.Item1;
						bool flag3 = tilesLayerActive && tiles[num16] != -1;
						int num17 = (flag3 ? tiles[num16] : ((spritesLayerActive && sprites[num16] != -1) ? sprites[num16] : (-1)));
						try
						{
							if (selectSameHoverTimer == null)
							{
								selectSameHoverTimer = new DispatcherTimer();
								selectSameHoverTimer.Interval = TimeSpan.FromMilliseconds(60.0);
								selectSameHoverTimer.Tick += delegate
								{
									selectSameHoverTimer?.Stop();
									selectSameHoverTimer = null;
									try
									{
										if (pendingHoverTileId != int.MinValue)
										{
											if (pendingHoverUseTiles)
											{
												if (pendingHoverTileId != lastHoveredSelectSameTileId)
												{
													lastHoveredSelectSameTileId = pendingHoverTileId;
													lastHoveredSelectSameSpriteId = int.MinValue;
													UpdateSelectSameOverlay(useTiles: true, pendingHoverTileId);
												}
											}
											else if (pendingHoverTileId != lastHoveredSelectSameSpriteId)
											{
												lastHoveredSelectSameSpriteId = pendingHoverTileId;
												lastHoveredSelectSameTileId = int.MinValue;
												UpdateSelectSameOverlay(useTiles: false, pendingHoverTileId);
											}
										}
									}
									catch
									{
									}
									finally
									{
										pendingHoverTileId = int.MinValue;
									}
								};
							}
							if (flag3 ? (num17 != lastHoveredSelectSameTileId) : (num17 != lastHoveredSelectSameSpriteId))
							{
								pendingHoverTileId = num17;
								pendingHoverUseTiles = flag3;
								selectSameHoverTimer.Stop();
								selectSameHoverTimer.Start();
							}
							return;
						}
						catch
						{
							return;
						}
					}
					ClearSelectSameOverlay();
					return;
				}
				catch
				{
					return;
				}
			}
			HoverRect.Visibility = Visibility.Collapsed;
			if (HoverBorder != null)
			{
				HoverBorder.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
		}
	}

	private (int x, int y) ViewportPointToTile(Point vp)
	{
		if (MapScrollViewer == null)
		{
			return (x: -1, y: -1);
		}
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		double num = (useFastZoom ? cachedScale : ((ZoomSlider != null) ? ZoomSlider.Value : 1.0));
		if (useFastZoom && ZoomSlider != null && cachedScale > 0.0)
		{
			double num2 = ZoomSlider.Value / cachedScale;
			if (Math.Abs(num2 - 1.0) > 1E-06)
			{
				vp.X /= num2;
				vp.Y /= num2;
			}
		}
		int num3 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleX));
		int num4 = Math.Max(1, (int)Math.Ceiling(16.0 * num * dpi.DpiScaleY));
		int num5 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
		int num6 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
		double x = vp.X;
		double y = vp.Y;
		double num7 = Math.Round(x * dpi.DpiScaleX);
		double num8 = Math.Round(y * dpi.DpiScaleY);
		int num9 = gridRenderShiftYPx;
		int num10 = (int)Math.Floor((num7 - (double)num5) / (double)num3 + 1E-09);
		int num11 = (int)Math.Floor((num8 - (double)num6 - (double)num9) / (double)num4 + 1E-09);
		if (num10 < 0)
		{
			num10 = 0;
		}
		if (num10 >= mapWidth)
		{
			num10 = mapWidth - 1;
		}
		if (num11 < 0)
		{
			num11 = 0;
		}
		if (num11 >= mapHeight)
		{
			num11 = mapHeight - 1;
		}
		return (x: num10, y: num11);
	}

	private IntPtr NativeWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == 526)
		{
			try
			{
				int num = wParam.ToInt32();
				short num2 = (short)(num >> 16);
				double num3 = (double)num2 * 0.5;
				if (MapScrollViewer != null)
				{
					if (!swapMouseWheelScroll)
					{
						double offset = (MapScrollViewer?.VerticalOffset ?? 0.0) - num3;
						MapScrollViewer?.ScrollToVerticalOffset(offset);
					}
					else
					{
						double offset2 = (MapScrollViewer?.HorizontalOffset ?? 0.0) - num3;
						MapScrollViewer?.ScrollToHorizontalOffset(offset2);
					}
					handled = true;
				}
			}
			catch
			{
			}
		}
		return IntPtr.Zero;
	}

	private void CanvasHost_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
	{
		try
		{
			if (HoverRect != null)
			{
				HoverRect.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
		}
		try
		{
			if (GhostImage != null)
			{
				GhostImage.Visibility = Visibility.Collapsed;
				GhostImage.Source = null;
			}
		}
		catch
		{
		}
		try
		{
			if (OffsetGhostContainer != null)
			{
				OffsetGhostContainer.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
		}
		try
		{
			if (OffsetTooltipContainer != null)
			{
				OffsetTooltipContainer.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
		}
		try
		{
			if (OffsetGhostTile != null)
			{
				OffsetGhostTile.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
		}
		try
		{
			ClearSelectSameOverlay();
		}
		catch
		{
		}
		try
		{
			if (selectSameHoverTimer != null)
			{
				selectSameHoverTimer.Stop();
				selectSameHoverTimer = null;
				pendingHoverTileId = int.MinValue;
			}
		}
		catch
		{
		}
	}

	private void GridDarknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		gridDarkness = e.NewValue;
		gridDirty = true;
		Redraw();
		try
		{
			SaveSettingsWithTriggerOption();
		}
		catch
		{
		}
	}

	private void MapScrollViewer_ManipulationDelta(object? sender, ManipulationDeltaEventArgs e)
	{
		if (ZoomSlider == null)
		{
			return;
		}
		double x = e.CumulativeManipulation.Scale.X;
		double num = 1.0;
		if (manipulationActive)
		{
			if (lastManipulationCumulativeScale <= 0.0)
			{
				lastManipulationCumulativeScale = 1.0;
			}
			num = x / lastManipulationCumulativeScale;
		}
		else
		{
			num = e.DeltaManipulation.Scale.X;
		}
		lastManipulationCumulativeScale = x;
		try
		{
			if (invertPinchGesture && Math.Abs(num) > 1E-12)
			{
				num = 1.0 / num;
			}
		}
		catch
		{
		}
		bool flag = false;
		Vector translation = e.DeltaManipulation.Translation;
		if (Math.Abs(translation.X) > 0.0)
		{
			double x2 = translation.X;
			double num2 = x2 * 1.0;
			if (!swapMouseWheelScroll)
			{
				double offset = (MapScrollViewer?.VerticalOffset ?? 0.0) - num2;
				MapScrollViewer?.ScrollToVerticalOffset(offset);
			}
			else
			{
				double offset2 = (MapScrollViewer?.HorizontalOffset ?? 0.0) - num2;
				MapScrollViewer?.ScrollToHorizontalOffset(offset2);
			}
			flag = true;
		}
		if (Math.Abs(translation.Y) > 0.0)
		{
			double y = translation.Y;
			double num3 = y * 1.0;
			double offset3 = (MapScrollViewer?.VerticalOffset ?? 0.0) - num3;
			MapScrollViewer?.ScrollToVerticalOffset(offset3);
			flag = true;
		}
		if (Math.Abs(num - 1.0) > 1E-09)
		{
			double num4 = ZoomSlider?.Value ?? 1.0;
			double num5 = num4 * num;
			double num6 = ZoomSlider?.Minimum ?? 1.0;
			double num7 = ZoomSlider?.Maximum ?? 4.0;
			if (num5 < num6)
			{
				num5 = num6;
			}
			if (num5 > num7)
			{
				num5 = num7;
			}
			try
			{
				Debug.WriteLine($"[PINCH] pinchDirectionDetected={pinchDirectionDetected} invertPinchGesture={invertPinchGesture} ratio={num}");
				DpiScale dpi = VisualTreeHelper.GetDpi(this);
				Point position = Mouse.GetPosition(MapScrollViewer);
				double num8 = MapScrollViewer?.HorizontalOffset ?? 0.0;
				double num9 = MapScrollViewer?.VerticalOffset ?? 0.0;
				int num10 = Math.Max(1, (int)Math.Ceiling(16.0 * num4 * dpi.DpiScaleX));
				int num11 = Math.Max(1, (int)Math.Ceiling(16.0 * num4 * dpi.DpiScaleY));
				int num12 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleX);
				int num13 = (int)Math.Round(mapViewportPadding * dpi.DpiScaleY);
				double num14 = (num8 + position.X) * dpi.DpiScaleX;
				double num15 = (num9 + position.Y) * dpi.DpiScaleY;
				double num16 = (num14 - (double)num12) / (double)num10;
				double num17 = (num15 - (double)num13) / (double)num11;
				try
				{
					zoomAnchorViewportX = position.X;
					zoomAnchorViewportY = position.Y;
					zoomAnchorMapX = num16;
					zoomAnchorMapY = num17;
					hasZoomAnchor = true;
				}
				catch
				{
					hasZoomAnchor = false;
				}
				int num18 = Math.Max(1, (int)Math.Ceiling(16.0 * num5 * dpi.DpiScaleX));
				int num19 = Math.Max(1, (int)Math.Ceiling(16.0 * num5 * dpi.DpiScaleY));
				double a = (double)num12 + num16 * (double)num18;
				double a2 = (double)num13 + num17 * (double)num19;
				int num20 = (int)Math.Round(a);
				int num21 = (int)Math.Round(a2);
				try
				{
					string text = $"pinch sc={num:F3} old={num4:F3} new={num5:F3} pxOld=({(int)num14},{(int)num15}) map=({num16:F3},{num17:F3}) pxNew=({num20},{num21})";
					Debug.WriteLine("[PINCH-DETAIL] " + text);
					if (StatusText != null)
					{
						StatusText.Text = text;
					}
					try
					{
						string contents = DateTime.UtcNow.ToString("o") + " " + text + Environment.NewLine;
						string text2 = null;
						try
						{
							string baseDirectory = AppContext.BaseDirectory;
							string text3 = System.IO.Path.Combine(baseDirectory, "pinch-debug.log");
							File.AppendAllText(text3, contents);
							text2 = text3;
						}
						catch (Exception ex)
						{
							Debug.WriteLine("[PINCH-LOG] base dir write failed: " + ex.Message);
							try
							{
								string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
								string text4 = System.IO.Path.Combine(folderPath, "FamidashEditor");
								Directory.CreateDirectory(text4);
								string text5 = System.IO.Path.Combine(text4, "pinch-debug.log");
								File.AppendAllText(text5, contents);
								text2 = text5;
							}
							catch (Exception ex2)
							{
								Debug.WriteLine("[PINCH-LOG] LocalAppData write failed: " + ex2.Message);
								try
								{
									string tempPath = System.IO.Path.GetTempPath();
									string text6 = System.IO.Path.Combine(tempPath, "pinch-debug.log");
									File.AppendAllText(text6, contents);
									text2 = text6;
									goto end_IL_0697;
								}
								catch (Exception ex3)
								{
									Debug.WriteLine("[PINCH-LOG] Temp write failed: " + ex3.Message);
									text2 = null;
									goto end_IL_0697;
								}
								end_IL_0697:;
							}
						}
						if (!string.IsNullOrEmpty(text2))
						{
							try
							{
								if (StatusText != null)
								{
									StatusText.Text = "Wrote pinch log to: " + text2;
								}
							}
							catch
							{
							}
						}
					}
					catch (Exception ex4)
					{
						Debug.WriteLine("[PINCH-LOG] final write failed: " + ex4.Message);
					}
				}
				catch
				{
				}
				try
				{
					zoomAnchorViewportX = position.X;
					zoomAnchorViewportY = position.Y;
					zoomAnchorMapX = num16;
					zoomAnchorMapY = num17;
					hasZoomAnchor = true;
				}
				catch
				{
					hasZoomAnchor = false;
				}
				if (ZoomSlider != null)
				{
					ZoomSlider.Value = num5;
				}
				double num22 = (double)num20 / dpi.DpiScaleX;
				double num23 = (double)num21 / dpi.DpiScaleY;
				double val = num22 - position.X;
				double val2 = num23 - position.Y;
				double val3 = Math.Max(0.0, (CanvasHost?.ActualWidth ?? 0.0) - SafeViewportWidth());
				double val4 = Math.Max(0.0, (CanvasHost?.ActualHeight ?? 0.0) - SafeViewportHeight());
				val = Math.Max(0.0, Math.Min(val3, val));
				val2 = Math.Max(0.0, Math.Min(val4, val2));
				MapScrollViewer?.ScrollToHorizontalOffset(val);
				MapScrollViewer?.ScrollToVerticalOffset(val2);
				try
				{
					deferZoomRebuild = true;
					UpdateQuickZoomTransform();
				}
				catch
				{
				}
				try
				{
					zoomCommitTimer?.Stop();
					zoomCommitTimer?.Start();
				}
				catch
				{
				}
				try
				{
					SnapScrollOffsetsToDevicePixels();
				}
				catch
				{
				}
				try
				{
					UpdateParallaxTransform();
				}
				catch
				{
				}
				try
				{
					UpdateCoords(Mouse.GetPosition(CanvasHost));
				}
				catch
				{
				}
				try
				{
					int num24 = (int)Math.Floor((double)(num20 - num12) / (double)num18 + 1E-09);
					int num25 = (int)Math.Floor((double)(num21 - num13) / (double)num19 + 1E-09);
					if (num24 < 0)
					{
						num24 = 0;
					}
					if (num24 >= mapWidth)
					{
						num24 = mapWidth - 1;
					}
					if (num25 < 0)
					{
						num25 = 0;
					}
					if (num25 >= mapHeight)
					{
						num25 = mapHeight - 1;
					}
					lastHoverX = num24;
					lastHoverY = num25;
					if (HoverBorder != null)
					{
						int num26 = num12 + num24 * num18;
						int num27 = num13 + num25 * num19 + gridRenderShiftYPx;
						int num28 = num19;
						double num29 = (double)num26 / dpi.DpiScaleX;
						double num30 = (double)num27 / dpi.DpiScaleY;
						double num31 = (double)num28 / dpi.DpiScaleY;
						double num32 = 1.0 / dpi.DpiScaleX;
						double length = num29 - num32;
						double length2 = num30 - num32;
						double width = num31 + 2.0 * num32;
						double height = num31 + 2.0 * num32;
						try
						{
							HoverBorder.BorderThickness = new Thickness(num32);
							HoverBorder.Width = width;
							HoverBorder.Height = height;
							Canvas.SetLeft(HoverBorder, length);
							Canvas.SetTop(HoverBorder, length2);
							HoverBorder.Visibility = Visibility.Visible;
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
			catch
			{
				if (ZoomSlider != null)
				{
					ZoomSlider.Value = num5;
				}
				try
				{
					deferZoomRebuild = true;
					UpdateQuickZoomTransform();
				}
				catch
				{
				}
				try
				{
					zoomCommitTimer?.Stop();
					zoomCommitTimer?.Start();
				}
				catch
				{
				}
			}
			flag = true;
		}
		if (flag)
		{
			e.Handled = true;
		}
	}

	private void MapScrollViewer_ManipulationStarting(object? sender, ManipulationStartingEventArgs e)
	{
		try
		{
			e.Mode = ManipulationModes.Translate | ManipulationModes.Scale;
			lastManipulationCumulativeScale = 1.0;
			manipulationActive = true;
		}
		catch
		{
		}
	}

	private void MapScrollViewer_ManipulationCompleted(object? sender, ManipulationCompletedEventArgs e)
	{
		try
		{
			manipulationActive = false;
			lastManipulationCumulativeScale = 1.0;
			try
			{
				UpdateCoords(Mouse.GetPosition(CanvasHost));
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void SaveButton_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrEmpty(currentFilePath))
		{
			try
			{
				string text = currentFilePath;
				string text2 = System.IO.Path.GetExtension(text).ToLower();
				if (text2 == ".tmx")
				{
					string value = loadedExportTarget;
					if (string.IsNullOrEmpty(value))
					{
						value = System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(text), ".csv");
					}
					int chunkHeight = (loadedHasEditorSettings ? loadedChunkHeight : mapHeight);
					TmxLevel level = new TmxLevel
					{
						Width = mapWidth,
						Height = mapHeight,
						Tiles = tiles,
						Sprites = sprites,
						TilesetSource = (loadedTilesetSource ?? "../../../GRAPHICS/famidash.bmp"),
						SpritesetSource = (loadedSpritesetSource ?? "../../../GRAPHICS/sprites.png"),
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
					string text3 = TmxHandler.SaveTmx(text, level, useLegacyTriggerOffset);
					if (!suppressCollisionMessages && !string.IsNullOrEmpty(text3))
					{
						System.Windows.MessageBox.Show("Sprite collision adjustments during save:\n\n" + text3, "Sprite Collision Resolution", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					}
					try
					{
						SaveTmxConfig(text);
					}
					catch
					{
					}
				}
				else
				{
					LevelModel value2 = new LevelModel
					{
						Width = mapWidth,
						Height = mapHeight,
						Tiles = tiles
					};
					File.WriteAllText(text, JsonSerializer.Serialize(value2));
				}
				SetHasUnsavedChanges(unsaved: false);
				if (StatusText != null)
				{
					StatusText.Text = "Saved " + text;
				}
				return;
			}
			catch (Exception ex)
			{
				if (StatusText != null)
				{
					StatusText.Text = "Save failed: " + ex.Message;
				}
				return;
			}
		}
		Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
		{
			Filter = "Tiled Map (TMX)|*.tmx|JSON level|*.json|All files|*.*",
			DefaultExt = "tmx"
		};
		if (saveFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			string text4 = System.IO.Path.GetExtension(saveFileDialog.FileName).ToLower();
			if (text4 == ".tmx")
			{
				string value3 = loadedExportTarget;
				if (string.IsNullOrEmpty(value3))
				{
					value3 = System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(saveFileDialog.FileName), ".csv");
				}
				int chunkHeight2 = (loadedHasEditorSettings ? loadedChunkHeight : mapHeight);
				TmxLevel level2 = new TmxLevel
				{
					Width = mapWidth,
					Height = mapHeight,
					Tiles = tiles,
					Sprites = sprites,
					TilesetSource = (loadedTilesetSource ?? "../../../GRAPHICS/famidash.bmp"),
					SpritesetSource = (loadedSpritesetSource ?? "../../../GRAPHICS/sprites.png"),
					HasEditorSettings = true,
					ChunkWidth = loadedChunkWidth,
					ChunkHeight = chunkHeight2,
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
				string text5 = TmxHandler.SaveTmx(saveFileDialog.FileName, level2, useLegacyTriggerOffset);
				if (!suppressCollisionMessages && !string.IsNullOrEmpty(text5))
				{
					System.Windows.MessageBox.Show("Sprite collision adjustments during save:\n\n" + text5, "Sprite Collision Resolution", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				}
				try
				{
					SaveTmxConfig(saveFileDialog.FileName);
				}
				catch
				{
				}
			}
			else
			{
				LevelModel value4 = new LevelModel
				{
					Width = mapWidth,
					Height = mapHeight,
					Tiles = tiles
				};
				File.WriteAllText(saveFileDialog.FileName, JsonSerializer.Serialize(value4));
			}
			currentFilePath = saveFileDialog.FileName;
			SetHasUnsavedChanges(unsaved: false);
			try
			{
				if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
				{
					openFiles[currentFileIndex].FilePath = currentFilePath;
					openFiles[currentFileIndex].HasUnsavedChanges = false;
					try
					{
						openFiles[currentFileIndex].CreatedAsUntitled = false;
					}
					catch
					{
					}
					try
					{
						UpdateTabHeaderForIndex(currentFileIndex);
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
				AddToRecentFiles(saveFileDialog.FileName);
			}
			catch
			{
			}
			if (StatusText != null)
			{
				StatusText.Text = "Saved " + saveFileDialog.FileName;
			}
		}
		catch (Exception ex2)
		{
			if (StatusText != null)
			{
				StatusText.Text = "Save failed: " + ex2.Message;
			}
		}
	}

	private void MainWindow_PreviewKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
	{
		ModifierKeys modifiers = Keyboard.Modifiers;
		if (e.Key == Key.F1)
		{
			try
			{
				SetOptionsButton_Click(this, new RoutedEventArgs());
			}
			catch
			{
			}
			e.Handled = true;
			return;
		}
		if (e.Key == Key.Escape && isConstructingPolygon)
		{
			CancelPolygon();
			e.Handled = true;
			return;
		}
		if ((modifiers & ModifierKeys.Control) != ModifierKeys.None)
		{
			if (e.Key == Key.Z)
			{
				if ((modifiers & ModifierKeys.Shift) != ModifierKeys.None)
				{
					try
					{
						Redo();
					}
					catch
					{
					}
				}
				else
				{
					try
					{
						Undo();
					}
					catch
					{
					}
				}
				e.Handled = true;
				return;
			}
			if (e.Key == Key.Y)
			{
				try
				{
					Redo();
				}
				catch
				{
				}
				e.Handled = true;
				return;
			}
			if (e.Key == Key.C)
			{
				try
				{
					CopySelection();
				}
				catch
				{
				}
				e.Handled = true;
				return;
			}
			if (e.Key == Key.X)
			{
				try
				{
					CutSelection();
				}
				catch
				{
				}
				e.Handled = true;
				return;
			}
			if (e.Key == Key.V)
			{
				int num = ((selW > 0 && selH > 0) ? selX : ((lastClickX >= 0) ? lastClickX : lastHoverX));
				int num2 = ((selW > 0 && selH > 0) ? selY : ((lastClickY >= 0) ? lastClickY : lastHoverY));
				try
				{
					if (num >= 0 && num2 >= 0)
					{
						PasteClipboardAt(num, num2);
					}
				}
				catch
				{
				}
				e.Handled = true;
				return;
			}
			if (e.Key == Key.Y)
			{
				try
				{
					Redo();
				}
				catch
				{
				}
				e.Handled = true;
				return;
			}
			if (e.Key == Key.O)
			{
				try
				{
					LoadButton_Click(this, new RoutedEventArgs());
				}
				catch
				{
				}
				e.Handled = true;
				return;
			}
			if (e.Key == Key.N)
			{
				try
				{
					NewMenuItem_Click(this, new RoutedEventArgs());
				}
				catch
				{
				}
				e.Handled = true;
				return;
			}
		}
		if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.None && (e.Key == Key.Left || e.Key == Key.Right) && MapScrollViewer != null)
		{
			shiftArrowScrollDir = ((e.Key == Key.Right) ? 1 : (-1));
			if (shiftArrowScrollTimer == null)
			{
				shiftArrowScrollTimer = new DispatcherTimer();
				shiftArrowScrollTimer.Interval = TimeSpan.FromMilliseconds(16.0);
				shiftArrowScrollTimer.Tick += delegate
				{
					try
					{
						double num6 = (MapScrollViewer?.HorizontalOffset ?? 0.0) + (double)shiftArrowScrollDir * 8.0;
						if (num6 < 0.0)
						{
							num6 = 0.0;
						}
						double num7 = Math.Max(0.0, (CanvasHost?.ActualWidth ?? 0.0) - SafeViewportWidth());
						if (num6 > num7)
						{
							num6 = num7;
						}
						MapScrollViewer?.ScrollToHorizontalOffset(num6);
					}
					catch
					{
					}
				};
				shiftArrowScrollTimer.Start();
			}
			e.Handled = true;
			return;
		}
		if ((e.Key == Key.P || e.Key == Key.B) && PlaceTool != null)
		{
			PlaceTool.IsChecked = true;
			e.Handled = true;
			return;
		}
		if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift)) == 0 && (e.Key == Key.Left || e.Key == Key.Right) && MapScrollViewer != null)
		{
			double num3 = 32.0;
			double num4 = (MapScrollViewer?.HorizontalOffset ?? 0.0) + ((e.Key == Key.Right) ? num3 : (0.0 - num3));
			if (num4 < 0.0)
			{
				num4 = 0.0;
			}
			double num5 = Math.Max(0.0, (CanvasHost?.ActualWidth ?? 0.0) - SafeViewportWidth());
			if (num4 > num5)
			{
				num4 = num5;
			}
			MapScrollViewer?.ScrollToHorizontalOffset(num4);
			e.Handled = true;
			return;
		}
		if (e.Key == Key.M)
		{
			if (MoveTool != null)
			{
				MoveTool.IsChecked = true;
			}
			e.Handled = true;
			return;
		}
		if (e.Key == Key.E)
		{
			if (EraseTool != null)
			{
				EraseTool.IsChecked = true;
			}
			e.Handled = true;
			return;
		}
		if (e.Key == Key.F)
		{
			if (FillTool != null)
			{
				FillTool.IsChecked = true;
			}
			e.Handled = true;
			return;
		}
		if (e.Key == Key.T && (modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift)) == 0 && FindName("StructureTool") is ToggleButton toggleButton)
		{
			toggleButton.IsChecked = true;
			e.Handled = true;
			return;
		}
		if (e.Key == Key.S)
		{
			if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Control)) == (ModifierKeys.Alt | ModifierKeys.Control))
			{
				try
				{
					MenuFileSaveAs_Click(this, new RoutedEventArgs());
				}
				catch
				{
				}
				e.Handled = true;
				return;
			}
			if ((modifiers & ModifierKeys.Control) != ModifierKeys.None)
			{
				try
				{
					SaveButton_Click(this, new RoutedEventArgs());
				}
				catch
				{
				}
				e.Handled = true;
				return;
			}
			if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Control)) == 0 && SelectTool != null)
			{
				SelectTool.IsChecked = true;
				e.Handled = true;
				return;
			}
		}
		if (e.Key == Key.Delete)
		{
			try
			{
				EraseSelectedLayers();
			}
			catch
			{
			}
			e.Handled = true;
		}
		else if (e.Key == Key.W)
		{
			if (MagicWandTool != null)
			{
				MagicWandTool.IsChecked = true;
			}
			e.Handled = true;
		}
	}

	private void MainWindow_PreviewKeyUp(object? sender, System.Windows.Input.KeyEventArgs e)
	{
		if ((e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.LeftShift || e.Key == Key.RightShift) && shiftArrowScrollTimer != null)
		{
			try
			{
				shiftArrowScrollTimer.Stop();
			}
			catch
			{
			}
			shiftArrowScrollTimer = null;
			shiftArrowScrollDir = 0;
		}
	}

	private void CalculatePathButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (tiles == null || tiles.Length == 0)
			{
				StatusText.Text = "Pathfinder: No tiles loaded.";
				return;
			}
			PathfinderSettingsWindow pathfinderSettingsWindow = new PathfinderSettingsWindow
			{
				Owner = this
			};
			if (pathfinderSettingsWindow.ShowDialog() != true)
			{
				return;
			}
			double jumpTimingBias = pathfinderSettingsWindow.JumpTimingBias;
			bool preferCoins = pathfinderSettingsWindow.PreferCoins;
			bool drawPathLine = pathfinderSettingsWindow.DrawPathLine;
			bool showPathfinderLive = pathfinderSettingsWindow.ShowPathfinderLive;
			bool showProspectivePaths = pathfinderSettingsWindow.ShowProspectivePaths;
			int startX_px = 0;
			int num = ((groundBitmap != null && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
			bool hasGround = groundBitmap != null && groundTileRows > 0;
			int startY_px;
			if (startPosMarkerX.HasValue && startPosMarkerY.HasValue)
			{
				startX_px = startPosMarkerX.Value;
				startY_px = startPosMarkerY.Value;
			}
			else
			{
				int num2 = (57 - mapHeight + num) * 16;
				int num3 = (loadedSpawnYPositionHi ?? 176) & 0xFF;
				int num4 = (loadedScrollYPositionHi ?? 2) & 0xFF;
				int num5 = (loadedScrollYPositionLow ?? 239) & 0xFF;
				int num6 = num4 * 240 + num5;
				startY_px = num3 + num6 - num2;
				int num7 = Math.Max(0, mapHeight * 16 - 16);
				if (startY_px < 0)
				{
					startY_px = 0;
				}
				if (startY_px > num7)
				{
					startY_px = num7;
				}
			}
			int startGameMode = (loadedStartingGameMode.HasValue ? loadedStartingGameMode.Value : 0);
			int startSpeedUiIndex = loadedStartingSpeedUiIndex;
			StatusText.Text = "Pathfinder: Calculating...";
			CalculatePathButton.IsEnabled = false;
			StopPathfinderButton.Visibility = Visibility.Visible;
			PathfinderProgressBar.Value = 0.0;
			PathfinderProgressBar.Visibility = Visibility.Visible;
			PathfinderJumpToButton.Visibility = Visibility.Visible;
			Progress<int> progress = new Progress<int>(delegate(int pct)
			{
				PathfinderProgressBar.Value = pct;
				StatusText.Text = $"Pathfinder: Calculating... {pct}%";
			});
			Task.Run(delegate
			{
				try
				{
					PathfinderEngine engine = new PathfinderEngine(tiles, sprites, spriteAnchors, mapWidth, mapHeight, hasGround, groundTileRows, loadedMaxFallSpeed, spritePixelOffsets);
					engine.LevelName = currentFilePath ?? "";
					engine.JumpTimingBias = jumpTimingBias;
					engine.PreferCoins = preferCoins;
					engine.UseBFS = preferCoins;
					engine.Progress = progress;
					engine.ConfigScrollYHi = loadedScrollYPositionHi;
					engine.ConfigScrollYLo = loadedScrollYPositionLow;
					engine.ConfigSpawnYLo = loadedSpawnYPositionLow;
					_activePathfinderEngine = engine;
					Stopwatch _lastSpecUpdate = Stopwatch.StartNew();
					int _specPathsInWindow = 0;
					int _specDataCount = 0;
					bool _specCancelled = false;
					_speculativePathData.Clear();
					engine.OnSpeculativePath = delegate(List<(int x, int y)>? path, int delay, int survival, bool isHold)
					{
						if (!_specCancelled)
						{
							List<(int x, int y)> pathSnapshot = ((path != null) ? new List<(int, int)>(path) : null);
							if (showProspectivePaths && pathSnapshot != null && pathSnapshot.Count >= 2 && _specDataCount < 500)
							{
								lock (_speculativePathData)
								{
									_speculativePathData.Add(pathSnapshot);
									_specDataCount++;
								}
							}
							if (showPathfinderLive)
							{
								int currentSpeculativeVizMode = engine.CurrentSpeculativeVizMode;
								bool flag = currentSpeculativeVizMode == 1 || currentSpeculativeVizMode == 6;
								if (_lastSpecUpdate.ElapsedMilliseconds >= 16)
								{
									_lastSpecUpdate.Restart();
									_specPathsInWindow = 0;
								}
								int num8 = (flag ? 10 : 3);
								if (path == null || _specPathsInWindow < num8)
								{
									_specPathsInWindow++;
									int survCopy = survival;
									bool isHoldCopy = isHold;
									int modeForUi = currentSpeculativeVizMode;
									base.Dispatcher.BeginInvoke((Action)delegate
									{
										try
										{
											if (!_specCancelled && CanvasHost != null && pathSnapshot != null && pathSnapshot.Count != 0)
											{
												double num9 = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
												double num10 = mapViewportPadding;
												Color color = ((modeForUi == 1) ? (isHoldCopy ? Color.FromArgb(208, 0, 215, 200) : Color.FromArgb(208, byte.MaxValue, 176, 58)) : ((modeForUi == 6) ? (isHoldCopy ? Color.FromArgb(208, 90, 157, byte.MaxValue) : Color.FromArgb(208, 168, 230, 60)) : ((survCopy >= 90) ? Color.FromArgb(192, 0, byte.MaxValue, 0) : ((!isHoldCopy) ? Color.FromArgb(192, byte.MaxValue, 64, 64) : Color.FromArgb(192, byte.MaxValue, 136, 0)))));
												double value = ((modeForUi == 1 || modeForUi == 6) ? 1200.0 : 350.0);
												double num11 = ((modeForUi == 1 || modeForUi == 6) ? 1.0 : 0.8);
												double num12 = Math.Max(1.0, (1.2 + Math.Min(2.0, (double)survCopy / 60.0)) * num9 * num11);
												while (_speculativePolylines.Count > 240)
												{
													UIElement element = _speculativePolylines[0];
													_speculativePolylines.RemoveAt(0);
													try
													{
														CanvasHost.Children.Remove(element);
													}
													catch
													{
													}
												}
												Polyline polyline = new Polyline
												{
													Stroke = new SolidColorBrush(Color.FromArgb((byte)Math.Max(48, color.A / 2), color.R, color.G, color.B)),
													StrokeThickness = num12 * 1.9,
													IsHitTestVisible = false
												};
												Polyline polyline2 = new Polyline
												{
													Stroke = new SolidColorBrush(color),
													StrokeThickness = num12,
													IsHitTestVisible = false
												};
												if (modeForUi == 1 || modeForUi == 6)
												{
													polyline2.StrokeDashArray = (isHoldCopy ? new DoubleCollection(new double[2] { 3.0, 2.0 }) : new DoubleCollection(new double[2] { 1.0, 2.0 }));
												}
												double num13 = 0.0;
												double num14 = 0.0;
												foreach (var item in pathSnapshot)
												{
													double num15 = num10 + (double)item.x * num9;
													double num16 = num10 + (double)(item.y + 48) * num9 + gridRenderShiftY;
													num13 = num15;
													num14 = num16;
													polyline.Points.Add(new Point(num15, num16));
													polyline2.Points.Add(new Point(num15, num16));
												}
												Ellipse ellipse = new Ellipse
												{
													Width = Math.Max(2.0, 3.0 * num9),
													Height = Math.Max(2.0, 3.0 * num9),
													Fill = new SolidColorBrush(color),
													Stroke = new SolidColorBrush(Color.FromArgb(208, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
													StrokeThickness = Math.Max(0.5, 0.8 * num9),
													IsHitTestVisible = false
												};
												Canvas.SetLeft(ellipse, num13 - ellipse.Width / 2.0);
												Canvas.SetTop(ellipse, num14 - ellipse.Height / 2.0);
												System.Windows.Controls.Panel.SetZIndex(polyline, 2098);
												System.Windows.Controls.Panel.SetZIndex(polyline2, 2100);
												System.Windows.Controls.Panel.SetZIndex(ellipse, 2101);
												CanvasHost.Children.Add(polyline);
												CanvasHost.Children.Add(polyline2);
												CanvasHost.Children.Add(ellipse);
												_speculativePolylines.Add(polyline);
												_speculativePolylines.Add(polyline2);
												_speculativePolylines.Add(ellipse);
												DispatcherTimer removeTimer = new DispatcherTimer();
												removeTimer.Interval = TimeSpan.FromMilliseconds(value);
												Polyline glowRef = polyline;
												Polyline polyRef = polyline2;
												Ellipse markerRef = ellipse;
												removeTimer.Tick += delegate
												{
													removeTimer.Stop();
													try
													{
														CanvasHost?.Children.Remove(glowRef);
														CanvasHost?.Children.Remove(polyRef);
														CanvasHost?.Children.Remove(markerRef);
														_speculativePolylines.Remove(glowRef);
														_speculativePolylines.Remove(polyRef);
														_speculativePolylines.Remove(markerRef);
													}
													catch
													{
													}
												};
												removeTimer.Start();
											}
										}
										catch
										{
										}
									});
								}
							}
						}
					};
					engine.Run(startX_px, startY_px, startSpeedUiIndex, startGameMode, startGravFlipped: false, startMini: false);
					_specCancelled = true;
					engine.OnSpeculativePath = null;
					base.Dispatcher.Invoke(DispatcherPriority.Send, (Action)delegate
					{
						try
						{
							string text = ((jumpTimingBias < 0.125) ? "Earliest" : ((jumpTimingBias < 0.375) ? "Early" : ((jumpTimingBias < 0.625) ? "Middle" : ((jumpTimingBias < 0.875) ? "Late" : "Latest"))));
							if (showProspectivePaths && engine.AttemptedPaths != null && engine.AttemptedPaths.Count > 0)
							{
								attemptedPaths.AddRange(engine.AttemptedPaths);
								_attemptedPathsCleared = false;
							}
							if (showProspectivePaths)
							{
								lock (_speculativePathData)
								{
									if (_speculativePathData.Count > 0)
									{
										attemptedPaths.AddRange(_speculativePathData);
										_attemptedPathsCleared = false;
									}
									_speculativePathData.Clear();
								}
							}
							else
							{
								lock (_speculativePathData)
								{
									_speculativePathData.Clear();
								}
							}
							if (engine.Success)
							{
								precomputedPathfinderInputs = engine.Inputs;
								precomputedCollectedCoins = engine.FinalCollectedCoinIndices;
								try
								{
									TryWritePathfinderReplayCsv(engine);
								}
								catch
								{
								}
								try
								{
									TryAutoSavePfDataToReplayDir();
								}
								catch
								{
								}
								if (drawPathLine)
								{
									pathfinderPaths.Add((new List<(int, int)>(engine.PathPoints), GetPathfinderBiasColor(jumpTimingBias)));
									if (engine.Path2Points != null && engine.Path2Points.Count > 0)
									{
										pathfinderPath2s.Add((new List<(int, int)>(engine.Path2Points), Color.FromArgb(224, 135, 206, 235)));
									}
									UpdatePlayerPathOverlay();
								}
								TextBlock statusText = StatusText;
								string[] obj5 = new string[6]
								{
									"Pathfinder (",
									text,
									"): ",
									engine.ResultMessage,
									(engine.CoinsCollected > 0) ? $" [{engine.CoinsCollected} coin(s)]" : "",
									null
								};
								List<List<(int x, int y)>> list = engine.AttemptedPaths;
								obj5[5] = ((list != null && list.Count > 0) ? $" ({engine.AttemptedPaths.Count} backtracks)" : "");
								statusText.Text = string.Concat(obj5);
							}
							else
							{
								precomputedPathfinderInputs = engine.Inputs;
								precomputedCollectedCoins = engine.FinalCollectedCoinIndices;
								try
								{
									TryWritePathfinderReplayCsv(engine);
								}
								catch
								{
								}
								try
								{
									TryAutoSavePfDataToReplayDir();
								}
								catch
								{
								}
								if (drawPathLine)
								{
									pathfinderPaths.Add((new List<(int, int)>(engine.PathPoints), GetPathfinderBiasColor(jumpTimingBias)));
									if (engine.Path2Points != null && engine.Path2Points.Count > 0)
									{
										pathfinderPath2s.Add((new List<(int, int)>(engine.Path2Points), Color.FromArgb(224, 135, 206, 235)));
									}
									UpdatePlayerPathOverlay();
								}
								string[] obj8 = new string[6]
								{
									"Pathfinder (",
									text,
									"): INCOMPLETE — ",
									engine.ResultMessage,
									(engine.CoinsCollected > 0) ? $" [{engine.CoinsCollected} coin(s)]" : "",
									null
								};
								List<List<(int x, int y)>> list2 = engine.AttemptedPaths;
								obj8[5] = ((list2 != null && list2.Count > 0) ? $" ({engine.AttemptedPaths.Count} backtracks)" : "");
								string text2 = string.Concat(obj8);
								StatusText.Text = text2;
								StatusText.Foreground = new SolidColorBrush(Color.FromRgb(byte.MaxValue, 102, 102));
								DispatcherTimer colorTimer = new DispatcherTimer();
								colorTimer.Interval = TimeSpan.FromSeconds(8.0);
								colorTimer.Tick += delegate
								{
									colorTimer.Stop();
									try
									{
										StatusText.Foreground = new SolidColorBrush(Colors.White);
									}
									catch
									{
									}
								};
								colorTimer.Start();
							}
						}
						catch (Exception ex5)
						{
							StatusText.Text = "Pathfinder error: " + ex5.Message;
						}
						finally
						{
							CalculatePathButton.IsEnabled = true;
							StopPathfinderButton.Visibility = Visibility.Collapsed;
							StopPathfinderButton.IsEnabled = true;
							PathfinderProgressBar.Visibility = Visibility.Collapsed;
							PathfinderJumpToButton.Visibility = Visibility.Collapsed;
							RefreshReplayButtonVisibility();
							try
							{
								if (File.Exists(MesenTraceFile))
								{
									RunTraceCompare(silent: true);
								}
							}
							catch
							{
							}
							_activePathfinderEngine = null;
							StopPathfinderFollow();
							foreach (UIElement speculativePolyline in _speculativePolylines)
							{
								try
								{
									CanvasHost.Children.Remove(speculativePolyline);
								}
								catch
								{
								}
							}
							_speculativePolylines.Clear();
						}
					});
				}
				catch (Exception ex2)
				{
					Exception ex3 = ex2;
					Exception ex4 = ex3;
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						StatusText.Text = "Pathfinder error: " + ex4.Message;
						CalculatePathButton.IsEnabled = true;
						StopPathfinderButton.Visibility = Visibility.Collapsed;
						PathfinderProgressBar.Visibility = Visibility.Collapsed;
						PathfinderJumpToButton.Visibility = Visibility.Collapsed;
						StopPathfinderFollow();
						_activePathfinderEngine = null;
					});
				}
			});
		}
		catch (Exception ex)
		{
			StatusText.Text = "Pathfinder error: " + ex.Message;
			CalculatePathButton.IsEnabled = true;
			StopPathfinderButton.Visibility = Visibility.Collapsed;
			try
			{
				PathfinderProgressBar.Visibility = Visibility.Collapsed;
			}
			catch
			{
			}
			try
			{
				StopPathfinderFollow();
			}
			catch
			{
			}
		}
	}

	private void StopPathfinderButton_Click(object sender, RoutedEventArgs e)
	{
		PathfinderEngine activePathfinderEngine = _activePathfinderEngine;
		if (activePathfinderEngine != null)
		{
			activePathfinderEngine.CancelRequested = true;
			StatusText.Text = "Pathfinder: Cancelling...";
			StopPathfinderButton.IsEnabled = false;
		}
	}

	private void PathfinderJumpToButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			_pathfinderFollowing = !_pathfinderFollowing;
			if (_pathfinderFollowing)
			{
				PathfinderJumpToButton.Content = "Following...";
				PathfinderJumpToButton.Background = new SolidColorBrush(Color.FromRgb(51, 119, 170));
				PathfinderJumpToButton.Foreground = new SolidColorBrush(Colors.White);
				if (_pathfinderFollowTimer == null)
				{
					_pathfinderFollowTimer = new DispatcherTimer();
					_pathfinderFollowTimer.Interval = TimeSpan.FromMilliseconds(100.0);
					_pathfinderFollowTimer.Tick += delegate
					{
						ScrollToPathfinderPosition();
					};
				}
				_pathfinderFollowTimer.Start();
				ScrollToPathfinderPosition();
			}
			else
			{
				StopPathfinderFollow();
			}
		}
		catch
		{
		}
	}

	private void ScrollToPathfinderPosition()
	{
		try
		{
			PathfinderEngine activePathfinderEngine = _activePathfinderEngine;
			if (activePathfinderEngine == null || MapScrollViewer == null)
			{
				StopPathfinderFollow();
				return;
			}
			int currentX_px = activePathfinderEngine.CurrentX_px;
			double num = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
			double num2 = mapViewportPadding;
			double num3 = num2 + (double)currentX_px * num;
			double num4 = SafeViewportWidth();
			double num5 = Math.Max(0.0, num3 - num4 / 2.0);
			double num6 = Math.Max(0.0, (CanvasHost?.ActualWidth ?? 0.0) - num4);
			if (num5 > num6)
			{
				num5 = num6;
			}
			MapScrollViewer.ScrollToHorizontalOffset(num5);
		}
		catch
		{
		}
	}

	private void StopPathfinderFollow()
	{
		_pathfinderFollowing = false;
		_pathfinderFollowTimer?.Stop();
		try
		{
			PathfinderJumpToButton.Content = "Follow";
			PathfinderJumpToButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
			PathfinderJumpToButton.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
		}
		catch
		{
		}
	}

	private void TryWritePathfinderReplayCsv(PathfinderEngine engine)
	{
		string text = engine.ExportReplayCsv();
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		try
		{
			File.WriteAllText(ReplayTempFile, text);
		}
		catch
		{
		}
		try
		{
			File.WriteAllText(OverlayLuaPath, BuildOverlayLuaScript(includeReplay: true, drawPathlines: true));
			File.WriteAllText(OverlayLuaNoPathlinesPath, BuildOverlayLuaScript(includeReplay: true, drawPathlines: false));
		}
		catch
		{
		}
	}

	private void PathfinderReplayButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!File.Exists(ReplayTempFile))
			{
				StatusText.Text = "Replay: no pathfinder solution available. Calculate a path first.";
				return;
			}
			StatusText.Text = "Replay: building ROM, then launching Mesen...";
			BuildAndTestForReplayAsync();
		}
		catch (Exception ex)
		{
			StatusText.Text = "Replay error: " + ex.Message;
		}
	}

	private async void SetOptionsButton_Click(object? sender, RoutedEventArgs e)
	{
		try
		{
			int waited = 0;
			while (isSwitchingTab && waited < 2000)
			{
				await Task.Delay(10);
				waited += 10;
			}
			string currentDeco = loadedDecoSet;
			string currentBlock = loadedBlockSet;
			string currentSpike = loadedSpikeSet;
			try
			{
				if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
				{
					FileTabData fd = openFiles[currentFileIndex];
					if (!string.IsNullOrEmpty(fd.LoadedDecoSet))
					{
						currentDeco = fd.LoadedDecoSet;
					}
					if (!string.IsNullOrEmpty(fd.LoadedBlockSet))
					{
						currentBlock = fd.LoadedBlockSet;
					}
					if (!string.IsNullOrEmpty(fd.LoadedSpikeSet))
					{
						currentSpike = fd.LoadedSpikeSet;
					}
				}
			}
			catch
			{
			}
			SetOptionsWindow dlg = new SetOptionsWindow(currentDeco, currentBlock, currentSpike)
			{
				Owner = this
			};
			if (dlg.ShowDialog() != true)
			{
				return;
			}
			string newDeco = dlg.SelectedDeco ?? "DECO1";
			string newBlock = dlg.SelectedBlockSet ?? "BLOCKSA";
			string newSpike = dlg.SelectedSpikeSet ?? "SPIKESA";
			bool changed = false;
			if (newDeco != loadedDecoSet)
			{
				loadedDecoSet = newDeco;
				changed = true;
				if (StatusText != null)
				{
					StatusText.Text = "Deco set saved: " + loadedDecoSet;
				}
				try
				{
					RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				}
				catch
				{
				}
				try
				{
					if (lockSpritesToSet)
					{
						ApplyLockSpritesToSet();
					}
				}
				catch
				{
				}
			}
			if (newBlock != loadedBlockSet)
			{
				loadedBlockSet = newBlock;
				changed = true;
				if (StatusText != null)
				{
					StatusText.Text = "Block set saved: " + loadedBlockSet;
				}
			}
			if (newSpike != loadedSpikeSet)
			{
				loadedSpikeSet = newSpike;
				changed = true;
				if (StatusText != null)
				{
					StatusText.Text = "Spike set saved: " + loadedSpikeSet;
				}
			}
			if (changed)
			{
				try
				{
					if (!string.IsNullOrEmpty(currentFilePath))
					{
						SaveTmxConfig(currentFilePath);
					}
				}
				catch
				{
				}
				try
				{
					Redraw();
				}
				catch
				{
				}
			}
			try
			{
				try
				{
					if (!string.IsNullOrEmpty(currentFilePath))
					{
						SaveTmxConfig(currentFilePath);
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
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Debug.WriteLine("SetOptions dialog failed: " + ex2.Message);
		}
	}

	public void SetNoParallax(bool enabled)
	{
		try
		{
			suppressNoParallaxHandler = true;
			noParallaxBg = enabled;
			ApplyParallaxChoice();
			try
			{
				if (showAccurateTileset)
				{
					SetShowAccurateTileset(enabled: true, loadedBlockSet, loadedSpikeSet);
				}
			}
			catch
			{
			}
			try
			{
				RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				ApplyLockSpritesToSet();
			}
			catch
			{
			}
		}
		finally
		{
			suppressNoParallaxHandler = false;
		}
	}

	public void ApplySpriteOffsets(ObjectOffsetEntry[] offsetEntries)
	{
		try
		{
			spritePixelOffsets.Clear();
			foreach (ObjectOffsetEntry objectOffsetEntry in offsetEntries)
			{
				if (objectOffsetEntry == null)
				{
					continue;
				}
				int valueOrDefault = objectOffsetEntry.offsetX.GetValueOrDefault();
				int valueOrDefault2 = objectOffsetEntry.offsetY.GetValueOrDefault();
				if ((valueOrDefault == 0 && valueOrDefault2 == 0) || objectOffsetEntry.coordinates == null || !(objectOffsetEntry.coordinates is JsonElement jsonElement) || 1 == 0 || jsonElement.ValueKind != JsonValueKind.Array)
				{
					continue;
				}
				List<JsonElement> list = jsonElement.EnumerateArray().ToList();
				if (list.Count <= 0)
				{
					continue;
				}
				JsonElement jsonElement2 = list[0];
				if (jsonElement2.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item in list)
					{
						List<JsonElement> list2 = item.EnumerateArray().ToList();
						if (list2.Count >= 2)
						{
							int @int = list2[0].GetInt32();
							int int2 = list2[1].GetInt32();
							int key = int2 * mapWidth + @int;
							spritePixelOffsets[key] = (valueOrDefault, valueOrDefault2);
						}
					}
				}
				else if (jsonElement2.ValueKind == JsonValueKind.Number && list.Count >= 2)
				{
					int int3 = list[0].GetInt32();
					int int4 = list[1].GetInt32();
					int key2 = int4 * mapWidth + int3;
					spritePixelOffsets[key2] = (valueOrDefault, valueOrDefault2);
				}
			}
			try
			{
				if (!string.IsNullOrEmpty(currentFilePath))
				{
					SaveTmxConfig(currentFilePath);
				}
			}
			catch
			{
			}
			try
			{
				RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
				Redraw();
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show("Error applying sprite offsets: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	public void ShowBackgroundTintPicker()
	{
		try
		{
			BgTintButton_Click(this, new RoutedEventArgs());
		}
		catch
		{
		}
	}

	public void ShowGroundTintPicker()
	{
		try
		{
			GroundTintButton_Click(this, new RoutedEventArgs());
		}
		catch
		{
		}
	}

	public void ShowTileTintPicker()
	{
		try
		{
			TileTintButton_Click(this, new RoutedEventArgs());
		}
		catch
		{
		}
	}

	public int GetSpriteOffsetCount()
	{
		return spritePixelOffsets.Count;
	}

	public Dictionary<int, (int offsetX, int offsetY)> GetSpriteOffsets()
	{
		return new Dictionary<int, (int, int)>(spritePixelOffsets);
	}

	public void RemoveAllSpriteOffsets()
	{
		spritePixelOffsets.Clear();
		spriteAnchors.Clear();
		try
		{
			RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
			Redraw();
		}
		SaveCurrentTmxConfig();
	}

	private void Undo()
	{
		if (undoStack.Count == 0)
		{
			if (StatusText != null)
			{
				StatusText.Text = "Undo: nothing to undo";
			}
			return;
		}
		IUndoAction undoAction = undoStack.Pop();
		try
		{
			suppressUndoRecording = true;
			undoAction.Undo(this);
		}
		finally
		{
			suppressUndoRecording = false;
		}
		redoStack.Push(undoAction);
		try
		{
			double scale = (useFastZoom ? cachedScale : ((ZoomSlider != null) ? ZoomSlider.Value : 1.0));
			RebuildAllTilesBitmap(scale, mapViewportPadding);
			RebuildAllSpritesBitmap(scale, mapViewportPadding);
		}
		catch
		{
			Redraw();
		}
		try
		{
			ClearSelection();
		}
		catch
		{
		}
		try
		{
			SetHasUnsavedChanges(undoStack.Count > 0);
		}
		catch
		{
		}
		if (StatusText != null)
		{
			StatusText.Text = "Undid action";
		}
	}

	private void Redo()
	{
		if (redoStack.Count == 0)
		{
			if (StatusText != null)
			{
				StatusText.Text = "Redo: nothing to redo";
			}
			return;
		}
		IUndoAction undoAction = redoStack.Pop();
		try
		{
			suppressUndoRecording = true;
			undoAction.Redo(this);
		}
		finally
		{
			suppressUndoRecording = false;
		}
		undoStack.Push(undoAction);
		try
		{
			double scale = (useFastZoom ? cachedScale : ((ZoomSlider != null) ? ZoomSlider.Value : 1.0));
			RebuildAllTilesBitmap(scale, mapViewportPadding);
			RebuildAllSpritesBitmap(scale, mapViewportPadding);
		}
		catch
		{
			Redraw();
		}
		try
		{
			ClearSelection();
		}
		catch
		{
		}
		try
		{
			SetHasUnsavedChanges(unsaved: true);
		}
		catch
		{
		}
		if (StatusText != null)
		{
			StatusText.Text = "Redid action";
		}
	}

	private void ResizeButton_Click(object sender, RoutedEventArgs e)
	{
		if (int.TryParse(WidthBox.Text, out var result) && int.TryParse(HeightBox.Text, out var result2) && result > 0 && result2 > 0)
		{
			ResizeMap(result, result2);
		}
		else if (StatusText != null)
		{
			StatusText.Text = "Invalid width/height";
		}
	}

	private void NewMenuItem_Click(object sender, RoutedEventArgs e)
	{
		if (openFiles.Count > 0)
		{
			int num = openFiles.FindIndex((FileTabData f) => string.IsNullOrEmpty(f.FilePath));
			if (num >= 0)
			{
				if (openFiles.Count == 1 && num == 0)
				{
					if (hasUnsavedChanges)
					{
						switch (System.Windows.MessageBox.Show("You have unsaved changes. Do you want to save before creating a new map?", "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
						{
						case MessageBoxResult.Yes:
							SaveButton_Click(this, e);
							if (hasUnsavedChanges)
							{
								return;
							}
							break;
						case MessageBoxResult.Cancel:
							return;
						}
					}
					SwitchToTab(num);
					currentFilePath = "";
					mapWidth = 200;
					mapHeight = 27;
					InitDefaultMap();
					try
					{
						spriteFrameOffsets.Clear();
					}
					catch
					{
					}
					try
					{
						scaledTileCaches.Clear();
					}
					catch
					{
					}
					noParallaxBg = false;
					UpdateParallaxTint();
					UpdateGroundTint();
					UpdateTileTint();
					undoStack.Clear();
					redoStack.Clear();
					SetHasUnsavedChanges(unsaved: false);
					try
					{
						loadedDecoSet = "DECO1";
					}
					catch
					{
					}
					try
					{
						loadedBlockSet = "BLOCKSA";
					}
					catch
					{
					}
					try
					{
						loadedSpikeSet = "SPIKESA";
					}
					catch
					{
					}
					try
					{
						loadedStartingSpeedUiIndex = 1;
					}
					catch
					{
					}
					try
					{
						loadedStartingDifficulty = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingStars = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingBackgroundColor = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingGroundColor = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingGameMode = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingLowerText = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingUpperText = null;
					}
					catch
					{
					}
					SaveCurrentTabState();
					try
					{
						RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					}
					catch
					{
					}
					try
					{
						RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
					}
					catch
					{
					}
					Redraw();
					if (StatusText != null)
					{
						StatusText.Text = "New map created (200x27) - replaced Untitled tab.";
					}
					EnsureNewTabButton();
					return;
				}
				for (int num2 = 0; num2 < FileTabControl.Items.Count; num2++)
				{
					if (!(FileTabControl.Items[num2] is TabItem { Tag: FileTabData tag } tabItem) || openFiles.IndexOf(tag) != num)
					{
						continue;
					}
					try
					{
						isHandlingNewTab = true;
						lastProgrammaticSelectedTab = tabItem;
						FileTabControl.SelectedItem = tabItem;
						try
						{
							base.Dispatcher.BeginInvoke((Action)delegate
							{
								lastProgrammaticSelectedTab = null;
							}, DispatcherPriority.Background);
						}
						catch
						{
						}
					}
					finally
					{
						isHandlingNewTab = false;
					}
					try
					{
						SwitchToTab(num);
					}
					catch
					{
					}
					if (StatusText != null)
					{
						StatusText.Text = "Switched to existing Untitled tab.";
					}
					return;
				}
			}
			InitDefaultMap();
			currentFilePath = null;
			SetHasUnsavedChanges(unsaved: false);
			try
			{
				string baseDirectory = AppContext.BaseDirectory;
				string path = System.IO.Path.Combine(baseDirectory, "editor-settings.json");
				if (File.Exists(path))
				{
					string json = File.ReadAllText(path);
					JsonDocument jsonDocument = JsonDocument.Parse(json);
					if (jsonDocument.RootElement.TryGetProperty("backgroundTint", out var value) && value.GetArrayLength() >= 4)
					{
						backgroundTint = Color.FromArgb((byte)value[0].GetInt32(), (byte)value[1].GetInt32(), (byte)value[2].GetInt32(), (byte)value[3].GetInt32());
					}
					if (jsonDocument.RootElement.TryGetProperty("groundTint", out var value2) && value2.GetArrayLength() >= 4)
					{
						groundTint = Color.FromArgb((byte)value2[0].GetInt32(), (byte)value2[1].GetInt32(), (byte)value2[2].GetInt32(), (byte)value2[3].GetInt32());
					}
					if (jsonDocument.RootElement.TryGetProperty("tileTint", out var value3) && value3.GetArrayLength() >= 4)
					{
						tileTint = Color.FromArgb((byte)value3[0].GetInt32(), (byte)value3[1].GetInt32(), (byte)value3[2].GetInt32(), (byte)value3[3].GetInt32());
					}
				}
			}
			catch
			{
			}
			UpdateParallaxTint();
			UpdateGroundTint();
			UpdateTileTint();
			noParallaxBg = false;
			backgroundDirty = true;
			try
			{
				scaledTileCaches.Clear();
			}
			catch
			{
			}
			undoStack.Clear();
			redoStack.Clear();
			if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
			{
				SaveCurrentTabState();
			}
			CreateNewTab();
			if (StatusText != null)
			{
				StatusText.Text = "New map created (200x27)";
			}
			return;
		}
		if (hasUnsavedChanges)
		{
			switch (System.Windows.MessageBox.Show("You have unsaved changes. Do you want to save before creating a new map?", "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
			{
			case MessageBoxResult.Yes:
				SaveButton_Click(sender, e);
				if (hasUnsavedChanges)
				{
					return;
				}
				break;
			case MessageBoxResult.Cancel:
				return;
			}
		}
		currentFilePath = "";
		mapWidth = 200;
		mapHeight = 27;
		InitDefaultMap();
		try
		{
			spriteFrameOffsets.Clear();
		}
		catch
		{
		}
		try
		{
			string baseDirectory2 = AppContext.BaseDirectory;
			string path2 = System.IO.Path.Combine(baseDirectory2, "editor-settings.json");
			if (File.Exists(path2))
			{
				string json2 = File.ReadAllText(path2);
				JsonDocument jsonDocument2 = JsonDocument.Parse(json2);
				if (jsonDocument2.RootElement.TryGetProperty("backgroundTint", out var value4) && value4.GetArrayLength() >= 4)
				{
					backgroundTint = Color.FromArgb((byte)value4[0].GetInt32(), (byte)value4[1].GetInt32(), (byte)value4[2].GetInt32(), (byte)value4[3].GetInt32());
				}
				if (jsonDocument2.RootElement.TryGetProperty("groundTint", out var value5) && value5.GetArrayLength() >= 4)
				{
					groundTint = Color.FromArgb((byte)value5[0].GetInt32(), (byte)value5[1].GetInt32(), (byte)value5[2].GetInt32(), (byte)value5[3].GetInt32());
				}
				if (jsonDocument2.RootElement.TryGetProperty("tileTint", out var value6) && value6.GetArrayLength() >= 4)
				{
					tileTint = Color.FromArgb((byte)value6[0].GetInt32(), (byte)value6[1].GetInt32(), (byte)value6[2].GetInt32(), (byte)value6[3].GetInt32());
				}
			}
		}
		catch
		{
		}
		UpdateParallaxTint();
		UpdateGroundTint();
		UpdateTileTint();
		noParallaxBg = false;
		backgroundDirty = true;
		try
		{
			scaledTileCaches.Clear();
		}
		catch
		{
		}
		try
		{
			RebuildAllTilesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
		}
		try
		{
			PopulateTilesPanel();
		}
		catch
		{
		}
		try
		{
			PopulateSpritesPanel();
		}
		catch
		{
		}
		try
		{
			if (FamiTrackCombo != null)
			{
				for (int num3 = 0; num3 < FamiTrackCombo.Items.Count; num3++)
				{
					if (FamiTrackCombo.Items[num3] is ComboBoxItem { Content: { } content } && content.ToString()?.Equals("Stereo Madness", StringComparison.OrdinalIgnoreCase) == true)
					{
						FamiTrackCombo.SelectedIndex = num3;
						break;
					}
				}
			}
		}
		catch
		{
		}
		undoStack.Clear();
		redoStack.Clear();
		SetHasUnsavedChanges(unsaved: false);
		if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
		{
			SaveCurrentTabState();
		}
		CreateNewTab();
		if (WidthBox != null)
		{
			WidthBox.Text = mapWidth.ToString();
		}
		if (HeightBox != null)
		{
			HeightBox.Text = mapHeight.ToString();
		}
		if (StatusText != null)
		{
			StatusText.Text = "New map created (200x27)";
		}
		try
		{
			RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
		}
		catch
		{
		}
		Redraw();
		try
		{
			Activate();
			Focus();
		}
		catch
		{
		}
	}

	private void Window_Closing(object? sender, CancelEventArgs e)
	{
		if (!hasUnsavedChanges)
		{
			return;
		}
		switch (System.Windows.MessageBox.Show("You have unsaved changes. Do you want to save before closing?", "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
		{
		case MessageBoxResult.Yes:
			SaveButton_Click(this, new RoutedEventArgs());
			if (hasUnsavedChanges)
			{
				e.Cancel = true;
			}
			break;
		case MessageBoxResult.Cancel:
			e.Cancel = true;
			break;
		}
	}

	private void MenuFileSaveAs_Click(object? sender, RoutedEventArgs e)
	{
		Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
		{
			Filter = "Tiled Map (TMX)|*.tmx|JSON level|*.json|All files|*.*",
			DefaultExt = "tmx"
		};
		if (saveFileDialog.ShowDialog(this) != true)
		{
			return;
		}
		try
		{
			string text = currentFilePath;
			currentFilePath = saveFileDialog.FileName;
			SaveButton_Click(this, e);
			currentFilePath = saveFileDialog.FileName;
			try
			{
				if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
				{
					openFiles[currentFileIndex].FilePath = currentFilePath;
					openFiles[currentFileIndex].HasUnsavedChanges = false;
					try
					{
						openFiles[currentFileIndex].CreatedAsUntitled = false;
					}
					catch
					{
					}
					try
					{
						UpdateTabHeaderForIndex(currentFileIndex);
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
				AddToRecentFiles(currentFilePath);
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			if (StatusText != null)
			{
				StatusText.Text = "Save failed: " + ex.Message;
			}
		}
	}

	private unsafe async void LoadButton_Click(object sender, RoutedEventArgs e)
	{
		if (hasUnsavedChanges)
		{
			switch (System.Windows.MessageBox.Show("You have unsaved changes. Do you want to save before loading?", "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
			{
			case MessageBoxResult.Yes:
				SaveButton_Click(sender, e);
				if (hasUnsavedChanges)
				{
					return;
				}
				break;
			case MessageBoxResult.Cancel:
				return;
			}
		}
		Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog
		{
			Filter = "Tiled Map (TMX)|*.tmx|JSON level|*.json|All files|*.*"
		};
		if (dlg.ShowDialog(this) != true)
		{
			return;
		}
		if (ZoomSlider != null)
		{
			ZoomSlider.Value = 1.0;
		}
		LoadingWindow loadingWindow = null;
		try
		{
			string ext = System.IO.Path.GetExtension(dlg.FileName).ToLower();
			int loadedWidth = 0;
			int loadedHeight = 0;
			int[] loadedTiles = null;
			int[] loadedSprites = null;
			if (ext == ".tmx")
			{
				loadingWindow = new LoadingWindow
				{
					Owner = this
				};
				loadingWindow.SetMessage("Loading TMX file...\nThis may take a while on larger maps.");
				loadingWindow.Show();
				base.Dispatcher.Invoke(delegate
				{
				}, DispatcherPriority.Background);
				TmxLevel tmxLevel = TmxHandler.LoadTmx(dlg.FileName, useLegacyTriggerOffset);
				loadedWidth = tmxLevel.Width;
				loadedHeight = tmxLevel.Height;
				loadedTiles = tmxLevel.Tiles;
				loadedSprites = tmxLevel.Sprites;
				if (!suppressCollisionMessages && !string.IsNullOrEmpty(tmxLevel.LoadCollisionMessages))
				{
					System.Windows.MessageBox.Show(this, "Sprite collision adjustments during load:\n\n" + tmxLevel.LoadCollisionMessages, "Sprite Collision Resolution", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					Activate();
					Focus();
				}
				loadedTilesetSource = tmxLevel.TilesetSource;
				loadedSpritesetSource = tmxLevel.SpritesetSource;
				loadedHasEditorSettings = tmxLevel.HasEditorSettings;
				loadedChunkWidth = tmxLevel.ChunkWidth;
				loadedChunkHeight = tmxLevel.ChunkHeight;
				loadedExportTarget = tmxLevel.ExportTarget;
				loadedExportFormat = tmxLevel.ExportFormat;
				loadedParallaxSource = tmxLevel.ParallaxSource;
				originalParallaxSource = tmxLevel.ParallaxSource;
				loadedParallaxX = tmxLevel.ParallaxX;
				loadedParallaxY = tmxLevel.ParallaxY;
				loadedParallaxRepeatX = tmxLevel.ParallaxRepeatX;
				loadedParallaxRepeatY = tmxLevel.ParallaxRepeatY;
				loadedHasParallaxLayer = tmxLevel.HasParallaxLayer;
				loadedGroundSource = tmxLevel.GroundSource;
				loadedGroundOffsetY = tmxLevel.GroundOffsetY;
				loadedGroundRepeatX = tmxLevel.GroundRepeatX;
				loadedHasGroundLayer = tmxLevel.HasGroundLayer;
				try
				{
					loadedDecoSet = (string.IsNullOrEmpty(tmxLevel.DecoSet) ? "deco1" : tmxLevel.DecoSet);
				}
				catch
				{
					loadedDecoSet = "deco1";
				}
			}
			else
			{
				string json = File.ReadAllText(dlg.FileName);
				LevelModel model = JsonSerializer.Deserialize<LevelModel>(json);
				if (model != null)
				{
					loadedWidth = model.Width;
					loadedHeight = model.Height;
					loadedTiles = model.Tiles;
					try
					{
						JsonDocument doc = JsonDocument.Parse(json);
						if (doc.RootElement.TryGetProperty("songID", out var songIdProp))
						{
							string songId = songIdProp.GetString();
							if (!string.IsNullOrEmpty(songId) && FamiTrackCombo != null)
							{
								if (songId.StartsWith("song_", StringComparison.OrdinalIgnoreCase))
								{
									songId = songId.Substring(5);
								}
								string normalizedSongId = NormalizeSongName(songId);
								bool foundSong = false;
								for (int i = 0; i < FamiTrackCombo.Items.Count; i++)
								{
									object obj2 = FamiTrackCombo.Items[i];
									ComboBoxItem item = obj2 as ComboBoxItem;
									if (item != null && item.Content != null)
									{
										string normalizedItemName = NormalizeSongName(item.Content.ToString() ?? "");
										if (normalizedItemName == normalizedSongId)
										{
											FamiTrackCombo.SelectedIndex = i;
											foundSong = true;
											break;
										}
									}
								}
								if (!foundSong)
								{
									for (int i2 = 0; i2 < FamiTrackCombo.Items.Count; i2++)
									{
										object obj2 = FamiTrackCombo.Items[i2];
										if (obj2 is ComboBoxItem { Content: var content } && content != null && content.ToString()?.Equals("Stereo Madness", StringComparison.OrdinalIgnoreCase) == true)
										{
											FamiTrackCombo.SelectedIndex = i2;
											break;
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
			}
			if (loadedWidth > 0 && loadedHeight > 0 && loadedTiles != null)
			{
				suppressUndoRecording = true;
				mapWidth = loadedWidth;
				mapHeight = loadedHeight;
				tiles = loadedTiles;
				sprites = loadedSprites ?? Enumerable.Repeat(-1, loadedWidth * loadedHeight).ToArray();
				if (WidthBox != null)
				{
					WidthBox.Text = mapWidth.ToString();
				}
				if (HeightBox != null)
				{
					HeightBox.Text = mapHeight.ToString();
				}
				ClearSelection();
				undoStack.Clear();
				redoStack.Clear();
				suppressUndoRecording = false;
				currentFilePath = dlg.FileName;
				SetHasUnsavedChanges(unsaved: false);
				AddToRecentFiles(dlg.FileName);
				bool replaceCurrentTab = false;
				if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
				{
					FileTabData currentTab = openFiles[currentFileIndex];
					if (string.IsNullOrEmpty(currentTab.FilePath) && !currentTab.HasUnsavedChanges)
					{
						replaceCurrentTab = true;
					}
				}
				if (replaceCurrentTab)
				{
					SaveCurrentTabState();
					openFiles[currentFileIndex].FilePath = dlg.FileName;
					try
					{
						openFiles[currentFileIndex].CreatedAsUntitled = false;
					}
					catch
					{
					}
					TextBlock txt = default(TextBlock);
					for (int i3 = 0; i3 < FileTabControl.Items.Count; i3++)
					{
						object obj2 = FileTabControl.Items[i3];
						TabItem tab = obj2 as TabItem;
						int num;
						if (tab != null)
						{
							obj2 = tab.Tag;
							if (obj2 is FileTabData td)
							{
								num = ((openFiles.IndexOf(td) == currentFileIndex) ? 1 : 0);
								goto IL_0ae9;
							}
						}
						num = 0;
						goto IL_0ae9;
						IL_0ae9:
						if (num != 0)
						{
							obj2 = tab.Header;
							int num2;
							if (obj2 is StackPanel panel)
							{
								UIElement uIElement = panel.Children[0];
								txt = uIElement as TextBlock;
								num2 = ((txt != null) ? 1 : 0);
							}
							else
							{
								num2 = 0;
							}
							if (num2 != 0)
							{
								txt.Text = System.IO.Path.GetFileName(dlg.FileName);
							}
							break;
						}
					}
				}
				else
				{
					try
					{
						loadedStartingSpeedUiIndex = 1;
					}
					catch
					{
					}
					try
					{
						loadedStartingBackgroundColor = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingGameMode = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingGroundColor = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingDifficulty = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingStars = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingLowerText = null;
					}
					catch
					{
					}
					try
					{
						loadedStartingUpperText = null;
					}
					catch
					{
					}
					if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
					{
						SaveCurrentTabState();
					}
					CreateNewTab(dlg.FileName);
				}
				if (loadedWidth * loadedHeight > 50000 && loadingWindow != null)
				{
					loadingWindow.SetMessage($"Rendering map ({loadedWidth}x{loadedHeight})...\nPlease wait, this may take a moment.");
					await Task.Delay(100);
					await Dispatcher.Yield(DispatcherPriority.Render);
				}
				if (previewMode)
				{
					try
					{
						if (PreviewModeCheckbox != null)
						{
							PreviewModeCheckbox.IsChecked = false;
						}
						else
						{
							previewMode = false;
							StopPreviewTimer();
							animationFrame = 0;
							try
							{
								base.Title = "Famidash Editor";
							}
							catch
							{
							}
							if (portalsWb != null)
							{
								try
								{
									portalsWb.Lock();
									IntPtr pBackBuffer = portalsWb.BackBuffer;
									if (pBackBuffer != IntPtr.Zero)
									{
										int stride = portalsWb.BackBufferStride;
										int bytesTotal = stride * cachedPixelHeight;
										byte* ptr = (byte*)pBackBuffer.ToPointer();
										for (int i4 = 0; i4 < bytesTotal; i4++)
										{
											ptr[i4] = 0;
										}
									}
									portalsWb.AddDirtyRect(new Int32Rect(0, 0, cachedPixelWidth, cachedPixelHeight));
								}
								finally
								{
									try
									{
										portalsWb.Unlock();
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
				}
				Redraw();
				if (MapScrollViewer != null)
				{
					MapScrollViewer?.UpdateLayout();
					MapScrollViewer?.ScrollToLeftEnd();
					double zoomScale = ((ZoomSlider != null) ? ZoomSlider.Value : 1.0);
					double tilePixelHeight = 16.0 * zoomScale;
					double pad = mapViewportPadding;
					double groundStartY = (double)mapHeight * tilePixelHeight + pad;
					double viewportHeight = SafeViewportHeight();
					double targetOffset = groundStartY + tilePixelHeight * 3.0 - viewportHeight;
					if (targetOffset < 0.0)
					{
						targetOffset = 0.0;
					}
					double maxScroll = MapScrollViewer?.ScrollableHeight ?? 0.0;
					if (targetOffset > maxScroll)
					{
						targetOffset = maxScroll;
					}
					MapScrollViewer?.ScrollToVerticalOffset(targetOffset);
				}
				if (StatusText != null)
				{
					StatusText.Text = $"Loaded {System.IO.Path.GetFileName(dlg.FileName)} ({mapWidth}x{mapHeight})";
				}
				LoadTmxConfig(dlg.FileName);
				try
				{
					if (!string.IsNullOrEmpty(loadedTilesetSource) && File.Exists(loadedTilesetSource))
					{
						LoadTileset(loadedTilesetSource);
						SliceTileset();
						PopulateTilesPanel();
					}
					else if (showAccurateTileset)
					{
						try
						{
							SetShowAccurateTileset(enabled: true, loadedBlockSet, loadedSpikeSet);
						}
						catch
						{
						}
					}
					else
					{
						BitmapImage emb = LoadEmbeddedImage("famidash.bmp");
						if (emb != null)
						{
							tilesetBitmap = emb;
							SliceTileset();
							PopulateTilesPanel();
						}
					}
				}
				catch
				{
				}
				Redraw();
				try
				{
					RebuildAllSpritesBitmap(ZoomSlider?.Value ?? 1.0, mapViewportPadding);
				}
				catch
				{
				}
			}
			else if (StatusText != null)
			{
				StatusText.Text = "Load failed: Invalid or empty map data";
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			if (StatusText != null)
			{
				StatusText.Text = "Load failed: " + ex2.Message;
			}
		}
		finally
		{
			loadingWindow?.Close();
		}
	}

	public void SetDecoSet(string deco)
	{
		try
		{
			if (string.IsNullOrEmpty(deco) || deco == loadedDecoSet)
			{
				return;
			}
			loadedDecoSet = deco;
			try
			{
				if (!string.IsNullOrEmpty(currentFilePath))
				{
					SaveTmxConfig(currentFilePath);
				}
			}
			catch
			{
			}
			try
			{
				RebuildAllSpritesBitmap((ZoomSlider != null) ? ZoomSlider.Value : 1.0, mapViewportPadding);
			}
			catch
			{
			}
			try
			{
				if (lockSpritesToSet)
				{
					ApplyLockSpritesToSet();
				}
			}
			catch
			{
			}
			try
			{
				Redraw();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	public void SetBlockSet(string block)
	{
		try
		{
			if (string.IsNullOrEmpty(block) || block == loadedBlockSet)
			{
				return;
			}
			loadedBlockSet = block;
			try
			{
				if (!string.IsNullOrEmpty(currentFilePath))
				{
					SaveTmxConfig(currentFilePath);
				}
			}
			catch
			{
			}
			try
			{
				if (showAccurateTileset)
				{
					SetShowAccurateTileset(enabled: true, loadedBlockSet, loadedSpikeSet);
				}
			}
			catch
			{
			}
			try
			{
				Redraw();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	public void SetSpikeSet(string spike)
	{
		try
		{
			if (string.IsNullOrEmpty(spike) || spike == loadedSpikeSet)
			{
				return;
			}
			loadedSpikeSet = spike;
			try
			{
				if (!string.IsNullOrEmpty(currentFilePath))
				{
					SaveTmxConfig(currentFilePath);
				}
			}
			catch
			{
			}
			try
			{
				if (showAccurateTileset)
				{
					SetShowAccurateTileset(enabled: true, loadedBlockSet, loadedSpikeSet);
				}
			}
			catch
			{
			}
			try
			{
				Redraw();
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	public string GetCurrentTmxPath()
	{
		return currentFilePath ?? string.Empty;
	}

	public void SaveCurrentTmxConfig()
	{
		try
		{
			if (!string.IsNullOrEmpty(currentFilePath))
			{
				SaveTmxConfig(currentFilePath);
			}
		}
		catch
		{
		}
	}

	public void ReloadCurrentTmxConfig()
	{
		try
		{
			if (!string.IsNullOrEmpty(currentFilePath))
			{
				LoadTmxConfig(currentFilePath);
			}
		}
		catch
		{
		}
	}

	public void SetSongFromMetadata(string songId)
	{
		try
		{
			if (string.IsNullOrEmpty(songId) || FamiTrackCombo == null)
			{
				return;
			}
			if (songId.StartsWith("song_", StringComparison.OrdinalIgnoreCase))
			{
				songId = songId.Substring(5);
			}
			string text = NormalizeSongName(songId);
			bool flag = false;
			for (int i = 0; i < FamiTrackCombo.Items.Count; i++)
			{
				if (!(FamiTrackCombo.Items[i] is ComboBoxItem { Content: not null } comboBoxItem))
				{
					continue;
				}
				string text2 = NormalizeSongName(comboBoxItem.Content.ToString() ?? "");
				if (!(text2 == text))
				{
					continue;
				}
				FamiTrackCombo.SelectedIndex = i;
				try
				{
					FieldInfo field = GetType().GetField("openFiles", BindingFlags.Instance | BindingFlags.NonPublic);
					FieldInfo field2 = GetType().GetField("currentFileIndex", BindingFlags.Instance | BindingFlags.NonPublic);
					if (field != null && field2 != null)
					{
						IList list = field.GetValue(this) as IList;
						object value = field2.GetValue(this);
						if (list != null && value is int num && num >= 0 && num < list.Count)
						{
							object obj = list[num];
							if (obj != null)
							{
								PropertyInfo property = obj.GetType().GetProperty("SelectedSong", BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
								if (property != null && property.CanWrite)
								{
									property.SetValue(obj, comboBoxItem.Content?.ToString());
								}
							}
						}
					}
				}
				catch
				{
				}
				flag = true;
				break;
			}
			if (flag)
			{
				return;
			}
			for (int j = 0; j < FamiTrackCombo.Items.Count; j++)
			{
				if (!(FamiTrackCombo.Items[j] is ComboBoxItem { Content: { } content } comboBoxItem2) || content.ToString()?.Equals("Stereo Madness", StringComparison.OrdinalIgnoreCase) != true)
				{
					continue;
				}
				FamiTrackCombo.SelectedIndex = j;
				try
				{
					FieldInfo field3 = GetType().GetField("openFiles", BindingFlags.Instance | BindingFlags.NonPublic);
					FieldInfo field4 = GetType().GetField("currentFileIndex", BindingFlags.Instance | BindingFlags.NonPublic);
					if (!(field3 != null) || !(field4 != null))
					{
						break;
					}
					IList list2 = field3.GetValue(this) as IList;
					object value2 = field4.GetValue(this);
					if (list2 == null || !(value2 is int num2) || num2 < 0 || num2 >= list2.Count)
					{
						break;
					}
					object obj3 = list2[num2];
					if (obj3 != null)
					{
						PropertyInfo property2 = obj3.GetType().GetProperty("SelectedSong", BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
						if (property2 != null && property2.CanWrite)
						{
							property2.SetValue(obj3, comboBoxItem2.Content?.ToString());
						}
					}
					break;
				}
				catch
				{
					break;
				}
			}
		}
		catch
		{
		}
	}

	private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.W && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
		{
			MenuFileClose_Click(sender, e);
			e.Handled = true;
		}
		if (e.Key == Key.F5)
		{
			try
			{
				OpenSimulatorWindow();
			}
			catch
			{
			}
			e.Handled = true;
		}
		if (e.Key != Key.Escape)
		{
			return;
		}
		try
		{
			if (selectionSet != null)
			{
				selectionSet.Clear();
			}
			selW = 0;
			selH = 0;
			try
			{
				UpdateSelectionVisuals(selX, selY, selW, selH);
			}
			catch
			{
			}
		}
		catch
		{
		}
		e.Handled = true;
	}

	public void PersistLoadedValuesToCurrentTab()
	{
		try
		{
			SaveCurrentTabState();
		}
		catch
		{
		}
		try
		{
			UpdateSpawnScrollOverlay();
		}
		catch
		{
		}
	}

	private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
	{
		if (parent == null)
		{
			return null;
		}
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			if (child is T result)
			{
				return result;
			}
			T val = FindVisualChild<T>(child);
			if (val != null)
			{
				return val;
			}
		}
		return null;
	}

	private static long ComputeArrayChecksum(int[] data)
	{
		long num = -3750763034362895579L;
		for (int i = 0; i < data.Length; i++)
		{
			num ^= data[i];
			num *= 1099511628211L;
		}
		return num;
	}

	private PathfinderSaveData? BuildPathfinderSaveData()
	{
		if (precomputedPathfinderInputs == null || precomputedPathfinderInputs.Count == 0)
		{
			return null;
		}
		return new PathfinderSaveData
		{
			Version = 1,
			Inputs = new List<bool>(precomputedPathfinderInputs),
			CollectedCoins = ((precomputedCollectedCoins != null) ? new List<int>(precomputedCollectedCoins) : null),
			Validation = new PathfinderValidation
			{
				MapWidth = mapWidth,
				MapHeight = mapHeight,
				StartGameMode = loadedStartingGameMode.GetValueOrDefault(),
				StartSpeedUiIndex = loadedStartingSpeedUiIndex,
				MaxFallSpeed = loadedMaxFallSpeed,
				TileChecksum = ComputeArrayChecksum(tiles),
				SpriteChecksum = ComputeArrayChecksum(sprites)
			}
		};
	}

	private void TryAutoSavePfDataToReplayDir()
	{
		try
		{
			PathfinderSaveData pathfinderSaveData = BuildPathfinderSaveData();
			if (pathfinderSaveData == null)
			{
				return;
			}
			string currentReplayDir = CurrentReplayDir;
			if (!string.IsNullOrEmpty(currentReplayDir))
			{
				try
				{
					Directory.CreateDirectory(currentReplayDir);
				}
				catch
				{
				}
				string path = System.IO.Path.Combine(currentReplayDir, "famidash_pathfinder.pfdat");
				JsonSerializerOptions options = new JsonSerializerOptions
				{
					WriteIndented = true
				};
				string contents = JsonSerializer.Serialize(pathfinderSaveData, options);
				File.WriteAllText(path, contents);
			}
		}
		catch
		{
		}
	}

	private void MenuFileSavePF_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (precomputedPathfinderInputs == null || precomputedPathfinderInputs.Count == 0)
			{
				System.Windows.MessageBox.Show("No pathfinder data to save. Run the pathfinder first.", "Save Pathfinder Data", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
			{
				Filter = "Pathfinder Data (*.pfdat)|*.pfdat|All Files (*.*)|*.*",
				DefaultExt = ".pfdat",
				Title = "Save Pathfinder Data"
			};
			if (saveFileDialog.ShowDialog() == true)
			{
				PathfinderSaveData value = BuildPathfinderSaveData();
				JsonSerializerOptions options = new JsonSerializerOptions
				{
					WriteIndented = true
				};
				string contents = JsonSerializer.Serialize(value, options);
				File.WriteAllText(saveFileDialog.FileName, contents);
				System.Windows.MessageBox.Show($"Pathfinder data saved ({precomputedPathfinderInputs.Count} frames).", "Save Pathfinder Data", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show("Failed to save pathfinder data:\n" + ex.Message, "Save Pathfinder Data", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void MenuFileLoadPF_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "Pathfinder Data (*.pfdat)|*.pfdat|All Files (*.*)|*.*",
				Title = "Load Pathfinder Data"
			};
			if (openFileDialog.ShowDialog() != true)
			{
				return;
			}
			string json = File.ReadAllText(openFileDialog.FileName);
			PathfinderSaveData pathfinderSaveData = JsonSerializer.Deserialize<PathfinderSaveData>(json);
			if (pathfinderSaveData == null || pathfinderSaveData.Inputs == null || pathfinderSaveData.Inputs.Count == 0)
			{
				System.Windows.MessageBox.Show("The file contains no pathfinder input data.", "Load Pathfinder Data", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				return;
			}
			PathfinderValidation validation = pathfinderSaveData.Validation;
			List<string> list = new List<string>();
			if (validation != null)
			{
				if (validation.MapWidth != mapWidth)
				{
					list.Add($"Map width: file={validation.MapWidth}, current={mapWidth}");
				}
				if (validation.MapHeight != mapHeight)
				{
					list.Add($"Map height: file={validation.MapHeight}, current={mapHeight}");
				}
				if (validation.StartGameMode != loadedStartingGameMode.GetValueOrDefault())
				{
					list.Add($"Start game mode: file={validation.StartGameMode}, current={loadedStartingGameMode.GetValueOrDefault()}");
				}
				if (validation.StartSpeedUiIndex != loadedStartingSpeedUiIndex)
				{
					list.Add($"Start speed: file={validation.StartSpeedUiIndex}, current={loadedStartingSpeedUiIndex}");
				}
				if (validation.MaxFallSpeed != loadedMaxFallSpeed)
				{
					list.Add($"Max fall speed: file={validation.MaxFallSpeed}, current={loadedMaxFallSpeed}");
				}
				if (validation.TileChecksum != ComputeArrayChecksum(tiles))
				{
					list.Add("Tile data has changed");
				}
				if (validation.SpriteChecksum != ComputeArrayChecksum(sprites))
				{
					list.Add("Sprite data has changed");
				}
			}
			else
			{
				list.Add("No validation data in file (old format)");
			}
			if (list.Count > 0)
			{
				string messageBoxText = "The pathfinder data may not match the current level:\n\n" + string.Join("\n", list) + "\n\nLoad anyway?";
				MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show(messageBoxText, "Pathfinder Data Mismatch", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);
				if (messageBoxResult != MessageBoxResult.Yes)
				{
					return;
				}
			}
			precomputedPathfinderInputs = pathfinderSaveData.Inputs;
			precomputedCollectedCoins = ((pathfinderSaveData.CollectedCoins != null) ? new HashSet<int>(pathfinderSaveData.CollectedCoins) : null);
			System.Windows.MessageBox.Show($"Pathfinder data loaded ({pathfinderSaveData.Inputs.Count} frames).\n" + "Open the simulator to use it.", "Load Pathfinder Data", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		catch (Exception ex)
		{
			System.Windows.MessageBox.Show("Failed to load pathfinder data:\n" + ex.Message, "Load Pathfinder Data", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private int FindValidStartPosY(int worldX, int worldY)
	{
		try
		{
			int num = 16;
			int num2 = 16;
			int num3 = 100;
			for (int i = 0; i < num3; i++)
			{
				bool flag = false;
				for (int j = 0; j < num2; j++)
				{
					for (int k = 0; k < num; k++)
					{
						int num4 = worldX + k;
						int num5 = worldY + j;
						int num6 = num4 / 16;
						int num7 = num5 / 16;
						if (num6 < 0 || num7 < 0 || num6 >= mapWidth || num7 >= mapHeight)
						{
							continue;
						}
						int num8 = num7 * mapWidth + num6;
						if (num8 >= 0 && num8 < tiles.Length)
						{
							int num9 = tiles[num8];
							if (num9 > 0)
							{
								flag = true;
								break;
							}
						}
					}
					if (flag)
					{
						break;
					}
				}
				if (!flag)
				{
					return worldY;
				}
				worldY -= 16;
				if (worldY < 0)
				{
					return 0;
				}
			}
			return worldY;
		}
		catch
		{
			return worldY;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/mainwindow.xaml", UriKind.Relative);
			System.Windows.Application.LoadComponent(this, resourceLocator);
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
			RootGrid = (Grid)target;
			break;
		case 2:
			MesenSplitterCol = (ColumnDefinition)target;
			break;
		case 3:
			MesenPanelCol = (ColumnDefinition)target;
			break;
		case 4:
			TileboardPanel = (Grid)target;
			break;
		case 5:
			TilesLabelBorder = (Border)target;
			TilesLabelBorder.MouseLeftButtonDown += TilesLabel_Click;
			break;
		case 6:
			TilesLabel = (TextBlock)target;
			break;
		case 7:
			TileEyeButton = (ToggleButton)target;
			TileEyeButton.Checked += TileEyeButton_Checked;
			TileEyeButton.Unchecked += TileEyeButton_Unchecked;
			break;
		case 8:
			TileSizeSlider = (Slider)target;
			break;
		case 9:
			TileIdIndicator = (TextBlock)target;
			break;
		case 10:
			TileSelectedPreviewImage = (Image)target;
			break;
		case 11:
			TilesPanel = (System.Windows.Controls.ListBox)target;
			break;
		case 12:
			SpritesLabelBorder = (Border)target;
			SpritesLabelBorder.MouseLeftButtonDown += SpritesLabel_Click;
			break;
		case 13:
			SpritesLabel = (TextBlock)target;
			break;
		case 14:
			SpriteEyeButton = (ToggleButton)target;
			SpriteEyeButton.Checked += SpriteEyeButton_Checked;
			SpriteEyeButton.Unchecked += SpriteEyeButton_Unchecked;
			break;
		case 15:
			SpriteSizeSlider = (Slider)target;
			break;
		case 16:
			SpriteIdIndicator = (TextBlock)target;
			break;
		case 17:
			SpriteSelectedPreviewImage = (Image)target;
			break;
		case 18:
			SpritesPanel = (System.Windows.Controls.ListBox)target;
			break;
		case 19:
			MainEditorPanel = (DockPanel)target;
			break;
		case 20:
			MenuFileNew = (MenuItem)target;
			break;
		case 21:
			MenuFileSave = (MenuItem)target;
			break;
		case 22:
			MenuFileSaveAs = (MenuItem)target;
			break;
		case 23:
			MenuFileLoad = (MenuItem)target;
			break;
		case 24:
			MenuFileRecent = (MenuItem)target;
			break;
		case 25:
			MenuFileSavePF = (MenuItem)target;
			break;
		case 26:
			MenuFileLoadPF = (MenuItem)target;
			break;
		case 27:
			MenuFileClose = (MenuItem)target;
			break;
		case 28:
			MenuFileExit = (MenuItem)target;
			break;
		case 29:
			MenuToolPlace = (MenuItem)target;
			break;
		case 30:
			MenuToolMove = (MenuItem)target;
			break;
		case 31:
			MenuToolErase = (MenuItem)target;
			break;
		case 32:
			MenuToolFill = (MenuItem)target;
			break;
		case 33:
			MenuToolSelect = (MenuItem)target;
			break;
		case 34:
			MenuToolLasso = (MenuItem)target;
			break;
		case 35:
			MenuToolEllipse = (MenuItem)target;
			break;
		case 36:
			Menu_Manipulate_Rotate_Tools = (MenuItem)target;
			Menu_Manipulate_Rotate_Tools.Click += Menu_Manipulate_Rotate_Click;
			break;
		case 37:
			Menu_Manipulate_RotateCCW_Tools = (MenuItem)target;
			Menu_Manipulate_RotateCCW_Tools.Click += Menu_Manipulate_RotateCCW_Click;
			break;
		case 38:
			Menu_Manipulate_Resize_Tools = (MenuItem)target;
			Menu_Manipulate_Resize_Tools.Click += Menu_Manipulate_Resize_Click;
			break;
		case 39:
			Menu_Manipulate_FlipH_Tools = (MenuItem)target;
			Menu_Manipulate_FlipH_Tools.Click += Menu_Manipulate_FlipH_Click;
			break;
		case 40:
			Menu_Manipulate_FlipV_Tools = (MenuItem)target;
			Menu_Manipulate_FlipV_Tools.Click += Menu_Manipulate_FlipV_Click;
			break;
		case 41:
			MenuToolSelectAllSame = (MenuItem)target;
			break;
		case 42:
			MenuToolReplaceSelected = (MenuItem)target;
			break;
		case 43:
			MenuToolWand = (MenuItem)target;
			break;
		case 44:
			MenuToolStructure = (MenuItem)target;
			break;
		case 45:
			MenuToolOffsetMap = (MenuItem)target;
			break;
		case 46:
			MenuEditCut = (MenuItem)target;
			break;
		case 47:
			MenuEditCopy = (MenuItem)target;
			break;
		case 48:
			MenuEditPaste = (MenuItem)target;
			break;
		case 49:
			MenuToolUndo = (MenuItem)target;
			break;
		case 50:
			MenuToolRedo = (MenuItem)target;
			break;
		case 51:
			MenuToolStartPos = (MenuItem)target;
			break;
		case 52:
			MenuOpenSimulator = (MenuItem)target;
			break;
		case 53:
			MenuConfigureFamidashRom = (MenuItem)target;
			break;
		case 54:
			MenuRunFamidashMesen = (MenuItem)target;
			break;
		case 55:
			MenuCaptureRamMesen = (MenuItem)target;
			break;
		case 56:
			MenuBuildAndTest = (MenuItem)target;
			break;
		case 57:
			MenuOverlayAndFollow = (MenuItem)target;
			break;
		case 58:
			MenuCamFollow = (MenuItem)target;
			break;
		case 59:
			MenuOptionLegacyTriggers = (MenuItem)target;
			break;
		case 60:
			MenuOptionSuppressCollisionMessages = (MenuItem)target;
			break;
		case 61:
			MenuOptionSwapMouseWheel = (MenuItem)target;
			break;
		case 62:
			MenuOptionSwapPinch = (MenuItem)target;
			break;
		case 63:
			MenuOptionHideBackground = (MenuItem)target;
			break;
		case 64:
			MenuOptionHideGround = (MenuItem)target;
			break;
		case 65:
			MenuOptionFastZoom = (MenuItem)target;
			break;
		case 66:
			MenuColorEditorBackground = (MenuItem)target;
			break;
		case 67:
			MenuTileboardLeft = (MenuItem)target;
			break;
		case 68:
			MenuTileboardRight = (MenuItem)target;
			break;
		case 69:
			MenuTileboardTop = (MenuItem)target;
			break;
		case 70:
			MenuTileboardBottom = (MenuItem)target;
			break;
		case 71:
			MenuTileboardHidden = (MenuItem)target;
			break;
		case 72:
			MenuColorPlayerTint = (MenuItem)target;
			break;
		case 73:
			MenuConfigureFamiStudio = (MenuItem)target;
			break;
		case 74:
			MenuScanFamiStudioTracks = (MenuItem)target;
			break;
		case 75:
			MenuColorBackgroundTint = (MenuItem)target;
			break;
		case 76:
			MenuColorGroundTint = (MenuItem)target;
			break;
		case 77:
			MenuColorTileTint = (MenuItem)target;
			break;
		case 78:
			MenuOptionShowAccurateTileset = (MenuItem)target;
			MenuOptionShowAccurateTileset.Checked += MenuOptionShowAccurateTileset_Checked;
			MenuOptionShowAccurateTileset.Unchecked += MenuOptionShowAccurateTileset_Unchecked;
			break;
		case 79:
			MenuOptionLockSprites = (MenuItem)target;
			break;
		case 80:
			MenuOptionHideColorTriggers = (MenuItem)target;
			break;
		case 81:
			MenuOptionHideInvisibleSprites = (MenuItem)target;
			break;
		case 82:
			MenuSimulatorSize1x = (MenuItem)target;
			break;
		case 83:
			MenuSimulatorSize2x = (MenuItem)target;
			break;
		case 84:
			MenuSimulatorSize3x = (MenuItem)target;
			break;
		case 85:
			MenuSimulatorSize4x = (MenuItem)target;
			break;
		case 86:
			MenuOptionShowSpriteHitboxes = (MenuItem)target;
			MenuOptionShowSpriteHitboxes.Checked += MenuOptionShowSpriteHitboxes_Checked;
			MenuOptionShowSpriteHitboxes.Unchecked += MenuOptionShowSpriteHitboxes_Unchecked;
			break;
		case 87:
			MenuOptionTileHitboxes = (MenuItem)target;
			MenuOptionTileHitboxes.Checked += MenuOptionTileHitboxes_Checked;
			MenuOptionTileHitboxes.Unchecked += MenuOptionTileHitboxes_Unchecked;
			break;
		case 88:
			MenuOptionNoDeath = (MenuItem)target;
			break;
		case 89:
			MenuOptionCamMode = (MenuItem)target;
			break;
		case 90:
			MenuOptionHideTriggerSprites = (MenuItem)target;
			MenuOptionHideTriggerSprites.Checked += MenuOptionHideTriggerSprites_Checked;
			MenuOptionHideTriggerSprites.Unchecked += MenuOptionHideTriggerSprites_Unchecked;
			break;
		case 91:
			MenuOptionOpenSimulatorPaused = (MenuItem)target;
			break;
		case 92:
			MenuOptionClearPlayerPath = (MenuItem)target;
			MenuOptionClearPlayerPath.Click += MenuOptionClearPlayerPath_Click;
			break;
		case 93:
			FileTabControl = (System.Windows.Controls.TabControl)target;
			FileTabControl.SelectionChanged += FileTabControl_SelectionChanged;
			break;
		case 94:
			PlaceTool = (ToggleButton)target;
			break;
		case 95:
			MoveTool = (ToggleButton)target;
			break;
		case 96:
			EraseTool = (ToggleButton)target;
			break;
		case 97:
			FillTool = (ToggleButton)target;
			break;
		case 98:
			FillToolMore = (System.Windows.Controls.Button)target;
			FillToolMore.Click += FillToolMore_Click;
			break;
		case 99:
			Menu_Fill_Normal = (MenuItem)target;
			break;
		case 100:
			Menu_Fill_ReplaceSelected = (MenuItem)target;
			break;
		case 101:
			SelectTool = (ToggleButton)target;
			break;
		case 102:
			SelectToolMore = (System.Windows.Controls.Button)target;
			SelectToolMore.Click += SelectToolMore_Click;
			break;
		case 103:
			Menu_Select_Normal = (MenuItem)target;
			break;
		case 104:
			Menu_Select_AllSame = (MenuItem)target;
			break;
		case 105:
			Menu_Select_Lasso = (MenuItem)target;
			break;
		case 106:
			Menu_Select_Ellipse = (MenuItem)target;
			break;
		case 107:
			MagicWandTool = (ToggleButton)target;
			break;
		case 108:
			StructureTool = (ToggleButton)target;
			StructureTool.Click += StructureTool_Click;
			break;
		case 109:
			StructureToolIcon = (Image)target;
			break;
		case 110:
			StartPosTool = (ToggleButton)target;
			break;
		case 111:
			StructurePopup = (Popup)target;
			break;
		case 112:
			StructureSetAButton = (System.Windows.Controls.Button)target;
			StructureSetAButton.Click += StructureSetAButton_Click;
			break;
		case 113:
			StructureSetAIcon = (Image)target;
			break;
		case 114:
			StructureSetBButton = (System.Windows.Controls.Button)target;
			StructureSetBButton.Click += StructureSetBButton_Click;
			break;
		case 115:
			StructureSetBIcon = (Image)target;
			break;
		case 116:
			StructureSetCButton = (System.Windows.Controls.Button)target;
			StructureSetCButton.Click += StructureSetCButton_Click;
			break;
		case 117:
			StructureSetCIcon = (Image)target;
			break;
		case 118:
			PreviewModeCheckbox = (System.Windows.Controls.CheckBox)target;
			break;
		case 119:
			FamiTrackCombo = (System.Windows.Controls.ComboBox)target;
			break;
		case 120:
			PlayFamiButton = (System.Windows.Controls.Button)target;
			break;
		case 121:
			StopFamiButton = (System.Windows.Controls.Button)target;
			break;
		case 122:
			DrawTileButton = (ToggleButton)target;
			break;
		case 123:
			DrawLineButton = (ToggleButton)target;
			break;
		case 124:
			DrawSquareButton = (ToggleButton)target;
			break;
		case 125:
			DrawCircleButton = (ToggleButton)target;
			break;
		case 126:
			DrawEllipseButton = (ToggleButton)target;
			break;
		case 127:
			DrawTriangleButton = (ToggleButton)target;
			break;
		case 128:
			DrawPolygonButton = (ToggleButton)target;
			break;
		case 129:
			HollowCheckBox = (System.Windows.Controls.CheckBox)target;
			break;
		case 130:
			BrushThicknessSlider = (Slider)target;
			break;
		case 131:
			ManipulateButton = (System.Windows.Controls.Button)target;
			ManipulateButton.Click += ManipulateButton_Click;
			break;
		case 132:
			Menu_Manipulate_Rotate = (MenuItem)target;
			Menu_Manipulate_Rotate.Click += Menu_Manipulate_Rotate_Click;
			break;
		case 133:
			Menu_Manipulate_RotateCCW = (MenuItem)target;
			Menu_Manipulate_RotateCCW.Click += Menu_Manipulate_RotateCCW_Click;
			break;
		case 134:
			Menu_Manipulate_Resize = (MenuItem)target;
			Menu_Manipulate_Resize.Click += Menu_Manipulate_Resize_Click;
			break;
		case 135:
			Menu_Manipulate_FlipH = (MenuItem)target;
			Menu_Manipulate_FlipH.Click += Menu_Manipulate_FlipH_Click;
			break;
		case 136:
			Menu_Manipulate_FlipV = (MenuItem)target;
			Menu_Manipulate_FlipV.Click += Menu_Manipulate_FlipV_Click;
			break;
		case 137:
			UndoButton = (System.Windows.Controls.Button)target;
			break;
		case 138:
			RedoButton = (System.Windows.Controls.Button)target;
			break;
		case 139:
			CutButton = (System.Windows.Controls.Button)target;
			break;
		case 140:
			CopyButton = (System.Windows.Controls.Button)target;
			break;
		case 141:
			PasteButton = (System.Windows.Controls.Button)target;
			break;
		case 142:
			SaveButton = (System.Windows.Controls.Button)target;
			break;
		case 143:
			LoadButton = (System.Windows.Controls.Button)target;
			break;
		case 144:
			SetOptionsButton = (System.Windows.Controls.Button)target;
			break;
		case 145:
			CalculatePathButton = (System.Windows.Controls.Button)target;
			CalculatePathButton.Click += CalculatePathButton_Click;
			break;
		case 146:
			StopPathfinderButton = (System.Windows.Controls.Button)target;
			StopPathfinderButton.Click += StopPathfinderButton_Click;
			break;
		case 147:
			PathfinderProgressBar = (System.Windows.Controls.ProgressBar)target;
			break;
		case 148:
			PathfinderJumpToButton = (System.Windows.Controls.Button)target;
			PathfinderJumpToButton.Click += PathfinderJumpToButton_Click;
			break;
		case 149:
			PathfinderReplayButton = (System.Windows.Controls.Button)target;
			PathfinderReplayButton.Click += PathfinderReplayButton_Click;
			break;
		case 150:
			WidthBox = (System.Windows.Controls.TextBox)target;
			break;
		case 151:
			HeightBox = (System.Windows.Controls.TextBox)target;
			break;
		case 152:
			ResizeButton = (System.Windows.Controls.Button)target;
			break;
		case 153:
			StatusText = (TextBlock)target;
			break;
		case 154:
			((MenuItem)target).Click += StatusText_Copy_Click;
			break;
		case 155:
			GridDarknessSlider = (Slider)target;
			break;
		case 156:
			ZoomSlider = (Slider)target;
			break;
		case 157:
			ZoomLevelLabel = (TextBlock)target;
			break;
		case 158:
			MapScrollViewer = (ScrollViewer)target;
			break;
		case 159:
			MapContentRoot = (Grid)target;
			break;
		case 160:
			BackgroundImage = (Image)target;
			break;
		case 161:
			ParallaxImage = (Image)target;
			break;
		case 162:
			GroundImage = (Image)target;
			break;
		case 163:
			TilesImage = (Image)target;
			break;
		case 164:
			PortalsImage = (Image)target;
			break;
		case 165:
			SpritesImage = (Image)target;
			break;
		case 166:
			GridImage = (Image)target;
			break;
		case 167:
			CanvasHost = (Canvas)target;
			break;
		case 168:
			HoverRect = (Rectangle)target;
			break;
		case 169:
			HoverBorder = (Border)target;
			break;
		case 170:
			SelectionOverlay = (Canvas)target;
			break;
		case 171:
			IncompatibleOverlay = (Canvas)target;
			break;
		case 172:
			SelectSameOverlay = (Canvas)target;
			break;
		case 173:
			GhostImage = (Image)target;
			break;
		case 174:
			OffsetGhostTile = (Rectangle)target;
			break;
		case 175:
			OffsetTooltipContainer = (Canvas)target;
			break;
		case 176:
			OffsetGhostContainer = (Canvas)target;
			break;
		case 177:
			MesenSplitter = (GridSplitter)target;
			break;
		case 178:
			MesenEmbeddedHost = (Border)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}

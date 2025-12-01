using Godot;
using System;

namespace FamidashEditor;

/// <summary>
/// Main editor controller. Coordinates map data, undo/redo, tilesets, and UI.
/// </summary>
public partial class MainEditor : Control
{
    // Core systems
    private MapData _mapData = new MapData();
    private TilesetManager _tilesetManager = new TilesetManager();
    private UndoRedoManager? _undoRedoManager;
    
    // UI references
    private MapCanvas? _mapCanvas;
    private PalettePanel? _tilePalette;
    private PalettePanel? _spritePalette;
    private Label? _statusLabel;
    private Label? _zoomLabel;
    private Label? _coordsLabel;
    
    // Menu references
    private MenuButton? _fileMenu;
    private MenuButton? _editMenu;
    private MenuButton? _viewMenu;
    
    // State
    private int _selectedTile = -1;
    private int _selectedSprite = -1;
    private bool _editingTiles = true; // true = tiles, false = sprites
    private string _currentFilePath = "";
    private bool _hasUnsavedChanges = false;
    
    public override void _Ready()
    {
        GD.Print("=== Famidash Editor Initializing ===");
        _undoRedoManager = new UndoRedoManager(_mapData);
        
        // Get UI references
        GD.Print("Getting UI references...");
        _mapCanvas = GetNode<MapCanvas>("VBoxContainer/HBoxContainer/MapCanvas");
        _tilePalette = GetNode<PalettePanel>("VBoxContainer/HBoxContainer/TilePalette");
        _spritePalette = GetNode<PalettePanel>("VBoxContainer/HBoxContainer/SpritePalette");
        _statusLabel = GetNode<Label>("VBoxContainer/StatusBar/StatusLabel");
        _zoomLabel = GetNode<Label>("VBoxContainer/StatusBar/ZoomLabel");
        _coordsLabel = GetNode<Label>("VBoxContainer/StatusBar/CoordsLabel");
        
        // Get menu buttons
        _fileMenu = GetNode<MenuButton>("VBoxContainer/MenuBar/FileMenu");
        _editMenu = GetNode<MenuButton>("VBoxContainer/MenuBar/EditMenu");
        _viewMenu = GetNode<MenuButton>("VBoxContainer/MenuBar/ViewMenu");
        
        // Make sure menus work
        _fileMenu.SwitchOnHover = false;
        _editMenu.SwitchOnHover = false;
        _viewMenu.SwitchOnHover = false;
        
        // Setup menus
        SetupMenus();
        
        // Connect signals
        _mapCanvas.CellClicked += OnMapCellClicked;
        _mapCanvas.CellHovered += OnMapCellHovered;
        _tilePalette.TileSelected += OnTileSelected;
        _spritePalette.TileSelected += OnSpriteSelected;
        
        // Initialize default map
        _mapData.Resize(200, 27);
        
        // Add test pattern to verify rendering (just a few tiles)
        for (int i = 0; i < 10; i++)
        {
            _mapData.SetTile(i, 10, 0); // Place tile 0 in a row
        }
        
        // Load default tileset (try to find assets in parent directory)
        bool assetsLoaded = false;
        
        // Get project root directory (parent of godot-port folder)
        string projectPath = ProjectSettings.GlobalizePath("res://");
        string parentDir = System.IO.Path.GetDirectoryName(projectPath.TrimEnd('/', '\\')) ?? "";
        
        string tilesetPath = System.IO.Path.Combine(parentDir, "famidash.bmp");
        string spritesetPath = System.IO.Path.Combine(parentDir, "sprites.png");
        
        GD.Print($"Looking for assets in: {parentDir}");
        GD.Print($"Tileset path: {tilesetPath}");
        GD.Print($"Spriteset path: {spritesetPath}");
        
        // Try loading from parent directory
        if (System.IO.File.Exists(tilesetPath))
        {
            _tilesetManager.LoadTileset(tilesetPath);
            assetsLoaded = true;
            GD.Print("Tileset loaded successfully");
        }
        else
        {
            GD.PrintErr($"Tileset not found at: {tilesetPath}");
        }
        
        if (System.IO.File.Exists(spritesetPath))
        {
            _tilesetManager.LoadSpriteset(spritesetPath);
            GD.Print("Spriteset loaded successfully");
        }
        else
        {
            GD.PrintErr($"Spriteset not found at: {spritesetPath}");
        }
        
        // Populate palettes
        if (assetsLoaded)
        {
            GD.Print("Populating palettes...");
            _tilePalette?.PopulateWithTiles(_tilesetManager);
            _spritePalette?.PopulateWithSprites(_tilesetManager);
            GD.Print("Palettes populated");
        }
        
        // Initialize canvas
        GD.Print("Initializing map canvas...");
        _mapCanvas?.Initialize(_mapData, _tilesetManager);
        _mapCanvas?.CenterCamera();
        _mapCanvas?.RedrawMap();
        
        GD.Print("=== Editor Ready ===");
        UpdateStatus("Ready - " + (assetsLoaded ? "Assets loaded" : "Warning: Assets not found in parent directory"));
    }
    
    private void SetupMenus()
    {
        GD.Print("Setting up menus...");
        
        // File menu
        if (_fileMenu != null)
        {
            GD.Print("Setting up File menu");
            var filePopup = _fileMenu.GetPopup();
            filePopup.Clear();
            filePopup.AddItem("New", 0);
            filePopup.AddItem("Open...", 1);
            filePopup.AddSeparator();
            filePopup.AddItem("Save", 2);
            filePopup.AddItem("Save As...", 3);
            
            filePopup.IdPressed += OnFileMenuItemPressed;
            GD.Print($"File menu has {filePopup.ItemCount} items");
        }
        else
        {
            GD.PrintErr("_fileMenu is null!");
        }
        
        // Edit menu
        if (_editMenu != null)
        {
            var editPopup = _editMenu.GetPopup();
            editPopup.Clear();
            editPopup.AddItem("Undo (Ctrl+Z)", 0);
            editPopup.AddItem("Redo (Ctrl+Y)", 1);
            
            editPopup.IdPressed += OnEditMenuItemPressed;
        }
        
        // View menu
        if (_viewMenu != null)
        {
            var viewPopup = _viewMenu.GetPopup();
            viewPopup.Clear();
            viewPopup.AddCheckItem("Show Tiles", 0);
            viewPopup.AddCheckItem("Show Sprites", 1);
            viewPopup.AddCheckItem("Show Grid", 2);
            viewPopup.AddSeparator();
            viewPopup.AddItem("Zoom In (+)", 3);
            viewPopup.AddItem("Zoom Out (-)", 4);
            viewPopup.AddItem("Reset Zoom (0)", 5);
            
            // Set initial checks
            viewPopup.SetItemChecked(0, true);
            viewPopup.SetItemChecked(1, true);
            viewPopup.SetItemChecked(2, true);
            
            viewPopup.IdPressed += OnViewMenuItemPressed;
        }
    }
    
    private void OnFileMenuItemPressed(long id)
    {
        GD.Print($"File menu item pressed: {id}");
        switch (id)
        {
            case 0: NewFile(); break;
            case 1: OpenFile(); break;
            case 2: SaveFile(); break;
            case 3: SaveFileAs(); break;
        }
    }
    
    private void OnEditMenuItemPressed(long id)
    {
        switch (id)
        {
            case 0: Undo(); break;
            case 1: Redo(); break;
        }
    }
    
    private void OnViewMenuItemPressed(long id)
    {
        if (_viewMenu == null) return;
        var viewPopup = _viewMenu.GetPopup();
        
        switch (id)
        {
            case 0: // Toggle tiles
                viewPopup.ToggleItemChecked(0);
                UpdateLayerVisibility();
                break;
            case 1: // Toggle sprites
                viewPopup.ToggleItemChecked(1);
                UpdateLayerVisibility();
                break;
            case 2: // Toggle grid
                viewPopup.ToggleItemChecked(2);
                UpdateLayerVisibility();
                break;
            case 3: _mapCanvas?.ZoomIn(); UpdateZoomLabel(); break;
            case 4: _mapCanvas?.ZoomOut(); UpdateZoomLabel(); break;
            case 5: _mapCanvas?.ZoomReset(); UpdateZoomLabel(); break;
        }
    }
    
    private void UpdateLayerVisibility()
    {
        if (_viewMenu == null || _mapCanvas == null) return;
        
        var viewPopup = _viewMenu.GetPopup();
        bool showTiles = viewPopup.IsItemChecked(0);
        bool showSprites = viewPopup.IsItemChecked(1);
        bool showGrid = viewPopup.IsItemChecked(2);
        
        _mapCanvas.SetLayerVisibility(showTiles, showSprites, showGrid);
        _mapCanvas.RedrawMap();
    }
    
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed)
        {
            // Ctrl+Z = Undo
            if (keyEvent.Keycode == Key.Z && keyEvent.CtrlPressed && !keyEvent.ShiftPressed)
            {
                Undo();
                GetViewport()?.SetInputAsHandled();
            }
            // Ctrl+Y or Ctrl+Shift+Z = Redo
            else if ((keyEvent.Keycode == Key.Y && keyEvent.CtrlPressed) ||
                     (keyEvent.Keycode == Key.Z && keyEvent.CtrlPressed && keyEvent.ShiftPressed))
            {
                Redo();
                GetViewport()?.SetInputAsHandled();
            }
            // Ctrl+S = Save
            else if (keyEvent.Keycode == Key.S && keyEvent.CtrlPressed)
            {
                SaveFile();
                GetViewport()?.SetInputAsHandled();
            }
            // Tab = Switch between tile/sprite layers
            else if (keyEvent.Keycode == Key.Tab)
            {
                _editingTiles = !_editingTiles;
                UpdateStatus(_editingTiles ? "Editing: Tiles" : "Editing: Sprites");
                GetViewport()?.SetInputAsHandled();
            }
        }
    }
    
    private void OnTileSelected(int tileId)
    {
        _selectedTile = tileId;
        _editingTiles = true;
        UpdateStatus($"Selected tile: {tileId}");
        GD.Print($"Tile selected: {tileId}");
    }
    
    private void OnSpriteSelected(int spriteId)
    {
        _selectedSprite = spriteId;
        _editingTiles = false;
        UpdateStatus($"Selected sprite: {spriteId}");
        GD.Print($"Sprite selected: {spriteId}");
    }
    
    private void OnMapCellClicked(int x, int y, bool isRightClick)
    {
        GD.Print($"Map cell clicked: ({x}, {y}), right={isRightClick}, editingTiles={_editingTiles}, selectedTile={_selectedTile}");
        if (isRightClick)
        {
            // Right click = erase
            if (_editingTiles)
            {
                int oldTile = _mapData.GetTile(x, y);
                var action = new SetCellAction(x, y, oldTile, -1, 0, 0, true, false);
                _undoRedoManager?.RecordAction(action);
                _mapData.SetTile(x, y, -1);
            }
            else
            {
                int oldSprite = _mapData.GetSprite(x, y);
                var action = new SetCellAction(x, y, 0, 0, oldSprite, -1, false, true);
                _undoRedoManager?.RecordAction(action);
                _mapData.SetSprite(x, y, -1);
            }
        }
        else
        {
            // Left click = place
            if (_editingTiles && _selectedTile >= 0)
            {
                int oldTile = _mapData.GetTile(x, y);
                var action = new SetCellAction(x, y, oldTile, _selectedTile, 0, 0, true, false);
                _undoRedoManager?.RecordAction(action);
                _mapData.SetTile(x, y, _selectedTile);
            }
            else if (!_editingTiles && _selectedSprite >= 0)
            {
                int oldSprite = _mapData.GetSprite(x, y);
                var action = new SetCellAction(x, y, 0, 0, oldSprite, _selectedSprite, false, true);
                _undoRedoManager?.RecordAction(action);
                _mapData.SetSprite(x, y, _selectedSprite);
            }
        }
        
        _hasUnsavedChanges = true;
        _mapCanvas?.RedrawMap();
    }
    
    private void OnMapCellHovered(int x, int y)
    {
        if (_coordsLabel != null)
        {
            _coordsLabel.Text = $"X: {x}, Y: {y}";
        }
    }
    
    private void Undo()
    {
        if (_undoRedoManager?.CanUndo == true)
        {
            _undoRedoManager.Undo();
            _mapCanvas?.RedrawMap();
            UpdateStatus("Undo");
        }
    }
    
    private void Redo()
    {
        if (_undoRedoManager?.CanRedo == true)
        {
            _undoRedoManager.Redo();
            _mapCanvas?.RedrawMap();
            UpdateStatus("Redo");
        }
    }
    
    private void NewFile()
    {
        // TODO: Prompt to save if unsaved changes
        _mapData.Resize(200, 27);
        _currentFilePath = "";
        _hasUnsavedChanges = false;
        _undoRedoManager?.Clear();
        _mapCanvas?.RedrawMap();
        UpdateStatus("New file created");
    }
    
    private void OpenFile()
    {
        // TODO: Implement file dialog and TMX loading
        UpdateStatus("Open file not yet implemented");
    }
    
    private void SaveFile()
    {
        if (string.IsNullOrEmpty(_currentFilePath))
        {
            SaveFileAs();
        }
        else
        {
            // TODO: Implement TMX saving
            UpdateStatus("Save not yet implemented");
        }
    }
    
    private void SaveFileAs()
    {
        // TODO: Implement file dialog and TMX saving
        UpdateStatus("Save As not yet implemented");
    }
    
    private void UpdateStatus(string message)
    {
        if (_statusLabel != null)
        {
            _statusLabel.Text = message;
        }
        GD.Print(message);
    }
    
    private void UpdateZoomLabel()
    {
        // This will be called by MapCanvas or via signals
    }
}

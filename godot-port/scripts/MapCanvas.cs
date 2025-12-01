using Godot;
using System;

namespace FamidashEditor
{
    /// <summary>
    /// Renders the tile and sprite map to a SubViewport with zoom and pan support.
    /// </summary>
    public partial class MapCanvas : SubViewportContainer
    {
        private const int TileSize = 16;
        
        // References
        private SubViewport? _viewport;
        private Camera2D? _camera;
        private Node2D? _renderRoot;
        
        private MapData? _mapData;
        private TilesetManager? _tilesetManager;
        
        // Layer visibility
        private bool _showTiles = true;
        private bool _showSprites = true;
        private bool _showGrid = true;
        
        // Camera state
        private float _zoom = 2.0f;
        private Vector2 _panOffset = Vector2.Zero;
        private bool _isPanning = false;
        private Vector2 _panStartPos = Vector2.Zero;
        
        // Selection
        private Vector2I _hoverCell = new Vector2I(-1, -1);
        private Vector2I _selectedCell = new Vector2I(-1, -1);
        
        // Signals
        [Signal]
        public delegate void CellClickedEventHandler(int x, int y, bool isRightClick);
        
        [Signal]
        public delegate void CellHoveredEventHandler(int x, int y);
        
        public override void _Ready()
        {
            // Get viewport and camera
            _viewport = GetNode<SubViewport>("Viewport");
            _camera = _viewport.GetNode<Camera2D>("Camera2D");
            _renderRoot = _viewport.GetNode<Node2D>("RenderRoot");
            
            // Add background to make canvas visible
            var background = new ColorRect();
            background.Color = new Color(0.25f, 0.25f, 0.28f);
            background.Size = new Vector2(10000, 10000);
            background.Position = new Vector2(-5000, -5000);
            background.ZIndex = -100;
            _renderRoot.AddChild(background);
            
            // Configure viewport
            _viewport.TransparentBg = false;
            
            // Configure camera
            _camera.Position = Vector2.Zero;
            _camera.Enabled = true;
            UpdateCameraZoom();
            
            // Enable input
            MouseFilter = MouseFilterEnum.Pass;
            
            GD.Print("MapCanvas ready with background");
        }
        
        public void Initialize(MapData mapData, TilesetManager tilesetManager)
        {
            _mapData = mapData;
            _tilesetManager = tilesetManager;
            QueueRedraw();
        }
        
        public void SetLayerVisibility(bool tiles, bool sprites, bool grid)
        {
            _showTiles = tiles;
            _showSprites = sprites;
            _showGrid = grid;
            QueueRedraw();
        }
        
        public void SetZoom(float zoom)
        {
            _zoom = Mathf.Clamp(zoom, 0.5f, 8.0f);
            UpdateCameraZoom();
        }
        
        public void ZoomIn() => SetZoom(_zoom * 1.25f);
        public void ZoomOut() => SetZoom(_zoom / 1.25f);
        public void ZoomReset() => SetZoom(2.0f);
        
        private void UpdateCameraZoom()
        {
            if (_camera != null)
            {
                _camera.Zoom = new Vector2(_zoom, _zoom);
            }
        }
        
        public void CenterCamera()
        {
            if (_camera != null && _mapData != null)
            {
                var mapCenter = new Vector2(_mapData.Width * TileSize / 2.0f, _mapData.Height * TileSize / 2.0f);
                _camera.Position = mapCenter;
            }
        }
        
        public override void _Input(InputEvent @event)
        {
            if (_mapData == null) return;
            
            // Mouse wheel zoom
            if (@event is InputEventMouseButton mouseButton)
            {
                if (mouseButton.ButtonIndex == MouseButton.WheelUp && mouseButton.Pressed)
                {
                    ZoomIn();
                    GetViewport()?.SetInputAsHandled();
                }
                else if (mouseButton.ButtonIndex == MouseButton.WheelDown && mouseButton.Pressed)
                {
                    ZoomOut();
                    GetViewport()?.SetInputAsHandled();
                }
                // Middle mouse button pan
                else if (mouseButton.ButtonIndex == MouseButton.Middle)
                {
                    if (mouseButton.Pressed)
                    {
                        _isPanning = true;
                        _panStartPos = GetGlobalMousePosition();
                    }
                    else
                    {
                        _isPanning = false;
                    }
                    GetViewport()?.SetInputAsHandled();
                }
                // Left/Right click
                else if (mouseButton.ButtonIndex == MouseButton.Left || mouseButton.ButtonIndex == MouseButton.Right)
                {
                    if (mouseButton.Pressed)
                    {
                        var cellPos = GetCellAtMouse();
                        if (IsValidCell(cellPos))
                        {
                            _selectedCell = cellPos;
                            EmitSignal(SignalName.CellClicked, cellPos.X, cellPos.Y, mouseButton.ButtonIndex == MouseButton.Right);
                            QueueRedraw();
                        }
                    }
                    GetViewport()?.SetInputAsHandled();
                }
            }
            
            // Mouse motion
            if (@event is InputEventMouseMotion motion)
            {
                if (_isPanning && _camera != null)
                {
                    var delta = (GetGlobalMousePosition() - _panStartPos) / _zoom;
                    _camera.Position -= delta;
                    _panStartPos = GetGlobalMousePosition();
                }
                else
                {
                    var cellPos = GetCellAtMouse();
                    if (cellPos != _hoverCell)
                    {
                        _hoverCell = cellPos;
                        if (IsValidCell(cellPos))
                        {
                            EmitSignal(SignalName.CellHovered, cellPos.X, cellPos.Y);
                        }
                        QueueRedraw();
                    }
                }
            }
        }
        
        private Vector2I GetCellAtMouse()
        {
            if (_camera == null || _viewport == null) return new Vector2I(-1, -1);
            
            var mousePos = GetLocalMousePosition();
            var viewportSize = _viewport.GetVisibleRect().Size;
            var canvasSize = Size;
            
            // Convert to viewport space
            var viewportPos = mousePos * (viewportSize / canvasSize);
            
            // Convert to world space
            var worldPos = _camera.GetScreenCenterPosition() - viewportSize / (2.0f * _zoom) + viewportPos / _zoom;
            
            // Convert to cell coordinates
            return new Vector2I(
                Mathf.FloorToInt(worldPos.X / TileSize),
                Mathf.FloorToInt(worldPos.Y / TileSize)
            );
        }
        
        private bool IsValidCell(Vector2I cell)
        {
            return _mapData != null &&
                   cell.X >= 0 && cell.X < _mapData.Width &&
                   cell.Y >= 0 && cell.Y < _mapData.Height;
        }
        
        public void RedrawMap()
        {
            if (_renderRoot == null || _mapData == null || _tilesetManager == null)
            {
                GD.PrintErr("Cannot redraw: missing dependencies");
                return;
            }
            
            GD.Print("Redrawing map...");
            
            // Clear existing children
            foreach (var child in _renderRoot.GetChildren())
            {
                child.QueueFree();
            }
            
            // Render tiles
            if (_showTiles)
            {
                RenderTileLayer();
            }
            
            // Render sprites
            if (_showSprites)
            {
                RenderSpriteLayer();
            }
            
            // Render grid
            if (_showGrid)
            {
                RenderGrid();
            }
            
            // Render selection
            RenderSelection();
        }
        
        private void RenderTileLayer()
        {
            if (_mapData == null || _tilesetManager == null) return;
            
            for (int y = 0; y < _mapData.Height; y++)
            {
                for (int x = 0; x < _mapData.Width; x++)
                {
                    int tileId = _mapData.GetTile(x, y);
                    if (tileId >= 0)
                    {
                        var texture = _tilesetManager.GetTileTexture(tileId);
                        if (texture != null)
                        {
                            var sprite = new Sprite2D();
                            sprite.Texture = texture;
                            sprite.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
                            sprite.Position = new Vector2(x * TileSize + TileSize / 2, y * TileSize + TileSize / 2);
                            sprite.Centered = true;
                            _renderRoot!.AddChild(sprite);
                        }
                    }
                }
            }
        }
        
        private void RenderSpriteLayer()
        {
            if (_mapData == null || _tilesetManager == null) return;
            
            for (int y = 0; y < _mapData.Height; y++)
            {
                for (int x = 0; x < _mapData.Width; x++)
                {
                    int spriteId = _mapData.GetSprite(x, y);
                    if (spriteId >= 0)
                    {
                        var texture = _tilesetManager.GetSpriteTexture(spriteId);
                        if (texture != null)
                        {
                            var sprite = new Sprite2D();
                            sprite.Texture = texture;
                            sprite.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
                            sprite.Position = new Vector2(x * TileSize + TileSize / 2, y * TileSize + TileSize / 2);
                            sprite.Centered = true;
                            _renderRoot!.AddChild(sprite);
                        }
                    }
                }
            }
        }
        
        private void RenderGrid()
        {
            if (_mapData == null || _renderRoot == null) return;
            
            // Simple grid implementation using Line2D
            var gridNode = new Node2D();
            gridNode.Name = "Grid";
            gridNode.ZIndex = 10;
            
            // Draw vertical lines every 16 pixels
            for (int x = 0; x <= _mapData.Width; x++)
            {
                var line = new Line2D();
                line.AddPoint(new Vector2(x * TileSize, 0));
                line.AddPoint(new Vector2(x * TileSize, _mapData.Height * TileSize));
                line.DefaultColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                line.Width = 1;
                gridNode.AddChild(line);
            }
            
            // Draw horizontal lines
            for (int y = 0; y <= _mapData.Height; y++)
            {
                var line = new Line2D();
                line.AddPoint(new Vector2(0, y * TileSize));
                line.AddPoint(new Vector2(_mapData.Width * TileSize, y * TileSize));
                line.DefaultColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
                line.Width = 1;
                gridNode.AddChild(line);
            }
            
            _renderRoot.AddChild(gridNode);
        }
        
        private void RenderSelection()
        {
            if (_renderRoot == null || !IsValidCell(_selectedCell)) return;
            
            var selectionRect = new ColorRect();
            selectionRect.Color = new Color(1, 1, 0, 0.3f); // Yellow semi-transparent
            selectionRect.Position = new Vector2(_selectedCell.X * TileSize, _selectedCell.Y * TileSize);
            selectionRect.Size = new Vector2(TileSize, TileSize);
            _renderRoot.AddChild(selectionRect);
        }
    }
}

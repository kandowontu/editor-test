using Godot;
using System;
using System.Collections.Generic;

namespace FamidashEditor
{
    /// <summary>
    /// Displays a grid of tile or sprite textures for selection.
    /// </summary>
    public partial class PalettePanel : ScrollContainer
    {
    private const int TileSize = 16;
    private const int ColumnsPerRow = 8;        private GridContainer? _grid;
        private List<TextureButton> _buttons = new List<TextureButton>();
        private int _selectedIndex = -1;
        
        [Signal]
        public delegate void TileSelectedEventHandler(int tileId);
        
        public override void _Ready()
        {
            // Create grid container
            _grid = new GridContainer();
            _grid.Columns = ColumnsPerRow;
            AddChild(_grid);
        }
        
        /// <summary>
        /// Populate palette with tile textures.
        /// </summary>
        public void PopulateWithTiles(TilesetManager tilesetManager)
        {
            Clear();
            
            if (tilesetManager.TileTextures == null)
            {
                GD.PrintErr("TileTextures is null!");
                return;
            }
            
            GD.Print($"Populating tile palette with {tilesetManager.TileTextures.Length} tiles");
            for (int i = 0; i < tilesetManager.TileTextures.Length; i++)
            {
                var texture = tilesetManager.TileTextures[i];
                if (texture != null)
                {
                    AddTile(i, texture);
                }
            }
        }
        
        /// <summary>
        /// Populate palette with sprite textures.
        /// </summary>
        public void PopulateWithSprites(TilesetManager tilesetManager)
        {
            Clear();
            
            if (tilesetManager.SpriteTextures == null)
            {
                GD.PrintErr("SpriteTextures is null!");
                return;
            }
            
            GD.Print($"Populating sprite palette with {tilesetManager.SpriteTextures.Length} sprites");
            for (int i = 0; i < tilesetManager.SpriteTextures.Length; i++)
            {
                var texture = tilesetManager.SpriteTextures[i];
                if (texture != null)
                {
                    AddTile(i, texture);
                }
            }
        }
        
        private void AddTile(int id, Texture2D texture)
        {
            if (_grid == null) return;
            
            var button = new TextureButton();
            button.TextureNormal = texture;
            button.Size = new Vector2(TileSize, TileSize);
            button.CustomMinimumSize = new Vector2(TileSize, TileSize);
            button.IgnoreTextureSize = true;
            button.StretchMode = TextureButton.StretchModeEnum.KeepCentered;
            button.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
            button.ToggleMode = true;
            
            // Store ID in metadata
            button.SetMeta("tile_id", id);
            
            // Connect signal
            button.Pressed += () => OnTileButtonPressed(button);
            
            _grid.AddChild(button);
            _buttons.Add(button);
        }
        
        private void OnTileButtonPressed(TextureButton button)
        {
            GD.Print("Palette button pressed!");
            
            // Deselect all other buttons
            foreach (var btn in _buttons)
            {
                if (btn != button)
                {
                    btn.ButtonPressed = false;
                }
            }
            
            // Get tile ID
            int tileId = (int)button.GetMeta("tile_id");
            _selectedIndex = tileId;
            
            GD.Print($"Emitting TileSelected signal with ID: {tileId}");
            
            // Emit signal
            EmitSignal(SignalName.TileSelected, tileId);
        }
        
        public void SelectTile(int tileId)
        {
            foreach (var button in _buttons)
            {
                if ((int)button.GetMeta("tile_id") == tileId)
                {
                    button.ButtonPressed = true;
                    _selectedIndex = tileId;
                }
                else
                {
                    button.ButtonPressed = false;
                }
            }
        }
        
        public int GetSelectedTile()
        {
            return _selectedIndex;
        }
        
        private void Clear()
        {
            if (_grid == null) return;
            
            foreach (var child in _grid.GetChildren())
            {
                child.QueueFree();
            }
            
            _buttons.Clear();
            _selectedIndex = -1;
        }
    }
}

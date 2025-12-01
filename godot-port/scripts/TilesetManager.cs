using Godot;
using System;
using System.Collections.Generic;
using System.IO;

namespace FamidashEditor;

/// <summary>
/// Manages tileset and sprite loading/slicing
/// </summary>
public partial class TilesetManager : GodotObject
{
    private const int TileSize = 16;
    
    public Image? TilesetImage { get; private set; }
    public Image? SpritesetImage { get; private set; }
    public Image? ParallaxImage { get; private set; }
    public Image? GroundImage { get; private set; }
    
    public ImageTexture[]? TileTextures { get; private set; }
    public ImageTexture[]? SpriteTextures { get; private set; }
    
    /// <summary>
    /// Load tileset from file path
    /// </summary>
    public bool LoadTileset(string path)
    {
        if (!File.Exists(path))
        {
            GD.PrintErr($"Tileset not found: {path}");
            return false;
        }
        
        TilesetImage = Image.LoadFromFile(path);
        if (TilesetImage == null)
        {
            GD.PrintErr($"Failed to load tileset: {path}");
            return false;
        }
        
        SliceTileset();
        GD.Print($"Loaded tileset: {path} ({TilesetImage.GetWidth()}x{TilesetImage.GetHeight()})");
        return true;
    }
    
    /// <summary>
    /// Load spriteset from file path
    /// </summary>
    public bool LoadSpriteset(string path)
    {
        if (!File.Exists(path))
        {
            GD.PrintErr($"Spriteset not found: {path}");
            return false;
        }
        
        SpritesetImage = Image.LoadFromFile(path);
        if (SpritesetImage == null)
        {
            GD.PrintErr($"Failed to load spriteset: {path}");
            return false;
        }
        
        SliceSpriteset();
        GD.Print($"Loaded spriteset: {path} ({SpritesetImage.GetWidth()}x{SpritesetImage.GetHeight()})");
        return true;
    }
    
    /// <summary>
    /// Load parallax background
    /// </summary>
    public bool LoadParallax(string path)
    {
        if (!File.Exists(path))
        {
            GD.PrintErr($"Parallax not found: {path}");
            return false;
        }
        
        ParallaxImage = Image.LoadFromFile(path);
        return ParallaxImage != null;
    }
    
    /// <summary>
    /// Load ground layer
    /// </summary>
    public bool LoadGround(string path)
    {
        if (!File.Exists(path))
        {
            GD.PrintErr($"Ground not found: {path}");
            return false;
        }
        
        GroundImage = Image.LoadFromFile(path);
        return GroundImage != null;
    }
    
    /// <summary>
    /// Slice tileset into 16x16 tiles
    /// </summary>
    private void SliceTileset()
    {
        if (TilesetImage == null) return;
        
        int width = TilesetImage.GetWidth();
        int height = TilesetImage.GetHeight();
        int tilesPerRow = width / TileSize;
        int tilesPerCol = height / TileSize;
        int tileCount = tilesPerRow * tilesPerCol;
        
        TileTextures = new ImageTexture[tileCount];
        
        for (int i = 0; i < tileCount; i++)
        {
            int tx = (i % tilesPerRow) * TileSize;
            int ty = (i / tilesPerRow) * TileSize;
            
            var region = new Rect2I(tx, ty, TileSize, TileSize);
            var tileImage = TilesetImage.GetRegion(region);
            TileTextures[i] = ImageTexture.CreateFromImage(tileImage);
        }
        
        GD.Print($"Sliced tileset: {tileCount} tiles ({tilesPerRow}x{tilesPerCol})");
    }
    
    /// <summary>
    /// Slice spriteset into 16x16 sprites
    /// </summary>
    private void SliceSpriteset()
    {
        if (SpritesetImage == null) return;
        
        int width = SpritesetImage.GetWidth();
        int height = SpritesetImage.GetHeight();
        int spritesPerRow = width / TileSize;
        int spritesPerCol = height / TileSize;
        int spriteCount = spritesPerRow * spritesPerCol;
        
        SpriteTextures = new ImageTexture[spriteCount];
        
        for (int i = 0; i < spriteCount; i++)
        {
            int sx = (i % spritesPerRow) * TileSize;
            int sy = (i / spritesPerRow) * TileSize;
            
            var region = new Rect2I(sx, sy, TileSize, TileSize);
            var spriteImage = SpritesetImage.GetRegion(region);
            SpriteTextures[i] = ImageTexture.CreateFromImage(spriteImage);
        }
        
        GD.Print($"Sliced spriteset: {spriteCount} sprites ({spritesPerRow}x{spritesPerCol})");
    }
    
    public ImageTexture? GetTileTexture(int tileId)
    {
        if (TileTextures == null || tileId < 0 || tileId >= TileTextures.Length)
            return null;
        return TileTextures[tileId];
    }
    
    public ImageTexture? GetSpriteTexture(int spriteId)
    {
        if (SpriteTextures == null || spriteId < 0 || spriteId >= SpriteTextures.Length)
            return null;
        return SpriteTextures[spriteId];
    }
}

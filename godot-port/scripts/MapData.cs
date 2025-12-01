using Godot;
using System;
using System.Collections.Generic;

namespace FamidashEditor;

/// <summary>
/// Core map data structure (framework-agnostic)
/// </summary>
public partial class MapData : GodotObject
{
    public int Width { get; set; } = 200;
    public int Height { get; set; } = 27;
    public int[] Tiles { get; set; }
    public int[] Sprites { get; set; }
    
    // Metadata
    public string? TilesetSource { get; set; }
    public string? SpritesetSource { get; set; }
    public string? ParallaxSource { get; set; }
    public string? GroundSource { get; set; }
    
    // Level config
    public string DecoSet { get; set; } = "DECO1";
    public string BlockSet { get; set; } = "BLOCKSA";
    public string SpikeSet { get; set; } = "SPIKESA";
    public bool NoParallaxBg { get; set; } = false;
    public bool LockSpritesToSet { get; set; } = false;
    
    // Tints (RGBA)
    public Color BackgroundTint { get; set; } = new Color(0, 0, 0, 0);
    public Color GroundTint { get; set; } = new Color(0, 0, 0, 0);
    public Color TileTint { get; set; } = new Color(0, 0, 0, 0);
    
    public MapData()
    {
        Tiles = new int[Width * Height];
        Sprites = new int[Width * Height];
        Array.Fill(Sprites, -1); // -1 = empty sprite cell
    }
    
    public MapData(int width, int height) : this()
    {
        Width = width;
        Height = height;
        Tiles = new int[Width * Height];
        Sprites = new int[Width * Height];
        Array.Fill(Sprites, -1);
    }
    
    public void Resize(int newWidth, int newHeight)
    {
        int[] newTiles = new int[newWidth * newHeight];
        int[] newSprites = new int[newWidth * newHeight];
        Array.Fill(newSprites, -1);
        
        // Copy old data
        for (int y = 0; y < Math.Min(Height, newHeight); y++)
        {
            for (int x = 0; x < Math.Min(Width, newWidth); x++)
            {
                int oldIdx = y * Width + x;
                int newIdx = y * newWidth + x;
                newTiles[newIdx] = Tiles[oldIdx];
                newSprites[newIdx] = Sprites[oldIdx];
            }
        }
        
        Width = newWidth;
        Height = newHeight;
        Tiles = newTiles;
        Sprites = newSprites;
    }
    
    public int GetTile(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return 0;
        return Tiles[y * Width + x];
    }
    
    public void SetTile(int x, int y, int tileId)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        Tiles[y * Width + x] = tileId;
    }
    
    public int GetSprite(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return -1;
        return Sprites[y * Width + x];
    }
    
    public void SetSprite(int x, int y, int spriteId)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        Sprites[y * Width + x] = spriteId;
    }
}

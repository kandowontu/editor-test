using System.Collections.Generic;

namespace FamidashEditor.Editor.Models
{
    public class TmxConfig
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
        public string? SelectedSong { get; set; } = null;
        // Sprite offsets: key is "x,y" and value is [offsetX, offsetY]
        public Dictionary<string, int[]>? SpriteOffsets { get; set; } = null;
        // Sprite anchors: key is "x,y" (sprite position) and value is [anchorTileX, anchorTileY]
        public Dictionary<string, int[]>? SpriteAnchors { get; set; } = null;
    }
}

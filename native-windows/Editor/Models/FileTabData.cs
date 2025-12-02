using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace FamidashEditor.Editor.Models
{
    public class FileTabData
    {
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
        public double LoadedGroundOffsetY { get; set; } = 432;
        public bool LoadedGroundRepeatX { get; set; } = true;
        public bool LoadedHasGroundLayer { get; set; }
        public string LoadedDecoSet { get; set; } = "DECO1";
        public string LoadedBlockSet { get; set; } = "BLOCKSA";
        public string LoadedSpikeSet { get; set; } = "SPIKESA";
        public bool NoParallaxBg { get; set; }
        public Color BackgroundTint { get; set; } = Color.FromArgb(0, 0, 0, 0);
        public Color GroundTint { get; set; } = Color.FromArgb(0, 0, 0, 0);
        public Color TileTint { get; set; } = Color.FromArgb(0, 0, 0, 0);
        public string? SelectedSong { get; set; } = null;
    }
}

namespace FamidashEditor;

public class TmxLevel
{
	public int Width { get; set; }

	public int Height { get; set; }

	public int[]? Tiles { get; set; }

	public int[]? Sprites { get; set; }

	public string? TilesetSource { get; set; } = "famidash.bmp";

	public string? SpritesetSource { get; set; } = "sprites.png";

	public bool HasEditorSettings { get; set; } = false;

	public int ChunkWidth { get; set; } = 16;

	public int ChunkHeight { get; set; } = 27;

	public string? ExportTarget { get; set; }

	public string ExportFormat { get; set; } = "csv";

	public string? ParallaxSource { get; set; }

	public double ParallaxX { get; set; } = 0.9;

	public double ParallaxY { get; set; } = 0.9;

	public bool ParallaxRepeatX { get; set; } = true;

	public bool ParallaxRepeatY { get; set; } = true;

	public bool HasParallaxLayer { get; set; } = false;

	public string? DecoSet { get; set; } = "deco1";

	public string? GroundSource { get; set; }

	public double GroundOffsetY { get; set; } = 432.0;

	public bool GroundRepeatX { get; set; } = true;

	public bool HasGroundLayer { get; set; } = false;

	public string? LoadCollisionMessages { get; set; }

	public string? SaveCollisionMessages { get; set; }
}

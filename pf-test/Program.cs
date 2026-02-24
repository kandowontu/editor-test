using System;
using System.Collections.Generic;
using System.IO;
using FamidashEditor;

// Quick console harness to run PathfinderEngine on a TMX file.
// Usage: dotnet run -- <path-to-tmx> [maxFallSpeed] [startingSpeed]

string tmxPath = args.Length > 0 ? args[0] : @"..\famidash\EXPORTS\everyend.tmx";
int maxFallSpeed = args.Length > 1 ? int.Parse(args[1]) : 0x07;
int startSpeedUiIndex = args.Length > 2 ? int.Parse(args[2]) : 1; // 1 = 1x speed

if (!File.Exists(tmxPath))
{
    Console.Error.WriteLine($"TMX not found: {tmxPath}");
    return 1;
}

Console.WriteLine($"Loading {tmxPath}...");
var level = TmxHandler.LoadTmx(tmxPath);
Console.WriteLine($"Map: {level.Width}x{level.Height}  tiles={level.Tiles?.Length}  sprites={level.Sprites?.Length}");

int[] tiles = level.Tiles ?? Array.Empty<int>();
int[] sprites = level.Sprites ?? Array.Empty<int>();
var spriteAnchors = new Dictionary<int, (int, int)>();
var spritePixelOffsets = new Dictionary<int, (int, int)>();

// Ground layer: the GUI always loads a ground bitmap (3 tile rows reserved)
bool hasGround = true;
int groundTileRows = 3;

// Default start position: on the ground at X=0
int groundRowsToReserve = (hasGround && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
int groundSurface_px = (level.Height - groundRowsToReserve) * 16;
int startX_px = 0;
int startY_px = Math.Max(0, groundSurface_px - 15); // 15 = cube hitbox height
int maxY = Math.Max(0, level.Height * 16 - 16);
if (startY_px > maxY) startY_px = maxY;

Console.WriteLine($"Start: ({startX_px}, {startY_px})  speed={startSpeedUiIndex}  maxFall=0x{maxFallSpeed:X}");

var engine = new PathfinderEngine(
    tiles, sprites, spriteAnchors,
    level.Width, level.Height,
    hasGround, groundTileRows,
    maxFallSpeed,
    spritePixelOffsets);

engine.Progress = new Progress<int>(pct =>
{
    Console.Write($"\r  Progress: {pct}%   ");
});

var sw = System.Diagnostics.Stopwatch.StartNew();
engine.Run(startX_px, startY_px, startSpeedUiIndex, 0, false, false);
sw.Stop();

Console.WriteLine();
Console.WriteLine($"Result: {(engine.Success ? "SUCCESS" : "FAILED")}");
Console.WriteLine($"Message: {engine.ResultMessage}");
Console.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"Path points: {engine.PathPoints.Count}");
Console.WriteLine($"Trace: {engine.FrameTracePath}");

return engine.Success ? 0 : 1;

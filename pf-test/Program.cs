using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FamidashEditor;

// Quick console harness to run PathfinderEngine on a TMX file.
// Usage: dotnet run -- <path-to-tmx> [maxFallSpeed] [startingSpeed] [jumpTimingBias] [--coins]
//   jumpTimingBias: 0.0=earliest, 0.25=early, 0.5=middle, 0.75=late, 1.0=latest
//   --coins: enable coin collection mode (pathfinder seeks coins)
//
// Sprite offsets are loaded from (in priority order):
//   1. Per-level editor config: Documents/Famidash Editor/<level>.tmx.cfg
//   2. lvlset_HUGE_metadata.json5 (searched in workspace root and parent dirs)

// Parse --coins flag (can appear anywhere in args)
bool preferCoins = args.Any(a => a.Equals("--coins", StringComparison.OrdinalIgnoreCase));
bool useBfs = args.Any(a => a.Equals("--bfs", StringComparison.OrdinalIgnoreCase));
bool verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("-v", StringComparison.OrdinalIgnoreCase));
// Filter out named flags before positional parsing
var positionalArgs = args.Where(a => !a.StartsWith("--") && !a.Equals("-v", StringComparison.OrdinalIgnoreCase)).ToArray();

string tmxPath = positionalArgs.Length > 0 ? positionalArgs[0] : @"..\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx";
int maxFallSpeed = positionalArgs.Length > 1 ? int.Parse(positionalArgs[1]) : 0x06;
int startSpeedUiIndex = positionalArgs.Length > 2 ? int.Parse(positionalArgs[2]) : 1; // 1 = 1x speed
double jumpTimingBias = positionalArgs.Length > 3 ? double.Parse(positionalArgs[3]) : 0.5; // default middle

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
int mapWidth = level.Width;
var spriteAnchors = new Dictionary<int, (int, int)>();
var spritePixelOffsets = new Dictionary<int, (int, int)>();
bool gotOffsetsFromConfig = false;

// ──────────────────────────────────────────────────────────────
// 1) Load editor config (Documents/Famidash Editor/<filename>.tmx.cfg)
// ──────────────────────────────────────────────────────────────
string cfgFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Famidash Editor");
string cfgPath = Path.Combine(cfgFolder, Path.GetFileName(tmxPath) + ".cfg");
if (File.Exists(cfgPath))
{
    try
    {
        var cfgJson = JsonDocument.Parse(File.ReadAllText(cfgPath));
        var root = cfgJson.RootElement;

        // Load SpriteOffsets: key "x,y" -> [offsetX, offsetY]
        if (root.TryGetProperty("SpriteOffsets", out var offsetsProp) && offsetsProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var kvp in offsetsProp.EnumerateObject())
            {
                var parts = kvp.Name.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                {
                    int posIdx = y * mapWidth + x;
                    var arr = kvp.Value;
                    if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() >= 2)
                        spritePixelOffsets[posIdx] = (arr[0].GetInt32(), arr[1].GetInt32());
                }
            }
            if (spritePixelOffsets.Count > 0)
                gotOffsetsFromConfig = true;
        }

        // Load SpriteAnchors: key "x,y" -> [anchorTileX, anchorTileY]
        if (root.TryGetProperty("SpriteAnchors", out var anchorsProp) && anchorsProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var kvp in anchorsProp.EnumerateObject())
            {
                var parts = kvp.Name.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                {
                    int posIdx = y * mapWidth + x;
                    var arr = kvp.Value;
                    if (arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() >= 2)
                        spriteAnchors[posIdx] = (arr[0].GetInt32(), arr[1].GetInt32());
                }
            }
        }

        // Load MaxFallSpeed from config if not explicitly provided on command line
        if (positionalArgs.Length <= 1 && root.TryGetProperty("MaxFallSpeed", out var mfsProp) && mfsProp.ValueKind == JsonValueKind.Number)
            maxFallSpeed = mfsProp.GetInt32();

        Console.WriteLine($"Config: {cfgPath} ({spritePixelOffsets.Count} offsets, {spriteAnchors.Count} anchors)");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Warning: failed to read config {cfgPath}: {ex.Message}");
    }
}

// ──────────────────────────────────────────────────────────────
// 2) Fallback: load objectOffsets from lvlset_HUGE_metadata.json5
// ──────────────────────────────────────────────────────────────
if (!gotOffsetsFromConfig)
{
    string levelName = Path.GetFileNameWithoutExtension(tmxPath).ToLowerInvariant();
    string? metaPath = FindMetadataFile(tmxPath);
    if (metaPath != null)
    {
        try
        {
            var metaOffsets = ParseMetadataOffsets(metaPath, levelName, mapWidth);
            if (metaOffsets.Count > 0)
            {
                spritePixelOffsets = metaOffsets;
                Console.WriteLine($"Metadata: {metaPath} ({spritePixelOffsets.Count} offsets for '{levelName}')");
            }
            else
            {
                Console.WriteLine($"Metadata: no objectOffsets for '{levelName}'");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: failed to parse metadata {metaPath}: {ex.Message}");
        }
    }
    else
    {
        Console.WriteLine($"No config or metadata found for sprite offsets");
    }
}
// globalObjectOffsets (bottom pad +8Y, medium post -8X) are now baked into
// SharedPhysics.sprite_y_offset / sprite_x_offset tables, matching SimulatorWindow.

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

Console.WriteLine($"Start: ({startX_px}, {startY_px})  speed={startSpeedUiIndex}  maxFall=0x{maxFallSpeed:X}  bias={jumpTimingBias:F2}");

var engine = new PathfinderEngine(
    tiles, sprites, spriteAnchors,
    mapWidth, level.Height,
    hasGround, groundTileRows,
    maxFallSpeed,
    spritePixelOffsets);
engine.JumpTimingBias = jumpTimingBias;
engine.PreferCoins = preferCoins;
engine.UseBFS = useBfs || preferCoins; // Editor: UseBFS = preferCoins (BFS collects all coins in a single pass)
engine.Verbose = verbose;

if (preferCoins)
    Console.WriteLine("Coin collection mode ENABLED");
if (useBfs)
    Console.WriteLine("BFS exploration mode ENABLED");

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

// ══════════════════════════════════════════════════════════════
// Helper: find lvlset_HUGE_metadata.json5 by searching upward from TMX
// ══════════════════════════════════════════════════════════════
static string? FindMetadataFile(string tmxPath)
{
    // Search from the TMX directory upward, then also check working directory ancestors
    var searchRoots = new List<string>();
    string? tmxDir = Path.GetDirectoryName(Path.GetFullPath(tmxPath));
    if (tmxDir != null) searchRoots.Add(tmxDir);
    searchRoots.Add(Directory.GetCurrentDirectory());

    foreach (var startDir in searchRoots)
    {
        string? dir = startDir;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "lvlset_HUGE_metadata.json5");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName;
        }
    }
    return null;
}

// ══════════════════════════════════════════════════════════════
// Helper: strip JSON5 features to standard JSON
// ══════════════════════════════════════════════════════════════
static string Json5ToJson(string json5)
{
    // 1. Remove single-line comments (// ...) but not inside strings
    var sb = new System.Text.StringBuilder(json5.Length);
    bool inString = false;
    char stringChar = '\0';
    for (int i = 0; i < json5.Length; i++)
    {
        char c = json5[i];
        if (inString)
        {
            sb.Append(c);
            if (c == '\\' && i + 1 < json5.Length)
            {
                sb.Append(json5[++i]); // skip escaped char
            }
            else if (c == stringChar)
            {
                inString = false;
            }
        }
        else if (c == '"' || c == '\'')
        {
            inString = true;
            stringChar = c;
            sb.Append(c);
        }
        else if (c == '/' && i + 1 < json5.Length && json5[i + 1] == '/')
        {
            // Skip until newline
            while (i < json5.Length && json5[i] != '\n') i++;
            if (i < json5.Length) sb.Append('\n');
        }
        else if (c == '/' && i + 1 < json5.Length && json5[i + 1] == '*')
        {
            // Skip block comment
            i += 2;
            while (i + 1 < json5.Length && !(json5[i] == '*' && json5[i + 1] == '/')) i++;
            i++; // skip closing /
        }
        else
        {
            sb.Append(c);
        }
    }
    string s = sb.ToString();

    // 2. Convert hex literals (0x1A) to decimal
    s = Regex.Replace(s, @"0x([0-9A-Fa-f]+)", m => Convert.ToInt32(m.Groups[1].Value, 16).ToString());

    // 3. Quote unquoted keys: word characters before a colon
    s = Regex.Replace(s, @"(?<=[\{\,\n]\s*)([a-zA-Z_]\w*)\s*:", "\"$1\":");

    // 4. Remove trailing commas before } or ]
    s = Regex.Replace(s, @",(\s*[\}\]])", "$1");

    // 5. Handle +N (positive sign prefix) — just remove the +
    s = Regex.Replace(s, @"(?<=:\s*)\+(\d)", "$1");

    return s;
}

// ══════════════════════════════════════════════════════════════
// Helper: parse objectOffsets for a specific level from metadata
// Matches the editor's ApplySpriteOffsets behavior (last entry wins)
// ══════════════════════════════════════════════════════════════
static Dictionary<int, (int, int)> ParseMetadataOffsets(string metaPath, string levelName, int mapWidth)
{
    var offsets = new Dictionary<int, (int, int)>();
    string raw = File.ReadAllText(metaPath);
    string json = Json5ToJson(raw);
    
    using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
    var root = doc.RootElement;

    JsonElement levelsArray;
    if (root.TryGetProperty("official_levels", out levelsArray) && levelsArray.ValueKind == JsonValueKind.Array)
    {
        foreach (var entry in levelsArray.EnumerateArray())
        {
            if (entry.TryGetProperty("level", out var lvlProp)
                && lvlProp.GetString()?.Equals(levelName, StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!entry.TryGetProperty("objectOffsets", out var ooArray)
                    || ooArray.ValueKind != JsonValueKind.Array)
                    break;

                // Process each offset entry (same logic as editor's ApplySpriteOffsets)
                foreach (var ooEntry in ooArray.EnumerateArray())
                {
                    int ox = 0, oy = 0;
                    if (ooEntry.TryGetProperty("offsetX", out var oxProp)) ox = oxProp.GetInt32();
                    if (ooEntry.TryGetProperty("offsetY", out var oyProp)) oy = oyProp.GetInt32();
                    if (ox == 0 && oy == 0) continue;

                    if (!ooEntry.TryGetProperty("coordinates", out var coords)) continue;

                    if (coords.ValueKind == JsonValueKind.Array && coords.GetArrayLength() > 0)
                    {
                        var first = coords[0];
                        if (first.ValueKind == JsonValueKind.Array)
                        {
                            // Nested: [[x,y], [x,y], ...]
                            foreach (var coord in coords.EnumerateArray())
                            {
                                if (coord.ValueKind == JsonValueKind.Array && coord.GetArrayLength() >= 2)
                                {
                                    int x = coord[0].GetInt32();
                                    int y = coord[1].GetInt32();
                                    int key = y * mapWidth + x;
                                    offsets[key] = (ox, oy);
                                }
                            }
                        }
                        else if (first.ValueKind == JsonValueKind.Number)
                        {
                            // Single: [x, y]
                            if (coords.GetArrayLength() >= 2)
                            {
                                int x = coords[0].GetInt32();
                                int y = coords[1].GetInt32();
                                int key = y * mapWidth + x;
                                offsets[key] = (ox, oy);
                            }
                        }
                    }
                }
                break; // Found the level
            }
        }
    }
    return offsets;
}

// ══════════════════════════════════════════════════════════════
// Helper: apply globalObjectOffsets from metadata root
// These are sprite-ID-based offsets that apply to ALL instances
// of matching sprite types across all levels (e.g. +8Y for bottom pads).
// Only applies to positions that don't already have a per-level offset.
// ══════════════════════════════════════════════════════════════
static int ApplyGlobalObjectOffsets(string metaPath, int[] sprites, int mapWidth,
    Dictionary<int, (int, int)> offsets)
{
    string raw = File.ReadAllText(metaPath);
    string json = Json5ToJson(raw);

    using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
    var root = doc.RootElement;

    if (!root.TryGetProperty("globalObjectOffsets", out var gooArray)
        || gooArray.ValueKind != JsonValueKind.Array)
        return 0;

    int applied = 0;
    foreach (var entry in gooArray.EnumerateArray())
    {
        int ox = 0, oy = 0;
        if (entry.TryGetProperty("offsetX", out var oxProp)) ox = oxProp.GetInt32();
        if (entry.TryGetProperty("offsetY", out var oyProp)) oy = oyProp.GetInt32();
        if (entry.TryGetProperty("offset", out var offProp) && offProp.ValueKind == JsonValueKind.Array && offProp.GetArrayLength() >= 2)
        {
            ox = offProp[0].GetInt32();
            oy = offProp[1].GetInt32();
        }
        if (ox == 0 && oy == 0) continue;

        // Collect matching object IDs
        var matchIds = new HashSet<int>();
        if (entry.TryGetProperty("objectID", out var oidProp))
        {
            if (oidProp.ValueKind == JsonValueKind.Number)
                matchIds.Add(oidProp.GetInt32());
            else if (oidProp.ValueKind == JsonValueKind.Array)
                foreach (var id in oidProp.EnumerateArray())
                    if (id.ValueKind == JsonValueKind.Number)
                        matchIds.Add(id.GetInt32());
        }
        if (matchIds.Count == 0) continue;

        bool isOverride = false;
        if (entry.TryGetProperty("override", out var ovProp) && ovProp.ValueKind == JsonValueKind.True)
            isOverride = true;

        // Apply to every sprite instance that matches
        for (int idx = 0; idx < sprites.Length; idx++)
        {
            int sid = sprites[idx] & 0xFF;
            if (sid == 0 || !matchIds.Contains(sid)) continue;

            if (!isOverride && offsets.ContainsKey(idx)) continue; // per-level offset takes priority

            // Merge with existing offset if override
            if (offsets.TryGetValue(idx, out var existing))
                offsets[idx] = (existing.Item1 + ox, existing.Item2 + oy);
            else
                offsets[idx] = (ox, oy);
            applied++;
        }
    }
    return applied;
}

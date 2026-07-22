using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FamidashEditor;

// Quick console harness to run PathfinderEngine on a TMX file.
// Usage: dotnet run -- <path-to-tmx> [jumpTimingBias] [--coins]
//   jumpTimingBias: 0.0=earliest, 0.25=early, 0.5=middle, 0.75=late, 1.0=latest
//   --coins: enable coin collection mode (pathfinder seeks coins)
//   startingSpeed and maxFallSpeed are read from lvlset_HUGE_metadata.json5 automatically
//
// Sprite offsets are loaded from (in priority order):
//   1. Per-level editor config: Documents/Famidash Editor/<level>.tmx.cfg
//   2. lvlset_HUGE_metadata.json5 (searched in workspace root and parent dirs)

// Parse --coins flag (can appear anywhere in args)
bool preferCoins = args.Any(a => a.Equals("--coins", StringComparison.OrdinalIgnoreCase));
bool useBfs = args.Any(a => a.Equals("--bfs", StringComparison.OrdinalIgnoreCase));
string? probeArg = args.FirstOrDefault(a => a.StartsWith("--probe", StringComparison.OrdinalIgnoreCase));
bool useProbe = probeArg != null;
string? tasInputFile = null;
string? bfsPrefixTrace = null;
int bfsPrefixFrames = int.MaxValue;
int tasPreRollFrames = 60; // default: TAS starts at 1 second (60 frames) into gameplay
foreach (var a in args)
{
    if (a.StartsWith("--tas=", StringComparison.OrdinalIgnoreCase))
        tasInputFile = a.Substring(6);
    else if (a.StartsWith("--bfs-prefix=", StringComparison.OrdinalIgnoreCase))
        bfsPrefixTrace = a.Substring(13);
    else if (a.StartsWith("--bfs-prefix-frames=", StringComparison.OrdinalIgnoreCase))
        bfsPrefixFrames = int.Parse(a.Substring(20));
    else if (a.StartsWith("--tas-preroll=", StringComparison.OrdinalIgnoreCase))
        tasPreRollFrames = int.Parse(a.Substring(14));
}
bool verbose = args.Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("-v", StringComparison.OrdinalIgnoreCase));
TextWriter originalOut = Console.Out;
if (!verbose)
    Console.SetOut(TextWriter.Null);
// Filter out named flags before positional parsing
var positionalArgs = args.Where(a => !a.StartsWith("--") && !a.Equals("-v", StringComparison.OrdinalIgnoreCase)).ToArray();
int? cliStartMode = null;
int? cliSpawnYLo = null;
foreach (var a in args)
{
    if (a.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))
        cliStartMode = int.Parse(a.Substring(7));
    else if (a.StartsWith("--spawn-y-lo=", StringComparison.OrdinalIgnoreCase))
    {
        string value = a.Substring(13);
        cliSpawnYLo = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(value.Substring(2), 16)
            : int.Parse(value);
    }
}

string tmxPath = positionalArgs.Length > 0 ? positionalArgs[0] : @"..\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx";
double jumpTimingBias = positionalArgs.Length > 1 ? double.Parse(positionalArgs[1]) : 0.5; // default middle

// Read startingSpeed and maxFallSpeed from lvlset_HUGE_metadata.json5 for this level
int maxFallSpeed = 0x06; // default
int startSpeedUiIndex = 1;  // default: 1 = 1x speed (index 0 = 0.5x)
int startGameMode = 0; // default: cube mode
int? metaSpawnYHi = null, metaSpawnYLo = null;
int? metaScrollYHi = null, metaScrollYLo = null;
{
    string lvlName = Path.GetFileNameWithoutExtension(tmxPath).ToLowerInvariant();
    string? metaFile = FindMetadataFile(tmxPath);
    Console.WriteLine($"Metadata file: {metaFile ?? "NOT FOUND"} (level: {lvlName})");
    if (metaFile != null)
    {
        try
        {
        var (metaSpeed, metaMaxFall, metaGameMode, mSpawnHi, mSpawnLo, mScrollHi, mScrollLo) = ParseMetadataLevelProperties(metaFile, lvlName);
        if (metaSpeed.HasValue)
        {
            // NES/metadata convention: 0=1x, 1=0.5x, 2+=same
            // UI convention: 0=0.5x, 1=1x, 2+=same
            // Swap 0 and 1 to convert metadata→UI index
            startSpeedUiIndex = (metaSpeed.Value == 1) ? 0 : (metaSpeed.Value == 0) ? 1 : metaSpeed.Value;
            Console.WriteLine($"Metadata: startingSpeed={metaSpeed.Value} (uiIndex={startSpeedUiIndex}) for '{lvlName}'");
        }
        if (metaMaxFall.HasValue)
        {
            maxFallSpeed = metaMaxFall.Value;
            Console.WriteLine($"Metadata: maxFallSpeed=0x{metaMaxFall.Value:X} for '{lvlName}'");
        }
        if (metaGameMode.HasValue)
        {
            startGameMode = metaGameMode.Value;
            Console.WriteLine($"Metadata: startingGameMode={startGameMode} for '{lvlName}'");
        }
        metaSpawnYHi = mSpawnHi;
        metaSpawnYLo = mSpawnLo;
        metaScrollYHi = mScrollHi;
        metaScrollYLo = mScrollLo;
        if (mSpawnHi.HasValue)
            Console.WriteLine($"Metadata: spawnY=0x{mSpawnHi.Value:X2}{(mSpawnLo ?? 0):X2} for '{lvlName}'");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Metadata parse error: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
// CLI override for start game mode
if (cliStartMode.HasValue)
{
    startGameMode = cliStartMode.Value;
    Console.WriteLine($"CLI override: startGameMode={startGameMode}");
}

if (!File.Exists(tmxPath))
{
    if (!verbose)
        Console.SetOut(originalOut);
    Console.Error.WriteLine($"TMX not found: {tmxPath}");
    return 1;
}

Console.WriteLine($"Loading {tmxPath}...");
var level = TmxHandler.LoadTmx(tmxPath);
Console.WriteLine($"Map: {level.Width}x{level.Height}  tiles={level.Tiles?.Length}  sprites={level.Sprites?.Length}");

int[] tiles = level.Tiles ?? Array.Empty<int>();
int[] sprites = level.Sprites ?? Array.Empty<int>();
int mapWidth = level.Width;

if (useProbe)
{
    int mapH = level.Height;
    int groundRows = level.HasGroundLayer ? 3 : 0;
    Console.WriteLine($"groundRows={groundRows} hasGround={level.HasGroundLayer}");
    var map = new SharedPhysics.CollisionMap(tiles, mapWidth, mapH, groundRows);
    int playerX = 29832, playerY = 519;
    if (probeArg != null && probeArg.StartsWith("--probe=", StringComparison.OrdinalIgnoreCase))
    {
        string[] probeParts = probeArg.Substring(8).Split(',');
        if (probeParts.Length == 2)
        {
            playerX = int.Parse(probeParts[0]);
            playerY = int.Parse(probeParts[1]);
        }
    }
    int rowBottomY = playerY + 15 - 2;
    int rowTopY = playerY + 2;
    int leftX = playerX + 3;
    int rightX = playerX + 15 - 3;
    void Probe(string name, int x, int y)
    {
        int tileX = x / 16;
        int tileY = y / 16;
        int arrY = tileY + groundRows;
        int idx = arrY * mapWidth + tileX;
        int tid = (idx >= 0 && idx < tiles.Length) ? tiles[idx] : -999;
        int mapped = SharedPhysics.MapTileForCollision(tid);
        var col = MetatileCollisionTable.GetCollision((byte)mapped);
        int lx = ((x % 16) + 16) % 16;
        int ly = ((y % 16) + 16) % 16;
        bool kills = MetatileCollisionTable.TileKillsAtPixel(col, lx, ly);
        bool fullKill = SharedPhysics.PointKillsPlayer(map, x, y);
        Console.WriteLine($"  [{name}] world=({x},{y}) tile=({tileX},{arrY}) tid={tid} mapped={mapped} col={col} local=({lx},{ly}) KILLS={kills} fullKill={fullKill}");
    }
    Probe("BL", leftX, rowBottomY);
    Probe("BR", rightX, rowBottomY);
    Probe("TL", leftX, rowTopY);
    Probe("TR", rightX, rowTopY);
    bool fs = SharedPhysics.CheckFloorSpikes(map, playerX, playerY, 15, 15, false, out int dx, out int dy);
    Console.WriteLine($"CheckFloorSpikes: {fs} at ({dx},{dy})");
    return 0;
}
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

        // Load MaxFallSpeed from config if present (overrides metadata)
        if (root.TryGetProperty("MaxFallSpeed", out var mfsProp) && mfsProp.ValueKind == JsonValueKind.Number)
            maxFallSpeed = mfsProp.GetInt32();
        // Match the editor's per-level startup settings. These must override
        // metadata too; otherwise the standalone runner can report a pass while
        // the editor starts from a different spawn/camera state.
        if (root.TryGetProperty("StartingSpeed", out var ssProp) && ssProp.ValueKind == JsonValueKind.Number)
        {
            int metadataSpeed = ssProp.GetInt32();
            startSpeedUiIndex = metadataSpeed == 1 ? 0 : metadataSpeed == 0 ? 1 : metadataSpeed;
        }
        if (root.TryGetProperty("StartingGameMode", out var gmProp) && gmProp.ValueKind == JsonValueKind.Number)
            startGameMode = gmProp.GetInt32();
        if (root.TryGetProperty("SpawnYPositionHi", out var spawnHiProp) && spawnHiProp.ValueKind == JsonValueKind.Number)
            metaSpawnYHi = spawnHiProp.GetInt32();
        if (root.TryGetProperty("SpawnYPositionLow", out var spawnLoProp) && spawnLoProp.ValueKind == JsonValueKind.Number)
            metaSpawnYLo = spawnLoProp.GetInt32();
        if (root.TryGetProperty("ScrollYPositionHi", out var scrollHiProp) && scrollHiProp.ValueKind == JsonValueKind.Number)
            metaScrollYHi = scrollHiProp.GetInt32();
        if (root.TryGetProperty("ScrollYPositionLow", out var scrollLoProp) && scrollLoProp.ValueKind == JsonValueKind.Number)
            metaScrollYLo = scrollLoProp.GetInt32();

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
// Build the current NES stream from the raw SP layer and current offsets.
// Generated records beside the source level may belong to an older export.

// Ground layer: the GUI always loads a ground bitmap (3 tile rows reserved)
bool hasGround = true;
int groundTileRows = 3;

// Default start position: match NES exactly.
// NES initializes currplayer_y = spawn_y_pos (screen-relative 8.8 fixed) and
// scroll_y from spawn_scroll_y_pos.  In PF coords:
//   PF_startY_top = NES_screen_Y_top + NES_scroll_y_linear - nesYOffset
// where:
//   NES_screen_Y_top = spawnYPositionHi (defaults to 0xB0 in export_levels.py)
//   NES_scroll_y_linear = scrollYPositionHi*240 + scrollYPositionLow
//                          (defaults 0x02/0xEF -> 719 in export_levels.py)
//   nesYOffset = (57 - mapHeight + groundRowsToReserve) * 16  (PathfinderEngine convention)
int groundRowsToReserve = (hasGround && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
int nesYOffset = (57 - level.Height + groundRowsToReserve) * 16;
int nesSpawnHi = (metaSpawnYHi ?? 0xB0) & 0xFF;
(int nesScrollHi, int nesScrollLo) = SharedPhysics.ResolveNesInitialScroll(metaScrollYHi, metaScrollYLo);
int nesScrollLinear = nesScrollHi * 240 + nesScrollLo;
int startX_px = 0;
int startY_px = nesSpawnHi + nesScrollLinear - nesYOffset;
int maxY = Math.Max(0, level.Height * 16 - 16);
if (startY_px < 0) startY_px = 0;
if (startY_px > maxY) startY_px = maxY;
Console.WriteLine($"Spawn: spawnHi=0x{nesSpawnHi:X2} scrollHi=0x{nesScrollHi:X2} scrollLo=0x{nesScrollLo:X2} scrollLin={nesScrollLinear} nesYOffset={nesYOffset} -> startY_px={startY_px}");

Console.WriteLine($"Start: ({startX_px}, {startY_px})  speed={startSpeedUiIndex}  maxFall=0x{maxFallSpeed:X}  bias={jumpTimingBias:F2}  mode={startGameMode}");

NesSpriteRecord[]? nesSpriteRecords = NesSpriteDataLoader.BuildRuntimeRecords(
    level.NesSpriteLayer ?? sprites,
    mapWidth,
    level.Height,
    spritePixelOffsets);

var engine = new PathfinderEngine(
    tiles, sprites, spriteAnchors,
    mapWidth, level.Height,
    hasGround, groundTileRows,
    maxFallSpeed,
    spritePixelOffsets,
    level.NesSpriteLayer,
    nesSpriteRecords);
engine.LevelName = tmxPath;
engine.JumpTimingBias = jumpTimingBias;
engine.PreferCoins = preferCoins;
engine.UseBFS = useBfs || preferCoins; // Editor: UseBFS = preferCoins (BFS collects all coins in a single pass)
engine.Verbose = verbose;
engine.ConfigScrollYHi = metaScrollYHi;
engine.ConfigScrollYLo = metaScrollYLo;
engine.ConfigSpawnYLo = cliSpawnYLo ?? metaSpawnYLo;
engine.UseNesSpawnScrollDefaults = true;
if (bfsPrefixTrace != null)
{
    engine.DebugBfsPrefixInputs = File.ReadLines(bfsPrefixTrace)
        .Skip(1)
        .Select(line => line.Split(','))
        .Where(fields => fields.Length > 4 && int.TryParse(fields[0], out _))
        .Select(fields => fields[4] == "1")
        .Take(bfsPrefixFrames)
        .ToList();
}

if (preferCoins)
    Console.WriteLine("Coin collection mode ENABLED");
if (useBfs)
    Console.WriteLine("BFS exploration mode ENABLED");

engine.Progress = new Progress<int>(pct =>
{
    originalOut.Write($"\r  Progress: {pct}%   ");
});

var sw = System.Diagnostics.Stopwatch.StartNew();

// TAS replay mode
if (tasInputFile != null)
{
    var tasLines = File.ReadAllLines(tasInputFile);
    const string IDLE = "|..|........|........";
    var tasInputs = new List<bool>(tasLines.Length);
    if (tasLines.Length > 0 && tasLines[0].StartsWith("emulator_frame,", StringComparison.Ordinal))
    {
        // Accept the full Mesen proof-capture format.  Select the attempt that
        // travelled farthest, discard the frozen level-start rows, and begin
        // with the input consumed by the frame after the intro pre-step (the
        // engine has already applied that pre-step before replay begins).
        string[] header = tasLines[0].Split(',');
        int attemptCol = Array.IndexOf(header, "attempt");
        int runFrameCol = Array.IndexOf(header, "run_frame");
        int worldXCol = Array.IndexOf(header, "p1_world_x");
        int joyHoldCol = Array.IndexOf(header, "joy1_hold");
        if (attemptCol < 0 || runFrameCol < 0 || worldXCol < 0 || joyHoldCol < 0)
            throw new InvalidDataException("Mesen capture is missing required columns.");

        var captureRows = tasLines.Skip(1)
            .Select(line => line.Split(','))
            .Where(fields => fields.Length > joyHoldCol &&
                int.TryParse(fields[attemptCol], out _) &&
                long.TryParse(fields[worldXCol], out long x) && x >= 0 && x < 100000)
            .ToList();
        int bestAttempt = captureRows
            .GroupBy(fields => int.Parse(fields[attemptCol]))
            .OrderByDescending(group => group.Max(fields => long.Parse(fields[worldXCol])))
            .ThenByDescending(group => group.Count())
            .First().Key;
        captureRows = captureRows
            .Where(fields => int.Parse(fields[attemptCol]) == bestAttempt)
            .OrderBy(fields => int.Parse(fields[runFrameCol]))
            .ToList();
        int firstMoved = captureRows.FindIndex(fields => long.Parse(fields[worldXCol]) > 8);
        int firstInputRow = Math.Max(0, firstMoved + 1);
        foreach (string[] fields in captureRows.Skip(firstInputRow))
        {
            int consumedPad = int.Parse(fields[joyHoldCol]);
            tasInputs.Add((consumedPad & (0x80 | 0x08)) != 0);
        }
        Console.WriteLine($"Mesen capture: attempt={bestAttempt}, firstInputRow={firstInputRow}, inputs={tasInputs.Count}");
    }
    else if (tasLines.Length > 0 && tasLines[0].StartsWith("frame,X_fixed,", StringComparison.Ordinal))
    {
        // Accept a Pathfinder frame trace directly so a reported divergence can
        // be replayed byte-for-byte without first converting it to FCEUX TAS text.
        foreach (var line in tasLines.Skip(1))
        {
            var fields = line.Split(',');
            if (fields.Length > 4 && int.TryParse(fields[0], out _))
                tasInputs.Add(fields[4] == "1");
        }
    }
    else
    {
        foreach (var line in tasLines)
            tasInputs.Add(line != IDLE);
    }

    Console.WriteLine($"TAS replay: {tasInputs.Count} frames, preroll={tasPreRollFrames}");
    Console.WriteLine($"Jump frames: {tasInputs.Count(b => b)}");
    engine.ReplayInputSequence(startX_px, startY_px, startSpeedUiIndex, startGameMode, false, false,
        tasInputs, tasPreRollFrames, Console.Out);
    sw.Stop();
    if (!verbose)
        Console.SetOut(originalOut);
    Console.Error.WriteLine($"TAS replay done in {sw.Elapsed.TotalSeconds:F1}s");
    return 0;
}

TextWriter? originalErr = null;
bool preserveDiagnosticError = Environment.GetEnvironmentVariable(
    "FAMIDASH_ROUTE_DIAG") == "1";
if (!verbose && !preserveDiagnosticError)
{
	originalErr = Console.Error;
	Console.SetError(TextWriter.Null);
}

engine.Run(startX_px, startY_px, startSpeedUiIndex, startGameMode, false, false);

if (originalErr != null)
    Console.SetError(originalErr);
sw.Stop();

if (!verbose)
    Console.SetOut(originalOut);

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
            // Also check metadata/ subdirectory
            string metaSubDir = Path.Combine(dir, "metadata", "lvlset_HUGE_metadata.json5");
            if (File.Exists(metaSubDir)) return metaSubDir;
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
// Helper: read startingSpeed and maxFallSpeed for a level from metadata
// ══════════════════════════════════════════════════════════════
static (int? startingSpeed, int? maxFallSpeed, int? startingGameMode, int? spawnYHi, int? spawnYLo, int? scrollYHi, int? scrollYLo) ParseMetadataLevelProperties(string metaPath, string levelName)
{
    string raw = File.ReadAllText(metaPath);
    string json = Json5ToJson(raw);

    using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
    var root = doc.RootElement;

    // Search both official_levels and community_levels arrays
    foreach (var arrayName in new[] { "official_levels", "community_levels" })
    {
        if (root.TryGetProperty(arrayName, out var levelsArray) && levelsArray.ValueKind == JsonValueKind.Array)
        {
            int count = 0;
            foreach (var entry in levelsArray.EnumerateArray())
            {
                count++;
                if (entry.TryGetProperty("level", out var lvlProp))
                {
                    string? lvl = lvlProp.GetString();
                    if (lvl?.Equals(levelName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        int? speed = null, gameMode = null, spawnHi = null, spawnLo = null, scrollHi = null, scrollLo = null;
                        int? maxFall = 0x06;
                        if (entry.TryGetProperty("startingSpeed", out var sp) && sp.ValueKind == JsonValueKind.Number)
                            speed = sp.GetInt32();
                        // Current source metadata uses a boolean-like numeric flag.
                        // Only exactly 1 selects 0x07; zero, absent, or any other
                        // value uses the NES default 0x06.
                        if (entry.TryGetProperty("maxFallSpeed_is_7", out var mf7)
                            && mf7.ValueKind == JsonValueKind.Number
                            && mf7.GetInt32() == 1)
                            maxFall = 0x07;
                        if (entry.TryGetProperty("startingGameMode", out var gm) && gm.ValueKind == JsonValueKind.Number)
                            gameMode = gm.GetInt32();
                        if (entry.TryGetProperty("spawnYPositionHi", out var syh) && syh.ValueKind == JsonValueKind.Number)
                            spawnHi = syh.GetInt32();
                        if (entry.TryGetProperty("spawnYPositionLow", out var syl) && syl.ValueKind == JsonValueKind.Number)
                            spawnLo = syl.GetInt32();
                        if (entry.TryGetProperty("scrollYPositionHi", out var schi) && schi.ValueKind == JsonValueKind.Number)
                            scrollHi = schi.GetInt32();
                        if (entry.TryGetProperty("scrollYPositionLow", out var sclo) && sclo.ValueKind == JsonValueKind.Number)
                            scrollLo = sclo.GetInt32();
                        Console.WriteLine($"  Found level '{lvl}' in {arrayName} at index {count}: speed={speed} maxFall={maxFall} gameMode={gameMode} spawnYHi={spawnHi} spawnYLo={spawnLo} scrollYHi={scrollHi} scrollYLo={scrollLo}");
                        return (speed, maxFall, gameMode, spawnHi, spawnLo, scrollHi, scrollLo);
                    }
                }
            }
            Console.WriteLine($"  Searched {count} levels in {arrayName}, '{levelName}' not found");
        }
    }
    return (null, null, null, null, null, null, null);
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

    // Search both official_levels and community_levels arrays
    foreach (var arrayName in new[] { "official_levels", "community_levels" })
    {
        if (!root.TryGetProperty(arrayName, out var levelsArray) || levelsArray.ValueKind != JsonValueKind.Array)
            continue;
        foreach (var entry in levelsArray.EnumerateArray())
        {
            if (entry.TryGetProperty("level", out var lvlProp)
                && lvlProp.GetString()?.Equals(levelName, StringComparison.OrdinalIgnoreCase) == true)
            {
                if (!entry.TryGetProperty("objectOffsets", out var ooArray)
                    || ooArray.ValueKind != JsonValueKind.Array)
                    return offsets; // found level but no offsets

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
                return offsets; // Found the level
            }
        }
    }
    return offsets;
}


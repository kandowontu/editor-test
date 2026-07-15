using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamidashEditor
{
    public readonly record struct NesSpriteRecord(int X, int Y, int SpriteId);

    internal static class NesSpriteDataLoader
    {
        public static NesSpriteRecord[]? BuildRuntimeRecords(
            int[] nesSpriteLayer,
            int mapWidth,
            int mapHeight,
            IReadOnlyDictionary<int, (int offsetX, int offsetY)> spritePixelOffsets)
        {
            if (nesSpriteLayer == null ||
                mapWidth <= 0 ||
                mapHeight <= 0 ||
                nesSpriteLayer.Length != mapWidth * mapHeight)
            {
                return null;
            }

            // Match LEVELS/export_levels.py:export_spr directly. Its stream is
            // column-major and includes every non-empty sprite in the first raw
            // numeric SP layer, including decorations and triggers.
            var records = new List<NesSpriteRecord>();
            int rowOffset = 57 - mapHeight;
            for (int column = 0; column < mapWidth; column++)
            {
                for (int row = 0; row < mapHeight; row++)
                {
                    int sourceIndex = row * mapWidth + column;
                    int spriteId = nesSpriteLayer[sourceIndex];
                    if (spriteId < 0)
                        continue;

                    int offsetX = 0;
                    int offsetY = 0;

                    // globalObjectOffsets in the NES levelset metadata templates.
                    switch (spriteId & 0xFF)
                    {
                        case 0x3E:
                            offsetX -= 8;
                            break;
                        case 0x0A:
                        case 0x0D:
                        case 0x25:
                        case 0x52:
                        case 0x56:
                        case 0xFD:
                            offsetY += 8;
                            break;
                    }

                    if (spritePixelOffsets.TryGetValue(sourceIndex, out var localOffset))
                    {
                        offsetX += localOffset.offsetX;
                        offsetY += localOffset.offsetY;
                    }

                    records.Add(new NesSpriteRecord(
                        column * 16 + offsetX,
                        (rowOffset + row) * 16 + offsetY,
                        spriteId));
                }
            }

            return records.ToArray();
        }

        public static bool TryLoadForTmx(string tmxPath, out NesSpriteRecord[] records)
        {
            records = Array.Empty<NesSpriteRecord>();
            if (string.IsNullOrWhiteSpace(tmxPath))
                return false;

            var tmxFile = new FileInfo(tmxPath);
            var levelSetDirectory = tmxFile.Directory;
            var levelsDirectory = levelSetDirectory?.Parent?.Parent;
            if (levelSetDirectory == null || levelsDirectory == null)
                return false;

            string includeDirectory = Path.Combine(levelsDirectory.FullName, "include");
            if (!Directory.Exists(includeDirectory))
                return false;

            string targetLabel = "sprite_data_" + Path.GetFileNameWithoutExtension(tmxFile.Name) + ":";
            string preferredLevelSetDirectory = Path.Combine(includeDirectory, levelSetDirectory.Name);
            string[] preferredAssemblyPaths =
            {
                Path.Combine(preferredLevelSetDirectory, "all_level_data.s"),
                Path.Combine(preferredLevelSetDirectory, "all_sprite_data.s")
            };

            // The selected NES levelset is authoritative when the same level label
            // exists in more than one generated build. If that levelset has generated
            // data but not this label, do not borrow a same-named stream from another
            // ROM build; the caller must derive the stream from this TMX instead.
            bool preferredLevelSetHasGeneratedData = false;
            foreach (string preferredAssemblyPath in preferredAssemblyPaths)
            {
                if (!File.Exists(preferredAssemblyPath))
                    continue;

                preferredLevelSetHasGeneratedData = true;
                if (TryLoadFromAssembly(preferredAssemblyPath, targetLabel, out records))
                    return true;
            }

            if (preferredLevelSetHasGeneratedData)
                return false;

            var assemblyPaths = new List<string>();
            try
            {
                foreach (string assemblyFileName in new[] { "all_level_data.s", "all_sprite_data.s" })
                {
                    assemblyPaths.AddRange(Directory.EnumerateFiles(
                        includeDirectory,
                        assemblyFileName,
                        SearchOption.AllDirectories));
                }
            }
            catch
            {
                return false;
            }

            assemblyPaths.Sort(StringComparer.OrdinalIgnoreCase);
            var preferredFullPaths = new HashSet<string>(
                preferredAssemblyPaths.Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);
            NesSpriteRecord[]? fallbackRecords = null;
            foreach (string assemblyPath in assemblyPaths)
            {
                if (preferredFullPaths.Contains(Path.GetFullPath(assemblyPath)))
                {
                    continue;
                }

                if (!TryLoadFromAssembly(assemblyPath, targetLabel, out NesSpriteRecord[] candidate))
                    continue;

                if (fallbackRecords == null)
                {
                    fallbackRecords = candidate;
                    continue;
                }

                // With no selected NES levelset there is no valid tiebreaker for
                // conflicting generated streams. Never silently choose the wrong ROM.
                if (!RecordsEqual(fallbackRecords, candidate))
                    return false;
            }

            if (fallbackRecords == null)
                return false;

            records = fallbackRecords;
            return true;
        }

        private static bool TryLoadFromAssembly(
            string assemblyPath,
            string targetLabel,
            out NesSpriteRecord[] records)
        {
            records = Array.Empty<NesSpriteRecord>();
            if (!File.Exists(assemblyPath))
                return false;

            bool inTarget = false;
            bool foundTerminator = false;
            var bytes = new List<int>();

            try
            {
                foreach (string sourceLine in File.ReadLines(assemblyPath))
                {
                    string line = StripComment(sourceLine).Trim();
                    if (!inTarget)
                    {
                        if (string.Equals(line, targetLabel, StringComparison.OrdinalIgnoreCase))
                            inTarget = true;
                        continue;
                    }

                    if (line.Length == 0)
                        continue;
                    if (line.EndsWith(":", StringComparison.Ordinal))
                        break;

                    int incbinDirective = line.IndexOf(".incbin", StringComparison.OrdinalIgnoreCase);
                    if (incbinDirective >= 0)
                    {
                        if (!TryResolveIncbinPath(assemblyPath, line[(incbinDirective + 7)..], out string incbinPath))
                            return false;
                        if (!File.Exists(incbinPath))
                            return false;

                        foreach (byte value in File.ReadAllBytes(incbinPath))
                        {
                            if (value == 0xFF && bytes.Count % 5 == 0)
                            {
                                foundTerminator = true;
                                break;
                            }
                            bytes.Add(value);
                        }

                        if (foundTerminator)
                            break;
                        continue;
                    }

                    int byteDirective = line.IndexOf(".byte", StringComparison.OrdinalIgnoreCase);
                    if (byteDirective < 0)
                        continue;

                    string[] tokens = line[(byteDirective + 5)..]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    foreach (string token in tokens)
                    {
                        if (!TryParseByte(token, out int value))
                            return false;
                        if (value == 0xFF && bytes.Count % 5 == 0)
                        {
                            foundTerminator = true;
                            break;
                        }
                        bytes.Add(value);
                    }

                    if (foundTerminator)
                        break;
                }
            }
            catch
            {
                return false;
            }

            if (!inTarget || !foundTerminator || bytes.Count % 5 != 0)
                return false;

            var parsed = new NesSpriteRecord[bytes.Count / 5];
            for (int i = 0, recordIndex = 0; i < bytes.Count; i += 5, recordIndex++)
            {
                int x = bytes[i] | (bytes[i + 1] << 8);
                int y = bytes[i + 2] | (bytes[i + 3] << 8);
                parsed[recordIndex] = new NesSpriteRecord(x, y, bytes[i + 4]);
            }

            records = parsed;
            return true;
        }

        private static bool RecordsEqual(NesSpriteRecord[] left, NesSpriteRecord[] right)
        {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        private static string StripComment(string line)
        {
            int semicolon = line.IndexOf(';');
            return semicolon >= 0 ? line[..semicolon] : line;
        }

        private static bool TryResolveIncbinPath(string assemblyPath, string incbinOperands, out string incbinPath)
        {
            incbinPath = string.Empty;
            int firstQuote = incbinOperands.IndexOf('"');
            if (firstQuote < 0)
                return false;
            int secondQuote = incbinOperands.IndexOf('"', firstQuote + 1);
            if (secondQuote < 0)
                return false;

            string relativePath = incbinOperands.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            incbinPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assemblyPath) ?? string.Empty, relativePath));
            return true;
        }

        private static bool TryParseByte(string token, out int value)
        {
            token = token.Trim();
            NumberStyles style = NumberStyles.Integer;
            if (token.StartsWith('$'))
            {
                token = token[1..];
                style = NumberStyles.AllowHexSpecifier;
            }
            else if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                token = token[2..];
                style = NumberStyles.AllowHexSpecifier;
            }

            if (!int.TryParse(token, style, CultureInfo.InvariantCulture, out value))
                return false;
            return value is >= 0 and <= 0xFF;
        }
    }
}

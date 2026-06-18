using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FamidashEditor
{
    public readonly record struct NesSpriteRecord(int X, int Y, int SpriteId);

    internal static class NesSpriteDataLoader
    {
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
            string preferredAssemblyPath = Path.Combine(
                includeDirectory,
                levelSetDirectory.Name,
                "all_sprite_data.s");

            // The selected NES levelset is authoritative when the same level label
            // exists in more than one generated build. If that levelset has generated
            // data but not this label, do not borrow a same-named stream from another
            // ROM build; the caller must derive the stream from this TMX instead.
            if (File.Exists(preferredAssemblyPath))
                return TryLoadFromAssembly(preferredAssemblyPath, targetLabel, out records);

            var assemblyPaths = new List<string>();
            try
            {
                assemblyPaths.AddRange(Directory.EnumerateFiles(
                    includeDirectory,
                    "all_sprite_data.s",
                    SearchOption.AllDirectories));
            }
            catch
            {
                return false;
            }

            assemblyPaths.Sort(StringComparer.OrdinalIgnoreCase);
            string preferredFullPath = Path.GetFullPath(preferredAssemblyPath);
            NesSpriteRecord[]? fallbackRecords = null;
            foreach (string assemblyPath in assemblyPaths)
            {
                if (string.Equals(
                    Path.GetFullPath(assemblyPath),
                    preferredFullPath,
                    StringComparison.OrdinalIgnoreCase))
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

                    int byteDirective = line.IndexOf(".byte", StringComparison.OrdinalIgnoreCase);
                    if (byteDirective < 0)
                        continue;

                    string[] tokens = line[(byteDirective + 5)..]
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (tokens.Length == 1 &&
                        TryParseByte(tokens[0], out int terminator) &&
                        terminator == 0xFF)
                    {
                        foundTerminator = true;
                        break;
                    }

                    foreach (string token in tokens)
                    {
                        if (!TryParseByte(token, out int value))
                            return false;
                        bytes.Add(value);
                    }
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamidashEditor
{
    internal sealed class MusicLibraryInstallResult
    {
        public required string AlbumPath { get; init; }
        public required string SourceAlbumPath { get; init; }
        public required string LibraryFolder { get; init; }
        public required IReadOnlyList<string> Tracks { get; init; }
        public required IReadOnlyDictionary<int, string> PlaybackFiles { get; init; }
        public int ImportedPreviewCount { get; init; }
        public int RenderedPreviewCount { get; init; }
        public IReadOnlyList<string> PreviewErrors { get; init; } = Array.Empty<string>();
    }

    internal sealed class MusicPlaybackEntry
    {
        public int index { get; set; }
        public string name { get; set; } = string.Empty;
        public string file { get; set; } = string.Empty;
    }

    internal static class MusicLibraryManager
    {
        private const int LibraryVersion = 1;
        internal const string InstalledAlbumFileName = "album.fms";
        internal const string ParsedNamesFileName = "fami-album-parsed.json";
        internal const string SongMapFileName = "fami-song-index-map.json";
        internal const string PlaybackMapFileName = "playback-map.json";
        internal const string ManifestFileName = "music-library.json";

        internal static string LibraryFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Famidash Editor", "Music Library");

        internal static MusicLibraryInstallResult Install(
            FamiStudioIntegration integration,
            string sourceAlbumPath,
            string? previewSourceFolder,
            bool renderMissingPreviews,
            Action<string>? reportProgress = null)
        {
            if (integration == null) throw new ArgumentNullException(nameof(integration));
            if (string.IsNullOrWhiteSpace(sourceAlbumPath) || !File.Exists(sourceAlbumPath))
                throw new FileNotFoundException("Select an existing FamiStudio album.", sourceAlbumPath);
            if (!Path.GetExtension(sourceAlbumPath).Equals(".fms", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The NES music build requires a FamiStudio .fms project.");

            string sourceFull = Path.GetFullPath(sourceAlbumPath);
            string library = LibraryFolder;
            string playbackFolder = Path.Combine(library, "Playback");
            Directory.CreateDirectory(library);
            Directory.CreateDirectory(playbackFolder);

            string installedAlbum = Path.Combine(library, InstalledAlbumFileName);
            if (!Path.GetFullPath(installedAlbum).Equals(sourceFull, StringComparison.OrdinalIgnoreCase))
                File.Copy(sourceFull, installedAlbum, overwrite: true);

            reportProgress?.Invoke("Exporting the FamiStudio song list...");
            string textExport = Path.Combine(library, "album.txt");
            List<string> tracks = integration.ExportTrackList(installedAlbum, textExport);

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(
                Path.Combine(library, ParsedNamesFileName),
                JsonSerializer.Serialize(new { parsedNames = tracks }, jsonOptions),
                new UTF8Encoding(false));

            var indexMap = tracks.Select((name, index) => new { index, name }).ToList();
            File.WriteAllText(
                Path.Combine(library, SongMapFileName),
                JsonSerializer.Serialize(indexMap, jsonOptions),
                new UTF8Encoding(false));

            var playback = new Dictionary<int, string>();
            // Preserve previews from a previous import only when both index and song name
            // still match. This keeps Refresh cheap without attaching stale audio after a reorder.
            string existingPlaybackMap = Path.Combine(library, PlaybackMapFileName);
            if (File.Exists(existingPlaybackMap))
            {
                try
                {
                    var existingEntries = JsonSerializer.Deserialize<List<MusicPlaybackEntry>>(File.ReadAllText(existingPlaybackMap));
                    foreach (var entry in existingEntries ?? new List<MusicPlaybackEntry>())
                    {
                        if (entry.index < 0 || entry.index >= tracks.Count ||
                            !string.Equals(entry.name, tracks[entry.index], StringComparison.Ordinal) ||
                            string.IsNullOrWhiteSpace(entry.file)) continue;
                        string existingPath = Path.IsPathRooted(entry.file) ? entry.file : Path.Combine(library, entry.file);
                        if (File.Exists(existingPath)) playback[entry.index] = Path.GetFullPath(existingPath);
                    }
                }
                catch { }
            }

            int imported = 0;
            if (!string.IsNullOrWhiteSpace(previewSourceFolder) && Directory.Exists(previewSourceFolder))
            {
                reportProgress?.Invoke("Matching MP3/WAV/OGG previews to songs...");
                foreach (var pair in MatchPreviewFiles(previewSourceFolder, tracks))
                {
                    string extension = Path.GetExtension(pair.Value).ToLowerInvariant();
                    string destination = Path.Combine(playbackFolder, $"{pair.Key:D3} - {SafeFileName(tracks[pair.Key])}{extension}");
                    if (!Path.GetFullPath(destination).Equals(Path.GetFullPath(pair.Value), StringComparison.OrdinalIgnoreCase))
                        File.Copy(pair.Value, destination, overwrite: true);
                    playback[pair.Key] = destination;
                    imported++;
                }
            }

            int rendered = 0;
            var previewErrors = new List<string>();
            if (renderMissingPreviews)
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    if (playback.ContainsKey(i)) continue;
                    reportProgress?.Invoke($"Rendering MP3 preview {i + 1}/{tracks.Count}: {tracks[i]}");
                    string destination = Path.Combine(playbackFolder, $"{i:D3} - {SafeFileName(tracks[i])}.mp3");
                    if (integration.ExportTrackPreviewToMp3(installedAlbum, i, destination, out string error))
                    {
                        playback[i] = destination;
                        rendered++;
                    }
                    else
                    {
                        previewErrors.Add($"{tracks[i]}: {error}");
                    }
                }
            }

            var playbackEntries = playback.OrderBy(p => p.Key).Select(p => new MusicPlaybackEntry
            {
                index = p.Key,
                name = tracks[p.Key],
                file = Path.GetRelativePath(library, p.Value)
            }).ToList();
            File.WriteAllText(
                Path.Combine(library, PlaybackMapFileName),
                JsonSerializer.Serialize(playbackEntries, jsonOptions),
                new UTF8Encoding(false));

            var manifest = new
            {
                version = LibraryVersion,
                sourceAlbum = sourceFull,
                installedAlbum = InstalledAlbumFileName,
                importedUtc = DateTime.UtcNow,
                trackCount = tracks.Count,
                previewSourceFolder = string.IsNullOrWhiteSpace(previewSourceFolder) ? null : Path.GetFullPath(previewSourceFolder),
                parsedNames = ParsedNamesFileName,
                songIndexMap = SongMapFileName,
                playbackMap = PlaybackMapFileName
            };
            File.WriteAllText(
                Path.Combine(library, ManifestFileName),
                JsonSerializer.Serialize(manifest, jsonOptions),
                new UTF8Encoding(false));

            return new MusicLibraryInstallResult
            {
                AlbumPath = installedAlbum,
                SourceAlbumPath = sourceFull,
                LibraryFolder = library,
                Tracks = tracks,
                PlaybackFiles = playback,
                ImportedPreviewCount = imported,
                RenderedPreviewCount = rendered,
                PreviewErrors = previewErrors
            };
        }

        internal static IReadOnlyDictionary<int, string> LoadPlaybackMap(string? albumPath)
        {
            var result = new Dictionary<int, string>();
            if (string.IsNullOrWhiteSpace(albumPath)) return result;
            string folder = Path.GetDirectoryName(albumPath) ?? string.Empty;
            string mapPath = Path.Combine(folder, PlaybackMapFileName);
            if (!File.Exists(mapPath)) return result;

            try
            {
                var entries = JsonSerializer.Deserialize<List<MusicPlaybackEntry>>(File.ReadAllText(mapPath));
                if (entries == null) return result;
                foreach (var entry in entries)
                {
                    if (entry.index < 0 || string.IsNullOrWhiteSpace(entry.file)) continue;
                    string path = Path.IsPathRooted(entry.file) ? entry.file : Path.Combine(folder, entry.file);
                    if (File.Exists(path)) result[entry.index] = Path.GetFullPath(path);
                }
            }
            catch { }
            return result;
        }

        internal static string? ReadSourceAlbumPath(string? installedAlbumPath)
        {
            if (string.IsNullOrWhiteSpace(installedAlbumPath)) return null;
            try
            {
                string folder = Path.GetDirectoryName(installedAlbumPath) ?? string.Empty;
                string manifestPath = Path.Combine(folder, ManifestFileName);
                using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
                return document.RootElement.TryGetProperty("sourceAlbum", out var source) ? source.GetString() : null;
            }
            catch { return null; }
        }

        private static Dictionary<int, string> MatchPreviewFiles(string folder, IReadOnlyList<string> tracks)
        {
            string[] extensions = { ".mp3", ".wav", ".ogg", ".flac", ".m4a" };
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var result = new Dictionary<int, string>();

            var byName = files
                .GroupBy(f => NormalizeName(RemoveNumericPrefix(Path.GetFileNameWithoutExtension(f))))
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tracks.Count; i++)
            {
                if (byName.TryGetValue(NormalizeName(tracks[i]), out string? match))
                    result[i] = match;
            }

            bool numericFilesAreZeroBased = files.Any(f => TryReadNumericPrefix(f, out int n) && n == 0);
            foreach (string file in files)
            {
                if (!TryReadNumericPrefix(file, out int numeric)) continue;
                int index = numericFilesAreZeroBased ? numeric : numeric - 1;
                if (index >= 0 && index < tracks.Count && !result.ContainsKey(index)) result[index] = file;
            }
            return result;
        }

        private static bool TryReadNumericPrefix(string path, out int number)
        {
            number = -1;
            var match = Regex.Match(Path.GetFileNameWithoutExtension(path), @"^\s*(\d{1,4})(?:\s*[-_. ]|$)");
            return match.Success && int.TryParse(match.Groups[1].Value, out number);
        }

        private static string RemoveNumericPrefix(string name) => Regex.Replace(name ?? string.Empty, @"^\s*\d{1,4}\s*[-_. ]*", string.Empty);

        private static string NormalizeName(string? value) => new string((value ?? string.Empty)
            .Normalize(NormalizationForm.FormD)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        private static string SafeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string safe = new string((value ?? "Song").Where(c => !invalid.Contains(c)).ToArray()).Trim();
            if (safe.Length > 80) safe = safe.Substring(0, 80).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "Song" : safe;
        }
    }
}

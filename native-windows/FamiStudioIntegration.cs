using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using NAudio.Wave;
using NAudio.MediaFoundation;
using NAudio.Wave.SampleProviders;
using System.Security.Cryptography;

#pragma warning disable CS8601,CS8600

namespace FamidashEditor
{
    public class FamiStudioIntegration
    {
        // Logging removed per user request.

        private readonly object playLock = new object();

        private AssemblyLoadContext? alc;
        private string? famiFolder;
        private WaveOutEvent? output;
        private WaveStream? reader;
        private string? lastTempWav;
        // Path of the currently opened playable file (mp3/wav) so we can reopen a fresh
        // reader at the same file when adjusting playback rate.
        private string? currentPlayingPath;
        private string? lastFmsPath = null;
        private int lastTrackIndex = -1;
        private string? lastTrackName = null;
        private readonly Dictionary<int, string> configuredPlaybackFiles = new Dictionary<int, string>();
        private readonly Dictionary<int, string> configuredTrackNames = new Dictionary<int, string>();
        private string? resolvedAlbumTrackListKey;
        private Dictionary<string, int> resolvedAlbumTrackIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Playback rate multiplier (1.0 == normal). When possible, audio output will be resampled to match.
        private double playbackRate = 1.0;
        public string? StatusMessage { get; private set; }
        public bool IsLoaded => alc != null;
        public bool IsPlaying => output != null && output.PlaybackState == PlaybackState.Playing;
        public bool IsPaused => output != null && output.PlaybackState == PlaybackState.Paused;

        public void ConfigurePlaybackFiles(IReadOnlyDictionary<int, string>? files)
        {
            lock (playLock)
            {
                configuredPlaybackFiles.Clear();
                if (files == null) return;
                foreach (var pair in files)
                {
                    if (pair.Key < 0 || string.IsNullOrWhiteSpace(pair.Value) || !File.Exists(pair.Value)) continue;
                    configuredPlaybackFiles[pair.Key] = Path.GetFullPath(pair.Value);
                }
            }
        }

        public void ConfigureTrackNames(IReadOnlyDictionary<int, string>? names)
        {
            lock (playLock)
            {
                configuredTrackNames.Clear();
                resolvedAlbumTrackListKey = null;
                resolvedAlbumTrackIndices.Clear();
                if (names == null) return;
                foreach (var pair in names)
                {
                    if (pair.Key < 0 || string.IsNullOrWhiteSpace(pair.Value)) continue;
                    configuredTrackNames[pair.Key] = pair.Value.Trim();
                }
                // Configured JSON names are useful labels, but are not marked as verified.
                // A changed FMS may have shifted every subsequent numeric index.
            }
        }

        // Resume playback if currently paused. No-op otherwise.
        public void Resume()
        {
            try
            {
                if (output != null && output.PlaybackState == PlaybackState.Paused)
                {
                    output.Volume = 1f;
                    output.Play();
                }
            }
            catch { }
        }

        // Pause playback if currently playing. No-op otherwise.
        public void Pause()
        {
            try
            {
                if (output != null && output.PlaybackState == PlaybackState.Playing)
                {
                    output.Pause();
                }
            }
            catch { }
        }

        public void LoadFromFolder(string folder)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
            famiFolder = folder;

            string exePath = Path.Combine(folder, "FamiStudio.exe");
            string dllPath = Path.Combine(folder, "FamiStudio.dll");

            try
            {
                alc = new AssemblyLoadContext("famistudio", isCollectible: true);
                if (File.Exists(dllPath))
                {
                    alc.LoadFromAssemblyPath(dllPath);
                    StatusMessage = "Loaded FamiStudio.dll";
                }
                else if (File.Exists(exePath))
                {
                    alc.LoadFromAssemblyPath(exePath);
                    StatusMessage = "Loaded FamiStudio.exe";
                }
                else
                {
                    alc.Unload();
                    alc = null;
                    StatusMessage = "No FamiStudio assemblies found in folder";
                }
            }
            catch (Exception ex)
            {
                alc = null;
                StatusMessage = "Failed to load FamiStudio assemblies: " + ex.Message;
                throw;
            }
        }

        public List<string> EnumerateTracks(string fmsPath)
        {
            // If this is a FamiStudio text export, parse it directly for song names.
            try
            {
                if (fmsPath != null && Path.GetExtension(fmsPath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    var txtList = ParseFamiStudioTextExport(fmsPath);
                    if (txtList != null && txtList.Count > 0)
                    {
                        StatusMessage = $"Found {txtList.Count} tracks via text export";
                        return txtList;
                    }
                }
            }
            catch { }

            var result = new List<string>();
            if (alc == null) return result;

            try
            {
                // Try to locate and invoke a loader to get a project object
                object? project = null;
                foreach (var asm in alc.Assemblies)
                {
                    var types = asm.GetTypes();

                    // Search for static/instance methods that look like loaders
                    foreach (var t in types)
                    {
                        var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                        foreach (var m in methods)
                        {
                            var ps = m.GetParameters();
                            if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                            {
                                var mname = m.Name.ToLowerInvariant();
                                if (mname.Contains("load") || mname.Contains("open") || mname.Contains("fromfile") || mname.Contains("loadproject"))
                                {
                                    try
                                    {
                                                if (m.IsStatic)
                                                    project = m.Invoke(null, new object[] { fmsPath });
                                                else
                                                {
                                                    var inst = Activator.CreateInstance(t);
                                                    if (inst != null)
                                                        project = m.Invoke(inst, new object[] { fmsPath });
                                                }
                                    }
                                    catch { project = null; }
                                    if (project != null) break;
                                }
                            }
                        }
                        if (project != null) break;
                    }
                    if (project != null) break;

                    // Fallback: try constructors that accept a string
                    foreach (var t in types)
                    {
                        try
                        {
                            var ctor = t.GetConstructor(new Type[] { typeof(string) });
                            if (ctor != null)
                            {
                                try { project = ctor.Invoke(new object[] { fmsPath }); } catch { project = null; }
                                if (project != null) break;
                            }
                        }
                        catch { }
                    }
                    if (project != null) break;
                }

                if (project == null) return result;

                var projectType = project.GetType();

                // Try to get a songs collection
                object? songsObj = null;
                var songsProp = projectType.GetProperty("Songs") ?? projectType.GetProperty("SongList") ?? projectType.GetProperty("Tracks");
                if (songsProp != null) songsObj = songsProp.GetValue(project);
                else
                {
                    System.Reflection.MethodInfo? method = projectType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                        .FirstOrDefault(mi => typeof(System.Collections.IEnumerable).IsAssignableFrom(mi.ReturnType) && mi.GetParameters().Length == 0);
                    if (method != null)
                    {
                        try { songsObj = method.Invoke(project, null); } catch { songsObj = null; }
                    }
                }

                if (songsObj is System.Collections.IEnumerable songs && !(songsObj is string))
                {
                    // Determine a string property to use as the song name by inspecting first element
                    string? namePropName = null;
                    Type? elemType = null;
                    foreach (var s in songs)
                    {
                        if (s == null) continue;
                        elemType = s.GetType();
                        var nameProp = elemType.GetProperty("Name") ?? elemType.GetProperty("Title") ?? elemType.GetProperty("SongName") ?? elemType.GetProperty("DisplayName");
                        if (nameProp != null && nameProp.PropertyType == typeof(string)) { namePropName = nameProp.Name; break; }

                        var stringProps = elemType.GetProperties().Where(pp => pp.PropertyType == typeof(string));
                        foreach (var pp in stringProps)
                        {
                            var pn = pp.Name.ToLowerInvariant();
                            if (pn.Contains("name") || pn.Contains("title") || pn.Contains("display")) { namePropName = pp.Name; break; }
                        }
                        if (!string.IsNullOrEmpty(namePropName)) break;

                        try { var ts = s.ToString(); if (!string.IsNullOrEmpty(ts) && ts.Length < 128 && Regex.IsMatch(ts, "[A-Za-z0-9]")) { namePropName = "__tostring"; break; } } catch { }
                        break;
                    }

                    int idx = 0;
                    foreach (var s in songs)
                    {
                        if (s == null) { idx++; continue; }
                        string name = null!;
                        if (!string.IsNullOrEmpty(namePropName) && namePropName != "__tostring")
                        {
                            try { var pn = elemType!.GetProperty(namePropName); if (pn != null) name = pn.GetValue(s)?.ToString() ?? ""; } catch { name = null; }
                        }
                        else if (namePropName == "__tostring")
                        {
                            try { name = s.ToString(); } catch { name = null; }
                        }

                        if (string.IsNullOrEmpty(name)) name = $"Song {idx}";
                        result.Add(name);
                        idx++;
                    }

                    StatusMessage = $"Found {result.Count} tracks via in-process API";
                    return result;
                }

                // Fallback: use an integer count if available
                var countProp = projectType.GetProperty("SongCount") ?? projectType.GetProperty("TrackCount") ?? projectType.GetProperty("SongsCount");
                if (countProp != null && countProp.PropertyType == typeof(int))
                {
                    var raw = countProp.GetValue(project);
                    if (raw == null) return result;
                    int count = Convert.ToInt32(raw);
                    var list = Enumerable.Range(0, count).Select(i => $"Song {i}").ToList();
                    StatusMessage = $"Found {list.Count} tracks via in-process API";
                    return list;
                }
            }
            catch { }

            return result;
        }

        // Parse a FamiStudio "text export" file. The format is a line-based token file where
        // entities appear like: Song Name="..." ...  or DPCMSample Name="..." ...
        // We attempt to extract Song names robustly across versions.
        public List<string> ParseFamiStudioTextExport(string path)
        {
            var result = new List<string>();
            if (!File.Exists(path)) return result;

            string[] lines;
            try { lines = File.ReadAllLines(path); } catch { return result; }

            var nameRegex = new System.Text.RegularExpressions.Regex("Name\\s*=\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Parse by locating 'Song' tokens and extracting the nearest Name attribute within that Song block.
            // This preserves the order of songs as they appear in the file and avoids capturing instruments/samples.
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                // Identify top-level Song token (either "Song" alone or "Song " followed by attributes)
                if (trimmed.StartsWith("Song", StringComparison.OrdinalIgnoreCase) && (trimmed.Length == 4 || char.IsWhiteSpace(trimmed[4]) || trimmed[4] == '\t'))
                {
                    // Search this line and a bounded number of subsequent lines for Name="..." inside this Song block.
                    // Stop searching the block if we encounter another top-level token (non-indented line starting with a word).
                    string? found = null;
                    for (int j = i; j < Math.Min(lines.Length, i + 24); j++)
                    {
                        var lj = lines[j];
                        if (j > i)
                        {
                            // if this line is a new top-level token (no leading whitespace) and not a continuation, break
                            if (lj.Length > 0 && !char.IsWhiteSpace(lj[0]))
                            {
                                // If this new token is itself a Song, we still allow processing it by breaking so outer loop will handle it.
                                break;
                            }
                        }

                        var m = nameRegex.Match(lj);
                        if (m.Success)
                        {
                            found = m.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(found) && !result.Contains(found)) result.Add(found);
                            break;
                        }
                    }
                }
            }

            return result;
        }

        public List<string> ProbeTracksViaCli(string fmsPath, int maxTracks = 32)
        {
            var list = new List<string>();
            if (!CanRunCli())
            {
                StatusMessage = "FamiStudio command-line tools are not configured";
                return list;
            }

            // Probe up to a high cap but stop after several consecutive misses to handle large song counts.
            int maxCap = Math.Max(maxTracks, 512);
            int consecutiveMisses = 0;
            for (int i = 0; i < maxCap; i++)
            {
                string tmp = Path.Combine(Path.GetTempPath(), $"fms_probe_{Guid.NewGuid()}.wav");
                try
                {
                    if (RunCli(fmsPath, "wav-export", tmp, 10_000, out _, out _, $"-export-songs:{i}", "-wav-export-rate:48000"))
                    {
                        if (File.Exists(tmp) && new FileInfo(tmp).Length > 100)
                        {
                            // Check exported wav duration to avoid picking up short samples. Require at least 0.5s length.
                            try
                            {
                                using var afr = new AudioFileReader(tmp);
                                var dur = afr.TotalTime.TotalSeconds;
                                if (dur >= 0.5)
                                {
                                    list.Add($"Song {i}");
                                    consecutiveMisses = 0;
                                }
                                else
                                {
                                    // short file -> likely a sample, skip
                                    consecutiveMisses++;
                                }
                            }
                            catch
                            {
                                // If we can't read the WAV, be conservative and treat it as a miss
                                consecutiveMisses++;
                            }

                            try { File.Delete(tmp); } catch { }
                            if (consecutiveMisses >= 12) break;
                            continue;
                        }
                        else
                        {
                            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                            consecutiveMisses++;
                            if (consecutiveMisses >= 12) break;
                            continue;
                        }
                    }
                    else
                    {
                        consecutiveMisses++;
                        if (consecutiveMisses >= 12) break;
                    }
                }
                catch
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    continue;
                }
            }

            StatusMessage = $"CLI probe found {list.Count} tracks";
            return list;
        }

        public List<string> ExportTrackList(string fmsPath, string outputTextPath)
        {
            if (string.IsNullOrWhiteSpace(fmsPath) || !File.Exists(fmsPath))
                throw new FileNotFoundException("FamiStudio album not found.", fmsPath);

            Directory.CreateDirectory(Path.GetDirectoryName(outputTextPath) ?? Environment.CurrentDirectory);
            try { if (File.Exists(outputTextPath)) File.Delete(outputTextPath); } catch { }

            if (!RunCli(fmsPath, "famistudio-txt-export", outputTextPath, 180_000, out string stdout, out string stderr))
            {
                string details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                throw new InvalidOperationException("FamiStudio could not export the album song list." +
                    (string.IsNullOrWhiteSpace(details) ? "" : "\n\n" + LastNonEmptyLines(details, 8)));
            }

            var names = ParseFamiStudioTextExport(outputTextPath);
            if (names.Count == 0)
                throw new InvalidOperationException("FamiStudio exported the project, but no Song entries were found.");

            StatusMessage = $"Exported {names.Count} tracks";
            return names;
        }

        public bool ExportTrackPreviewToMp3(string fmsPath, int trackIndex, string outputPath, out string error)
        {
            error = string.Empty;
            if (trackIndex < 0)
            {
                error = "Track index must be zero or greater.";
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            bool ok = RunCli(fmsPath, "mp3-export", outputPath, 180_000, out string stdout, out string stderr,
                $"-export-songs:{trackIndex}", "-mp3-export-rate:48000", "-mp3-export-bitrate:192", "-mp3-export-loop:1");
            if (!ok || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                error = LastNonEmptyLines(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr, 8);
                if (string.IsNullOrWhiteSpace(error)) error = "FamiStudio produced no MP3 file.";
                return false;
            }
            return true;
        }

        public List<string> TryParseFmsSongNames(string fmsPath)
        {
            var result = new List<string>();
            try
            {
                byte[] data = File.ReadAllBytes(fmsPath);
                string text = System.Text.Encoding.UTF8.GetString(data);
                if (string.IsNullOrEmpty(text)) return result;

                    var matches = Regex.Matches(text, @"""name""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                foreach (Match m in matches)
                {
                    var v = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(v) && !result.Contains(v)) result.Add(v);
                }

                if (result.Count == 0)
                {
                        var matches2 = Regex.Matches(text, @"Song\s*[:=]\s*""?([A-Za-z0-9 _-]{1,60})""?", RegexOptions.IgnoreCase);
                    foreach (Match m in matches2)
                    {
                        var v = m.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(v) && !result.Contains(v)) result.Add(v);
                    }
                }

                return result;
            }
            catch
            {
                return result;
            }
        }

        public void PlayTrack(string fmsPath, int trackIndex)
        {
            PlayTrack(fmsPath, trackIndex, null);
        }

        public void PlayTrack(string fmsPath, int trackIndex, string? trackName)
        {
            lock (playLock)
            {
                string? expectedTrackName = string.IsNullOrWhiteSpace(trackName) ? null : trackName.Trim();
                if (string.IsNullOrWhiteSpace(expectedTrackName) && configuredTrackNames.TryGetValue(trackIndex, out string? configuredName))
                    expectedTrackName = configuredName;
                trackIndex = ResolveCurrentTrackIndex(fmsPath, trackIndex, expectedTrackName);

                // Remember requested track so we can restart if playback rate changes.
                lastFmsPath = fmsPath;
                lastTrackIndex = trackIndex;
                lastTrackName = expectedTrackName;
                Stop();

                // If we have a cached mp3 or wav for this track, play it immediately.
                try
                {
                    string? configured = null;
                    if (configuredPlaybackFiles.TryGetValue(trackIndex, out string? indexedPreview) &&
                        File.Exists(indexedPreview) && FileNameMatchesTrack(indexedPreview, expectedTrackName))
                    {
                        configured = indexedPreview;
                    }
                    else if (!string.IsNullOrWhiteSpace(expectedTrackName))
                    {
                        configured = configuredPlaybackFiles.Values.FirstOrDefault(path =>
                            File.Exists(path) && FileNameMatchesTrack(path, expectedTrackName));
                    }

                    if (!string.IsNullOrWhiteSpace(configured))
                    {
                        PlayWav(configured);
                        StatusMessage = "Playing library preview";
                        return;
                    }

                    var existing = FindExistingCachedMusic(fmsPath, trackIndex, expectedTrackName);
                    if (!string.IsNullOrEmpty(existing) && File.Exists(existing))
                    {
                        PlayWav(existing);
                        StatusMessage = "Playing cached track";
                        return;
                    }
                }
                catch { }

                // No cache present. Use FamiStudio CLI to export a WAV, convert to cached MP3 (or WAV fallback), then play.
                if (!CanRunCli())
                {
                    StatusMessage = "FamiStudio not configured";
                    throw new InvalidOperationException("FamiStudio folder not configured");
                }

                string tmpWav = Path.Combine(Path.GetTempPath(), $"fms_play_{Guid.NewGuid()}.wav");
                if (!RunCli(fmsPath, "wav-export", tmpWav, 180_000, out string stdout, out string stderr,
                    $"-export-songs:{trackIndex}", "-wav-export-rate:48000"))
                    throw new Exception("FamiStudio audio export failed.\n" + LastNonEmptyLines(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr, 8));

                if (!File.Exists(tmpWav)) throw new Exception("Export failed or produced no WAV");

                var cachedTarget = GetCachedMusicPath(fmsPath, trackIndex, expectedTrackName);
                var outPath = ConvertWavToCached(tmpWav, cachedTarget);
                if (!string.IsNullOrEmpty(outPath) && File.Exists(outPath))
                {
                    // Play the cached MP3 (or WAV fallback)
                    PlayWav(outPath);
                    lastTempWav = null;
                    StatusMessage = "Playing (cached)";
                    return;
                }

                // Fallback: play the exported WAV and leave it as temp
                PlayWav(tmpWav);
                lastTempWav = tmpWav;
                StatusMessage = "Playing (CLI fallback)";
                return;
            }
        }

        private void PlayWav(string wavPath)
        {
            try { currentPlayingPath = wavPath; } catch { currentPlayingPath = wavPath; }
            try
            {
                reader = new AudioFileReader(wavPath);
            }
            catch
            {
                reader = new WaveFileReader(wavPath);
            }
            output = new WaveOutEvent();
            try
            {
                if (Math.Abs(playbackRate - 1.0) > 0.0001 && reader != null)
                {
                    try
                    {
                        // Use managed varispeed provider to change playback speed (affects pitch).
                        var sp = reader.ToSampleProvider();
                        var varispeed = new VarispeedSampleProvider(sp, playbackRate);
                        var waveProvider = new SampleToWaveProvider16(varispeed);
                        output.Init(waveProvider);
                    }
                    catch
                    {
                        output.Init(reader);
                    }
                }
                else
                {
                    
                    output.Init(reader);
                }
            }
            catch
            {
                try { output.Init(reader); } catch { }
            }
            output.PlaybackStopped += (s, e) =>
            {
                try { reader?.Dispose(); } catch { }
                try { output?.Dispose(); } catch { }
                reader = null; output = null;
                try { currentPlayingPath = null; } catch { }
                if (lastTempWav != null)
                {
                    try { File.Delete(lastTempWav); } catch { }
                    lastTempWav = null;
                }
            };

            output.Volume = 1f;
            output.Play();
        }

        private void PlayWavStream(Stream wavStream)
        {
            try { if (wavStream.CanSeek) wavStream.Position = 0; } catch { }
            try
            {
                reader = new WaveFileReader(wavStream);
            }
            catch (Exception ex)
            {
                throw new Exception("In-process render did not produce a WAV stream: " + ex.Message);
            }

            output = new WaveOutEvent();
            try
            {
                if (Math.Abs(playbackRate - 1.0) > 0.0001 && reader != null)
                {
                    try
                    {
                        var newFormat = new WaveFormat((int)(reader.WaveFormat.SampleRate * playbackRate), reader.WaveFormat.BitsPerSample, reader.WaveFormat.Channels);
                        var resampler = new MediaFoundationResampler(reader, newFormat) { ResamplerQuality = 60 };
                        output.Init(resampler);
                    }
                    catch
                    {
                        output.Init(reader);
                    }
                }
                else
                {
                    output.Init(reader);
                }
            }
            catch
            {
                try { output.Init(reader); } catch { }
            }
            output.PlaybackStopped += (s, e) =>
            {
                try { reader?.Dispose(); } catch { }
                try { output?.Dispose(); } catch { }
                reader = null; output = null;
                if (lastTempWav != null)
                {
                    try { File.Delete(lastTempWav); } catch { }
                    lastTempWav = null;
                }
            };

            output.Volume = 1f;
            output.Play();
        }

        public void Stop()
        {
            lock (playLock)
            {
                try
                {
                    // Mute and stop immediately for instant silence
                    if (output != null)
                    {
                        try 
                        { 
                            // Set volume to 0 for immediate silence
                            output.Volume = 0f;
                            
                            // Stop playback
                            if (output.PlaybackState != PlaybackState.Stopped)
                            {
                                output.Stop();
                            }
                        } 
                        catch { }
                        
                        // Force dispose
                        try { output.Dispose(); } catch { }
                    }
                    
                    // Dispose reader
                    try { reader?.Dispose(); } catch { }
                    
                    // Clear references
                    reader = null; 
                    output = null;
                    currentPlayingPath = null;
                }
                catch { }
                try { if (lastTempWav != null && File.Exists(lastTempWav)) File.Delete(lastTempWav); } catch { }
                lastTempWav = null;
                StatusMessage = "Stopped";
            }
        }

        // Compute the cache directory and filename for an exported track
        private string GetMusicCacheDir()
        {
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var dir = Path.Combine(docs, "Famidash Editor", "Music");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                return Path.GetTempPath();
            }
        }

        private string GetCachedMusicPath(string fmsPath, int trackIndex, string? trackName)
        {
            try
            {
                if (string.IsNullOrEmpty(fmsPath)) return null!;
                using var sha = SHA1.Create();
                string version = "missing";
                try
                {
                    var info = new FileInfo(fmsPath);
                    if (info.Exists) version = info.Length + "|" + info.LastWriteTimeUtc.Ticks;
                }
                catch { }
                var key = (Path.GetFullPath(fmsPath) + "|" + version + "|" + trackIndex.ToString());
                var bytes = System.Text.Encoding.UTF8.GetBytes(key);
                var hash = sha.ComputeHash(bytes);
                var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                string safeName = SafeTrackFileName(string.IsNullOrWhiteSpace(trackName) ? $"Song {trackIndex}" : trackName);
                var fname = $"cached {trackIndex:D3} - {safeName} - {hex.Substring(0, 16)}.mp3";
                return Path.Combine(GetMusicCacheDir(), fname);
            }
            catch
            {
                string safeName = SafeTrackFileName(string.IsNullOrWhiteSpace(trackName) ? $"Song {trackIndex}" : trackName);
                return Path.Combine(Path.GetTempPath(), $"cached {trackIndex:D3} - {safeName} - 000000000000.mp3");
            }
        }

        // If a cached file exists for this track (either MP3 or WAV), return its path; otherwise return null.
        private string? FindExistingCachedMusic(string fmsPath, int trackIndex, string? trackName)
        {
            try
            {
                // A cache without a known song name is unsafe after insertions/reordering.
                if (string.IsNullOrWhiteSpace(trackName)) return null;
                var mp3 = GetCachedMusicPath(fmsPath, trackIndex, trackName);
                if (File.Exists(mp3) && FileNameMatchesTrack(mp3, trackName)) return mp3;
                var wav = Path.ChangeExtension(mp3, ".wav");
                if (File.Exists(wav) && FileNameMatchesTrack(wav, trackName)) return wav;
                return null;
            }
            catch { return null; }
        }

        // Ensure a cached MP3 exists for the given track. Returns path to playable file (mp3 or wav fallback).
        private string EnsureCachedMusic(string fmsPath, int trackIndex)
        {
            configuredTrackNames.TryGetValue(trackIndex, out string? trackName);
            var cached = GetCachedMusicPath(fmsPath, trackIndex, trackName);
            if (File.Exists(cached) && FileNameMatchesTrack(cached, trackName)) return cached;
            // Not cached - attempt to export via CLI and then convert to mp3
            if (!CanRunCli()) return string.Empty;
            string tmpWav = Path.Combine(Path.GetTempPath(), $"fms_export_{Guid.NewGuid()}.wav");
            try { RunCli(fmsPath, "wav-export", tmpWav, 180_000, out _, out _, $"-export-songs:{trackIndex}", "-wav-export-rate:48000"); } catch { }
            if (!File.Exists(tmpWav)) return string.Empty;
            var outPath = ConvertWavToCached(tmpWav, cached);
            return outPath ?? string.Empty;
        }

        // Convert a WAV file to the cached MP3 (or WAV fallback) path and return that path.
        private string ConvertWavToCached(string wavPath, string cachedTarget)
        {
            try
            {
                if (!File.Exists(wavPath)) return string.Empty;
                try
                {
                    using var reader = new AudioFileReader(wavPath);
                    try
                    {
                        MediaFoundationApi.Startup();
                        var mp3Out = cachedTarget;
                        MediaFoundationEncoder.EncodeToMp3(reader, mp3Out, 192000);
                        try { File.Delete(wavPath); } catch { }
                        return mp3Out;
                    }
                    catch
                    {
                        // Encoding failed - fall back to wav copy
                    }
                }
                catch { }

                try
                {
                    var fallback = Path.ChangeExtension(cachedTarget, ".wav");
                    File.Copy(wavPath, fallback, true);
                    try { File.Delete(wavPath); } catch { }
                    return fallback;
                }
                catch { return string.Empty; }
            }
            catch { return string.Empty; }
        }

        private bool CanRunCli()
        {
            if (string.IsNullOrWhiteSpace(famiFolder) || !Directory.Exists(famiFolder)) return false;
            return File.Exists(Path.Combine(famiFolder, "FamiStudio.dll")) ||
                   File.Exists(Path.Combine(famiFolder, "FamiStudio.exe"));
        }

        private int ResolveCurrentTrackIndex(string albumPath, int fallbackIndex, string? expectedTrackName)
        {
            if (string.IsNullOrWhiteSpace(expectedTrackName)) return fallbackIndex;
            if (string.IsNullOrWhiteSpace(albumPath) || !File.Exists(albumPath))
                throw new FileNotFoundException("Music album not found.", albumPath);

            string versionKey = GetAlbumVersionKey(albumPath);
            if (!string.Equals(resolvedAlbumTrackListKey, versionKey, StringComparison.Ordinal))
            {
                List<string> names;
                if (Path.GetExtension(albumPath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    names = ParseFamiStudioTextExport(albumPath);
                }
                else
                {
                    string? adjacentExport = FindFreshAdjacentTextExport(albumPath);
                    if (!string.IsNullOrWhiteSpace(adjacentExport))
                    {
                        names = ParseFamiStudioTextExport(adjacentExport);
                    }
                    else
                    {
                        string temporaryExport = Path.Combine(Path.GetTempPath(), $"famistudio_tracks_{Guid.NewGuid():N}.txt");
                        try { names = ExportTrackList(albumPath, temporaryExport); }
                        finally { try { if (File.Exists(temporaryExport)) File.Delete(temporaryExport); } catch { } }
                    }
                }

                if (names.Count == 0)
                    throw new InvalidOperationException("Could not verify the current FamiStudio song ordering.");

                var verified = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < names.Count; i++)
                {
                    if (!verified.ContainsKey(names[i])) verified[names[i]] = i;
                }
                resolvedAlbumTrackIndices = verified;
                resolvedAlbumTrackListKey = versionKey;
            }

            if (resolvedAlbumTrackIndices.TryGetValue(expectedTrackName, out int exactIndex)) return exactIndex;

            string normalizedExpected = NormalizeTrackName(expectedTrackName);
            var normalizedMatches = resolvedAlbumTrackIndices
                .Where(pair => string.Equals(NormalizeTrackName(pair.Key), normalizedExpected, StringComparison.Ordinal))
                .Select(pair => pair.Value)
                .Distinct()
                .Take(2)
                .ToList();
            if (normalizedMatches.Count == 1) return normalizedMatches[0];

            throw new InvalidOperationException($"Song '{expectedTrackName}' was not found uniquely in the current FamiStudio album. Refresh the Custom Music Library.");
        }

        private static string GetAlbumVersionKey(string albumPath)
        {
            var info = new FileInfo(albumPath);
            return Path.GetFullPath(albumPath) + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
        }

        private static string? FindFreshAdjacentTextExport(string albumPath)
        {
            try
            {
                var album = new FileInfo(albumPath);
                string folder = album.DirectoryName ?? string.Empty;
                string[] candidates =
                {
                    Path.Combine(folder, "album.txt"),
                    Path.ChangeExtension(albumPath, ".txt")
                };
                return candidates
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(path => File.Exists(path) && File.GetLastWriteTimeUtc(path) >= album.LastWriteTimeUtc);
            }
            catch { return null; }
        }

        private ProcessStartInfo CreateCliStartInfo(string inputPath, string command, string outputPath, params string[] options)
        {
            if (string.IsNullOrWhiteSpace(famiFolder))
                throw new InvalidOperationException("FamiStudio folder not configured.");

            string dll = Path.Combine(famiFolder, "FamiStudio.dll");
            string exe = Path.Combine(famiFolder, "FamiStudio.exe");
            ProcessStartInfo psi;
            if (File.Exists(dll))
            {
                // The Windows GUI launcher may detach without executing CLI work. The DLL is
                // FamiStudio's documented command-line entry point and returns a useful exit code.
                psi = new ProcessStartInfo("dotnet");
                psi.ArgumentList.Add(dll);
            }
            else if (File.Exists(exe))
            {
                psi = new ProcessStartInfo(exe);
            }
            else
            {
                throw new FileNotFoundException("FamiStudio.dll/FamiStudio.exe not found in the configured folder.", famiFolder);
            }

            psi.ArgumentList.Add(inputPath);
            psi.ArgumentList.Add(command);
            psi.ArgumentList.Add(outputPath);
            foreach (string option in options) psi.ArgumentList.Add(option);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            return psi;
        }

        private bool RunCli(string inputPath, string command, string outputPath, int timeoutMs,
            out string stdout, out string stderr, params string[] options)
        {
            stdout = string.Empty;
            stderr = string.Empty;
            try
            {
                var psi = CreateCliStartInfo(inputPath, command, outputPath, options);
                using var process = Process.Start(psi);
                if (process == null) return false;
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(Math.Max(1_000, timeoutMs)))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    stdout = stdoutTask.GetAwaiter().GetResult();
                    stderr = stderrTask.GetAwaiter().GetResult();
                    if (string.IsNullOrWhiteSpace(stderr)) stderr = "FamiStudio command timed out.";
                    return false;
                }
                stdout = stdoutTask.GetAwaiter().GetResult();
                stderr = stderrTask.GetAwaiter().GetResult();
                return process.ExitCode == 0 && File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return false;
            }
        }

        private static string LastNonEmptyLines(string? text, int count)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return string.Join("\n", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).TakeLast(Math.Max(1, count)));
        }

        private static bool FileNameMatchesTrack(string path, string? expectedTrackName)
        {
            if (string.IsNullOrWhiteSpace(expectedTrackName)) return false;
            string stem = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            stem = Regex.Replace(stem, @"^cached\s+\d+\s*-\s*", string.Empty, RegexOptions.IgnoreCase);
            stem = Regex.Replace(stem, @"^\d+\s*-\s*", string.Empty, RegexOptions.IgnoreCase);
            stem = Regex.Replace(stem, @"\s*-\s*[0-9a-f]{12,40}$", string.Empty, RegexOptions.IgnoreCase);
            return string.Equals(NormalizeTrackName(stem), NormalizeTrackName(SafeTrackFileName(expectedTrackName)), StringComparison.Ordinal);
        }

        private static string NormalizeTrackName(string? value) => new string((value ?? string.Empty)
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        private static string SafeTrackFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string safe = new string((value ?? "Song").Where(c => !invalid.Contains(c)).ToArray()).Trim();
            if (safe.Length > 80) safe = safe.Substring(0, 80).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "Song" : safe;
        }

        // Request a playback rate multiplier (e.g. 2.0 for 2x).
        // Reverted to a safe no-op that only records the desired rate but does not modify playback.
        // Seek to a specific time position (in seconds) in the currently playing track
        public void SeekToPosition(double seconds)
        {
            try
            {
                lock (playLock)
                {
                    if (reader == null) return;
                    
                    try
                    {
                        if (reader is AudioFileReader afr)
                        {
                            afr.CurrentTime = TimeSpan.FromSeconds(seconds);
                        }
                        else
                        {
                            // Fallback for non-AudioFileReader streams
                            long bytePosition = (long)(seconds * reader.WaveFormat.AverageBytesPerSecond);
                            if (reader.CanSeek && bytePosition >= 0 && bytePosition < reader.Length)
                            {
                                reader.Position = bytePosition;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void SetPlaybackRate(double rate)
        {
            try
            {
                if (rate <= 0) return;
                playbackRate = rate;
                // If audio is currently playing, attempt to reinitialize the output so the
                // new playback rate takes effect without fully restarting the track list.
                lock (playLock)
                {
                    try
                    {
                        if (output == null && reader == null) return;
                        // Capture current playback time and re-open a fresh reader from the
                        // currently playing file so we can reliably seek and reconnect the
                        // resampler without odd state in the existing stream.
                        TimeSpan currentTime = TimeSpan.Zero;
                        try { if (reader is AudioFileReader afr) currentTime = afr.CurrentTime; } catch { currentTime = TimeSpan.Zero; }

                        try { output?.Stop(); } catch { }
                        try { output?.Dispose(); } catch { }

                        // Create a fresh reader instance from the same file path and position it.
                        WaveStream? newReader = null;
                        try
                        {
                            if (!string.IsNullOrEmpty(currentPlayingPath))
                            {
                                try { newReader = new AudioFileReader(currentPlayingPath); } catch { newReader = new WaveFileReader(currentPlayingPath); }
                                try { if (newReader is AudioFileReader afr2) afr2.CurrentTime = currentTime; else newReader.Position = (long)(currentTime.TotalSeconds * newReader.WaveFormat.AverageBytesPerSecond); } catch { }
                            }
                            else if (reader != null)
                            {
                                // Last-resort: reuse old reader if we couldn't create a fresh one.
                                newReader = reader;
                            }
                        }
                        catch { newReader = reader; }

                        // Swap in the new reader
                        try { reader?.Dispose(); } catch { }
                        reader = newReader;

                        // Re-create output and init with desired resampler/format.
                        output = new WaveOutEvent();
                        try
                        {
                            if (reader != null && Math.Abs(playbackRate - 1.0) > 0.0001)
                            {
                                try
                                {
                                    var sp = reader.ToSampleProvider();
                                    var varispeed = new VarispeedSampleProvider(sp, playbackRate);
                                    var waveProvider = new SampleToWaveProvider16(varispeed);
                                    output.Init(waveProvider);
                                }
                                catch
                                {
                                    output.Init(reader);
                                }
                            }
                            else
                            {
                                output.Init(reader);
                            }
                        }
                        catch
                        {
                            try { output.Init(reader); } catch { }
                        }

                        output.Volume = 1f;
                        output.Play();
                    }
                    catch { }
                }
                // If reinitialization did not produce audible output (some resamplers or device
                // combinations may fail for certain rates), attempt a full restart of the current
                // track using the cached path. This is heavier but more robust.
                try
                {
                    if ((output == null || output.PlaybackState != PlaybackState.Playing) && !string.IsNullOrEmpty(lastFmsPath) && lastTrackIndex >= 0)
                    {
                        try
                        {
                            // Fire-and-forget restart so UI doesn't block.
                            string? restartName = lastTrackName;
                            _ = System.Threading.Tasks.Task.Run(() => PlayTrack(lastFmsPath!, lastTrackIndex, restartName));
                        }
                        catch { }
                    }
                }
                catch { }
            }
            catch { }
        }

        // Warm up FamiStudio in-process API and audio device to reduce first-play latency.
        // This will attempt to ensure assemblies are loaded, enumerate tracks, and perform
        // a minimal in-process render into a MemoryStream (but not play it) to JIT and warm audio.
        public void WarmAndPrime(string fmsPath)
        {
            try
            {
                if (string.IsNullOrEmpty(fmsPath) || !File.Exists(fmsPath)) return;

                // Ensure assemblies are loaded if we have a configured famiFolder
                try { if (!IsLoaded && !string.IsNullOrEmpty(famiFolder) && Directory.Exists(famiFolder)) LoadFromFolder(famiFolder); } catch { }

                // Enumerate tracks to warm loaders and any data parsing
                try { var _ = EnumerateTracks(fmsPath); } catch { }

                // Try an in-process render to MemoryStream to JIT render path and warm audio pipeline
                if (alc != null)
                {
                    foreach (var asm in alc.Assemblies)
                    {
                        Type? playType = asm.GetTypes().FirstOrDefault(t => t.Name.ToLower().Contains("player") || t.Name.ToLower().Contains("audio"));
                        if (playType == null) continue;

                        var methods = playType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                            .Where(mi => mi.Name.ToLower().Contains("export") || mi.Name.ToLower().Contains("render") || mi.Name.ToLower().Contains("play"))
                            .ToList();

                        object? inst = null;
                        foreach (var method in methods)
                        {
                            try
                            {
                                var parameters = method.GetParameters();
                                if (!method.IsStatic && inst == null) inst = Activator.CreateInstance(playType);

                                if (parameters.Length == 3 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(int) && typeof(Stream).IsAssignableFrom(parameters[2].ParameterType))
                                {
                                    using (var ms = new MemoryStream())
                                    {
                                        method.Invoke(inst, new object[] { fmsPath, 0, ms });
                                        if (ms.Length > 0)
                                        {
                                            try
                                            {
                                                ms.Position = 0;
                                                using var wf = new WaveFileReader(ms);
                                                using var wo = new WaveOutEvent();
                                                wo.Init(wf);
                                            }
                                            catch { }
                                            return;
                                        }
                                    }
                                }

                                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(int) && method.ReturnType == typeof(byte[]))
                                {
                                    var ret = method.Invoke(inst, new object[] { fmsPath, 0 }) as byte[];
                                    if (ret != null && ret.Length > 0)
                                    {
                                        try
                                        {
                                            using var ms = new MemoryStream(ret);
                                            using var wf = new WaveFileReader(ms);
                                            using var wo = new WaveOutEvent();
                                            wo.Init(wf);
                                        }
                                        catch { }
                                        return;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }
    }
}

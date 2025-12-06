using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using NAudio.Wave;

#pragma warning disable CS8601,CS8600

namespace FamidashEditor
{
    public class FamiStudioIntegration
    {
        private AssemblyLoadContext? alc;
        private string? famiFolder;
        private WaveOutEvent? output;
        private WaveStream? reader;
        private string? lastTempWav;
        private string? lastFmsPath = null;
        private int lastTrackIndex = -1;
        // Playback rate multiplier (1.0 == normal). When possible, audio output will be resampled to match.
        private double playbackRate = 1.0;
        public string? StatusMessage { get; private set; }
        public bool IsLoaded => alc != null;
        public bool IsPlaying => output != null && output.PlaybackState == PlaybackState.Playing;
        public bool IsPaused => output != null && output.PlaybackState == PlaybackState.Paused;

        // Resume playback if currently paused. No-op otherwise.
        public void Resume()
        {
            try
            {
                if (output != null && output.PlaybackState == PlaybackState.Paused)
                {
                    output.Play();
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
            if (famiFolder == null)
            {
                StatusMessage = "FamiStudio folder not configured";
                return list;
            }

            string exe = Path.Combine(famiFolder, "FamiStudio.exe");
            if (!File.Exists(exe))
            {
                StatusMessage = "FamiStudio.exe not found in bundled folder";
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
                    var args = $"\"{fmsPath}\" wav-export \"{tmp}\" -export-songs:{i} -wav-export-rate:48000";
                    var psi = new ProcessStartInfo(exe, args)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using (var p = Process.Start(psi))
                    {
                        if (p == null) continue;
                        p.WaitForExit(3000);
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
            // Remember requested track so we can restart if playback rate changes.
            lastFmsPath = fmsPath;
            lastTrackIndex = trackIndex;
            Stop();
            // No PlayTrack diagnostic logging

            if (alc != null)
            {
                try
                {
                    foreach (var asm in alc.Assemblies)
                    {
                        Type? playType = asm.GetTypes().FirstOrDefault(t => t.Name.ToLower().Contains("player") || t.Name.ToLower().Contains("audio"));
                        if (playType != null)
                        {
                            // Prefer any in-process method that can render to a Stream or return byte[] before falling back to file-based export
                            var candidates = playType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                                .Where(mi => mi.Name.ToLower().Contains("export") || mi.Name.ToLower().Contains("render") || mi.Name.ToLower().Contains("play"))
                                .ToList();

                            object? inst = null;
                            foreach (var method in candidates)
                            {
                                try
                                {
                                    var parameters = method.GetParameters();
                                    if (!method.IsStatic && inst == null) inst = Activator.CreateInstance(playType);

                                    // signature: (string path, int index, Stream outStream)
                                    if (parameters.Length == 3 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(int) && typeof(Stream).IsAssignableFrom(parameters[2].ParameterType))
                                    {
                                        using (var ms = new MemoryStream())
                                        {
                                            method.Invoke(inst, new object[] { fmsPath, trackIndex, ms });
                                            if (ms.Length > 0)
                                            {
                                                ms.Position = 0;
                                                PlayWavStream(ms);
                                                StatusMessage = "Playing via in-process stream export";
                                                return;
                                            }
                                        }
                                    }

                                    // signature: (string path, int index) returning byte[]
                                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(int) && method.ReturnType == typeof(byte[]))
                                    {
                                        var bytes = method.Invoke(inst, new object[] { fmsPath, trackIndex }) as byte[];
                                        if (bytes != null && bytes.Length > 0)
                                        {
                                            using (var ms = new MemoryStream(bytes))
                                            {
                                                PlayWavStream(ms);
                                                StatusMessage = "Playing via in-process byte[] export";
                                                return;
                                            }
                                        }
                                    }

                                    // Fallback to file-based third-parameter signature (string outpath)
                                    if (parameters.Length == 3 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(int) && parameters[2].ParameterType == typeof(string))
                                    {
                                        string tmp = Path.Combine(Path.GetTempPath(), $"fms_play_{Guid.NewGuid()}.wav");
                                        method.Invoke(inst, new object[] { fmsPath, trackIndex, tmp });
                                        if (File.Exists(tmp))
                                        {
                                            PlayWav(tmp);
                                            lastTempWav = tmp;
                                            StatusMessage = "Playing via in-process export";
                                            return;
                                        }
                                    }
                                }
                                catch { /* try next candidate */ }
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (famiFolder == null)
            {
                StatusMessage = "FamiStudio not configured";
                throw new InvalidOperationException("FamiStudio folder not configured");
            }

            string exe = Path.Combine(famiFolder, "FamiStudio.exe");
            if (!File.Exists(exe))
            {
                StatusMessage = "FamiStudio.exe not found in bundled folder";
                throw new FileNotFoundException("FamiStudio.exe not found", exe);
            }

            string tmpWav = Path.Combine(Path.GetTempPath(), $"fms_play_{Guid.NewGuid()}.wav");
            var args = $"\"{fmsPath}\" wav-export \"{tmpWav}\" -export-songs:{trackIndex} -wav-export-rate:48000";
            var psi2 = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var p = Process.Start(psi2))
            {
                if (p == null) throw new Exception("Failed to start FamiStudio CLI");
                p.WaitForExit(15000);
            }

            if (!File.Exists(tmpWav)) throw new Exception("Export failed or produced no WAV");

            PlayWav(tmpWav);
            lastTempWav = tmpWav;
            StatusMessage = "Playing (CLI fallback)";
        }

        private void PlayWav(string wavPath)
        {
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

            output.Play();
        }

        public void Stop()
        {
            try
            {
                output?.Stop();
                reader?.Dispose();
                output?.Dispose();
                reader = null; output = null;
            }
            catch { }
            try { if (lastTempWav != null && File.Exists(lastTempWav)) File.Delete(lastTempWav); } catch { }
            lastTempWav = null;
            StatusMessage = "Stopped";
        }

        // Request a playback rate multiplier (e.g. 2.0 for 2x).
        // Reverted to a safe no-op that only records the desired rate but does not modify playback.
        public void SetPlaybackRate(double rate)
        {
            try
            {
                if (rate <= 0) return;
                playbackRate = rate;
                // Do not restart or reinitialize audio here; simulator no longer affects music.
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
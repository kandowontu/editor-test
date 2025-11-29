using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace FamidashEditor
{
    public class FamiStudioIntegration
    {
        private AssemblyLoadContext? alc;
        private string? famiFolder;
        private WaveOutEvent? output;
        private AudioFileReader? reader;
        private string? lastTempWav;
        public string? StatusMessage { get; private set; }
        public bool IsLoaded => alc != null;
        public bool IsPlaying => output != null && output.PlaybackState == PlaybackState.Playing;

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
            if (alc == null) return new List<string>();

            try
            {
                foreach (var asm in alc.Assemblies)
                {
                    var types = asm.GetTypes();
                    foreach (var t in types)
                    {
                        var prop = t.GetProperty("Songs") ?? t.GetProperty("SongCount");
                        if (prop != null)
                        {
                            MethodInfo? loader = types.SelectMany(tt => tt.GetMethods(BindingFlags.Public | BindingFlags.Static)).FirstOrDefault(mi => mi.Name.ToLower().Contains("load") && mi.GetParameters().Length == 1 && mi.GetParameters()[0].ParameterType == typeof(string));
                            if (loader != null)
                            {
                                var project = loader.Invoke(null, new object[] { fmsPath });
                                if (project != null)
                                {
                                    var songsObj = prop.GetValue(project);
                                    if (songsObj is System.Collections.IEnumerable songs)
                                    {
                                        var list = new List<string>();
                                        int idx = 0;
                                        foreach (var s in songs)
                                        {
                                            list.Add($"Song {idx}");
                                            idx++;
                                        }
                                        StatusMessage = $"Found {list.Count} tracks via in-process API";
                                        return list;
                                    }
                                    else if (prop.PropertyType == typeof(int))
                                    {
                                        int count = (int)prop.GetValue(project)!;
                                        var list = Enumerable.Range(0, count).Select(i => $"Song {i}").ToList();
                                        StatusMessage = $"Found {list.Count} tracks via in-process API";
                                        return list;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return new List<string>();
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

            for (int i = 0; i < maxTracks; i++)
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
                            list.Add($"Song {i}");
                            try { File.Delete(tmp); } catch { }
                            continue;
                        }
                        else
                        {
                            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
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
            Stop();

            if (alc != null)
            {
                try
                {
                    foreach (var asm in alc.Assemblies)
                    {
                        var playType = asm.GetTypes().FirstOrDefault(t => t.Name.ToLower().Contains("player") || t.Name.ToLower().Contains("audio"));
                        if (playType != null)
                        {
                            var method = playType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance).FirstOrDefault(mi => mi.Name.ToLower().Contains("export") || mi.Name.ToLower().Contains("render") || mi.Name.ToLower().Contains("play"));
                            if (method != null)
                            {
                                var parameters = method.GetParameters();
                                object? inst = null;
                                if (!method.IsStatic)
                                {
                                    inst = Activator.CreateInstance(playType);
                                }

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
            reader = new AudioFileReader(wavPath);
            output = new WaveOutEvent();
            output.Init(reader);
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
        }
    }
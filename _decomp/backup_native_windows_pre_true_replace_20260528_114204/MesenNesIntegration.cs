using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamidashEditor
{
    internal sealed class MesenRamCaptureOptions
    {
        public string MesenExePath { get; set; } = string.Empty;
        public string RomPath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 180;
        public int MaxFrames { get; set; } = 1200;
        public int SampleEveryNFrames { get; set; } = 1;
        public IReadOnlyList<ushort> Addresses { get; set; } = Array.Empty<ushort>();
    }

    internal sealed class MesenRamCaptureResult
    {
        public int ExitCode { get; set; }
        public int StopCode { get; set; }
        public int Samples { get; set; }
        public string StdErr { get; set; } = string.Empty;
    }

    internal static class MesenNesIntegration
    {
        private const string RamCapPrefix = "RAMCAP|";

        internal static void LaunchGui(string mesenExePath, string romPath)
        {
            if (string.IsNullOrWhiteSpace(mesenExePath)) throw new InvalidOperationException("Mesen executable path is not configured.");
            if (string.IsNullOrWhiteSpace(romPath)) throw new InvalidOperationException("Famidash ROM path is not configured.");
            if (!File.Exists(mesenExePath)) throw new FileNotFoundException("Mesen executable not found.", mesenExePath);
            if (!File.Exists(romPath)) throw new FileNotFoundException("ROM file not found.", romPath);

            var psi = new ProcessStartInfo(mesenExePath, QuoteArg(romPath))
            {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(mesenExePath) ?? Environment.CurrentDirectory,
            };
            Process.Start(psi);
        }

        internal static MesenRamCaptureResult CaptureRam(MesenRamCaptureOptions options)
        {
            Validate(options);

            Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath) ?? Environment.CurrentDirectory);

            string scriptDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Famidash Editor", "Replays");
            Directory.CreateDirectory(scriptDir);
            string scriptPath = Path.Combine(scriptDir, "famidash-mesen-ramcap.lua");
            File.WriteAllText(scriptPath, BuildLuaScript(options), Encoding.UTF8);

            string args = string.Join(" ", new[]
            {
                "--doNotSaveSettings",
                "--testRunner",
                QuoteArg(scriptPath),
                QuoteArg(options.RomPath),
                "--timeout=" + Math.Max(10, options.TimeoutSeconds).ToString(CultureInfo.InvariantCulture)
            });

            var psi = new ProcessStartInfo(options.MesenExePath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(options.MesenExePath) ?? Environment.CurrentDirectory,
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    lock (stdout)
                    {
                        stdout.AppendLine(e.Data);
                    }
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    lock (stderr)
                    {
                        stderr.AppendLine(e.Data);
                    }
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start Mesen process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            string fullOut;
            string fullErr;
            lock (stdout) fullOut = stdout.ToString();
            lock (stderr) fullErr = stderr.ToString();

            int sampleCount = WriteCaptureFile(options, fullOut);
            int stopCode = TryExtractStopCode(fullOut, process.ExitCode);

            return new MesenRamCaptureResult
            {
                ExitCode = process.ExitCode,
                StopCode = stopCode,
                Samples = sampleCount,
                StdErr = fullErr,
            };
        }

        private static void Validate(MesenRamCaptureOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.MesenExePath) || !File.Exists(options.MesenExePath))
                throw new FileNotFoundException("Mesen executable not found.", options.MesenExePath);
            if (string.IsNullOrWhiteSpace(options.RomPath) || !File.Exists(options.RomPath))
                throw new FileNotFoundException("ROM file not found.", options.RomPath);
            if (options.Addresses == null || options.Addresses.Count == 0)
                throw new InvalidOperationException("No RAM addresses configured for capture.");
            if (options.MaxFrames < 1) options.MaxFrames = 1;
            if (options.SampleEveryNFrames < 1) options.SampleEveryNFrames = 1;
            if (options.TimeoutSeconds < 10) options.TimeoutSeconds = 10;
        }

        private static string BuildLuaScript(MesenRamCaptureOptions options)
        {
            string addresses = string.Join(", ", options.Addresses.Select(a => "0x" + a.ToString("X4", CultureInfo.InvariantCulture)));
            int maxFrames = Math.Max(1, options.MaxFrames);
            int sampleEvery = Math.Max(1, options.SampleEveryNFrames);

            return string.Join("\n", new[]
            {
                "-- Auto-generated by FamidashEditor Mesen bridge (NES only)",
                "local addresses = { " + addresses + " }",
                "local maxFrames = " + maxFrames.ToString(CultureInfo.InvariantCulture),
                "local sampleEvery = " + sampleEvery.ToString(CultureInfo.InvariantCulture),
                "",
                "local function capture()",
                "  local state = emu.getState()",
                "  local frame = state['frameCount'] or 0",
                "  if frame % sampleEvery == 0 then",
                "    local parts = { '" + RamCapPrefix.TrimEnd('|') + "', 'f=' .. tostring(frame) }",
                "    for i=1,#addresses do",
                "      local addr = addresses[i]",
                "      local v = emu.read(addr, emu.memType.nesMemory)",
                "      parts[#parts+1] = string.format('%04X=%02X', addr, v)",
                "    end",
                "    print(table.concat(parts, '|'))",
                "  end",
                "  if frame >= maxFrames then",
                "    emu.stop(0)",
                "  end",
                "end",
                "",
                "emu.addEventCallback(capture, emu.eventType.endFrame)",
                ""
            });
        }

        private static int WriteCaptureFile(MesenRamCaptureOptions options, string stdout)
        {
            int count = 0;
            using var writer = new StreamWriter(options.OutputPath, false, new UTF8Encoding(false));

            writer.WriteLine("{\"type\":\"meta\",\"tool\":\"mesen2\",\"mode\":\"nes-testRunner\",\"rom\":\"" + EscapeJson(options.RomPath) + "\",\"frames\":" + options.MaxFrames.ToString(CultureInfo.InvariantCulture) + ",\"sampleEvery\":" + options.SampleEveryNFrames.ToString(CultureInfo.InvariantCulture) + "}");

            using var reader = new StringReader(stdout ?? string.Empty);
            while (true)
            {
                string? line = reader.ReadLine();
                if (line == null) break;
                if (!line.StartsWith(RamCapPrefix, StringComparison.Ordinal)) continue;

                var sampleJson = ParseSampleLineToJson(line);
                if (sampleJson.Length == 0) continue;
                writer.WriteLine(sampleJson);
                count++;
            }

            return count;
        }

        private static string ParseSampleLineToJson(string line)
        {
            // Format: RAMCAP|f=123|00A0=12|00A1=34
            string[] parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return string.Empty;

            int frame = 0;
            var values = new List<string>();
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.StartsWith("f=", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(p.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out frame);
                    continue;
                }

                int eq = p.IndexOf('=');
                if (eq <= 0 || eq >= p.Length - 1) continue;
                string addr = p.Substring(0, eq).ToUpperInvariant();
                string val = p.Substring(eq + 1).ToUpperInvariant();
                values.Add("\"" + EscapeJson(addr) + "\":\"" + EscapeJson(val) + "\"");
            }

            return "{\"type\":\"sample\",\"frame\":" + frame.ToString(CultureInfo.InvariantCulture) + ",\"values\":{" + string.Join(",", values) + "}}";
        }

        private static int TryExtractStopCode(string stdout, int exitCode)
        {
            // In testRunner mode, process exit code is usually the script's emu.stop(code).
            // Keep this helper in case output format changes later.
            return exitCode;
        }

        private static string QuoteArg(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }
    }
}

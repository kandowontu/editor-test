using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;

namespace FamidashEditor
{
    public partial class MainWindow
    {
        private string? mesenPath = null;
        private string? famidashRomPath = null;
        private string mesenRamAddresses = "0000,0001,0002,0003,0010,0011,0012,0013";
        private int mesenCaptureFrames = 1200;
        private int mesenCaptureEveryNFrames = 1;
        private int mesenCaptureTimeoutSeconds = 180;

        private void MenuConfigureMesen_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Select Mesen2 executable",
                    Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
                    CheckFileExists = true,
                };

                try
                {
                    if (!string.IsNullOrWhiteSpace(mesenPath))
                    {
                        dlg.InitialDirectory = Path.GetDirectoryName(mesenPath);
                    }
                    else
                    {
                        string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mesen2");
                        if (Directory.Exists(defaultDir)) dlg.InitialDirectory = defaultDir;
                    }
                }
                catch { }

                if (dlg.ShowDialog(this) == true)
                {
                    mesenPath = dlg.FileName;
                    SaveEditorSettings();
                    if (StatusText != null) StatusText.Text = "Mesen configured: " + Path.GetFileName(mesenPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to configure Mesen: " + ex.Message, "Mesen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuConfigureFamidashRom_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Select Famidash NES ROM",
                    Filter = "NES ROM (*.nes)|*.nes|All files (*.*)|*.*",
                    CheckFileExists = true,
                };

                try
                {
                    if (!string.IsNullOrWhiteSpace(famidashRomPath))
                        dlg.InitialDirectory = Path.GetDirectoryName(famidashRomPath);
                }
                catch { }

                if (dlg.ShowDialog(this) == true)
                {
                    famidashRomPath = dlg.FileName;
                    SaveEditorSettings();
                    if (StatusText != null) StatusText.Text = "Famidash ROM configured: " + Path.GetFileName(famidashRomPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to configure ROM: " + ex.Message, "Mesen", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MenuRunFamidashMesen_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                string exePath = ResolveMesenExePath();

                if (string.IsNullOrWhiteSpace(famidashRomPath) || !File.Exists(famidashRomPath))
                    throw new InvalidOperationException("Famidash ROM is not configured. Use Tools → Mesen (NES) → Configure Famidash ROM.");

                string workDir = Path.GetDirectoryName(exePath)!;
                string luaPath = OverlayLuaPath;
                try
                {
                    RefreshMesenLogStamp();
                    WriteGeneratedOverlayLuaScripts(includeReplay: true);
                }
                catch { luaPath = ""; }

                string args = $"\"{famidashRomPath}\"";
                if (!string.IsNullOrEmpty(luaPath) && File.Exists(luaPath))
                    args += $" \"{luaPath}\"";

                var psi = new ProcessStartInfo(exePath, args)
                {
                    UseShellExecute  = false,
                    CreateNoWindow   = false,
                    WorkingDirectory = workDir,
                };
                var proc = Process.Start(psi);
                if (proc != null)
                    StartMesenRunMonitor(proc);

                if (StatusText != null) StatusText.Text = "Launched Famidash in Mesen.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Mesen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void MenuCaptureRamMesen_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                EnsureMesenConfigured();

                var addrs = ParseAddressList(mesenRamAddresses);
                if (addrs.Count == 0)
                {
                    throw new InvalidOperationException("No valid RAM addresses configured. Set mesenRamAddresses in editor-settings.json (hex list, e.g. 00A0,00A1,00F0). ");
                }

                string outputPath = GetDefaultRamCapturePath();
                var saveDlg = new SaveFileDialog
                {
                    Title = "Save RAM capture output",
                    Filter = "JSON Lines (*.jsonl)|*.jsonl|All files (*.*)|*.*",
                    FileName = Path.GetFileName(outputPath),
                    InitialDirectory = Path.GetDirectoryName(outputPath),
                };

                if (saveDlg.ShowDialog(this) != true) return;
                outputPath = saveDlg.FileName;

                var options = new MesenRamCaptureOptions
                {
                    MesenExePath = ResolveMesenExePath(),
                    RomPath = famidashRomPath!,
                    OutputPath = outputPath,
                    TimeoutSeconds = Math.Max(10, mesenCaptureTimeoutSeconds),
                    MaxFrames = Math.Max(1, mesenCaptureFrames),
                    SampleEveryNFrames = Math.Max(1, mesenCaptureEveryNFrames),
                    Addresses = addrs,
                };

                if (StatusText != null) StatusText.Text = "Capturing RAM via Mesen test runner...";

                var result = await Task.Run(() => MesenNesIntegration.CaptureRam(options));

                string msg = "RAM capture complete.\n\n" +
                             "Samples: " + result.Samples.ToString(CultureInfo.InvariantCulture) + "\n" +
                             "Output: " + outputPath + "\n" +
                             "ExitCode: " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + "\n" +
                             "StopCode: " + result.StopCode.ToString(CultureInfo.InvariantCulture);

                if (!string.IsNullOrWhiteSpace(result.StdErr))
                {
                    msg += "\n\nStderr:\n" + result.StdErr;
                }

                MessageBox.Show(this, msg, "Mesen RAM Capture", MessageBoxButton.OK, MessageBoxImage.Information);
                if (StatusText != null) StatusText.Text = "RAM capture written to: " + Path.GetFileName(outputPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "RAM capture failed: " + ex.Message, "Mesen RAM Capture", MessageBoxButton.OK, MessageBoxImage.Error);
                if (StatusText != null) StatusText.Text = "RAM capture failed.";
            }
        }

        private void EnsureMesenConfigured()
        {
            _ = ResolveMesenExePath();

            if (string.IsNullOrWhiteSpace(famidashRomPath) || !File.Exists(famidashRomPath))
            {
                throw new InvalidOperationException("Famidash ROM is not configured. Use Tools -> Mesen (NES) -> Configure Famidash ROM.");
            }
        }

        private string ResolveMesenExePath()
        {
            // The release-local bundled copy is authoritative. A manually configured
            // path remains only as a fallback for developer machines that have not
            // rebuilt/copied the bundle yet.
            string bundled = LocalRuntimeFolders.MesenExePath;
            if (File.Exists(bundled))
                return bundled;

            if (!string.IsNullOrWhiteSpace(mesenPath) && File.Exists(mesenPath))
                return mesenPath!;

            throw new InvalidOperationException(
                $"Bundled Mesen not found at:\n{bundled}\n\n" +
                "Rebuild/publish the editor to copy Mesen into the local mesen folder.");
        }

        private List<ushort> ParseAddressList(string? csv)
        {
            var result = new List<ushort>();
            if (string.IsNullOrWhiteSpace(csv)) return result;

            string[] tokens = csv.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string t in tokens)
            {
                string s = t.Trim();
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);

                if (ushort.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value))
                {
                    if (!result.Contains(value)) result.Add(value);
                }
            }
            return result;
        }

        private string GetDefaultRamCapturePath()
        {
            string docsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string outFolder = Path.Combine(docsFolder, "Famidash Editor", "mesen-ram-captures");
            Directory.CreateDirectory(outFolder);

            string levelName = "famidash";
            try
            {
                if (!string.IsNullOrWhiteSpace(currentFilePath))
                {
                    levelName = Path.GetFileNameWithoutExtension(currentFilePath) ?? levelName;
                }
            }
            catch { }

            string safeLevel = string.Concat(levelName.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
            if (string.IsNullOrWhiteSpace(safeLevel)) safeLevel = "famidash";

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            return Path.Combine(outFolder, safeLevel + "_ram_" + stamp + ".jsonl");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FamidashEditor
{
    public partial class FmsPlayerWindow : Window
    {
        private readonly FamiStudioIntegration fami;
        private string? currentFmsPath;

        public FmsPlayerWindow()
        {
            InitializeComponent();
            fami = new FamiStudioIntegration();

            BtnOpen.Click += BtnOpen_Click;
            BtnPlay.Click += BtnPlay_Click;
            BtnStop.Click += BtnStop_Click;

            UpdateUiState();
        }

        private void UpdateUiState()
        {
            BtnPlay.IsEnabled = currentFmsPath != null && ComboTracks.Items.Count > 0;
            BtnStop.IsEnabled = fami.IsPlaying;
            TxtPath.Text = currentFmsPath ?? "No file selected";
            TxtStatus.Text = fami.StatusMessage ?? "";
        }

        private void BtnOpen_Click(object? sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "FamiStudio files (*.fms)|*.fms|All files|*.*";
            if (dlg.ShowDialog() == true)
            {
                LoadFms(dlg.FileName);
            }
        }

        private void LoadFms(string path)
        {
            currentFmsPath = path;
            TxtPath.Text = currentFmsPath;
            TxtStatus.Text = "Loading...";
            ComboTracks.Items.Clear();
            ComboTracks.Items.Add("Loading...");
            UpdateUiState();

            // Kick off asynchronous enumeration so UI is instant. We'll update the ComboBox as results arrive.
            List<string>? tracks = null;

            // Try a very fast heuristic parse of the .fms file for song names first
            try
            {
                var parsed = fami.TryParseFmsSongNames(currentFmsPath);
                if (parsed != null && parsed.Count > 0)
                {
                    tracks = parsed;
                    // update UI immediately
                    ComboTracks.Items.Clear();
                    foreach (var t in tracks) ComboTracks.Items.Add(t);
                    if (ComboTracks.Items.Count > 0) ComboTracks.SelectedIndex = 0;
                    TxtStatus.Text = "Tracks parsed from .fms";
                }
            }
            catch { }

            // Now continue discovery in background (in-process loader or CLI probe) to get more reliable list
            _ = Task.Run(() =>
            {
                try
                {
                        if (!fami.IsLoaded)
                        {
                            // Try common candidate relative to the app base
                            string candidate = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "famistudio");
                            if (Directory.Exists(candidate))
                            {
                                fami.LoadFromFolder(candidate);
                            }
                            else
                            {
                            // Walk up parent directories and check likely project locations (handles running from bin/* folders)
                            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                            bool loaded = false;
                            for (int depth = 0; dir != null && depth < 8 && !loaded; depth++)
                            {
                                // Check <parent>/native-windows/libs/famistudio
                                var p1 = Path.Combine(dir.FullName, "native-windows", "libs", "famistudio");
                                if (Directory.Exists(p1))
                                {
                                    try { fami.LoadFromFolder(p1); loaded = fami.IsLoaded; }
                                    catch { }
                                    if (loaded) break;
                                }

                                // Check <parent>/libs/famistudio
                                var p2 = Path.Combine(dir.FullName, "libs", "famistudio");
                                if (Directory.Exists(p2))
                                {
                                    try { fami.LoadFromFolder(p2); loaded = fami.IsLoaded; }
                                    catch { }
                                    if (loaded) break;
                                }

                                // Try to find an executable anywhere under this directory (shallow search)
                                try
                                {
                                    var files = Directory.GetFiles(dir.FullName, "FamiStudio.exe", SearchOption.TopDirectoryOnly);
                                    if (files.Length == 0)
                                    {
                                        // check common subfolder
                                        var sub = Path.Combine(dir.FullName, "famistudio");
                                        if (Directory.Exists(sub)) files = Directory.GetFiles(sub, "FamiStudio.exe", SearchOption.TopDirectoryOnly);
                                    }
                                    if (files.Length > 0)
                                    {
                                        var folder = Path.GetDirectoryName(files[0])!;
                                        try { fami.LoadFromFolder(folder); loaded = fami.IsLoaded; }
                                        catch { }
                                        if (loaded) break;
                                    }
                                }
                                catch { }

                                dir = dir.Parent;
                            }

                            if (!loaded)
                            {
                                try
                                {
                                    string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FamiStudio");
                                    if (Directory.Exists(installed)) fami.LoadFromFolder(installed);
                                }
                                catch { }
                            }
                        }
                    }

                    List<string> discovered = new List<string>();
                    if (fami.IsLoaded)
                    {
                        discovered = fami.EnumerateTracks(currentFmsPath);
                    }

                    if (discovered == null || discovered.Count == 0)
                    {
                        // CLI probe (may take a bit) to find any additional songs
                        discovered = fami.ProbeTracksViaCli(currentFmsPath, 64);
                    }

                    Dispatcher.Invoke(() =>
                    {
                        ComboTracks.Items.Clear();
                        if (discovered != null && discovered.Count > 0)
                        {
                            foreach (var t in discovered) ComboTracks.Items.Add(t);
                        }
                        if (ComboTracks.Items.Count > 0) ComboTracks.SelectedIndex = 0;
                        TxtStatus.Text = fami.StatusMessage ?? "Ready";
                        UpdateUiState();
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => { TxtStatus.Text = "In-process integration failed: " + ex.Message; UpdateUiState(); });
                }
            });
        }

        private async void BtnPlay_Click(object? sender, RoutedEventArgs e)
        {
            if (currentFmsPath == null) return;
            int trackIndex = ComboTracks.SelectedIndex;
            if (trackIndex < 0) trackIndex = 0;
            string? trackName = ComboTracks.SelectedItem?.ToString();

            TxtStatus.Text = "Playing...";
            UpdateUiState();

            try
            {
                await Task.Run(() => fami.PlayTrack(currentFmsPath, trackIndex, trackName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Play failed: " + ex.Message);
            }

            TxtStatus.Text = fami.StatusMessage ?? "Ready";
            UpdateUiState();
        }

        private void BtnStop_Click(object? sender, RoutedEventArgs e)
        {
            fami.Stop();
            TxtStatus.Text = fami.StatusMessage ?? "Stopped";
            UpdateUiState();
        }
    }
}

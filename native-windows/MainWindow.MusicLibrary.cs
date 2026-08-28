using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace FamidashEditor
{
    public partial class MainWindow
    {
        private string? customMusicAlbumPath;
        private string? customMusicSourceAlbumPath;
        private string? customMusicPreviewFolder;

        private async void MenuMusicLibrary_Click(object? sender, RoutedEventArgs e)
        {
            string? sourcePath = customMusicSourceAlbumPath;
            if (string.IsNullOrWhiteSpace(sourcePath))
                sourcePath = MusicLibraryManager.ReadSourceAlbumPath(customMusicAlbumPath);

            var dialog = new MusicLibraryWindow(sourcePath, customMusicPreviewFolder) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            if (dialog.UseBundledAlbum)
            {
                famiIntegration.Stop();
                customMusicAlbumPath = null;
                customMusicSourceAlbumPath = null;
                customMusicPreviewFolder = null;
                albumTxtPath = null;
                mappingLoadedFromFile = false;
                famiIntegration.ConfigurePlaybackFiles(null);
                SaveEditorSettings();
                TryLoadFamiAlbumParsedJson();
                try
                {
                    string simRoot = await Task.Run(() => LocalRuntimeFolders.EnsureSimFamidashFolder());
                    RestoreOriginalMusicAlbumForBuild(GetSimFamidashPaths(simRoot));
                }
                catch { }
                if (StatusText != null) StatusText.Text = "Using the bundled FamiStudio album for editor playback and Mesen builds.";
                return;
            }

            if (!EnsureFamiStudioCommandLineReady())
            {
                MessageBox.Show(this,
                    "FamiStudio 4.4.2 or newer is required to read and export the album.\n\n" +
                    "Use Options → FamiStudio → Configure FamiStudio, then select the folder containing FamiStudio.dll.",
                    "Custom Music Library", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // WPF controls are owned by the dispatcher thread. Snapshot every
            // dialog-backed value before entering Task.Run so the worker never
            // evaluates TextBox.Text or CheckBox.IsChecked through the closure.
            string albumPath = dialog.AlbumPath;
            string? previewFolder = dialog.PreviewFolder;
            bool renderMissingPreviews = dialog.RenderMissingPreviews;

            try
            {
                if (StatusText != null) StatusText.Text = "Music library: importing album...";
                var progress = new Action<string>(message =>
                {
                    try { Dispatcher.BeginInvoke(() => { if (StatusText != null) StatusText.Text = "Music library: " + message; }); } catch { }
                });

                MusicLibraryInstallResult result = await Task.Run(() => MusicLibraryManager.Install(
                    famiIntegration,
                    albumPath,
                    previewFolder,
                    renderMissingPreviews,
                    progress));

                customMusicAlbumPath = result.AlbumPath;
                customMusicSourceAlbumPath = result.SourceAlbumPath;
                customMusicPreviewFolder = previewFolder;
                SaveEditorSettings();
                TryLoadFamiAlbumParsedJson();

                string stagingMessage;
                try
                {
                    string simRoot = await Task.Run(() => LocalRuntimeFolders.EnsureSimFamidashFolder());
                    StageConfiguredMusicAlbumForBuild(GetSimFamidashPaths(simRoot));
                    stagingMessage = "The album is staged for the local Mesen build.";
                }
                catch (Exception stageError)
                {
                    stagingMessage = "The library was installed, but it could not be staged until build time: " + stageError.Message;
                }

                string previewMessage = result.ImportedPreviewCount + " existing preview(s) imported";
                if (renderMissingPreviews) previewMessage += ", " + result.RenderedPreviewCount + " preview(s) rendered";
                if (result.PreviewErrors.Count > 0) previewMessage += $", {result.PreviewErrors.Count} preview(s) failed";

                MessageBox.Show(this,
                    $"Installed {result.Tracks.Count} songs.\n{previewMessage}.\n\n{stagingMessage}\n\n" +
                    "Songs without an imported preview will be rendered to the MP3 cache automatically the first time they are played.",
                    "Custom Music Library", MessageBoxButton.OK,
                    result.PreviewErrors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                if (StatusText != null) StatusText.Text = $"Custom music library ready: {result.Tracks.Count} songs.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not install the custom music library:\n\n" + ex.Message,
                    "Custom Music Library", MessageBoxButton.OK, MessageBoxImage.Error);
                if (StatusText != null) StatusText.Text = "Music library import failed.";
            }
        }

        private bool EnsureFamiStudioCommandLineReady()
        {
            var candidates = new[]
            {
                famiStudioPath,
                Path.Combine(AppContext.BaseDirectory, "FamiStudio"),
                Path.Combine(AppContext.BaseDirectory, "libs", "famistudio"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FamiStudio"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FamiStudio")
            };

            string? folder = candidates.FirstOrDefault(candidate =>
            {
                if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate)) return false;
                return File.Exists(Path.Combine(candidate, "FamiStudio.dll")) || File.Exists(Path.Combine(candidate, "FamiStudio.exe"));
            });
            if (string.IsNullOrWhiteSpace(folder)) return false;

            famiStudioPath = Path.GetFullPath(folder);
            // LoadFromFolder also configures the CLI folder. Reflective in-process loading is
            // optional; command-line export still works if a dependency blocks that loader.
            try { famiIntegration.LoadFromFolder(famiStudioPath); } catch { }
            return true;
        }

        private void StageConfiguredMusicAlbumForBuild(SimFamidashPaths paths)
        {
            string? albumToStage = customMusicAlbumPath;
            if (string.IsNullOrWhiteSpace(albumToStage)) return;
            if (!File.Exists(albumToStage))
                throw new FileNotFoundException("The configured custom album is missing. Open the Custom Music Library and reinstall it.", albumToStage);
            if (!File.Exists(paths.MusicMetadata))
                throw new FileNotFoundException("Local Mesen music metadata was not found.", paths.MusicMetadata);

            string metadata = File.ReadAllText(paths.MusicMetadata);
            var match = Regex.Match(metadata, @"\bsongModule\s*:\s*""([^""]+\.fms)""", RegexOptions.IgnoreCase);
            if (!match.Success)
                throw new InvalidOperationException("The local Mesen music metadata has no songModule .fms entry.");

            string moduleName = match.Groups[1].Value.Trim();
            if (!string.Equals(moduleName, Path.GetFileName(moduleName), StringComparison.Ordinal))
                throw new InvalidOperationException("The music metadata songModule must be a filename, not a path.");

            string modulesFolder = Path.Combine(paths.Root, "MUSIC", "MODULES");
            Directory.CreateDirectory(modulesFolder);
            string destination = Path.Combine(modulesFolder, moduleName);
            string backup = GetOriginalModuleBackupPath(moduleName);
            if (File.Exists(destination) && !File.Exists(backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup) ?? MusicLibraryManager.LibraryFolder);
                File.Copy(destination, backup, overwrite: false);
            }
            if (!Path.GetFullPath(destination).Equals(Path.GetFullPath(albumToStage), StringComparison.OrdinalIgnoreCase))
                File.Copy(albumToStage, destination, overwrite: true);
        }

        private static void RestoreOriginalMusicAlbumForBuild(SimFamidashPaths paths)
        {
            if (!File.Exists(paths.MusicMetadata)) return;
            string metadata = File.ReadAllText(paths.MusicMetadata);
            var match = Regex.Match(metadata, @"\bsongModule\s*:\s*""([^""]+\.fms)""", RegexOptions.IgnoreCase);
            if (!match.Success) return;
            string moduleName = Path.GetFileName(match.Groups[1].Value.Trim());
            string backup = GetOriginalModuleBackupPath(moduleName);
            if (!File.Exists(backup)) return; // No custom replacement was made by this feature.
            string destination = Path.Combine(paths.Root, "MUSIC", "MODULES", moduleName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? paths.Root);
            File.Copy(backup, destination, overwrite: true);
        }

        private static string GetOriginalModuleBackupPath(string moduleName) =>
            Path.Combine(MusicLibraryManager.LibraryFolder, "Original Mesen Modules", Path.GetFileName(moduleName));
    }
}

using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;

namespace FamidashEditor
{
    public partial class MusicLibraryWindow : Window
    {
        public string AlbumPath => AlbumPathBox.Text.Trim();
        public string? PreviewFolder => string.IsNullOrWhiteSpace(PreviewFolderBox.Text) ? null : PreviewFolderBox.Text.Trim();
        public bool RenderMissingPreviews => RenderPreviewsCheckBox.IsChecked == true;
        public bool UseBundledAlbum { get; private set; }

        public MusicLibraryWindow(string? currentSourceAlbum, string? currentPreviewFolder)
        {
            InitializeComponent();
            AlbumPathBox.Text = currentSourceAlbum ?? string.Empty;
            PreviewFolderBox.Text = currentPreviewFolder ?? string.Empty;
            BrowseAlbumButton.Click += BrowseAlbumButton_Click;
            BrowsePreviewButton.Click += BrowsePreviewButton_Click;
            InstallButton.Click += InstallButton_Click;
            UseBundledButton.Click += UseBundledButton_Click;
            CancelButton.Click += (_, _) => DialogResult = false;
        }

        private void BrowseAlbumButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select a FamiStudio album",
                Filter = "FamiStudio project (*.fms)|*.fms|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            try
            {
                if (File.Exists(AlbumPath)) dialog.InitialDirectory = Path.GetDirectoryName(AlbumPath);
            }
            catch { }
            if (dialog.ShowDialog(this) == true) AlbumPathBox.Text = dialog.FileName;
        }

        private void BrowsePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Optional folder containing MP3/WAV/OGG previews",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            try { if (Directory.Exists(PreviewFolder)) dialog.SelectedPath = PreviewFolder; } catch { }
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) PreviewFolderBox.Text = dialog.SelectedPath;
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(AlbumPath) || !Path.GetExtension(AlbumPath).Equals(".fms", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Choose an existing FamiStudio .fms project.", "Custom Music Library", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!string.IsNullOrWhiteSpace(PreviewFolder) && !Directory.Exists(PreviewFolder))
            {
                MessageBox.Show(this, "The audio preview folder does not exist.", "Custom Music Library", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            UseBundledAlbum = false;
            DialogResult = true;
        }

        private void UseBundledButton_Click(object sender, RoutedEventArgs e)
        {
            UseBundledAlbum = true;
            DialogResult = true;
        }
    }
}

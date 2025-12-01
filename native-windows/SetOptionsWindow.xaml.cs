using System.Windows;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace FamidashEditor
{
    // Shared data class for sprite offset entries from JSON
    public class ObjectOffsetEntry
    {
        public object? coordinates { get; set; } // Can be [int, int] or [[int, int], [int, int], ...]
        public int? offsetX { get; set; }
        public int? offsetY { get; set; }
    }

    public partial class SetOptionsWindow : Window
    {
        public string SelectedDeco { get; private set; } = "DECO1";
        public string SelectedBlockSet { get; private set; } = "BLOCKSA";
        public string SelectedSpikeSet { get; private set; } = "SPIKESA";
        public bool LockSpritesToSet { get; private set; } = false;
        public SetOptionsWindow(string current, string currentBlock, string currentSpike)
        {
            InitializeComponent();
            // select current
            foreach (var item in DecoCombo.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem cbi && (string)cbi.Content == current)
                {
                    DecoCombo.SelectedItem = item;
                    break;
                }
            }
            // select block combo
            foreach (var item in BlockCombo.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem cbi && (string)cbi.Content == currentBlock)
                {
                    BlockCombo.SelectedItem = item; break;
                }
            }
            // select spike combo
            foreach (var item in SpikeCombo.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem cbi && (string)cbi.Content == currentSpike)
                {
                    SpikeCombo.SelectedItem = item; break;
                }
            }
            OkButton.Click += (s, e) => {
                if (DecoCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi2) SelectedDeco = (string)cbi2.Content;
                if (BlockCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi3) SelectedBlockSet = (string)cbi3.Content;
                if (SpikeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi4) SelectedSpikeSet = (string)cbi4.Content;
                this.DialogResult = true;
            };
            CancelButton.Click += (s, e) => { this.DialogResult = false; };

            // Wire quick tint buttons to call methods on owner MainWindow
            BgTintButton.Click += (s, e) =>
            {
                try
                {
                    if (this.Owner is MainWindow mw) mw.ShowBackgroundTintPicker();
                }
                catch { }
            };
            GroundTintButton.Click += (s, e) =>
            {
                try
                {
                    if (this.Owner is MainWindow mw) mw.ShowGroundTintPicker();
                }
                catch { }
            };
            TileTintButton.Click += (s, e) =>
            {
                try
                {
                    if (this.Owner is MainWindow mw) mw.ShowTileTintPicker();
                }
                catch { }
            };

            // Wire up Attempt JSON Load button
            AttemptJsonLoadButton.Click += (s, e) =>
            {
                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        AttemptJsonLoad(mw);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading JSON: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            // Owner is set by caller via object-initializer after constructor completes.
            // Wire up the NoParallax checkbox in Loaded so Owner is available.
            this.Loaded += (s, e) =>
            {
                try
                {
                    if (this.Owner is MainWindow mw && NoParallaxCheckBox != null)
                    {
                        var opt = mw.MenuOptionNoParallax;
                        NoParallaxCheckBox.IsChecked = (opt != null && opt.IsChecked == true);
                        NoParallaxCheckBox.Checked += (ss, ee) => { try { if (this.Owner is MainWindow mw2) mw2.SetNoParallax(true); } catch { } };
                        NoParallaxCheckBox.Unchecked += (ss, ee) => { try { if (this.Owner is MainWindow mw2) mw2.SetNoParallax(false); } catch { } };
                    }
                    // Initialize Show Accurate Tileset checkbox and wire immediate updates to owner
                    if (this.Owner is MainWindow mwAcc && ShowAccurateTilesetCheckBox != null)
                    {
                        try { ShowAccurateTilesetCheckBox.IsChecked = mwAcc.ShowAccurateTileset; } catch { ShowAccurateTilesetCheckBox.IsChecked = false; }
                        ShowAccurateTilesetCheckBox.Checked += (ss, ee) => { try { if (this.Owner is MainWindow mw2) { var b = (BlockCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string; var s2 = (SpikeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string; mw2.SetShowAccurateTileset(true, b, s2); } } catch { } };
                        ShowAccurateTilesetCheckBox.Unchecked += (ss, ee) => { try { if (this.Owner is MainWindow mw2) { var b = (BlockCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string; var s2 = (SpikeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string; mw2.SetShowAccurateTileset(false, b, s2); } } catch { } };
                        // When block or spike selection changes, apply tileset immediately if option enabled
                        SpikeCombo.SelectionChanged += (ss, ee) => {
                            try {
                                var sel = (SpikeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string;
                                if (!string.IsNullOrEmpty(sel)) SelectedSpikeSet = sel;
                                if (this.Owner is MainWindow mw3)
                                {
                                    mw3.SetSpikeSet(sel ?? "SPIKESA");
                                }
                            } catch { }
                        };
                        BlockCombo.SelectionChanged += (ss, ee) => {
                            try {
                                var sel = (BlockCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string;
                                if (!string.IsNullOrEmpty(sel)) SelectedBlockSet = sel;
                                if (this.Owner is MainWindow mw3)
                                {
                                    mw3.SetBlockSet(sel ?? "BLOCKSA");
                                }
                            } catch { }
                        };
                    }
                    // Initialize LockSprites checkbox and wire immediate updates to owner
                    if (this.Owner is MainWindow mw2 && LockSpritesCheckBox != null)
                    {
                        try { LockSpritesCheckBox.IsChecked = mw2.LockSpritesToSet; } catch { LockSpritesCheckBox.IsChecked = false; }
                        LockSpritesCheckBox.Checked += (ss, ee) => { try { if (this.Owner is MainWindow mw3) { var selected = (DecoCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string; mw3.SetLockSpritesToSet(true, selected); LockSpritesToSet = true; } } catch { } };
                        LockSpritesCheckBox.Unchecked += (ss, ee) => { try { if (this.Owner is MainWindow mw3) { var selected = (DecoCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string; mw3.SetLockSpritesToSet(false, selected); LockSpritesToSet = false; } } catch { } };

                        // When the deco selection changes in this dialog, apply immediately and save to per-level config
                        DecoCombo.SelectionChanged += (ss, ee) => {
                            try {
                                var sel = (DecoCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string;
                                // Update own SelectedDeco for OK
                                if (!string.IsNullOrEmpty(sel)) SelectedDeco = sel;
                                if (this.Owner is MainWindow mw4)
                                {
                                    mw4.SetDecoSet(sel ?? "DECO1");
                                    // If lock is enabled, re-apply disabled sprites for the new deco
                                    if (mw4.LockSpritesToSet) mw4.SetLockSpritesToSet(mw4.LockSpritesToSet, sel);
                                }
                            } catch { }
                        };
                    }
                }
                catch { }
            };
        }

        private void AttemptJsonLoad(MainWindow mainWindow)
        {
            try
            {
                // Read the JSON5 file from the application directory
                var assembly = Assembly.GetExecutingAssembly();
                var appDirectory = Path.GetDirectoryName(assembly.Location);
                var jsonFilePath = Path.Combine(appDirectory!, "lvlset_HUGE_metadata.json5");
                
                string json5Content;
                
                if (!File.Exists(jsonFilePath))
                {
                    MessageBox.Show($"JSON metadata file not found at: {jsonFilePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                json5Content = File.ReadAllText(jsonFilePath);

                // Convert JSON5 to standard JSON
                string jsonContent;
                try
                {
                    jsonContent = ConvertJson5ToJson(json5Content);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to convert JSON5: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Parse the JSON
                LevelMetadata? metadata;
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        AllowTrailingCommas = true,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    };
                    metadata = JsonSerializer.Deserialize<LevelMetadata>(jsonContent, options);
                }
                catch (Exception ex)
                {
                    // Save the converted JSON to a temp file for debugging
                    string tempPath = Path.Combine(Path.GetTempPath(), "converted_json_debug.json");
                    File.WriteAllText(tempPath, jsonContent);
                    MessageBox.Show($"JSON parse error: {ex.Message}\n\nConverted JSON saved to: {tempPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                
                if (metadata == null)
                {
                    MessageBox.Show("Invalid JSON format - metadata is null.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Get the current TMX filename (without extension)
                string currentTmxPath = mainWindow.GetCurrentTmxPath();
                if (string.IsNullOrEmpty(currentTmxPath))
                {
                    MessageBox.Show("No TMX file is currently loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string levelName = Path.GetFileNameWithoutExtension(currentTmxPath).ToLower();

                // Find matching level in JSON - search both official and community levels
                LevelData? levelData = null;
                
                if (metadata.official_levels != null)
                {
                    levelData = metadata.official_levels.FirstOrDefault(l => l.level?.ToLower() == levelName);
                }
                
                if (levelData == null && metadata.community_levels != null)
                {
                    levelData = metadata.community_levels.FirstOrDefault(l => l.level?.ToLower() == levelName);
                }
                if (levelData == null)
                {
                    MessageBox.Show("Level not found in JSON.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Load the data
                bool dataChanged = false;

                // Set sprite/deco set
                if (!string.IsNullOrEmpty(levelData.decoType))
                {
                    mainWindow.SetDecoSet(levelData.decoType);
                    // Update combo box
                    foreach (var item in DecoCombo.Items)
                    {
                        if (item is System.Windows.Controls.ComboBoxItem cbi && (string)cbi.Content == levelData.decoType)
                        {
                            DecoCombo.SelectedItem = item;
                            break;
                        }
                    }
                    SelectedDeco = levelData.decoType;
                    dataChanged = true;
                }

                // Set block set
                if (!string.IsNullOrEmpty(levelData.blockSet))
                {
                    string blockSet = $"BLOCKS{levelData.blockSet}";
                    mainWindow.SetBlockSet(blockSet);
                    foreach (var item in BlockCombo.Items)
                    {
                        if (item is System.Windows.Controls.ComboBoxItem cbi && (string)cbi.Content == blockSet)
                        {
                            BlockCombo.SelectedItem = item;
                            break;
                        }
                    }
                    SelectedBlockSet = blockSet;
                    dataChanged = true;
                }

                // Set spike set
                if (!string.IsNullOrEmpty(levelData.spikeSet))
                {
                    string spikeSet = $"SPIKES{levelData.spikeSet}";
                    mainWindow.SetSpikeSet(spikeSet);
                    foreach (var item in SpikeCombo.Items)
                    {
                        if (item is System.Windows.Controls.ComboBoxItem cbi && (string)cbi.Content == spikeSet)
                        {
                            SpikeCombo.SelectedItem = item;
                            break;
                        }
                    }
                    SelectedSpikeSet = spikeSet;
                    dataChanged = true;
                }

                // Set parallax option
                if (levelData.parallaxDisable.HasValue)
                {
                    mainWindow.SetNoParallax(levelData.parallaxDisable.Value);
                    if (NoParallaxCheckBox != null)
                    {
                        NoParallaxCheckBox.IsChecked = levelData.parallaxDisable.Value;
                    }
                    dataChanged = true;
                }

                // Apply sprite object offsets
                if (levelData.objectOffsets != null && levelData.objectOffsets.Length > 0)
                {
                    mainWindow.ApplySpriteOffsets(levelData.objectOffsets);
                    dataChanged = true;
                }

                if (dataChanged)
                {
                    // Save to level-specific config file
                    mainWindow.SaveCurrentTmxConfig();
                    MessageBox.Show("Settings loaded successfully from JSON and saved to config.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading JSON: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ConvertJson5ToJson(string json5)
        {
            try
            {
                // Remove C-style single-line comments, but preserve the line structure
                // This handles comments at end of lines with values
                var lines = json5.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    // Find // comments and remove them
                    int commentIndex = lines[i].IndexOf("//");
                    if (commentIndex >= 0)
                    {
                        lines[i] = lines[i].Substring(0, commentIndex);
                    }
                }
                json5 = string.Join("\n", lines);

                // Remove block comments (/* */)
                json5 = Regex.Replace(json5, @"/\*.*?\*/", "", RegexOptions.Singleline);

                // Quote unquoted property names
                // Match property names that are not already quoted
                json5 = Regex.Replace(json5, @"([{,]\s*)([a-zA-Z_][a-zA-Z0-9_]*)\s*:", @"$1""$2"":");

                // Convert hex numbers to decimal (0x format)
                json5 = Regex.Replace(json5, @"0x([0-9A-Fa-f]+)", match =>
                {
                    return Convert.ToInt32(match.Groups[1].Value, 16).ToString();
                });

                // Remove leading + signs from positive numbers (JSON doesn't allow +8, only 8)
                json5 = Regex.Replace(json5, @":\s*\+(\d+)", ": $1");
                json5 = Regex.Replace(json5, @",\s*\+(\d+)", ", $1");

                // Remove trailing commas before closing brackets/braces
                json5 = Regex.Replace(json5, @",\s*([}\]])", "$1");

                return json5;
            }
            catch (Exception ex)
            {
                throw new Exception($"JSON5 conversion failed: {ex.Message}");
            }
        }

        // JSON data classes
        private class LevelMetadata
        {
            public LevelData[]? official_levels { get; set; }
            public LevelData[]? community_levels { get; set; }
        }

        private class LevelData
        {
            public string? level { get; set; }
            public string? decoType { get; set; }
            public string? spikeSet { get; set; }
            public string? blockSet { get; set; }
            public string? sawSet { get; set; }
            public bool? parallaxDisable { get; set; }
            public ObjectOffsetEntry[]? objectOffsets { get; set; }
        }
    }
}

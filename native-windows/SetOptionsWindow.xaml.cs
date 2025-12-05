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
        public int SelectedStartingSpeedUiIndex { get; private set; } = 1; // UI indices: 0=0.5x,1=1x,2=2x,3=3x,4=4x
        
        // Store original values for cancel functionality
        private string originalDeco = "DECO1";
        private string originalBlockSet = "BLOCKSA";
        private string originalSpikeSet = "SPIKESA";
        private bool originalLockSprites = false;
        private bool originalShowAccurateTileset = false;
        private bool originalNoParallax = false;
        
        public SetOptionsWindow(string current, string currentBlock, string currentSpike)
        {
            InitializeComponent();
            
            // Store original values for cancel functionality
            originalDeco = current;
            originalBlockSet = currentBlock;
            originalSpikeSet = currentSpike;
            
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
                // Apply all changes when OK is clicked
                if (DecoCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi2) SelectedDeco = (string)cbi2.Content;
                if (BlockCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi3) SelectedBlockSet = (string)cbi3.Content;
                if (SpikeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi4) SelectedSpikeSet = (string)cbi4.Content;
                
                // Apply changes to MainWindow
                if (this.Owner is MainWindow mw)
                {
                    // Apply deco set
                    if (SelectedDeco != originalDeco)
                    {
                        mw.SetDecoSet(SelectedDeco);
                    }
                    
                    // Apply block set
                    if (SelectedBlockSet != originalBlockSet)
                    {
                        mw.SetBlockSet(SelectedBlockSet);
                    }
                    
                    // Apply spike set
                    if (SelectedSpikeSet != originalSpikeSet)
                    {
                        mw.SetSpikeSet(SelectedSpikeSet);
                    }
                    
                    // Apply lock sprites setting
                    if (LockSpritesCheckBox?.IsChecked == true)
                    {
                        mw.SetLockSpritesToSet(true, SelectedDeco);
                    }
                    else if (LockSpritesCheckBox?.IsChecked == false && originalLockSprites)
                    {
                        mw.SetLockSpritesToSet(false, SelectedDeco);
                    }
                    
                    // Apply show accurate tileset
                    if (ShowAccurateTilesetCheckBox?.IsChecked != originalShowAccurateTileset)
                    {
                        mw.SetShowAccurateTileset(ShowAccurateTilesetCheckBox?.IsChecked == true, SelectedBlockSet, SelectedSpikeSet);
                    }
                    
                    // Apply no parallax setting
                    if (NoParallaxCheckBox?.IsChecked != originalNoParallax)
                    {
                        mw.SetNoParallax(NoParallaxCheckBox?.IsChecked == true);
                    }
                    
                    // Save starting speed selection back to main window before saving config
                    try
                    {
                        if (StartingSpeedCombo != null && StartingSpeedCombo.SelectedIndex >= 0)
                        {
                            SelectedStartingSpeedUiIndex = StartingSpeedCombo.SelectedIndex;
                            mw.LoadedStartingSpeedUiIndex = SelectedStartingSpeedUiIndex;
                        }
                    }
                    catch { }

                    // Save config
                    mw.SaveCurrentTmxConfig();
                }
                
                this.DialogResult = true;
            };
            CancelButton.Click += (s, e) => {
                // Revert all changes when Cancel is clicked
                if (this.Owner is MainWindow mw)
                {
                    // Revert deco set
                    if (DecoCombo.SelectedItem is System.Windows.Controls.ComboBoxItem currentDeco && 
                        (string)currentDeco.Content != originalDeco)
                    {
                        mw.SetDecoSet(originalDeco);
                    }
                    
                    // Revert block set
                    if (BlockCombo.SelectedItem is System.Windows.Controls.ComboBoxItem currentBlock && 
                        (string)currentBlock.Content != originalBlockSet)
                    {
                        mw.SetBlockSet(originalBlockSet);
                    }
                    
                    // Revert spike set
                    if (SpikeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem currentSpike && 
                        (string)currentSpike.Content != originalSpikeSet)
                    {
                        mw.SetSpikeSet(originalSpikeSet);
                    }
                    
                    // Revert lock sprites
                    if (LockSpritesCheckBox?.IsChecked != originalLockSprites)
                    {
                        mw.SetLockSpritesToSet(originalLockSprites, originalDeco);
                    }
                    
                    // Revert show accurate tileset
                    if (ShowAccurateTilesetCheckBox?.IsChecked != originalShowAccurateTileset)
                    {
                        mw.SetShowAccurateTileset(originalShowAccurateTileset, originalBlockSet, originalSpikeSet);
                    }
                    
                    // Revert no parallax
                    if (NoParallaxCheckBox?.IsChecked != originalNoParallax)
                    {
                        mw.SetNoParallax(originalNoParallax);
                    }
                }
                
                this.DialogResult = false;
            };

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

            // Wire up Remove Sprite Shifts button
            RemoveSpriteShiftsButton.Click += (s, e) =>
            {
                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        RemoveAllSpriteShifts(mw);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error removing sprite shifts: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            // Wire up Export Shift JSON button
            ExportShiftJsonButton.Click += (s, e) =>
            {
                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        ExportShiftJson(mw);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting shift JSON: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        originalNoParallax = (opt != null && opt.IsChecked == true);
                        NoParallaxCheckBox.IsChecked = originalNoParallax;
                        // Note: No immediate handlers - changes applied only on OK
                    }
                    // Initialize Show Accurate Tileset checkbox
                    if (this.Owner is MainWindow mwAcc && ShowAccurateTilesetCheckBox != null)
                    {
                        try { originalShowAccurateTileset = mwAcc.ShowAccurateTileset; } catch { originalShowAccurateTileset = false; }
                        ShowAccurateTilesetCheckBox.IsChecked = originalShowAccurateTileset;
                        // Note: No immediate handlers - changes applied only on OK
                    }
                    // Initialize LockSprites checkbox
                    if (this.Owner is MainWindow mw2 && LockSpritesCheckBox != null)
                    {
                        try { originalLockSprites = mw2.LockSpritesToSet; } catch { originalLockSprites = false; }
                        LockSpritesCheckBox.IsChecked = originalLockSprites;
                        LockSpritesToSet = originalLockSprites;
                        // Note: No immediate handlers - changes applied only on OK
                        
                        // Track changes to LockSpritesToSet property for when OK is clicked
                        LockSpritesCheckBox.Checked += (ss, ee) => { LockSpritesToSet = true; };
                        LockSpritesCheckBox.Unchecked += (ss, ee) => { LockSpritesToSet = false; };
                    }

                    // Wire immediate-preview + persist handlers so changes apply and save right away
                    try
                    {
                        if (this.Owner is MainWindow mwOwner)
                        {
                            if (DecoCombo != null)
                            {
                                DecoCombo.SelectionChanged += (ss, ee) =>
                                {
                                    try
                                    {
                                        if (DecoCombo.SelectedItem is System.Windows.Controls.ComboBoxItem sel)
                                        {
                                            var val = (string)sel.Content;
                                            mwOwner.SetDecoSet(val);
                                        }
                                    }
                                    catch { }
                                };
                            }

                            if (BlockCombo != null)
                            {
                                BlockCombo.SelectionChanged += (ss, ee) =>
                                {
                                    try
                                    {
                                        if (BlockCombo.SelectedItem is System.Windows.Controls.ComboBoxItem sel)
                                        {
                                            var val = (string)sel.Content;
                                            mwOwner.SetBlockSet(val);
                                        }
                                    }
                                    catch { }
                                };
                            }

                            if (SpikeCombo != null)
                            {
                                SpikeCombo.SelectionChanged += (ss, ee) =>
                                {
                                    try
                                    {
                                        if (SpikeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem sel)
                                        {
                                            var val = (string)sel.Content;
                                            mwOwner.SetSpikeSet(val);
                                        }
                                    }
                                    catch { }
                                };
                            }

                            if (NoParallaxCheckBox != null)
                            {
                                NoParallaxCheckBox.Checked += (ss, ee) => { try { mwOwner.SetNoParallax(true); } catch { } };
                                NoParallaxCheckBox.Unchecked += (ss, ee) => { try { mwOwner.SetNoParallax(false); } catch { } };
                            }

                            if (ShowAccurateTilesetCheckBox != null)
                            {
                                ShowAccurateTilesetCheckBox.Checked += (ss, ee) =>
                                {
                                    try { mwOwner.SetShowAccurateTileset(true, (BlockCombo?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string, (SpikeCombo?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string); } catch { }
                                };
                                ShowAccurateTilesetCheckBox.Unchecked += (ss, ee) =>
                                {
                                    try { mwOwner.SetShowAccurateTileset(false, (BlockCombo?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string, (SpikeCombo?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string); } catch { }
                                };
                            }

                            // Wire StartingSpeed combo: initialize from owner's loaded value and persist on change
                            if (StartingSpeedCombo != null)
                            {
                                try { StartingSpeedCombo.SelectedIndex = mwOwner.LoadedStartingSpeedUiIndex; } catch { }
                                StartingSpeedCombo.SelectionChanged += (ss, ee) =>
                                {
                                    try
                                    {
                                        if (StartingSpeedCombo.SelectedIndex >= 0)
                                        {
                                            mwOwner.LoadedStartingSpeedUiIndex = StartingSpeedCombo.SelectedIndex;
                                            try { mwOwner.SaveCurrentTmxConfig(); } catch { }
                                        }
                                    }
                                    catch { }
                                };
                            }
                        }
                    }
                    catch { }
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
                
                // Set song selection
                if (!string.IsNullOrEmpty(levelData.songID))
                {
                    mainWindow.SetSongFromMetadata(levelData.songID);
                    dataChanged = true;
                }

                // Apply starting speed metadata if present. Metadata numeric codes map as:
                // 0 -> 1x, 1 -> 0.5x, 2 -> 2x, 3 -> 3x, 4 -> 4x
                if (levelData.startingSpeed.HasValue)
                {
                    try
                    {
                        int jsonVal = levelData.startingSpeed.Value;
                        int uiIndex = (jsonVal == 1) ? 0 : (jsonVal == 0) ? 1 : jsonVal;
                        mainWindow.LoadedStartingSpeedUiIndex = uiIndex;
                        if (StartingSpeedCombo != null) StartingSpeedCombo.SelectedIndex = uiIndex;
                        dataChanged = true;
                    }
                    catch { }
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

        private void RemoveAllSpriteShifts(MainWindow mainWindow)
        {
            try
            {
                int count = mainWindow.GetSpriteOffsetCount();
                if (count == 0)
                {
                    MessageBox.Show("No sprite shifts found on this map.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var result = MessageBox.Show(
                    $"Are you sure you want to remove all {count} sprite shift(s) from this map?\n\nThis action cannot be undone.",
                    "Confirm Remove Sprite Shifts",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    mainWindow.RemoveAllSpriteOffsets();
                    MessageBox.Show($"Successfully removed {count} sprite shift(s).", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing sprite shifts: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportShiftJson(MainWindow mainWindow)
        {
            try
            {
                var offsets = mainWindow.GetSpriteOffsets();
                if (offsets.Count == 0)
                {
                    MessageBox.Show("No sprite shifts found on this map.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Get map dimensions
                int mapWidth = mainWindow.MapWidth;
                int mapHeight = mainWindow.MapHeight;

                // Group offsets by their offset values
                var groupedOffsets = new Dictionary<(int offsetX, int offsetY), List<(int x, int y)>>();
                
                foreach (var kvp in offsets)
                {
                    int posIndex = kvp.Key;
                    int x = posIndex % mapWidth;
                    int y = posIndex / mapWidth;
                    var offset = kvp.Value;
                    
                    var key = (offset.offsetX, offset.offsetY);
                    if (!groupedOffsets.ContainsKey(key))
                    {
                        groupedOffsets[key] = new List<(int x, int y)>();
                    }
                    groupedOffsets[key].Add((x, y));
                }

                // Build JSON5 output
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("\t\t\tobjectOffsets: [");
                
                bool firstGroup = true;
                foreach (var group in groupedOffsets)
                {
                    if (!firstGroup)
                    {
                        sb.AppendLine(",");
                    }
                    firstGroup = false;
                    
                    sb.AppendLine("\t\t\t\t{");
                    
                    // Write coordinates
                    var coords = group.Value;
                    if (coords.Count == 1)
                    {
                        // Single coordinate: [x, y]
                        sb.AppendLine($"\t\t\t\t\tcoordinates: [{coords[0].x}, {coords[0].y}],");
                    }
                    else
                    {
                        // Multiple coordinates: [[x, y], [x, y], ...]
                        sb.AppendLine("\t\t\t\t\tcoordinates: [");
                        for (int i = 0; i < coords.Count; i++)
                        {
                            string separator = (i < coords.Count - 1) ? "," : "";
                            sb.AppendLine($"\t\t\t\t\t\t[{coords[i].x}, {coords[i].y}]{separator}");
                        }
                        sb.AppendLine("\t\t\t\t\t],");
                    }
                    
                    // Write offsets
                    if (group.Key.offsetX != 0 && group.Key.offsetY != 0)
                    {
                        string offsetXStr = group.Key.offsetX >= 0 ? $"+{group.Key.offsetX}" : group.Key.offsetX.ToString();
                        string offsetYStr = group.Key.offsetY >= 0 ? $"+{group.Key.offsetY}" : group.Key.offsetY.ToString();
                        sb.AppendLine($"\t\t\t\t\toffsetY: {offsetYStr},");
                        sb.AppendLine($"\t\t\t\t\toffsetX: {offsetXStr}");
                    }
                    else if (group.Key.offsetY != 0)
                    {
                        string offsetYStr = group.Key.offsetY >= 0 ? $"+{group.Key.offsetY}" : group.Key.offsetY.ToString();
                        sb.AppendLine($"\t\t\t\t\toffsetY: {offsetYStr}");
                    }
                    else if (group.Key.offsetX != 0)
                    {
                        string offsetXStr = group.Key.offsetX >= 0 ? $"+{group.Key.offsetX}" : group.Key.offsetX.ToString();
                        sb.AppendLine($"\t\t\t\t\toffsetX: {offsetXStr}");
                    }
                    
                    sb.Append("\t\t\t\t}");
                }
                
                sb.AppendLine();
                sb.AppendLine("\t\t\t]");

                // Get current TMX filename
                string currentTmxPath = mainWindow.GetCurrentTmxPath();
                string levelName = string.IsNullOrEmpty(currentTmxPath) ? "level" : Path.GetFileNameWithoutExtension(currentTmxPath);

                // Save to Documents folder
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string fileName = $"{levelName}_sprite_shifts.json5";
                string filePath = Path.Combine(documentsPath, fileName);

                File.WriteAllText(filePath, sb.ToString());

                // Open the file automatically
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // If opening fails, just show the path
                }

                MessageBox.Show($"Exported {offsets.Count} sprite shift(s) to:\n{filePath}\n\nThe file has been opened in your default text editor.", 
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting shift JSON: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            public string? songID { get; set; }
            // Optional starting speed metadata. Values are numeric codes per the metadata spec:
            // 0 -> 1x, 1 -> 0.5x, 2 -> 2x, 3 -> 3x, 4 -> 4x
            public int? startingSpeed { get; set; }
        }
    }
}

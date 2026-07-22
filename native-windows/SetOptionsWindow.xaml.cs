using System.Windows;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Globalization;

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
        public int? SelectedStartingBackgroundColor { get; private set; } = null;
        public int? SelectedStartingGroundColor { get; private set; } = null;
        
        // Flag to prevent saves during initialization
        private bool _isInitializing = true;
        
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

                    // Save starting background/ground color selections (hex strings -> int)
                    try
                    {
                        if (StartingBackgroundColorCombo != null && StartingBackgroundColorCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbiBg && cbiBg.Content is string sBg)
                        {
                            try { mw.LoadedStartingBackgroundColor = int.Parse(sBg.Replace("0x", ""), NumberStyles.HexNumber); } catch { mw.LoadedStartingBackgroundColor = null; }
                        }
                        if (StartingGroundColorCombo != null && StartingGroundColorCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbiG && cbiG.Content is string sG)
                        {
                            try { mw.LoadedStartingGroundColor = int.Parse(sG.Replace("0x", ""), NumberStyles.HexNumber); } catch { mw.LoadedStartingGroundColor = null; }
                        }
                    }
                    catch { }

                    // Read optional hex fields and store into main window loaded values
                    try
                    {
                        if (SpawnYPositionHiTextBox != null && !string.IsNullOrWhiteSpace(SpawnYPositionHiTextBox.Text))
                        {
                            string t = SpawnYPositionHiTextBox.Text.Trim(); t = t.Replace("0x", "").Replace("0X", "");
                            if (byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte bv)) mw.LoadedSpawnYPositionHi = bv;
                            else mw.LoadedSpawnYPositionHi = null;
                        }
                        else mw.LoadedSpawnYPositionHi = null;

                        if (ScrollYPositionLowTextBox != null && !string.IsNullOrWhiteSpace(ScrollYPositionLowTextBox.Text))
                        {
                            string t = ScrollYPositionLowTextBox.Text.Trim(); t = t.Replace("0x", "").Replace("0X", "");
                            if (byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte bv4)) mw.LoadedScrollYPositionLow = bv4;
                            else mw.LoadedScrollYPositionLow = null;
                        }
                        else mw.LoadedScrollYPositionLow = null;
                    }
                    catch { }

                    // Persist into current tab snapshot so changes stick for untitled/new tabs
                    try { mw.PersistLoadedValuesToCurrentTab(); } catch { }

                    // Force Platformer checkbox
                    try
                    {
                        if (ForcePlatformerCheckBox != null)
                        {
                            mw.LoadedForcePlatformer = (ForcePlatformerCheckBox.IsChecked == true) ? true : (bool?)false;
                        }
                        else
                        {
                            mw.LoadedForcePlatformer = null;
                        }
                    }
                    catch { }

                        
                    // Save config (writes out to disk only when the TMX has a file path)
                    if (!_isInitializing)
                    {
                        mw.SaveCurrentTmxConfig();
                    }
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

            // Upper/Lower text initialization and handlers are wired in Loaded handler (owner available there)

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

            // Arrange tab order: top-left column top-to-bottom, then right column top-to-bottom, placing OK/Cancel at the end
            try
            {
                var tabList = new System.Collections.Generic.List<System.Windows.IInputElement>();
                // Left column (top to bottom)
                tabList.Add(DecoCombo);
                tabList.Add(LockSpritesCheckBox);
                tabList.Add(BlockCombo);
                tabList.Add(SpikeCombo);
                tabList.Add(ShowAccurateTilesetCheckBox);
                tabList.Add(StarsCombo);

                // Right column (top to bottom)
                tabList.Add(StartingBackgroundColorCombo);
                tabList.Add(StartingGroundColorCombo);
                tabList.Add(StartingGameModeCombo);
                tabList.Add(StartingSpeedCombo);
                tabList.Add(DifficultyCombo);
                tabList.Add(UpperTextBox);
                tabList.Add(LowerTextBox);

                // Quick tint + other right-side buttons
                tabList.Add(BgTintButton);
                tabList.Add(GroundTintButton);
                tabList.Add(TileTintButton);
                tabList.Add(NoParallaxCheckBox);
                tabList.Add(MaxFallSpeedCombo);
                tabList.Add(AttemptJsonLoadButton);
                tabList.Add(RemoveSpriteShiftsButton);
                tabList.Add(ExportShiftJsonButton);

                // Assign TabIndex sequentially, skipping nulls
                int idx = 0;
                foreach (var el in tabList)
                {
                    if (el == null) continue;
                    try
                    {
                        if (el is System.Windows.Controls.Control c)
                        {
                            c.TabIndex = idx;
                            c.IsTabStop = true;
                        }
                        else if (el is System.Windows.UIElement ue)
                        {
                            ue.Focusable = true;
                            System.Windows.Input.KeyboardNavigation.SetTabIndex(ue, idx);
                        }
                        idx++;
                    }
                    catch { }
                }
                // Put OK and Cancel at the end
                try { OkButton.TabIndex = idx++; OkButton.IsTabStop = true; } catch { }
                try { CancelButton.TabIndex = idx++; CancelButton.IsTabStop = true; } catch { }
            }
            catch { }

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
            // Keyboard shortcuts: Ctrl+Enter => OK, Esc => Cancel
            try
            {
                this.PreviewKeyDown += (ss, ee) =>
                {
                    try
                    {
                        if (ee.Key == System.Windows.Input.Key.Escape)
                        {
                            // Trigger cancel
                            try { CancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); } catch { }
                            ee.Handled = true;
                            return;
                        }
                        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0 && (ee.Key == System.Windows.Input.Key.Enter || ee.Key == System.Windows.Input.Key.Return))
                        {
                            try { OkButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); } catch { }
                            ee.Handled = true;
                            return;
                        }
                    }
                    catch { }
                };
            }
            catch { }
            this.Loaded += (s, e) =>
            {
                try
                {
                    if (this.Owner is MainWindow mw && NoParallaxCheckBox != null)
                    {
                        // Use public NoParallaxBg property instead of MenuOptionNoParallax
                        originalNoParallax = mw.NoParallaxBg;
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

                            

                            // Initialize MaxFallSpeed combo and persist on change
                            try
                            {
                                if (MaxFallSpeedCombo != null)
                                {
                                    // Read current value from owner
                                    try { MaxFallSpeedCombo.SelectedIndex = (mwOwner.LoadedMaxFallSpeed == 7) ? 1 : 0; } catch { MaxFallSpeedCombo.SelectedIndex = 0; }
                                    MaxFallSpeedCombo.SelectionChanged += (ss, ee) =>
                                    {
                                        try
                                        {
                                            if (MaxFallSpeedCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi && cbi.Tag != null)
                                            {
                                                if (int.TryParse(cbi.Tag.ToString(), out int tagVal))
                                                {
                                                    mwOwner.LoadedMaxFallSpeed = tagVal;
                                                    if (!_isInitializing)
                                                    {
                                                        try { mwOwner.SaveCurrentTmxConfig(); } catch { }
                                                    }
                                                }
                                            }
                                        }
                                        catch { }
                                    };
                                }
                            }
                            catch { }

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

                            // Wire StartingSpeed/GameMode/Background/Ground combos: initialize from owner's loaded value (or current tab snapshot) and persist on change
                            var startingVals = mwOwner.GetLoadedStartingValues();
                            // Wire StartingSpeed combo
                            if (StartingSpeedCombo != null)
                            {
                                try { StartingSpeedCombo.SelectedIndex = startingVals.startingSpeedUiIndex; } catch { }
                                StartingSpeedCombo.SelectionChanged += (ss, ee) =>
                                {
                                    try
                                    {
                                        if (StartingSpeedCombo.SelectedIndex >= 0)
                                        {
                                            mwOwner.LoadedStartingSpeedUiIndex = StartingSpeedCombo.SelectedIndex;
                                            if (!_isInitializing)
                                            {
                                                try { mwOwner.SaveCurrentTmxConfig(); } catch { }
                                            }
                                        }
                                    }
                                    catch { }
                                };
                            }
                            // Wire StartingGameMode combo: initialize from owner's loaded value and persist on change
                            if (StartingGameModeCombo != null)
                            {
                                try {
                                    if (startingVals.startingGameMode.HasValue)
                                    {
                                        int g = startingVals.startingGameMode.Value;
                                        for (int i = 0; i < StartingGameModeCombo.Items.Count; i++)
                                        {
                                            if (StartingGameModeCombo.Items[i] is System.Windows.Controls.ComboBoxItem c && c.Tag != null && int.TryParse(c.Tag.ToString(), out int tagVal) && tagVal == g)
                                            {
                                                StartingGameModeCombo.SelectedIndex = i; break;
                                            }
                                        }
                                    }
                                } catch { }
                                StartingGameModeCombo.SelectionChanged += (ss, ee) =>
                                {
                                    try
                                    {
                                        if (StartingGameModeCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi && cbi.Tag != null)
                                        {
                                            if (int.TryParse(cbi.Tag.ToString(), out int tagVal))
                                            {
                                                mwOwner.LoadedStartingGameMode = tagVal;
                                                if (!_isInitializing)
                                                {
                                                    try { mwOwner.SaveCurrentTmxConfig(); } catch { }
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                };
                            }
                            // Wire StartingBackgroundColor combo: initialize and persist
                            if (StartingBackgroundColorCombo != null)
                            {
                                try {
                                    if (startingVals.startingBackground.HasValue)
                                    {
                                        string hex = $"0x{startingVals.startingBackground.Value:X2}";
                                        for (int i = 0; i < StartingBackgroundColorCombo.Items.Count; i++)
                                        {
                                            if (StartingBackgroundColorCombo.Items[i] is System.Windows.Controls.ComboBoxItem c && (string)c.Content == hex)
                                            {
                                                StartingBackgroundColorCombo.SelectedIndex = i; break;
                                            }
                                        }
                                    }
                                } catch { }
                                StartingBackgroundColorCombo.SelectionChanged += (ss, ee) =>
                                {
                                    try
                                    {
                                        if (StartingBackgroundColorCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi && cbi.Content is string s)
                                        {
                                            try { mwOwner.LoadedStartingBackgroundColor = int.Parse(s.Replace("0x", ""), NumberStyles.HexNumber); } catch { mwOwner.LoadedStartingBackgroundColor = null; }
                                            if (!_isInitializing)
                                            {
                                                try { mwOwner.SaveCurrentTmxConfig(); } catch { }
                                            }
                                        }
                                    }
                                    catch { }
                                };
                            }
                            // Wire StartingGroundColor combo: initialize and persist
                            if (StartingGroundColorCombo != null)
                            {
                                try {
                                    if (startingVals.startingGround.HasValue)
                                    {
                                        string hex = $"0x{startingVals.startingGround.Value:X2}";
                                        for (int i = 0; i < StartingGroundColorCombo.Items.Count; i++)
                                        {
                                            if (StartingGroundColorCombo.Items[i] is System.Windows.Controls.ComboBoxItem c && (string)c.Content == hex)
                                            {
                                                StartingGroundColorCombo.SelectedIndex = i; break;
                                            }
                                        }
                                    }
                                } catch { }
                                StartingGroundColorCombo.SelectionChanged += (ss, ee) =>
                                {
                                    try
                                    {
                                        if (StartingGroundColorCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi && cbi.Content is string s)
                                        {
                                            try { mwOwner.LoadedStartingGroundColor = int.Parse(s.Replace("0x", ""), NumberStyles.HexNumber); } catch { mwOwner.LoadedStartingGroundColor = null; }
                                            if (!_isInitializing)
                                            {
                                                try { mwOwner.SaveCurrentTmxConfig(); } catch { }
                                            }
                                        }
                                    }
                                    catch { }
                                };
                            }
                            
                            // Wire Difficulty combo: initialize from owner's loaded value and persist on change
                            if (DifficultyCombo != null)
                            {
                                try {
                                    if (startingVals.startingDifficulty.HasValue)
                                    {
                                        int d = startingVals.startingDifficulty.Value;
                                        for (int i = 0; i < DifficultyCombo.Items.Count; i++)
                                        {
                                            if (DifficultyCombo.Items[i] is System.Windows.Controls.ComboBoxItem c && c.Tag != null && int.TryParse(c.Tag.ToString(), out int tagVal) && tagVal == d)
                                            {
                                                DifficultyCombo.SelectedIndex = i; break;
                                            }
                                        }
                                    }
                                } catch { }
                                DifficultyCombo.SelectionChanged += (ss, ee) =>
                                {
                                    try
                                    {
                                        if (DifficultyCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi && cbi.Tag != null)
                                        {
                                            if (int.TryParse(cbi.Tag.ToString(), out int tagVal))
                                            {
                                                mwOwner.LoadedStartingDifficulty = tagVal;
                                                if (!_isInitializing)
                                                {
                                                    try { mwOwner.SaveCurrentTmxConfig(); } catch { }
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                };
                            }

                            // Wire Stars combo: initialize from owner's loaded value and persist on change
                            if (StarsCombo != null)
                            {
                                try {
                                    if (startingVals.startingStars.HasValue)
                                    {
                                        int starVal = startingVals.startingStars.Value;
                                        for (int i = 0; i < StarsCombo.Items.Count; i++)
                                        {
                                            if (StarsCombo.Items[i] is System.Windows.Controls.ComboBoxItem c && c.Tag != null && int.TryParse(c.Tag.ToString(), out int tagVal) && tagVal == starVal)
                                            {
                                                StarsCombo.SelectedIndex = i; break;
                                            }
                                        }
                                    }
                                } catch { }
                                StarsCombo.SelectionChanged += (ss, ee) =>
                                {
                                    try
                                    {
                                        if (StarsCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi && cbi.Tag != null)
                                        {
                                            if (int.TryParse(cbi.Tag.ToString(), out int tagVal))
                                            {
                                                mwOwner.LoadedStartingStars = tagVal;
                                                if (!_isInitializing)
                                                {
                                                    try { mwOwner.SaveCurrentTmxConfig(); } catch { }
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                };
                            }

                            // Initialize Upper/Lower text UI and handlers now that Owner (mwOwner) is available
                            try
                            {
                                try { if (LowerTextBox != null) LowerTextBox.Text = mwOwner.LoadedStartingLowerText ?? ""; } catch { }
                                try { if (UpperTextBox != null) UpperTextBox.Text = mwOwner.LoadedStartingUpperText ?? ""; } catch { }
                                try { if (UpperTextBox != null) UpperTextBox.IsEnabled = !string.IsNullOrEmpty(mwOwner.LoadedStartingLowerText) || !string.IsNullOrEmpty(mwOwner.LoadedStartingUpperText); } catch { }

                                if (LowerTextBox != null)
                                {
                                    LowerTextBox.TextChanged += (ss2, ee2) =>
                                    {
                                        try
                                        {
                                            string txt = LowerTextBox.Text ?? "";
                                            txt = txt.ToUpperInvariant();
                                            if (LowerTextBox.Text != txt) { LowerTextBox.Text = txt; LowerTextBox.CaretIndex = txt.Length; }
                                            mwOwner.LoadedStartingLowerText = string.IsNullOrEmpty(txt) ? null : txt;
                                            try
                                            {
                                                if (UpperTextBox != null)
                                                {
                                                    if (string.IsNullOrEmpty(txt))
                                                    {
                                                        UpperTextBox.Text = "";
                                                        UpperTextBox.IsEnabled = false;
                                                        mwOwner.LoadedStartingUpperText = null;
                                                    }
                                                    else
                                                    {
                                                        UpperTextBox.IsEnabled = true;
                                                    }
                                                }
                                            }
                                            catch { }
                                            if (!_isInitializing)
                                            {
                                                try { mwOwner.SaveCurrentTmxConfig(); } catch { }
                                            }
                                        }
                                        catch { }
                                    };
                                }

                                if (UpperTextBox != null)
                                {
                                    UpperTextBox.TextChanged += (ss2, ee2) =>
                                    {
                                        try
                                        {
                                            string txt = UpperTextBox.Text ?? "";
                                            txt = txt.ToUpperInvariant();
                                            if (UpperTextBox.Text != txt) { UpperTextBox.Text = txt; UpperTextBox.CaretIndex = txt.Length; }
                                            mwOwner.LoadedStartingUpperText = string.IsNullOrEmpty(txt) ? null : txt;
                                            if (!_isInitializing)
                                            {
                                                try { mwOwner.SaveCurrentTmxConfig(); } catch { }
                                            }
                                        }
                                        catch { }
                                    };
                                }
                            }
                            catch { }

                            // Initialize the remaining Y-position fields from loaded values.
                            try
                            {
                                if (SpawnYPositionHiTextBox != null) SpawnYPositionHiTextBox.Text = mwOwner.LoadedSpawnYPositionHi.HasValue ? $"0x{mwOwner.LoadedSpawnYPositionHi.Value:X2}" : "";
                                if (ScrollYPositionLowTextBox != null) ScrollYPositionLowTextBox.Text = mwOwner.LoadedScrollYPositionLow.HasValue ? $"0x{mwOwner.LoadedScrollYPositionLow.Value:X2}" : "";

                                // Format entered values to 0xHEX on lost focus
                                void FormatHexOnLost(object? s, RoutedEventArgs ea)
                                {
                                    try
                                    {
                                        if (s is System.Windows.Controls.TextBox tb)
                                        {
                                            string t = tb.Text ?? "";
                                            t = t.Trim();
                                            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
                                            // Allow decimal or hex input; try hex first
                                            if (byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte val)) tb.Text = $"0x{val:X2}";
                                            else if (int.TryParse(t, out int dv) && dv >= 0 && dv <= 255) tb.Text = $"0x{dv:X2}";
                                            else if (string.IsNullOrWhiteSpace(t)) tb.Text = "";
                                            else tb.Text = t.ToUpperInvariant();
                                        }
                                    }
                                    catch { }
                                }

                                if (SpawnYPositionHiTextBox != null) SpawnYPositionHiTextBox.LostFocus += FormatHexOnLost;
                                if (ScrollYPositionLowTextBox != null) ScrollYPositionLowTextBox.LostFocus += FormatHexOnLost;
                            }
                            catch { }

                                // Initialize ForcePlatformer checkbox from main window loaded value
                                try { if (ForcePlatformerCheckBox != null) ForcePlatformerCheckBox.IsChecked = mwOwner.LoadedForcePlatformer == true; } catch { }

                                            // Simulator size moved to main Options menu (handled there)
                            
                            // Initialization complete - allow saves now
                            _isInitializing = false;
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
                // Use AppContext.BaseDirectory instead of Assembly.Location for single-file app compatibility
                var appDirectory = AppContext.BaseDirectory;
                var jsonFilePath = Path.Combine(appDirectory, "lvlset_HUGE_metadata.json5");
                
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
                    // Sync mainWindow.SelectedSong from the combo so exporter/readers that rely on the property see the change immediately
                    try
                    {
                        object? combo = null;
                        var comboProp = mainWindow.GetType().GetProperty("FamiTrackCombo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (comboProp != null) combo = comboProp.GetValue(mainWindow);
                        else
                        {
                            var comboField = mainWindow.GetType().GetField("FamiTrackCombo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (comboField != null) combo = comboField.GetValue(mainWindow);
                        }

                        if (combo != null)
                        {
                            var sel = combo.GetType().GetProperty("SelectedItem")?.GetValue(combo);
                            if (sel != null)
                            {
                                var contentProp = sel.GetType().GetProperty("Content");
                                string? content = null;
                                if (contentProp != null) content = contentProp.GetValue(sel)?.ToString();
                                else content = sel.ToString();

                                if (!string.IsNullOrEmpty(content))
                                {
                                    var selectedSongProp = mainWindow.GetType().GetProperty("SelectedSong", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    if (selectedSongProp != null && selectedSongProp.CanWrite)
                                    {
                                        selectedSongProp.SetValue(mainWindow, content);
                                    }
                                }
                            }
                        }
                    }
                    catch { }

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

                // Current source metadata stores this as a flag, not the legacy
                // numeric maxFallSpeed value: exactly 1 selects 0x07; zero,
                // another value, or an absent property selects the 0x06 default.
                // Writers intentionally continue emitting the legacy format for
                // the ROM build/test pipeline until that pipeline is migrated.
                try
                {
                    mainWindow.LoadedMaxFallSpeed = levelData.maxFallSpeed_is_7 == 1 ? 0x07 : 0x06;
                    dataChanged = true;
                }
                catch { mainWindow.LoadedMaxFallSpeed = 0x06; }
                // Update the MaxFallSpeedCombo in the dialog immediately so the UI reflects the loaded value
                try
                {
                    if (MaxFallSpeedCombo != null)
                    {
                        MaxFallSpeedCombo.SelectedIndex = (mainWindow.LoadedMaxFallSpeed == 7) ? 1 : 0;
                    }
                }
                catch { }

                // Apply starting background/ground color metadata (numeric codes). These are provided
                // in the JSON5 as hex (converted to decimal by ConvertJson5ToJson). Save into mainWindow
                // so config persists and simulator can pick them up on launch.
                if (levelData.startingBackgroundColor.HasValue)
                {
                    try
                    {
                        int code = levelData.startingBackgroundColor.Value;
                        mainWindow.LoadedStartingBackgroundColor = code;
                        // Select matching combobox item if present
                        try
                        {
                            string hex = $"0x{code:X2}";
                            if (StartingBackgroundColorCombo != null)
                            {
                                for (int i = 0; i < StartingBackgroundColorCombo.Items.Count; i++)
                                {
                                    if (StartingBackgroundColorCombo.Items[i] is System.Windows.Controls.ComboBoxItem c && (string)c.Content == hex)
                                    {
                                        StartingBackgroundColorCombo.SelectedIndex = i; break;
                                    }
                                }
                            }
                        }
                        catch { }
                        dataChanged = true;
                    }
                    catch { }
                }

                if (levelData.startingGroundColor.HasValue)
                {
                    try
                    {
                        int code = levelData.startingGroundColor.Value;
                        mainWindow.LoadedStartingGroundColor = code;
                        try
                        {
                            string hex = $"0x{code:X2}";
                            if (StartingGroundColorCombo != null)
                            {
                                for (int i = 0; i < StartingGroundColorCombo.Items.Count; i++)
                                {
                                    if (StartingGroundColorCombo.Items[i] is System.Windows.Controls.ComboBoxItem c && (string)c.Content == hex)
                                    {
                                        StartingGroundColorCombo.SelectedIndex = i; break;
                                    }
                                }
                            }
                        }
                        catch { }
                        dataChanged = true;
                    }
                    catch { }
                }

                // Apply starting game mode metadata (numeric code, 0=cube..8=ninja)
                if (levelData.startingGameMode.HasValue)
                {
                    try
                    {
                        int code = levelData.startingGameMode.Value;
                        mainWindow.LoadedStartingGameMode = code;
                        if (StartingGameModeCombo != null)
                        {
                            for (int i = 0; i < StartingGameModeCombo.Items.Count; i++)
                            {
                                if (StartingGameModeCombo.Items[i] is System.Windows.Controls.ComboBoxItem c && c.Tag != null && int.TryParse(c.Tag.ToString(), out int tagVal) && tagVal == code)
                                {
                                    StartingGameModeCombo.SelectedIndex = i; break;
                                }
                            }
                        }
                        dataChanged = true;
                    }
                    catch { }
                }

                // Apply difficulty/stars metadata if present
                if (!string.IsNullOrEmpty(levelData.difficulty))
                {
                    try
                    {
                        string diff = levelData.difficulty.Trim().ToUpperInvariant();
                        int mapped = 6; // AUTO default
                        switch (diff)
                        {
                            case "AUTO": mapped = 6; break;
                            case "EASY": mapped = 0; break;
                            case "NORMAL": mapped = 1; break;
                            case "HARD": mapped = 2; break;
                            case "HARDER": mapped = 3; break;
                            case "INSANE": mapped = 4; break;
                            case "DEMON": mapped = 5; break;
                            // Demon difficulties map to UI tags 7-13 (repeat of 0-6 internally, different string names)
                            case "EASYDEMON": mapped = 7; break;
                            case "MEDIUMDEMON": mapped = 8; break;
                            case "HARDDEMON": mapped = 9; break;
                            case "INSANEDEMON": mapped = 10; break;
                            case "EXTREMEDEMON": mapped = 11; break;
                            case "IMPOSSIBLEDEMON": mapped = 12; break;
                            case "GRANDPADEMON": mapped = 13; break;
                            default: mapped = 6; break;
                        }
                        mainWindow.LoadedStartingDifficulty = mapped;
                        try
                        {
                            if (DifficultyCombo != null)
                            {
                                for (int i = 0; i < DifficultyCombo.Items.Count; i++)
                                {
                                    if (DifficultyCombo.Items[i] is System.Windows.Controls.ComboBoxItem c && c.Tag != null && int.TryParse(c.Tag.ToString(), out int tagVal) && tagVal == mapped)
                                    {
                                        DifficultyCombo.SelectedIndex = i; break;
                                    }
                                }
                            }
                        }
                        catch { }
                        dataChanged = true;
                    }
                    catch { }
                }

                if (levelData.stars.HasValue)
                {
                    try
                    {
                        int starsVal = levelData.stars.Value;
                        if (starsVal < 1) starsVal = 1; if (starsVal > 15) starsVal = 15;
                        mainWindow.LoadedStartingStars = starsVal;
                        try
                        {
                            if (StarsCombo != null)
                            {
                                for (int i = 0; i < StarsCombo.Items.Count; i++)
                                {
                                    if (StarsCombo.Items[i] is System.Windows.Controls.ComboBoxItem c && c.Tag != null && int.TryParse(c.Tag.ToString(), out int tagVal) && tagVal == starsVal)
                                    {
                                        StarsCombo.SelectedIndex = i; break;
                                    }
                                }
                            }
                        }
                        catch { }
                        dataChanged = true;
                    }
                    catch { }
                }

                // Apply lower/upper text metadata if present (force uppercase). If upper text is provided
                // we enable the upper box even if lower is empty (this is the JSON-load exception).
                if (!string.IsNullOrEmpty(levelData.lowerText))
                {
                    try
                    {
                        string lt = levelData.lowerText.Trim().ToUpperInvariant();
                        mainWindow.LoadedStartingLowerText = lt;
                        try { if (LowerTextBox != null) LowerTextBox.Text = lt; } catch { }
                        // If we have lower text, ensure upper is enabled (but don't change its text here)
                        try { if (UpperTextBox != null) UpperTextBox.IsEnabled = true; } catch { }
                        dataChanged = true;
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(levelData.upperText))
                {
                    try
                    {
                        string ut = levelData.upperText.Trim().ToUpperInvariant();
                        mainWindow.LoadedStartingUpperText = ut;
                        try { if (UpperTextBox != null) { UpperTextBox.Text = ut; UpperTextBox.IsEnabled = true; } } catch { }
                        dataChanged = true;
                    }
                    catch { }
                }

                // Optional custom Y position metadata (process regardless of upperText presence)
                if (levelData.spawnYPositionHi.HasValue)
                {
                    try
                    {
                        int v = levelData.spawnYPositionHi.Value;
                        mainWindow.LoadedSpawnYPositionHi = v;
                        try { if (SpawnYPositionHiTextBox != null) SpawnYPositionHiTextBox.Text = $"0x{v:X2}"; } catch { }
                        dataChanged = true;
                    }
                    catch { }
                }
                if (levelData.spawnYPositionLow.HasValue)
                {
                    try
                    {
                        int v = levelData.spawnYPositionLow.Value;
                        mainWindow.LoadedSpawnYPositionLow = v;
                        dataChanged = true;
                    }
                    catch { }
                }
                if (levelData.scrollYPositionHi.HasValue)
                {
                    try
                    {
                        int v = levelData.scrollYPositionHi.Value;
                        mainWindow.LoadedScrollYPositionHi = v;
                        dataChanged = true;
                    }
                    catch { }
                }
                if (levelData.scrollYPositionLow.HasValue)
                {
                    try
                    {
                        int v = levelData.scrollYPositionLow.Value;
                        mainWindow.LoadedScrollYPositionLow = v;
                        try { if (ScrollYPositionLowTextBox != null) ScrollYPositionLowTextBox.Text = $"0x{v:X2}"; } catch { }
                        dataChanged = true;
                    }
                    catch { }
                }

                // Optional forcePlatformer flag (process regardless of upperText presence)
                if (levelData.forcePlatformer.HasValue)
                {
                    try
                    {
                        bool v = levelData.forcePlatformer.Value;
                        mainWindow.LoadedForcePlatformer = v;
                        try { if (ForcePlatformerCheckBox != null) ForcePlatformerCheckBox.IsChecked = v; } catch { }
                        dataChanged = true;
                    }
                    catch { }
                }

                if (dataChanged)
                {
                    // Persist loaded values into the current tab snapshot so untitled/new tabs
                    // immediately reflect the changes (this fixes stale-export behavior).
                    try { mainWindow.PersistLoadedValuesToCurrentTab(); } catch { }

                    // Save to level-specific config file (writes only when TMX has a file path)
                    // Note: This call is from AttemptJsonLoad, not from initialization, so we don't guard it
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

                // Build full JSON5 metadata block in the same format and ordering
                // as lvlset_HUGE_metadata.json5. We'll emit a single level object
                // inside an array (official_levels) so it's easy to drop into the
                // existing metadata file.
                var sb = new System.Text.StringBuilder();

                // Prepare top-level values
                string currentTmxPath = mainWindow.GetCurrentTmxPath();
                string levelName = string.IsNullOrEmpty(currentTmxPath) ? "level" : Path.GetFileNameWithoutExtension(currentTmxPath).ToLowerInvariant();

                // Helper to safely read properties from MainWindow (reflection) so
                // this exporter remains robust if member names differ between builds.
                object? GetProp(string name) => mainWindow.GetType().GetProperty(name)?.GetValue(mainWindow);
                string GetString(string name, string @default = "") => (GetProp(name) as string) ?? @default;
                int? GetNullableInt(string name)
                {
                    var v = GetProp(name);
                    if (v == null) return null;
                    try { return Convert.ToInt32(v); } catch { return null; }
                }
                bool GetBool(string name)
                {
                    var v = GetProp(name);
                    if (v == null) return false;
                    try { return Convert.ToBoolean(v); } catch { return false; }
                }

                // Upper/Lower - uppercase and trim
                string upper = GetString("LoadedStartingUpperText", "").ToUpperInvariant();
                string lower = GetString("LoadedStartingLowerText", "").ToUpperInvariant();

                // Helper: try to read the current tab's FileTabData values when available
                object? TryGetCurrentTabValue(string propName)
                {
                    try
                    {
                        var openFilesField = mainWindow.GetType().GetField("openFiles", BindingFlags.NonPublic | BindingFlags.Instance);
                        var currentIndexField = mainWindow.GetType().GetField("currentFileIndex", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (openFilesField != null && currentIndexField != null)
                        {
                            var list = openFilesField.GetValue(mainWindow) as System.Collections.IList;
                            var idxObj = currentIndexField.GetValue(mainWindow);
                            if (list != null && idxObj is int idx && idx >= 0 && idx < list.Count)
                            {
                                var tabData = list[idx];
                                if (tabData != null)
                                {
                                    // Try property on the FileTabData instance first
                                    var p = tabData.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                    if (p != null) return p.GetValue(tabData);
                                    // Try field
                                    var f = tabData.GetType().GetField(propName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                                    if (f != null) return f.GetValue(tabData);
                                }
                            }
                        }
                    }
                    catch { }
                    return null;
                }

                // Deco/block/spike sets - prefer live MainWindow properties first, then per-tab snapshots
                string deco = GetString("LoadedDecoSet", (TryGetCurrentTabValue("LoadedDecoSet") as string) ?? "DECO1");
                // Block/spike sets may be stored as BLOCKSA/BLOCKSB or SPIKESA/SPIKESB; extract trailing letter if present
                string blockSet = GetString("LoadedBlockSet", (TryGetCurrentTabValue("LoadedBlockSet") as string) ?? "BLOCKSA");
                string spikeSet = GetString("LoadedSpikeSet", (TryGetCurrentTabValue("LoadedSpikeSet") as string) ?? "SPIKESA");
                string blockLetter = blockSet != null && blockSet.StartsWith("BLOCKS", StringComparison.OrdinalIgnoreCase) ? blockSet.Substring(6) : (blockSet ?? "A");
                string spikeLetter = spikeSet != null && spikeSet.StartsWith("SPIKES", StringComparison.OrdinalIgnoreCase) ? spikeSet.Substring(6) : (spikeSet ?? "A");
                blockLetter = (blockLetter ?? "").ToUpperInvariant();
                spikeLetter = (spikeLetter ?? "").ToUpperInvariant();

                // Difficulty / stars
                // UI tags 0-6 map to normal difficulties, tags 7-13 map to demon difficulties (same internal values 0-6 with different string names)
                string difficulty = "AUTO";
                var diffVal = GetNullableInt("LoadedStartingDifficulty");
                try
                {
                    if (diffVal.HasValue)
                    {
                        int val = Math.Clamp(diffVal.Value, 0, 13);
                        string[] normalDiffs = { "EASY", "NORMAL", "HARD", "HARDER", "INSANE", "DEMON", "AUTO" };
                        string[] demonDiffs = { "EASYDEMON", "MEDIUMDEMON", "HARDDEMON", "INSANEDEMON", "EXTREMEDEMON", "IMPOSSIBLEDEMON", "GRANDPADEMON" };
                        difficulty = (val < 7) ? normalDiffs[val] : demonDiffs[val - 7];
                    }
                }
                catch { difficulty = "AUTO"; }
                int stars = GetNullableInt("LoadedStartingStars") ?? 3;

                // Song normalization: prefer live MainWindow property, then per-tab SelectedSong, then FamiTrackCombo control
                string rawSong = GetString("SelectedSong", "");
                if (string.IsNullOrEmpty(rawSong)) rawSong = TryGetCurrentTabValue("SelectedSong") as string ?? "";
                if (string.IsNullOrEmpty(rawSong))
                {
                    try
                    {
                        // Try to get the FamiTrackCombo as a property or a field (WPF x:Name often generates a field)
                        object? combo = null;
                        try
                        {
                            var comboProp = mainWindow.GetType().GetProperty("FamiTrackCombo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (comboProp != null) combo = comboProp.GetValue(mainWindow);
                            else
                            {
                                var comboField = mainWindow.GetType().GetField("FamiTrackCombo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (comboField != null) combo = comboField.GetValue(mainWindow);
                            }
                        }
                        catch { combo = null; }
                        if (combo != null)
                        {
                            var sel = combo.GetType().GetProperty("SelectedItem")?.GetValue(combo);
                            if (sel != null)
                            {
                                // ComboBoxItem.Content
                                var contentProp = sel.GetType().GetProperty("Content");
                                if (contentProp != null)
                                {
                                    var content = contentProp.GetValue(sel)?.ToString();
                                    if (!string.IsNullOrEmpty(content)) rawSong = content;
                                }
                                else
                                {
                                    // Fallback: ToString()
                                    var s = sel.ToString();
                                    if (!string.IsNullOrEmpty(s)) rawSong = s;
                                }
                            }
                        }
                    }
                    catch { }
                }
                string songId = "";
                if (!string.IsNullOrEmpty(rawSong))
                {
                    string s = rawSong.ToLowerInvariant();
                    s = Regex.Replace(s, "\\s+", "_");
                    s = Regex.Replace(s, "[^a-z0-9_]", "");
                    songId = "song_" + s;
                }

                // Starting game mode / speed / colors
                int? gameMode = GetNullableInt("LoadedStartingGameMode");
                int startingGameMode = gameMode ?? 0;
                int startingSpeedJson = 0; // default metadata uses 0 -> 1x
                try { int ui = GetNullableInt("LoadedStartingSpeedUiIndex") ?? 1; startingSpeedJson = (ui == 0) ? 1 : (ui == 1) ? 0 : ui; } catch { startingSpeedJson = 0; }
                // Max fall speed: prefer live MainWindow property, then per-tab snapshot; default 0x06
                int maxFallSpeed = 6;
                try { var ni = GetNullableInt("LoadedMaxFallSpeed"); if (ni.HasValue) maxFallSpeed = ni.Value; else { var tv = TryGetCurrentTabValue("LoadedMaxFallSpeed"); if (tv is int ti) maxFallSpeed = ti; } } catch { try { var tv = TryGetCurrentTabValue("LoadedMaxFallSpeed"); if (tv is int ti) maxFallSpeed = ti; } catch { } }
                if (maxFallSpeed == 0) maxFallSpeed = 6;
                int? bgColor = GetNullableInt("LoadedStartingBackgroundColor");
                int? groundColor = GetNullableInt("LoadedStartingGroundColor");

                // Build header of one-level object
                sb.AppendLine("\t\t{ ");
                sb.AppendLine($"\t\t\tlevel: \"{levelName}\",");
                if (!string.IsNullOrEmpty(upper)) sb.AppendLine($"\t\t\tupperText: \"{upper}\",");
                if (!string.IsNullOrEmpty(lower)) sb.AppendLine($"\t\t\tlowerText: \"{lower}\",");
                sb.AppendLine($"\t\t\tdecoType: \"{deco}\",");
                sb.AppendLine($"\t\t\tspikeSet: \"{spikeLetter}\",");
                sb.AppendLine($"\t\t\tblockSet: \"{blockLetter}\",");
                sb.AppendLine($"\t\t\tsawSet: \"A\",");
                sb.AppendLine($"\t\t\tdifficulty: \"{difficulty}\",");
                sb.AppendLine($"\t\t\tstars: {stars},");
                if (!string.IsNullOrEmpty(songId)) sb.AppendLine($"\t\t\tsongID: \"{songId}\",");
                sb.AppendLine($"\t\t\tstartingGameMode: {startingGameMode},");
                sb.AppendLine($"\t\t\tstartingSpeed: {startingSpeedJson},");
                // Source/game metadata uses a flag. Omission means the default
                // 0x06; only 0x07 emits the enabled flag.
                if (maxFallSpeed == 7)
                {
                    sb.AppendLine("\t\t\tmaxFallSpeed_is_7: 0x01,");
                }
                if (bgColor.HasValue) sb.AppendLine($"\t\t\tstartingBackgroundColor: 0x{bgColor.Value:X2},"); else sb.AppendLine($"\t\t\tstartingBackgroundColor: 0x12,");
                if (groundColor.HasValue) sb.AppendLine($"\t\t\tstartingGroundColor: 0x{groundColor.Value:X2},"); else sb.AppendLine($"\t\t\tstartingGroundColor: 0x02,");

                // Current source metadata retains only spawn high and scroll low.
                // Prefer values currently shown in the dialog textboxes (to avoid stale in-memory state),
                // then prefer live MainWindow props, then per-tab snapshot.
                int? spawnHi = null, scrollLow = null;

                // Helper to parse textbox hex/decimal to nullable int
                int? ParseBox(System.Windows.Controls.TextBox? tb)
                {
                    try
                    {
                        if (tb == null) return null;
                        var t = (tb.Text ?? "").Trim();
                        if (string.IsNullOrEmpty(t)) return null;
                        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
                        if (int.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int hv)) return hv & 0xFF;
                        if (int.TryParse(t, out int dv)) return dv & 0xFF;
                    }
                    catch { }
                    return null;
                }

                // Read directly from textboxes first
                try { spawnHi = ParseBox(this.SpawnYPositionHiTextBox); } catch { }
                try { scrollLow = ParseBox(this.ScrollYPositionLowTextBox); } catch { }

                // Fallback to MainWindow properties if boxes empty
                if (!spawnHi.HasValue) spawnHi = GetNullableInt("LoadedSpawnYPositionHi");
                if (!scrollLow.HasValue) scrollLow = GetNullableInt("LoadedScrollYPositionLow");

                // Finally fallback to per-tab snapshot
                if (!spawnHi.HasValue)
                {
                    var tv = TryGetCurrentTabValue("LoadedSpawnYPositionHi"); if (tv is int vi1) spawnHi = vi1;
                }
                if (!scrollLow.HasValue)
                {
                    var tv = TryGetCurrentTabValue("LoadedScrollYPositionLow"); if (tv is int vi2) scrollLow = vi2;
                }

                if (spawnHi.HasValue) sb.AppendLine($"\t\t\tspawnYPositionHi: 0x{spawnHi.Value:X2},");
                if (scrollLow.HasValue) sb.AppendLine($"\t\t\tscrollYPositionLow: 0x{scrollLow.Value:X2},");

                // Optional forcePlatformer flag - prefer the dialog checkbox to avoid stale in-memory state
                bool? forcePlatformer = null;
                try { if (this.ForcePlatformerCheckBox != null && this.ForcePlatformerCheckBox.IsChecked.HasValue) forcePlatformer = this.ForcePlatformerCheckBox.IsChecked.Value; } catch { }
                if (!forcePlatformer.HasValue) {
                    try { var v = TryGetCurrentTabValue("LoadedForcePlatformer"); if (v is bool b) forcePlatformer = b; } catch { }
                }
                if (!forcePlatformer.HasValue) {
                    try { forcePlatformer = GetBool("LoadedForcePlatformer"); } catch { }
                }
                if (forcePlatformer.HasValue && forcePlatformer.Value)
                {
                    sb.AppendLine("\t\t\tforcePlatformer: true,");
                }

                // Optionally include parallaxDisable. Try per-tab value then fields/properties and finally menu option state.
                bool noParallax = false;
                try { var v = TryGetCurrentTabValue("NoParallaxBg"); if (v is bool vb) noParallax = vb; }
                catch { }
                if (!noParallax)
                {
                    try { noParallax = GetBool("NoParallaxBg"); } catch { }
                }
                if (!noParallax)
                {
                    // Use public NoParallaxBg property instead of MenuOptionNoParallax
                    try
                    {
                        noParallax = mainWindow.NoParallaxBg;
                    }
                    catch { }
                }
                if (noParallax)
                {
                    sb.AppendLine($"\t\t\tparallaxDisable: true,");
                }

                // Try to detect the level set letter from the TMX's internal export target
                // (many TMX files include an editors/export target path like ".../lvlset_B/...").
                try
                {
                    if (!string.IsNullOrEmpty(currentTmxPath) && File.Exists(currentTmxPath))
                    {
                        var tmxContents = File.ReadAllText(currentTmxPath);
                        var m = Regex.Match(tmxContents, "lvlset_([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
                        if (m.Success && m.Groups.Count > 1)
                        {
                            var setLetter = m.Groups[1].Value.ToUpperInvariant();
                            // if block/spike letters are single-letter or defaults, prefer the TMX-indicated set
                            if (blockLetter == "A" || blockLetter.Length == 0) blockLetter = setLetter;
                            if (spikeLetter == "A" || spikeLetter.Length == 0) spikeLetter = setLetter;
                        }
                    }
                }
                catch { }

                // objectOffsets: only include if there are any sprite shifts
                if (groupedOffsets.Count > 0)
                {
                    sb.AppendLine("\t\t\tobjectOffsets: [");
                    bool firstGroup = true;
                    foreach (var group in groupedOffsets)
                    {
                        if (!firstGroup) sb.AppendLine(",");
                        firstGroup = false;

                        sb.AppendLine("\t\t\t\t{");
                        var coords = group.Value;
                        if (coords.Count == 1)
                        {
                            sb.AppendLine($"\t\t\t\t\tcoordinates: [{coords[0].x}, {coords[0].y}],");
                        }
                        else
                        {
                            sb.AppendLine("\t\t\t\t\tcoordinates: [");
                            for (int i = 0; i < coords.Count; i++)
                            {
                                string separator = (i < coords.Count - 1) ? "," : "";
                                sb.AppendLine($"\t\t\t\t\t\t[{coords[i].x}, {coords[i].y}]{separator}");
                            }
                            sb.AppendLine("\t\t\t\t\t],");
                        }

                        // offsets formatting (+/-)
                        if (group.Key.offsetX != 0 || group.Key.offsetY != 0)
                        {
                            if (group.Key.offsetY != 0)
                            {
                                string offsetYStr = group.Key.offsetY >= 0 ? $"+{group.Key.offsetY}" : group.Key.offsetY.ToString();
                                sb.AppendLine($"\t\t\t\t\toffsetY: {offsetYStr}{(group.Key.offsetX != 0 ? "," : "")} ");
                            }
                            if (group.Key.offsetX != 0)
                            {
                                string offsetXStr = group.Key.offsetX >= 0 ? $"+{group.Key.offsetX}" : group.Key.offsetX.ToString();
                                sb.AppendLine($"\t\t\t\t\toffsetX: {offsetXStr}");
                            }
                        }

                        sb.Append("\t\t\t\t}");
                    }

                    sb.AppendLine();
                    sb.AppendLine("\t\t\t]");
                    sb.AppendLine("\t\t}");
                }
                else
                {
                    // Close the level object when there are no offsets
                    sb.AppendLine("\t\t}");
                }

                // Save to Documents folder
                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string fileName = $"{levelName}_metadata.json5";
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

                MessageBox.Show($"Exported JSON metadata to:\n{filePath}\n\nThe file has been opened in your default text editor.", 
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
            // Optional difficulty/stars
            public string? difficulty { get; set; }
            public int? stars { get; set; }
            // Optional per-level lower/upper text
            public string? lowerText { get; set; }
            public string? upperText { get; set; }
            // Optional starting speed metadata. Values are numeric codes per the metadata spec:
            // 0 -> 1x, 1 -> 0.5x, 2 -> 2x, 3 -> 3x, 4 -> 4x
            public int? startingSpeed { get; set; }
            // Optional starting color metadata (hex codes converted to decimal by parser)
            public int? startingBackgroundColor { get; set; }
            public int? startingGroundColor { get; set; }
            // Optional starting game mode metadata (numeric code, 0=cube..8=ninja)
            public int? startingGameMode { get; set; }
            // Legacy output field retained for ROM build/test JSON generation.
            public int? maxFallSpeed { get; set; }
            // Current source metadata input flag. 1 => 0x07; absent/other => 0x06.
            public int? maxFallSpeed_is_7 { get; set; }
            // Optional custom Y position metadata (hex in JSON5 converted to decimal)
            public int? spawnYPositionHi { get; set; }
            public int? spawnYPositionLow { get; set; }
            public int? scrollYPositionHi { get; set; }
            public int? scrollYPositionLow { get; set; }
            // Optional force platformer flag
            public bool? forcePlatformer { get; set; }
        }
    }
}

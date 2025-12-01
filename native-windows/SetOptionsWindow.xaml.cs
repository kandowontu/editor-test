using System.Windows;

namespace FamidashEditor
{
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
    }
}

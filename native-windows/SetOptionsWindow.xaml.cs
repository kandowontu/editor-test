using System.Windows;

namespace FamidashEditor
{
    public partial class SetOptionsWindow : Window
    {
        public string SelectedDeco { get; private set; } = "DECO1";
        public string SelectedBlockSet { get; private set; } = "BLOCKSA";
        public string SelectedSpikeSet { get; private set; } = "SPIKESA";
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
                }
                catch { }
            };
        }
    }
}

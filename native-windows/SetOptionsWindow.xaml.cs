using System.Windows;

namespace FamidashEditor
{
    public partial class SetOptionsWindow : Window
    {
        public string SelectedDeco { get; private set; } = "deco1";
        public SetOptionsWindow(string current)
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
            OkButton.Click += (s, e) => { if (DecoCombo.SelectedItem is System.Windows.Controls.ComboBoxItem cbi2) SelectedDeco = (string)cbi2.Content; this.DialogResult = true; };
            CancelButton.Click += (s, e) => { this.DialogResult = false; };
        }
    }
}

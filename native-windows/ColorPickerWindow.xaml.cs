using System.Windows;
using System.Windows.Media;

namespace FamidashEditor
{
    public partial class ColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; } = Color.FromRgb(40,40,40);

        public ColorPickerWindow(Color initial)
        {
            InitializeComponent();
            SelectedColor = initial;
            RSlider.Value = initial.R; GSlider.Value = initial.G; BSlider.Value = initial.B;
            UpdatePreview();
            RSlider.ValueChanged += (_, __) => { RVal.Text = ((int)RSlider.Value).ToString(); UpdatePreview(); };
            GSlider.ValueChanged += (_, __) => { GVal.Text = ((int)GSlider.Value).ToString(); UpdatePreview(); };
            BSlider.ValueChanged += (_, __) => { BVal.Text = ((int)BSlider.Value).ToString(); UpdatePreview(); };
            OkButton.Click += (s, e) => { SelectedColor = Color.FromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value); DialogResult = true; };
            CancelButton.Click += (s, e) => { DialogResult = false; };
        }

        private void UpdatePreview()
        {
            var c = Color.FromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value);
            PreviewBorder.Background = new SolidColorBrush(c);
        }
    }
}

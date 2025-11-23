using System;
using System.Windows;
using System.Windows.Media;

namespace FamidashEditor
{
    public partial class ColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; } = Color.FromArgb(255, 40, 40, 40);
        // raised while sliders change so callers can react in realtime
        public event Action<Color>? ColorChanged;

        public ColorPickerWindow(Color initial)
        {
            InitializeComponent();
            SelectedColor = initial;
            RSlider.Value = initial.R; GSlider.Value = initial.G; BSlider.Value = initial.B; ASlider.Value = initial.A;
            UpdatePreview();
            RSlider.ValueChanged += (_, __) => { RVal.Text = ((int)RSlider.Value).ToString(); UpdatePreview(); };
            GSlider.ValueChanged += (_, __) => { GVal.Text = ((int)GSlider.Value).ToString(); UpdatePreview(); };
            BSlider.ValueChanged += (_, __) => { BVal.Text = ((int)BSlider.Value).ToString(); UpdatePreview(); };
            ASlider.ValueChanged += (_, __) => { AVal.Text = ((int)ASlider.Value).ToString(); UpdatePreview(); };
            OkButton.Click += (s, e) => { SelectedColor = Color.FromArgb((byte)ASlider.Value, (byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value); DialogResult = true; };
            CancelButton.Click += (s, e) => { DialogResult = false; };
        }

        private void UpdatePreview()
        {
            var c = Color.FromArgb((byte)ASlider.Value, (byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value);
            PreviewBorder.Background = new SolidColorBrush(c);
            ColorChanged?.Invoke(c);
        }
    }
}

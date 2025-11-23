using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamidashEditor
{
    public partial class ColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; } = Color.FromArgb(255, 40, 40, 40);
        // raised while selection/alpha change so callers can react in realtime
        public event Action<Color>? ColorChanged;

        // Palette colors - arranged visually to match the provided small palette image
        private static readonly Color[] PaletteColors = new Color[] {
            // row 1: darks / deep hues
            Color.FromRgb(32,32,64), Color.FromRgb(48,0,128), Color.FromRgb(96,16,96), Color.FromRgb(120,40,40), Color.FromRgb(88,40,16), Color.FromRgb(32,72,32), Color.FromRgb(16,72,64), Color.FromRgb(16,64,96), Color.FromRgb(8,48,64), Color.FromRgb(0,0,0), Color.FromRgb(64,64,64), Color.FromRgb(88,88,88),
            // row 2: brights / pastels
            Color.FromRgb(240,240,240), Color.FromRgb(224,224,224), Color.FromRgb(200,192,200), Color.FromRgb(200,152,96), Color.FromRgb(240,200,160), Color.FromRgb(200,240,120), Color.FromRgb(128,240,160), Color.FromRgb(96,208,224), Color.FromRgb(64,200,216), Color.FromRgb(80,80,80), Color.FromRgb(120,120,120), Color.FromRgb(152,152,152),
            // row 3: saturated colors
            Color.FromRgb(32,96,240), Color.FromRgb(112,32,200), Color.FromRgb(192,32,112), Color.FromRgb(208,80,24), Color.FromRgb(160,96,24), Color.FromRgb(72,144,40), Color.FromRgb(40,120,40), Color.FromRgb(16,120,80), Color.FromRgb(8,96,120), Color.FromRgb(24,24,24), Color.FromRgb(144,144,144), Color.FromRgb(200,200,200),
            // row 4: lighter pastels
            Color.FromRgb(248,248,240), Color.FromRgb(232,216,224), Color.FromRgb(216,192,232), Color.FromRgb(232,168,136), Color.FromRgb(232,208,168), Color.FromRgb(216,232,160), Color.FromRgb(176,232,200), Color.FromRgb(160,232,232), Color.FromRgb(152,232,232), Color.FromRgb(200,200,200), Color.FromRgb(216,216,216), Color.FromRgb(208,208,208),
        };

    // selection state (single index)
    private int selectedIndex = -1;
    private readonly bool allowAlpha;

        public ColorPickerWindow(Color initial, bool allowAlpha = true)
        {
            InitializeComponent();
            SelectedColor = initial;
            this.allowAlpha = allowAlpha;

            // Build palette UI (single-select). Preview on hover, select on click.
            for (int i = 0; i < PaletteColors.Length; i++)
            {
                var col = PaletteColors[i];
                // create a compact swatch that matches the visual style of the attached palette
                var swatchBorder = new Border
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(col),
                    Tag = i,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
                    CornerRadius = new CornerRadius(2)
                };
                swatchBorder.MouseLeftButtonDown += (s, e) => { SelectIndex((int)((Border)s).Tag, clearOthers: true); };
                swatchBorder.MouseEnter += (s, e) => { PreviewIndex((int)((Border)s).Tag); };
                swatchBorder.MouseLeave += (s, e) => { RevertPreview(); };
                PalettePanel.Items.Add(swatchBorder);
            }

            // Alpha slider: only enable if allowed; otherwise force 255 and hide the control
            if (allowAlpha)
            {
                ASlider.Value = initial.A;
                AVal.Text = ((int)ASlider.Value).ToString();
                ASlider.ValueChanged += (_, __) => { AVal.Text = ((int)ASlider.Value).ToString(); UpdatePreviewAndNotify(); };
            }
            else
            {
                // hide alpha UI and force full strength
                if (AlphaPanel != null) AlphaPanel.Visibility = Visibility.Collapsed;
                ASlider.Value = 255;
                AVal.Text = "255";
            }

            // Preselect nearest palette color if any
            int nearest = FindNearestPaletteIndex(initial);
            if (nearest >= 0) SelectIndex(nearest, clearOthers: true);
            else
            {
                // if no exact match, select first by default
                SelectIndex(0, clearOthers: true);
            }

            OkButton.Click += (s, e) => { DialogResult = true; SelectedColor = GetBlendedColor(); };
            CancelButton.Click += (s, e) => { DialogResult = false; };

            UpdatePreviewAndNotify();
        }

        private void PreviewIndex(int idx)
        {
            if (idx < 0 || idx >= PaletteColors.Length) return;
            var alpha = allowAlpha ? (byte)ASlider.Value : (byte)255;
            var c = Color.FromArgb(alpha, PaletteColors[idx].R, PaletteColors[idx].G, PaletteColors[idx].B);
            PreviewBorder.Background = new SolidColorBrush(c);
            ColorChanged?.Invoke(c);
        }

        private void RevertPreview()
        {
            // restore preview to the currently selected index
            if (selectedIndex >= 0) PreviewIndex(selectedIndex);
        }

        private void SelectIndex(int idx, bool clearOthers)
        {
            if (idx < 0 || idx >= PaletteColors.Length) return;
            selectedIndex = idx;
            // update borders
            for (int i = 0; i < PalettePanel.Items.Count; i++)
            {
                if (PalettePanel.Items[i] is Border b)
                {
                    b.BorderBrush = (i == selectedIndex) ? Brushes.White : Brushes.Transparent;
                }
            }
            UpdatePreviewAndNotify();
        }

        private Color GetBlendedColor()
        {
            // single selection -> return selected palette color with alpha
            var alpha = allowAlpha ? (byte)ASlider.Value : (byte)255;
            if (selectedIndex < 0 || selectedIndex >= PaletteColors.Length) return Color.FromArgb(alpha, 0, 0, 0);
            var p = PaletteColors[selectedIndex];
            return Color.FromArgb(alpha, p.R, p.G, p.B);
        }

        private void UpdatePreviewAndNotify()
        {
            var c = GetBlendedColor();
            PreviewBorder.Background = new SolidColorBrush(c);
            ColorChanged?.Invoke(c);
        }

        private int FindNearestPaletteIndex(Color initial)
        {
            int best = -1; double bestDist = double.MaxValue;
            for (int i = 0; i < PaletteColors.Length; i++)
            {
                var p = PaletteColors[i];
                double dr = p.R - initial.R; double dg = p.G - initial.G; double db = p.B - initial.B;
                double d = dr * dr + dg * dg + db * db;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }
}

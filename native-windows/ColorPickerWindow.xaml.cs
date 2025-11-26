using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamidashEditor
{
    public partial class ColorPickerWindow : Window
    {
        public Color SelectedColor { get; private set; } = Color.FromArgb(255, 40, 40, 40);
        // raised while selection/alpha change so callers can react in realtime
        public event Action<Color>? ColorChanged;

        // Palette colors. We prefer loading an exact palette image (14x4 grid) from disk if present
        // (place a file named "palette.png" or "palette.bmp" next to the exe). Otherwise fall back
        // to the built-in approximate palette below.
        private readonly Color[] PaletteColors;
        private static readonly Color[] DefaultPaletteColors = new Color[] {
            // Row 1 (dark accents)
            Color.FromRgb(128,128,128), Color.FromRgb(0,40,120), Color.FromRgb(48,0,128), Color.FromRgb(88,24,120), Color.FromRgb(128,24,24), Color.FromRgb(160,72,32), Color.FromRgb(96,64,24), Color.FromRgb(24,96,24), Color.FromRgb(32,120,48), Color.FromRgb(8,112,104), Color.FromRgb(0,96,128), Color.FromRgb(0,72,104), Color.FromRgb(0,48,72), Color.FromRgb(16,16,16),
            // Row 2 (vibrant midtones)
            Color.FromRgb(255,255,255), Color.FromRgb(32,96,220), Color.FromRgb(96,32,208), Color.FromRgb(136,24,176), Color.FromRgb(192,48,48), Color.FromRgb(216,112,48), Color.FromRgb(176,120,48), Color.FromRgb(136,168,48), Color.FromRgb(96,200,48), Color.FromRgb(48,200,120), Color.FromRgb(0,168,200), Color.FromRgb(0,136,176), Color.FromRgb(64,112,120), Color.FromRgb(24,24,24),
            // Row 3 (bright pastels / neons)
            Color.FromRgb(248,248,248), Color.FromRgb(88,176,248), Color.FromRgb(160,112,232), Color.FromRgb(232,64,160), Color.FromRgb(232,96,48), Color.FromRgb(232,152,56), Color.FromRgb(200,168,72), Color.FromRgb(168,208,88), Color.FromRgb(128,224,96), Color.FromRgb(88,224,176), Color.FromRgb(80,192,224), Color.FromRgb(72,168,192), Color.FromRgb(64,152,176), Color.FromRgb(96,96,96),
            // Row 4 (paler pastels / neutrals)
            Color.FromRgb(240,240,240), Color.FromRgb(200,160,200), Color.FromRgb(176,128,208), Color.FromRgb(232,160,112), Color.FromRgb(224,200,160), Color.FromRgb(216,232,120), Color.FromRgb(200,232,184), Color.FromRgb(168,232,232), Color.FromRgb(144,232,232), Color.FromRgb(200,200,200), Color.FromRgb(216,216,216), Color.FromRgb(200,200,200), Color.FromRgb(176,176,176), Color.FromRgb(144,144,144),
        };

        // selection state (single index)
        private int selectedIndex = -1;
        private readonly bool allowAlpha;
        // debounce timer to avoid flooding callers with rapid ColorChanged events
        private System.Windows.Threading.DispatcherTimer? debounceTimer = null;
        private Color? pendingColor = null;

        public ColorPickerWindow(Color initial, bool allowAlpha = true)
        {
            InitializeComponent();
            SelectedColor = initial;
            this.allowAlpha = allowAlpha;

            // Try to load palette data. Prefer a .pal file in the repo assets (if present), then
            // a palette image next to the exe (palette.png/bmp). Fall back to defaults.
            Color[]? loaded = null;
            try
            {
                loaded = TryLoadPaletteFromPalFile("palette.pal") ?? TryLoadPaletteFromFile("palette.png") ?? TryLoadPaletteFromFile("palette.bmp");
            }
            catch { /* ignore load failures and fall back to defaults */ }
            PaletteColors = loaded ?? DefaultPaletteColors;

            // Build palette UI (single-select). Preview on hover, select on click.
            for (int i = 0; i < PaletteColors.Length; i++)
            {
                var col = PaletteColors[i];
                // create a compact swatch that matches the visual style of the attached palette
                var swatchBorder = new Border
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Thickness(2),
                    Background = new SolidColorBrush(col),
                    Tag = i,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
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
                ASlider.ValueChanged += (_, __) => { AVal.Text = ((int)ASlider.Value).ToString(); SchedulePreviewAndNotify(); };
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
            ScheduleNotify(c);
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
            ScheduleNotify(c);
        }

        // Schedule ColorChanged notification with a short debounce so callers (which may rebuild
        // heavy bitmaps) are not flooded while the user drags/hovers. Immediate preview border
        // updates still occur visually.
        private void ScheduleNotify(Color c)
        {
            pendingColor = c;
            if (debounceTimer == null)
            {
                debounceTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal)
                {
                    Interval = TimeSpan.FromMilliseconds(60)
                };
                debounceTimer.Tick += (s, e) =>
                {
                    debounceTimer?.Stop();
                    debounceTimer = null;
                    if (pendingColor.HasValue) ColorChanged?.Invoke(pendingColor.Value);
                    pendingColor = null;
                };
            }
            else
            {
                // restart
                debounceTimer.Stop();
            }
            debounceTimer.Start();
        }

        // Helper used by slider handler and preview to schedule with current blended color
        private void SchedulePreviewAndNotify()
        {
            var c = GetBlendedColor();
            // update immediate preview border
            PreviewBorder.Background = new SolidColorBrush(c);
            ScheduleNotify(c);
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

        // Try to load a palette image (14 cols x 4 rows) from a file beside the exe and sample the
        // center pixel of each cell to build the palette colors. Returns null on failure.
        private Color[]? TryLoadPaletteFromFile(string fileName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var full = System.IO.Path.Combine(baseDir, fileName);
            if (!System.IO.File.Exists(full)) return null;

            // Load bitmap into a BitmapSource with Bgra32 format
            BitmapSource? src;
            using (var fs = System.IO.File.OpenRead(full))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = fs;
                bmp.EndInit();
                bmp.Freeze();
                src = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            }

            if (src == null) return null;
            int w = src.PixelWidth; int h = src.PixelHeight;
            const int cols = 14; const int rows = 4;
            if (w < cols || h < rows) return null;

            int stride = w * 4;
            byte[] pixels = new byte[h * stride];
            src.CopyPixels(pixels, stride, 0);

            // Determine a likely background color by sampling the top-left pixel and nearby area.
            // Use a small tolerance because anti-aliased borders may slightly differ.
            byte bgB = pixels[0]; byte bgG = pixels[1]; byte bgR = pixels[2];
            bool IsNonBackground(int px, int py)
            {
                int off = (py * w + px) * 4;
                int db = pixels[off + 0] - bgB; int dg = pixels[off + 1] - bgG; int dr = pixels[off + 2] - bgR;
                int dist2 = db * db + dg * dg + dr * dr;
                return dist2 > (12 * 12); // tolerance ~12
            }

            // Trim columns/rows from the outside that are predominantly background.
            int left = 0, right = w - 1, top = 0, bottom = h - 1;
            // helper to test a vertical column for non-bg pixels
            bool ColumnHasNonBg(int col)
            {
                int count = 0; int total = bottom - top + 1;
                for (int y = top; y <= bottom; y++) if (IsNonBackground(col, y)) count++;
                return count * 100 > total * 3; // >3% non-bg
            }
            bool RowHasNonBg(int row)
            {
                int count = 0; int total = right - left + 1;
                for (int x = left; x <= right; x++) if (IsNonBackground(x, row)) count++;
                return count * 100 > total * 3; // >3% non-bg
            }

            // shrink left/top/right/bottom until we hit non-bg
            while (left < right && !ColumnHasNonBg(left)) left++;
            while (right > left && !ColumnHasNonBg(right)) right--;
            while (top < bottom && !RowHasNonBg(top)) top++;
            while (bottom > top && !RowHasNonBg(bottom)) bottom--;

            if (right <= left || bottom <= top) return null; // weird image

            int cropW = right - left + 1;
            int cropH = bottom - top + 1;

            // compute cells from trimmed area
            double cellWf = (double)cropW / cols;
            double cellHf = (double)cropH / rows;

            var outColors = new Color[cols * rows];
            int sampleRadius = 1; // 3x3
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    double fx = left + (c + 0.5) * cellWf;
                    double fy = top + (r + 0.5) * cellHf;
                    int cx = Math.Min(w - 1, Math.Max(0, (int)Math.Round(fx)));
                    int cy = Math.Min(h - 1, Math.Max(0, (int)Math.Round(fy)));

                    long sumR = 0, sumG = 0, sumB = 0; int count = 0;
                    for (int sy = cy - sampleRadius; sy <= cy + sampleRadius; sy++)
                    {
                        if (sy < 0 || sy >= h) continue;
                        for (int sx = cx - sampleRadius; sx <= cx + sampleRadius; sx++)
                        {
                            if (sx < 0 || sx >= w) continue;
                            int off = (sy * w + sx) * 4;
                            byte b = pixels[off + 0];
                            byte g = pixels[off + 1];
                            byte rcol = pixels[off + 2];
                            sumR += rcol; sumG += g; sumB += b; count++;
                        }
                    }
                    if (count == 0) outColors[r * cols + c] = Color.FromRgb(0, 0, 0);
                    else outColors[r * cols + c] = Color.FromRgb((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
                }
            }

            return outColors;
        }

        // Try to locate and read a .pal file (raw RGB triplets). We expect at least 56 entries (14x4).
        // Check a few likely locations: exe folder, exe/..\..\src\renderer\assets, and repo src path.
        private Color[]? TryLoadPaletteFromPalFile(string fileName)
        {
            // candidate locations
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new List<string>() {
                System.IO.Path.Combine(baseDir, fileName),
                System.IO.Path.Combine(baseDir, "assets", fileName),
                System.IO.Path.Combine(baseDir, "renderer", "assets", fileName),
                System.IO.Path.Combine(baseDir, "..", "..", "src", "renderer", "assets", fileName),
                System.IO.Path.Combine(Environment.CurrentDirectory, "src", "renderer", "assets", fileName),
                System.IO.Path.Combine(baseDir, "..", "assets", fileName),
            };

            // also try repository-relative absolute path if running in development (common workspace layout)
            var repoPal = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar), "..", "..", "..", "src", "renderer", "assets", fileName);
            candidates.Add(System.IO.Path.GetFullPath(repoPal));

            string? found = null;
            foreach (var c in candidates)
            {
                try { if (System.IO.File.Exists(c)) { found = c; break; } } catch { }
            }
            if (found == null) return null;

            byte[] data;
            try { data = System.IO.File.ReadAllBytes(found); } catch { return null; }
            if (data == null || data.Length < 3) return null;

            int triplets = data.Length / 3;
            // prefer first 56 entries
            int needed = Math.Min(56, triplets);
            if (needed <= 0) return null;

            var outColors = new Color[needed];
            for (int i = 0; i < needed; i++)
            {
                int off = i * 3;
                byte r = data[off + 0];
                byte g = data[off + 1];
                byte b = data[off + 2];
                outColors[i] = Color.FromRgb(r, g, b);
            }

            // If the file had fewer than 56 colors, return what we have.
            return outColors;
        }
    }
}

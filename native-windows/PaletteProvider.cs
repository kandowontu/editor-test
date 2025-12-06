using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamidashEditor
{
    public static class PaletteProvider
    {
        // Public access to a palette (14 cols x 4 rows expected). Will attempt to load a palette
        // file next to the exe; otherwise fall back to a built-in default.
        public static Color[] GetPalette()
        {
            try
            {
                var loaded = TryLoadPaletteFromPalFile("palette.pal") ?? TryLoadPaletteFromFile("palette.png") ?? TryLoadPaletteFromFile("palette.bmp");
                if (loaded != null) return loaded;
            }
            catch { }
            return DefaultPaletteColors;
        }

        private static readonly Color[] DefaultPaletteColors = new Color[] {
            Color.FromRgb(105,107,99), Color.FromRgb(0,23,116), Color.FromRgb(30,0,135), Color.FromRgb(52,0,115), Color.FromRgb(86,0,87), Color.FromRgb(94,0,19), Color.FromRgb(83,26,0), Color.FromRgb(59,36,0), Color.FromRgb(36,48,0), Color.FromRgb(6,58,0), Color.FromRgb(0,63,0), Color.FromRgb(0,59,30), Color.FromRgb(0,51,78), Color.FromRgb(0,0,0),
            Color.FromRgb(185,187,179), Color.FromRgb(20,83,185), Color.FromRgb(77,44,218), Color.FromRgb(103,30,222), Color.FromRgb(152,24,156), Color.FromRgb(157,35,68), Color.FromRgb(160,62,0), Color.FromRgb(141,85,0), Color.FromRgb(101,109,0), Color.FromRgb(44,121,0), Color.FromRgb(0,129,0), Color.FromRgb(0,125,66), Color.FromRgb(0,120,138), Color.FromRgb(0,0,0),
            Color.FromRgb(255,255,255), Color.FromRgb(105,168,255), Color.FromRgb(150,145,255), Color.FromRgb(178,138,250), Color.FromRgb(234,125,250), Color.FromRgb(243,123,199), Color.FromRgb(242,142,89), Color.FromRgb(230,173,39), Color.FromRgb(215,200,5), Color.FromRgb(144,223,7), Color.FromRgb(100,229,60), Color.FromRgb(69,226,125), Color.FromRgb(72,213,217), Color.FromRgb(78,80,72),
            Color.FromRgb(255,255,255), Color.FromRgb(210,234,255), Color.FromRgb(226,226,255), Color.FromRgb(233,216,255), Color.FromRgb(245,210,255), Color.FromRgb(248,217,234), Color.FromRgb(250,222,185), Color.FromRgb(249,232,155), Color.FromRgb(243,242,140), Color.FromRgb(211,250,145), Color.FromRgb(184,252,168), Color.FromRgb(174,250,202), Color.FromRgb(202,243,243), Color.FromRgb(190,192,184),
        };

        private static Color[]? TryLoadPaletteFromFile(string fileName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var full = System.IO.Path.Combine(baseDir, fileName);
            if (!System.IO.File.Exists(full)) return null;

            try
            {
                BitmapSource? src;
                using (var fs = System.IO.File.OpenRead(full))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = fs; bmp.EndInit(); bmp.Freeze();
                    src = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
                }
                if (src == null) return null;
                int w = src.PixelWidth; int h = src.PixelHeight;
                const int cols = 14; const int rows = 4;
                if (w < cols || h < rows) return null;

                int stride = w * 4; byte[] pixels = new byte[h * stride]; src.CopyPixels(pixels, stride, 0);

                // determine trimmed area similar to ColorPickerWindow logic
                byte bgB = pixels[0]; byte bgG = pixels[1]; byte bgR = pixels[2];
                bool IsNonBackground(int px, int py)
                {
                    int off = (py * w + px) * 4;
                    int db = pixels[off + 0] - bgB; int dg = pixels[off + 1] - bgG; int dr = pixels[off + 2] - bgR;
                    int dist2 = db * db + dg * dg + dr * dr;
                    return dist2 > (12 * 12);
                }
                int left = 0, right = w - 1, top = 0, bottom = h - 1;
                bool ColumnHasNonBg(int col) { int count = 0; int total = bottom - top + 1; for (int y = top; y <= bottom; y++) if (IsNonBackground(col, y)) count++; return count * 100 > total * 3; }
                bool RowHasNonBg(int row) { int count = 0; int total = right - left + 1; for (int x = left; x <= right; x++) if (IsNonBackground(x, row)) count++; return count * 100 > total * 3; }
                while (left < right && !ColumnHasNonBg(left)) left++; while (right > left && !ColumnHasNonBg(right)) right--; while (top < bottom && !RowHasNonBg(top)) top++; while (bottom > top && !RowHasNonBg(bottom)) bottom--;
                if (right <= left || bottom <= top) return null;
                int cropW = right - left + 1; int cropH = bottom - top + 1;
                double cellWf = (double)cropW / cols; double cellHf = (double)cropH / rows;

                var outColors = new Color[cols * rows]; int sampleRadius = 1;
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
                                int off = (sy * w + sx) * 4; byte b = pixels[off + 0]; byte g = pixels[off + 1]; byte rcol = pixels[off + 2]; sumR += rcol; sumG += g; sumB += b; count++;
                            }
                        }
                        if (count == 0) outColors[r * cols + c] = Color.FromRgb(0, 0, 0);
                        else outColors[r * cols + c] = Color.FromRgb((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
                    }
                }
                return outColors;
            }
            catch { return null; }
        }

        private static Color[]? TryLoadPaletteFromPalFile(string fileName)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new List<string>() { System.IO.Path.Combine(baseDir, fileName), System.IO.Path.Combine(baseDir, "assets", fileName), System.IO.Path.Combine(baseDir, "renderer", "assets", fileName), System.IO.Path.Combine(baseDir, "..", "assets", fileName) };
            string? found = null;
            foreach (var c in candidates) { try { if (System.IO.File.Exists(c)) { found = c; break; } } catch { } }
            if (found == null) return null;
            byte[] data; try { data = System.IO.File.ReadAllBytes(found); } catch { return null; }
            if (data == null || data.Length < 3) return null; int triplets = data.Length / 3; int needed = Math.Min(56, triplets); if (needed <= 0) return null;
            var outColors = new Color[needed]; for (int i = 0; i < needed; i++) { int off = i * 3; byte r = data[off + 0]; byte g = data[off + 1]; byte b = data[off + 2]; outColors[i] = Color.FromRgb(r, g, b); }
            return outColors;
        }
    }
}

using System.Windows.Media;
using System;

namespace FamidashEditor
{
    public static class PaletteHelper
    {
        public const int Columns = 14;
        public const int Rows = 4;
        // Copied default palette from ColorPickerWindow.DefaultPaletteColors
        public static readonly Color[] Palette = new Color[] {
            Color.FromRgb(105,107,99), Color.FromRgb(0,23,116), Color.FromRgb(30,0,135), Color.FromRgb(52,0,115), Color.FromRgb(86,0,87), Color.FromRgb(94,0,19), Color.FromRgb(83,26,0), Color.FromRgb(59,36,0), Color.FromRgb(36,48,0), Color.FromRgb(6,58,0), Color.FromRgb(0,63,0), Color.FromRgb(0,59,30), Color.FromRgb(0,51,78), Color.FromRgb(0,0,0),
            Color.FromRgb(185,187,179), Color.FromRgb(20,83,185), Color.FromRgb(77,44,218), Color.FromRgb(103,30,222), Color.FromRgb(152,24,156), Color.FromRgb(157,35,68), Color.FromRgb(160,62,0), Color.FromRgb(141,85,0), Color.FromRgb(101,109,0), Color.FromRgb(44,121,0), Color.FromRgb(0,129,0), Color.FromRgb(0,125,66), Color.FromRgb(0,120,138), Color.FromRgb(0,0,0),
            Color.FromRgb(255,255,255), Color.FromRgb(105,168,255), Color.FromRgb(150,145,255), Color.FromRgb(178,138,250), Color.FromRgb(234,125,250), Color.FromRgb(243,123,199), Color.FromRgb(242,142,89), Color.FromRgb(230,173,39), Color.FromRgb(215,200,5), Color.FromRgb(144,223,7), Color.FromRgb(100,229,60), Color.FromRgb(69,226,125), Color.FromRgb(72,213,217), Color.FromRgb(78,80,72),
            Color.FromRgb(255,255,255), Color.FromRgb(210,234,255), Color.FromRgb(226,226,255), Color.FromRgb(233,216,255), Color.FromRgb(245,210,255), Color.FromRgb(248,217,234), Color.FromRgb(250,222,185), Color.FromRgb(249,232,155), Color.FromRgb(243,242,140), Color.FromRgb(211,250,145), Color.FromRgb(184,252,168), Color.FromRgb(174,250,202), Color.FromRgb(202,243,243), Color.FromRgb(190,192,184),
        };

        // Find nearest palette index by RGB distance (ignores alpha)
        public static int FindNearestIndex(Color c)
        {
            int best = 0; double bestDist = double.MaxValue;
            for (int i = 0; i < Palette.Length; i++)
            {
                var p = Palette[i];
                double dr = p.R - c.R; double dg = p.G - c.G; double db = p.B - c.B;
                double d = dr * dr + dg * dg + db * db;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // Given a selected color, return the palette color one row up (darker);
        // if the selected color is on the top row, return black.
        public static Color RowUpColor(Color selected)
        {
            int idx = FindNearestIndex(selected);
            int col = idx % Columns;
            int row = idx / Columns;
            if (row <= 0) return Color.FromRgb(0, 0, 0);
            int upIdx = (row - 1) * Columns + col;
            if (upIdx < 0 || upIdx >= Palette.Length) return Color.FromRgb(0, 0, 0);
            return Palette[upIdx];
        }
    }
}

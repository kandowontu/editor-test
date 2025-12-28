using System;

class Program
{
    static void Main()
    {
        // sample grey-like pixel from tiles and sample tint (first column color)
        byte pr = 78, pg = 80, pb = 72; // pixel
        byte tr = 105, tg = 107, tb = 99; // tint (palette first column)

        RgbToHsl(tr, tg, tb, out double tintH, out double tintS, out double tintL);
        RgbToHsl(pr, pg, pb, out double ph, out double ps, out double pl);

        Console.WriteLine($"pixel RGB=({pr},{pg},{pb}) -> HSL=({ph:F2},{ps:F3},{pl:F3})");
        Console.WriteLine($"tint  RGB=({tr},{tg},{tb}) -> HSL=({tintH:F2},{tintS:F3},{tintL:F3})");

        double nh = tintH; double ns = ps; double nl = pl;
        RgbFromHsl(nh, ns, nl, out byte nr, out byte ng, out byte nb);
        Console.WriteLine($"mapped RGB=({nr},{ng},{nb}) from HSL=({nh:F2},{ns:F3},{nl:F3})");
    }

    private static void RgbToHsl(byte r8, byte g8, byte b8, out double h, out double s, out double l)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;
        if (max == min)
        {
            h = 0.0; s = 0.0; return;
        }
        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h *= 60.0;
    }

    private static void RgbFromHsl(double h, double s, double l, out byte r8, out byte g8, out byte b8)
    {
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l; // achromatic
        }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            double hk = (h % 360.0) / 360.0;
            double[] t = new double[3] { hk + 1.0 / 3.0, hk, hk - 1.0 / 3.0 };
            double[] rgb = new double[3];
            for (int i = 0; i < 3; i++)
            {
                double tc = t[i];
                if (tc < 0) tc += 1.0; if (tc > 1) tc -= 1.0;
                if (tc < 1.0 / 6.0) rgb[i] = p + (q - p) * 6.0 * tc;
                else if (tc < 1.0 / 2.0) rgb[i] = q;
                else if (tc < 2.0 / 3.0) rgb[i] = p + (q - p) * (2.0 / 3.0 - tc) * 6.0;
                else rgb[i] = p;
            }
            r = rgb[0]; g = rgb[1]; b = rgb[2];
        }
        r8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(r * 255.0)));
        g8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(g * 255.0)));
        b8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(b * 255.0)));
    }
}

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamidashEditor.Editor.Rendering
{
    /// <summary>
    /// Helper methods for color manipulation and image tinting operations.
    /// </summary>
    public static class ColorHelpers
    {
        /// <summary>
        /// Create tinted copies of images using simple alpha blend with the tint color.
        /// </summary>
        public static ImageSource[]? CreateTintedImages(ImageSource[]? originals, Color tint)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // no tint => return originals so drawing still works
            var outList = new List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
                {
                    // convert to Bgra32 for pixel access
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                    int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    byte ta = tint.A; int tintA = ta;
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        int b = pixels[i + 0];
                        int g = pixels[i + 1];
                        int r = pixels[i + 2];
                        int a = pixels[i + 3];
                        // simple linear blend: out = original*(1 - tA) + tintRGB * tA
                        int outR = (r * (255 - tintA) + tint.R * tintA) / 255;
                        int outG = (g * (255 - tintA) + tint.G * tintA) / 255;
                        int outB = (b * (255 - tintA) + tint.B * tintA) / 255;
                        pixels[i + 0] = (byte)outB;
                        pixels[i + 1] = (byte)outG;
                        pixels[i + 2] = (byte)outR;
                        pixels[i + 3] = (byte)a; // keep original alpha
                    }

                    var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                    wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                    wb.Freeze();
                    outList.Add(wb);
                }
                else
                {
                    outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        /// <summary>
        /// Create exact RGB-replaced copies of images. For every non-black, non-transparent pixel
        /// we replace the RGB channels with the tint's RGB (preserve original alpha).
        /// </summary>
        public static ImageSource[]? CreateRgbReplacedImages(ImageSource[]? originals, Color tint)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // no change requested
            var outList = new List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            byte a = pixels[i + 3];
                            bool isBlack = (r <= 12 && g <= 12 && b <= 12);
                            if (a != 0 && !isBlack)
                            {
                                pixels[i + 0] = tint.B;
                                pixels[i + 1] = tint.G;
                                pixels[i + 2] = tint.R;
                            }
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        wb.Freeze();
                        outList.Add(wb);
                    }
                    catch
                    {
                        outList.Add(src);
                    }
                }
                else
                {
                    outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        /// <summary>
        /// Create HSL-hue shifted copies of images. For each visible, non-black/non-white pixel
        /// we replace the hue with the tint's hue while preserving the original saturation and lightness.
        /// </summary>
        public static ImageSource[]? CreateHslShiftedImages(ImageSource[]? originals, Color tint)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // no change requested
            // Precompute tint hue
            RgbToHsl(tint.R, tint.G, tint.B, out double tintH, out double tintS, out double tintL);

            var outList = new List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            byte a = pixels[i + 3];
                            // Skip fully transparent, near-black, and near-white pixels
                            bool isBlack = (r <= 12 && g <= 12 && b <= 12);
                            bool isWhite = (r >= 249 && g >= 249 && b >= 249);
                            if (a == 0 || isBlack || isWhite) continue;

                            // Convert pixel to HSL, replace hue with tint hue, keep S/L
                            RgbToHsl(r, g, b, out double ph, out double ps, out double pl);
                            double nh = tintH; // replace hue
                            double ns = ps;
                            double nl = pl;
                            RgbFromHsl(nh, ns, nl, out byte nr, out byte ng, out byte nb);

                            pixels[i + 0] = nb;
                            pixels[i + 1] = ng;
                            pixels[i + 2] = nr;
                            // alpha unchanged
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        wb.Freeze();
                        outList.Add(wb);
                    }
                    catch
                    {
                        outList.Add(src);
                    }
                }
                else
                {
                    outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        /// <summary>
        /// Create hue-shifted copies with interpolated saturation strength.
        /// </summary>
        public static ImageSource[]? CreateHueShiftedImages(ImageSource[]? originals, Color tint)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // strength 0 => no change

            double strength = tint.A / 255.0;
            // convert tint color to HSL once
            RgbToHsl(tint.R, tint.G, tint.B, out double tintH, out double tintS, out double tintL);
            var outList = new List<ImageSource>(originals.Length);

            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            int b = pixels[i + 0];
                            int g = pixels[i + 1];
                            int r = pixels[i + 2];
                            int a = pixels[i + 3];

                            RgbToHsl((byte)r, (byte)g, (byte)b, out double h0, out double s0, out double l0);

                            // interpolate hue towards tint hue, and optionally scale/lerp saturation
                            double newH = LerpAngle(h0, tintH, strength);
                            double newS = s0 * (1.0 - strength) + tintS * strength;
                            double newL = l0; // preserve original lightness to keep details

                            RgbFromHsl(newH, newS, newL, out byte r2, out byte g2, out byte b2);

                            pixels[i + 0] = b2;
                            pixels[i + 1] = g2;
                            pixels[i + 2] = r2;
                            pixels[i + 3] = (byte)a; // keep original alpha
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        wb.Freeze();
                        outList.Add(wb);
                    }
                    catch
                    {
                        outList.Add(src);
                    }
                }
                else
                {
                    outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        /// <summary>
        /// Convert RGB byte values to HSL (H in degrees 0..360, S/L 0..1).
        /// </summary>
        public static void RgbToHsl(byte r8, byte g8, byte b8, out double h, out double s, out double l)
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

        /// <summary>
        /// Convert HSL to RGB bytes. H in degrees 0..360, S/L 0..1.
        /// </summary>
        public static void RgbFromHsl(double h, double s, double l, out byte r8, out byte g8, out byte b8)
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

        /// <summary>
        /// Linear interpolation for circular hue (degrees). t in 0..1.
        /// </summary>
        public static double LerpAngle(double a, double b, double t)
        {
            // convert to radians for shortest path
            double diff = (b - a + 540.0) % 360.0 - 180.0;
            return (a + diff * t + 360.0) % 360.0;
        }

        /// <summary>
        /// Create an ImageSource that uses the toned image for colors but replaces any pixels 
        /// where the original (base) image has G >= 180 with the provided player tint.
        /// </summary>
        public static ImageSource? CreatePlayerReplacedFromBaseAndToned(ImageSource? baseSrc, ImageSource? tonedSrc, Color tint)
        {
            if (baseSrc == null && tonedSrc == null) return null;
            try
            {
                var baseBs = baseSrc as BitmapSource;
                var tonedBs = tonedSrc as BitmapSource ?? baseBs;
                if (tonedBs == null) return baseSrc;
                var convBase = baseBs != null ? new FormatConvertedBitmap(baseBs, PixelFormats.Bgra32, null, 0) : null;
                var convToned = new FormatConvertedBitmap(tonedBs, PixelFormats.Bgra32, null, 0);
                int w = convToned.PixelWidth, h = convToned.PixelHeight;
                if (w <= 0 || h <= 0) return tonedSrc;
                int stride = w * 4;
                var basePixels = new byte[h * stride];
                var tonedPixels = new byte[h * stride];
                if (convBase != null)
                {
                    convBase.CopyPixels(basePixels, stride, 0);
                }
                convToned.CopyPixels(tonedPixels, stride, 0);

                for (int i = 0; i < tonedPixels.Length; i += 4)
                {
                    byte baseG = convBase != null ? basePixels[i + 1] : (byte)0;
                    if (baseG >= 180)
                    {
                        byte pr = tint.R, pg = tint.G, pb = tint.B;
                        tonedPixels[i + 0] = pb;
                        tonedPixels[i + 1] = pg;
                        tonedPixels[i + 2] = pr;
                    }
                }

                var wb = new WriteableBitmap(w, h, convToned.DpiX, convToned.DpiY, PixelFormats.Bgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), tonedPixels, stride, 0);
                wb.Freeze();
                return wb;
            }
            catch
            {
                return tonedSrc;
            }
        }
    }
}

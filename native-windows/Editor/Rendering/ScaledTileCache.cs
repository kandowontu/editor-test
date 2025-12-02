using System.Windows;

namespace FamidashEditor.Editor.Rendering
{
    public class ScaledTileCache
    {
        public byte[][] Pixels { get; set; }
        public int TileW { get; set; }
        public int TileH { get; set; }
        public int Stride { get; set; }
        public double Scale { get; set; }
        public DpiScale Dpi { get; set; }

        public ScaledTileCache(byte[][] pixels, int w, int h, int stride, double scale, DpiScale dpi)
        {
            Pixels = pixels;
            TileW = w;
            TileH = h;
            Stride = stride;
            Scale = scale;
            Dpi = dpi;
        }
    }
}

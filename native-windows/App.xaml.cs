using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamidashEditor
{
    public partial class App : Application
    {
        private SplashScreen? splash;
        
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Install global exception handlers so we capture crashes to disk for diagnosis.
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
            {
                try { LogException(ev.ExceptionObject as Exception, "AppDomain.UnhandledException"); } catch { }
            };
            this.DispatcherUnhandledException += (s, ev) =>
            {
                try { LogException(ev.Exception, "DispatcherUnhandledException"); } catch { }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ev) =>
            {
                try { LogException(ev.Exception, "UnobservedTaskException"); } catch { }
            };

            // Show splash screen immediately
            splash = new SplashScreen();
            splash.Show();

            // Allow splash to render
            DoEvents();

            try
            {
                // Create main window
                var mainWindow = new MainWindow();
                this.MainWindow = mainWindow;

                // When main window is ready, close splash and show main window
                mainWindow.Loaded += (s, args) =>
                {
                    // Close splash after main window is loaded
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            splash?.Close();
                            splash = null;
                        }
                        catch { }
                    }), DispatcherPriority.ApplicationIdle);
                };

                // Show main window
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                try { LogException(ex, "Application_Startup:MainWindowCreation"); } catch { }
                // Re-throw after logging to allow normal crash behavior
                throw;
            }
        }
        
        private void DoEvents()
        {
            Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }

        private static void LogException(Exception? ex, string source)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "sim-crash.log");
                using (var sw = new StreamWriter(logPath, true))
                {
                    sw.WriteLine("---- {0} {1:O} ----", source, DateTime.UtcNow);
                    if (ex != null)
                    {
                        sw.WriteLine(ex.ToString());
                    }
                    else
                    {
                        sw.WriteLine("(no Exception object)");
                    }
                    sw.WriteLine();
                }
            }
            catch { }
        }

        // Create an unfrozen BitmapSource copy suitable for rendering inside DrawingVisuals.
        // Avoid returning frozen BitmapImage instances directly because WPF's ImageVisualManager
        // may reject frozen images in some Render paths. This copies pixels into a Pbgra32
        // BitmapSource and returns it unfrozen.
        public static BitmapSource? EnsureUnfrozenForRender(ImageSource? src)
        {
            try
            {
                if (src == null) return null;
                if (!(src is BitmapSource bs)) return null;

                // If already unfrozen and not a WriteableBitmap, it's safe to use
                if (bs is System.Windows.Freezable f && !f.IsFrozen) return bs;
                if (bs is System.Windows.Media.Imaging.WriteableBitmap) return bs;

                // Convert to Pbgra32 to ensure consistent pixel format
                BitmapSource conv = bs;
                if (conv.Format != PixelFormats.Pbgra32)
                {
                    conv = new FormatConvertedBitmap(conv, PixelFormats.Pbgra32, null, 0);
                }

                int w = Math.Max(1, conv.PixelWidth);
                int h = Math.Max(1, conv.PixelHeight);
                int stride = (w * conv.Format.BitsPerPixel + 7) / 8;
                var buffer = new byte[stride * h];
                conv.CopyPixels(buffer, stride, 0);
                var copy = BitmapSource.Create(w, h, conv.DpiX, conv.DpiY, conv.Format, null, buffer, stride);
                // Intentionally do not freeze this copy — the render path will accept unfrozen images.
                return copy;
            }
            catch { return src as BitmapSource; }
        }

        // Maximum texture dimension we will allow when preparing images for render.
        // Keep this conservative to reduce VRAM pressure on older AMD GPUs.
        public const int MAX_RENDER_TEXTURE_SIZE = 4096;

        // Prepare a BitmapSource for safe rendering: ensure it's decoded on the UI thread,
        // optionally downscale very large images to `MAX_RENDER_TEXTURE_SIZE`, and
        // return an unfrozen BitmapSource suitable for passing into DrawingContext/RTB.
        public static BitmapSource? EnsureSafeBitmapForRender(ImageSource? src)
        {
            try
            {
                if (src == null) return null;
            if (!(src is BitmapSource bs)) return null;

                // If it's already a WriteableBitmap, return as-is (must remain writable)
                if (bs is WriteableBitmap) return bs;

                // If it's already reasonably small, just return an unfrozen pixel copy.
                if (bs.PixelWidth <= MAX_RENDER_TEXTURE_SIZE && bs.PixelHeight <= MAX_RENDER_TEXTURE_SIZE)
                {
                    return EnsureUnfrozenForRender(bs) ?? bs;
                }

                // Need to downscale large bitmap to avoid allocating huge GPU textures.
                double scale = Math.Min((double)MAX_RENDER_TEXTURE_SIZE / bs.PixelWidth, (double)MAX_RENDER_TEXTURE_SIZE / bs.PixelHeight);
                if (scale <= 0.0 || scale >= 1.0) return EnsureUnfrozenForRender(bs) ?? bs;

                // Create a scaled bitmap using a TransformedBitmap which is lightweight.
                var tb = new TransformedBitmap(bs, new ScaleTransform(scale, scale));

                // Force a pixel-copy into Pbgra32 and return unfrozen copy.
                return EnsureUnfrozenForRender(tb) ?? tb;
            }
            catch { return src as BitmapSource; }
        }
    }
}

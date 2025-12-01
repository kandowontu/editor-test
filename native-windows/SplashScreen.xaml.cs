using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FamidashEditor
{
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
            LoadSplashImage();
        }

        private void LoadSplashImage()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();
                
                // Find the resource name that contains our image
                string? resourceName = null;
                foreach (var name in resourceNames)
                {
                    if (name.EndsWith("that_one_render_1.png"))
                    {
                        resourceName = name;
                        break;
                    }
                }
                
                if (resourceName != null)
                {
                    using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream != null)
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.StreamSource = stream;
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            
                            SplashImage.Source = bitmap;
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Splash image resource not found. Available resources:");
                    foreach (var name in resourceNames)
                    {
                        System.Diagnostics.Debug.WriteLine($"  {name}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load splash image: {ex.Message}");
            }
        }
    }
}

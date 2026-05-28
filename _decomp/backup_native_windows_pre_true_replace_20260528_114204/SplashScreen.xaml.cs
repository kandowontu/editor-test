using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Media;

namespace FamidashEditor
{
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
            LoadSplashImage();
            
            // 1/256 chance to play easter egg audio
            var random = new Random();
            if (random.Next(256) == 0)
            {
                PlayEasterEggAudio();
            }
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
        
        private void PlayEasterEggAudio()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceNames = assembly.GetManifestResourceNames();
                
                // Find the audio resource
                string? resourceName = null;
                foreach (var name in resourceNames)
                {
                    if (name.EndsWith("fire.wav"))
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
                            // Write to temp file and play
                            string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".wav");
                            
                            using (var fileStream = File.Create(tempPath))
                            {
                                stream.CopyTo(fileStream);
                            }
                            
                            using (var player = new SoundPlayer(tempPath))
                            {
                                player.Play();
                            }
                            
                            // Clean up temp file after a delay
                            System.Threading.Tasks.Task.Delay(10000).ContinueWith(_ => 
                            {
                                try { File.Delete(tempPath); } catch { }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to play easter egg audio: {ex.Message}");
            }
        }
    }
}

using System;
using System.Windows;
using System.Windows.Threading;

namespace FamidashEditor
{
    public partial class App : Application
    {
        private SplashScreen? splash;
        
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Show splash screen immediately
            splash = new SplashScreen();
            splash.Show();
            
            // Allow splash to render
            DoEvents();
            
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
        
        private void DoEvents()
        {
            Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }
}

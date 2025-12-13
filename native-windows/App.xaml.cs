using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

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
    }
}

using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamidashEditor;

public class App : Application
{
	private SplashScreen? splash;

	public const int MAX_RENDER_TEXTURE_SIZE = 4096;

	private static bool softwareRenderForced;

	private bool _contentLoaded;

	private void Application_Startup(object sender, StartupEventArgs e)
	{
		try
		{
			DetectAndForceSoftwareRenderingIfNeeded();
		}
		catch
		{
		}
		AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs ev)
		{
			try
			{
				LogException(ev.ExceptionObject as Exception, "AppDomain.UnhandledException");
			}
			catch
			{
			}
		};
		base.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs ev)
		{
			try
			{
				LogException(ev.Exception, "DispatcherUnhandledException");
			}
			catch
			{
			}
			try
			{
				if (ev.Exception is COMException { HResult: -2003303418 })
				{
					try
					{
						EnableSoftwareRendering();
					}
					catch
					{
					}
					try
					{
						MessageBox.Show("Render thread failure detected. The application has switched to software rendering. Please save your work and restart the application to ensure stability.", "Render thread failure", MessageBoxButton.OK, MessageBoxImage.Exclamation);
					}
					catch
					{
					}
					ev.Handled = true;
				}
			}
			catch
			{
			}
		};
		TaskScheduler.UnobservedTaskException += delegate(object? s, UnobservedTaskExceptionEventArgs ev)
		{
			try
			{
				LogException(ev.Exception, "UnobservedTaskException");
			}
			catch
			{
			}
		};
		splash = new SplashScreen();
		splash.Show();
		DoEvents();
		try
		{
			MainWindow mainWindow = (MainWindow)(base.MainWindow = new MainWindow());
			mainWindow.Loaded += delegate
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					try
					{
						splash?.Close();
						splash = null;
					}
					catch
					{
					}
				}, DispatcherPriority.ApplicationIdle);
			};
			mainWindow.Show();
		}
		catch (Exception ex)
		{
			try
			{
				LogException(ex, "Application_Startup:MainWindowCreation");
			}
			catch
			{
			}
			throw;
		}
	}

	private void DetectAndForceSoftwareRenderingIfNeeded()
	{
		try
		{
			string environmentVariable = Environment.GetEnvironmentVariable("FAMIDASH_FORCE_SOFTWARE");
			if (!string.IsNullOrEmpty(environmentVariable) && environmentVariable == "1")
			{
				EnableSoftwareRendering();
				return;
			}
			try
			{
				using Process process = Process.Start(new ProcessStartInfo("wmic", "path win32_VideoController get Name,AdapterCompatibility /format:csv")
				{
					CreateNoWindow = true,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				});
				if (process != null)
				{
					string text = process.StandardOutput.ReadToEnd();
					try
					{
						process.WaitForExit(1500);
					}
					catch
					{
					}
					string text2 = text.ToLowerInvariant();
					if (text2.Contains("amd") || text2.Contains("radeon") || text2.Contains("advanced micro devices") || text2.Contains("ati"))
					{
						EnableSoftwareRendering();
					}
				}
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void DoEvents()
	{
		base.Dispatcher.Invoke(delegate
		{
		}, DispatcherPriority.Background);
	}

	private static void LogException(Exception? ex, string source)
	{
		try
		{
			using StreamWriter streamWriter = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "sim-crash.log"), append: true);
			streamWriter.WriteLine("---- {0} {1:O} ----", source, DateTime.UtcNow);
			if (ex != null)
			{
				streamWriter.WriteLine(ex.ToString());
			}
			else
			{
				streamWriter.WriteLine("(no Exception object)");
			}
			streamWriter.WriteLine();
		}
		catch
		{
		}
	}

	public static BitmapSource? EnsureUnfrozenForRender(ImageSource? src)
	{
		try
		{
			if (src == null)
			{
				return null;
			}
			if (!(src is BitmapSource bitmapSource))
			{
				return null;
			}
			Freezable freezable = bitmapSource;
			if (freezable != null && !freezable.IsFrozen)
			{
				return bitmapSource;
			}
			if (bitmapSource is WriteableBitmap)
			{
				return bitmapSource;
			}
			BitmapSource bitmapSource2 = bitmapSource;
			if (bitmapSource2.Format != PixelFormats.Pbgra32)
			{
				bitmapSource2 = new FormatConvertedBitmap(bitmapSource2, PixelFormats.Pbgra32, null, 0.0);
			}
			int num = Math.Max(1, bitmapSource2.PixelWidth);
			int num2 = Math.Max(1, bitmapSource2.PixelHeight);
			int num3 = (num * bitmapSource2.Format.BitsPerPixel + 7) / 8;
			byte[] pixels = new byte[num3 * num2];
			bitmapSource2.CopyPixels(pixels, num3, 0);
			return BitmapSource.Create(num, num2, bitmapSource2.DpiX, bitmapSource2.DpiY, bitmapSource2.Format, null, pixels, num3);
		}
		catch
		{
			return src as BitmapSource;
		}
	}

	public static BitmapSource? EnsureSafeBitmapForRender(ImageSource? src)
	{
		try
		{
			if (src == null)
			{
				return null;
			}
			if (!(src is BitmapSource bitmapSource))
			{
				return null;
			}
			if (bitmapSource is WriteableBitmap)
			{
				return bitmapSource;
			}
			if (bitmapSource.PixelWidth <= 4096 && bitmapSource.PixelHeight <= 4096)
			{
				return EnsureUnfrozenForRender(bitmapSource) ?? bitmapSource;
			}
			double num = Math.Min(4096.0 / (double)bitmapSource.PixelWidth, 4096.0 / (double)bitmapSource.PixelHeight);
			if (num <= 0.0 || num >= 1.0)
			{
				return EnsureUnfrozenForRender(bitmapSource) ?? bitmapSource;
			}
			TransformedBitmap transformedBitmap = new TransformedBitmap(bitmapSource, new ScaleTransform(num, num));
			return EnsureUnfrozenForRender(transformedBitmap) ?? transformedBitmap;
		}
		catch
		{
			return src as BitmapSource;
		}
	}

	public static void EnableSoftwareRendering()
	{
		try
		{
			if (!softwareRenderForced)
			{
				RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
				softwareRenderForced = true;
			}
		}
		catch
		{
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			base.Startup += Application_Startup;
			Uri resourceLocator = new Uri("/FamidashEditor;component/app.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public static void Main()
	{
		App app = new App();
		app.InitializeComponent();
		app.Run();
	}
}

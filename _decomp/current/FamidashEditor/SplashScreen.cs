#define DEBUG
using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace FamidashEditor;

public class SplashScreen : Window, IComponentConnector
{
	internal Image SplashImage;

	private bool _contentLoaded;

	public SplashScreen()
	{
		InitializeComponent();
		LoadSplashImage();
		Random random = new Random();
		if (random.Next(256) == 0)
		{
			PlayEasterEggAudio();
		}
	}

	private void LoadSplashImage()
	{
		try
		{
			Assembly executingAssembly = Assembly.GetExecutingAssembly();
			string[] manifestResourceNames = executingAssembly.GetManifestResourceNames();
			string text = null;
			string[] array = manifestResourceNames;
			foreach (string text2 in array)
			{
				if (text2.EndsWith("that_one_render_1.png"))
				{
					text = text2;
					break;
				}
			}
			if (text != null)
			{
				using (Stream stream = executingAssembly.GetManifestResourceStream(text))
				{
					if (stream != null)
					{
						BitmapImage bitmapImage = new BitmapImage();
						bitmapImage.BeginInit();
						bitmapImage.StreamSource = stream;
						bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
						bitmapImage.EndInit();
						bitmapImage.Freeze();
						SplashImage.Source = bitmapImage;
					}
					return;
				}
			}
			Debug.WriteLine("Splash image resource not found. Available resources:");
			string[] array2 = manifestResourceNames;
			foreach (string text3 in array2)
			{
				Debug.WriteLine("  " + text3);
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to load splash image: " + ex.Message);
		}
	}

	private void PlayEasterEggAudio()
	{
		try
		{
			Assembly executingAssembly = Assembly.GetExecutingAssembly();
			string[] manifestResourceNames = executingAssembly.GetManifestResourceNames();
			string text = null;
			string[] array = manifestResourceNames;
			foreach (string text2 in array)
			{
				if (text2.EndsWith("fire.wav"))
				{
					text = text2;
					break;
				}
			}
			if (text == null)
			{
				return;
			}
			using Stream stream = executingAssembly.GetManifestResourceStream(text);
			if (stream == null)
			{
				return;
			}
			string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".wav");
			using (FileStream destination = File.Create(tempPath))
			{
				stream.CopyTo(destination);
			}
			using (SoundPlayer soundPlayer = new SoundPlayer(tempPath))
			{
				soundPlayer.Play();
			}
			Task.Delay(10000).ContinueWith(delegate
			{
				try
				{
					File.Delete(tempPath);
				}
				catch
				{
				}
			});
		}
		catch (Exception ex)
		{
			Debug.WriteLine("Failed to play easter egg audio: " + ex.Message);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/splashscreen.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		if (connectionId == 1)
		{
			SplashImage = (Image)target;
		}
		else
		{
			_contentLoaded = true;
		}
	}
}

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamidashEditor;

public class ColorPickerWindow : Window, IComponentConnector
{
	private readonly Color[] PaletteColors;

	private static readonly Color[] DefaultPaletteColors = new Color[56]
	{
		Color.FromRgb(105, 107, 99),
		Color.FromRgb(0, 23, 116),
		Color.FromRgb(30, 0, 135),
		Color.FromRgb(52, 0, 115),
		Color.FromRgb(86, 0, 87),
		Color.FromRgb(94, 0, 19),
		Color.FromRgb(83, 26, 0),
		Color.FromRgb(59, 36, 0),
		Color.FromRgb(36, 48, 0),
		Color.FromRgb(6, 58, 0),
		Color.FromRgb(0, 63, 0),
		Color.FromRgb(0, 59, 30),
		Color.FromRgb(0, 51, 78),
		Color.FromRgb(0, 0, 0),
		Color.FromRgb(185, 187, 179),
		Color.FromRgb(20, 83, 185),
		Color.FromRgb(77, 44, 218),
		Color.FromRgb(103, 30, 222),
		Color.FromRgb(152, 24, 156),
		Color.FromRgb(157, 35, 68),
		Color.FromRgb(160, 62, 0),
		Color.FromRgb(141, 85, 0),
		Color.FromRgb(101, 109, 0),
		Color.FromRgb(44, 121, 0),
		Color.FromRgb(0, 129, 0),
		Color.FromRgb(0, 125, 66),
		Color.FromRgb(0, 120, 138),
		Color.FromRgb(0, 0, 0),
		Color.FromRgb(byte.MaxValue, byte.MaxValue, byte.MaxValue),
		Color.FromRgb(105, 168, byte.MaxValue),
		Color.FromRgb(150, 145, byte.MaxValue),
		Color.FromRgb(178, 138, 250),
		Color.FromRgb(234, 125, 250),
		Color.FromRgb(243, 123, 199),
		Color.FromRgb(242, 142, 89),
		Color.FromRgb(230, 173, 39),
		Color.FromRgb(215, 200, 5),
		Color.FromRgb(144, 223, 7),
		Color.FromRgb(100, 229, 60),
		Color.FromRgb(69, 226, 125),
		Color.FromRgb(72, 213, 217),
		Color.FromRgb(78, 80, 72),
		Color.FromRgb(byte.MaxValue, byte.MaxValue, byte.MaxValue),
		Color.FromRgb(210, 234, byte.MaxValue),
		Color.FromRgb(226, 226, byte.MaxValue),
		Color.FromRgb(233, 216, byte.MaxValue),
		Color.FromRgb(245, 210, byte.MaxValue),
		Color.FromRgb(248, 217, 234),
		Color.FromRgb(250, 222, 185),
		Color.FromRgb(249, 232, 155),
		Color.FromRgb(243, 242, 140),
		Color.FromRgb(211, 250, 145),
		Color.FromRgb(184, 252, 168),
		Color.FromRgb(174, 250, 202),
		Color.FromRgb(202, 243, 243),
		Color.FromRgb(190, 192, 184)
	};

	private int selectedIndex = -1;

	private readonly bool allowAlpha;

	private DispatcherTimer? debounceTimer = null;

	private Color? pendingColor = null;

	internal Border PreviewBorder;

	internal StackPanel AlphaPanel;

	internal Slider ASlider;

	internal TextBlock AVal;

	internal ItemsControl PalettePanel;

	internal CheckBox SetDefaultCheckbox;

	internal Button OkButton;

	internal Button CancelButton;

	private bool _contentLoaded;

	public Color SelectedColor { get; private set; } = Color.FromArgb(byte.MaxValue, 40, 40, 40);

	public bool SetAsDefault => SetDefaultCheckbox != null && SetDefaultCheckbox.IsChecked == true;

	public event Action<Color>? ColorChanged;

	public ColorPickerWindow(Color initial, bool allowAlpha = true, int forcedIndex = -1)
	{
		InitializeComponent();
		SelectedColor = initial;
		this.allowAlpha = allowAlpha;
		Color[] array = null;
		try
		{
			array = TryLoadPaletteFromPalFile("palette.pal") ?? TryLoadPaletteFromFile("palette.png") ?? TryLoadPaletteFromFile("palette.bmp");
		}
		catch
		{
		}
		PaletteColors = array ?? DefaultPaletteColors;
		try
		{
			for (int i = 0; i < 3; i++)
			{
				int num = 128 + i * 16;
				for (int j = 0; j < 12; j++)
				{
					int num2 = num + j;
					int num3 = -1;
					if (num2 >= 128 && num2 <= 140)
					{
						num3 = num2 - 128;
					}
					else if (num2 >= 144 && num2 <= 156)
					{
						num3 = num2 - 144 + 14;
					}
					else if (num2 >= 160 && num2 <= 172)
					{
						num3 = num2 - 160 + 28;
					}
					if (num3 >= 0 && num3 < PaletteHelper.Palette.Length)
					{
						int num4 = i * 14 + j;
						if (num4 >= 0 && num4 < PaletteColors.Length)
						{
							PaletteColors[num4] = PaletteHelper.Palette[num3];
						}
					}
				}
			}
		}
		catch
		{
		}
		for (int k = 0; k < PaletteColors.Length; k++)
		{
			Color color = PaletteColors[k];
			Border border = new Border
			{
				Width = 28.0,
				Height = 28.0,
				Margin = new Thickness(2.0),
				Background = new SolidColorBrush(color),
				Tag = k,
				BorderThickness = new Thickness(1.0),
				BorderBrush = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
				CornerRadius = new CornerRadius(2.0)
			};
			border.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				SelectIndex((int)((Border)s).Tag, clearOthers: true);
			};
			border.MouseEnter += delegate(object s, MouseEventArgs e)
			{
				PreviewIndex((int)((Border)s).Tag);
			};
			border.MouseLeave += delegate
			{
				RevertPreview();
			};
			PalettePanel.Items.Add(border);
		}
		if (allowAlpha)
		{
			ASlider.Value = (int)initial.A;
			AVal.Text = ((int)ASlider.Value).ToString();
			ASlider.ValueChanged += delegate
			{
				AVal.Text = ((int)ASlider.Value).ToString();
				SchedulePreviewAndNotify();
			};
		}
		else
		{
			if (AlphaPanel != null)
			{
				AlphaPanel.Visibility = Visibility.Collapsed;
			}
			ASlider.Value = 255.0;
			AVal.Text = "255";
		}
		if (forcedIndex >= 0 && forcedIndex < PaletteColors.Length)
		{
			SelectIndex(forcedIndex, clearOthers: true);
		}
		else
		{
			int num5 = FindNearestPaletteIndex(initial);
			if (num5 >= 0)
			{
				SelectIndex(num5, clearOthers: true);
			}
			else
			{
				SelectIndex(0, clearOthers: true);
			}
		}
		OkButton.Click += delegate
		{
			base.DialogResult = true;
			SelectedColor = GetBlendedColor();
		};
		CancelButton.Click += delegate
		{
			base.DialogResult = false;
		};
		UpdatePreviewAndNotify();
	}

	private void PreviewIndex(int idx)
	{
		if (idx >= 0 && idx < PaletteColors.Length)
		{
			byte a = (allowAlpha ? ((byte)ASlider.Value) : byte.MaxValue);
			Color color = Color.FromArgb(a, PaletteColors[idx].R, PaletteColors[idx].G, PaletteColors[idx].B);
			PreviewBorder.Background = new SolidColorBrush(color);
			ScheduleNotify(color);
		}
	}

	private void RevertPreview()
	{
		if (selectedIndex >= 0)
		{
			PreviewIndex(selectedIndex);
		}
	}

	private void SelectIndex(int idx, bool clearOthers)
	{
		if (idx < 0 || idx >= PaletteColors.Length)
		{
			return;
		}
		selectedIndex = idx;
		for (int i = 0; i < PalettePanel.Items.Count; i++)
		{
			if (PalettePanel.Items[i] is Border border)
			{
				border.BorderBrush = ((i == selectedIndex) ? Brushes.White : Brushes.Transparent);
			}
		}
		UpdatePreviewAndNotify();
	}

	private Color GetBlendedColor()
	{
		byte a = (allowAlpha ? ((byte)ASlider.Value) : byte.MaxValue);
		if (selectedIndex < 0 || selectedIndex >= PaletteColors.Length)
		{
			return Color.FromArgb(a, 0, 0, 0);
		}
		Color color = PaletteColors[selectedIndex];
		return Color.FromArgb(a, color.R, color.G, color.B);
	}

	private void UpdatePreviewAndNotify()
	{
		Color blendedColor = GetBlendedColor();
		PreviewBorder.Background = new SolidColorBrush(blendedColor);
		ScheduleNotify(blendedColor);
	}

	private void ScheduleNotify(Color c)
	{
		pendingColor = c;
		if (debounceTimer == null)
		{
			debounceTimer = new DispatcherTimer(DispatcherPriority.Normal)
			{
				Interval = TimeSpan.FromMilliseconds(60.0)
			};
			debounceTimer.Tick += delegate
			{
				debounceTimer?.Stop();
				debounceTimer = null;
				if (pendingColor.HasValue)
				{
					this.ColorChanged?.Invoke(pendingColor.Value);
				}
				pendingColor = null;
			};
		}
		else
		{
			debounceTimer.Stop();
		}
		debounceTimer.Start();
	}

	private void SchedulePreviewAndNotify()
	{
		Color blendedColor = GetBlendedColor();
		PreviewBorder.Background = new SolidColorBrush(blendedColor);
		ScheduleNotify(blendedColor);
	}

	private int FindNearestPaletteIndex(Color initial)
	{
		int result = -1;
		double num = double.MaxValue;
		for (int i = 0; i < PaletteColors.Length; i++)
		{
			Color color = PaletteColors[i];
			double num2 = color.R - initial.R;
			double num3 = color.G - initial.G;
			double num4 = color.B - initial.B;
			double num5 = num2 * num2 + num3 * num3 + num4 * num4;
			if (num5 < num)
			{
				num = num5;
				result = i;
			}
		}
		return result;
	}

	private Color[]? TryLoadPaletteFromFile(string fileName)
	{
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		string path = Path.Combine(baseDirectory, fileName);
		if (!File.Exists(path))
		{
			return null;
		}
		BitmapSource bitmapSource;
		using (FileStream streamSource = File.OpenRead(path))
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.StreamSource = streamSource;
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			bitmapSource = new FormatConvertedBitmap(bitmapImage, PixelFormats.Bgra32, null, 0.0);
		}
		if (bitmapSource == null)
		{
			return null;
		}
		int w = bitmapSource.PixelWidth;
		int pixelHeight = bitmapSource.PixelHeight;
		if (w < 14 || pixelHeight < 4)
		{
			return null;
		}
		int num = w * 4;
		byte[] pixels = new byte[pixelHeight * num];
		bitmapSource.CopyPixels(pixels, num, 0);
		byte bgB = pixels[0];
		byte bgG = pixels[1];
		byte bgR = pixels[2];
		int left = 0;
		int right = w - 1;
		int top = 0;
		int bottom = pixelHeight - 1;
		for (; left < right && !ColumnHasNonBg(left); left++)
		{
		}
		for (; right > left && !ColumnHasNonBg(right); right--)
		{
		}
		for (; top < bottom && !RowHasNonBg(top); top++)
		{
		}
		for (; bottom > top && !RowHasNonBg(bottom); bottom--)
		{
		}
		if (right <= left || bottom <= top)
		{
			return null;
		}
		int num2 = right - left + 1;
		int num3 = bottom - top + 1;
		double num4 = (double)num2 / 14.0;
		double num5 = (double)num3 / 4.0;
		Color[] array = new Color[56];
		int num6 = 1;
		for (int i = 0; i < 4; i++)
		{
			for (int j = 0; j < 14; j++)
			{
				double a = (double)left + ((double)j + 0.5) * num4;
				double a2 = (double)top + ((double)i + 0.5) * num5;
				int num7 = Math.Min(w - 1, Math.Max(0, (int)Math.Round(a)));
				int num8 = Math.Min(pixelHeight - 1, Math.Max(0, (int)Math.Round(a2)));
				long num9 = 0L;
				long num10 = 0L;
				long num11 = 0L;
				int num12 = 0;
				for (int k = num8 - num6; k <= num8 + num6; k++)
				{
					if (k < 0 || k >= pixelHeight)
					{
						continue;
					}
					for (int l = num7 - num6; l <= num7 + num6; l++)
					{
						if (l >= 0 && l < w)
						{
							int num13 = (k * w + l) * 4;
							byte b = pixels[num13];
							byte b2 = pixels[num13 + 1];
							byte b3 = pixels[num13 + 2];
							num9 += b3;
							num10 += b2;
							num11 += b;
							num12++;
						}
					}
				}
				if (num12 == 0)
				{
					array[i * 14 + j] = Color.FromRgb(0, 0, 0);
				}
				else
				{
					array[i * 14 + j] = Color.FromRgb((byte)(num9 / num12), (byte)(num10 / num12), (byte)(num11 / num12));
				}
			}
		}
		return array;
		bool ColumnHasNonBg(int col)
		{
			int num14 = 0;
			int num15 = bottom - top + 1;
			for (int m = top; m <= bottom; m++)
			{
				if (IsNonBackground(col, m))
				{
					num14++;
				}
			}
			return num14 * 100 > num15 * 3;
		}
		bool IsNonBackground(int px, int py)
		{
			int num14 = (py * w + px) * 4;
			int num15 = pixels[num14] - bgB;
			int num16 = pixels[num14 + 1] - bgG;
			int num17 = pixels[num14 + 2] - bgR;
			int num18 = num15 * num15 + num16 * num16 + num17 * num17;
			return num18 > 144;
		}
		bool RowHasNonBg(int row)
		{
			int num14 = 0;
			int num15 = right - left + 1;
			for (int m = left; m <= right; m++)
			{
				if (IsNonBackground(m, row))
				{
					num14++;
				}
			}
			return num14 * 100 > num15 * 3;
		}
	}

	private Color[]? TryLoadPaletteFromPalFile(string fileName)
	{
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		List<string> list = new List<string>
		{
			Path.Combine(baseDirectory, fileName),
			Path.Combine(baseDirectory, "assets", fileName),
			Path.Combine(baseDirectory, "renderer", "assets", fileName),
			Path.Combine(baseDirectory, "..", "assets", fileName)
		};
		string text = null;
		foreach (string item in list)
		{
			try
			{
				if (File.Exists(item))
				{
					text = item;
					break;
				}
			}
			catch
			{
			}
		}
		if (text == null)
		{
			return null;
		}
		byte[] array;
		try
		{
			array = File.ReadAllBytes(text);
		}
		catch
		{
			return null;
		}
		if (array == null || array.Length < 3)
		{
			return null;
		}
		int val = array.Length / 3;
		int num = Math.Min(56, val);
		if (num <= 0)
		{
			return null;
		}
		Color[] array2 = new Color[num];
		for (int i = 0; i < num; i++)
		{
			int num2 = i * 3;
			byte r = array[num2];
			byte g = array[num2 + 1];
			byte b = array[num2 + 2];
			array2[i] = Color.FromRgb(r, g, b);
		}
		return array2;
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/colorpickerwindow.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			PreviewBorder = (Border)target;
			break;
		case 2:
			AlphaPanel = (StackPanel)target;
			break;
		case 3:
			ASlider = (Slider)target;
			break;
		case 4:
			AVal = (TextBlock)target;
			break;
		case 5:
			PalettePanel = (ItemsControl)target;
			break;
		case 6:
			SetDefaultCheckbox = (CheckBox)target;
			break;
		case 7:
			OkButton = (Button)target;
			break;
		case 8:
			CancelButton = (Button)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}

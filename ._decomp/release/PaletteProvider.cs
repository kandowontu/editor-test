using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamidashEditor;

public static class PaletteProvider
{
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

	public static Color[] GetPalette()
	{
		try
		{
			Color[] array = TryLoadPaletteFromPalFile("palette.pal") ?? TryLoadPaletteFromFile("palette.png") ?? TryLoadPaletteFromFile("palette.bmp");
			if (array != null)
			{
				return array;
			}
		}
		catch
		{
		}
		return DefaultPaletteColors;
	}

	private static Color[]? TryLoadPaletteFromFile(string fileName)
	{
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
		if (!File.Exists(path))
		{
			return null;
		}
		int w;
		byte[] pixels;
		byte bgB;
		byte bgG;
		byte bgR;
		try
		{
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
			w = bitmapSource.PixelWidth;
			int pixelHeight = bitmapSource.PixelHeight;
			if (w < 14 || pixelHeight < 4)
			{
				return null;
			}
			int num = w * 4;
			pixels = new byte[pixelHeight * num];
			bitmapSource.CopyPixels(pixels, num, 0);
			bgB = pixels[0];
			bgG = pixels[1];
			bgR = pixels[2];
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
				int num20 = 0;
				int num21 = bottom - top + 1;
				for (int n = top; n <= bottom; n++)
				{
					if (IsNonBackground(col, n))
					{
						num20++;
					}
				}
				return num20 * 100 > num21 * 3;
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
		catch
		{
			return null;
		}
		bool IsNonBackground(int px, int py)
		{
			int num16 = (py * w + px) * 4;
			int num17 = pixels[num16] - bgB;
			int num18 = pixels[num16 + 1] - bgG;
			int num19 = pixels[num16 + 2] - bgR;
			return num17 * num17 + num18 * num18 + num19 * num19 > 144;
		}
	}

	private static Color[]? TryLoadPaletteFromPalFile(string fileName)
	{
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		List<string> obj = new List<string>
		{
			Path.Combine(baseDirectory, fileName),
			Path.Combine(baseDirectory, "assets", fileName),
			Path.Combine(baseDirectory, "renderer", "assets", fileName),
			Path.Combine(baseDirectory, "..", "assets", fileName)
		};
		string text = null;
		foreach (string item in obj)
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
}

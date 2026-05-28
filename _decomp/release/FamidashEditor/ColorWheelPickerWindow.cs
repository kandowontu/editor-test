using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FamidashEditor;

public class ColorWheelPickerWindow : Window, IComponentConnector
{
	private double hue;

	private double saturation = 1.0;

	private double brightness = 1.0;

	private bool isDragging;

	internal Canvas ColorWheelCanvas;

	internal Image ColorWheelImage;

	internal Ellipse SelectionIndicator;

	internal Slider BrightnessSlider;

	internal Border PreviewBorder;

	internal TextBlock RgbValueText;

	internal TextBlock HexValueText;

	internal CheckBox SetDefaultCheckbox;

	internal Button OkButton;

	internal Button CancelButton;

	private bool _contentLoaded;

	public Color SelectedColor { get; private set; }

	public bool SetAsDefault
	{
		get
		{
			if (SetDefaultCheckbox != null)
			{
				return SetDefaultCheckbox.IsChecked == true;
			}
			return false;
		}
	}

	public ColorWheelPickerWindow(Color initialColor)
	{
		InitializeComponent();
		RgbToHsv(initialColor.R, initialColor.G, initialColor.B, out hue, out saturation, out brightness);
		BrightnessSlider.Value = brightness * 100.0;
		GenerateColorWheel();
		UpdateSelectionIndicator();
		UpdatePreview();
		BrightnessSlider.ValueChanged += delegate(object s, RoutedPropertyChangedEventArgs<double> e)
		{
			brightness = e.NewValue / 100.0;
			UpdatePreview();
		};
		OkButton.Click += delegate
		{
			base.DialogResult = true;
		};
		CancelButton.Click += delegate
		{
			base.DialogResult = false;
		};
	}

	private void GenerateColorWheel()
	{
		WriteableBitmap writeableBitmap = new WriteableBitmap(280, 280, 96.0, 96.0, PixelFormats.Bgra32, null);
		byte[] array = new byte[313600];
		for (int i = 0; i < 280; i++)
		{
			for (int j = 0; j < 280; j++)
			{
				double num = j - 140;
				double num2 = i - 140;
				double num3 = Math.Sqrt(num * num + num2 * num2);
				int num4 = (i * 280 + j) * 4;
				if (num3 <= 140.0)
				{
					double h = (Math.Atan2(num2, num) * 180.0 / Math.PI + 360.0) % 360.0;
					double s = Math.Min(1.0, num3 / 140.0);
					HsvToRgb(h, s, 1.0, out var r, out var g, out var b);
					array[num4] = b;
					array[num4 + 1] = g;
					array[num4 + 2] = r;
					array[num4 + 3] = byte.MaxValue;
				}
				else
				{
					array[num4] = 0;
					array[num4 + 1] = 0;
					array[num4 + 2] = 0;
					array[num4 + 3] = 0;
				}
			}
		}
		writeableBitmap.WritePixels(new Int32Rect(0, 0, 280, 280), array, 1120, 0);
		ColorWheelImage.Source = writeableBitmap;
	}

	private void ColorWheel_MouseDown(object sender, MouseButtonEventArgs e)
	{
		isDragging = true;
		ColorWheelCanvas.CaptureMouse();
		UpdateColorFromPosition(e.GetPosition(ColorWheelCanvas));
	}

	private void ColorWheel_MouseMove(object sender, MouseEventArgs e)
	{
		if (isDragging)
		{
			UpdateColorFromPosition(e.GetPosition(ColorWheelCanvas));
		}
	}

	private void ColorWheel_MouseUp(object sender, MouseButtonEventArgs e)
	{
		isDragging = false;
		ColorWheelCanvas.ReleaseMouseCapture();
	}

	private void UpdateColorFromPosition(Point pos)
	{
		double num = pos.X - 140.0;
		double num2 = pos.Y - 140.0;
		double num3 = Math.Sqrt(num * num + num2 * num2);
		if (num3 > 140.0)
		{
			num = num / num3 * 140.0;
			num2 = num2 / num3 * 140.0;
			num3 = 140.0;
		}
		double num4 = Math.Atan2(num2, num);
		hue = (num4 * 180.0 / Math.PI + 360.0) % 360.0;
		saturation = Math.Min(1.0, num3 / 140.0);
		UpdateSelectionIndicator();
		UpdatePreview();
	}

	private void UpdateSelectionIndicator()
	{
		double num = hue * Math.PI / 180.0;
		double num2 = saturation * 140.0;
		double length = 140.0 + num2 * Math.Cos(num) - 6.0;
		double length2 = 140.0 + num2 * Math.Sin(num) - 6.0;
		Canvas.SetLeft(SelectionIndicator, length);
		Canvas.SetTop(SelectionIndicator, length2);
	}

	private void UpdatePreview()
	{
		HsvToRgb(hue, saturation, brightness, out var r, out var g, out var b);
		SelectedColor = Color.FromArgb(byte.MaxValue, r, g, b);
		PreviewBorder.Background = new SolidColorBrush(SelectedColor);
		RgbValueText.Text = $"RGB: {r}, {g}, {b}";
		HexValueText.Text = $"Hex: #{r:X2}{g:X2}{b:X2}";
	}

	private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
	{
		double num = (double)(int)r / 255.0;
		double num2 = (double)(int)g / 255.0;
		double num3 = (double)(int)b / 255.0;
		double num4 = Math.Max(num, Math.Max(num2, num3));
		double num5 = Math.Min(num, Math.Min(num2, num3));
		double num6 = num4 - num5;
		if (num6 == 0.0)
		{
			h = 0.0;
		}
		else if (num4 == num)
		{
			h = 60.0 * ((num2 - num3) / num6 % 6.0);
		}
		else if (num4 == num2)
		{
			h = 60.0 * ((num3 - num) / num6 + 2.0);
		}
		else
		{
			h = 60.0 * ((num - num2) / num6 + 4.0);
		}
		if (h < 0.0)
		{
			h += 360.0;
		}
		s = ((num4 == 0.0) ? 0.0 : (num6 / num4));
		v = num4;
	}

	private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
	{
		double num = v * s;
		double num2 = num * (1.0 - Math.Abs(h / 60.0 % 2.0 - 1.0));
		double num3 = v - num;
		double num4;
		double num5;
		double num6;
		if (h < 60.0)
		{
			num4 = num;
			num5 = num2;
			num6 = 0.0;
		}
		else if (h < 120.0)
		{
			num4 = num2;
			num5 = num;
			num6 = 0.0;
		}
		else if (h < 180.0)
		{
			num4 = 0.0;
			num5 = num;
			num6 = num2;
		}
		else if (h < 240.0)
		{
			num4 = 0.0;
			num5 = num2;
			num6 = num;
		}
		else if (h < 300.0)
		{
			num4 = num2;
			num5 = 0.0;
			num6 = num;
		}
		else
		{
			num4 = num;
			num5 = 0.0;
			num6 = num2;
		}
		r = (byte)Math.Round((num4 + num3) * 255.0);
		g = (byte)Math.Round((num5 + num3) * 255.0);
		b = (byte)Math.Round((num6 + num3) * 255.0);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/colorwheelpickerwindow.xaml", UriKind.Relative);
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
			ColorWheelCanvas = (Canvas)target;
			ColorWheelCanvas.MouseLeftButtonDown += ColorWheel_MouseDown;
			ColorWheelCanvas.MouseMove += ColorWheel_MouseMove;
			ColorWheelCanvas.MouseLeftButtonUp += ColorWheel_MouseUp;
			break;
		case 2:
			ColorWheelImage = (Image)target;
			break;
		case 3:
			SelectionIndicator = (Ellipse)target;
			break;
		case 4:
			BrightnessSlider = (Slider)target;
			break;
		case 5:
			PreviewBorder = (Border)target;
			break;
		case 6:
			RgbValueText = (TextBlock)target;
			break;
		case 7:
			HexValueText = (TextBlock)target;
			break;
		case 8:
			SetDefaultCheckbox = (CheckBox)target;
			break;
		case 9:
			OkButton = (Button)target;
			break;
		case 10:
			CancelButton = (Button)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}

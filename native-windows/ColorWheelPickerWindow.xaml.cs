using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamidashEditor
{
    public partial class ColorWheelPickerWindow : Window
    {
        public Color SelectedColor { get; private set; }
        
        private double hue = 0; // 0-360
        private double saturation = 1; // 0-1
        private double brightness = 1; // 0-1
        private bool isDragging = false;
        
        public ColorWheelPickerWindow(Color initialColor)
        {
            InitializeComponent();
            
            // Convert initial color to HSV
            RgbToHsv(initialColor.R, initialColor.G, initialColor.B, out hue, out saturation, out brightness);
            
            // Set initial values
            BrightnessSlider.Value = brightness * 100;
            
            // Generate color wheel
            GenerateColorWheel();
            
            // Update selection indicator position
            UpdateSelectionIndicator();
            UpdatePreview();
            
            // Wire up events
            BrightnessSlider.ValueChanged += (s, e) => { brightness = e.NewValue / 100.0; UpdatePreview(); };
            OkButton.Click += (s, e) => { DialogResult = true; };
            CancelButton.Click += (s, e) => { DialogResult = false; };
        }
        
        private void GenerateColorWheel()
        {
            const int size = 280;
            const int centerX = size / 2;
            const int centerY = size / 2;
            const double radius = size / 2.0;
            
            var wb = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new byte[size * size * 4];
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double dx = x - centerX;
                    double dy = y - centerY;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    
                    int index = (y * size + x) * 4;
                    
                    if (distance <= radius)
                    {
                        // Calculate hue based on angle
                        double angle = Math.Atan2(dy, dx);
                        double hueDeg = (angle * 180.0 / Math.PI + 360) % 360;
                        
                        // Calculate saturation based on distance from center
                        double sat = Math.Min(1.0, distance / radius);
                        
                        // Convert HSV to RGB (using full brightness for the wheel)
                        HsvToRgb(hueDeg, sat, 1.0, out byte r, out byte g, out byte b);
                        
                        pixels[index + 0] = b; // B
                        pixels[index + 1] = g; // G
                        pixels[index + 2] = r; // R
                        pixels[index + 3] = 255; // A
                    }
                    else
                    {
                        // Transparent outside the circle
                        pixels[index + 0] = 0;
                        pixels[index + 1] = 0;
                        pixels[index + 2] = 0;
                        pixels[index + 3] = 0;
                    }
                }
            }
            
            wb.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
            ColorWheelImage.Source = wb;
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
            const double centerX = 140;
            const double centerY = 140;
            const double radius = 140;
            
            double dx = pos.X - centerX;
            double dy = pos.Y - centerY;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            
            // Clamp to circle
            if (distance > radius)
            {
                dx = dx / distance * radius;
                dy = dy / distance * radius;
                distance = radius;
            }
            
            // Calculate hue from angle
            double angle = Math.Atan2(dy, dx);
            hue = (angle * 180.0 / Math.PI + 360) % 360;
            
            // Calculate saturation from distance
            saturation = Math.Min(1.0, distance / radius);
            
            UpdateSelectionIndicator();
            UpdatePreview();
        }
        
        private void UpdateSelectionIndicator()
        {
            const double centerX = 140;
            const double centerY = 140;
            const double radius = 140;
            
            // Convert hue and saturation to position
            double angleRad = hue * Math.PI / 180.0;
            double distance = saturation * radius;
            
            double x = centerX + distance * Math.Cos(angleRad) - 6; // -6 to center the 12px indicator
            double y = centerY + distance * Math.Sin(angleRad) - 6;
            
            Canvas.SetLeft(SelectionIndicator, x);
            Canvas.SetTop(SelectionIndicator, y);
        }
        
        private void UpdatePreview()
        {
            HsvToRgb(hue, saturation, brightness, out byte r, out byte g, out byte b);
            
            SelectedColor = Color.FromArgb(255, r, g, b);
            PreviewBorder.Background = new SolidColorBrush(SelectedColor);
            
            RgbValueText.Text = $"RGB: {r}, {g}, {b}";
            HexValueText.Text = $"Hex: #{r:X2}{g:X2}{b:X2}";
        }
        
        private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
        {
            double rd = r / 255.0;
            double gd = g / 255.0;
            double bd = b / 255.0;
            
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;
            
            // Hue calculation
            if (delta == 0)
                h = 0;
            else if (max == rd)
                h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd)
                h = 60 * (((bd - rd) / delta) + 2);
            else
                h = 60 * (((rd - gd) / delta) + 4);
            
            if (h < 0) h += 360;
            
            // Saturation calculation
            s = max == 0 ? 0 : delta / max;
            
            // Value calculation
            v = max;
        }
        
        private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            
            double rp, gp, bp;
            
            if (h < 60)
            {
                rp = c; gp = x; bp = 0;
            }
            else if (h < 120)
            {
                rp = x; gp = c; bp = 0;
            }
            else if (h < 180)
            {
                rp = 0; gp = c; bp = x;
            }
            else if (h < 240)
            {
                rp = 0; gp = x; bp = c;
            }
            else if (h < 300)
            {
                rp = x; gp = 0; bp = c;
            }
            else
            {
                rp = c; gp = 0; bp = x;
            }
            
            r = (byte)Math.Round((rp + m) * 255);
            g = (byte)Math.Round((gp + m) * 255);
            b = (byte)Math.Round((bp + m) * 255);
        }
    }
}

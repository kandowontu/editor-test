using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Shapes;
using Shapes = System.Windows.Shapes;

namespace FamidashEditor
{
    public partial class VisualizerWindow : Window
    {
        private DispatcherTimer timer;
        private Random rnd = new Random();
        private double t = 0.0;
        private MainWindow? ownerMain = null;
        private double smoothedLevel = 0.0;

        public VisualizerWindow(MainWindow? owner = null)
        {
            InitializeComponent();
            ownerMain = owner;
            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(33); // ~30fps
            timer.Tick += Timer_Tick;
            timer.Start();
            this.Closed += (s,e) => { try { timer.Stop(); } catch { } };
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (VizCanvas == null) return;
                var w = VizCanvas.ActualWidth; var h = VizCanvas.ActualHeight;
                if (w <= 0 || h <= 0) return;
                VizCanvas.Children.Clear();

                bool reactive = ReactiveCheck.IsChecked == true && ownerMain != null && ownerMain.IsMusicPlaying();
                double baseline = 0.02 + 0.02 * Math.Abs(Math.Sin(t));
                double level = baseline;
                    double sensitivity = 1.0;
                    try { sensitivity = SensitivitySlider?.Value ?? 1.0; } catch { sensitivity = 1.0; }
                // If reactive and owner can provide level, try to get it and smooth it. Apply silence threshold.
                try
                {
                    if (ReactiveCheck.IsChecked == true && ownerMain != null)
                    {
                        var raw = ownerMain.GetApproxAudioLevel();
                        if (!double.IsNaN(raw))
                        {
                            double lv = Math.Max(0.0, Math.Min(1.0, raw));
                            // Lower silence threshold so quiet tracks still register, then amplify for perceptual response.
                            if (lv < 0.001) lv = 0.0;
                            else lv = Math.Min(1.0, lv * 10.0);
                            // Smooth to avoid spiky jumps (higher responsiveness).
                            smoothedLevel = smoothedLevel * 0.65 + lv * 0.35;
                            if (reactive) level = Math.Max(level, smoothedLevel);
                        }
                        else if (reactive)
                        {
                            // Reactive but no meter available: keep low gentle motion so UI isn't dead.
                            level = 0.02 + 0.02 * rnd.NextDouble();
                        }
                    }
                }
                catch { }
                // Apply sensitivity and clamp
                try { level = Math.Max(0.0, Math.Min(1.0, level * sensitivity)); } catch { level = Math.Max(0.0, Math.Min(1.0, level)); }

                var selected = EffectCombo.SelectedItem as System.Windows.Controls.ComboBoxItem;
                var name = selected?.Content?.ToString() ?? "Bars";
                if (name == "Bars") DrawBars(w, h, level);
                else if (name == "Waveform") DrawWave(w, h, level);
                else if (name == "Particles") DrawParticles(w, h, level);
                else if (name == "Radial") DrawRadial(w, h, level);
                else if (name == "Spectrum") DrawSpectrum(w, h, level);
                else if (name == "Neon Pulse") DrawNeonPulse(w, h, level);
                else if (name == "Radial Bars") DrawRadialBars(w, h, level);
                else if (name == "Circular Spectrum") DrawCircularSpectrum(w, h, level);
                else if (name == "Starfield") DrawStarfield(w, h, level);
                else if (name == "Glitch") DrawGlitch(w, h, level);
                else if (name == "Echo Ribbons") DrawEchoRibbons(w, h, level);
                else if (name == "Fireflies") DrawFireflies(w, h, level);
                else if (name == "Glowing Rings") DrawGlowingRings(w, h, level);
                else if (name == "Sine Field") DrawSineField(w, h, level);
                else if (name == "Lattice") DrawLattice(w, h, level);
                else if (name == "Moving Tiles") DrawMovingTiles(w, h, level);
                else if (name == "Bokeh") DrawBokeh(w, h, level);
                else if (name == "Ripple") DrawRipple(w, h, level);
                else if (name == "Grid Warp") DrawGridWarp(w, h, level);
                else if (name == "Lava") DrawLava(w, h, level);
                else if (name == "Aurora") DrawAurora(w, h, level);
                else if (name == "Heartbeat") DrawHeartbeat(w, h, level);
                else if (name == "Falling Notes") DrawFallingNotes(w, h, level);
                else if (name == "Star Trails") DrawStarTrails(w, h, level);
                else if (name == "Mirror Waves") DrawMirrorWaves(w, h, level);
                else if (name == "Color Blocks") DrawColorBlocks(w, h, level);
                else if (name == "Binary Bars") DrawBinaryBars(w, h, level);
                else if (name == "Chromatic Blur") DrawChromaticBlur(w, h, level);
                else if (name == "Wavescape") DrawWavescape(w, h, level);
                else if (name == "Spiral") DrawSpiral(w, h, level);
                else if (name == "Brush Strokes") DrawBrushStrokes(w, h, level);
                else if (name == "Particle Bloom") DrawParticleBloom(w, h, level);
                else if (name == "Plasma") DrawPlasma(w, h, level);
                else if (name == "Kaleidoscope") DrawKaleidoscope(w, h, level);
                else if (name == "Pulse Grid") DrawPulseGrid(w, h, level);
                else if (name == "Rain") DrawRain(w, h, level);
                else if (name == "Voronoi") DrawVoronoi(w, h, level);
                else if (name == "Oscilloscope") DrawOscilloscope(w, h, level);
                else if (name == "Spiral Lines") DrawSpiralLines(w, h, level);
                else if (name == "Radial Pulse") DrawRadialPulse(w, h, level);
                else if (name == "Blob") DrawBlob(w, h, level);
                else if (name == "Grid Pulse") DrawGridPulse(w, h, level);
                else if (name == "Circular Rings") DrawCircularRings(w, h, level);
                else if (name == "Noise Field") DrawNoiseField(w, h, level);
                else if (name == "Bars 2") DrawBars2(w, h, level);
                else if (name == "Spectrum Radial") DrawSpectrumRadial(w, h, level);
                else if (name == "Strobe") DrawStrobe(w, h, level);
                else if (name == "Flicker") DrawFlicker(w, h, level);
                else if (name == "Ribbon Flow") DrawRibbonFlow(w, h, level);
                else if (name == "Zen Garden") DrawZenGarden(w, h, level);
                else if (name == "Energy Orbs") DrawEnergyOrbs(w, h, level);
                else if (name == "Pulsing Hex") DrawPulsingHex(w, h, level);
                else if (name == "Wave Warp") DrawWaveWarp(w, h, level);
                else if (name == "Star Burst") DrawStarBurst(w, h, level);
                else if (name == "Concentric Squares") DrawConcentricSquares(w, h, level);
                else if (name == "Neon Grid") DrawNeonGrid(w, h, level);
                else if (name == "Moving Dots") DrawMovingDots(w, h, level);
                else if (name == "Flame") DrawFlame(w, h, level);

                t += 0.033;
            }
            catch { }
        }

        private void DrawBars(double w, double h, double level)
        {
            int cols = 32;
            double bw = w / cols;
            for (int i = 0; i < cols; i++)
            {
                double x = i * bw;
                double amp = Math.Pow((0.2 + 0.8 * ((i % 5) / 4.0)) * level * (0.6 + 0.4 * rnd.NextDouble()), 0.9);
                double hh = amp * h;
                var rect = new Rectangle { Width = Math.Max(2, bw - 2), Height = hh, Fill = new SolidColorBrush(Color.FromRgb((byte)(120 + i*3%135), (byte)(180 - i*2%120), (byte)(200 - i*4%200))) };
                Canvas.SetLeft(rect, x + 1);
                Canvas.SetTop(rect, h - hh);
                VizCanvas.Children.Add(rect);
            }
        }

        private void DrawWave(double w, double h, double level)
        {
            var path = new System.Windows.Shapes.Path();
            var geo = new PathGeometry();
            var fig = new PathFigure();
            int samples = 256;
            for (int i = 0; i < samples; i++)
            {
                double nx = (double)i / (samples - 1);
                double vx = nx * w;
                double val = Math.Sin((t + nx * 6.28) * (1.0 + 3.0 * nx)) * (0.5 + 0.5 * Math.Sin(t * 0.7 + i));
                val *= level;
                double vy = h * 0.5 - val * (h * 0.45);
                if (i == 0) fig.StartPoint = new Point(vx, vy);
                else fig.Segments.Add(new PolyLineSegment(new Point[] { new Point(vx, vy) }, true));
            }
            geo.Figures.Add(fig);
            path.Data = geo;
            path.Stroke = Brushes.Cyan;
            path.StrokeThickness = 2;
            VizCanvas.Children.Add(path);
        }

        private void DrawParticles(double w, double h, double level)
        {
            int p = 60;
            for (int i = 0; i < p; i++)
            {
                double px = (rnd.NextDouble() + Math.Sin(t * 0.5 + i)) * 0.5 * w;
                double py = (rnd.NextDouble() + Math.Cos(t * 0.7 + i)) * 0.5 * h;
                double size = 2 + 10 * level * rnd.NextDouble();
                var ell = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromArgb((byte)(100 + rnd.Next(100)), (byte)rnd.Next(40,255), (byte)rnd.Next(40,255), (byte)rnd.Next(40,255))) };
                Canvas.SetLeft(ell, px);
                Canvas.SetTop(ell, py);
                VizCanvas.Children.Add(ell);
            }
        }

        private void DrawRadial(double w, double h, double level)
        {
            var cx = w * 0.5; var cy = h * 0.5;
            int rays = 48;
            for (int i = 0; i < rays; i++)
            {
                double a = (i / (double)rays) * Math.PI * 2.0 + t * 0.7;
                double len = 20 + (level * (0.5 + 0.5 * Math.Sin(t * 1.5 + i)) * Math.Min(w, h) * 0.45);
                var line = new Shapes.Line { X1 = cx, Y1 = cy, X2 = cx + Math.Cos(a) * len, Y2 = cy + Math.Sin(a) * len, Stroke = new SolidColorBrush(Color.FromArgb(180, (byte)(120 + i % 120), (byte)(200 - i % 120), (byte)(240 - i % 100))), StrokeThickness = 2 };
                VizCanvas.Children.Add(line);
            }
            var center = new Ellipse { Width = 12 + 30 * level, Height = 12 + 30 * level, Fill = new SolidColorBrush(Color.FromArgb(220, 255, 200, 80)) };
            Canvas.SetLeft(center, cx - center.Width / 2);
            Canvas.SetTop(center, cy - center.Height / 2);
            VizCanvas.Children.Add(center);
        }

        private void DrawSpectrum(double w, double h, double level)
        {
            int bands = 64;
            double bw = w / bands;
            for (int i = 0; i < bands; i++)
            {
                double x = i * bw;
                double value = Math.Pow(level * (0.2 + 0.8 * Math.Abs(Math.Sin(t * 0.5 + i * 0.11))), 0.9) * (0.3 + 0.7 * (i / (double)bands));
                double hh = value * h;
                var grad = new LinearGradientBrush();
                grad.StartPoint = new Point(0, 1); grad.EndPoint = new Point(0, 0);
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(220, (byte)(50 + i%120), (byte)(180 - i%120), 255), 0.0));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(220, (byte)(200), (byte)(80 + i%120), (byte)(120 + i%60)), 1.0));
                var rect = new Rectangle { Width = Math.Max(1, bw - 2), Height = hh, Fill = grad }; 
                Canvas.SetLeft(rect, x + 1);
                Canvas.SetTop(rect, h - hh);
                VizCanvas.Children.Add(rect);
            }
        }

        private void DrawNeonPulse(double w, double h, double level)
        {
            var cx = w * 0.5; var cy = h * 0.5;
            int rings = 5;
            for (int r = rings - 1; r >= 0; r--)
            {
                double rr = (r + 1) * (Math.Min(w, h) / (rings * 2.0)) * (0.6 + 0.4 * Math.Sin(t * (0.8 + r * 0.2)));
                var circ = new Ellipse { Width = rr * 2, Height = rr * 2, Stroke = new SolidColorBrush(Color.FromArgb((byte)(50 + r * 40 + (int)(level * 120)), (byte)(100 + r * 20), (byte)(200 - r * 30), (byte)(220))), StrokeThickness = 4 + r };
                Canvas.SetLeft(circ, cx - rr);
                Canvas.SetTop(circ, cy - rr);
                VizCanvas.Children.Add(circ);
            }
        }

        private void DrawRadialBars(double w, double h, double level)
        {
            var cx = w * 0.5; var cy = h * 0.5;
            int cols = 48;
            double maxR = Math.Min(w, h) * 0.45;
            for (int i = 0; i < cols; i++)
            {
                double a = (i / (double)cols) * Math.PI * 2.0 + t * 0.6;
                double amp = Math.Pow(level * (0.3 + 0.7 * ((i % 7) / 6.0)) * (0.6 + 0.4 * rnd.NextDouble()), 0.95);
                double r0 = maxR * 0.15;
                double r1 = r0 + amp * (maxR - r0);
                var line = new Shapes.Line { X1 = cx + Math.Cos(a) * r0, Y1 = cy + Math.Sin(a) * r0, X2 = cx + Math.Cos(a) * r1, Y2 = cy + Math.Sin(a) * r1, Stroke = new SolidColorBrush(Color.FromArgb(200, (byte)(180 - i%120), (byte)(120 + i%120), (byte)(220 - i%100))), StrokeThickness = 3 };
                VizCanvas.Children.Add(line);
            }
        }

        private void DrawCircularSpectrum(double w, double h, double level)
        {
            var cx = w * 0.5; var cy = h * 0.5;
            int bands = 64;
            double radius = Math.Min(w, h) * 0.35;
            for (int i = 0; i < bands; i++)
            {
                double theta = (i / (double)bands) * Math.PI * 2.0 + t * 0.2;
                double val = Math.Pow(level * (0.2 + 0.8 * Math.Abs(Math.Sin(t * 0.2 + i * 0.2))), 1.0);
                double r = radius * (0.4 + 0.6 * val);
                var circ = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.FromArgb(220, (byte)(100 + i%120), (byte)(180 - i%100), (byte)(240 - i%120))) };
                Canvas.SetLeft(circ, cx + Math.Cos(theta) * r - 3);
                Canvas.SetTop(circ, cy + Math.Sin(theta) * r - 3);
                VizCanvas.Children.Add(circ);
            }
        }

        private void DrawStarfield(double w, double h, double level)
        {
            int stars = 120;
            for (int i = 0; i < stars; i++)
            {
                double sx = (rnd.NextDouble() + Math.Sin(t * 0.1 + i)) * 0.5 * w + 0.25 * w;
                double sy = (rnd.NextDouble() + Math.Cos(t * 0.13 + i)) * 0.5 * h + 0.25 * h;
                double size = 1 + 3 * rnd.NextDouble() * (0.5 + level);
                var ell = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromArgb((byte)(120 + rnd.Next(135)), 255, 255, 220)) };
                Canvas.SetLeft(ell, sx);
                Canvas.SetTop(ell, sy);
                VizCanvas.Children.Add(ell);
            }
        }

        private void DrawGlitch(double w, double h, double level)
        {
            // Glitch effect: horizontal scanline slices offset by level and time
            int slices = 18;
            for (int s = 0; s < slices; s++)
            {
                double h0 = h / slices;
                double y0 = s * h0;
                double shift = Math.Sin(t * (2.0 + s * 0.1) + s) * (20 + level * 120);
                var rect = new Rectangle { Width = Math.Max(1, w), Height = Math.Max(1, h0 + 1), Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(40 + (int)(level * 200)), (byte)ClampToByte(180 + s * 3), (byte)ClampToByte(120 + s * 5), (byte)ClampToByte(220 - s * 4))) };
                Canvas.SetLeft(rect, shift);
                Canvas.SetTop(rect, y0 + Math.Sin(t * 1.5 + s) * 6);
                VizCanvas.Children.Add(rect);
            }
        }

        private void DrawEchoRibbons(double w, double h, double level)
        {
            int ribbons = 5;
            for (int r = 0; r < ribbons; r++)
            {
                int pts = 80;
                var pg = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb((byte)(50 + r * 30 + (int)(level * 120)), (byte)(120 + r * 20), (byte)(200 - r * 30), (byte)(240 - r * 20))), StrokeThickness = 2 + r };
                for (int i = 0; i < pts; i++)
                {
                    double nx = i / (double)(pts - 1);
                    double x = nx * w;
                    double y = h * 0.5 + Math.Sin(nx * 8.0 + t * (0.5 + r * 0.2) + r) * (30 + 80 * level) * (1.0 - r * 0.12);
                    pg.Points.Add(new Point(x, y));
                }
                VizCanvas.Children.Add(pg);
            }
        }

        private void DrawFireflies(double w, double h, double level)
        {
            int flies = 24;
            for (int i = 0; i < flies; i++)
            {
                double px = (0.2 + 0.6 * rnd.NextDouble()) * w + Math.Sin(t * (0.3 + i * 0.01)) * (20 + level * 80);
                double py = (0.2 + 0.6 * rnd.NextDouble()) * h + Math.Cos(t * (0.25 + i * 0.008)) * (20 + level * 80);
                double size = (2 + 6 * Math.Abs(Math.Sin(t * 1.2 + i))) * (0.5 + 1.5 * level);
                byte alpha = (byte)ClampToByte(80 + (int)(level * 175) + rnd.Next(40));
                var ell = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromArgb(alpha, 255, (byte)ClampToByte(200 + (int)(level * 55)), 120)) };
                Canvas.SetLeft(ell, px);
                Canvas.SetTop(ell, py);
                VizCanvas.Children.Add(ell);
            }
        }

        private void DrawPlasma(double w, double h, double level)
        {
            int cols = 40; int rows = 25;
            double cw = w / cols; double rh = h / rows;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    double nx = x / (double)cols; double ny = y / (double)rows;
                    double v = Math.Sin((nx * 10.0 + t) * (0.9 + 0.2 * Math.Sin(t * 0.3))) + Math.Cos((ny * 8.0 - t * 0.6));
                    v = (v * 0.5 + 0.5) * level;
                    byte r = (byte)(128 + 127 * Math.Sin(v * 6.28));
                    byte g = (byte)(80 + 175 * Math.Abs(Math.Sin(v * 3.14 + 1.0)));
                    byte b = (byte)(200 - 180 * Math.Abs(Math.Cos(v * 2.0)));
                    var rect = new Rectangle { Width = Math.Max(1, cw - 1), Height = Math.Max(1, rh - 1), Fill = new SolidColorBrush(Color.FromArgb(220, r, g, b)) };
                    Canvas.SetLeft(rect, x * cw);
                    Canvas.SetTop(rect, y * rh);
                    VizCanvas.Children.Add(rect);
                }
            }
        }

        private void DrawKaleidoscope(double w, double h, double level)
        {
            var cx = w * 0.5; var cy = h * 0.5;
            int slices = 6;
            int pts = 120;
            for (int s = 0; s < slices; s++)
            {
                var poly = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb((byte)(120 + s * 20), (byte)(150 + s * 10), (byte)(120 + s * 15), (byte)(200 - s * 10))), StrokeThickness = 1.5 };
                for (int i = 0; i < pts; i++)
                {
                    double a = (i / (double)pts) * Math.PI * 2.0 + t * (0.4 + s * 0.05);
                    double r = Math.Abs(Math.Sin(i * 0.1 + t * 0.5 + s)) * (Math.Min(w, h) * 0.35) * (0.4 + 0.6 * level);
                    double x = cx + Math.Cos(a + s * (2 * Math.PI / slices)) * r;
                    double y = cy + Math.Sin(a + s * (2 * Math.PI / slices)) * r;
                    poly.Points.Add(new Point(x, y));
                }
                VizCanvas.Children.Add(poly);
            }
        }

        private void DrawPulseGrid(double w, double h, double level)
        {
            int cols = 12, rows = 8;
            double cw = w / cols, ch = h / rows;
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                double phase = Math.Sin(t * 2.0 + (x + y) * 0.3);
                double v = (0.5 + 0.5 * phase) * level;
                var rect = new Rectangle { Width = Math.Max(4, cw - 6), Height = Math.Max(4, ch - 6), Fill = new SolidColorBrush(Color.FromArgb((byte)(100 + v * 155), (byte)(30 + x * 10), (byte)(80 + y * 12), (byte)(200 - x * 8))) };
                Canvas.SetLeft(rect, x * cw + (cw - rect.Width) / 2);
                Canvas.SetTop(rect, y * ch + (ch - rect.Height) / 2);
                VizCanvas.Children.Add(rect);
            }
        }

        private void DrawRain(double w, double h, double level)
        {
            int drops = 80;
            for (int i = 0; i < drops; i++)
            {
                double sx = (i * 9973 % 1000) / 1000.0 * w;
                double py = ((t * (0.5 + (i % 7) * 0.03) + (i * 13) % 47) % 1.0) * h;
                double len = 8 + 40 * (0.2 + (i % 5) * 0.12) * level;
                var line = new Shapes.Line { X1 = sx, Y1 = py - len, X2 = sx + (i % 3 - 1) * 4, Y2 = py, Stroke = new SolidColorBrush(Color.FromArgb((byte)(150 + (int)(level * 80)), 180, 200, 255)), StrokeThickness = 1 + (i % 3) };
                VizCanvas.Children.Add(line);
            }
        }

        private void DrawVoronoi(double w, double h, double level)
        {
            int seeds = 20;
            var sx = new double[seeds]; var sy = new double[seeds];
            // Seed positions animated slightly by time and level so the field moves when audio is present.
            for (int i = 0; i < seeds; i++)
            {
                double bx = (0.1 + 0.8 * ((i * 997) % 1000) / 1000.0) * w;
                double by = (0.1 + 0.8 * ((i * 1499) % 1000) / 1000.0) * h;
                double jitter = Math.Min(1.0, level) * Math.Min(w, h) * 0.07;
                sx[i] = bx + Math.Sin(t * (0.4 + (i % 5) * 0.07) + i) * jitter;
                sy[i] = by + Math.Cos(t * (0.35 + (i % 7) * 0.05) + i * 1.3) * jitter;
            }
            int cols = 40, rows = 25; double cw = w / cols, rh = h / rows;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    double cx = (x + 0.5) * cw, cy = (y + 0.5) * rh;
                    int best = 0; double bestd = double.MaxValue;
                    for (int i = 0; i < seeds; i++)
                    {
                        double dx = cx - sx[i]; double dy = cy - sy[i];
                        double d = dx * dx + dy * dy;
                        if (d < bestd) { bestd = d; best = i; }
                    }
                    // Color varies with seed index and pulses with level
                    byte r = (byte)((80 + best * 13) % 255);
                    byte g = (byte)((60 + best * 23) % 255);
                    byte b = (byte)((140 + best * 37) % 255);
                    byte alpha = (byte)ClampToByte(120 + (int)(level * 180));
                    var rect = new Rectangle { Width = Math.Max(1, cw - 1), Height = Math.Max(1, rh - 1), Fill = new SolidColorBrush(Color.FromArgb(alpha, r, g, b)) };
                    Canvas.SetLeft(rect, x * cw); Canvas.SetTop(rect, y * rh); VizCanvas.Children.Add(rect);
                }
            }
        }

        private void DrawOscilloscope(double w, double h, double level)
        {
            var path = new Polyline { Stroke = Brushes.Lime, StrokeThickness = 2 };
            int samples = 512;
            for (int i = 0; i < samples; i++)
            {
                double nx = i / (double)(samples - 1);
                double x = nx * w;
                double val = Math.Sin((nx * 8.0 + t * (1.0 + 2.0 * nx)) * (1.0 + 0.2 * Math.Sin(t * 0.3))) * level;
                double y = h * 0.5 - val * (h * 0.45);
                path.Points.Add(new Point(x, y));
            }
            VizCanvas.Children.Add(path);
        }

        // --- New additional simple effects (lightweight implementations) ---
        private void DrawGlowingRings(double w, double h, double level)
        {
            var cx = w*0.5; var cy = h*0.5;
            for (int i=0;i<8;i++){
                double r = (i+1) * (Math.Min(w,h)/20.0) * (1.0 + 0.4*Math.Sin(t*0.6 + i));
                var e = new Ellipse { Width = r*2, Height = r*2, Stroke = new SolidColorBrush(Color.FromArgb((byte)(30+level*200),(byte)(200-i*10),(byte)(150+i*8),(byte)(220-i*6))), StrokeThickness = 6 - i/2 };
                Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r); VizCanvas.Children.Add(e);
            }
        }

        private void DrawSineField(double w, double h, double level)
        {
            int cols=40, rows=20; double cw=w/cols, ch=h/rows;
            for(int y=0;y<rows;y++) for(int x=0;x<cols;x++){
                double v = Math.Sin((x*0.2+y*0.15)+t*(0.5+0.2*y/rows))*level;
                var rect = new Rectangle { Width=cw-1, Height=ch-1, Fill=new SolidColorBrush(Color.FromArgb((byte)(80+v*120),(byte)(120+v*60),(byte)(100+v*80),(byte)(200))) };
                Canvas.SetLeft(rect,x*cw); Canvas.SetTop(rect,y*ch); VizCanvas.Children.Add(rect);
            }
        }

        private void DrawLattice(double w, double h, double level)
        {
            int step = (int)Math.Max(8, 20 - level*10);
            for(int x=0;x<w;x+=step) for(int y=0;y<h;y+=step){
                var r = new Rectangle{ Width=step-2, Height=step-2, Fill=new SolidColorBrush(Color.FromArgb((byte)(50+ (int)(level*180)),(byte)rnd.Next(80,255),(byte)rnd.Next(80,255),(byte)rnd.Next(80,255)))};
                Canvas.SetLeft(r,x); Canvas.SetTop(r,y); VizCanvas.Children.Add(r);
            }
        }

        private void DrawMovingTiles(double w, double h, double level)
        {
            // Richer moving tiles: per-tile phase, color variation and parallax based on level
            int cols = Math.Max(8, (int)(12 + level * 20));
            int rows = Math.Max(4, (int)(6 + level * 12));
            double cw = w / cols, ch = h / rows;
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                double phase = Math.Sin(t * (0.6 + 0.2 * ((x + y) % 5)) + (x * 7 + y * 13) * 0.02);
                double offX = phase * (cw * 0.35) * (0.5 + level);
                double offY = Math.Cos(t * (0.4 + 0.15 * ((x + y) % 3)) + (x + y)) * (ch * 0.12) * (0.3 + level);
                byte r = (byte)ClampToByte(40 + x * 10 + (int)(level * 60));
                byte g = (byte)ClampToByte(60 + y * 12 + (int)(level * 50));
                byte b = (byte)ClampToByte(100 + ((x + y) * 7) % 120);
                var rect = new Rectangle { Width = Math.Max(4, cw - 6), Height = Math.Max(4, ch - 6), Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(120 + (int)(level * 120)), r, g, b)) };
                Canvas.SetLeft(rect, x * cw + (cw - rect.Width) / 2 + offX);
                Canvas.SetTop(rect, y * ch + (ch - rect.Height) / 2 + offY);
                VizCanvas.Children.Add(rect);
            }
        }

        private void DrawBokeh(double w, double h, double level)
        {
            // Layered bokeh with soft circles that grow/shrink with audio level
            int layers = 6;
            for (int L = 0; L < layers; L++)
            {
                int count = 8 + L * 6;
                double layerScale = 0.6 + L * 0.2 + level * 0.8;
                for (int i = 0; i < count; i++)
                {
                    double ang = (i / (double)count) * Math.PI * 2 + (L % 2 == 0 ? t * 0.1 : -t * 0.09);
                    double radius = Math.Min(w, h) * (0.15 + 0.25 * L / layers) * (0.8 + 0.6 * Math.Sin(t * (0.2 + L * 0.05) + i));
                    double cx = w * 0.5 + Math.Cos(ang) * radius * layerScale;
                    double cy = h * 0.5 + Math.Sin(ang) * radius * layerScale;
                    double sz = 20 + 120 * Math.Pow(level * (0.3 + 0.7 * rnd.NextDouble()), 0.8) * (1.0 + L * 0.15);
                    var grad = new RadialGradientBrush();
                    grad.GradientStops.Add(new GradientStop(Color.FromArgb((byte)ClampToByte(10 + (int)(level * 180)), 255, 240, 220), 0.0));
                    grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 240, 220), 1.0));
                    var e = new Ellipse { Width = sz, Height = sz, Fill = grad };
                    Canvas.SetLeft(e, cx - sz / 2); Canvas.SetTop(e, cy - sz / 2); VizCanvas.Children.Add(e);
                }
            }
        }

        private int ClampToByte(int v)
        {
            if (v < 0) return 0; if (v > 255) return 255; return v;
        }

        private void DrawRipple(double w, double h, double level)
        {
            double cx = w * 0.5; double cy = h * 0.5; for(int i=0;i<10;i++){
                double r = (i+1)*(Math.Min(w,h)/22.0)*(1.0 + 0.6*Math.Sin(t*0.9 + i));
                var circ = new Ellipse{ Width=r*2, Height=r*2, Stroke=new SolidColorBrush(Color.FromArgb((byte)(60+ i*12 + level*120), (byte)(200-i*6),(byte)(120+i*7),(byte)(240-i*8))), StrokeThickness=2 };
                Canvas.SetLeft(circ,cx-r); Canvas.SetTop(circ,cy-r); VizCanvas.Children.Add(circ);
            }
        }

        private void DrawGridWarp(double w, double h, double level)
        {
            int cols=20, rows=12; double cw=w/cols, ch=h/rows;
            for(int y=0;y<rows;y++) for(int x=0;x<cols;x++){
                double dx = Math.Sin(t*0.6 + x*0.3 + y*0.2)*level*10;
                double dy = Math.Cos(t*0.5 + x*0.2 + y*0.3)*level*8;
                var rect = new Rectangle{ Width=cw-2, Height=ch-2, Fill=new SolidColorBrush(Color.FromArgb((byte)(100),(byte)(80 + x*6),(byte)(100 + y*8),(byte)(200))) };
                Canvas.SetLeft(rect, x*cw+dx); Canvas.SetTop(rect, y*ch+dy); VizCanvas.Children.Add(rect);
            }
        }

        private void DrawLava(double w, double h, double level)
        {
            int bands=6; for(int b=0;b<bands;b++){
                var poly = new Polyline{ Stroke=new SolidColorBrush(Color.FromArgb((byte)(120 - b*10), (byte)(200),(byte)(80 + b*10),(byte)(30))), StrokeThickness=6-b };
                for(int i=0;i<60;i++){ double x = i/(60.0)*w; double y = h*0.3 + Math.Sin(i*0.2 + t*(0.5+b*0.2))* (40 + b*10)*level + b*20; poly.Points.Add(new Point(x,y)); }
                VizCanvas.Children.Add(poly);
            }
        }

        private void DrawAurora(double w, double h, double level)
        {
            for(int i=0;i<4;i++){
                var g = new LinearGradientBrush(); g.StartPoint=new Point(0,1); g.EndPoint=new Point(1,0);
                g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(40+ i*30),(byte)(50+i*30),(byte)(200-i*20),(byte)(200)),0)); g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(10+i*20), (byte)(20+i*30),(byte)(180-i*10),(byte)(150)),1));
                var rect = new Rectangle{ Width=w, Height=h*(0.2+0.05*i)* (1.0+0.5*level), Fill=g }; Canvas.SetLeft(rect,0); Canvas.SetTop(rect, h* (0.1 + i*0.15 + 0.05*Math.Sin(t*0.6))); VizCanvas.Children.Add(rect);
            }
        }

        private void DrawHeartbeat(double w, double h, double level)
        {
            var path = new Polyline{ Stroke=Brushes.Red, StrokeThickness=3 };
            int samples=200; for(int i=0;i<samples;i++){ double nx=i/(double)(samples-1); double x=nx*w; double val = Math.Sin(nx*8 + t*3)*level; double y = h*0.5 - val*(h*0.35 + 20*Math.Sin(t*2)); path.Points.Add(new Point(x,y)); } VizCanvas.Children.Add(path);
        }

        private void DrawFallingNotes(double w, double h, double level)
        {
            int n = 10 + (int)(level * 50);
            for (int i = 0; i < n; i++)
            {
                double x = ((i * 9973) % 1000) / 1000.0 * w;
                double speed = 0.3 + level * 2.5;
                double y = ((t * speed + i * 0.13) % 1.0) * h;
                byte alpha = (byte)ClampToByte(80 + (int)(level * 175));
                var rect = new Rectangle { Width = 6, Height = 12, Fill = new SolidColorBrush(Color.FromArgb(alpha, (byte)rnd.Next(120, 255), (byte)rnd.Next(120, 255), (byte)rnd.Next(120, 255))) };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                VizCanvas.Children.Add(rect);
            }
        }

        private void DrawStarTrails(double w, double h, double level)
        {
            int s=80; for(int i=0;i<s;i++){ double x = rnd.NextDouble()*w; double y = rnd.NextDouble()*h; var line=new Shapes.Line{ X1=x, Y1=y, X2=x+Math.Cos(t+i)* (10+40*level), Y2=y+Math.Sin(t+i)*(10+40*level), Stroke=new SolidColorBrush(Color.FromArgb((byte)(120),255,255,200)), StrokeThickness=1}; VizCanvas.Children.Add(line);} }

        private void DrawMirrorWaves(double w, double h, double level)
        {
            var p=new Polyline{ Stroke=Brushes.Magenta, StrokeThickness=2}; int s=300; for(int i=0;i<s;i++){ double nx=i/(double)(s-1); double x=nx*w; double v=Math.Sin(nx*6 + t)*level; double y=h*0.5 - v*h*0.3; p.Points.Add(new Point(x,y)); } VizCanvas.Children.Add(p); var p2=new Polyline{ Stroke=Brushes.Cyan, StrokeThickness=2}; for(int i=0;i<s;i++){ double nx=i/(double)(s-1); double x=nx*w; double v=Math.Sin(nx*6 + t + 1.5)*level; double y=h*0.5 + v*h*0.3; p2.Points.Add(new Point(x,y)); } VizCanvas.Children.Add(p2);
        }

        private void DrawColorBlocks(double w, double h, double level)
        {
            int cols = 8, rows = 4; double cw = w / cols, ch = h / rows;
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                byte alpha = (byte)ClampToByte(80 + (int)(level * 175));
                var r = new Rectangle { Width = cw - 4, Height = ch - 4, Fill = new SolidColorBrush(Color.FromArgb(alpha, (byte)(20 + x * 30), (byte)(40 + y * 40), (byte)(100 + (x * y) % 150))) };
                Canvas.SetLeft(r, x * cw + 2);
                Canvas.SetTop(r, y * ch + 2);
                VizCanvas.Children.Add(r);
            }
        }

        private void DrawBinaryBars(double w, double h, double level)
        {
            int cols=16; double bw=w/cols; for(int i=0;i<cols;i++){ bool bit = ((int)(t*4) + i)%2==0; var rect=new Rectangle{ Width=bw-2, Height= bit? h*level: h*0.05, Fill= bit? Brushes.Lime: Brushes.Gray }; Canvas.SetLeft(rect,i*bw+1); Canvas.SetTop(rect,h-rect.Height); VizCanvas.Children.Add(rect);} }

        private void DrawChromaticBlur(double w, double h, double level)
        {
            for (int i = 0; i < 6; i++)
            {
                double ofs = Math.Sin(t * (0.3 + i * 0.2)) * (10 + level * 60);
                var rect = new Rectangle { Width = w * (0.6 - i * 0.08), Height = h * (0.6 - i * 0.08), Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(30 + i * 30 + (int)(level * 80)), (byte)ClampToByte(200 - i * 20), (byte)ClampToByte(100 + i * 10), (byte)ClampToByte(180 + i * 5))) };
                Canvas.SetLeft(rect, (w - rect.Width) / 2 + ofs);
                Canvas.SetTop(rect, (h - rect.Height) / 2 + Math.Cos(t * (0.25 + i * 0.2)) * (6 + level * 40));
                VizCanvas.Children.Add(rect);
            }
        }

        private void DrawWavescape(double w, double h, double level)
        {
            int layers=6; for(int l=0;l<layers;l++){ var poly=new Polyline{ Stroke=new SolidColorBrush(Color.FromArgb((byte)(80 + l*20), (byte)(50+l*20), (byte)(120+l*10), (byte)(200))), StrokeThickness=2 }; for(int i=0;i<120;i++){ double nx=i/119.0; double x=nx*w; double v=Math.Sin(nx*6 + t*(1.0 + l*0.2))* (0.2 + l*0.15) * level; double y=h*(0.2 + l*0.12) + v*h*0.4; poly.Points.Add(new Point(x,y)); } VizCanvas.Children.Add(poly);} }

        private void DrawSpiral(double w, double h, double level)
        {
            double cx = w * 0.5; double cy = h * 0.5; var poly=new Polyline{ Stroke=Brushes.Gold, StrokeThickness=2}; for(int i=0;i<400;i++){ double a = i*0.1 + t*1.2; double r = i*0.6 * level; double x = cx + Math.Cos(a)*r%w; double y = cy + Math.Sin(a)*r%h; poly.Points.Add(new Point(x,y)); } VizCanvas.Children.Add(poly); }

        private void DrawBrushStrokes(double w, double h, double level)
        {
            // More organic strokes: long bezier-like paths that vary with level
            int strokes = 6 + (int)(level * 12);
            for (int i = 0; i < strokes; i++)
            {
                var path = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(80 + (int)(level * 160)), (byte)rnd.Next(80, 255), (byte)rnd.Next(50, 200), (byte)rnd.Next(80, 240))), StrokeThickness = 2 + level * 8 };
                double cx = rnd.NextDouble() * w; double cy = rnd.NextDouble() * h;
                int pts = 20 + (int)(level * 60);
                for (int p = 0; p < pts; p++)
                {
                    double ang = t * (0.2 + p * 0.01) + i + p;
                    double rad = Math.Min(w, h) * (0.05 + 0.02 * p) * (0.5 + level);
                    double x = (cx + Math.Cos(ang) * rad + w) % w;
                    double y = (cy + Math.Sin(ang) * rad + h) % h;
                    path.Points.Add(new Point(x, y));
                }
                VizCanvas.Children.Add(path);
            }
        }

        private void DrawParticleBloom(double w, double h, double level)
        {
            // Pseudo-particle system: deterministic positions based on time so particles appear continuous
            int p = 160;
            for (int i = 0; i < p; i++)
            {
                double seed = i * 997.0;
                double phase = (seed % 1000) / 1000.0;
                double life = (Math.Sin(t * (0.5 + (i % 7) * 0.03) + seed) * 0.5 + 0.5);
                double x = (phase + 0.3 * Math.Sin(t * 0.2 + seed)) % 1.0 * w;
                double y = (phase + 0.4 * Math.Cos(t * 0.17 + seed * 1.3)) % 1.0 * h;
                double speed = 10 + level * 120;
                x = (x + Math.Sin(t * 0.3 + seed) * level * 40) % w;
                y = (y + Math.Cos(t * 0.25 + seed) * level * 60) % h;
                double sz = 1 + 10 * life * (0.2 + level * 1.8);
                var col = Color.FromArgb((byte)ClampToByte(30 + (int)(level * 200)), (byte)ClampToByte(180 + (int)(Math.Sin(seed) * 40)), (byte)ClampToByte(140 + (int)(Math.Cos(seed) * 60)), 255);
                var e = new Ellipse { Width = sz, Height = sz, Fill = new SolidColorBrush(col) };
                Canvas.SetLeft(e, (x + w) % w); Canvas.SetTop(e, (y + h) % h); VizCanvas.Children.Add(e);
            }
        }

        // --- New extra effects ---
        private void DrawSpiralLines(double w, double h, double level)
        {
            var path = new Polyline { Stroke = Brushes.Gold, StrokeThickness = 1 + level * 3 };
            int pts = 300;
            for (int i = 0; i < pts; i++)
            {
                double a = i * 0.12 + t * (0.5 + level);
                double r = 0.2 + i * 0.002 * (1.0 + level * 2.0);
                double x = w * 0.5 + Math.Cos(a) * r * Math.Min(w, h);
                double y = h * 0.5 + Math.Sin(a) * r * Math.Min(w, h);
                path.Points.Add(new Point(x, y));
            }
            VizCanvas.Children.Add(path);
        }

        private void DrawRadialPulse(double w, double h, double level)
        {
            var cx = w * 0.5; var cy = h * 0.5;
            int rings = 8;
            for (int i = 0; i < rings; i++)
            {
                double r = (i + 1) * (Math.Min(w, h) / (rings * 2.0)) * (0.6 + 1.2 * level * Math.Abs(Math.Sin(t * (0.5 + i * 0.1))));
                var e = new Ellipse { Width = r * 2, Height = r * 2, Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(40 + (int)(level * 180)), (byte)(200 - i * 10), (byte)(120 + i * 10), (byte)(220 - i * 6))), StrokeThickness = 3 };
                Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r); VizCanvas.Children.Add(e);
            }
        }

        private void DrawBlob(double w, double h, double level)
        {
            int points = 24; var poly = new Polygon();
            for (int i = 0; i < points; i++)
            {
                double a = (i / (double)points) * Math.PI * 2.0;
                double r = (Math.Min(w, h) * 0.25) * (0.6 + 0.6 * level * Math.Sin(t + i));
                poly.Points.Add(new Point(w * 0.5 + Math.Cos(a) * r, h * 0.5 + Math.Sin(a) * r));
            }
            poly.Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(100 + (int)(level * 150)), 140, 100, 220));
            VizCanvas.Children.Add(poly);
        }

        private void DrawGridPulse(double w, double h, double level)
        {
            int cols = 20, rows = 12; double cw = w / cols, ch = h / rows;
            for (int y = 0; y < rows; y++) for (int x = 0; x < cols; x++)
            {
                double v = (0.5 + 0.5 * Math.Sin(t * (1.0 + level * 6) + (x + y))) * level;
                var rect = new Rectangle { Width = Math.Max(2, cw - 2), Height = Math.Max(2, ch - 2), Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(40 + (int)(v * 210)), (byte)(50 + x * 5), (byte)(80 + y * 6), 200)) };
                Canvas.SetLeft(rect, x * cw); Canvas.SetTop(rect, y * ch); VizCanvas.Children.Add(rect);
            }
        }

        private void DrawCircularRings(double w, double h, double level)
        {
            var cx = w * 0.5; var cy = h * 0.5; int rings = 12;
            for (int i = 0; i < rings; i++)
            {
                double ang = t * (0.2 + i * 0.03) + i;
                double r = Math.Abs(Math.Sin(ang)) * Math.Min(w, h) * 0.45 * level + (i * 6);
                var e = new Ellipse { Width = r, Height = r, Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(20 + (int)(level * 230)), (byte)(120 + i * 8), (byte)(200 - i * 6), (byte)(180 + i * 3))), StrokeThickness = 2 };
                Canvas.SetLeft(e, cx - r / 2); Canvas.SetTop(e, cy - r / 2); VizCanvas.Children.Add(e);
            }
        }

        private void DrawNoiseField(double w, double h, double level)
        {
            // Smooth noise field using layered sin/cos waves for visual texture
            int cols = 40, rows = 28; double cw = w / cols, ch = h / rows;
            for (int y = 0; y < rows; y++) for (int x = 0; x < cols; x++)
            {
                double nx = x / (double)cols, ny = y / (double)rows;
                double v = (Math.Sin((nx + t * 0.1) * 6.0) + Math.Cos((ny + t * 0.12) * 7.0)) * 0.5;
                v = (v * 0.5 + 0.5) * (0.2 + level * 0.8);
                byte a = (byte)ClampToByte(30 + (int)(v * 220));
                var rect = new Rectangle { Width = Math.Max(2, cw - 1), Height = Math.Max(2, ch - 1), Fill = new SolidColorBrush(Color.FromArgb(a, (byte)ClampToByte(120 + (int)(v * 80)), (byte)ClampToByte(130 + (int)(v * 80)), (byte)ClampToByte(200))) };
                Canvas.SetLeft(rect, x * cw); Canvas.SetTop(rect, y * ch); VizCanvas.Children.Add(rect);
            }
        }

        private void DrawBars2(double w, double h, double level)
        {
            int cols = 16; double bw = w / cols;
            for (int i = 0; i < cols; i++)
            {
                double amp = Math.Pow(level * (0.3 + 0.7 * Math.Abs(Math.Sin(t + i))), 0.9);
                double hh = amp * h;
                var rect = new Rectangle { Width = Math.Max(2, bw - 4), Height = hh, Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(120 + (int)(level * 135)), (byte)(200 - i * 6), (byte)(80 + i * 8), 220)) };
                Canvas.SetLeft(rect, i * bw + 2); Canvas.SetTop(rect, h - hh); VizCanvas.Children.Add(rect);
            }
        }

        private void DrawSpectrumRadial(double w, double h, double level)
        {
            var cx = w * 0.5; var cy = h * 0.5; int bands = 48;
            for (int i = 0; i < bands; i++)
            {
                double a = (i / (double)bands) * Math.PI * 2.0 + t * 0.1;
                double r = (0.2 + level * (0.3 + 0.7 * Math.Abs(Math.Sin(t + i)))) * Math.Min(w, h) * 0.45;
                var rect = new Rectangle { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(140 + (int)(level * 120)), (byte)(120 + i * 2), (byte)(180 - i * 2), 240)) };
                Canvas.SetLeft(rect, cx + Math.Cos(a) * r - 3); Canvas.SetTop(rect, cy + Math.Sin(a) * r - 3); VizCanvas.Children.Add(rect);
            }
        }

        private void DrawStrobe(double w, double h, double level)
        {
            double flick = (Math.Sin(t * 60.0) * 0.5 + 0.5) * level;
            if (flick > 0.6)
            {
                var rect = new Rectangle { Width = w, Height = h, Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(60 + (int)(level * 180)), 255, 255, 255)) };
                Canvas.SetLeft(rect, 0); Canvas.SetTop(rect, 0); VizCanvas.Children.Add(rect);
            }
        }

        private void DrawFlicker(double w, double h, double level)
        {
            int p = 40;
            for (int i = 0; i < p; i++)
            {
                double x = rnd.NextDouble() * w; double y = rnd.NextDouble() * h;
                double s = 1 + rnd.NextDouble() * 6 * level;
                var e = new Ellipse { Width = s, Height = s, Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(30 + (int)(level * 220)), 255, 240, 200)) };
                Canvas.SetLeft(e, x); Canvas.SetTop(e, y); VizCanvas.Children.Add(e);
            }
        }

        private void DrawRibbonFlow(double w, double h, double level)
        {
            int ribbons = 4;
            for (int r = 0; r < ribbons; r++)
            {
                var pg = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(60 + (int)(level * 160)), (byte)(120 + r * 20), (byte)(200 - r * 30), (byte)(240 - r * 20))), StrokeThickness = 2 + r };
                for (int i = 0; i < 120; i++)
                {
                    double nx = i / 119.0; double x = nx * w; double y = h * 0.5 + Math.Sin(nx * 6.0 + t * (0.5 + r * 0.2)) * (20 + 120 * level) * (1.0 - r * 0.15);
                    pg.Points.Add(new Point(x, y));
                }
                VizCanvas.Children.Add(pg);
            }
        }

        private void DrawZenGarden(double w, double h, double level)
        {
            int rings = 20; var cx = w * 0.5; var cy = h * 0.5;
            for (int i = 0; i < rings; i++)
            {
                double r = (i + 1) * (Math.Min(w, h) / (rings * 2.0));
                var e = new Ellipse { Width = r * 2, Height = r * 2, Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(8 + (int)(level * 180)), 200, 200, 220)), StrokeThickness = 1 };
                Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r); VizCanvas.Children.Add(e);
            }
        }

        private void DrawEnergyOrbs(double w, double h, double level)
        {
            int n = 12 + (int)(level * 30);
            for (int i = 0; i < n; i++)
            {
                double a = (i / (double)n) * Math.PI * 2.0 + t * (0.2 + level);
                double r = (0.2 + 0.6 * level) * Math.Min(w, h) * 0.35;
                double x = w * 0.5 + Math.Cos(a) * r; double y = h * 0.5 + Math.Sin(a) * r;
                double s = 6 + 20 * level * rnd.NextDouble();
                var e = new Ellipse { Width = s, Height = s, Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(80 + (int)(level * 170)), (byte)rnd.Next(120, 255), (byte)rnd.Next(120, 255), 255)) };
                Canvas.SetLeft(e, x - s / 2); Canvas.SetTop(e, y - s / 2); VizCanvas.Children.Add(e);
            }
        }

        private void DrawPulsingHex(double w, double h, double level)
        {
            int rings = 6; var cx = w * 0.5; var cy = h * 0.5; double baseR = Math.Min(w, h) * 0.2;
            for (int r = 0; r < rings; r++)
            {
                double R = baseR * (1.0 + r * 0.25) * (1.0 + 0.6 * level * Math.Abs(Math.Sin(t * (0.6 + r * 0.1))));
                var poly = new Polygon();
                for (int i = 0; i < 6; i++) poly.Points.Add(new Point(cx + Math.Cos(i * Math.PI * 2 / 6) * R, cy + Math.Sin(i * Math.PI * 2 / 6) * R));
                poly.Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(40 + (int)(level * 200)), 200, 160, 240)); poly.StrokeThickness = 2;
                VizCanvas.Children.Add(poly);
            }
        }

        private void DrawWaveWarp(double w, double h, double level)
        {
            int lines = 30;
            for (int i = 0; i < lines; i++)
            {
                var p = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(80 + (int)(level * 160)), 120, 200, 240)), StrokeThickness = 1 + level * 2 };
                for (int x = 0; x < 200; x++)
                {
                    double nx = x / 199.0; double X = nx * w; double Y = h * (0.2 + i / (double)lines * 0.6) + Math.Sin(nx * 6.0 + t * (0.5 + i * 0.03)) * (10 + level * 80);
                    p.Points.Add(new Point(X, Y));
                }
                VizCanvas.Children.Add(p);
            }
        }

        private void DrawStarBurst(double w, double h, double level)
        {
            var cx = w * 0.5; var cy = h * 0.5; int rays = 60 + (int)(level * 120);
            for (int i = 0; i < rays; i++)
            {
                double a = i / (double)rays * Math.PI * 2 + rnd.NextDouble() * 0.2;
                double len = 20 + level * (100 + rnd.NextDouble() * 200);
                var line = new Shapes.Line { X1 = cx, Y1 = cy, X2 = cx + Math.Cos(a) * len, Y2 = cy + Math.Sin(a) * len, Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(120 + (int)(level * 120)), 255, 220, 160)), StrokeThickness = 1 + level * 2 };
                VizCanvas.Children.Add(line);
            }
        }

        private void DrawConcentricSquares(double w, double h, double level)
        {
            int n = 10;
            for (int i = 0; i < n; i++)
            {
                double s = Math.Min(w, h) * (1.0 - i / (double)n) * (0.4 + level * 0.6);
                var rect = new Rectangle { Width = s, Height = s, Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(30 + (int)(level * 200)), 200, 180, 220)), StrokeThickness = 2 };
                Canvas.SetLeft(rect, w * 0.5 - s / 2); Canvas.SetTop(rect, h * 0.5 - s / 2); VizCanvas.Children.Add(rect);
            }
        }

        private void DrawNeonGrid(double w, double h, double level)
        {
            int cols = 20, rows = 12; double cw = w / cols, ch = h / rows;
            for (int y = 0; y < rows; y++) for (int x = 0; x < cols; x++)
            {
                var line = new Shapes.Line { X1 = x * cw, Y1 = y * ch, X2 = x * cw + cw, Y2 = y * ch + ch, Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(20 + (int)(level * 220)), (byte)ClampToByte(120 + x * 5), (byte)ClampToByte(200 - y * 6), 255)), StrokeThickness = 1 };
                VizCanvas.Children.Add(line);
            }
        }

        private void DrawMovingDots(double w, double h, double level)
        {
            int n = 40 + (int)(level * 200);
            for (int i = 0; i < n; i++)
            {
                double x = (rnd.NextDouble() + Math.Sin(t * (0.2 + i * 0.01))) * 0.5 * w + 0.25 * w;
                double y = (rnd.NextDouble() + Math.Cos(t * (0.13 + i * 0.008))) * 0.5 * h + 0.25 * h;
                double s = 1 + level * 4 * rnd.NextDouble();
                var e = new Ellipse { Width = s, Height = s, Fill = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(40 + (int)(level * 220)), 255, 255, 200)) };
                Canvas.SetLeft(e, x); Canvas.SetTop(e, y); VizCanvas.Children.Add(e);
            }
        }

        private void DrawFlame(double w, double h, double level)
        {
            int bands = 6;
            for (int b = 0; b < bands; b++)
            {
                var poly = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb((byte)ClampToByte(80 + (int)(level * 160)), (byte)ClampToByte(200 - b * 20), (byte)ClampToByte(80 + b * 15), 30)), StrokeThickness = 4 - b * 0.4 };
                for (int i = 0; i < 40; i++)
                {
                    double nx = i / 39.0; double x = nx * w; double y = h * 0.8 - Math.Abs(Math.Sin(nx * 6.0 + t * (0.5 + b * 0.03))) * (80 + level * 300) - b * 10;
                    poly.Points.Add(new Point(x, y));
                }
                VizCanvas.Children.Add(poly);
            }
        }
    }
}

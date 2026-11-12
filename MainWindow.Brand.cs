using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>
/// "--brand &lt;folder&gt;": renders the icon at every size and the intro / outro animations of the
/// review video frame by frame (60 fps PNGs), so they are perfectly smooth and deterministic.
/// </summary>
public partial class MainWindow
{
    private const double Fps = 60;

    private static double Clamp01(double x) => Math.Clamp(x, 0, 1);
    private static double EaseOut(double x) => 1 - Math.Pow(1 - Clamp01(x), 3);
    private static double SpringIn(double t) => t <= 0 ? 0 : 1 - Math.Exp(-6 * t) * Math.Cos(10 * t);

    private static void SavePngBitmap(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private static void BrandText(DrawingContext dc, string text, double size, Brush brush, double cy, double opacity, double rise,
        FontWeight? weight = null, string font = "Segoe UI Variable Display, Segoe UI")
    {
        if (opacity <= 0) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(font), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size, brush, 1.0)
        {
            TextAlignment = TextAlignment.Center, MaxTextWidth = PW - 120,
        };
        dc.PushOpacity(opacity);
        dc.DrawText(ft, new Point(60, cy - ft.Height / 2 + rise));
        dc.Pop();
    }

    private static void BrandBackdrop(DrawingContext dc, double t, double glow)
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x0A, 0x0A, 0x10), Color.FromRgb(0x17, 0x10, 0x1E), 90), null, new Rect(0, 0, PW, PH));
        void Glow(Color c, Point p, double r, byte a)
        {
            if (r < 1) return;
            var b = new RadialGradientBrush(Color.FromArgb(a, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
            dc.DrawEllipse(b, null, p, r, r);
        }
        Glow(BrandArt.Orange, new Point(540 + Math.Sin(t * 0.7) * 40, 760), 680 * glow, 0x62);
        Glow(BrandArt.Violet, new Point(540 + Math.Cos(t * 0.5) * 60, 1400), 760 * glow, 0x70);
        // drifting sparkles
        var rnd = new Random(7);
        for (int i = 0; i < 34; i++)
        {
            double x = rnd.NextDouble() * PW, y0 = rnd.NextDouble() * PH, sp = 18 + rnd.NextDouble() * 40, ph = rnd.NextDouble() * 6;
            double y = (y0 - t * sp + PH) % PH;
            double a = glow * (0.25 + 0.35 * (0.5 + 0.5 * Math.Sin(t * 2.2 + ph)));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(a * 255), 0xFF, 0xE2, 0xD0)), null, new Point(x, y), 1.6 + (i % 3), 1.6 + (i % 3));
        }
    }

    private void RenderIcons(string folder)
    {
        foreach (int size in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256, 1024 })
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                BrandArt.DrawIcon(dc, new Rect(0, 0, size, size));
            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            SavePngBitmap(rtb, Path.Combine(folder, $"icon_{size}.png"));
        }
    }

    private void RenderIntro(string folder, int frames)
    {
        Directory.CreateDirectory(folder);
        var rtb = new RenderTargetBitmap(PW, PH, 96, 96, PixelFormats.Pbgra32);
        var orange = new SolidColorBrush(Color.FromRgb(0xF0, 0xA2, 0x7A));
        var gray = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB2));
        for (int f = 0; f < frames; f++)
        {
            double t = f / Fps;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                BrandBackdrop(dc, t, EaseOut(t / 1.1));
                double appear = SpringIn(t - 0.2);
                double expand = t is > 1.15 and < 1.95 ? Math.Pow(Math.Sin(Math.PI * (t - 1.15) / 0.8), 2) : 0;
                double blink = t is > 2.3 and < 2.46 ? Math.Sin(Math.PI * (t - 2.3) / 0.16)
                             : t is > 4.6 and < 4.76 ? Math.Sin(Math.PI * (t - 4.6) / 0.16) : 0;
                double size = 440 * appear;
                var c = new Point(540, 800 - EaseOut((t - 1.4) / 0.6) * 40 + Math.Sin(Math.Max(0, t - 2.4) * 1.6) * 8);

                // a ring that pulses out when the island stretches
                double rt = t - 1.15;
                if (rt is > 0 and < 0.9)
                {
                    double rr = 240 + rt / 0.9 * 330;
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(130 * (1 - rt / 0.9)), 0xFF, 0xB0, 0x90)), 4);
                    dc.DrawEllipse(null, pen, c, rr, rr);
                }
                if (size > 2)
                {
                    dc.PushOpacity(Clamp01((t - 0.2) * 5));
                    BrandArt.DrawIcon(dc, new Rect(c.X - size / 2, c.Y - size / 2, size, size), expand, blink, Math.Sin(t * 2.4) * 0.8);
                    dc.Pop();
                }
                BrandText(dc, "Dynamic Island", 108, Brushes.White, 1170, EaseOut((t - 1.5) / 0.45), (1 - EaseOut((t - 1.5) / 0.45)) * 50, FontWeights.Bold);
                BrandText(dc, "для Windows", 60, orange, 1290, EaseOut((t - 1.75) / 0.45), (1 - EaseOut((t - 1.75) / 0.45)) * 40, FontWeights.SemiBold);
                BrandText(dc, "обзор версии 1.1", 42, gray, 1385, EaseOut((t - 2.0) / 0.45), (1 - EaseOut((t - 2.0) / 0.45)) * 30, null, "Segoe UI Variable Text, Segoe UI");
            }
            rtb.Clear();
            rtb.Render(dv);
            SavePngBitmap(rtb, Path.Combine(folder, $"{f:D4}.png"));
        }
    }

    private void RenderOutro(string folder, int frames)
    {
        Directory.CreateDirectory(folder);
        var rtb = new RenderTargetBitmap(PW, PH, 96, 96, PixelFormats.Pbgra32);
        var orange = new SolidColorBrush(Color.FromRgb(0xF0, 0xA2, 0x7A));
        var gray = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xAC));
        var soft = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF));

        // Claude Code waves goodbye (a real CharacterView, ticked frame by frame)
        var buddy = new CharacterView { Kind = "clawd", LayoutTransform = new ScaleTransform(9, 9) };
        var host = new Canvas { Width = PW, Height = PH };
        host.Children.Add(buddy);
        Canvas.SetLeft(buddy, 540 - 15 * 9);
        Canvas.SetTop(buddy, 880 - 11 * 9);
        host.Measure(new Size(PW, PH));
        host.Arrange(new Rect(0, 0, PW, PH));

        for (int f = 0; f < frames; f++)
        {
            double t = f / Fps;
            if (f == (int)(3.0 * Fps)) buddy.Emote(Emotion.Love);
            buddy.LookX = Math.Sin(t * 1.3) * 0.7;
            buddy.Tick(1 / Fps);
            host.UpdateLayout();

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                BrandBackdrop(dc, t + 4, 1);
                double size = 300 * SpringIn(t - 0.05);
                if (size > 2)
                    BrandArt.DrawIcon(dc, new Rect(540 - size / 2, 470 - size / 2, size, size), 0, t is > 2.0 and < 2.16 ? Math.Sin(Math.PI * (t - 2.0) / 0.16) : 0, Math.Sin(t * 2) * 0.8);
                BrandText(dc, "Скачай бесплатно", 92, Brushes.White, 1180, EaseOut((t - 0.5) / 0.45), (1 - EaseOut((t - 0.5) / 0.45)) * 50, FontWeights.Bold);
                BrandText(dc, "ссылка в профиле", 54, orange, 1295, EaseOut((t - 0.8) / 0.45), (1 - EaseOut((t - 0.8) / 0.45)) * 40, FontWeights.SemiBold);
                BrandText(dc, "github.com/mykolaichechelnytskyi/dynamic-island-windows", 30, gray, 1385, EaseOut((t - 1.1) / 0.45), 0, null, "Segoe UI");
                double up = EaseOut((t - 1.7) / 0.5);
                BrandText(dc, "Проект будет обновляться", 46, soft, 1530, up, (1 - up) * 30, FontWeights.SemiBold);
                // three dots that keep "loading" the next update
                if (up > 0)
                    for (int i = 0; i < 3; i++)
                    {
                        double a = 0.3 + 0.7 * Math.Max(0, Math.Sin(t * 5 - i * 0.8));
                        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(255 * a * up), 0xF0, 0xA2, 0x7A)), null, new Point(510 + i * 30, 1600), 7, 7);
                    }
            }
            rtb.Clear();
            rtb.Render(dv);
            rtb.Render(host);
            SavePngBitmap(rtb, Path.Combine(folder, $"{f:D4}.png"));
        }
    }

    private async Task RunBrandAsync(string folder)
    {
        folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        await Task.Delay(500);
        RenderIcons(folder);
        RenderIntro(Path.Combine(folder, "intro"), (int)(6.4 * Fps));
        RenderOutro(Path.Combine(folder, "outro"), (int)(7.0 * Fps));
        Services.Log.Write("brand: done -> " + folder);
        Application.Current.Shutdown();
    }
}

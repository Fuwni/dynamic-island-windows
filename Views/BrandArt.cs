using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Views;

/// <summary>
/// The app icon, drawn in code so it stays sharp at every size: a deep violet "squircle" with a warm
/// glow rising from the bottom, and the island itself — a black pill with two little eyes — floating
/// near the top, lit from below by its aura.
/// </summary>
public static class BrandArt
{
    public static readonly Color Ink = Color.FromRgb(0x06, 0x06, 0x09);
    public static readonly Color Orange = Color.FromRgb(0xFF, 0x8A, 0x5B);
    public static readonly Color Pink = Color.FromRgb(0xF0, 0x4E, 0x98);
    public static readonly Color Violet = Color.FromRgb(0x6D, 0x3B, 0xF5);
    public static readonly Color Deep = Color.FromRgb(0x17, 0x12, 0x3D);

    private static Geometry Squircle(Rect r)
    {
        // a superellipse-ish rounded square (continuous corners like on iOS)
        double k = r.Width * 0.5, n = 5.0;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int i = 0; i <= 160; i++)
            {
                double a = i / 160.0 * Math.PI * 2;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                double x = Math.Sign(ca) * Math.Pow(Math.Abs(ca), 2 / n) * k;
                double y = Math.Sign(sa) * Math.Pow(Math.Abs(sa), 2 / n) * k;
                var p = new Point(r.X + k + x, r.Y + r.Height / 2 + y * r.Height / r.Width);
                if (i == 0) c.BeginFigure(p, true, true); else c.LineTo(p, true, true);
            }
        }
        g.Freeze();
        return g;
    }

    /// <param name="expand">0 = resting pill, 1 = stretched wide (used by the intro animation).</param>
    /// <param name="blink">0 = eyes open, 1 = closed.</param>
    public static void DrawIcon(DrawingContext dc, Rect r, double expand = 0, double blink = 0, double lookX = 0.25)
    {
        double s = r.Width;
        bool tiny = s < 40;
        var shape = Squircle(r);

        // background: deep violet, a glow rising from the bottom
        var bg = new LinearGradientBrush { StartPoint = new Point(0.2, 0), EndPoint = new Point(0.8, 1) };
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x1C, 0x17, 0x4A), 0));
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x3B, 0x1F, 0x86), 0.55));
        bg.GradientStops.Add(new GradientStop(Color.FromRgb(0x6A, 0x2A, 0x9E), 1));
        dc.DrawGeometry(bg, null, shape);
        dc.PushClip(shape);
        var warm = new RadialGradientBrush
        {
            Center = new Point(0.5, 1.05), GradientOrigin = new Point(0.5, 1.05), RadiusX = 0.75, RadiusY = 0.62,
        };
        warm.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, Orange.R, Orange.G, Orange.B), 0));
        warm.GradientStops.Add(new GradientStop(Color.FromArgb(0x90, Pink.R, Pink.G, Pink.B), 0.45));
        warm.GradientStops.Add(new GradientStop(Color.FromArgb(0, Pink.R, Pink.G, Pink.B), 1));
        dc.DrawRectangle(warm, null, r);
        var cool = new RadialGradientBrush { Center = new Point(0.15, 0.1), GradientOrigin = new Point(0.15, 0.1), RadiusX = 0.6, RadiusY = 0.6 };
        cool.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0x4F, 0x7C, 0xFF), 0));
        cool.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x4F, 0x7C, 0xFF), 1));
        dc.DrawRectangle(cool, null, r);
        // glassy top sheen
        var sheen = new LinearGradientBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 90);
        dc.DrawRectangle(sheen, null, new Rect(r.X, r.Y, s, s * 0.5));

        // the island
        double pw = s * (tiny ? 0.72 : 0.6) * (1 + 0.32 * expand), ph = s * (tiny ? 0.26 : 0.2) * (1 + 0.55 * expand);
        var pill = new Rect(r.X + (s - pw) / 2, r.Y + s * (tiny ? 0.3 : 0.27), pw, ph);
        // aura under it
        if (!tiny)
        {
            for (int i = 6; i >= 1; i--)
            {
                double g = i * s * 0.022;
                var a = new Rect(pill.X - g, pill.Y - g * 0.4, pill.Width + g * 2, pill.Height + g * 1.6);
                byte alpha = (byte)(26 - i * 3);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(alpha, Orange.R, Orange.G, Orange.B)), null, a, a.Height / 2, a.Height / 2);
            }
        }
        dc.DrawRoundedRectangle(new SolidColorBrush(Ink), tiny ? null : new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), s * 0.006),
            pill, ph / 2, ph / 2);

        if (!tiny)
        {
            // two eyes on the left of the pill, a little equaliser on the right
            double er = ph * 0.26, ey = pill.Y + ph / 2;
            foreach (double ex in new[] { pill.X + ph * 0.62, pill.X + ph * 1.24 })
            {
                double ry = er * (1 - 0.9 * blink);
                dc.DrawEllipse(Brushes.White, null, new Point(ex, ey), er, Math.Max(er * 0.12, ry));
                if (blink < 0.5)
                    dc.DrawEllipse(new SolidColorBrush(Ink), null, new Point(ex + er * 0.3 * lookX, ey + er * 0.08), er * 0.5, er * 0.5 * (1 - blink));
            }
            var eq = new LinearGradientBrush(Orange, Pink, 90);
            double bw = ph * 0.13, gap = ph * 0.1, x0 = pill.Right - ph * 0.55 - 3 * (bw + gap);
            double[] hs = [0.45, 0.8, 0.6];
            for (int i = 0; i < 3; i++)
            {
                double bh = ph * 0.6 * hs[i];
                dc.DrawRoundedRectangle(eq, null, new Rect(x0 + i * (bw + gap), ey - bh / 2, bw, bh), bw / 2, bw / 2);
            }
        }
        dc.Pop();
        // fine rim
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), Math.Max(0.6, s * 0.006)), shape);
    }
}

using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Views;

/// <summary>
/// The Atoll mark, drawn in code so it stays sharp at every size: a graphite rounded tile with a
/// white ring-shaped pill (the atoll) and one warm dot in its lagoon — the island's live activity.
/// </summary>
public static class BrandArt
{
    public static readonly Color Ink = Color.FromRgb(0x06, 0x06, 0x09);
    public static readonly Color Accent = Color.FromRgb(0xFF, 0x7A, 0x45);
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

    /// <param name="expand">0 = resting ring, 1 = stretched wide (used by the intro animation).</param>
    /// <param name="blink">0 = round dot, 1 = squashed flat (a "blink" of the live dot).</param>
    /// <param name="lookX">-1 … 1: where the dot sits inside the lagoon (-0.6 = its resting place on the left).</param>
    public static void DrawIcon(DrawingContext dc, Rect r, double expand = 0, double blink = 0, double lookX = -0.6)
    {
        double s = r.Width;
        bool tiny = s < 40;
        var shape = Squircle(r);

        // tile: soft graphite, lighter at the top, a hairline rim
        var bg = new LinearGradientBrush(Color.FromRgb(0x22, 0x22, 0x2A), Color.FromRgb(0x0D, 0x0D, 0x11), 90);
        bg.Freeze();
        dc.DrawGeometry(bg, null, shape);
        if (!tiny)
        {
            dc.PushClip(shape);
            var sheen = new LinearGradientBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 90);
            dc.DrawRectangle(sheen, null, new Rect(r.X, r.Y, s, s * 0.45));
            dc.Pop();
        }
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), Math.Max(0.6, s * 0.008)), shape);

        // the atoll: a white ring shaped like the island
        double stroke = s * (tiny ? 0.095 : 0.056);
        double w = s * (tiny ? 0.70 : 0.60) * (1 + 0.22 * expand);
        double h = s * (tiny ? 0.36 : 0.26) * (1 + 0.12 * expand);
        var ring = new Rect(r.X + (s - w) / 2 + stroke / 2, r.Y + (s - h) / 2 + stroke / 2, w - stroke, h - stroke);
        dc.DrawRoundedRectangle(null, new Pen(Brushes.White, stroke), ring, ring.Height / 2, ring.Height / 2);

        // the live dot in the lagoon
        double inner = ring.Height - stroke;                     // lagoon height
        double dr = inner * (tiny ? 0.40 : 0.36);
        double travel = (ring.Width - stroke - inner) / 2;       // how far the dot may slide from the centre
        var dot = new Point(ring.X + ring.Width / 2 + lookX * travel, ring.Y + ring.Height / 2);
        var accent = new SolidColorBrush(Accent);
        accent.Freeze();
        dc.DrawEllipse(accent, null, dot, dr, dr * (1 - 0.75 * Math.Clamp(blink, 0, 1)));
    }
}

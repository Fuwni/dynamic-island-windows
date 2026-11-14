using System.Windows;
using System.Windows.Media;

namespace DynamicIsland.Views;

/// <summary>
/// Animated season scenes inside the island (Settings › Appearance › Themes): spring flowers and falling
/// petals, a summer beach with a wind-blown umbrella, a cosy winter window with snow and candle light, and
/// autumn leaves. Everything is drawn in code, kept to the edges so the island's text stays readable, and
/// only a few dozen shapes a frame. <see cref="Step"/> moves it; with light graphics it is drawn once, still.
/// </summary>
public sealed class SeasonView : FrameworkElement
{
    public static readonly string[] Kinds = ["spring", "summer", "autumn", "winter"];

    private string _kind = "";
    public string Kind
    {
        get => _kind;
        set { if (_kind == value) return; _kind = value; Reset(); InvalidateVisual(); }
    }

    /// <summary>The island is folded: a quieter scene (no flowers or umbrella over the pill's text).</summary>
    public bool Compact { get; set; }

    private sealed class Bit
    {
        /// <summary>X is a fraction of the width (the island keeps changing width), Y is in pixels.</summary>
        public double X, Y, Vx, Vy, Size, Angle, Spin, Phase, Hue;
        public bool Resting;
    }

    private readonly List<Bit> _bits = [];
    private readonly Random _rnd = new(7);
    private double _t;

    // summer: the umbrella's state (planted, swaying, blown away, coming back)
    private double _gustAt = 7, _umbFly = -1, _umbX, _umbY, _umbAngle;

    private void Reset()
    {
        _bits.Clear();
        _t = 0;
        _gustAt = 6 + _rnd.NextDouble() * 4;
        _umbFly = -1;
        int n = _kind switch { "winter" => 46, "spring" => 18, "autumn" => 16, _ => 0 };
        for (int i = 0; i < n; i++) _bits.Add(NewBit(true));
    }

    private Bit NewBit(bool anywhere)
    {
        double h = Math.Max(ActualHeight, 36);
        var b = new Bit
        {
            X = _rnd.NextDouble(),
            Y = anywhere ? _rnd.NextDouble() * h : -8 - _rnd.NextDouble() * 20,
            Phase = _rnd.NextDouble() * 6.28,
            Angle = _rnd.NextDouble() * 360,
            Spin = (_rnd.NextDouble() - 0.5) * 160,
            Hue = _rnd.NextDouble(),
        };
        switch (_kind)
        {
            case "winter":
                b.Size = 0.8 + _rnd.NextDouble() * 1.9;          // near flakes are bigger and faster
                b.Vy = 9 + b.Size * 9;
                b.Vx = -3 - _rnd.NextDouble() * 4;
                break;
            case "spring":
                b.Size = 3 + _rnd.NextDouble() * 2.5;
                b.Vy = 11 + _rnd.NextDouble() * 9;
                b.Vx = 8 + _rnd.NextDouble() * 10;
                break;
            case "autumn":
                b.Size = 4.5 + _rnd.NextDouble() * 3.5;
                b.Vy = 10 + _rnd.NextDouble() * 10;
                b.Vx = -4 + _rnd.NextDouble() * 8;
                break;
        }
        return b;
    }

    /// <summary>Advance the scene by dt seconds (called ~30 times a second while it's visible).</summary>
    public void Step(double dt)
    {
        if (_kind.Length == 0) return;
        dt = Math.Min(dt, 0.1);
        _t += dt;
        double w = ActualWidth, h = ActualHeight;
        if (w < 2 || h < 2) return;
        double floor = h - (Compact ? 1 : 4);
        for (int i = 0; i < _bits.Count; i++)
        {
            var b = _bits[i];
            if (b.Resting)
            {
                // an autumn leaf on the ground stays a while, then makes room
                b.Phase += dt;
                if (b.Phase > 9) _bits[i] = NewBit(false);
                continue;
            }
            double sway = Math.Sin(_t * (_kind == "winter" ? 1.3 : 1.8) + b.Phase);
            b.X += (b.Vx + sway * (_kind == "winter" ? 6 : 14)) * dt / w;
            b.Y += b.Vy * dt * (_kind == "autumn" ? 0.75 + 0.35 * Math.Abs(sway) : 1);
            b.Angle += b.Spin * dt * (0.6 + 0.4 * sway);
            if (_kind == "autumn" && !Compact && b.Y > floor - 3 && _rnd.NextDouble() < 0.5) { b.Y = floor - 2 - _rnd.NextDouble() * 3; b.Resting = true; b.Phase = 0; continue; }
            if (b.Y > h + 8 || b.X < -0.06 || b.X > 1.06) _bits[i] = NewBit(false);
        }

        if (_kind == "summer" && !Compact)
        {
            if (_umbFly < 0 && _t > _gustAt) { _umbFly = 0; }
            if (_umbFly >= 0)
            {
                _umbFly += dt;
                if (_umbFly > 6.5) { _umbFly = -1; _gustAt = _t + 9 + _rnd.NextDouble() * 6; }
            }
        }
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ drawing

    private static SolidColorBrush B(byte a, byte r, byte g, byte b2) { var x = new SolidColorBrush(Color.FromArgb(a, r, g, b2)); x.Freeze(); return x; }
    private static Pen P(Brush b, double t) { var p = new Pen(b, t) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; p.Freeze(); return p; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 2 || h < 2 || _kind.Length == 0) return;
        if (_bits.Count == 0 && _kind is "winter" or "spring" or "autumn") Reset();
        switch (_kind)
        {
            case "spring": DrawSpring(dc, w, h); break;
            case "summer": DrawSummer(dc, w, h); break;
            case "autumn": DrawAutumn(dc, w, h); break;
            case "winter": DrawWinter(dc, w, h); break;
        }
    }

    // ---- spring: flowers peeking from the bottom edge, petals drifting down

    private static readonly SolidColorBrush[] PetalColors =
        [B(0xE6, 0xFF, 0xB7, 0xD5), B(0xE6, 0xFF, 0xD6, 0xE7), B(0xDD, 0xFF, 0x8F, 0xBF), B(0xE6, 0xFF, 0xFF, 0xFF)];
    private static readonly SolidColorBrush[] FlowerColors =
        [B(0xFF, 0xFF, 0x7E, 0xB6), B(0xFF, 0xFF, 0xD1, 0x5C), B(0xFF, 0xB3, 0x9D, 0xFF), B(0xFF, 0xFF, 0xFF, 0xFF), B(0xFF, 0xFF, 0x9F, 0x6B)];
    private static readonly Pen Stem = P(B(0xFF, 0x5F, 0xB8, 0x6A), 1.6);
    private static readonly SolidColorBrush Leaf = B(0xFF, 0x4C, 0xA8, 0x5A);
    private static readonly SolidColorBrush FlowerHeart = B(0xFF, 0xFF, 0xD8, 0x4D);

    private void DrawSpring(DrawingContext dc, double w, double h)
    {
        // a soft green glow along the bottom: the meadow
        var meadow = new LinearGradientBrush(Color.FromArgb(0, 0x40, 0xA0, 0x50), Color.FromArgb(0x55, 0x40, 0xA0, 0x50), 90);
        dc.DrawRectangle(meadow, null, new Rect(0, h * (Compact ? 0.55 : 0.78), w, h * (Compact ? 0.45 : 0.22)));

        if (!Compact)
        {
            // flowers at both ends of the bottom edge (the middle is where the island's buttons are)
            double[] xs = [0.035, 0.075, 0.115, 0.16, 0.84, 0.885, 0.925, 0.965];
            for (int i = 0; i < xs.Length; i++)
            {
                double x = xs[i] * w, stem = 13 + (i * 7 % 5) * 3.2;
                double sway = Math.Sin(_t * 1.6 + i * 1.3) * 2.2;
                var top = new Point(x + sway, h - stem);
                dc.DrawLine(Stem, new Point(x, h + 2), top);
                // a leaf on the stem
                var leaf = new EllipseGeometry(new Point(x + (i % 2 == 0 ? 3 : -3) + sway * 0.4, h - stem * 0.4), 3.2, 1.5);
                dc.PushTransform(new RotateTransform(i % 2 == 0 ? -35 : 35, x, h - stem * 0.4));
                dc.DrawGeometry(Leaf, null, leaf);
                dc.Pop();
                var c = FlowerColors[i % FlowerColors.Length];
                double r = 3.2 + (i % 3) * 0.6;
                for (int k = 0; k < 5; k++)
                {
                    double a = k * Math.PI * 2 / 5 + _t * 0.15 + i;
                    dc.DrawEllipse(c, null, new Point(top.X + Math.Cos(a) * r, top.Y + Math.Sin(a) * r), r * 0.85, r * 0.85);
                }
                dc.DrawEllipse(FlowerHeart, null, top, r * 0.6, r * 0.6);
            }
        }
        int limit = Compact ? 7 : _bits.Count;
        for (int i = 0; i < limit && i < _bits.Count; i++)
        {
            var b = _bits[i];
            double px = b.X * w;
            dc.PushTransform(new RotateTransform(b.Angle, px, b.Y));
            dc.DrawEllipse(PetalColors[(int)(b.Hue * PetalColors.Length) % PetalColors.Length], null, new Point(px, b.Y), b.Size, b.Size * 0.55);
            dc.Pop();
        }
    }

    // ---- summer: the sea and the beach along the bottom, a sun, an umbrella the wind takes away

    private static readonly SolidColorBrush Sand = B(0xFF, 0xE8, 0xC9, 0x8A);
    private static readonly SolidColorBrush SandDark = B(0xFF, 0xD4, 0xAE, 0x6A);
    private static readonly SolidColorBrush Foam = B(0xD0, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush UmbRed = B(0xFF, 0xFF, 0x4D, 0x4D);
    private static readonly SolidColorBrush UmbWhite = B(0xFF, 0xFF, 0xF4, 0xE8);
    private static readonly Pen Pole = P(B(0xFF, 0xF2, 0xE6, 0xD0), 1.4);

    private void DrawSummer(DrawingContext dc, double w, double h)
    {
        double seaTop = Compact ? h - 7 : h - 26, sandTop = Compact ? h - 2.5 : h - 11;
        // the sun, low over the water on the left
        if (!Compact)
        {
            var glow = new RadialGradientBrush(Color.FromArgb(0x70, 0xFF, 0xC8, 0x5A), Color.FromArgb(0, 0xFF, 0xC8, 0x5A));
            dc.DrawEllipse(glow, null, new Point(w * 0.2, seaTop - 4), 46, 30);
            dc.DrawEllipse(B(0xFF, 0xFF, 0xD2, 0x6B), null, new Point(w * 0.2, seaTop - 3), 9, 9);
        }
        // the sea: two layers of waves
        for (int layer = 0; layer < 2; layer++)
        {
            double amp = Compact ? 1.2 : 2.2 - layer * 0.6, y0 = seaTop + layer * (Compact ? 2 : 6);
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(0, h + 2), true, true);
                for (double x = 0; x <= w + 6; x += 6)
                    c.LineTo(new Point(x, y0 + Math.Sin(x * 0.045 + _t * (1.6 + layer * 0.7) + layer * 2) * amp), true, true);
                c.LineTo(new Point(w + 6, h + 2), true, true);
            }
            g.Freeze();
            var sea = new LinearGradientBrush(layer == 0 ? Color.FromArgb(0xE0, 0x2E, 0xA8, 0xD8) : Color.FromArgb(0xF0, 0x16, 0x7F, 0xB8),
                                              Color.FromArgb(0xF0, 0x0B, 0x4F, 0x86), 90);
            dc.DrawGeometry(sea, null, g);
            // foam on the crest
            if (!Compact && layer == 1)
                for (int k = 0; k < 7; k++)
                {
                    double fx = ((k * 0.17 + _t * 0.03) % 1.0) * w;
                    dc.DrawEllipse(Foam, null, new Point(fx, y0 + Math.Sin(fx * 0.045 + _t * 2.3 + 2) * amp - 0.5), 5, 0.9);
                }
        }
        // the beach: a sandy shore from the right
        if (!Compact)
        {
            var shore = new StreamGeometry();
            using (var c = shore.Open())
            {
                c.BeginFigure(new Point(w * 0.5, h + 2), true, true);
                c.BezierTo(new Point(w * 0.62, sandTop + 2), new Point(w * 0.75, sandTop - 3), new Point(w + 2, sandTop - 4), true, true);
                c.LineTo(new Point(w + 2, h + 2), true, true);
            }
            shore.Freeze();
            dc.DrawGeometry(Sand, null, shore);
            for (int k = 0; k < 9; k++) dc.DrawEllipse(SandDark, null, new Point(w * (0.66 + k * 0.035), h - 3 - (k % 3) * 2), 0.9, 0.9);
            DrawUmbrella(dc, w, h, sandTop);
        }
        else
        {
            dc.DrawRectangle(Sand, null, new Rect(w * 0.7, sandTop, w * 0.3, h - sandTop + 1));
        }
    }

    private void DrawUmbrella(DrawingContext dc, double w, double h, double sandTop)
    {
        double baseX = w * 0.86, baseY = sandTop + 2;
        double sway = Math.Sin(_t * 1.7) * 4 + Math.Sin(_t * 3.1) * 1.5;   // the breeze
        double x = baseX, y = baseY, angle = sway, alpha = 1;
        if (_umbFly >= 0)
        {
            double f = _umbFly;
            if (f < 0.8) angle = sway - 22 * (f / 0.8) + Math.Sin(f * 30) * 3;            // the gust leans it over
            else if (f < 3.8)
            {
                // lifted and carried off to the left, tumbling
                double p = (f - 0.8) / 3.0;
                x = baseX - p * (w * 0.95);
                y = baseY - Math.Sin(p * Math.PI) * 26 - p * 6;
                angle = -22 - p * 340;
                alpha = p > 0.8 ? (1 - p) / 0.2 : 1;
            }
            else if (f < 4.6) alpha = 0;                                                  // gone…
            else
            {
                // …and a new one drops in and wobbles into the sand
                double p = Math.Min(1, (f - 4.6) / 0.7);
                y = baseY - (1 - p) * 40;
                angle = Math.Sin(p * 12) * (1 - p) * 18;
                alpha = p * 1.5;
            }
        }
        _umbX = x; _umbY = y; _umbAngle = angle;
        if (alpha <= 0.01) return;
        dc.PushOpacity(Math.Min(1, alpha));
        dc.PushTransform(new RotateTransform(angle, x, y));
        double poleH = 30, r = 15;
        dc.DrawLine(Pole, new Point(x, y), new Point(x, y - poleH));
        // the canopy: a dome of red and white segments
        var cx = x; var cy = y - poleH + 1;
        for (int k = 0; k < 4; k++)
        {
            double a0 = Math.PI + k * Math.PI / 4, a1 = Math.PI + (k + 1) * Math.PI / 4;
            var seg = new StreamGeometry();
            using (var c = seg.Open())
            {
                c.BeginFigure(new Point(cx, cy), true, true);
                c.LineTo(new Point(cx + Math.Cos(a0) * r, cy + Math.Sin(a0) * r * 0.75), true, true);
                c.ArcTo(new Point(cx + Math.Cos(a1) * r, cy + Math.Sin(a1) * r * 0.75), new Size(r, r * 0.75), 0, false, SweepDirection.Clockwise, true, true);
            }
            seg.Freeze();
            dc.DrawGeometry(k % 2 == 0 ? UmbRed : UmbWhite, null, seg);
        }
        dc.DrawEllipse(UmbRed, null, new Point(cx, cy - r * 0.75), 1.4, 1.4);
        dc.Pop();
        dc.Pop();
    }

    // ---- autumn: coloured leaves falling and settling, a warm glow, a cup of tea

    private static readonly SolidColorBrush[] LeafColors =
        [B(0xF0, 0xFF, 0x8A, 0x3D), B(0xF0, 0xE8, 0x45, 0x2C), B(0xF0, 0xFF, 0xC1, 0x3B), B(0xF0, 0xB8, 0x6B, 0x2E), B(0xF0, 0xD9, 0x5D, 0x1E)];
    private static readonly Pen LeafVein = P(B(0x70, 0x5A, 0x2A, 0x10), 0.6);
    private static readonly SolidColorBrush Cup = B(0xFF, 0xF4, 0xE9, 0xDA);
    private static readonly SolidColorBrush Tea = B(0xFF, 0x9A, 0x4E, 0x1C);
    private static readonly Pen CupHandle = P(Cup, 1.4);
    private static readonly Pen Steam = P(B(0x80, 0xFF, 0xFF, 0xFF), 1.1);
    private static Geometry? _leaf;

    private static Geometry LeafShape()
    {
        if (_leaf != null) return _leaf;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(-1, 0), true, true);
            c.BezierTo(new Point(-0.4, -0.75), new Point(0.5, -0.6), new Point(1, 0), true, true);
            c.BezierTo(new Point(0.5, 0.6), new Point(-0.4, 0.75), new Point(-1, 0), true, true);
        }
        g.Freeze();
        return _leaf = g;
    }

    private void DrawLeaf(DrawingContext dc, double x, double y, double size, double angle, double hue)
    {
        dc.PushTransform(new TranslateTransform(x, y));
        dc.PushTransform(new RotateTransform(angle));
        dc.PushTransform(new ScaleTransform(size, size * (0.75 + 0.25 * Math.Abs(Math.Cos(angle * 0.05)))));   // tumbling: seen from the side now and then
        dc.DrawGeometry(LeafColors[(int)(hue * LeafColors.Length) % LeafColors.Length], null, LeafShape());
        dc.Pop();
        dc.DrawLine(LeafVein, new Point(-size * 0.8 * Math.Cos(0), 0), new Point(size * 0.8, 0));
        dc.Pop();
        dc.Pop();
    }

    private void DrawAutumn(DrawingContext dc, double w, double h)
    {
        // cosy: warm light rising from the bottom
        var warm = new RadialGradientBrush(Color.FromArgb(0x55, 0xFF, 0x8A, 0x3D), Color.FromArgb(0, 0xFF, 0x8A, 0x3D))
        { Center = new Point(0.5, 1.1), GradientOrigin = new Point(0.5, 1.1), RadiusX = 0.7, RadiusY = Compact ? 1.2 : 0.55 };
        dc.DrawRectangle(warm, null, new Rect(0, 0, w, h));
        if (!Compact)
        {
            // a cup of tea in the corner, steaming
            double cx = w - 26, cy = h - 6;
            dc.DrawRoundedRectangle(Cup, null, new Rect(cx - 7, cy - 10, 14, 10), 2.5, 2.5);
            dc.DrawRectangle(Tea, null, new Rect(cx - 5.5, cy - 9, 11, 2));
            dc.DrawEllipse(null, CupHandle, new Point(cx + 8, cy - 5.5), 2.6, 3);
            for (int k = 0; k < 2; k++)
            {
                var s = new StreamGeometry();
                double ph = _t * 1.4 + k * 1.7, sx = cx - 2 + k * 4;
                using (var c = s.Open())
                {
                    c.BeginFigure(new Point(sx, cy - 12), false, false);
                    for (int j = 1; j <= 6; j++) c.LineTo(new Point(sx + Math.Sin(ph + j * 0.9) * 1.8, cy - 12 - j * 2.2), true, true);
                }
                s.Freeze();
                dc.PushOpacity(0.5 + 0.3 * Math.Sin(ph));
                dc.DrawGeometry(null, Steam, s);
                dc.Pop();
            }
        }
        int limit = Compact ? 6 : _bits.Count;
        for (int i = 0; i < limit && i < _bits.Count; i++)
        {
            var b = _bits[i];
            DrawLeaf(dc, b.X * w, b.Y, Compact ? b.Size * 0.7 : b.Size, b.Angle, b.Hue);
        }
    }

    // ---- winter: a frosty window at night, snow outside, a candle's warm light on the glass

    private static readonly Pen WindowFrame = P(B(0x55, 0xA0, 0x74, 0x48), 4);
    private static readonly SolidColorBrush Flake = B(0xF0, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush Drift = B(0xF5, 0xF4, 0xF8, 0xFF);
    private static readonly SolidColorBrush Wax = B(0xFF, 0xFF, 0xF1, 0xDC);
    private static readonly SolidColorBrush FlameOuter = B(0xFF, 0xFF, 0xA8, 0x3A);
    private static readonly SolidColorBrush FlameInner = B(0xFF, 0xFF, 0xF2, 0xB8);

    private void DrawWinter(DrawingContext dc, double w, double h)
    {
        // the candle's light flickers on the glass
        double flicker = 0.8 + 0.12 * Math.Sin(_t * 9.1) + 0.08 * Math.Sin(_t * 23.7 + 1);
        var light = new RadialGradientBrush(Color.FromArgb((byte)(0x80 * flicker), 0xFF, 0xA8, 0x4A), Color.FromArgb(0, 0xFF, 0xA8, 0x4A))
        { Center = new Point(0.08, 1.05), GradientOrigin = new Point(0.08, 1.05), RadiusX = Compact ? 0.35 : 0.45, RadiusY = Compact ? 1.6 : 0.9 };
        dc.DrawRectangle(light, null, new Rect(0, 0, w, h));

        // frost creeping in from the corners
        foreach (var (fx, fy) in new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0) })
        {
            var frost = new RadialGradientBrush(Color.FromArgb(0x3A, 0xE8, 0xF4, 0xFF), Color.FromArgb(0, 0xE8, 0xF4, 0xFF))
            { Center = new Point(fx, fy), GradientOrigin = new Point(fx, fy), RadiusX = Compact ? 0.15 : 0.22, RadiusY = Compact ? 0.9 : 0.4 };
            dc.DrawRectangle(frost, null, new Rect(0, 0, w, h));
        }

        // snow outside
        int limit = Compact ? 16 : _bits.Count;
        for (int i = 0; i < limit && i < _bits.Count; i++)
        {
            var b = _bits[i];
            dc.DrawEllipse(Flake, null, new Point(b.X * w, b.Y), b.Size * (Compact ? 0.8 : 1), b.Size * (Compact ? 0.8 : 1));
        }

        if (!Compact)
        {
            // the window frame: warm wood along the edge (no cross — it would run through the text)
            dc.DrawRoundedRectangle(null, WindowFrame, new Rect(2.5, 2.5, w - 5, h - 5), 26, 26);
            // snow piled on the sill
            var drift = new StreamGeometry();
            using (var c = drift.Open())
            {
                c.BeginFigure(new Point(0, h + 1), true, true);
                for (double x = 0; x <= w + 8; x += 8) c.LineTo(new Point(x, h - 4.5 - Math.Sin(x * 0.07) * 1.6 - Math.Sin(x * 0.023 + 1) * 1.4), true, true);
                c.LineTo(new Point(w + 8, h + 1), true, true);
            }
            drift.Freeze();
            dc.DrawGeometry(Drift, null, drift);
            // the candle on the sill
            double cx = 30, cy = h - 4;
            dc.DrawRoundedRectangle(Wax, null, new Rect(cx - 4.5, cy - 17, 9, 17), 2, 2);
            double fl = Math.Sin(_t * 11) * 0.9;
            var halo = new RadialGradientBrush(Color.FromArgb((byte)(0x90 * flicker), 0xFF, 0xC0, 0x60), Color.FromArgb(0, 0xFF, 0xC0, 0x60));
            dc.DrawEllipse(halo, null, new Point(cx, cy - 22), 14, 16);
            dc.DrawEllipse(FlameOuter, null, new Point(cx + fl * 0.4, cy - 22), 3, 5.4 + fl * 0.5);
            dc.DrawEllipse(FlameInner, null, new Point(cx + fl * 0.3, cy - 21), 1.4, 2.8);
        }
        else
        {
            var flameY = h - 6;
            dc.DrawEllipse(FlameOuter, null, new Point(12, flameY), 1.6, 2.8 + Math.Sin(_t * 11) * 0.3);
        }
    }
}

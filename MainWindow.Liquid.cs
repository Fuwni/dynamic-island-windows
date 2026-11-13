using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// "Liquid" physics: the island's outline ripples, it squashes / leans while dragged and wobbles back,
/// clicks spread a ripple, music makes the bottom edge drip with the beat, content appears with an iOS-like
/// spring + blur.
/// </summary>
public partial class MainWindow
{
    // jelly: scale & lean, underdamped so they wobble a couple of times like a water drop
    private readonly Spring _sx = new(1) { Stiffness = 420, Damping = 12 };
    private readonly Spring _sy = new(1) { Stiffness = 420, Damping = 12 };
    private readonly Spring _skew = new(0) { Stiffness = 300, Damping = 10 };

    // floating window position while / after dragging
    private readonly Spring _px = new(0) { Stiffness = 560, Damping = 38 };
    private readonly Spring _py = new(0) { Stiffness = 560, Damping = 38 };
    private bool _posActive;

    private double _ripple;       // amplitude (px) of the wave running around the edge
    private double _ripplePhase;
    private double _audioLevel;
    private double _dripPhase;

    private bool Liquid => _settings.LiquidWaves;

    // ---- reactive pill: the lower edge reaches for the cursor and breathes with the music ----
    private readonly Spring _reach = new(0) { Stiffness = 210, Damping = 17 };
    private double _reachX;
    private Color? _albumColor;

    private readonly Spring _lean = new(0) { Stiffness = 190, Damping = 16 };
    private Point _reachCursor;

    /// <summary>
    /// The pill feels the cursor: from below the lower edge swells towards it like a drop about to fall,
    /// from the side the whole island leans over a few pixels.
    /// </summary>
    private void UpdateReach(Native.POINT cursor)
    {
        double bump = 0, lean = 0;
        if (_settings.Reactive && _mode == Mode.Compact && !_dragging && !_hidden && Island.IsVisible)
        {
            Point tl;
            try { tl = Island.PointToScreen(new Point(0, 0)); }
            catch { return; }
            double x = (cursor.X - tl.X) / Dpi, y = (cursor.Y - tl.Y) / Dpi;
            double w = Island.ActualWidth, h = Island.ActualHeight;
            double dx = x < 0 ? x : x > w ? x - w : 0;      // horizontal distance outside the pill
            double dy = y > h ? y - h : y < 0 ? y : 0;       // vertical distance outside the pill
            double dist = Math.Sqrt(dx * dx + dy * dy);
            const double range = 170;
            if (dist > 0 && dist < range)
            {
                double k = 1 - dist / range;
                k = k * k * (3 - 2 * k);                     // smoothstep: soft start, firm end
                bump = k;                                    // 0…1: how hard the body flows to the cursor
                if (dx != 0) lean = Math.Sign(dx) * 4 * k;
                // where the cursor is, in island coordinates (smoothed so the wave glides)
                _reachCursor = new Point(_reachCursor.X + (x - _reachCursor.X) * 0.35, _reachCursor.Y + (y - _reachCursor.Y) * 0.35);
            }
        }
        bool changed = Math.Abs(_reach.Target - bump) > 0.05 || Math.Abs(_lean.Target - lean) > 0.05;
        _reach.Target = bump;
        _lean.Target = lean;
        if (changed) StartRendering();
    }

    /// <summary>For a split second the island splits into red / green / blue, like a misaligned print.</summary>
    private Views.ChromaticEffect? _chromatic;
    private bool _chromaticTried;

    private void PlayChromatic(double px = 7)
    {
        if (!_chromaticTried)
        {
            _chromaticTried = true;
            _chromatic = Views.ChromaticEffect.TryCreate();
        }
        if (_chromatic == null || IslandBody.ActualWidth < 10) return;

        double peak = px / Math.Max(200, IslandBody.ActualWidth);
        IslandBody.Effect = _chromatic;
        var anim = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(45)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(-peak * 0.35, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), new SineEase()));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        anim.Completed += (_, _) => { if (IslandBody.Effect == _chromatic) IslandBody.Effect = null; }; // back to crisp
        _chromatic.BeginAnimation(Views.ChromaticEffect.AmountProperty, anim);
    }

    /// <summary>A soft coloured flash around the island when something happens.</summary>
    private void PulseGlow(Color color)
    {
        if (!_settings.Reactive || IslandShadow is not DropShadowEffect fx) return;
        var dur = TimeSpan.FromMilliseconds(1300);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        fx.BeginAnimation(DropShadowEffect.ColorProperty, new ColorAnimation(color, Colors.Black, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        fx.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(1, fx.Opacity, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        fx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(52, 34, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        fx.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(0, 6, dur) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
    }

    /// <summary>The vivid colour of an album cover (for the equaliser, glow and character accents).</summary>
    private static Color? DominantColor(System.Windows.Media.Imaging.BitmapSource art)
    {
        try
        {
            double k = 12.0 / Math.Max(art.PixelWidth, art.PixelHeight);
            var small = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                new System.Windows.Media.Imaging.TransformedBitmap(art, new ScaleTransform(k, k)), PixelFormats.Bgra32, null, 0);
            int w = small.PixelWidth, h = small.PixelHeight;
            var px = new byte[w * h * 4];
            small.CopyPixels(px, w * 4, 0);
            double r = 0, g = 0, b = 0, total = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                double bb = px[i], gg = px[i + 1], rr = px[i + 2];
                double max = Math.Max(rr, Math.Max(gg, bb)), min = Math.Min(rr, Math.Min(gg, bb));
                double weight = 0.05 + (max - min) / 255.0 * (max / 255.0); // colourful, bright pixels count most
                r += rr * weight; g += gg * weight; b += bb * weight; total += weight;
            }
            if (total <= 0) return null;
            r /= total; g /= total; b /= total;
            double top = Math.Max(r, Math.Max(g, b));
            if (top < 1) return null;
            double boost = 235 / top; // bright enough to read on a black island
            return Color.FromRgb((byte)Math.Min(255, r * boost), (byte)Math.Min(255, g * boost), (byte)Math.Min(255, b * boost));
        }
        catch { return null; }
    }

    /// <summary>
    /// The island's outline. Floating: a rounded pill; docked: flat top with small "ears".
    /// <paramref name="bump"/> px of the lower edge bulge out around <paramref name="bumpX"/>.
    /// </summary>
    private static Geometry PillGeometry(double w, double h, double r, bool docked, double bumpX, double bump)
    {
        const double ear = 9;
        double half = Math.Min(40, Math.Max(6, (w - 2 * r) / 2));
        double bx = Math.Clamp(bumpX, r + half, Math.Max(r + half, w - r - half));
        bool hasBump = bump > 0.2 && w - 2 * r > 14;

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            if (docked)
            {
                c.BeginFigure(new Point(-ear, 0), true, true);
                c.QuadraticBezierTo(new Point(0, 0), new Point(0, ear), true, true);
                c.LineTo(new Point(0, h - r), true, true);
            }
            else
            {
                c.BeginFigure(new Point(r, 0), true, true);
                c.ArcTo(new Point(0, r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
                c.LineTo(new Point(0, h - r), true, true);
            }
            c.ArcTo(new Point(r, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            if (hasBump)
            {
                c.LineTo(new Point(bx - half, h), true, true);
                c.BezierTo(new Point(bx - half * 0.5, h), new Point(bx - half * 0.45, h + bump), new Point(bx, h + bump), true, true);
                c.BezierTo(new Point(bx + half * 0.45, h + bump), new Point(bx + half * 0.5, h), new Point(bx + half, h), true, true);
            }
            c.LineTo(new Point(w - r, h), true, true);
            c.ArcTo(new Point(w, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            if (docked)
            {
                c.LineTo(new Point(w, ear), true, true);
                c.QuadraticBezierTo(new Point(w, 0), new Point(w + ear, 0), true, true);
            }
            else
            {
                c.LineTo(new Point(w, r), true, true);
                c.ArcTo(new Point(w - r, 0), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            }
        }
        return g;
    }

    /// <summary>Send a wave around the island's edge.</summary>
    public void Kick(double px)
    {
        if (!Liquid) return;
        _ripple = Math.Min(7, _ripple + px);
        StartRendering();
    }

    private void Squish(double scale)
    {
        if (!Liquid) return;
        _sx.Target = _sy.Target = scale;
        StartRendering();
    }

    private void Unsquish(double bounce)
    {
        _sx.Target = _sy.Target = 1;
        if (Liquid)
        {
            _sx.Velocity += bounce;
            _sy.Velocity += bounce;
        }
        StartRendering();
    }

    /// <summary>Called every frame. Returns true while anything still moves.</summary>
    private bool StepLiquid(double dt)
    {
        _sx.Step(dt);
        _sy.Step(dt);
        _skew.Step(dt);
        IslandScale.ScaleX = _sx.Value;
        IslandScale.ScaleY = _sy.Value;
        IslandSkew.AngleX = _skew.Value;

        if (_posActive)
        {
            _px.Step(dt);
            _py.Step(dt);
            Left = _px.Value;
            Top = _py.Value;
            if (!_dragging && _px.IsSettled && _py.IsSettled)
            {
                _posActive = false;
                ClampPosition();
                SavePosition();
            }
        }

        bool playing = _lastMedia?.IsPlaying == true && !_hidden && !_fullscreenHidden;
        double peak = playing ? _audio.Peak : 0;
        _audioLevel += (peak - _audioLevel) * (peak > _audioLevel ? 0.45 : 0.08);
        if (!playing && _audioLevel < 0.005) _audioLevel = 0;

        _ripple *= Math.Exp(-dt * 2.3);
        if (_ripple < 0.03) _ripple = 0;
        _ripplePhase += dt * 7.5;
        _dripPhase += dt * (2.2 + _audioLevel * 5);

        _reach.Step(dt);
        _lean.Step(dt);
        IslandShift.X = _lean.Value;
        bool shapeMoving = Liquid && (_ripple > 0 || playing || _audioLevel > 0);
        bool reactive = _settings.Reactive && _mode == Mode.Compact &&
                        (playing || _audioLevel > 0 || !_reach.IsSettled || _reach.Value > 0.01 || !_lean.IsSettled);
        UpdateShape(shapeMoving);

        return shapeMoving || reactive || _posActive || !(_sx.IsSettled && _sy.IsSettled && _skew.IsSettled);
    }

    private double Radius(double h) => _settings.Docked
        ? Math.Min(h / 2, h <= 60 ? 15 : 28)
        : Math.Min(h / 2, 30);

    /// <summary>
    /// Notch shape: flat top flush with the screen edge, small concave "ears" that melt into it,
    /// rounded bottom corners.
    /// </summary>
    private static Geometry DockedGeometry(double w, double h, double r)
    {
        const double ear = 9;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(-ear, 0), true, true);
            c.QuadraticBezierTo(new Point(0, 0), new Point(0, ear), true, true);
            c.LineTo(new Point(0, h - r), true, true);
            c.ArcTo(new Point(r, h), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            c.LineTo(new Point(w - r, h), true, true);
            c.ArcTo(new Point(w, h - r), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, true);
            c.LineTo(new Point(w, ear), true, true);
            c.QuadraticBezierTo(new Point(w, 0), new Point(w + ear, 0), true, true);
        }
        return g;
    }

    private void UpdateShape(bool liquid)
    {
        double w = Island.Width, h = Island.Height;
        if (double.IsNaN(w) || double.IsNaN(h) || w < 2 || h < 2) return;
        double r = Radius(h);

        // reactive bulge of the lower edge: towards the cursor, or a gentle wandering swell with the music
        double bump = 0, bumpX = w / 2;
        if (_settings.Reactive && _mode == Mode.Compact)
        {
            if (_reach.Value > 0.01) bump = -1; // handled by FlowGeometry below
            else if (_audioLevel > 0.02) { bump = _audioLevel * 5.5; bumpX = w / 2 + Math.Sin(_dripPhase * 0.6) * w * 0.22; }
        }

        Geometry g = bump < 0 ? FlowGeometry(w, h, r, _settings.Docked, _reachCursor, _reach.Value)
                   : liquid && !_settings.Docked ? LiquidGeometry(w, h, r)
                   : PillGeometry(w, h, r, _settings.Docked, bumpX, bump);
        g.Freeze();
        IslandShape.Data = g;
        IslandShine.Data = g;
        ShapeChanged(g, w);
    }

    /// <summary>
    /// The whole outline flows towards the cursor like a wave: every point of the edge moves
    /// towards it, the nearer and the more it faces the cursor, the more. The top edge stays put.
    /// </summary>
    private static Geometry FlowGeometry(double w, double h, double r, bool docked, Point cursor, double strength)
    {
        var pts = new List<Point>(220);
        // one smooth field: the whole body leans towards the cursor, the side facing it swells most
        var centre = new Point(w / 2, h / 2);
        double gx = cursor.X - centre.X, gy = cursor.Y - centre.Y;
        double glen = Math.Max(1, Math.Sqrt(gx * gx + gy * gy));
        gx /= glen; gy /= glen;
        double reach = 18 * strength, spread = 60 + w * 0.45;

        void Add(double x, double y, double nx, double ny)
        {
            double dx = cursor.X - x, dy = cursor.Y - y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            double facing = Math.Max(0, nx * gx + ny * gy);
            facing = facing * facing * (3 - 2 * facing);                  // smoothstep: no hard edges
            double hold = Math.Pow(Math.Clamp(y / h, 0, 1), 0.8);        // anchored at the top
            double near = 1 / (1 + (dist / spread) * (dist / spread));    // gentle bell, no cusp
            double amount = reach * (0.12 + 0.88 * facing) * near * hold;
            pts.Add(new Point(x + gx * amount, y + gy * amount));
        }

        void Arc(double cx, double cy, double fromDeg, double toDeg)
        {
            int n = Math.Max(5, (int)(r * Math.PI / 2 / 3));
            for (int i = 1; i <= n; i++)
            {
                double a = (fromDeg + (toDeg - fromDeg) * i / n) * Math.PI / 180;
                double nx = Math.Cos(a), ny = Math.Sin(a);
                Add(cx + r * nx, cy + r * ny, nx, ny);
            }
        }

        const double step = 4, ear = 9;
        if (docked)
        {
            pts.Add(new Point(-ear, 0));
            for (int i = 1; i <= 6; i++) { double t = i / 6.0; pts.Add(new Point(-ear + ear * (1 - (1 - t) * (1 - t)), ear * t * t)); }
            for (double y = ear + step; y < h - r; y += step) Add(0, y, -1, 0);
        }
        else
        {
            pts.Add(new Point(r, 0));
            Arc(r, r, 270, 180);
            for (double y = r + step; y < h - r; y += step) Add(0, y, -1, 0);
        }
        Arc(r, h - r, 180, 90);
        for (double x = r + step; x < w - r; x += step) Add(x, h, 0, 1);
        Arc(w - r, h - r, 90, 0);
        if (docked)
        {
            for (double y = h - r - step; y > ear; y -= step) Add(w, y, 1, 0);
            for (int i = 0; i <= 6; i++) { double t = i / 6.0; pts.Add(new Point(w + ear * t * t, ear * (1 - t) * (1 - t) * 1 + 0)); }
        }
        else
        {
            for (double y = h - r - step; y > r; y -= step) Add(w, y, 1, 0);
            Arc(w - r, r, 0, -90);
            for (double x = w - r - step; x > r; x -= step) Add(x, 0, 0, -1);
        }

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true, true);
            c.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        return g;
    }

    private Geometry LiquidGeometry(double W, double H, double R)
    {
        var pts = new List<Point>(260);
        double L = 2 * (W - 2 * R) + 2 * (H - 2 * R) + 2 * Math.PI * R;
        int waves = Math.Max(3, (int)Math.Round(L / 150));
        bool playing = _lastMedia?.IsPlaying == true;
        double drip = playing ? 1.2 + _audioLevel * 9 : _audioLevel * 9;
        double s = 0;

        void Add(double x, double y, double nx, double ny, double ds, double bottomT = -1)
        {
            s += ds;
            double weight = 0.3 + 0.7 * (y / H); // it hangs from the top, the bottom moves most
            double d = _ripple * weight * Math.Sin(2 * Math.PI * waves * s / L - _ripplePhase);
            if (bottomT >= 0 && drip > 0)
            {
                double env = Math.Sin(Math.PI * bottomT);
                d += Math.Max(0, drip * env * (0.55 + 0.45 * Math.Sin(bottomT * 9 + _dripPhase) * Math.Cos(bottomT * 4 - _dripPhase * 0.7)));
            }
            pts.Add(new Point(x + nx * d, y + ny * d));
        }

        void Arc(double cx, double cy, double fromDeg, double toDeg)
        {
            int n = Math.Max(4, (int)(R * Math.PI / 2 / 4));
            double step = (toDeg - fromDeg) / n * Math.PI / 180;
            for (int i = 1; i <= n; i++)
            {
                double a = fromDeg * Math.PI / 180 + step * i;
                double nx = Math.Cos(a), ny = Math.Sin(a);
                Add(cx + R * nx, cy + R * ny, nx, ny, R * Math.Abs(step));
            }
        }

        const double seg = 5;
        pts.Add(new Point(R, 0));
        for (double x = R + seg; x < W - R; x += seg) Add(x, 0, 0, -1, seg);               // top
        Arc(W - R, R, -90, 0);                                                            // top-right
        for (double y = R + seg; y < H - R; y += seg) Add(W, y, 1, 0, seg);               // right
        Arc(W - R, H - R, 0, 90);                                                         // bottom-right
        double bw = Math.Max(1, W - 2 * R);
        for (double x = W - R - seg; x > R; x -= seg) Add(x, H, 0, 1, seg, (W - R - x) / bw); // bottom (drips)
        Arc(R, H - R, 90, 180);                                                           // bottom-left
        for (double y = H - R - seg; y > R; y -= seg) Add(0, y, -1, 0, seg);              // left
        Arc(R, R, 180, 270);                                                              // top-left

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], true, true);
            ctx.PolyLineTo(pts.Skip(1).ToList(), true, true);
        }
        return g;
    }

    // ---------------------------------------------------------------- click ripple

    private void SpawnRipple(Point p)
    {
        if (!Liquid || RippleLayer.ActualWidth < 1) return;
        double max = Math.Max(Island.ActualWidth, Island.ActualHeight) * 0.85;
        var tint = _settings.DarkTheme ? Colors.White : Colors.Black;
        var fill = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x00, tint.R, tint.G, tint.B), 0.0),
                new GradientStop(Color.FromArgb(0x16, tint.R, tint.G, tint.B), 0.72),
                new GradientStop(Color.FromArgb(0x30, tint.R, tint.G, tint.B), 0.93),
                new GradientStop(Color.FromArgb(0x00, tint.R, tint.G, tint.B), 1.0),
            },
        };
        var scale = new ScaleTransform(0.04, 0.04);
        var e = new Ellipse
        {
            Width = max * 2, Height = max * 2, Fill = fill, IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale,
        };
        System.Windows.Controls.Canvas.SetLeft(e, p.X - max);
        System.Windows.Controls.Canvas.SetTop(e, p.Y - max);
        RippleLayer.Children.Add(e);

        var dur = TimeSpan.FromMilliseconds(700);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.04, 1, dur) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.04, 1, dur) { EasingFunction = ease });
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(560)) { BeginTime = TimeSpan.FromMilliseconds(160) };
        fade.Completed += (_, _) => RippleLayer.Children.Remove(e);
        e.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------------------------------------------------------- iOS-like transitions

    /// <summary>Appear: spring scale-up from <paramref name="fromScale"/>, optional slide, un-blur, fade in.</summary>
    private void Reveal(FrameworkElement el, double fromScale = 0.94, double slideX = 0, int delayMs = 0)
    {
        el.Visibility = Visibility.Visible;
        var st = new ScaleTransform(fromScale, fromScale);
        var tt = new TranslateTransform(slideX, 0);
        el.RenderTransformOrigin = new Point(0.5, 0);
        el.RenderTransform = new TransformGroup { Children = { st, tt } };

        var begin = TimeSpan.FromMilliseconds(delayMs);
        bool hop = _settings.Bouncy;
        var spring = new SpringEase { DampingRatio = Liquid ? 0.68 : hop ? 0.7 : 0.86 };
        var move = TimeSpan.FromMilliseconds((Liquid ? 520 : hop ? 500 : 420) * (_settings.FastAnimations ? 0.65 : 1));
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(fromScale, 1, move) { BeginTime = begin, EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(fromScale, 1, move) { BeginTime = begin, EasingFunction = spring });
        if (slideX != 0)
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(slideX, 0, move) { BeginTime = begin, EasingFunction = spring });
        if (hop)  // a small jump: drops in from a few pixels above and settles on a spring
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-4, 0, move) { BeginTime = begin, EasingFunction = spring });

        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
        {
            BeginTime = begin,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

        // content comes into focus: soft first, sharp only at rest
        var blur = new BlurEffect { Radius = 18, RenderingBias = RenderingBias.Performance };
        el.Effect = blur;
        var unblur = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(380))
        {
            BeginTime = begin,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        unblur.Completed += (_, _) => { if (el.Effect == blur) el.Effect = null; };
        blur.BeginAnimation(BlurEffect.RadiusProperty, unblur);
    }

    /// <summary>Disappear: quick fade with a little blur and shrink.</summary>
    private void Conceal(FrameworkElement el, Action? done = null)
    {
        var dur = TimeSpan.FromMilliseconds(160);
        var fade = new DoubleAnimation(0, dur);
        if (done != null) fade.Completed += (_, _) => done();
        el.BeginAnimation(OpacityProperty, fade);

        if (el.RenderTransform is TransformGroup { Children.Count: > 0 } group && group.Children[0] is ScaleTransform st && !st.IsFrozen)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, dur));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, dur));
        }
        var blur = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
        el.Effect = blur;
        blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, 14, dur));
    }
}

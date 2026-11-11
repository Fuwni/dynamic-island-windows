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

        bool shapeMoving = Liquid && (_ripple > 0 || playing || _audioLevel > 0);
        UpdateShape(shapeMoving);

        return shapeMoving || _posActive || !(_sx.IsSettled && _sy.IsSettled && _skew.IsSettled);
    }

    private double Radius(double h) => Math.Min(h / 2, 30);

    private void UpdateShape(bool liquid)
    {
        double w = Island.Width, h = Island.Height;
        if (double.IsNaN(w) || double.IsNaN(h) || w < 2 || h < 2) return;
        double r = Radius(h);

        Geometry g = liquid ? LiquidGeometry(w, h, r) : new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        g.Freeze();
        IslandShape.Data = g;
        IslandShine.Data = g;
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
        var spring = new SpringEase { DampingRatio = Liquid ? 0.68 : 0.92 }; // calm = no visible bounce
        var move = TimeSpan.FromMilliseconds(Liquid ? 520 : 380);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(fromScale, 1, move) { BeginTime = begin, EasingFunction = spring });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(fromScale, 1, move) { BeginTime = begin, EasingFunction = spring });
        if (slideX != 0)
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(slideX, 0, move) { BeginTime = begin, EasingFunction = spring });

        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            BeginTime = begin,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

        if (Liquid)
        {
            var blur = new BlurEffect { Radius = 12, RenderingBias = RenderingBias.Performance };
            el.Effect = blur;
            var unblur = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(300)) { BeginTime = begin };
            unblur.Completed += (_, _) => { if (el.Effect == blur) el.Effect = null; };
            blur.BeginAnimation(BlurEffect.RadiusProperty, unblur);
        }
    }

    /// <summary>Disappear: quick fade with a little blur and shrink.</summary>
    private void Conceal(FrameworkElement el, Action? done = null)
    {
        var dur = TimeSpan.FromMilliseconds(130);
        var fade = new DoubleAnimation(0, dur);
        if (done != null) fade.Completed += (_, _) => done();
        el.BeginAnimation(OpacityProperty, fade);

        if (el.RenderTransform is TransformGroup { Children.Count: > 0 } group && group.Children[0] is ScaleTransform st && !st.IsFrozen)
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, dur));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, dur));
        }
        if (Liquid)
        {
            var blur = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
            el.Effect = blur;
            blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, 8, dur));
        }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

public enum CharacterMood { Idle, Music, Happy, Think, Sleep, Typing }

/// <summary>The easter egg: poke a character and it shows one of these.</summary>
public enum Emotion { None, Love, Angry, Surprised, Sad, Dizzy, Laugh, Cool }

/// <summary>
/// The little creature that lives in the pill. Everything is drawn and animated in code (no image files):
/// it watches the cursor, dances to the music, cheers when something finishes, thinks while an AI agent
/// waits for you and falls asleep when you are away.
/// </summary>
public sealed class CharacterView : FrameworkElement
{
    public static readonly (string Id, string Name)[] All =
    [
        ("eyes", L.T("Глазки")), ("cat", L.T("Кот")), ("ghost", L.T("Призрак")), ("robot", L.T("Робот")),
        ("fish", L.T("Рыбка")), ("planet", L.T("Планета")), ("slime", L.T("Слайм")), ("pulse", L.T("Пульс")),
        ("bunny", L.T("Зайчик")), ("owl", L.T("Сова")), ("dino", L.T("Дино")), ("star", L.T("Звёздочка")),
        ("cloud", L.T("Облачко")), ("claude", "Claude"), ("clawd", "Claude Code"),
    ];

    public string Kind { get; set; } = "eyes";
    public CharacterMood Mood { get; set; } = CharacterMood.Idle;
    /// <summary>Where it looks: -1..1 on both axes.</summary>
    public double LookX { get; set; }
    public double LookY { get; set; }
    /// <summary>Loudness of the music right now, 0..1.</summary>
    public double Beat { get; set; }
    /// <summary>Tint for accents (album colour); null = the app accent.</summary>
    public Color? Tint { get; set; }
    /// <summary>Hand-drawn look: held frames (12 fps), ink outlines and a gentle line boil.</summary>
    public bool Sketch { get; set; }

    private double _t;
    private double _nextBlink = 2;
    private double _blink;           // 0 open … 1 closed
    private double _happy;           // seconds of "happy" left
    private readonly Spring _jump = new(0) { Stiffness = 260, Damping = 9 };
    private double _lookX, _lookY, _beat;
    private readonly Random _rng = new();

    private Emotion _emote, _emoNow;
    private double _emoteLeft;
    private const double EmoteTime = 2.4;

    /// <summary>Show an emotion for a couple of seconds (a random one, never the same twice in a row).</summary>
    public Emotion Emote(Emotion? which = null)
    {
        var e = which ?? Emotion.None;
        while (e == Emotion.None || (which == null && e == _emote))
            e = (Emotion)_rng.Next(1, 8);
        _emote = e;
        _emoteLeft = EmoteTime;
        Hop(e == Emotion.Surprised ? 1.2 : 0.6);
        _real += 0.0001;          // make sure the next tick redraws even in hand-drawn mode
        _frame = -1;
        return e;
    }

    public CharacterView()
    {
        Width = 30;
        Height = 22;
        IsHitTestVisible = false;
    }

    /// <summary>A happy hop: call when something good happens.</summary>
    public void Cheer()
    {
        _jump.Velocity -= 70;
        _happy = 1.3;
    }

    private double _real;
    private double _sinceDraw, _drawnLookX, _drawnLookY;
    private int _frame = -1;

    /// <summary>Performance test only: stop drawing.</summary>
    public static bool PerfFreeze;

    public void Tick(double dt)
    {
        if (PerfFreeze) return;
        _real += dt;
        // hand-drawn mode animates "on twos": the picture changes 12 times a second, not 30
        double t = Sketch ? Math.Floor(_real * 12) / 12 : _real;
        bool changed = t != _t;
        _t = t;
        _jump.Step(dt);
        if (_happy > 0) _happy -= dt;
        if (_emoteLeft > 0) _emoteLeft -= dt;

        // smooth so nothing snaps
        _lookX += (LookX - _lookX) * Math.Min(1, dt * 12);
        _lookY += (LookY - _lookY) * Math.Min(1, dt * 12);
        _beat += (Beat - _beat) * Math.Min(1, dt * (Beat > _beat ? 30 : 6));

        // blinking
        if (_t > _nextBlink)
        {
            _blink = 1;
            _nextBlink = _t + 2.2 + _rng.NextDouble() * 4;
        }
        else _blink = Math.Max(0, _blink - dt * 9);

        int frame = (int)Math.Floor(_real * 12);
        if (Sketch && frame == _frame && !changed) return;   // hold the drawing
        _frame = frame;

        // a calm character breathes slowly: 15 redraws a second are enough, 8 while asleep.
        // Anything lively (an emotion, a jump, music, the eyes following the cursor) gets the full 30.
        _sinceDraw += dt;
        bool lively = _emoteLeft > 0 || _happy > 0 || !_jump.IsSettled || _blink > 0 || Mood == CharacterMood.Music
                      || Math.Abs(_lookX - _drawnLookX) > 0.015 || Math.Abs(_lookY - _drawnLookY) > 0.015 || _beat > 0.02;
        double interval = lively ? 0 : Mood == CharacterMood.Sleep ? 1 / 8.0 : 1 / 15.0;
        if (_sinceDraw + 1e-6 < interval) return;
        _sinceDraw = 0;
        _drawnLookX = _lookX;
        _drawnLookY = _lookY;
        InvalidateVisual();
    }

    // ------------------------------------------------------------------ drawing

    private Brush _fg = Brushes.White, _dark = Brushes.Black, _accent = Brushes.LimeGreen;

    protected override void OnRender(DrawingContext dc)
    {
        _fg = TryFindResource("Fg") as Brush ?? Brushes.White;
        _dark = TryFindResource("PlayFg") as Brush ?? Brushes.Black;
        _accent = Tint is Color c ? new SolidColorBrush(c) : TryFindResource("Accent") as Brush ?? Brushes.LimeGreen;

        _emoNow = _emoteLeft > 0 ? _emote : Emotion.None;
        var mood = _happy > 0 || _emoNow == Emotion.Laugh ? CharacterMood.Happy : Mood;
        if (_emoNow is not (Emotion.None or Emotion.Laugh) && mood is CharacterMood.Sleep or CharacterMood.Think) mood = CharacterMood.Idle;
        double w = ActualWidth, h = ActualHeight, cx = w / 2, cy = h / 2;
        double et = EmoteTime - _emoteLeft;     // seconds into the emotion

        // how the whole body moves with the emotion
        if (_emoNow != Emotion.None)
        {
            double rot = 0, sx = 1, sy = 1, dx = 0, dy = 0;
            switch (_emoNow)
            {
                case Emotion.Angry: dx = Math.Sin(_t * 55) * 0.9; break;                       // trembling
                case Emotion.Dizzy: rot = Math.Sin(_t * 5) * 9; break;                          // swaying
                case Emotion.Laugh: dy = -Math.Abs(Math.Sin(_t * 16)) * 1.6; break;             // giggling
                case Emotion.Surprised: { double k = Math.Exp(-et * 6); sy = 1 + 0.25 * k; sx = 1 - 0.12 * k; break; }
                case Emotion.Sad: dy = 1.2; sy = 0.95; break;                                   // droops
                case Emotion.Love: sx = sy = 1 + Math.Sin(_t * 8) * 0.04; break;                // heart beat
                case Emotion.Cool: dy = Math.Sin(_t * 4) > 0.8 ? 0.6 : 0; break;                // nodding along
            }
            dc.PushTransform(new TransformGroup
            {
                Children = { new ScaleTransform(sx, sy, cx, h * 0.8), new RotateTransform(rot, cx, cy), new TranslateTransform(dx, dy) },
            });
        }

        // shared body motion: hop, dance, breathe, slow float while asleep
        double dance = mood == CharacterMood.Music ? Math.Abs(Math.Sin(_t * 7)) * (1.2 + _beat * 3.2) : 0;
        double breathe = Math.Sin(_t * (mood == CharacterMood.Sleep ? 1.2 : 2.2)) * 0.5;
        double y = cy + _jump.Value * 0.1 - dance + breathe;

        // the line boil: each new drawing sits a hair off the last one, like retraced frames
        bool boil = Sketch && Kind is not ("claude" or "clawd");
        if (boil)
        {
            int k = _frame % 3;
            dc.PushTransform(new TransformGroup
            {
                Children =
                {
                    new RotateTransform(Noise(k, 1) * 2.2, cx, cy),
                    new TranslateTransform(Noise(k, 2) * 0.45, Noise(k, 3) * 0.45),
                },
            });
        }

        switch (Kind)
        {
            case "cat": DrawCat(dc, cx, y, mood); break;
            case "ghost": DrawGhost(dc, cx, y, mood); break;
            case "robot": DrawRobot(dc, cx, y, mood); break;
            case "fish": DrawFish(dc, cx, cy, mood); break;
            case "planet": DrawPlanet(dc, cx, y, mood); break;
            case "slime": DrawSlime(dc, cx, cy, mood); break;
            case "pulse": DrawPulse(dc, w, cy, mood); break;
            case "bunny": DrawBunny(dc, cx, y, mood); break;
            case "owl": DrawOwl(dc, cx, y, mood); break;
            case "dino": DrawDino(dc, cx, y, mood); break;
            case "star": DrawStar(dc, cx, y, mood); break;
            case "cloud": DrawCloud(dc, cx, y, mood); break;
            case "claude": DrawClaude(dc, cx, cy, mood); break;
            case "clawd": DrawClawd(dc, cx, cy, mood); break;
            default:
                // big round eyes
                Eye(dc, cx - 6.4, y - 0.5, 5.9, 6.5, mood, _fg, _dark);
                Eye(dc, cx + 6.4, y - 0.5, 5.9, 6.5, mood, _fg, _dark);
                break;
        }

        if (boil) dc.Pop();
        if (_emoNow != Emotion.None)
        {
            if (_emoNow == Emotion.Cool && Kind != "clawd") Shades(dc, cx, y - 0.5, et);
            dc.Pop();
            DrawEmotion(dc, w, h, cx, cy, et);
        }
        if (mood == CharacterMood.Sleep) DrawZ(dc, w - 5, 5);
        if (mood == CharacterMood.Think) DrawDots(dc, w - 8, 3);
    }

    /// <summary>One eye: white with a pupil that follows the cursor; closed, happy (^) and sleepy variants.</summary>
    private void Eye(DrawingContext dc, double x, double y, double rx, double ry, CharacterMood mood, Brush white, Brush pupil, bool small = false)
    {
        switch (_emoNow)
        {
            case Emotion.Love:
                dc.DrawGeometry(LoveBrush, null, HeartGeometry(x, y + 0.4, rx * 0.95));
                return;
            case Emotion.Dizzy:
                Spiral(dc, x, y, Math.Max(rx, ry) * 0.95, white, small ? 0.9 : 1.2);
                return;
            case Emotion.Surprised:
                dc.DrawEllipse(white, null, new Point(x, y), rx * 1.05, ry * 1.12);
                dc.DrawEllipse(pupil, null, new Point(x, y), rx * 0.3, rx * 0.3);
                return;
            case Emotion.Angry:
            case Emotion.Sad:
            {
                bool angry = _emoNow == Emotion.Angry;
                double side = x < ActualWidth / 2 ? -1 : 1;                // inner corner points to the middle
                dc.DrawEllipse(white, null, new Point(x, y + (angry ? 0.6 : 0.3)), rx, ry * (angry ? 0.8 : 0.9));
                double prr = rx * 0.6;
                dc.DrawEllipse(pupil, null, new Point(x - side * (angry ? 0.8 : 0), y + (angry ? 1 : ry * 0.35)), prr, prr);
                var brow = new Pen(white, small ? 1.1 : 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                double inner = angry ? y - ry * 0.55 : y - ry * 1.3, outer = angry ? y - ry * 1.3 : y - ry * 0.75;
                dc.DrawLine(brow, new Point(x - side * rx * 1.05, inner), new Point(x + side * rx * 0.95, outer));
                return;
            }
        }
        if (mood == CharacterMood.Sleep)
        {
            // closed, relaxed arc
            var pen = new Pen(white, small ? 1.2 : 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, Arc(x - rx * 0.8, y + 1, x + rx * 0.8, y + 1, rx * 0.5));
            return;
        }
        if (mood == CharacterMood.Happy)
        {
            // ^ ^
            var pen = new Pen(white, small ? 1.3 : 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, Arc(x - rx * 0.8, y + 1.5, x + rx * 0.8, y + 1.5, -rx * 0.9));
            return;
        }

        double open = 1 - _blink;
        double lookY = mood == CharacterMood.Think ? -0.9 : _lookY, lookX = mood == CharacterMood.Think ? 0.5 : _lookX;
        // a blink squishes the eye flat and a touch wider - softer than a hard cut
        dc.DrawEllipse(white, null, new Point(x, y + (1 - open) * ry * 0.35), rx * (1 + (1 - open) * 0.12), Math.Max(0.6, ry * open));
        if (open < 0.4) return;
        double pr = small ? rx * 0.66 : rx * 0.64;                       // big pupils read as cute
        var p = new Point(x + lookX * (rx - pr) * 0.9, y + lookY * (ry - pr) * 0.85);
        dc.DrawEllipse(pupil, null, p, pr, pr * Math.Min(1, open + 0.2));
        // two sparkles: a big one up-right, a tiny one low-left
        var shine = Brushes.White;
        dc.DrawEllipse(shine, null, new Point(p.X + pr * 0.32, p.Y - pr * 0.36), pr * (small ? 0.34 : 0.36), pr * (small ? 0.34 : 0.36));
        if (!small) dc.DrawEllipse(shine, null, new Point(p.X - pr * 0.38, p.Y + pr * 0.38), pr * 0.15, pr * 0.15);
    }

    private static readonly Brush BlushBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0x7A, 0x9C)));

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private void Blush(DrawingContext dc, double x, double y, double r)
    {
        double a = Mood == CharacterMood.Sleep ? 0.5 : _happy > 0 ? 1 : 0.8;
        dc.PushOpacity(a);
        dc.DrawEllipse(BlushBrush, null, new Point(x, y), r, r * 0.62);
        dc.Pop();
    }

    /// <summary>A tiny mouth: a smile, a little "o" while thinking, wider when happy.</summary>
    private void Mouth(DrawingContext dc, double x, double y, CharacterMood mood, double size, Brush brush)
    {
        var pen = new Pen(brush, 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        switch (mood)
        {
            case CharacterMood.Think:
                dc.DrawEllipse(null, pen, new Point(x, y), size * 0.6, size * 0.7);
                break;
            case CharacterMood.Sleep:
                dc.DrawLine(pen, new Point(x - size * 0.6, y), new Point(x + size * 0.6, y));
                break;
            case CharacterMood.Music:
                dc.DrawEllipse(brush, null, new Point(x, y), size * 0.7, size * (0.4 + _beat * 0.8));
                break;
            default:
                double wide = _happy > 0 ? 1.6 : 1;
                dc.DrawGeometry(null, pen, Arc(x - size * wide, y - 0.4, x + size * wide, y - 0.4, size * 0.55 * wide));
                break;
        }
    }

    private static Geometry Arc(double x1, double y1, double x2, double y2, double bulge)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x1, y1), false, false);
            c.QuadraticBezierTo(new Point((x1 + x2) / 2, y1 + bulge * 2), new Point(x2, y2), true, true);
        }
        return g;
    }

    private void DrawCat(DrawingContext dc, double cx, double y, CharacterMood mood)
    {
        // ears twitch now and then
        double twitch = Math.Max(0, Math.Sin(_t * 1.3 + 1)) > 0.97 ? 1.2 : 0;
        var ears = new StreamGeometry();
        using (var c = ears.Open())
        {
            c.BeginFigure(new Point(cx - 9, y - 2), true, true);
            c.LineTo(new Point(cx - 8.5, y - 10 - twitch), true, true);
            c.LineTo(new Point(cx - 2.5, y - 6), true, true);
            c.BeginFigure(new Point(cx + 9, y - 2), true, true);
            c.LineTo(new Point(cx + 8.5, y - 10), true, true);
            c.LineTo(new Point(cx + 2.5, y - 6), true, true);
        }
        dc.DrawGeometry(_fg, null, ears);
        dc.DrawEllipse(_fg, null, new Point(cx, y + 1), 10, 7.6);
        Eye(dc, cx - 4.2, y, 2.6, 3.0, mood, _dark, _fg, small: true);
        Eye(dc, cx + 4.2, y, 2.6, 3.0, mood, _dark, _fg, small: true);
        // nose and whiskers
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0x8F, 0xA3)), null, new Point(cx, y + 3.4), 1.1, 0.8);
        var wp = new Pen(_dark, 0.6);
        dc.DrawLine(wp, new Point(cx - 9.5, y + 3), new Point(cx - 5.5, y + 3.6));
        dc.DrawLine(wp, new Point(cx + 9.5, y + 3), new Point(cx + 5.5, y + 3.6));
    }

    private void DrawGhost(DrawingContext dc, double cx, double y, CharacterMood mood)
    {
        y += Math.Sin(_t * 2.4) * 1.2; // floats
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double l = cx - 8, r = cx + 8, top = y - 9, bottom = y + 8;
            c.BeginFigure(new Point(l, bottom), true, true);
            c.LineTo(new Point(l, top + 8), true, true);
            c.ArcTo(new Point(r, top + 8), new Size(8, 8), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(r, bottom), true, true);
            // wavy hem that keeps moving
            for (int i = 0; i <= 8; i++)
            {
                double x = r - (r - l) * i / 8.0;
                c.LineTo(new Point(x, bottom + Math.Sin(i * 1.6 + _t * 6) * 1.5 - 1.5), true, true);
            }
        }
        dc.PushOpacity(0.94);
        dc.DrawGeometry(_fg, null, g);
        dc.Pop();
        Eye(dc, cx - 3.4, y - 2, 2.4, 3.1, mood, _dark, _fg, small: true);
        Eye(dc, cx + 3.4, y - 2, 2.4, 3.1, mood, _dark, _fg, small: true);
        if (mood == CharacterMood.Music) dc.DrawEllipse(_dark, null, new Point(cx, y + 3), 1.6, 1 + _beat * 2);
    }

    private void DrawRobot(DrawingContext dc, double cx, double y, CharacterMood mood)
    {
        // antenna with a light that blinks (faster with music)
        dc.DrawLine(new Pen(_fg, 1.2), new Point(cx, y - 7), new Point(cx, y - 10));
        bool on = Math.Sin(_t * (mood == CharacterMood.Music ? 12 : 3)) > 0 && mood != CharacterMood.Sleep;
        dc.DrawEllipse(on ? _accent : _fg, null, new Point(cx, y - 10.5), 1.6, 1.6);

        dc.DrawRoundedRectangle(_fg, null, new Rect(cx - 10, y - 7, 20, 15), 4.5, 4.5);
        dc.DrawRoundedRectangle(_dark, null, new Rect(cx - 8, y - 5, 16, 8), 2.5, 2.5);

        // LED eyes on the dark visor
        if (mood == CharacterMood.Sleep)
        {
            var pen = new Pen(_accent, 1.2);
            dc.DrawLine(pen, new Point(cx - 5.5, y - 1), new Point(cx - 2, y - 1));
            dc.DrawLine(pen, new Point(cx + 2, y - 1), new Point(cx + 5.5, y - 1));
        }
        else
        {
            double eh = mood == CharacterMood.Happy ? 1.2 : 3.6 * (1 - _blink);
            double ox = _lookX * 1.3, oy = _lookY * 0.8;
            dc.DrawRoundedRectangle(_accent, null, new Rect(cx - 5.6 + ox, y - 1 - eh / 2 + oy, 3.2, Math.Max(0.8, eh)), 1, 1);
            dc.DrawRoundedRectangle(_accent, null, new Rect(cx + 2.4 + ox, y - 1 - eh / 2 + oy, 3.2, Math.Max(0.8, eh)), 1, 1);
        }
        // mouth: a tiny equaliser while music plays
        for (int i = 0; i < 4; i++)
        {
            double mh = mood == CharacterMood.Music ? 0.8 + Math.Abs(Math.Sin(_t * 9 + i * 1.3)) * (1 + _beat * 2.5) : 0.9;
            dc.DrawRectangle(_dark, null, new Rect(cx - 4.4 + i * 2.4, y + 6.4 - mh, 1.5, mh));
        }
    }

    private void DrawFish(DrawingContext dc, double cx, double cy, CharacterMood mood)
    {
        // swims back and forth across its little tank
        double speed = mood == CharacterMood.Sleep ? 0.25 : mood == CharacterMood.Music ? 1.5 + _beat : 0.8;
        double phase = _t * speed;
        double x = cx + Math.Sin(phase) * 5;
        bool right = Math.Cos(phase) > 0;
        double dir = right ? 1 : -1;
        double y = cy + Math.Sin(_t * 3) * 1.2 + _jump.Value * 0.1;
        double wag = Math.Sin(_t * (9 + _beat * 8)) * 2.4;

        var body = new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A));
        var tail = new StreamGeometry();
        using (var c = tail.Open())
        {
            c.BeginFigure(new Point(x - dir * 5.5, y), true, true);
            c.LineTo(new Point(x - dir * 11, y - 4.5 + wag), true, true);
            c.LineTo(new Point(x - dir * 11, y + 4.5 + wag), true, true);
        }
        dc.DrawGeometry(body, null, tail);
        dc.DrawEllipse(body, null, new Point(x, y), 7, 4.8);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)), null, new Point(x + dir * 1, y - 1.6), 4, 1.6);
        Eye(dc, x + dir * 3.6, y - 0.8, 1.9, 1.9, mood, Brushes.White, Brushes.Black, small: true);

        // bubbles
        for (int i = 0; i < 2; i++)
        {
            double life = (_t * 0.7 + i * 0.5) % 1;
            dc.DrawEllipse(null, new Pen(_fg, 0.6), new Point(x + dir * (8 + i * 2), y - 2 - life * 9), 1 + life * 0.8, 1 + life * 0.8);
        }
    }

    private void DrawPlanet(DrawingContext dc, double cx, double y, CharacterMood mood)
    {
        var planet = new LinearGradientBrush(Color.FromRgb(0x7B, 0x79, 0xF7), Color.FromRgb(0x3A, 0x9B, 0xFF), 60);
        double orbit = _t * (mood == CharacterMood.Music ? 3 + _beat * 4 : mood == CharacterMood.Sleep ? 0.4 : 1.4);
        var moon = new Point(cx + Math.Cos(orbit) * 12, y + Math.Sin(orbit) * 3.6);
        bool behind = Math.Sin(orbit) < 0;

        if (behind) dc.DrawEllipse(_fg, null, moon, 1.8, 1.8);
        dc.DrawEllipse(planet, null, new Point(cx, y), 7.4, 7.4);
        Eye(dc, cx - 2.6, y - 0.6, 1.9, 2.4, mood, Brushes.White, Brushes.Black, small: true);
        Eye(dc, cx + 2.6, y - 0.6, 1.9, 2.4, mood, Brushes.White, Brushes.Black, small: true);
        // ring in front
        dc.PushTransform(new RotateTransform(-14, cx, y));
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xD6, 0x0A)), 1.3),
            new EllipseGeometry(new Point(cx, y + 0.5), 12, 3.2));
        dc.Pop();
        dc.DrawEllipse(planet, null, new Point(cx, y - 3.2), 6.2, 4.4); // hide the far half of the ring
        Eye(dc, cx - 2.6, y - 0.6, 1.9, 2.4, mood, Brushes.White, Brushes.Black, small: true);
        Eye(dc, cx + 2.6, y - 0.6, 1.9, 2.4, mood, Brushes.White, Brushes.Black, small: true);
        if (!behind) dc.DrawEllipse(_fg, null, moon, 1.8, 1.8);
    }

    private void DrawSlime(DrawingContext dc, double cx, double cy, CharacterMood mood)
    {
        // squashes and stretches like jelly: with the beat, with a hop, with every breath
        double squash = Math.Sin(_t * (mood == CharacterMood.Music ? 7 : 2.4)) * (mood == CharacterMood.Music ? 0.1 + _beat * 0.22 : 0.05)
                        + Math.Clamp(-_jump.Value * 0.012, -0.25, 0.25);
        double rx = 10 * (1 + squash), ry = 8 * (1 - squash);
        double baseY = cy + 8;
        var green = new LinearGradientBrush(Color.FromRgb(0x7C, 0xF0, 0x8D), Color.FromRgb(0x22, 0xB3, 0x48), 90);

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(cx - rx, baseY), true, true);
            c.BezierTo(new Point(cx - rx, baseY - ry * 2.3), new Point(cx + rx, baseY - ry * 2.3), new Point(cx + rx, baseY), true, true);
        }
        dc.DrawGeometry(green, null, g);
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), null, new Point(cx - rx * 0.4, baseY - ry * 1.25), 2.2, 1.2);
        double ey = baseY - ry * 0.85;
        Eye(dc, cx - 3.4, ey, 2.3, 2.7, mood, Brushes.White, Brushes.Black, small: true);
        Eye(dc, cx + 3.4, ey, 2.3, 2.7, mood, Brushes.White, Brushes.Black, small: true);
    }

    private void DrawPulse(DrawingContext dc, double w, double cy, CharacterMood mood)
    {
        // a heartbeat line that scrolls; music makes the spikes taller, sleep makes it slow and low
        double speed = mood == CharacterMood.Sleep ? 0.5 : mood == CharacterMood.Music ? 2.4 : 1.2;
        double amp = mood == CharacterMood.Sleep ? 2.5 : mood == CharacterMood.Music ? 5 + _beat * 5 : 6;
        if (_happy > 0) amp = 9;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int i = 0; i <= 30; i++)
            {
                double x = 1 + (w - 2) * i / 30.0;
                double p = (i / 30.0 * 2 + _t * speed) % 1; // position inside one beat
                double y = cy + p switch
                {
                    < 0.12 => 0,
                    < 0.18 => -amp * 0.25,
                    < 0.24 => amp * 0.3,
                    < 0.32 => -amp,
                    < 0.40 => amp * 0.55,
                    < 0.48 => 0,
                    _ => 0,
                };
                if (i == 0) c.BeginFigure(new Point(x, y), false, false);
                else c.LineTo(new Point(x, y), true, true);
            }
        }
        dc.DrawGeometry(null, new Pen(_accent, 1.7) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
    }

    // ------------------------------------------------------------------ newer friends

    private void DrawBunny(DrawingContext dc, double cx, double y, CharacterMood mood)
    {
        // long ears that flop with the beat and droop when asleep
        double flop = mood == CharacterMood.Music ? Math.Sin(_t * 7) * (6 + _beat * 14) : Math.Sin(_t * 1.4) * 3;
        double droop = mood == CharacterMood.Sleep ? 38 : 0;
        var pink = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0xC6));
        foreach (var side in new[] { -1, 1 })
        {
            dc.PushTransform(new RotateTransform(side * (12 + droop) + flop * side, cx + side * 3.4, y - 3));
            dc.DrawEllipse(_fg, null, new Point(cx + side * 3.4, y - 9), 2.6, 6.2);
            dc.DrawEllipse(pink, null, new Point(cx + side * 3.4, y - 9), 1.2, 4.4);
            dc.Pop();
        }
        dc.DrawEllipse(_fg, null, new Point(cx, y + 1.5), 9, 7);
        Eye(dc, cx - 3.6, y + 0.8, 2.3, 2.7, mood, _dark, _fg, small: true);
        Eye(dc, cx + 3.6, y + 0.8, 2.3, 2.7, mood, _dark, _fg, small: true);
        dc.DrawEllipse(pink, null, new Point(cx, y + 4), 1, 0.7);
    }

    private void DrawOwl(DrawingContext dc, double cx, double y, CharacterMood mood)
    {
        var body = new LinearGradientBrush(Color.FromRgb(0x9C, 0x7B, 0x5B), Color.FromRgb(0x6E, 0x52, 0x3A), 90);
        var belly = new SolidColorBrush(Color.FromRgb(0xE9, 0xD8, 0xBF));
        // head tilts towards the cursor, like a curious owl
        dc.PushTransform(new RotateTransform(_lookX * 9, cx, y + 4));
        var tufts = new StreamGeometry();
        using (var c = tufts.Open())
        {
            c.BeginFigure(new Point(cx - 9, y - 4), true, true); c.LineTo(new Point(cx - 9.5, y - 10), true, true); c.LineTo(new Point(cx - 4, y - 7), true, true);
            c.BeginFigure(new Point(cx + 9, y - 4), true, true); c.LineTo(new Point(cx + 9.5, y - 10), true, true); c.LineTo(new Point(cx + 4, y - 7), true, true);
        }
        dc.DrawGeometry(body, null, tufts);
        dc.DrawEllipse(body, null, new Point(cx, y + 1), 10, 8.5);
        dc.DrawEllipse(belly, null, new Point(cx, y + 5.5), 5.5, 4);
        // big round eyes with amber rims
        var amber = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x40));
        foreach (var side in new[] { -1, 1 })
        {
            dc.DrawEllipse(amber, null, new Point(cx + side * 4.4, y - 0.5), 4.2, 4.2);
            Eye(dc, cx + side * 4.4, y - 0.5, 3.4, 3.4, mood, Brushes.White, Brushes.Black, small: true);
        }
        var beak = new StreamGeometry();
        using (var c = beak.Open())
        {
            c.BeginFigure(new Point(cx - 1.4, y + 2.4), true, true); c.LineTo(new Point(cx + 1.4, y + 2.4), true, true); c.LineTo(new Point(cx, y + 4.6), true, true);
        }
        dc.DrawGeometry(amber, null, beak);
        dc.Pop();
    }

    private void DrawDino(DrawingContext dc, double cx, double y, CharacterMood mood)
    {
        var green = new LinearGradientBrush(Color.FromRgb(0x7C, 0xE0, 0x7A), Color.FromRgb(0x3C, 0xA8, 0x4E), 90);
        double stomp = mood == CharacterMood.Music ? Math.Abs(Math.Sin(_t * 7)) * (1 + _beat * 2) : 0;
        double baseY = y + 6 - stomp * 0.4;
        // tail sways
        var tail = new StreamGeometry();
        double sway = Math.Sin(_t * (mood == CharacterMood.Music ? 8 : 2.2)) * 2;
        using (var c = tail.Open())
        {
            c.BeginFigure(new Point(cx - 5, baseY - 1), true, true);
            c.QuadraticBezierTo(new Point(cx - 11, baseY - 1 + sway), new Point(cx - 14, baseY - 4 + sway), true, true);
            c.QuadraticBezierTo(new Point(cx - 10, baseY + 2), new Point(cx - 4, baseY + 2), true, true);
        }
        dc.DrawGeometry(green, null, tail);
        // back plates
        var plates = new StreamGeometry();
        using (var c = plates.Open())
        {
            for (int i = 0; i < 3; i++)
            {
                double px = cx - 4 + i * 3.6, py = baseY - 8.2 + Math.Abs(i - 1) * 1.2;
                c.BeginFigure(new Point(px - 1.6, py + 2), true, true); c.LineTo(new Point(px, py - 1.6), true, true); c.LineTo(new Point(px + 1.6, py + 2), true, true);
            }
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x3E)), null, plates);
        dc.DrawRoundedRectangle(green, null, new Rect(cx - 6, baseY - 8, 14, 10), 5, 5);
        dc.DrawEllipse(green, null, new Point(cx + 7, baseY - 6), 4.6, 3.8);           // head
        dc.DrawRectangle(green, null, new Rect(cx - 4, baseY + 1, 2.4, 2.4 + stomp * 0.4));
        dc.DrawRectangle(green, null, new Rect(cx + 3, baseY + 1, 2.4, 2.4 + (1 - stomp * 0.2) * 0.4));
        Eye(dc, cx + 8, baseY - 7, 1.7, 1.9, mood, Brushes.White, Brushes.Black, small: true);
    }

    private void DrawStar(DrawingContext dc, double cx, double y, CharacterMood mood)
    {
        double spin = mood == CharacterMood.Music ? _t * (60 + _beat * 160) : mood == CharacterMood.Sleep ? 0 : Math.Sin(_t * 1.2) * 10;
        double pulse = 1 + (mood == CharacterMood.Music ? _beat * 0.18 : Math.Sin(_t * 2.4) * 0.03);
        var gold = new RadialGradientBrush(Color.FromRgb(0xFF, 0xE6, 0x7A), Color.FromRgb(0xFF, 0xB8, 0x1C));
        var star = new StreamGeometry();
        using (var c = star.Open())
        {
            for (int i = 0; i < 10; i++)
            {
                double a = (-90 + i * 36) * Math.PI / 180;
                double rr = (i % 2 == 0 ? 10.5 : 5.2) * pulse;
                var pt = new Point(cx + Math.Cos(a) * rr, y + 1 + Math.Sin(a) * rr);
                if (i == 0) c.BeginFigure(pt, true, true); else c.LineTo(pt, true, true);
            }
        }
        dc.PushTransform(new RotateTransform(spin, cx, y + 1));
        dc.DrawGeometry(gold, new Pen(gold, 1.6) { LineJoin = PenLineJoin.Round }, star);
        dc.Pop();
        Eye(dc, cx - 2.6, y + 1.2, 1.5, 1.9, mood, Brushes.Black, Brushes.White, small: true);
        Eye(dc, cx + 2.6, y + 1.2, 1.5, 1.9, mood, Brushes.Black, Brushes.White, small: true);
        // twinkles around it
        for (int i = 0; i < 2; i++)
        {
            double life = (_t * 0.8 + i * 0.5) % 1;
            dc.PushOpacity(Math.Sin(life * Math.PI));
            dc.DrawEllipse(Brushes.White, null, new Point(cx + (i == 0 ? 12 : -12), y - 6 + i * 9), 0.9, 0.9);
            dc.Pop();
        }
    }

    private void DrawCloud(DrawingContext dc, double cx, double y, CharacterMood mood)
    {
        y += Math.Sin(_t * 1.8) * 1;   // drifts
        var cloud = new GeometryGroup { FillRule = FillRule.Nonzero };
        cloud.Children.Add(new EllipseGeometry(new Point(cx - 5.5, y + 2), 5.5, 4.6));
        cloud.Children.Add(new EllipseGeometry(new Point(cx + 5.5, y + 2), 5.5, 4.6));
        cloud.Children.Add(new EllipseGeometry(new Point(cx - 1, y - 2), 6.4, 6));
        cloud.Children.Add(new EllipseGeometry(new Point(cx + 4, y - 0.5), 4.4, 4.4));
        dc.DrawGeometry(_fg, null, cloud);
        Eye(dc, cx - 3, y + 1, 1.7, 2, mood, _dark, _fg, small: true);
        Eye(dc, cx + 3, y + 1, 1.7, 2, mood, _dark, _fg, small: true);
        // it rains while asleep, sparkles with music
        var drop = new SolidColorBrush(Color.FromRgb(0x64, 0xD2, 0xFF));
        if (mood == CharacterMood.Sleep || mood == CharacterMood.Music)
        {
            for (int i = 0; i < 3; i++)
            {
                double life = (_t * 1.3 + i * 0.33) % 1;
                dc.PushOpacity(1 - life);
                var pt = new Point(cx - 5 + i * 5, y + 8 + life * 6);
                if (mood == CharacterMood.Music) dc.DrawEllipse(_accent, null, pt, 0.9, 0.9);
                else dc.DrawEllipse(drop, null, pt, 0.7, 1.3);
                dc.Pop();
            }
        }
    }

    public static readonly Color ClaudeOrange = Color.FromRgb(0xD9, 0x77, 0x57);

    /// <summary>
    /// Claude: the orange starburst, one colour, no face, never spins. Like the logo animation,
    /// the rounded rays stretch and pull back one after another - a soft wave running round the
    /// mark - and it swells in when its mood changes (faster while thinking, with the beat for music).
    /// </summary>
    private void DrawClaude(DrawingContext dc, double cx, double cy, CharacterMood mood)
    {
        if (mood != _lastMood) { _moodSince = _t; _lastMood = mood; }
        var orange = new SolidColorBrush(ClaudeOrange);
        orange.Freeze();
        double speed = mood is CharacterMood.Think or CharacterMood.Typing ? 5.5 : mood == CharacterMood.Sleep ? 0.9 : 2.0;
        double depth = mood == CharacterMood.Music ? 0.14 + _beat * 0.32 : mood == CharacterMood.Sleep ? 0.05 : 0.17;
        if (_happy > 0) depth = 0.3;

        // swell in after a mood change: grows from 80 % with a little overshoot
        double since = _t - _moodSince;
        double pop = since < 0.6 ? 1 - 0.2 * Math.Exp(-since * 7) * Math.Cos(since * 14) : 1;
        int seed = _frame % 3;

        double[] lens = [9.8, 8.6, 9.4, 8.2, 10.0, 8.8, 9.2, 8.0, 9.8, 8.6, 9.4, 8.4];
        double[] skew = [0, 4, -3, 2, -2, 5, -4, 1, 3, -5, 2, -1];
        for (int i = 0; i < lens.Length; i++)
        {
            double phase = i * Math.PI * 2 / lens.Length;
            // a wave travelling round the circle + a slower wobble of each ray's own
            double wave = Math.Sin(_t * speed - phase * 2) * 0.7 + Math.Sin(_t * speed * 0.43 + i * 2.3) * 0.3;
            double len = lens[i] * (1 + depth * wave) * pop;
            double ang = (i * 30 + skew[i] + Math.Sin(_t * speed * 0.5 + i) * 2 - 90) * Math.PI / 180;
            double dx = Math.Cos(ang), dy = Math.Sin(ang);
            var a = new Point(cx + dx * 1.5, cy + dy * 1.5);
            var b = new Point(cx + dx * len, cy + dy * len);
            if (!Sketch)
            {
                var pen = new Pen(orange, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                pen.Freeze();
                dc.DrawLine(pen, a, b);
                continue;
            }
            // brush stroke: slightly bent, uneven width, redrawn a little differently every frame
            var mid = new Point((a.X + b.X) / 2 - dy * Noise(seed, i) * 0.7, (a.Y + b.Y) / 2 + dx * Noise(seed, i) * 0.7);
            var tip = new Point(b.X + Noise(seed, i + 40) * 0.4, b.Y + Noise(seed, i + 80) * 0.4);
            var brushPen = new Pen(orange, 2.3 + Noise(seed, i + 120) * 0.45) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            brushPen.Freeze();
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(a, false, false);
                c.QuadraticBezierTo(mid, tip, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, brushPen, g);
        }
        if (Sketch) dc.DrawGeometry(orange, null, Wobble(new EllipseGeometry(new Point(cx, cy), 3.1 * pop, 3.1 * pop), 0.35, 9));
        else dc.DrawEllipse(orange, null, new Point(cx, cy), 3.0 * pop, 3.0 * pop);
    }

    // ------------------------------------------------------------------ hand-drawn helpers

    private static double Noise(int a, int b)
    {
        double v = Math.Sin(a * 127.1 + b * 311.7 + 0.5) * 43758.5453;
        return (v - Math.Floor(v)) * 2 - 1;
    }

    /// <summary>Retrace an outline by hand: the edge gets a small tremble that changes every drawing.</summary>
    private Geometry Wobble(Geometry geometry, double amp, int salt)
    {
        var flat = geometry.GetFlattenedPathGeometry(0.2, ToleranceType.Absolute);
        var sg = new StreamGeometry { FillRule = FillRule.Nonzero };
        int seed = (_frame % 3) * 101 + salt * 17, k = 0;
        using (var c = sg.Open())
        {
            foreach (var fig in flat.Figures)
            {
                var pts = new List<Point> { fig.StartPoint };
                foreach (var seg in fig.Segments)
                {
                    if (seg is PolyLineSegment pl) pts.AddRange(pl.Points);
                    else if (seg is LineSegment ls) pts.Add(ls.Point);
                }
                if (pts.Count < 3) continue;
                var dense = new List<Point>();
                for (int i = 0; i < pts.Count; i++)
                {
                    Point a = pts[i], b = pts[(i + 1) % pts.Count];
                    var d = b - a;
                    int n = Math.Max(1, (int)Math.Ceiling(d.Length / 2.4));
                    for (int j = 0; j < n; j++)
                    {
                        var q = a + d * ((double)j / n);
                        k++;
                        dense.Add(new Point(q.X + Noise(seed, k) * amp, q.Y + Noise(seed + 50, k) * amp));
                    }
                }
                c.BeginFigure(dense[0], true, true);
                c.PolyLineTo(dense.Skip(1).ToList(), true, true);
            }
        }
        sg.Freeze();
        return sg;
    }

    // ------------------------------------------------------------------ Claude Code (pixel guy)

    private CharacterMood _lastMood;
    private double _moodSince, _typingSince, _lastDraw;
    private double _greet;              // seconds of greeting left
    private double _profile;            // 0 facing you ... 1 turned to its side
    private double _laptop;             // 0 put away ... 1 standing in front of it
    private double _lid;                // 0 closed ... 1 open
    private bool _greeted;

    /// <summary>A little hop - e.g. when you start typing to the AI.</summary>
    public void Hop(double strength = 1)
    {
        _jump.Velocity -= 55 * strength;
    }

    /// <summary>Hello! A hop, a wink and a wave.</summary>
    public void Greet()
    {
        _greet = 1.6;
        Hop(0.8);
    }

    private static double Approach(double v, double to, double step) =>
        v + Math.Sign(to - v) * Math.Min(Math.Abs(to - v), step);

    /// <summary>
    /// Claude Code, drawn after the mascot: a wide orange block, short arms halfway up, two pairs
    /// of legs at the corners and small square eyes. Greets with a wink and a wave, strolls, dances.
    /// When you type it turns side-on, sets down a laptop, opens it and types; when you stop it
    /// closes the lid, puts the laptop away and turns back to you.
    /// </summary>
    private void DrawClawd(DrawingContext dc, double cx, double cy, CharacterMood mood)
    {
        if (!_greeted) { _greeted = true; Greet(); }
        if (mood != _lastMood)
        {
            if (mood == CharacterMood.Typing) { Hop(1.1); _typingSince = _t; }
            _lastMood = mood;
        }
        double dt = Math.Clamp(_t - _lastDraw, 0, 0.1);
        _lastDraw = _t;
        if (_greet > 0) _greet -= dt;

        // turn -> set the laptop down -> open the lid; and the same backwards
        if (mood == CharacterMood.Typing)
        {
            _profile = Approach(_profile, 1, dt * 7);
            if (_profile >= 1) _laptop = Approach(_laptop, 1, dt * 4.5);
            if (_laptop >= 1) _lid = Approach(_lid, 1, dt * 4);
        }
        else
        {
            _lid = Approach(_lid, 0, dt * 5);
            if (_lid <= 0) _laptop = Approach(_laptop, 0, dt * 5);
            if (_laptop <= 0) _profile = Approach(_profile, 0, dt * 7);
        }
        double f = Math.Round(_profile * 4) / 4;   // stepped: it is pixel art
        bool side = f >= 0.5;
        bool typing = side && _lid >= 1;

        const double p = 1.9;                      // one "pixel"
        var body = new SolidColorBrush(ClaudeOrange);
        var shade = new SolidColorBrush(Color.FromRgb(0xB8, 0x5C, 0x40));
        var dark = new SolidColorBrush(Color.FromRgb(0x14, 0x10, 0x0E));
        var metal = new SolidColorBrush(Color.FromRgb(0xA4, 0xA6, 0xAC));
        var screen = new SolidColorBrush(Color.FromRgb(0xF4, 0xE9, 0xDC));
        body.Freeze(); shade.Freeze(); dark.Freeze(); metal.Freeze(); screen.Freeze();
        var ink = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x19, 0x12)), 0.8) { LineJoin = PenLineJoin.Round };
        ink.Freeze();

        // whole-body motion
        double bob = mood switch
        {
            CharacterMood.Music => -Math.Abs(Math.Sin(_t * 7)) * (2 + _beat * 4),
            CharacterMood.Sleep => Math.Sin(_t * 1.2) * 0.6,
            CharacterMood.Typing => typing ? Math.Abs(Math.Sin(_t * 9)) * 0.35 : 0,   // nods while typing
            _ => Math.Sin(_t * 2.4) * 0.5,
        };
        bool strolling = mood == CharacterMood.Idle && _greet <= 0 && _profile <= 0;
        double walk = strolling ? Math.Sin(_t * 0.7) * 2.2 : 0;
        bool walking = strolling && Math.Abs(Math.Cos(_t * 0.7)) > 0.25;
        double lean = f * 0.4;
        // the laptop needs room in front, so it steps back a little while turned
        double ox = cx - 7.4 * p + walk + f * 2.6 * p;
        double oy = cy + 7.3 - 9.5 * p + bob + _jump.Value * 0.1;

        // pixels are collected into same-colour layers so the hand-drawn mode can trace one outline
        var layers = new List<(Brush Brush, GeometryGroup Shape, bool Ink)>();
        int salt = 0;
        void Px(double col, double row, double w = 1, double h = 1, Brush? b = null, bool outline = true)
        {
            b ??= body;
            var r = new Rect(ox + col * p, oy + row * p, w * p + 0.15, h * p + 0.15);
            if (!Sketch) { dc.DrawRectangle(b, null, r); return; }
            if (layers.Count == 0 || layers[^1].Brush != b) layers.Add((b, new GeometryGroup { FillRule = FillRule.Nonzero }, outline));
            layers[^1].Shape.Children.Add(new RectangleGeometry(r));
        }
        void Flush()
        {
            foreach (var (b, shape, outline) in layers)
                dc.DrawGeometry(b, outline ? ink : null, Wobble(shape.GetOutlinedPathGeometry(), 0.42, ++salt));
            layers.Clear();
        }
        void Bar(Point a, Point b2, double width, Brush b)
        {
            var pen = new Pen(b, width) { StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square };
            pen.Freeze();
            if (!Sketch) { dc.DrawLine(pen, a, b2); return; }
            var line = new LineGeometry(a, b2).GetWidenedPathGeometry(pen);
            dc.DrawGeometry(b, ink, Wobble(line, 0.3, ++salt));
        }

        // ---- the laptop, in front of it (to the left), seen from the side
        if (_laptop > 0 && side)
        {
            double carry = 1 - _laptop;                                   // still in its hands, up high
            double lx = ox + (-6.2 + carry * 2.5) * p, ly = oy + (9.2 - carry * 5) * p;
            double len = 4.6 * p;
            dc.PushOpacity(Math.Min(1, _laptop * 2.5));
            Bar(new Point(lx, ly), new Point(lx + len, ly), 0.6 * p, metal);   // base with the keyboard
            double ang = _lid * 108 * Math.PI / 180;                       // lid tilts back, screen facing it
            var top = new Point(lx + Math.Cos(ang) * len, ly - Math.Sin(ang) * len);
            Bar(new Point(lx, ly), top, 0.6 * p, metal);
            if (_lid > 0.6)
            {
                // the screen's light on the inside of the lid
                double glow = (_lid - 0.6) / 0.4 * (0.75 + 0.25 * Math.Sin(_t * 9));
                var n = new Vector(Math.Sin(ang), Math.Cos(ang)) * 0.55 * p;
                dc.PushOpacity(glow);
                Bar(new Point(lx, ly) + n + (top - new Point(lx, ly)) * 0.15, top + n - (top - new Point(lx, ly)) * 0.1, 0.35 * p, screen);
                dc.Pop();
            }
            dc.Pop();
            // letters popping off the keys
            if (typing)
            {
                for (int i = 0; i < 2; i++)
                {
                    double life = ((_t - _typingSince) * 1.4 + i * 0.5) % 1;
                    dc.PushOpacity(1 - life);
                    dc.DrawRectangle(screen, null, new Rect(lx + (1.4 + i * 1.8) * p, ly - (1 + life * 4) * p, 0.6 * p, 0.6 * p));
                    dc.Pop();
                }
            }
        }

        // ---- legs: two pairs at the corners. Stepping, tapping, kicking
        double[] legX = [2.9, 4.7, 9.3, 11.1];
        for (int i = 0; i < 4; i++)
        {
            double lift = 0;
            if (walking) lift = Math.Sin(_t * 9 + i * Math.PI) > 0 ? 0.7 : 0;
            else if (mood == CharacterMood.Think) lift = i == 3 && Math.Sin(_t * 10) > 0 ? 0.6 : 0;
            else if (mood == CharacterMood.Music) lift = Math.Sin(_t * 7 + i * 1.6) > 0.3 ? 0.6 : 0;
            else if (typing) lift = i == 3 && Math.Sin(_t * 5) > 0.6 ? 0.5 : 0;
            Px(legX[i] - lean * 0.5, 7, 1, 2.5 - lift);
        }

        // ---- body
        Px(1.5 - lean, 0, 12, 7);

        // ---- arms, halfway up the sides
        double armL = 0, armR = 0;
        switch (mood)
        {
            case CharacterMood.Music:
                armL = Math.Sin(_t * 7) > 0 ? -1 : 0;
                armR = Math.Sin(_t * 7) > 0 ? 0 : -1;
                break;
            case CharacterMood.Happy:
                armL = armR = -1.4;
                break;
            case CharacterMood.Idle:
                if (_profile <= 0) armR = Math.Sin(_t * 0.9) > 0.93 ? -1 + Math.Sin(_t * 14) * 0.4 : 0;   // an occasional wave
                break;
        }
        if (_happy > 0) armL = armR = -1.4;
        if (_greet > 0 && !side) armR = Math.Sin(_greet * 18) > 0 ? -1.7 : -0.8;      // hello wave
        if (!side)
        {
            Px(0.2, 2.2 + armL, 1.4, 1.7);
            Px(13.4, 2.2 + armR, 1.4, 1.7);
        }
        else
        {
            // the near arm reaches for the laptop: up high while carrying it, then down to the keys, tapping
            double reach = (1 - _laptop) * -2.4;
            double tap = typing && Math.Sin(_t * 26) > 0 ? 0.4 : 0;
            Px(-0.5 - lean, 4.3 + reach + tap, 2.1, 1.5);
        }
        Flush();

        // the far side falls into shade when turned
        if (f > 0)
        {
            dc.PushOpacity(f);
            Px(11.6 - lean, 0.25, 1.65, 6.5, shade, false);
            Flush();
            dc.Pop();
        }

        // ---- eyes: small squares. Glance with the cursor, look up while thinking, down at the screen
        // while typing, blink, close while sleeping, squint (> <) when happy, wink while greeting
        double look = side ? 0 : Math.Round(_lookX * 0.6 * 2) / 2;
        double lookUp = side ? 0 : Math.Round(_lookY * 0.4 * 2) / 2;
        double shift = -f * 1.5 - lean;
        double eyeRow = (mood == CharacterMood.Think ? 2.0 : side ? 2.9 : 2.6) + lookUp;
        double leftEye = 3.6 + look + shift, rightEye = 10.4 + look + shift * 1.6;

        void Chevron(double x, double y, bool pointsRight)
        {
            double a = pointsRight ? 0 : 0.5, b = pointsRight ? 0.5 : 0;
            Px(x + a, y - 0.3, 0.55, 0.55, dark, false);
            Px(x + b, y + 0.2, 0.55, 0.55, dark, false);
            Px(x + a, y + 0.7, 0.55, 0.55, dark, false);
        }

        void Cells(double x, double y, string pattern, Brush b)
        {
            // a tiny 3x3 pixel picture, cells of half a pixel
            for (int i = 0; i < 9; i++)
                if (pattern[i] == '#') Px(x + i % 3 * 0.5, y + i / 3 * 0.5, 0.55, 0.55, b, false);
        }

        if (_emoNow != Emotion.None && _emoNow != Emotion.Laugh)
        {
            switch (_emoNow)
            {
                case Emotion.Love:
                    Cells(leftEye - 0.15, eyeRow, "#.####.#.", LoveBrush);
                    Cells(rightEye - 0.15, eyeRow, "#.####.#.", LoveBrush);
                    break;
                case Emotion.Dizzy:
                    string x1 = _frame % 2 == 0 ? "#.#.#.#.#" : ".#.###.#.";
                    Cells(leftEye - 0.15, eyeRow, x1, dark);
                    Cells(rightEye - 0.15, eyeRow, x1, dark);
                    break;
                case Emotion.Surprised:
                    Px(leftEye - 0.35, eyeRow - 0.35, 1.8, 1.8, dark, false);
                    Px(rightEye - 0.35, eyeRow - 0.35, 1.8, 1.8, dark, false);
                    break;
                case Emotion.Angry:
                case Emotion.Sad:
                {
                    bool angry = _emoNow == Emotion.Angry;
                    Px(leftEye, eyeRow + 0.3, 1.3, 1.0, dark, false);
                    Px(rightEye, eyeRow + 0.3, 1.3, 1.0, dark, false);
                    // two-step pixel brows: angry slope down to the middle, sad slope up
                    double hi = eyeRow - 1.0, lo = eyeRow - 0.55;
                    Px(leftEye - 0.3, angry ? hi : lo, 0.9, 0.45, dark, false);
                    Px(leftEye + 0.6, angry ? lo : hi, 0.9, 0.45, dark, false);
                    Px(rightEye - 0.2, angry ? lo : hi, 0.9, 0.45, dark, false);
                    Px(rightEye + 0.7, angry ? hi : lo, 0.9, 0.45, dark, false);
                    break;
                }
                case Emotion.Cool:
                {
                    double drop = (1 - Math.Min(1, (EmoteTime - _emoteLeft) / 0.3)) * -3;
                    Px(leftEye - 0.6, eyeRow - 0.2 + drop, rightEye - leftEye + 2.5, 0.5, dark, false);
                    Px(leftEye - 0.5, eyeRow - 0.2 + drop, 2.1, 1.6, dark, false);
                    Px(rightEye - 0.3, eyeRow - 0.2 + drop, 2.1, 1.6, dark, false);
                    Px(leftEye - 0.1, eyeRow + 0.1 + drop, 0.6, 0.35, Brushes.White, false);
                    Px(rightEye + 0.1, eyeRow + 0.1 + drop, 0.6, 0.35, Brushes.White, false);
                    break;
                }
            }
        }
        else if (mood == CharacterMood.Sleep)
        {
            Px(leftEye - 0.2, eyeRow + 0.5, 1.4, 0.4, dark, false);
            Px(rightEye - 0.2, eyeRow + 0.5, 1.4, 0.4, dark, false);
        }
        else if (_happy > 0 || _emoNow == Emotion.Laugh)
        {
            Chevron(leftEye, eyeRow, true);
            Chevron(rightEye, eyeRow, false);
        }
        else
        {
            double eh = 1.3 * (1 - _blink * 0.8);
            Px(leftEye - 0.1, eyeRow + (1.3 - eh) / 2 - 0.1, 1.3, eh, dark, false);
            if (_greet > 0.2 && _greet < 1.3 && !side) Chevron(rightEye, eyeRow, false);   // wink
            else Px(rightEye - 0.1, eyeRow + (1.3 - eh) / 2 - 0.1, 1.3, eh, dark, false);
        }
        Flush();
    }

    // ------------------------------------------------------------------ emotions

    private static readonly Brush LoveBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x8A)));
    private static readonly Brush AngryBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)));
    private static readonly Brush TearBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x6C, 0xC4, 0xFF)));
    private static readonly Brush StarBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD3, 0x4D)));
    private static readonly Brush ShadesBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x12)));

    private static Geometry HeartGeometry(double x, double y, double s)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, y + s * 0.9), true, true);
            c.BezierTo(new Point(x - s * 1.7, y - s * 0.15), new Point(x - s * 0.75, y - s * 1.35), new Point(x, y - s * 0.45), true, true);
            c.BezierTo(new Point(x + s * 0.75, y - s * 1.35), new Point(x + s * 1.7, y - s * 0.15), new Point(x, y + s * 0.9), true, true);
        }
        g.Freeze();
        return g;
    }

    private static Geometry DropGeometry(double x, double y, double s)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, y - s * 1.4), true, true);
            c.BezierTo(new Point(x + s * 0.4, y - s * 0.6), new Point(x + s, y - s * 0.1), new Point(x + s, y + s * 0.3), true, true);
            c.ArcTo(new Point(x - s, y + s * 0.3), new Size(s, s), 0, true, SweepDirection.Clockwise, true, true);
            c.BezierTo(new Point(x - s, y - s * 0.1), new Point(x - s * 0.4, y - s * 0.6), new Point(x, y - s * 1.4), true, true);
        }
        g.Freeze();
        return g;
    }

    private void Spiral(DrawingContext dc, double x, double y, double r, Brush b, double width)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double spin = _t * 9;
            for (double a = 0; a <= Math.PI * 5; a += 0.25)
            {
                double rr = r * a / (Math.PI * 5);
                var pt = new Point(x + Math.Cos(a + spin) * rr, y + Math.Sin(a + spin) * rr);
                if (a == 0) c.BeginFigure(pt, false, false); else c.LineTo(pt, true, true);
            }
        }
        g.Freeze();
        dc.DrawGeometry(null, new Pen(b, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
    }

    private static void Star4(DrawingContext dc, double x, double y, double s, Brush b)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, y - s), true, true);
            c.QuadraticBezierTo(new Point(x, y), new Point(x + s, y), true, true);
            c.QuadraticBezierTo(new Point(x, y), new Point(x, y + s), true, true);
            c.QuadraticBezierTo(new Point(x, y), new Point(x - s, y), true, true);
            c.QuadraticBezierTo(new Point(x, y), new Point(x, y - s), true, true);
        }
        g.Freeze();
        dc.DrawGeometry(b, null, g);
    }

    /// <summary>Sunglasses that slide down onto the eyes.</summary>
    private static readonly Pen ShadesRim = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9C, 0xA4)), 0.9));

    private static Pen Frozen(Pen p) { p.Freeze(); return p; }

    private static void Shades(DrawingContext dc, double cx, double y, double et)
    {
        y -= (1 - Math.Min(1, et / 0.3)) * 9;
        dc.DrawLine(ShadesRim, new Point(cx - 1.5, y - 2.5), new Point(cx + 1.5, y - 2.5));
        foreach (double ex in new[] { cx - 6.6, cx + 6.6 })
        {
            dc.DrawRoundedRectangle(ShadesBrush, ShadesRim, new Rect(ex - 5.6, y - 4.6, 11.2, 8.6), 3.6, 3.6);
            dc.DrawRectangle(Brushes.White, null, new Rect(ex - 3.6, y - 3.0, 3.0, 1.0));
        }
    }

    /// <summary>The little extras around the character: hearts, an anger mark, "!", a tear, stars...</summary>
    private void DrawEmotion(DrawingContext dc, double w, double h, double cx, double cy, double et)
    {
        double fadeOut = Math.Min(1, _emoteLeft / 0.35);
        dc.PushOpacity(fadeOut);
        switch (_emoNow)
        {
            case Emotion.Love:
                for (int i = 0; i < 3; i++)
                {
                    double life = (et * 0.9 + i * 0.33) % 1;
                    double x = cx + (i - 1) * 9 + Math.Sin(life * 7 + i) * 1.5, y = 2 - life * 9;
                    dc.PushOpacity(1 - life);
                    dc.DrawGeometry(LoveBrush, null, HeartGeometry(x, y, 2 + (1 - life)));
                    dc.Pop();
                }
                break;
            case Emotion.Angry:
            {
                double x = w - 2, y = 1, k = 1 + Math.Sin(_t * 14) * 0.15;
                var pen = new Pen(AngryBrush, 1.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(x + sx * 0.9 * k, y + sy * 3 * k), false, false);
                        c.QuadraticBezierTo(new Point(x + sx * 0.9 * k, y + sy * 0.9 * k), new Point(x + sx * 3 * k, y + sy * 0.9 * k), true, true);
                    }
                    dc.DrawGeometry(null, pen, g);
                }
                break;
            }
            case Emotion.Surprised:
            {
                double pop = Math.Min(1, et / 0.15), y = 3 - pop * 3;
                dc.DrawRoundedRectangle(StarBrush, null, new Rect(w - 3, y - 4, 2.2, 5.5 * pop), 1, 1);
                dc.DrawEllipse(StarBrush, null, new Point(w - 1.9, y + 3.6), 1.2, 1.2);
                break;
            }
            case Emotion.Sad:
            {
                double life = et * 0.8 % 1;
                dc.PushOpacity(1 - life * life);
                dc.DrawGeometry(TearBrush, null, DropGeometry(cx - 7, cy + 3 + life * 9, 1.4));
                dc.Pop();
                break;
            }
            case Emotion.Dizzy:
                for (int i = 0; i < 3; i++)
                {
                    double a = _t * 5 + i * Math.PI * 2 / 3;
                    double x = cx + Math.Cos(a) * 11, y = 0.5 + Math.Sin(a) * 2.5;
                    Star4(dc, x, y, 2.2 + Math.Sin(a) * 0.5, StarBrush);
                }
                break;
            case Emotion.Laugh:
                for (int i = 0; i < 2; i++)
                {
                    double life = (et * 1.6 + i * 0.5) % 1, side = i == 0 ? -1 : 1;
                    dc.PushOpacity(1 - life);
                    dc.DrawGeometry(TearBrush, null, DropGeometry(cx + side * (11 + life * 6), cy - 2 - Math.Sin(life * Math.PI) * 4, 1.2));
                    dc.Pop();
                }
                break;
            case Emotion.Cool:
                if (et > 0.3 && et < 0.9) Star4(dc, cx + 9, cy - 4, 2.6 * Math.Sin((et - 0.3) / 0.6 * Math.PI), Brushes.White);
                break;
        }
        dc.Pop();
    }

    private void DrawZ(DrawingContext dc, double x, double y)
    {
        double life = _t * 0.5 % 1;
        var ft = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            6 + life * 2, _fg, 1.25);
        dc.PushOpacity(1 - life);
        dc.DrawText(ft, new Point(x - life * 2, y - life * 4));
        dc.Pop();
    }

    private void DrawDots(DrawingContext dc, double x, double y)
    {
        for (int i = 0; i < 3; i++)
        {
            double up = Math.Max(0, Math.Sin(_t * 6 - i * 0.9)) * 1.6;
            dc.DrawEllipse(_accent, null, new Point(x + i * 3, y - up), 1, 1);
        }
    }
}

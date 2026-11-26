using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Services;
using Drawing = System.Drawing;

namespace DynamicIsland;

/// <summary>
/// Frosted glass, the see-through lower half, the shimmering colour aura,
/// page dots in the header and the settings sidebar.
/// </summary>
public partial class MainWindow
{
    /// <summary>The island's glass: on in settings and not turned off for everything in Windows (Transparency effects).</summary>
    private bool IslandGlass => _settings.Glass && !Services.Motion.NoTransparency;

    private const double FrostMargin = 28;      // a little of the screen around the island, for the blur to pull in

    /// <summary>
    /// How far the bottom edge can drip below the island with the music (LiquidGeometry: up to 10.2 × the waves'
    /// strength). The glass's picture reaches that much lower: cut at the island's own bottom, its blur faded to
    /// 0.81 at the tip of a drip and the dripping part looked thinner than the rest.
    /// </summary>
    private double DripRoom => Liquid && _settings.MusicWaves && !Attached ? Math.Ceiling(10.2 * LiquidK) : 0;
    private double _frostHalf = 200, _frostHeight = 80;
    private byte[]? _frostPixels;
    private long _frostSig;
    /// <summary>Test counters (--run glassbench): pictures shown, grabs taken and their time off the UI thread.</summary>
    private int _frostShownCount, _frostGrabCount, _frostFades;
    private double _frostFadeMs;
    private long _frostGrabTicks;
    private LinearGradientBrush? _auraBrush;

    public static readonly (string Id, string Name, string[] Colors)[] Auras =
    [
        ("none", L.T("Нет"), []),
        ("aurora", L.T("Аврора"), ["#5B8CFF", "#A066FF", "#3DE1FF"]),
        ("sunset", L.T("Закат"), ["#FF6B6B", "#FFB347", "#FF5E9C"]),
        ("ocean", L.T("Океан"), ["#00C6FF", "#0072FF", "#00E0B0"]),
        ("neon", L.T("Неон"), ["#39FF14", "#00F0FF", "#FF00E5"]),
        ("mono", L.T("Иней"), ["#FFFFFF", "#8E8E93", "#D1D1D6"]),
    ];

    private void InitGlass()
    {
        foreach (var _ in new int[3]) _frostFree.Enqueue(new FrostFrame());
        _showFrostFrame = ShowFrostFrame;
        _frostLoop = new Thread(FrostLoop) { IsBackground = true, Name = "Atoll glass loop", Priority = ThreadPriority.BelowNormal };
        _frostLoop.Start();
        ApplyGlass();
        SetupAura();
        UpdatePageDots();
        ApplyTabIconsMode();
    }

    // ================================================================ fill: tint + transparent bottom

    /// <summary>
    /// The frosted picture lies under the whole island right now (right size, visible). Only then may the
    /// island be see-through: otherwise the windows behind it (a web page, a chat) would show through
    /// sharp - for example for half a second while the island opens, before the picture is retaken.
    /// </summary>
    private bool _frostCovers;

    private void SetFrostCovers(bool covers, bool fade = false)
    {
        if (_frostCovers == covers) return;
        _frostCovers = covers;
        UpdateGlassFill(fade);
    }

    /// <summary>The island's fill: the theme colour, see-through over the frost, fading at the bottom.</summary>
    private void ApplyGlass()
    {
        UpdateGlassFill();
        ApplyCaptureExclusion();
        FrostHost.Visibility = IslandGlass ? Visibility.Visible : Visibility.Collapsed;
        if (IslandGlass) CaptureFrost();
        UpdateAuraVisibility();
    }

    /// <summary>fade: the colours slide to the new ones (the glass's picture no longer covers a moving island).</summary>
    private void UpdateGlassFill(bool fade = false)
    {
        var c = (FindResource("IslandBg") as SolidColorBrush)?.Color ?? Colors.Black;
        c.A = 255;
        bool expanded = _mode != Mode.Compact;
        bool photo = PhotoHost.Visibility == Visibility.Visible;
        // see-through only over a picture: the frost (when it covers the island) or your photo
        bool overPicture = photo || (IslandGlass && !Lite && _frostCovers);
        double tint = overPicture && !photo ? Math.Clamp(_settings.GlassTint, 20, 100) / 100.0 : 1;
        // the folded pill stays readable: never thinner than 80 %
        double body = expanded ? tint : Math.Max(tint, 0.8);
        // your photo shows through a lighter tint (still dark enough for white text)
        if (photo) body = expanded ? 0.5 : 0.62;
        double bottom = _settings.BottomFade && overPicture ? body * (expanded ? 0.55 : 0.45) : body;

        // your photo, where there is one, keeps its own tint
        if (!photo && ClearGlass) { c = LiquidTintColor(c); body = bottom = ClearAlpha(); }
        if (overPicture && !photo && MatteGlass) { c = LiquidTintColor(c); body = bottom = LiquidAlpha(); }
        bool pane = !photo && (ClearGlass || overPicture && MatteGlass);
        // liquid glass: one even pane (only a docked one meets the screen edge solid), folded or open
        bool liquid = overPicture && !photo && LiquidGlass;
        if (liquid) { c = LiquidTintColor(c); body = bottom = LiquidAlpha(); }
        if (liquid && FrostLens.Effect is Views.GlassEffect lens)
        {
            // the lens tints its own picture, thinner along the rim so the bending shows; the fill only takes the mouse
            var to = Color.FromArgb((byte)Math.Round(body * 255), c.R, c.G, c.B);
            lens.BeginAnimation(Views.GlassEffect.TintProperty, new ColorAnimation(to, TimeSpan.FromMilliseconds(fade && !Services.Motion.Reduced ? 200 : 0)));
            body = bottom = 1 / 255.0;
        }

        Color A(double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);
        // from the edge it is attached to outward (folded on a side, the pill lies along that edge)
        var (fillFrom, fillTo) = _edge switch
        {
            Edge.Bottom => (new Point(0.5, 1), new Point(0.5, 0)),
            Edge.Left when _mode == Mode.Compact => (new Point(0, 0.5), new Point(1, 0.5)),
            Edge.Right when _mode == Mode.Compact => (new Point(1, 0.5), new Point(0, 0.5)),
            _ => (new Point(0.5, 0), new Point(0.5, 1)),
        };
        var fill = new LinearGradientBrush { StartPoint = fillFrom, EndPoint = fillTo };
        fill.GradientStops.Add(new GradientStop(A((liquid || pane) && _edge != Edge.Top ? body : 1), 0));          // the top edge of the screen meets it: solid
        fill.GradientStops.Add(new GradientStop(A(body), 0.14));
        fill.GradientStops.Add(new GradientStop(A(body), 0.5));
        fill.GradientStops.Add(new GradientStop(A(bottom), 1));
        if (fade && !Services.Motion.Reduced && IslandShape.Fill is LinearGradientBrush old && old.GradientStops.Count == 4)
        {
            for (int i = 0; i < 4; i++)
                fill.GradientStops[i].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(old.GradientStops[i].Color, fill.GradientStops[i].Color, TimeSpan.FromMilliseconds(160)));
        }
        else fill.Freeze();
        IslandShape.Fill = fill;
    }

    private bool? _excluded;

    /// <summary>
    /// With frosted glass on, the island is left out of every screen capture - otherwise it would photograph
    /// itself and the glass turns opaque. With glass off it shows up in screenshots and recordings again.
    /// </summary>
    private static readonly string[] NvidiaRecorders = ["NVIDIA Overlay", "NVIDIA Share", "nvsphelper64", "nvsphelper"];
    private volatile bool _nvidiaRecorder;
    private DateTime _nvidiaCheckedAt;
    private bool _nvidiaChecking;

    /// <summary>
    /// Is NVIDIA's screen recorder around? Checked at most once a minute, after the first time on a background
    /// thread (listing every process takes a while): until it answers, the last answer stands.
    /// </summary>
    private bool NvidiaRecorderRunning()
    {
        if (DateTime.UtcNow - _nvidiaCheckedAt < TimeSpan.FromMinutes(1) || _nvidiaChecking) return _nvidiaRecorder;
        bool first = _nvidiaCheckedAt == default;
        _nvidiaCheckedAt = DateTime.UtcNow;
        if (first) return _nvidiaRecorder = FindNvidiaRecorder();   // the start decides how the island begins: ask right away
        _nvidiaChecking = true;
        Task.Run(FindNvidiaRecorder).ContinueWith(t =>
        {
            _nvidiaChecking = false;
            if (!t.IsFaulted) _nvidiaRecorder = t.Result;
        }, TaskScheduler.FromCurrentSynchronizationContext());
        return _nvidiaRecorder;
    }

    private bool FindNvidiaRecorder()
    {
        bool found = false;
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            if (!found && NvidiaRecorders.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase)) found = true;
            p.Dispose();
        }
        if (found != _nvidiaRecorder) Log.Write("NVIDIA recorder " + (found ? "found: the island stays visible to screen capture" : "gone"));
        return found;
    }

    /// <summary>Keep the island out of screenshots / recordings? The user's choice, or automatic.</summary>
    private bool HideFromCapture => _settings.CaptureHidden ?? !NvidiaRecorderRunning();

    private void ApplyCaptureExclusion()
    {
        bool hide = HideFromCapture;
        if (_sideToast != null) _sideToast.HideFromCapture = hide;   // the notice beside the open island goes along
        if (_hwnd == IntPtr.Zero || _excluded == hide) return;
        _excluded = hide;
        if (!Native.SetWindowDisplayAffinity(_hwnd, _excluded == true ? Native.WDA_EXCLUDEFROMCAPTURE : 0))
            Log.Write("display affinity: not supported");
    }

    /// <summary>Snapshot of the screen behind the island (our own window isn't in it), blurred by the Image's effect.</summary>
    /// <summary>Test: a made-up backdrop instead of the real screen (see --run liquidtest).</summary>
    private Func<int, int, byte[]>? _frostTestBackdrop;

    /// <summary>Live demo: a still picture of the desktop (taken with the island hidden from it) stands in for the screen.</summary>
    private Drawing.Bitmap? _demoPlate;
    private Drawing.Point _demoPlateAt;
    /// <summary>
    /// The glass needs a picture of the screen without the island. Either the island is hidden from all
    /// capture (then a plain grab does), or — when it must stay visible to screenshots and recorders —
    /// the Magnification API leaves just our window out (where this Windows supports it).
    /// </summary>
    private bool FrostCanGrab => _excluded == true || _demoPlate != null || (!Settings.Safe && MagCapture.Works != false);

    /// <summary>
    /// Tells the grab loop what to take now (cheap: called whenever the outline, the state or a setting changes,
    /// and once a second). The loop stops while the glass is off, hidden, dragged or changing size.
    /// </summary>
    private void CaptureFrost()
    {
        if (ClearGlass)   // no picture: what is behind shows through the island itself
        {
            PauseFrost();
            FrostHost.BeginAnimation(OpacityProperty, null);
            FrostHost.Opacity = 0;
            SetFrostCovers(false);
            return;
        }
        if (!IslandGlass || Lite || !FrostCanGrab || _hidden || _fullscreenHidden || _gameFront || !IsLoaded || !Island.IsVisible) { PauseFrost(); if (Lite || !FrostCanGrab || _gameFront) { FrostHost.BeginAnimation(OpacityProperty, null); FrostHost.Opacity = 0; } SetFrostCovers(false); return; }
        // grabbing the screen makes Windows finish composing first: never while the size is changing
        if (_rendering && !(_w.IsSettled && _h.IsSettled)) { PauseFrost(); return; }
        if (_frostTestBackdrop != null || _demoPlate != null)   // still pictures: taken again when the size changes, or after half a second
        {
            PauseFrost();
            double half = Island.ActualWidth / 2 + FrostMargin, height = Island.ActualHeight + FrostMargin + DripRoom;
            if (_frostCovers && Math.Abs(half - _frostHalf) < 0.5 && Math.Abs(height - _frostHeight) < 0.5 && Environment.TickCount64 - _frostStillAt < 500) return;
            _frostStillAt = Environment.TickCount64;
            GrabFrost(Island.ActualWidth, Island.ActualHeight);
            return;
        }
        if (_hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var wr)) return;
        double islandW = Island.ActualWidth, islandH = Island.ActualHeight, k = Dpi * UiScale;
        var top = Island.PointToScreen(new Point(islandW / 2, 0));
        // moving (dragged, or floating to its place): a wider picture, also above the island where the screen goes on,
        // grabbed at the open rate; each picture is shifted back by how far the island went since (PlaceFrost)
        bool moving = _dragging || _posActive;
        double margin = moving ? FrostMoveMargin : FrostMargin;
        // (in steps of 16: a picture of a new size each frame would mean a new bitmap each frame)
        double above = moving ? Math.Clamp(Math.Floor((top.Y - SystemParameters.VirtualScreenTop * Dpi) / k / 16) * 16, 0, FrostMoveMargin) : 0;
        var want = new FrostWant(top.X - wr.Left, top.Y - wr.Top, islandW / 2 + margin, above + islandH + margin + DripRoom, above, islandW, islandH,
                                 k, LiquidGlass ? 2 : 4, _excluded != true, _mode != Mode.Compact || moving, _frostGen);
        var was = _frostWant;
        if (want == was) return;
        _frostWant = want;
        // the loop is woken for a new size or a new start; a move (the island bobbing with the music) waits for its next grab
        if (was == null || want.Fast != was.Fast || want.Gen != was.Gen || Math.Abs(want.IslandW - was.IslandW) > 0.5 || Math.Abs(want.IslandH - was.IslandH) > 0.5) _frostWake.Set();
    }

    /// <summary>How far the island may move between two pictures (20 a second) before the picture no longer covers it.</summary>
    private const double FrostMoveMargin = 96;

    private long _frostStillAt;
    private int _frostGen;

    /// <summary>The loop stops; when it starts again its first picture is shown even if nothing changed behind.</summary>
    private void PauseFrost()
    {
        if (_frostWant == null) return;
        _frostWant = null;
        _frostGen++;
    }

    // ---------------------------------------------------------------- the grab loop

    /// <summary>
    /// What the loop grabs: the island's top centre in pixels from the window's corner (the window's own place is
    /// read at every grab, so a moved window needs nothing new), the picture's size in DIP (Above of it over the
    /// island's top), the island's size, DIP → pixels, the shrink factor, through the magnifier or not, the fast rate.
    /// </summary>
    private sealed record FrostWant(double OffX, double OffY, double Half, double Height, double Above, double IslandW, double IslandH, double K, int Div, bool Mag, bool Fast, int Gen)
    {
        public bool SameSize(FrostWant o) => Math.Abs(Half - o.Half) < 0.5 && Math.Abs(Height - o.Height) < 0.5 && Above == o.Above && Math.Abs(IslandW - o.IslandW) < 0.5 && Math.Abs(IslandH - o.IslandH) < 0.5
                                             && K == o.K && Div == o.Div && Mag == o.Mag && Gen == o.Gen;
    }

    /// <summary>A grabbed picture (At: the island's top centre on the screen when it was taken); three go round, no new arrays per frame.</summary>
    private sealed class FrostFrame { public byte[] Px = []; public int W, H; public long Sig; public Point At; public FrostWant Want = null!; }

    private volatile FrostWant? _frostWant;
    private readonly AutoResetEvent _frostWake = new(false);
    private readonly System.Collections.Concurrent.ConcurrentQueue<FrostFrame> _frostFree = new();
    private FrostFrame? _frostPending;
    private Thread? _frostLoop;

    // up to 20 pictures a second open, 4–5 folded, under 2 while nothing behind changes; the magnifier at most 8
    private const int FrostOpenMs = 50, FrostFoldedMs = 220, FrostStillMs = 600, FrostMagMs = 125, FrostStillAfter = 10;

    private void FrostLoop()
    {
        int same = 0;
        long lastSig = 0;
        FrostWant? lastWant = null;
        var lastRect = Drawing.Rectangle.Empty;
        while (!_shutDown)
        {
            var want = _frostWant;
            if (want == null) { lastWant = null; _frostWake.WaitOne(); continue; }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!_frostFree.TryDequeue(out var f)) { _frostWake.WaitOne(20); continue; }
            bool handed = false;
            try
            {
                if (!Native.GetWindowRect(_hwnd, out var wr)) continue;
                var at = new Point(wr.Left + want.OffX, wr.Top + want.OffY);
                var rect = new Drawing.Rectangle((int)(at.X - want.Half * want.K), (int)(at.Y - want.Above * want.K), (int)(want.Half * 2 * want.K), (int)(want.Height * want.K));
                int w = Math.Max(1, rect.Width / want.Div), h = Math.Max(1, rect.Height / want.Div);
                if (f.Px.Length != w * h * 4) f.Px = new byte[w * h * 4];
                byte[]? px;
                if (want.Mag)
                {
                    var t = MagCapture.CaptureAsync(_hwnd, rect, w, h, f.Px);
                    px = t.Wait(1000) ? t.Result : null;
                }
                else px = CaptureService.CaptureSmall(rect, w, h, f.Px);
                Interlocked.Add(ref _frostGrabTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
                Interlocked.Increment(ref _frostGrabCount);
                if (px == null || _shutDown) continue;
                long sig = w * 73856093L ^ h * 19349663L;
                for (int i = 0; i < px.Length; i += 16) sig = sig * 31 + px[i] + (px[i + 1] << 8) + (px[i + 2] << 16);
                same = sig == lastSig && rect == lastRect && lastWant != null && want.SameSize(lastWant) ? same + 1 : 0;
                lastSig = sig;
                lastWant = want;
                lastRect = rect;
                if (same > 0) continue;   // nothing changed behind: nothing to show
                f.Px = px; f.W = w; f.H = h; f.Sig = sig; f.Want = want; f.At = new Point(rect.X + want.Half * want.K, rect.Y + want.Above * want.K);
                // the newest picture wins: one the UI hasn't taken yet is dropped
                var old = Interlocked.Exchange(ref _frostPending, f);
                handed = true;
                if (old != null) _frostFree.Enqueue(old);
                else Dispatcher.BeginInvoke(DispatcherPriority.Render, _showFrostFrame!);
            }
            catch (Exception e) { Log.Error("glass loop", e); }
            finally
            {
                if (!handed) _frostFree.Enqueue(f);
                int every = same >= FrostStillAfter ? FrostStillMs : want.Fast ? FrostOpenMs : FrostFoldedMs;
                if (want.Mag) every = Math.Max(every, FrostMagMs);
                int spent = (int)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000 / System.Diagnostics.Stopwatch.Frequency);
                if (!_shutDown) _frostWake.WaitOne(Math.Max(1, every - spent));
            }
        }
    }

    private Action? _showFrostFrame;

    /// <summary>UI thread: the newest picture from the loop, if the island still wants it.</summary>
    private void ShowFrostFrame()
    {
        var f = Interlocked.Exchange(ref _frostPending, null);
        if (f == null) return;
        try
        {
            var want = f.Want;
            if (_frostWant == null || !IslandGlass || Lite || _shutDown || _hidden || _fullscreenHidden || _gameFront) return;
            UiStep("glass");
            Measure("frost show", () => ShowFrost(f.Px, f.W, f.H, want.Half, want.Height, want.IslandW, want.IslandH, f.At, want.Above, f.Sig));
        }
        finally { _frostFree.Enqueue(f); }
    }

    /// <summary>
    /// The island is about to open (the cursor reached it, or it starts growing): take the backdrop for its
    /// coming size now, before anything moves — the glass then stays see-through all the way, instead of
    /// turning solid (grey) for the moment until the picture is retaken after the animation.
    /// </summary>
    private bool PrecaptureFrost(Size coming)
    {
        if (ClearGlass) return true;   // nothing to take: what is behind shows by itself
        if (!IslandGlass || Lite || !FrostCanGrab || _hidden || _fullscreenHidden || _gameFront || !IsLoaded || !Island.IsVisible || _dragging || _posActive) return false;
        double iw = Math.Max(coming.Width, Island.ActualWidth), ih = Math.Max(coming.Height, Island.ActualHeight);
        if (_frostCovers && iw / 2 + FrostMargin <= _frostHalf + 0.5 && ih + FrostMargin + DripRoom <= _frostHeight + 0.5) return true;   // already big enough
        // waits briefly for the picture: opening without it would show the island solid (grey) for a moment
        return GrabFrost(iw, ih, waitMs: 40);
    }

    /// <summary>A grab is on its way (one at a time; its buffer is _frostPixels).</summary>
    private bool _frostBusy;

    /// <summary>
    /// Grab and show the backdrop for an island of this size (centred where the island is). The grab itself
    /// runs off the UI thread; with waitMs the picture is shown at once if it comes in time, otherwise
    /// whenever it arrives. True: the picture is shown now.
    /// </summary>
    private bool GrabFrost(double islandW, double islandH, int waitMs = 0) => Measure("frost grab", () => GrabFrostNow(islandW, islandH, waitMs));

    private bool GrabFrostNow(double islandW, double islandH, int waitMs)
    {
        UiStep("glass");
        try
        {
            // only the island and a thin margin, at a quarter of the resolution (it is blurred anyway),
            // into one reused bitmap - no new image (and no garbage) every time
            double half = islandW / 2 + FrostMargin, height = islandH + FrostMargin + DripRoom;
            var top = Island.PointToScreen(new Point(Island.ActualWidth / 2, 0));
            double dpi = Dpi;
            double k = dpi * UiScale;
            var rect = new Drawing.Rectangle((int)(top.X - half * k), (int)top.Y, (int)(half * 2 * k), (int)(height * k));
            // liquid glass bends the picture at the edge: it needs a sharper grab than plain frost
            int div = LiquidGlass ? 2 : 4;
            int w = Math.Max(1, rect.Width / div), h = Math.Max(1, rect.Height / div);
            if (_frostTestBackdrop != null || _demoPlate != null)
            {
                var still = _frostTestBackdrop != null ? _frostTestBackdrop(w, h) : CaptureService.CropSmall(_demoPlate!, _demoPlateAt, rect, w, h, _frostPixels);
                if (still == null) return false;
                if (_frostTestBackdrop == null) _frostPixels = still;
                return ShowFrost(still, w, h, half, height, islandW, islandH, top, 0);
            }
            if (_shutDown || _frostBusy) return false;
            _frostBusy = true;
            var buffer = _frostPixels;
            long g0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Task<byte[]?> grab = (_excluded == true
                ? Task.Run(() => CaptureService.CaptureSmall(rect, w, h, buffer))
                : MagCapture.CaptureAsync(_hwnd, rect, w, h, buffer)).ContinueWith(t =>
                {
                    Interlocked.Add(ref _frostGrabTicks, System.Diagnostics.Stopwatch.GetTimestamp() - g0);
                    Interlocked.Increment(ref _frostGrabCount);
                    return t.Result;
                }, TaskContinuationOptions.ExecuteSynchronously);
            if (waitMs > 0 && grab.Wait(waitMs))
            {
                _frostBusy = false;
                if (grab.Result is not { } now) return false;
                _frostPixels = now;
                return ShowFrost(now, w, h, half, height, islandW, islandH, top, 0);
            }
            grab.ContinueWith(t =>
            {
                _frostBusy = false;
                if (t.IsFaulted) { Log.Write("glass: grab failed: " + t.Exception?.InnerException?.Message); return; }
                // the island may have been dragged, hidden or switched to solid while the picture was taken
                if (t.Result is not { } later) return;
                _frostPixels = later;
                if (!IslandGlass || Lite || _shutDown || _hidden || _fullscreenHidden) return;
                Measure("frost show", () => ShowFrost(later, w, h, half, height, islandW, islandH, top, 0));
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return false;
        }
        catch (Exception e)
        {
            _frostBusy = false;
            Log.Write("glass: " + e.Message);
            return false;
        }
    }

    /// <summary>at: the island's top centre on the screen (pixels) when the picture was taken; above: DIP of picture over it.</summary>
    private bool ShowFrost(byte[] px, int w, int h, double half, double height, double islandW, double islandH, Point at, double above, long sig = 0)
    {
        if (sig == 0)
        {
            sig = w * 73856093L ^ h * 19349663L;
            for (int i = 0; i < px.Length; i += 16) sig = sig * 31 + px[i] + (px[i + 1] << 8) + (px[i + 2] << 16);
        }
        bool sizeChanged = _frostBmp == null || _frostBmp.PixelWidth != w || _frostBmp.PixelHeight != h;
        if (!sizeChanged && sig == _frostSig && Math.Abs(half - _frostHalf) < 0.5 && Math.Abs(height - _frostHeight) < 0.5 && at == _layerAt[_frostFront] && FrostHost.Opacity > 0.5)
        {
            SetFrostCovers(true);
            return true;   // nothing changed behind
        }
        _frostSig = sig;
        _frostShownCount++;
        MeasureBackdrop(px);

        // the new picture goes into the other layer, on top, and fades in over the one shown now
        FinishFrostFade();
        bool sameGeometry = _frostCovers && FrostHost.Opacity > 0.5 && Math.Abs(half - _frostHalf) < 0.5 && Math.Abs(height - _frostHeight) < 0.5 && above == _layerAbove[_frostFront];
        int next = 1 - _frostFront;
        var layers = FrostLayers;
        var layer = layers[next];
        if (_frostBmps[next] is not { } bmp || bmp.PixelWidth != w || bmp.PixelHeight != h)
        {
            bmp = _frostBmps[next] = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgr32, null);
            layer.Source = bmp;
        }
        ApplyLiquidBlur();
        var shown = px;
        if (LiquidGlass && FrostLens.Effect == null)
        {
            double sb = w / (half * 2);   // DIP → grab pixels
            shown = Refract(px, w, h, (half - islandW / 2) * sb, islandW * sb, islandH * sb, Radius(islandH) * sb);
        }
        bmp.WritePixels(new Int32Rect(0, 0, w, h), shown, w * 4, 0);
        _frostFront = next;
        _layerHalf[next] = half;
        _layerAt[next] = at;
        _layerAbove[next] = above;
        _frostHalf = half;
        _frostHeight = height;
        layer.Width = half * 2;
        layer.Height = height;
        PlaceFrost(Island.ActualWidth, Island.ActualHeight);
        Panel.SetZIndex(layer, 1);
        Panel.SetZIndex(layers[1 - next], 0);
        // 120–180 ms when pictures come slowly, shorter when they come faster than that; at once for a new size
        // (the old picture may not cover it), with animations turned off in Windows, and folded (the pill is nearly
        // opaque: a fade there cost more than it showed)
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double since = (now - _frostShownAt) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _frostShownAt = now;
        double fade = sameGeometry && !Services.Motion.Reduced && _mode != Mode.Compact ? Math.Min(150, since * 0.8) : 0;
        if (fade < 16) { layer.Opacity = 1; layers[1 - next].Opacity = 0; }
        else
        {
            _frostFading = true;
            _frostFades++;
            _frostFadeMs += fade;
            int id = ++_frostFadeId;
            // a blend of two blurred pictures looks the same at 30 frames a second, for half the redraws of the window
            var a = Slow(new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(fade)), 30);
            a.Completed += (_, _) => { if (id == _frostFadeId) FinishFrostFade(); };
            layer.BeginAnimation(OpacityProperty, a);
        }
        FrostHost.BeginAnimation(OpacityProperty, null);
        FrostHost.Opacity = 1;
        SetFrostCovers(true);
        return true;
    }

    // the glass's two picture layers: the newest is on top, fading in over the one before
    private Image[]? _frostLayersCached;
    private Image[] FrostLayers => _frostLayersCached ??= [Frost, FrostNext];
    private readonly WriteableBitmap?[] _frostBmps = new WriteableBitmap?[2];
    private readonly double[] _layerHalf = [200, 200], _layerAbove = [0, 0];
    private readonly Point[] _layerAt = new Point[2];
    private int _frostFront, _frostFadeId;
    private bool _frostFading;
    private long _frostShownAt;
    private WriteableBitmap? _frostBmp => _frostBmps[_frostFront];

    /// <summary>A fade still running ends now: the newest picture fully shown, the one under it hidden.</summary>
    private void FinishFrostFade()
    {
        if (!_frostFading) return;
        _frostFading = false;
        _frostFadeId++;
        var layers = FrostLayers;
        layers[_frostFront].BeginAnimation(OpacityProperty, null);
        layers[_frostFront].Opacity = 1;
        layers[1 - _frostFront].Opacity = 0;
    }

    /// <summary>Called whenever the outline changes: keep frost and aura glued to the shape.</summary>
    private void ShapeChanged(Geometry g, double width)
    {
        // the shadow follows the outline; only the part outside the island is drawn
        IslandShadowPath.Data = g;
        ShadowHost.Width = width;
        ShadowHost.Height = Math.Max(1, Island.Height);
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-200, -200, width + 400, Island.Height + 400)), g);
        outside.Freeze();
        ShadowHost.Clip = outside;
        FrostHost.Clip = g;
        PhotoHost.Clip = g;
        Season.Clip = g;
        Season.Outline = g;
        if (Season.Kind == "liquid" && Season.IsVisible) Season.InvalidateVisual();   // its rim light is drawn along this outline
        Season.Width = Math.Max(1, width);
        Season.Height = Math.Max(1, Island.Height);
        PhotoImage.Width = Math.Max(1, width);
        PhotoImage.Height = Math.Max(1, Island.Height);
        PlaceFrost(width, Island.Height);
        CaptureFrost();
        AuraHost.Clip = g;
        AuraFill.Width = width;
        AuraFill.Height = Island.Height + 12;
        AuraLine.Data = g;
        AuraLine.Clip = g;   // only the inner half of the edge line shows: it's part of the island
    }

    // ================================================================ aura

    private void SetupAura()
    {
        var aura = Auras.FirstOrDefault(a => a.Id == _settings.Aura);
        // the Claude Code character brings its own warm orange light
        if (_settings.Character is "claude" or "clawd" && _settings.ClaudeGlow) aura = ("claude", "Claude", ["#D97757", "#F0A27A", "#C2552F"]);
        if (aura.Colors == null || aura.Colors.Length == 0)
        {
            _auraBrush = null;
            UpdateAuraVisibility();
            return;
        }

        // colours repeat so the band can slide forever without a seam
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(0.5, 0.5), SpreadMethod = GradientSpreadMethod.Repeat };
        var cols = aura.Colors.Append(aura.Colors[0]).ToArray();
        for (int i = 0; i < cols.Length; i++)
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(cols[i]), (double)i / (cols.Length - 1)));
        var shift = new TranslateTransform();
        brush.RelativeTransform = shift;
        shift.BeginAnimation(TranslateTransform.XProperty, Slow(new DoubleAnimation(0, 0.5, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever }));
        _auraBrush = brush;
        _auraPaused = false;

        // light rises from the bottom edge and dies out before the middle
        var mask = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.5));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 0.85));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0, 0, 0), 1));
        mask.Freeze();
        var lineMask = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        lineMask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.45));
        lineMask.GradientStops.Add(new GradientStop(Colors.Black, 1));
        lineMask.Freeze();

        AuraFill.Fill = brush;
        AuraFill.OpacityMask = mask;
        AuraLine.Stroke = brush;
        AuraLine.OpacityMask = lineMask;
        // slow breathing
        AuraFill.BeginAnimation(OpacityProperty, Slow(new DoubleAnimation(0.45, 0.9, TimeSpan.FromSeconds(3))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        }));
        UpdateAuraVisibility();
    }

    private bool _auraThinking;

    /// <summary>While Claude Code waits for you, its orange light breathes faster.</summary>
    private void UpdateAuraMood(bool thinking)
    {
        if (_settings.Character is not ("claude" or "clawd") || !_settings.ClaudeGlow || thinking == _auraThinking) return;
        _auraThinking = thinking;
        AuraFill.BeginAnimation(OpacityProperty, Slow(new DoubleAnimation(thinking ? 0.5 : 0.45, thinking ? 1 : 0.9, TimeSpan.FromSeconds(thinking ? 0.8 : 3))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        }));
    }

    /// <summary>
    /// Endless animations make WPF redraw the whole window at 60 fps for as long as they run. A soft,
    /// blurred glow looks the same at 15 frames a second for a quarter of the work.
    /// </summary>
    private static T Slow<T>(T timeline, int fps = 12) where T : Timeline
    {
        Timeline.SetDesiredFrameRate(timeline, fps);
        return timeline;
    }

    private bool _auraPaused;

    private void UpdateAuraVisibility()
    {
        // never over a game or a full-screen video: the glow would cost frames there
        bool show = _auraBrush != null && !Lite && _mode == Mode.Compact && !_recording.IsRecording && !_gameMode && !_fullscreenHidden && !_hidden;
        if (show && _auraPaused) { _auraPaused = false; SetupAura(); return; }   // starts the glow again
        if (!show && _auraBrush != null && !_auraPaused)
        {
            // a hidden glow still ticks its animations (and WPF keeps drawing): stop them
            _auraPaused = true;
            AuraFill.BeginAnimation(OpacityProperty, null);
            (_auraBrush.RelativeTransform as TranslateTransform)?.BeginAnimation(TranslateTransform.XProperty, null);
        }
        var v = show ? Visibility.Visible : Visibility.Collapsed;
        if (AuraLine.Visibility == v) return;
        AuraLine.Visibility = AuraHost.Visibility = v;
        if (show) AuraHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)));
    }

    // ================================================================ header: page dots

    private bool _iconsShown;

    private void UpdatePageDots()
    {
        if (DotsRow == null) return;
        if (DotsRow.Children.Count == 0)
        {
            foreach (var tab in PageTabs)
            {
                var dot = new Border { Height = 6, Width = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0), Cursor = Cursors.Hand, Tag = tab };
                var t = tab;
                dot.MouseLeftButtonUp += (_, _) => t.IsChecked = true;
                DotsRow.Children.Add(dot);
            }
        }
        var current = PageTabs.FirstOrDefault(t => t.IsChecked == true);
        foreach (Border dot in DotsRow.Children)
        {
            bool on = dot.Tag == current;
            dot.SetResourceReference(Border.BackgroundProperty, on ? "Fg" : "Fg3");
            dot.BeginAnimation(WidthProperty, new DoubleAnimation(on ? 18 : 6, TimeSpan.FromMilliseconds(320)) { EasingFunction = new SpringEase { DampingRatio = 0.6 } });
        }
        PageTitle.Text = current?.Content as string ?? (TabSettings.IsChecked == true ? L.T("Настройки") : "");
    }

    private void ApplyTabIconsMode()
    {
        bool always = _settings.TabIcons == "always";
        ShowTabIcons(always, animate: false);
    }

    private readonly DispatcherTimer _iconsHide = new() { Interval = TimeSpan.FromMilliseconds(160) };

    private void TabArea_MouseEnter(object sender, MouseEventArgs e)
    {
        _iconsHide.Stop();
        if (_settings.TabIcons != "always") ShowTabIcons(true, animate: true);
    }

    private void TabArea_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_settings.TabIcons == "always") return;
        // a short grace period: crossing a gap between icons must not flicker the strip
        _iconsHide.Tick -= HideIconsTick;
        _iconsHide.Tick += HideIconsTick;
        _iconsHide.Start();
    }

    private void HideIconsTick(object? sender, EventArgs e)
    {
        _iconsHide.Stop();
        if (!TabArea.IsMouseOver) ShowTabIcons(false, animate: true);
    }

    /// <summary>Dots melt into icons one after another (and back) - opacity, scale and a little drift together.</summary>
    private void ShowTabIcons(bool show, bool animate)
    {
        if (_iconsShown == show && animate) return;
        _iconsShown = show;
        TabStrip.IsHitTestVisible = show;
        TabStrip.Opacity = 1;
        TabStripShift.X = 0;

        var tabs = PageTabs;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var soft = new SineEase { EasingMode = EasingMode.EaseInOut };
        for (int i = 0; i < tabs.Length; i++)
        {
            var tab = tabs[i];
            if (tab.RenderTransform is not ScaleTransform st || st.IsFrozen)
            {
                st = new ScaleTransform(1, 1);
                tab.RenderTransform = st;
                tab.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            int order = show ? i : tabs.Length - 1 - i;
            var begin = TimeSpan.FromMilliseconds(animate ? order * (show ? 16 : 10) : 0);
            var dur = TimeSpan.FromMilliseconds(animate ? (show ? 300 : 200) : 0);
            tab.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, dur) { BeginTime = begin, EasingFunction = soft });
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(show ? 1 : 0.55, dur) { BeginTime = begin, EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(show ? 1 : 0.55, dur) { BeginTime = begin, EasingFunction = ease });
        }

        // dots shrink away in the same rhythm, the page name slides aside
        for (int i = 0; i < DotsRow.Children.Count; i++)
        {
            var dot = (UIElement)DotsRow.Children[i];
            int order = show ? i : DotsRow.Children.Count - 1 - i;
            dot.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 0 : 1, TimeSpan.FromMilliseconds(animate ? 220 : 0))
            {
                BeginTime = TimeSpan.FromMilliseconds(animate ? order * 12 + (show ? 0 : 90) : 0), EasingFunction = soft,
            });
        }
        PageTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 0 : 1, TimeSpan.FromMilliseconds(animate ? 240 : 0))
        {
            BeginTime = TimeSpan.FromMilliseconds(show || !animate ? 0 : 140), EasingFunction = soft,
        });
    }

    // ================================================================ settings: sidebar, look controls

    private void BuildSettingsNav()
    {
        bool first = true;
        foreach (StackPanel section in SettingsSections.Children)
        {
            var parts = ((string)section.Tag).Split('|');
            var nav = new RadioButton { Style = (Style)FindResource("SideNav"), GroupName = "SettingsNav", Content = parts[0], Tag = parts[1] };
            var sec = section;
            nav.Checked += (_, _) =>
            {
                foreach (UIElement s in SettingsSections.Children) s.Visibility = Visibility.Collapsed;
                SettingsScroll.ScrollToTop();
                Reveal(sec, 0.99, 0, 0);
            };
            SettingsNav.Children.Add(nav);
            if (first) { nav.IsChecked = true; first = false; }
        }
    }

    private void BuildAuraChips()
    {
        foreach (var (id, name, colors) in Auras)
        {
            var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 7, 0) };
            if (colors.Length > 0)
            {
                var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(colors[0]), (Color)ColorConverter.ConvertFromString(colors[^1]), 0);
                g.GradientStops.Insert(1, new GradientStop((Color)ColorConverter.ConvertFromString(colors[1]), 0.5));
                dot.Background = g;
            }
            else
            {
                dot.BorderThickness = new Thickness(1.2);
                dot.SetResourceReference(Border.BorderBrushProperty, "Fg3");
            }
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(dot);
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var chip = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "Aura", Content = content, Tag = id, Margin = new Thickness(0, 0, 4, 4) };
            chip.Checked += (_, _) =>
            {
                if (_settingsLoading) return;
                _settings.Aura = id;
                _settings.Save();
                SetupAura();
            };
            AuraChips.Children.Add(chip);
        }
    }

    private void RefreshLookControls()
    {
        foreach (var card in new[] { StyleDocked, StyleFloating, StyleSide, StyleTaskbar })
        {
            bool on = (string)card.Tag == PlacementTag;
            if (on) card.SetResourceReference(Border.BorderBrushProperty, "AccentBlue");
            else card.BorderBrush = Brushes.Transparent;
        }
        GlassSwitch.IsChecked = _settings.Glass;
        TintRow.Visibility = _settings.Glass ? Visibility.Visible : Visibility.Collapsed;
        bool liquid = GlassScene;
        if (liquid) TintRow.Visibility = Visibility.Collapsed;   // liquid glass has its own slider, and one even pane
        LiquidTintSlider.Visibility = liquid && _settings.Glass ? Visibility.Visible : Visibility.Collapsed;
        LiquidTintSlider.Label = L.T("Плотность стекла");
        LiquidTintSlider.Format = v => $"{v:0}%";
        LiquidTintSlider.Value = SceneTint;
        FadeSwitch.Visibility = liquid ? Visibility.Collapsed : Visibility.Visible;
        TintLabel.Text = $"{_settings.GlassTint}%";
        FadeSwitch.IsChecked = _settings.BottomFade;
        BounceSwitch.IsChecked = _settings.Bouncy;
        (_settings.FastAnimations ? SpeedFast : SpeedNormal).IsChecked = true;
        (_settings.TabIcons == "always" ? IconsAlways : IconsHover).IsChecked = true;
        foreach (RadioButton chip in AuraChips.Children)
            chip.IsChecked = (string)chip.Tag == _settings.Aura;
    }

    private void StyleCard_Click(object sender, MouseButtonEventArgs e)
    {
        SetPlacement((string)((FrameworkElement)sender).Tag);
        RefreshLookControls();
        PlayChromatic(5);
    }

    private void Glass_Click(object sender, RoutedEventArgs e)
    {
        _settings.Glass = GlassSwitch.IsChecked == true;
        _settings.Save();
        ApplyGlass();
        RefreshLookControls();
        Remeasure();
    }

    private void TintMinus_Click(object sender, RoutedEventArgs e) => ChangeTint(-5);
    private void TintPlus_Click(object sender, RoutedEventArgs e) => ChangeTint(5);

    private void LiquidTint_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        if (_settingsLoading || !IsLoaded || v == SceneTint) return;
        SceneTint = v;
        _settings.SaveSoon();
        UpdateGlassFill();
    }

    private void ChangeTint(int delta)
    {
        _settings.GlassTint = Math.Clamp(_settings.GlassTint + delta, 20, 100);
        _settings.Save();
        TintLabel.Text = $"{_settings.GlassTint}%";
        ApplyGlass();
    }

    private void Fade_Click(object sender, RoutedEventArgs e)
    {
        _settings.BottomFade = FadeSwitch.IsChecked == true;
        _settings.Save();
        ApplyGlass();
    }

    private void Bounce_Click(object sender, RoutedEventArgs e)
    {
        _settings.Bouncy = BounceSwitch.IsChecked == true;
        _settings.Save();
        if (_settings.Bouncy) Reveal(SettingsSections, 0.97, 0, 0); // show what it feels like
    }

    private void Speed_Checked(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading || SpeedFast == null) return;
        _settings.FastAnimations = SpeedFast.IsChecked == true;
        _settings.Save();
    }

    private void Icons_Checked(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading || IconsAlways == null) return;
        _settings.TabIcons = IconsAlways.IsChecked == true ? "always" : "hover";
        _settings.Save();
        ApplyTabIconsMode();
    }
}

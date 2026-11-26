using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;
using Forms = System.Windows.Forms;

namespace DynamicIsland;

/// <summary>
/// Where the island sits on its screen. It is never cut by the screen's edge: wherever the user put it, it is
/// fitted onto its screen for its size right now, so it opens inward near an edge and goes back to its place
/// when it folds (the place itself never moves). Besides floating and the notch at the top edge, it can be
/// attached to the left or right side (an upright pill that opens outward) or sit on the taskbar (it grows
/// upward, rises with a taskbar that hides itself, and keeps out of the way of Start and the other flyouts).
/// The window keeps the island at its top centre; only the window moves.
/// </summary>
public partial class MainWindow
{
    private enum Edge { Free, Top, Bottom, Left, Right }

    private Edge _edge;
    /// <summary>The edge it is attached to (the screen's side or the taskbar's inner side), the work area and the whole screen, in DIP.</summary>
    private double _edgeLine;
    private Rect _edgeArea, _edgeScreen;
    private IntPtr _edgeTaskbar;

    private bool Vertical => _edge is Edge.Left or Edge.Right;
    private bool Attached => _edge != Edge.Free;
    private const double EdgeGap = 8, EditLift = 72;

    private double IslandW => (double.IsNaN(Island.Width) ? Island.ActualWidth : Island.Width) * UiScale;
    private double IslandH => (double.IsNaN(Island.Height) ? Island.ActualHeight : Island.Height) * UiScale;

    /// <summary>The folded pill's outline for this length: upright on a side.</summary>
    private Size PillSize(double length) => Vertical ? new Size(CompactHeight, Math.Min(length, 360)) : new Size(length, CompactHeight);

    private static double Fit(double v, double lo, double hi) => hi < lo ? (lo + hi) / 2 : Math.Clamp(v, lo, hi);

    /// <summary>A point on the screen the island belongs to (DIP).</summary>
    private Point Anchor()
    {
        if (_settings.Attach is "left" or "right" or "taskbar" && _settings.AttachAt is { Length: 2 } at) return new Point(at[0], at[1]);
        var (cx, top) = PlaceOf(_placeOwner);
        return new Point(cx, top + 20);
    }

    /// <summary>Works out the edge, its screen and the taskbar from the settings (cheap: a few calls into Windows).</summary>
    private void UpdateEdge()
    {
        double k = Dpi;
        var a = Anchor();
        var s = Forms.Screen.FromPoint(new System.Drawing.Point((int)(a.X * k), (int)(a.Y * k)));
        Rect R(System.Drawing.Rectangle r) => new(r.X / k, r.Y / k, r.Width / k, r.Height / k);
        _edgeScreen = R(s.Bounds);
        _edgeArea = R(s.WorkingArea);
        var was = _edge;
        _edgeTaskbar = IntPtr.Zero;
        switch (_settings.Attach)
        {
            case "left": _edge = Edge.Left; _edgeLine = _edgeArea.Left; break;
            case "right": _edge = Edge.Right; _edgeLine = _edgeArea.Right; break;
            case "taskbar":
                (_edge, _edgeLine, _edgeTaskbar) = TaskbarEdge(s.Bounds, k);
                // an auto-hiding taskbar leaves the whole screen as the work area: keep the island off the strip it slides in from
                if (_edge == Edge.Bottom) _edgeArea.Height = Math.Max(0, Math.Min(_edgeArea.Bottom, _edgeLine) - _edgeArea.Top);
                break;
            default: _edge = _settings.Docked ? Edge.Top : Edge.Free; _edgeLine = _edgeScreen.Top; break;
        }
        if (was != _edge) ApplyEdgeLayout();
    }

    /// <summary>
    /// The taskbar of this screen: which side it is on and its inner side (where the island sits), in DIP. Hidden
    /// (auto-hide), only the thin strip it leaves counts, so the island rises with it when it slides in.
    /// </summary>
    private static (Edge Edge, double Line, IntPtr Bar) TaskbarEdge(System.Drawing.Rectangle screen, double k)
    {
        foreach (var bar in Native.Taskbars())
        {
            if (!Native.GetWindowRect(bar, out var r) || !Native.IsWindowVisible(bar)) continue;
            var rect = System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            var on = System.Drawing.Rectangle.Intersect(rect, screen);
            if (on.Width <= 0 || on.Height <= 0) continue;
            // the bar's long side tells along which edge it lies
            bool flat = rect.Width >= rect.Height;
            if (flat && Math.Abs(rect.Width - screen.Width) > screen.Width / 3) continue;
            if (!flat && Math.Abs(rect.Height - screen.Height) > screen.Height / 3) continue;
            if (flat) return rect.Top + rect.Height / 2 < screen.Top + screen.Height / 2 ? (Edge.Top, on.Bottom / k, bar) : (Edge.Bottom, on.Top / k, bar);
            return rect.Left + rect.Width / 2 < screen.Left + screen.Width / 2 ? (Edge.Left, on.Right / k, bar) : (Edge.Right, on.Left / k, bar);
        }
        return (Edge.Bottom, screen.Bottom / k, IntPtr.Zero);   // no taskbar on this screen: its bottom edge
    }

    /// <summary>Where the window goes for an island of this size (DIP): wholly on its screen, at its edge or place.</summary>
    private (double X, double Y) PlaceFor(double w, double h)
    {
        double half = (Width - w) / 2, ear = Attached ? 9 * UiScale : 0;
        var a = Anchor();
        switch (_edge)
        {
            case Edge.Left or Edge.Right:
            {
                double cy = Fit(a.Y, _edgeArea.Top + h / 2 + EdgeGap, _edgeArea.Bottom - h / 2 - EdgeGap);
                return (_edge == Edge.Left ? _edgeLine - half : _edgeLine - w - half, cy - h / 2);
            }
            case Edge.Bottom:
            {
                double cx = Fit(a.X, _edgeArea.Left + w / 2 + ear, _edgeArea.Right - w / 2 - ear);
                return (cx - Width / 2, _edgeLine - h - (_editMode ? EditLift : 0));
            }
            case Edge.Top:
            {
                double cx = Fit(a.X, _edgeScreen.Left + w / 2 + ear, _edgeScreen.Right - w / 2 - ear);
                return (cx - Width / 2, _edgeLine);
            }
            default:
            {
                double cx = Fit(a.X, _edgeArea.Left + w / 2 + EdgeGap, _edgeArea.Right - w / 2 - EdgeGap);
                double top = Fit(a.Y - 20 + IslandTopMargin, _edgeScreen.Top + IslandTopMargin, _edgeArea.Bottom - h - EdgeGap);
                return (cx - Width / 2, top - IslandTopMargin);
            }
        }
    }

    /// <summary>
    /// Puts the window where the island belongs for its size right now: every frame while it changes size, and
    /// whenever the screen, the taskbar or the settings change. Floating to a new place, the springs carry it there.
    /// </summary>
    private void FollowEdge(bool animate = false)
    {
        if (_hidden || _dragging || _hwnd == IntPtr.Zero) return;
        var (x, y) = PlaceFor(IslandW, IslandH);
        ClipToEdge();
        if (animate && !_posActive)
        {
            if (Math.Abs(_px.Value - x) < 0.5 && Math.Abs(_py.Value - y) < 0.5) return;
            _px.Snap(_px.Value); _py.Snap(_py.Value);
            _posActive = true;
            StartRendering();
        }
        if (_posActive) { _px.Target = x; _py.Target = y; return; }
        if (Math.Abs(_px.Value - x) < 0.3 && Math.Abs(_py.Value - y) < 0.3) return;
        _px.Snap(x); _py.Snap(y);
        Native.MoveTo(_hwnd, (int)Math.Round(x * Dpi), (int)Math.Round(y * Dpi));
        if (FrostHost.IsVisible) { PlaceFrost(Island.ActualWidth, Island.ActualHeight); CaptureFrost(); }
    }

    /// <summary>
    /// Nothing of the island is drawn past the edge it is attached to: the window reaches over it (onto the
    /// taskbar, or another screen beside), and the shadow would show there.
    /// </summary>
    private Rect _edgeClip = Rect.Empty;
    private void ClipToEdge()
    {
        double w = IslandW, h = IslandH, x0 = (Width - w) / 2, big = 100000;
        Rect clip = _hidden ? Rect.Empty : _edge switch
        {
            Edge.Left => new Rect(x0, -big, big, 2 * big),
            Edge.Right => new Rect(-big, -big, big + x0 + w, 2 * big),
            Edge.Bottom => new Rect(-big, -big, 2 * big, big + IslandTopMargin + h + (_editMode ? EditLift : 0)),
            _ => Rect.Empty,
        };
        if (clip == _edgeClip || (!clip.IsEmpty && !_edgeClip.IsEmpty && Math.Abs(clip.Left - _edgeClip.Left) + Math.Abs(clip.Right - _edgeClip.Right) + Math.Abs(clip.Bottom - _edgeClip.Bottom) < 0.3)) return;
        _edgeClip = clip;
        if (clip.IsEmpty) { Root.Clip = null; return; }
        var g = new RectangleGeometry(clip);
        g.Freeze();
        Root.Clip = g;
    }

    /// <summary>What changes with the edge: the folded content turns upright on a side, and the island squeezes toward its edge.</summary>
    private void ApplyEdgeLayout()
    {
        bool v = Vertical;
        CompactView.LayoutTransform = v ? new RotateTransform(_edge == Edge.Left ? -90 : 90) : Transform.Identity;
        CompactView.VerticalAlignment = v ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        CompactView.Margin = v ? new Thickness(0, 15, 0, 15) : new Thickness(15, 0, 15, 0);
        Island.RenderTransformOrigin = _edge switch
        {
            Edge.Left => new Point(0, 0.5),
            Edge.Right => new Point(1, 0.5),
            Edge.Bottom => new Point(0.5, 1),
            _ => new Point(0.5, 0),
        };
        Island.Margin = new Thickness(0, IslandTopMargin, 0, 0);
        ShadowHost.Margin = Island.Margin;
        _bodyX.Snap(0); _bodyY.Snap(0); _leadX.Snap(0); _leadY.Snap(0);
        if (Island.ActualWidth > 0) Island_SizeChanged(this, null!);
        UpdateGlassFill();
        if (_mode == Mode.Compact && IsLoaded) MorphTo(TargetSize(Mode.Compact));
        else UpdateShape(false);
    }

    /// <summary>The outline is drawn as if attached to the top edge, then turned to its own edge.</summary>
    private Geometry OnEdge(Geometry g, double w, double h)
    {
        var m = _edge switch
        {
            Edge.Bottom => new Matrix(1, 0, 0, -1, 0, h),
            Edge.Left => new Matrix(0, 1, 1, 0, 0, 0),
            Edge.Right => new Matrix(0, 1, -1, 0, w, 0),
            _ => Matrix.Identity,
        };
        if (m.IsIdentity) return g;
        return Geometry.Combine(g, Geometry.Empty, GeometryCombineMode.Union, new MatrixTransform(m));
    }

    /// <summary>The settings' "Position" cards and the island's menu: "dock", "float", "side", "left", "right" or "taskbar".</summary>
    private void SetPlacement(string tag)
    {
        var c = IslandCenter();
        switch (tag)
        {
            case "side":
                if (_settings.Attach is not ("left" or "right"))
                    _settings.Attach = c.X < _edgeScreen.Left + _edgeScreen.Width / 2 ? "left" : "right";
                _settings.AttachAt = [c.X, c.Y];
                break;
            case "left" or "right" or "taskbar":
                _settings.Attach = tag;
                _settings.AttachAt = [c.X, c.Y];
                break;
            default:
                _settings.Attach = "";
                _settings.Docked = tag == "dock";
                break;
        }
        if (_settings.Attach != "") _settings.Docked = false;
        _settings.Save();
        ApplyDock(animate: true);
        FollowEdge(animate: true);
    }

    private string PlacementTag => _settings.Attach switch
    {
        "left" or "right" => "side",
        "taskbar" => "taskbar",
        _ => _settings.Docked ? "dock" : "float",
    };

    private void PlacementMenu_Opened(object sender, RoutedEventArgs e)
    {
        string now = _settings.Attach is "left" or "right" ? _settings.Attach : PlacementTag;
        foreach (var item in PlacementMenu.Items.OfType<System.Windows.Controls.MenuItem>()) item.IsChecked = (string)item.Tag == now;
    }

    private void PlacementItem_Click(object sender, RoutedEventArgs e)
    {
        SetPlacement((string)((FrameworkElement)sender).Tag);
        RefreshLookControls();
    }

    /// <summary>The island's centre on the screen right now (DIP).</summary>
    private Point IslandCenter()
    {
        if (!IsLoaded || PresentationSource.FromVisual(Island) == null) return Anchor();
        var p = Island.PointToScreen(new Point(Island.ActualWidth / 2, Island.ActualHeight / 2));
        return new Point(p.X / Dpi, p.Y / Dpi);
    }

    private Point IslandCenterAt(double windowX, double windowY) => new(windowX + Width / 2, windowY + IslandTopMargin + IslandH / 2);

    /// <summary>
    /// The drag ended: where does the island belong now? Floating, its new place (with a little momentum); on a
    /// side, the side of its screen it was let go on; on the taskbar, the taskbar of that screen.
    /// </summary>
    private void DropAt(Vector v)
    {
        var at = IslandCenterAt(_px.Target, _py.Target);
        double k = Dpi;
        var s = Forms.Screen.FromPoint(new System.Drawing.Point((int)(at.X * k), (int)(at.Y * k)));
        var b = s.Bounds;
        double left = b.Left / k, right = b.Right / k, top = b.Top / k, bottom = b.Bottom / k;
        at = new Point(Math.Clamp(at.X + v.X * 0.07, left + 1, right - 1), Math.Clamp(at.Y + (_edge == Edge.Free ? v.Y * 0.05 : 0), top + 1, bottom - 1));
        switch (_settings.Attach)
        {
            case "left" or "right":
                _settings.Attach = at.X < (left + right) / 2 ? "left" : "right";
                _settings.AttachAt = [at.X, at.Y];
                break;
            case "taskbar":
                _settings.AttachAt = [at.X, at.Y];
                break;
            default:
            {
                double top0 = _settings.Docked ? top : at.Y - IslandH / 2 - IslandTopMargin;
                if (_placeOwner != null) _settings.AppPlaces[_placeOwner] = [at.X, top0];
                else { _settings.CenterX = at.X; _settings.Top = top0; }
                break;
            }
        }
        _settings.Save();
        UpdateEdge();
        var (x, y) = PlaceFor(IslandW, IslandH);
        _px.Target = x; _py.Target = y;
        ClipToEdge();
    }

    /// <summary>Following the cursor while dragged: floating, it gives way softly at the edge of the work area instead of going past it.</summary>
    private (double X, double Y) Rubber(double x, double y)
    {
        if (_edge != Edge.Free) return (x, y);
        var c = CursorDip();
        double k = Dpi;
        var a = Forms.Screen.FromPoint(new System.Drawing.Point((int)(c.X * k), (int)(c.Y * k))).WorkingArea;
        double w = IslandW, h = IslandH;
        static double R(double over) => 40 * over / (over + 40);   // at most 40 DIP past the edge
        double lo = a.Left / k + w / 2 + EdgeGap, hi = a.Right / k - w / 2 - EdgeGap, cx = x + Width / 2;
        if (hi >= lo) cx = cx < lo ? lo - R(lo - cx) : cx > hi ? hi + R(cx - hi) : cx;
        double top = y + IslandTopMargin, maxTop = a.Bottom / k - h - EdgeGap;   // no give at the top: pulled past it, it hides
        if (top > maxTop) top = maxTop + R(top - maxTop);
        return (cx - Width / 2, top - IslandTopMargin);
    }

    // ---------------------------------------------------------------- watching the screen

    private long _edgeTick;

    /// <summary>~30 times a second (the gaze loop) on the taskbar or a side: a taskbar sliding in or out, Start or another flyout opening.</summary>
    private void WatchEdge()
    {
        if (_edge is not (Edge.Bottom or Edge.Left or Edge.Right)) { if (_flyoutUp) SetFlyoutUp(false); return; }
        WatchFlyouts();
        if (_edgeTaskbar == IntPtr.Zero || _dragging || _hidden || ++_edgeTick % 2 != 0) return;
        double line = _edgeLine;
        UpdateEdge();
        if (Math.Abs(line - _edgeLine) > 0.5) FollowEdge(animate: true);
    }

    /// <summary>Every 0.6 s: the screens, the work area or the taskbar may have changed.</summary>
    private void WatchScreenEdge()
    {
        if (_hidden || _dragging || _hwnd == IntPtr.Zero) return;
        UpdateEdge();
        FollowEdge(animate: true);
    }

    // Start, search, the notification centre, quick settings: Windows opens them right where the island sits on
    // the taskbar or a side. While one is open over the island, the island fades away and lets the clicks through.
    private bool _flyoutUp;
    private IntPtr _flyoutSeen;
    private bool _flyoutSeenShell;
    private bool? _testFlyout;

    private void WatchFlyouts()
    {
        bool up = _testFlyout ?? false;
        if (_testFlyout == null)
        {
            var fg = Native.GetForegroundWindow();
            if (fg != _flyoutSeen)
            {
                _flyoutSeen = fg;
                _flyoutSeenShell = fg != IntPtr.Zero && Native.ClassName(fg) is "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow"
                    or "Shell_InputSwitchTopLevelWindow" or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland";
            }
            if (_flyoutSeenShell && Native.GetWindowRect(fg, out var r) && PresentationSource.FromVisual(Island) != null)
            {
                var island = new Rect(Island.PointToScreen(new Point(0, 0)), Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight)));
                island.Inflate(12, 12);
                up = island.IntersectsWith(new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top)));
            }
        }
        if (up != _flyoutUp) SetFlyoutUp(up);
    }

    private void SetFlyoutUp(bool up)
    {
        _flyoutUp = up;
        if (up)
        {
            if (_mode is Mode.Expanded) { _pinnedOpen = false; SetMode(Mode.Compact); }
            Root.IsHitTestVisible = false;
            Fade(Root, 0, 120, 0);
        }
        else if (!_fullscreenHidden)
        {
            Root.IsHitTestVisible = true;
            Fade(Root, _yield.Yielding ? 0.12 : 1, 200, 0);
        }
    }

#if DEBUG
    /// <summary>Test (--run edgetest, with --cover): floating near every edge, at the top edge, on both sides, on the taskbar; open and folded, dragged across, hidden and shown.</summary>
    private async Task RunEdgeTestAsync()
    {
        Settings.ReadOnly = true;
        Island.IsHitTestVisible = false; _settings.ExpandOnHover = false; NoClickOutside = true;
        _settings.YieldToCursor = false; _settings.PlacePerApp = false; _settings.FollowMonitor = false;
        await Task.Delay(4000);
        double k = Dpi;
        var scr = Forms.Screen.FromHandle(_hwnd);
        var b = scr.Bounds; var wa = scr.WorkingArea;
        Rect Isl() => new(Island.PointToScreen(new Point(0, 0)), Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight)));
        static bool Inside(Rect r, System.Drawing.Rectangle a) => r.Left >= a.Left - 1 && r.Right <= a.Right + 1 && r.Top >= a.Top - 1 && r.Bottom <= a.Bottom + 1;
        static string Show(Rect r) => $"{r.Left:0},{r.Top:0} {r.Width:0}x{r.Height:0}";
        void Check(string what, bool ok) => Log.Write($"edgetest: {what}: island {Show(Isl())} -> {(ok ? "ok" : "FAIL")}");
        async Task Open() { _pinnedOpen = true; TabMusic.IsChecked = true; Expand(); await Task.Delay(1500); }
        async Task Fold() { _pinnedOpen = false; SetMode(Mode.Compact); await Task.Delay(1700); }
        async Task Put(string attach, bool docked, double x, double y)
        {
            _settings.Attach = attach; _settings.Docked = docked;
            if (attach == "") { _settings.CenterX = x; _settings.Top = y; } else _settings.AttachAt = [x, y];
            ApplyDock(animate: false); ClampPosition(); await Task.Delay(1500);
        }
        Log.Write($"edgetest: screen {b}, work area {wa}, scale {k:0.00}");

        // floating, pushed against the right edge, the left edge, low over the taskbar
        foreach (var (name, x, y) in new[] { ("right edge", b.Right / k - 4, b.Top / k), ("left edge", b.Left / k + 4, b.Top / k), ("bottom", (b.Left + b.Right) / 2.0 / k, wa.Bottom / k - 50) })
        {
            await Put("", false, x, y);
            Check($"floating at the {name}, folded: whole on the screen", Inside(Isl(), wa));
            await Open(); Check($"floating at the {name}, open: whole on the screen", Inside(Isl(), wa));
            await Fold(); Check($"floating at the {name}, folded again: back, its place unchanged", Inside(Isl(), wa) && _settings.CenterX == x && _settings.Top == y);
        }

        // the notch at the top edge, far right
        await Put("", true, b.Right / k, b.Top / k);
        Check("top edge, far right, folded: touching the top, whole on the screen", Inside(Isl(), b) && Math.Abs(Isl().Top - b.Top) < 2);
        await Open(); Check("top edge, far right, open: touching the top, whole on the screen", Inside(Isl(), b) && Math.Abs(Isl().Top - b.Top) < 2);
        await Fold();

        // both sides, low (the open island must not go below the work area)
        foreach (var side in new[] { "left", "right" })
        {
            double edgeX = side == "left" ? wa.Left : wa.Right;
            bool Flush(Rect r) => Math.Abs((side == "left" ? r.Left : r.Right) - edgeX) < 2;
            await Put(side, false, side == "left" ? b.Left / k + 40 : b.Right / k - 40, wa.Bottom / k - 20);
            var r = Isl();
            Check($"{side} side, folded: upright, flush with the edge, whole on the screen", Flush(r) && r.Height > r.Width * 2 && Inside(r, wa));
            Check($"{side} side: nothing drawn past the edge (clip {Root.Clip?.Bounds})", Root.Clip != null);
            await Open(); r = Isl(); Check($"{side} side, open: flush, whole on the screen", Flush(r) && Inside(r, wa) && r.Width > r.Height);
            await Fold(); r = Isl(); Check($"{side} side, folded again: upright and flush", Flush(r) && r.Height > r.Width * 2);
        }

        // dragged from the right side and let go on the left half: it goes to the left side
        await Put("right", false, b.Right / k - 40, (b.Top + b.Bottom) / 2.0 / k);
        var from = IslandCenter();
        _pressCursor = from; _pressWindow = new Point(_px.Value, _py.Value); _pressed = true; _dragging = true; _posActive = true;
        _px.Stiffness = _py.Stiffness = 1500; _px.Damping = _py.Damping = 72;
        StartRendering();
        _testCursor = (from, new Vector(-b.Width / k * 0.7 / 0.8, 0), DateTime.UtcNow);
        await Task.Delay(800);
        _testCursor = (CursorDip(), new Vector(), DateTime.UtcNow);
        await Task.Delay(100);
        _dragging = false; _pressed = false; _px.Stiffness = _py.Stiffness = 560; _px.Damping = _py.Damping = 38;
        DropAt(new Vector()); _testCursor = null; StartRendering();
        await Task.Delay(1800);
        Check($"dragged from the right side to the left half: on the left side now ({_settings.Attach})", _settings.Attach == "left" && Math.Abs(Isl().Left - wa.Left) < 2);

        // hidden and shown again on a side
        HideIsland(animate: true); await Task.Delay(1300);
        Check($"left side, hidden: the handle at the top of the screen, no clip ({Handle.IsVisible}, {Root.Clip == null})", Handle.IsVisible && Root.Clip == null && Math.Abs(Top * k - b.Top) < 2);
        ShowIsland(); await Task.Delay(1800);
        Check("left side, shown again: flush with the edge", Math.Abs(Isl().Left - wa.Left) < 2);

        // on the taskbar: sits on it, grows upward, rises for the size grips, fades for Start
        await Put("taskbar", false, b.Right / k - 30, b.Bottom / k - 30);
        var (te, line, _) = TaskbarEdge(b, k);
        double L = line * k;
        bool OnBar(Rect r) => te switch { Edge.Bottom => Math.Abs(r.Bottom - L) < 2, Edge.Top => Math.Abs(r.Top - L) < 2, Edge.Left => Math.Abs(r.Left - L) < 2, _ => Math.Abs(r.Right - L) < 2 };
        Check($"on the taskbar ({te} at {L:0}), folded: on it, whole on the screen", OnBar(Isl()) && Inside(Isl(), b));
        await Open(); Check("on the taskbar, open: on it, whole on the screen", OnBar(Isl()) && Inside(Isl(), b));
        await Fold();
        EnterEditMode(); await Task.Delay(1500);
        Check("on the taskbar, changing its size: lifted off it for the grips", te != Edge.Bottom || Isl().Bottom < L - 40);
        ExitEditMode(); await Task.Delay(1500);
        Check("on the taskbar, size set: back on it", OnBar(Isl()));
        _testFlyout = true; await Task.Delay(500);
        Check($"on the taskbar, Start opened over it: faded away ({Root.Opacity:0.00})", Root.Opacity < 0.05 && !Root.IsHitTestVisible);
        _testFlyout = false; await Task.Delay(600);
        Check($"on the taskbar, Start closed: back ({Root.Opacity:0.00})", Root.Opacity > 0.95 && Root.IsHitTestVisible);
        _testFlyout = null;

        // back to floating: no clip, the usual place
        await Put("", false, (b.Left + b.Right) / 2.0 / k, b.Top / k);
        Check("floating again: no clip, content not turned", Root.Clip == null && CompactView.LayoutTransform.Value.IsIdentity && Isl().Width > Isl().Height);
        Application.Current.Shutdown();
    }
#endif

#if DEBUG
    /// <summary>Pictures (--run edgeshot &lt;dir&gt;, with --cover): the island in each place, over our own picture covering the whole screen.</summary>
    private async Task RunEdgeShotsAsync(string dir)
    {
        Settings.ReadOnly = true;
        Island.IsHitTestVisible = false; _settings.ExpandOnHover = false; NoClickOutside = true;
        _settings.YieldToCursor = false; _settings.PlacePerApp = false; _settings.FollowMonitor = false;
        System.IO.Directory.CreateDirectory(dir);
        var scr = Forms.Screen.FromHandle(_hwnd).Bounds;
        double k = Dpi;
        var wall = new System.Windows.Media.Imaging.BitmapImage(new Uri(System.IO.Path.Combine(dir, "wall.png")));
        var backdrop = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
            Left = scr.Left / k, Top = scr.Top / k, Width = scr.Width / k, Height = scr.Height / k, Background = new ImageBrush(wall) { Stretch = Stretch.UniformToFill } };
        backdrop.Show();
        await Task.Delay(5000);
        Native.KeepTopmost(_hwnd);
        if (Environment.GetCommandLineArgs().Contains("glass"))
        {
            ApplyThemePreset(BuiltInThemes.First(t => t.Id == "liquid"));
            _settings.Glass = true; _settings.CaptureHidden = false; _settings.LiteGraphics = false;
            ApplyCaptureExclusion(); ApplyGlass(); await Task.Delay(1500);
        }
        _demoMedia = _demoMedia! with { IsPlaying = true }; UpdateMediaUi();
        void Shot(string name, System.Drawing.Rectangle r)
        {
            r.Intersect(scr);
            using var bmp = new System.Drawing.Bitmap(r.Width, r.Height);
            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size);
            bmp.Save(System.IO.Path.Combine(dir, name + ".png"));
        }
        async Task Put(string attach, bool docked, double x, double y)
        {
            _settings.Attach = attach; _settings.Docked = docked;
            if (attach == "") { _settings.CenterX = x; _settings.Top = y; } else _settings.AttachAt = [x, y];
            ApplyDock(animate: false); ClampPosition(); Native.KeepTopmost(_hwnd); await Task.Delay(1800);
        }
        async Task Open() { _pinnedOpen = true; TabMusic.IsChecked = true; Expand(); await Task.Delay(1600); }
        async Task Fold() { _pinnedOpen = false; SetMode(Mode.Compact); await Task.Delay(1800); }
        int W = scr.Width, H = scr.Height;
        var left = new System.Drawing.Rectangle(scr.Left, scr.Top, 820, H);
        var right = new System.Drawing.Rectangle(scr.Right - 820, scr.Top, 820, H);
        var bottom = new System.Drawing.Rectangle(scr.Right - 820, scr.Bottom - 560, 820, 560);
        var top = new System.Drawing.Rectangle(scr.Right - 820, scr.Top, 820, 520);

        await Put("left", false, scr.Left / k + 40, scr.Top / k + H / k * 0.45);
        Shot("left-folded", left);
        ShowToast("", "Загрузка завершена", "presentation.pdf", null, (Brush)FindResource("AccentBlue"), ms: 4000); await Task.Delay(1300);
        Shot("left-toast", left); await Task.Delay(3500);
        await Open(); Shot("left-open", left); await Fold();
        EnterEditMode(); await Task.Delay(1500); Shot("left-edit", left); ExitEditMode(); await Task.Delay(1200);
        await Put("right", false, scr.Right / k - 40, scr.Top / k + H / k * 0.85);
        Shot("right-low-folded", right);
        await Open(); Shot("right-low-open", right); await Fold();
        await Put("taskbar", false, scr.Left / k + W / k * 0.5, scr.Bottom / k - 20);
        Shot("taskbar-folded", new System.Drawing.Rectangle(scr.Left + W / 2 - 410, scr.Bottom - 560, 820, 560));
        await Put("taskbar", false, scr.Right / k - 10, scr.Bottom / k - 20);
        Shot("taskbar-corner-folded", bottom);
        await Open(); Shot("taskbar-corner-open", bottom); await Fold();
        await Put("", false, scr.Right / k - 4, scr.Top / k);
        await Open(); Shot("floating-right-open", top); await Fold(); Shot("floating-right-folded", top);
        await Put("", true, scr.Right / k, scr.Top / k);
        await Open(); Shot("docked-right-open", top); await Fold();
        backdrop.Close();
        Application.Current.Shutdown();
    }
#endif
}

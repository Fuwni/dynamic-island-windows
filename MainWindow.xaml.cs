using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Services;
using Forms = System.Windows.Forms;

namespace DynamicIsland;

/// <summary>Core: island states, spring morph, floating drag / swipe-to-hide, fullscreen hiding, hotkeys, tray, toasts.</summary>
public partial class MainWindow : Window
{
    private enum Mode { Compact, Expanded, Toast, Claude, Drop, Command }

    /// <summary>Floating pill keeps a small gap; docked it grows straight out of the screen edge.</summary>
    private double IslandTopMargin => _settings.Docked ? 0 : 8;
    private const double CompactHeight = 36;

    private readonly Settings _settings = Settings.Current;
    private readonly Random _rng = new();

    // size morph: slightly underdamped = soft liquid overshoot
    private readonly Spring _w = new(200) { Stiffness = 340, Damping = 31 };
    private readonly Spring _h = new(CompactHeight) { Stiffness = 340, Damping = 31 };
    private readonly Spring _y = new(0) { Stiffness = 300, Damping = 24 };
    private bool _rendering;
    private TimeSpan _lastFrame;

    private Mode _mode = Mode.Compact;
    private bool _hidden;          // swiped away
    private bool _fullscreenHidden;
    private bool _pinnedOpen;      // --expand (screenshots / debugging)

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _gaze = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _blink = new();
    private readonly DispatcherTimer _expandDelay = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _collapseDelay = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _toastTimer = new();
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private int _tickCount;

    private Forms.NotifyIcon? _tray;
    private IntPtr _hwnd;

    public MainWindow(string[] args)
    {
        InitializeComponent();
        Services.Loc.TranslateTree(this);
        if (ContextMenu != null) Services.Loc.TranslateTree(ContextMenu);

        PlaceWindow();
        _tick.Tick += (_, _) => OnTick();
        _gaze.Tick += (_, _) => UpdateGaze();
        _blink.Tick += (_, _) => Blink();
        _expandDelay.Tick += (_, _) => { _expandDelay.Stop(); if (_mode == Mode.Compact && !_dragging) Expand(); };
        _collapseDelay.Tick += (_, _) => { _collapseDelay.Stop(); TryCollapse(); };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); if (_mode == Mode.Toast && !Island.IsMouseOver) SetMode(Mode.Compact); };
        _watch.Tick += (_, _) => WatchFullscreen();

        SourceInitialized += (_, _) => OnSourceInitialized();
        Deactivated += (_, _) =>
        {
            if (_mode == Mode.Expanded && !Island.IsMouseOver) _collapseDelay.Start();
            if (_mode == Mode.Command) CloseCommandBar();
        };
        Loaded += async (_, _) => await OnLoadedAsync(args);
        Closing += (_, _) => Shutdown();
        // Windows is shutting down / the user signs out: close properly instead of being killed
        Application.Current.SessionEnding += (_, _) => { if (!_shutDown) Close(); };
    }

    private async Task OnLoadedAsync(string[] args)
    {
        int snap = Array.IndexOf(args, "--snapshot");
        int promo = Array.IndexOf(args, "--promo");
        int review = Array.IndexOf(args, "--review");
        int live = Array.IndexOf(args, "--live-demo");
        if (snap >= 0 || promo >= 0 || review >= 0 || live >= 0) BeginDemo();
        if (live >= 0) StartLiveDemo(args.ElementAtOrDefault(live + 1) ?? "demo-commands.txt");
        ApplyThemeLocal();
        Character.Kind = _settings.Character;
        InitGlass();
        UpdateShape(false);
        InitHome();
        InitSettingsPage();
        InitBuddyReactions();
        InitClaude();
        InitRecording();
        InitExtras();

        OnTick();
        _tick.Start();
        _gaze.Start();
        _watch.Start();
        ScheduleBlink();
        CreateTray();

        if (_settings.Hidden) HideIsland(animate: false);

        int i = Array.IndexOf(args, "--expand");
        if (i >= 0)
        {
            _pinnedOpen = true;
            Expand();
            if (i + 1 < args.Length)
            {
                var tab = args[i + 1] switch
                {
                    "music" => TabMusic, "weather" => TabWeather, "chat" => TabChat,
                    "timer" => TabTimer, "clip" => TabClip, "notes" => TabNotes,
                    "calendar" => TabCalendar, "system" => TabSystem,
                    "settings" => TabSettings, _ => TabHome,
                };
                tab.IsChecked = true;
            }
        }

        // --run <action> [seconds]: trigger a quick action (testing without touching the keyboard)
        int r = Array.IndexOf(args, "--run");
        if (r >= 0 && r + 1 < args.Length)
        {
            var action = args[r + 1];
            int secs = r + 2 < args.Length && int.TryParse(args[r + 2], out var s) ? s : 0;
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1500);
                if (action.StartsWith("lang:")) { SwitchLanguage(action[5..]); return; }   // test the language switch
                if (action == "kick") { Kick(6); Squish(0.92); await Task.Delay(120); Unsquish(3); return; }
                if (action == "timer") { _timer.SetKind(TimerKind.Timer); _timer.SetDuration(TimeSpan.FromMinutes(secs > 0 ? secs : 5)); _timer.Start(); return; }
                RunAction(action);
                if (action == "record" && secs > 0)
                {
                    await Task.Delay(secs * 1000);
                    RunAction(action);
                }
            });
        }

        if (snap >= 0)
            _ = RunSnapshotsAsync(snap + 1 < args.Length ? args[snap + 1] : System.IO.Path.Combine(AppContext.BaseDirectory, "snapshots"));
        if (review >= 0) _ = RunReviewAsync(args.ElementAtOrDefault(review + 1) ?? "review");
        int brand = Array.IndexOf(args, "--brand");
        if (brand >= 0) _ = RunBrandAsync(args.ElementAtOrDefault(brand + 1) ?? "brand");
        if (promo >= 0)
            _ = RunPromoAsync(args.ElementAtOrDefault(promo + 1) ?? "promo", args.ElementAtOrDefault(promo + 2) ?? "ru");

        await InitMediaAsync();
        await InitWeatherAsync();
    }

    private void OnSourceInitialized()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);

        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        // the frosted glass photographs the screen behind the island: without this the island would
        // photograph itself (the glass turned opaque a second after opening). Also keeps it out of screenshots.
        ApplyCaptureExclusion();
        Native.AddClipboardFormatListener(_hwnd);
        uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
        foreach (var (id, vk, name) in new[] { (1, 0x49u, "I"), (2, 0x54u, "T"), (3, 0x53u, "S"), (4, 0x52u, "R") })
            if (!Native.RegisterHotKey(_hwnd, id, mods, vk))
                Log.Write($"Ctrl+Alt+{name} is taken by another program");
        // command bar: Alt+Space, and Ctrl+Alt+Space in case another launcher already owns Alt+Space
        if (!Native.RegisterHotKey(_hwnd, 5, Native.MOD_ALT | Native.MOD_NOREPEAT, 0x20))
            Log.Write("Alt+Space is taken by another program (use Ctrl+Alt+Space)");
        Native.RegisterHotKey(_hwnd, 6, mods, 0x20);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_CLIPBOARDUPDATE)
        {
            Dispatcher.BeginInvoke(OnClipboardChanged, DispatcherPriority.Background);
            return IntPtr.Zero;
        }
        if (msg == Native.WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case 1: ToggleHidden(); break;
                case 2: ToggleTheme(); break;
                case 3: RunAction("region"); break;
                case 4: RunAction("record"); break;
                case 5 or 6: ToggleCommandBar(); break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private bool _shutDown;

    private void Shutdown()
    {
        if (_shutDown) return;
        _shutDown = true;
        for (int id = 1; id <= 6; id++) Native.UnregisterHotKey(_hwnd, id);
        Native.RemoveClipboardFormatListener(_hwnd);
        ShutdownChat();
        _settings.Save();
        FocusMode.End(); // never leave Windows notifications switched off
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        if (_recording.IsRecording) _recording.Stop();
    }

    // ================================================================ position

    private double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private void PlaceWindow()
    {
        double cx = _settings.CenterX ?? SystemParameters.PrimaryScreenWidth / 2;
        double top = _settings.Top ?? 0;
        MoveWindow(cx - Width / 2, top);
        ClampPosition();
        ApplyDock(animate: false);
    }

    /// <summary>Switch between the floating pill and the notch attached to the top edge.</summary>
    private void ApplyDock(bool animate)
    {
        Island.Margin = new Thickness(0, IslandTopMargin, 0, 0);
        if (_settings.Docked && !_hidden) MoveWindow(Left, MonitorTop());
        if (Island.ActualWidth > 0) Island_SizeChanged(this, null!);
        UpdateShape(false);
        if (animate && _mode != Mode.Compact) MorphTo(TargetSize(_mode));
    }

    /// <summary>Jump the window (no floating) and keep the position springs in sync.</summary>
    private void MoveWindow(double left, double top)
    {
        _posActive = false;
        Left = left;
        Top = top;
        _px.Snap(left);
        _py.Snap(top);
    }

    private void ClampPosition()
    {
        double minX = SystemParameters.VirtualScreenLeft, maxX = minX + SystemParameters.VirtualScreenWidth;
        double minY = SystemParameters.VirtualScreenTop, maxY = minY + SystemParameters.VirtualScreenHeight;
        double cx = Math.Clamp(Left + Width / 2, minX + 110, maxX - 110);
        MoveWindow(cx - Width / 2, Math.Clamp(Top, minY, maxY - 80));
    }

    private void SavePosition()
    {
        _settings.CenterX = Left + Width / 2;
        _settings.Top = Top;
        _settings.Save();
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        _settings.CenterX = null;
        _settings.Top = null;
        _settings.Save();
        PlaceWindow();
        Kick(4);
    }

    // ================================================================ frame loop

    private void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;

        _w.Step(dt);
        _h.Step(dt);
        _y.Step(dt);
        Island.Width = Math.Max(20, _w.Value);
        Island.Height = Math.Max(8, _h.Value);
        IslandShift.Y = _y.Value;

        bool liquid = StepLiquid(dt);

        if (_w.IsSettled && _h.IsSettled && _y.IsSettled && !liquid)
        {
            CompositionTarget.Rendering -= OnRendering;
            _rendering = false;
            UpdateShape(false);
        }
    }

    private void MorphTo(Size size)
    {
        // calm mode: near-critical damping, the size glides without bouncing
        _w.Damping = _h.Damping = Liquid ? 23 : _settings.Bouncy ? 25 : 31;   // "springy": a small overshoot
        _w.Stiffness = _h.Stiffness = _settings.FastAnimations ? 560 : 340;
        _w.Target = size.Width;
        _h.Target = size.Height;
        StartRendering();
    }

    private void Island_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double r = Radius(Island.ActualHeight);
        // docked: square at the top (flush with the screen edge), round only at the bottom
        IslandContent.Clip = _settings.Docked
            ? new RectangleGeometry(new Rect(0, -r, Island.ActualWidth, Island.ActualHeight + r), r, r)
            : new RectangleGeometry(new Rect(0, 0, Island.ActualWidth, Island.ActualHeight), r, r);
        if (!_rendering) UpdateShape(false);
    }

    // ================================================================ modes

    /// <summary>Compact pill hugs its content (live activity text, dots, weather…).</summary>
    private double CompactWidth
    {
        get
        {
            var vis = CompactView.Visibility;
            CompactView.Visibility = Visibility.Visible;
            CompactView.Measure(new Size(double.PositiveInfinity, CompactHeight));
            CompactView.Visibility = vis;
            return Math.Clamp(CompactView.DesiredSize.Width + 8, 200, 440);
        }
    }

    private RadioButton[] Tabs => [TabHome, TabMusic, TabWeather, TabChat, TabTimer, TabClip, TabNotes, TabCalendar, TabSystem, TabSettings];

    private FrameworkElement[] AllPages => [HomePage, MusicPage, WeatherPage, ChatPage, TimerPage, ClipPage, NotesPage, CalendarPage, SystemPage, SettingsPage];

    private FrameworkElement CurrentPage
    {
        get
        {
            int i = Array.FindIndex(Tabs, t => t.IsChecked == true);
            return AllPages[Math.Max(0, i)];
        }
    }

    private Size TargetSize(Mode mode)
    {
        switch (mode)
        {
            case Mode.Expanded:
            {
                var page = CurrentPage;
                page.Measure(new Size(page.Width, double.PositiveInfinity));
                return new Size(page.Width + 28, 10 + 32 + 10 + page.DesiredSize.Height + 14);
            }
            case Mode.Toast:
                return new Size(Math.Clamp(ToastWidth(), 260, 420), 58);
            case Mode.Claude:
            {
                ClaudeView.Measure(new Size(ClaudeView.Width, double.PositiveInfinity));
                return new Size(ClaudeView.Width + 32, ClaudeView.DesiredSize.Height + 30);
            }
            case Mode.Command:
            {
                CommandView.Measure(new Size(CommandView.Width, double.PositiveInfinity));
                return new Size(CommandView.Width + 32, CommandView.DesiredSize.Height + 26);
            }
            case Mode.Drop:
            {
                DropView.Measure(new Size(DropView.Width, double.PositiveInfinity));
                return new Size(DropView.Width + 32, DropView.DesiredSize.Height + 30);
            }
            default:
                return new Size(CompactWidth, CompactHeight);
        }
    }

    private double ToastWidth()
    {
        ToastView.Measure(new Size(double.PositiveInfinity, 58));
        return ToastView.DesiredSize.Width + 30;
    }

    private void SetMode(Mode mode)
    {
        var old = _mode;
        _mode = mode;

        var views = new Dictionary<Mode, FrameworkElement>
        {
            [Mode.Compact] = CompactView,
            [Mode.Expanded] = ExpandedView,
            [Mode.Toast] = ToastView,
            [Mode.Claude] = ClaudeView,
            [Mode.Drop] = DropView,
            [Mode.Command] = CommandView,
        };

        foreach (var (m, view) in views)
        {
            if (m == mode || view.Visibility != Visibility.Visible) continue;
            var v = view;
            Conceal(v, () => { if (_mode != m) v.Visibility = Visibility.Collapsed; });
        }

        if (old != mode)
        {
            if (mode != Mode.Compact) PlayChromatic(mode == Mode.Toast ? 5 : 8); // the "print" shimmer when it opens
            Reveal(views[mode], mode == Mode.Compact ? 0.92 : 0.95, 0, 30);
            Kick(mode == Mode.Compact ? 2.5 : 3.5);
        }
        else
        {
            views[mode].Visibility = Visibility.Visible;
            views[mode].Opacity = 1;
        }

        MorphTo(TargetSize(mode));
        UpdateEyesVisibility();
        ApplyGlass();
    }

    private void Expand()
    {
        if (_hidden || _mode is Mode.Claude or Mode.Drop or Mode.Command) return;
        _collapseDelay.Stop();
        ClearUnread();
        SetMode(Mode.Expanded);
        OnPageShown();
    }

    private void TryCollapse()
    {
        if (_mode == Mode.Drop && !_dragging)
        {
            _dropped = [];
            SetMode(Mode.Compact);
            return;
        }
        if (_pinnedOpen || _dragging || _mode != Mode.Expanded) return;
        if (IslandMenu.IsOpen) return;
        if (IsActive && IsKeyboardFocusWithin) return;              // typing (city, chat…)
        if (IsActive && TabChat.IsChecked == true) return;           // chatting
        SetMode(Mode.Compact);
    }

    private void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseDelay.Stop();
        if (_mode == Mode.Compact && !_dragging)
        {
            Squish(1.05); // swells a little under the cursor
            Kick(1.2);
            if (_settings.ExpandOnHover) _expandDelay.Start();
        }
    }

    private void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        _expandDelay.Stop();
        if (!_dragging) Unsquish(0);
        if (_mode is Mode.Expanded) _collapseDelay.Start();
        if (_mode is Mode.Drop && _dropped.Length > 0) _collapseDelay.Start();
        if (_mode == Mode.Toast && !_toastTimer.IsEnabled) _toastTimer.Start();
    }

    private int _tabIndex;

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (HomePage == null || TabSettings == null) return; // during InitializeComponent
        int index = Array.IndexOf(Tabs, sender as RadioButton);
        double slide = index > _tabIndex ? 26 : index < _tabIndex ? -26 : 0;
        _tabIndex = Math.Max(0, index);

        foreach (var page in AllPages)
            page.Visibility = Visibility.Collapsed;

        Reveal(CurrentPage, 0.98, slide, 0);
        UpdatePageDots();
        if (_mode == Mode.Expanded)
        {
            Kick(1.5);
            MorphTo(TargetSize(Mode.Expanded));
            OnPageShown();
        }
    }

    /// <summary>Lazy work for the tab that just became visible.</summary>
    private void OnPageShown()
    {
        if (TabChat.IsChecked == true) _ = EnsureChatAsync();
        else if (TabMusic.IsChecked == true) RefreshVolume();
        else if (TabSettings.IsChecked == true) RefreshSettingsPage();
        else if (TabClip.IsChecked == true) RenderClipSection();
        else if (TabNotes.IsChecked == true) RenderNotes();
        else if (TabCalendar.IsChecked == true) RenderCalendar();
        else if (TabTimer.IsChecked == true) RenderTimer();
        else if (TabSystem.IsChecked == true) _ = RefreshSystemAsync(full: true);
        Remeasure();
    }

    /// <summary>Re-measure after content changed (e.g. Home layout).</summary>
    private void Remeasure()
    {
        if (_mode is Mode.Expanded or Mode.Claude)
            Dispatcher.BeginInvoke(() => MorphTo(TargetSize(_mode)), DispatcherPriority.Loaded);
    }

    private static void Fade(UIElement element, double to, int ms, int delayMs, Action? completed = null)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (completed != null) anim.Completed += (_, _) => completed();
        element.BeginAnimation(OpacityProperty, anim);
    }

    // ================================================================ press, drag, swipe

    private bool _pressed, _dragging, _justDragged;
    private Point _pressCursor, _pressWindow;
    private readonly List<(DateTime T, Point P)> _samples = new();

    private Point CursorDip()
    {
        Native.GetCursorPos(out var p);
        return new Point(p.X / Dpi, p.Y / Dpi);
    }

    private static bool IsInteractive(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is ButtonBase or TextBoxBase or Slider or ScrollBar or InkCanvas) return true;
            if (d is FrameworkElement fe && fe.Name is "ChatHost" or "ShelfList" or "CommandResults") return true;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    private Vector CursorVelocity()
    {
        var recent = _samples.Where(s => (DateTime.Now - s.T).TotalMilliseconds < 90).ToList();
        if (recent.Count < 2) return new Vector();
        double secs = Math.Max(0.012, (recent[^1].T - recent[0].T).TotalSeconds);
        return (recent[^1].P - recent[0].P) / secs;
    }

    private void Island_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // every touch makes a wave
        SpawnRipple(e.GetPosition(RippleLayer));
        Squish(0.955);
        Kick(1.4);

        if (IsInteractive(e.OriginalSource as DependencyObject)) return;
        _pressed = true;
        _dragging = false;
        _pressCursor = CursorDip();
        _pressWindow = new Point(Left, Top);
        _samples.Clear();
        _samples.Add((DateTime.Now, _pressCursor));
    }

    private void Island_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
        var c = CursorDip();
        var d = c - _pressCursor;
        if (!_dragging)
        {
            if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
            _dragging = true;
            _expandDelay.Stop();
            _collapseDelay.Stop();
            Island.CaptureMouse();
            _px.Snap(Left);
            _py.Snap(Top);
            _posActive = true;
        }

        _samples.Add((DateTime.Now, c));
        if (_samples.Count > 16) _samples.RemoveAt(0);

        // the window floats after the cursor on a spring
        _px.Target = _pressWindow.X + d.X;
        // docked: slides along the top edge only (pulling up still hides it)
        _py.Target = _pressWindow.Y + (_settings.Docked ? Math.Min(0, d.Y) : d.Y);

        if (Liquid)
        {
            // lean into the motion and stretch along it, like a drop of water
            var v = CursorVelocity();
            _skew.Target = Math.Clamp(-v.X * 0.007, -12, 12);
            double sx = Math.Clamp(Math.Abs(v.X) * 0.00007, 0, 0.12), sy = Math.Clamp(Math.Abs(v.Y) * 0.00007, 0, 0.12);
            _sx.Target = 1 + sx - sy * 0.5;
            _sy.Target = 1 + sy - sx * 0.5;
            _ripple = Math.Min(6, _ripple + v.Length * 0.00035);
        }
        StartRendering();
    }

    private void Island_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed)
        {
            Unsquish(1.6);
            return;
        }
        _pressed = false;

        if (_dragging)
        {
            _dragging = false;
            Island.ReleaseMouseCapture();
            e.Handled = true;
            _justDragged = true;
            Dispatcher.BeginInvoke(() => _justDragged = false, DispatcherPriority.Background);

            var v = CursorVelocity();
            _skew.Target = 0;
            _sx.Target = _sy.Target = 1; // ...and wobbles back into shape
            Kick(Math.Min(5, 1.5 + v.Length * 0.002));

            // upward flick, or dragged above the screen edge → hide
            double screenTop = SystemParameters.VirtualScreenTop;
            if (_py.Target + IslandTopMargin < screenTop - 12 || (v.Y < -900 && _py.Target < _pressWindow.Y - 15))
            {
                HideIsland(animate: true);
                return;
            }

            // a little momentum, then it settles where it lands
            double minX = SystemParameters.VirtualScreenLeft, maxX = minX + SystemParameters.VirtualScreenWidth;
            double cx = Math.Clamp(_px.Target + Width / 2 + v.X * 0.07, minX + 110, maxX - 110);
            _px.Target = cx - Width / 2;
            _py.Target = _settings.Docked
                ? MonitorTop()
                : Math.Clamp(_py.Target + v.Y * 0.05, screenTop, screenTop + SystemParameters.VirtualScreenHeight - 80);
            StartRendering();
            if (!Island.IsMouseOver && _mode == Mode.Expanded) _collapseDelay.Start();
            return;
        }

        Unsquish(1.6);
        // easter egg: poking a character shows one of seven emotions
        if (TryEmote(e)) return;
        // plain click on the compact pill opens it
        if (_mode == Mode.Compact) Expand();
    }

    // ================================================================ hide / show

    private void HideIsland_Click(object sender, RoutedEventArgs e) => HideIsland(animate: true);

    private void ToggleHidden()
    {
        if (_hidden) ShowIsland(); else HideIsland(animate: true);
    }

    private void HideIsland(bool animate)
    {
        if (_hidden && animate) return;
        if (_mode != Mode.Compact && _mode != Mode.Claude) SetMode(Mode.Compact);
        _hidden = true;
        _posActive = false;
        _settings.Hidden = true;
        if (Top > SystemParameters.VirtualScreenTop + 1) _settings.Top = Top; // remember where it was
        _settings.CenterX = Left + Width / 2;
        _settings.Save();

        void Finish()
        {
            Island.Visibility = Visibility.Hidden;
            MoveWindow(Left, MonitorTop());
            Handle.Visibility = Visibility.Visible;
            Reveal(Handle, 0.6, 0);
        }

        if (!animate) { Finish(); return; }

        // a drop being pulled up: stretch thin, then zip away
        if (Liquid)
        {
            _sx.Target = 0.8;
            _sy.Target = 1.25;
        }
        _y.Target = -(Island.ActualHeight + IslandTopMargin + 40);
        _y.Velocity = -900;
        Kick(4);
        StartRendering();
        Fade(Island, 0, 260, 60, () => { if (_hidden) Finish(); });
    }

    private void ShowIsland()
    {
        if (!_hidden) return;
        _hidden = false;
        _settings.Hidden = false;
        _settings.Save();

        Handle.Visibility = Visibility.Collapsed;
        MoveWindow(Left, _settings.Top ?? MonitorTop());
        ClampPosition();
        Island.Visibility = Visibility.Visible;
        _y.Snap(-(Island.ActualHeight + IslandTopMargin + 30));
        _y.Target = 0;
        // drops back in (with waves on: squashes on "landing" and wobbles)
        _sx.Snap(Liquid ? 0.85 : 1);
        _sy.Snap(Liquid ? 1.2 : 1);
        _sx.Target = _sy.Target = 1;
        Kick(5);
        StartRendering();
        Fade(Island, 1, 220, 0);
        HandlePill.BeginAnimation(WidthProperty, null);
    }

    private double MonitorTop()
    {
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)((Left + Width / 2) * Dpi), (int)((Top + 20) * Dpi)));
        return screen.Bounds.Top / Dpi;
    }

    private bool _handlePressed;
    private double _handlePressY;

    private void Handle_MouseEnter(object sender, MouseEventArgs e)
    {
        var ease = new SpringEase { DampingRatio = 0.55 };
        HandlePill.BeginAnimation(WidthProperty, new DoubleAnimation(84, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        HandlePill.BeginAnimation(HeightProperty, new DoubleAnimation(9, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        HandlePill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
    }

    private void Handle_MouseLeave(object sender, MouseEventArgs e)
    {
        _handlePressed = false;
        var ease = new SpringEase { DampingRatio = 0.6 };
        HandlePill.BeginAnimation(WidthProperty, new DoubleAnimation(56, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        HandlePill.BeginAnimation(HeightProperty, new DoubleAnimation(6, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
        HandlePill.BeginAnimation(OpacityProperty, new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(220)));
    }

    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _handlePressed = true;
        _handlePressY = CursorDip().Y;
        Handle.CaptureMouse();
        Handle.MouseLeftButtonUp += HandleUp;

        void HandleUp(object s, MouseButtonEventArgs a)
        {
            Handle.MouseLeftButtonUp -= HandleUp;
            Handle.ReleaseMouseCapture();
            if (_handlePressed) { _handlePressed = false; ShowIsland(); }
        }
    }

    private void Handle_MouseMove(object sender, MouseEventArgs e)
    {
        // pull down to open
        if (_handlePressed && CursorDip().Y - _handlePressY > 14)
        {
            _handlePressed = false;
            Handle.ReleaseMouseCapture();
            ShowIsland();
        }
    }

    // ================================================================ fullscreen & topmost

    private bool _gameMode;

    private void WatchFullscreen()
    {
        // remember where the user was typing, so clipboard items can be pasted back there
        var fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != _hwnd && !OwnsWindow(fg)) _lastExternalWindow = fg;

        bool fullscreen = _settings.HideInFullscreen && _hwnd != IntPtr.Zero && Native.IsFullscreenAppActive(_hwnd);
        // game mode: keep a tiny stats pill instead of hiding completely
        bool game = fullscreen && _settings.GameStats;
        if (game != _gameMode)
        {
            _gameMode = game;
            if (_mode == Mode.Compact) MorphTo(TargetSize(Mode.Compact));
        }
        bool fs = fullscreen && !game;
        if (fs != _fullscreenHidden)
        {
            _fullscreenHidden = fs;
            if (fs)
            {
                Root.IsHitTestVisible = false;
                Fade(Root, 0, 180, 0, () => { if (_fullscreenHidden) Root.Visibility = Visibility.Hidden; });
            }
            else
            {
                Root.Visibility = Visibility.Visible;
                Root.IsHitTestVisible = true;
                Fade(Root, 1, 250, 0);
                Kick(3);
            }
        }
        if (!fs && !_dragging) Native.KeepTopmost(_hwnd);
    }

    // ================================================================ clock & eyes

    private void OnTick()
    {
        _tickCount++;
        var now = DateTime.Now.ToString("HH:mm");
        ExpandedTime.Text = now;
        if (_recording.IsRecording)
        {
            var t = DateTime.Now - _recording.StartedAt;
            CompactTime.Text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
            CompactTime.Foreground = (Brush)FindResource("Danger");
        }
        else
        {
            CompactTime.Text = now;
            CompactTime.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        }

        UpdateProgress();
        UpdateLyricLine();
        UpdateLive();
        if (_tickCount % 4 == 0)
        {
            SlowTick();
            _media.PollLegacy();
            if (_mode == Mode.Expanded && TabMusic.IsChecked == true && !VolumeSlider.IsMouseCaptureWithin) RefreshVolume();
        }
    }

    private void UpdateEyesVisibility()
    {
        bool playing = _lastMedia?.IsPlaying == true;
        bool showArt = playing && _lastMedia?.Art != null && !_recording.IsRecording;
        RecDot.Visibility = _recording.IsRecording ? Visibility.Visible : Visibility.Collapsed;
        MiniArt.Visibility = showArt ? Visibility.Visible : Visibility.Collapsed;
        Eyes.Visibility = !_recording.IsRecording ? Visibility.Visible : Visibility.Collapsed; // the character stays next to the cover
        EqBars.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
        CompactWeather.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
    }

    private readonly List<Views.CharacterView> _previewCharacters = [];
    private Native.POINT? _fakeCursor;

    /// <summary>~30 times a second: drive the character and the pill's reaction to the cursor.</summary>
    private void UpdateGaze()
    {
        if (!IsLoaded || _hidden || _fullscreenHidden) return;
        Native.GetCursorPos(out var cursor);
        if (_fakeCursor is Native.POINT fake) cursor = fake;   // screenshots / tests
        bool playing = _lastMedia?.IsPlaying == true;
        double beat = playing ? Math.Min(1, _audio.Peak * 1.6) : 0;
        var mood = _demoMedia == null && Native.IdleSeconds() > 120 ? Views.CharacterMood.Sleep
                 : _claudeCurrent != null ? Views.CharacterMood.Think
                 : playing ? Views.CharacterMood.Music : Views.CharacterMood.Idle;

        void Drive(Views.CharacterView ch, Views.CharacterMood? moodOverride = null)
        {
            if (!ch.IsVisible) return;
            Point c;
            try { c = ch.PointToScreen(new Point(ch.ActualWidth / 2, ch.ActualHeight / 2)); }
            catch { return; }
            ch.LookX = Math.Clamp((cursor.X - c.X) / 170.0, -1, 1);
            ch.LookY = Math.Clamp((cursor.Y - c.Y) / 170.0, -1, 1);
            ch.Beat = beat;
            ch.Mood = moodOverride ?? mood;
            ch.Tint = _albumColor;
            ch.Sketch = _settings.HandDrawn;
            ch.Tick(0.033);
        }

        if (_mode == Mode.Compact) Drive(Character);
        // the Claude Code buddy on the chat window
        bool buddy = _settings.Character == "clawd" && _mode == Mode.Expanded && TabChat.IsChecked == true;
        var buddyVis = buddy ? Visibility.Visible : Visibility.Collapsed;
        if (ChatBuddy.Visibility != buddyVis)
        {
            ChatBuddy.Visibility = buddyVis;
            if (buddy) ChatBuddy.Greet();   // pops up over the chat and says hi
        }
        if (buddy) Drive(ChatBuddy, ChatBuddyMood());

        // the same character keeps you company in the open menu (unless it already sits on the chat)
        bool menu = _mode == Mode.Expanded && !buddy;
        var menuVis = menu ? Visibility.Visible : Visibility.Collapsed;
        if (MenuBuddy.Visibility != menuVis)
        {
            MenuBuddy.Visibility = menuVis;
            if (menu) MenuBuddy.Greet();
        }
        if (menu)
        {
            MenuBuddy.Kind = _settings.Character;
            Drive(MenuBuddy, MenuBuddyMood());
        }
        UpdateAuraMood(mood == Views.CharacterMood.Think);
        if (_mode == Mode.Expanded && TabSettings.IsChecked == true)
            foreach (var preview in _previewCharacters) Drive(preview);

        UpdateReach(cursor);
    }

    private void ScheduleBlink()
    {
        // the character blinks by itself now (CharacterView)
    }

    private void Blink()
    {
        _blink.Stop();
        EyesScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.1, TimeSpan.FromMilliseconds(70)) { AutoReverse = true });
        ScheduleBlink();
    }

    // ================================================================ toast

    private Action? _toastAction;

    public void ShowToast(string glyph, string title, string? text, Action? onClick = null, Brush? badge = null, int ms = 2600)
    {
        if (_mode == Mode.Claude || _hidden) return;
        ToastIcon.Text = glyph;
        ToastTitle.Text = title;
        ToastText.Text = text ?? "";
        ToastText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (badge != null) ToastBadge.Background = badge; else ToastBadge.SetResourceReference(Border.BackgroundProperty, "ChipBg");
        _toastAction = onClick;
        Character.Cheer();
        MenuBuddy.Cheer();
        PulseGlow(badge is SolidColorBrush sb ? sb.Color : Color.FromRgb(0x0A, 0x84, 0xFF));
        bool already = _mode == Mode.Toast;
        SetMode(Mode.Toast);
        if (already) { Kick(3); Squish(1.04); Dispatcher.InvokeAsync(async () => { await Task.Delay(140); Unsquish(1); }); }
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(ms);
        _toastTimer.Start();
    }

    private void Toast_Click(object sender, MouseButtonEventArgs e)
    {
        if (_justDragged) return;
        var a = _toastAction;
        _toastAction = null;
        SetMode(Mode.Compact);
        a?.Invoke();
    }

    // ================================================================ theme

    private void ApplyTheme() => Theme.Apply(_settings.DarkTheme, _settings.IslandColor, _settings.GlassOpacity);

    private void ToggleTheme()
    {
        _settings.DarkTheme = !_settings.DarkTheme;
        _settings.Save();
        ApplyTheme();
        ApplyThemeLocal();
        ApplyGlass();
        DarkSwitch.IsChecked = _settings.DarkTheme;
        Kick(4);
    }

    private void ApplyThemeLocal()
    {
        if (Island.Effect is System.Windows.Media.Effects.DropShadowEffect fx)
            fx.Opacity = _settings.DarkTheme ? 0.38 : 0.18;
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e) => ToggleTheme();

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenTab(TabSettings);

    private void OpenTab(RadioButton tab)
    {
        if (_hidden) ShowIsland();
        tab.IsChecked = true;
        Expand();
        Activate();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // ================================================================ tray

    private void CreateTray()
    {
        using var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"))!.Stream;
        _tray = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize),
            Text = "Dynamic Island",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add(L.T("Показать / спрятать  (Ctrl+Alt+I)"), null, (_, _) => ToggleHidden());
        _tray.ContextMenuStrip.Items.Add(L.T("Скриншот области  (Ctrl+Alt+S)"), null, (_, _) => RunAction("region"));
        _tray.ContextMenuStrip.Items.Add(L.T("Сменить тему  (Ctrl+Alt+T)"), null, (_, _) => ToggleTheme());
        _tray.ContextMenuStrip.Items.Add(L.T("Настройки"), null, (_, _) => OpenTab(TabSettings));
        _tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add(L.T("Выход"), null, (_, _) => Close());
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) ToggleHidden(); };
    }
}
